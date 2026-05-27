using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;

namespace GRDNInterchange.Data
{
    public class InterchangeConfig
    {
        public List<string> HubYardIds { get; set; } = new List<string> { "HB", "MF" };
        public List<string> ExcludedYardIds { get; set; } = new List<string> { "MB", "HMB", "MFMB" };
        public Dictionary<string, string> SpokeHubOverrides { get; set; } = new Dictionary<string, string>();

        public static InterchangeConfig Load(string modFolder)
        {
            var path = Path.Combine(modFolder, "config.json");
            if (!File.Exists(path))
            {
                Main.Log("[Config] config.json not found — using defaults");
                return new InterchangeConfig();
            }
            try
            {
                return JsonConvert.DeserializeObject<InterchangeConfig>(File.ReadAllText(path))
                       ?? new InterchangeConfig();
            }
            catch (System.Exception ex)
            {
                Main.Log($"[Config] Parse error: {ex.Message}");
                return new InterchangeConfig();
            }
        }
    }
}
