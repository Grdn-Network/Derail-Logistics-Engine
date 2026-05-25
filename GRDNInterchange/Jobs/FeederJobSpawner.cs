using DV.Logic.Job;
using DV.ThingTypes;
using GRDNInterchange.Data;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GRDNInterchange.Jobs
{
    /// <summary>
    /// Spawns a small Transport job from an origin station to the assigned hub.
    /// Called after a vanilla job is intercepted and destroyed by NewJobChainInterceptPatch.
    /// </summary>
    public static class FeederJobSpawner
    {
        /// <summary>
        /// Replace a vanilla origin→destination transport job with a feeder origin→hub job.
        /// Cars and their true destinations are registered in CarDestinationStore.
        /// </summary>
        public static void SpawnFeeder(
            List<TrainCar> cars,
            Track startingTrack,
            StationController originStation,
            StationController hubStation)
        {
            var originYardId = originStation.stationInfo.YardID;
            var hubYardId    = hubStation.stationInfo.YardID;

            // We don't have the true destination here — it was extracted by the intercept patch
            // and already stored in CarDestinationStore before this call.

            // Find an inbound track at the hub with space
            var destTrack = JobUtils.BestInboundTrack(hubStation);
            if (destTrack == null)
            {
                Main.Log($"[FeederJobSpawner] No inbound track available at {hubYardId} — feeder not spawned");
                return;
            }

            var go  = new GameObject($"GI-FEEDER-{originYardId}-{hubYardId}");
            var def = go.AddComponent<StaticTransportJobDefinition>();

            def.carsToTransport         = JobUtils.ToLogicCars(cars);
            def.startingTrack           = startingTrack;
            def.destinationTrack        = destTrack;
            def.transportedCargoPerCar  = JobUtils.GetCargoes(cars);
            def.cargoAmountPerCar       = JobUtils.GetCargoAmounts(cars);
            def.forceCorrectCargoStateOnCars = false;

            def.PopulateBaseJobDefinition(
                originStation.logicStation,
                JobUtils.EstimateTimeLimit(cars.Count),
                JobUtils.EstimateWage(cars.Count),
                JobUtils.Chain(originYardId, hubYardId),
                JobLicenses.Basic | JobLicenses.FreightHaul
            );
            def.ForceJobId(JobUtils.NextId("FEED"));

            JobUtils.ActivateJobChain(def, originStation);

            Main.Log($"[FeederJobSpawner] Spawned {def.job?.ID}: {originYardId}→{hubYardId} ({cars.Count} cars)");
        }

        /// <summary>
        /// Spawn feeder jobs for all loaded cars on outbound tracks at an origin station
        /// that don't already have an interchange job. Called during world-load scan.
        /// </summary>
        public static void ScanAndSpawnMissingFeeders(StationController originStation)
        {
            if (HubRegistry.Instance == null || !HubRegistry.Instance.IsReady) return;

            var originYardId = originStation.stationInfo.YardID;
            if (HubRegistry.Instance.IsHub(originYardId)) return;

            var hubYardId   = HubRegistry.Instance.GetAssignedHubId(originYardId);
            var hubStation  = HubRegistry.Instance.GetHub(hubYardId);
            if (hubStation == null) return;

            var store = CarDestinationStore.Instance;
            var registry = TrainCarRegistry.Instance;
            if (registry == null) return;

            // Find cars at this station not already in the interchange system
            var unregistered = registry.logicCarToTrainCar.Values
                .Where(tc => !tc.IsLoco
                          && tc.logicCar?.CurrentTrack != null
                          && TrackClassifier.GetYardId(tc.logicCar.CurrentTrack) == originYardId
                          && !store.IsInterchangeCar(tc.CarGUID)
                          && tc.logicCar.CurrentCargoTypeInCar != CargoType.None)
                .ToList();

            if (unregistered.Count == 0) return;

            // Group by track and spawn one feeder per track group
            var byTrack = unregistered.GroupBy(tc => tc.logicCar.CurrentTrack);
            foreach (var group in byTrack)
            {
                var cars = group.ToList();
                // True destination unknown on scan — use a placeholder (same as origin for now)
                // Real feeder interception happens via the patch on newly-generated vanilla jobs
                foreach (var car in cars)
                    if (!store.IsInterchangeCar(car.CarGUID))
                        store.Register(car.CarGUID, hubYardId, originYardId, hubYardId);

                SpawnFeeder(cars, group.Key, originStation, hubStation);
            }
        }
    }
}
