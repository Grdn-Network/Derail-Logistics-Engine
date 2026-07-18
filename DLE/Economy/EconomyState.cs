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
        private const int SchemaVersion = 2;

        public static readonly EconomyState Instance = new EconomyState();
        private EconomyState() { }

        // yardId -> (cargo -> carloads on hand). _stock is the TOTAL pile; _imported
        // tracks the portion of it that arrived by delivery and has not been consumed
        // (invariant: 0 <= imported <= stock). Produced = stock - imported. Paid hauls
        // draw produced only; imported goods move payless until conversion consumes them
        // and credits produced outputs, so a unit of goods pays once per production
        // stage instead of once per bounce (#64).
        private Dictionary<string, Dictionary<CargoType, float>> _stock =
            new Dictionary<string, Dictionary<CargoType, float>>(StringComparer.Ordinal);
        private Dictionary<string, Dictionary<CargoType, float>> _imported =
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
            _imported.Clear();
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
                if (made <= 0f) continue;

                // Only produce, and only wear boosters, for output the store can actually
                // hold. A source sitting at its storage cap (nobody is hauling it away) must
                // not silently burn delivered booster tools and log phantom production for
                // carloads that Credit just clamps away.
                float actuallyMade = 0f;
                foreach (var cargo in f.Outputs)
                {
                    float add = Math.Min(made, GetRoom(f.YardId, cargo));
                    if (add <= 0f) continue;
                    Credit(f.YardId, cargo, add);
                    EconomyHistory.Record("production", f.YardId, cargo.ToString(), add);
                    if (add > actuallyMade) actuallyMade = add;
                }
                if (actuallyMade <= 0f) continue; // every output is full: no wear, no phantom log
                foreach (var (b, c) in active)
                    ConsumeImportedFirst(f.YardId, c, b.ConsumedPerCarload * actuallyMade);
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
                    foreach (var i in recipe.Inputs) ConsumeImportedFirst(yardId, i.Cargo, i.Amount);
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
        /// SOURCES seed outputs only (#91): their inputs are booster tools, and seeding
        /// every brand at every mine put ~30 carloads of free tools in each pit. Tools
        /// are a boost someone hauls in, not furniture the world spawns with.
        /// </summary>
        public void SeedInitialStock(int amount)
        {
            if (amount <= 0) return;
            int seeded = 0;
            foreach (var f in _facilities.Values)
            {
                var cargos = f.IsSource ? f.Outputs : f.Outputs.Concat(f.Inputs).Distinct();
                foreach (var cargo in cargos)
                {
                    Credit(f.YardId, cargo, amount);
                    // Input buffers seed as imported: consumables to work through, not
                    // local product, so shipping the seed around cannot mint pay.
                    if (!f.Produces(cargo))
                        MarkImported(f.YardId, cargo, amount);
                    seeded++;
                }
            }
            Main.Log($"[Economy] seeded {amount} carloads into {seeded} stockpile(s) (outputs, plus inputs at factories; sources get no free tools).");
        }

        public void ReloadRecipes(string modPath)
        {
            _facilities = RecipeProvider.BuildFacilities();
            RecipeProvider.ApplyOverlay(_facilities, modPath);
            Main.Log($"[Economy] recipes reloaded ({_facilities.Count} facilities). Stockpiles kept.");
        }

        public float GetStock(string yardId, CargoType cargo) =>
            _stock.TryGetValue(yardId, out var m) && m.TryGetValue(cargo, out var v) ? v : 0f;

        public float GetImported(string yardId, CargoType cargo) =>
            _imported.TryGetValue(yardId, out var m) && m.TryGetValue(cargo, out var v)
                ? Math.Min(v, GetStock(yardId, cargo)) : 0f;

        public float GetProduced(string yardId, CargoType cargo) =>
            GetStock(yardId, cargo) - GetImported(yardId, cargo);

        private void MarkImported(string yardId, CargoType cargo, float amount)
        {
            if (amount <= 0f) return;
            if (!_imported.TryGetValue(yardId, out var m))
                _imported[yardId] = m = new Dictionary<CargoType, float>();
            m.TryGetValue(cargo, out var cur);
            m[cargo] = Math.Min(GetStock(yardId, cargo), cur + amount);
        }

        /// <summary>Consume drawing the imported portion down first: conversion inputs,
        /// booster wear and unpaid moves all eat delivered goods before local product.</summary>
        private void ConsumeImportedFirst(string yardId, CargoType cargo, float amount)
        {
            if (amount <= 0f) return;
            float imp = GetImported(yardId, cargo);
            if (imp > 0f && _imported.TryGetValue(yardId, out var m))
                m[cargo] = Math.Max(0f, imp - amount);
            Consume(yardId, cargo, amount);
        }

        /// <summary>Consume local product only; the imported portion stays intact.</summary>
        private void ConsumeProduced(string yardId, CargoType cargo, float amount)
        {
            if (amount <= 0f) return;
            Consume(yardId, cargo, amount);
            // Re-clamp: produced may not cover it all (defensive), never let imported
            // exceed the remaining pile.
            if (_imported.TryGetValue(yardId, out var m) && m.TryGetValue(cargo, out var imp))
                m[cargo] = Math.Min(imp, GetStock(yardId, cargo));
        }

        // Supply reservations (#67, two tiers). Open (un-taken) paper holds SOFT: it
        // counts only against the auto-director's own generation, so paper never freezes
        // stock against dispatchers or crews. Taking a booklet hardens the hold after a
        // stock check; stale paper (supply promised away since printing) expires instead
        // of lying. The hold is consumed when cars attach or released when the booklet
        // dies. Paid reservations draw the produced pot; unpaid moves draw the pile as
        // a whole.

        [Serializable]
        public class Reservation
        {
            public string YardId;
            public string Cargo;
            public float Amount;
            public bool Soft;
            public bool Paid = true;
        }

        private Dictionary<string, Reservation> _reservations =
            new Dictionary<string, Reservation>(StringComparer.Ordinal);

        private float SumReservations(string yardId, CargoType cargo, bool includeSoft, bool? paid = null)
        {
            float total = 0f;
            var name = cargo.ToString();
            foreach (var r in _reservations.Values)
            {
                if (r.YardId != yardId || r.Cargo != name) continue;
                if (r.Soft && !includeSoft) continue;
                if (paid.HasValue && r.Paid != paid.Value) continue;
                total += r.Amount;
            }
            return total;
        }

        /// <summary>Hard holds only: what taken hauls have promised away.</summary>
        public float GetReserved(string yardId, CargoType cargo) =>
            SumReservations(yardId, cargo, includeSoft: false);

        /// <summary>
        /// Produced stock an unpaid hold will draw once its imported backing runs out. An
        /// unpaid move eats imported-first, but a hold larger than the imported portion
        /// bites into produced, so the produced views must reserve that overflow too or the
        /// same produced carloads get promised to both an unpaid move and a paid haul.
        /// </summary>
        private float UnpaidProducedClaim(string yardId, CargoType cargo, bool includeSoft) =>
            Math.Max(0f, SumReservations(yardId, cargo, includeSoft, paid: false) - GetImported(yardId, cargo));

        /// <summary>Dispatch view: produced stock minus hard paid holds (and the produced
        /// overflow of hard unpaid holds). Open paper does not block a dispatcher; taking
        /// that paper later is what gets refused if the supply is really gone.</summary>
        public float GetAvailable(string yardId, CargoType cargo) =>
            GetProduced(yardId, cargo)
            - SumReservations(yardId, cargo, includeSoft: false, paid: true)
            - UnpaidProducedClaim(yardId, cargo, includeSoft: false);

        /// <summary>Director view: produced minus every paid hold including open paper (and
        /// the produced overflow of unpaid holds), so generation never double-books a pile
        /// across booklets.</summary>
        public float GetDirectorAvailable(string yardId, CargoType cargo) =>
            GetProduced(yardId, cargo)
            - SumReservations(yardId, cargo, includeSoft: true, paid: true)
            - UnpaidProducedClaim(yardId, cargo, includeSoft: true);

        /// <summary>Unpaid-move view: the whole pile minus every hard hold.</summary>
        public float GetUnpaidAvailable(string yardId, CargoType cargo) =>
            GetStock(yardId, cargo) - SumReservations(yardId, cargo, includeSoft: false);

        /// <summary>Every carload on hand at a station, all cargo together: what counts
        /// against the shared storage total (#92).</summary>
        public float TotalStock(string yardId)
        {
            if (!_stock.TryGetValue(yardId, out var m)) return 0f;
            float total = 0f;
            foreach (var v in m.Values) total += v;
            return total;
        }

        /// <summary>The station's shared storage total; unlimited for yards with no
        /// facility definition.</summary>
        public float TotalCapOf(string yardId) =>
            _facilities.TryGetValue(yardId, out var f) ? f.TotalCap : float.MaxValue;

        /// <summary>
        /// Storage room left at a station. Capacity is ONE shared pool (#92): every cargo
        /// pile counts against the same total, so room is the same whatever you deliver
        /// (the cargo parameter stays for call-site clarity). A consumer with no room
        /// stops accepting deliveries, which is what caps demand and ends the
        /// source-to-consumer grind (finite demand, not just finite supply).
        /// </summary>
        public float GetRoom(string yardId, CargoType cargo)
        {
            return Math.Max(0f, TotalCapOf(yardId) - TotalStock(yardId));
        }

        public bool HasRoomFor(string yardId, CargoType cargo, float amount) =>
            GetRoom(yardId, cargo) >= amount;

        public void Reserve(string jobId, string yardId, CargoType cargo, float amount, bool paid = true)
        {
            _reservations[jobId] = new Reservation
            {
                YardId = yardId,
                Cargo = cargo.ToString(),
                Amount = amount,
                Soft = true,
                Paid = paid,
            };
            Main.Log($"[Economy] {jobId} soft-reserved {amount:0.#} {cargo} at {yardId}" +
                     $"{(paid ? "" : " (unpaid move)")}.");
        }

        /// <summary>
        /// Taking a booklet converts its soft hold to a hard one, validated against what
        /// is genuinely left. Returns false when the supply was promised away since the
        /// paper printed; the caller expires the stale booklet.
        /// </summary>
        public bool HardenReservation(string jobId)
        {
            if (!_reservations.TryGetValue(jobId, out var r)) return true; // attached or legacy
            if (!r.Soft) return true;
            if (!Enum.TryParse<CargoType>(r.Cargo, out var cargo)) return true;
            float free = r.Paid ? GetAvailable(r.YardId, cargo) : GetUnpaidAvailable(r.YardId, cargo);
            if (free + 0.001f < r.Amount)
            {
                Main.LogAlways($"[Economy] {jobId} is stale paper: needs {r.Amount:0.#} {r.Cargo} " +
                               $"at {r.YardId}, only {free:0.#} remains unpromised.");
                return false;
            }
            r.Soft = false;
            Main.Log($"[Economy] {jobId} hardened its hold: {r.Amount:0.#} {r.Cargo} at {r.YardId}.");
            return true;
        }

        /// <summary>Align reservation tiers with job reality after a world load: open
        /// paper holds soft, taken hauls hold hard.</summary>
        public void SyncReservationTiers(Func<string, bool> isOpenPaper)
        {
            foreach (var kv in _reservations)
                kv.Value.Soft = isOpenPaper(kv.Key);
        }

        /// <summary>The booklet died before cars attached: the supply returns.</summary>
        public void ReleaseReservation(string jobId)
        {
            if (!_reservations.TryGetValue(jobId, out var r)) return;
            _reservations.Remove(jobId);
            Main.Log($"[Economy] {jobId} released its reservation: {r.Amount:0.#} {r.Cargo} back at {r.YardId}.");
        }

        /// <summary>Cars attached: the promised supply physically leaves the stockpile.
        /// Paid hauls take local product; unpaid moves take the imported portion first.</summary>
        public void ConsumeReservation(string jobId, string yardId, CargoType cargo, float actualAmount)
        {
            bool paid = !_reservations.TryGetValue(jobId, out var r) || r.Paid;
            _reservations.Remove(jobId);
            if (paid) ConsumeProduced(yardId, cargo, actualAmount);
            else ConsumeImportedFirst(yardId, cargo, actualAmount);
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
            MarkImported(yardId, cargo, accepted);
            EconomyHistory.Record("delivered", yardId, cargo.ToString(), accepted);
            Main.Log($"[Economy] {yardId} received {accepted}/{carloads} {cargo} (now {GetStock(yardId, cargo):0.#}).");
            Conversion.Current.OnDelivered(this, yardId);
            return accepted;
        }

        public void Credit(string yardId, CargoType cargo, float amount)
        {
            if (!_stock.TryGetValue(yardId, out var m))
                _stock[yardId] = m = new Dictionary<CargoType, float>();
            // Clamp against the SHARED total (#92): whatever exceeds the station's free
            // space is dropped, exactly as the old per-cargo clamp dropped overflow.
            float room = Math.Max(0f, TotalCapOf(yardId) - TotalStock(yardId));
            m.TryGetValue(cargo, out var cur);
            m[cargo] = cur + Math.Min(amount, room);
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
                    foreach (var i in recipe.Inputs) ConsumeImportedFirst(yardId, i.Cargo, i.Amount);
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

        // Conversion may only draw stock that is not already promised to a taken haul.
        // Without the hard-hold subtraction, paced/instant conversion could eat an input
        // pile out from under a hardened reservation between accept and attach; the cars
        // then loaded real cargo the pile no longer had (minted carloads).
        private bool HasInputs(string yardId, RecipeDef r) =>
            r.Inputs.All(i => GetStock(yardId, i.Cargo) - GetReserved(yardId, i.Cargo) >= i.Amount);

        // Room is the shared pool (#92), and a conversion frees its input space as it
        // fills output space: the check is the NET stock change, so a full station can
        // still convert 2-in-2-out (net zero) but a 1-in-2-out recipe needs a free slot.
        // A non-growing conversion passes UNCONDITIONALLY: a station already over its
        // total (stock carried across the cap conversion) must be able to digest its
        // way back down, or it deadlocks with dead demand and blocked recipes at once.
        private bool HasOutputRoom(string yardId, FacilityDef f, RecipeDef r)
        {
            float net = 0f;
            foreach (var o in r.Outputs) net += o.Amount;
            foreach (var i in r.Inputs) net -= i.Amount;
            if (net <= 0f) return true;
            return TotalStock(yardId) + net <= f.TotalCap + 0.001f;
        }

        private static string Describe(List<CargoStack> stacks) =>
            string.Join(", ", stacks.Select(s => $"{s.Amount:0.#} {s.Cargo}"));

        // Persistence: stockpiles only. Cargo enums are stored by name so the save is stable.

        [Serializable]
        private class SaveData
        {
            public int SchemaVersion;
            public Dictionary<string, Dictionary<string, float>> Stock;
            public Dictionary<string, Dictionary<string, float>> Imported;
            public Dictionary<string, Reservation> Reservations;
        }

        private static Dictionary<string, Dictionary<string, float>> Pack(
            Dictionary<string, Dictionary<CargoType, float>> map) =>
            map.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.ToDictionary(c => c.Key.ToString(), c => c.Value));

        private static Dictionary<string, Dictionary<CargoType, float>> Unpack(
            Dictionary<string, Dictionary<string, float>> packed)
        {
            var map = new Dictionary<string, Dictionary<CargoType, float>>(StringComparer.Ordinal);
            if (packed == null) return map;
            foreach (var yard in packed)
            {
                var m = new Dictionary<CargoType, float>();
                foreach (var c in yard.Value)
                    if (Enum.TryParse<CargoType>(c.Key, out var cargo)) m[cargo] = c.Value;
                map[yard.Key] = m;
            }
            return map;
        }

        public void SaveTo(SaveGameData data)
        {
            var payload = new SaveData
            {
                SchemaVersion = SchemaVersion,
                Stock = Pack(_stock),
                Imported = Pack(_imported),
                Reservations = _reservations,
            };
            data.SetObject(SaveKey, payload);
            Main.Log($"[Economy] saved stock for {_stock.Count} station(s).");
        }

        public void LoadFrom(SaveGameData data)
        {
            _stock = new Dictionary<string, Dictionary<CargoType, float>>(StringComparer.Ordinal);
            _imported = new Dictionary<string, Dictionary<CargoType, float>>(StringComparer.Ordinal);
            _reservations = new Dictionary<string, Reservation>(StringComparer.Ordinal);
            SaveData payload = null;
            try { payload = data.GetObject<SaveData>(SaveKey); }
            catch (Exception ex) { Main.LogAlways($"[Economy] stock save unreadable, starting empty: {ex.Message}"); }

            if (payload?.Stock == null) return;
            if (payload.SchemaVersion > SchemaVersion)
            {
                // A full stockpile wipe (mod downgrade against a newer save) is not routine
                // chatter: log it unconditionally so it is never mistaken for corruption.
                Main.LogAlways($"[Economy] stock schema {payload.SchemaVersion} is newer than {SchemaVersion}; this build cannot read it, starting empty. Do not re-save or the newer economy is lost.");
                return;
            }

            _stock = Unpack(payload.Stock);
            _imported = Unpack(payload.Imported);

            // Reservations for jobs that no longer exist after the load are released by
            // the job restore pass (the surviving jobs re-register via their save entries).
            if (payload.Reservations != null)
                _reservations = payload.Reservations;

            if (payload.SchemaVersion < 2)
            {
                // v1: one pot, reservations without tiers. Existing stock counts as
                // produced (generous once, correct forever after) and every old
                // reservation was created by the paid path.
                foreach (var r in _reservations.Values) { r.Paid = true; r.Soft = false; }
                Main.LogAlways("[Economy] migrated v1 economy save: stock counted as produced.");
            }
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
                    .Select(c =>
                    {
                        float imp = GetImported(yard.Key, c.Key);
                        return imp >= 0.5f
                            ? $"{c.Value:0.#} {c.Key} ({imp:0.#} imported)"
                            : $"{c.Value:0.#} {c.Key}";
                    }));
                if (!string.IsNullOrEmpty(line)) Main.Log($"[Economy]   {yard.Key}: {line}");
            }
        }
    }
}
