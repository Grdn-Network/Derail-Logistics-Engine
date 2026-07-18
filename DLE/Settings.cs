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
        [Draw("Available booklets per station")]
        public int MaxOpenBookletsPerStation = 10;

        [Draw("Available booklets on the whole map")]
        public int MaxOpenBookletsTotal = 60;

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
