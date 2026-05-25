using System.Collections.Generic;
using System.Linq;

namespace GRDNInterchange.Data
{
    /// <summary>
    /// Knows which stations are hubs and which hub serves each origin station.
    /// Initialized once after WorldStreamingInit.LoadingFinished.
    /// </summary>
    public class HubRegistry
    {
        public static HubRegistry Instance { get; private set; }

        private readonly HashSet<string> _hubYardIds;
        private readonly HashSet<string> _mfSideStations;
        private readonly Dictionary<string, StationController> _hubStations =
            new Dictionary<string, StationController>();

        private HubRegistry(Settings s)
        {
            _hubYardIds       = new HashSet<string>(s.HubYardIds);
            _mfSideStations   = new HashSet<string>(s.MFSideStations);
        }

        public static void Initialize(Settings settings)
        {
            Instance = new HubRegistry(settings);

            foreach (var sc in StationController.allStations)
            {
                var yardId = sc.stationInfo.YardID;
                if (Instance._hubYardIds.Contains(yardId))
                {
                    Instance._hubStations[yardId] = sc;
                    Main.Log($"[HubRegistry] Hub registered: {yardId}");
                }
            }

            var missing = Instance._hubYardIds.Except(Instance._hubStations.Keys).ToList();
            if (missing.Count > 0)
                Main.Log($"[HubRegistry] WARNING: configured hubs not found in world: {string.Join(", ", missing)}");
        }

        // ── Queries ────────────────────────────────────────────────────────────────

        public bool IsHub(string yardId) =>
            yardId != null && _hubYardIds.Contains(yardId);

        public StationController GetHub(string yardId) =>
            _hubStations.TryGetValue(yardId, out var sc) ? sc : null;

        /// <summary>
        /// Returns the hub yard ID that serves the given origin station.
        /// MF-side stations → "MF", everything else → "HB".
        /// Falls back to the first configured hub if neither "HB" nor "MF" exists.
        /// </summary>
        public string GetAssignedHubId(string originYardId)
        {
            var preferredHub = _mfSideStations.Contains(originYardId) ? "MF" : "HB";
            if (_hubStations.ContainsKey(preferredHub)) return preferredHub;
            // Fallback: any available hub
            return _hubStations.Keys.FirstOrDefault() ?? preferredHub;
        }

        /// <summary>
        /// The "opposite" hub — where cross-hub block trains are destined.
        /// </summary>
        public string GetOppositeHubId(string fromHubYardId)
        {
            return _hubStations.Keys.FirstOrDefault(id => id != fromHubYardId)
                ?? fromHubYardId;
        }

        public IEnumerable<StationController> AllHubs => _hubStations.Values;

        public bool IsReady => _hubStations.Count > 0;
    }
}
