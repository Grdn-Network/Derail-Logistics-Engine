using DV.ThingTypes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DLE.Economy
{
    /// <summary>
    /// The virtual economy: per-station stockpiles plus the derived facility recipes.
    /// A delivery credits the destination stockpile and immediately converts inputs to
    /// outputs by recipe ratio (0.1 has no production clock). Host/singleplayer only.
    /// </summary>
    public class EconomyState
    {
        private const string SaveKey = "DLE_Economy_v1";
        private const int SchemaVersion = 1;

        public static readonly EconomyState Instance = new EconomyState();
        private EconomyState() { }

        // yardId -> (cargo -> carloads on hand)
        private Dictionary<string, Dictionary<CargoType, float>> _stock =
            new Dictionary<string, Dictionary<CargoType, float>>(StringComparer.Ordinal);

        private Dictionary<string, FacilityDef> _facilities =
            new Dictionary<string, FacilityDef>(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, FacilityDef> Facilities => _facilities;

        // Rebuild recipes from the world, apply the overlay, then load saved stockpiles.
        // Returns true when this is a fresh economy that was just seeded (new game).
        public bool Init(SaveGameData saveData, string modPath)
        {
            _facilities = RecipeProvider.BuildFacilities();
            RecipeProvider.ApplyOverlay(_facilities, modPath);
            LoadFrom(saveData);
            bool seeded = _stock.Count == 0;
            if (seeded)
                SeedInitialStock(RecipeProvider.Tuning.initialStock);
            Main.Log($"[Economy] initialised {_facilities.Count} facilities; " +
                     $"{_stock.Count} have stock.");
            return seeded;
        }

        /// <summary>company.resupply: wipe every stockpile back to the starting seed.</summary>
        public void ResetToDefault(int amount)
        {
            _stock.Clear();
            SeedInitialStock(amount);
        }

        /// <summary>
        /// Source industries produce over time with no required inputs; that is where
        /// cargo enters the world. Factories only gain stock from real deliveries.
        /// A source is any facility flagged in economy.json, or one with outputs and no
        /// inputs at all. Boosters multiply the rate: any one in-stock cargo of a booster
        /// entry activates it (one tool brand is enough), active boosters stack, and each
        /// is slowly consumed per carload produced.
        /// </summary>
        public void TickSourceProduction(float carloads)
        {
            if (carloads <= 0f) return;
            foreach (var f in _facilities.Values)
            {
                if (f.Outputs.Count == 0) continue;
                if (!f.IsSource && f.Inputs.Count > 0) continue;

                float mult = 1f;
                var active = new List<(BoosterDef def, CargoType cargo)>();
                foreach (var b in f.Boosters)
                    foreach (var c in b.Cargo)
                        if (GetStock(f.YardId, c) >= 1f)
                        {
                            mult *= b.Speedup;
                            active.Add((b, c));
                            break;
                        }

                float made = carloads * mult;
                foreach (var cargo in f.Outputs)
                {
                    Credit(f.YardId, cargo, made);
                    EconomyHistory.Record("production", f.YardId, cargo.ToString(), made);
                }
                foreach (var (b, c) in active)
                    Debit(f.YardId, c, b.ConsumedPerCarload * made);
            }

            // Factories chew their input buffers on the same clock: at most one batch per
            // recipe per interval, so seeded or stockpiled ingredients drain at a visible
            // rate. Deliveries still convert immediately (InstantConversion), which only
            // matters when the paced clock has left a backlog.
            foreach (var f in _facilities.Values)
                if (f.Recipes.Count > 0)
                    ConvertBatches(f.YardId, (int)carloads);
        }

        /// <summary>Run each recipe at a station at most a set number of times.</summary>
        public void ConvertBatches(string yardId, int batches)
        {
            if (batches <= 0 || !_facilities.TryGetValue(yardId, out var facility)) return;
            foreach (var recipe in facility.Recipes)
            {
                if (recipe.Inputs.Count == 0 || recipe.Outputs.Count == 0) continue;
                for (int n = 0; n < batches; n++)
                {
                    if (!HasInputs(yardId, recipe) || !HasOutputRoom(yardId, facility, recipe)) break;
                    foreach (var i in recipe.Inputs) Consume(yardId, i.Cargo, i.Amount);
                    foreach (var o in recipe.Outputs)
                    {
                        Credit(yardId, o.Cargo, o.Amount);
                        EconomyHistory.Record("converted", yardId, o.Cargo.ToString(), o.Amount);
                    }
                    Main.Log($"[Economy] {yardId} converted [{Describe(recipe.Inputs)}] -> [{Describe(recipe.Outputs)}] (paced).");
                }
            }
        }

        /// <summary>
        /// New game: give each facility a starting stock of its outputs AND its inputs.
        /// The input side is the working buffer: a sawmill spawns with logs to consume,
        /// so conversion runs from the first tick instead of waiting on a delivery.
        /// </summary>
        public void SeedInitialStock(int amount)
        {
            if (amount <= 0) return;
            int seeded = 0;
            foreach (var f in _facilities.Values)
                foreach (var cargo in f.Outputs.Concat(f.Inputs).Distinct())
                {
                    Credit(f.YardId, cargo, amount);
                    seeded++;
                }
            Main.Log($"[Economy] seeded {amount} carloads into {seeded} stockpile(s) (outputs and inputs).");
        }

        public void ReloadRecipes(string modPath)
        {
            _facilities = RecipeProvider.BuildFacilities();
            RecipeProvider.ApplyOverlay(_facilities, modPath);
            Main.Log($"[Economy] recipes reloaded ({_facilities.Count} facilities). Stockpiles kept.");
        }

        public float GetStock(string yardId, CargoType cargo) =>
            _stock.TryGetValue(yardId, out var m) && m.TryGetValue(cargo, out var v) ? v : 0f;

        // Supply reservations: a spawned job pre-allocates its supply. The reservation is
        // consumed when cars attach (real debit) or released when the booklet dies
        // (expire, abandon, delete), returning the supply to the available pool.

        [Serializable]
        public class Reservation
        {
            public string YardId;
            public string Cargo;
            public float Amount;
        }

        private Dictionary<string, Reservation> _reservations =
            new Dictionary<string, Reservation>(StringComparer.Ordinal);

        public float GetReserved(string yardId, CargoType cargo)
        {
            float total = 0f;
            var name = cargo.ToString();
            foreach (var r in _reservations.Values)
                if (r.YardId == yardId && r.Cargo == name)
                    total += r.Amount;
            return total;
        }

        /// <summary>Stock a new job could still claim: on hand minus already promised.</summary>
        public float GetAvailable(string yardId, CargoType cargo) =>
            GetStock(yardId, cargo) - GetReserved(yardId, cargo);

        /// <summary>
        /// Storage room left for a cargo at a station: its cap minus what is on hand. A
        /// consumer with no room stops accepting deliveries, which is what caps demand and
        /// ends the source-to-consumer grind (finite demand, not just finite supply).
        /// </summary>
        public float GetRoom(string yardId, CargoType cargo)
        {
            float cap = _facilities.TryGetValue(yardId, out var f) ? f.Cap(cargo) : float.MaxValue;
            return Math.Max(0f, cap - GetStock(yardId, cargo));
        }

        public bool HasRoomFor(string yardId, CargoType cargo, float amount) =>
            GetRoom(yardId, cargo) >= amount;

        public void Reserve(string jobId, string yardId, CargoType cargo, float amount)
        {
            _reservations[jobId] = new Reservation { YardId = yardId, Cargo = cargo.ToString(), Amount = amount };
            Main.Log($"[Economy] {jobId} reserved {amount:0.#} {cargo} at {yardId} " +
                     $"(available now {GetAvailable(yardId, cargo):0.#}).");
        }

        /// <summary>The booklet died before cars attached: the supply returns.</summary>
        public void ReleaseReservation(string jobId)
        {
            if (!_reservations.TryGetValue(jobId, out var r)) return;
            _reservations.Remove(jobId);
            Main.Log($"[Economy] {jobId} released its reservation: {r.Amount:0.#} {r.Cargo} back at {r.YardId}.");
        }

        /// <summary>Cars attached: the promised supply physically leaves the stockpile.</summary>
        public void ConsumeReservation(string jobId, string yardId, CargoType cargo, float actualAmount)
        {
            _reservations.Remove(jobId);
            Debit(yardId, cargo, actualAmount);
        }

        /// <summary>
        /// Called when a Direct Haul unloads its cars at the destination. Only the cargo the
        /// station still has room for is accepted (a full consumer accepts nothing and so
        /// pays nothing); returns the accepted carloads so the payment gate pays for exactly
        /// what was delivered.
        /// </summary>
        public int OnDelivered(string yardId, CargoType cargo, int carloads)
        {
            if (carloads <= 0) return 0;
            // Epsilon absorbs float drift from fractional recipes (HasOutputRoom does the
            // same); without it 3.9999962 room truncated to 3 and ate a real carload.
            int accepted = (int)Math.Min(carloads, GetRoom(yardId, cargo) + 0.001f);
            if (accepted <= 0)
            {
                Main.Log($"[Economy] {yardId} is full of {cargo}; delivery accepted nothing.");
                return 0;
            }
            Credit(yardId, cargo, accepted);
            EconomyHistory.Record("delivered", yardId, cargo.ToString(), accepted);
            Main.Log($"[Economy] {yardId} received {accepted}/{carloads} {cargo} (now {GetStock(yardId, cargo):0.#}).");
            Conversion.Current.OnDelivered(this, yardId);
            return accepted;
        }

        public void Credit(string yardId, CargoType cargo, float amount)
        {
            if (!_stock.TryGetValue(yardId, out var m))
                _stock[yardId] = m = new Dictionary<CargoType, float>();
            float cap = _facilities.TryGetValue(yardId, out var f) ? f.Cap(cargo) : float.MaxValue;
            m.TryGetValue(cargo, out var cur);
            m[cargo] = Math.Min(cap, cur + amount);
        }

        private void Consume(string yardId, CargoType cargo, float amount)
        {
            if (_stock.TryGetValue(yardId, out var m) && m.TryGetValue(cargo, out var cur))
                m[cargo] = Math.Max(0f, cur - amount);
        }

        /// <summary>Remove stock (e.g. a producer loading cars for a haul).</summary>
        public void Debit(string yardId, CargoType cargo, float amount) =>
            Consume(yardId, cargo, amount);

        /// <summary>Run every recipe at a station while its inputs are available and outputs have room.</summary>
        public void Convert(string yardId)
        {
            if (!_facilities.TryGetValue(yardId, out var facility))
            {
                Main.Log($"[Economy] {yardId} has no facility definition; nothing to convert.");
                return;
            }
            if (facility.Recipes.Count == 0)
            {
                // Pure source or pure sink: stock just accumulates, by design.
                if (Main.Settings?.VerboseLogging == true)
                    Main.Log($"[Economy] {yardId} has no recipe (source or sink); stock holds.");
                return;
            }

            foreach (var recipe in facility.Recipes)
            {
                if (recipe.Inputs.Count == 0 || recipe.Outputs.Count == 0) continue;

                int guard = 0;
                while (HasInputs(yardId, recipe) && HasOutputRoom(yardId, facility, recipe) && guard++ < 1000)
                {
                    foreach (var i in recipe.Inputs) Consume(yardId, i.Cargo, i.Amount);
                    foreach (var o in recipe.Outputs)
                    {
                        Credit(yardId, o.Cargo, o.Amount);
                        EconomyHistory.Record("converted", yardId, o.Cargo.ToString(), o.Amount);
                    }
                    Main.Log($"[Economy] {yardId} converted [{Describe(recipe.Inputs)}] -> [{Describe(recipe.Outputs)}].");
                }

                if (guard == 0)
                {
                    // Say WHY the recipe did not run, so balance problems are visible.
                    var missing = recipe.Inputs
                        .Where(i => GetStock(yardId, i.Cargo) < i.Amount)
                        .Select(i => $"{i.Cargo} ({GetStock(yardId, i.Cargo):0.#}/{i.Amount:0.#})")
                        .ToList();
                    if (missing.Count > 0)
                        Main.Log($"[Economy] {yardId} recipe idle, missing inputs: {string.Join(", ", missing)}.");
                    else if (!HasOutputRoom(yardId, facility, recipe))
                        Main.Log($"[Economy] {yardId} recipe idle, output storage full.");
                }
            }
        }

        private bool HasInputs(string yardId, RecipeDef r) =>
            r.Inputs.All(i => GetStock(yardId, i.Cargo) >= i.Amount);

        private bool HasOutputRoom(string yardId, FacilityDef f, RecipeDef r) =>
            r.Outputs.All(o => GetStock(yardId, o.Cargo) + o.Amount <= f.Cap(o.Cargo) + 0.001f);

        private static string Describe(List<CargoStack> stacks) =>
            string.Join(", ", stacks.Select(s => $"{s.Amount:0.#} {s.Cargo}"));

        // Persistence: stockpiles only. Cargo enums are stored by name so the save is stable.

        [Serializable]
        private class SaveData
        {
            public int SchemaVersion;
            public Dictionary<string, Dictionary<string, float>> Stock;
            public Dictionary<string, Reservation> Reservations;
        }

        public void SaveTo(SaveGameData data)
        {
            var payload = new SaveData
            {
                SchemaVersion = SchemaVersion,
                Stock = _stock.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value.ToDictionary(c => c.Key.ToString(), c => c.Value)),
                Reservations = _reservations,
            };
            data.SetObject(SaveKey, payload);
            Main.Log($"[Economy] saved stock for {_stock.Count} station(s).");
        }

        public void LoadFrom(SaveGameData data)
        {
            _stock = new Dictionary<string, Dictionary<CargoType, float>>(StringComparer.Ordinal);
            _reservations = new Dictionary<string, Reservation>(StringComparer.Ordinal);
            SaveData payload = null;
            try { payload = data.GetObject<SaveData>(SaveKey); }
            catch (Exception ex) { Main.LogAlways($"[Economy] stock save unreadable, starting empty: {ex.Message}"); }

            if (payload?.Stock == null) return;
            if (payload.SchemaVersion != SchemaVersion)
            {
                Main.Log($"[Economy] stock schema {payload.SchemaVersion} != {SchemaVersion}, starting empty.");
                return;
            }

            foreach (var yard in payload.Stock)
            {
                var m = new Dictionary<CargoType, float>();
                foreach (var c in yard.Value)
                    if (Enum.TryParse<CargoType>(c.Key, out var cargo)) m[cargo] = c.Value;
                _stock[yard.Key] = m;
            }

            // Reservations for jobs that no longer exist after the load are released by
            // the job restore pass (the surviving jobs re-register via their save entries).
            if (payload.Reservations != null)
                _reservations = payload.Reservations;
        }

        /// <summary>Drop reservations whose job did not survive the load; supply returns.</summary>
        public void ReleaseOrphanedReservations(Func<string, bool> jobStillExists)
        {
            var dead = _reservations.Keys.Where(id => !jobStillExists(id)).ToList();
            foreach (var id in dead) ReleaseReservation(id);
            if (dead.Count > 0)
                Main.LogAlways($"[Economy] released {dead.Count} orphaned supply reservation(s) after load.");
        }

        public void DumpToLog()
        {
            Main.Log($"[Economy] {_facilities.Count} facilities, {_stock.Count} with stock:");
            foreach (var yard in _stock.OrderBy(k => k.Key))
            {
                var line = string.Join(", ", yard.Value.Where(c => c.Value > 0f)
                    .Select(c => $"{c.Value:0.#} {c.Key}"));
                if (!string.IsNullOrEmpty(line)) Main.Log($"[Economy]   {yard.Key}: {line}");
            }
        }
    }
}
