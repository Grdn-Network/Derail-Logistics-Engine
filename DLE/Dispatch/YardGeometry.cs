using DV.Utils;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DLE.Dispatch
{
    /// <summary>
    /// Real per-yard rail geometry (Desk v10 backbone): the station's tracks as world
    /// polylines plus the junctions that join them, so the board can draw the actual
    /// ladder instead of a synthetic fan. Built once per yard per world and cached as
    /// serialized bytes keyed by the game's track hash (the old TrackMap's scan-once
    /// rule); positions subtract WorldMover.currentMove at build time, which keeps
    /// them stable when the streaming origin shifts later. Read-only throughout.
    /// </summary>
    internal static class YardGeometry
    {
        // Yard scale: ladder switches live metres apart, so thinning is far tighter
        // than the old whole-railway map's 10m.
        private const float ThinMeters = 2f;
        // Connector rails and ladder throats sit just outside the named tracks; the
        // box grows by this much and anonymous rails inside it ride along.
        private const float PadMeters = 70f;

        private class Built { public string Hash; public byte[] Bytes; }
        private static readonly Dictionary<string, Built> _cache =
            new Dictionary<string, Built>(StringComparer.OrdinalIgnoreCase);
        private static Junction[] _junctions = Array.Empty<Junction>();

        public static byte[] Payload(string yardId, out string error)
        {
            error = null;
            var sc = StationController.GetStationByYardID(yardId ?? "");
            if (sc == null) { error = $"unknown yard '{yardId}'"; return null; }
            yardId = sc.stationInfo?.YardID ?? yardId;

            string hash = null;
            try { hash = SingletonBehaviour<RailTrackRegistryBase>.Instance?.TracksHash; } catch { }
            if (_cache.TryGetValue(yardId, out var built) && built.Hash == hash && JunctionsAlive())
                return built.Bytes;

            var move = WorldMover.currentMove;

            // The same track set the yard view serves: warehouse tracks included,
            // matched by yard id exactly like YardPayload does (#218).
            var logicTracks = DispatchServicing.StationTracks(sc, null);
            var warehouse = new HashSet<DV.Logic.Job.Track>();
            try
            {
                foreach (var c in WarehouseMachineController.allControllers)
                {
                    var wt = c?.warehouseMachine?.WarehouseTrack;
                    if (wt?.ID == null || !string.Equals(wt.ID.yardId, yardId, StringComparison.OrdinalIgnoreCase)) continue;
                    warehouse.Add(wt);
                    if (!logicTracks.Contains(wt)) logicTracks.Add(wt);
                }
            }
            catch { }

            var named = new Dictionary<RailTrack, DV.Logic.Job.Track>();
            foreach (var lt in logicTracks)
            {
                if (lt == null) continue;
                if (RailTrackRegistry.LogicToRailTrack.TryGetValue(lt, out var rt)
                    && rt != null && rt.curve != null && rt.curve.pointCount >= 2)
                    named[rt] = lt;
            }
            if (named.Count == 0) { error = $"no rails resolved for '{yardId}' (world still loading?)"; return null; }

            // Bounds of the named rails, grown by the pad: everything the ladder
            // needs, throats and crossovers included, sits inside this box.
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var rt in named.Keys)
                for (int i = 0; i < rt.curve.pointCount; i++)
                {
                    var bp = rt.curve[i];
                    if (bp == null) continue;
                    var p = bp.position - move;
                    if (p.x < minX) minX = p.x;
                    if (p.x > maxX) maxX = p.x;
                    if (p.z < minZ) minZ = p.z;
                    if (p.z > maxZ) maxZ = p.z;
                }
            minX -= PadMeters; maxX += PadMeters; minZ -= PadMeters; maxZ += PadMeters;
            bool Inside(Vector3 p) => p.x >= minX && p.x <= maxX && p.z >= minZ && p.z <= maxZ;

            // Named rails first, then every anonymous rail wholly inside the box:
            // those are the throats and junction connectors between the named tracks.
            var rails = new List<RailTrack>(named.Keys);
            try
            {
                foreach (var rt in SingletonBehaviour<RailTrackRegistryBase>.Instance.OrderedRailtracks)
                {
                    if (rt == null || named.ContainsKey(rt) || rt.curve == null || rt.curve.pointCount < 2) continue;
                    var a = rt.curve[0];
                    var b = rt.curve[rt.curve.pointCount - 1];
                    if (a == null || b == null) continue;
                    if (Inside(a.position - move) && Inside(b.position - move)) rails.Add(rt);
                }
            }
            catch { }

            var index = new Dictionary<RailTrack, int>();
            var trackRows = new List<object>();
            foreach (var rt in rails)
            {
                var pts = new List<float>();
                float lx = 0f, lz = 0f;
                bool have = false;
                for (int i = 0; i < rt.curve.pointCount; i++)
                {
                    var bp = rt.curve[i];
                    if (bp == null) continue;
                    var p = bp.position - move;
                    bool last = i == rt.curve.pointCount - 1;
                    // Ends always go in (they are where the junctions sit); the
                    // middle thins so the payload stays a drawing, not a survey.
                    if (have && !last && (p.x - lx) * (p.x - lx) + (p.z - lz) * (p.z - lz) < ThinMeters * ThinMeters) continue;
                    pts.Add((float)Math.Round(p.x, 1));
                    pts.Add((float)Math.Round(p.z, 1));
                    lx = p.x; lz = p.z; have = true;
                }
                if (pts.Count < 4) continue;
                index[rt] = trackRows.Count;
                named.TryGetValue(rt, out var lt);
                trackRows.Add(new
                {
                    id = lt?.ID?.FullDisplayID,
                    lengthM = (int)Math.Round(lt != null ? lt.length : (double)rt.curve.length),
                    warehouse = lt != null && warehouse.Contains(lt),
                    connector = lt == null,
                    junctionTrack = rt.isJunctionTrack,
                    pts,
                });
            }

            // Junctions under the game's track root, scanned once and kept; only the
            // ones inside the box ride out, legs as indexes into tracks[] (-1 when a
            // leg leaves the box toward open line).
            if (_junctions.Length == 0 || !JunctionsAlive())
            {
                try
                {
                    var root = RailTrackRegistry.Instance.TrackRootParent;
                    _junctions = root != null ? root.GetComponentsInChildren<Junction>() : Array.Empty<Junction>();
                }
                catch { _junctions = Array.Empty<Junction>(); }
            }
            var junctionRows = new List<object>();
            foreach (var j in _junctions)
            {
                if (j == null) continue;
                var p = j.transform.position - move;
                if (!Inside(p)) continue;
                int Leg(RailTrack rt) => rt != null && index.TryGetValue(rt, out var ix) ? ix : -1;
                var outs = new List<object>();
                if (j.outBranches != null)
                    foreach (var b in j.outBranches)
                        outs.Add(new { t = Leg(b?.track), first = b?.first ?? false });
                junctionRows.Add(new
                {
                    x = (float)Math.Round(p.x, 1),
                    z = (float)Math.Round(p.z, 1),
                    selected = (int)j.selectedBranch,
                    inT = Leg(j.inBranch?.track),
                    outs,
                });
            }

            var payload = new
            {
                yard = yardId,
                hash,
                bounds = new
                {
                    minX = (float)Math.Round(minX, 1),
                    maxX = (float)Math.Round(maxX, 1),
                    minZ = (float)Math.Round(minZ, 1),
                    maxZ = (float)Math.Round(maxZ, 1),
                },
                tracks = trackRows,
                junctions = junctionRows,
            };
            var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload));
            _cache[yardId] = new Built { Hash = hash, Bytes = bytes };
            Main.Log($"[YardGeometry] {yardId}: {trackRows.Count} rail(s), {junctionRows.Count} junction(s), {bytes.Length / 1024}KB built and cached.");
            return bytes;
        }

        /// <summary>Unity fake-nulls destroyed refs after a world reload within one
        /// session; a dead first entry means the whole set must be rescanned.</summary>
        private static bool JunctionsAlive() => _junctions.Length == 0 || _junctions[0] != null;
    }
}
