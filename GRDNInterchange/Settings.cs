using System.Collections.Generic;
using System.Linq;
using UnityModManagerNet;

namespace GRDNInterchange
{
    public class Settings : UnityModManager.ModSettings, IDrawable
    {
        // ── Hub configuration ──────────────────────────────────────────────────────
        // Comma-separated yard IDs of hub stations.  Everything else is a spoke.
        [Draw("Hub Yard IDs (comma-separated, e.g. HB,MF)")]
        public string HubYardIdsRaw = "HB,MF";

        public List<string> HubYardIds =>
            HubYardIdsRaw.Split(',')
                .Select(s => s.Trim().ToUpperInvariant())
                .Where(s => s.Length > 0)
                .ToList();

        // ── Feeder job limits ──────────────────────────────────────────────────────
        [Draw("Max cars per feeder job (1–20)")]
        public int MaxCarsPerFeeder = 6;

        // ── Block haul threshold ───────────────────────────────────────────────────
        [Draw("Block threshold — min cars at hub before a hub→hub block is generated")]
        public int BlockThresholdCars = 6;

        // ── Shunting behaviour ─────────────────────────────────────────────────────
        // When false (default) the mod auto-completes shunting jobs at spoke stations
        // so players only drive the mainline legs.
        // When true, shunting must be done manually (hardcore ops).
        [Draw("Require manual shunting at spoke stations (uncheck for auto-complete)")]
        public bool RequireManualShunting = false;

        public override void Save(UnityModManager.ModEntry modEntry) =>
            UnityModManager.ModSettings.Save<Settings>(this, modEntry);

        public void OnChange() { }
    }
}
