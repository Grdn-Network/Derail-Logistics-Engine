using DLE.Jobs;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DLE.Economy
{
    /// <summary>
    /// The order spike (Desk v10): what each factory is short on toward its next
    /// batch, derived on demand from stock, recipes, live hauls and shippable supply.
    /// Nothing is stored and no balance changes here: a slip is a reading, not an
    /// event. Cities (pure consumers, no recipes) are not listed yet: their par level
    /// is a balance question the owner rules on when the spike gets wired. The phone
    /// and rush orders (double pay) are gameplay and wait for the same ruling.
    /// </summary>
    public static class OrderBook
    {
        public static object OrdersPayload()
        {
            var econ = EconomyState.Instance;
            var options = EconomyDirector.GetOptions();
            var rows = new List<(float shortfall, object row)>();
            foreach (var f in econ.Facilities.Values)
            {
                if (f.Recipes == null || f.Recipes.Count == 0) continue;
                // Deepest need per family across recipes: two recipes sharing an
                // input raise one slip, not two.
                var wants = new Dictionary<string, (CargoStack stack, float want)>(StringComparer.Ordinal);
                foreach (var r in f.Recipes)
                    foreach (var i in r.Inputs)
                    {
                        var key = i.Category ?? CargoCategories.DisplayName(i.Cargo);
                        if (!wants.TryGetValue(key, out var w) || i.Amount > w.want) wants[key] = (i, i.Amount);
                    }
                foreach (var kv in wants)
                {
                    float have = econ.AvailableOf(f.YardId, kv.Value.stack);
                    if (have >= kv.Value.want) continue;
                    int enRoute = EnRouteCars(f.YardId, kv.Key);
                    rows.Add((kv.Value.want - have - enRoute, new
                    {
                        dest = f.YardId,
                        cargo = kv.Key,
                        have = Math.Round(have, 1),
                        want = Math.Round(kv.Value.want, 1),
                        // Carloads already rolling or on open paper toward this slip:
                        // the bar's drafted segment.
                        enRouteCars = enRoute,
                        // Who could fill it right now, with how much standing stock:
                        // the FROM chips. Empty means nobody can: scarcity is the point.
                        suppliers = options
                            .Where(o => o.Consumers.Contains(f.YardId)
                                        && CargoCategories.DisplayName(o.Cargo) == kv.Key)
                            .Select(o => new { yard = o.Origin, stock = (int)o.Stock })
                            .ToList(),
                    }));
                }
            }
            return rows.OrderByDescending(r => r.shortfall).Select(r => r.row).ToList();
        }

        /// <summary>Cars on live DLE hauls headed to this yard with this cargo family.
        /// Carless jobs (players bring empties) count their planned size; logi moves
        /// carry no cargo and do not count.</summary>
        private static int EnRouteCars(string destYard, string display)
        {
            int n = 0;
            foreach (var kv in StaticDirectHaulJobDefinition.jobDefinitions)
            {
                var def = kv.Value;
                if (def?.chainData == null) continue;
                if (!string.Equals(def.chainData.chainDestinationYardId, destYard, StringComparison.OrdinalIgnoreCase)) continue;
                if (def.manifest != null && def.manifest.Count > 0)
                {
                    foreach (var l in def.manifest)
                        if (CargoCategories.DisplayName(l.Cargo) == display)
                            n += l.CarIds?.Count ?? 0;
                }
                else if (CargoCategories.DisplayName(def.transportedCargo) == display)
                {
                    n += Math.Max(def.carsToTransport?.Count ?? 0, def.plannedCarCount);
                }
            }
            return n;
        }
    }
}
