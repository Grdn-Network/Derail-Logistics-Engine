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
            // MultiplayerAPI.dll is already in the AppDomain by then).
            ResolveDvmpApi();

            // The packet channel (client booklet data, plates, fax) arms only when DVMP
            // is really here; a pure singleplayer install never loads MultiplayerAPI.dll.
            // TryInit no-ops without DVMP and re-arms at world load if this attempt was
            // too early (assemblies load lazily; DVMP may not have touched its API yet).
            Dispatch.DleMpChannel.TryInit();

            LogAlways("[Main] Derail Logistics Engine loaded.");
            return true;
        }

        // World ready

        // #207: within a loaded session the role cannot change (a DVMP host stays host),
        // so the verdict latches at world load and every later call reads a plain bool
        // instead of paying two reflected property reads and their boxed results. The
        // hottest caller is the deleter patch, once per candidate car per vanilla sweep.
        // Latching EARLIER than world load is the trap #207 warns about: a host asked
        // before its server starts reads as a client and caches the lie.
        private static bool _roleLatched;
        private static bool _roleCache;

        private static void OnWorldLoaded()
        {
            Log("[Main] World loaded; initialising DLE systems.");
            _roleLatched = false;

            // Build the economy for this world (recipes from stations, overlay, saved
            // stock; a fresh economy seeds its starting stock inside Init).
            try
            {
                var data = SaveGameManager.Instance?.data;
                if (data != null)
                    EconomyState.Instance.Init(data, ModEntry.Path);
            }
            catch (Exception ex)
            {
                LogAlways($"[Main] Economy init failed ({ex.GetType().Name}): {ex.Message}");
            }

            // Rebuild live Direct Haul jobs from DLE's own save data (they are filtered out of the
            // vanilla job save), restore assignments, and start the local HTTP API.
            // Host only; clients receive jobs from the host via DVMP.
            bool hostOrSp = IsHostOrSingleplayer();
            // Only a settled DVMP resolution latches: present-but-unresolved keeps the
            // live check so a slow-arming client can never cache the wrong role.
            if (_dvmpResolved) { _roleCache = hostOrSp; _roleLatched = true; }
            LogAlways(hostOrSp
                ? "[Main] running as host/singleplayer; DLE server logic active."
                : "[Main] running as a multiplayer client; DLE host logic stays off (the host runs it).");

            // Late-arm the packet channel (the load-time attempt can predate DVMP's lazy
            // assembly load) and re-announce to the host: the ClientStarted hello can
            // fire before the connection settles, and the world being loaded is the one
            // moment we KNOW the session is fully up.
            Dispatch.DleMpChannel.TryInit();
            if (!hostOrSp)
            {
                Dispatch.DleMpChannel.AnnounceToHost();
                // Local stale-paper sweep (#73): the game respawns office overviews on
                // every peer, so host-side destroys never reach this client's copies.
                Dispatch.DleClientPaperSweeper.StartOnClient();
                // The board server now survives world reloads (#215); a world loaded as
                // a CLIENT must not keep serving the previous hosted session's board.
                Dispatch.DleHttpServer.Stop();
            }
            if (hostOrSp)
            {
                var data = SaveGameManager.Instance?.data;
                if (data != null)
                {
                    // Each store restores under its own guard: a failure in one must not
                    // abandon the others. A job-restore throw that skipped the car-pool arm
                    // would strand the whole fleet for the deleter, and skipping the
                    // assignment/board load would leave the previous session's data live to
                    // be written into this save.
                    SafeRestore("car pool", () => { DleCarPool.Instance.LoadFrom(data); DleCarPool.Instance.PruneDeadGuids(); });
                    SafeRestore("jobs", () => DleJobStore.RestoreFrom(data));
                    SafeRestore("taken-order reconcile", () => Dispatch.DispatchLifecycle.ReconcileTakenOrders());
                    SafeRestore("assignments", () => AssignmentStore.Instance.LoadFrom(data));
                    SafeRestore("logistics board", () => LogisticsBoard.Instance.LoadFrom(data));
                }
                DleHttpServer.StartOnHost();
                // The director behaviour also runs the one-time pool seeding once the
                // world is fully live; spawning cars straight from LoadingFinished
                // silently failed.
                DleDirectorBehaviour.StartOnHost();
            }

            // Subscribe to the save event so DLE state persists before the game writes to disk.
            // Unsubscribe first: OnWorldLoaded fires on every save/load within a session;
            // without the unsub, each reload would add another handler and OnAboutToSave
            // would be called N times per save after N reloads.
            SaveGameManager.AboutToSave -= OnAboutToSave;
            SaveGameManager.AboutToSave += OnAboutToSave;
        }

        private static void OnAboutToSave(SaveType saveType)
        {
            var data = SaveGameManager.Instance?.data;
            if (data == null) return;
            // Each store writes under its own guard. One store throwing must not skip the
            // rest: the vanilla save proceeds regardless, so a half-written DLE state (e.g.
            // fresh car positions paired with a stale guid list) would let the deleter cull
            // every car spawned since the last clean save.
            SafeRestore("economy save", () => EconomyState.Instance.SaveTo(data));
            SafeRestore("jobs save", () => DleJobStore.SaveTo(data));
            SafeRestore("assignments save", () => AssignmentStore.Instance.SaveTo(data));
            SafeRestore("car pool save", () => DleCarPool.Instance.SaveTo(data));
            SafeRestore("logistics board save", () => LogisticsBoard.Instance.SaveTo(data));
        }

        /// <summary>Run one save/restore step under its own guard so a single failure is
        /// logged and confined instead of abandoning every step after it.</summary>
        private static void SafeRestore(string what, Action step)
        {
            try { step(); }
            catch (Exception ex)
            {
                LogAlways($"[Main] {what} failed ({ex.GetType().Name}: {ex.Message}); continuing with the rest.");
            }
        }

        // UMM settings GUI

        private static void OnGUI(UnityModManager.ModEntry entry)
        {
            Settings.Draw(entry);

            UnityEngine.GUILayout.Space(10);
            if (UnityEngine.GUILayout.Button("Reload economy.json", UnityEngine.GUILayout.Width(240)))
                EconomyState.Instance.ReloadRecipes(ModEntry.Path);
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
        /// Returns true when server-side logic should run here.
        /// In single-player this is always true; in multiplayer only the host.
        /// Uses reflection against the public DVMP MultiplayerAPI so there is no
        /// hard compile-time dependency on MultiplayerAPI.dll.
        /// </summary>
        public static bool IsHostOrSingleplayer()
        {
            if (_roleLatched) return _roleCache; // settled at world load (#207)
            ResolveDvmpApi(); // idempotent: resolves once then returns immediately
            if (_mpApiInstance == null) return true; // no DVMP present, single-player
            try
            {
                bool isHost = (bool)(_isHostProp?.GetValue(_mpApiInstance) ?? true);
                bool isSP   = (bool)(_isSinglePlayerProp?.GetValue(_mpApiInstance) ?? true);

                // A joined DVMP client has BOTH false as its steady state (verified against
                // dv-mp: IsHost => Server.IsRunning, IsSinglePlayer => Server.IsRunning &&
                // Server.IsSinglePlayer, and a client runs no server). A real host or SP
                // session always has the local server running by the time any DLE code runs,
                // so isHost || isSP is true for them. The old "both false defaults to HOST"
                // fallback made every client run the host stack (duplicate jobs, phantom
                // cars, double pay) and is removed on purpose.
                return isHost || isSP;
            }
            catch (Exception ex)
            {
                // Fail toward host/SP: a reflection break must not silently disable DLE in
                // singleplayer. Logged unconditionally so a client mis-detection is visible.
                LogAlways($"[Main] host detection threw ({ex.GetType().Name}: {ex.Message}); assuming host/singleplayer.");
                return true;
            }
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

            try
            {
                // MultiplayerAPI.dll is loaded by DVMP before DLE (LoadAfter: ["Multiplayer"])
                var mpApiType = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "MultiplayerAPI")
                    ?.GetType("MPAPI.MultiplayerAPI");

                if (mpApiType == null)
                {
                    // No DVMP installed at all: this never changes within a session, so
                    // latch it and treat every session as singleplayer.
                    _dvmpResolved = true;
                    Log("[Main] DVMP MultiplayerAPI not found; assuming single-player");
                    return;
                }

                var isLoaded = (bool)(mpApiType.GetProperty("IsMultiplayerLoaded")?.GetValue(null) ?? false);
                if (!isLoaded)
                {
                    // DVMP present but not yet initialised. Do NOT latch: it may load later
                    // this session (a client that joins after DLE's menu-time load). Latching
                    // here left _mpApiInstance null forever, so IsHostOrSingleplayer reported
                    // singleplayer and a client ran the host stack.
                    Log("[Main] DVMP present but multiplayer not loaded; will retry");
                    return;
                }

                _mpApiInstance = mpApiType.GetProperty("Instance")?.GetValue(null);
                if (_mpApiInstance == null)
                {
                    Log("[Main] DVMP Instance is null; will retry");
                    return; // not latched: retry on the next call
                }

                var instanceType = _mpApiInstance.GetType();
                _isHostProp         = instanceType.GetProperty("IsHost");
                _isSinglePlayerProp = instanceType.GetProperty("IsSinglePlayer");
                _dvmpResolved = true; // fully hooked; stop retrying

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
