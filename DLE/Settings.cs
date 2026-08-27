using UnityModManagerNet;

namespace DLE
{
    /// <summary>
    /// Host preferences only. World tuning (starting stock, production pace, pool size
    /// and packing, director tick) lives in economy.json's "settings" block; haul sizing
    /// for auto-generation is fixed in EconomyDirector, and dispatcher-picked hauls are
    /// never bound by it.
    /// </summary>
    public class Settings : UnityModManager.ModSettings, IDrawable
    {
        // The per-station number IS the cap (owner ruling 2026-08-27, #217): a map-wide
        // total silently stalled all generation once ~60 untaken orders piled up at
        // singleplayer pace, and nothing anywhere said why. Priority orders that bump
        // stale paper come later.
        [Draw("Available booklets per station")]
        public int MaxOpenBookletsPerStation = 10;

        // The password IS the switch: set one and the board serves on the network
        // (LAN, port-forward, or a tunnel); leave it blank and the board is host-only.
        [Draw("Board password (set one to serve the board on the network)")]
        public string BoardPassword = "";

        [Draw("Verbose logging")]
        public bool VerboseLogging = false;

        public override void Save(UnityModManager.ModEntry modEntry) =>
            UnityModManager.ModSettings.Save<Settings>(this, modEntry);

        public void OnChange() { }
    }
}
