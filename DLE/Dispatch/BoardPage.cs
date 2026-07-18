namespace DLE.Dispatch
{
    /// <summary>
    /// The built-in dispatch board served at / by DleHttpServer. One self-contained page:
    /// inline styles and script, no external assets, so it works offline and through the
    /// RemoteDispatch proxy. Talks only to the v1 API endpoints. RemoteDispatch
    /// integration supersedes this board later.
    /// </summary>
    internal static class BoardPage
    {
        public const string Html = @"
<!doctype html><html lang='en'><head><meta charset='utf-8'>
<meta name='viewport' content='width=device-width,initial-scale=1'>
<meta name='theme-color' content='#0e1116'>
<title>DLE Dispatch</title>
<style>
:root{--bg:#0e1116;--panel:#141a22;--panel2:#1a2129;--line:#232d3a;--line2:#313d4e;
--text:#dbe3ec;--dim:#8b95a5;--violet:#a98ff0;--vdeep:#3a2a63;--amber:#e8b64c;
--green:#6fce8f;--red:#e07a6a;--blue:#63a5e8}
*{box-sizing:border-box}
html{scrollbar-color:var(--line2) var(--bg)}
body{margin:0;background:var(--bg);color:var(--text);
font:14px/1.45 -apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif}
header{position:sticky;top:0;z-index:5;display:flex;align-items:center;gap:12px;
padding:10px 18px;background:rgba(14,17,22,.92);backdrop-filter:blur(6px);
border-bottom:1px solid var(--line)}
.brand{font-weight:800;font-size:17px;letter-spacing:.06em;color:var(--violet);white-space:nowrap}
.brand span{color:var(--dim);font-weight:600;margin-left:6px;letter-spacing:.22em;font-size:12px}
.dot{width:8px;height:8px;border-radius:50%;background:var(--green);flex:none}
.dot.bad{background:var(--red)}
.chip{background:var(--panel2);border:1px solid var(--line);border-radius:999px;
padding:2px 10px;font-size:12px;color:var(--dim);white-space:nowrap}
.spacer{flex:1}
button{font:inherit;cursor:pointer;border-radius:6px;border:1px solid var(--line2);
background:transparent;color:var(--text);padding:5px 12px;transition:border-color .15s,background .15s}
button:hover{border-color:var(--violet)}
button.primary{background:var(--vdeep);border-color:#54418c}
button.primary:hover{background:#473378}
button.mini{padding:2px 9px;font-size:12px;color:var(--dim)}
button.mini.danger{color:#e06c6c;border-color:#8a3d3d}
button.mini:hover{color:var(--text)}
.lockbtn{font-weight:700;letter-spacing:.05em;font-size:12px;padding:6px 14px}
.lockbtn.on{background:#4a3a14;border-color:var(--amber);color:var(--amber)}
main{max-width:1280px;margin:0 auto;padding:16px;display:grid;gap:14px;
grid-template-columns:repeat(12,1fr)}
.card{background:var(--panel);border:1px solid var(--line);border-radius:12px;padding:14px 16px}
.col5{grid-column:span 5}.col6{grid-column:span 6}.col7{grid-column:span 7}.col12{grid-column:span 12}
@media(max-width:900px){.col5,.col6,.col7{grid-column:span 12}}
h2{margin:0 0 10px;font-size:13px;font-weight:700;letter-spacing:.1em;
text-transform:uppercase;color:var(--dim)}
h2 .sub{font-weight:400;letter-spacing:0;text-transform:none;margin-left:8px;font-size:12px}
h2 .count{color:var(--violet);margin-left:6px}
main section[data-sec] h2{cursor:pointer;user-select:none}
main section[data-sec] h2:before{content:'\25BE';margin-right:7px;color:var(--line2)}
main section[data-sec].closed h2:before{content:'\25B8'}
main section[data-sec].closed>*:not(h2){display:none}
main section[data-sec].closed h2{margin-bottom:0}
label{display:flex;flex-direction:column;gap:3px;font-size:12px;color:var(--dim)}
input,select{font:inherit;background:var(--panel2);color:var(--text);
border:1px solid var(--line2);border-radius:6px;padding:5px 8px;min-width:0}
input:focus,select:focus{outline:none;border-color:var(--violet)}
.formrow{display:flex;gap:10px;flex-wrap:wrap;align-items:flex-end}
.formrow button{margin-bottom:1px}
.tablewrap{overflow-x:auto}
table{border-collapse:collapse;width:100%;font-size:13px}
th{text-align:left;color:var(--dim);font-weight:600;font-size:11px;letter-spacing:.08em;
text-transform:uppercase;padding:4px 10px;border-bottom:1px solid var(--line)}
td{padding:6px 10px;border-bottom:1px solid var(--line)}
tr:last-child td{border-bottom:0}
tr.pick{cursor:pointer}
tr.pick:hover td{background:var(--panel2)}
.num{font-variant-numeric:tabular-nums}
.cards{display:grid;gap:12px;grid-template-columns:repeat(auto-fill,minmax(360px,1fr))}
@media(max-width:460px){.cards{grid-template-columns:1fr}}
.job{background:var(--panel);border:1px solid var(--line);border-radius:12px;
padding:12px 14px;display:flex;flex-direction:column;gap:8px}
.jobtop{display:flex;align-items:center;gap:8px}
.jid{font-weight:700;letter-spacing:.03em}
.wage{margin-left:auto;font-weight:700;color:var(--green)}
.pill{font-size:11px;font-weight:700;letter-spacing:.06em;border-radius:999px;
padding:2px 9px;text-transform:uppercase}
.pill.available{background:#16283c;color:var(--blue)}
.pill.inprogress{background:#3a2e12;color:var(--amber)}
.pill.completed{background:#173424;color:var(--green)}
.pill.other{background:var(--panel2);color:var(--dim)}
.tag{font-size:11px;border-radius:999px;padding:2px 9px;background:#3a2e12;color:var(--amber)}
.route{font-size:16px}
.route .arr{color:var(--dim);margin:0 8px}
.meta{font-size:12.5px;color:var(--dim)}
.meta b{color:var(--text);font-weight:600}
.acts{display:flex;gap:6px;flex-wrap:wrap;align-items:center;
border-top:1px solid var(--line);padding-top:9px;margin-top:2px}
.crew{width:96px;padding:4px 8px;font-size:12.5px}
.carsbox{background:var(--panel2);border:1px solid var(--line);border-radius:8px;
padding:8px 10px;font-size:12.5px}
.carsbox table{font-size:12.5px}
.carsbox th,.carsbox td{padding:3px 8px}
.loadpill{font-size:10px;font-weight:700;border-radius:4px;padding:1px 6px}
.loadpill.yes{background:#173424;color:var(--green)}
.loadpill.no{background:var(--panel);color:var(--dim);border:1px solid var(--line2)}
.empty{color:var(--dim);font-size:13px;padding:8px 2px}
.carchip{display:inline-block;border:1px solid var(--line2);border-radius:4px;
padding:1px 7px;margin:2px 4px 2px 0;font-size:12px;cursor:default}
.carchip.ok{border-color:#2c5c3f;color:var(--green)}
.carchip.busy{color:var(--dim)}
#net{background:radial-gradient(circle,#1b2430 1px,transparent 1px);background-size:26px 26px;
border:1px solid var(--line);border-radius:8px}
#net text{font-family:inherit;user-select:none;pointer-events:none}
.nnode{cursor:pointer}
.nedge{cursor:pointer}
.netdetail{display:none;border-top:1px solid var(--line);margin-top:10px;padding-top:9px;font-size:12.5px}
.netdetail.show{display:block}
.nrecipe{margin:4px 0}
.nrecipe b{font-weight:600}
.nmiss{color:var(--red)}
.econ{display:grid;gap:12px}
.sublab{font-size:10px;font-weight:700;letter-spacing:.08em;text-transform:uppercase;
color:var(--dim);margin:4px 0 2px}
.yard .yhead{font-weight:700;margin-bottom:5px}
.stockrow{display:grid;grid-template-columns:110px 1fr 84px;gap:10px;
align-items:center;padding:2px 0;font-size:12.5px}
.stockrow .cname{color:var(--dim);overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.bar{height:7px;border-radius:4px;background:var(--panel2);border:1px solid var(--line);overflow:hidden}
.bar i{display:block;height:100%;background:linear-gradient(90deg,var(--blue),var(--violet))}
.bar i.full{background:var(--amber)}
.nums{text-align:right;color:var(--dim)}
#toasts{position:fixed;right:16px;bottom:16px;display:flex;flex-direction:column;
gap:8px;z-index:10;max-width:340px}
.toast{background:var(--panel2);border:1px solid var(--line2);border-left:3px solid var(--green);
border-radius:8px;padding:9px 13px;font-size:13px;box-shadow:0 4px 16px rgba(0,0,0,.4);
animation:tin .18s ease-out}
.toast.err{border-left-color:var(--red)}
@keyframes tin{from{opacity:0;transform:translateY(6px)}to{opacity:1;transform:none}}
footer{max-width:1280px;margin:0 auto;padding:4px 16px 22px;color:var(--dim);font-size:12px}
</style></head><body>
<header>
 <div class='brand'>DLE<span>DISPATCH</span></div>
 <div class='dot' id='dot' title='board connection'></div>
 <span class='chip' id='chipVer'></span>
 <span class='chip' id='chipStations'></span>
 <span class='chip' id='chipJobs'></span>
 <div class='spacer'></div>
 <button class='lockbtn' id='bLock' data-act='lock'
  title='When ON, crews can only accept hauls assigned to them and Company Haul papers leave the station offices. Faxed booklets still work.'>LOCK &middot; &hellip;</button>
</header>
<main>
 <section class='card col12' data-sec='create'>
  <h2>Create a haul</h2>
  <div class='formrow'>
   <label>Origin<select id='hOrigin'></select></label>
   <label>Cargo<select id='hCargo'></select></label>
   <label>Destination<select id='hDest'></select></label>
   <label>Cars<input id='hCars' type='number' value='4' min='1' max='20' style='width:64px'></label>
   <button class='primary' data-act='spawnHaul'>Spawn haul</button>
  </div>
 </section>
 <section class='card col12' data-sec='net'>
  <h2>Network <span class='sub'>the whole economy lives here: click a station for its recipes, storage and stock; click a route to fill the haul form</span></h2>
  <svg id='net' viewBox='0 0 1040 760' style='width:100%;height:auto;max-height:78vh'></svg>
  <div id='netDetail' class='netdetail'></div>
 </section>
 <section class='col12' data-sec='acc'>
  <h2>Accepted hauls <span class='count' id='cAcc'></span></h2>
  <div class='cards' id='accCards'></div>
 </section>
 <section class='card col12' data-sec='logi'>
  <h2>Logistics runs <span class='sub'>unpaid coordination, no booklet</span></h2>
  <div class='formrow' style='margin-bottom:10px'>
   <label>From<input id='lFrom' style='width:64px'></label>
   <label>To<input id='lTo' style='width:64px'></label>
   <label>Cars<input id='lCars' type='number' value='4' min='1' style='width:60px'></label>
   <label>For cargo<input id='lCargo' style='width:110px'></label>
   <label>Note<input id='lNote' style='width:180px'></label>
   <button data-act='postRun'>Post run</button>
  </div>
  <div class='tablewrap'><table id='tLog'></table></div>
 </section>
 <section class='card col12' id='finder' data-sec='finder'>
  <h2>Car finder <span class='sub'>compatible freight cars anywhere in the world; results are a snapshot, click Find to refresh; blank the cargo field to clear</span></h2>
  <div class='formrow' style='margin-bottom:10px'>
   <label>Cargo<select id='fCargo'></select></label>
   <label>Yard<input id='fYard' style='width:70px' placeholder='any'></label>
   <button class='primary' data-act='findCars'>Find</button>
   <span class='meta' id='fSummary'></span>
  </div>
  <div class='tablewrap'><table id='tFleet'></table></div>
 </section>
 <section class='col12' data-sec='avail'>
  <h2>Available hauls <span class='count' id='cAvail'></span></h2>
  <div class='cards' id='availCards'></div>
 </section>
 <section class='card col12' data-sec='dlog'>
  <h2>Dispatch log <span class='sub'>production, conversion, loading, deliveries; newest first</span></h2>
  <div class='formrow' style='margin-bottom:6px'>
   <label>Type<select id='dlType'>
    <option value=''>all</option>
    <option value='production'>produced</option>
    <option value='converted'>made</option>
    <option value='delivered'>received</option>
    <option value='loaded'>loaded</option>
    <option value='unloaded'>unloaded</option>
    <option value='haul_created'>haul posted</option>
   </select></label>
   <label>Yard<input id='dlYard' style='width:70px' placeholder='any'></label>
  </div>
  <div id='dlog' style='max-height:260px;overflow-y:auto;font-size:12.5px'></div>
 </section>
</main>
<footer>Derail Logistics Engine &middot; refreshes every 5s</footer>
<div id='toasts'></div>
<datalist id='crewNames'></datalist>
<script>
const $=id=>document.getElementById(id);
const esc=s=>String(s==null?'':s).replace(/[&<>']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;',""'"":'&#39;'}[c]));
let options=[],lockOn=false,expanded=new Set(),pickOpen=new Set(),pickers={},last={},lastJobs=[];
async function authedFetch(u,m,b){
 const mk=()=>{const h={};const k=localStorage.getItem('dleKey');if(k)h['X-DLE-Key']=k;
  return {method:m||'GET',body:b?JSON.stringify(b):undefined,headers:h}};
 let r=await fetch(u,mk());
 if(r.status===401){
  // Re-read the key first: prompt() blocks the single JS thread, so a sibling request
  // that already prompted has stored it by the time this one runs. Without this the
  // first refresh (seven parallel calls) popped seven password prompts.
  let k=localStorage.getItem('dleKey');
  if(!k){const p=prompt('Board password');if(p){localStorage.setItem('dleKey',p);k=p}}
  if(k)r=await fetch(u,mk())}
 return r}
async function j(u,m,b){return (await authedFetch(u,m,b)).json()}
// Polling reads must FAIL on a non-2xx, or an error body ({error:...}) flows into the
// render as data (options.map is not a function) and the board freezes half-drawn while
// the connection dot still shows green. Actions keep using j(): they read the body on
// failure to surface the server's message.
async function jget(u){const r=await authedFetch(u);if(!r.ok)throw new Error('HTTP '+r.status);return r.json()}
function toast(t,err){const d=document.createElement('div');d.className='toast'+(err?' err':'');
 d.textContent=t;$('toasts').appendChild(d);setTimeout(()=>d.remove(),4200)}
function pillClass(s){s=(s||'').toLowerCase();
 return s==='available'?'available':s==='inprogress'?'inprogress':s==='completed'?'completed':'other'}
function money(x){return '$'+Math.round(x||0).toLocaleString('en-US')}
function jobCard(x,avail){
 const cars=x.cars||x.plannedCars||0;
 const acts=avail
  ?`<button class='primary' data-act='take' data-id='${esc(x.id)}'>Take</button>`
  :`<button data-act='${x.awaitingEmpties?'pickCars':'load'}' data-id='${esc(x.id)}'>${x.awaitingEmpties?(pickOpen.has(x.id)?'Close picker':'Load&hellip;'):'Load'}</button>
    <button data-act='unload' data-id='${esc(x.id)}'>Unload</button>
    <button class='primary' data-act='complete' data-id='${esc(x.id)}'>Turn in</button>`;
 return `<div class='job'>
  <div class='jobtop'><span class='jid'>${esc(x.id)}</span>
   <span class='pill ${pillClass(x.state)}'>${esc(x.state)}</span>
   ${x.unpaid?`<span class='pill other' title='Relocating received goods; delivery pays nothing'>unpaid move</span>`:''}
   ${x.awaitingEmpties?`<span class='tag'>awaiting empties</span>`:''}
   ${!x.awaitingEmpties&&x.cars>0&&x.loadedCars>=x.cars?`<span class='pill completed'>loaded</span>`:''}
   ${!x.awaitingEmpties&&x.cars>0&&x.loadedCars>0&&x.loadedCars<x.cars?`<span class='tag'>loading ${x.loadedCars}/${x.cars}</span>`:''}
   <span class='wage num'${x.unpaid?` style='color:var(--dim)'`:''}>${money(x.wage)}</span></div>
  <div class='route'><b>${esc(x.origin)}</b><span class='arr'>&#8594;</span><b>${esc(x.destination)}</b></div>
  <div class='meta'><b>${esc(x.cargo)}</b> &middot; ${cars} cars${x.tonnes?` &middot; ${x.tonnes} t loaded`:''}${x.pickupTrack?` &middot; pickup <b>${esc(x.pickupTrack)}</b>`:''}</div>
  <div class='meta'>${x.assignedTo?`crew: <b>${esc(x.assignedTo)}</b>`:'unassigned'}</div>
  <div class='acts'>${acts}
   <button data-act='fax' data-id='${esc(x.id)}' title='Fax the booklet: typed name first, else the assigned crew, else you'>Fax</button>
   <button class='mini' data-act='cars' data-id='${esc(x.id)}'>${expanded.has(x.id)?'Hide cars':'Cars'}</button>
   <button class='mini' data-act='findEmpties' data-id='${esc(x.id)}' title='Show every compatible car in the world for this cargo'>Find empties</button>
   <input class='crew' id='a_${esc(x.id)}' placeholder='crew name' list='crewNames'>
   <button class='mini' data-act='assign' data-id='${esc(x.id)}'>Assign</button>
   <button class='mini' data-act='unassign' data-id='${esc(x.id)}' title='Clear assignment'>Unassign</button>
   <button class='mini danger' data-act='delhaul' data-id='${esc(x.id)}' title='Delete this haul; its supply returns to the pile'>&times;</button>
  </div>
  ${expanded.has(x.id)?`<div class='carsbox' id='cars_${esc(x.id)}'>fetching&hellip;</div>`:''}
  ${pickOpen.has(x.id)?`<div class='carsbox' id='pick_${esc(x.id)}'>fetching&hellip;</div>`:''}
 </div>`}
function snapshotCrew(){const m={};document.querySelectorAll('.crew').forEach(i=>{if(i.value)m[i.id]=i.value});
 const f=document.activeElement;return{m,focus:f&&f.classList&&f.classList.contains('crew')?f.id:null}}
function restoreCrew(s){for(const id in s.m){const i=$(id);if(i)i.value=s.m[id]}
 if(s.focus){const i=$(s.focus);if(i){i.focus();i.setSelectionRange(i.value.length,i.value.length)}}}
function keepSelect(sel,items){const cur=sel.value;
 sel.innerHTML=items.map(v=>`<option>${esc(v)}</option>`).join('');
 if([...sel.options].some(o=>o.value===cur))sel.value=cur}
async function refresh(){
 let state,jobs,econ,logs,hist;
 let crews;
 try{[state,options,jobs,econ,logs,hist,crews]=await Promise.all([
  jget('/api/v1/state'),jget('/api/v1/options'),jget('/api/v1/jobs'),jget('/api/v1/economy'),jget('/api/v1/logistics'),jget('/api/v1/history?limit=60'),jget('/api/v1/players')]);
  $('dot').className='dot'}
 catch(e){$('dot').className='dot bad';return}
 lastJobs=jobs;
 const cKey=JSON.stringify(crews||[]);
 if(last.crews!==cKey){last.crews=cKey;
  $('crewNames').innerHTML=(crews||[]).map(n=>`<option>${esc(n)}</option>`).join('')}
 lockOn=!!state.lockEnabled;
 $('bLock').textContent='LOCK '+(lockOn?'ON':'OFF');
 $('bLock').className='lockbtn'+(lockOn?' on':'');
 $('chipVer').textContent='v'+(state.modVersion||'?');
 $('chipStations').textContent=state.stationCount+' stations';
 $('chipJobs').textContent=state.jobCount+' hauls';
 keepSelect($('hOrigin'),[...new Set(options.map(o=>o.origin))]);
 originChanged();
 keepSelect($('fCargo'),['','any cargo'].concat([...new Set([].concat(options.map(o=>o.cargo),jobs.map(x=>x.cargo)))].sort()));
 lastEconData=econ;
 const netKey=JSON.stringify(options)+JSON.stringify(econ);
 if(last.net!==netKey){last.net=netKey;drawNet()}
 const jKey=JSON.stringify(jobs)+[...expanded].join();
 if(last.jobs!==jKey){last.jobs=jKey;
  const snap=snapshotCrew();
  const av=jobs.filter(x=>x.state==='Available'),ac=jobs.filter(x=>x.state!=='Available');
  $('cAvail').textContent=av.length||'';$('cAcc').textContent=ac.length||'';
  $('availCards').innerHTML=av.length?av.map(x=>jobCard(x,true)).join(''):`<div class='empty'>${lockOn?'lock is on: the director is paused; create hauls above and assign them to crews':'no open hauls; spawn one above or wait for the director'}</div>`;
  $('accCards').innerHTML=ac.length?ac.map(x=>jobCard(x,false)).join(''):`<div class='empty'>nothing accepted yet</div>`;
  restoreCrew(snap)}
 for(const id of expanded)fillCars(id);
 for(const id of pickOpen)fillPicker(id);
 const lKey=JSON.stringify(logs);
 if(last.logs!==lKey){last.logs=lKey;
  $('tLog').innerHTML='<tr><th>Id</th><th>Route</th><th>Cars</th><th>Cargo</th><th>Note</th><th>Status</th><th></th></tr>'+
   (logs.length?logs.map(o=>`<tr><td>${esc(o.Id)}</td><td>${esc(o.FromYardId)} &#8594; ${esc(o.ToYardId)}</td>`+
    `<td class='num'>${o.CarCount}</td><td>${esc(o.Cargo)}</td><td>${esc(o.Note)}</td><td>${esc(o.Status)}</td>`+
    `<td><button class='mini' data-act='logStart' data-id='${esc(o.Id)}'>Start</button>`+
    `<button class='mini' data-act='logDone' data-id='${esc(o.Id)}'>Done</button>`+
    `<button class='mini' data-act='logDel' data-id='${esc(o.Id)}'>&times;</button></td></tr>`).join('')
   :`<tr><td class='empty' colspan='7'>no runs posted</td></tr>`)}
 const hKey=JSON.stringify(hist);
 if(last.hist!==hKey){last.hist=hKey;renderLog(hist)}
}
async function fillCars(id){
 const box=$('cars_'+id);if(!box)return;
 try{const r=await j('/api/v1/jobs/'+id+'/cars');
  const html=`<div style='margin-bottom:5px'>loading track: <b>${esc(r.loadingTrack||'?')}</b></div>`+
   (r.cars.length?`<table><tr><th>Car</th><th>Type</th><th>Cargo</th><th>Track</th><th>Dist</th></tr>`+
    r.cars.map(c=>`<tr><td>${esc(c.carId)}</td><td>${esc(c.type)}</td>`+
     `<td><span class='loadpill ${c.loaded?'yes':'no'}'>${c.loaded?'LOADED':'empty'}</span></td>`+
     `<td>${esc(c.track)}</td><td class='num'>${c.metersFromLoading==null?'':c.metersFromLoading+' m'}</td></tr>`).join('')+
    `</table>`:'no cars attached yet: bring empties to the loading track');
  if(box.innerHTML!==html)box.innerHTML=html}
 catch(e){box.textContent='car view failed'}
}
// Network diagram: nodes come from the live economy, edges from what is
// shippable right now. Station layout follows the in-game network poster.
const NET_POS={IMW:[161,133],FF:[612,127],MB:[796,73],HMB:[860,105],MFMB:[830,143],
 IME:[950,60],CME:[966,237],OWN:[740,218],OR:[421,232],MF:[176,246],GF:[822,243],
 CP:[161,339],FRC:[379,350],FM:[394,447],OWC:[310,470],SM:[503,413],CW:[154,489],
 HB:[834,594],FRS:[357,577],CMS:[552,594],CS:[638,690],SW:[113,644]};
const NET_NAMES={OWC:'Oil Wells C',OWN:'Oil Wells N',OR:'Oil Refinery',FRS:'Forest S',
 FRC:'Forest C',CMS:'Coal Mine S',CME:'Coal Mine E',IME:'Iron Mine E',IMW:'Iron Mine W',
 CP:'Coal Power',SM:'Steel Mill',SW:'Sawmill',FM:'Farm',HB:'Harbour',GF:'Goods Factory',
 MF:'Machine Factory',FF:'Food Factory',CW:'City West',CS:'City South'};
const NET_STYLE={source:{fill:'#0f1a2a',stroke:'#3d78b8'},factory:{fill:'#141026',stroke:'#7a63d8'},
 sink:{fill:'#0f2020',stroke:'#2a9d8f'},hub:{fill:'#0a1230',stroke:'#4a8ae0'}};
let netSel=null,lastEconData=[];
function buildNet(econ,opts){
 const nodes={};let fx=940,fy=560;
 for(const e of econ){nodes[e.yardId]=e;
  if(!NET_POS[e.yardId]){NET_POS[e.yardId]=[fx,fy];fy+=64}}
 const em={};
 for(const o of opts)for(const d of o.consumers){
  if(!nodes[o.origin]||!nodes[d])continue;
  const k=o.origin+'|'+d;
  if(!em[k])em[k]={src:o.origin,dst:d,cargos:[],stock:0};
  if(!em[k].cargos.includes(o.cargo))em[k].cargos.push(o.cargo);
  em[k].stock+=o.stock}
 return {nodes,edges:Object.values(em)};
}
let lastHist=[];
function renderLog(hist){
 const box=$('dlog');if(!box)return;
 lastHist=hist||[];
 const ty=($('dlType')||{}).value||'';
 const yd=((($('dlYard')||{}).value)||'').trim().toUpperCase();
 hist=lastHist.filter(e=>(!ty||e.Type===ty)&&(!yd||String(e.Yard||'').toUpperCase().includes(yd)));
 if(!hist.length){box.innerHTML=`<div class='empty'>${lastHist.length?'nothing matches the filter':'nothing has happened yet'}</div>`;return}
 const verb={production:'produced',converted:'made',delivered:'received',loaded:'loaded',unloaded:'unloaded',haul_created:'posted a haul for'};
 box.innerHTML=[...hist].reverse().map(e=>{
  const t=e.Utc?new Date(e.Utc).toLocaleTimeString([],{hour:'2-digit',minute:'2-digit'}):'';
  const amt=e.Amount?Math.round(e.Amount*10)/10:'';
  return `<div style='padding:2px 0;border-bottom:1px solid var(--line)'><span class='meta num'>${t}</span> <b>${esc(e.Yard||'')}</b> ${verb[e.Type]||esc(e.Type)} ${amt} ${esc(e.Cargo||'')}${e.JobId?` <span class='meta'>(${esc(e.JobId)})</span>`:''}</div>`}).join('');
}
function stockRow(s,cap){
 const pct=cap>0?Math.min(100,Math.round(100*s.amount/cap)):0;
 const held=s.reserved>=1?` &middot; ${Math.round(s.reserved)} held`:'';
 const recv=s.imported>=1?` &middot; ${Math.round(s.imported)} received`:'';
 return `<div class='stockrow'><span class='cname' title='held = committed to a taken haul; received = delivered here, ships onward unpaid until consumed; bars show the share of the station total'>${esc(s.cargo)}</span>`+
  `<div class='bar'><i style='width:${pct}%'></i></div>`+
  `<span class='nums num'>${Math.round(s.amount)}${held}${recv}</span></div>`;
}
function stockAmt(n,cargo){const s=(n.stock||[]).find(x=>x.cargo===cargo);return s?s.amount:0}
function netMissing(n){const out=[];
 for(const r of (n.recipes||[]))for(const i of (r.inputs||[]))
  if(stockAmt(n,i.cargo)<i.amount&&!out.includes(i.cargo))out.push(i.cargo);
 return out}
function netPath(e,bidi){
 const A=NET_POS[e.src],B=NET_POS[e.dst];
 const mx=(A[0]+B[0])/2,my=(A[1]+B[1])/2;
 const dx=B[0]-A[0],dy=B[1]-A[1];
 const len=Math.sqrt(dx*dx+dy*dy)||1;
 const px=-dy/len,py=dx/len;
 const two=bidi.has(e.dst+'|'+e.src);
 const curve=two?0.20:0.09;
 const sign=(!two||e.src<e.dst)?1:-1;
 const cx=mx+px*len*curve*sign,cy=my+py*len*curve*sign;
 const rA=33,rB=42;
 const dax=cx-A[0],day=cy-A[1],da=Math.sqrt(dax*dax+day*day)||1;
 const sx=A[0]+dax/da*rA,sy=A[1]+day/da*rA;
 const dbx=B[0]-cx,dby=B[1]-cy,db=Math.sqrt(dbx*dbx+dby*dby)||1;
 const ex=B[0]-dbx/db*rB,ey=B[1]-dby/db*rB;
 return `M${sx},${sy} Q${cx},${cy} ${ex},${ey}`;
}
function drawNet(){
 const svg=$('net');if(!svg)return;
 const {nodes,edges}=buildNet(lastEconData,options);
 const bidi=new Set(edges.map(e=>e.src+'|'+e.dst));
 const sel=netSel&&nodes[netSel]?netSel:null;
 let h=`<defs>
  <marker id='arw' markerWidth='7' markerHeight='6' refX='6' refY='3' orient='auto' markerUnits='userSpaceOnUse'><path d='M0,0 L0,6 L7,3 z' fill='#3d5a7a'/></marker>
  <marker id='arwB' markerWidth='7' markerHeight='6' refX='6' refY='3' orient='auto' markerUnits='userSpaceOnUse'><path d='M0,0 L0,6 L7,3 z' fill='#8fb8e8'/></marker>
 </defs>`;
 for(const e of edges){
  const on=!sel||e.src===sel||e.dst===sel;
  const w=1+Math.min(3,e.stock/10);
  h+=`<path class='nedge' data-act='netEdge' data-src='${esc(e.src)}' data-dst='${esc(e.dst)}' data-cargo='${esc(e.cargos[0])}'
   d='${netPath(e,bidi)}' fill='none' stroke='${on&&sel?'#8fb8e8':'#3d5a7a'}'
   stroke-opacity='${sel?(on?0.95:0.05):0.5}' stroke-width='${sel&&on?w+1.5:w}'
   marker-end='url(#${sel&&on?'arwB':'arw'})'>
   <title>${esc(e.src)} to ${esc(e.dst)}: ${esc(e.cargos.join(', '))} (${Math.round(e.stock)} shippable)</title></path>`;
 }
 for(const id in nodes){
  const n=nodes[id];const p=NET_POS[id];
  const cls=id==='HB'?'hub':((n.inputs||[]).length===0&&(n.outputs||[]).length>0?'source':((n.outputs||[]).length===0?'sink':'factory'));
  const st=NET_STYLE[cls];
  const miss=netMissing(n);
  const r=cls==='hub'?36:30;
  const dim=sel&&id!==sel&&!edges.some(e=>(e.src===sel&&e.dst===id)||(e.dst===sel&&e.src===id));
  h+=`<g class='nnode' data-act='netNode' data-id='${esc(id)}' transform='translate(${p[0]},${p[1]})' opacity='${dim?0.25:1}'>
   <circle r='${r}' fill='${st.fill}' stroke='${miss.length?'#e07a6a':st.stroke}' stroke-width='${sel===id?3:1.5}'/>
   <text y='-1' text-anchor='middle' dominant-baseline='middle' fill='#eef2f8' font-size='14' font-weight='700'>${esc(id)}</text>
   <text y='14' text-anchor='middle' fill='#8b95a5' font-size='8.5'>${esc(NET_NAMES[id]||'')}</text>
   <title>${esc(id)}${miss.length?': waiting on '+esc(miss.join(', ')):''}</title></g>`;
 }
 svg.innerHTML=h;
 renderNetDetail(nodes,edges,sel);
}
function renderNetDetail(nodes,edges,sel){
 const d=$('netDetail');if(!d)return;
 if(!sel){d.className='netdetail';d.innerHTML='';return}
 const n=nodes[sel];
 let h=`<b>${esc(sel)}</b> <span class='meta'>${esc(NET_NAMES[sel]||'')}</span>`;
 if((n.recipes||[]).length)
  h+=n.recipes.map(r=>`<div class='nrecipe'>needs ${r.inputs.map(i=>esc(i.amount+' '+i.cargo)).join(' + ')} &#8594; makes ${r.outputs.map(o=>esc(o.amount+' '+o.cargo)).join(' + ')}</div>`).join('');
 else if((n.inputs||[]).length===0&&(n.outputs||[]).length>0)
  h+=`<div class='nrecipe'>produces <b>${esc(n.outputs.join(', '))}</b> over time</div>`;
 else if((n.outputs||[]).length===0)
  h+=`<div class='nrecipe'>accepts <b>${esc(n.inputs.join(', '))}</b>; storage is the demand</div>`;
 const miss=netMissing(n);
 if(miss.length)h+=`<div class='nrecipe nmiss'>waiting on: ${esc(miss.join(', '))}</div>`;
 for(const b of (n.boosters||[]))
  h+=`<div class='nrecipe' style='color:${b.active?'var(--green)':'var(--dim)'}'>${b.active?'boosted &#215;'+b.speedup:'runs &#215;'+b.speedup+' faster with'}: ${esc([...b.cargo].join(', '))} (any one)</div>`;
 const rows=(n.stock||[]);
 if(rows.length||n.totalCap){
  const cap=Math.round(n.totalCap||0),used=Math.round(n.totalStock||0);
  const upct=n.totalCap>0?Math.min(100,Math.round(100*used/n.totalCap)):0;
  h+=`<div class='sublab'>storage &middot; one shared pool: every cargo counts against the same total</div>`;
  h+=`<div class='stockrow'><span class='cname'><b>total</b></span>`+
   `<div class='bar'><i class='${upct>=100?'full':''}' style='width:${upct}%'></i></div>`+
   `<span class='nums num'>${used} / ${cap}</span></div>`;
  const dprod=rows.filter(s=>(n.outputs||[]).includes(s.cargo));
  const dcons=rows.filter(s=>!(n.outputs||[]).includes(s.cargo));
  if(dprod.length)h+=`<div class='sublab'>produced</div>`+dprod.map(s=>stockRow(s,n.totalCap||0)).join('');
  if(dcons.length)h+=`<div class='sublab'>consumed</div>`+dcons.map(s=>stockRow(s,n.totalCap||0)).join('');
 }
 const outs=edges.filter(e=>e.src===sel),ins=edges.filter(e=>e.dst===sel);
 if(outs.length)h+=`<div class='meta' style='margin-top:6px'>can ship: `+outs.map(e=>`<b>${esc(e.cargos.join(', '))}</b> &#8594; ${esc(e.dst)}`).join(' &middot; ')+`</div>`;
 if(ins.length)h+=`<div class='meta'>incoming supply: `+ins.map(e=>`${esc(e.src)}: ${esc(e.cargos.join(', '))}`).join(' &middot; ')+`</div>`;
 d.className='netdetail show';d.innerHTML=h;
}
function renderFleet(r){
 $('fSummary').textContent=r.total+' car(s), '+r.usable+' usable now';
 const groups={};
 for(const c of r.cars){const k=(c.yard||'~')+'|'+c.track;(groups[k]=groups[k]||[]).push(c)}
 const keys=Object.keys(groups).sort();
 $('tFleet').innerHTML=keys.length?'<tr><th>Yard</th><th>Track</th><th>Usable</th><th>Cars</th></tr>'+
  keys.map(k=>{const g=groups[k];g.sort((a,b)=>(b.usable?1:0)-(a.usable?1:0));
   const u=g.filter(c=>c.usable).length;
   return `<tr><td>${esc(g[0].yard||'')}</td><td>${esc(g[0].track)}</td><td class='num'>${u}/${g.length}</td><td>`+
    g.map(c=>{const why=c.loadedCargo?('loaded: '+c.loadedCargo):c.jobId?('on job '+c.jobId):c.reservedBy?('reserved for '+c.reservedBy):c.playerSpawned?'player car':'usable';
     return `<span class='carchip ${c.usable?'ok':'busy'}' title='${esc(c.type)}; ${esc(why)}'>${esc(c.carId)}</span>`}).join('')+
    `</td></tr>`}).join('')
  :`<tr><td class='empty' colspan='4'>no matching cars found</td></tr>`;
}
function fmtSecs(s){s=Math.round(s);const m=Math.floor(s/60);return m>0?m+'m '+(s%60)+'s':s+'s'}
async function fillPicker(id){
 if(!pickers[id]){
  try{const r=await j('/api/v1/jobs/'+id+'/candidates');
   if(r.error){toast(r.error,true);return}
   pickers[id]={data:r,sel:[]}}
  catch(e){return}}
 renderPickPanel(id);
}
function renderPickPanel(id){
 const box=$('pick_'+id);const p=pickers[id];
 if(!box||!p)return;
 const d=p.data;
 if(d.carsAttached){box.innerHTML='cars are already attached; use Load on them';return}
 if(!d.cars.length){box.innerHTML='no suitable empties at '+esc(d.origin)+'; use Find empties to locate cars elsewhere';return}
 const selSet=new Set(p.sel);
 const byId={};d.cars.forEach(c=>byId[c.carId]=c);
 const lastSel=p.sel.length?byId[p.sel[p.sel.length-1]]:null;
 const rest=d.cars.filter(c=>!selSet.has(c.carId));
 rest.sort((a,b)=>{
  if(lastSel){
   const ta=a.track===lastSel.track?0:1,tb=b.track===lastSel.track?0:1;
   if(ta!==tb)return ta-tb;
   return Math.hypot(a.x-lastSel.x,a.z-lastSel.z)-Math.hypot(b.x-lastSel.x,b.z-lastSel.z)}
  const da=a.metersFromLoading==null?1e9:a.metersFromLoading;
  const db=b.metersFromLoading==null?1e9:b.metersFromLoading;
  return da-db});
 const chip=(c,on)=>{
  const dist=lastSel&&!on?Math.round(Math.hypot(c.x-lastSel.x,c.z-lastSel.z)):(c.metersFromLoading==null?null:Math.round(c.metersFromLoading));
  const sameTrack=lastSel&&!on&&c.track===lastSel.track;
  return `<span class='carchip ${on?'ok':''}' data-act='pickCar' data-id='${esc(id)}' data-car='${esc(c.carId)}'
   title='${esc(c.type)} on ${esc(c.track)}' style='cursor:pointer${sameTrack?';border-color:#3d78b8':''}'>${on?'&#10003; ':''}${esc(c.carId)} &middot; ${esc(c.track)}${dist==null?'':' &middot; '+dist+'m'}</span>`};
 const done=p.sel.length===d.wanted;
 box.innerHTML=`<div style='margin-bottom:5px'>pick <b>${d.wanted}</b> car(s), ${lastSel?'same track as <b>'+esc(lastSel.carId)+'</b> (<b>'+esc(lastSel.track)+'</b>) first, then nearest elsewhere':'sorted by distance to the loading track'}</div>`+
  p.sel.map(cid=>chip(byId[cid],true)).join('')+rest.map(c=>chip(c,false)).join('')+
  `<div style='margin-top:8px;display:flex;gap:8px;align-items:center'>
   <button class='primary' data-act='loadPicked' data-id='${esc(id)}' ${done?'':'disabled'}>Start loading</button>
   <button class='mini' data-act='pickAuto' data-id='${esc(id)}' title='Let the station pick the nearest suitable empties'>Auto-pick</button>
   <span class='meta'>${p.sel.length}/${d.wanted} picked &middot; staff &#8776; ${fmtSecs(Math.max(0,p.sel.length-1)*d.perCarSeconds)} (first car instant, ${d.perCarSeconds}s per car after)</span>
  </div>`;
}
function crewVal(id){const i=$('a_'+id);return i&&i.value?i.value:null}
const actions={
 lock:async()=>{const r=await j('/api/v1/lock','PUT',{enabled:!lockOn});
  toast('Assignment lock is now '+(r.lockEnabled?'ON':'OFF')+(r.purged?'; '+r.purged+' open booklet(s) expired, supply returned':''));refresh()},
 spawnHaul:async()=>{const b={origin:$('hOrigin').value,destination:$('hDest').value,
   cargo:$('hCargo').value,cars:parseInt($('hCars').value)};
  const r=await j('/api/v1/hauls','POST',b);
  r.jobId?toast('Created '+r.jobId+(r.unpaid?' as an UNPAID move (produced stock is short; this relocates received goods)':'')):toast('Failed: '+(r.error||'see game log'),true);refresh()},
 netNode:(id,el)=>{const v=el.dataset.id;netSel=netSel===v?null:v;drawNet()},
 netEdge:(id,el)=>{const o=el.dataset.src,c=el.dataset.cargo,d=el.dataset.dst;
  const os=$('hOrigin');
  if(![...os.options].some(x=>x.value===o)){toast('nothing shippable from '+o+' right now',true);return}
  os.value=o;originChanged();
  const cs=$('hCargo');
  if([...cs.options].some(x=>x.value===c)){cs.value=c;cargoChanged()}
  const ds=$('hDest');
  if([...ds.options].some(x=>x.value===d))ds.value=d;
  toast('Form filled: '+o+' '+c+' to '+d)},
 take:async id=>{const r=await j('/api/v1/jobs/'+id+'/take','POST',{player:crewVal(id)});
  toast(r.message||'failed',!r.ok);refresh()},
 complete:async id=>{const r=await j('/api/v1/jobs/'+id+'/complete','POST');
  toast(r.message||'failed',!r.ok);refresh()},
 load:async id=>{const r=await j('/api/v1/jobs/'+id+'/load','POST');
  toast(r.message||'failed',!r.ok);setTimeout(refresh,1200)},
 pickCars:id=>{if(pickOpen.has(id)){pickOpen.delete(id);delete pickers[id]}else pickOpen.add(id);
  last.jobs=null;refresh()},
 pickCar:(id,el)=>{const p=pickers[id];if(!p)return;
  const car=el.dataset.car;const i=p.sel.indexOf(car);
  if(i>=0)p.sel.splice(i,1);
  else if(p.sel.length<p.data.wanted)p.sel.push(car);
  else{toast('already picked '+p.data.wanted+'; unpick one first',true);return}
  renderPickPanel(id)},
 loadPicked:async id=>{const p=pickers[id];if(!p)return;
  const r=await j('/api/v1/jobs/'+id+'/load','POST',{cars:p.sel});
  toast(r.message||'failed',!r.ok);
  if(r.ok){pickOpen.delete(id);delete pickers[id];last.jobs=null}
  setTimeout(refresh,1200)},
 pickAuto:async id=>{const r=await j('/api/v1/jobs/'+id+'/load','POST');
  toast(r.message||'failed',!r.ok);
  if(r.ok){pickOpen.delete(id);delete pickers[id];last.jobs=null}
  setTimeout(refresh,1200)},
 unload:async id=>{const r=await j('/api/v1/jobs/'+id+'/unload','POST');
  toast(r.message||'failed',!r.ok);setTimeout(refresh,1200)},
 fax:async id=>{const r=await j('/api/v1/jobs/'+id+'/fax','POST',{player:crewVal(id)});
  toast(r.message||'failed',!r.ok)},
 assign:async id=>{const p=crewVal(id);if(!p){toast('enter a crew name first',true);return}
  const r=await j('/api/v1/assignments/'+id,'PUT',{player:p,assignedBy:'board'});
  toast(r.ok?'Assigned '+id+' to '+p:'assign failed',!r.ok);refresh()},
 unassign:async id=>{await j('/api/v1/assignments/'+id,'DELETE');toast('Unassigned '+id);refresh()},
 delhaul:async id=>{if(!confirm('Delete '+id+'? Its supply returns to the pile.'))return;
  const r=await j('/api/v1/jobs/'+id,'DELETE');toast(r.message||(r.ok?'Deleted '+id:'delete failed'),!r.ok);refresh()},
 cars:id=>{expanded.has(id)?expanded.delete(id):expanded.add(id);last.jobs=null;refresh()},
 findCars:async()=>{const c=$('fCargo').value,y=$('fYard').value.trim();
  if(!c){clearFleet();return}
  const q=[];if(c!=='any cargo')q.push('cargo='+encodeURIComponent(c));
  if(y)q.push('yard='+encodeURIComponent(y.toUpperCase()));
  const r=await j('/api/v1/fleet'+(q.length?'?'+q.join('&'):''));
  if(r.error){toast(r.error,true);return}
  renderFleet(r)},
 findEmpties:id=>{const x=lastJobs.find(v=>v.id===id);if(!x)return;
  const sel=$('fCargo');
  if(![...sel.options].some(o=>o.value===x.cargo)){const o=document.createElement('option');o.textContent=x.cargo;sel.appendChild(o)}
  sel.value=x.cargo;$('fYard').value='';
  openSec('finder');
  actions.findCars();$('finder').scrollIntoView({behavior:'smooth'})},
 postRun:async()=>{const b={from:$('lFrom').value,to:$('lTo').value,cars:parseInt($('lCars').value),
   cargo:$('lCargo').value||null,note:$('lNote').value||null};
  if(!b.from||!b.to){toast('from and to are required',true);return}
  const r=await j('/api/v1/logistics','POST',b);
  r.Id?toast('Posted '+r.Id):toast('failed',true);refresh()},
 logStart:async id=>{await j('/api/v1/logistics/'+id,'PUT',{status:'InProgress'});refresh()},
 logDone:async id=>{await j('/api/v1/logistics/'+id,'PUT',{status:'Done'});refresh()},
 logDel:async id=>{await j('/api/v1/logistics/'+id,'DELETE');refresh()},
};
document.addEventListener('click',e=>{const el=e.target.closest('[data-act]');if(!el)return;
 const fn=actions[el.dataset.act];if(fn)fn(el.dataset.id,el)});
function originChanged(){const o=$('hOrigin').value;
 keepSelect($('hCargo'),options.filter(x=>x.origin===o).map(x=>x.cargo));cargoChanged()}
function cargoChanged(){const o=$('hOrigin').value,c=$('hCargo').value;
 const opt=options.find(x=>x.origin===o&&x.cargo===c);
 keepSelect($('hDest'),opt?opt.consumers:[])}
$('hOrigin').addEventListener('change',originChanged);
$('hCargo').addEventListener('change',cargoChanged);
$('dlType').onchange=()=>renderLog(lastHist);
$('dlYard').oninput=()=>renderLog(lastHist);
// Blanking the cargo field clears the finder back to its fresh-page state; a separate
// mechanic from collapsing the section (which just hides it).
function clearFleet(){$('tFleet').innerHTML='';$('fSummary').textContent=''}
$('fCargo').addEventListener('change',()=>{if(!$('fCargo').value)clearFleet()});
// Collapsible sections: click a heading to fold it away. The dispatch log starts
// folded; everything else starts open. Remembered per browser.
const closedSecs=new Set(JSON.parse(localStorage.getItem('dleClosed')||'0')||['dlog']);
function applySecs(){document.querySelectorAll('main section[data-sec]').forEach(s=>
 s.classList.toggle('closed',closedSecs.has(s.dataset.sec)))}
function openSec(k){if(!closedSecs.has(k))return;closedSecs.delete(k);
 localStorage.setItem('dleClosed',JSON.stringify([...closedSecs]));applySecs()}
document.addEventListener('click',e=>{const h=e.target.closest('h2');if(!h)return;
 const s=h.closest('section[data-sec]');if(!s)return;
 const k=s.dataset.sec;closedSecs.has(k)?closedSecs.delete(k):closedSecs.add(k);
 localStorage.setItem('dleClosed',JSON.stringify([...closedSecs]));applySecs()});
applySecs();
refresh();setInterval(refresh,5000);
</script></body></html>
";
    }
}
