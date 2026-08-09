using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace DLE.Data
{
    /// <summary>
    /// The lag meter (#141 verification tooling): allocation-free frame sampling on the
    /// director, per-second GC and heap snapshots, and per-endpoint board handler time.
    /// Read it three ways: the perf chip on the board header, the perf block in
    /// /api/v1/state, and company.lag in the console (one shot dump, or 'watch' for a
    /// 10 second periodic log line to correlate with what you are doing in the world).
    /// A real Unity profiler needs a development build of the game; this is the
    /// practical in-mod equivalent for the numbers that matter to the fps floor hunt.
    /// </summary>
    public static class PerfMeter
    {
        // Frame ring: raw unscaled delta times, main thread only.
        private const int FrameN = 2048;
        private static readonly float[] _frames = new float[FrameN];
        private static readonly float[] _sortBuf = new float[FrameN];
        private static int _head;
        private static int _filled;

        // Per-second ring: GC count, heap, hitch count, 3 minutes of history.
        private const int SecN = 180;
        private static readonly int[] _gc = new int[SecN];
        private static readonly float[] _heapMb = new float[SecN];
        private static readonly int[] _hitches = new int[SecN];
        private static int _secHead;
        private static int _secFilled;
        private static float _secAccum;
        private static int _hitchAccum;

        // Board handler time by route, cumulative for the session.
        private static readonly Dictionary<string, long> _reqMs = new Dictionary<string, long>(StringComparer.Ordinal);
        private static readonly Dictionary<string, int> _reqN = new Dictionary<string, int>(StringComparer.Ordinal);

        public static bool Watch;
        private static float _watchAccum;

        /// <summary>Called every frame from the director. No allocations.</summary>
        public static void Sample(float dt)
        {
            _frames[_head] = dt;
            _head = (_head + 1) % FrameN;
            if (_filled < FrameN) _filled++;
            if (dt > 0.05f) _hitchAccum++;

            _secAccum += dt;
            if (_secAccum >= 1f)
            {
                _secAccum = 0f;
                _gc[_secHead] = GC.CollectionCount(0);
                _heapMb[_secHead] = GC.GetTotalMemory(false) / (1024f * 1024f);
                _hitches[_secHead] = _hitchAccum;
                _hitchAccum = 0;
                _secHead = (_secHead + 1) % SecN;
                if (_secFilled < SecN) _secFilled++;

                if (Watch)
                {
                    _watchAccum += 1f;
                    if (_watchAccum >= 10f) { _watchAccum = 0f; Main.LogAlways("[Lag] " + OneLine()); }
                }
            }
        }

        /// <summary>Board handler time, keyed by route shape (ids stripped).</summary>
        public static void RecordRequest(string path, long ms)
        {
            // Key by route shape (/api/v1/route, ids stripped) without Split's string[]
            // garbage: cut once at the fourth slash, allocate nothing otherwise (#211).
            var key = path ?? "/";
            int slashes = 0;
            for (int i = 0; i < key.Length; i++)
                if (key[i] == '/' && ++slashes == 4) { key = key.Substring(0, i); break; }
            _reqMs.TryGetValue(key, out var t); _reqMs[key] = t + ms;
            _reqN.TryGetValue(key, out var n); _reqN[key] = n + 1;
        }

        public static (float p50, float p95, float max) FramePercentiles()
        {
            if (_filled == 0) return (0, 0, 0);
            Array.Copy(_frames, _sortBuf, _filled);
            Array.Sort(_sortBuf, 0, _filled);
            float p50 = _sortBuf[(int)(_filled * 0.50f)] * 1000f;
            float p95 = _sortBuf[Math.Min(_filled - 1, (int)(_filled * 0.95f))] * 1000f;
            float max = _sortBuf[_filled - 1] * 1000f;
            return (p50, p95, max);
        }

        /// <summary>Sum or delta over the last <paramref name="seconds"/> of the second ring.</summary>
        private static int SumRing(int[] ring, int seconds)
        {
            int take = Math.Min(seconds, _secFilled);
            int total = 0;
            for (int i = 1; i <= take; i++)
                total += ring[(_secHead - i + SecN) % SecN];
            return total;
        }

        public static int Gc60()
        {
            int take = Math.Min(60, _secFilled);
            if (take < 2) return 0;
            int newest = _gc[(_secHead - 1 + SecN) % SecN];
            int oldest = _gc[(_secHead - take + SecN) % SecN];
            return Math.Max(0, newest - oldest);
        }

        public static int Hitches60() => SumRing(_hitches, 60);

        public static float HeapMb() =>
            _secFilled == 0 ? 0f : _heapMb[(_secHead - 1 + SecN) % SecN];

        public static int LiveCars()
        {
            try { return TrainCarRegistry.Instance?.logicCarToTrainCar?.Count ?? 0; }
            catch { return 0; }
        }

        public static object StatePayload()
        {
            var (p50, p95, max) = FramePercentiles();
            return new
            {
                frameP50Ms = Math.Round(p50, 1),
                frameP95Ms = Math.Round(p95, 1),
                frameMaxMs = Math.Round(max, 1),
                hitches60s = Hitches60(),
                gc60s = Gc60(),
                heapMb = Math.Round(HeapMb()),
                liveCars = LiveCars(),
            };
        }

        public static string OneLine()
        {
            var (p50, p95, max) = FramePercentiles();
            return $"frame p50 {p50:0.0}ms p95 {p95:0.0}ms max {max:0.0}ms; " +
                   $"{Hitches60()} hitch(es)/60s; {Gc60()} GC/60s; heap {HeapMb():0}MB; " +
                   $"{LiveCars()} live car(s), {DleCarPool.Instance.DormantCount} dormant";
        }

        public static string FullReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[Lag] " + OneLine());
            if (_reqMs.Count > 0)
            {
                sb.AppendLine("[Lag] board handler time this session (main thread):");
                foreach (var kv in _reqMs.OrderByDescending(kv => kv.Value).Take(8))
                    sb.AppendLine($"[Lag]   {kv.Key}: {kv.Value}ms TOTAL over {_reqN[kv.Key]} request(s), avg {(kv.Value / (float)Math.Max(1, _reqN[kv.Key])):0.0}ms each");
            }
            return sb.ToString().TrimEnd();
        }
    }
}
