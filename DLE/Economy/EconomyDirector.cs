using DLE.Jobs;
using DV.ThingTypes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DLE.Economy
{
    /// <summary>
    /// Turns stock into hauls: finds a producer holding enough of an output cargo and a
    /// consumer that needs it, spawns a pre-loaded Direct Haul, and debits the producer.
    /// One haul per call (0.1 debug button; a tick can call it repeatedly later).
    /// Host or singleplayer only.
    /// </summary>
    public static class EconomyDirector
    {
        public static bool GenerateOne()
        {
            if (!Main.IsHostOrSingleplayer())
            {
                Main.Log("[Director] host or singleplayer only.");
                return false;
            }

            var econ = EconomyState.Instance;
            int min = Math.Max(1, Main.Settings?.MinShipCarloads ?? 3);
            int max = Math.Max(min, Main.Settings?.MaxCarsPerHaul ?? 6);

            foreach (var producer in econ.Facilities.Values)
            {
                foreach (var cargo in producer.Outputs)
                {
                    float stock = econ.GetStock(producer.YardId, cargo);
                    if (stock < min) continue;

                    var consumer = FindConsumer(econ, cargo, producer.YardId);
                    if (consumer == null) continue;

                    int carCount = (int)Math.Min(max, Math.Floor(stock));
                    if (carCount < min) continue;

                    var producerSc = StationController.GetStationByYardID(producer.YardId);
                    var consumerSc = StationController.GetStationByYardID(consumer.YardId);
                    if (producerSc == null || consumerSc == null) continue;

                    int spawned = DirectHaulGenerator.TryCreatePreloaded(producerSc, consumerSc, cargo, carCount);
                    if (spawned <= 0) continue;

                    econ.Debit(producer.YardId, cargo, spawned);
                    Main.Log($"[Director] haul {producer.YardId}->{consumer.YardId}: {spawned} {cargo} " +
                             $"(producer now {econ.GetStock(producer.YardId, cargo):0.#}).");
                    return true;
                }
            }

            Main.Log("[Director] nothing to haul (no producer with enough stock plus a consumer).");
            return false;
        }

        private static FacilityDef FindConsumer(EconomyState econ, CargoType cargo, string excludeYard)
        {
            foreach (var f in econ.Facilities.Values)
            {
                if (f.YardId == excludeYard) continue;
                if (!f.Consumes(cargo)) continue;

                var sc = StationController.GetStationByYardID(f.YardId);
                if (sc?.logicStation?.yard == null) continue;
                if (sc.logicStation.yard
                        .GetWarehouseMachinesThatSupportCargoTypes(new List<CargoType> { cargo }).Count == 0)
                    continue;

                return f;
            }
            return null;
        }
    }
}
