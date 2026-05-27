using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;

namespace GRDNInterchange.Data
{
    public class InterchangeConfig
    {
        public List<string> HubYardIds { get; set; } = new List<string> { "HB", "MF" };

        public List<string> ExcludedYardIds { get; set; } = new List<string>
        {
            "MB", "HMB", "MFMB"
        };

        public Dictionary<string, string> SpokeHubOverrides { get; set; } =
            new Dictionary<string, string>
            {
                // West → Machine Factory
                { "IMW", "MF" },
                { "CP",  "MF" },
                { "FRC", "MF" },
                { "OR",  "MF" },
                { "FF",  "MF" },
                { "FM",  "MF" },
                { "OWC", "MF" },
                { "CW",  "MF" },
                { "SW",  "MF" },
                // East → Harbor
                { "SM",  "HB" },
                { "FRS", "HB" },
                { "CMS", "HB" },
                { "CS",  "HB" },
                { "CME", "HB" },
                { "GF",  "HB" },
                { "OWN", "HB" },
                { "IME", "HB" },
            };

        public static InterchangeConfig Load(string modFolder)
        {
            var path = Path.Combine(modFolder, "config.json");

            if (!File.Exists(path))
            {
                var defaults = new InterchangeConfig();
                try
                {
                    File.WriteAllText(path,
                        JsonConvert.SerializeObject(defaults, Formatting.Indented));
                    Main.Log($"[Config] config.json not found — wrote defaults to {path}");
                }
                catch (System.Exception ex)
                {
                    Main.Log($"[Config] Could not write default config.json: {ex.Message}");
                }
                return defaults;
            }

            try
            {
                var loaded = JsonConvert.DeserializeObject<InterchangeConfig>(
                    File.ReadAllText(path));
                Main.Log($"[Config] Loaded config.json from {path}");
                return loaded ?? new InterchangeConfig();
            }
            catch (System.Exception ex)
            {
                Main.Log($"[Config] Parse error in config.json: {ex.Message} — using defaults");
                return new InterchangeConfig();
            }
        }
    }
}
