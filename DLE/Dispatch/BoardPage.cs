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
<link rel='icon' type='image/png' href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAACeUlEQVR4AexWS2gUQRB907NJ1CVKzGIk4MEPIioeRFjMQQQxN6MQryLqxYBEEHLwJHrxIAiKEC8qfsCLguYiCIJ4UBZEMBgRES9eVGIUwWCSmWm7alNL92yHLLibWUiWfV3dU1Vdb6qrZkZ1re3WWUIh498SAScD5/aF+HCmxcHo6RZc7QvRuSLgw1rZBjw+kmObW4dzfC09rO8I8HKgbJPej9a2n0MgvRGtW0Ogd5PCzf4QBUNCGR4E0tUDXgJ/pjWGSwkuvYjx7pvmOJsLAQ5tM9F5VftQ+qJx8Xni4HoprmzgJTBl9CPvE9x4nWDoSYwfkxqBiV1cZ4aKa22TzxMat9/EDoiUeHsJiJLkTxN8YpJmQK6euS9vCTUrMxMNJ7BhdYCjO0PGflPM6UNsOAGqm7N7FQiDPQqrlrnJbjgBO1yiAYJ9reEE7r9NsOXyDOPg3Qi/p+zwWARF6N5v9WreI+huD1DIB+wZpQ+Qr/7f4CXQZp7/fVsVTuxSuNAbomM5oE0B2U8wCrsmD24vaTOS1GqkE9htSHoCdYbovQTyrQEGigpDe0Js7yrf/cdxjUdjhoV4GrmxM+D2ohYTUKu1mzemUfOfgolO5MmiuUPWYv4inDbvhaefEhx/GGPcPJZn/eomnAycfxZzu0jbkNxxZQaDIzG/kCjqr7/AgTtRlR3ZEkg3+lWjZ3hum2MPItqK4RDgKws8NAcB+kzKApTs5sjA2HeNLFDJQP+9CFmgQoAmglO7FX9yS03QWnS2FL1IWydz8hU9SVqLTmRz1ICwyUJ6M2AX5FykbBua12Lns6kicO1V4hQkrX2O6aL12ZCvbUfrtN0/AAAA//89h+LdAAAABklEQVQDAI+K4FDa7/FNAAAAAElFTkSuQmCC'>
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
.chip.warn{border-color:var(--amber);color:var(--amber);font-weight:700}
.machrow{display:flex;gap:8px;align-items:center;font-size:12.5px;padding:2px 0}
.machrow .mname{font-family:inherit;min-width:110px;color:var(--text)}
.machrow .mcount{font-weight:700}
.machrow .mcount.low{color:var(--amber)}
.machrow .mcount.out{color:var(--red)}
.machrow .mwear{color:var(--dim)}
.ctag{font-size:9.5px;letter-spacing:.05em;text-transform:uppercase;color:var(--line2);margin-left:3px}
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
.stockrow{display:grid;grid-template-columns:160px 1fr 130px;gap:10px;max-width:640px;
align-items:center;padding:2px 0;font-size:12.5px}
.stockrow .cname{color:var(--dim);overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.bar{height:7px;border-radius:4px;background:var(--panel2);border:1px solid var(--line);overflow:hidden}
.bar i{display:block;height:100%;background:linear-gradient(90deg,var(--blue),var(--violet))}
.bar i.full{background:var(--amber)}
.nums{text-align:right;color:var(--dim)}
.yardbox{display:flex;flex-direction:column;gap:6px}
.ytrack{display:flex;align-items:center;gap:8px;padding:4px 8px;background:var(--panel2);
border:1px solid var(--line);border-radius:8px}
.ytlabel{min-width:128px;font-size:11.5px;color:var(--dim);flex:none;line-height:1.3}
.ytlabel b{color:var(--text);font-size:12.5px}
.ycars{display:flex;gap:8px;overflow-x:auto;padding:2px 0;flex:1}
.ycut{display:flex;gap:2px;flex:none}
.ycar{flex:none;border:1px solid var(--line2);border-radius:4px;padding:2px 7px;font-size:11.5px;
color:var(--dim);white-space:nowrap;user-select:none}
.ycar.ok{color:var(--text);border-color:#2c5c3f;cursor:pointer}
.ycar.ok:hover{border-color:var(--green)}
.ycar.sel{background:var(--vdeep);border-color:var(--violet);color:var(--text)}
.ycar.loaded{border-color:#6b5619;color:var(--amber)}
.ycar.loco{background:#101c2c;border-color:#2c4a6e;color:var(--blue)}
.ycar.incompat{opacity:.3}
.ykey{display:flex;gap:14px;flex-wrap:wrap;font-size:11px;color:var(--dim);margin-top:6px}
.ykey i{display:inline-block;width:10px;height:10px;border-radius:3px;border:1px solid var(--line2);
margin-right:4px;vertical-align:-1px;font-style:normal}
.bar i.warn{background:var(--amber)}
.bar i.crit{background:var(--red)}
.sthead{display:flex;align-items:center;gap:10px;flex-wrap:wrap;margin-bottom:6px}
.stchip{font-size:11px;font-weight:700;border-radius:999px;padding:2px 9px}
.stchip.bad{background:#3a1a14;color:var(--red)}
.stchip.warn{background:#3a2e12;color:var(--amber)}
.stchip.good{background:#173424;color:var(--green)}
.stchip.idle{background:var(--panel2);color:var(--dim)}
.needrow{display:flex;gap:8px;align-items:baseline;font-size:12.5px;padding:1px 0}
.needrow b{min-width:120px;font-weight:600}
.foldbtn{font-size:11.5px;font-weight:600;color:var(--dim);cursor:pointer;user-select:none;margin-top:7px}
.foldbtn:hover{color:var(--text)}
.foldbody{margin:3px 0 6px 8px;padding-left:12px;border-left:1px solid var(--line)}
.netdetail{max-width:840px}
.mline{display:flex;gap:10px;align-items:center;padding:3px 9px;background:var(--panel2);
border:1px solid var(--line);border-radius:6px;margin:3px 0;font-size:12.5px;flex-wrap:wrap}
.mline .meta{overflow:hidden;text-overflow:ellipsis;white-space:nowrap;max-width:52%}
.mline.total{border-style:dashed;background:transparent}
.mline button{margin-left:auto}
.ycar.inline{background:#241d3a;border-color:#54418c;color:var(--dim)}
.yend{flex:none;font-size:10px;font-weight:700;color:var(--dim);align-self:center;
letter-spacing:.05em;user-select:none}
.accf{margin-left:12px;display:inline-flex;gap:5px;flex-wrap:wrap;vertical-align:middle}
.fchip{font-size:10.5px;font-weight:700;letter-spacing:.04em;border:1px solid var(--line2);
border-radius:999px;padding:1px 8px;color:var(--dim);cursor:pointer;text-transform:none}
.fchip:hover{color:var(--text);border-color:var(--violet)}
.fchip.on{background:var(--vdeep);border-color:var(--violet);color:var(--text)}
.fchip.off{opacity:.4;text-decoration:line-through}
.fchip.clear{border-style:dashed}
.shipto{margin:-1px 0 4px 170px;font-size:11px;color:var(--dim)}
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
 <span class='chip' id='chipBoost' title='Global productivity from city consumption: keep the cities fed and every industry speeds up'></span>
 <span class='chip warn' id='chipMachines' style='display:none' title='Stations on their last machine: ship replacements or they crawl'></span>
 <div class='spacer'></div>
 <button class='lockbtn' id='bLock' data-act='lock'
  title='When ON, crews can only accept hauls assigned to them and Company Haul papers leave the station offices. Faxed booklets still work.'>LOCK &middot; &hellip;</button>
</header>
<main>
 <section class='card col12' data-sec='create'>
  <h2>Job maker <span class='sub'>pick a station, click the cars you want, choose cargo and destination; one booklet comes out</span></h2>
  <div class='formrow' style='margin-bottom:8px'>
   <label>Station<select id='hOrigin'></select></label>
   <span class='meta' id='jmMeta'></span>
  </div>
  <div id='jmYard' class='yardbox'></div>
  <div class='ykey'><span><i style='border-color:#2c5c3f'></i>selectable</span>
   <span><i style='background:var(--vdeep);border-color:var(--violet)'></i>selected</span>
   <span><i style='border-color:#6b5619'></i>loaded</span>
   <span><i></i>on a job / reserved / player car</span>
   <span><i style='background:#101c2c;border-color:#2c4a6e'></i>loco</span></div>
  <div class='formrow' style='margin-top:10px'>
   <label>Cargo<select id='hCargo'></select></label>
   <label>Destination<select id='hDest'></select></label>
   <label>Cars<input id='hCars' type='number' value='4' min='1' max='40' style='width:64px'></label>
   <button class='mini' data-act='jmAddLine' title='Bank the picked cars as a cargo line, then pick more cars for another cargo. One booklet covers every line.'>+ Add line</button>
   <span class='meta' id='hEstimate' title='Estimated from the car types this cargo loads into; staff loading is first car instant, then per-car time'></span>
  </div>
  <div id='jmManifest' style='margin-top:6px'></div>
  <div class='formrow' style='margin-top:8px'>
   <label>Crew<input id='hCrew' class='crew' list='crewNames' placeholder='optional'></label>
   <label style='flex-direction:row;align-items:center;gap:6px;padding-top:14px'>
    <input type='checkbox' id='hTake' style='min-width:0'> take on create</label>
   <button class='primary' data-act='spawnHaul'>Create booklet</button>
   <button data-act='spawnHaulLoad' title='Create the booklet, take it, and have station staff load the picked cars where they stand'>Create + load now</button>
   <button class='mini' data-act='jmClear'>Clear picks</button>
  </div>
  <div class='meta' id='jmSel' style='margin-top:4px'></div>
 </section>
 <section class='card col12' data-sec='net'>
  <h2>Network <span class='sub'>the whole economy lives here: click a station for its recipes, storage and stock; click a route to fill the haul form</span></h2>
  <svg id='net' viewBox='0 0 1040 760' style='width:100%;height:auto;max-height:78vh'></svg>
  <div id='netDetail' class='netdetail'></div>
 </section>
 <section class='col12' data-sec='acc'>
  <h2>Accepted hauls <span class='count' id='cAcc'></span>
   <span id='accFilter' class='accf'></span></h2>
  <div class='cards' id='accCards'></div>
 </section>
 <section class='col12' data-sec='avail'>
  <h2>Available hauls <span class='count' id='cAvail'></span></h2>
  <div class='cards' id='availCards'></div>
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
<footer style='display:flex;gap:10px;align-items:center'>Derail Logistics Engine &middot; refreshes every 5s
 <span class='spacer'></span><span id='ftStats' class='num'></span></footer>
<div id='toasts'></div>
<datalist id='crewNames'></datalist>
<script>
const $=id=>document.getElementById(id);
const esc=s=>String(s==null?'':s).replace(/[&<>']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;',""'"":'&#39;'}[c]));
let options=[],lockOn=false,expanded=new Set(),pickOpen=new Set(),pickers={},last={},lastJobs=[];
// Job maker state: the picked cars, the compatible-car set for the chosen cargo,
// the banked manifest lines, and the last yard snapshot. Selection survives
// refreshes; a station change clears everything.
let jmYardData=null,jmSelSet=new Set(),jmCompat=null,jmStation=null,jmLines=[],jmDest=null;
// The destination is sticky the moment the dispatcher touches it: cargo changes
// must never move it. Banked lines harden the stickiness into a hard lock.
let jmDestPicked=false;
function effDest(){return (jmLines.length&&jmDest)||(jmDestPicked?$('hDest').value:null)||null}
// Accepted-hauls station filter: left-clicks build the DESK (a set of stations;
// hauls touching any of them show), right-clicks hide stations. Both multi, both
// per browser, both surviving reloads.
const accSel=new Set(JSON.parse(localStorage.getItem('dleAccSel')||'[]'));
const accHidden=new Set(JSON.parse(localStorage.getItem('dleAccHidden')||'[]'));
function saveAccFilter(){
 localStorage.setItem('dleAccSel',JSON.stringify([...accSel]));
 localStorage.setItem('dleAccHidden',JSON.stringify([...accHidden]))}
const LOGI='__logi';
function isLogi(){return $('hCargo').value===LOGI}
function lineCarSet(){const s=new Set();for(const l of jmLines)for(const c of l.cars)s.add(c);return s}
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
 // Logi moves are paperwork-light: no loading, no pay, close on arrival. The card
 // offers assignment and cancellation, nothing else.
 if(x.logi){
  return `<div class='job'>
   <div class='jobtop'><span class='jid'>${esc(x.id)}</span>
    <span class='pill ${pillClass(x.state)}'>${esc(x.state)}</span>
    <span class='pill other' title='Unpaid dispatcher move; closes on its own when the cars arrive'>logi move</span>
    <span class='wage num' style='color:var(--dim)'>$0</span></div>
   <div class='route'><b>${esc(x.origin)}</b><span class='arr'>&#8594;</span><b>${esc(x.destination)}</b></div>
   <div class='meta'><b>${x.cars} car(s)</b> &middot; closes on arrival at the booklet's track</div>
   <div class='meta'>${x.assignedTo?`crew: <b>${esc(x.assignedTo)}</b>`:'dispatch move'}</div>
   <div class='acts'>
    <button data-act='fax' data-id='${esc(x.id)}' title='Fax the booklet: typed name or loco plate first, else the assigned crew, else you'>Fax</button>
    <input class='crew' id='a_${esc(x.id)}' placeholder='crew or loco' list='crewNames'>
    <button class='mini' data-act='assign' data-id='${esc(x.id)}'>Assign</button>
    <button class='mini' data-act='unassign' data-id='${esc(x.id)}'>Unassign</button>
    <button class='mini danger' data-act='delhaul' data-id='${esc(x.id)}' title='Cancel the move; the cars free up'>&times;</button>
   </div></div>`}
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
  <div class='meta'><b>${esc(disp(x.cargo))}</b> &middot; ${cars} cars${x.tonnes?` &middot; ${x.tonnes} t loaded`:''}${x.pickupTrack?` &middot; pickup <b>${esc(x.pickupTrack)}</b>`:''}</div>
  ${x.lines&&x.lines.length?`<div class='meta'>${x.lines.map(l=>`<b>${l.cars}</b> ${esc(disp(l.cargo))}${l.loaded?` (${l.loaded} loaded)`:''}${l.unpaid?' (unpaid)':''}`).join(' + ')}</div>`:''}
  <div class='meta'>${x.assignedTo?`crew: <b>${esc(x.assignedTo)}</b>`:'unassigned'}</div>
  <div class='acts'>${acts}
   <button data-act='fax' data-id='${esc(x.id)}' title='Fax the booklet: typed name first, else the assigned crew, else you'>Fax</button>
   <button class='mini' data-act='cars' data-id='${esc(x.id)}'>${expanded.has(x.id)?'Hide cars':'Cars'}</button>
   <button class='mini' data-act='findEmpties' data-id='${esc(x.id)}' title='Show every compatible car in the world for this cargo'>Find empties</button>
   <input class='crew' id='a_${esc(x.id)}' placeholder='crew or loco' list='crewNames'>
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
 sel.innerHTML=items.map(v=>`<option value='${esc(v)}'>${esc(disp(v))}</option>`).join('');
 if([...sel.options].some(o=>o.value===cur))sel.value=cur}
async function refresh(){
 let state,jobs,econ,hist;
 let crews;
 try{[state,options,jobs,econ,hist,crews]=await Promise.all([
  jget('/api/v1/state'),jget('/api/v1/options'),jget('/api/v1/jobs'),jget('/api/v1/economy'),jget('/api/v1/history?limit=60'),jget('/api/v1/players')]);
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
 // Perf and dormancy live in the footer: reference material, not headline.
 const pf=state.perf||{};
 const ftBits=[];
 if(pf.liveCars)ftBits.push(pf.liveCars+' live');
 if(state.dormantCars)ftBits.push(state.dormantCars+' dormant');
 if(pf.frameP95Ms)ftBits.push('p95 '+pf.frameP95Ms+'ms');
 if(pf.gc60s!=null&&pf.frameP95Ms)ftBits.push(pf.gc60s+' GC/min');
 $('ftStats').textContent=ftBits.join(' · ');
 $('ftStats').title='host frame p50 '+(pf.frameP50Ms||'?')+'ms, p95 '+(pf.frameP95Ms||'?')+'ms, worst '+(pf.frameMaxMs||'?')+'ms · '
  +(pf.hitches60s||0)+' hitches/60s · heap '+(pf.heapMb||'?')+'MB · dormant cars respawn on approach, on Wake, or when a booklet claims them · company.lag in the console for the full report';
 $('chipBoost').textContent='boost ×'+(state.globalBoost||1);
 const mw=state.machineWarnings||[];
 $('chipMachines').style.display=mw.length?'':'none';
 $('chipMachines').textContent='MACHINES LOW: '+mw.join(', ');
 keepSelect($('hOrigin'),[...new Set(econ.map(e=>e.yardId))].sort());
 originChanged();
 keepSelect($('fCargo'),['','any cargo'].concat([...new Set([].concat(options.map(o=>o.cargo),jobs.map(x=>x.cargo)))].sort()));
 lastEconData=econ;
 const netKey=JSON.stringify(options)+JSON.stringify(econ);
 if(last.net!==netKey){last.net=netKey;drawNet()}
 const jKey=JSON.stringify(jobs)+[...expanded].join()+'|'+[...accSel].join()+'|'+[...accHidden].join()+'|'+lastEconData.length;
 if(last.jobs!==jKey){last.jobs=jKey;
  const snap=snapshotCrew();
  const av=jobs.filter(x=>x.state==='Available'),ac=jobs.filter(x=>x.state!=='Available');
  // Every station gets a chip, hauls or not: a dispatcher sets their desk up
  // BEFORE the work exists. A haul belongs to a desk when EITHER end touches it:
  // the only-chip matches origin or destination, and a haul hides only when BOTH
  // of its ends are hidden. The filter is per browser (nothing goes server-side).
  const origins=[...new Set(lastEconData.map(e=>e.yardId))].sort();
  const perOrigin={};
  for(const x of ac){perOrigin[x.origin]=(perOrigin[x.origin]||0)+1;
   if(x.destination!==x.origin)perOrigin[x.destination]=(perOrigin[x.destination]||0)+1}
  const acShown=ac.filter(x=>accSel.size
   ?(accSel.has(x.origin)||accSel.has(x.destination))
   :!(accHidden.has(x.origin)&&accHidden.has(x.destination)));
  $('accFilter').innerHTML=origins.map(o=>{
   const cls=accSel.has(o)?' on':accHidden.has(o)?' off':'';
   return `<span class='fchip${cls}' data-act='accChip' data-id='${esc(o)}' title='click: add/remove ${esc(o)} from your desk (hauls touching any desk station show) &middot; right-click: hide/show ${esc(o)} (a haul hides when both its ends are hidden)'>${esc(o)}${perOrigin[o]?' '+perOrigin[o]:''}</span>`}).join('')
   +(accSel.size||accHidden.size?`<span class='fchip clear' data-act='accClear' title='clear the desk and every hide'>&times; all</span>`:'');
  $('cAvail').textContent=av.length||'';
  $('cAcc').textContent=acShown.length===ac.length?(ac.length||''):acShown.length+'/'+ac.length;
  $('availCards').innerHTML=av.length?av.map(x=>jobCard(x,true)).join(''):`<div class='empty'>${lockOn?'lock is on: the director is paused; create hauls above and assign them to crews':'no open hauls; spawn one above or wait for the director'}</div>`;
  $('accCards').innerHTML=acShown.length?acShown.map(x=>jobCard(x,false)).join(''):`<div class='empty'>${ac.length?'every haul here is hidden by the station filter':'nothing accepted yet'}</div>`;
  restoreCrew(snap)}
 for(const id of expanded)fillCars(id);
 for(const id of pickOpen)fillPicker(id);
 pollYard();
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
     `<td>${esc(trackDisp(c.track))}</td><td class='num'>${c.metersFromLoading==null?'':c.metersFromLoading+' m'}</td></tr>`).join('')+
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
function stockRow(s,cap,tag){
 const pct=cap>0?Math.min(100,Math.round(100*s.amount/cap)):0;
 const held=s.reserved>=1?` &middot; ${Math.round(s.reserved)} held`:'';
 const recv=s.imported>=1?` &middot; ${Math.round(s.imported)} received`:'';
 return `<div class='stockrow'><span class='cname' title='held = committed to a taken haul; received = delivered here, ships onward unpaid until consumed; bars show the share of the station total'>${esc(disp(s.cargo))} <span class='ctag'>${cargoClass(s.cargo)}</span>${tag||''}</span>`+
  `<div class='bar'><i style='width:${pct}%'></i></div>`+
  `<span class='nums num'>${Math.round(s.amount)}${held}${recv}</span></div>`;
}
// Brand bundles, mirroring the mod's CargoCategories: a recipe input naming a
// category is satisfied by any member brand in stock.
const CATS={Tools:['ToolsIskar','ToolsBrohm','ToolsAAG','ToolsNovae','ToolsTraeg'],
 Electronics:['ElectronicsIskar','ElectronicsKrugmann','ElectronicsAAG','ElectronicsNovae','ElectronicsTraeg'],
 Clothing:['ClothingObco','ClothingNeoGamma','ClothingNovae','ClothingTraeg'],
 Chemicals:['ChemicalsIskar','ChemicalsSperex'],
 Gases:['CryoHydrogen','Ammonia','SodiumHydroxide'],
 EmptyContainers:['EmptySunOmni','EmptyIskar','EmptyObco','EmptyGoorsk','EmptyKrugmann',
  'EmptyBrohm','EmptyAAG','EmptySperex','EmptyNovae','EmptyTraeg','EmptyChemlek','EmptyNeoGamma']};
// Display names: the one-cargo bundles read as their category on the board; the
// API keeps the real enum names underneath.
const DISP={ToolsIskar:'Tools',ElectronicsIskar:'Electronics',ChemicalsIskar:'Chemicals',
 __logi:'Logistics move (no cargo, unpaid)',None:'Empty riders'};
function disp(c){return DISP[c]||c}
function lineDisp(c){return c===LOGI?'Empty riders':disp(c)}
// Cargo classes: RESOURCES come out of the ground and the farm, MATERIALS are
// processed intermediates, everything else is finished goods.
const RESOURCES=new Set(['IronOre','Coal','Logs','CrudeOil','Methane','ScrapMetal','ScrapWood',
 'Wheat','Corn','Milk','Eggs','Cotton','Wool','SunflowerSeeds','Pigs','Cows','Poultry','Sheep','Goats',
 'TemperateFruits','Vegetables','Flour','Fish']);
const MATERIALS=new Set(['SteelRolls','SteelBillets','SteelSlabs','SteelBentPlates','SteelRails',
 'Boards','Plywood','Sleepers','WoodChips','Pipes','Gasoline','Diesel','ChemicalsIskar',
 'CryoHydrogen','Ammonia','SodiumHydroxide','Argon','CryoOxygen','Nitrogen','Acetylene','AmmoniumNitrate']);
function cargoClass(c){return RESOURCES.has(c)?'resource':MATERIALS.has(c)?'material':'goods'}
// Stock rows are keyed by FAMILY now, not by brand: the server groups them so a
// factory reads Tools 12 instead of five brand piles. Recipe inputs still name
// either a category or a concrete brand, so every lookup resolves to the family
// name first. Without this, an input naming a brand (or a category whose members
// are brands) found no row at all and every branded ingredient read as missing,
// which is what put a false waiting-on line on a station that held plenty.
function famOf(c){
 if(CATS[c])return c;
 for(const k in CATS)if(CATS[k].indexOf(c)>=0)return k;
 return c}
function stockAmt(n,cargo){
 const s=(n.stock||[]).find(x=>x.cargo===famOf(cargo));return s?s.amount:0}
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
   <title>${esc(e.src)} to ${esc(e.dst)}: ${esc(e.cargos.map(disp).join(', '))} (${Math.round(e.stock)} shippable)</title></path>`;
 }
 for(const id in nodes){
  const n=nodes[id];const p=NET_POS[id];
  const cls=n.importHub?'hub':(n.source?'source':(n.consumer||(n.outputs||[]).length===0?'sink':'factory'));
  const st=NET_STYLE[cls];
  const miss=netMissing(n);
  const r=cls==='hub'?36:30;
  const dim=sel&&id!==sel&&!edges.some(e=>(e.src===sel&&e.dst===id)||(e.dst===sel&&e.src===id));
  h+=`<g class='nnode' data-act='netNode' data-id='${esc(id)}' transform='translate(${p[0]},${p[1]})' opacity='${dim?0.25:1}'>
   <circle r='${r}' fill='${st.fill}' stroke='${miss.length?'#e07a6a':(n.machineWarning?'#e8b64c':st.stroke)}' stroke-width='${sel===id?3:1.5}'/>
   <text y='-1' text-anchor='middle' dominant-baseline='middle' fill='#eef2f8' font-size='14' font-weight='700'>${esc(id)}</text>
   <text y='14' text-anchor='middle' fill='#8b95a5' font-size='8.5'>${esc(NET_NAMES[id]||'')}</text>
   <title>${esc(id)}${miss.length?': waiting on '+esc(miss.join(', ')):''}</title></g>`;
 }
 svg.innerHTML=h;
 renderNetDetail(nodes,edges,sel);
}
// Station panel (#126). The rule: a row earns its place by being actionable. Status
// strip up top, what the station NEEDS, what it CAN SHIP; the full inventory lives
// in a nested fold tree the dispatcher opens on purpose.
let netFolds=new Set();
function fold(key,title,inner,count){
 const open=netFolds.has(key);
 return `<div class='foldbtn' data-act='netFold' data-key='${key}'>${open?'&#9662;':'&#9656;'} ${title}${count!=null?` <span class='count'>${count}</span>`:''}</div>`+
  (open?`<div class='foldbody'>${inner}</div>`:'')}
function renderNetDetail(nodes,edges,sel){
 const d=$('netDetail');if(!d)return;
 if(!sel){d.className='netdetail';d.innerHTML='';return}
 const n=nodes[sel];
 const cap=Math.round(n.totalCap||0),used=Math.round(n.totalStock||0);
 const upct=n.totalCap>0?Math.min(100,Math.round(100*used/n.totalCap)):0;
 const barCls=upct>=95?'crit':upct>=80?'warn':'';
 const miss=netMissing(n);
 let h=`<div class='sthead'><b style='font-size:15px'>${esc(sel)}</b><span class='meta'>${esc(NET_NAMES[sel]||'')}</span>`;
 if(miss.length)h+=`<span class='stchip bad'>waiting on ${esc(miss.map(disp).join(', '))}</span>`;
 if(n.machineWarning)h+=`<span class='stchip warn'>machines low</span>`;
 const catNames=esc((n.catalysts||[]).map(disp).join(' or '));
 if((n.catalysts||[]).length)h+=`<span class='stchip ${n.catalystActive?'good':'idle'}' title='${n.source?'slows machine wear':'doubles batch speed'} while active'>${n.catalystActive?'catalyst ('+catNames+') active &middot; '+n.catalystHoursLeft+'h left':n.catalystStocked?'catalyst ('+catNames+') stocked':'no catalyst ('+catNames+')'}</span>`;
 if(upct>=95)h+=`<span class='stchip bad'>storage full</span>`;
 else if(upct>=80)h+=`<span class='stchip warn'>storage ${upct}%</span>`;
 h+=`<span class='spacer'></span><button class='mini' data-act='jmOpen' data-id='${esc(sel)}'>Open in Job maker</button></div>`;
 if(cap>0)h+=`<div class='stockrow'><span class='cname' title='one shared pool: every cargo counts against the same total'><b>storage</b></span>`+
  `<div class='bar'><i class='${barCls}' style='width:${upct}%'></i></div><span class='nums num'>${used} / ${cap}</span></div>`;
 const needs=[];
 for(const r of (n.recipes||[]))for(const i of (r.inputs||[])){
  const have=stockAmt(n,i.cargo);
  if(have<i.amount&&!needs.some(x=>x.cargo===i.cargo))needs.push({cargo:i.cargo,have,need:i.amount})}
 if(needs.length){h+=`<div class='sublab'>needs</div>`;
  needs.sort((a,b)=>disp(a.cargo).localeCompare(disp(b.cargo)));
  h+=needs.map(x=>`<div class='needrow'><b>${esc(disp(x.cargo))}</b><span class='num'>${Math.round(x.have)} of ${x.need} on hand</span></div>`).join('')}
 // Shippable stock renders INSIDE the Produced fold as each pile's destinations;
 // the panel's top level stays status, storage and needs only (owner ruling).
 const outs=edges.filter(e=>e.src===sel);
 const outFams=new Set((n.outputs||[]).map(famOf));
 const ship=[];
 for(const s of (n.stock||[])){
  if(!outFams.has(famOf(s.cargo))||s.amount<1)continue;
  const dests=[...new Set(outs.filter(e=>e.cargos.some(c=>famOf(c)===famOf(s.cargo))).map(e=>e.dst))];
  ship.push({cargo:s.cargo,amount:s.amount,dests})}
 if(!n.source&&(n.outputs||[]).length===0)
  h+=`<div class='nrecipe meta'>${n.consumer?'consumes its stock on the clock; keeping it fed boosts every industry':'accepts <b>'+esc((n.inputs||[]).map(disp).join(', '))+'</b>; storage is the demand'}</div>`;
 if(n.source&&!ship.length)h+=`<div class='nrecipe meta'>produces ${esc((n.outputs||[]).map(disp).join(', '))} over time; nothing shippable yet</div>`;
 if(n.importHub)h+=`<div class='nrecipe meta'>imports scale with the exports delivered here</div>`;
 // Full inventory: everything that is true but not urgent, one fold with four
 // folds inside, so the panel reads top-down: state, needs, shippable, archive.
 const byName=(a,b)=>disp(a.cargo).localeCompare(disp(b.cargo));
 const rows=(n.stock||[]);
 const dprod=rows.filter(s=>outFams.has(famOf(s.cargo))).sort(byName);
 const dcons=rows.filter(s=>!outFams.has(famOf(s.cargo))).sort(byName);
 const gsum=g=>Math.round(g.reduce((t,s)=>t+(s.amount||0),0));
 let recipesH='';
 if(n.source)recipesH+=`<div class='nrecipe'>produces resources over time: <b>${esc((n.outputs||[]).map(disp).join(', '))}</b></div>`;
 if((n.recipes||[]).length)
  recipesH+=n.recipes.map(r=>`<div class='nrecipe'>needs ${r.inputs.map(i=>esc(i.amount+' '+disp(i.cargo))).join(' + ')} &#8594; makes ${r.outputs.map(o=>esc(o.amount+' '+disp(o.cargo))).join(' + ')}</div>`).join('');
 if(!recipesH)recipesH=`<div class='meta'>no recipes; storage itself is the demand</div>`;
 // Each produced pile carries its live destinations (the old Can-ship rows, folded
 // in next to their totals); consumed piles are tagged when they are the machine
 // or the catalyst, so what a pile is FOR is never a mystery.
 const shipMap={};for(const x of ship)shipMap[famOf(x.cargo)]=x.dests;
 const prodH=dprod.length?dprod.map(s=>{
  const ds=shipMap[famOf(s.cargo)];
  const shipTag=ds?` <span class='tag' style='background:#16283c;color:var(--blue)'>can ship</span>`:'';
  return stockRow(s,n.totalCap||0,shipTag)+
   (ds?`<div class='shipto'>&#8594; ${ds.length?esc(ds.join(', ')):'no consumer has room'}</div>`:'')}).join('')
  :`<div class='meta'>nothing produced on hand</div>`;
 const catSet=new Set((n.catalysts||[]).map(famOf));
 const machSet=new Set((n.machines||[]).map(m=>famOf(m.cargo)));
 const consH=dcons.length?dcons.map(s=>{
  const f2=famOf(s.cargo);
  const tag=machSet.has(f2)?` <span class='tag'>machine</span>`:catSet.has(f2)?` <span class='tag' style='background:#173424;color:var(--green)'>catalyst</span>`:'';
  return stockRow(s,n.totalCap||0,tag)}).join('')
  :`<div class='meta'>nothing on hand to work through</div>`;
 let machH='';
 if((n.machines||[]).length){
  for(const m of n.machines){
   const cls=m.have<=0?'out':m.have<2?'low':'';
   machH+=`<div class='machrow'><span class='mname'>${esc(m.cargo)}</span>`+
    `<span class='mcount ${cls}'>&times;${m.have}${m.have<=0?' &middot; CRAWLING':m.have<2?' &middot; last one':''}</span>`+
    `<span class='mwear'>current unit: ${m.wearRemaining} carloads of work left</span></div>`;
  }
 }
 if((n.catalysts||[]).length){
  machH+=`<div class='sublab'>catalyst &middot; ${esc(n.catalysts.join(' or '))}</div>`+
   `<div class='nrecipe' style='color:${n.catalystActive?'var(--green)':'var(--dim)'}'>`+
   (n.catalystActive?`active &middot; ${n.catalystHoursLeft}h of work left on this carload`
    :n.catalystStocked?'in stock, starts with the next shift':'none in stock')+
   ` <span class='meta'>(${n.source?'slows machine wear':'doubles batch speed'})</span></div>`;
 }
 if(!machH)machH=`<div class='meta'>no machines required here</div>`;
 const ins=edges.filter(e=>e.dst===sel);
 const inH=ins.length?ins.map(e=>`<div class='nrecipe'>${esc(e.src)}: ${esc(e.cargos.map(disp).join(', '))}</div>`).join(''):`<div class='meta'>nothing inbound on the map</div>`;
 h+=fold('inv','Full inventory',
  fold('inv-r','Recipes',recipesH)+
  fold('inv-p','Produced',prodH,gsum(dprod)||null)+
  fold('inv-c','Consumes',consH,gsum(dcons)||null)+
  fold('inv-m','Machines and catalyst',machH)+
  fold('inv-i','Consumption supply points',inH));
 d.className='netdetail show';d.innerHTML=h;
}
// Unnamed world tracks come through as raw ids like #Y-#S1437#T; read them as
// what they are: a numbered siding outside any yard.
function trackDisp(t){if(!t||t[0]!=='#')return t;const m=String(t).match(/S(\d+)/);
 return m?'siding '+m[1]:'siding'}
function renderFleet(r){
 $('fSummary').textContent=(r.total+(r.dormant||0))+' freight car(s), '+r.usable+' usable now';
 $('fSummary').title='locomotives and tenders are not listed; the footer live count includes them';
 const groups={};
 for(const c of r.cars){const k=(c.yard||'~')+'|'+c.track;(groups[k]=groups[k]||[]).push(c)}
 const keys=Object.keys(groups).sort();
 $('tFleet').innerHTML=keys.length?'<tr><th>Yard</th><th>Track</th><th>Usable</th><th>Cars</th></tr>'+
  keys.map(k=>{const g=groups[k];g.sort((a,b)=>(b.usable?1:0)-(a.usable?1:0));
   const u=g.filter(c=>c.usable).length;
   return `<tr><td>${esc(g[0].yard&&g[0].yard[0]!=='#'?g[0].yard:'')}</td><td>${esc(trackDisp(g[0].track))}</td><td class='num'>${u}/${g.length}</td><td>`+
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
// Job maker yard canvas (#118, #119): tracks with cars in consist order. Polls with
// the refresh cycle while the section is open; re-renders only when the yard changed.
let yardKey='',yardBusy=false;
async function pollYard(force){
 const y=$('hOrigin').value;
 if(!y||(!force&&closedSecs.has('create'))||yardBusy)return;
 yardBusy=true;
 try{const r=await jget('/api/v1/yard?yard='+encodeURIComponent(y));
  if($('hOrigin').value!==y)return;
  const k=JSON.stringify(r);
  if(force||k!==yardKey){yardKey=k;jmYardData=r;renderYard()}}
 catch(e){}
 finally{yardBusy=false}}
function renderYard(){
 const box=$('jmYard');if(!box)return;
 const d=jmYardData;
 if(!d){box.innerHTML=`<div class='empty'>pick a station to see its yard</div>`;$('jmMeta').textContent='';return}
 // Re-rendering wipes each track row's horizontal scroll, which made the view
 // jump home every poll while staff were loading. Capture and restore per track.
 const scrolls={};
 box.querySelectorAll('.ytrack').forEach(el=>{
  const sc=el.querySelector('.ycars');
  if(el.dataset.track&&sc&&sc.scrollLeft)scrolls[el.dataset.track]=sc.scrollLeft});
 let total=0;
 const inLine=lineCarSet();
 const rows=(d.tracks||[]).map(t=>{
  total+=t.carCount;
  const cuts=(t.cuts||[]).map(cut=>`<span class='ycut'>`+cut.map(c=>{
   const banked=inLine.has(c.carId);
   const on=jmSelSet.has(c.carId);
   const compat=jmCompat===null||jmCompat.has(c.carId);
   const cls=on?'sel':c.loco?'loco':banked?'inline':c.usable?(compat?'ok':'incompat'):(c.cargo?'loaded':'');
   const why=c.loco?'locomotive':banked?'banked in a manifest line':c.cargo?('loaded: '+c.cargo):c.jobId?('on job '+c.jobId):c.reservedBy?('reserved for '+c.reservedBy):c.playerSpawned?'player car':compat?'empty and free':'cannot carry the chosen cargo';
   return `<span class='ycar ${cls}' data-act='ycar' data-car='${esc(c.carId)}' title='${esc(c.type)} &middot; ${esc(why)}'>${esc(c.carId)}</span>`}).join('')+`</span>`)
   .join(`<span class='meta' style='flex:none;align-self:center'>&middot;</span>`);
  const e=(t.ends||'').split('|');
  return `<div class='ytrack' data-track='${esc(t.track)}'><span class='ytlabel'><b>${esc(t.track)}</b>`+
   `${t.warehouse?` <span class='ctag' style='color:var(--amber)' title='${esc((t.warehouseCargos||[]).join(', '))}'>loading</span>`:''}`+
   `<br><span class='num'>${t.usedM}/${t.lengthM}m &middot; ${t.carCount+(t.dormantCount||0)} car(s)</span></span>`+
   `<span class='yend'>${esc(e[0]||'')}</span>`+
   `<div class='ycars'>${cuts||`<span class='meta'>empty</span>`}</div>`+
   `<span class='yend'>${esc(e[1]||'')}</span></div>`});
 box.innerHTML=rows.join('')||`<div class='empty'>no yard tracks reported</div>`;
 box.querySelectorAll('.ytrack').forEach(el=>{
  const sc=el.querySelector('.ycars');
  if(el.dataset.track&&sc&&scrolls[el.dataset.track])sc.scrollLeft=scrolls[el.dataset.track]});
 $('jmMeta').textContent=(d.name||'')+' · '+(total+(d.dormantCars||0))+' cars in yard';
}
// Which usable cars can carry the chosen cargo. The fleet endpoint already answers
// this; the yard view just borrows its verdict.
let compatSeq=0,compatKey='',compatAt=0;
async function fetchCompat(){
 const y=$('hOrigin').value,c=$('hCargo').value;
 if(!y||!c){if(jmCompat!==null){jmCompat=null;renderYard()}compatKey='';return}
 // Refresh calls through here every cycle; only actually ask when the pair changed
 // or the verdict is stale (cars move, load, get reserved).
 const key=y+'|'+c,now=Date.now();
 if(key===compatKey&&now-compatAt<15000)return;
 compatKey=key;compatAt=now;
 const seq=++compatSeq;
 try{const r=await jget('/api/v1/fleet?cargo='+encodeURIComponent(c)+'&yard='+encodeURIComponent(y));
  if(seq!==compatSeq)return;
  jmCompat=new Set((r.cars||[]).filter(x=>x.usable).map(x=>x.carId));
  let dropped=0;
  for(const id of [...jmSelSet])if(!jmCompat.has(id)){jmSelSet.delete(id);dropped++}
  if(dropped)toast(dropped+' picked car(s) cannot carry '+disp(c)+'; dropped',true);
  syncSelUi();renderYard()}
 catch(e){}}
function syncSelUi(){
 const n=jmSelSet.size,inp=$('hCars');
 if(n>0){inp.value=n;inp.disabled=true}else inp.disabled=false;
 $('jmSel').innerHTML=n
  ?`<b>${n}</b> car(s) picked${jmLines.length?' for the next line':''}; the booklet takes exactly these`
  :jmLines.length?'pick cars for another line, or create the booklet from the banked lines'
  :'no cars picked: the booklet goes out carless and crews or staff auto-pick bring empties';
 updateEstimate()}
// The banked manifest: each line is a cargo and its exact cars; one booklet, one
// destination, every line aboard.
function renderManifest(){
 const box=$('jmManifest');if(!box)return;
 if(!jmLines.length){box.innerHTML='';return}
 let cars=0,pay=0;
 let per=0;
 const rows=jmLines.map((l,i)=>{cars+=l.cars.length;pay+=l.pay||0;if(l.per)per=l.per;
  return `<div class='mline'><b>${l.cars.length} &times; ${esc(lineDisp(l.cargo))}</b>`+
   `<span class='meta'>${l.cars.map(esc).join(', ')}</span>`+
   `${l.pay?`<span class='num' style='color:var(--green)'>${money(l.pay)}</span>`:''}`+
   `<button class='mini danger' data-act='jmDelLine' data-id='${i}'>&times;</button></div>`});
 const staff=cars>1&&per?` &middot; staff load &#8776; ${fmtSecs((cars-1)*per)}`:'';
 box.innerHTML=rows.join('')+
  `<div class='mline total'><b>${cars} car(s), ${jmLines.length} line(s)</b>`+
  `${pay?`<span class='num' style='color:var(--green)'>&#8776; ${money(pay)}${staff}</span>`:''}`+
  `<span class='meta'>one booklet on create; picked cars still count as their own line</span></div>`}
function crewVal(id){const i=$('a_'+id);return i&&i.value?i.value:null}
const actions={
 lock:async()=>{const r=await j('/api/v1/lock','PUT',{enabled:!lockOn});
  toast('Assignment lock is now '+(r.lockEnabled?'ON':'OFF')+(r.purged?'; '+r.purged+' open booklet(s) expired, supply returned':''));refresh()},
 // One create path (#118): banked lines plus the current pick become the manifest.
 // A LOGI line means empty riders; a manifest that is ONLY riders is a logi move.
 spawnHaul:async()=>{
  const o=$('hOrigin').value,d=$('hDest').value,c=$('hCargo').value;
  if(!d){toast('choose a destination first',true);return}
  const sel=[...jmSelSet];
  const lines=jmLines.map(l=>({cargo:l.cargo,cars:l.cars}));
  if(sel.length){
   if(!c){toast('choose a cargo for the picked cars',true);return}
   lines.push({cargo:c,cars:sel})}
  if(lines.length){
   if(lines.every(l=>l.cargo===LOGI)){
    const carIds=[].concat(...lines.map(l=>l.cars));
    const r=await j('/api/v1/hauls','POST',{origin:o,destination:d,logi:true,carIds});
    if(r.jobId){toast('Move '+r.jobId+' created; '+(r.note||'closes on arrival'));
     const crew=$('hCrew').value;
     if(crew)await j('/api/v1/assignments/'+r.jobId,'PUT',{player:crew,assignedBy:'job maker'});
     jmLines=[];jmSelSet.clear();renderManifest();syncSelUi()}
    else toast('Failed: '+(r.error||'see game log'),true);
    refresh();return}
   const body={origin:o,destination:d,lines:lines.map(l=>({cargo:l.cargo===LOGI?'__logi':l.cargo,cars:l.cars}))};
   const r=await j('/api/v1/hauls','POST',body);
   if(r.jobId){toast('Created '+r.jobId+(lines.length>1?' with '+lines.length+' line(s)':''));
    await afterCreate(r.jobId,false);
    jmLines=[];jmSelSet.clear();renderManifest();syncSelUi()}
   else toast('Failed: '+(r.error||'see game log'),true);
   refresh();return}
  const b={origin:o,destination:d,cargo:c,cars:parseInt($('hCars').value)};
  if(!b.cargo||isLogi()){toast(isLogi()?'pick the cars to move first':'choose cargo and destination first',true);return}
  const r=await j('/api/v1/hauls','POST',b);
  if(r.jobId){toast('Created '+r.jobId+(r.unpaid?' as an UNPAID move (produced stock is short; this relocates received goods)':''));
   await afterCreate(r.jobId,false)}
  else toast('Failed: '+(r.error||'see game log'),true);
  refresh()},
 // The dispatcher-directions flow (#129): create the booklet (picked cars come
 // attached), take it, and put station staff straight onto the cars.
 spawnHaulLoad:async()=>{
  const o=$('hOrigin').value,d=$('hDest').value,c=$('hCargo').value;
  const sel=[...jmSelSet];
  const lines=jmLines.map(l=>({cargo:l.cargo,cars:l.cars}));
  if(sel.length){
   if(!c){toast('choose a cargo for the picked cars',true);return}
   lines.push({cargo:c,cars:sel})}
  if(!lines.length){toast('pick cars in the yard first',true);return}
  if(lines.every(l=>l.cargo===LOGI)){toast('riders carry nothing; use Create booklet for a logi move',true);return}
  if(!d){toast('choose a destination first',true);return}
  const body={origin:o,destination:d,lines:lines.map(l=>({cargo:l.cargo===LOGI?'__logi':l.cargo,cars:l.cars}))};
  const r=await j('/api/v1/hauls','POST',body);
  if(!r.jobId){toast('Failed: '+(r.error||'see game log'),true);return}
  await afterCreate(r.jobId,true); // staff loading needs the job taken
  const l=await j('/api/v1/jobs/'+r.jobId+'/load','POST');
  toast('Created '+r.jobId+'; '+(l.message||'load failed'),!l.ok);
  jmLines=[];jmSelSet.clear();renderManifest();syncSelUi();setTimeout(refresh,1200)},
 jmAddLine:async()=>{
  const c=$('hCargo').value,d=$('hDest').value,sel=[...jmSelSet];
  if(!c){toast('choose a cargo first',true);return}
  if(!d){toast('choose the destination first: every line of the booklet goes there',true);return}
  if(!sel.length){toast('pick the cars for this line first',true);return}
  const line={cargo:c,cars:sel,pay:0,per:0};
  try{if(c!==LOGI){const r=await jget(`/api/v1/estimate?origin=${encodeURIComponent($('hOrigin').value)}&destination=${encodeURIComponent(d)}&cargo=${encodeURIComponent(c)}&cars=${sel.length}`);line.pay=r.pay||0;line.per=r.perCarSeconds||0}}catch(e){}
  jmLines.push(line);
  if(jmLines.length===1)jmDest=d; // the first line locks the booklet's destination
  jmSelSet.clear();renderManifest();syncSelUi();renderYard();originChanged();
  toast('Line banked: '+sel.length+' x '+lineDisp(c)+' to '+jmDest+'; pick cars for the next cargo')},
 jmDelLine:(id,el)=>{const i=parseInt(el.dataset.id);if(!(i>=0)||i>=jmLines.length)return;
  jmLines.splice(i,1);if(!jmLines.length)jmDest=null;
  renderManifest();renderYard();syncSelUi();originChanged()},
 jmClear:()=>{jmSelSet.clear();jmLines=[];jmDest=null;
  renderManifest();syncSelUi();renderYard();originChanged()},
 jmOpen:(id,el)=>{const v=el.dataset.id;const os=$('hOrigin');
  if(![...os.options].some(x=>x.value===v)){toast('station not on the board yet',true);return}
  openSec('create');os.value=v;originChanged();
  document.querySelector(`section[data-sec='create']`).scrollIntoView({behavior:'smooth'})},
 ycar:(id,el)=>{const car=el.dataset.car;
  if(el.classList.contains('inline')){toast('that car is banked in a manifest line; remove the line to free it',true);return}
  if(el.classList.contains('incompat')){toast('that car cannot carry the chosen cargo',true);return}
  if(jmSelSet.has(car))jmSelSet.delete(car);
  else if(el.classList.contains('ok'))jmSelSet.add(car);
  else return;
  syncSelUi();renderYard()},
 netFold:(id,el)=>{const k=el.dataset.key;
  netFolds.has(k)?netFolds.delete(k):netFolds.add(k);drawNet()},
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
 // Unload doubles as the undo: a consist standing back at its ORIGIN returns the
 // supply to the pile and cancels the booklet, after a confirm.
 unload:async id=>{const x=lastJobs.find(v=>v.id===id);
  if(x&&x.carsAtOrigin&&!x.logi){
   if(!confirm('The cars are standing at the ORIGIN ('+x.origin+'). Return the supply to the station and cancel the booklet?'))return;
   const r=await j('/api/v1/jobs/'+id+'/return','POST');
   toast(r.message||'failed',!r.ok);setTimeout(refresh,1200);return}
  const r=await j('/api/v1/jobs/'+id+'/unload','POST');
  toast(r.message||'failed',!r.ok);setTimeout(refresh,1200)},
 fax:async id=>{const r=await j('/api/v1/jobs/'+id+'/fax','POST',{player:crewVal(id)});
  toast(r.message||'failed',!r.ok)},
 assign:async id=>{const p=crewVal(id);if(!p){toast('enter a crew name first',true);return}
  const r=await j('/api/v1/assignments/'+id,'PUT',{player:p,assignedBy:'board'});
  toast(r.ok?'Assigned '+id+' to '+p:'assign failed',!r.ok);refresh()},
 unassign:async id=>{await j('/api/v1/assignments/'+id,'DELETE');toast('Unassigned '+id);refresh()},
 delhaul:async id=>{const x=lastJobs.find(v=>v.id===id)||{};
  const msg=x.logi?('Cancel move '+id+'? The cars free up where they stand.')
   :x.loadedCars>0?('Abandon supply? '+x.loadedCars+' loaded carload(s) on '+id+' will be LOST (the unloaded remainder returns to '+x.origin+'). If the cars are back at '+x.origin+', use Unload there to return everything instead.')
   :x.cars>0?('Close '+id+'? Its cars free up and its supply returns to '+x.origin+'.')
   :('Delete '+id+'? Its supply returns to the pile.');
  if(!confirm(msg))return;
  const r=await j('/api/v1/jobs/'+id,'DELETE');toast(r.message||(r.ok?'Deleted '+id:'delete failed'),!r.ok);refresh()},
 accChip:(id,el)=>{const o=el.dataset.id;
  accSel.has(o)?accSel.delete(o):accSel.add(o);
  accHidden.delete(o); // a desk station is never simultaneously hidden
  saveAccFilter();last.jobs=null;refresh()},
 accClear:()=>{accSel.clear();accHidden.clear();saveAccFilter();last.jobs=null;refresh()},
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
};
// Post-create wiring: a typed crew name records who the haul is for; the take box
// (or a flow that needs it) takes the booklet the moment it exists.
async function afterCreate(jobId,forceTake){
 const crew=$('hCrew').value,take=forceTake||$('hTake').checked;
 try{
  if(crew)await j('/api/v1/assignments/'+jobId,'PUT',{player:crew,assignedBy:'job maker'});
  if(take){const t=await j('/api/v1/jobs/'+jobId+'/take','POST',{player:crew||null});
   if(!t.ok){toast('created, but take failed: '+(t.message||''),true);return}
   // A named crew on a taken booklet gets the paper in hand without asking.
   if(crew){const f=await j('/api/v1/jobs/'+jobId+'/fax','POST',{player:crew});
    toast(f.ok?'booklet faxed to '+crew:'fax failed: '+(f.message||''),!f.ok)}}
 }catch(e){}}
document.addEventListener('click',e=>{const el=e.target.closest('[data-act]');if(!el)return;
 const fn=actions[el.dataset.act];if(fn)fn(el.dataset.id,el)});
function originChanged(){const o=$('hOrigin').value;
 if(o!==jmStation){jmStation=o;jmSelSet.clear();jmCompat=null;jmYardData=null;yardKey='';
  jmLines=[];jmDest=null;jmDestPicked=false;renderManifest();syncSelUi();renderYard();pollYard(true)}
 // A logi move ships from anywhere, so the option is always on the menu. Once a
 // destination is chosen (touched or line-locked), cargo that cannot go there is
 // HIDDEN, so an impossible line can never even be assembled.
 const ed=effDest();
 keepSelect($('hCargo'),options.filter(x=>x.origin===o&&(!ed||(x.consumers||[]).includes(ed))).map(x=>x.cargo).concat([LOGI]));
 cargoChanged()}
function cargoChanged(){const o=$('hOrigin').value,c=$('hCargo').value;
 const locked=jmLines.length>0&&jmDest;
 const allYards=[...new Set(lastEconData.map(e=>e.yardId))].filter(y=>y!==o).sort();
 // The destination menu never narrows to one cargo's consumers once the pick is
 // sticky: that narrowing was what silently swapped the booklet's destination.
 const union=[...new Set([].concat(...options.filter(x=>x.origin===o).map(x=>x.consumers||[])))].sort();
 let destOpts;
 if(locked)destOpts=[jmDest];
 else if(c===LOGI)destOpts=allYards;
 else if(jmDestPicked)destOpts=union.length?union:allYards;
 else{const opt0=options.find(x=>x.origin===o&&x.cargo===c);destOpts=opt0?opt0.consumers:[]}
 // The sticky pick can never fall out of its own menu, whatever list is showing.
 const ed2=effDest();
 if(!locked&&ed2&&!destOpts.includes(ed2))destOpts=[ed2].concat(destOpts);
 keepSelect($('hDest'),destOpts);
 $('hDest').disabled=!!locked;
 if(c===LOGI){jmCompat=null;compatKey='';renderYard();updateEstimate();return}
 fetchCompat();updateEstimate()}
// Live haul estimate: weight, length, pay and staff loading time for the form
// as it stands. Debounced; a stale response never overwrites a newer one.
let estTimer=null,estSeq=0,lastEstQ='';
function updateEstimate(){
 clearTimeout(estTimer);
 estTimer=setTimeout(async()=>{
  const o=$('hOrigin').value,c=$('hCargo').value,d=$('hDest').value,n=parseInt($('hCars').value);
  const box=$('hEstimate');
  if(c===LOGI){box.textContent=jmLines.length?'riders: these cars travel empty with the booklet':'unpaid move; closes on arrival';lastEstQ='';return}
  if(!o||!c||!d||!(n>0)){box.textContent='';lastEstQ='';return}
  const q=`${o}|${c}|${d}|${n}`;
  if(q===lastEstQ)return;
  lastEstQ=q;
  const seq=++estSeq;
  try{const r=await jget(`/api/v1/estimate?origin=${encodeURIComponent(o)}&destination=${encodeURIComponent(d)}&cargo=${encodeURIComponent(c)}&cars=${n}`);
   if(seq!==estSeq)return;
   box.innerHTML=`&#8776; ${r.tonnes} t &middot; ${r.lengthMeters} m &middot; <b style='color:var(--green)'>${money(r.pay)}</b> &middot; staff load ${fmtSecs(r.remoteLoadSeconds)}`}
  catch(e){if(seq===estSeq)$('hEstimate').textContent=''}
 },250)}
$('hOrigin').addEventListener('change',originChanged);
$('hCargo').addEventListener('change',cargoChanged);
$('hDest').addEventListener('change',()=>{jmDestPicked=true;originChanged()});
$('hCars').addEventListener('input',updateEstimate);
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
document.addEventListener('click',e=>{
 if(e.target.closest('[data-act]'))return; // chips inside a heading are not a fold toggle
 const h=e.target.closest('h2');if(!h)return;
 const s=h.closest('section[data-sec]');if(!s)return;
 const k=s.dataset.sec;closedSecs.has(k)?closedSecs.delete(k):closedSecs.add(k);
 localStorage.setItem('dleClosed',JSON.stringify([...closedSecs]));applySecs()});
document.addEventListener('contextmenu',e=>{
 const el=e.target.closest(`[data-act='accChip']`);if(!el)return;
 e.preventDefault();const o=el.dataset.id;
 accHidden.has(o)?accHidden.delete(o):accHidden.add(o);
 accSel.delete(o); // hiding a station takes it off the desk
 saveAccFilter();
 last.jobs=null;refresh()});
applySecs();
refresh();setInterval(refresh,5000);
</script></body></html>
";
    }
}
