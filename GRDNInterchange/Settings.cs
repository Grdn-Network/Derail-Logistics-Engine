using UnityModManagerNet;

namespace GRDNInterchange
{
    public class Settings : UnityModManager.ModSettings, IDrawable
    {
        // ── Feeder job limits ──────────────────────────────────────────────────────
        [Draw("Max cars per feeder job (1–20)")]
        public int MaxCarsPerFeeder = 6;

        [Draw("Max interchange cars per spoke station (0 = unlimited)")]
        public int MaxCarsPerStation = 6;

        // ── Block haul threshold ───────────────────────────────────────────────────
        [Draw("Block threshold — min SortAtHub cars before hub→hub block spawns")]
        public int BlockThresholdCars = 3;

        // ── Shunting behaviour ─────────────────────────────────────────────────────
        [Draw("Hub shunting — players take sort jobs at the hub (HB-SO-XX)")]
        public bool HubShunting = true;

        [Draw("Global shunting — allow vanilla SL/SU at spoke stations")]
        public bool GlobalShunting = false;

        public override void Save(UnityModManager.ModEntry modEntry) =>
            UnityModManager.ModSettings.Save<Settings>(this, modEntry);

        public void OnChange() { }
    }
}
