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
    /// Intercepts every job chain created at a spoke station destined somewhere
    /// other than its assigned hub.
    ///
    /// Transport (FH) jobs → destroy vanilla chain, spawn GRDN feeder to hub.
    /// Any other type (LH, SL, SU, …) → destroy. No vanilla non-hub routing survives.
    ///
    /// If a feeder cannot be built (hub full, station at cap) → destroy anyway.
    /// Dead cars on track are preferable to confusing vanilla job noise on the board.
    /// </summary>
    [HarmonyPatch(typeof(JobChainController), nameof(JobChainController.FinalizeSetupAndGenerateFirstJob))]
    public static class NewJobChainInterceptPatch
    {
        [HarmonyPostfix]
        public static void Postfix(JobChainController __instance)
        {
            // Diagnostic: always log so we can confirm the hook fires at all.
            // If this line never appears for dynamic jobs, the hook itself isn't reaching us
            // (DVMP client-side JCC creation bypasses FinalizeSetupAndGenerateFirstJob).
            var _earlyJob = __instance?.currentJobInChain;
            Main.Log($"[Intercept] Postfix entered: id={_earlyJob?.ID ?? "null"} " +
                     $"type={_earlyJob?.jobType.ToString() ?? "null"} " +
                     $"origin={_earlyJob?.chainData?.chainOriginYardId ?? "null"}");

            if (!GRDNInterchange.Main.IsHostOrSingleplayer()) return;

            var registry = HubRegistry.Instance;
            if (registry == null || !registry.IsReady) return;

            var job = __instance.currentJobInChain;
            if (job == null) return;

            // Skip jobs we spawned — they're already routed correctly
            if (job.ID != null && Jobs.JobUtils.ManagedJobIds.Contains(job.ID)) return;

            var originYardId = job.chainData?.chainOriginYardId;
            var destYardId   = job.chainData?.chainDestinationYardId;

            if (string.IsNullOrEmpty(originYardId) || string.IsNullOrEmpty(destYardId))
                return;

            // Hub-originated jobs (sort, block, final-mile) are ours — leave them
            if (registry.IsHub(originYardId)) return;

            // Excluded yards (military, etc.) — never touch
            var excluded = GRDNInterchange.Main.Config.ExcludedYardIds;
            if (excluded.Contains(originYardId) || excluded.Contains(destYardId))
            {
                Main.Log($"[Intercept] Excluded route {originYardId}→{destYardId} — leaving {job.ID}");
                return;
            }

            // ── We are at a spoke station. Log everything from here for diagnosis. ──

            var assignedHubId = registry.GetAssignedHubId(originYardId);

            Main.Log($"[Intercept] Hook: {job.ID} type={job.jobType}({(int)job.jobType}) " +
                     $"{originYardId}→{destYardId} assignedHub={assignedHubId}");

            // ── Job is at a spoke. It must be intercepted (Transport) or killed (everything else). ──
            // Even Transport jobs already routed to the hub are replaced with GRDN feeders so
            // the cars are registered in CarDestinationStore and the sort/final-mile pipeline fires.

            var hubStation = registry.GetHub(assignedHubId);
            if (hubStation == null)
            {
                Main.Log($"[Intercept] Hub station {assignedHubId} not found — destroying {job.ID}");
                __instance.DestroyChain();
                return;
            }

            var originStation = StationController.allStations
                .FirstOrDefault(sc => sc.stationInfo.YardID == originYardId);
            if (originStation == null)
            {
                Main.Log($"[Intercept] Origin station {originYardId} not found — destroying {job.ID}");
                __instance.DestroyChain();
                return;
            }

            // Non-Transport job types (LH, SU, SL…) cannot be converted to feeders.
            // Log the int value so we can identify LH's enum entry from the log.
            if (job.jobType != JobType.Transport)
            {
                Main.Log($"[Intercept] Non-transport type {job.jobType}({(int)job.jobType}) " +
                         $"at spoke {originYardId} — destroying {job.ID}");
                __instance.DestroyChain();
                return;
            }

            // ── Transport job at a spoke going to wrong destination. Try feeder. ──

            var cars = GetJobTrainCars(__instance);
            if (cars == null || cars.Count == 0)
            {
                Main.Log($"[Intercept] {job.ID} — no cars resolved, destroying");
                __instance.DestroyChain();
                return;
            }

            var startingTrack = JobUtils.FindCommonTrack(cars);
            if (startingTrack == null)
            {
                Main.Log($"[Intercept] {job.ID} — FindCommonTrack returned null (cars: {cars.Count}) — destroying");
                __instance.DestroyChain();
                return;
            }

            // Hub inbound full → destroy, don't let the vanilla job through
            if (JobUtils.BestInboundTrack(hubStation) == null)
            {
                Main.Log($"[Intercept] {assignedHubId} inbound full — destroying {job.ID} at {originYardId}");
                __instance.DestroyChain();
                return;
            }

            // Station car cap → destroy, don't let the vanilla job through
            var store = CarDestinationStore.Instance;
            if (Main.Settings.MaxCarsPerStation > 0)
            {
                int active = store.CountByOriginAndPhase(originYardId, InterchangePhase.Feeder);
                if (active >= Main.Settings.MaxCarsPerStation)
                {
                    Main.Log($"[Intercept] {originYardId} at cap ({active}/{Main.Settings.MaxCarsPerStation}) " +
                             $"— destroying {job.ID}");
                    __instance.DestroyChain();
                    return;
                }
            }

            // ── All checks passed — replace with GRDN feeder ──────────────────────

            Main.Log($"[Intercept] Converting {job.ID} ({originYardId}→{destYardId}) → feeder to {assignedHubId}");
            __instance.DestroyChain();

            int max = Mathf.Max(1, Main.Settings.MaxCarsPerFeeder);
            for (int i = 0; i < cars.Count; i += max)
            {
                int take  = System.Math.Min(max, cars.Count - i);
                var batch = cars.GetRange(i, take);
                var jobId = JobUtils.NextId(originYardId, assignedHubId);

                foreach (var car in batch)
                    if (!store.IsInterchangeCar(car.CarGUID))
                        store.Register(car.CarGUID, destYardId, originYardId, assignedHubId, jobId);

                FeederJobSpawner.SpawnFeeder(batch, startingTrack, originStation, hubStation, jobId);
            }
        }

        private static List<TrainCar> GetJobTrainCars(JobChainController jcc)
        {
            var go = jcc.jobChainGO;
            if (go == null) return null;

            var tcRegistry = TrainCarRegistry.Instance;
            if (tcRegistry == null) return null;

            var transport = go.GetComponent<StaticTransportJobDefinition>();
            if (transport?.carsToTransport != null)
                return ResolveTrainCars(transport.carsToTransport, tcRegistry);

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
