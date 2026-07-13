using DLE.Economy;
using System;
using System.Linq;

namespace DLE.Jobs
{
    /// <summary>
    /// Phase 2 test harness. Exercises the economy without a train: it credits a factory's
    /// recipe inputs directly and lets conversion run, so deliveries and production can be
    /// verified from the log in seconds.
    /// </summary>
    public static class DebugEconomy
    {
        public static void SimulateDelivery()
        {
            if (!Main.IsHostOrSingleplayer())
            {
                Main.Log("[Economy debug] host or singleplayer only.");
                return;
            }

            var facility = EconomyState.Instance.Facilities.Values
                .FirstOrDefault(f => f.Recipes.Any(r => r.Inputs.Count > 0 && r.Outputs.Count > 0));
            if (facility == null)
            {
                Main.Log("[Economy debug] no facility with an input+output recipe was derived.");
                return;
            }

            var recipe = facility.Recipes.First(r => r.Inputs.Count > 0 && r.Outputs.Count > 0);
            Main.Log($"[Economy debug] simulating input delivery to {facility.YardId} " +
                     $"([{string.Join(", ", recipe.Inputs.Select(i => i.Cargo))}] -> " +
                     $"[{string.Join(", ", recipe.Outputs.Select(o => o.Cargo))}]).");

            foreach (var input in recipe.Inputs)
                EconomyState.Instance.OnDelivered(facility.YardId, input.Cargo, (int)Math.Ceiling(input.Amount));

            EconomyState.Instance.DumpToLog();
        }
    }
}
