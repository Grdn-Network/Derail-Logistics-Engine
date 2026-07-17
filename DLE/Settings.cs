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

        [Draw("Serve the dispatch board on the network (password required)")]
        public bool RemoteBoard = false;

        [Draw("Board password (blank keeps the board host-only)")]
        public string BoardPassword = "";

        [Draw("Verbose logging")]
        public bool VerboseLogging = false;

        public override void Save(UnityModManager.ModEntry modEntry) =>
            UnityModManager.ModSettings.Save<Settings>(this, modEntry);

        public void OnChange() { }
    }
}
