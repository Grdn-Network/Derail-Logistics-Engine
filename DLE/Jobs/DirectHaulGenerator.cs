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
            var logicCars = new List<Car>();
            var amounts = new List<float>();
            foreach (var tc in spawned)
            {
                var c = tc.logicCar;
                c.LoadCargo(c.capacity, cargo, loadMachine);
                logicCars.Add(c);
                amounts.Add(c.capacity);
            }

            var chainData = new StationsChainData(
                producer.stationInfo.YardID, consumer.stationInfo.YardID);
            var requiredLicenses =
                JobLicenseType_v2.ListToFlags(LicenseManager.Instance.GetRequiredLicensesForCargoTypes(cargoList))
                | (LicenseManager.Instance.GetRequiredLicenseForNumberOfTransportedCars(spawned.Count)?.v1 ?? JobLicenses.Basic);
            float wage = JobUtils.EstimateWage(spawned.Count);

            var go = new GameObject(
                $"ChainJob[Direct Haul]: {chainData.chainOriginYardId} - {chainData.chainDestinationYardId}");
            go.transform.SetParent(producer.transform);

            var def = go.AddComponent<StaticDirectHaulJobDefinition>();
            def.carsToTransport   = logicCars;
            def.cargoAmountPerCar = amounts;
            def.loadMachine       = loadMachine;
            def.unloadMachine     = unloadMachine;
            def.transportedCargo  = cargo;
            def.includeLoadTask   = false; // 0.1: cars arrive pre-loaded
            def.displayCars       = logicCars.Select(c => new Car_data(c, false)).ToList();
            def.ForceJobId(JobUtils.NextId(chainData.chainOriginYardId, chainData.chainDestinationYardId));
            def.PopulateBaseJobDefinition(producer.logicStation, 0f, wage, chainData, requiredLicenses);

            var jcc = new JobChainController(go);
            jcc.carsForJobChain = logicCars;
            jcc.AddJobDefinitionToChain(def);
            try
            {
                jcc.FinalizeSetupAndGenerateFirstJob(false);
            }
            catch (System.Exception ex)
            {
                Main.Log($"[DirectHaul] generation failed: {ex.GetType().Name}: {ex.Message}; deleting spawned cars.");
                try { jcc.DestroyChain(); } catch { }
                CarSpawner.Instance.DeleteTrainCars(spawned, true);
                Object.Destroy(go);
                return 0;
            }

            producer.ProceduralJobsController.AddJobChainController(jcc);
            Main.Log($"[DirectHaul] {chainData.chainOriginYardId}->{chainData.chainDestinationYardId}: " +
                     $"{spawned.Count} car(s) of {cargo} spawned and loaded.");
            return spawned.Count;
        }
    }
}
