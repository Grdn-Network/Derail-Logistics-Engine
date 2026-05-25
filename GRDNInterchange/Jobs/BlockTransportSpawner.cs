using DV.Logic.Job;
using DV.ThingTypes;
using GRDNInterchange.Data;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GRDNInterchange.Jobs
{
    /// <summary>
    /// Spawns a hub-to-hub block transport job once enough cross-hub cars have
    /// accumulated on an outbound track at the origin hub.
    ///
    /// Called either:
    ///   (a) When a sort job completes and the outbound track now meets threshold, or
    ///   (b) From the world-load scan to recover any orphaned outbound-ready cars.
    /// </summary>
    public static class BlockTransportSpawner
    {
        /// <summary>
        /// Check the outbound track(s) at fromHub. If any has >= threshold cars,
        /// spawn a block transport to the opposite hub.
        /// </summary>
        public static void TrySpawnBlock(StationController fromHub, Settings settings)
        {
            var registry = HubRegistry.Instance;
            if (registry == null) return;

            var fromYardId   = fromHub.stationInfo.YardID;
            var toYardId     = registry.GetOppositeHubId(fromYardId);
            var toHub        = registry.GetHub(toYardId);
            if (toHub == null)
            {
                Main.Log($"[BlockTransportSpawner] No opposite hub found from {fromYardId}");
                return;
            }

            var store         = CarDestinationStore.Instance;
            var outboundTracks = TrackClassifier.GetOutboundTracks(fromHub);
            foreach (var track in outboundTracks)
            {
                // Only count interchange cars that have finished sorting (SortAtHub phase).
                // Ignores any non-GRDN cars and cars whose sort job is still running.
                var carsOnTrack = track.GetCarsFullyOnTrack()
                    ?.Where(c => store.IsInterchangeCar(c.carGuid) &&
                                 store.Get(c.carGuid)?.Phase == InterchangePhase.SortAtHub)
                    .ToList();
                if (carsOnTrack == null || carsOnTrack.Count < settings.BlockThresholdCars)
                    continue;

                // Resolve TrainCar objects
                var trainCars = ResolveTrainCars(carsOnTrack);
                if (trainCars.Count == 0) continue;

                var destTrack = JobUtils.BestInboundTrack(toHub);
                if (destTrack == null)
                {
                    Main.Log($"[BlockTransportSpawner] No inbound track at {toYardId} — block not spawned");
                    continue;
                }

                SpawnBlockJob(trainCars, track, destTrack, fromHub, fromYardId, toYardId);

                foreach (var c in trainCars)
                    store.SetPhase(c.CarGUID, InterchangePhase.BlockHaul);

                Main.Log($"[BlockTransportSpawner] Block {fromYardId}→{toYardId}: {trainCars.Count} cars");
            }
        }

        private static void SpawnBlockJob(
            List<TrainCar> cars,
            Track fromTrack,
            Track toTrack,
            StationController fromHub,
            string fromYardId,
            string toYardId)
        {
            var go  = new GameObject($"GRDN-Block-{fromYardId}-{toYardId}");
            var def = go.AddComponent<StaticTransportJobDefinition>();

            def.carsToTransport         = JobUtils.ToLogicCars(cars);
            def.startingTrack           = fromTrack;
            def.destinationTrack        = toTrack;
            def.transportedCargoPerCar  = JobUtils.GetCargoes(cars);
            def.cargoAmountPerCar       = JobUtils.GetCargoAmounts(cars);
            def.forceCorrectCargoStateOnCars = false;

            def.PopulateBaseJobDefinition(
                fromHub.logicStation,
                JobUtils.EstimateTimeLimit(cars.Count, distanceFactor: 2.0f),
                JobUtils.EstimateWage(cars.Count, distanceFactor: 2.0f),
                JobUtils.Chain(fromYardId, toYardId),
                JobLicenses.Basic | JobLicenses.FreightHaul
            );
            def.ForceJobId(JobUtils.NextId(fromYardId, toYardId));

            JobUtils.ActivateJobChain(def, fromHub);
            Main.Log($"[BlockTransportSpawner] Spawned block job {def.job?.ID} ({fromYardId}→{toYardId}, {cars.Count} cars)");
        }

        private static List<TrainCar> ResolveTrainCars(IEnumerable<Car> logicCars)
        {
            var result = new List<TrainCar>();
            var registry = TrainCarRegistry.Instance;
            if (registry == null) return result;

            foreach (var lc in logicCars)
            {
                if (registry.logicCarToTrainCar.TryGetValue(lc, out var tc))
                    result.Add(tc);
            }
            return result;
        }
    }
}
