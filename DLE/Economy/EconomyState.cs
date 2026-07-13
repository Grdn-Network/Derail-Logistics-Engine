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
        public void Init(SaveGameData saveData, string modPath)
        {
            _facilities = RecipeProvider.BuildFacilities();
            RecipeProvider.ApplyOverlay(_facilities, modPath);
            LoadFrom(saveData);
            if (_stock.Count == 0)
                SeedInitialStock(Main.Settings?.InitialStock ?? 6);
            Main.Log($"[Economy] initialised {_facilities.Count} facilities; " +
                     $"{_stock.Count} have stock.");
        }

        /// <summary>
        /// Source industries (no inputs: mines, forests, wells) produce over time; that is
        /// where cargo enters the world. Factories only gain stock from real deliveries.
        /// </summary>
        public void TickSourceProduction(float carloads)
        {
            if (carloads <= 0f) return;
            foreach (var f in _facilities.Values)
            {
                if (f.Inputs.Count > 0 || f.Outputs.Count == 0) continue;
                foreach (var cargo in f.Outputs)
                {
                    Credit(f.YardId, cargo, carloads);
                    EconomyHistory.Record("production", f.YardId, cargo.ToString(), carloads);
                }
            }
        }

        /// <summary>New game: give each facility a starting stock of its output cargo.</summary>
        public void SeedInitialStock(int amount)
        {
            if (amount <= 0) return;
            int seeded = 0;
            foreach (var f in _facilities.Values)
                foreach (var cargo in f.Outputs)
                {
                    Credit(f.YardId, cargo, amount);
                    seeded++;
                }
            Main.Log($"[Economy] seeded {amount} carloads into {seeded} output stockpile(s).");
        }

        public void ReloadRecipes(string modPath)
        {
            _facilities = RecipeProvider.BuildFacilities();
            RecipeProvider.ApplyOverlay(_facilities, modPath);
            Main.Log($"[Economy] recipes reloaded ({_facilities.Count} facilities). Stockpiles kept.");
        }

        public float GetStock(string yardId, CargoType cargo) =>
            _stock.TryGetValue(yardId, out var m) && m.TryGetValue(cargo, out var v) ? v : 0f;

        /// <summary>Called when a Direct Haul unloads its cars at the destination.</summary>
        public void OnDelivered(string yardId, CargoType cargo, int carloads)
        {
            if (carloads <= 0) return;
            Credit(yardId, cargo, carloads);
            EconomyHistory.Record("delivered", yardId, cargo.ToString(), carloads);
            Main.Log($"[Economy] {yardId} received {carloads} {cargo} (now {GetStock(yardId, cargo):0.#}).");
            Convert(yardId);
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
        }

        public void SaveTo(SaveGameData data)
        {
            var payload = new SaveData
            {
                SchemaVersion = SchemaVersion,
                Stock = _stock.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value.ToDictionary(c => c.Key.ToString(), c => c.Value)),
            };
            data.SetObject(SaveKey, payload);
            Main.Log($"[Economy] saved stock for {_stock.Count} station(s).");
        }

        public void LoadFrom(SaveGameData data)
        {
            _stock = new Dictionary<string, Dictionary<CargoType, float>>(StringComparer.Ordinal);
            SaveData payload = null;
            try { payload = data.GetObject<SaveData>(SaveKey); }
            catch (Exception ex) { Main.Log($"[Economy] stock save unreadable, starting empty: {ex.Message}"); }

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
