using DV.Logic.Job;
using DV.ThingTypes;
using GRDNInterchange.Data;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GRDNInterchange.Jobs
{
    /// <summary>
    /// After a block train arrives at the receiving hub and the break/sort is done,
    /// spawns small Transport jobs from hub storage → true final destination.
    ///
    /// Groups cars by their TrueDestYardId so each final destination gets its own job.
    /// Hub-local cars (TrueDest == hub's own yardId) are already handled by SortJobSpawner
    /// and should not reach this spawner, but they're skipped defensively.
    /// </summary>
    public static class FinalMileSpawner
    {
        /// <summary>
        /// Spawn final-mile jobs for all cars currently in storage at the hub
        /// that are in the BreakAtHub phase (ready for final delivery).
        /// </summary>
        public static void SpawnFinalMileJobs(StationController hub)
        {
            var hubYardId = hub.stationInfo.YardID;
            var store     = CarDestinationStore.Instance;
            var registry  = HubRegistry.Instance;

            if (store == null || registry == null) return;

            // Collect all cars in storage at this hub that are awaiting final mile
            var storageTracks = TrackClassifier.GetStorageTracks(hub, allowPaxOverflow: true);
            var readyCars     = new List<TrainCar>();

            var trainCarRegistry = TrainCarRegistry.Instance;
            if (trainCarRegistry == null) return;

            foreach (var track in storageTracks)
            {
                var logicCars = track.GetCarsFullyOnTrack();
                if (logicCars == null) continue;

                foreach (var lc in logicCars)
                {
                    if (!trainCarRegistry.logicCarToTrainCar.TryGetValue(lc, out var tc)) continue;
                    var rec = store.Get(tc.CarGUID);
                    if (rec == null || rec.Phase != InterchangePhase.BreakAtHub) continue;
                    if (rec.TrueDestYardId == hubYardId) continue; // hub-local, already delivered
                    readyCars.Add(tc);
                }
            }

            if (readyCars.Count == 0)
            {
                Main.Log($"[FinalMileSpawner] No BreakAtHub cars at {hubYardId}");
                return;
            }

            // Group by true destination
            var byDest = readyCars.GroupBy(tc => store.Get(tc.CarGUID)?.TrueDestYardId ?? "");

            foreach (var group in byDest)
            {
                var destYardId = group.Key;
                if (string.IsNullOrEmpty(destYardId)) continue;

                var cars = group.ToList();

                // Find the destination StationController
                var destStation = StationController.allStations
                    .FirstOrDefault(sc => sc.stationInfo.YardID == destYardId);
                if (destStation == null)
                {
                    Main.Log($"[FinalMileSpawner] Station not found for dest {destYardId}");
                    continue;
                }

                // Pick the loading track at destination (or fallback to any inbound)
                var destTrack = BestFinalMileDestTrack(destStation);
                if (destTrack == null)
                {
                    Main.Log($"[FinalMileSpawner] No suitable track at {destYardId} for final-mile delivery");
                    continue;
                }

                // Cars could be spread across multiple storage tracks; pick the most common one
                var fromTrack = JobUtils.FindCommonTrack(cars);
                if (fromTrack == null)
                {
                    Main.Log($"[FinalMileSpawner] Could not determine source track for {destYardId} cars");
                    continue;
                }

                SpawnFinalMileJob(cars, fromTrack, destTrack, hub, hubYardId, destYardId, destStation);

                foreach (var c in cars)
                    store.SetPhase(c.CarGUID, InterchangePhase.FinalMile);

                Main.Log($"[FinalMileSpawner] Final-mile {hubYardId}→{destYardId}: {cars.Count} cars");
            }
        }

        /// <summary>
        /// Prefer a loading track at the destination; fall back to inbound or any track.
        /// </summary>
        private static Track BestFinalMileDestTrack(StationController dest)
        {
            if (dest?.logicStation?.yard == null) return null;
            var yard = dest.logicStation.yard;

            // Prefer inbound (TransferIn) track — that's where deliveries arrive
            var inbound = (yard.TransferInTracks ?? new List<Track>())
                .OrderByDescending(t => t.length - t.OccupiedLength)
                .FirstOrDefault();
            if (inbound != null) return inbound;

            // Fall back to storage
            var storage = (yard.StorageTracks ?? new List<Track>())
                .OrderByDescending(t => t.length - t.OccupiedLength)
                .FirstOrDefault();
            return storage;
        }

        private static void SpawnFinalMileJob(
            List<TrainCar> cars,
            Track fromTrack,
            Track toTrack,
            StationController hub,
            string hubYardId,
            string destYardId,
            StationController destStation)
        {
            _ = destStation; // reserved for future ShuntingUnload variant

            var go  = new GameObject($"GI-FM-{hubYardId}-{destYardId}");
            var def = go.AddComponent<StaticTransportJobDefinition>();

            def.carsToTransport         = JobUtils.ToLogicCars(cars);
            def.startingTrack           = fromTrack;
            def.destinationTrack        = toTrack;
            def.transportedCargoPerCar  = JobUtils.GetCargoes(cars);
            def.cargoAmountPerCar       = JobUtils.GetCargoAmounts(cars);
            def.forceCorrectCargoStateOnCars = false;

            def.PopulateBaseJobDefinition(
                hub.logicStation,
                JobUtils.EstimateTimeLimit(cars.Count, distanceFactor: 1.2f),
                JobUtils.EstimateWage(cars.Count, distanceFactor: 1.2f),
                JobUtils.Chain(hubYardId, destYardId),
                JobLicenses.Basic | JobLicenses.FreightHaul
            );
            def.ForceJobId(JobUtils.NextId("FM"));

            JobUtils.ActivateJobChain(def, hub);
            Main.Log($"[FinalMileSpawner] Spawned {def.job?.ID}: {hubYardId}→{destYardId} ({cars.Count} cars)");
        }
    }
}
