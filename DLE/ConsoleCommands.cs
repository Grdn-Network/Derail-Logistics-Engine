using CommandTerminal;
using DLE.Data;
using DLE.Economy;
using UnityEngine;

namespace DLE
{
    /// <summary>
    /// In-game console recovery commands (same registration pattern as Persistent Jobs).
    /// Host or singleplayer only; both log what they did.
    /// </summary>
    public static class ConsoleCommands
    {
        [RegisterCommand("company.respawn",
            Help = "DLE: clear idle jobless empties at every economy station and respawn fresh station car pools. Recovery after derailments or car loss.",
            MinArgCount = 0, MaxArgCount = 0)]
        public static void Respawn(CommandArg[] args)
        {
            if (!Main.IsHostOrSingleplayer()) { Debug.Log("company.respawn: host or singleplayer only."); return; }
            bool started = DleDirectorBehaviour.TryRun(
                DleCarPool.Instance.RespawnStationPoolsRoutine(deleteFirst: true,
                    n => Debug.Log($"company.respawn: pools rebuilt, {n} car(s) spawned.")));
            Debug.Log(started
                ? "company.respawn: rebuilding pools, spreading spawns across frames..."
                : "company.respawn: world not ready yet, try again once loaded.");
        }

        [RegisterCommand("company.wake",
            Help = "DLE: respawn every dormant pool car regardless of distance (#141). Escape hatch and A/B test lever.",
            MinArgCount = 0, MaxArgCount = 0)]
        public static void Wake(CommandArg[] args)
        {
            if (!Main.IsHostOrSingleplayer()) { Debug.Log("company.wake: host or singleplayer only."); return; }
            int dormant = DleCarPool.Instance.DormantCount;
            if (dormant == 0) { Debug.Log("company.wake: nothing is dormant."); return; }
            bool started = DleDirectorBehaviour.TryRun(CarDormancy.WakeAllRoutine());
            Debug.Log(started
                ? $"company.wake: respawning {dormant} dormant car(s)..."
                : "company.wake: world not ready yet, try again once loaded.");
        }

        [RegisterCommand("company.lag",
            Help = "DLE: lag meter dump (frame percentiles, hitches, GC, heap, live vs dormant cars, board handler cost). 'company.lag watch' toggles a 10 second periodic log line.",
            MinArgCount = 0, MaxArgCount = 1)]
        public static void Lag(CommandArg[] args)
        {
            if (args.Length > 0 && string.Equals(args[0].String, "watch", System.StringComparison.OrdinalIgnoreCase))
            {
                PerfMeter.Watch = !PerfMeter.Watch;
                Debug.Log($"company.lag: watch {(PerfMeter.Watch ? "ON, one line every 10s in the log" : "off")}.");
                return;
            }
            var report = PerfMeter.FullReport();
            Debug.Log(report);
            Main.LogAlways(report);
        }

        [RegisterCommand("company.orders",
            Help = "DLE: dump the vanilla taken-order list against DLE's job definitions. Run this the moment the validator says DENIED for concurrent orders (#216) and send the log.",
            MinArgCount = 0, MaxArgCount = 0)]
        public static void Orders(CommandArg[] args)
        {
            var jm = DV.Utils.SingletonBehaviour<DV.Logic.Job.JobsManager>.Instance;
            if (jm == null) { Debug.Log("company.orders: world not ready."); return; }
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[Orders] vanilla currentJobs (what the concurrent limit and reprint read): {jm.currentJobs.Count}");
            foreach (var j in jm.currentJobs)
            {
                if (j == null) { sb.AppendLine("  - (null entry)"); continue; }
                string defNote = "no DLE def";
                if (Jobs.StaticDirectHaulJobDefinition.jobDefinitions.TryGetValue(j.ID, out var d))
                    defNote = d.LiveJob == null ? "def has no live job"
                        : ReferenceEquals(d.LiveJob, j) ? "SAME instance as the def's job"
                        : $"DIFFERENT instance than the def's job (def state {d.LiveJob.State})";
                sb.AppendLine($"  - {j.ID} state={j.State} ({defNote})");
            }
            sb.AppendLine($"[Orders] DLE job definitions: {Jobs.StaticDirectHaulJobDefinition.jobDefinitions.Count}");
            foreach (var kv in Jobs.StaticDirectHaulJobDefinition.jobDefinitions)
                sb.AppendLine($"  - {kv.Key} live={(kv.Value.LiveJob == null ? "none" : kv.Value.LiveJob.State.ToString())}");
            var report = sb.ToString();
            Debug.Log(report);
            Main.LogAlways(report);
        }

        [RegisterCommand("company.resupply",
            Help = "DLE: wipe all facility stockpiles back to the starting stock values.",
            MinArgCount = 0, MaxArgCount = 0)]
        public static void Resupply(CommandArg[] args)
        {
            if (!Main.IsHostOrSingleplayer()) { Debug.Log("company.resupply: host or singleplayer only."); return; }
            EconomyState.Instance.ResetToDefault(RecipeProvider.Tuning.initialStock);
            Debug.Log("company.resupply: stockpiles reset to starting stock.");
        }

        [RegisterCommand("company.haul",
            Help = "DLE: generate one haul from current stock, exactly like a director tick.",
            MinArgCount = 0, MaxArgCount = 0)]
        public static void Haul(CommandArg[] args)
        {
            if (!Main.IsHostOrSingleplayer()) { Debug.Log("company.haul: host or singleplayer only."); return; }
            Debug.Log(EconomyDirector.GenerateOne()
                ? "company.haul: haul created; see the board."
                : "company.haul: nothing to haul (stock, room or booklet caps).");
        }

        [RegisterCommand("company.dump",
            Help = "DLE debug: dump every facility's stock and recipes to the log.",
            MinArgCount = 0, MaxArgCount = 0)]
        public static void Dump(CommandArg[] args)
        {
            EconomyState.Instance.DumpToLog();
            Debug.Log("company.dump: economy written to the log.");
        }

        [RegisterCommand("company.testdelivery",
            Help = "DLE debug: simulate a delivery with no train to exercise the economy.",
            MinArgCount = 0, MaxArgCount = 0)]
        public static void TestDelivery(CommandArg[] args)
        {
            if (!Main.IsHostOrSingleplayer()) { Debug.Log("company.testdelivery: host or singleplayer only."); return; }
            Jobs.DebugEconomy.SimulateDelivery();
            Debug.Log("company.testdelivery: done; see the log.");
        }
    }
}
