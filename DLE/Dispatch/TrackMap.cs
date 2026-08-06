using DV.Utils;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace DLE.Dispatch
{
    /// <summary>
    /// The Rails map data (#131 first pass): the real railway, read-only.
    /// Geometry comes from the game's own registries and is built ONCE per world
    /// (the scan-once rule), cached as serialized bytes keyed by the track hash.
    /// Traffic (consists, junction states, occupied tracks) is sampled per request
    /// and rides the board's 2 second payload cache. Nothing here mutates anything.
    /// </summary>
    internal static class TrackMap
    {
        private const float YardRadius = 340f;    // inside this, the yard owns the picture
        private const float MergeCell = 22f;      // parallel-rail clustering cell, metres
        private const float JunctionCell = 45f;   // junction thinning cell, metres

        private static byte[] _geometryBytes;
        private static string _geometryHash;
        private static Junction[] _junctions = Array.Empty<Junction>();

        public static byte[] GeometryBytes()
        {
            string hash = null;
            try { hash = SingletonBehaviour<RailTrackRegistryBase>.Instance?.TracksHash; } catch { }
            if (_geometryBytes != null && _geometryHash == hash && JunctionsAlive()) return _geometryBytes;

            var move = WorldMover.currentMove;
            var names = new Dictionary<RailTrack, string>();
            try
            {
                foreach (var kv in RailTrackRegistry.LogicToRailTrack)
                    if (kv.Value != null && kv.Key?.ID != null) names[kv.Value] = kv.Key.ID.FullDisplayID;
            }
            catch { }

            // Station anchors first: they decide what counts as yard interior.
            var stations = new List<object>();
            var stPos = new List<Vector2>();
            foreach (var f in Economy.EconomyState.Instance.Facilities.Values.ToList())
            {
                var sc = StationController.GetStationByYardID(f.YardId);
                if (sc == null) continue;
                var p = sc.transform.position - move;
                stations.Add(new { id = f.YardId, x = (float)Math.Round(p.x, 1), z = (float)Math.Round(p.z, 1) });
                stPos.Add(new Vector2(p.x, p.z));
            }
            bool InYard(float x, float z)
            {
                for (int i = 0; i < stPos.Count; i++)
                {
                    float dx = x - stPos[i].x, dz = z - stPos[i].y;
                    if (dx * dx + dz * dz < YardRadius * YardRadius) return true;
                }
                return false;
            }

            // Only open rail is drawn on the network view (owner ruling): yard interiors
            // belong to the station's own view, so the approach rails just run into the
            // bubble and stop. Parallel rails sit ~4m apart, which is about one pixel at
            // any scale a dispatcher can use, so they are merged here into CORRIDORS
            // carrying a track count and fanned apart on screen instead.
            var corrA = new List<Vector2>();
            var corrB = new List<Vector2>();
            var corrN = new List<int>();
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var rt in SingletonBehaviour<RailTrackRegistryBase>.Instance.OrderedRailtracks)
            {
                if (rt == null || rt.curve == null || rt.curve.pointCount < 2) continue;
                if (names.TryGetValue(rt, out var id) && id != null && id.Length > 0 && id[0] != '#')
                    continue; // named track = yard interior
                Vector2? prev = null;
                for (int i = 0; i < rt.curve.pointCount; i++)
                {
                    var bp = rt.curve[i];
                    if (bp == null) continue;
                    var p = bp.position - move;
                    var cur = new Vector2(p.x, p.z);
                    if (prev.HasValue)
                    {
                        var a = prev.Value; var b = cur;
                        if ((a - b).sqrMagnitude > 1f && !(InYard(a.x, a.y) && InYard(b.x, b.y)))
                        {
                            var mid = (a + b) * 0.5f;
                            float ang = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
                            int ab = (int)(((ang % 180f) + 180f) % 180f / 20f);
                            int cx = Mathf.FloorToInt(mid.x / MergeCell), cz = Mathf.FloorToInt(mid.y / MergeCell);
                            int hit = -1;
                            for (int dx = -1; dx <= 1 && hit < 0; dx++)
                                for (int dz = -1; dz <= 1 && hit < 0; dz++)
                                    for (int da = -1; da <= 1 && hit < 0; da++)
                                    {
                                        var k = (cx + dx) + ":" + (cz + dz) + ":" + (((ab + da) % 9) + 9) % 9;
                                        if (index.TryGetValue(k, out var at)) hit = at;
                                    }
                            if (hit >= 0) corrN[hit]++;
                            else
                            {
                                index[cx + ":" + cz + ":" + ab] = corrA.Count;
                                corrA.Add(a); corrB.Add(b); corrN.Add(1);
                            }
                            foreach (var q in new[] { a, b })
                            {
                                if (q.x < minX) minX = q.x;
                                if (q.x > maxX) maxX = q.x;
                                if (q.y < minZ) minZ = q.y;
                                if (q.y > maxZ) maxZ = q.y;
                            }
                        }
                    }
                    prev = cur;
                }
            }
            var corridors = new List<float[]>(corrA.Count);
            for (int i = 0; i < corrA.Count; i++)
                corridors.Add(new[]
                {
                    (float)Math.Round(corrA[i].x, 1), (float)Math.Round(corrA[i].y, 1),
                    (float)Math.Round(corrB[i].x, 1), (float)Math.Round(corrB[i].y, 1),
                    corrN[i]
                });

            // Junctions live under the same track root dv-mp indexes them from; one
            // scan at map build, refs kept for the live state reads.
            try
            {
                var root = RailTrackRegistry.Instance.TrackRootParent;
                _junctions = root != null ? root.GetComponentsInChildren<Junction>() : Array.Empty<Junction>();
            }
            catch { _junctions = Array.Empty<Junction>(); }
            // Junctions inside a yard live in that station's view; the rest are thinned
            // to one per cell so they read as marks instead of a blob.
            var js = new List<float[]>();
            var jseen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var j in _junctions)
            {
                if (j == null) continue;
                var p = j.transform.position - move;
                if (InYard(p.x, p.z)) continue;
                var k = Mathf.FloorToInt(p.x / JunctionCell) + ":" + Mathf.FloorToInt(p.z / JunctionCell);
                if (!jseen.Add(k)) continue;
                js.Add(new[] { (float)Math.Round(p.x, 1), (float)Math.Round(p.z, 1) });
            }

            var payload = new
            {
                hash,
                corridors,
                junctions = js,
                stations,
                bounds = new { minX, maxX, minZ, maxZ }
            };
            _geometryBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload));
            _geometryHash = hash;
            int doubles = corrN.Count(n => n > 1);
            Main.LogAlways($"[TrackMap] network built: {corridors.Count} corridor(s) ({doubles} multi-track), "
                + $"{js.Count} junction(s), {stations.Count} station(s), {_geometryBytes.Length / 1024}KB; yard interiors excluded.");
            return _geometryBytes;
        }

        private static bool JunctionsAlive()
        {
            // Unity fake-null flags destroyed refs after a world reload within one
            // session; a dead first entry means the whole set must be rescanned.
            return _junctions.Length == 0 || _junctions[0] != null;
        }

        public static object TrafficPayload()
        {
            var move = WorldMover.currentMove;
            var jobsManager = SingletonBehaviour<DV.Logic.Job.JobsManager>.Instance;

            var consists = new List<object>();
            var seen = new HashSet<Trainset>();
            var occupied = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kv in TrainCarRegistry.Instance.logicCarToTrainCar)
            {
                var car = kv.Key;
                var tc = kv.Value;
                if (car?.CurrentTrack?.ID?.FullDisplayID is string tid) occupied.Add(tid);
                if (tc == null || tc.trainset == null || seen.Contains(tc.trainset)) continue;
                seen.Add(tc.trainset);
                var cars = tc.trainset.cars;
                if (cars == null || cars.Count == 0) continue;
                var head = cars[0];
                var tail = cars[cars.Count - 1];
                if (head == null || tail == null) continue;
                var a = head.transform.position - move;
                var b = tail.transform.position - move;
                bool loco = false;
                string jobId = null;
                foreach (var c in cars)
                {
                    if (c == null) continue;
                    if (c.IsLoco) loco = true;
                    if (jobId == null && c.logicCar != null && jobsManager != null)
                    {
                        var j = jobsManager.GetJobOfCar(c.logicCar);
                        if (j != null) jobId = j.ID;
                    }
                }
                consists.Add(new
                {
                    x1 = (float)Math.Round(a.x, 1), z1 = (float)Math.Round(a.z, 1),
                    x2 = (float)Math.Round(b.x, 1), z2 = (float)Math.Round(b.z, 1),
                    n = cars.Count, loco, jobId
                });
            }

            int[] branches = null;
            if (_junctions.Length > 0 && JunctionsAlive())
            {
                branches = new int[_junctions.Length];
                for (int i = 0; i < _junctions.Length; i++)
                    branches[i] = _junctions[i] != null ? _junctions[i].selectedBranch : -1;
            }

            return new { consists, junctions = branches, occupied = occupied.ToList() };
        }
    }
}
