using DLE.Data;
using DV.Logic.Job;
using DV.ThingTypes;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;

namespace DLE.Jobs
{
    /// <summary>
    /// Phase 1 test harness. Turns the consist the player is standing on/in into a clean
    /// Direct Haul: it empties the cars and makes them job-eligible, then generates a
    /// load-at-origin, haul, unload-at-destination job, which is the same shape the economy
    /// will dispatch in Phase 3 (empty cars sent to a producer to load). Not shipped logic.
    /// </summary>
    public static class DebugDirectHaul
    {
        public static void CreateFromPlayerConsist()
        {
            if (!Main.IsHostOrSingleplayer())
            {
                Main.Log("[DirectHaul debug] clients cannot generate jobs; run on the host.");
                return;
            }

            var playerCar = PlayerManager.Car;
            if (playerCar == null || playerCar.trainset == null)
            {
                Main.Log("[DirectHaul debug] stand on or in a car of the consist you want hauled, then retry.");
                return;
            }

            // Freight cars only; a loco cannot carry cargo.
            var cars = playerCar.trainset.cars
                .Where(c => c != null && c.logicCar != null && !c.IsLoco)
                .ToList();
            if (cars.Count == 0)
            {
                Main.Log("[DirectHaul debug] no freight cars in the current consist (locomotives are skipped).");
                return;
            }

            // Origin = the yard the cars are standing in.
            var track = cars[0].logicCar.CurrentTrack;
            var originYardId = TrackClassifier.GetYardId(track);
            var origin = string.IsNullOrEmpty(originYardId)
                ? null : StationController.GetStationByYardID(originYardId);
            if (origin == null)
            {
                Main.Log($"[DirectHaul debug] cars are not in a known yard (track yard '{originYardId}').");
                return;
            }

            // Pick a cargo this yard can load that every car in the consist can carry.
            var cargo = FirstLoadableCompatibleCargo(origin, cars);
            if (cargo == CargoType.None)
            {
                Main.Log($"[DirectHaul debug] {origin.stationInfo.YardID} loads nothing these car types can carry.");
                return;
            }

            var destination = FirstDestinationFor(cargo, origin);
            if (destination == null)
            {
                Main.Log($"[DirectHaul debug] no other yard can unload {cargo}.");
                return;
            }

            // Make the cars usable in a job: DV refuses player-spawned cars, and a Direct
            // Haul loads empties at the origin, so clear any existing cargo.
            foreach (var car in cars)
            {
                MakeJobEligible(car);
                car.logicCar.DumpCargo();
            }

            var chain = DirectHaulGenerator.TryCreate(origin, destination, cars, cargo);
            if (chain == null)
                Main.Log("[DirectHaul debug] generation returned null; see log above.");
            else
                Main.Log($"[DirectHaul debug] created {origin.stationInfo.YardID}->{destination.stationInfo.YardID} for {cargo}. " +
                         "Load at origin, haul, unload at destination.");
        }

        /// <summary>
        /// Clear the player-spawned flag DV checks before allowing a car into a job. This is
        /// the lean version; the full PJ conversion (debt and damage trackers) is absorbed in
        /// a later phase when the economy spawns its own cars.
        /// </summary>
        private static void MakeJobEligible(TrainCar car)
        {
            if (!car.playerSpawnedCar) return;
            car.playerSpawnedCar = false;
            // Car.playerSpawnedCar is readonly; set it through Traverse like PJ does.
            Traverse.Create(car.logicCar).Field(nameof(Car.playerSpawnedCar)).SetValue(false);
        }

        private static CargoType FirstLoadableCompatibleCargo(StationController station, List<TrainCar> cars)
        {
            var groups = station.proceduralJobsRuleset?.outputCargoGroups;
            if (groups == null) return CargoType.None;
            foreach (var group in groups)
                foreach (var c in group.cargoTypes)
                    if (station.logicStation.yard
                            .GetWarehouseMachinesThatSupportCargoTypes(new List<CargoType> { c }).Count > 0
                        && CarsCanCarry(c, cars))
                        return c;
            return CargoType.None;
        }

        private static bool CarsCanCarry(CargoType cargo, List<TrainCar> cars)
        {
            if (!DV.Globals.G.Types.CargoType_to_v2.TryGetValue(cargo, out var v2)) return false;
            if (!DV.Globals.G.Types.CargoToLoadableCarTypes.TryGetValue(v2, out var loadable)) return false;
            return cars.All(c => loadable.Contains(c.carLivery.parentType));
        }

        private static StationController FirstDestinationFor(CargoType cargo, StationController origin)
        {
            var cargoList = new List<CargoType> { cargo };
            foreach (var sc in StationController.allStations)
            {
                if (sc == origin) continue;
                if (sc.logicStation?.yard == null) continue;
                if (sc.logicStation.yard.GetWarehouseMachinesThatSupportCargoTypes(cargoList).Count > 0)
                    return sc;
            }
            return null;
        }
    }
}
