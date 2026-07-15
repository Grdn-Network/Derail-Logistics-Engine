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
.econ{display:grid;gap:12px}
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
 <section class='card col5'>
  <h2>Create a haul</h2>
  <div class='formrow'>
   <label>Origin<select id='hOrigin'></select></label>
   <label>Cargo<select id='hCargo'></select></label>
   <label>Destination<select id='hDest'></select></label>
   <label>Cars<input id='hCars' type='number' value='4' min='1' max='20' style='width:64px'></label>
   <button class='primary' data-act='spawnHaul'>Spawn haul</button>
  </div>
 </section>
 <section class='card col7'>
  <h2>Shippable now <span class='sub'>click a row to fill the form</span></h2>
  <div class='tablewrap'><table id='tOptions'></table></div>
 </section>
 <section class='col12'>
  <h2>Available hauls <span class='count' id='cAvail'></span></h2>
  <div class='cards' id='availCards'></div>
 </section>
 <section class='col12'>
  <h2>Accepted hauls <span class='count' id='cAcc'></span></h2>
  <div class='cards' id='accCards'></div>
 </section>
 <section class='card col6'>
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
 <section class='card col6'>
  <h2>Economy <span class='sub'>stock against storage cap</span></h2>
  <div class='econ' id='econGrid'></div>
 </section>
</main>
<footer>Derail Logistics Engine &middot; local board on 127.0.0.1:7246 &middot; refreshes every 5s</footer>
<div id='toasts'></div>
<script>
const $=id=>document.getElementById(id);
const esc=s=>String(s==null?'':s).replace(/[&<>']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;',""'"":'&#39;'}[c]));
let options=[],lockOn=false,expanded=new Set(),last={};
async function j(u,m,b){const r=await fetch(u,{method:m||'GET',body:b?JSON.stringify(b):undefined});return r.json()}
function toast(t,err){const d=document.createElement('div');d.className='toast'+(err?' err':'');
 d.textContent=t;$('toasts').appendChild(d);setTimeout(()=>d.remove(),4200)}
function pillClass(s){s=(s||'').toLowerCase();
 return s==='available'?'available':s==='inprogress'?'inprogress':s==='completed'?'completed':'other'}
function money(x){return '$'+Math.round(x||0).toLocaleString('en-US')}
function jobCard(x,avail){
 const cars=x.cars||x.plannedCars||0;
 const acts=avail
  ?`<button class='primary' data-act='take' data-id='${esc(x.id)}'>Take</button>`
  :`<button data-act='load' data-id='${esc(x.id)}'>Load</button>
    <button data-act='unload' data-id='${esc(x.id)}'>Unload</button>
    <button class='primary' data-act='complete' data-id='${esc(x.id)}'>Turn in</button>`;
 return `<div class='job'>
  <div class='jobtop'><span class='jid'>${esc(x.id)}</span>
   <span class='pill ${pillClass(x.state)}'>${esc(x.state)}</span>
   ${x.awaitingEmpties?`<span class='tag'>awaiting empties</span>`:''}
   <span class='wage num'>${money(x.wage)}</span></div>
  <div class='route'><b>${esc(x.origin)}</b><span class='arr'>&#8594;</span><b>${esc(x.destination)}</b></div>
  <div class='meta'><b>${esc(x.cargo)}</b> &middot; ${cars} cars${x.pickupTrack?` &middot; pickup <b>${esc(x.pickupTrack)}</b>`:''}</div>
  <div class='meta'>${x.assignedTo?`crew: <b>${esc(x.assignedTo)}</b>`:'unassigned'}</div>
  <div class='acts'>${acts}
   <button data-act='fax' data-id='${esc(x.id)}' title='Fax the booklet to the named crew (blank = you)'>Fax</button>
   <button class='mini' data-act='cars' data-id='${esc(x.id)}'>${expanded.has(x.id)?'Hide cars':'Cars'}</button>
   <input class='crew' id='a_${esc(x.id)}' placeholder='crew name'>
   <button class='mini' data-act='assign' data-id='${esc(x.id)}'>Assign</button>
   <button class='mini' data-act='unassign' data-id='${esc(x.id)}' title='Clear assignment'>&times;</button>
  </div>
  ${expanded.has(x.id)?`<div class='carsbox' id='cars_${esc(x.id)}'>fetching&hellip;</div>`:''}
 </div>`}
function snapshotCrew(){const m={};document.querySelectorAll('.crew').forEach(i=>{if(i.value)m[i.id]=i.value});
 const f=document.activeElement;return{m,focus:f&&f.classList&&f.classList.contains('crew')?f.id:null}}
function restoreCrew(s){for(const id in s.m){const i=$(id);if(i)i.value=s.m[id]}
 if(s.focus){const i=$(s.focus);if(i){i.focus();i.setSelectionRange(i.value.length,i.value.length)}}}
function keepSelect(sel,items){const cur=sel.value;
 sel.innerHTML=items.map(v=>`<option>${esc(v)}</option>`).join('');
 if([...sel.options].some(o=>o.value===cur))sel.value=cur}
async function refresh(){
 let state,jobs,econ,logs;
 try{[state,options,jobs,econ,logs]=await Promise.all([
  j('/api/v1/state'),j('/api/v1/options'),j('/api/v1/jobs'),j('/api/v1/economy'),j('/api/v1/logistics')]);
  $('dot').className='dot'}
 catch(e){$('dot').className='dot bad';return}
 lockOn=!!state.lockEnabled;
 $('bLock').textContent='LOCK '+(lockOn?'ON':'OFF');
 $('bLock').className='lockbtn'+(lockOn?' on':'');
 $('chipVer').textContent='v'+(state.modVersion||'?');
 $('chipStations').textContent=state.stationCount+' stations';
 $('chipJobs').textContent=state.jobCount+' hauls';
 keepSelect($('hOrigin'),[...new Set(options.map(o=>o.origin))]);
 originChanged();
 const oKey=JSON.stringify(options);
 if(last.opt!==oKey){last.opt=oKey;
  $('tOptions').innerHTML='<tr><th>Origin</th><th>Cargo</th><th>Stock</th><th>Consumers</th></tr>'+
   (options.length?options.map(o=>`<tr class='pick' data-act='ship' data-o='${esc(o.origin)}' data-c='${esc(o.cargo)}'>`+
    `<td>${esc(o.origin)}</td><td>${esc(o.cargo)}</td><td class='num'>${o.stock}</td><td>${esc(o.consumers.join(', '))}</td></tr>`).join('')
   :`<tr><td class='empty'>nothing shippable: no producer has unreserved stock</td></tr>`)}
 const jKey=JSON.stringify(jobs)+[...expanded].join();
 if(last.jobs!==jKey){last.jobs=jKey;
  const snap=snapshotCrew();
  const av=jobs.filter(x=>x.state==='Available'),ac=jobs.filter(x=>x.state!=='Available');
  $('cAvail').textContent=av.length||'';$('cAcc').textContent=ac.length||'';
  $('availCards').innerHTML=av.length?av.map(x=>jobCard(x,true)).join(''):`<div class='empty'>no open hauls; spawn one above or wait for the director</div>`;
  $('accCards').innerHTML=ac.length?ac.map(x=>jobCard(x,false)).join(''):`<div class='empty'>nothing accepted yet</div>`;
  restoreCrew(snap)}
 for(const id of expanded)fillCars(id);
 const lKey=JSON.stringify(logs);
 if(last.logs!==lKey){last.logs=lKey;
  $('tLog').innerHTML='<tr><th>Id</th><th>Route</th><th>Cars</th><th>Cargo</th><th>Note</th><th>Status</th><th></th></tr>'+
   (logs.length?logs.map(o=>`<tr><td>${esc(o.Id)}</td><td>${esc(o.FromYardId)} &#8594; ${esc(o.ToYardId)}</td>`+
    `<td class='num'>${o.CarCount}</td><td>${esc(o.Cargo)}</td><td>${esc(o.Note)}</td><td>${esc(o.Status)}</td>`+
    `<td><button class='mini' data-act='logStart' data-id='${esc(o.Id)}'>Start</button>`+
    `<button class='mini' data-act='logDone' data-id='${esc(o.Id)}'>Done</button>`+
    `<button class='mini' data-act='logDel' data-id='${esc(o.Id)}'>&times;</button></td></tr>`).join('')
   :`<tr><td class='empty' colspan='7'>no runs posted</td></tr>`)}
 const eKey=JSON.stringify(econ);
 if(last.econ!==eKey){last.econ=eKey;
  $('econGrid').innerHTML=econ.filter(e=>e.stock.length).map(e=>`<div class='yard'>`+
   `<div class='yhead'>${esc(e.yardId)}</div>`+
   e.stock.map(s=>{const pct=s.cap>0?Math.min(100,Math.round(100*s.amount/s.cap)):0;
    return `<div class='stockrow'><span class='cname'>${esc(s.cargo)}</span>`+
     `<div class='bar'><i class='${pct>=100?'full':''}' style='width:${pct}%'></i></div>`+
     `<span class='nums num'>${Math.round(s.amount)} / ${Math.round(s.cap)}</span></div>`}).join('')+
   `</div>`).join('')||`<div class='empty'>no stock anywhere yet</div>`}
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
function crewVal(id){const i=$('a_'+id);return i&&i.value?i.value:null}
const actions={
 lock:async()=>{const r=await j('/api/v1/lock','PUT',{enabled:!lockOn});
  toast('Assignment lock is now '+(r.lockEnabled?'ON':'OFF'));refresh()},
 spawnHaul:async()=>{const b={origin:$('hOrigin').value,destination:$('hDest').value,
   cargo:$('hCargo').value,cars:parseInt($('hCars').value)};
  const r=await j('/api/v1/hauls','POST',b);
  r.jobId?toast('Created '+r.jobId):toast('Failed: '+(r.error||'see game log'),true);refresh()},
 ship:(id,el)=>{$('hOrigin').value=el.dataset.o;originChanged();
  $('hCargo').value=el.dataset.c;cargoChanged()},
 take:async id=>{const r=await j('/api/v1/jobs/'+id+'/take','POST',{player:crewVal(id)});
  toast(r.message||'failed',!r.ok);refresh()},
 complete:async id=>{const r=await j('/api/v1/jobs/'+id+'/complete','POST');
  toast(r.message||'failed',!r.ok);refresh()},
 load:async id=>{const r=await j('/api/v1/jobs/'+id+'/load','POST');
  toast(r.message||'failed',!r.ok);setTimeout(refresh,1200)},
 unload:async id=>{const r=await j('/api/v1/jobs/'+id+'/unload','POST');
  toast(r.message||'failed',!r.ok);setTimeout(refresh,1200)},
 fax:async id=>{const r=await j('/api/v1/jobs/'+id+'/fax','POST',{player:crewVal(id)});
  toast(r.message||'failed',!r.ok)},
 assign:async id=>{const p=crewVal(id);if(!p){toast('enter a crew name first',true);return}
  const r=await j('/api/v1/assignments/'+id,'PUT',{player:p,assignedBy:'board'});
  toast(r.ok?'Assigned '+id+' to '+p:'assign failed',!r.ok);refresh()},
 unassign:async id=>{await j('/api/v1/assignments/'+id,'DELETE');toast('Unassigned '+id);refresh()},
 cars:id=>{expanded.has(id)?expanded.delete(id):expanded.add(id);last.jobs=null;refresh()},
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
refresh();setInterval(refresh,5000);
</script></body></html>
";
    }
}
