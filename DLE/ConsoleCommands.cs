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
            int spawned = DleCarPool.Instance.RespawnStationPools(deleteFirst: true);
            Debug.Log($"company.respawn: pools rebuilt, {spawned} car(s) spawned.");
        }

        [RegisterCommand("company.clear",
            Help = "DLE: delete every idle jobless empty car in all yards with NO respawn. Flood recovery; leaves loaded, player-owned and job-assigned cars untouched. Save the game afterwards to persist.",
            MinArgCount = 0, MaxArgCount = 0)]
        public static void Clear(CommandArg[] args)
        {
            if (!Main.IsHostOrSingleplayer()) { Debug.Log("company.clear: host or singleplayer only."); return; }
            int deleted = DleCarPool.Instance.ClearIdleEmpties();
            Debug.Log($"company.clear: deleted {deleted} idle jobless empty car(s), no respawn.");
        }

        [RegisterCommand("company.resupply",
            Help = "DLE: wipe all facility stockpiles back to the starting stock values.",
            MinArgCount = 0, MaxArgCount = 0)]
        public static void Resupply(CommandArg[] args)
        {
            if (!Main.IsHostOrSingleplayer()) { Debug.Log("company.resupply: host or singleplayer only."); return; }
            EconomyState.Instance.ResetToDefault(Main.Settings?.InitialStock ?? 6);
            Debug.Log("company.resupply: stockpiles reset to starting stock.");
        }
    }
}
