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
    /// The dispatch board and API, served from a raw TcpListener. Raw sockets on purpose:
    /// Windows' HttpListener rides http.sys, which demands an admin urlacl for any
    /// non-loopback bind (the board silently became unreachable without it) and rejects
    /// any Host header it does not know ("Bad Request (Invalid host)" through tunnels).
    /// A TcpListener needs neither: it binds like any game server does and ignores the
    /// Host header entirely. The password is the only switch: set one and the board binds
    /// the network; blank binds loopback only.
    ///
    /// Threading: sockets are accepted on the main thread (non-blocking Pending poll);
    /// each request is read and parsed on a worker thread (so a slow client can never
    /// stall the game), HANDLED on the main thread (game state is main-thread-only) via
    /// a queue the coroutine drains, and written back on the worker.
    /// </summary>
    public class DleHttpServer : MonoBehaviour
    {
        public const int Port = 7246;

        private System.Net.Sockets.TcpListener _tcp;
        private static GameObject _host;
        private readonly System.Collections.Concurrent.ConcurrentQueue<DleRequest> _pending =
            new System.Collections.Concurrent.ConcurrentQueue<DleRequest>();

        public static void StartOnHost()
        {
            // Recreate per world load, like the director: the old listener closes in
            // OnDestroy (end of frame) before the new component's Start opens the port.
            if (_host != null) Destroy(_host);
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
            bool network = !string.IsNullOrEmpty(Main.Settings?.BoardPassword);
            try
            {
                _tcp = new System.Net.Sockets.TcpListener(
                    network ? IPAddress.Any : IPAddress.Loopback, Port);
                _tcp.Start();
            }
            catch (Exception ex)
            {
                Main.LogAlways($"[Http] failed to start on port {Port} ({ex.GetType().Name}: {ex.Message}); the board is offline. Is another program on the port?");
                _tcp = null;
                return;
            }
            Main.LogAlways(network
                ? $"[Http] board serving on ALL interfaces port {Port} (password set). Plain HTTP: prefer an https tunnel over raw internet exposure."
                : $"[Http] board serving on 127.0.0.1:{Port} (no password: host-only).");
            StartCoroutine(ListenLoop());
        }

        private void OnDestroy()
        {
            try { _tcp?.Stop(); } catch { }
            _tcp = null;
        }

        private IEnumerator ListenLoop()
        {
            while (_tcp != null)
            {
                // Accept everything queued this frame; workers own each socket from here.
                try
                {
                    while (_tcp != null && _tcp.Pending())
                    {
                        var client = _tcp.AcceptTcpClient();
                        System.Threading.ThreadPool.QueueUserWorkItem(_ => ServeClient(client));
                    }
                }
                catch (Exception ex)
                {
                    if (_tcp != null)
                        Main.LogAlways($"[Http] accept failed ({ex.GetType().Name}: {ex.Message}).");
                }

                // Handle parsed requests on the main thread, where game state lives.
                while (_pending.TryDequeue(out var req))
                {
                    try { Handle(req); }
                    catch (Exception ex)
                    {
                        Main.LogAlways($"[Http] {req.Method} {req.Path} failed: {ex.GetType().Name}: {ex.Message}");
                        try { Json(req, 500, new { error = "internal error; see game log" }); } catch { }
                    }
                    if (req.RespBytes == null)
                        Json(req, 404, new { error = "not found" });
                    req.Done.Set();
                }
                yield return null;
            }
        }

        /// <summary>
        /// Worker thread: read and parse one HTTP request, hand it to the main thread,
        /// wait for the handler, write the response, close. All blocking socket IO lives
        /// here so a slow or hostile client stalls only its own worker, never the game.
        /// </summary>
        private void ServeClient(System.Net.Sockets.TcpClient client)
        {
            try
            {
                client.ReceiveTimeout = 10000;
                client.SendTimeout = 10000;
                var stream = client.GetStream();
                var req = ParseRequest(client, stream);
                if (req == null) { WriteRaw(stream, 400, "text/plain", Encoding.UTF8.GetBytes("bad request")); return; }

                _pending.Enqueue(req);
                // The main thread drains the queue every frame; a long stall means the
                // game itself is wedged, so give up rather than hold sockets forever.
                if (!req.Done.Wait(15000))
                { WriteRaw(stream, 504, "text/plain", Encoding.UTF8.GetBytes("game busy")); return; }

                WriteRaw(stream, req.RespStatus, req.RespType, req.RespBytes);
            }
            catch (Exception ex)
            {
                Main.Log($"[Http] client dropped: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                try { client.Close(); } catch { }
            }
        }

        private const int MaxHeaderBytes = 32 * 1024;
        private const int MaxBodyBytes = 2 * 1024 * 1024;

        private DleRequest ParseRequest(System.Net.Sockets.TcpClient client, Stream stream)
        {
            // Read until the blank line ends the headers.
            var buf = new byte[MaxHeaderBytes];
            int used = 0, headerEnd = -1;
            while (headerEnd < 0)
            {
                if (used >= buf.Length) return null;
                int n = stream.Read(buf, used, buf.Length - used);
                if (n <= 0) return null;
                used += n;
                for (int i = 3; i < used; i++)
                    if (buf[i - 3] == '\r' && buf[i - 2] == '\n' && buf[i - 1] == '\r' && buf[i] == '\n')
                    { headerEnd = i + 1; break; }
            }

            var head = Encoding.UTF8.GetString(buf, 0, headerEnd);
            var lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
            var parts = lines[0].Split(' ');
            if (parts.Length < 2) return null;

            var headers = new System.Collections.Specialized.NameValueCollection(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines.Skip(1))
            {
                int c = line.IndexOf(':');
                if (c > 0) headers[line.Substring(0, c).Trim()] = line.Substring(c + 1).Trim();
            }

            // Path and query, percent-decoded per component.
            var rawUrl = parts[1];
            var query = new System.Collections.Specialized.NameValueCollection();
            string path = rawUrl;
            int q = rawUrl.IndexOf('?');
            if (q >= 0)
            {
                path = rawUrl.Substring(0, q);
                foreach (var pair in rawUrl.Substring(q + 1).Split('&'))
                {
                    if (pair.Length == 0) continue;
                    int eq = pair.IndexOf('=');
                    var k = eq >= 0 ? pair.Substring(0, eq) : pair;
                    var v = eq >= 0 ? pair.Substring(eq + 1) : "";
                    query[Uri.UnescapeDataString(k)] = Uri.UnescapeDataString(v.Replace('+', ' '));
                }
            }
            path = Uri.UnescapeDataString(path);

            // Body by Content-Length only (browsers send exactly that for fetch bodies).
            string body = null;
            if (int.TryParse(headers["Content-Length"], out var len) && len > 0)
            {
                if (len > MaxBodyBytes) return null;
                var bodyBytes = new byte[len];
                int have = Math.Min(used - headerEnd, len);
                Array.Copy(buf, headerEnd, bodyBytes, 0, have);
                while (have < len)
                {
                    int n = stream.Read(bodyBytes, have, len - have);
                    if (n <= 0) return null;
                    have += n;
                }
                body = Encoding.UTF8.GetString(bodyBytes);
            }

            bool isLocal = false;
            try
            {
                if (client.Client.RemoteEndPoint is IPEndPoint ep)
                    isLocal = IPAddress.IsLoopback(ep.Address);
            }
            catch { }
            // A tunnel or reverse proxy connects FROM loopback but relays a remote
            // viewer, and every such relay stamps forwarding headers on the way through.
            // Treat those as remote: otherwise anyone holding the tunnel URL reads the
            // whole board (jobs, economy, fleet, crew names) without the password, and
            // only actions ever prompt. A real local caller sends none of these.
            if (isLocal && (headers["X-Forwarded-For"] != null ||
                            headers["Cf-Connecting-Ip"] != null ||
                            headers["X-Real-Ip"] != null))
                isLocal = false;

            return new DleRequest
            {
                Method = parts[0].ToUpperInvariant(),
                Path = path,
                Body = body,
                Request = new RequestShim
                {
                    HttpMethod = parts[0].ToUpperInvariant(),
                    QueryString = query,
                    Headers = headers,
                    IsLocal = isLocal,
                    Url = new UrlShim { AbsolutePath = path },
                },
            };
        }

        private static void WriteRaw(Stream stream, int status, string contentType, byte[] bytes)
        {
            bytes = bytes ?? Array.Empty<byte>();
            var reason = status == 200 ? "OK" : status == 201 ? "Created" : status == 204 ? "No Content"
                : status == 400 ? "Bad Request" : status == 401 ? "Unauthorized" : status == 403 ? "Forbidden"
                : status == 404 ? "Not Found" : status == 409 ? "Conflict" : status == 504 ? "Gateway Timeout"
                : "Internal Server Error";
            var head = Encoding.UTF8.GetBytes(
                $"HTTP/1.1 {status} {reason}\r\nContent-Type: {contentType ?? "application/octet-stream"}\r\n" +
                $"Content-Length: {bytes.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
            stream.Write(head, 0, head.Length);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }

        /// <summary>
        /// One parsed request plus its response slot. The Request/Url shims mirror the
        /// HttpListener property shapes so the routing code reads naturally
        /// (ctx.Request.QueryString, ctx.Request.IsLocal, ctx.Request.Headers).
        /// </summary>
        private class DleRequest
        {
            public string Method;
            public string Path;
            public string Body;
            public RequestShim Request;
            public int RespStatus;
            public string RespType;
            public byte[] RespBytes;
            public readonly System.Threading.ManualResetEventSlim Done =
                new System.Threading.ManualResetEventSlim(false);
        }

        private class RequestShim
        {
            public string HttpMethod;
            public System.Collections.Specialized.NameValueCollection QueryString;
            public System.Collections.Specialized.NameValueCollection Headers;
            public bool IsLocal;
            public UrlShim Url;
        }

        private class UrlShim { public string AbsolutePath; }

        private void Handle(DleRequest ctx)
        {
            string path = ctx.Request.Url.AbsolutePath.TrimEnd('/');
            string method = ctx.Request.HttpMethod;
            try
            {
                // CSRF guard: state-changing calls from another web origin need the board
                // password. A tunneled board is the legitimate case: the tunnel rewrites
                // the Host header but not Origin, so its own page reads as foreign here;
                // the key (which a drive-by site cannot know or send) is what tells the
                // dispatcher's page apart from an attack. No password configured means no
                // foreign mutations at all. Same-origin board requests and non-browser
                // clients (curl, RemoteDispatch) pass untouched.
                if ((method == "POST" || method == "PUT" || method == "DELETE") && IsForeignOrigin(ctx) && !Authorized(ctx))
                {
                    if (!string.IsNullOrEmpty(Main.Settings?.BoardPassword))
                    { Json(ctx, 401, new { error = "password required" }); return; } // board JS prompts and retries
                    Json(ctx, 403, new { error = "cross-origin request refused (set a board password to dispatch through a tunnel)" });
                    return;
                }

                // Network callers must present the board password on every API call; the
                // page itself is public chrome, every fact and lever sits behind the key.
                if (!ctx.Request.IsLocal && path.StartsWith("/api/", StringComparison.Ordinal) && !Authorized(ctx))
                { Json(ctx, 401, new { error = "password required" }); return; }

                if (method == "GET" && (path == "" || path == "/")) { Html(ctx, BoardPage.Html); return; }
                if (method == "GET" && path == "/api/v1/state") { Json(ctx, 200, StatePayload()); return; }
                if (method == "GET" && path == "/api/v1/economy") { Json(ctx, 200, EconomyPayload()); return; }
                if (method == "GET" && path == "/api/v1/jobs") { Json(ctx, 200, JobsPayload()); return; }
                if (method == "GET" && path == "/api/v1/options") { Json(ctx, 200, OptionsPayload()); return; }
                if (method == "GET" && path == "/api/v1/players") { Json(ctx, 200, DispatchFax.GetPlayerNames()); return; }
                if (method == "GET" && path == "/api/v1/fleet")
                {
                    var payload = FleetPayload(ctx.Request.QueryString["cargo"], ctx.Request.QueryString["yard"], out var fleetError);
                    if (payload == null) { Json(ctx, 400, new { error = fleetError }); return; }
                    Json(ctx, 200, payload);
                    return;
                }
                if (method == "GET" && path == "/api/v1/history")
                {
                    int limit = 200;
                    int.TryParse(ctx.Request.QueryString["limit"], out var q);
                    if (q > 0) limit = Math.Min(q, 600);
                    Json(ctx, 200, EconomyHistory.Snapshot(limit));
                    return;
                }

                // Dispatcher-picked priority haul.
                if (method == "POST" && path == "/api/v1/hauls")
                {
                    var req = JsonConvert.DeserializeObject<HaulRequest>(ReadBody(ctx) ?? "");
                    if (req == null || string.IsNullOrEmpty(req.origin) || string.IsNullOrEmpty(req.destination) ||
                        string.IsNullOrEmpty(req.cargo) || req.cars <= 0)
                    { Json(ctx, 400, new { error = "origin, destination, cargo, cars required" }); return; }
                    if (!Enum.TryParse<DV.ThingTypes.CargoType>(req.cargo, out var cargoType))
                    { Json(ctx, 400, new { error = $"unknown cargo '{req.cargo}'" }); return; }
                    var jobId = EconomyDirector.CreateSpecific(req.origin, req.destination, cargoType, req.cars, req.reserveCars, out var createReason, out var unpaidMove);
                    if (jobId == null) { Json(ctx, 409, new { error = createReason ?? "could not create haul; see game log" }); return; }
                    Json(ctx, 201, new { ok = true, jobId, unpaid = unpaidMove });
                    return;
                }

                // Spawn empty pool cars (finite mode fleet management).
                if (method == "POST" && path == "/api/v1/empties")
                {
                    var req = JsonConvert.DeserializeObject<EmptiesRequest>(ReadBody(ctx) ?? "");
                    if (req == null || string.IsNullOrEmpty(req.yardId) || string.IsNullOrEmpty(req.cargo) || req.count <= 0)
                    { Json(ctx, 400, new { error = "yardId, cargo, count required" }); return; }
                    if (!Enum.TryParse<DV.ThingTypes.CargoType>(req.cargo, out var cargoType))
                    { Json(ctx, 400, new { error = $"unknown cargo '{req.cargo}'" }); return; }
                    var sc = StationController.GetStationByYardID(req.yardId);
                    if (sc == null) { Json(ctx, 404, new { error = $"unknown yard '{req.yardId}'" }); return; }
                    int spawned = Data.DleCarPool.Instance.SpawnEmpties(sc, cargoType, req.count);
                    Json(ctx, spawned > 0 ? 201 : 409, new { ok = spawned > 0, spawned, poolSize = Data.DleCarPool.Instance.Count });
                    return;
                }

                // Logistics board: unpaid, bookletless coordination runs.
                if (path == "/api/v1/logistics" && method == "GET")
                { Json(ctx, 200, LogisticsBoard.Instance.All); return; }
                if (path == "/api/v1/logistics" && method == "POST")
                {
                    var req = JsonConvert.DeserializeObject<LogisticsRequest>(ReadBody(ctx) ?? "");
                    if (req == null || string.IsNullOrEmpty(req.from) || string.IsNullOrEmpty(req.to) || req.cars <= 0)
                    { Json(ctx, 400, new { error = "from, to, cars required" }); return; }
                    var order = LogisticsBoard.Instance.Create(req.from, req.to, req.cars, req.cargo, req.note);

                    // Paper for the run: a zero-pay vanilla EmptyHaul over idle empties at
                    // the origin. Vanilla type so it renders and syncs everywhere. When no
                    // suitable empties stand idle, the run stays a coordination note.
                    string bookletNote = null;
                    var fromSc = StationController.GetStationByYardID(req.from);
                    var toSc = StationController.GetStationByYardID(req.to);
                    if (fromSc != null && toSc != null)
                    {
                        DV.ThingTypes.CargoType? forCargo = null;
                        if (!string.IsNullOrEmpty(req.cargo) && Enum.TryParse<DV.ThingTypes.CargoType>(req.cargo, out var ct))
                            forCargo = ct;
                        order.JobId = Jobs.DirectHaulGenerator.TryCreateLogiRun(
                            fromSc, toSc, forCargo, req.cars, out bookletNote, out var bound);
                        if (order.JobId != null && bound < req.cars)
                            bookletNote = $"booklet {order.JobId} covers {bound} of {req.cars} car(s); the rest had no idle empties on that track";
                    }
                    Json(ctx, 201, new { order.Id, order.FromYardId, order.ToYardId, order.CarCount,
                        order.Cargo, order.Note, order.Status, order.JobId, bookletNote });
                    return;
                }
                const string logisticsPrefix = "/api/v1/logistics/";
                if (path.StartsWith(logisticsPrefix, StringComparison.Ordinal))
                {
                    var id = path.Substring(logisticsPrefix.Length);
                    if (method == "PUT")
                    {
                        var req = JsonConvert.DeserializeObject<StatusRequest>(ReadBody(ctx) ?? "");
                        if (string.IsNullOrEmpty(req?.status)) { Json(ctx, 400, new { error = "status required" }); return; }
                        Json(ctx, LogisticsBoard.Instance.SetStatus(id, req.status) ? 200 : 404, new { ok = true });
                        return;
                    }
                    if (method == "DELETE")
                    {
                        // Deleting a run must also expire its backing EmptyHaul booklet,
                        // or the bound cars stay on a job forever and the picker can
                        // never use them again.
                        var order = LogisticsBoard.Instance.All.FirstOrDefault(o => o.Id == id);
                        string freed = null;
                        if (!string.IsNullOrEmpty(order?.JobId))
                        {
                            var jm = DV.Utils.SingletonBehaviour<DV.Logic.Job.JobsManager>.Instance;
                            var job = jm?.jobToJobCars.Keys.FirstOrDefault(j => j != null && j.ID == order.JobId);
                            if (job != null && job.State == DV.ThingTypes.JobState.Available)
                            {
                                try { job.ExpireJob(); freed = order.JobId; Main.LogAlways($"[Logistics] {id}: booklet {order.JobId} expired with the run; cars freed."); }
                                catch (Exception ex) { Main.LogAlways($"[Logistics] {id}: could not expire {order.JobId}: {ex.GetType().Name}: {ex.Message}"); }
                            }
                            else if (job != null)
                                Main.LogAlways($"[Logistics] {id} deleted but booklet {order.JobId} is {job.State}; the crew keeps it.");
                        }
                        Json(ctx, LogisticsBoard.Instance.Delete(id) ? 200 : 404, new { ok = true, freedBooklet = freed });
                        return;
                    }
                }

                // Where every car of a job is right now, nearest to the loading track first.
                // This is the dispatcher's logi-coordination view until RD renders car types.
                const string jobCarsPrefix = "/api/v1/jobs/";

                // Delete an open haul from the board (the per-job cousin of the lock
                // purge): the booklet expires and its supply hold returns to the pile.
                if (method == "DELETE" && path.StartsWith(jobCarsPrefix, StringComparison.Ordinal) &&
                    path.IndexOf('/', jobCarsPrefix.Length) < 0)
                {
                    var jobId = path.Substring(jobCarsPrefix.Length);
                    var r = DispatchLifecycle.DeleteHaul(jobId);
                    Json(ctx, r.Ok ? 200 : 409, new { ok = r.Ok, message = r.Message });
                    return;
                }

                if (method == "GET" && path.StartsWith(jobCarsPrefix, StringComparison.Ordinal) &&
                    path.EndsWith("/cars", StringComparison.Ordinal))
                {
                    var jobId = path.Substring(jobCarsPrefix.Length,
                        path.Length - jobCarsPrefix.Length - "/cars".Length);
                    if (!StaticDirectHaulJobDefinition.jobDefinitions.TryGetValue(jobId, out var def))
                    { Json(ctx, 404, new { error = $"unknown job '{jobId}'" }); return; }
                    Json(ctx, 200, JobCarsPayload(def));
                    return;
                }

                // Remote lifecycle: dispatch takes and turns in hauls from the board (#30).
                if (method == "POST" && path.StartsWith(jobCarsPrefix, StringComparison.Ordinal) &&
                    path.EndsWith("/take", StringComparison.Ordinal))
                {
                    var jobId = path.Substring(jobCarsPrefix.Length,
                        path.Length - jobCarsPrefix.Length - "/take".Length);
                    var req = JsonConvert.DeserializeObject<TakeRequest>(ReadBody(ctx) ?? "");
                    var r = DispatchLifecycle.TakeJob(jobId, req?.player);
                    Json(ctx, r.Ok ? 200 : 409, new { ok = r.Ok, message = r.Message });
                    return;
                }
                if (method == "POST" && path.StartsWith(jobCarsPrefix, StringComparison.Ordinal) &&
                    path.EndsWith("/complete", StringComparison.Ordinal))
                {
                    var jobId = path.Substring(jobCarsPrefix.Length,
                        path.Length - jobCarsPrefix.Length - "/complete".Length);
                    var r = DispatchLifecycle.CompleteJob(jobId);
                    Json(ctx, r.Ok ? 200 : 409, new { ok = r.Ok, message = r.Message });
                    return;
                }

                // Suitable empties for a carless haul, nearest to the loading track first,
                // with positions so a picker can chain-sort by proximity to a chosen car.
                if (method == "GET" && path.StartsWith(jobCarsPrefix, StringComparison.Ordinal) &&
                    path.EndsWith("/candidates", StringComparison.Ordinal))
                {
                    var jobId = path.Substring(jobCarsPrefix.Length,
                        path.Length - jobCarsPrefix.Length - "/candidates".Length);
                    if (!StaticDirectHaulJobDefinition.jobDefinitions.TryGetValue(jobId, out var def))
                    { Json(ctx, 404, new { error = $"unknown job '{jobId}'" }); return; }
                    Json(ctx, 200, CandidatesPayload(def));
                    return;
                }

                // Dispatch servicing: load and unload a haul remotely (#43). Body may name
                // the exact cars to load; empty body keeps the automatic pick.
                if (method == "POST" && path.StartsWith(jobCarsPrefix, StringComparison.Ordinal) &&
                    path.EndsWith("/load", StringComparison.Ordinal))
                {
                    var jobId = path.Substring(jobCarsPrefix.Length,
                        path.Length - jobCarsPrefix.Length - "/load".Length);
                    var req = JsonConvert.DeserializeObject<LoadRequest>(ReadBody(ctx) ?? "");
                    var r = DispatchServicing.LoadJob(jobId, req?.cars);
                    Json(ctx, r.Ok ? 200 : 409, new { ok = r.Ok, message = r.Message });
                    return;
                }
                if (method == "POST" && path.StartsWith(jobCarsPrefix, StringComparison.Ordinal) &&
                    path.EndsWith("/unload", StringComparison.Ordinal))
                {
                    var jobId = path.Substring(jobCarsPrefix.Length,
                        path.Length - jobCarsPrefix.Length - "/unload".Length);
                    var r = DispatchServicing.UnloadJob(jobId);
                    Json(ctx, r.Ok ? 200 : 409, new { ok = r.Ok, message = r.Message });
                    return;
                }

                // Fax the booklet to a player (#33). Empty player = the local player.
                if (method == "POST" && path.StartsWith(jobCarsPrefix, StringComparison.Ordinal) &&
                    path.EndsWith("/fax", StringComparison.Ordinal))
                {
                    var jobId = path.Substring(jobCarsPrefix.Length,
                        path.Length - jobCarsPrefix.Length - "/fax".Length);
                    var req = JsonConvert.DeserializeObject<TakeRequest>(ReadBody(ctx) ?? "");
                    var r = DispatchFax.Fax(jobId, req?.player);
                    Json(ctx, r.Ok ? 200 : 409, new { ok = r.Ok, message = r.Message });
                    return;
                }

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
                        // Echo what the store actually recorded (issue #79 forensics): the
                        // caller can verify the assignment landed under the id it expects.
                        Json(ctx, 200, new { ok = true, jobId, assignedTo = AssignmentStore.Instance.Get(jobId)?.Player });
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
                    if (req?.enabled == null)
                    { Json(ctx, 400, new { error = "enabled (true or false) required" }); return; }
                    bool wasLocked = AssignmentStore.Instance.LockEnabled;
                    AssignmentStore.Instance.LockEnabled = req.enabled.Value;
                    Main.Log($"[Dispatch] lock {(AssignmentStore.Instance.LockEnabled ? "ENABLED" : "disabled")} via API.");

                    // Lock ON clears the public job board: the office papers are swept, so
                    // the unassigned jobs behind them expire too. Dispatch-assigned work
                    // survives, and the director stays paused while the lock holds.
                    int purged = 0;
                    if (!wasLocked && AssignmentStore.Instance.LockEnabled)
                        purged = DispatchLifecycle.ExpireUnassignedAvailable();

                    Json(ctx, 200, new { ok = true, lockEnabled = AssignmentStore.Instance.LockEnabled, purged });
                    return;
                }

                Json(ctx, 404, new { error = "not found" });
            }
            catch (Exception ex)
            {
                Main.LogAlways($"[Http] {method} {path} failed: {ex.Message}");
                try { Json(ctx, 500, new { error = ex.Message }); } catch { }
            }
        }

        // Set by JSON deserialization.
        private class AssignRequest { public string player = null; public string assignedBy = null; }
        private class TakeRequest { public string player = null; }
        private class LockRequest { public bool? enabled = null; }
        private class HaulRequest { public string origin = null; public string destination = null; public string cargo = null; public int cars = 0; public List<string> reserveCars = null; }
        private class EmptiesRequest { public string yardId = null; public string cargo = null; public int count = 0; }
        private class LogisticsRequest { public string from = null; public string to = null; public int cars = 0; public string cargo = null; public string note = null; }
        private class LoadRequest { public List<string> cars = null; }
        private class StatusRequest { public string status = null; }

        /// <summary>
        /// Every freight car in the world with its track and availability, optionally
        /// narrowed to the car types that can load a given cargo and to one yard. A car
        /// is usable when it is empty, jobless, unreserved and not player-spawned.
        /// </summary>
        private static object FleetPayload(string cargoFilter, string yardFilter, out string error)
        {
            error = null;
            List<DV.ThingTypes.TrainCarType_v2> loadable = null;
            if (!string.IsNullOrEmpty(cargoFilter))
            {
                if (!Enum.TryParse<DV.ThingTypes.CargoType>(cargoFilter, out var cargoType))
                { error = $"unknown cargo '{cargoFilter}'"; return null; }
                if (!DV.Globals.G.Types.CargoType_to_v2.TryGetValue(cargoType, out var v2) ||
                    !DV.Globals.G.Types.CargoToLoadableCarTypes.TryGetValue(v2, out loadable))
                { error = $"no car type carries {cargoType}"; return null; }
            }

            var reservedBy = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in StaticDirectHaulJobDefinition.jobDefinitions)
                if (kv.Value?.reservedCarIds != null)
                    foreach (var rid in kv.Value.reservedCarIds)
                        reservedBy[rid] = kv.Key;

            var jobsManager = DV.Utils.SingletonBehaviour<DV.Logic.Job.JobsManager>.Instance;
            var rows = new List<(string sortKey, object row)>();
            int usable = 0;
            foreach (var pair in TrainCarRegistry.Instance.logicCarToTrainCar)
            {
                var car = pair.Key;
                var tc = pair.Value;
                if (car == null || tc == null || tc.IsLoco) continue;
                if (loadable != null && (car.carType?.parentType == null || !loadable.Contains(car.carType.parentType))) continue;

                var trackId = car.CurrentTrack?.ID;
                if (!string.IsNullOrEmpty(yardFilter) &&
                    !string.Equals(trackId?.yardId, yardFilter, StringComparison.OrdinalIgnoreCase)) continue;

                var jobOfCar = jobsManager != null ? jobsManager.GetJobOfCar(car) : null;
                reservedBy.TryGetValue(car.ID, out var holder);
                bool free = car.LoadedCargoAmount == 0f && jobOfCar == null && holder == null && !car.playerSpawnedCar;
                if (free) usable++;
                rows.Add(($"{trackId?.yardId}|{trackId?.FullDisplayID}|{car.ID}", new
                {
                    carId = car.ID,
                    type = tc.carLivery?.name ?? car.carType?.parentType?.name ?? "?",
                    yard = trackId?.yardId,
                    track = trackId?.FullDisplayID ?? "in motion",
                    loadedCargo = car.LoadedCargoAmount > 0f ? car.CurrentCargoTypeInCar.ToString() : null,
                    jobId = jobOfCar?.ID,
                    reservedBy = holder,
                    playerSpawned = car.playerSpawnedCar,
                    usable = free,
                }));
            }
            rows.Sort((a, b) => string.CompareOrdinal(a.sortKey, b.sortKey));
            return new
            {
                cargo = string.IsNullOrEmpty(cargoFilter) ? null : cargoFilter,
                total = rows.Count,
                usable,
                cars = rows.Select(r => r.row).ToList(),
            };
        }

        private static object OptionsPayload() =>
            EconomyDirector.GetOptions().Select(o => new
            {
                origin = o.Origin,
                cargo = o.Cargo.ToString(),
                stock = o.Stock,
                consumers = o.Consumers,
                unpaidOnly = o.UnpaidOnly,
            }).ToList();

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
                source = f.IsSource,
                // Storage is one shared pool per station (#92): the board scales every
                // stock bar against the same total.
                totalCap = f.TotalCap,
                totalStock = econ.TotalStock(f.YardId),
                outputs = f.Outputs.Select(c => c.ToString()),
                inputs = f.Inputs.Select(c => c.ToString()),
                boosters = f.Boosters.Select(b => new
                {
                    cargo = b.Cargo.Select(c => c.ToString()),
                    speedup = b.Speedup,
                    active = b.Cargo.Any(c => econ.GetStock(f.YardId, c) >= 1f),
                }),
                stock = f.Outputs.Concat(f.Inputs).Distinct()
                    .Select(c => new
                    {
                        cargo = c.ToString(),
                        amount = econ.GetStock(f.YardId, c),
                        reserved = econ.GetReserved(f.YardId, c),
                        imported = econ.GetImported(f.YardId, c),
                        // Empty piles show when a recipe needs them: an idle factory's
                        // missing ingredient is information, an empty warehouse is noise.
                        required = f.Recipes.Any(r => r.Inputs.Any(i => i.Cargo == c)),
                    })
                    .Where(s => s.amount > 0f || s.required),
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
                plannedCars = kv.Value.plannedCarCount,
                awaitingEmpties = kv.Value.includeLoadTask && (kv.Value.carsToTransport?.Count ?? 0) == 0,
                wage = kv.Value.deliveryPayment,
                unpaid = kv.Value.unpaidMove,
                tonnes = LoadedTrainTonnes(kv.Value),
                loadedCars = kv.Value.carsToTransport?.Count(c => c.LoadedCargoAmount > 0f) ?? 0,
                pickupTrack = kv.Value.spawnTrackDisplay,
                state = kv.Value.LiveJob?.State.ToString() ?? "Unknown",
                assignedTo = AssignmentStore.Instance.Get(kv.Key)?.Player,
                reservedCars = kv.Value.reservedCarIds,
            }).ToList();
        }

        /// <summary>
        /// Loaded train mass in tonnes: tare plus cargo (capacity times the cargo's mass
        /// per unit), from the attached cars when they exist, else from the booklet's
        /// display cars. Same formula the vanilla booklet stats use.
        /// </summary>
        private static int LoadedTrainTonnes(StaticDirectHaulJobDefinition def)
        {
            try
            {
                float perUnit = 0f;
                if (DV.Globals.G.Types.CargoType_to_v2.TryGetValue(def.transportedCargo, out var v2) && v2 != null)
                    perUnit = v2.massPerUnit;

                float kg = 0f;
                if (def.carsToTransport != null && def.carsToTransport.Count > 0)
                {
                    foreach (var car in def.carsToTransport)
                        kg += (car.carType?.parentType?.mass ?? 0f) + car.capacity * perUnit;
                }
                else if (def.displayCars != null)
                {
                    foreach (var cd in def.displayCars)
                        kg += cd.carOnlyMass + cd.capacity * perUnit;
                }
                return (int)Math.Round(kg / 1000f);
            }
            catch { return 0; }
        }

        private static void Html(DleRequest ctx, string html)
        {
            ctx.RespStatus = 200;
            ctx.RespType = "text/html; charset=utf-8";
            ctx.RespBytes = Encoding.UTF8.GetBytes(html);
        }

        /// <summary>
        /// Suitable empties for a carless haul with everything a picker needs: distance
        /// to the loading track for the initial sort, world x/z so the client can re-sort
        /// by proximity to the last chosen car, and the staff seconds-per-car so it can
        /// estimate load time before committing.
        /// </summary>
        private static object CandidatesPayload(StaticDirectHaulJobDefinition def)
        {
            var originYard = def.chainData?.chainOriginYardId;
            var sc = originYard != null ? StationController.GetStationByYardID(originYard) : null;
            int wanted = def.plannedCarCount > 0 ? def.plannedCarCount : def.displayCars?.Count ?? 0;
            float perCar = 45f;
            if (originYard != null && EconomyState.Instance.Facilities.TryGetValue(originYard, out var fac))
                perCar = fac.RemoteSecondsPerCar;

            UnityEngine.Vector3? loadPos = null;
            var loadTrack = def.loadMachine?.WarehouseTrack;
            if (loadTrack != null && RailTrackRegistry.LogicToRailTrack.TryGetValue(loadTrack, out var loadRail))
                loadPos = loadRail.transform.position;

            var rows = new List<object>();
            bool attached = (def.carsToTransport?.Count ?? 0) > 0;
            var pool = (!attached && sc != null) ? DispatchServicing.AllLoadCandidates(def, sc) : null;
            if (pool != null)
            {
                foreach (var car in pool)
                {
                    var tc = car.TrainCar();
                    float meters = -1f;
                    UnityEngine.Vector3 pos = default;
                    if (tc != null)
                    {
                        pos = tc.transform.position;
                        if (loadPos.HasValue) meters = UnityEngine.Vector3.Distance(pos, loadPos.Value);
                    }
                    rows.Add(new
                    {
                        carId = car.ID,
                        type = tc?.carLivery?.name ?? car.carType?.parentType?.name ?? "?",
                        track = car.CurrentTrack?.ID?.FullDisplayID ?? "in motion",
                        metersFromLoading = meters < 0f ? (float?)null : (float)Math.Round(meters),
                        x = (float)Math.Round(pos.x, 1),
                        z = (float)Math.Round(pos.z, 1),
                    });
                }
            }
            return new
            {
                jobId = def.LiveJob?.ID,
                origin = originYard,
                wanted,
                perCarSeconds = perCar,
                loadingTrack = loadTrack?.ID?.FullDisplayID,
                carsAttached = attached,
                cars = rows,
            };
        }

        private static object JobCarsPayload(StaticDirectHaulJobDefinition def)
        {
            // World-space distance from each car to the loading track is a good enough
            // sort key for coordination (not rail distance, but stable and cheap).
            UnityEngine.Vector3? loadPos = null;
            var loadTrack = def.loadMachine?.WarehouseTrack;
            if (loadTrack != null && RailTrackRegistry.LogicToRailTrack.TryGetValue(loadTrack, out var loadRail))
                loadPos = loadRail.transform.position;

            var rows = new List<object>();
            if (def.carsToTransport != null)
            {
                var sortable = new List<(float meters, object row)>();
                foreach (var car in def.carsToTransport)
                {
                    var tc = car.TrainCar();
                    float meters = -1f;
                    if (loadPos.HasValue && tc != null)
                        meters = UnityEngine.Vector3.Distance(tc.transform.position, loadPos.Value);
                    sortable.Add((meters < 0f ? float.MaxValue : meters, new
                    {
                        carId = car.ID,
                        type = tc?.carLivery?.name ?? car.carType?.parentType?.name ?? "?",
                        loaded = car.LoadedCargoAmount > 0f,
                        track = car.CurrentTrack?.ID?.FullDisplayID ?? "in motion",
                        metersFromLoading = meters < 0f ? (float?)null : (float)System.Math.Round(meters),
                    }));
                }
                sortable.Sort((a, b) => a.meters.CompareTo(b.meters));
                foreach (var s in sortable) rows.Add(s.row);
            }
            return new
            {
                jobId = def.LiveJob?.ID,
                loadingTrack = loadTrack?.ID?.FullDisplayID,
                cars = rows,
            };
        }

        // The body was already read on the worker thread; nothing here can block the game.
        private static string ReadBody(DleRequest ctx) => ctx.Body;

        // A same-origin board request (or a non-browser client like curl / RemoteDispatch)
        // either sends no Origin header or sends the board's own; anything else is a foreign site.
        private static bool IsForeignOrigin(DleRequest ctx)
        {
            var origin = ctx.Request.Headers["Origin"];
            if (string.IsNullOrEmpty(origin)) return false;
            if (origin == $"http://127.0.0.1:{Port}" || origin == $"http://localhost:{Port}") return false;
            // A remote dispatcher's browser sends the board's own host as its origin.
            var host = ctx.Request.Headers["Host"];
            return string.IsNullOrEmpty(host) || origin != $"http://{host}";
        }

        private static bool Authorized(DleRequest ctx)
        {
            // The password stands on its own: RemoteBoard only controls the network BIND.
            // A tunnel points at the loopback listener with RemoteBoard off, and its
            // dispatcher still authenticates with the key.
            var s = Main.Settings;
            if (s == null || string.IsNullOrEmpty(s.BoardPassword)) return false;
            var key = ctx.Request.Headers["X-DLE-Key"] ?? ctx.Request.QueryString["key"];
            return string.Equals(key, s.BoardPassword, StringComparison.Ordinal);
        }

        private static void Json(DleRequest ctx, int status, object payload)
        {
            ctx.RespStatus = status;
            ctx.RespType = "application/json";
            ctx.RespBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload));
        }
    }
}
