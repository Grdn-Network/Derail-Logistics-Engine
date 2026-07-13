using UnityModManagerNet;

namespace DLE
{
    public class Settings : UnityModManager.ModSettings, IDrawable
    {
        [Draw("Starting stock per output cargo (carloads)")]
        public int InitialStock = 6;

        [Draw("Smallest haul the economy will generate (carloads)")]
        public int MinShipCarloads = 3;

        [Draw("Largest haul the economy will generate (cars)")]
        public int MaxCarsPerHaul = 6;

        [Draw("Verbose logging")]
        public bool VerboseLogging = false;

        // Finite cars mode is experimental and NOT surfaced in the settings GUI on
        // purpose (no Draw attribute): switching it mid-save strands carless jobs, and
        // the warehouse attach mechanic is unproven in game. Dev use only: set it by
        // editing Settings.xml while the game is closed.
        public bool FiniteCars = false;

        public override void Save(UnityModManager.ModEntry modEntry) =>
            UnityModManager.ModSettings.Save<Settings>(this, modEntry);

        public void OnChange() { }
    }
}
