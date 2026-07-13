using UnityModManagerNet;

namespace DLE
{
    public class Settings : UnityModManager.ModSettings, IDrawable
    {
        [Draw("Initial stock per output cargo (carloads)")]
        public int InitialStock = 6;

        [Draw("Minimum carloads before a haul is generated")]
        public int MinShipCarloads = 3;

        [Draw("Maximum cars per haul")]
        public int MaxCarsPerHaul = 6;

        // Mode B (finite empty cars, player loads on demand) is the 0.5 target.
        // Off for 0.1: jobs spawn pre-loaded. Structural only; not wired yet.
        [Draw("Finite cars mode (0.5, not implemented yet)")]
        public bool FiniteCars = false;

        [Draw("Verbose logging")]
        public bool VerboseLogging = false;

        public override void Save(UnityModManager.ModEntry modEntry) =>
            UnityModManager.ModSettings.Save<Settings>(this, modEntry);

        public void OnChange() { }
    }
}
