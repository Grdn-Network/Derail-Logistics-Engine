using DV.JObjectExtstensions;
using DV.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using JobsManager = DV.Logic.Job.JobsManager;

namespace DLE.Data
{
    /// <summary>
    /// Car dormancy (#141): a pool car far from every player persists as DATA instead
    /// of a live GameObject. The capture is vanilla's own per-car save record
    /// (CarsSaveManager.GetCarSaveData), the respawn is vanilla's own save restore
    /// (InstantiateCarFromSavegame with the ORIGINAL carGuid and plate ID, then
    /// RestoreCarConnections), so a dormant car comes back as the same car in every
    /// sense: the mechanism vanilla already uses to resurrect unique cars, generalized.
    ///
    /// v1 scope (owner ruling 2026-08-02, approach B): IDLE EMPTIES only, whole cuts
    /// at a time, host or singleplayer only, default OFF via TuningDef.dormancyEnabled.
    /// Loaded-but-jobless cars join in v2 by widening one predicate.
    /// </summary>
    public static class CarDormancy
    {
        private static bool _inFlight;
        private static bool _hashWarned;
        private static int _nextCut = 1;

        public static void Reset()
        {
            _inFlight = false;
            _hashWarned = false;
        }

        /// <summary>Hosted by DleDirectorBehaviour; stale means a different world loaded.</summary>
        public static IEnumerator SweepLoop(Func<bool> stale)
        {
            while (true)
            {
                float wait = Mathf.Max(3f, Economy.RecipeProvider.Tuning.dormancySweepSeconds);
                yield return new WaitForSeconds(wait);
                if (stale()) yield break;
                if (!Main.IsHostOrSingleplayer()) continue;
                if (!Economy.RecipeProvider.Tuning.dormancyEnabled)
                {
                    // Toggled off with cars dormant: wake everything, the fleet must
                    // never be silently smaller than the player asked for.
                    if (DleCarPool.Instance.DormantCount > 0)
                        yield return WakeAllRoutine();
                    continue;
                }
                if (DleCarPool.SweepInFlight || _inFlight) continue;

                _inFlight = true;
                IEnumerator pass = null;
                try { pass = SweepOnce(stale); }
                catch (Exception ex)
                {
                    Main.LogAlways($"[Dormancy] sweep setup failed: {ex.GetType().Name}: {ex.Message}");
                    _inFlight = false;
                }
                if (pass != null)
                {
                    // Run the pass to completion; exceptions inside individual steps are
                    // caught per cut so one bad car never kills the loop for the session.
                    while (true)
                    {
                        bool moved;
                        try { moved = pass.MoveNext(); }
                        catch (Exception ex)
                        {
                            Main.LogAlways($"[Dormancy] sweep aborted: {ex.GetType().Name}: {ex.Message}");
                            break;
                        }
                        if (!moved) break;
                        yield return pass.Current;
                    }
                    _inFlight = false;
                }
            }
        }

        private static IEnumerator SweepOnce(Func<bool> stale)
        {
            var pool = DleCarPool.Instance;

            // A record whose car is LIVE is stale (a crash between capture and delete):
            // the live car wins, the record drops, nothing is ever duplicated.
            var liveGuids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kv in TrainCarRegistry.Instance.logicCarToTrainCar)
                if (kv.Key?.carGuid != null) liveGuids.Add(kv.Key.carGuid);
            foreach (var r in pool.DormantRecords.Where(r => liveGuids.Contains(r.Guid)).ToList())
            {
                Main.LogAlways($"[Dormancy] {r.Id}: dormant record is stale (car is live); record dropped.");
                pool.WakeDormant(r.Guid);
            }

            var players = PlayerPositions();
            if (players.Count == 0) yield break; // nobody to be near or far from

            float wakeR = Mathf.Max(200f, Economy.RecipeProvider.Tuning.dormancyRespawnMeters);
            float sleepR = Mathf.Max(wakeR + 200f, Economy.RecipeProvider.Tuning.dormancyDespawnMeters);

            // RESPAWN pass first: waking is always higher priority than sleeping more.
            // Grouping carries the yard as well as the cut id: two cuts can never fuse
            // across yards even if a legacy save still holds a collided id pair.
            foreach (var cut in pool.DormantRecords.GroupBy(r => r.Cut + "|" + r.YardId).ToList())
            {
                if (stale()) yield break;
                var anchor = YardAnchor(cut.First().YardId);
                if (anchor == null) continue;
                if (!AnyWithin(players, anchor.Value, wakeR)) continue;
                TryRespawnCut(cut.ToList());
                yield return null;
            }

            // DESPAWN pass: whole yards with no player anywhere near.
            foreach (var facility in Economy.EconomyState.Instance.Facilities.Values.ToList())
            {
                if (stale()) yield break;
                var sc = StationController.GetStationByYardID(facility.YardId);
                if (sc == null) continue;
                if (AnyWithin(players, sc.transform.position, sleepR)) continue;

                foreach (var cutCars in EligibleCuts(sc, players, sleepR))
                {
                    DespawnCut(cutCars, facility.YardId);
                    yield return null;
                }
            }
        }

        /// <summary>
        /// Eligible cuts in a yard: every member a pool car, empty (v1), jobless,
        /// unreserved, not player-spawned, not derailed, no loco anywhere in the set,
        /// and the whole trainset standing on this station's tracks. All or nothing:
        /// coupling is only restorable within a cut captured together.
        /// </summary>
        private static List<List<TrainCar>> EligibleCuts(StationController sc,
            List<Vector3> players, float sleepR)
        {
            var pool = DleCarPool.Instance;
            var tracks = Dispatch.DispatchServicing.StationTracks(sc, null);
            var jobsManager = SingletonBehaviour<JobsManager>.Instance;

            var reserved = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kv in Jobs.StaticDirectHaulJobDefinition.jobDefinitions)
                if (kv.Value?.reservedCarIds != null)
                    foreach (var rid in kv.Value.reservedCarIds)
                        reserved.Add(rid);

            var byTrainset = new Dictionary<Trainset, List<TrainCar>>();
            var whole = new Dictionary<Trainset, bool>();
            foreach (var kv in TrainCarRegistry.Instance.logicCarToTrainCar)
            {
                var car = kv.Key;
                var tc = kv.Value;
                if (car == null || tc == null || tc.trainset == null) continue;
                if (car.CurrentTrack == null || !tracks.Contains(car.CurrentTrack)) continue;

                bool ok = pool.Contains(car.carGuid)
                          && !tc.IsLoco
                          && !(tc.carLivery != null && DV.ThingTypes.CarTypes.IsAnyLocoSlugTender(tc.carLivery))
                          && !tc.derailed
                          && car.LoadedCargoAmount == 0f   // v1: empties only
                          && !car.playerSpawnedCar
                          && !reserved.Contains(car.ID)
                          && (jobsManager == null || jobsManager.GetJobOfCar(car) == null)
                          && !AnyWithin(players, tc.transform.position, sleepR);

                if (!byTrainset.TryGetValue(tc.trainset, out var list))
                {
                    byTrainset[tc.trainset] = list = new List<TrainCar>();
                    whole[tc.trainset] = true;
                }
                list.Add(tc);
                if (!ok) whole[tc.trainset] = false;
            }

            var cuts = new List<List<TrainCar>>();
            foreach (var kv in byTrainset)
            {
                // The whole trainset must be eligible AND fully collected here: a set
                // straddling out of the yard, or holding one loco, loaded, reserved or
                // near-a-player car, stays live in its entirety.
                if (!whole[kv.Key]) continue;
                if (kv.Value.Count != (kv.Key.cars?.Count ?? -1)) continue;
                cuts.Add(kv.Value);
            }
            return cuts;
        }

        private static void DespawnCut(List<TrainCar> cut, string yardId)
        {
            if (!TrackHashOk()) return;
            RailTrack[] tracks;
            try { tracks = SingletonBehaviour<RailTrackRegistryBase>.Instance.OrderedRailtracks; }
            catch { return; }

            var pool = DleCarPool.Instance;
            // The counter restarts every session while records persist in the save:
            // minting below the ledger's high-water mark fuses unrelated cuts into one
            // group (a wake at one yard spawning consists at another, proximity anchors
            // pointing at the wrong station). Always mint above everything stored.
            foreach (var r in pool.DormantRecords) if (r.Cut >= _nextCut) _nextCut = r.Cut + 1;
            int cutId = _nextCut++;
            var captured = new List<DleCarPool.DormantRecord>();
            try
            {
                // Capture EVERY car first: a cut that cannot fully capture stays live.
                foreach (var tc in cut)
                {
                    var jo = CarsSaveManager.GetCarSaveData(tc, tracks);
                    captured.Add(new DleCarPool.DormantRecord
                    {
                        Guid = tc.logicCar.carGuid,
                        Id = tc.logicCar.ID,
                        Livery = tc.carLivery?.name,
                        YardId = yardId,
                        Cut = cutId,
                        State = jo.ToString(Formatting.None),
                        Track = tc.logicCar.CurrentTrack?.ID?.FullDisplayID,
                    });
                }
            }
            catch (Exception ex)
            {
                Main.LogAlways($"[Dormancy] capture failed at {yardId} ({ex.GetType().Name}: {ex.Message}); cut stays live.");
                return;
            }

            // Record before delete: a crash in the window leaves car AND record, and the
            // stale-record reconcile drops the record. The other order loses the car.
            foreach (var r in captured) pool.MarkDormant(r);
            foreach (var tc in cut)
            {
                var id = tc.logicCar?.ID;
                var guid = tc.logicCar?.carGuid;
                try
                {
                    CarSpawner.Instance.DeleteCar(tc);
                    // Hold the plate while dormant, exactly like vanilla's unique cars,
                    // so no new spawn can mint the same ID in the meantime.
                    if (id != null)
                        try { SingletonBehaviour<DV.Logic.Job.IdGenerator>.Instance.ReserveCarId(id); } catch { }
                }
                catch (Exception ex)
                {
                    Main.LogAlways($"[Dormancy] delete of {id} failed ({ex.GetType().Name}: {ex.Message}); car stays live, record dropped.");
                    if (guid != null) pool.WakeDormant(guid);
                }
            }
            Main.Log($"[Dormancy] {captured.Count} car(s) dormant at {yardId} (cut {cutId}); {pool.DormantCount} dormant total.");
        }

        /// <summary>
        /// Respawn one cut through vanilla's own save restore. Deferral is always safe:
        /// records stay dormant and the next sweep retries. A respawned car is never
        /// quarantine-deleted; failure modes keep it either dormant or live, never gone.
        /// </summary>
        /// <summary>
        /// Wake the whole cut containing a plate-identified dormant car: the assignment
        /// wake (#146). A booklet that picks a stored car is the claim on it; cars on
        /// jobs never sleep, so it stays awake as long as it is booked. Returns true
        /// when the car is live afterwards.
        /// </summary>
        internal static bool WakeCutContaining(string plateId)
        {
            var pool = DleCarPool.Instance;
            if (!pool.TryGetDormantByPlate(plateId, out var rec)) return false;
            var cut = pool.DormantRecords.Where(r => r.Cut == rec.Cut
                && string.Equals(r.YardId, rec.YardId, StringComparison.OrdinalIgnoreCase)).ToList();
            try { TryRespawnCut(cut); }
            catch (Exception ex)
            {
                Main.LogAlways($"[Dormancy] assignment wake of {plateId} failed: {ex.GetType().Name}: {ex.Message}");
            }
            return !pool.IsDormant(rec.Guid);
        }

        private static void TryRespawnCut(List<DleCarPool.DormantRecord> cut)
        {
            if (!TrackHashOk()) return;
            // A record can leave the ledger between a sweep's snapshot and this call
            // (an assignment wake, another pass): respawning from a stale record would
            // DUPLICATE the car. Only records still dormant right now count.
            cut = cut.Where(r => DleCarPool.Instance.IsDormant(r.Guid)).ToList();
            if (cut.Count == 0) return;
            RailTrack[] tracks;
            try { tracks = SingletonBehaviour<RailTrackRegistryBase>.Instance.OrderedRailtracks; }
            catch { return; }

            var pool = DleCarPool.Instance;
            var parsed = new List<(DleCarPool.DormantRecord rec, JObject jo)>();
            foreach (var r in cut)
            {
                JObject jo;
                try { jo = JObject.Parse(r.State); }
                catch (Exception ex)
                {
                    Main.LogAlways($"[Dormancy] {r.Id}: dormant record unreadable ({ex.Message}); record dropped, car lost. Run company.respawn to refill.");
                    pool.WakeDormant(r.Guid);
                    return;
                }
                if (SpanBlocked(jo, tracks))
                    return; // something is parked there; whole cut waits for the next sweep
                parsed.Add((r, jo));
            }

            var spawned = new List<TrainCar>();
            foreach (var (rec, jo) in parsed)
            {
                TrainCar tc = null;
                try
                {
                    try { SingletonBehaviour<DV.Logic.Job.IdGenerator>.Instance.UnReserveCarId(rec.Id); } catch { }
                    tc = CarsSaveManager.InstantiateCarFromSavegame(jo, tracks);
                }
                catch (Exception ex)
                {
                    Main.LogAlways($"[Dormancy] respawn of {rec.Id} threw ({ex.GetType().Name}: {ex.Message}); cut deferred.");
                }
                if (tc == null)
                {
                    // Cars already spawned this pass stay live (their records clear);
                    // the rest of the cut stays dormant and retries later.
                    foreach (var live in spawned) FinishRespawn(live);
                    return;
                }
                pool.WakeDormant(rec.Guid);
                spawned.Add(tc);
            }

            // Couplings restore from each car's own record, all partners now present.
            foreach (var (_, jo) in parsed)
            {
                try { CarsSaveManager.RestoreCarConnections(jo); }
                catch (Exception ex) { Main.Log($"[Dormancy] coupling restore: {ex.Message}"); }
            }
            foreach (var tc in spawned) FinishRespawn(tc);
            TrySendSpawnTrainset(spawned);
            Main.Log($"[Dormancy] {spawned.Count} car(s) awake at {cut[0].YardId}; {pool.DormantCount} dormant remain.");
        }

        private static void FinishRespawn(TrainCar tc)
        {
            try { CarsSaveManager.SetBrakesOnSpawn(tc); } catch { }
            try { tc.ForceSleep(true); } catch { }
        }

        /// <summary>Wake one yard's dormant cars regardless of distance: the board's
        /// wake button, the dispatcher reaching for stored stock on purpose.</summary>
        public static IEnumerator WakeYardRoutine(string yardId)
        {
            if (_inFlight) yield break;
            _inFlight = true;
            var pool = DleCarPool.Instance;
            foreach (var cut in pool.DormantRecords
                .Where(r => string.Equals(r.YardId, yardId, StringComparison.OrdinalIgnoreCase))
                .GroupBy(r => r.Cut).ToList())
            {
                try { TryRespawnCut(cut.ToList()); }
                catch (Exception ex) { Main.LogAlways($"[Dormancy] yard wake failed: {ex.GetType().Name}: {ex.Message}"); }
                yield return null;
            }
            _inFlight = false;
        }

        /// <summary>Wake everything regardless of distance (toggle off, or company.wake).
        /// Shares the sweep's single-flight guard: two respawn passes over the same
        /// records at once is how a car gets duplicated, so one runs at a time.</summary>
        public static IEnumerator WakeAllRoutine()
        {
            if (_inFlight)
            {
                Main.Log("[Dormancy] a sweep is already running; wake will happen on its own shortly.");
                yield break;
            }
            _inFlight = true;
            var pool = DleCarPool.Instance;
            foreach (var cut in pool.DormantRecords.GroupBy(r => r.Cut + "|" + r.YardId).ToList())
            {
                try { TryRespawnCut(cut.ToList()); }
                catch (Exception ex) { Main.LogAlways($"[Dormancy] wake failed: {ex.GetType().Name}: {ex.Message}"); }
                yield return null;
            }
            _inFlight = false;
            if (pool.DormantCount > 0)
                Main.LogAlways($"[Dormancy] {pool.DormantCount} car(s) still dormant (blocked spans or errors); they retry every sweep.");
        }

        // Helpers

        /// <summary>
        /// True when a live car stands close to the stored spot: the parking space is
        /// taken and the respawn defers. The stored position is absolute (the vanilla
        /// record), live transforms are shifted; adding WorldMover.currentMove converts
        /// exactly the way InstantiateCarFromSavegame does. Conservative on any doubt.
        /// </summary>
        private static bool SpanBlocked(JObject jo, RailTrack[] tracks)
        {
            try
            {
                var stored = jo.GetVector3("position");
                if (!stored.HasValue) return true;
                var target = stored.Value + WorldMover.currentMove;
                foreach (var kv in TrainCarRegistry.Instance.logicCarToTrainCar)
                {
                    var tc = kv.Value;
                    if (tc == null) continue;
                    if ((tc.transform.position - target).sqrMagnitude < 18.0f * 18.0f) return true;
                }
                return false;
            }
            catch { return true; }
        }

        private static bool TrackHashOk()
        {
            try
            {
                var current = SingletonBehaviour<RailTrackRegistryBase>.Instance?.TracksHash;
                var stored = DleCarPool.Instance.DormantTrackHash;
                if (string.IsNullOrEmpty(stored) || string.IsNullOrEmpty(current) || stored == current) return true;
                if (!_hashWarned)
                {
                    _hashWarned = true;
                    Main.LogAlways($"[Dormancy] track layout changed since the dormant cars were captured; {DleCarPool.Instance.DormantCount} dormant car(s) cannot be restored and are dropped. Run company.respawn to refill the fleet.");
                    DleCarPool.Instance.DropAllDormant();
                }
                return false;
            }
            catch { return true; }
        }

        private static Vector3? YardAnchor(string yardId)
        {
            var sc = StationController.GetStationByYardID(yardId ?? "");
            return sc == null ? (Vector3?)null : sc.transform.position;
        }

        private static bool AnyWithin(List<Vector3> players, Vector3 pos, float range)
        {
            float sq = range * range;
            foreach (var p in players)
                if ((p - pos).sqrMagnitude <= sq) return true;
            return false;
        }

        /// <summary>
        /// Every player position in the same shifted world frame as cars and stations:
        /// the local player plus every DVMP remote avatar (same enumeration the fax
        /// uses). Frame-consistent by construction, no absolute-position math.
        /// </summary>
        private static List<Vector3> PlayerPositions()
        {
            var list = new List<Vector3>();
            try
            {
                var lp = PlayerManager.PlayerTransform;
                if (lp != null) list.Add(lp.position);
            }
            catch { }
            try
            {
                // The scene scan is the expensive part (the lag meter caught it on the
                // roster endpoint); avatars change only on join and leave, so cache the
                // component refs briefly and rescan early if any died.
                var now = Time.realtimeSinceStartup;
                bool refresh = _avatarCache == null || now - _avatarCacheAt > 15f;
                if (!refresh)
                    foreach (var c in _avatarCache) if (c == null) { refresh = true; break; }
                if (refresh)
                {
                    var type = _networkedPlayerType ?? (_networkedPlayerType = AppDomain.CurrentDomain.GetAssemblies()
                        .Select(a => a.GetType("Multiplayer.Components.Networking.Player.NetworkedPlayer"))
                        .FirstOrDefault(t => t != null));
                    _avatarCache = type == null
                        ? Array.Empty<Component>()
                        : UnityEngine.Object.FindObjectsOfType(type).OfType<Component>().ToArray();
                    _avatarCacheAt = now;
                }
                foreach (var comp in _avatarCache)
                    if (comp != null) list.Add(comp.transform.position);
            }
            catch { }
            return list;
        }

        private static Type _networkedPlayerType;
        private static Component[] _avatarCache;
        private static float _avatarCacheAt = -999f;
        private static bool _mpSendFailed;

        /// <summary>
        /// Announce a respawned cut to DVMP clients. SpawnLoadedCar is not one of the
        /// spawn entry points the Multiplayer mod postfixes, and dv-mp itself calls
        /// SendSpawnTrainset manually for exactly such a path (its work-train spawn),
        /// so this mirrors that precedent by reflection: fail-soft, no compile-time
        /// dependency, single-player silently skips.
        /// </summary>
        private static void TrySendSpawnTrainset(List<TrainCar> cars)
        {
            if (cars.Count == 0 || _mpSendFailed) return;
            try
            {
                var lifecycleType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("Multiplayer.Components.Networking.NetworkLifecycle"))
                    .FirstOrDefault(t => t != null);
                if (lifecycleType == null) return; // no DVMP: nothing to announce
                var instance = lifecycleType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (instance == null) return;
                var isHost = lifecycleType.GetMethod("IsHost", Type.EmptyTypes)?.Invoke(instance, null) as bool?;
                if (isHost != true) return;
                var server = lifecycleType.GetProperty("Server")?.GetValue(instance)
                             ?? lifecycleType.GetField("Server")?.GetValue(instance);
                if (server == null) return;

                foreach (var m in server.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (m.Name != "SendSpawnTrainset") continue;
                    var ps = m.GetParameters();
                    var args = new object[ps.Length];
                    bool usable = true;
                    for (int i = 0; i < ps.Length; i++)
                    {
                        var pt = ps[i].ParameterType;
                        if (pt.IsAssignableFrom(typeof(List<TrainCar>))) args[i] = cars;
                        else if (pt == typeof(TrainCar[])) args[i] = cars.ToArray();
                        else if (pt == typeof(bool))
                            // Couplings were restored exactly from the save record; the
                            // join path sends restored cars with autoCouple false too.
                            args[i] = !string.Equals(ps[i].Name, "autoCouple", StringComparison.OrdinalIgnoreCase);
                        else if (ps[i].HasDefaultValue)
                            // The real signature ends in an optional per-peer target
                            // (ITransportPeer sendTo = null); any trailing optional we
                            // do not recognize takes its declared default.
                            args[i] = ps[i].DefaultValue;
                        else { usable = false; break; }
                    }
                    if (!usable) continue;
                    m.Invoke(server, args);
                    return;
                }
                _mpSendFailed = true;
                Main.LogAlways("[Dormancy] DVMP SendSpawnTrainset has no callable overload; clients will see respawned cars on rejoin only. Dormancy still works, but report this.");
            }
            catch (Exception ex)
            {
                _mpSendFailed = true;
                Main.LogAlways($"[Dormancy] DVMP spawn announce failed ({ex.GetType().Name}: {ex.Message}); clients will see respawned cars on rejoin only.");
            }
        }
    }
}
