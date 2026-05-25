using DV.Common;
using GRDNInterchange.Data;
using GRDNInterchange.Jobs;
using HarmonyLib;
using UnityModManagerNet;

namespace GRDNInterchange
{
    public static class Main
    {
        public static UnityModManager.ModEntry ModEntry { get; private set; }
        public static Settings Settings { get; private set; }

        private static Harmony _harmony;

        // ── UMM entry point ────────────────────────────────────────────────────────

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            ModEntry = modEntry;

            Settings = UnityModManager.ModSettings.Load<Settings>(modEntry);
            modEntry.OnSaveGUI = OnSaveGUI;
            modEntry.OnGUI     = OnGUI;

            _harmony = new Harmony(modEntry.Info.Id);
            _harmony.PatchAll();

            // Hook world-ready event so HubRegistry and CarDestinationStore can init
            // after all stations are loaded.
            WorldStreamingInit.LoadingFinished += OnWorldLoaded;

            Log("[Main] GRDN Interchange loaded.");
            return true;
        }

        // ── World ready ────────────────────────────────────────────────────────────

        private static void OnWorldLoaded()
        {
            Log("[Main] World loaded — initialising interchange systems.");

            HubRegistry.Initialize(Settings);

            // Load car store from save data (instance field on SaveGameManager singleton)
            try
            {
                var saveData = SaveGameManager.Instance?.data;
                if (saveData != null)
                    CarDestinationStore.Instance.LoadFromSave(saveData);
            }
            catch (System.Exception ex)
            {
                Log($"[Main] Could not load CarDestinationStore from save: {ex.Message}");
            }

            // Subscribe to save event so we persist before the game writes to disk
            SaveGameManager.AboutToSave += OnAboutToSave;

            // Scan all origin stations for cars that might have slipped through
            // (e.g. loaded world mid-job before the mod was installed)
            if (IsHostOrSingleplayer())
                ScanAndRecoverOrphanedFeeders();
        }

        private static void OnAboutToSave(SaveType saveType)
        {
            try
            {
                var data = SaveGameManager.Instance?.data;
                if (data != null)
                {
                    CarDestinationStore.Instance.SaveTo(data);
                    Log("[Main] CarDestinationStore saved.");
                }
            }
            catch (System.Exception ex)
            {
                Log($"[Main] Error saving CarDestinationStore: {ex.Message}");
            }
        }

        private static void ScanAndRecoverOrphanedFeeders()
        {
            var registry = HubRegistry.Instance;
            if (registry == null || !registry.IsReady) return;

            foreach (var sc in StationController.allStations)
            {
                var yardId = sc.stationInfo.YardID;
                if (registry.IsHub(yardId)) continue;
                FeederJobSpawner.ScanAndSpawnMissingFeeders(sc);
            }
        }

        // ── UMM settings GUI ──────────────────────────────────────────────────────

        private static void OnGUI(UnityModManager.ModEntry entry)
        {
            Settings.Draw(entry);
        }

        private static void OnSaveGUI(UnityModManager.ModEntry entry)
        {
            Settings.Save(entry);
        }

        // ── Helpers ────────────────────────────────────────────────────────────────

        public static void Log(string message) => ModEntry?.Logger?.Log(message);

        /// <summary>
        /// Returns true when we should be running server-side logic.
        /// In single-player this is always true; in multiplayer only the host.
        /// </summary>
        public static bool IsHostOrSingleplayer()
        {
            // Multiplayer check — expand when DV's networking API is confirmed.
            // For now, always act as host (safe for single-player development).
            return true;
        }
    }
}
