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
    /// applies the optional economy.json overlay. Military yards are excluded.
    /// </summary>
    public static class RecipeProvider
    {
        // Military yards stay out of the economy for 0.1 (configurable later).
        public static readonly HashSet<string> ExcludedYards =
            new HashSet<string>(StringComparer.Ordinal) { "MB", "HMB", "MFMB" };

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
                // Hub storage is CONFIG, not code: the default economy.json grants it to
                // the designated hubs (HB largest, GF has the storage buildings);
                // SM and CW join when contracts can gate their use.

                // Default recipe: consume one of each input to make one of each output.
                // Sources (no inputs) and sinks (no outputs) simply hold stock in 0.1.
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
                // stray comma reverted maxPoolCars, tick rates and the rest to defaults on
                // reload while the log claimed the file was merely "ignored".
                Main.LogAlways($"[Economy] economy.json failed to parse, keeping the previous settings: {ex.Message}");
                ApplyExclusions(facilities);
                return;
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
                    if (so.recipes != null)
                        facility.Recipes = so.recipes.Select(ToRecipe).Where(r => r != null).ToList();
                    facility.Role = ParseRole(so.role, facility.Role);
                    if (so.remoteLoad.HasValue) facility.RemoteLoad = so.remoteLoad.Value;
                    if (so.remoteUnload.HasValue) facility.RemoteUnload = so.remoteUnload.Value;
                    if (so.remoteSecondsPerCar.HasValue) facility.RemoteSecondsPerCar = so.remoteSecondsPerCar.Value;
                    if (so.source.HasValue) facility.IsSource = so.source.Value;
                    if (so.boosters != null)
                        facility.Boosters = so.boosters.Select(ToBooster).Where(b => b != null && b.Cargo.Count > 0).ToList();
                }

            // A source produces on the clock with no required inputs: whatever recipe was
            // auto-derived from its cargo groups (tools in, ore out) would gate production
            // on deliveries, so it goes. Its inputs act through Boosters instead, and its
            // DEMAND narrows to the booster cargos: the raw derived inputs include vanilla
            // supply flavor (mines "accept" DumpTrucks) that nothing at a source consumes,
            // and the director was routing real hauls of it there (DumpTrucks to CMS)
            // while the board's map correctly showed no such need.
            foreach (var f in facilities.Values)
            {
                if (!f.IsSource) continue;
                if (f.Recipes.Count > 0) f.Recipes.Clear();
                f.Inputs = f.Boosters.SelectMany(b => b.Cargo).Distinct().ToList();
            }

            if (_legacyCapsSeen)
            {
                Main.LogAlways("[Economy] economy.json still sets per-cargo 'caps': storage is now ONE shared pool per station (#92), so those entries are ignored. Set 'totalCap' instead.");
                _legacyCapsSeen = false;
            }
            if (_legacyDefaultCapSeen)
            {
                Main.LogAlways("[Economy] economy.json still uses 'defaultCap': it now counts as HALF the station total (doubled on read, matching the storage conversion). Rename it to 'totalCap' with the doubled value to silence this.");
                _legacyDefaultCapSeen = false;
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

        /// <summary>
        /// Strip excluded cargo (settings.excludedCargos) from every facility: outputs,
        /// inputs, caps and recipes. A recipe left without inputs or outputs is removed;
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
                foreach (var r in f.Recipes)
                {
                    r.Inputs.RemoveAll(s => excluded.Contains(s.Cargo));
                    r.Outputs.RemoveAll(s => excluded.Contains(s.Cargo));
                }
                f.Recipes.RemoveAll(r => r.Inputs.Count == 0 || r.Outputs.Count == 0);
            }
        }

        private static bool _legacyCapsSeen;
        private static bool _legacyDefaultCapSeen;

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

        private static BoosterDef ToBooster(BoosterOverlay bo)
        {
            if (bo?.cargo == null) return null;
            var def = new BoosterDef();
            foreach (var name in bo.cargo)
                if (TryCargo(name, out var cargo)) def.Cargo.Add(cargo);
            if (bo.speedup.HasValue) def.Speedup = Math.Max(1f, bo.speedup.Value);
            if (bo.consumedPerCarload.HasValue) def.ConsumedPerCarload = Math.Max(0f, bo.consumedPerCarload.Value);
            return def;
        }

        private static RecipeDef ToRecipe(RecipeOverlay ro)
        {
            var recipe = new RecipeDef();
            if (ro.inputs != null)
                foreach (var i in ro.inputs)
                    if (TryCargo(i.Key, out var cargo)) recipe.Inputs.Add(new CargoStack(cargo, i.Value));
            if (ro.outputs != null)
                foreach (var o in ro.outputs)
                    if (TryCargo(o.Key, out var cargo)) recipe.Outputs.Add(new CargoStack(cargo, o.Value));
            return recipe;
        }

        private static bool TryCargo(string name, out CargoType cargo)
        {
            if (Enum.TryParse(name, out cargo)) return true;
            Main.LogAlways($"[Economy] unknown cargo '{name}' in economy.json, skipped. A typo here silently drops the recipe/booster entry.");
            return false;
        }
    }
}
