using System.Collections.Generic;
using UnityModManagerNet;

namespace GRDNInterchange
{
    public class Settings : UnityModManager.ModSettings, IDrawable
    {
        // Hub yard IDs — stations in this list ARE hubs (never rerouted, always sort targets)
        public List<string> HubYardIds = new List<string> { "HB", "MF" };

        // Stations that feed into MF hub. Everything not listed here feeds into HB.
        public List<string> MFSideStations = new List<string>
        {
            "MF", "CP", "CW", "IMW", "SW", "OR", "FM", "OWC", "FRC", "FRS", "SM", "FF"
        };

        // Minimum cars on an outbound track before a hub→hub block job is generated.
        public int BlockThresholdCars = 6;

        // When true, shunt jobs at hubs require actual cargo load/unload operations.
        // When false, shunt jobs are simple car-move-to-track orders.
        public bool RequireLoadUnload = false;

        public override void Save(UnityModManager.ModEntry modEntry) => Save(this, modEntry);

        public void OnChange() { }
    }
}
