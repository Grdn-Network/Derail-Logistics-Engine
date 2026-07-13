using System.Collections;
using UnityEngine;

namespace DLE.Economy
{
    /// <summary>
    /// The job generator machine: on the host, tick the economy and keep the map stocked
    /// with hauls up to the configured caps. One haul per tick spreads the spawn cost; an
    /// initial burst shortly after world load fills the board for the session.
    /// </summary>
    public class DleDirectorBehaviour : MonoBehaviour
    {
        private static GameObject _host;

        public static void StartOnHost()
        {
            if (_host != null) return;
            _host = new GameObject("DLE_Director");
            DontDestroyOnLoad(_host);
            _host.AddComponent<DleDirectorBehaviour>();
        }

        private void Start() => StartCoroutine(TickLoop());

        private IEnumerator TickLoop()
        {
            // Let world streaming settle, then fill the board.
            yield return new WaitForSeconds(15f);
            Main.Log("[Director] initial fill starting.");
            int safety = 0;
            while (Main.IsHostOrSingleplayer() && WorldReady() &&
                   EconomyDirector.GenerateOne() && safety++ < 40)
                yield return new WaitForSeconds(1.5f); // one spawn per frame-slice, no hitching

            Main.Log("[Director] initial fill done; ticking.");
            float productionAccumulator = 0f;
            while (true)
            {
                float tick = Mathf.Max(15, Main.Settings?.DirectorTickSeconds ?? 60);
                yield return new WaitForSeconds(tick);
                if (!Main.IsHostOrSingleplayer() || !WorldReady()) continue;

                // Cargo enters the world at the sources on a slow clock.
                productionAccumulator += tick / 60f;
                float interval = Mathf.Max(1, Main.Settings?.SourceProductionMinutes ?? 10);
                if (productionAccumulator >= interval)
                {
                    EconomyState.Instance.TickSourceProduction((int)(productionAccumulator / interval));
                    productionAccumulator %= interval;
                }

                EconomyDirector.GenerateOne();
            }
        }

        private static bool WorldReady() =>
            StationController.allStations != null && StationController.allStations.Count > 0;
    }
}
