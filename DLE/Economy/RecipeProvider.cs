using DV.ThingTypes;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DLE.Economy
{
    /// <summary>
    /// Builds each freight station's FacilityDef from the game's own cargo groups, then
    /// applies the economy.json overlay: recipes, required machines, catalysts, storage
    /// totals and the living-demand flags (#100). Military yards are excluded.
    /// </summary>
    public static class RecipeProvider
    {
        // Military yards stay out of the economy for 0.1 (configurable later).
        public static readonly HashSet<string> ExcludedYards =
            new HashSet<string>(StringComparer.Ordinal) { "MB", "HMB", "MFMB" };

        // The shipped default config's version. A user economy.json below this predates
        // the machines/catalysts economy and is auto-replaced (with a .bak) because the
        // old recipe set cannot express the new mechanics.
        private const int CurrentConfigVersion = 2;

        public static Dictionary<string, FacilityDef> BuildFacilities()
        {
            var result = new Dictionary<string, FacilityDef>(StringComparer.Ordinal);

            foreach (var sc in StationController.allStations)
            {
                var yardId = sc.stationInfo.YardID;
                if (ExcludedYards.Contains(yardId)) continue;
                var ruleset = sc.proceduralJobsRuleset;
                if (ruleset == null) continue;

                var inputs = CargoesOf(ruleset.inputCargoGroups);
                var outputs = CargoesOf(ruleset.outputCargoGroups);
                if (inputs.Count == 0 && outputs.Count == 0) continue;

                var facility = new FacilityDef
                {
                    YardId = yardId,
                    Inputs = inputs,
                    Outputs = outputs,
                };

                // The vanilla route table, straight from the same objects the game
                // routes with: each output group names its destination stations.
                if (ruleset.outputCargoGroups != null)
                {
                    foreach (var group in ruleset.outputCargoGroups)
                    {
                        if (group?.cargoTypes == null || group.stations == null) continue;
                        var dests = group.stations
                            .Where(s => s?.stationInfo?.YardID != null)
                            .Select(s => s.stationInfo.YardID)
                            .Where(y => !ExcludedYards.Contains(y) && y != yardId)
                            .ToList();
                        if (dests.Count == 0) continue;
                        foreach (var cargo in group.cargoTypes)
                        {
                            if (!facility.RouteMap.TryGetValue(cargo, out var set))
                                facility.RouteMap[cargo] = set = new HashSet<string>(StringComparer.Ordinal);
                            foreach (var d in dests) set.Add(d);
                        }
                    }
                }

                // Default recipe: consume one of each input to make one of each output.
                // The shipped economy.json replaces these with real recipes; a station
                // it does not cover keeps this derived placeholder.
                if (outputs.Count > 0 && inputs.Count > 0)
                {
                    facility.Recipes.Add(new RecipeDef
                    {
                        Inputs = inputs.Select(c => new CargoStack(c, 1f)).ToList(),
                        Outputs = outputs.Select(c => new CargoStack(c, 1f)).ToList(),
                    });
                }

                result[yardId] = facility;
            }

            return result;
        }

        private static List<CargoType> CargoesOf(List<CargoGroup> groups)
        {
            var set = new List<CargoType>();
            if (groups == null) return set;
            foreach (var g in groups)
                if (g?.cargoTypes != null)
                    foreach (var c in g.cargoTypes)
                        if (!set.Contains(c)) set.Add(c);
            return set;
        }

        /// <summary>World tuning from economy.json's "settings" block; defaults when the
        /// file or block is absent. Refreshed by every ApplyOverlay (load and reload).</summary>
        public static TuningDef Tuning { get; private set; } = new TuningDef();

        public static void ApplyOverlay(Dictionary<string, FacilityDef> facilities, string modPath)
        {
            var path = Path.Combine(modPath, "economy.json");
            if (!File.Exists(path)) { Tuning = new TuningDef(); ApplyExclusions(facilities); return; }

            EconomyOverlay overlay;
            try
            {
                overlay = JsonConvert.DeserializeObject<EconomyOverlay>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                // Keep the last-good tuning: resetting it before the parse meant a single
                // stray comma reverted every rate to defaults on reload while the log
                // claimed the file was merely "ignored".
                Main.LogAlways($"[Economy] economy.json failed to parse, keeping the previous settings: {ex.Message}");
                ApplyExclusions(facilities);
                return;
            }

            // Config migration (#100): a pre-0.44 file cannot describe machines or
            // catalysts, so its recipes would leave the new economy half-dead. Back the
            // user file up and start from the shipped default; hand edits live in .bak.
            if (overlay != null && overlay.configVersion < CurrentConfigVersion)
            {
                var defaultPath = Path.Combine(modPath, "economy.default.json");
                if (File.Exists(defaultPath))
                {
                    try
                    {
                        File.Copy(path, path + ".bak", overwrite: true);
                        File.Copy(defaultPath, path, overwrite: true);
                        overlay = JsonConvert.DeserializeObject<EconomyOverlay>(File.ReadAllText(path));
                        Main.LogAlways("[Economy] economy.json predates the 0.44 economy; replaced with the new default. Your old file is economy.json.bak; port custom edits into the new shape.");
                    }
                    catch (Exception ex)
                    {
                        Main.LogAlways($"[Economy] could not migrate economy.json ({ex.Message}); running with the old file. Machines and catalysts stay off until it carries configVersion {CurrentConfigVersion}.");
                    }
                }
            }

            if (overlay == null) { Tuning = new TuningDef(); ApplyExclusions(facilities); return; }
            Tuning = overlay.settings ?? new TuningDef();
            if (overlay.stations == null && overlay.defaults == null) { ApplyExclusions(facilities); return; }

            // Global defaults first: the baseline for every facility.
            if (overlay.defaults != null)
                foreach (var facility in facilities.Values)
                    ApplyDefaults(facility, overlay.defaults);

            if (overlay.stations != null)
                foreach (var kv in overlay.stations)
                {
                    if (!facilities.TryGetValue(kv.Key, out var facility))
                    {
                        // No auto-derived facility for this yard id: usually a typo. Say so,
                        // because the intended real station silently keeps its defaults.
                        Main.LogAlways($"[Economy] economy.json station '{kv.Key}' has no auto-derived facility (typo'd yard id?); its overrides apply to an inert entry.");
                        facility = new FacilityDef { YardId = kv.Key };
                        ApplyDefaults(facility, overlay.defaults);
                        facilities[kv.Key] = facility;
                    }
                    var so = kv.Value;
                    if (so.totalCap.HasValue) facility.TotalCap = so.totalCap.Value;
                    else if (so.defaultCap.HasValue) { facility.TotalCap = so.defaultCap.Value * 2f; _legacyDefaultCapSeen = true; }
                    if (so.caps != null) _legacyCapsSeen = true;
                    if (so.boosters != null) _legacyBoostersSeen = true;
                    if (so.recipes != null)
                        facility.Recipes = so.recipes.Select(ToRecipe).Where(r => r != null).ToList();
                    facility.Role = ParseRole(so.role, facility.Role);
                    if (so.remoteLoad.HasValue) facility.RemoteLoad = so.remoteLoad.Value;
                    if (so.remoteUnload.HasValue) facility.RemoteUnload = so.remoteUnload.Value;
                    if (so.remoteSecondsPerCar.HasValue) facility.RemoteSecondsPerCar = so.remoteSecondsPerCar.Value;
                    if (so.source.HasValue) facility.IsSource = so.source.Value;
                    if (so.consumesStock.HasValue) facility.ConsumesStock = so.consumesStock.Value;
                    if (so.emitsScrap.HasValue) facility.EmitsScrap = so.emitsScrap.Value;
                    if (so.importHub.HasValue) facility.IsImportHub = so.importHub.Value;
                    if (so.machines != null)
                        facility.Machines = so.machines
                            .Where(name => TryCargo(name, out _))
                            .Select(name => { TryCargo(name, out var c); return c; })
                            .ToList();
                    if (so.catalysts != null)
                        facility.Catalysts = so.catalysts
                            .Where(name => CargoCategories.TryGet(name, out _) || TryCargo(name, out _))
                            .ToList();
                }

            if (_legacyCapsSeen)
            {
                Main.LogAlways("[Economy] economy.json still sets per-cargo 'caps': storage is ONE shared pool per station (#92), so those entries are ignored. Set 'totalCap' instead.");
                _legacyCapsSeen = false;
            }
            if (_legacyDefaultCapSeen)
            {
                Main.LogAlways("[Economy] economy.json still uses 'defaultCap': it now counts as HALF the station total (doubled on read, matching the storage conversion). Rename it to 'totalCap' with the doubled value to silence this.");
                _legacyDefaultCapSeen = false;
            }
            if (_legacyBoostersSeen)
            {
                Main.LogAlways("[Economy] economy.json still defines 'boosters': the 0.44 economy replaced them with 'machines' (required equipment) and 'catalysts'. The entries are ignored.");
                _legacyBoostersSeen = false;
            }

            foreach (var f in facilities.Values)
            {
                // A source produces on the clock: whatever recipe was auto-derived from
                // its cargo groups would gate production on deliveries, so it goes. Its
                // DEMAND narrows to what it actually uses: required machines plus
                // catalyst cargos (the raw derived inputs carry vanilla flavor nothing
                // at a source consumes, which mis-routed real hauls).
                if (f.IsSource)
                {
                    if (f.Recipes.Count > 0) f.Recipes.Clear();
                    f.Inputs = f.Machines
                        .Concat(CatalystCargos(f))
                        .Distinct()
                        .ToList();
                }
                // A consumer eats whatever arrives; a derived one-of-all-34 recipe is
                // noise that can never run. Overlay recipes (if any) survive.
                else if (f.ConsumesStock && !HasOverlayRecipes(overlay, f.YardId))
                {
                    f.Recipes.Clear();
                }
                // The import hub's derived 37-in-33-out placeholder likewise never runs;
                // its real behavior is the export-scaled import rate.
                if (f.IsImportHub) f.Recipes.Clear();

                // Machine and catalyst cargos must register as demand everywhere they
                // are used, or the director never routes replacement hauls there.
                foreach (var needed in f.Machines.Concat(CatalystCargos(f)))
                    if (!f.Inputs.Contains(needed))
                        f.Inputs.Add(needed);
            }

            // Warn when a role contradicts the station's derived economy, so a config
            // typo that silently strands stock or starves inputs is visible in the log.
            foreach (var f in facilities.Values)
            {
                if (f.Role == ServiceRole.Unload && f.Outputs.Count > 0)
                    Main.LogAlways($"[Economy] {f.YardId}: role=unload but it produces " +
                                   $"{string.Join(", ", f.Outputs)}; those never ship.");
                if (f.Role == ServiceRole.Load && f.Inputs.Count > 0)
                    Main.LogAlways($"[Economy] {f.YardId}: role=load but it consumes " +
                                   $"{string.Join(", ", f.Inputs)}; those never arrive.");
            }
            ApplyExclusions(facilities);
            Main.Log($"[Economy] applied economy.json overlay ({overlay.stations?.Count ?? 0} station(s)).");
        }

        private static bool HasOverlayRecipes(EconomyOverlay overlay, string yardId) =>
            overlay.stations != null &&
            overlay.stations.TryGetValue(yardId, out var so) &&
            so.recipes != null && so.recipes.Count > 0;

        private static IEnumerable<CargoType> CatalystCargos(FacilityDef f)
        {
            foreach (var name in f.Catalysts)
            {
                if (CargoCategories.TryGet(name, out var members))
                    foreach (var m in members) yield return m;
                else if (Enum.TryParse<CargoType>(name, out var cargo))
                    yield return cargo;
            }
        }

        /// <summary>
        /// Strip excluded cargo (settings.excludedCargos) from every facility: outputs,
        /// inputs and recipes. A recipe left without inputs or outputs is removed;
        /// the facility then behaves as the source or sink it really is.
        /// </summary>
        private static void ApplyExclusions(Dictionary<string, FacilityDef> facilities)
        {
            var excluded = new HashSet<CargoType>();
            foreach (var name in Tuning.excludedCargos ?? new List<string>())
                if (TryCargo(name, out var c)) excluded.Add(c);
            if (excluded.Count == 0) return;
            foreach (var f in facilities.Values)
            {
                f.Outputs.RemoveAll(excluded.Contains);
                f.Inputs.RemoveAll(excluded.Contains);
                foreach (var key in f.RouteMap.Keys.Where(excluded.Contains).ToList())
                    f.RouteMap.Remove(key);
                foreach (var r in f.Recipes)
                {
                    r.Inputs.RemoveAll(s => s.Category == null && excluded.Contains(s.Cargo));
                    r.Outputs.RemoveAll(s => excluded.Contains(s.Cargo));
                }
                f.Recipes.RemoveAll(r => r.Inputs.Count == 0 || r.Outputs.Count == 0);
            }
        }

        private static bool _legacyCapsSeen;
        private static bool _legacyDefaultCapSeen;
        private static bool _legacyBoostersSeen;

        private static void ApplyDefaults(FacilityDef f, OverlayDefaults d)
        {
            if (d == null) return;
            f.Role = ParseRole(d.role, f.Role);
            if (d.remoteLoad.HasValue) f.RemoteLoad = d.remoteLoad.Value;
            if (d.remoteUnload.HasValue) f.RemoteUnload = d.remoteUnload.Value;
            if (d.remoteSecondsPerCar.HasValue) f.RemoteSecondsPerCar = d.remoteSecondsPerCar.Value;
            if (d.totalCap.HasValue) f.TotalCap = d.totalCap.Value;
            else if (d.defaultCap.HasValue) { f.TotalCap = d.defaultCap.Value * 2f; _legacyDefaultCapSeen = true; }
        }

        private static ServiceRole ParseRole(string s, ServiceRole fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            switch (s.Trim().ToLowerInvariant())
            {
                case "load": return ServiceRole.Load;
                case "unload": return ServiceRole.Unload;
                case "both": return ServiceRole.Both;
                default:
                    Main.LogAlways($"[Economy] unknown role '{s}' in economy.json, using {fallback}. Fix the typo or the override is silently ignored.");
                    return fallback;
            }
        }

        private static RecipeDef ToRecipe(RecipeOverlay ro)
        {
            var recipe = new RecipeDef();
            if (ro.inputs != null)
                foreach (var i in ro.inputs)
                {
                    // Inputs accept category names (Tools, Electronics, Clothing,
                    // Chemicals, Gases): any member brand satisfies and is consumed.
                    if (CargoCategories.TryGet(i.Key, out _))
                        recipe.Inputs.Add(new CargoStack(i.Key, i.Value));
                    else if (TryCargo(i.Key, out var cargo))
                        recipe.Inputs.Add(new CargoStack(cargo, i.Value));
                }
            if (ro.outputs != null)
                foreach (var o in ro.outputs)
                {
                    // Outputs stay concrete: production credits the vanilla-true
                    // domestic brand; the category applies when goods are USED.
                    if (CargoCategories.TryGet(o.Key, out _))
                        Main.LogAlways($"[Economy] recipe output '{o.Key}' is a category; outputs must be a concrete cargo. Entry skipped.");
                    else if (TryCargo(o.Key, out var cargo))
                        recipe.Outputs.Add(new CargoStack(cargo, o.Value));
                }
            return recipe;
        }

        private static bool TryCargo(string name, out CargoType cargo)
        {
            if (Enum.TryParse(name, out cargo)) return true;
            Main.LogAlways($"[Economy] unknown cargo '{name}' in economy.json, skipped. A typo here silently drops the entry.");
            return false;
        }
    }
}
