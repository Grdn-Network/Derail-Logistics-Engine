using DLE.Data;
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

            // World-ready hook: persistence and (in later phases) economy init run
            // after all stations are loaded.
            WorldStreamingInit.LoadingFinished += OnWorldLoaded;

            // Resolve DVMP API at load time (LoadAfter: ["Multiplayer"] guarantees
            // MultiplayerAPI.dll is already in the AppDomain when we run).
            ResolveDvmpApi();

            Log("[Main] Derail Logistics Engine loaded.");
            return true;
        }

        // World ready

        private static void OnWorldLoaded()
        {
            Log("[Main] World loaded; initialising DLE systems.");

            // Load car store from save data (instance field on SaveGameManager singleton)
            try
            {
                var saveData = SaveGameManager.Instance?.data;
                if (saveData != null)
                    CarDestinationStore.Instance.LoadFromSave(saveData);
            }
            catch (Exception ex)
            {
                Log($"[Main] Could not load CarDestinationStore from save: {ex.Message}");
            }

            // Build the economy for this world (recipes from stations, overlay, saved stock).
            try
            {
                var data = SaveGameManager.Instance?.data;
                if (data != null)
                    EconomyState.Instance.Init(data, ModEntry.Path);
            }
            catch (Exception ex)
            {
                Log($"[Main] Economy init failed: {ex.Message}");
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
                    CarDestinationStore.Instance.SaveTo(data);
                    EconomyState.Instance.SaveTo(data);
                }
            }
            catch (Exception ex)
            {
                Log($"[Main] Error saving CarDestinationStore: {ex.Message}");
            }
        }

        // UMM settings GUI

        private static void OnGUI(UnityModManager.ModEntry entry)
        {
            Settings.Draw(entry);

            UnityEngine.GUILayout.Space(8);
            if (UnityEngine.GUILayout.Button("Dump economy to log", UnityEngine.GUILayout.Width(240)))
                EconomyState.Instance.DumpToLog();
            if (UnityEngine.GUILayout.Button("Reload economy.json", UnityEngine.GUILayout.Width(240)))
                EconomyState.Instance.ReloadRecipes(ModEntry.Path);

            UnityEngine.GUILayout.Space(4);
            UnityEngine.GUILayout.Label("Phase 1 debug:");
            if (UnityEngine.GUILayout.Button("Direct Haul from my consist", UnityEngine.GUILayout.Width(240)))
                DebugDirectHaul.CreateFromPlayerConsist();
        }

        private static void DumpState()
        {
            var store = CarDestinationStore.Instance;
            var all = store.GetAll();
            Log($"[Debug] DLE state: {all.Count} tracked car(s)");

            var byPhase = all.Values
                .GroupBy(r => r.Phase)
                .OrderBy(g => (int)g.Key);
            foreach (var g in byPhase)
                Log($"[Debug]   {g.Key}: {g.Count()} car(s)");
        }

        private static void OnSaveGUI(UnityModManager.ModEntry entry)
        {
            Settings.Save(entry);
        }

        // Helpers

        public static void Log(string message) => ModEntry?.Logger?.Log(message);

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
