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
        // The whole railway is drawn, yards included (owner ruling). This used to cut
        // out everything within 500m of a station, which on the live world hid 420 of
        // the 641 switches and 58 percent of all track: that is why the map looked like
        // it had holes in it and switches missing. It did. Zoom is what makes a yard
        // workable now, so nothing is hidden and the picture is the actual railway.
        private const float YardRadius = 0f;
        private const float ThinMeters = 10f;     // drop points closer than this together
        private const float JunctionCell = 60f;   // junction thinning cell, metres

        private static byte[] _geometryBytes;
        private static string _geometryHash;

        /// <summary>Counts how many times this map has been built. The board keeps the
        /// geometry it fetched, so it needs something that changes when that copy goes
        /// stale, and the track hash cannot do it: loading a second save rebuilds the same
        /// railway with the same hash, but the game may place the world at a different
        /// origin, which this payload bakes in.</summary>
        internal static int Epoch { get; private set; }
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

            // Station anchors, kept as labels on the map and as the yard test the
            // interlocking still takes (now zero, so it excludes nothing).
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
            // EVERY rail goes out, as its REAL polyline: yards, sidings, the lot. An
            // earlier build drew only open line and left the map full of holes. Another
            // tried merging parallel rails into fanned corridors and it drew combs: the
            // clustering counted sequential pieces of one track as parallel neighbours,
            // so a single curve claimed fifteen tracks. Real geometry, drawn heavy, reads
            // better than any synthetic spreading.
            var lines = new List<float[]>();
            var lineTrack = new List<string>();          // which rail each polyline came from
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            var run = new List<float>();
            string runTrack = null;
            void Flush()
            {
                if (run.Count >= 4) { lines.Add(run.ToArray()); lineTrack.Add(runTrack); }
                run.Clear();
            }
            foreach (var rt in SingletonBehaviour<RailTrackRegistryBase>.Instance.OrderedRailtracks)
            {
                if (rt == null || rt.curve == null || rt.curve.pointCount < 2) continue;
                names.TryGetValue(rt, out var id);
                run.Clear();
                runTrack = id;
                float lx = 0f, lz = 0f; bool have = false;
                for (int i = 0; i < rt.curve.pointCount; i++)
                {
                    var bp = rt.curve[i];
                    if (bp == null) continue;
                    var p = bp.position - move;
                    // Thin the jitter: anything inside ThinMeters of the last kept point
                    // adds noise and payload without changing the drawn shape.
                    if (have && (p.x - lx) * (p.x - lx) + (p.z - lz) * (p.z - lz) < ThinMeters * ThinMeters) continue;
                    run.Add((float)Math.Round(p.x, 1));
                    run.Add((float)Math.Round(p.z, 1));
                    lx = p.x; lz = p.z; have = true;
                    if (p.x < minX) minX = p.x;
                    if (p.x > maxX) maxX = p.x;
                    if (p.z < minZ) minZ = p.z;
                    if (p.z > maxZ) maxZ = p.z;
                }
                Flush();
            }

            // Junctions live under the same track root dv-mp indexes them from; one
            // scan at map build, refs kept for the live state reads.
            try
            {
                var root = RailTrackRegistry.Instance.TrackRootParent;
                _junctions = root != null ? root.GetComponentsInChildren<Junction>() : Array.Empty<Junction>();
            }
            catch { _junctions = Array.Empty<Junction>(); }
            // Thinned to one per cell so a ladder reads as marks instead of a blob.
            var js = new List<float[]>();
            var jseen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var j in _junctions)
            {
                if (j == null) continue;
                var p = j.transform.position - move;
                var k = Mathf.FloorToInt(p.x / JunctionCell) + ":" + Mathf.FloorToInt(p.z / JunctionCell);
                if (!jseen.Add(k)) continue;
                js.Add(new[] { (float)Math.Round(p.x, 1), (float)Math.Round(p.z, 1) });
            }

            // Double track sits about 4m apart, which is well under a pixel at any scale a
            // dispatcher can use, so parallel rails would draw straight on top of each
            // other. The true pairs are found HERE, once, and each line carries a side so
            // the board can fan them apart on screen. This works on whole polylines,
            // unlike the earlier per-segment clustering that mistook one track's own
            // consecutive pieces for neighbours and drew combs.
            var sides = DetectParallel(lines);
            // Only OPEN LINE gets the artificial double-track spread. Yard ladders are
            // real parallel tracks a few metres apart, and fanning them 22px sideways
            // physically rearranged whole yards on the board (owner report: HB's E, F
            // and G ladders drew wrong). Named tracks stay exactly where they are.
            for (int i = 0; i < lines.Count; i++)
                if (lineTrack[i] != null && lineTrack[i].Length > 0 && lineTrack[i][0] != '#')
                    sides[i] = 0;
            _sideByTrack.Clear();
            for (int i = 0; i < lines.Count; i++)
                if (sides[i] != 0 && lineTrack[i] != null) _sideByTrack[lineTrack[i]] = sides[i];
            var lineOut = new List<object>(lines.Count);
            // Each line carries its track id so the board can paint occupied blocks red:
            // the live poll already says which tracks have cars, and the id is the join.
            for (int i = 0; i < lines.Count; i++)
                lineOut.Add(new { id = lineTrack[i], side = sides[i], pts = lines[i] });

            // Every junction and every signal, since the map now draws every rail.
            try { Interlocking.Build(stPos, YardRadius); }
            catch (Exception ex) { Main.LogAlways($"[Interlocking] build failed: {ex.GetType().Name}: {ex.Message}"); }

            // Switch legs are geometry too, so they ride here and are built once, not
            // re-sent on every live poll.
            object legs = null;
            try { legs = Interlocking.LegsPayload(); } catch { }

            Epoch++;
            var payload = new
            {
                hash,
                epoch = Epoch,
                lines = lineOut,
                junctions = js,
                stations,
                legs,
                bounds = new { minX, maxX, minZ, maxZ }
            };
            _geometryBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload));
            _geometryHash = hash;
            Main.LogAlways($"[TrackMap] network built: {lines.Count} rail line(s) ({sides.Count(v => v != 0)} paired as double track), {js.Count} junction(s), "
                + $"{stations.Count} station(s), {_geometryBytes.Length / 1024}KB; the whole railway, yards included.");
            return _geometryBytes;
        }

        /// <summary>Which way a rail is offset on screen, so junctions and signals
        /// standing on it shift the same way instead of stacking on the centreline.
        /// Zero when the rail is single track.</summary>
        internal static int SideOfTrack(string trackId) =>
            trackId != null && _sideByTrack.TryGetValue(trackId, out var s) ? s : 0;

        private static readonly Dictionary<string, int> _sideByTrack =
            new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>
        /// Find rails that genuinely run alongside each other and put them on opposite
        /// sides. Two polylines only pair when many of their points sit within a track
        /// spacing AND their headings agree, which is what stops a curve pairing with
        /// itself. Antiparallel pairs flip the second side so both push apart rather than
        /// both the same way.
        /// </summary>
        private static int[] DetectParallel(List<float[]> lines)
        {
            const float cell = 20f, near = 14f;
            var side = new int[lines.Count];
            var grid = new Dictionary<long, List<int>>();
            long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;
            for (int i = 0; i < lines.Count; i++)
            {
                var p = lines[i];
                for (int k = 0; k + 1 < p.Length; k += 2)
                {
                    var key = Key((int)Math.Floor(p[k] / cell), (int)Math.Floor(p[k + 1] / cell));
                    if (!grid.TryGetValue(key, out var l)) grid[key] = l = new List<int>();
                    l.Add((i << 12) | (k >> 1));
                }
            }
            void DirAt(float[] p, int idx, out float ox, out float oz)
            {
                int n = p.Length / 2;
                int a = Math.Max(0, idx - 1), b = Math.Min(n - 1, idx + 1);
                float dx = p[b * 2] - p[a * 2], dz = p[b * 2 + 1] - p[a * 2 + 1];
                float len = Mathf.Sqrt(dx * dx + dz * dz);
                if (len < 0.001f) { ox = 1f; oz = 0f; } else { ox = dx / len; oz = dz / len; }
            }
            var counts = new Dictionary<long, int>();
            var dots = new Dictionary<long, float>();
            for (int i = 0; i < lines.Count; i++)
            {
                var p = lines[i];
                for (int k = 0; k + 1 < p.Length; k += 2)
                {
                    int cx = (int)Math.Floor(p[k] / cell), cz = (int)Math.Floor(p[k + 1] / cell);
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            if (!grid.TryGetValue(Key(cx + dx, cz + dz), out var bucket)) continue;
                            foreach (var packed in bucket)
                            {
                                int j = packed >> 12, m = packed & 0xFFF;
                                if (j <= i) continue;
                                var q = lines[j];
                                if (m * 2 + 1 >= q.Length) continue;
                                float ddx = q[m * 2] - p[k], ddz = q[m * 2 + 1] - p[k + 1];
                                float d = Mathf.Sqrt(ddx * ddx + ddz * ddz);
                                if (d > near || d < 0.5f) continue;
                                DirAt(p, k >> 1, out var ix, out var iz);
                                DirAt(q, m, out var jx, out var jz);
                                float dot = ix * jx + iz * jz;
                                if (Math.Abs(dot) < 0.94f) continue;
                                long pk = ((long)i << 32) | (uint)j;
                                counts.TryGetValue(pk, out var c); counts[pk] = c + 1;
                                dots.TryGetValue(pk, out var s2); dots[pk] = s2 + dot;
                            }
                        }
                }
            }
            foreach (var kv in counts.OrderByDescending(kv => kv.Value))
            {
                int i = (int)(kv.Key >> 32), j = (int)(kv.Key & 0xFFFFFFFF);
                int need = Math.Max(2, (int)(0.35f * Math.Min(lines[i].Length, lines[j].Length) / 2));
                if (kv.Value < need) continue;
                int orient = dots[kv.Key] >= 0 ? 1 : -1;
                if (side[i] == 0 && side[j] == 0) { side[i] = 1; side[j] = -orient; }
                else if (side[i] != 0 && side[j] == 0) side[j] = -side[i] * orient;
                else if (side[j] != 0 && side[i] == 0) side[i] = -side[j] * orient;
            }
            return side;
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
