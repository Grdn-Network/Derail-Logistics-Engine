using DLE.Economy;
using DLE.Jobs;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using UnityEngine;

namespace DLE.Dispatch
{
    /// <summary>
    /// Local HTTP API for RemoteDispatch (and curl). Host or singleplayer only, bound to
    /// 127.0.0.1 so nothing is exposed off the machine; RD proxies it later. Requests are
    /// handled on the main thread (coroutine polls BeginGetContext, same proven pattern as
    /// GRDNConnect) so game state can be read safely.
    ///
    /// v1 endpoints:
    ///   GET  /api/v1/state
    ///   GET  /api/v1/economy
    ///   GET  /api/v1/jobs
    ///   PUT  /api/v1/assignments/{jobId}   body {"player":"name","assignedBy":"dispatcher"}
    ///   DELETE /api/v1/assignments/{jobId}
    ///   PUT  /api/v1/lock                  body {"enabled":true}
    /// </summary>
    public class DleHttpServer : MonoBehaviour
    {
        public const int Port = 7246;

        private HttpListener _listener;
        private static GameObject _host;

        public static void StartOnHost()
        {
            if (_host != null) return;
            _host = new GameObject("DLE_HttpServer");
            DontDestroyOnLoad(_host);
            _host.AddComponent<DleHttpServer>();
        }

        public static void Stop()
        {
            if (_host == null) return;
            Destroy(_host);
            _host = null;
        }

        private void Start()
        {
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
                _listener.Start();
                Main.Log($"[Http] listening on 127.0.0.1:{Port}.");
                StartCoroutine(ListenLoop());
            }
            catch (Exception ex)
            {
                Main.Log($"[Http] failed to start on port {Port}: {ex.Message}");
                _listener = null;
            }
        }

        private void OnDestroy()
        {
            try { _listener?.Stop(); _listener?.Close(); } catch { }
            _listener = null;
        }

        private IEnumerator ListenLoop()
        {
            while (_listener != null && _listener.IsListening)
            {
                IAsyncResult async;
                try { async = _listener.BeginGetContext(null, null); }
                catch { yield break; }

                while (!async.IsCompleted) yield return null;

                HttpListenerContext ctx = null;
                try { ctx = _listener.EndGetContext(async); }
                catch (Exception ex) { Main.Log($"[Http] context error: {ex.Message}"); }

                if (ctx != null) Handle(ctx);
            }
        }

        private void Handle(HttpListenerContext ctx)
        {
            string path = ctx.Request.Url.AbsolutePath.TrimEnd('/');
            string method = ctx.Request.HttpMethod;
            try
            {
                if (method == "GET" && path == "/api/v1/state") { Json(ctx, 200, StatePayload()); return; }
                if (method == "GET" && path == "/api/v1/economy") { Json(ctx, 200, EconomyPayload()); return; }
                if (method == "GET" && path == "/api/v1/jobs") { Json(ctx, 200, JobsPayload()); return; }

                const string assignPrefix = "/api/v1/assignments/";
                if (path.StartsWith(assignPrefix, StringComparison.Ordinal))
                {
                    var jobId = path.Substring(assignPrefix.Length);
                    if (method == "PUT")
                    {
                        var body = ReadBody(ctx);
                        var req = JsonConvert.DeserializeObject<AssignRequest>(body ?? "");
                        if (string.IsNullOrEmpty(req?.player)) { Json(ctx, 400, new { error = "player required" }); return; }
                        AssignmentStore.Instance.Assign(jobId, req.player, req.assignedBy ?? "dispatcher");
                        Json(ctx, 200, new { ok = true });
                        return;
                    }
                    if (method == "DELETE")
                    {
                        Json(ctx, AssignmentStore.Instance.Unassign(jobId) ? 200 : 404, new { ok = true });
                        return;
                    }
                }

                if (method == "PUT" && path == "/api/v1/lock")
                {
                    var req = JsonConvert.DeserializeObject<LockRequest>(ReadBody(ctx) ?? "");
                    AssignmentStore.Instance.LockEnabled = req?.enabled ?? false;
                    Main.Log($"[Dispatch] lock {(AssignmentStore.Instance.LockEnabled ? "ENABLED" : "disabled")} via API.");
                    Json(ctx, 200, new { ok = true, lockEnabled = AssignmentStore.Instance.LockEnabled });
                    return;
                }

                Json(ctx, 404, new { error = "not found" });
            }
            catch (Exception ex)
            {
                Main.Log($"[Http] {method} {path} failed: {ex.Message}");
                try { Json(ctx, 500, new { error = ex.Message }); } catch { }
            }
        }

        // Set by JSON deserialization.
        private class AssignRequest { public string player = null; public string assignedBy = null; }
        private class LockRequest { public bool enabled = false; }

        private static object StatePayload() => new
        {
            modVersion = Main.ModEntry?.Info?.Version,
            lockEnabled = AssignmentStore.Instance.LockEnabled,
            stationCount = EconomyState.Instance.Facilities.Count,
            jobCount = StaticDirectHaulJobDefinition.jobDefinitions.Count,
        };

        private static object EconomyPayload()
        {
            var econ = EconomyState.Instance;
            return econ.Facilities.Values.Select(f => new
            {
                yardId = f.YardId,
                outputs = f.Outputs.Select(c => c.ToString()),
                inputs = f.Inputs.Select(c => c.ToString()),
                stock = f.Outputs.Concat(f.Inputs).Distinct()
                    .Select(c => new { cargo = c.ToString(), amount = econ.GetStock(f.YardId, c), cap = f.Cap(c) })
                    .Where(s => s.amount > 0f),
                recipes = f.Recipes.Select(r => new
                {
                    inputs = r.Inputs.Select(i => new { cargo = i.Cargo.ToString(), amount = i.Amount }),
                    outputs = r.Outputs.Select(o => new { cargo = o.Cargo.ToString(), amount = o.Amount }),
                }),
            }).ToList();
        }

        private static object JobsPayload()
        {
            return StaticDirectHaulJobDefinition.jobDefinitions.Select(kv => new
            {
                id = kv.Key,
                origin = kv.Value.chainData?.chainOriginYardId,
                destination = kv.Value.chainData?.chainDestinationYardId,
                cargo = kv.Value.transportedCargo.ToString(),
                cars = kv.Value.carsToTransport?.Count ?? 0,
                wage = kv.Value.initialWage,
                pickupTrack = kv.Value.spawnTrackDisplay,
                state = kv.Value.LiveJob?.State.ToString() ?? "Unknown",
                assignedTo = AssignmentStore.Instance.Get(kv.Key)?.Player,
            }).ToList();
        }

        private static string ReadBody(HttpListenerContext ctx)
        {
            using (var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding ?? Encoding.UTF8))
                return reader.ReadToEnd();
        }

        private static void Json(HttpListenerContext ctx, int status, object payload)
        {
            var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload));
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json";
            ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.OutputStream.Close();
        }
    }
}
