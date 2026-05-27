using DV.Logic.Job;
using GRDNInterchange.Data;
using GRDNInterchange.Jobs;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;

namespace GRDNInterchange.Patches
{
    /// <summary>
    /// Fires after any job chain completes. Routes GRDN job completions to the
    /// appropriate next-phase spawner using hub topology.
    ///
    ///   Spoke → Hub           = Feeder completed → trigger sort at hub
    ///   Hub   → same Hub      = Sort completed   → check block threshold + final mile
    ///   Hub   → different Hub = Block arrived    → mark BreakAtHub, spawn final-mile
    ///   Hub   → Spoke         = Final-mile done  → mark Delivered
    ///
    /// Job IDs are in {trueOrigin}-{trueDest}-{NN} format (e.g. "SM-GF-77") and are
    /// reused across the feeder and final-mile legs of the same shipment.
    /// Sort and block jobs carry hub-route IDs (e.g. "HB-HB-01", "HB-MF-01").
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

            var jobId = job.ID ?? string.Empty;

            // NOTE: ManagedJobIds guard has been intentionally removed.
            // ManagedJobIds is empty after every save/load (it is not persisted), so
            // checking it here would cause the pipeline to silently die after any reload.
            // Downstream spawners guard themselves: SortJobSpawner checks CarDestinationStore,
            // FinalMileSpawner checks phase == BreakAtHub, BlockTransportSpawner checks
            // IsInterchangeCar. Vanilla job completions are no-ops because none of those
            // phase/store guards match.

            var registry = HubRegistry.Instance;
            var store    = CarDestinationStore.Instance;
            if (registry == null || store == null) return;

            Main.Log($"[JobCompletionPatch] GRDN job completed: {jobId}");

            var originYardId = job.chainData?.chainOriginYardId;
            var destYardId   = job.chainData?.chainDestinationYardId;
            if (string.IsNullOrEmpty(originYardId) || string.IsNullOrEmpty(destYardId)) return;

            bool originIsHub = registry.IsHub(originYardId);
            bool destIsHub   = registry.IsHub(destYardId);

            // Sort jobs carry "-SO-" in their ID regardless of chain data destination.
            // Cross-hub sort jobs have chain data hub→OTHER_HUB so origin!=dest; we must
            // detect them by ID pattern before the block-haul branch matches them.
            bool isSortJob = jobId.Contains("-SO-");

            // ── Feeder: spoke → hub ────────────────────────────────────────────────
            if (!originIsHub && destIsHub)
            {
                var cars = GetTrainCarsFromChain(__instance);
                if (cars == null || cars.Count == 0) return;

                var hub = registry.GetHub(destYardId);
                if (hub == null) return;

                var inboundTrack = JobUtils.FindCommonTrack(cars);
                SortJobSpawner.SpawnSortJobs(cars, inboundTrack, hub);
                return;
            }

            // ── Sort (intra-hub shunt): identified by "-SO-" in job ID ─────────────
            // Chain data may be hub→hub (same-hub sort) or hub→other_hub (cross-hub sort
            // booklet shows next stop). In both cases completion logic is the same.
            if (originIsHub && isSortJob)
            {
                var hub = registry.GetHub(originYardId);
                if (hub == null) return;

                // Check if a cross-hub block train threshold has been met
                BlockTransportSpawner.TrySpawnBlock(hub, GRDNInterchange.Main.Settings);

                // Spawn final-mile for any same-hub cars (now in BreakAtHub phase)
                FinalMileSpawner.SpawnFinalMileJobs(hub);
                return;
            }

            // ── Block haul: hub → different hub (not a sort job) ──────────────────
            if (originIsHub && destIsHub && originYardId != destYardId)
            {
                var cars = GetTrainCarsFromChain(__instance);
                if (cars == null || cars.Count == 0) return;

                foreach (var c in cars)
                    store.SetPhase(c.CarGUID, InterchangePhase.BreakAtHub);

                var hub = registry.GetHub(destYardId);
                if (hub == null) return;

                var inboundTrack = JobUtils.FindCommonTrack(cars);
                SortJobSpawner.SpawnSortJobs(cars, inboundTrack, hub);
                FinalMileSpawner.SpawnFinalMileJobs(hub);
                return;
            }

            // ── Final mile: hub → spoke ────────────────────────────────────────────
            if (originIsHub && !destIsHub)
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
