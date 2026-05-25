using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GRDNInterchange.Data
{
    /// <summary>
    /// Knows which stations are hubs and which hub serves each origin station.
    /// Hub assignment is computed by nearest-hub distance so no manual list is needed.
    /// Initialized once after WorldStreamingInit.LoadingFinished.
    /// </summary>
    public class HubRegistry
    {
        public static HubRegistry Instance { get; private set; }

        private readonly HashSet<string> _hubYardIds;
        private readonly Dictionary<string, StationController> _hubStations =
            new Dictionary<string, StationController>();

        private HubRegistry(Settings s)
        {
            _hubYardIds = new HashSet<string>(s.HubYardIds);
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
        /// Returns the hub yard ID nearest to the given origin station by world distance.
        /// Falls back to the first configured hub if positions can't be compared.
        /// </summary>
        public string GetAssignedHubId(string originYardId)
        {
            var origin = StationController.allStations
                .FirstOrDefault(sc => sc.stationInfo.YardID == originYardId);

            if (origin == null)
                return _hubStations.Keys.FirstOrDefault() ?? "HB";

            var nearest = _hubStations
                .OrderBy(kvp => Vector3.Distance(
                    origin.transform.position,
                    kvp.Value.transform.position))
                .FirstOrDefault();

            if (nearest.Key != null)
            {
                Main.Log($"[HubRegistry] {originYardId} → nearest hub is {nearest.Key} " +
                         $"({Vector3.Distance(origin.transform.position, nearest.Value.transform.position):F0} m)");
            }

            return nearest.Key ?? _hubStations.Keys.FirstOrDefault() ?? "HB";
        }

        /// <summary>
        /// The "opposite" hub — where cross-hub block trains are destined.
        /// </summary>
        public string GetOppositeHubId(string fromHubYardId) =>
            _hubStations.Keys.FirstOrDefault(id => id != fromHubYardId) ?? fromHubYardId;

        public IEnumerable<StationController> AllHubs => _hubStations.Values;

        public bool IsReady => _hubStations.Count > 0;
    }
}
