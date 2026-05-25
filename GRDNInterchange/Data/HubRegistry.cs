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

        // Cache nearest-hub lookups — computed once per spoke station, not per-car
        private readonly Dictionary<string, string> _assignedHubCache =
            new Dictionary<string, string>(System.StringComparer.Ordinal);

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
        /// Returns the hub yard ID nearest to the given station by world distance.
        /// Result is cached after the first lookup — safe because hub topology is
        /// fixed for the lifetime of a loaded world.
        /// </summary>
        public string GetAssignedHubId(string originYardId)
        {
            if (_assignedHubCache.TryGetValue(originYardId, out var cached))
                return cached;

            var origin = StationController.allStations
                .FirstOrDefault(sc => sc.stationInfo.YardID == originYardId);

            if (origin == null)
            {
                var fallback = _hubStations.Keys.FirstOrDefault() ?? "HB";
                _assignedHubCache[originYardId] = fallback;
                return fallback;
            }

            var nearest = _hubStations
                .OrderBy(kvp => Vector3.Distance(
                    origin.transform.position,
                    kvp.Value.transform.position))
                .FirstOrDefault();

            var result = nearest.Key ?? _hubStations.Keys.FirstOrDefault() ?? "HB";
            _assignedHubCache[originYardId] = result;

            Main.Log($"[HubRegistry] {originYardId} → nearest hub {result} " +
                     $"({Vector3.Distance(origin.transform.position, nearest.Value.transform.position):F0} m) [cached]");
            return result;
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
