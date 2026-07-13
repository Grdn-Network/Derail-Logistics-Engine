using DV.Booklets;
using DV.Logic.Job;
using DV.ThingTypes;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DLE.Jobs
{
    /// <summary>
    /// Builds Company Hauls. Every haul is carless: it carries a load task and synthetic
    /// booklet cars; crews bring cars to the producer's loading track, the servicing patch
    /// attaches them (booklets printed after that show the real cars), and the producer's
    /// stock debits at that moment. Host or singleplayer only; the EconomyDirector gates
    /// creation on real stock. TryRebuild restores saved jobs, attached cars included.
    /// </summary>
    public static class DirectHaulGenerator
    {
        public static string TryCreateCarless(
            StationController producer,
            StationController consumer,
            CargoType cargo,
            int carCount,
            List<string> reservedCarIds = null)
        {
            if (producer == null || consumer == null || carCount <= 0) return null;

            var cargoList = new List<CargoType> { cargo };
            var loadMachine = producer.logicStation.yard
                .GetWarehouseMachinesThatSupportCargoTypes(cargoList).FirstOrDefault();
            var unloadMachine = consumer.logicStation.yard
                .GetWarehouseMachinesThatSupportCargoTypes(cargoList).FirstOrDefault();
            if (loadMachine == null || unloadMachine == null)
            {
                Main.Log($"[DirectHaul] carless: missing warehouse machine for {cargo}.");
                return null;
            }
            if (!DV.Globals.G.Types.CargoType_to_v2.TryGetValue(cargo, out var v2) ||
                !DV.Globals.G.Types.CargoToLoadableCarTypes.TryGetValue(v2, out var carTypes) ||
                carTypes.Count == 0)
            {
                Main.Log($"[DirectHaul] carless: no loadable car type for {cargo}.");
                return null;
            }

            // Synthetic display cars: the booklet shows what to bring before cars attach.
            var displayCars = new List<Car_data>();
            var liveryCounts = new Dictionary<TrainCarLivery, int>();
            for (int i = 0; i < carCount; i++)
            {
                var shownType = carTypes[i % carTypes.Count];
                var livery = shownType.liveries[i % shownType.liveries.Count];
                displayCars.Add(new Car_data("?", livery, false, false, 0f, 0f, 0f));
                liveryCounts.TryGetValue(livery, out var n);
                liveryCounts[livery] = n + 1;
            }

            float distance = JobPaymentCalculator.GetDistanceBetweenStations(producer, consumer);
            float bonusTime = JobPaymentCalculator.CalculateHaulBonusTimeLimit(distance);
            float wage = JobPaymentCalculator.CalculateJobPayment(
                JobType.Transport, distance,
                new PaymentCalculationData(liveryCounts, new Dictionary<CargoType, int> { { cargo, carCount } }));

            var jobId = JobUtils.NextId(producer.stationInfo.YardID, consumer.stationInfo.YardID);
            bool ok = BuildChain(producer, consumer, new List<TrainCar>(), cargo,
                loadMachine, unloadMachine, jobId, wage, bonusTime,
                spawnTrackDisplay: $"warehouse {loadMachine.WarehouseTrack?.ID?.FullDisplayID ?? producer.stationInfo.YardID}",
                includeLoadTask: true, displayCarsOverride: displayCars, plannedCarCount: carCount);
            if (!ok) return null;

            if (reservedCarIds != null && reservedCarIds.Count > 0 &&
                StaticDirectHaulJobDefinition.jobDefinitions.TryGetValue(jobId, out var createdDef))
            {
                createdDef.reservedCarIds = new List<string>(reservedCarIds);
                Main.Log($"[DirectHaul] {jobId}: dispatcher reserved cars {string.Join(", ", reservedCarIds)}.");
            }

            Economy.EconomyHistory.Record("haul_created", producer.stationInfo.YardID, cargo.ToString(), carCount, jobId);
            Main.Log($"[DirectHaul] carless {jobId}: bring {carCount} empt{(carCount == 1 ? "y" : "ies")} " +
                     $"for {cargo} to {producer.stationInfo.YardID}.");
            return jobId;
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
            string spawnTrackDisplay,
            bool includeLoadTask = false,
            int plannedCars = 0)
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

            List<Car_data> displayOverride = null;
            if (includeLoadTask && cars.Count == 0 && plannedCars > 0 &&
                DV.Globals.G.Types.CargoType_to_v2.TryGetValue(cargo, out var v2) &&
                DV.Globals.G.Types.CargoToLoadableCarTypes.TryGetValue(v2, out var carTypes) &&
                carTypes.Count > 0)
            {
                displayOverride = new List<Car_data>();
                for (int i = 0; i < plannedCars; i++)
                {
                    var shownType = carTypes[i % carTypes.Count];
                    displayOverride.Add(new Car_data("?",
                        shownType.liveries[i % shownType.liveries.Count], false, false, 0f, 0f, 0f));
                }
            }

            bool ok = BuildChain(producer, consumer, cars, cargo, loadMachine, unloadMachine,
                jobId, wage, bonusTime, spawnTrackDisplay, includeLoadTask, displayOverride, plannedCars);
            if (ok) Main.Log($"[DirectHaul] rebuilt {jobId} over {cars.Count} existing car(s)" +
                             (includeLoadTask && cars.Count == 0 ? " (carless, awaiting empties)" : "") + ".");
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
            string spawnTrackDisplay,
            bool includeLoadTask = false,
            List<Car_data> displayCarsOverride = null,
            int plannedCarCount = 0)
        {
            var logicCars = TrainCar.ExtractLogicCars(cars);
            int licenseCarCount = logicCars.Count > 0 ? logicCars.Count : plannedCarCount;
            var chainData = new StationsChainData(
                producer.stationInfo.YardID, consumer.stationInfo.YardID);
            var requiredLicenses =
                JobLicenseType_v2.ListToFlags(LicenseManager.Instance.GetRequiredLicensesForCargoTypes(new List<CargoType> { cargo }))
                | (LicenseManager.Instance.GetRequiredLicenseForNumberOfTransportedCars(licenseCarCount)?.v1 ?? JobLicenses.Basic);

            var go = new GameObject(
                $"ChainJob[Company Haul]: {chainData.chainOriginYardId} - {chainData.chainDestinationYardId}");
            go.transform.SetParent(producer.transform);

            var def = go.AddComponent<StaticDirectHaulJobDefinition>();
            def.carsToTransport   = logicCars;
            def.cargoAmountPerCar = logicCars.Select(c => c.capacity).ToList();
            def.loadMachine       = loadMachine;
            def.unloadMachine     = unloadMachine;
            def.transportedCargo  = cargo;
            def.includeLoadTask   = includeLoadTask;
            def.plannedCarCount   = plannedCarCount > 0 ? plannedCarCount : logicCars.Count;
            def.displayCars       = displayCarsOverride ?? logicCars.Select(c => new Car_data(c, false)).ToList();
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
                Main.LogAlways($"[DirectHaul] generation failed: {ex.GetType().Name}: {ex.Message}");
                try { jcc.DestroyChain(); } catch { }
                Object.Destroy(go);
                return false;
            }

            producer.ProceduralJobsController.AddJobChainController(jcc);
            return true;
        }
    }
}
