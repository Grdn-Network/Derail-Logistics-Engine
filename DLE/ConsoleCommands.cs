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
