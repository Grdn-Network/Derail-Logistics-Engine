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
        /// <summary>Count live hauls once per sweep: total plus per-origin, one pass.</summary>
        private static int CountActiveHauls(Dictionary<string, int> perOrigin)
        {
            int total = 0;
            foreach (var kv in Jobs.StaticDirectHaulJobDefinition.jobDefinitions)
            {
                total++;
                var origin = kv.Value.chainData?.chainOriginYardId;
                if (origin == null) continue;
                perOrigin.TryGetValue(origin, out var n);
                perOrigin[origin] = n + 1;
            }
            return total;
        }

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
            int perStation = Math.Max(1, Main.Settings?.MaxHaulsPerStation ?? 4);
            int total = Math.Max(1, Main.Settings?.MaxHaulsTotal ?? 40);

            var perOrigin = new Dictionary<string, int>(StringComparer.Ordinal);
            if (CountActiveHauls(perOrigin) >= total) return false;

            // Backhaul preference: yards that just received a delivery come up first as
            // origins, so the crew standing there finds a return haul waiting.
            var producers = econ.Facilities.Values
                .OrderByDescending(p => econ.DeliveryRecency(p.YardId));

            foreach (var producer in producers)
            {
                if (!producer.CanLoad) continue; // unload-only station is never an origin
                if (perOrigin.TryGetValue(producer.YardId, out var active) && active >= perStation) continue;
                foreach (var cargo in producer.Outputs)
                {
                    float stock = econ.GetAvailable(producer.YardId, cargo);
                    if (stock < min) continue;

                    var consumer = FindConsumer(econ, cargo, producer.YardId);
                    if (consumer == null) continue;

                    // Size to what the destination can actually accept, not just to stock:
                    // oversizing meant the delivery gate destroyed the excess unpaid on a
                    // haul the director itself ordered.
                    double room = econ.GetRoom(consumer.YardId, cargo) + 0.001;
                    int carCount = (int)Math.Min(max, Math.Min(Math.Floor(stock), Math.Floor(room)));
                    if (carCount < min) continue;

                    var producerSc = StationController.GetStationByYardID(producer.YardId);
                    var consumerSc = StationController.GetStationByYardID(consumer.YardId);
                    if (producerSc == null || consumerSc == null) continue;

                    // Every haul is carless: crews bring cars to the loading track, the
                    // booklet fills in when they attach, and stock debits at that moment.
                    if (DirectHaulGenerator.TryCreateCarless(producerSc, consumerSc, cargo, carCount) == null)
                        continue;
                    Main.Log($"[Director] haul {producer.YardId}->{consumer.YardId}: {carCount} {cargo} " +
                             "(bring cars; stock debits when they attach).");
                    return true;
                }
            }

            Main.Log("[Director] nothing to haul (no producer with enough stock plus a consumer).");
            return false;
        }

        /// <summary>
        /// Everything a dispatcher could order right now: per producer output with stock,
        /// every consumer that could take it. Read-only; the API serves this.
        /// </summary>
        public static List<HaulOption> GetOptions()
        {
            var econ = EconomyState.Instance;
            var options = new List<HaulOption>();
            foreach (var producer in econ.Facilities.Values)
            {
                if (!producer.CanLoad) continue;
                foreach (var cargo in producer.Outputs)
                {
                    float stock = econ.GetAvailable(producer.YardId, cargo);
                    if (stock < 1f) continue;
                    var consumers = econ.Facilities.Values
                        .Where(f => f.YardId != producer.YardId && f.CanUnload && f.Consumes(cargo)
                                    && econ.GetRoom(f.YardId, cargo) >= 1f)
                        .Select(f => f.YardId)
                        .ToList();
                    if (consumers.Count == 0) continue;
                    options.Add(new HaulOption
                    {
                        Origin = producer.YardId,
                        Cargo = cargo,
                        Stock = stock,
                        Consumers = consumers,
                    });
                }
            }
            return options;
        }

        public class HaulOption
        {
            public string Origin;
            public CargoType Cargo;
            public float Stock;
            public List<string> Consumers;
        }

        /// <summary>
        /// Dispatcher-picked haul. Non-finite mode spawns a pre-loaded consist and debits
        /// stock now; finite mode creates a carless job (players bring empties; stock is
        /// debited when the cars attach at the warehouse). Returns the job id or null.
        /// </summary>
        public static string CreateSpecific(string originYard, string destYard, CargoType cargo, int carCount,
            List<string> reservedCarIds = null)
        {
            if (!Main.IsHostOrSingleplayer()) return null;

            var econ = EconomyState.Instance;
            float stock = econ.GetAvailable(originYard, cargo);
            if (stock < carCount)
            {
                Main.Log($"[Director] {originYard} has {stock:0.#} {cargo}, cannot ship {carCount}.");
                return null;
            }

            var producerSc = StationController.GetStationByYardID(originYard);
            var consumerSc = StationController.GetStationByYardID(destYard);
            if (producerSc == null || consumerSc == null)
            {
                Main.Log($"[Director] unknown yard ({originYard} or {destYard}).");
                return null;
            }

            // Every haul is carless; dispatch can reserve specific cars for it (API).
            return DirectHaulGenerator.TryCreateCarless(producerSc, consumerSc, cargo, carCount, reservedCarIds);
        }

        private static readonly Random _rng = new Random();

        private static FacilityDef FindConsumer(EconomyState econ, CargoType cargo, string excludeYard)
        {
            // The auto-director is only a baseline: it picks randomly among eligible
            // consumers. Deliberate routing belongs to the dispatcher (POST /api/v1/hauls),
            // and later to contracts.
            var eligible = new List<FacilityDef>();
            foreach (var f in econ.Facilities.Values)
            {
                if (f.YardId == excludeYard) continue;
                if (!f.CanUnload) continue; // load-only station is never a destination
                if (!f.Consumes(cargo)) continue;
                if (econ.GetRoom(f.YardId, cargo) < 1f) continue; // consumer storage is full

                var sc = StationController.GetStationByYardID(f.YardId);
                if (sc?.logicStation?.yard == null) continue;
                if (sc.logicStation.yard
                        .GetWarehouseMachinesThatSupportCargoTypes(new List<CargoType> { cargo }).Count == 0)
                    continue;

                eligible.Add(f);
            }
            return eligible.Count > 0 ? eligible[_rng.Next(eligible.Count)] : null;
        }
    }
}
