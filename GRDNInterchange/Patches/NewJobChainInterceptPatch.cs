using DV.Logic.Job;
using DV.ThingTypes;
using GRDNInterchange.Data;
using GRDNInterchange.Jobs;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GRDNInterchange.Patches
{
    /// <summary>
    /// Intercepts vanilla job chain creation.
    ///
    /// When a vanilla FreightHaul/Transport job is generated for an origin station
    /// (not a hub) destined for a non-hub station, we:
    ///   1. Extract the true destination from the job's chain data.
    ///   2. Register each car in CarDestinationStore with its true destination.
    ///   3. Destroy the vanilla job chain.
    ///   4. Spawn a feeder job from the origin to the assigned hub.
    ///
    /// We only intercept Transport (FH) jobs. ShuntingLoad (SL) jobs are skipped
    /// because those cars are not yet loaded (CargoType.None), and DV generates a
    /// Transport job naturally when the SL completes — we intercept that one instead.
    ///
    /// We only intercept on the host player; clients see the GRDN jobs
    /// replicated normally by DV's own networking.
    /// </summary>
    [HarmonyPatch(typeof(JobChainController), nameof(JobChainController.FinalizeSetupAndGenerateFirstJob))]
    public static class NewJobChainInterceptPatch
    {
        [HarmonyPostfix]
        public static void Postfix(JobChainController __instance)
        {
            // Only run on host
            if (!GRDNInterchange.Main.IsHostOrSingleplayer()) return;

            var registry = HubRegistry.Instance;
            if (registry == null || !registry.IsReady) return;

            var job = __instance.currentJobInChain;
            if (job == null) return;

            // Skip jobs we spawned ourselves (tracked by ID in JobUtils.ManagedJobIds)
            if (job.ID != null && Jobs.JobUtils.ManagedJobIds.Contains(job.ID)) return;

            // Only intercept Transport (FH) jobs — not ShuntingLoad, EmptyHaul, etc.
            if (job.jobType != JobType.Transport)
                return;

            // Extract origin and destination yard IDs from the job's chain data
            var originYardId = job.chainData?.chainOriginYardId;
            var destYardId   = job.chainData?.chainDestinationYardId;

            if (string.IsNullOrEmpty(originYardId) || string.IsNullOrEmpty(destYardId))
                return;

            // Skip excluded yards (military chain, etc.) — configurable in UMM settings
            var excluded = GRDNInterchange.Main.Settings.ExcludedYardIds;
            if (excluded.Contains(originYardId) || excluded.Contains(destYardId))
            {
                Main.Log($"[Intercept] Skipping excluded route {originYardId}→{destYardId}");
                return;
            }

            // If origin is a hub — let it pass (sort/block/final-mile jobs can target hubs)
            if (registry.IsHub(originYardId)) return;

            // If destination is already the assigned hub — also let it pass
            var assignedHubId = registry.GetAssignedHubId(originYardId);
            if (destYardId == assignedHubId) return;

            // Find the hub station
            var hubStation = registry.GetHub(assignedHubId);
            if (hubStation == null) return;

            // Find the origin StationController
            var originStation = StationController.allStations
                .FirstOrDefault(sc => sc.stationInfo.YardID == originYardId);
            if (originStation == null) return;

            // Collect the cars from this job (logic Cars → resolve TrainCar objects)
            var cars = GetJobTrainCars(__instance);
            if (cars == null || cars.Count == 0) return;

            // ── Pre-flight: verify the replacement can actually be built ──────────
            // Do NOT destroy the vanilla job until we are certain a feeder can replace it.

            var startingTrack = JobUtils.FindCommonTrack(cars);
            if (startingTrack == null)
            {
                Main.Log($"[Intercept] Skipping {job.ID} — could not determine starting track");
                return;
            }

            if (JobUtils.BestInboundTrack(hubStation) == null)
            {
                Main.Log($"[Intercept] Skipping {job.ID} — no inbound space at {assignedHubId}");
                return;
            }

            // All checks passed — safe to replace
            Main.Log($"[Intercept] Replacing vanilla job {job.ID} ({originYardId}→{destYardId}) with feeder to {assignedHubId}");
            __instance.DestroyChain();

            var store = CarDestinationStore.Instance;

            // Spawn feeder job(s), capped at MaxCarsPerFeeder per job.
            // Each batch gets its own job ID based on true origin/destination (e.g. "SM-GF-77").
            // This ID will be reused for the final-mile leg so the player sees the same number
            // on pick-up (at origin) and on delivery (from hub to true destination).
            int max = Mathf.Max(1, Main.Settings.MaxCarsPerFeeder);
            for (int i = 0; i < cars.Count; i += max)
            {
                int take  = System.Math.Min(max, cars.Count - i);
                var batch = cars.GetRange(i, take);

                // Job ID encodes the true journey, not the intermediate hub leg
                var jobId = JobUtils.NextId(originYardId, destYardId);

                foreach (var car in batch)
                    if (!store.IsInterchangeCar(car.CarGUID))
                        store.Register(car.CarGUID, destYardId, originYardId, assignedHubId, jobId);

                FeederJobSpawner.SpawnFeeder(batch, startingTrack, originStation, hubStation, jobId);
            }
        }

        private static List<TrainCar> GetJobTrainCars(JobChainController jcc)
        {
            // JCC is NOT a MonoBehaviour — use its jobChainGO reference.
            var go = jcc.jobChainGO;
            if (go == null) return null;

            var tcRegistry = TrainCarRegistry.Instance;
            if (tcRegistry == null) return null;

            // StaticTransportJobDefinition
            var transport = go.GetComponent<StaticTransportJobDefinition>();
            if (transport?.carsToTransport != null)
                return ResolveTrainCars(transport.carsToTransport, tcRegistry);

            // StaticShuntingLoadJobDefinition — grab from carsPerStartingTrack
            var load = go.GetComponent<StaticShuntingLoadJobDefinition>();
            if (load?.carsPerStartingTrack != null)
            {
                var logicCars = new List<Car>();
                foreach (var cs in load.carsPerStartingTrack)
                    if (cs?.cars != null)
                        logicCars.AddRange(cs.cars);
                return ResolveTrainCars(logicCars, tcRegistry);
            }

            return null;
        }

        private static List<TrainCar> ResolveTrainCars(
            IEnumerable<Car> logicCars,
            TrainCarRegistry registry)
        {
            var result = new List<TrainCar>();
            foreach (var lc in logicCars)
                if (registry.logicCarToTrainCar.TryGetValue(lc, out var tc))
                    result.Add(tc);
            return result;
        }
    }
}
