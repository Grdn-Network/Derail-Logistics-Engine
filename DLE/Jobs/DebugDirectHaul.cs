using DLE.Data;
using DV.Logic.Job;
using DV.ThingTypes;
using System.Collections.Generic;
using System.Linq;

namespace DLE.Jobs
{
    /// <summary>
    /// Phase 1 test harness. Turns the consist the player is currently standing on/in into
    /// a Direct Haul job: origin is the yard the cars sit in, cargo is what the cars carry
    /// (or, if empty, a cargo that yard can load), and destination is the first other yard
    /// with a warehouse machine that unloads that cargo. Not shipped logic; the economy
    /// drives generation from Phase 3.
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

            var cars = playerCar.trainset.cars.Where(c => c != null && c.logicCar != null).ToList();
            if (cars.Count == 0)
            {
                Main.Log("[DirectHaul debug] no logic cars in the current consist.");
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

            // Cargo = what the cars carry, else a cargo this yard can load.
            var cargo = cars[0].logicCar.CurrentCargoTypeInCar;
            if (cargo == CargoType.None)
            {
                cargo = FirstLoadableCargo(origin);
                if (cargo == CargoType.None)
                {
                    Main.Log($"[DirectHaul debug] cars are empty and {origin.stationInfo.YardID} loads nothing; load the cars first.");
                    return;
                }
                Main.Log($"[DirectHaul debug] cars empty; using {cargo} which {origin.stationInfo.YardID} can load.");
            }

            var destination = FirstDestinationFor(cargo, origin);
            if (destination == null)
            {
                Main.Log($"[DirectHaul debug] no other yard can unload {cargo}.");
                return;
            }

            var chain = DirectHaulGenerator.TryCreate(origin, destination, cars, cargo);
            if (chain == null)
                Main.Log("[DirectHaul debug] generation returned null; see log above.");
        }

        private static CargoType FirstLoadableCargo(StationController station)
        {
            var groups = station.proceduralJobsRuleset?.outputCargoGroups;
            if (groups == null) return CargoType.None;
            foreach (var group in groups)
                foreach (var c in group.cargoTypes)
                    if (station.logicStation.yard
                            .GetWarehouseMachinesThatSupportCargoTypes(new List<CargoType> { c }).Count > 0)
                        return c;
            return CargoType.None;
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
