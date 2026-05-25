using DV.Logic.Job;
using GRDNInterchange.Data;
using GRDNInterchange.Jobs;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;

namespace GRDNInterchange.Patches
{
    /// <summary>
    /// Fires after any job chain completes. Routes GI-* job completions to the
    /// appropriate next-phase spawner based on the job ID prefix.
    ///
    ///   GI-FEED-*     → Feeder completed: trigger sort at hub
    ///   GI-SORT-*     → Sort completed:   check if block train threshold met
    ///   GI-BLOCK-*    → Block arrived:    mark cars BreakAtHub, spawn final-mile
    ///   GI-FM-*       → Final-mile done:  mark cars Delivered
    /// </summary>
    [HarmonyPatch(typeof(JobChainController), "OnLastJobInChainCompleted")]
    public static class JobCompletionPatch
    {
        [HarmonyPostfix]
        public static void Postfix(JobChainController __instance)
        {
            if (!GRDNInterchange.Main.IsHostOrSingleplayer()) return;

            var job = __instance.currentJobInChain;
            if (job == null) return;

            var jobId = job.ID ?? "";
            if (!jobId.StartsWith("GI-")) return;

            var registry = HubRegistry.Instance;
            var store    = CarDestinationStore.Instance;
            if (registry == null || store == null) return;

            Main.Log($"[JobCompletionPatch] GI job completed: {jobId}");

            // ── Feeder arrived at hub ──────────────────────────────────────────────
            if (jobId.StartsWith("GI-FEED-"))
            {
                var cars = GetTrainCarsFromChain(__instance);
                if (cars == null || cars.Count == 0) return;

                var destYardId = job.chainData?.chainDestinationYardId;
                if (string.IsNullOrEmpty(destYardId)) return;

                var hub = registry.GetHub(destYardId);
                if (hub == null) return;

                var inboundTrack = JobUtils.FindCommonTrack(cars);
                SortJobSpawner.SpawnSortJobs(cars, inboundTrack, hub);
                return;
            }

            // ── Sort job completed ─────────────────────────────────────────────────
            if (jobId.StartsWith("GI-SORT-"))
            {
                var hubYardId = job.chainData?.chainOriginYardId;
                if (string.IsNullOrEmpty(hubYardId)) return;

                var hub = registry.GetHub(hubYardId);
                if (hub == null) return;

                BlockTransportSpawner.TrySpawnBlock(hub, GRDNInterchange.Main.Settings);
                return;
            }

            // ── Block train arrived at receiving hub ───────────────────────────────
            if (jobId.StartsWith("GI-BLOCK-"))
            {
                var cars = GetTrainCarsFromChain(__instance);
                if (cars == null || cars.Count == 0) return;

                foreach (var c in cars)
                    store.SetPhase(c.CarGUID, InterchangePhase.BreakAtHub);

                var destYardId = job.chainData?.chainDestinationYardId;
                if (string.IsNullOrEmpty(destYardId)) return;

                var hub = registry.GetHub(destYardId);
                if (hub == null) return;

                var inboundTrack = JobUtils.FindCommonTrack(cars);
                SortJobSpawner.SpawnSortJobs(cars, inboundTrack, hub);
                FinalMileSpawner.SpawnFinalMileJobs(hub);
                return;
            }

            // ── Final-mile delivered ───────────────────────────────────────────────
            if (jobId.StartsWith("GI-FM-"))
            {
                var cars = GetTrainCarsFromChain(__instance);
                if (cars == null) return;

                foreach (var c in cars)
                    store.SetPhase(c.CarGUID, InterchangePhase.Delivered);

                Main.Log($"[JobCompletionPatch] {cars.Count} cars marked Delivered after {jobId}");
            }
        }

        private static List<TrainCar> GetTrainCarsFromChain(JobChainController jcc)
        {
            // JCC is NOT a MonoBehaviour — access its GameObject via jobChainGO
            var go = jcc.jobChainGO;
            if (go == null) return null;

            var tcRegistry = TrainCarRegistry.Instance;
            if (tcRegistry == null) return null;

            var transport = go.GetComponent<StaticTransportJobDefinition>();
            if (transport?.carsToTransport != null)
            {
                var result = new List<TrainCar>();
                foreach (var lc in transport.carsToTransport)
                    if (tcRegistry.logicCarToTrainCar.TryGetValue(lc, out var tc))
                        result.Add(tc);
                return result;
            }

            return null;
        }
    }
}
