using DV.Booklets;
using DV.Logic.Job;
using DV.ThingTypes;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DLE.Jobs
{
    /// <summary>
    /// Builds a pre-loaded Direct Haul (0.1, non-finite mode): spawn a consist of the cargo
    /// at the producer, load it, and create a haul + unload job to the consumer. Host or
    /// singleplayer only; the EconomyDirector gates this on real stock. Returns the number
    /// of cars spawned (0 on failure) so the caller can debit the producer stockpile.
    /// </summary>
    public static class DirectHaulGenerator
    {
        public static int TryCreatePreloaded(
            StationController producer,
            StationController consumer,
            CargoType cargo,
            int carCount)
        {
            if (producer == null || consumer == null || carCount <= 0)
                return 0;

            var cargoList = new List<CargoType> { cargo };
            var loadMachine = producer.logicStation.yard
                .GetWarehouseMachinesThatSupportCargoTypes(cargoList).FirstOrDefault();
            var unloadMachine = consumer.logicStation.yard
                .GetWarehouseMachinesThatSupportCargoTypes(cargoList).FirstOrDefault();
            if (loadMachine == null || unloadMachine == null)
            {
                Main.Log($"[DirectHaul] missing warehouse machine (load={loadMachine != null}, unload={unloadMachine != null}) for {cargo}.");
                return 0;
            }

            // Pick a car livery that carries this cargo, one type for the whole consist.
            if (!DV.Globals.G.Types.CargoType_to_v2.TryGetValue(cargo, out var v2) ||
                !DV.Globals.G.Types.CargoToLoadableCarTypes.TryGetValue(v2, out var carTypes) ||
                carTypes.Count == 0)
            {
                Main.Log($"[DirectHaul] no loadable car type for {cargo}.");
                return 0;
            }
            var livery = carTypes[0].liveries[0];
            var liveries = Enumerable.Repeat(livery, carCount).ToList();

            // Find a free producer outbound track long enough for the consist.
            float length = CarSpawner.Instance.GetTotalCarLiveriesLength(liveries, true);
            var freeTracks = producer.logicStation.yard.TransferOutTracks.Where(t => t.IsFree()).ToList();
            var track = YardTracksOrganizer.Instance
                .FilterOutTracksWithoutRequiredFreeSpace(freeTracks, length)
                .FirstOrDefault();
            if (track == null)
            {
                Main.Log($"[DirectHaul] {producer.stationInfo.YardID} has no free outbound track for {carCount} cars.");
                return 0;
            }

            var railTrack = RailTrackRegistry.LogicToRailTrack[track];
            var spawned = CarSpawner.Instance
                .SpawnCarTypesOnTrackRandomOrientation(liveries, railTrack, true, applyHandbrakeOnLastCars: true);
            if (spawned == null || spawned.Count == 0)
            {
                Main.Log($"[DirectHaul] failed to spawn cars at {producer.stationInfo.YardID}.");
                return 0;
            }

            // Load each spawned car with the cargo so it arrives ready to unload.
            foreach (var tc in spawned)
                tc.logicCar.LoadCargo(tc.logicCar.capacity, cargo, loadMachine);

            // Pay like a vanilla haul of the same distance and consist (Transport rates;
            // ComplexTransport has no vanilla payment config of its own).
            float distance = JobPaymentCalculator.GetDistanceBetweenStations(producer, consumer);
            float bonusTime = JobPaymentCalculator.CalculateHaulBonusTimeLimit(distance);
            var liveryCounts = new Dictionary<TrainCarLivery, int>();
            foreach (var tc in spawned)
            {
                liveryCounts.TryGetValue(tc.carLivery, out var n);
                liveryCounts[tc.carLivery] = n + 1;
            }
            var cargoCounts = new Dictionary<CargoType, int> { { cargo, spawned.Count } };
            float wage = JobPaymentCalculator.CalculateJobPayment(
                JobType.Transport, distance, new PaymentCalculationData(liveryCounts, cargoCounts));

            var jobId = JobUtils.NextId(producer.stationInfo.YardID, consumer.stationInfo.YardID);
            if (!BuildChain(producer, consumer, spawned, cargo, loadMachine, unloadMachine,
                    jobId, wage, bonusTime, track.ID?.FullDisplayID ?? ""))
            {
                Main.Log("[DirectHaul] chain build failed; deleting spawned cars.");
                CarSpawner.Instance.DeleteTrainCars(spawned, true);
                return 0;
            }

            Main.Log($"[DirectHaul] {producer.stationInfo.YardID}->{consumer.stationInfo.YardID}: " +
                     $"{spawned.Count} car(s) of {cargo} spawned and loaded.");
            return spawned.Count;
        }

        /// <summary>
        /// Rebuild a saved job over cars that already exist in the world (save restore).
        /// The cars keep whatever cargo the vanilla save gave them.
        /// </summary>
        public static bool TryRebuild(
            StationController producer,
            StationController consumer,
            List<TrainCar> cars,
            CargoType cargo,
            string jobId,
            float wage,
            float bonusTime,
            string spawnTrackDisplay)
        {
            var cargoList = new List<CargoType> { cargo };
            var loadMachine = producer.logicStation.yard
                .GetWarehouseMachinesThatSupportCargoTypes(cargoList).FirstOrDefault();
            var unloadMachine = consumer.logicStation.yard
                .GetWarehouseMachinesThatSupportCargoTypes(cargoList).FirstOrDefault();
            if (unloadMachine == null)
            {
                Main.Log($"[DirectHaul] rebuild {jobId}: no unload machine for {cargo}; skipped.");
                return false;
            }

            bool ok = BuildChain(producer, consumer, cars, cargo, loadMachine, unloadMachine,
                jobId, wage, bonusTime, spawnTrackDisplay);
            if (ok) Main.Log($"[DirectHaul] rebuilt {jobId} over {cars.Count} existing car(s).");
            return ok;
        }

        private static bool BuildChain(
            StationController producer,
            StationController consumer,
            List<TrainCar> cars,
            CargoType cargo,
            WarehouseMachine loadMachine,
            WarehouseMachine unloadMachine,
            string jobId,
            float wage,
            float bonusTime,
            string spawnTrackDisplay)
        {
            var logicCars = TrainCar.ExtractLogicCars(cars);
            var chainData = new StationsChainData(
                producer.stationInfo.YardID, consumer.stationInfo.YardID);
            var requiredLicenses =
                JobLicenseType_v2.ListToFlags(LicenseManager.Instance.GetRequiredLicensesForCargoTypes(new List<CargoType> { cargo }))
                | (LicenseManager.Instance.GetRequiredLicenseForNumberOfTransportedCars(cars.Count)?.v1 ?? JobLicenses.Basic);

            var go = new GameObject(
                $"ChainJob[Direct Haul]: {chainData.chainOriginYardId} - {chainData.chainDestinationYardId}");
            go.transform.SetParent(producer.transform);

            var def = go.AddComponent<StaticDirectHaulJobDefinition>();
            def.carsToTransport   = logicCars;
            def.cargoAmountPerCar = logicCars.Select(c => c.capacity).ToList();
            def.loadMachine       = loadMachine;
            def.unloadMachine     = unloadMachine;
            def.transportedCargo  = cargo;
            def.includeLoadTask   = false; // 0.1: cars arrive pre-loaded
            def.displayCars       = logicCars.Select(c => new Car_data(c, false)).ToList();
            def.spawnTrackDisplay = spawnTrackDisplay;
            def.ForceJobId(jobId);
            def.PopulateBaseJobDefinition(producer.logicStation, bonusTime, wage, chainData, requiredLicenses);

            var jcc = new JobChainController(go);
            jcc.carsForJobChain = logicCars;
            jcc.AddJobDefinitionToChain(def);
            try
            {
                jcc.FinalizeSetupAndGenerateFirstJob(false);
            }
            catch (System.Exception ex)
            {
                Main.Log($"[DirectHaul] generation failed: {ex.GetType().Name}: {ex.Message}");
                try { jcc.DestroyChain(); } catch { }
                Object.Destroy(go);
                return false;
            }

            producer.ProceduralJobsController.AddJobChainController(jcc);
            return true;
        }
    }
}
