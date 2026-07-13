using DLE.Data;
using DLE.Dispatch;
using DLE.Economy;
using DLE.Jobs;
using DV.Common;
using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;
using UnityModManagerNet;

namespace DLE
{
    public static class Main
    {
        public static UnityModManager.ModEntry ModEntry { get; private set; }
        public static Settings Settings { get; private set; }

        private static Harmony _harmony;

        // DVMP API reflection handles
        private static PropertyInfo _isHostProp;
        private static PropertyInfo _isSinglePlayerProp;
        private static object       _mpApiInstance;
        private static bool         _dvmpResolved;

        // UMM entry point

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            ModEntry = modEntry;

            Settings = UnityModManager.ModSettings.Load<Settings>(modEntry);

            modEntry.OnSaveGUI = OnSaveGUI;
            modEntry.OnGUI     = OnGUI;

            _harmony = new Harmony(modEntry.Info.Id);
            _harmony.PatchAll();

            // First run: give the player an editable economy.json based on the shipped example.
            try
            {
                var target = System.IO.Path.Combine(modEntry.Path, "economy.json");
                var example = System.IO.Path.Combine(modEntry.Path, "economy.default.json");
                if (!System.IO.File.Exists(target) && System.IO.File.Exists(example))
                    System.IO.File.Copy(example, target);
            }
            catch (Exception ex)
            {
                LogAlways($"[Main] economy.json first-run copy failed: {ex.Message}");
            }

            // World-ready hook: persistence and (in later phases) economy init run
            // after all stations are loaded.
            WorldStreamingInit.LoadingFinished += OnWorldLoaded;

            // Resolve DVMP API at load time (LoadAfter: ["Multiplayer"] guarantees
            // MultiplayerAPI.dll is already in the AppDomain when we run).
            ResolveDvmpApi();

            LogAlways("[Main] Derail Logistics Engine loaded.");
            return true;
        }

        // World ready

        private static void OnWorldLoaded()
        {
            Log("[Main] World loaded; initialising DLE systems.");

            // Build the economy for this world (recipes from stations, overlay, saved stock).
            bool freshEconomy = false;
            try
            {
                var data = SaveGameManager.Instance?.data;
                if (data != null)
                    freshEconomy = EconomyState.Instance.Init(data, ModEntry.Path);
            }
            catch (Exception ex)
            {
                LogAlways($"[Main] Economy init failed: {ex.Message}");
            }

            // Rebuild live Direct Haul jobs from our own save (they are filtered out of the
            // vanilla job save), restore assignments, and start the local HTTP API.
            // Host only; clients receive jobs from the host via DVMP.
            if (IsHostOrSingleplayer())
            {
                try
                {
                    var data = SaveGameManager.Instance?.data;
                    if (data != null)
                    {
                        DleCarPool.Instance.LoadFrom(data);
                        DleJobStore.RestoreFrom(data);
                        AssignmentStore.Instance.LoadFrom(data);
                        LogisticsBoard.Instance.LoadFrom(data);
                    }
                }
                catch (Exception ex)
                {
                    LogAlways($"[Main] Job restore failed: {ex.Message}");
                }
                DleHttpServer.StartOnHost();
                DleDirectorBehaviour.StartOnHost();

                // New game: the finite world needs a starter car pool to move its
                // starter stock. Existing saves keep whatever cars they have
                // (company.respawn rebuilds pools on demand).
                if (freshEconomy)
                    DleCarPool.Instance.RespawnStationPools(deleteFirst: false);
            }

            // Subscribe to save event so we persist before the game writes to disk.
            // Unsubscribe first: OnWorldLoaded fires on every save/load within a session;
            // without the unsub, each reload would add another handler and OnAboutToSave
            // would be called N times per save after N reloads.
            SaveGameManager.AboutToSave -= OnAboutToSave;
            SaveGameManager.AboutToSave += OnAboutToSave;
        }

        private static void OnAboutToSave(SaveType saveType)
        {
            try
            {
                var data = SaveGameManager.Instance?.data;
                if (data != null)
                {
                    EconomyState.Instance.SaveTo(data);
                    DleJobStore.SaveTo(data);
                    AssignmentStore.Instance.SaveTo(data);
                    DleCarPool.Instance.SaveTo(data);
                    LogisticsBoard.Instance.SaveTo(data);
                }
            }
            catch (Exception ex)
            {
                LogAlways($"[Main] Error saving DLE state: {ex.Message}");
            }
        }

        // UMM settings GUI

        private static void OnGUI(UnityModManager.ModEntry entry)
        {
            Settings.Draw(entry);

            UnityEngine.GUILayout.Space(10);
            UnityEngine.GUILayout.Label("Economy");
            if (UnityEngine.GUILayout.Button("Generate a haul from stock", UnityEngine.GUILayout.Width(240)))
                DLE.Economy.EconomyDirector.GenerateOne();
            if (UnityEngine.GUILayout.Button("Seed starting stock now", UnityEngine.GUILayout.Width(240)))
                EconomyState.Instance.SeedInitialStock(Settings?.InitialStock ?? 6);
            if (UnityEngine.GUILayout.Button("Reload economy.json", UnityEngine.GUILayout.Width(240)))
                EconomyState.Instance.ReloadRecipes(ModEntry.Path);

            UnityEngine.GUILayout.Space(6);
            UnityEngine.GUILayout.Label("Debug");
            if (UnityEngine.GUILayout.Button("Dump economy to log", UnityEngine.GUILayout.Width(240)))
                EconomyState.Instance.DumpToLog();
            if (UnityEngine.GUILayout.Button("Simulate a delivery (no train)", UnityEngine.GUILayout.Width(240)))
                DebugEconomy.SimulateDelivery();
        }

        private static void OnSaveGUI(UnityModManager.ModEntry entry)
        {
            Settings.Save(entry);
        }

        // Helpers

        /// <summary>
        /// Routine logging, gated behind the verbose setting: UMM writes every line to
        /// disk on the main thread, so the steady-state chatter (generation, economy,
        /// http) stays silent unless someone is actually debugging.
        /// </summary>
        public static void Log(string message)
        {
            if (Settings?.VerboseLogging == true)
                ModEntry?.Logger?.Log(message);
        }

        /// <summary>Errors and rare lifecycle milestones: always written.</summary>
        public static void LogAlways(string message) => ModEntry?.Logger?.Log(message);

        /// <summary>
        /// Returns true when we should run server-side logic.
        /// In single-player this is always true; in multiplayer only the host.
        /// Uses reflection against the public DVMP MultiplayerAPI so there is no
        /// hard compile-time dependency on MultiplayerAPI.dll.
        /// </summary>
        public static bool IsHostOrSingleplayer()
        {
            ResolveDvmpApi(); // idempotent: resolves once then returns immediately
            if (_mpApiInstance == null) return true; // no DVMP present, single-player
            try
            {
                bool isHost = (bool)(_isHostProp?.GetValue(_mpApiInstance) ?? true);
                bool isSP   = (bool)(_isSinglePlayerProp?.GetValue(_mpApiInstance) ?? true);

                if (!isHost && !isSP)
                {
                    // Both false in single-player DVMP mode is a known edge case: the server
                    // may not have propagated IsHost/IsSinglePlayer yet at job-generation time.
                    Log($"[Main] IsHost={isHost} IsSinglePlayer={isSP}; both false, defaulting to HOST");
                    return true;
                }

                return isHost || isSP;
            }
            catch { return true; }
        }

        /// <summary>
        /// Locate DVMP's MultiplayerAPI assembly in the AppDomain and cache reflection
        /// handles for IsHost and IsSinglePlayer. Also registers this mod as Host-only
        /// so clients can join a DLE-hosted server without installing the mod themselves.
        /// Safe to call multiple times; resolves only on the first call.
        /// </summary>
        private static void ResolveDvmpApi()
        {
            if (_dvmpResolved) return;
            _dvmpResolved = true;

            try
            {
                // MultiplayerAPI.dll is loaded by DVMP before DLE (LoadAfter: ["Multiplayer"])
                var mpApiType = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "MultiplayerAPI")
                    ?.GetType("MPAPI.MultiplayerAPI");

                if (mpApiType == null)
                {
                    Log("[Main] DVMP MultiplayerAPI not found; assuming single-player");
                    return;
                }

                var isLoaded = (bool)(mpApiType.GetProperty("IsMultiplayerLoaded")?.GetValue(null) ?? false);
                if (!isLoaded)
                {
                    Log("[Main] DVMP present but multiplayer not loaded");
                    return;
                }

                _mpApiInstance = mpApiType.GetProperty("Instance")?.GetValue(null);
                if (_mpApiInstance == null)
                {
                    Log("[Main] DVMP Instance is null");
                    return;
                }

                var instanceType = _mpApiInstance.GetType();
                _isHostProp         = instanceType.GetProperty("IsHost");
                _isSinglePlayerProp = instanceType.GetProperty("IsSinglePlayer");

                // Register as Host-only: clients don't need this mod installed
                // MultiplayerCompatibility.Host = byte value 3
                var compatType = mpApiType.Assembly.GetType("MPAPI.Types.MultiplayerCompatibility");
                if (compatType != null)
                {
                    var hostCompat = Enum.ToObject(compatType, (byte)3);
                    instanceType.GetMethod("SetModCompatibility")
                        ?.Invoke(_mpApiInstance, new[] { ModEntry.Info.Id, hostCompat });
                }

                Log("[Main] DVMP API hooked; registered as Host-only mod");
            }
            catch (Exception ex)
            {
                Log($"[Main] DVMP API hook failed: {ex.Message}");
            }
        }
    }
}
