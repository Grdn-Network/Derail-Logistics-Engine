using DV.Common;
using DV.Logic.Job;
using DV.ThingTypes;
using GRDNInterchange.Data;
using GRDNInterchange.Jobs;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
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

            if (IsHostOrSingleplayer())
            {
                // Intercept vanilla FH jobs that were loaded from save before HubRegistry
                // was ready — the Harmony patch saw !registry.IsReady and bailed on them.
                InterceptSavedVanillaJobs();

                // Scan for any cars with cargo but no job at all (e.g. mod installed mid-save)
                ScanAndRecoverOrphanedFeeders();
            }
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

        /// <summary>
        /// Retroactively intercept vanilla FH jobs that were loaded from a save file.
        /// DV calls FinalizeSetupAndGenerateFirstJob during world streaming, before
        /// OnWorldLoaded fires, so HubRegistry isn't ready and the Harmony patch skips them.
        /// This sweep runs once immediately after HubRegistry is initialised.
        /// </summary>
        private static void InterceptSavedVanillaJobs()
        {
            var registry  = HubRegistry.Instance;
            var store     = CarDestinationStore.Instance;
            var tcReg     = TrainCarRegistry.Instance;
            if (registry == null || !registry.IsReady || tcReg == null) return;

            var excluded = Settings.ExcludedYardIds;

            // jobChainControllers is private — reflect it once
            var jccField = typeof(StationProceduralJobsController).GetField(
                "jobChainControllers",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            int intercepted = 0;

            foreach (var sc in StationController.allStations)
            {
                var originYardId = sc.stationInfo.YardID;
                if (registry.IsHub(originYardId)) continue;
                if (excluded.Contains(originYardId)) continue;

                var assignedHubId = registry.GetAssignedHubId(originYardId);
                var hubStation    = registry.GetHub(assignedHubId);
                if (hubStation == null) continue;

                var chains = jccField?.GetValue(sc.ProceduralJobsController)
                             as List<JobChainController>;
                if (chains == null) continue;

                foreach (var jcc in chains.ToList()) // ToList — DestroyChain modifies the list
                {
                    var job = jcc.currentJobInChain;
                    if (job == null) continue;
                    if (job.jobType != JobType.Transport) continue;
                    if (Jobs.JobUtils.ManagedJobIds.Contains(job.ID)) continue;

                    var destYardId = job.chainData?.chainDestinationYardId;
                    if (string.IsNullOrEmpty(destYardId)) continue;
                    if (excluded.Contains(destYardId)) continue;
                    if (destYardId == assignedHubId) continue; // already going to the right hub

                    // Collect cars
                    var go        = jcc.jobChainGO;
                    var transport = go?.GetComponent<StaticTransportJobDefinition>();
                    if (transport?.carsToTransport == null) continue;

                    var cars = new List<TrainCar>();
                    foreach (var lc in transport.carsToTransport)
                        if (tcReg.logicCarToTrainCar.TryGetValue(lc, out var tc))
                            cars.Add(tc);
                    if (cars.Count == 0) continue;

                    var startTrack = Jobs.JobUtils.FindCommonTrack(cars);
                    if (startTrack == null) continue;

                    // Only destroy if hub has inbound space to accept the feeder
                    if (Jobs.JobUtils.BestInboundTrack(hubStation) == null)
                    {
                        Log($"[Main] Skipping late-intercept of {job.ID} — no inbound space at {assignedHubId}");
                        continue;
                    }

                    Log($"[Main] Late-intercepting saved job {job.ID} ({originYardId}→{destYardId})");
                    jcc.DestroyChain();

                    int max = Mathf.Max(1, Settings.MaxCarsPerFeeder);
                    for (int i = 0; i < cars.Count; i += max)
                    {
                        int take  = System.Math.Min(max, cars.Count - i);
                        var batch = cars.GetRange(i, take);
                        var jobId = Jobs.JobUtils.NextId(originYardId, destYardId);

                        foreach (var car in batch)
                            if (!store.IsInterchangeCar(car.CarGUID))
                                store.Register(car.CarGUID, destYardId, originYardId, assignedHubId, jobId);

                        Jobs.FeederJobSpawner.SpawnFeeder(batch, startTrack, sc, hubStation, jobId);
                    }
                    intercepted++;
                }
            }

            Log($"[Main] InterceptSavedVanillaJobs: {intercepted} job chain(s) replaced with feeders.");
        }

        private static void ScanAndRecoverOrphanedFeeders()
        {
            var registry = HubRegistry.Instance;
            if (registry == null || !registry.IsReady) return;

            var excluded = Settings.ExcludedYardIds;

            foreach (var sc in StationController.allStations)
            {
                var yardId = sc.stationInfo.YardID;
                if (registry.IsHub(yardId)) continue;
                if (excluded.Contains(yardId)) continue;   // never touch military/excluded yards
                FeederJobSpawner.ScanAndSpawnMissingFeeders(sc);
            }
        }

        // ── UMM settings GUI ──────────────────────────────────────────────────────

        private static void OnGUI(UnityModManager.ModEntry entry)
        {
            Settings.Draw(entry);

            UnityEngine.GUILayout.Space(8);
            if (UnityEngine.GUILayout.Button("Dump Interchange State to Log", UnityEngine.GUILayout.Width(280)))
                DumpState();
        }

        private static void DumpState()
        {
            var store = CarDestinationStore.Instance;
            if (store == null) { Log("[Debug] CarDestinationStore not initialised"); return; }

            var all = store.GetAll();
            Log($"[Debug] ── Interchange State ── ({all.Count} tracked cars)");

            // Group by phase for readability
            var byPhase = all.Values
                .GroupBy(r => r.Phase)
                .OrderBy(g => (int)g.Key);
            foreach (var g in byPhase)
                Log($"[Debug]   {g.Key}: {g.Count()} car(s)");

            // Per-car detail
            foreach (var kv in all)
            {
                var r = kv.Value;
                Log($"[Debug]   {kv.Key.Substring(0, 8)}… " +
                    $"{r.TrueOriginYardId}→{r.TrueDestYardId} via {r.AssignedHubYardId} [{r.Phase}]");
            }

            // Hub registry summary
            var reg = HubRegistry.Instance;
            if (reg != null)
            {
                Log("[Debug] Registered hubs:");
                foreach (var hub in reg.AllHubs)
                    Log($"[Debug]   {hub.stationInfo.YardID}");
            }
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
