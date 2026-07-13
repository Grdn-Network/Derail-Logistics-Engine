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
                if (method == "GET" && (path == "" || path == "/")) { Html(ctx, DashboardPage); return; }
                if (method == "GET" && path == "/api/v1/state") { Json(ctx, 200, StatePayload()); return; }
                if (method == "GET" && path == "/api/v1/economy") { Json(ctx, 200, EconomyPayload()); return; }
                if (method == "GET" && path == "/api/v1/jobs") { Json(ctx, 200, JobsPayload()); return; }
                if (method == "GET" && path == "/api/v1/options") { Json(ctx, 200, OptionsPayload()); return; }
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
                    var jobId = EconomyDirector.CreateSpecific(req.origin, req.destination, cargoType, req.cars);
                    if (jobId == null) { Json(ctx, 409, new { error = "could not create haul; see game log" }); return; }
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
        private class HaulRequest { public string origin = null; public string destination = null; public string cargo = null; public int cars = 0; }
        private class EmptiesRequest { public string yardId = null; public string cargo = null; public int count = 0; }
        private class LogisticsRequest { public string from = null; public string to = null; public int cars = 0; public string cargo = null; public string note = null; }
        private class StatusRequest { public string status = null; }

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
                plannedCars = kv.Value.plannedCarCount,
                awaitingEmpties = kv.Value.includeLoadTask && (kv.Value.carsToTransport?.Count ?? 0) == 0,
                wage = kv.Value.initialWage,
                pickupTrack = kv.Value.spawnTrackDisplay,
                state = kv.Value.LiveJob?.State.ToString() ?? "Unknown",
                assignedTo = AssignmentStore.Instance.Get(kv.Key)?.Player,
            }).ToList();
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

        // Minimal built-in dispatch board so the economy is fully manageable without
        // RemoteDispatch. RD integration supersedes this later.
        private const string DashboardPage = @"<!doctype html><html><head><meta charset='utf-8'>
<title>DLE Dispatch</title>
<style>
body{font-family:Segoe UI,Arial,sans-serif;background:#1b1d21;color:#ddd;margin:16px}
h1{font-size:20px;color:#b58ee0} h2{font-size:15px;margin:18px 0 6px;color:#9fc2e8}
table{border-collapse:collapse;width:100%;font-size:13px}
td,th{border:1px solid #333;padding:4px 8px;text-align:left}
th{background:#26282e} tr:nth-child(even){background:#22242a}
button{background:#5a3f78;color:#fff;border:0;padding:4px 10px;cursor:pointer;border-radius:3px}
input,select{background:#26282e;color:#ddd;border:1px solid #444;padding:3px 6px}
#msg{color:#8fd18f;min-height:18px}
</style></head><body>
<h1>Derail Logistics Engine: dispatch board</h1><div id='msg'></div>
<h2>Create a haul</h2>
<div>Origin <select id='hOrigin'></select> Cargo <select id='hCargo'></select>
Destination <select id='hDest'></select> Cars <input id='hCars' type='number' value='4' min='1' max='20' style='width:60px'>
<button onclick='createHaul()'>Spawn haul</button></div>
<h2>Shippable now (options)</h2><table id='tOptions'></table>
<h2>Active hauls <button onclick='toggleLock()' id='bLock'>Lock: ?</button></h2><table id='tJobs'></table>
<pre id='carsOut' style='background:#22242a;padding:6px;display:none'></pre>
<h2>Logistics runs (unpaid, no booklet)</h2>
<div>From <input id='lFrom' style='width:50px'> To <input id='lTo' style='width:50px'>
Cars <input id='lCars' type='number' value='4' min='1' style='width:55px'>
For cargo <input id='lCargo' style='width:110px'> Note <input id='lNote' style='width:220px'>
<button onclick='createLog()'>Post run</button></div>
<table id='tLog'></table>
<h2>Economy</h2><table id='tEcon'></table>
<script>
let options=[];let lockOn=false;
async function j(u,m,b){const r=await fetch(u,{method:m||'GET',body:b?JSON.stringify(b):undefined});return r.json()}
function msg(t){document.getElementById('msg').textContent=t;setTimeout(()=>msg2(t),4000)}
function msg2(t){const m=document.getElementById('msg');if(m.textContent===t)m.textContent=''}
async function refresh(){
 const state=await j('/api/v1/state');lockOn=state.lockEnabled;
 document.getElementById('bLock').textContent='Lock: '+(lockOn?'ON':'off');
 options=await j('/api/v1/options');
 const jobs=await j('/api/v1/jobs'); const econ=await j('/api/v1/economy'); const logs=await j('/api/v1/logistics');
 const oSel=document.getElementById('hOrigin');const cur=oSel.value;
 oSel.innerHTML=options.map(o=>`<option>${o.origin}</option>`).join('');
 if([...oSel.options].some(x=>x.value===cur))oSel.value=cur;
 originChanged();
 document.getElementById('tOptions').innerHTML='<tr><th>Origin</th><th>Cargo</th><th>Stock</th><th>Consumers</th></tr>'+
  options.map(o=>`<tr><td>${o.origin}</td><td>${o.cargo}</td><td>${o.stock}</td><td>${o.consumers.join(', ')}</td></tr>`).join('');
 document.getElementById('tJobs').innerHTML='<tr><th>Job</th><th>Route</th><th>Cargo</th><th>Cars</th><th>Wage</th><th>Pickup</th><th>State</th><th>Assigned</th><th></th></tr>'+
  jobs.map(x=>`<tr><td>${x.id}</td><td>${x.origin} to ${x.destination}</td><td>${x.cargo}</td><td>${x.cars||x.plannedCars}</td><td>$${Math.round(x.wage)}</td><td>${x.pickupTrack||''}</td><td>${x.state}</td><td>${x.assignedTo||''}</td>`+
   `<td><input id='a_${x.id}' placeholder='player' style='width:90px'><button onclick=""assign('${x.id}')"">Assign</button><button onclick=""unassign('${x.id}')"">X</button><button onclick=""showCars('${x.id}')"">Cars</button></td></tr>`).join('');
 document.getElementById('tLog').innerHTML='<tr><th>Id</th><th>Route</th><th>Cars</th><th>Cargo</th><th>Note</th><th>Status</th><th></th></tr>'+
  logs.map(o=>`<tr><td>${o.Id}</td><td>${o.FromYardId} to ${o.ToYardId}</td><td>${o.CarCount}</td><td>${o.Cargo||''}</td><td>${o.Note||''}</td><td>${o.Status}</td>`+
   `<td><button onclick=""logStatus('${o.Id}','InProgress')"">Start</button><button onclick=""logStatus('${o.Id}','Done')"">Done</button><button onclick=""logDel('${o.Id}')"">Del</button></td></tr>`).join('');
 document.getElementById('tEcon').innerHTML='<tr><th>Yard</th><th>Stock</th></tr>'+
  econ.filter(e=>e.stock.length).map(e=>`<tr><td>${e.yardId}</td><td>${e.stock.map(s=>s.amount+' '+s.cargo).join(', ')}</td></tr>`).join('');
}
async function assign(id){const p=document.getElementById('a_'+id).value;if(!p){msg('enter a player name');return}
 const r=await j('/api/v1/assignments/'+id,'PUT',{player:p,assignedBy:'board'});msg(r.ok?('Assigned '+id+' to '+p):'assign failed');refresh()}
async function unassign(id){await j('/api/v1/assignments/'+id,'DELETE');msg('Unassigned '+id);refresh()}
async function toggleLock(){const r=await j('/api/v1/lock','PUT',{enabled:!lockOn});msg('Lock now '+(r.lockEnabled?'ON':'off'));refresh()}
async function createLog(){const b={from:lFrom.value,to:lTo.value,cars:parseInt(lCars.value),cargo:lCargo.value||null,note:lNote.value||null};
 if(!b.from||!b.to){msg('from and to required');return}
 const r=await j('/api/v1/logistics','POST',b);msg(r.Id?('Posted '+r.Id):'failed');refresh()}
async function logStatus(id,s){await j('/api/v1/logistics/'+id,'PUT',{status:s});refresh()}
async function showCars(id){
 const r=await j('/api/v1/jobs/'+id+'/cars');const o=document.getElementById('carsOut');
 o.style.display='block';
 o.textContent=id+' loading track: '+(r.loadingTrack||'?')+'\n'+
  (r.cars.length?r.cars.map(c=>`${c.carId}  ${c.type}  ${c.loaded?'LOADED':'empty'}  on ${c.track}`+(c.metersFromLoading!=null?`  ${c.metersFromLoading}m from loading`:''))
   .join('\n'):'no cars attached yet (bring empties to the loading track)');}
async function logDel(id){await j('/api/v1/logistics/'+id,'DELETE');refresh()}
function originChanged(){
 const o=document.getElementById('hOrigin').value;
 const mine=options.filter(x=>x.origin===o);
 document.getElementById('hCargo').innerHTML=mine.map(x=>`<option>${x.cargo}</option>`).join('');
 cargoChanged();
}
function cargoChanged(){
 const o=document.getElementById('hOrigin').value;const c=document.getElementById('hCargo').value;
 const opt=options.find(x=>x.origin===o&&x.cargo===c);
 document.getElementById('hDest').innerHTML=(opt?opt.consumers:[]).map(x=>`<option>${x}</option>`).join('');
}
document.getElementById('hOrigin').addEventListener('change',originChanged);
document.getElementById('hCargo').addEventListener('change',cargoChanged);
async function createHaul(){
 const b={origin:hOrigin.value,destination:hDest.value,cargo:hCargo.value,cars:parseInt(hCars.value)};
 const r=await j('/api/v1/hauls','POST',b);
 msg(r.jobId?('Created '+r.jobId):('Failed: '+(r.error||'see game log')));
 refresh();
}
refresh();setInterval(refresh,5000);
</script></body></html>";

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
