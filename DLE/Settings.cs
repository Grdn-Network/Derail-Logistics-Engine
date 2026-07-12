using UnityModManagerNet;

namespace DLE
{
    public class Settings : UnityModManager.ModSettings, IDrawable
    {
        // Economy, generation, and dispatch settings arrive with their phases.

        [Draw("Verbose logging")]
        public bool VerboseLogging = false;

        public override void Save(UnityModManager.ModEntry modEntry) =>
            UnityModManager.ModSettings.Save<Settings>(this, modEntry);

        public void OnChange() { }
    }
}
