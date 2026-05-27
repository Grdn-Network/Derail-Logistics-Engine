using DV.Logic.Job;
using DV.ThingTypes;
using GRDNInterchange.Data;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GRDNInterchange.Jobs
{
    /// <summary>
    /// Spawns a single hub-to-hub block transport job once enough cross-hub cars have
    /// accumulated at the origin hub.
    ///
    /// Fixes vs the original version:
    ///   - One block per hub, not one per outbound track. Aggregates all SortAtHub cars
    ///     and picks the track with the most ready cars.
    ///   - In-transit guard: if a BlockHaul-phase car already exists from this hub,
    ///     no new block is spawned until the current one completes.
    /// </summary>
    public static class BlockTransportSpawner
    {
        public static void TrySpawnBlock(StationController fromHub, Settings settings)
        {
            var registry   = HubRegistry.Instance;
            var store      = CarDestinationStore.Instance;
            if (registry == null || store == null) return;

            var fromYardId = fromHub.stationInfo.YardID;
            var toYardId   = registry.GetOppositeHubId(fromYardId);
            var toHub      = registry.GetHub(toYardId);
            if (toHub == null)
            {
                Main.Log($"[BlockTransportSpawner] No opposite hub found from {fromYardId}");
                return;
            }

            // In-transit guard: don't spawn a second block while one is already running
            bool blockInTransit = store.GetAll().Values
                .Any(r => r.AssignedHubYardId == fromYardId && r.Phase == InterchangePhase.BlockHaul);
            if (blockInTransit)
            {
                Main.Log($"[BlockTransportSpawner] Block already in transit from {fromYardId} — skipping");
                return;
            }

            // Find the outbound track with the most SortAtHub-phase interchange cars
            Track bestTrack = null;
            List<Car> bestCars = null;

            foreach (var track in TrackClassifier.GetOutboundTracks(fromHub))
            {
                var carsOnTrack = track.GetCarsFullyOnTrack()
                    ?.Where(c => store.IsInterchangeCar(c.carGuid) &&
                                 store.Get(c.carGuid)?.Phase == InterchangePhase.SortAtHub)
                    .ToList();

                if (carsOnTrack != null && (bestCars == null || carsOnTrack.Count > bestCars.Count))
                {
                    bestTrack = track;
                    bestCars  = carsOnTrack;
                }
            }

            if (bestCars == null || bestCars.Count < settings.BlockThresholdCars)
            {
                Main.Log($"[BlockTransportSpawner] Outbound cars at {fromYardId}: " +
                         $"{bestCars?.Count ?? 0} < threshold {settings.BlockThresholdCars}");
                return;
            }

            var trainCars = ResolveTrainCars(bestCars);
            if (trainCars.Count == 0) return;

            var destTrack = JobUtils.BestInboundTrack(toHub);
            if (destTrack == null)
            {
                Main.Log($"[BlockTransportSpawner] No inbound track at {toYardId} — block not spawned");
                return;
            }

            SpawnBlockJob(trainCars, bestTrack, destTrack, fromHub, fromYardId, toYardId);

            foreach (var c in trainCars)
                store.SetPhase(c.CarGUID, InterchangePhase.BlockHaul);

            Main.Log($"[BlockTransportSpawner] Block {fromYardId}→{toYardId}: {trainCars.Count} cars");
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
            var result   = new List<TrainCar>();
            var registry = TrainCarRegistry.Instance;
            if (registry == null) return result;

            foreach (var lc in logicCars)
                if (registry.logicCarToTrainCar.TryGetValue(lc, out var tc))
                    result.Add(tc);

            return result;
        }
    }
}
