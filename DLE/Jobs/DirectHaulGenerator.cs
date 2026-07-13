using DV.Logic.Job;
using DV.ThingTypes;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DLE.Jobs
{
    /// <summary>
    /// Builds a single Direct Haul job (load at origin, haul, unload at destination) for a
    /// given set of cars and cargo. Host/singleplayer only; the caller must gate on
    /// Main.IsHostOrSingleplayer(). The economy drives this in Phase 3; Phase 1 calls it
    /// from a debug button.
    /// </summary>
    public static class DirectHaulGenerator
    {
        public static JobChainController TryCreate(
            StationController origin,
            StationController destination,
            List<TrainCar> cars,
            CargoType cargo)
        {
            if (origin == null || destination == null || cars == null || cars.Count == 0)
                return null;

            var cargoList = new List<CargoType> { cargo };

            var loadMachine = origin.logicStation.yard
                .GetWarehouseMachinesThatSupportCargoTypes(cargoList).FirstOrDefault();
            var unloadMachine = destination.logicStation.yard
                .GetWarehouseMachinesThatSupportCargoTypes(cargoList).FirstOrDefault();
            if (loadMachine == null)
            {
                Main.Log($"[DirectHaul] {origin.stationInfo.YardID} has no warehouse machine for {cargo}");
                return null;
            }
            if (unloadMachine == null)
            {
                Main.Log($"[DirectHaul] {destination.stationInfo.YardID} has no warehouse machine for {cargo}");
                return null;
            }

            var logicCars = TrainCar.ExtractLogicCars(cars);
            var cargoAmounts = cars
                .Select(c => c.logicCar.LoadedCargoAmount > 0f ? c.logicCar.LoadedCargoAmount : 1f)
                .ToList();

            var chainData = new StationsChainData(
                origin.stationInfo.YardID, destination.stationInfo.YardID);

            var requiredLicenses =
                JobLicenseType_v2.ListToFlags(LicenseManager.Instance.GetRequiredLicensesForCargoTypes(cargoList))
                | (LicenseManager.Instance.GetRequiredLicenseForNumberOfTransportedCars(cars.Count)?.v1 ?? JobLicenses.Basic);

            float wage = JobUtils.EstimateWage(cars.Count);
            float timeLimit = 0f; // unlimited for the primitive; the economy tunes this later

            var go = new GameObject(
                $"ChainJob[Direct Haul]: {chainData.chainOriginYardId} - {chainData.chainDestinationYardId}");
            go.transform.SetParent(origin.transform);

            var def = go.AddComponent<StaticDirectHaulJobDefinition>();
            def.carsToTransport   = logicCars;
            def.cargoAmountPerCar = cargoAmounts;
            def.loadMachine       = loadMachine;
            def.unloadMachine     = unloadMachine;
            def.transportedCargo  = cargo;
            def.ForceJobId(JobUtils.NextId(chainData.chainOriginYardId, chainData.chainDestinationYardId));
            def.PopulateBaseJobDefinition(origin.logicStation, timeLimit, wage, chainData, requiredLicenses);

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
                return null;
            }

            origin.ProceduralJobsController.AddJobChainController(jcc);
            Main.Log($"[DirectHaul] created {chainData.chainOriginYardId}->{chainData.chainDestinationYardId} " +
                     $"({cars.Count} car(s) of {cargo})");
            return jcc;
        }
    }
}
