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
                Main.LogAlways($"[Http] failed to start on port {Port}: {ex.Message}");
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
                // CSRF guard: refuse state-changing calls from another web origin. Same-origin
                // board requests and non-browser clients (curl, RemoteDispatch) are allowed.
                if ((method == "POST" || method == "PUT" || method == "DELETE") && IsForeignOrigin(ctx))
                { Json(ctx, 403, new { error = "cross-origin request refused" }); return; }

                if (method == "GET" && (path == "" || path == "/")) { Html(ctx, BoardPage.Html); return; }
                if (method == "GET" && path == "/api/v1/state") { Json(ctx, 200, StatePayload()); return; }
                if (method == "GET" && path == "/api/v1/economy") { Json(ctx, 200, EconomyPayload()); return; }
                if (method == "GET" && path == "/api/v1/jobs") { Json(ctx, 200, JobsPayload()); return; }
                if (method == "GET" && path == "/api/v1/options") { Json(ctx, 200, OptionsPayload()); return; }
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
                    var jobId = EconomyDirector.CreateSpecific(req.origin, req.destination, cargoType, req.cars, req.reserveCars, out var createReason);
                    if (jobId == null) { Json(ctx, 409, new { error = createReason ?? "could not create haul; see game log" }); return; }
                    Json(ctx, 201, new { ok = true, jobId });
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
                    Json(ctx, 201, order);
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
                        Json(ctx, LogisticsBoard.Instance.Delete(id) ? 200 : 404, new { ok = true });
                        return;
                    }
                }

                // Where every car of a job is right now, nearest to the loading track first.
                // This is the dispatcher's logi-coordination view until RD renders car types.
                const string jobCarsPrefix = "/api/v1/jobs/";
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
                    bool wasLocked = AssignmentStore.Instance.LockEnabled;
                    AssignmentStore.Instance.LockEnabled = req?.enabled ?? false;
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
        private class LockRequest { public bool enabled = false; }
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
                        cap = f.Cap(c),
                        reserved = econ.GetReserved(f.YardId, c),
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

        private static void Html(HttpListenerContext ctx, string html)
        {
            var bytes = Encoding.UTF8.GetBytes(html);
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.OutputStream.Close();
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

        private static string ReadBody(HttpListenerContext ctx)
        {
            using (var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding ?? Encoding.UTF8))
                return reader.ReadToEnd();
        }

        // A same-origin board request (or a non-browser client like curl / RemoteDispatch)
        // either sends no Origin header or sends the board's own; anything else is a foreign site.
        private static bool IsForeignOrigin(HttpListenerContext ctx)
        {
            var origin = ctx.Request.Headers["Origin"];
            if (string.IsNullOrEmpty(origin)) return false;
            return origin != $"http://127.0.0.1:{Port}" && origin != $"http://localhost:{Port}";
        }

        private static void Json(HttpListenerContext ctx, int status, object payload)
        {
            var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload));
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.OutputStream.Close();
        }
    }
}
