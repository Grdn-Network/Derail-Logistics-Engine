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
<meta name='theme-color' content='#161826'>
<link rel='icon' type='image/png' href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAACeUlEQVR4AexWS2gUQRB907NJ1CVKzGIk4MEPIioeRFjMQQQxN6MQryLqxYBEEHLwJHrxIAiKEC8qfsCLguYiCIJ4UBZEMBgRES9eVGIUwWCSmWm7alNL92yHLLibWUiWfV3dU1Vdb6qrZkZ1re3WWUIh498SAScD5/aF+HCmxcHo6RZc7QvRuSLgw1rZBjw+kmObW4dzfC09rO8I8HKgbJPej9a2n0MgvRGtW0Ogd5PCzf4QBUNCGR4E0tUDXgJ/pjWGSwkuvYjx7pvmOJsLAQ5tM9F5VftQ+qJx8Xni4HoprmzgJTBl9CPvE9x4nWDoSYwfkxqBiV1cZ4aKa22TzxMat9/EDoiUeHsJiJLkTxN8YpJmQK6euS9vCTUrMxMNJ7BhdYCjO0PGflPM6UNsOAGqm7N7FQiDPQqrlrnJbjgBO1yiAYJ9reEE7r9NsOXyDOPg3Qi/p+zwWARF6N5v9WreI+huD1DIB+wZpQ+Qr/7f4CXQZp7/fVsVTuxSuNAbomM5oE0B2U8wCrsmD24vaTOS1GqkE9htSHoCdYbovQTyrQEGigpDe0Js7yrf/cdxjUdjhoV4GrmxM+D2ohYTUKu1mzemUfOfgolO5MmiuUPWYv4inDbvhaefEhx/GGPcPJZn/eomnAycfxZzu0jbkNxxZQaDIzG/kCjqr7/AgTtRlR3ZEkg3+lWjZ3hum2MPItqK4RDgKws8NAcB+kzKApTs5sjA2HeNLFDJQP+9CFmgQoAmglO7FX9yS03QWnS2FL1IWydz8hU9SVqLTmRz1ICwyUJ6M2AX5FykbBua12Lns6kicO1V4hQkrX2O6aL12ZCvbUfrtN0/AAAA//89h+LdAAAABklEQVQDAI+K4FDa7/FNAAAAAElFTkSuQmCC'>
<title>DLE Dispatch</title>
<link rel='preconnect' href='https://fonts.googleapis.com'>
<link href='https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap' rel='stylesheet'>
<style>
:root{--bg:#161826;--panel:#1b1d2b;--raised:#232532;--line:#2b2e3f;--line2:#4a4e60;
--text:#e9e9ed;--dim:#8b8fa3;--dim2:#75798c;--acc:#9184d9;--acc-hi:#d2cefd;--acc-deep:#3a2f6b;
--amber:#d9b47a;--green:#84c6a1;--red:#e09b95;--blue:#8fb8e0;
--w:#7aa2c2;--e:#86c0a8;--h:#b6a0dd}
*{box-sizing:border-box}
html{scrollbar-color:var(--line2) var(--bg)}
html,body{height:100%}
body{margin:0;background:var(--bg);color:var(--text);overflow:hidden;
font:12.5px/1.5 Inter,-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif}
.k{font:600 9.5px Inter,sans-serif;letter-spacing:.14em;text-transform:uppercase;color:var(--dim2)}
.num{font-variant-numeric:tabular-nums}
button{font:500 11.5px Inter,sans-serif;cursor:pointer;border-radius:5px;border:1px solid var(--line2);
background:transparent;color:#cfd3e5;height:26px;padding:0 11px;transition:border-color .15s,background .15s,color .15s}
button:hover{border-color:var(--acc);color:var(--text);background:rgba(145,132,217,.1)}
button.primary{border-color:var(--acc);color:var(--acc-hi);background:rgba(145,132,217,.14)}
button.primary:hover{background:rgba(145,132,217,.24)}
button.mini{height:22px;padding:0 8px;font-size:10.5px;color:var(--dim)}
button.mini:hover{color:var(--text)}
button.mini.danger{color:#e08a84;border-color:#7a4a46}
button:disabled{opacity:.45;cursor:not-allowed}
input,select{font:inherit;font-size:12px;background:var(--raised);color:var(--text);
border:1px solid var(--line);border-radius:5px;padding:4px 8px;min-width:0;height:26px}
input:focus,select:focus{outline:none;border-color:var(--acc)}
input[type='checkbox']{height:auto}
label{display:flex;flex-direction:column;gap:3px;font-size:11px;color:var(--dim)}
::selection{background:rgba(145,132,217,.3)}
/* ── shell ─────────────────────────────────────────────── */
#app{display:flex;flex-direction:column;height:100%;min-width:1080px}
#topbar{flex:none;height:44px;display:flex;align-items:center;gap:12px;padding:0 14px;
background:var(--panel);border-bottom:1px solid var(--line)}
.brand{font:700 13px Inter,sans-serif;letter-spacing:.14em;color:var(--text)}
.tbdiv{width:1px;height:18px;background:var(--line)}
.tab{display:inline-flex;align-items:center;gap:6px;height:30px;padding:0 13px;border-radius:5px;
font:600 11px Inter,sans-serif;letter-spacing:.06em;text-transform:uppercase;color:var(--dim2);cursor:pointer;user-select:none}
.tab:hover{color:#cfd3e5}
.tab.on{color:var(--text);background:#262940;box-shadow:inset 0 -2px 0 var(--acc)}
.tab.mini{height:24px;padding:0 9px;font-size:10.5px}
.dot{width:7px;height:7px;border-radius:50%;background:var(--green);flex:none}
.dot.bad{background:var(--red)}
.chip{font:600 10px Inter,sans-serif;letter-spacing:.05em;color:var(--dim);white-space:nowrap}
.pill{display:inline-flex;align-items:center;gap:4px;height:17px;padding:0 7px;border-radius:4px;
font:600 9.5px/1 Inter,sans-serif;letter-spacing:.06em;text-transform:uppercase;border:1px solid;white-space:nowrap}
.pav{color:#9397ab;border-color:#4a4e60}
.ppr{color:#b5abfc;border-color:#5d5294;background:#241f3c}
.pld{color:#d9b47a;border-color:#6b5a34;background:#241f16}
.pun{color:#8b8fa3;border-color:#4a4e60;border-style:dashed}
.plg{color:#a7a1db;border-color:#5c5783;border-style:dashed}
.pal{color:#e09b95;border-color:#7a4a46;background:#2c1c1b}
.pok{color:#84c6a1;border-color:#3f6b54}
.lockbtn{font-weight:700;letter-spacing:.08em;font-size:11px}
.lockbtn.on{background:#3a2c10;border-color:var(--amber);color:var(--amber)}
/* ── stage: surface + dock ─────────────────────────────── */
#stage{flex:1;display:flex;min-height:0}
#surface{flex:1;display:flex;flex-direction:column;min-width:0;position:relative}
.surf{flex:1;min-height:0;display:none;flex-direction:column}
.surf.on{display:flex}
#dock{flex:none;width:360px;border-left:1px solid var(--line);background:#181a29;
display:flex;flex-direction:column;min-height:0}
#dock.hidden{display:none}
.dockpane{flex:1;min-height:0;overflow-y:auto;display:none}
.dockpane.on{display:block}
.dhead{display:flex;align-items:center;gap:8px;height:32px;padding:0 13px;
border-bottom:1px solid var(--line);position:sticky;top:0;background:#181a29;z-index:2}
.dsec{padding:11px 13px;border-bottom:1px solid var(--line)}
.spacer{flex:1}
/* ── map surface ───────────────────────────────────────── */
#mapWrap{flex:1;position:relative;min-height:0;background:radial-gradient(120% 90% at 50% 0%,#1a1d2e 0%,#141626 70%)}
#net{position:absolute;inset:0;width:100%;height:100%}
#net text{font-family:Inter,sans-serif;user-select:none;pointer-events:none}
.nnode{cursor:pointer}
.nedge{cursor:pointer}
.maplegend{position:absolute;left:12px;bottom:12px;display:flex;gap:13px;align-items:center;
background:rgba(22,24,38,.88);border:1px solid var(--line);border-radius:6px;padding:6px 11px;font-size:11px;color:#b2b6ca}
.maplegend i{display:inline-block;width:16px;height:2px;vertical-align:2px;margin-right:5px}
/* ── yard surface ──────────────────────────────────────── */
#yardHead{flex:none;display:flex;align-items:center;gap:10px;height:42px;padding:0 14px;
border-bottom:1px solid var(--line);background:var(--panel);overflow-x:auto}
#yardHead::-webkit-scrollbar{height:0}
.crumb{font:600 9.5px Inter,sans-serif;letter-spacing:.14em;text-transform:uppercase;
color:var(--dim2);cursor:pointer;white-space:nowrap}
.crumb:hover{color:var(--text)}
.sc{display:inline-flex;align-items:center;justify-content:center;min-width:30px;height:22px;
padding:0 5px;border-radius:2px;font:700 10px/1.05 Inter,sans-serif;color:#12141f;text-align:center;
cursor:pointer;flex:none;user-select:none}
.sc:hover{outline:1px solid var(--text);outline-offset:1px}
.sc.cur{outline:2px solid var(--text);outline-offset:1px}
.sc.txl{color:#e9e9ed}
#yardScroll{flex:1;overflow:auto;padding:16px 14px 8px;
background:linear-gradient(180deg,#151726,#111320)}
.ytrack{display:flex;align-items:center;gap:9px;padding:5px 8px;margin-bottom:7px;
border:1px solid transparent;border-radius:6px}
.ytrack:hover{border-color:#20233a}
.ytrack.wh{background:linear-gradient(90deg,rgba(107,90,52,.10),transparent 60%)}
.tid{display:inline-flex;align-items:center;justify-content:center;min-width:24px;height:24px;
padding:0 4px;border-radius:3px;background:#5d5294;color:#f3f5fe;font:700 11px/1 Inter,sans-serif;flex:none}
.ytlabel{width:118px;flex:none;font-size:10.5px;color:var(--dim2);line-height:1.3}
.ytlabel b{color:#b2b6ca;font-size:11px;font-weight:600;display:block}
.ytlabel .whlab{color:var(--amber);font:600 9px Inter,sans-serif;letter-spacing:.1em;text-transform:uppercase}
.yend{flex:none;font:700 10px Inter,sans-serif;color:var(--dim2);letter-spacing:.05em;user-select:none;
border-right:2px dashed #3d4257;padding-right:7px}
.yend.r{border-right:0;border-left:2px dashed #3d4257;padding-right:0;padding-left:7px}
.ycars{display:flex;gap:8px;overflow-x:auto;padding:3px 0;flex:1;min-height:30px;align-items:center;
background:linear-gradient(to right,transparent,#3d4257 8px,#3d4257 calc(100% - 8px),transparent) no-repeat center/100% 2px}
.ycut{display:flex;gap:2px;flex:none}
.ycar{flex:none;height:22px;border:1px solid var(--line2);border-radius:2px;padding:0 7px;
font:600 10px/21px Inter,sans-serif;letter-spacing:.02em;color:#b2b6ca;background:var(--raised);
white-space:nowrap;user-select:none;text-align:center}
.ycar.ok{cursor:pointer}
.ycar.ok:hover{border-color:var(--acc)}
.ycar.sel{background:var(--acc-deep);border-color:var(--acc);color:#f3f5fe;box-shadow:0 0 0 1px var(--acc);cursor:pointer}
.ycar.loaded{border-color:#6b5a34;color:var(--amber);background:#241f16}
.ycar.loco{background:#16233a;border-color:#39597f;color:var(--blue)}
.ycar.incompat{opacity:.34}
.ycar.busy{opacity:.34}
.ycar.inline{border-style:dashed;border-color:#5c5783;color:#a7a1db;background:transparent;cursor:pointer}
.ytmeta{flex:none;width:150px;text-align:right;font-size:10px;color:var(--dim2)}
.ytmeta .ld{color:var(--amber)}
#yardKey{flex:none;display:flex;gap:14px;flex-wrap:wrap;align-items:center;font-size:11px;color:var(--dim);
padding:6px 14px;border-top:1px solid var(--line);background:var(--panel)}
#yardKey i{display:inline-block;width:11px;height:11px;border-radius:2px;border:1px solid var(--line2);
margin-right:4px;vertical-align:-1px;font-style:normal}
#strip{flex:none;display:flex;align-items:center;gap:3px;height:38px;padding:0 12px;
border-top:1px solid var(--line);background:var(--panel);overflow-x:auto}
/* ── fleet + log surfaces ──────────────────────────────── */
.surfpad{flex:1;overflow-y:auto;padding:16px 18px}
.formrow{display:flex;gap:10px;flex-wrap:wrap;align-items:flex-end;margin-bottom:10px}
table{border-collapse:collapse;width:100%;font-size:12px}
th{text-align:left;color:var(--dim2);font-weight:600;font-size:10px;letter-spacing:.08em;
text-transform:uppercase;padding:4px 10px;
background:linear-gradient(to right,transparent,var(--line) 20px,var(--line) calc(100% - 20px),transparent) no-repeat bottom/100% 1px}
td{padding:6px 10px;
background:linear-gradient(to right,transparent,#23253a 20px,#23253a calc(100% - 20px),transparent) no-repeat bottom/100% 1px}
tr:last-child td{background:none}
.carchip{display:inline-block;border:1px solid var(--line2);border-radius:3px;
padding:1px 7px;margin:2px 4px 2px 0;font:600 10.5px Inter,sans-serif;color:var(--dim);cursor:default}
.carchip.ok{border-color:#3f6b54;color:var(--green)}
.carchip.busy{color:var(--dim2)}
.empty{color:var(--dim);font-size:12px;padding:8px 2px}
.meta{font-size:11.5px;color:var(--dim)}
.meta b{color:var(--text);font-weight:600}
/* ── dock: station panel ───────────────────────────────── */
.sthead{display:flex;align-items:center;gap:9px;flex-wrap:wrap}
.stname{font:600 15px Inter,sans-serif}
.stchip{font:600 9.5px Inter,sans-serif;letter-spacing:.06em;text-transform:uppercase;
border-radius:4px;padding:2px 7px;border:1px solid}
.stchip.bad{color:var(--red);border-color:#7a4a46;background:#2c1c1b}
.stchip.warn{color:var(--amber);border-color:#6b5a34;background:#241f16}
.stchip.good{color:var(--green);border-color:#3f6b54}
.stchip.idle{color:var(--dim);border-color:var(--line2)}
.sublab{font:600 9.5px Inter,sans-serif;letter-spacing:.14em;text-transform:uppercase;
color:var(--dim2);margin:6px 0 4px}
.stockrow{display:grid;grid-template-columns:118px 1fr 92px;gap:8px;align-items:center;padding:2px 0;font-size:11.5px}
.stockrow .cname{color:var(--dim);overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.ctag{font-size:8.5px;letter-spacing:.05em;text-transform:uppercase;color:#595d6c;margin-left:3px}
.bar{height:5px;border-radius:3px;background:var(--line);overflow:hidden}
.bar i{display:block;height:100%;background:linear-gradient(90deg,#796cbf,#9184d9)}
.bar i.warn{background:var(--amber)}
.bar i.crit{background:var(--red)}
.nums{text-align:right;color:var(--dim)}
.needrow{display:flex;gap:8px;align-items:baseline;font-size:11.5px;padding:1px 0}
.needrow b{min-width:104px;font-weight:600}
.foldbtn{font:600 11px Inter,sans-serif;color:var(--dim);cursor:pointer;user-select:none;margin-top:7px}
.foldbtn:hover{color:var(--text)}
.foldbtn .count{color:var(--acc);margin-left:4px}
.foldbody{margin:3px 0 6px 6px;padding-left:10px;border-left:1px solid var(--line)}
.nrecipe{margin:4px 0;font-size:11.5px}
.nrecipe b{font-weight:600}
.shipto{margin:-1px 0 4px 126px;font-size:10px;color:var(--dim2)}
.machrow{display:flex;gap:8px;align-items:center;font-size:11.5px;padding:2px 0}
.machrow .mname{min-width:96px;color:var(--text)}
.machrow .mcount{font-weight:700}
.machrow .mcount.low{color:var(--amber)}
.machrow .mcount.out{color:var(--red)}
.machrow .mwear{color:var(--dim2);font-size:10.5px}
.tag{font:600 9px Inter,sans-serif;letter-spacing:.05em;text-transform:uppercase;
border-radius:4px;padding:1px 6px;background:#241f16;color:var(--amber)}
/* ── dock: haul detail ─────────────────────────────────── */
.job{display:flex;flex-direction:column;gap:8px}
.jobtop{display:flex;align-items:center;gap:8px;flex-wrap:wrap}
.jid{font:600 13px Inter,sans-serif;letter-spacing:.03em}
.wage{margin-left:auto;font-weight:600;color:var(--green)}
.route{font-size:14px;font-weight:600}
.route .arr{color:var(--dim2);margin:0 7px;font-weight:400}
.acts{display:flex;gap:6px;flex-wrap:wrap;align-items:center;
border-top:1px solid var(--line);padding-top:9px}
.crew{width:96px;height:24px;font-size:11.5px}
.carsbox{background:var(--raised);border:1px solid var(--line);border-radius:6px;padding:8px 10px;font-size:11.5px}
.carsbox table{font-size:11px}
.carsbox th,.carsbox td{padding:3px 8px}
.loadpill{font:700 9px Inter,sans-serif;border-radius:3px;padding:1px 5px}
.loadpill.yes{background:#1d3527;color:var(--green)}
.loadpill.no{background:var(--panel);color:var(--dim2);border:1px solid var(--line2)}
/* ── dock: booklet ─────────────────────────────────────── */
.mline{border:1px solid #5d5294;border-radius:6px;background:var(--panel);
padding:8px 10px;display:flex;flex-direction:column;gap:5px;margin-bottom:8px}
.mline .lhead{display:flex;align-items:center;gap:6px}
.mline .lhead b{font:600 12px Inter,sans-serif;color:var(--acc-hi)}
.mline .lpay{margin-left:auto;font-size:11.5px;color:var(--green)}
.mline .lcars{display:flex;gap:3px;flex-wrap:wrap}
.mline .lcars .ycar{cursor:default}
.mline.total{border-style:dashed;border-color:var(--line2);background:transparent;flex-direction:row;align-items:center;gap:8px}
.nextline{border:1px dashed var(--line2);border-radius:6px;padding:8px 10px;
display:flex;flex-direction:column;gap:7px;margin-bottom:8px}
#destChips{display:flex;gap:5px;flex-wrap:wrap;margin:4px 0 2px}
.krow{display:flex;justify-content:space-between;font-size:11.5px;color:var(--dim);padding:1px 0}
.krow .v{color:var(--text)}
.crewrow{display:flex;align-items:center;gap:8px;background:var(--panel);border:1px solid var(--line);
border-radius:6px;padding:5px 9px;margin:8px 0}
/* ── haul lane ─────────────────────────────────────────── */
#lane{flex:none;height:112px;border-top:1px solid var(--line);background:#181a29;
display:flex;flex-direction:column;min-width:0}
#laneHead{flex:none;display:flex;align-items:center;height:28px;padding:0 13px;gap:9px;
border-bottom:1px solid var(--line);overflow-x:auto}
#laneHead::-webkit-scrollbar{height:0}
#laneCards{flex:1;display:flex;gap:9px;padding:8px 13px;overflow-x:auto;align-items:stretch}
.jc{flex:none;width:212px;border:1px solid var(--line);border-radius:6px;background:var(--panel);
padding:7px 9px;display:flex;flex-direction:column;gap:4px;cursor:pointer}
.jc:hover{border-color:var(--line2)}
.jc.cur{border-color:var(--acc);box-shadow:0 0 0 1px var(--acc)}
.jc .r1{display:flex;align-items:center;gap:6px;min-width:0}
.jc .rt{font:600 11.5px Inter,sans-serif;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.jc .r2{font:600 9.5px Inter,sans-serif;letter-spacing:.08em;text-transform:uppercase;color:var(--dim2);
white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.sp{width:3px;border-radius:2px;align-self:stretch;flex:none}
.fchip{font:600 10px Inter,sans-serif;letter-spacing:.04em;border:1px solid var(--line2);
border-radius:999px;padding:1px 8px;color:var(--dim);cursor:pointer;white-space:nowrap;user-select:none}
.fchip:hover{color:var(--text);border-color:var(--acc)}
.fchip.on{background:var(--acc-deep);border-color:var(--acc);color:var(--text)}
.fchip.off{opacity:.4;text-decoration:line-through}
.fchip.clear{border-style:dashed}
/* ── toasts ────────────────────────────────────────────── */
#toasts{position:fixed;right:16px;bottom:126px;display:flex;flex-direction:column;gap:8px;z-index:10;max-width:340px}
.toast{background:var(--raised);border:1px solid var(--line2);border-left:3px solid var(--green);
border-radius:8px;padding:9px 13px;font-size:12.5px;box-shadow:0 6px 18px rgba(0,0,0,.55);animation:tin .18s ease-out}
.toast.err{border-left-color:var(--red)}
@keyframes tin{from{opacity:0;transform:translateY(6px)}to{opacity:1;transform:none}}
@media(max-width:1080px){body{overflow:auto}}
</style></head><body>
<div id='app'>
<header id='topbar'>
 <div class='brand'>DLE</div>
 <div class='tbdiv'></div>
 <span class='tab on' id='tabLogi' data-act='lens' data-id='logi'>Logistics</span>
 <span class='tab' id='tabRails' data-act='lens' data-id='rails'>Rails</span>
 <span class='tab' id='tabFleet' data-act='lens' data-id='fleet'>Fleet</span>
 <span class='tab' id='tabLog' data-act='lens' data-id='log'>Log</span>
 <div class='spacer'></div>
 <div class='dot' id='dot' title='board connection'></div>
 <span class='chip' id='chipVer'></span>
 <span class='chip num' id='chipStations'></span>
 <span class='chip num' id='chipJobs'></span>
 <span class='pill pld' id='chipBoost' title='Global productivity from city consumption: keep the cities fed and every industry speeds up'></span>
 <span class='pill pal' id='chipMachines' style='display:none' title='Stations on their last machine: ship replacements or they crawl'></span>
 <div class='tbdiv'></div>
 <button class='lockbtn' id='bCtc' data-act='ctc'
  title='CTC: every main signal held at stop until you clear a road through it. Off, signals run on their own automatic logic and a crew can work the railway without dispatch.'>CTC &middot; &hellip;</button>
 <button class='lockbtn' id='bLock' data-act='lock'
  title='Director OFF stops new hauls being generated, sweeps the station office papers, and leaves crews only the hauls dispatch has assigned them. Faxed booklets still work.'>DIRECTOR &middot; &hellip;</button>
</header>
<div id='stage'>
 <div id='surface'>
  <div class='surf on' id='surfMap'>
   <div id='mapWrap'>
    <svg id='net' viewBox='0 0 1040 760' preserveAspectRatio='xMidYMid meet'></svg>
    <div class='maplegend'>
     <span class='k'>Edges</span>
     <span><i style='background:#9184d9'></i>shippable now</span>
     <span><i style='background:#3a3e55'></i>faded = elsewhere</span>
     <span class='k' style='letter-spacing:.06em'>click a station &middot; click an edge to load the booklet</span>
    </div>
   </div>
  </div>
  <div class='surf' id='surfYard'>
   <div id='yardHead'>
    <span class='crumb' data-act='backMap'>Network</span>
    <span style='color:#4a4e60'>/</span>
    <span class='sc' id='yhChip'>?</span>
    <span style='font:600 14px Inter,sans-serif;white-space:nowrap' id='yhName'></span>
    <select id='hOrigin' style='display:none'></select>
    <div style='display:flex;gap:3px;margin-left:6px' id='sheetTabs'></div>
    <div class='spacer'></div>
    <span class='chip num' id='jmMeta'></span>
    <button class='mini' data-act='backMap' title='back to the network map (Esc)'>Esc</button>
   </div>
   <div id='yardScroll'><div id='jmYard'></div></div>
   <div id='yardKey'><span><i style='border-color:#3f6b54'></i>selectable</span>
    <span><i style='background:var(--acc-deep);border-color:var(--acc)'></i>picked</span>
    <span><i style='border-color:#6b5a34;background:#241f16'></i>loaded</span>
    <span><i style='border-style:dashed;border-color:#5c5783'></i>banked in a line</span>
    <span><i style='opacity:.4'></i>on a job / reserved / player car</span>
    <span><i style='background:#16233a;border-color:#39597f'></i>power</span>
    <span class='spacer'></span><span id='jmSel' class='meta'></span></div>
   <div id='strip'></div>
  </div>
  <div class='surf' id='surfRails'>
   <div style='flex:1;position:relative;overflow:hidden;background:#101220'>
    <svg id='railsSvg' style='position:absolute;inset:0;width:100%;height:100%;cursor:grab'>
     <g id='railsStatic'></g><g id='railsDyn'></g><g id='railsTop'></g>
    </svg>
    <div class='maplegend'>
     <span class='k'>Rails</span>
     <span><i style='background:#d5dcec'></i>rail</span>
     <span><i style='background:#2f9e63;height:5px'></i>road set</span>
     <span><i style='background:#c98f6b;width:8px;height:8px;border-radius:50%'></i>switch, click to throw</span>
     <span><i style='background:#c25f5a;width:8px;height:8px;border-radius:50%'></i>signal at stop</span>
     <span><i style='background:#57c78e;width:8px;height:8px;border-radius:50%'></i>signal clear, click to set or drop a road</span>
     <span><i style='background:#e09b95;height:5px'></i>consist on a job</span>
     <span><i style='background:#8fb8e0;height:5px'></i>light engine</span>
     <span class='k' style='letter-spacing:.06em'>drag to move · wheel to zoom · click a signal for a road, a switch to throw it</span>
    </div>
    <div style='position:absolute;right:12px;top:12px;display:flex;align-items:center;gap:6px;
     background:rgba(22,24,38,.92);border:1px solid var(--line);border-radius:6px;padding:6px 9px'>
     <span class='k'>Size</span>
     <button class='mini' data-act='railZoom' data-id='out' title='fit more railway on screen'>&minus;</button>
     <button class='mini' data-act='railZoom' data-id='in' title='fewer kilometres, everything bigger'>+</button>
     <span class='k' style='margin-left:6px'>Glyphs</span>
     <button class='mini' data-act='railGlyph' data-id='down' title='smaller marks'>&minus;</button>
     <button class='mini' data-act='railGlyph' data-id='up' title='bigger marks'>+</button>
     <span class='k num' id='railScaleLabel' style='margin-left:6px'></span>
    </div>
   </div>
  </div>
  <div class='surf' id='surfFleet'>
   <div class='surfpad'>
    <div class='formrow'>
     <label>Cargo<select id='fCargo'></select></label>
     <label>Yard<input id='fYard' style='width:70px' placeholder='any'></label>
     <button class='primary' data-act='findCars'>Find</button>
     <span class='meta' id='fSummary'></span>
    </div>
    <div class='meta' style='margin-bottom:8px'>compatible freight cars anywhere in the world; results are a snapshot, click Find to refresh; blank the cargo field to clear</div>
    <table id='tFleet'></table>
   </div>
  </div>
  <div class='surf' id='surfLog'>
   <div class='surfpad'>
    <div class='formrow'>
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
    <div id='dlog' style='font-size:12px'></div>
   </div>
  </div>
 </div>
 <aside id='dock'>
  <div class='dockpane on' id='dockHint'>
   <div class='dhead'><span class='k'>Inspector</span></div>
   <div class='dsec meta'>Click a station on the map for its economy, storage and needs. Click a haul below for its actions. Open a yard to build booklets from the cars where they stand.</div>
  </div>
  <div class='dockpane' id='dockStation'>
   <div class='dhead'><span class='k'>Station</span><div class='spacer'></div>
    <button class='mini' data-act='dockClose'>&times;</button></div>
   <div id='dockStationBody'></div>
  </div>
  <div class='dockpane' id='dockHaul'>
   <div class='dhead'><span class='k'>Haul</span><div class='spacer'></div>
    <button class='mini' data-act='dockClose'>&times;</button></div>
   <div class='dsec' id='dockHaulBody'></div>
  </div>
  <div class='dockpane' id='dockBooklet'>
   <div class='dhead'><span class='k'>Booklet</span><div class='spacer'></div><span class='k' id='bkRoute'></span></div>
   <div class='dsec' style='border-bottom:0'>
    <div id='jmManifest'></div>
    <div class='nextline'>
     <div style='display:flex;align-items:center;gap:6px'><span class='k'>Next line</span>
      <div class='spacer'></div><span class='k' id='bkHint'>pick cars on any track</span></div>
     <div style='display:flex;gap:6px;align-items:flex-end'>
      <label style='flex:1'>Cargo<select id='hCargo'></select></label>
      <label>Cars<input id='hCars' type='number' value='4' min='1' max='40' style='width:56px'></label>
      <button class='mini' data-act='jmAddLine' style='height:26px'
       title='Bank the picked cars as a cargo line, then pick more cars for another cargo. One booklet covers every line.'>+ Add</button>
     </div>
     <span class='meta' id='hEstimate' title='Estimated from the car types this cargo loads into; staff loading is first car instant, then per-car time'></span>
    </div>
    <div class='k' id='destLab'>Destination</div>
    <select id='hDest' style='display:none'></select>
    <div id='destChips'></div>
    <div class='crewrow'><span class='k'>Crew</span>
     <input id='hCrew' class='crew' list='crewNames' placeholder='optional' style='flex:1'>
     <label style='flex-direction:row;align-items:center;gap:5px;font-size:11px;white-space:nowrap'>
      <input type='checkbox' id='hTake'> take</label>
    </div>
    <div id='bkTotals'></div>
    <div style='display:flex;gap:6px;margin-top:8px'>
     <button class='primary' data-act='spawnHaul' style='flex:1;height:30px'>Create booklet</button>
     <button data-act='spawnHaulLoad' style='height:30px'
      title='Create the booklet, take it, and have station staff load the picked cars where they stand'>+ load now</button>
     <button class='mini' data-act='jmClear' style='height:30px'>Clear</button>
    </div>
   </div>
  </div>
 </aside>
</div>
<div id='lane'>
 <div id='laneHead'>
  <span class='k'>Board</span>
  <span id='lanePills' style='display:inline-flex;gap:5px'></span>
  <span id='accFilter' style='display:inline-flex;gap:5px'></span>
  <div class='spacer'></div>
  <span id='ftStats' class='chip num'></span>
 </div>
 <div id='laneCards'></div>
</div>
</div>
<div id='toasts'></div>
<datalist id='crewNames'></datalist>
<script>
const $=id=>document.getElementById(id);
const esc=s=>String(s==null?'':s).replace(/[&<>']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','\'':'&#39;'}[c]));
let railMarks=[],options=[],lockOn=false,ctcOn=false,expanded=new Set(),pickOpen=new Set(),pickers={},last={},lastJobs=[];
// Shell state: which lens, which surface inside Logistics, what the inspector shows.
let lens='logi',surface='map',dockMode='hint',haulSel=null;
// Job maker state: the picked cars, the compatible-car set for the chosen cargo,
// the banked manifest lines, and the last yard snapshot. Selection survives
// refreshes; a station change clears everything.
let jmYardData=null,jmSelSet=new Set(),jmCompat=null,jmStation=null,jmLines=[],jmDest=null;
// The destination is sticky the moment the dispatcher touches it: cargo changes
// must never move it. Banked lines harden the stickiness into a hard lock.
let jmDestPicked=false;
let jmSheet='ALL';
// Reentrancy guards: banking a line and creating a booklet both await the
// network mid-action, and a double-click in that window duplicated the work.
let jmAddBusy=false,spawnBusy=false;
function effDest(){return (jmLines.length&&jmDest)||(jmDestPicked?$('hDest').value:null)||null}
// Board station filter: left-clicks build the DESK (a set of stations; hauls
// touching any of them show), right-clicks hide stations. Both multi, both
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
  let k=localStorage.getItem('dleKey');
  if(!k){const p=prompt('Board password');if(p){localStorage.setItem('dleKey',p);k=p}}
  if(k)r=await fetch(u,mk())}
 return r}
async function j(u,m,b){return (await authedFetch(u,m,b)).json()}
async function jget(u){const r=await authedFetch(u);if(!r.ok)throw new Error('HTTP '+r.status);return r.json()}
function toast(t,err){const d=document.createElement('div');d.className='toast'+(err?' err':'');
 d.textContent=t;$('toasts').appendChild(d);setTimeout(()=>d.remove(),4200)}
function money(x){return '$'+Math.round(x||0).toLocaleString('en-US')}
function fmtSecs(s){s=Math.round(s);const m=Math.floor(s/60);return m>0?m+'m '+(s%60)+'s':s+'s'}
// ── the visual system: station colours, desks, status roles ──────────────
// Station colours are the game's own board colours; identity, never status.
const SC={CME:'#59595c',CMS:'#6e7566',CP:'#4b3b33',CS:'#b7c3cf',CW:'#a2a7ac',FF:'#7bb0e0',
 FM:'#e3a85c',FRC:'#a5c95f',FRS:'#77bd77',GF:'#eaa0c6',HB:'#a996cd',IME:'#c4735d',IMW:'#b96b52',
 MB:'#d0b87e',MF:'#ec8f4f',OR:'#b878b8',OWC:'#6f6e49',OWN:'#8f8f57',SM:'#86a0c8',SW:'#dcbf90'};
const SC_DARK=new Set(['CME','CP','OWC','CMS']);
function scChip(y,cur,act){const bg=SC[y]||'#4a4e60';
 return `<span class='sc${SC_DARK.has(y)?' txl':''}${cur?' cur':''}' style='background:${bg}'`+
  `${act?` data-act='${act}' data-id='${esc(y)}'`:''}>${esc(y)}</span>`}
// Desk ownership is position, not hue: a 3px spine on the leading edge.
const DESKS={West:['IMW','MF','CP','CW','SW','OWC','FM','FRS','FRC'],
 East:['IME','CME','GF','OWN','FF','MB','HMB','MFMB'],Hub:['SM','OR','CMS','CS','HB']};
const DESK_COLOR={West:'#7aa2c2',East:'#86c0a8',Hub:'#b6a0dd'};
function deskOf(y){for(const d in DESKS)if(DESKS[d].includes(y))return d;return null}
function spine(o,d){
 const a=DESK_COLOR[deskOf(o)]||'#4a4e60',b=DESK_COLOR[deskOf(d)]||'#4a4e60';
 const bg=a===b?a:`linear-gradient(180deg,${a} 50%,${b} 50%)`;
 return `<div class='sp' style='background:${bg}'></div>`}
// Job state roles: quiet grey available, accent in-progress (a human has it),
// warm loaded, dashed unpaid/logi, red only for problems.
function statusPill(x){
 if(x.logi)return `<span class='pill plg'>logi</span>`;
 const s=(x.state||'').toLowerCase();
 if(s==='available')return `<span class='pill pav'>open</span>`;
 if(x.cars>0&&x.loadedCars>=x.cars)return `<span class='pill pld'>loaded</span>`;
 if(x.cars>0&&x.loadedCars>0)return `<span class='pill pld'>loading ${x.loadedCars}/${x.cars}</span>`;
 if(x.unpaid)return `<span class='pill pun'>unpaid</span>`;
 if(s==='inprogress')return `<span class='pill ppr'>in progress</span>`;
 if(s==='completed')return `<span class='pill pok'>delivered</span>`;
 return `<span class='pill pav'>${esc(x.state||'?')}</span>`}
function unpaidPill(x){
 return x.unpaid&&!x.logi&&x.state!=='Available'&&x.loadedCars>0
  ?` <span class='pill pun' title='Relocating received goods; delivery pays nothing'>unpaid</span>`:''}
// ── lens / surface switching: Lens > Surface > Inspector, one back ───────
function setLens(l){lens=l;
 $('tabLogi').classList.toggle('on',l==='logi');
 $('tabRails').classList.toggle('on',l==='rails');
 $('tabFleet').classList.toggle('on',l==='fleet');
 $('tabLog').classList.toggle('on',l==='log');
 $('surfMap').classList.toggle('on',l==='logi'&&surface==='map');
 $('surfYard').classList.toggle('on',l==='logi'&&surface==='yard');
 $('surfRails').classList.toggle('on',l==='rails');
 $('surfFleet').classList.toggle('on',l==='fleet');
 $('surfLog').classList.toggle('on',l==='log');
 $('dock').classList.toggle('hidden',l!=='logi');
 if(l==='rails')loadRails();
 syncDock()}
function setSurface(s){surface=s;setLens('logi');
 if(s==='yard')pollYard(true)}
function syncDock(){
 const mode=surface==='yard'?'booklet':dockMode;
 for(const m of ['Hint','Station','Haul','Booklet'])
  $('dock'+m).classList.toggle('on',mode===m.toLowerCase())}
function openYard(y){
 const os=$('hOrigin');
 if(![...os.options].some(x=>x.value===y)){toast('station not on the board yet',true);return}
 os.value=y;originChanged();setSurface('yard')}
function backToMap(){setSurface('map')}
// ── refresh cycle ────────────────────────────────────────────────────────
function snapshotCrew(){const m={};document.querySelectorAll('.crew').forEach(i=>{if(i.value)m[i.id]=i.value});
 const f=document.activeElement;return{m,focus:f&&f.classList&&f.classList.contains('crew')?f.id:null}}
function restoreCrew(s){for(const id in s.m){const i=$(id);if(i&&!i.value)i.value=s.m[id]}
 if(s.focus){const i=$(s.focus);if(i){i.focus();i.setSelectionRange(i.value.length,i.value.length)}}}
function keepSelect(sel,items){const cur=sel.value;
 sel.innerHTML=items.map(v=>`<option value='${esc(v)}'>${esc(disp(v))}</option>`).join('');
 if([...sel.options].some(o=>o.value===cur))sel.value=cur}
let crewTick=0,lastCrews=[];
async function refresh(){
 let state,jobs,econ,hist,crews;
 // Crew names change on join and leave only; poll them every 6th cycle (30s)
 // instead of every 5s. The roster was the priciest thing the board asked for.
 const wantCrews=(crewTick++%6)===0;
 try{[state,options,jobs,econ,hist,crews]=await Promise.all([
  jget('/api/v1/state'),jget('/api/v1/options'),jget('/api/v1/jobs'),jget('/api/v1/economy'),jget('/api/v1/history?limit=60'),
  wantCrews?jget('/api/v1/players'):Promise.resolve(lastCrews)]);
  $('dot').className='dot'}
 catch(e){$('dot').className='dot bad';return}
 lastCrews=crews||[];
 lastJobs=jobs;
 const cKey=JSON.stringify(crews||[]);
 if(last.crews!==cKey){last.crews=cKey;
  $('crewNames').innerHTML=(crews||[]).map(n=>`<option>${esc(n)}</option>`).join('')}
 lockOn=!!state.lockEnabled;
 // The lock is what pauses the director, so the button says so: locked means the
 // director is off and the only work on offer is what dispatch has handed out.
 $('bLock').textContent='DIRECTOR '+(lockOn?'OFF':'ON');
 $('bLock').className='lockbtn'+(lockOn?' on':'');
 ctcOn=!!state.ctc;
 $('bCtc').textContent='CTC '+(ctcOn?'ON':'OFF');
 $('bCtc').className='lockbtn'+(ctcOn?' on':'');
 $('chipVer').textContent='v'+(state.modVersion||'?');
 $('chipStations').textContent=state.stationCount+' stations';
 $('chipJobs').textContent=state.jobCount+' hauls';
 const pf=state.perf||{};
 const ftBits=[];
 if(pf.liveCars)ftBits.push(pf.liveCars+' live');
 if(state.dormantCars)ftBits.push(state.dormantCars+' dormant');
 if(pf.frameP95Ms)ftBits.push('p95 '+pf.frameP95Ms+'ms');
 if(pf.gc60s!=null&&pf.frameP95Ms)ftBits.push(pf.gc60s+' GC/min');
 $('ftStats').textContent=ftBits.join(' · ');
 $('ftStats').title='host frame p50 '+(pf.frameP50Ms||'?')+'ms, p95 '+(pf.frameP95Ms||'?')+'ms, worst '+(pf.frameMaxMs||'?')+'ms · '
  +(pf.hitches60s||0)+' hitches/60s · heap '+(pf.heapMb||'?')+'MB · dormant cars respawn on approach or when a booklet claims them · company.lag in the console for the full report';
 $('chipBoost').textContent='boost ×'+(state.globalBoost||1);
 const mw=state.machineWarnings||[];
 $('chipMachines').style.display=mw.length?'':'none';
 $('chipMachines').textContent='MACHINES LOW: '+mw.join(', ');
 keepSelect($('hOrigin'),[...new Set(econ.map(e=>e.yardId))].sort());
 originChanged();
 // Job pseudo-cargos ('Mixed freight', 'Logistics move') are display names the
 // fleet endpoint cannot resolve; enum names never contain a space.
 keepSelect($('fCargo'),['','any cargo'].concat([...new Set([].concat(options.map(o=>o.cargo),
  jobs.filter(x=>!x.logi&&x.cargo&&x.cargo.indexOf(' ')<0).map(x=>x.cargo)))].sort()));
 lastEconData=econ;
 const stripKey=[...new Set(econ.map(e=>e.yardId))].sort().join();
 if(last.strip!==stripKey){last.strip=stripKey;renderStrip()}
 const netKey=JSON.stringify(options)+JSON.stringify(econ)+'|'+haulSel+'|'+JSON.stringify(lastJobs.map(x=>x.id));
 if(last.net!==netKey){last.net=netKey;drawNet()}
 const jKey=JSON.stringify(jobs)+[...expanded].join()+'|'+[...accSel].join()+'|'+[...accHidden].join()+'|'+haulSel+'|'+lastEconData.length;
 if(last.jobs!==jKey){last.jobs=jKey;
  const snap=snapshotCrew();
  renderLane(jobs);
  renderDockHaul();
  restoreCrew(snap)}
 if(dockMode==='station')renderDockStation();
 for(const id of expanded)fillCars(id);
 for(const id of pickOpen)fillPicker(id);
 pollYard();
 const hKey=JSON.stringify(hist);
 if(last.hist!==hKey){last.hist=hKey;renderLog(hist)}
 if(lens==='rails'&&railsGeo){
  try{[lastTraffic,lastInter]=await Promise.all([jget('/api/v1/traffic'),jget('/api/v1/interlocking')])}catch(e){}
  // The map is fetched once and kept. Loading a second save rebuilds the same railway
  // under a possibly different world origin, so the copy in hand can be stale while
  // every id still matches. The server counts its rebuilds; a change means refetch.
  if(lastInter&&railsGeo.epoch!=null&&lastInter.epoch!=null
   &&lastInter.epoch!==railsGeo.epoch&&lastInter.epoch!==railsEpochSeen){
   // Remembering which epoch was chased stops a refetch every five seconds forever
   // if the two payloads ever disagree about it for a reason we did not foresee.
   railsEpochSeen=lastInter.epoch;
   railsGeo=null;railLegs={};railMarks=[];loadRails();return}
  renderRailsDyn()}
}
// ── haul lane: the whole board in one strip, filter chips included ───────
function laneCard(x){
 const cars=x.cars||x.plannedCars||0;
 const crew=x.assignedTo?esc(x.assignedTo):(x.state==='Available'?'unassigned':'crewless');
 const d2=x.logi?'empties · closes on arrival':`${crew}${x.wage?' · '+money(x.wage):''}${x.cargo?' · '+esc(disp(x.cargo)):''}`;
 return `<div class='jc${haulSel===x.id?' cur':''}' data-act='laneOpen' data-id='${esc(x.id)}'>
  <div class='r1'>${spine(x.origin,x.destination)}
   <span class='rt num'>${esc(x.origin)}→${esc(x.destination)} ${cars}</span>
   <span class='spacer'></span>${statusPill(x)}${unpaidPill(x)}</div>
  <div class='r2'>${d2}</div></div>`}
function renderLane(jobs){
 const av=jobs.filter(x=>x.state==='Available'),ac=jobs.filter(x=>x.state!=='Available');
 const vis=x=>accSel.size?(accSel.has(x.origin)||accSel.has(x.destination))
  :!(accHidden.has(x.origin)&&accHidden.has(x.destination));
 // The desk filter narrows ACCEPTED work only (the owner's original rule):
 // open hauls are new demand and must never be silently invisible.
 const acS=ac.filter(vis),avS=av;
 const counts={open:av.length,prog:0,loaded:0,unpaid:0,logi:0};
 for(const x of ac){if(x.logi)counts.logi++;else if(x.unpaid)counts.unpaid++;
  else if(x.cars>0&&x.loadedCars>=x.cars)counts.loaded++;else counts.prog++}
 $('lanePills').innerHTML=
  `<span class='pill pav'>open ${counts.open}</span>`+
  `<span class='pill ppr'>in progress ${counts.prog}</span>`+
  (counts.loaded?`<span class='pill pld'>loaded ${counts.loaded}</span>`:'')+
  (counts.unpaid?`<span class='pill pun'>unpaid ${counts.unpaid}</span>`:'')+
  (counts.logi?`<span class='pill plg'>logi ${counts.logi}</span>`:'')
  +(acS.length!==ac.length?`<span class='chip num'>showing ${acS.length}/${ac.length} accepted</span>`:'');
 const origins=[...new Set(lastEconData.map(e=>e.yardId))].sort();
 const perOrigin={};
 for(const x of ac){perOrigin[x.origin]=(perOrigin[x.origin]||0)+1;
  if(x.destination!==x.origin)perOrigin[x.destination]=(perOrigin[x.destination]||0)+1}
 $('accFilter').innerHTML=origins.map(o=>{
  const cls=accSel.has(o)?' on':accHidden.has(o)?' off':'';
  return `<span class='fchip${cls}' data-act='accChip' data-id='${esc(o)}' title='click: add/remove ${esc(o)} from your desk (hauls touching any desk station show) · right-click: hide/show ${esc(o)} (a haul hides when both its ends are hidden)'>${esc(o)}${perOrigin[o]?' '+perOrigin[o]:''}</span>`}).join('')
  +(accSel.size||accHidden.size?`<span class='fchip clear' data-act='accClear' title='clear the desk and every hide'>× all</span>`:'');
 const all=acS.concat(avS);
 $('laneCards').innerHTML=all.length?all.map(laneCard).join('')
  :`<div class='empty'>${ac.length+av.length?'every haul is hidden by the station filter':(lockOn?'lock is on: the director is paused; open a yard and create hauls for your crews':'no hauls yet; open a yard to create one, or wait for the director')}</div>`;
}
// ── dock: haul detail (all actions live here) ────────────────────────────
function jobDetail(x){
 const avail=x.state==='Available';
 if(x.logi){
  return `<div class='job'>
   <div class='jobtop'><span class='jid'>${esc(x.id)}</span>${statusPill(x)}
    <span class='wage num' style='color:var(--dim)'>$0</span></div>
   <div class='route'><b>${esc(x.origin)}</b><span class='arr'>→</span><b>${esc(x.destination)}</b></div>
   <div class='meta'><b>${x.cars} car(s)</b> · unpaid dispatcher move; closes on its own when the cars arrive</div>
   <div class='meta'>${x.assignedTo?`crew: <b>${esc(x.assignedTo)}</b>`:'dispatch move'}</div>
   <div class='acts'>
    <button data-act='fax' data-id='${esc(x.id)}' title='Fax the booklet: typed name or loco plate first, else the assigned crew, else you'>Fax</button>
    <input class='crew' id='a_${esc(x.id)}' placeholder='crew or loco' list='crewNames'>
    <button class='mini' data-act='assign' data-id='${esc(x.id)}'>Assign</button>
    <button class='mini' data-act='unassign' data-id='${esc(x.id)}'>Unassign</button>
    <button class='mini danger' data-act='delhaul' data-id='${esc(x.id)}' title='Cancel the move; the cars free up'>×</button>
   </div></div>`}
 const cars=x.cars||x.plannedCars||0;
 const acts=avail
  ?`<button class='primary' data-act='take' data-id='${esc(x.id)}'>Take</button>`
  :`<button data-act='${x.awaitingEmpties?'pickCars':'load'}' data-id='${esc(x.id)}'>${x.awaitingEmpties?(pickOpen.has(x.id)?'Close picker':'Load…'):'Load'}</button>
    <button data-act='unload' data-id='${esc(x.id)}'>Unload</button>
    <button class='primary' data-act='complete' data-id='${esc(x.id)}'>Turn in</button>`;
 return `<div class='job'>
  <div class='jobtop'><span class='jid'>${esc(x.id)}</span>${statusPill(x)}${unpaidPill(x)}
   ${x.awaitingEmpties?`<span class='tag'>awaiting empties</span>`:''}
   <span class='wage num'${x.unpaid?` style='color:var(--dim)'`:''}>${money(x.wage)}</span></div>
  <div class='route'><b>${esc(x.origin)}</b><span class='arr'>→</span><b>${esc(x.destination)}</b></div>
  <div class='meta'><b>${esc(disp(x.cargo))}</b> · ${cars} cars${x.tonnes?` · ${x.tonnes} t loaded`:''}${x.pickupTrack?` · pickup <b>${esc(trackDisp(x.pickupTrack))}</b>`:''}</div>
  ${x.lines&&x.lines.length?`<div class='meta'>${x.lines.map(l=>`<b>${l.cars}</b> ${esc(disp(l.cargo))}${l.loaded?` (${l.loaded} loaded)`:''}${l.unpaid?' (unpaid)':''}`).join(' + ')}</div>`:''}
  <div class='meta'>${x.assignedTo?`crew: <b>${esc(x.assignedTo)}</b>`:'unassigned'}</div>
  <div class='acts'>${acts}
   <button data-act='fax' data-id='${esc(x.id)}' title='Fax the booklet: typed name first, else the assigned crew, else you'>Fax</button>
   <button class='mini' data-act='cars' data-id='${esc(x.id)}'>${expanded.has(x.id)?'Hide cars':'Cars'}</button>
   <button class='mini' data-act='findEmpties' data-id='${esc(x.id)}' title='Show every compatible car in the world for this cargo'>Find empties</button>
   <input class='crew' id='a_${esc(x.id)}' placeholder='crew or loco' list='crewNames'>
   <button class='mini' data-act='assign' data-id='${esc(x.id)}'>Assign</button>
   <button class='mini' data-act='unassign' data-id='${esc(x.id)}' title='Clear assignment'>Unassign</button>
   <button class='mini danger' data-act='delhaul' data-id='${esc(x.id)}' title='Delete this haul; its supply returns to the pile'>×</button>
  </div>
  ${expanded.has(x.id)?`<div class='carsbox' id='cars_${esc(x.id)}'>fetching…</div>`:''}
  ${pickOpen.has(x.id)?`<div class='carsbox' id='pick_${esc(x.id)}'>fetching…</div>`:''}
 </div>`}
function renderDockHaul(){
 const box=$('dockHaulBody');if(!box)return;
 if(!haulSel){box.innerHTML=`<div class='empty'>click a haul in the lane below</div>`;return}
 const x=lastJobs.find(v=>v.id===haulSel);
 if(!x){box.innerHTML=`<div class='empty'>that haul is gone from the board</div>`;return}
 box.innerHTML=jobDetail(x)}
async function fillCars(id){
 const box=$('cars_'+id);if(!box)return;
 try{const r=await j('/api/v1/jobs/'+id+'/cars');
  const html=`<div style='margin-bottom:5px'>loading track: <b>${esc(trackDisp(r.loadingTrack||'?'))}</b></div>`+
   (r.cars.length?`<table><tr><th>Car</th><th>Type</th><th>Cargo</th><th>Track</th><th>Dist</th></tr>`+
    r.cars.map(c=>`<tr><td>${esc(c.carId)}</td><td>${esc(c.type)}</td>`+
     `<td><span class='loadpill ${c.loaded?'yes':'no'}'>${c.loaded?'LOADED':'empty'}</span></td>`+
     `<td>${esc(trackDisp(c.track))}</td><td class='num'>${c.metersFromLoading==null?'':c.metersFromLoading+' m'}</td></tr>`).join('')+
    `</table>`:'no cars attached yet: bring empties to the loading track');
  if(box.innerHTML!==html)box.innerHTML=html}
 catch(e){box.textContent='car view failed'}
}
// ── network map: nodes from the live economy, edges from what ships now ──
const NET_POS={IMW:[161,133],FF:[612,127],MB:[796,73],HMB:[860,105],MFMB:[830,143],
 IME:[950,60],CME:[966,237],OWN:[740,218],OR:[421,232],MF:[176,246],GF:[822,243],
 CP:[161,339],FRC:[379,350],FM:[394,447],OWC:[310,470],SM:[503,413],CW:[154,489],
 HB:[834,594],FRS:[357,577],CMS:[552,594],CS:[638,690],SW:[113,644]};
const NET_NAMES={OWC:'Oil Wells C',OWN:'Oil Wells N',OR:'Oil Refinery',FRS:'Forest S',
 FRC:'Forest C',CMS:'Coal Mine S',CME:'Coal Mine E',IME:'Iron Mine E',IMW:'Iron Mine W',
 CP:'Coal Power',SM:'Steel Mill',SW:'Sawmill',FM:'Farm',HB:'Harbour',GF:'Goods Factory',
 MF:'Machine Factory',FF:'Food Factory',CW:'City West',CS:'City South'};
const NET_STYLE={source:{fill:'#16233a',stroke:'#4a7fae',tx:'#8fb8e0'},
 factory:{fill:'#241f3c',stroke:'#796cbf',tx:'#d2cefd'},
 sink:{fill:'#1a2a24',stroke:'#4f9679',tx:'#84c6a1'},
 hub:{fill:'#1b2340',stroke:'#7d8fd0',tx:'#c3caf0'}};
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
// Desk territory: a 5 percent wash under each desk's cluster; position, not hue.
const DESK_WASH=[
 {c:'#7aa2c2',cx:250,cy:400,rx:290,ry:330},
 {c:'#86c0a8',cx:860,cy:220,rx:260,ry:240},
 {c:'#b6a0dd',cx:660,cy:600,rx:290,ry:200}];
function drawNet(){
 const svg=$('net');if(!svg)return;
 const {nodes,edges}=buildNet(lastEconData,options);
 const bidi=new Set(edges.map(e=>e.src+'|'+e.dst));
 const sel=netSel&&nodes[netSel]?netSel:null;
 // A selected haul idents itself on the map: endpoints ring up, its route
 // draws as a bold amber arc, and the economy edges subdue to context.
 const hj=haulSel?lastJobs.find(v=>v.id===haulSel):null;
 const hOn=!!(hj&&nodes[hj.origin]&&nodes[hj.destination]&&hj.origin!==hj.destination);
 let h=`<defs>
  <marker id='arw' markerWidth='7' markerHeight='6' refX='6' refY='3' orient='auto' markerUnits='userSpaceOnUse'><path d='M0,0 L0,6 L7,3 z' fill='#5d5294'/></marker>
  <marker id='arwB' markerWidth='7' markerHeight='6' refX='6' refY='3' orient='auto' markerUnits='userSpaceOnUse'><path d='M0,0 L0,6 L7,3 z' fill='#b5abfc'/></marker>
  <marker id='arwH' markerWidth='8' markerHeight='7' refX='7' refY='3.5' orient='auto' markerUnits='userSpaceOnUse'><path d='M0,0 L0,7 L8,3.5 z' fill='#d9b47a'/></marker>
 </defs>`;
 for(const w of DESK_WASH)
  h+=`<ellipse cx='${w.cx}' cy='${w.cy}' rx='${w.rx}' ry='${w.ry}' fill='${w.c}' opacity='0.05'/>`;
 for(const e of edges){
  const on=!sel||e.src===sel||e.dst===sel;
  const op=hOn?0.06:(sel?(on?0.95:0.06):0.45);
  const w=1+Math.min(3,e.stock/10);
  h+=`<path class='nedge' data-act='netEdge' data-src='${esc(e.src)}' data-dst='${esc(e.dst)}' data-cargo='${esc(e.cargos[0])}'
   d='${netPath(e,bidi)}' fill='none' stroke='${on&&sel&&!hOn?'#b5abfc':'#9184d9'}'
   stroke-opacity='${op}' stroke-width='${sel&&on&&!hOn?w+1.5:w}'
   marker-end='url(#${sel&&on&&!hOn?'arwB':'arw'})'>
   <title>${esc(e.src)} to ${esc(e.dst)}: ${esc(e.cargos.map(disp).join(', '))} (${Math.round(e.stock)} shippable)</title></path>`;
 }
 if(hOn){
  const A=NET_POS[hj.origin],B=NET_POS[hj.destination];
  h+=`<path d='${netPath({src:hj.origin,dst:hj.destination},bidi)}' fill='none'
   stroke='#d9b47a' stroke-width='3' stroke-dasharray='8 6' stroke-opacity='0.95'
   marker-end='url(#arwH)'/>`;
  const mx=(A[0]+B[0])/2,my=(A[1]+B[1])/2-14;
  h+=`<g><rect x='${mx-58}' y='${my-11}' width='116' height='18' rx='4' fill='#241f16' stroke='#6b5a34'/>
   <text x='${mx}' y='${my+2}' text-anchor='middle' fill='#d9b47a' font-size='9.5' font-weight='700'>${esc(hj.id)} · ${esc(hj.assignedTo||(hj.state==='Available'?'open':'crewless'))}</text></g>`;
 }
 for(const id in nodes){
  const n=nodes[id];const p=NET_POS[id];
  const cls=n.importHub?'hub':(n.source?'source':(n.consumer||(n.outputs||[]).length===0?'sink':'factory'));
  const st=NET_STYLE[cls];
  const miss=netMissing(n);
  const r=cls==='hub'?24:cls==='factory'?20:17;
  const hEnd=hOn&&(id===hj.origin||id===hj.destination);
  const dim=hOn?!hEnd
   :sel&&id!==sel&&!edges.some(e=>(e.src===sel&&e.dst===id)||(e.dst===sel&&e.src===id));
  h+=`<g class='nnode' data-act='netNode' data-id='${esc(id)}' transform='translate(${p[0]},${p[1]})' opacity='${dim?0.25:1}'>
   <circle r='${r}' fill='${st.fill}' stroke='${hEnd?'#d9b47a':miss.length?'#a8615c':(n.machineWarning?'#d9b47a':st.stroke)}' stroke-width='${sel===id||hEnd?2.6:1.6}'/>
   <text y='4' text-anchor='middle' fill='${st.tx}' font-size='${r>=20?12:10.5}' font-weight='700'>${esc(id)}</text>
   ${sel===id?`<text y='${r+13}' text-anchor='middle' fill='#75798c' font-size='9'>${esc(NET_NAMES[id]||'')}</text>`:''}
   ${miss.length?`<text y='${-r-6}' text-anchor='middle' fill='#e09b95' font-size='8.5' font-weight='600'>WAITING</text>`:''}
   <title>${esc(id)} · ${esc(NET_NAMES[id]||'')}${miss.length?' · waiting on '+esc(miss.map(disp).join(', ')):''}</title></g>`;
 }
 svg.innerHTML=h;
 if(dockMode==='station')renderDockStation();
}
// ── the Rails map (#131 first pass): the real railway, read-only ─────────
// Geometry loads once per session (the server memoizes it per world); traffic
// rides the 5s refresh while the lens is open. World x,z map to SVG with north
// up; RS is the uniform scale, so distances stay honest.
let railsGeo=null,railsEpochSeen=null,railLegs={},railsB=null,railsVB=null,lastTraffic=null,lastInter=null,railsLoading=false;
// Fixed scale, never zoomed: 7 metres a pixel keeps a ten-car train readable while
// putting the whole railway inside about two screens, and the sideways stretch makes
// the drag mostly horizontal on a wide monitor. Rails draw as their REAL polylines
// with a dark casing under a bright core, so crossings read and parallel track just
// looks like heavier line work instead of the comb an earlier fan attempt drew.
const RAIL_XS=2.0;
// Scale and glyph size are DIALS, not constants. I cannot see the board, so rather than
// guessing sizes on someone's behalf these are set on screen and remembered per browser.
// Set them once and then pan; nothing here changes while you work.
let RAIL_MPP=+localStorage.getItem('dleRailMpp')||7.0;
let RAIL_G=+localStorage.getItem('dleRailGlyph')||1.0;
// Double track must read as TWO tracks. Nine pixels each way is eighteen apart, but
// every rail carries a sixteen pixel casing, so the casings all but touched and a
// double track section drew as one fat line with a hairline down it. Real spacing is
// four metres, which is invisible at any scale a dispatcher can use, so the board
// spreads it on purpose: geography decides WHERE the pair runs, this decides that you
// can see and click both of them.
const RAIL_FAN=()=>22*RAIL_G;
// The scaling law (owner ruling): zooming OUT never shrinks a glyph, zooming IN grows
// them a little, up to a cap. Far out, everything holds its screen size so the map
// stays readable; close in, the throat under the cursor turns chunky instead of the
// marks sitting spindly on fat empty space.
function railZ(){return Math.min(1.6,Math.max(1,Math.pow(7/RAIL_MPP,0.3)))}
function railSize(k){return k*RAIL_G*railZ()}
function setRailScale(mpp,glyph){
 RAIL_MPP=Math.min(20,Math.max(0.3,mpp));
 RAIL_G=Math.min(4,Math.max(0.5,glyph));
 localStorage.setItem('dleRailMpp',RAIL_MPP);
 localStorage.setItem('dleRailGlyph',RAIL_G);
 if(!railsGeo)return;
 railsB.w=(railsB.maxX-railsB.minX)/RAIL_MPP*RAIL_XS+railSize(260);
 railsB.h=(railsB.maxZ-railsB.minZ)/RAIL_MPP+railSize(260);
 renderRailsStatic();centreRails();renderRailsDyn();
 const l=$('railScaleLabel');
 if(l)l.textContent=RAIL_MPP.toFixed(1)+' m/px · glyphs '+RAIL_G.toFixed(1)+'x';}
function rxy(x,z){return [(x-railsB.minX)/RAIL_MPP*RAIL_XS,(railsB.maxZ-z)/RAIL_MPP]}
async function loadRails(){
 if(railsGeo||railsLoading)return;
 railsLoading=true;
 try{const g=await jget('/api/v1/trackmap');
  if(!g||!g.lines||!g.lines.length){toast('track map is empty; is the world loaded?',true);return}
  railsGeo=g;railsB=g.bounds;
  // Switch legs are geometry: they arrive once with the map and are keyed by switch
  // here, rather than riding the live poll where they would be hundreds of kilobytes
  // of unchanging track every five seconds.
  railLegs={};
  for(const e of (g.legs||[]))railLegs[e.id]=e.legs;
  railsB.w=(railsB.maxX-railsB.minX)/RAIL_MPP*RAIL_XS+railSize(260);
  railsB.h=(railsB.maxZ-railsB.minZ)/RAIL_MPP+railSize(260);
  renderRailsStatic();
  centreRails();
  renderRailsDyn();
  const l=$('railScaleLabel');
  if(l)l.textContent=RAIL_MPP.toFixed(1)+' m/px · glyphs '+RAIL_G.toFixed(1)+'x'}
 catch(e){toast('track map failed to load',true)}
 finally{railsLoading=false}}
function railsViewport(){const r=$('railsSvg').getBoundingClientRect();
 return [Math.max(200,r.width),Math.max(200,r.height)]}
function centreRails(){
 const [vw,vh]=railsViewport();
 railsVB=[railsB.w/2-vw/2,railsB.h/2-vh/2,vw,vh];
 clampRails();applyRailsVB()}
function clampRails(){
 if(!railsVB||!railsB)return;
 const m=260;
 railsVB[0]=Math.min(Math.max(railsVB[0],-m),Math.max(-m,railsB.w-railsVB[2]+m));
 railsVB[1]=Math.min(Math.max(railsVB[1],-m),Math.max(-m,railsB.h-railsVB[3]+m))}
function renderRailsStatic(){
 const g=$('railsStatic');if(!g||!railsGeo)return;
 let h='';
 // Casing pass first, then the bright core, so every crossing reads cleanly.
 // Rails the server paired as double track carry a side and get fanned apart here,
 // in SCREEN space: the sideways stretch means a world perpendicular is not a screen
 // perpendicular, so the shift has to be computed after projection.
 const paths=[];
 for(const ln of railsGeo.lines){
  const a=ln.pts||ln,side=ln.side||0;
  const w=[];
  for(let i=0;i<a.length;i+=2)w.push([a[i],a[i+1]]);
  const q=w.map(p=>rxy(p[0],p[1]));
  paths.push({d:railPath(q,side).map(v=>v[0].toFixed(1)+','+v[1].toFixed(1)).join(' '),side});}
 for(const p of paths)
  h+=`<polyline points='${p.d}' fill='none' stroke='#0d0f1a' stroke-width='${railSize(16)}' stroke-linecap='round' stroke-linejoin='round'/>`;
 for(const p of paths)
  h+=`<polyline points='${p.d}' fill='none' stroke='${p.side?'#e4eaf8':'#aab4cd'}' stroke-width='${railSize(p.side?9:10)}' stroke-linecap='round' stroke-linejoin='round'/>`;
 g.innerHTML=h;
 // No station bubbles (owner ruling): the whole railway is drawn now, yards included,
 // and a filled disc over a yard throat hides the very track you would be working. The
 // name stays as a plain label so you still know where you are.
 let tp='';
 for(const s of (railsGeo.stations||[])){
  const q=rxy(s.x,s.z);
  tp+=`<text x='${q[0].toFixed(1)}' y='${(q[1]-railSize(26)).toFixed(1)}' text-anchor='middle'
   font-size='${railSize(26)}' font-weight='700' fill='${SC[s.id]||'#7f879c'}'
   stroke='#0d0f1a' stroke-width='${railSize(4)}' paint-order='stroke'>${esc(s.id)}</text>`}
 $('railsTop').innerHTML=tp}
function renderRailsDyn(){
 const g=$('railsDyn');if(!g)return;
 const tr=lastTraffic;
 if(!railsGeo){g.innerHTML='';return}
 let h='';
 // Cleared roads first, under the traffic: a green that runs the way the switches
 // are actually set, from the signal to the next one.
 const il=lastInter||{};
 // A cleared road RECOLOURS the rail rather than sitting beside it, so each piece is
 // drawn at that rail's own width and fan offset: one line, turned green.
 for(const r of (il.routes||[])){
  for(const seg of (r.poly||[])){
   const q=[];
   for(let i=0;i<seg.pts.length;i+=2)q.push(rxy(seg.pts[i],seg.pts[i+1]));
   const d=railPath(q,seg.side||0).map(v=>v[0].toFixed(1)+','+v[1].toFixed(1)).join(' ');
   if(q.length>1)h+=`<polyline points='${d}' fill='none' stroke='#2f9e63' stroke-width='${railSize(seg.side?9:10)}' stroke-linecap='round' stroke-linejoin='round'/>`}}
 // WHERE EVERY MARK GOES. True positions first, then one spreading pass over the lot,
 // because real geography puts switches on top of each other at map scale: on this
 // world 196 pairs of switches sit closer than 28px and the worst are 0.7px apart, so
 // no amount of careful sizing makes them separately visible or clickable. Geography
 // matters, so a mark that has room does not move at all (the median shift is under
 // four pixels); only a crowded throat fans out, and never further than a hard limit.
 const jById={};for(const j of (il.junctions||[]))jById[j.id]=j;
 const marks=[];
 (il.junctions||[]).forEach((j,i)=>{
  // A plain track join is not a switch: nothing to throw, nothing to draw.
  if(j.branches<2)return;
  const p=railPoint(j.x,j.z,j.side,j.dx,j.dz);
  marks.push({kind:'jn',id:j.id,j,click:true,x:p[0],y:p[1],ax:p[0],ay:p[1]})});
 for(const sg of (il.signals||[])){
  const j=jById[sg.jid];
  // Two signals stand at a switch on this board (owner ruling): the one on the trunk,
  // and the one on whichever branch the points are set to. Both branch signals drawn
  // together is illegible and pointless, since a shallow turnout puts them within two
  // pixels of each other and a road follows the points anyway, so the branch that is
  // not set can carry no road.
  if(j&&sg.leg>=0&&sg.leg!==j.branch)continue;
  let q=null,u=[1,0];
  const leg=j&&(railLegs[j.id]||[]).find(l=>l.branch===sg.leg);
  if(leg){
   const pts=[];for(let i=0;i<leg.pts.length;i+=2)pts.push(rxy(leg.pts[i],leg.pts[i+1]));
   // Far enough out that the triangle clears the switch mark and its lock ring.
   const w=walkAlong(railPath(pts,leg.side||0),railSize(52)+sg.slot*railSize(34));
   if(w){q=w[0];u=w[1]}}
  // A signal with no leg to stand on falls back to its own coordinates, which
  // the server only sends in that case.
  if(!q){if(sg.x==null)continue;q=rxy(sg.x,sg.z)}
  if(sg.inbound)u=[-u[0],-u[1]];
  marks.push({kind:'sig',id:sg.id,sg,u,click:true,x:q[0],y:q[1],ax:q[0],ay:q[1]})}
 // A nudge, not a rearrangement: enough that two marks on one spot can both be hit,
 // little enough that nothing is anywhere it is not. Zoom does the rest.
 spread(marks,railSize(30),railSize(18));
 railMarks=marks;
 const jItems=marks.filter(m=>m.kind==='jn');
 // Switches: a black disc with the track lines running THROUGH it (owner ruling), the
 // leg it is set to solid white and the others greyed so a dispatcher can see there IS
 // a connection and exactly where it goes. The disc goes down first and the legs are
 // drawn over it, which is why this is in three passes rather than one.
 for(const m of jItems)
  h+=`<circle cx='${m.x.toFixed(1)}' cy='${m.y.toFixed(1)}' r='${railSize(11)}' fill='#07080e' stroke='#454c5e' stroke-width='${railSize(1.5)}'/>`;
 for(const j of (il.junctions||[])){
  if(j.branches<2)continue;
  const legs=railLegs[j.id]||[];
  const toQ=l=>{const q=[];for(let i=0;i<l.pts.length;i+=2)q.push(rxy(l.pts[i],l.pts[i+1]));return q};
  // The arm is a length of TRACK, clamped in screen terms at both ends: never gone
  // when zoomed out, never a thicket when zoomed in.
  const armPx=Math.max(railSize(40),Math.min(railSize(300),300/RAIL_MPP));
  // Branches NOT set: dim, and PARTED from the switch by a gap, the way a panel shows
  // a route that is not made. They only appear once the zoom gives them room.
  for(const leg of legs){
   if(leg.branch<0||leg.branch===j.branch)continue;
   const q=toQ(leg);if(q.length<2)continue;
   const cut=skipAlong(railPath(clip(q,armPx*0.85),leg.side||0),railSize(17));
   if(pathLen(cut)<railSize(9))continue;
   h+=`<polyline points='${cut.map(v=>v[0].toFixed(1)+','+v[1].toFixed(1)).join(' ')}' fill='none' stroke='#5f6880' stroke-width='${railSize(4.5)}' stroke-linecap='butt' stroke-linejoin='round'/>`}
  // The route that IS made: one bright line from the trunk THROUGH the switch onto the
  // set branch. That is the whole read: where the white goes, the train goes. A stub
  // sitting inside a ring said nothing; a line that bends through the points says it
  // from across the room.
  const setLeg=legs.find(l=>l.branch===j.branch);
  const trunk=legs.find(l=>l.branch<0);
  for(const leg of [trunk,setLeg]){
   if(!leg)continue;
   const q=toQ(leg);if(q.length<2)continue;
   const d=railPath(clip(q,leg===setLeg?armPx:armPx*0.55),leg.side||0)
    .map(v=>v[0].toFixed(1)+','+v[1].toFixed(1)).join(' ');
   h+=`<polyline points='${d}' fill='none' stroke='#ffffff' stroke-width='${railSize(7.5)}' stroke-linecap='round' stroke-linejoin='round'/>`}}
 for(const m of jItems){
  const j=m.j,q=[m.x,m.y];
  const t=`switch ${j.id}: branch ${j.branch+1} of ${j.branches}${j.locked?' (locked by a cleared road)':' - click to throw'}`;
  h+=`<g data-act='throwSwitch' data-id='${j.id}' style='cursor:pointer'>
   <circle cx='${q[0].toFixed(1)}' cy='${q[1].toFixed(1)}' r='${railSize(20)}' fill='transparent'/>
   ${j.locked?`<circle cx='${q[0].toFixed(1)}' cy='${q[1].toFixed(1)}' r='${railSize(18)}' fill='none' stroke='#d9b47a' stroke-width='${railSize(3.5)}'/>`:''}
   <title>${esc(t)}</title></g>`}
 // Signals belong to the DV Signals mod: the colour is the aspect the world is
 // actually showing, and clicking sets or drops the road through it.
 for(const m of marks){
  if(m.kind!=='sig')continue;
  const sg=m.sg,q=[m.x,m.y],u=m.u;
  const a=sg.aspect||'';
  const col=!sg.on?'#727a90':a==='S2'?'#57c78e':(a==='S6'||a==='S4')?'#d9b47a':'#c25f5a';
  const nm=a==='S2'?'clear':a==='S6'?'caution':a==='S4'?'expect caution':a?'stop':'off';
  const t=`${sg.id}: ${nm}${sg.manual?' (manual)':''}${sg.road?' - road set by dispatch':''}${sg.jid>=0?'':' - not standing at a switch this board knows'}`;
  h+=`<g data-act='signal' data-id='${esc(sg.id)}' style='cursor:pointer'>
   <circle cx='${q[0].toFixed(1)}' cy='${q[1].toFixed(1)}' r='${railSize(20)}' fill='transparent'/>
   ${sg.road?`<circle cx='${q[0].toFixed(1)}' cy='${q[1].toFixed(1)}' r='${railSize(16)}' fill='none' stroke='#2f9e63' stroke-width='${railSize(3.5)}'/>`:''}
   <polygon points='${triAt(q,u,sg.on?1:0.82)}' fill='${col}' stroke='#0d0f1a' stroke-width='${railSize(3)}' stroke-linejoin='round'/>
   <title>${esc(t)}</title></g>`}
 if(!tr)  {g.innerHTML=h;return}
 // A consist is drawn car by car at true length, so a train reads as a train.
 for(const c of (tr.consists||[])){
  const a=rxy(c.x1,c.z1),b=rxy(c.x2,c.z2);
  const n=Math.max(1,c.n||1);
  const col=c.jobId?'#e09b95':(c.loco?'#8fb8e0':'#9397ab');
  const dx=(b[0]-a[0])/n,dy=(b[1]-a[1])/n;
  const short=Math.hypot(b[0]-a[0],b[1]-a[1])<4;
  if(short){
   h+=`<circle cx='${a[0].toFixed(1)}' cy='${a[1].toFixed(1)}' r='4' fill='${col}'><title>${n} car(s)</title></circle>`}
  else for(let i=0;i<n;i++){
   const x1=a[0]+dx*i,y1=a[1]+dy*i,x2=a[0]+dx*(i+0.82),y2=a[1]+dy*(i+0.82);
   h+=`<line x1='${x1.toFixed(1)}' y1='${y1.toFixed(1)}' x2='${x2.toFixed(1)}' y2='${y2.toFixed(1)}' stroke='${col}' stroke-width='${railSize(15)}' stroke-linecap='butt'>
    <title>${n} car(s)${c.loco?' with power':''}${c.jobId?' · '+esc(c.jobId):''}</title></line>`}
  if(c.jobId){const x=lastJobs.find(v=>v.id===c.jobId);
   h+=`<text x='${a[0].toFixed(1)}' y='${(a[1]-11).toFixed(1)}' font-size='12' font-weight='700' fill='#d9b47a'>${esc(c.jobId)}${x&&x.assignedTo?' · '+esc(x.assignedTo):''}</text>`}}
 g.innerHTML=h}
// A world heading is not a screen heading here, because the map is stretched sideways;
// project two points and measure the result instead of rotating the raw vector.
function screenDir(x,z,dx,dz){
 const a=rxy(x,z),b=rxy(x+(dx||1)*10,z+(dz||0)*10);
 let ux=b[0]-a[0],uy=b[1]-a[1];const L=Math.hypot(ux,uy)||1;
 return [ux/L,uy/L]}
// Track geometry is sampled every ten metres or so, and at map scale that sampling
// shows up as a shiver along every curve. One Chaikin pass rounds the corners off
// without moving the line anywhere: the route keeps its real shape, it just stops
// looking hand-drawn.
function smooth(q){
 if(!q||q.length<3)return q;
 const o=[q[0]];
 for(let i=0;i<q.length-1;i++){
  const a=q[i],b=q[i+1];
  o.push([a[0]*0.75+b[0]*0.25,a[1]*0.75+b[1]*0.25]);
  o.push([a[0]*0.25+b[0]*0.75,a[1]*0.25+b[1]*0.75])}
 o.push(q[q.length-1]);
 return o}
// Walk a projected line from its first point until a given number of screen pixels
// have gone by, and report where that lands plus the way the line is running there.
// This is how a signal keeps the same distance off its switch at any scale.
// A leg stub is a couple of hundred metres, which is only a few pixels at map scale,
// so a line that runs out is CARRIED ON in the direction it was going. Without that
// every mark clamped to the end of its stub and piled back onto the switch.
// Cut a projected path back to a given number of screen pixels.
function clip(q,maxPx){
 if(q.length<2)return q;
 const out=[q[0]];let run=0;
 for(let i=1;i<q.length;i++){
  const dx=q[i][0]-q[i-1][0],dy=q[i][1]-q[i-1][1],L=Math.hypot(dx,dy);
  if(run+L>=maxPx){const t=(maxPx-run)/L;out.push([q[i-1][0]+dx*t,q[i-1][1]+dy*t]);return out}
  out.push(q[i]);run+=L}
 return out}
function pathLen(q){let t=0;for(let i=1;i<q.length;i++)t+=Math.hypot(q[i][0]-q[i-1][0],q[i][1]-q[i-1][1]);return t}
// Drop the first PX of a path and keep the rest: this is what parts an unset branch
// from the switch by a visible gap, the way a panel shows a route that is not made.
function skipAlong(q,fromPx){
 if(q.length<2)return q;
 let run=0;
 for(let i=1;i<q.length;i++){
  const dx=q[i][0]-q[i-1][0],dy=q[i][1]-q[i-1][1],L=Math.hypot(dx,dy)||1e-6;
  if(run+L>=fromPx){
   const t=(fromPx-run)/L;
   const out=[[q[i-1][0]+dx*t,q[i-1][1]+dy*t]];
   for(let m=i;m<q.length;m++)out.push(q[m]);
   return out}
  run+=L}
 return [q[q.length-1]]}
function walkAlong(q,dist){
 if(!q||q.length<2)return null;
 let run=0;
 for(let i=1;i<q.length;i++){
  const dx=q[i][0]-q[i-1][0],dy=q[i][1]-q[i-1][1],L=Math.hypot(dx,dy)||1e-6;
  if(run+L>=dist){
   const t=(dist-run)/L;
   return [[q[i-1][0]+dx*t,q[i-1][1]+dy*t],[dx/L,dy/L]]}
  run+=L}
 const n=q.length-1;
 const dx=q[n][0]-q[n-1][0],dy=q[n][1]-q[n-1][1],L=Math.hypot(dx,dy)||1e-6;
 const ux=dx/L,uy=dy/L,over=dist-run;
 return [[q[n][0]+ux*over,q[n][1]+uy*over],[ux,uy]]}
// The mark nearest the pointer, in the map's own coordinates. Hit areas at a switch
// overlap however carefully they are sized, so proximity decides instead of stacking
// order: whatever the eye reads as closest is what answers the click.
function nearestMark(e){
 const svg=$('railsSvg');
 if(!svg||!railMarks.length||!svg.getScreenCTM)return null;
 const m=svg.getScreenCTM();
 if(!m)return null;
 const pt=svg.createSVGPoint();pt.x=e.clientX;pt.y=e.clientY;
 const p=pt.matrixTransform(m.inverse());
 const reach=railSize(34);
 let best=null,bd=reach*reach;
 for(const k of railMarks){
  if(!k.click)continue;
  const d=(k.x-p.x)*(k.x-p.x)+(k.y-p.y)*(k.y-p.y);
  if(d<bd){bd=d;best=k}}
 return best}
// EVERY rail on this map goes through here: the lines, the cleared roads, the switch
// arms, and the walk that decides where a signal stands. If they did not share it, a
// green road would sit beside its own rail and a signal would float off its leg.
// Geographic, and only geographic: real curves, real positions, nothing moved. The
// schematic and the field that opened out throats are gone (owner ruling); zoom is what
// makes a throat workable now, and it does not lie about where anything is.
function railPath(q,side){return smooth(railFan(q,side))}
function railFan(q,side){
 if(!side||q.length<2)return q;
 const out=[];
 for(let k=0;k<q.length;k++){
  const a=q[Math.max(0,k-1)],b=q[Math.min(q.length-1,k+1)];
  const dx=b[0]-a[0],dy=b[1]-a[1],L=Math.hypot(dx,dy)||1;
  out.push([q[k][0]-dy/L*RAIL_FAN()*side, q[k][1]+dx/L*RAIL_FAN()*side]);}
 return out}
// A switch or signal standing on a fanned rail has to move with it, or it sits on the
// centreline between the two tracks. The server sends the rail's heading in world
// units; projecting it first keeps the offset square to what is drawn.
function railPoint(x,z,side,dirX,dirZ){
 const q=rxy(x,z);
 if(!side)return q;
 const a=rxy(x,z),b=rxy(x+(dirX||1)*10,z+(dirZ||0)*10);
 const dx=b[0]-a[0],dy=b[1]-a[1],L=Math.hypot(dx,dy)||1;
 return [q[0]-dy/L*RAIL_FAN()*side, q[1]+dx/L*RAIL_FAN()*side]}
// Signals come in groups at every junction (a trunk and one per branch), so at map
// scale they land on top of each other. Slide a clashing mark ALONG its own rail until
// it has room: it stays on the rail it belongs to, which keeps the picture honest while
// keeping every mark visible and clickable.
// A signal is drawn as a triangle whose apex points the way it governs, so its facing
// reads without hovering (owner ruling, and how the reference panel does it).
function triAt(q,u,k){
 const ux=u[0],uy=u[1],z=k||1;
 const px=-uy,py=ux,L=railSize(21)*z,W=railSize(13)*z;
 return [[q[0]+ux*L,q[1]+uy*L],
         [q[0]-ux*railSize(5)*z+px*W,q[1]-uy*railSize(5)*z+py*W],
         [q[0]-ux*railSize(5)*z-px*W,q[1]-uy*railSize(5)*z-py*W]]
   .map(v=>v[0].toFixed(1)+','+v[1].toFixed(1)).join(' ')}
// Push crowded marks apart until each has room, without letting any of them wander off
// the railway they belong to. Every mark keeps an anchor at its true position; each
// round shoves overlapping pairs apart, then a spring pulls everything back toward its
// anchor and a hard limit caps how far it can ever end up. Marks with room never move.
//
// Measured on a live world at the default scale: 537 marks, 433 pairs overlapping,
// worst pair 0.6px apart. After this, no pair under 22px, median shift 3.9px, ninety
// percent under 15px. The earlier version slid a clashing mark ALONG its own rail,
// which kept it on the line but strung junction groups out down the track.
function spread(items,minSep,limit){
 if(items.length<2)return items;
 const cell=minSep;
 for(let round=0;round<60;round++){
  const g=new Map();
  for(const m of items){
   const k=Math.floor(m.x/cell)+':'+Math.floor(m.y/cell);
   let b=g.get(k);if(!b)g.set(k,b=[]);b.push(m)}
  let hits=0;
  for(let i=0;i<items.length;i++){
   const m=items[i],cx=Math.floor(m.x/cell),cy=Math.floor(m.y/cell);
   for(let ax=-1;ax<=1;ax++)for(let ay=-1;ay<=1;ay++){
    const b=g.get((cx+ax)+':'+(cy+ay));
    if(!b)continue;
    for(const o of b){
     if(o===m)continue;
     let dx=m.x-o.x,dy=m.y-o.y,d=Math.hypot(dx,dy);
     if(d>=minSep)continue;
     if(d<0.01){
      // Dead level: fan them by index so the result is the same every render
      // rather than jittering from poll to poll.
      const a=(i%8)*0.785398;dx=Math.cos(a);dy=Math.sin(a);d=1}
     // A fixed mark shoves but is never shoved: switches are placed first and the
     // railway is drawn to follow them, so nothing may move one afterwards.
     const push=(minSep-d)*(m.fix||o.fix?0.6:0.3);
     if(!m.fix){m.x+=dx/d*push;m.y+=dy/d*push}
     if(!o.fix){o.x-=dx/d*push;o.y-=dy/d*push}
     hits++}}}
  for(const m of items){
   if(m.fix)continue;
   m.x+=(m.ax-m.x)*0.03;m.y+=(m.ay-m.y)*0.03;
   const dx=m.x-m.ax,dy=m.y-m.ay,d=Math.hypot(dx,dy);
   if(d>limit){m.x=m.ax+dx/d*limit;m.y=m.ay+dy/d*limit}}
  if(!hits)break}
 return items}
function applyRailsVB(){if(railsVB)$('railsSvg').setAttribute('viewBox',railsVB.map(v=>v.toFixed(1)).join(' '))}
// ── dispatch log ─────────────────────────────────────────────────────────
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
  return `<div style='padding:3px 0;background:linear-gradient(to right,transparent,#23253a 20px,#23253a calc(100% - 20px),transparent) no-repeat bottom/100% 1px'><span class='meta num'>${t}</span> <b>${esc(e.Yard||'')}</b> ${verb[e.Type]||esc(e.Type)} ${amt} ${esc(e.Cargo||'')}${e.JobId?` <span class='meta'>(${esc(e.JobId)})</span>`:''}</div>`}).join('');
}
// ── cargo naming ─────────────────────────────────────────────────────────
const CATS={Tools:['ToolsIskar','ToolsBrohm','ToolsAAG','ToolsNovae','ToolsTraeg'],
 Electronics:['ElectronicsIskar','ElectronicsKrugmann','ElectronicsAAG','ElectronicsNovae','ElectronicsTraeg'],
 Clothing:['ClothingObco','ClothingNeoGamma','ClothingNovae','ClothingTraeg'],
 Chemicals:['ChemicalsIskar','ChemicalsSperex'],
 Gases:['CryoHydrogen','Ammonia','SodiumHydroxide'],
 EmptyContainers:['EmptySunOmni','EmptyIskar','EmptyObco','EmptyGoorsk','EmptyKrugmann',
  'EmptyBrohm','EmptyAAG','EmptySperex','EmptyNovae','EmptyTraeg','EmptyChemlek','EmptyNeoGamma']};
const DISP={ToolsIskar:'Tools',ElectronicsIskar:'Electronics',ChemicalsIskar:'Chemicals',
 __logi:'Empty riders (logi)',None:'Empty riders'};
function disp(c){return DISP[c]||c}
function lineDisp(c){return c===LOGI?'Empty riders':disp(c)}
const RESOURCES=new Set(['IronOre','Coal','Logs','CrudeOil','Methane','ScrapMetal','ScrapWood',
 'Wheat','Corn','Milk','Eggs','Cotton','Wool','SunflowerSeeds','Pigs','Cows','Poultry','Sheep','Goats',
 'TemperateFruits','Vegetables','Flour','Fish']);
const MATERIALS=new Set(['SteelRolls','SteelBillets','SteelSlabs','SteelBentPlates','SteelRails',
 'Boards','Plywood','Sleepers','WoodChips','Pipes','Gasoline','Diesel','ChemicalsIskar',
 'CryoHydrogen','Ammonia','SodiumHydroxide','Argon','CryoOxygen','Nitrogen','Acetylene','AmmoniumNitrate']);
function cargoClass(c){return RESOURCES.has(c)?'resource':MATERIALS.has(c)?'material':'goods'}
function stockRow(s,cap,tag){
 const pct=cap>0?Math.min(100,Math.round(100*s.amount/cap)):0;
 const held=s.reserved>=1?` · ${Math.round(s.reserved)} held`:'';
 const recv=s.imported>=1?` · ${Math.round(s.imported)} received`:'';
 return `<div class='stockrow'><span class='cname' title='held = committed to a taken haul; received = delivered here, ships onward unpaid until consumed; bars show the share of the station total'>${esc(disp(s.cargo))} <span class='ctag'>${cargoClass(s.cargo)}</span>${tag||''}</span>`+
  `<div class='bar'><i style='width:${pct}%'></i></div>`+
  `<span class='nums num'>${Math.round(s.amount)}${held}${recv}</span></div>`;
}
// ── dock: station panel (#126 layout, Nocturne skin) ─────────────────────
let netFolds=new Set();
function fold(key,title,inner,count){
 const open=netFolds.has(key);
 return `<div class='foldbtn' data-act='netFold' data-key='${key}'>${open?'▾':'▸'} ${title}${count!=null?` <span class='count'>${count}</span>`:''}</div>`+
  (open?`<div class='foldbody'>${inner}</div>`:'')}
function renderDockStation(){
 const d=$('dockStationBody');if(!d)return;
 const sel=netSel;
 if(!sel||!lastEconData.length){d.innerHTML=`<div class='dsec meta'>click a station on the map</div>`;return}
 const {nodes,edges}=buildNet(lastEconData,options);
 const n=nodes[sel];
 if(!n){d.innerHTML=`<div class='dsec meta'>no data for ${esc(sel)}</div>`;return}
 const cap=Math.round(n.totalCap||0),used=Math.round(n.totalStock||0);
 const upct=n.totalCap>0?Math.min(100,Math.round(100*used/n.totalCap)):0;
 const barCls=upct>=95?'crit':upct>=80?'warn':'';
 const miss=netMissing(n);
 const desk=deskOf(sel);
 let h=`<div class='dsec'><div class='sthead'>${spine(sel,sel)}
  <div><div class='stname'>${scChip(sel)} ${esc(NET_NAMES[sel]||sel)}</div>
  <div class='k' style='margin-top:3px'>${n.source?'source':n.importHub?'import hub':n.consumer?'city':'factory'}${desk?' · '+desk+' desk':''}</div></div>
  <span class='spacer'></span>
  <button class='primary' data-act='jmOpen' data-id='${esc(sel)}'>Open yard →</button></div>
  <div style='display:flex;gap:6px;flex-wrap:wrap;margin-top:8px'>`;
 if(miss.length)h+=`<span class='stchip bad'>waiting on ${esc(miss.map(disp).join(', '))}</span>`;
 if(n.machineWarning)h+=`<span class='stchip warn'>machines low</span>`;
 const catNames=esc((n.catalysts||[]).map(disp).join(' or '));
 if((n.catalysts||[]).length)h+=`<span class='stchip ${n.catalystActive?'good':'idle'}' title='${n.source?'slows machine wear':'doubles batch speed'} while active'>${n.catalystActive?'catalyst ('+catNames+') active · '+n.catalystHoursLeft+'h left':n.catalystStocked?'catalyst ('+catNames+') stocked':'no catalyst ('+catNames+')'}</span>`;
 if(upct>=95)h+=`<span class='stchip bad'>storage full</span>`;
 else if(upct>=80)h+=`<span class='stchip warn'>storage ${upct}%</span>`;
 h+=`</div></div>`;
 if(cap>0)h+=`<div class='dsec' title='one shared pool: every cargo counts against the same station total'><div style='display:flex;justify-content:space-between;margin-bottom:5px'><span class='k'>Storage</span><span class='num' style='font:600 11.5px Inter,sans-serif;color:#b2b6ca'>${used} / ${cap}</span></div>
  <div class='bar'><i class='${barCls}' style='width:${upct}%'></i></div></div>`;
 const needs=[];
 for(const r of (n.recipes||[]))for(const i of (r.inputs||[])){
  const have=stockAmt(n,i.cargo);
  if(have<i.amount&&!needs.some(x=>x.cargo===i.cargo))needs.push({cargo:i.cargo,have,need:i.amount})}
 if(needs.length){
  needs.sort((a,b)=>disp(a.cargo).localeCompare(disp(b.cargo)));
  h+=`<div class='dsec'><div class='k' style='margin-bottom:5px'>Needs</div>`+
   needs.map(x=>`<div class='needrow'><b>${esc(disp(x.cargo))}</b><span class='num'>${Math.round(x.have)} of ${x.need} on hand</span></div>`).join('')+`</div>`}
 const outs=edges.filter(e=>e.src===sel);
 const outFams=new Set((n.outputs||[]).map(famOf));
 const ship=[];
 for(const s of (n.stock||[])){
  if(!outFams.has(famOf(s.cargo))||s.amount<1)continue;
  const dests=[...new Set(outs.filter(e=>e.cargos.some(c=>famOf(c)===famOf(s.cargo))).map(e=>e.dst))];
  ship.push({cargo:s.cargo,amount:s.amount,dests})}
 let noteH='';
 if(!n.source&&(n.outputs||[]).length===0)
  noteH+=`<div class='nrecipe meta'>${n.consumer?'consumes its stock on the clock; keeping it fed boosts every industry':'accepts <b>'+esc((n.inputs||[]).map(disp).join(', '))+'</b>; storage is the demand'}</div>`;
 if(n.source&&!ship.length)noteH+=`<div class='nrecipe meta'>produces ${esc((n.outputs||[]).map(disp).join(', '))} over time; nothing shippable yet</div>`;
 if(n.importHub)noteH+=`<div class='nrecipe meta'>imports scale with the exports delivered here</div>`;
 if(noteH)h+=`<div class='dsec'>${noteH}</div>`;
 const byName=(a,b)=>disp(a.cargo).localeCompare(disp(b.cargo));
 const rows=(n.stock||[]);
 const dprod=rows.filter(s=>outFams.has(famOf(s.cargo))).sort(byName);
 const dcons=rows.filter(s=>!outFams.has(famOf(s.cargo))).sort(byName);
 const gsum=g=>Math.round(g.reduce((t,s)=>t+(s.amount||0),0));
 let recipesH='';
 if(n.source)recipesH+=`<div class='nrecipe'>produces resources over time: <b>${esc((n.outputs||[]).map(disp).join(', '))}</b></div>`;
 if((n.recipes||[]).length)
  recipesH+=n.recipes.map(r=>`<div class='nrecipe'>needs ${r.inputs.map(i=>esc(i.amount+' '+disp(i.cargo))).join(' + ')} → makes ${r.outputs.map(o=>esc(o.amount+' '+disp(o.cargo))).join(' + ')}</div>`).join('');
 if(!recipesH)recipesH=`<div class='meta'>no recipes; storage itself is the demand</div>`;
 const shipMap={};for(const x of ship)shipMap[famOf(x.cargo)]=x.dests;
 const prodH=dprod.length?dprod.map(s=>{
  const ds=shipMap[famOf(s.cargo)];
  const shipTag=ds?` <span class='tag' style='background:#16233a;color:var(--blue)'>can ship</span>`:'';
  return stockRow(s,n.totalCap||0,shipTag)+
   (ds?`<div class='shipto'>→ ${ds.length?esc(ds.join(', ')):'no consumer has room'}</div>`:'')}).join('')
  :`<div class='meta'>nothing produced on hand</div>`;
 const catSet=new Set((n.catalysts||[]).map(famOf));
 const machSet=new Set((n.machines||[]).map(m=>famOf(m.cargo)));
 const consH=dcons.length?dcons.map(s=>{
  const f2=famOf(s.cargo);
  const tag=machSet.has(f2)?` <span class='tag'>machine</span>`:catSet.has(f2)?` <span class='tag' style='background:#1d3527;color:var(--green)'>catalyst</span>`:'';
  return stockRow(s,n.totalCap||0,tag)}).join('')
  :`<div class='meta'>nothing on hand to work through</div>`;
 let machH='';
 if((n.machines||[]).length){
  for(const m of n.machines){
   const cls=m.have<=0?'out':m.have<2?'low':'';
   machH+=`<div class='machrow'><span class='mname'>${esc(m.cargo)}</span>`+
    `<span class='mcount ${cls}'>×${m.have}${m.have<=0?' · CRAWLING':m.have<2?' · last one':''}</span>`+
    `<span class='mwear'>current unit: ${m.wearRemaining} carloads of work left</span></div>`;
  }
 }
 if((n.catalysts||[]).length){
  machH+=`<div class='sublab'>catalyst · ${esc(n.catalysts.join(' or '))}</div>`+
   `<div class='nrecipe' style='color:${n.catalystActive?'var(--green)':'var(--dim)'}'>`+
   (n.catalystActive?`active · ${n.catalystHoursLeft}h of work left on this carload`
    :n.catalystStocked?'in stock, starts with the next shift':'none in stock')+
   ` <span class='meta'>(${n.source?'slows machine wear':'doubles batch speed'})</span></div>`;
 }
 if(!machH)machH=`<div class='meta'>no machines required here</div>`;
 const ins=edges.filter(e=>e.dst===sel);
 const inH=ins.length?ins.map(e=>`<div class='nrecipe'>${esc(e.src)}: ${esc(e.cargos.map(disp).join(', '))}</div>`).join(''):`<div class='meta'>nothing inbound on the map</div>`;
 h+=`<div class='dsec' style='border-bottom:0'>`+fold('inv','Full inventory',
  fold('inv-r','Recipes',recipesH)+
  fold('inv-p','Produced',prodH,gsum(dprod)||null)+
  fold('inv-c','Consumes',consH,gsum(dcons)||null)+
  fold('inv-m','Machines and catalyst',machH)+
  fold('inv-i','Consumption supply points',inH))+`</div>`;
 d.innerHTML=h;
}
// Unnamed world tracks come through as raw ids like #Y-#S1437#T; read them as
// what they are: a numbered siding outside any yard.
function trackDisp(t){if(!t||t[0]!=='#')return t;const m=String(t).match(/S(\d+)/);
 return m?'siding '+m[1]:'siding'}
// Short designator for the track-ID chip: the yard letter and number when the
// name parses, the siding number otherwise, else a dot.
function tidOf(t){
 if(!t)return '·';
 if(t[0]==='#'){const m=String(t).match(/S(\d+)/);return m?m[1]:'S'}
 const m=String(t).match(/(?:^|-)([A-H])-?(\d+)/);
 if(m)return m[1]+m[2];
 const n=String(t).match(/(\d+)/);
 return n?n[1]:String(t).slice(0,3)}
function sheetOf(t){
 if(!t)return '~';
 if(t[0]==='#')return '~';
 const m=String(t).match(/(?:^|-)([A-H])-?\d/);
 return m?m[1]:'~'}
// ── fleet surface ────────────────────────────────────────────────────────
function renderFleet(r){
 $('fSummary').textContent=(r.total+(r.dormant||0))+' freight car(s), '+r.usable+' usable now';
 $('fSummary').title='locomotives and tenders are not listed; the board live count includes them';
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
// ── candidate picker (dock) ──────────────────────────────────────────────
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
   title='${esc(c.type)} on ${esc(c.track)}' style='cursor:pointer${sameTrack?';border-color:#4a7fae':''}'>${on?'✓ ':''}${esc(c.carId)} · ${esc(trackDisp(c.track))}${dist==null?'':' · '+dist+'m'}</span>`};
 const done=p.sel.length===d.wanted;
 box.innerHTML=`<div style='margin-bottom:5px'>pick <b>${d.wanted}</b> car(s), ${lastSel?'same track as <b>'+esc(lastSel.carId)+'</b> (<b>'+esc(trackDisp(lastSel.track))+'</b>) first, then nearest elsewhere':'sorted by distance to the loading track'}</div>`+
  p.sel.map(cid=>chip(byId[cid],true)).join('')+rest.map(c=>chip(c,false)).join('')+
  `<div style='margin-top:8px;display:flex;gap:8px;align-items:center'>
   <button class='primary' data-act='loadPicked' data-id='${esc(id)}' ${done?'':'disabled'}>Start loading</button>
   <button class='mini' data-act='pickAuto' data-id='${esc(id)}' title='Let the station pick the nearest suitable empties'>Auto-pick</button>
   <span class='meta'>${p.sel.length}/${d.wanted} picked · staff ≈ ${fmtSecs(Math.max(0,p.sel.length-1)*d.perCarSeconds)} (first car instant, ${d.perCarSeconds}s per car after)</span>
  </div>`;
}
// ── yard surface (#118, #119): tracks with cars in consist order ─────────
let yardKey='',yardBusy=false;
function yardOpen(){return lens==='logi'&&surface==='yard'}
async function pollYard(force){
 const y=$('hOrigin').value;
 // A forced poll (station change, yard open) must never be dropped because a
 // routine poll is in flight; the origin re-check below keeps stale responses out.
 if(!y||(!force&&!yardOpen())||(!force&&yardBusy))return;
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
 const y=$('hOrigin').value;
 $('yhChip').textContent=y||'?';
 $('yhChip').style.background=SC[y]||'#4a4e60';
 $('yhChip').className='sc'+(SC_DARK.has(y)?' txl':'');
 $('yhName').textContent=d?(d.name||''):'';
 if(!d){box.innerHTML=`<div class='empty'>pick a station on the strip below</div>`;$('jmMeta').textContent='';$('sheetTabs').innerHTML='';return}
 // Sheet tabs: the game's own board letters, parsed from the track names.
 const sheets=[...new Set((d.tracks||[]).map(t=>sheetOf(t.track)))].sort();
 const showTabs=sheets.filter(s=>s!=='~').length>1;
 if(showTabs){
  if(jmSheet!=='ALL'&&!sheets.includes(jmSheet))jmSheet='ALL';
  $('sheetTabs').innerHTML=[`<span class='tab mini${jmSheet==='ALL'?' on':''}' data-act='sheet' data-id='ALL'>A overview</span>`]
   .concat(sheets.filter(s=>s!=='~').map(s=>`<span class='tab mini${jmSheet===s?' on':''}' data-act='sheet' data-id='${s}'>${s}</span>`))
   .concat(sheets.includes('~')?[`<span class='tab mini${jmSheet==='~'?' on':''}' data-act='sheet' data-id='~'>sidings</span>`]:[]).join('')}
 else{$('sheetTabs').innerHTML='';jmSheet='ALL'}
 // Re-rendering wipes each track row's horizontal scroll; capture and restore.
 const scrolls={};
 box.querySelectorAll('.ytrack').forEach(el=>{
  const sc=el.querySelector('.ycars');
  if(el.dataset.track&&sc&&sc.scrollLeft)scrolls[el.dataset.track]=sc.scrollLeft});
 let total=0,selDropped=0;
 const inLine=lineCarSet();
 const shown=(d.tracks||[]).filter(t=>jmSheet==='ALL'||sheetOf(t.track)===jmSheet);
 const rows=shown.map(t=>{
  total+=t.carCount;
  let loaded=0,free=0;
  const cuts=(t.cuts||[]).map(cut=>`<span class='ycut'>`+cut.map(c=>{
   if(c.cargo)loaded++;else if(c.usable)free++;
   const banked=inLine.has(c.carId);
   // A picked car that got loaded, booked or reserved since the pick is no
   // longer the car the dispatcher chose: drop it rather than booklet it.
   if(jmSelSet.has(c.carId)&&(c.loco||c.cargo||!c.usable)){jmSelSet.delete(c.carId);selDropped++}
   const on=jmSelSet.has(c.carId);
   const compat=jmCompat===null||jmCompat.has(c.carId);
   const cls=on?'sel':c.loco?'loco':banked?'inline':c.usable?(compat?'ok':'incompat'):(c.cargo?'loaded':'busy');
   const why=c.loco?'locomotive':banked?'banked in a manifest line':c.cargo?('loaded: '+c.cargo):c.jobId?('on job '+c.jobId):c.reservedBy?('reserved for '+c.reservedBy):c.playerSpawned?'player car':compat?'empty and free':'cannot carry the chosen cargo';
   return `<span class='ycar ${cls}' data-act='ycar' data-car='${esc(c.carId)}' title='${esc(c.type)} · ${esc(why)}'>${esc(c.carId)}</span>`}).join('')+`</span>`)
   .join(`<span class='meta' style='flex:none'>·</span>`);
  const e=(t.ends||'').split('|');
  const n=t.carCount+(t.dormantCount||0);
  const summary=n===0?`clear · ${t.lengthM} m`
   :`${n} cars · ${t.usedM}/${t.lengthM} m${loaded?` · <span class='ld'>${loaded} loaded</span>`:''}`;
  return `<div class='ytrack${t.warehouse?' wh':''}' data-track='${esc(t.track)}'>
   <span class='tid' title='${esc(t.track)}'>${esc(tidOf(t.track))}</span>
   <span class='ytlabel'><b>${esc(trackDisp(t.track))}</b>`+
   `${t.warehouse?`<span class='whlab' title='${esc((t.warehouseCargos||[]).join(', '))}'>loading</span>`:''}</span>`+
   `<span class='yend'>${esc(e[0]||'')}</span>`+
   `<div class='ycars'>${cuts||''}</div>`+
   `<span class='yend r'>${esc(e[1]||'')}</span>`+
   `<span class='ytmeta num'>${summary}</span></div>`});
 box.innerHTML=rows.join('')||`<div class='empty'>no yard tracks reported</div>`;
 box.querySelectorAll('.ytrack').forEach(el=>{
  const sc=el.querySelector('.ycars');
  if(el.dataset.track&&sc&&scrolls[el.dataset.track])sc.scrollLeft=scrolls[el.dataset.track]});
 $('jmMeta').textContent=jmSheet==='ALL'
  ?(total+(d.dormantCars||0))+' cars in yard'
  :total+' cars on sheet '+(jmSheet==='~'?'sidings':jmSheet);
 if(selDropped){toast(selDropped+' picked car(s) are no longer free; dropped',true);syncSelUi()}
}
function renderStrip(){
 const box=$('strip');if(!box)return;
 const ys=[...new Set(lastEconData.map(e=>e.yardId))].sort();
 const cur=$('hOrigin').value;
 box.innerHTML=ys.map(y=>scChip(y,y===cur,'stripJump')).join('')
  +`<div class='spacer'></div><span class='k' style='white-space:nowrap'>station colours are the game's own · click to jump</span>`}
// ── compatibility: which usable cars can carry the chosen cargo ──────────
let compatSeq=0,compatKey='',compatAt=0;
async function fetchCompat(){
 const y=$('hOrigin').value,c=$('hCargo').value;
 if(!y||!c){if(jmCompat!==null){jmCompat=null;renderYard()}compatKey='';return}
 const key=y+'|'+c,now=Date.now();
 if(key===compatKey&&now-compatAt<15000)return;
 const seq=++compatSeq;
 try{const r=await jget('/api/v1/fleet?cargo='+encodeURIComponent(c)+'&yard='+encodeURIComponent(y));
  if(seq!==compatSeq)return;
  // Commit the cache key only on success: a failed fetch used to poison the
  // 15s window with the PREVIOUS cargo's verdict and block every retry.
  compatKey=key;compatAt=now;
  jmCompat=new Set((r.cars||[]).filter(x=>x.usable).map(x=>x.carId));
  let dropped=0;
  for(const id of [...jmSelSet])if(!jmCompat.has(id)){jmSelSet.delete(id);dropped++}
  if(dropped)toast(dropped+' picked car(s) cannot carry '+disp(c)+'; dropped',true);
  syncSelUi();renderYard()}
 catch(e){}}
function syncSelUi(){
 const n=jmSelSet.size,inp=$('hCars');
 if(n>0){inp.value=n;inp.disabled=true}else inp.disabled=false;
 $('bkHint').textContent=n?`${n} picked`:'pick cars on any track';
 $('jmSel').innerHTML=n
  ?`<b>${n}</b> car(s) picked${jmLines.length?' for the next line':''}; the booklet takes exactly these`
  :jmLines.length?'pick cars for another line, or create the booklet from the banked lines'
  :'no cars picked: the booklet goes out carless and crews or staff auto-pick bring empties';
 updateEstimate()}
// ── the banked manifest: the drawing is the form ─────────────────────────
function renderManifest(){
 const box=$('jmManifest');if(!box)return;
 $('bkRoute').textContent=($('hOrigin').value||'?')+' → '+(effDest()||'?');
 if(!jmLines.length){box.innerHTML='';return}
 let cars=0,pay=0,per=0;
 const rows=jmLines.map((l,i)=>{cars+=l.cars.length;pay+=l.pay||0;if(l.per)per=l.per;
  return `<div class='mline'><div class='lhead'><b>Line ${i+1} · ${esc(lineDisp(l.cargo))}</b>
   <span class='lpay num'>${l.pay?money(l.pay):''}</span>
   <button class='mini danger' data-act='jmDelLine' data-id='${i}'>×</button></div>
   <div class='lcars'>${l.cars.map(c=>`<span class='ycar sel'>${esc(c)}</span>`).join('')}</div>
   <span class='k'>${l.cars.length} car(s)</span></div>`});
 const staff=cars>1&&per?` · staff load ≈ ${fmtSecs((cars-1)*per)}`:'';
 box.innerHTML=rows.join('')+
  `<div class='mline total'><b>${cars} car(s), ${jmLines.length} line(s)</b>`+
  `${pay?`<span class='num' style='color:var(--green);margin-left:auto'>≈ ${money(pay)}${staff}</span>`:''}</div>`}
function renderDestChips(){
 const sel=$('hDest');const box=$('destChips');if(!box)return;
 const locked=jmLines.length>0&&jmDest;
 const opts=[...sel.options].map(o=>o.value);
 $('destLab').textContent=locked?'Destination · locked by line 1':'Destination · shippable from '+($('hOrigin').value||'?');
 box.innerHTML=opts.length?opts.map(v=>scChip(v,v===sel.value,'destPick')).join('')
  :`<span class='meta'>pick a cargo first; only reachable stations appear</span>`;
 if(locked)box.innerHTML=scChip(jmDest,true)+`<span class='meta' style='align-self:center'>every line of the booklet goes here</span>`}
function crewVal(id){const i=$('a_'+id);return i&&i.value?i.value:null}
// ── actions ──────────────────────────────────────────────────────────────
const actions={
 lens:(id)=>{setLens(id)},
 backMap:()=>backToMap(),
 sheet:(id,el)=>{jmSheet=el.dataset.id;renderYard()},
 railZoom:(id,el)=>setRailScale(RAIL_MPP*(el.dataset.id==='in'?0.7:1/0.7),RAIL_G),
 railGlyph:(id,el)=>setRailScale(RAIL_MPP,RAIL_G*(el.dataset.id==='up'?1.25:0.8)),
 throwSwitch:async(id,el)=>{
  const r=await j('/api/v1/junctions/'+el.dataset.id+'/throw','POST');
  toast(r.message||(r.ok?'switch thrown':'throw refused'),!r.ok);
  if(r.ok){try{lastInter=await jget('/api/v1/interlocking')}catch(e){}renderRailsDyn()}},
 signal:async(id,el)=>{
  const sid=el.dataset.id;
  const set=(lastInter&&(lastInter.signals||[]).find(s=>String(s.id)===String(sid))||{}).road;
  const r=await j('/api/v1/signals/'+encodeURIComponent(sid)+'/'+(set?'cancel':'clear'),'POST');
  toast(r.message||(r.ok?(set?'signal back on':'road set'):'refused'),!r.ok);
  try{lastInter=await jget('/api/v1/interlocking')}catch(e){}
  renderRailsDyn()},
 stripJump:(id,el)=>{openYard(el.dataset.id)},
 destPick:(id,el)=>{const v=el.dataset.id;const sel=$('hDest');
  if(jmLines.length&&jmDest)return;
  if(![...sel.options].some(o=>o.value===v))return;
  sel.value=v;jmDestPicked=true;originChanged()},
 laneOpen:(id)=>{haulSel=haulSel===id?null:id;dockMode=haulSel?'haul':'hint';
  if(surface==='yard'&&haulSel){setSurface('map')}
  syncDock();renderDockHaul();drawNet();last.jobs=null;refresh()},
 dockClose:()=>{dockMode='hint';haulSel=null;netSel=null;drawNet();syncDock()},
 ctc:async()=>{const r=await j('/api/v1/ctc','PUT',{enabled:!ctcOn});
  toast(r.message||(r.ok?'CTC changed':'CTC refused'),!r.ok);
  try{lastInter=await jget('/api/v1/interlocking')}catch(e){}
  renderRailsDyn();refresh()},
 lock:async()=>{const r=await j('/api/v1/lock','PUT',{enabled:!lockOn});
  toast('The director is now '+(r.lockEnabled?'OFF':'ON')+(r.purged?'; '+r.purged+' open booklet(s) expired, supply returned':''));refresh()},
 spawnHaul:async()=>{
  if(spawnBusy)return;spawnBusy=true;try{
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
  refresh()}finally{spawnBusy=false}},
 spawnHaulLoad:async()=>{
  if(spawnBusy)return;spawnBusy=true;try{
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
  await afterCreate(r.jobId,true);
  const l=await j('/api/v1/jobs/'+r.jobId+'/load','POST');
  toast('Created '+r.jobId+'; '+(l.message||'load failed'),!l.ok);
  jmLines=[];jmSelSet.clear();renderManifest();syncSelUi();setTimeout(refresh,1200)}finally{spawnBusy=false}},
 jmAddLine:async()=>{
  if(jmAddBusy)return;
  const c=$('hCargo').value,d=$('hDest').value,sel=[...jmSelSet];
  if(!c){toast('choose a cargo first',true);return}
  if(!d){toast('choose the destination first: every line of the booklet goes there',true);return}
  if(!sel.length){toast('pick the cars for this line first',true);return}
  jmAddBusy=true;
  try{
   const line={cargo:c,cars:sel,pay:0,per:0};
   try{if(c!==LOGI){const r=await jget(`/api/v1/estimate?origin=${encodeURIComponent($('hOrigin').value)}&destination=${encodeURIComponent(d)}&cargo=${encodeURIComponent(c)}&cars=${sel.length}`);line.pay=r.pay||0;line.per=r.perCarSeconds||0}}catch(e){}
   jmLines.push(line);
   if(jmLines.length===1)jmDest=d;
   jmSelSet.clear();lastEstQ='';renderManifest();syncSelUi();renderYard();originChanged();
   toast('Line banked: '+sel.length+' x '+lineDisp(c)+' to '+jmDest+'; pick cars for the next cargo')}
  finally{jmAddBusy=false}},
 jmDelLine:(id,el)=>{const i=parseInt(el.dataset.id);if(!(i>=0)||i>=jmLines.length)return;
  jmLines.splice(i,1);if(!jmLines.length)jmDest=null;
  lastEstQ='';renderManifest();renderYard();syncSelUi();originChanged()},
 jmClear:()=>{jmSelSet.clear();jmLines=[];jmDest=null;
  lastEstQ='';renderManifest();syncSelUi();renderYard();originChanged()},
 jmOpen:(id,el)=>{openYard(el.dataset.id)},
 ycar:(id,el)=>{const car=el.dataset.car;
  if(el.classList.contains('inline')){toast('that car is banked in a manifest line; remove the line to free it',true);return}
  if(el.classList.contains('incompat')){toast('that car cannot carry the chosen cargo',true);return}
  if(jmSelSet.has(car))jmSelSet.delete(car);
  else if(el.classList.contains('ok'))jmSelSet.add(car);
  else return;
  syncSelUi();renderYard()},
 netFold:(id,el)=>{const k=el.dataset.key;
  netFolds.has(k)?netFolds.delete(k):netFolds.add(k);renderDockStation()},
 netNode:(id,el)=>{const v=el.dataset.id;netSel=netSel===v?null:v;
  dockMode=netSel?'station':'hint';haulSel=null;
  drawNet();syncDock();renderDockStation()},
 netEdge:(id,el)=>{const o=el.dataset.src,c=el.dataset.cargo,d=el.dataset.dst;
  const os=$('hOrigin');
  if(![...os.options].some(x=>x.value===o)){toast('nothing shippable from '+o+' right now',true);return}
  os.value=o;originChanged();
  const cs=$('hCargo');
  if([...cs.options].some(x=>x.value===c)){cs.value=c;cargoChanged()}
  const ds=$('hDest');
  if([...ds.options].some(x=>x.value===d)){ds.value=d;jmDestPicked=true;originChanged()}
  setSurface('yard');
  toast('Booklet loaded: '+o+' '+disp(c)+' to '+d)},
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
  const r=await j('/api/v1/jobs/'+id,'DELETE');toast(r.message||(r.ok?'Deleted '+id:'delete failed'),!r.ok);
  if(haulSel===id){haulSel=null;dockMode='hint';syncDock()}
  refresh()},
 accChip:(id,el)=>{const o=el.dataset.id;
  accSel.has(o)?accSel.delete(o):accSel.add(o);
  accHidden.delete(o);
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
  // Mixed manifests report a pseudo-cargo the fleet endpoint rejects; search
  // by the first real line cargo instead.
  let cargo=x.cargo&&x.cargo.indexOf(' ')<0?x.cargo:null;
  if(!cargo&&x.lines&&x.lines.length){const l=x.lines.find(l2=>l2.cargo&&l2.cargo!=='None');cargo=l?l.cargo:null}
  if(!cargo){toast('this haul has no searchable cargo',true);return}
  const sel=$('fCargo');
  if(![...sel.options].some(o=>o.value===cargo)){const o=document.createElement('option');o.textContent=cargo;sel.appendChild(o)}
  sel.value=cargo;$('fYard').value='';
  setLens('fleet');
  actions.findCars()},
};
async function afterCreate(jobId,forceTake){
 const crew=$('hCrew').value,take=forceTake||$('hTake').checked;
 try{
  if(crew)await j('/api/v1/assignments/'+jobId,'PUT',{player:crew,assignedBy:'job maker'});
  if(take){const t=await j('/api/v1/jobs/'+jobId+'/take','POST',{player:crew||null});
   if(!t.ok){toast('created, but take failed: '+(t.message||''),true);return}
   if(crew){const f=await j('/api/v1/jobs/'+jobId+'/fax','POST',{player:crew});
    toast(f.ok?'booklet faxed to '+crew:'fax failed: '+(f.message||''),!f.ok)}}
 }catch(e){}}
document.addEventListener('click',e=>{const el=e.target.closest('[data-act]');if(!el)return;
 const fn=actions[el.dataset.act];if(fn)fn(el.dataset.id,el)});
function originChanged(){const o=$('hOrigin').value;
 if(o!==jmStation){jmStation=o;jmSelSet.clear();jmCompat=null;jmYardData=null;yardKey='';jmSheet='ALL';
  jmLines=[];jmDest=null;jmDestPicked=false;renderManifest();syncSelUi();renderYard();renderStrip();pollYard(true)}
 const ed=effDest();
 keepSelect($('hCargo'),options.filter(x=>x.origin===o&&(!ed||(x.consumers||[]).includes(ed))).map(x=>x.cargo).concat([LOGI]));
 cargoChanged()}
function cargoChanged(){const o=$('hOrigin').value,c=$('hCargo').value;
 const locked=jmLines.length>0&&jmDest;
 const allYards=[...new Set(lastEconData.map(e=>e.yardId))].filter(y=>y!==o).sort();
 const union=[...new Set([].concat(...options.filter(x=>x.origin===o).map(x=>x.consumers||[])))].sort();
 let destOpts;
 if(locked)destOpts=[jmDest];
 else if(c===LOGI)destOpts=allYards;
 else if(jmDestPicked)destOpts=union.length?union:allYards;
 else{const opt0=options.find(x=>x.origin===o&&x.cargo===c);destOpts=opt0?opt0.consumers:[]}
 const ed2=effDest();
 if(!locked&&ed2&&!destOpts.includes(ed2))destOpts=[ed2].concat(destOpts);
 keepSelect($('hDest'),destOpts);
 $('hDest').disabled=!!locked;
 renderDestChips();renderManifest();
 if(c===LOGI){jmCompat=null;compatKey='';renderYard();updateEstimate();return}
 fetchCompat();updateEstimate()}
let estTimer=null,estSeq=0,lastEstQ='';
function updateEstimate(){
 clearTimeout(estTimer);
 estTimer=setTimeout(async()=>{
  const o=$('hOrigin').value,c=$('hCargo').value,d=$('hDest').value,n=parseInt($('hCars').value);
  const box=$('hEstimate');
  if(c===LOGI){box.textContent=jmLines.length?'riders: these cars travel empty with the booklet':'unpaid move; closes on arrival';lastEstQ='';renderTotals(null);return}
  // Banked lines carry their own pay; the live estimate only joins the total
  // while cars are actually picked for the next line. Otherwise the leftover
  // cars count double-priced a line that does not exist.
  if(jmLines.length&&jmSelSet.size===0){box.textContent='';lastEstQ='';renderTotals(null);return}
  if(!o||!c||!d||!(n>0)){box.textContent='';lastEstQ='';renderTotals(null);return}
  const q=`${o}|${c}|${d}|${n}`;
  if(q===lastEstQ)return;
  lastEstQ=q;
  const seq=++estSeq;
  try{const r=await jget(`/api/v1/estimate?origin=${encodeURIComponent(o)}&destination=${encodeURIComponent(d)}&cargo=${encodeURIComponent(c)}&cars=${n}`);
   if(seq!==estSeq)return;
   box.textContent='';
   renderTotals(r)}
  catch(e){if(seq===estSeq){$('hEstimate').textContent='';renderTotals(null)}}
 },250)}
function renderTotals(r){
 const box=$('bkTotals');if(!box)return;
 let linePay=0;for(const l of jmLines)linePay+=l.pay||0;
 const pay=(r?r.pay:0)+linePay;
 box.innerHTML=
  `<div class='krow'><span>Pay</span><span class='v num' style='color:var(--green);font-weight:600'>${pay?money(pay):'—'}</span></div>`+
  (r?`<div class='krow'><span>Weight · length</span><span class='v num'>${r.tonnes} t · ${r.lengthMeters} m</span></div>`+
   `<div class='krow'><span>Staff load</span><span class='v num'>${fmtSecs(r.remoteLoadSeconds)}</span></div>`
  :`<div class='krow'><span>Weight · length</span><span class='v'>—</span></div>`)}
$('hOrigin').addEventListener('change',originChanged);
$('hCargo').addEventListener('change',cargoChanged);
$('hDest').addEventListener('change',()=>{jmDestPicked=true;originChanged()});
$('hCars').addEventListener('input',updateEstimate);
$('dlType').onchange=()=>renderLog(lastHist);
$('dlYard').oninput=()=>renderLog(lastHist);
function clearFleet(){$('tFleet').innerHTML='';$('fSummary').textContent=''}
$('fCargo').addEventListener('change',()=>{if(!$('fCargo').value)clearFleet()});
document.addEventListener('contextmenu',e=>{
 const el=e.target.closest(`[data-act='accChip']`);if(!el)return;
 e.preventDefault();const o=el.dataset.id;
 accHidden.has(o)?accHidden.delete(o):accHidden.add(o);
 accSel.delete(o);
 saveAccFilter();
 last.jobs=null;refresh()});
document.addEventListener('keydown',e=>{
 if(e.key!=='Escape')return;
 const t=e.target;
 if(t&&(t.tagName==='INPUT'||t.tagName==='SELECT'||t.tagName==='TEXTAREA'))return;
 if(lens==='logi'&&surface==='yard')backToMap()});
// The Rails map moves ONLY when the dispatcher drags it: no zoom control, no
// auto-pan, no click-to-centre. The scale is fixed, so panning is 1:1 with the
// mouse and the drawing never rebuilds, only the viewBox origin moves.
(function(){
 const svg=$('railsSvg');if(!svg)return;
 let panning=false,px=0,py=0,moved=0;
 svg.addEventListener('mousedown',e=>{panning=true;moved=0;px=e.clientX;py=e.clientY;svg.style.cursor='grabbing'});
 // Zoom answers on the frame it is asked. Redrawing the railway at a new scale costs
 // tens of milliseconds, and doing that per wheel notch is exactly the stutter the old
 // dispatch map had, so the viewBox moves at once and the redraw follows once the wheel
 // stops. Nothing is uncached and rebuilt mid-gesture.
 let pend=1,settle=null;
 svg.addEventListener('wheel',e=>{
  if(!railsVB)return;
  e.preventDefault();
  let k=e.deltaY<0?1.15:1/1.15;
  // The scale has limits, so the GESTURE respects them too. Without this the viewBox
  // kept zooming past what the settle could honour, and the settle then scaled the
  // camera origin by the factor the wheel ASKED for rather than the factor the clamp
  // ALLOWED, throwing the view across the map. That was the teleport at full zoom.
  const target=Math.min(RAIL_MPP/0.3,Math.max(RAIL_MPP/20,pend*k));
  k=target/pend;
  if(Math.abs(k-1)<0.0005)return;
  pend=target;
  const r=svg.getBoundingClientRect();
  const cx=railsVB[0]+(e.clientX-r.left)/r.width*railsVB[2];
  const cy=railsVB[1]+(e.clientY-r.top)/r.height*railsVB[3];
  railsVB[0]=cx-(cx-railsVB[0])/k;railsVB[1]=cy-(cy-railsVB[1])/k;
  railsVB[2]/=k;railsVB[3]/=k;
  applyRailsVB();
  clearTimeout(settle);
  settle=setTimeout(()=>{
   const f=pend;pend=1;
   const keepX=railsVB[0],keepY=railsVB[1];
   const before=RAIL_MPP;
   setRailScale(RAIL_MPP/f,RAIL_G);
   const ratio=before/RAIL_MPP;
   const [vw,vh]=railsViewport();
   railsVB=[keepX*ratio,keepY*ratio,vw,vh];
   clampRails();applyRailsVB()},170)},{passive:false});
 window.addEventListener('mouseup',()=>{panning=false;svg.style.cursor='grab'});
 window.addEventListener('mousemove',e=>{
  if(!panning||!railsVB)return;
  railsVB[0]-=(e.clientX-px);
  railsVB[1]-=(e.clientY-py);
  moved+=Math.abs(e.clientX-px)+Math.abs(e.clientY-py);
  px=e.clientX;py=e.clientY;clampRails();applyRailsVB()});
 // A drag must not fire the station bubble underneath it; the distance clears on
 // use so a later plain click is never swallowed by an older drag.
 // Marks stand close together at a switch, so a click goes to the NEAREST one rather
 // than to whichever hit area happens to lie on top. Chasing a signal or a switch
 // around with the mouse to find the pixel that answers is no way to run a railway.
 svg.addEventListener('click',e=>{
  if(moved>6){moved=0;e.stopPropagation();e.preventDefault();return}
  if(e.target.closest && e.target.closest(`[data-act='stripJump']`))return;
  const m=nearestMark(e);
  if(!m)return;
  e.stopPropagation();e.preventDefault();
  const fake={dataset:{id:String(m.id)}};
  if(m.kind==='sig')actions.signal(null,fake); else actions.throwSwitch(null,fake)},true);
 window.addEventListener('resize',()=>{
  if(!railsVB)return;
  const [vw,vh]=railsViewport();
  railsVB[2]=vw;railsVB[3]=vh;clampRails();applyRailsVB()});
})();
syncDock();
refresh();setInterval(refresh,5000);
</script></body></html>
";
    }
}
