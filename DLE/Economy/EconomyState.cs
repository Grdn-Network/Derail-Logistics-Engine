using DV.ThingTypes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DLE.Economy
{
    /// <summary>
    /// The virtual economy (#100): per-station stockpiles, the derived facility recipes,
    /// and since 0.44 the whole simulation runs on ONE clock, in-game time. Sources make
    /// carloads per game hour (machines required, catalysts slow their wear), factories
    /// run recipe batches per game hour (catalysts double them), cities and the power
    /// plant consume on the clock and feed a global productivity boost, and the harbor
    /// restocks imports in proportion to the exports it receives. Host/singleplayer only.
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

        // Per-yard runtime machinery state (#100). Machine wear and the installed
        // catalyst persist; the fractional credits are volatile (worst case a save
        // loses under one carload of progress per station).
        private class YardOps
        {
            public Dictionary<string, float> MachineWear = new Dictionary<string, float>(StringComparer.Ordinal);
            public float CatalystHoursLeft;
            public float ProdCredit;
            public float BatchCredit;
            public float ConsumeCredit;
            public float ScrapCredit;
            public bool ScrapMetalNext = true;
            public int Rotation;
            public int RecipeRotation;
            public bool WarnedLastMachine;
        }

        private readonly Dictionary<string, YardOps> _ops =
            new Dictionary<string, YardOps>(StringComparer.Ordinal);

        private YardOps Ops(string yardId)
        {
            if (!_ops.TryGetValue(yardId, out var ops))
                _ops[yardId] = ops = new YardOps();
            return ops;
        }

        // 24-hour rings for the global boost (consumption) and harbor import scaling
        // (exports received). Advanced by whole game hours.
        private readonly float[] _consumedByHour = new float[24];
        private readonly float[] _hbExportsByHour = new float[24];
        private int _hourSlot;
        private float _hourAccum;

        public float GlobalBoost { get; private set; } = 1f;

        private int _importCounter;

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
            else if (_generation < CurrentGeneration)
            {
                // A pre-0.44 world has no machines anywhere: without this one-time grant
                // every mine, forest and the farm would crawl from the first minute with
                // a four-haul bootstrap chain between them and full speed.
                GrantStarterMachines();
            }
            Main.Log($"[Economy] initialised {_facilities.Count} facilities; " +
                     $"{_stock.Count} have stock.");
            return seeded;
        }

        // Bumped when a new economy generation needs a one-time migration on old saves.
        private const int CurrentGeneration = 2;
        private int _generation = CurrentGeneration;

        private void GrantStarterMachines()
        {
            int granted = 0;
            foreach (var f in _facilities.Values)
            {
                foreach (var machine in f.Machines)
                {
                    if (GetStock(f.YardId, machine) >= 1f) continue;
                    Credit(f.YardId, machine, RecipeProvider.Tuning.seedMachines);
                    MarkImported(f.YardId, machine, RecipeProvider.Tuning.seedMachines);
                    granted += RecipeProvider.Tuning.seedMachines;
                }
            }
            _generation = CurrentGeneration;
            if (granted > 0)
                Main.LogAlways($"[Economy] pre-0.44 save: granted {granted} starter machine(s) so the sources open at full speed. Replacements are on you.");
        }

        /// <summary>company.resupply: wipe every stockpile back to the starting seed.</summary>
        public void ResetToDefault(int amount)
        {
            _stock.Clear();
            _imported.Clear();
            _ops.Clear();
            SeedInitialStock(amount);
        }

        public void ReloadRecipes(string modPath)
        {
            _facilities = RecipeProvider.BuildFacilities();
            RecipeProvider.ApplyOverlay(_facilities, modPath);
            Main.Log($"[Economy] recipes reloaded ({_facilities.Count} facilities). Stockpiles kept.");
        }

        /// <summary>
        /// New game: every facility starts with stock of its outputs, factories also get
        /// their input working buffers (a sawmill spawns with logs to cut), and sites
        /// with required MACHINES are seeded with a starter set so the world opens at
        /// full speed (#100: seed 2 of each; the first replacement haul is the player's
        /// problem). Sources get no free catalyst tools: tools are hauled in, always.
        /// </summary>
        public void SeedInitialStock(int amount)
        {
            if (amount <= 0) return;
            int seeded = 0;
            foreach (var f in _facilities.Values)
            {
                // Consumers seed NOTHING: they are demand, and pre-filling a city's 34
                // accepted cargos put it at its cap before the first train ran. The
                // import hub seeds only its import stock (exports arrive by rail);
                // sources seed outputs only; factories also get input working buffers.
                IEnumerable<CargoType> cargos;
                if (f.ConsumesStock) continue;
                else if (f.IsSource || f.IsImportHub) cargos = f.Outputs;
                else cargos = f.Outputs.Concat(f.Inputs).Distinct();
                foreach (var cargo in cargos)
                {
                    if (f.Machines.Contains(cargo)) continue; // machines seed separately below
                    Credit(f.YardId, cargo, amount);
                    // Input buffers seed as imported: consumables to work through, not
                    // local product, so shipping the seed around cannot mint pay.
                    if (!f.Produces(cargo))
                        MarkImported(f.YardId, cargo, amount);
                    seeded++;
                }
                foreach (var machine in f.Machines)
                {
                    Credit(f.YardId, machine, RecipeProvider.Tuning.seedMachines);
                    MarkImported(f.YardId, machine, RecipeProvider.Tuning.seedMachines);
                }
            }
            Main.Log($"[Economy] seeded {amount} carloads into {seeded} stockpile(s) plus starter machines.");
        }

        // ------------------------------------------------------------------
        // The clock (#100): everything below TickGameTime runs the simulation.
        // ------------------------------------------------------------------

        /// <summary>Advance the whole economy by a slice of in-game time.</summary>
        public void TickGameTime(float hours)
        {
            if (hours <= 0f) return;
            AdvanceHourRings(hours);
            GlobalBoost = ComputeGlobalBoost();

            foreach (var f in _facilities.Values)
            {
                try
                {
                    if (f.ConsumesStock) TickConsumer(f, hours);
                    if (f.IsSource) TickSource(f, hours);
                    else if (f.Recipes.Count > 0) TickFactory(f, hours);
                    if (f.IsImportHub) TickImportHub(f, hours);
                    UpdateMachineWarning(f);
                }
                catch (Exception ex)
                {
                    Main.LogAlways($"[Economy] {f.YardId} tick failed: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        private void AdvanceHourRings(float hours)
        {
            _hourAccum += hours;
            while (_hourAccum >= 1f)
            {
                _hourAccum -= 1f;
                _hourSlot = (_hourSlot + 1) % 24;
                _consumedByHour[_hourSlot] = 0f;
                _hbExportsByHour[_hourSlot] = 0f;
            }
        }

        private float ComputeGlobalBoost()
        {
            var t = RecipeProvider.Tuning;
            if (t.globalBoostMax <= 1f || t.globalBoostFullAt <= 0f) return 1f;
            float consumed24 = 0f;
            foreach (var v in _consumedByHour) consumed24 += v;
            float share = Math.Min(1f, consumed24 / t.globalBoostFullAt);
            return 1f + share * (t.globalBoostMax - 1f);
        }

        /// <summary>All required machine types present (one of each is enough).</summary>
        public bool MachinesOk(FacilityDef f) =>
            f.Machines.Count == 0 || f.Machines.All(m => GetStock(f.YardId, m) >= 1f);

        private void TickSource(FacilityDef f, float hours)
        {
            var t = RecipeProvider.Tuning;
            var ops = Ops(f.YardId);
            if (f.Outputs.Count == 0) return;

            bool machinesOk = MachinesOk(f);
            float rate = t.sourceCarloadsPerGameHour * hours * GlobalBoost
                * (machinesOk ? 1f : t.crawlFactor);
            ops.ProdCredit = Math.Min(ops.ProdCredit + rate, 3f);

            int made = 0;
            while (ops.ProdCredit >= 1f)
            {
                if (GetRoom(f.YardId, default) < 1f) break; // shared pool is full
                var cargo = f.Outputs[ops.Rotation % f.Outputs.Count];
                ops.Rotation++;
                Credit(f.YardId, cargo, 1f);
                EconomyHistory.Record("production", f.YardId, cargo.ToString(), 1);
                made++;
                ops.ProdCredit -= 1f;
            }
            if (made == 0) return; // no production, no wear, no catalyst burn

            bool catalystActive = EnsureCatalyst(f, ops);
            float wear = made * (catalystActive ? 1f / Math.Max(1f, t.catalystWearFactor) : 1f);
            foreach (var machine in f.Machines)
                AddMachineWear(f, ops, machine, wear);
            if (catalystActive)
                ops.CatalystHoursLeft = Math.Max(0f, ops.CatalystHoursLeft - hours);
        }

        private void TickFactory(FacilityDef f, float hours)
        {
            var t = RecipeProvider.Tuning;
            var ops = Ops(f.YardId);

            bool catalystReady = ops.CatalystHoursLeft > 0f || AnyCatalystStock(f);
            float speed = t.factoryBatchesPerGameHour * hours * GlobalBoost
                * (catalystReady ? t.factoryBoostFactor : 1f);
            ops.BatchCredit = Math.Min(ops.BatchCredit + speed, 3f);

            int ran = 0;
            while (ops.BatchCredit >= 1f)
            {
                var recipe = NextRunnableRecipe(f, ops);
                if (recipe == null) break;
                RunBatch(f, recipe);
                ops.BatchCredit -= 1f;
                ran++;
            }
            if (ran == 0) return;

            if (EnsureCatalyst(f, ops))
                ops.CatalystHoursLeft = Math.Max(0f, ops.CatalystHoursLeft - hours);
        }

        private RecipeDef NextRunnableRecipe(FacilityDef f, YardOps ops)
        {
            // Rotate the starting recipe so a station with several (GF has five) shares
            // its batches instead of forever favouring the first runnable one.
            int n = f.Recipes.Count;
            for (int i = 0; i < n; i++)
            {
                var recipe = f.Recipes[(ops.RecipeRotation + i) % n];
                if (recipe.Inputs.Count == 0 || recipe.Outputs.Count == 0) continue;
                if (HasInputs(f.YardId, recipe) && HasOutputRoom(f.YardId, f, recipe))
                {
                    ops.RecipeRotation = (ops.RecipeRotation + i + 1) % n;
                    return recipe;
                }
            }
            return null;
        }

        private void RunBatch(FacilityDef f, RecipeDef recipe)
        {
            foreach (var input in recipe.Inputs)
                ConsumeStack(f.YardId, input);
            foreach (var output in recipe.Outputs)
            {
                Credit(f.YardId, output.Cargo, output.Amount);
                EconomyHistory.Record("converted", f.YardId, output.Cargo.ToString(), output.Amount);
            }
            Main.Log($"[Economy] {f.YardId} converted [{Describe(recipe.Inputs)}] -> [{Describe(recipe.Outputs)}].");
        }

        private void TickConsumer(FacilityDef f, float hours)
        {
            var t = RecipeProvider.Tuning;
            var ops = Ops(f.YardId);
            ops.ConsumeCredit = Math.Min(ops.ConsumeCredit + t.cityConsumptionPerHour * hours, 4f);

            while (ops.ConsumeCredit >= 1f)
            {
                var cargo = BiggestConsumablePile(f);
                if (cargo == null) break;
                ConsumeImportedFirst(f.YardId, cargo.Value, 1f);
                EconomyHistory.Record("consumed", f.YardId, cargo.Value.ToString(), 1);
                _consumedByHour[_hourSlot] += 1f;
                ops.ConsumeCredit -= 1f;

                if (f.EmitsScrap)
                {
                    ops.ScrapCredit += 1f;
                    if (ops.ScrapCredit >= Math.Max(1f, t.carloadsPerScrap))
                    {
                        ops.ScrapCredit -= Math.Max(1f, t.carloadsPerScrap);
                        var scrap = ops.ScrapMetalNext ? CargoType.ScrapMetal : CargoType.ScrapWood;
                        ops.ScrapMetalNext = !ops.ScrapMetalNext;
                        Credit(f.YardId, scrap, 1f);
                        EconomyHistory.Record("scrap", f.YardId, scrap.ToString(), 1);
                    }
                }
            }
        }

        private CargoType? BiggestConsumablePile(FacilityDef f)
        {
            if (!_stock.TryGetValue(f.YardId, out var piles)) return null;
            CargoType? best = null;
            float bestAmount = 1f - 0.001f;
            foreach (var kv in piles)
            {
                // A city never eats its own scrap output back (that cargo exists to be
                // hauled to the mills), and nobody consumes below a whole carload.
                if (f.EmitsScrap && (kv.Key == CargoType.ScrapMetal || kv.Key == CargoType.ScrapWood)) continue;
                if (kv.Value > bestAmount)
                {
                    bestAmount = kv.Value;
                    best = kv.Key;
                }
            }
            return best;
        }

        private void TickImportHub(FacilityDef f, float hours)
        {
            var t = RecipeProvider.Tuning;
            if (f.Outputs.Count == 0) return;
            var ops = Ops(f.YardId);

            float exports24 = 0f;
            foreach (var v in _hbExportsByHour) exports24 += v;
            float gain = exports24 / 24f * t.harborImportFactor * hours;
            ops.ProdCredit = Math.Min(ops.ProdCredit + gain, 6f);

            var toolMembers = CargoCategories.TryGet("Tools", out var tools)
                ? tools.Where(c => f.Outputs.Contains(c)).ToList()
                : new List<CargoType>();
            var nonTools = f.Outputs.Where(c => !toolMembers.Contains(c)).ToList();

            while (ops.ProdCredit >= 1f)
            {
                if (GetRoom(f.YardId, default) < 1f) break;
                CargoType cargo;
                _importCounter++;
                if (toolMembers.Count > 0 && t.toolImportRarity > 0 && _importCounter % t.toolImportRarity == 0)
                    cargo = toolMembers[_importCounter / t.toolImportRarity % toolMembers.Count];
                else if (nonTools.Count > 0)
                    cargo = nonTools[ops.Rotation++ % nonTools.Count];
                else
                    break;
                Credit(f.YardId, cargo, 1f);
                EconomyHistory.Record("imported", f.YardId, cargo.ToString(), 1);
                ops.ProdCredit -= 1f;
            }
        }

        // Machines and catalysts ------------------------------------------------

        private void AddMachineWear(FacilityDef f, YardOps ops, CargoType machine, float wear)
        {
            if (GetStock(f.YardId, machine) < 1f) return; // nothing installed to wear
            var key = machine.ToString();
            ops.MachineWear.TryGetValue(key, out var current);
            current += wear;
            int life = Math.Max(1, RecipeProvider.Tuning.machineLifeCarloads);
            if (current >= life)
            {
                current -= life;
                ConsumeImportedFirst(f.YardId, machine, 1f);
                int left = (int)Math.Floor(GetStock(f.YardId, machine) + 0.001f);
                Main.LogAlways($"[Economy] {f.YardId}: a {machine} wore out after {life} carloads of work; {left} left" +
                               (left == 0 ? $". Production crawls at {RecipeProvider.Tuning.crawlFactor:P0} until a new one arrives." : "."));
                EconomyHistory.Record("machine_worn", f.YardId, key, 1);
            }
            ops.MachineWear[key] = current;
        }

        private void UpdateMachineWarning(FacilityDef f)
        {
            if (f.Machines.Count == 0) return;
            var ops = Ops(f.YardId);
            bool onLast = f.Machines.Any(m => GetStock(f.YardId, m) < 2f);
            if (onLast && !ops.WarnedLastMachine)
            {
                var low = f.Machines.Where(m => GetStock(f.YardId, m) < 2f)
                    .Select(m => $"{m} ({(int)Math.Floor(GetStock(f.YardId, m) + 0.001f)})");
                Main.LogAlways($"[Economy] {f.YardId} is down to its last machine(s): {string.Join(", ", low)}. Ship replacements before it crawls.");
            }
            ops.WarnedLastMachine = onLast;
        }

        /// <summary>Board: a facility that needs a machine haul soon.</summary>
        public bool OnLastMachine(string yardId) =>
            _facilities.TryGetValue(yardId, out var f) && f.Machines.Count > 0 &&
            f.Machines.Any(m => GetStock(yardId, m) < 2f);

        public class MachineInfo
        {
            public string Cargo;
            public int Have;
            public float WearRemaining; // carloads of work left on the current unit
        }

        public List<MachineInfo> MachineStatus(string yardId)
        {
            var result = new List<MachineInfo>();
            if (!_facilities.TryGetValue(yardId, out var f)) return result;
            var ops = Ops(yardId);
            int life = Math.Max(1, RecipeProvider.Tuning.machineLifeCarloads);
            foreach (var machine in f.Machines)
            {
                ops.MachineWear.TryGetValue(machine.ToString(), out var wear);
                result.Add(new MachineInfo
                {
                    Cargo = machine.ToString(),
                    Have = (int)Math.Floor(GetStock(yardId, machine) + 0.001f),
                    WearRemaining = Math.Max(0f, life - wear),
                });
            }
            return result;
        }

        public (bool active, float hoursLeft, bool anyStock) CatalystStatus(string yardId)
        {
            if (!_facilities.TryGetValue(yardId, out var f) || f.Catalysts.Count == 0)
                return (false, 0f, false);
            var ops = Ops(yardId);
            return (ops.CatalystHoursLeft > 0f, ops.CatalystHoursLeft, AnyCatalystStock(f));
        }

        private IEnumerable<CargoType> CatalystMembers(FacilityDef f)
        {
            foreach (var name in f.Catalysts)
            {
                if (CargoCategories.TryGet(name, out var members))
                {
                    foreach (var m in members) yield return m;
                }
                else if (Enum.TryParse<CargoType>(name, out var cargo))
                {
                    yield return cargo;
                }
            }
        }

        private bool AnyCatalystStock(FacilityDef f) =>
            CatalystMembers(f).Any(c => GetStock(f.YardId, c) >= 1f);

        /// <summary>Install a catalyst carload when none is active: consumed on the spot,
        /// good for catalystLifeGameHours of actual production time.</summary>
        private bool EnsureCatalyst(FacilityDef f, YardOps ops)
        {
            if (f.Catalysts.Count == 0) return false;
            if (ops.CatalystHoursLeft > 0f) return true;
            foreach (var member in CatalystMembers(f).OrderByDescending(c => GetStock(f.YardId, c)))
            {
                if (GetStock(f.YardId, member) < 1f) continue;
                ConsumeImportedFirst(f.YardId, member, 1f);
                ops.CatalystHoursLeft = Math.Max(1f, RecipeProvider.Tuning.catalystLifeGameHours);
                Main.Log($"[Economy] {f.YardId} put a carload of {member} to work " +
                         $"({RecipeProvider.Tuning.catalystLifeGameHours:0}h of production).");
                EconomyHistory.Record("catalyst", f.YardId, member.ToString(), 1);
                return true;
            }
            return false;
        }

        // Stock access ----------------------------------------------------------

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
        /// machine wear, catalysts and unpaid moves all eat delivered goods before local
        /// product.</summary>
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
            // Epsilon absorbs float drift from fractional recipes; without it 3.9999962
            // room truncated to 3 and ate a real carload.
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
            // Exports into the import hub feed the incoming-ship rate (#100).
            if (_facilities.TryGetValue(yardId, out var f) && f.IsImportHub)
                _hbExportsByHour[_hourSlot] += accepted;
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
            {
                var next = Math.Max(0f, cur - amount);
                if (next <= 0f) m.Remove(cargo); else m[cargo] = next;
            }
        }

        /// <summary>Remove stock (e.g. a producer loading cars for a haul).</summary>
        public void Debit(string yardId, CargoType cargo, float amount) =>
            Consume(yardId, cargo, amount);

        /// <summary>Debug/console path: run every recipe at a station until inputs or
        /// room run out. The live economy paces batches on the clock instead.</summary>
        public void RunAllBatchesNow(string yardId)
        {
            if (!_facilities.TryGetValue(yardId, out var facility)) return;
            foreach (var recipe in facility.Recipes)
            {
                if (recipe.Inputs.Count == 0 || recipe.Outputs.Count == 0) continue;
                int guard = 0;
                while (HasInputs(yardId, recipe) && HasOutputRoom(yardId, facility, recipe) && guard++ < 1000)
                    RunBatch(facility, recipe);
            }
        }

        // Category-aware recipe plumbing (#100): an input stack naming a category is
        // satisfied by any member brand and consumed biggest-pile-first.

        private IEnumerable<CargoType> StackMembers(CargoStack s)
        {
            if (s.Category != null && CargoCategories.TryGet(s.Category, out var members))
                return members;
            return new[] { s.Cargo };
        }

        private float StackStock(string yardId, CargoStack s) =>
            StackMembers(s).Sum(c => GetStock(yardId, c));

        private float StackReserved(string yardId, CargoStack s) =>
            StackMembers(s).Sum(c => GetReserved(yardId, c));

        private void ConsumeStack(string yardId, CargoStack s)
        {
            float remaining = s.Amount;
            foreach (var member in StackMembers(s).OrderByDescending(c => GetStock(yardId, c)))
            {
                if (remaining <= 0f) break;
                float take = Math.Min(remaining, GetStock(yardId, member));
                if (take <= 0f) continue;
                ConsumeImportedFirst(yardId, member, take);
                remaining -= take;
            }
        }

        // Conversion may only draw stock that is not already promised to a taken haul.
        // Without the hard-hold subtraction, batches could eat an input pile out from
        // under a hardened reservation between accept and attach; the cars then loaded
        // real cargo the pile no longer had (minted carloads).
        private bool HasInputs(string yardId, RecipeDef r) =>
            r.Inputs.All(i => StackStock(yardId, i) - StackReserved(yardId, i) >= i.Amount);

        // Room is the shared pool (#92), and a conversion frees its input space as it
        // fills output space: the check is the NET stock change. A non-growing
        // conversion passes UNCONDITIONALLY so an over-full station can digest its way
        // back under its cap instead of deadlocking.
        private bool HasOutputRoom(string yardId, FacilityDef f, RecipeDef r)
        {
            float net = 0f;
            foreach (var o in r.Outputs) net += o.Amount;
            foreach (var i in r.Inputs) net -= i.Amount;
            if (net <= 0f) return true;
            return TotalStock(yardId) + net <= f.TotalCap + 0.001f;
        }

        private static string Describe(List<CargoStack> stacks) =>
            string.Join(", ", stacks.Select(s => $"{s.Amount:0.#} {s.Display}"));

        // Persistence: stockpiles plus machine/catalyst state. Cargo enums are stored
        // by name so the save is stable.

        [Serializable]
        private class SaveData
        {
            public int SchemaVersion;
            public Dictionary<string, Dictionary<string, float>> Stock;
            public Dictionary<string, Dictionary<string, float>> Imported;
            public Dictionary<string, Reservation> Reservations;
            // Additive since 0.44: absent in older saves, machines simply start fresh.
            public Dictionary<string, Dictionary<string, float>> MachineWear;
            public Dictionary<string, float> CatalystHours;
            public int Generation; // 0 in pre-0.44 saves: triggers the starter-machine grant
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
                MachineWear = _ops.Where(kv => kv.Value.MachineWear.Count > 0)
                    .ToDictionary(kv => kv.Key, kv => new Dictionary<string, float>(kv.Value.MachineWear)),
                CatalystHours = _ops.Where(kv => kv.Value.CatalystHoursLeft > 0f)
                    .ToDictionary(kv => kv.Key, kv => kv.Value.CatalystHoursLeft),
                Generation = CurrentGeneration,
            };
            data.SetObject(SaveKey, payload);
            Main.Log($"[Economy] saved stock for {_stock.Count} station(s).");
        }

        public void LoadFrom(SaveGameData data)
        {
            _stock = new Dictionary<string, Dictionary<CargoType, float>>(StringComparer.Ordinal);
            _imported = new Dictionary<string, Dictionary<CargoType, float>>(StringComparer.Ordinal);
            _reservations = new Dictionary<string, Reservation>(StringComparer.Ordinal);
            _ops.Clear();
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
            _generation = payload.Generation;

            // Reservations for jobs that no longer exist after the load are released by
            // the job restore pass (the surviving jobs re-register via their save entries).
            if (payload.Reservations != null)
                _reservations = payload.Reservations;

            if (payload.MachineWear != null)
                foreach (var kv in payload.MachineWear)
                    Ops(kv.Key).MachineWear = new Dictionary<string, float>(kv.Value, StringComparer.Ordinal);
            if (payload.CatalystHours != null)
                foreach (var kv in payload.CatalystHours)
                    Ops(kv.Key).CatalystHoursLeft = kv.Value;

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
            Main.Log($"[Economy] {_facilities.Count} facilities, {_stock.Count} with stock, global boost {GlobalBoost:0.00}x:");
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
