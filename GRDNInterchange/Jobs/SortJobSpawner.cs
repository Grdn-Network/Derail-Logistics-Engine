using DV.Logic.Job;
using DV.ThingTypes;
using GRDNInterchange.Data;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GRDNInterchange.Jobs
{
    /// <summary>
    /// When a feeder train arrives at a hub, spawns small Transport jobs ("shunt-sort")
    /// that move each car group from the inbound track to the appropriate storage or
    /// outbound track based on the car's true final destination.
    ///
    /// Cross-hub cars  → outbound track (to be picked up by block train)
    /// Same-hub cars   → storage track grouped with cars going to the same area
    /// Hub-local cars  → storage track at the hub (final destination IS the hub)
    /// </summary>
    public static class SortJobSpawner
    {
        public static void SpawnSortJobs(
            List<TrainCar> arrivedCars,
            Track inboundTrack,
            StationController hub)
        {
            var hubYardId    = hub.stationInfo.YardID;
            var store        = CarDestinationStore.Instance;
            var registry     = HubRegistry.Instance;

            // Partition cars: same-hub-side vs cross-hub vs hub-local
            var crossHubCars    = new List<TrainCar>();
            var sameHubCars     = new Dictionary<string, List<TrainCar>>(); // keyed by trueDestYardId
            var hubLocalCars    = new List<TrainCar>();

            foreach (var car in arrivedCars)
            {
                var rec = store.Get(car.CarGUID);
                if (rec == null)
                {
                    Main.Log($"[SortJobSpawner] Car {car.CarGUID} not in store — skipping sort");
                    continue;
                }

                var trueDestYardId = rec.TrueDestYardId;

                if (trueDestYardId == hubYardId)
                {
                    // Destination IS this hub — it was the final stop
                    hubLocalCars.Add(car);
                }
                else if (registry != null && registry.GetAssignedHubId(trueDestYardId) == hubYardId)
                {
                    // Same hub side — goes to storage grouped by destination
                    if (!sameHubCars.TryGetValue(trueDestYardId, out var group))
                    {
                        group = new List<TrainCar>();
                        sameHubCars[trueDestYardId] = group;
                    }
                    group.Add(car);
                }
                else
                {
                    // Cross-hub — goes to outbound track for the block train
                    crossHubCars.Add(car);
                }
            }

            // ── Cross-hub cars → outbound track ───────────────────────────────────
            if (crossHubCars.Count > 0)
            {
                var outboundTrack = JobUtils.BestOutboundTrack(hub);
                if (outboundTrack != null)
                {
                    SpawnMoveJob(crossHubCars, inboundTrack, outboundTrack, hub, hubYardId);
                    foreach (var c in crossHubCars)
                        store.SetPhase(c.CarGUID, InterchangePhase.SortAtHub);
                    Main.Log($"[SortJobSpawner] {crossHubCars.Count} cross-hub cars → outbound at {hubYardId}");
                }
                else
                {
                    Main.Log($"[SortJobSpawner] No outbound track at {hubYardId} for {crossHubCars.Count} cross-hub cars");
                }
            }

            // ── Same-side cars → storage grouped by destination ───────────────────
            foreach (var kv in sameHubCars)
            {
                var destYardId    = kv.Key;
                var cars          = kv.Value;
                var storageTrack  = JobUtils.BestStorageTrack(hub, destYardId);
                if (storageTrack == null)
                {
                    Main.Log($"[SortJobSpawner] No storage track at {hubYardId} for dest {destYardId}");
                    continue;
                }
                SpawnMoveJob(cars, inboundTrack, storageTrack, hub, hubYardId);
                // Mark BreakAtHub so FinalMileSpawner picks these up once the sort job completes
                foreach (var c in cars)
                    store.SetPhase(c.CarGUID, InterchangePhase.BreakAtHub);
                Main.Log($"[SortJobSpawner] {cars.Count} same-side cars for {destYardId} → storage at {hubYardId}");
            }

            // ── Hub-local cars → storage (they'll get a final-mile job immediately) ─
            if (hubLocalCars.Count > 0)
            {
                var storageTrack = JobUtils.BestStorageTrack(hub, hubYardId);
                if (storageTrack != null)
                {
                    SpawnMoveJob(hubLocalCars, inboundTrack, storageTrack, hub, hubYardId);
                    foreach (var c in hubLocalCars)
                        store.SetPhase(c.CarGUID, InterchangePhase.Delivered);
                }
            }
        }

        /// <summary>
        /// Spawn a sort/shunt move job within the hub. Both origin and destination
        /// are the same yard (hubYardId), so the job ID is e.g. "HB-HB-01".
        /// </summary>
        private static void SpawnMoveJob(
            List<TrainCar> cars,
            Track fromTrack,
            Track toTrack,
            StationController hub,
            string hubYardId)
        {
            var go  = new GameObject($"GRDN-Sort-{hubYardId}");
            var def = go.AddComponent<StaticTransportJobDefinition>();

            def.carsToTransport         = JobUtils.ToLogicCars(cars);
            def.startingTrack           = fromTrack;
            def.destinationTrack        = toTrack;
            def.transportedCargoPerCar  = JobUtils.GetCargoes(cars);
            def.cargoAmountPerCar       = JobUtils.GetCargoAmounts(cars);
            def.forceCorrectCargoStateOnCars = false;

            def.PopulateBaseJobDefinition(
                hub.logicStation,
                JobUtils.EstimateTimeLimit(cars.Count, distanceFactor: 0.3f),
                JobUtils.EstimateWage(cars.Count, distanceFactor: 0.3f),
                JobUtils.Chain(hubYardId, hubYardId),
                JobLicenses.Basic | JobLicenses.Shunting
            );
            def.ForceJobId(JobUtils.NextId(hubYardId, hubYardId));

            JobUtils.ActivateJobChain(def, hub);
            Main.Log($"[SortJobSpawner] Spawned sort job {def.job?.ID} ({cars.Count} cars, {fromTrack?.ID?.FullID}→{toTrack?.ID?.FullID})");
        }
    }
}
