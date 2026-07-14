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

        public static void ApplyOverlay(Dictionary<string, FacilityDef> facilities, string modPath)
        {
            var path = Path.Combine(modPath, "economy.json");
            if (!File.Exists(path)) return;

            EconomyOverlay overlay;
            try
            {
                overlay = JsonConvert.DeserializeObject<EconomyOverlay>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Main.LogAlways($"[Economy] economy.json failed to parse, ignoring it: {ex.Message}");
                return;
            }
            if (overlay == null || (overlay.stations == null && overlay.defaults == null)) return;

            // Global defaults first: the baseline for every facility.
            if (overlay.defaults != null)
                foreach (var facility in facilities.Values)
                    ApplyDefaults(facility, overlay.defaults);

            if (overlay.stations != null)
                foreach (var kv in overlay.stations)
                {
                    if (!facilities.TryGetValue(kv.Key, out var facility))
                    {
                        facility = new FacilityDef { YardId = kv.Key };
                        ApplyDefaults(facility, overlay.defaults);
                        facilities[kv.Key] = facility;
                    }
                    var so = kv.Value;
                    if (so.defaultCap.HasValue) facility.DefaultCap = so.defaultCap.Value;
                    if (so.caps != null)
                        foreach (var c in so.caps)
                            if (TryCargo(c.Key, out var cargo)) facility.StorageCaps[cargo] = c.Value;
                    if (so.recipes != null)
                        facility.Recipes = so.recipes.Select(ToRecipe).Where(r => r != null).ToList();
                    facility.Role = ParseRole(so.role, facility.Role);
                    if (so.remoteLoad.HasValue) facility.RemoteLoad = so.remoteLoad.Value;
                    if (so.remoteUnload.HasValue) facility.RemoteUnload = so.remoteUnload.Value;
                    if (so.remoteSecondsPerCar.HasValue) facility.RemoteSecondsPerCar = so.remoteSecondsPerCar.Value;
                }
            Main.Log($"[Economy] applied economy.json overlay ({overlay.stations?.Count ?? 0} station(s)).");
        }

        private static void ApplyDefaults(FacilityDef f, OverlayDefaults d)
        {
            if (d == null) return;
            f.Role = ParseRole(d.role, f.Role);
            if (d.remoteLoad.HasValue) f.RemoteLoad = d.remoteLoad.Value;
            if (d.remoteUnload.HasValue) f.RemoteUnload = d.remoteUnload.Value;
            if (d.remoteSecondsPerCar.HasValue) f.RemoteSecondsPerCar = d.remoteSecondsPerCar.Value;
            if (d.defaultCap.HasValue) f.DefaultCap = d.defaultCap.Value;
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
                    Main.Log($"[Economy] unknown role '{s}' in economy.json, using {fallback}.");
                    return fallback;
            }
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
            Main.Log($"[Economy] unknown cargo '{name}' in economy.json, skipped.");
            return false;
        }
    }
}
