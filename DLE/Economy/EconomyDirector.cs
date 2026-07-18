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
        // Auto-generation sizing. Dispatcher-picked hauls (CreateSpecific) are bound only
        // by stock and track length, never by these.
        private const int MinAutoHaulCarloads = 3;
        private const int MaxAutoHaulCars = 6;

        /// <summary>
        /// Count open booklets once per sweep: total plus per-origin, one pass. Only
        /// not-yet-taken hauls count toward the generation caps, so a map full of crews
        /// running work keeps offering fresh paper; the caps limit un-taken paper only.
        /// </summary>
        private static int CountOpenBooklets(Dictionary<string, int> perOrigin)
        {
            int total = 0;
            foreach (var kv in Jobs.StaticDirectHaulJobDefinition.jobDefinitions)
            {
                var state = kv.Value.LiveJob?.State;
                if (state != null && state != JobState.Available) continue;
                if (kv.Value.unpaidMove) continue; // dispatch work, not public paper
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

            // While the assignment lock holds, operations are dispatch-run: the lock-on
            // purge cleared the public board and no fresh public paper appears until the
            // lock lifts. Dispatcher-created hauls (CreateSpecific) are unaffected.
            if (Dispatch.AssignmentStore.Instance.LockEnabled)
            {
                Main.Log("[Director] assignment lock is on; no public hauls generated.");
                return false;
            }

            var econ = EconomyState.Instance;
            const int min = MinAutoHaulCarloads;
            const int max = MaxAutoHaulCars;
            int perStation = Math.Max(1, Main.Settings?.MaxOpenBookletsPerStation ?? 10);
            int total = Math.Max(1, Main.Settings?.MaxOpenBookletsTotal ?? 60);

            var perOrigin = new Dictionary<string, int>(StringComparer.Ordinal);
            if (CountOpenBooklets(perOrigin) >= total) return false;

            foreach (var producer in econ.Facilities.Values)
            {
                if (!producer.CanLoad) continue; // unload-only station is never an origin
                if (perOrigin.TryGetValue(producer.YardId, out var active) && active >= perStation) continue;
                foreach (var cargo in producer.Outputs)
                {
                    float stock = econ.GetDirectorAvailable(producer.YardId, cargo);
                    if (stock < min) continue;

                    var consumer = FindConsumer(econ, cargo, producer);
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
                    // Paid options draw produced stock; a pile that is all imported still
                    // offers an unpaid relocation so the form never hides movable goods.
                    float paidStock = econ.GetAvailable(producer.YardId, cargo);
                    float unpaidStock = econ.GetUnpaidAvailable(producer.YardId, cargo);
                    bool unpaidOnly = paidStock < 1f;
                    float stock = unpaidOnly ? unpaidStock : paidStock;
                    if (stock < 1f) continue;
                    // The vanilla route table is the authority on WHERE this origin's
                    // cargo may go; accept-list inference is only the fallback for
                    // cargo the table does not cover.
                    var routes = producer.DestinationsFor(cargo);
                    var consumers = econ.Facilities.Values
                        .Where(f => f.YardId != producer.YardId && f.CanUnload
                                    && (routes != null ? routes.Contains(f.YardId) : f.Consumes(cargo))
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
                        UnpaidOnly = unpaidOnly,
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
            public bool UnpaidOnly;
        }

        /// <summary>
        /// Dispatcher-picked haul. Non-finite mode spawns a pre-loaded consist and debits
        /// stock now; finite mode creates a carless job (players bring empties; stock is
        /// debited when the cars attach at the warehouse). Returns the job id or null.
        /// </summary>
        public static string CreateSpecific(string originYard, string destYard, CargoType cargo, int carCount,
            List<string> reservedCarIds, out string reason, out bool unpaidMove)
        {
            reason = null;
            unpaidMove = false;
            if (!Main.IsHostOrSingleplayer()) { reason = "host or singleplayer only"; return null; }
            if (string.Equals(originYard, destYard, StringComparison.OrdinalIgnoreCase))
            {
                // A same-yard haul loads and unloads in place: zero work that would flip
                // produced stock to imported and pay for nothing.
                reason = "origin and destination are the same yard";
                return null;
            }

            var econ = EconomyState.Instance;

            // The dispatcher is bound by the map too: vanilla's route table says where
            // each origin's cargo may go, and a haul to anywhere else would end at a
            // station that has no business receiving it.
            if (econ.Facilities.TryGetValue(originYard, out var originFacility) &&
                !originFacility.CanSend(cargo, destYard))
            {
                var allowed = originFacility.DestinationsFor(cargo);
                reason = $"{cargo} from {originYard} goes to {string.Join(", ", allowed.OrderBy(y => y))} (vanilla routing), not {destYard}";
                Main.Log($"[Director] {reason}");
                return null;
            }

            // Paid when produced stock covers it; otherwise fall back to an unpaid move
            // of imported goods (relocation is legitimate work, it just cannot pay twice).
            float paidStock = econ.GetAvailable(originYard, cargo);
            if (paidStock < carCount)
            {
                float total = econ.GetUnpaidAvailable(originYard, cargo);
                if (total < carCount)
                {
                    float reserved = econ.GetReserved(originYard, cargo);
                    reason = $"{originYard} has {paidStock:0.#} produced and {total:0.#} total {cargo} unpromised" +
                        (reserved >= 1f ? $" ({reserved:0.#} committed to taken hauls)" : "") +
                        $"; cannot ship {carCount}";
                    Main.Log($"[Director] {reason}");
                    return null;
                }
                unpaidMove = true;
            }

            var producerSc = StationController.GetStationByYardID(originYard);
            var consumerSc = StationController.GetStationByYardID(destYard);
            if (producerSc == null || consumerSc == null)
            {
                reason = $"unknown yard ({originYard} or {destYard})";
                Main.Log($"[Director] {reason}");
                return null;
            }

            // Name the blocking end when a warehouse cannot handle the cargo, instead of
            // a generic failure after the fact.
            var cargoList = new List<CargoType> { cargo };
            if ((producerSc.logicStation?.yard?.GetWarehouseMachinesThatSupportCargoTypes(cargoList)?.Count ?? 0) == 0)
            {
                reason = $"{originYard} has no warehouse that handles {cargo}";
                Main.Log($"[Director] {reason}");
                return null;
            }
            if ((consumerSc.logicStation?.yard?.GetWarehouseMachinesThatSupportCargoTypes(cargoList)?.Count ?? 0) == 0)
            {
                reason = $"{destYard} has no warehouse that handles {cargo}";
                Main.Log($"[Director] {reason}");
                return null;
            }

            // Every haul is carless; dispatch can reserve specific cars for it (API).
            var jobId = DirectHaulGenerator.TryCreateCarless(producerSc, consumerSc, cargo, carCount, reservedCarIds, unpaidMove);
            if (jobId == null) reason = "job creation failed; see game log";
            return jobId;
        }

        private static readonly Random _rng = new Random();

        private static FacilityDef FindConsumer(EconomyState econ, CargoType cargo, FacilityDef producer)
        {
            // The auto-director is only a baseline: it picks randomly among eligible
            // consumers. Deliberate routing belongs to the dispatcher (POST /api/v1/hauls),
            // and later to contracts. Eligibility follows the vanilla route table.
            var routes = producer.DestinationsFor(cargo);
            var eligible = new List<FacilityDef>();
            foreach (var f in econ.Facilities.Values)
            {
                if (f.YardId == producer.YardId) continue;
                if (!f.CanUnload) continue; // load-only station is never a destination
                if (routes != null ? !routes.Contains(f.YardId) : !f.Consumes(cargo)) continue;
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
