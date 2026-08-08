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
                // The LAST point always goes in, even when thinning would drop it: a
                // track's ends are its junctions, and the board matches line ends to
                // junction display positions, so an endpoint ten metres short of its
                // junction reads as a track that does not reach its own switch.
                var endBp = rt.curve[rt.curve.pointCount - 1];
                if (endBp != null && have)
                {
                    var ep = endBp.position - move;
                    if ((ep.x - lx) * (ep.x - lx) + (ep.z - lz) * (ep.z - lz) > 0.01f)
                    {
                        run.Add((float)Math.Round(ep.x, 1));
                        run.Add((float)Math.Round(ep.z, 1));
                    }
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
            var lineOut = new List<object>(lines.Count);
            // Full RD (owner ruling): no artificial double-track spread, no sides. The
            // line is the track, exactly where it is; zoom resolves parallels. Each line
            // keeps its id so occupied blocks paint red and named tracks label themselves.
            for (int i = 0; i < lines.Count; i++)
                lineOut.Add(new { id = lineTrack[i], pts = lines[i] });

            // Every junction and every signal, since the map now draws every rail.
            try { Interlocking.Build(); }
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
            Main.LogAlways($"[TrackMap] network built: {lines.Count} rail line(s), {js.Count} junction(s), "
                + $"{stations.Count} station(s), {_geometryBytes.Length / 1024}KB; the whole railway, yards included.");
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
                // Every car for itself (owner ruling, the RD way): true position, true
                // length, its own heading, its own job. The board draws the vehicle,
                // not a line between two ends.
                var carsOut = new List<object>(cars.Count);
                foreach (var c in cars)
                {
                    if (c == null) continue;
                    var p = c.transform.position - move;
                    var f = c.transform.forward;
                    string jid = null;
                    if (c.logicCar != null && jobsManager != null)
                    {
                        var j = jobsManager.GetJobOfCar(c.logicCar);
                        if (j != null) jid = j.ID;
                    }
                    float len = 20f;
                    try { if (c.logicCar != null) len = c.logicCar.length; } catch { }
                    string type = null;
                    try { type = c.carLivery != null ? c.carLivery.id : null; } catch { }
                    string cid = null;
                    try { cid = c.ID; } catch { }
                    carsOut.Add(new
                    {
                        x = (float)Math.Round(p.x, 1),
                        z = (float)Math.Round(p.z, 1),
                        dx = (float)Math.Round(f.x, 2),
                        dz = (float)Math.Round(f.z, 2),
                        len = (float)Math.Round(len, 1),
                        loco = c.IsLoco,
                        id = cid,
                        type,
                        job = jid,
                    });
                }
                if (carsOut.Count > 0) consists.Add(new { cars = carsOut });
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
