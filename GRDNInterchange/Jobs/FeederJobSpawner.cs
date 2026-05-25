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
        /// <summary>
        /// Spawn a feeder job from <paramref name="originStation"/> to
        /// <paramref name="hubStation"/>. The <paramref name="jobId"/> is the
        /// true-origin→true-destination ID (e.g. "SM-GF-77") that the player sees
        /// and that will be reused on the final-mile leg.
        /// </summary>
        public static void SpawnFeeder(
            List<TrainCar> cars,
            Track startingTrack,
            StationController originStation,
            StationController hubStation,
            string jobId)
        {
            var originYardId = originStation.stationInfo.YardID;
            var hubYardId    = hubStation.stationInfo.YardID;

            // Find an inbound track at the hub with space
            var destTrack = JobUtils.BestInboundTrack(hubStation);
            if (destTrack == null)
            {
                Main.Log($"[FeederJobSpawner] No inbound track available at {hubYardId} — feeder not spawned");
                return;
            }

            var go  = new GameObject($"GRDN-Feeder-{originYardId}-{hubYardId}");
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
                // chainData uses the actual next-stop (hub), not the true final destination.
                // The job ID carries the true journey identity.
                JobUtils.Chain(originYardId, hubYardId),
                JobLicenses.Basic | JobLicenses.FreightHaul
            );
            def.ForceJobId(jobId);

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
            if (GRDNInterchange.Main.Settings.ExcludedYardIds.Contains(originYardId)) return;

            var hubYardId   = HubRegistry.Instance.GetAssignedHubId(originYardId);
            var hubStation  = HubRegistry.Instance.GetHub(hubYardId);
            if (hubStation == null) return;

            var store = CarDestinationStore.Instance;
            var registry = TrainCarRegistry.Instance;
            if (registry == null) return;

            // Build a set of logic cars that already belong to an active job chain at this station.
            // jobChainControllers is private so we access it via reflection (same pattern as
            // responsibleStationForJobChain in JobUtils.ActivateJobChain).
            var carsInJobs = new HashSet<Car>();
            var jccField = typeof(StationProceduralJobsController).GetField(
                "jobChainControllers",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var activeChains = jccField?.GetValue(originStation.ProceduralJobsController)
                               as List<JobChainController>;
            if (activeChains != null)
                foreach (var jcc in activeChains)
                    if (jcc?.carsForJobChain != null)
                        foreach (var c in jcc.carsForJobChain)
                            if (c != null) carsInJobs.Add(c);

            // Find cars at this station not already in the interchange system and not in any active job
            var unregistered = registry.logicCarToTrainCar.Values
                .Where(tc => !tc.IsLoco
                          && tc.logicCar?.CurrentTrack != null
                          && TrackClassifier.GetYardId(tc.logicCar.CurrentTrack) == originYardId
                          && !store.IsInterchangeCar(tc.CarGUID)
                          && tc.logicCar.CurrentCargoTypeInCar != CargoType.None
                          && !carsInJobs.Contains(tc.logicCar))  // skip cars already in a job
                .ToList();

            if (unregistered.Count == 0) return;

            // Group by track, then batch within each group by MaxCarsPerFeeder
            int max    = Mathf.Max(1, GRDNInterchange.Main.Settings.MaxCarsPerFeeder);
            var byTrack = unregistered.GroupBy(tc => tc.logicCar.CurrentTrack);
            foreach (var group in byTrack)
            {
                var trackCars = group.ToList();
                var track     = group.Key;

                for (int i = 0; i < trackCars.Count; i += max)
                {
                    int take  = System.Math.Min(max, trackCars.Count - i);
                    var batch = trackCars.GetRange(i, take);

                    // True destination unknown at scan time — placeholder is the hub itself.
                    // Real feeder interception handles newly-generated vanilla jobs via the patch.
                    var jobId = JobUtils.NextId(originYardId, hubYardId);

                    foreach (var car in batch)
                        if (!store.IsInterchangeCar(car.CarGUID))
                            store.Register(car.CarGUID, hubYardId, originYardId, hubYardId, jobId);

                    SpawnFeeder(batch, track, originStation, hubStation, jobId);
                }
            }
        }
    }
}
