(() => {
'use strict';

const CONFIG = window.CONFIG;   // config/config.js
const AN = window.ANATOMY;      // config/anatomy.js
const CASES = window.CASES;     // cases/*.js

const TOOLS = [
  { id: 'wash',    name: 'Wash',    verb: 'Hold',  color: '#4fc3f7', radius: () => CONFIG.washRadius },
  { id: 'cautery', name: 'Cautery', verb: 'Hold',  color: '#ffb347', radius: () => CONFIG.cauteryContactRadius },
  { id: 'banding', name: 'Banding', verb: 'Tap',   color: '#b69cff', radius: () => CONFIG.bandRadius },
];

/* =====================================================================
   Geometry. The tract is one tube along depth w: oesophagus, gastro-oesophageal
   junction, stomach, pylorus, then the first part of the duodenum (D1); see
   config/anatomy.js. A wall point (theta, w) projects to radius S*R/(w-z) from
   the picture centre. The instrument tip is fixed on screen, so advancing or
   withdrawing slides tissue radially past it, and steering (left/right) rotates
   the tip round the wall like a clock hand while the camera stays steady:
   reaching a lesion needs both. The tip rides a fixed-radius circle on screen,
   so it touches the wall of every region (narrow oesophagus, wide stomach) at
   the depth where that wall projects onto the circle.
   ===================================================================== */
const W = 640, CX = 320, CY = 320, RV = 314, S = 230, DELTA = 0.25, NEAR = 0.2;
const WMAX = AN.end, ZMAX = AN.scopeMax;
const NA = 36, NB = Math.ceil(WMAX / DELTA), TAU = Math.PI * 2, DEG = Math.PI / 180;
const TIP_R = 135, TIP = { x: 0, y: 0 };                   // instrument tip: rides a circle round the picture centre
let tipAng = 60 * DEG, tipVel = 0, probeAng = 60 * DEG;       // tip clock angle; the shaft follows with a lag
const angDiff = (a, b) => Math.atan2(Math.sin(a - b), Math.cos(a - b));
function setTip() { TIP.x = CX + Math.cos(tipAng) * TIP_R; TIP.y = CY + Math.sin(tipAng) * TIP_R; }

const clamp = (x, a, b) => Math.max(a, Math.min(b, x));
const lerp = (a, b, t) => a + (b - a) * t;
const smooth = (a, b, x) => { const t = clamp((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t); };
function hash(a, b) { let h = (Math.imul(a | 0, 374761393) + Math.imul(b | 0, 668265263)) | 0; h = Math.imul(h ^ (h >>> 13), 1274126177); return ((h ^ (h >>> 16)) >>> 0) / 4294967295; }
function tab(t, w) {                                             // smooth piecewise lookup in a [[w, value], ...] table
  if (w <= t[0][0]) return t[0][1];
  for (let i = 1; i < t.length; i++) if (w <= t[i][0]) return lerp(t[i - 1][1], t[i][1], smooth(t[i - 1][0], t[i][0], w));
  return t[t.length - 1][1];
}

const Rof = w => tab(AN.radius, w);
const starAmp = w => 0.24 * (1 - smooth(AN.zline - 1.0, AN.zline + 0.1, w));   // the oesophagus is a collapsed, star-shaped lumen
const C5 = []; for (let a = 0; a <= NA; a++) C5.push(Math.cos(5 * a * TAU / NA));
const Rat = (th, w) => Rof(w) * (1 - starAmp(w) * 0.5 * (1 + Math.cos(5 * th)));
const pxOf = w => tab(AN.pathX, w), pyOf = w => tab(AN.pathY, w);
const bright = d => clamp(1.3 / (1 + 0.18 * d * d), 0.06, 1);
const regionAt = w => AN.regions.find(r => w < r.to) || AN.regions[AN.regions.length - 1];
const cmAt = w => Math.round(tab(AN.cm, w));
const mix3 = (a, b, f) => [lerp(a[0], b[0], f), lerp(a[1], b[1], f), lerp(a[2], b[2], f)];
function wallColour(w, a) {                                      // base wall colour by region; the Z-line is jagged
  const P = AN.palette, ww = Math.abs(w - AN.zline) < 0.5 ? w + 0.7 * (hash(a, 77) - 0.5) : w;
  for (let i = 0; i < P.length; i++) if (ww < P[i].to) {
    const nx = P[i + 1]; return nx && !P[i].sharp ? mix3(P[i].c, nx.c, smooth(P[i].to - 0.6, P[i].to, ww)) : P[i].c;
  }
  return P[P.length - 1].c;
}
function texture(a, w) {                                         // longitudinal fold shading: oesophageal ridges, gastric rugae, smooth duodenum
  const th = (a + 0.5) * TAU / NA;
  if (w < AN.zline - 0.2) return 1 + 0.06 * Math.cos(5 * th);
  if (w < 14.6) return 1 + 0.15 * (0.6 * Math.sin(7 * th + 0.5 * Math.sin(w * 0.7)) + 0.4 * Math.sin(3 * th + 1.2));
  if (w < 18.4) return 1 + 0.06 * Math.sin(6 * th + w);
  return 1 + 0.025 * Math.sin(9 * th);
}

const cam = { z: 0, px: 0, py: 0, ox: 0 };
function setCam() { cam.z = z; cam.ox = 0; cam.px = pxOf(z) + 0.012 * Math.sin(clock * 1.3); cam.py = pyOf(z) + 0.01 * Math.sin(clock * 0.93 + 1); }
const P = { x: 0, y: 0, d: 0 };
function proj(th, w, R) {
  const d = w - cam.z, k = S / d; if (R === undefined) R = Rat(th, w);
  P.x = CX + cam.ox + ((pxOf(w) - cam.px) + R * Math.cos(th)) * k;
  P.y = CY + ((pyOf(w) - cam.py) + R * Math.sin(th)) * k; P.d = d; return P;
}
const COS = [], SIN = [];
for (let a = 0; a <= NA; a++) { COS.push(Math.cos(a * TAU / NA)); SIN.push(Math.sin(a * TAU / NA)); }
const FOLD_BY_K = new Map();
AN.folds.forEach(f => { const k = Math.floor(f.w / DELTA); (FOLD_BY_K.get(k) || FOLD_BY_K.set(k, []).get(k)).push(f); });

/* =====================================================================
   State
   ===================================================================== */
let state = 'intro';                    // intro | play | paused | ended
let CASE, LES, LES_TH, SRC_J = 0, SRC_A = 0, caseIdx = 0, bandsLeft = 0, landmarkSeen = new Set(), controlledBy = null;
let t = 0, clock = 0, z = 0, v = 0, toolIdx = 0, loss = 0;
let bleeding = true, controlledAt = null, cauteryProg = 0, bandCool = 0, offAccum = 0, bloodAcc = 0, sampleAcc = 0;
let stats, events, samples, burns, seg, fx, overshootActive, clearView, lastCue = {};
const input = { up: false, down: false, left: false, right: false, use: false, armed: false };
const tapHold = { up: 0, down: 0, left: 0, right: 0 };   // a very quick tap still moves the scope a little (min. 90 ms of motion)

const amt = new Float32Array(NB * NA), fr = new Float32Array(NB * NA);
const dAmt = new Float32Array(NB * NA), dMass = new Float32Array(NB * NA);

function freshStats() {
  return { washTime: 0, washOnSrc: 0, washRevealed: false, washAfterControl: 0, cauteryTime: 0, cauteryOn: 0, cauteryOff: 0,
    overtreat: 0, firstCautery: null, bandAttempts: 0, bandOnTarget: 0, overshoots: 0, tFound: null, toolSwitches: 0,
    toolsUsed: {}, bestClearView: 0, sawLimit: 0, cauteryWrong: 0, bandsUsed: 0, bandsWasted: 0 };
}

/* =====================================================================
   DOM
   ===================================================================== */
const $ = id => document.getElementById(id);
const cv = $('scope'), ctx = cv.getContext('2d');
let scale = 1;
function resize() {
  const r = cv.getBoundingClientRect(), dpr = Math.min(window.devicePixelRatio || 1, 2);
  const px = Math.max(200, Math.round(r.width * dpr));
  if (cv.width !== px) { cv.width = px; cv.height = px; }
  scale = cv.width / W;
}
new ResizeObserver(resize).observe(cv); resize();
const touchUI = matchMedia('(pointer: coarse)').matches;
if (/[?&]pad=1/.test(location.search)) document.body.classList.add('forcepad');

/* =====================================================================
   Blood simulation (on the wall: depth bin x angle bin)
   ===================================================================== */
function addB(j, a, m, f) {
  if (j < 0 || j >= NB) return; a = (a + NA) % NA; const i = j * NA + a;
  const tot = amt[i] + m; fr[i] = (fr[i] * amt[i] + f * m) / (tot || 1); amt[i] = Math.min(1.8, tot);
}
function bloodTick(dt) {
  if (bleeding) {
    const e = (CASE.emit ?? CONFIG.emitPerSec) * dt * (0.8 + 0.2 * Math.sin(t * 7.2));
    addB(SRC_J, SRC_A, e * 0.5, 1);
    addB(SRC_J, SRC_A + 1, e * 0.125, 1); addB(SRC_J, SRC_A - 1, e * 0.125, 1);
    addB(SRC_J + 1, SRC_A, e * 0.125, 1); addB(SRC_J - 1, SRC_A, e * 0.125, 1);
  }
  dAmt.fill(0); dMass.fill(0);
  for (let j = 0; j < NB; j++) for (let a = 0; a < NA; a++) {
    const i = j * NA + a, m = amt[i]; if (m < 0.001) continue;
    const da = Math.min((a - SRC_A + NA) % NA, (SRC_A - a + NA) % NA);
    const thr = CONFIG.flowBase + CONFIG.flowPerCell * Math.max(Math.abs(j - SRC_J), da);
    if (m <= thr) continue;
    const ex = (m - thr) * 0.30;
    // gravity pulls blood to the dependent wall (bottom of the view, bins 8/9) and back along the lumen
    let dir = 0; if (a < 8) dir = 1; else if (a > 9) dir = (a <= 27) ? -1 : 1;
    const prox = dir === 0 ? ex : ex * 0.55, ang = ex - prox;
    const f = fr[i];
    dAmt[i] -= ex; dMass[i] -= ex * f;
    const jt = j + (CASE.flowDir || -1);
    if (jt >= 0 && jt < NB) { const k = jt * NA + a; dAmt[k] += prox; dMass[k] += prox * f; }   // off either end of the tract it drains away
    if (ang > 0) { const k = j * NA + ((a + dir + NA) % NA); dAmt[k] += ang; dMass[k] += ang * f; }
  }
  for (let i = 0; i < amt.length; i++) {
    if (dAmt[i] === 0 && fr[i] <= 0) continue;
    const mass = amt[i] * fr[i] + dMass[i], na = clamp(amt[i] + dAmt[i], 0, 1.8);
    amt[i] = na; fr[i] = na > 1e-4 ? clamp(mass / na, 0, 1) : 0;
  }
  for (let i = 0; i < fr.length; i++) if (fr[i] > 0) fr[i] = Math.max(0, fr[i] - 0.0075);  // fresh -> dark with age
}
function cellCentre(j, a) { return [(a + 0.5) * TAU / NA, (j + 0.5) * DELTA]; }
function coverAtLesion() {
  // blood thickness over the crater: the thickest cell whose centre lies within ~1.25 lesion radii of the lesion centre (world distance)
  let m = 0; const R = Rat(LES_TH, LES.w), lim = Math.max(0.2, LES.radius * 1.25);
  for (let j = Math.max(0, SRC_J - 2); j <= Math.min(NB - 1, SRC_J + 2); j++) for (let da = -3; da <= 3; da++) {
    const a = (SRC_A + da + NA) % NA, [th, w] = cellCentre(j, a);
    let dth = Math.abs(th - LES_TH); if (dth > Math.PI) dth = TAU - dth;
    if (Math.hypot(R * dth, w - LES.w) <= lim) m = Math.max(m, amt[j * NA + a]);
  }
  return m;
}
function totalBlood() { let s = 0; for (let i = 0; i < amt.length; i++) s += amt[i]; return s; }
function washClear(dt) {
  const R = CONFIG.washRadius;
  for (let j = 0; j < NB; j++) {
    const w = (j + 0.5) * DELTA, d = w - cam.z; if (d < 0.3 || d > 4) continue;
    for (let a = 0; a < NA; a++) {
      const i = j * NA + a; if (amt[i] <= 0.002) continue;
      const p = proj((a + 0.5) * TAU / NA, w), dist = Math.hypot(p.x - TIP.x, p.y - TIP.y);
      if (dist > R) continue;
      const f = 1 - (dist / R) * (dist / R) * 0.7;
      amt[i] = Math.max(0, amt[i] - CONFIG.washClearRate * f * dt);
    }
  }
}

/* =====================================================================
   Reset / events
   ===================================================================== */
function ev(kind, text) { events.push({ t: +t.toFixed(2), kind, text }); }
function setCase(i) {
  caseIdx = (i + CASES.length) % CASES.length; CASE = CASES[caseIdx]; LES = CASE.lesion; LES_TH = LES.deg * DEG;
  SRC_J = Math.floor(LES.w / DELTA); SRC_A = Math.floor((((LES.deg % 360) + 360) % 360) / (360 / NA));
  const V = CASE.varices;                                        // the ruptured cord runs through the lesion
  if (V) V.cords.forEach((cd, n) => { cd.th0 = n === V.bleed ? LES_TH - 0.16 * Math.sin(1.7 * (LES.w - V.w0) + cd.ph) : cd.deg * DEG; });
}
function buildPills() {
  const html = CASES.map((c, n) => `<button class="pill${n === caseIdx ? ' on' : ''}" data-case="${n}">${n + 1} · ${c.short}</button>`).join('');
  $('casePills').innerHTML = html; $('dbPills').innerHTML = html;
}
function reset(startNow) {
  t = 0; z = CASE.start.z; v = 0; tipAng = (CASE.start.tip ?? 60) * DEG; tipVel = 0; probeAng = tipAng; setTip(); toolIdx = 0; loss = 0; bleeding = true; controlledAt = null; controlledBy = null; cauteryProg = 0; bandCool = 0; offAccum = 0;
  bandsLeft = CONFIG.bandsLoaded; landmarkSeen = new Set();
  bloodAcc = 0; sampleAcc = 0; stats = freshStats(); events = []; samples = []; burns = []; seg = null; overshootActive = false; clearView = 0; lastCue = {};
  fx = { washing: false, cauteryOn: false, aligned: false, bandFlash: 0, bandOk: false };
  amt.fill(0); fr.fill(0); input.armed = false;
  for (let i = 0; i < CONFIG.trailWarmupTicks; i++) { t = i * 0.1; bloodTick(0.1); }        // give the case a trail before the first frame
  t = 0; setCam();
  ev('start', `Scope in the ${regionAt(z).name.toLowerCase()} (${cmAt(z)} cm); fresh blood trail ahead`);
  ev('tool', 'Wash selected');
  $('debrief').hidden = true; $('pauseOv').hidden = true;
  state = startNow ? 'play' : 'intro'; $('intro').hidden = !!startNow;
  $('introTitle').textContent = `Case ${caseIdx + 1}: ${CASE.title}`; $('introWhere').textContent = CASE.where; $('introText').textContent = CASE.vignette;
  $('caseSub').textContent = `Case ${caseIdx + 1} of ${CASES.length} · ${CASE.title} · fictional patient`;
  buildPills(); hideCue(); updateToolUI(); updateHUD(); $('pauseBtn').textContent = 'Pause (P)';
}
function loadCase(i, startNow) { setCase(i); reset(startNow); }

/* =====================================================================
   Input
   ===================================================================== */
function toolIs(id) { return TOOLS[toolIdx].id === id; }
function useDown() {
  if (input.use) return; input.use = true; input.armed = true;
  if (state === 'play' && toolIs('banding')) attemptBand();
}
function useUp() { input.use = false; input.armed = false; }
function changeTool(dir) {
  if (state !== 'play') return;
  toolIdx = (toolIdx + dir + TOOLS.length) % TOOLS.length;
  input.armed = false;                       // holding Space across a switch does nothing until Space is pressed again
  stats.toolSwitches++; ev('tool', TOOLS[toolIdx].name + ' selected'); updateToolUI(); closeSeg();
}
const KEY = { ArrowUp: 'up', ArrowDown: 'down', ArrowLeft: 'left', ArrowRight: 'right', Tab: 'tab' };
function keyName(e) { return e.code === 'Space' || e.key === ' ' ? 'use' : KEY[e.key] || null; }
window.addEventListener('keydown', e => {
  if (e.metaKey || e.ctrlKey || e.altKey) return;
  const k = keyName(e);
  if (k === 'tab') {                                              // Tab cycles attachments while playing (Shift+Tab goes back); elsewhere it keeps normal focus behaviour
    if (state !== 'play') return;
    e.preventDefault(); if (!e.repeat) changeTool(e.shiftKey ? -1 : 1); return;
  }
  if (k) {
    if (e.target && e.target.type === 'range' && state === 'ended') return;
    e.preventDefault();
    if (state === 'intro') { if (k === 'use' && !e.repeat) { start(); input.use = true; input.armed = false; } return; }
    if (k === 'up' || k === 'down' || k === 'left' || k === 'right') { input[k] = true; if (!e.repeat) tapHold[k] = 0.09; }
    else if (e.repeat) return;
    else if (k === 'use') { if (state === 'play') useDown(); else input.use = true; }
    return;
  }
  if (e.key === 'Enter') { e.preventDefault(); if (e.repeat) return;
    if (state === 'intro') start(); else if (state === 'play') signOff(); else if (state === 'ended') reset(true); }
  else if ((e.key === 'p' || e.key === 'P' || e.key === 'Escape') && !e.repeat) { if (state === 'play' || state === 'paused') togglePause(); }
  else if ((e.key === 'r' || e.key === 'R') && !e.repeat && state === 'ended') reset(true);
  else if ((e.key === 'n' || e.key === 'N') && !e.repeat && state === 'ended') loadCase(caseIdx + 1, true);
  else if (/^[1-9]$/.test(e.key) && +e.key <= CASES.length && (state === 'intro' || state === 'ended')) loadCase(+e.key - 1, state === 'ended');
}, { passive: false });
document.addEventListener('click', e => {
  const b = e.target.closest && e.target.closest('.pill'); if (!b || (state !== 'intro' && state !== 'ended')) return;
  loadCase(+b.dataset.case, state === 'ended'); b.blur();
});
window.addEventListener('keyup', e => {
  const k = keyName(e); if (!k) return; e.preventDefault();
  if (k === 'tab') return;
  if (k === 'use') useUp(); else if (k === 'up' || k === 'down' || k === 'left' || k === 'right') input[k] = false;
}, { passive: false });
window.addEventListener('blur', () => { input.up = input.down = input.left = input.right = false; useUp(); });
if (!/[?&]nopause=1/.test(location.search))                      // auto-pause when the tab is hidden (?nopause=1 disables, for testing)
  document.addEventListener('visibilitychange', () => { if (document.hidden && state === 'play') togglePause(); });

function bindHold(el, down, up) {
  el.addEventListener('pointerdown', e => { e.preventDefault(); try { el.setPointerCapture(e.pointerId); } catch (_) {} el.classList.add('on'); down(); });
  ['pointerup', 'pointercancel', 'lostpointercapture'].forEach(n => el.addEventListener(n, () => { if (!el.classList.contains('on')) return; el.classList.remove('on'); up(); }));
  el.addEventListener('contextmenu', e => e.preventDefault());
}
document.querySelectorAll('.pad button').forEach(b => {
  const a = b.dataset.act;
  if (a === 'up' || a === 'down' || a === 'left' || a === 'right') bindHold(b, () => { if (state === 'play') { input[a] = true; tapHold[a] = 0.09; } }, () => { input[a] = false; });
  else if (a === 'use') bindHold(b, () => { if (state === 'play') useDown(); }, useUp);
  else bindHold(b, () => changeTool(a === 'prev' ? -1 : 1), () => {});
});
$('prevTool').addEventListener('click', e => { changeTool(-1); e.currentTarget.blur(); });
$('nextTool').addEventListener('click', e => { changeTool(1); e.currentTarget.blur(); });
document.querySelectorAll('.chip').forEach(c => c.addEventListener('click', () => {
  const i = +c.dataset.i; if (state !== 'play' || i === toolIdx) return; changeTool(i - toolIdx);
}));
function start() { if (state !== 'intro') return; state = 'play'; $('intro').hidden = true; }
$('startBtn').addEventListener('click', e => { start(); e.currentTarget.blur(); });
$('pauseBtn').addEventListener('click', e => { togglePause(); e.currentTarget.blur(); });
$('resumeBtn').addEventListener('click', e => { togglePause(); e.currentTarget.blur(); });
$('signBtn').addEventListener('click', e => { signOff(); e.currentTarget.blur(); });
$('retryBtn').addEventListener('click', e => { reset(true); e.currentTarget.blur(); });
$('nextBtn').addEventListener('click', e => { loadCase(caseIdx + 1, true); e.currentTarget.blur(); });
function togglePause() {
  if (state === 'play') { state = 'paused'; $('pauseOv').hidden = false; $('pauseBtn').textContent = 'Resume (P)'; closeSeg(); }
  else if (state === 'paused') { state = 'play'; $('pauseOv').hidden = true; $('pauseBtn').textContent = 'Pause (P)'; input.armed = false; }
}

/* =====================================================================
   Cues + HUD
   ===================================================================== */
let cueTimer = 0;
function showCue(text, kind, ms = 2600) {
  const c = $('cue'); c.textContent = text; c.className = 'cue show ' + (kind || '');
  clearTimeout(cueTimer); cueTimer = setTimeout(hideCue, ms);
}
function hideCue() { clearTimeout(cueTimer); $('cue').className = 'cue'; }
function cueOnce(key, gap, text, kind, ms) { if (t - (lastCue[key] ?? -1e9) < gap) return; lastCue[key] = t; showCue(text, kind, ms); }

function updateToolUI() {
  const tl = TOOLS[toolIdx];
  document.querySelectorAll('.chip').forEach(c => c.classList.toggle('sel', +c.dataset.i === toolIdx));
  const L = $('toolLabel'); L.style.color = tl.color;
  L.textContent = tl.name.toUpperCase() + ' · ' + tl.verb + ' ' + (touchUI ? 'USE' : 'Space') + (tl.id === 'banding' && CASE.suitable.banding ? ` · ${bandsLeft} band${bandsLeft === 1 ? '' : 's'} left` : '');
}
const mmss = s => Math.floor(s / 60) + ':' + String(Math.floor(s % 60)).padStart(2, '0');
function vitals() {
  const hr = Math.round(76 + loss * 0.058 + (bleeding ? 3 : 0)), sbp = Math.round(124 - loss * 0.043);
  return { hr, sbp, dbp: Math.round(sbp * 0.62) };
}
function updateHUD() {
  const vs = vitals();
  $('vTime').lastChild.textContent = mmss(t);
  $('vHR').lastChild.textContent = vs.hr; $('vHR').className = 'vit ' + (vs.hr > 130 ? 'bad' : vs.hr > 105 ? 'warn' : '');
  $('vBP').lastChild.textContent = vs.sbp + '/' + vs.dbp; $('vBP').className = 'vit ' + (vs.sbp < 80 ? 'bad' : vs.sbp < 100 ? 'warn' : '');
  $('vLoss').lastChild.textContent = Math.round(loss) + ' mL'; $('vLoss').className = 'vit ' + (loss > 1000 ? 'bad' : loss > 500 ? 'warn' : '');
}

/* =====================================================================
   Simulation step
   ===================================================================== */
function lesionScreen() {
  const d = LES.w - z; if (d < 0.3) return { vis: false, d, x: 0, y: 0 };
  const p = proj(LES_TH, LES.w, Rof(LES.w));
  return { vis: Math.hypot(p.x - CX, p.y - CY) < RV - 12, d, x: p.x, y: p.y };
}
function wallPointUnderTip() {                  // where the tip's line of sight meets the wall (solve |lateral| = R at depth d)
  const u = (TIP.x - CX - cam.ox) / S, vv = (TIP.y - CY) / S, h = Math.hypot(u, vv); let w = z + 1.3;
  for (let i = 0; i < 4; i++) w = z + Rof(w) / h;
  return { th: Math.atan2(vv, u), w };
}
function closeSeg() {
  if (!seg) return;
  const d = seg.dur;
  if (d > 0.15) {
    if (seg.tool === 'wash') ev('wash', `Wash ${d.toFixed(1)} s, ${seg.on.toFixed(1)} s on the source`, seg.t0);
    else ev('cautery', `Cautery ${d.toFixed(1)} s, ${seg.on.toFixed(1)} s on target` + (seg.off > 0.2 ? `, ${seg.off.toFixed(1)} s off target` : ''), seg.t0);
    events[events.length - 1].t = +seg.t0.toFixed(2);
  }
  seg = null;
}
function attemptBand() {
  if (bandCool > 0) return;
  const L = lesionScreen(), on = L.vis && Math.hypot(L.x - TIP.x, L.y - TIP.y) <= CONFIG.bandRadius + 8;
  if (CASE.suitable.banding) {
    if (bandsLeft <= 0) { showCue('No bands left on the cap.', 'warn'); return; }
    bandCool = CONFIG.bandCooldown; stats.bandAttempts++; fx.bandFlash = 0.6;
    if (on && !bleeding) { showCue('That varix is already banded.', ''); ev('band', 'Extra band attempted on an already banded varix'); return; }
    bandsLeft--; updateToolUI();
    if (on) { stats.bandsUsed++; stats.bandOnTarget++; fx.bandOk = true; control('banding'); }
    else { stats.bandsWasted++; addBurn('band'); ev('band', 'Band fired away from the varix: wasted'); showCue(CASE.cues.bandingMiss, 'warn', 3000); }
    return;
  }
  bandCool = CONFIG.bandCooldown; stats.bandAttempts++; fx.bandFlash = 0.6; fx.bandOk = false;
  if (on) stats.bandOnTarget++;
  ev('band', on ? 'Banding attempted on the ulcer: not capturable' : 'Banding attempted away from the lesion');
  showCue(on ? CASE.cues.banding : CASE.cues.bandingMiss, 'warn', 3200);
}
function control(by) {
  bleeding = false; controlledAt = t; controlledBy = by; cauteryProg = 0;
  ev('control', `Bleeding controlled by ${by}`);
  showCue(by === 'banding' ? 'Band deployed: the varix is strangulated and the bleeding has stopped.' : 'Contact complete. Fresh flow has stopped.', 'good', 3200);
}
function addBurn(kind) {
  const p = wallPointUnderTip(); burns.push({ kind: kind || 'burn', th: p.th + (hash(burns.length, 2) - 0.5) * 0.05, w: p.w }); if (burns.length > 14) burns.shift();
}

function update(dt) {
  clock += dt; if (state !== 'play') return;
  t += dt;
  // --- scope motion
  const upOn = input.up || tapHold.up > 0, downOn = input.down || tapHold.down > 0;
  tapHold.up = Math.max(0, tapHold.up - dt); tapHold.down = Math.max(0, tapHold.down - dt);
  const tgt = (upOn ? 1 : 0) - (downOn ? 1 : 0), dv = CONFIG.scopeAccel * dt;
  v += clamp(tgt * CONFIG.scopeSpeed - v, -dv, dv);
  let nz = z + v * dt;
  if (nz <= 0) { nz = 0; v = 0; if (tgt < 0) cueOnce('lim0', 3, 'Upper oesophagus (20 cm): this simulation goes no higher.', '', 1800); }
  else if (nz >= ZMAX) { nz = ZMAX; v = 0; if (tgt > 0) cueOnce('limZ', 3, 'End of D1 (superior duodenal flexure). The scope cannot go beyond the first part of the duodenum.', '', 2600); }
  z = nz;
  const lOn = input.left || tapHold.left > 0, rOn = input.right || tapHold.right > 0, da = CONFIG.tipAccel * dt;
  tapHold.left = Math.max(0, tapHold.left - dt); tapHold.right = Math.max(0, tapHold.right - dt);
  const tgtS = (rOn ? 1 : 0) - (lOn ? 1 : 0);                    // → turns the tip clockwise round the wall, ← anticlockwise
  tipVel += clamp(tgtS * CONFIG.tipSpeed - tipVel, -da, da);
  tipAng = (tipAng + tipVel * dt + TAU) % TAU;
  probeAng += angDiff(tipAng, probeAng) * (1 - Math.exp(-dt / CONFIG.probeLag));   // the tip twists first; the whole probe follows
  setTip(); setCam();
  for (const lm of AN.landmarks) if (!landmarkSeen.has(lm.name) && Math.abs(z - lm.w) < 0.3) {
    landmarkSeen.add(lm.name); ev('landmark', lm.name); cueOnce('lm', 1.5, `${lm.name} · ${cmAt(lm.w)} cm`, '', 1700);
  }

  const L = lesionScreen(), dist = L.vis ? Math.hypot(L.x - TIP.x, L.y - TIP.y) : Infinity;
  const aimed = dist <= CONFIG.cauteryContactRadius;
  if (stats.tFound === null && dist <= CONFIG.cauteryContactRadius * 1.6) { stats.tFound = t; ev('found', 'Source brought under the instrument tip'); }
  const d = LES.w - z;
  if (!overshootActive && d < 0.55) { overshootActive = true; stats.overshoots++; ev('overshoot', 'Passed the lesion'); cueOnce('over', 2, 'Passed the lesion. Withdraw (↓) to find it again.', 'warn', 2600); }
  else if (overshootActive && d > 1.1) overshootActive = false;

  // --- attachments
  const active = input.use && input.armed, tool = TOOLS[toolIdx].id;
  fx.washing = fx.cauteryOn = false; bandCool = Math.max(0, bandCool - dt); fx.bandFlash = Math.max(0, fx.bandFlash - dt);
  const cover = coverAtLesion(), cur = active && (tool === 'wash' || tool === 'cautery') ? tool : null;
  if (!seg || seg.tool !== cur) { closeSeg(); if (cur) seg = { tool: cur, t0: t, dur: 0, on: 0, off: 0 }; }
  if (cur) { seg.dur += dt; stats.toolsUsed[cur] = true; }

  if (cur === 'wash') {
    fx.washing = true; stats.washTime += dt; washClear(dt);
    if (controlledAt !== null) stats.washAfterControl += dt;
    if (dist <= CONFIG.washRadius + 14) {
      stats.washOnSrc += dt; seg.on += dt;
      if (bleeding && cover < 0.55) {
        if (!stats.washRevealed) { stats.washRevealed = true; ev('reveal', 'Fresh blood from the ' + CASE.sourceName + ': active bleed confirmed'); }
        cueOnce('well', 5, CASE.cues.reveal, 'bad', 2600);
      }
    }
  } else if (cur === 'cautery') {
    fx.cauteryOn = true; stats.cauteryTime += dt; if (stats.firstCautery === null) stats.firstCautery = t;
    if (aimed && !CASE.suitable.cautery) {                      // e.g. a varix: thermal contact is the wrong treatment
      stats.cauteryWrong += dt; seg.off += dt; cueOnce('wrongc', 4, CASE.cues.cautery, 'bad', 3200);
    } else if (aimed) {
      stats.cauteryOn += dt; seg.on += dt;
      if (bleeding) { cauteryProg += dt; if (cauteryProg >= CONFIG.cauteryContactSec) control('cautery'); }
      else { stats.overtreat += dt; if (stats.overtreat > 2.5) cueOnce('over-t', 4, 'Enough: further cautery risks deep thermal injury.', 'warn', 2200); }
    } else {
      stats.cauteryOff += dt; seg.off += dt; offAccum += dt; cauteryProg = Math.max(0, cauteryProg - dt * CONFIG.cauteryDecay);
      if (offAccum > 0.5) { offAccum = 0; addBurn('burn'); }
      if (bleeding) cueOnce('off', 4, 'Cautery away from the lesion: it is still bleeding.', 'warn', 2200);
    }
  }
  if (cur !== 'cautery') cauteryProg = Math.max(0, cauteryProg - dt * CONFIG.cauteryDecay);
  fx.aligned = tool === 'wash' ? dist <= CONFIG.washRadius : tool === 'cautery' ? aimed : dist <= CONFIG.bandRadius;

  // --- blood + patient
  bloodAcc += dt; while (bloodAcc >= 0.1) { bloodTick(0.1); bloodAcc -= 0.1; }
  if (bleeding) loss += (CASE.bleedMl ?? CONFIG.bleedMlPerSec) * dt;

  // --- re-inspection timer: after control, is the site in view and free of blood?
  if (controlledAt !== null && L.vis && L.d > 0.8 && cover < CONFIG.clearCover) { clearView += dt; stats.bestClearView = Math.max(stats.bestClearView, clearView); }
  else clearView = 0;

  sampleAcc += dt; if (sampleAcc >= 0.2) { sampleAcc = 0; samples.push({ t: +t.toFixed(2), z: +z.toFixed(3), tool: toolIdx, use: cur ? 1 : 0, bleed: bleeding ? 1 : 0, on: aimed ? 1 : 0 }); }
  updateHUD();
  if (loss >= CONFIG.lossLimitMl) end('haemorrhage');
}

/* =====================================================================
   Rendering
   ===================================================================== */
function edge(w) { const d = w - cam.z, k = S / d; return { x: CX + cam.ox + (pxOf(w) - cam.px) * k, y: CY + (pyOf(w) - cam.py) * k, k, R: Rof(w), d, sa: starAmp(w) }; }
function ringPath(c, e) {                                        // outline of the lumen at depth e (a circle, or the collapsed oesophageal star)
  if (e.sa < 0.005) { c.moveTo(e.x + e.R * e.k, e.y); c.arc(e.x, e.y, e.R * e.k, 0, TAU); return; }
  for (let i = 0; i <= 72; i++) {
    const th = i / 72 * TAU, r = e.R * (1 - e.sa * 0.5 * (1 + Math.cos(5 * th))) * e.k, x = e.x + Math.cos(th) * r, y = e.y + Math.sin(th) * r;
    i ? c.lineTo(x, y) : c.moveTo(x, y);
  }
  c.closePath();
}
function patchPath(c, th, w, hth, hw, n, jit, seed) {
  c.beginPath();
  for (let i = 0; i < n; i++) {
    const a = i / n * TAU, m = 1 + jit * (hash(seed, i) - 0.5);
    const p = proj(th + Math.cos(a) * hth * m, w + Math.sin(a) * hw * m);
    if (p.d < 0.05) continue;
    i ? c.lineTo(p.x, p.y) : c.moveTo(p.x, p.y);
  }
  c.closePath();
}
function drawStrip(c, k) {
  const w0 = Math.max(k * DELTA, cam.z + NEAR), w1 = Math.min((k + 1) * DELTA + 0.03, WMAX); if (w1 <= w0 + 1e-3) return;   // +0.03 overlaps the farther strip so no seam shows
  const e0 = edge(w0), e1 = edge(w1), b = bright((w0 + w1) / 2 - cam.z);
  c.save();
  c.beginPath(); ringPath(c, e0); ringPath(c, e1); c.clip('evenodd');
  { const bc = wallColour((w0 + w1) / 2, 3); c.fillStyle = `rgb(${bc[0] * b * 0.95 | 0},${bc[1] * b * 0.95 | 0},${bc[2] * b * 0.95 | 0})`; c.fillRect(0, 0, W, W); }   // base coat so no gaps show between wedges
  const f0 = i => e0.R * (1 - e0.sa * 0.5 * (1 + C5[i])) * e0.k, f1 = i => e1.R * (1 - e1.sa * 0.5 * (1 + C5[i])) * e1.k;
  for (let a = 0; a < NA; a++) {
    const h = hash(k, a), wm = (w0 + w1) / 2, base = wallColour(wm, a);
    const nz = 0.5 * Math.sin(a * 0.85 + k * 0.41) + 0.5 * Math.sin(a * 2.3 - k * 0.77 + 1.3);       // smooth mottling, not a checkerboard
    const vv = (0.98 + 0.045 * nz + 0.03 * (h - 0.5)) * texture(a, wm);
    const col = `rgb(${Math.min(255, base[0] * b * vv) | 0},${Math.min(255, (base[1] + 8 * nz) * b * vv) | 0},${Math.min(255, base[2] * b * vv) | 0})`;
    c.fillStyle = col; c.strokeStyle = col; c.lineWidth = 1;
    const ax = e0.x + COS[a] * f0(a), ay = e0.y + SIN[a] * f0(a), bx = e0.x + COS[a + 1] * f0(a + 1), by = e0.y + SIN[a + 1] * f0(a + 1);
    const cx2 = e1.x + COS[a + 1] * f1(a + 1), cy2 = e1.y + SIN[a + 1] * f1(a + 1), dx = e1.x + COS[a] * f1(a), dy = e1.y + SIN[a] * f1(a);
    c.beginPath(); c.moveTo(ax, ay); c.lineTo(bx, by); c.lineTo(cx2, cy2); c.lineTo(dx, dy); c.closePath(); c.fill(); c.stroke();
    if (h > 0.93) {                                   // wet specular glints
      c.fillStyle = `rgba(255,235,225,${0.22 * b})`; c.beginPath(); c.ellipse((ax + cx2) / 2, (ay + cy2) / 2, 3 + 5 * e0.k / 100, 1.6 + 2.4 * e0.k / 100, 0, 0, TAU); c.fill();
    } else if (h < (wm < AN.zline ? 0.2 : 0.07)) {    // small vessels (more visible in the oesophagus)
      c.strokeStyle = `rgba(150,40,45,${0.35 * b})`; c.lineWidth = 1.2; c.beginPath(); c.moveTo(ax, ay); c.lineTo(dx, dy); c.stroke();
    }
  }
  const lo = k * DELTA, hi = (k + 1) * DELTA;
  if (CASE.varices) drawCords(c, w0, w1);
  if (hi > LES.w - 0.32 && lo < LES.w + 0.32) drawLesion(c);
  for (const bm of burns) if (bm.w + 0.1 > lo && bm.w - 0.1 < hi) drawBurn(c, bm);
  for (let j = Math.max(0, k - 1); j <= Math.min(NB - 1, k + 1); j++) for (let a = 0; a < NA; a++) { const i = j * NA + a; if (amt[i] > 0.03) drawBlob(c, j, a, i); }
  c.restore();
  const fl = FOLD_BY_K.get(k); if (fl) for (const f of fl) drawFold(c, f);
}
function drawCords(c, wa, wb) {                                  // oesophageal varices: blue, serpiginous columns running along the wall
  const V = CASE.varices, a = Math.max(wa, V.w0), z1 = Math.min(wb, V.w1); if (a >= z1) return;
  for (const cd of V.cords) for (let w = a; w < z1; w += 0.08) {
    const w2 = Math.min(w + 0.08, z1), th = cd.th0 + 0.16 * Math.sin(1.7 * (w - V.w0) + cd.ph), th2 = cd.th0 + 0.16 * Math.sin(1.7 * (w2 - V.w0) + cd.ph);
    const hw = 0.13 * (0.85 + 0.4 * smooth(V.w0, V.w1, w)) * (0.92 + 0.16 * Math.sin(w * 5 + cd.ph)), bb = bright(w - cam.z);
    const pts = [[th - hw, w], [th + hw, w], [th2 + hw, w2], [th2 - hw, w2]].map(q => { const p = proj(q[0], q[1]); return [p.x, p.y]; });
    c.beginPath(); pts.forEach((q, i) => i ? c.lineTo(q[0], q[1]) : c.moveTo(q[0], q[1])); c.closePath();
    c.fillStyle = `rgba(${(60 + 40 * bb) | 0},${(66 + 44 * bb) | 0},${(120 + 90 * bb) | 0},0.93)`; c.fill();
    const hp = [[th - hw * 0.45, w], [th - hw * 0.1, w], [th2 - hw * 0.1, w2], [th2 - hw * 0.45, w2]].map(q => { const p = proj(q[0], q[1]); return [p.x, p.y]; });
    c.beginPath(); hp.forEach((q, i) => i ? c.lineTo(q[0], q[1]) : c.moveTo(q[0], q[1])); c.closePath(); c.fillStyle = `rgba(170,185,255,${0.42 * bb})`; c.fill();
  }
}
function drawBlob(c, j, a, i) {
  const [th, w] = cellCentre(j, a), m = amt[i], f = fr[i], hth = (0.15 + 0.035 * Math.min(m, 1.2)) * 0.9 / Math.max(0.55, Math.min(1, Rof(w))) * Math.min(1, Rof(w) / 0.9 + 0.35), hw = 0.2 + 0.05 * Math.min(m, 1.2);
  if (w - cam.z < 0.12) return;
  patchPath(c, th + (hash(i, 31) - 0.5) * 0.05, w + (hash(i, 32) - 0.5) * 0.06, hth, hw, 12, 0.35, i);
  const al = (1 - Math.exp(-1.5 * m)) * 0.74, r = lerp(88, 228, f), g = lerp(14, 20, f), bl = lerp(18, 26, f);
  c.fillStyle = `rgba(${r | 0},${g | 0},${bl | 0},${al})`; c.fill();
  if (f > 0.55 && m > 0.8) { const p = proj(th - 0.03, w - 0.03); c.fillStyle = `rgba(255,190,190,${0.22 * f})`; c.beginPath(); c.ellipse(p.x, p.y, 2.5, 1.5, 0, 0, TAU); c.fill(); }
}
function drawLesion(c) {
  const r = LES.radius, R = Rat(LES_TH, LES.w), hth = r / R, pulse = 0.5 + 0.5 * Math.sin(clock * (LES.kind === 'varix' ? 10 : 7.2));
  if (LES.kind === 'varix') {
    if (bleeding) {                                              // ruptured varix: red wale on the cord, with a spurting point
      patchPath(c, LES_TH, LES.w, hth * 2.0, r * 2.0, 20, 0.25, 71); c.fillStyle = 'rgba(190,50,80,0.5)'; c.fill();
      patchPath(c, LES_TH, LES.w, hth * 1.15, r * 1.15, 18, 0.3, 72); c.fillStyle = 'rgb(196,40,62)'; c.fill();
      patchPath(c, LES_TH, LES.w, hth * 0.55, r * 0.55, 14, 0.3, 73); c.fillStyle = 'rgb(120,18,36)'; c.fill();
      const ph = (clock * 2.4) % 1;
      patchPath(c, LES_TH, LES.w, hth * (0.25 + 0.15 * pulse), r * (0.25 + 0.15 * pulse), 12, 0.2, 75); c.fillStyle = 'rgb(235,26,34)'; c.fill();
      patchPath(c, LES_TH, LES.w, hth * (0.3 + 1.3 * ph), r * (0.3 + 1.3 * ph), 20, 0.2, 76); c.strokeStyle = `rgba(235,30,40,${0.8 * (1 - ph)})`; c.lineWidth = 2.5; c.stroke();
      const a = proj(LES_TH, LES.w), x0 = a.x, y0 = a.y, b = proj(LES_TH + 0.1, LES.w - 0.45 * (0.5 + pulse)); c.strokeStyle = 'rgba(235,30,40,.85)'; c.lineWidth = 3; c.lineCap = 'round'; c.beginPath(); c.moveTo(x0, y0); c.lineTo(b.x, b.y); c.stroke();
    } else drawBand(c, LES_TH, LES.w, 1.0);                      // banded: strangulated dark nodule with the elastic band at its base
    return;
  }
  patchPath(c, LES_TH, LES.w, hth * 1.7, r * 1.7, 22, 0.25, 71); c.fillStyle = bleeding ? 'rgba(200,50,55,0.42)' : 'rgba(205,90,80,0.32)'; c.fill();     // hyperaemic edge
  if (LES.style === 'gastric') for (let i = 0; i < 9; i++) {      // folds converging on a gastric ulcer
    const an = i / 9 * TAU + 0.3, p1 = proj(LES_TH + Math.cos(an) * hth * 1.3, LES.w + Math.sin(an) * r * 1.3), x1 = p1.x, y1 = p1.y, p2 = proj(LES_TH + Math.cos(an) * hth * 2.8, LES.w + Math.sin(an) * r * 2.8);
    c.strokeStyle = 'rgba(240,160,145,.45)'; c.lineWidth = 4; c.lineCap = 'round'; c.beginPath(); c.moveTo(x1, y1); c.lineTo(p2.x, p2.y); c.stroke();
  }
  patchPath(c, LES_TH, LES.w, hth, r, 22, 0.3, 72);
  if (bleeding) { c.fillStyle = 'rgb(238,214,160)'; c.fill(); c.lineWidth = 2; c.strokeStyle = 'rgba(120,35,35,.8)'; c.stroke(); }
  else { c.fillStyle = 'rgb(214,190,150)'; c.fill(); c.lineWidth = 2; c.strokeStyle = 'rgba(120,70,50,.8)'; c.stroke(); }
  patchPath(c, LES_TH, LES.w, hth * 0.55, r * 0.55, 16, 0.3, 73);
  c.fillStyle = bleeding ? 'rgb(206,160,110)' : 'rgb(78,52,40)'; c.fill();                        // base / eschar after cautery
  if (!bleeding) { patchPath(c, LES_TH, LES.w, hth * 0.3, r * 0.3, 12, 0.3, 74); c.fillStyle = 'rgb(38,26,22)'; c.fill(); }
  if (bleeding) {                                                                                    // welling point + ripple
    const ph = (clock * 1.7) % 1;
    patchPath(c, LES_TH, LES.w, hth * (0.22 + 0.1 * pulse), r * (0.22 + 0.1 * pulse), 14, 0.2, 75); c.fillStyle = 'rgb(225,22,28)'; c.fill();
    patchPath(c, LES_TH, LES.w, hth * (0.3 + 0.9 * ph), r * (0.3 + 0.9 * ph), 20, 0.2, 76); c.strokeStyle = `rgba(230,30,35,${0.7 * (1 - ph)})`; c.lineWidth = 2; c.stroke();
  }
}
function drawBand(c, th, w, sc) {                                // a banded nodule: purple-black bulge with a tan elastic ring
  const R = Rat(th, w), r = 0.15 * sc, hth = r / R;
  patchPath(c, th, w, hth * 1.5, r * 1.5, 18, 0.2, 81); c.fillStyle = 'rgb(112,52,100)'; c.fill();
  patchPath(c, th, w, hth * 1.0, r * 1.0, 18, 0.2, 82); c.fillStyle = 'rgb(54,20,50)'; c.fill();
  patchPath(c, th, w, hth * 1.5, r * 1.5, 18, 0.1, 83); c.strokeStyle = 'rgba(196,172,120,.95)'; c.lineWidth = 3; c.stroke();
  const p = proj(th - hth * 0.3, w - r * 0.3); c.fillStyle = 'rgba(255,200,230,.35)'; c.beginPath(); c.ellipse(p.x, p.y, 3, 2, 0, 0, TAU); c.fill();
}
function drawBurn(c, bm) {
  if (bm.kind === 'band') { drawBand(c, bm.th, bm.w, 0.8); return; }
  patchPath(c, bm.th, bm.w, 0.12, 0.10, 10, 0.4, 91); c.fillStyle = 'rgba(225,200,170,0.55)'; c.fill();
  patchPath(c, bm.th, bm.w, 0.07, 0.06, 10, 0.4, 92); c.fillStyle = 'rgba(60,36,28,0.9)'; c.fill();
}
function drawFold(c, f) {
  const d = f.w - cam.z; if (d < 0.22) return;
  const e = edge(f.w), b = bright(d), a0 = f.a0 * DEG, sw = f.sweep * DEG, n = f.sweep > 350 ? 48 : 22, wc = wallColour(f.w, 5);
  c.beginPath();
  for (let i = 0; i <= n; i++) { const th = a0 + sw * i / n; const x = e.x + e.R * Math.cos(th) * e.k, y = e.y + e.R * Math.sin(th) * e.k; i ? c.lineTo(x, y) : c.moveTo(x, y); }
  for (let i = n; i >= 0; i--) {
    const u = i / n, th = a0 + sw * u, pr = f.uniform ? f.prot : f.prot * Math.pow(Math.sin(Math.PI * u), 0.7), rr = e.R * (1 - pr) * e.k;
    c.lineTo(e.x + Math.cos(th) * rr, e.y + Math.sin(th) * rr);
  }
  c.closePath();
  c.fillStyle = `rgb(${Math.min(255, wc[0] * 1.1 * b) | 0},${Math.min(255, wc[1] * 1.12 * b) | 0},${Math.min(255, wc[2] * 1.1 * b) | 0})`; c.fill();
  c.lineJoin = 'round'; c.strokeStyle = `rgba(255,205,185,${0.7 * b})`; c.lineWidth = Math.max(1.2, 2.4 * Math.min(2, e.k / 90)); c.stroke();
  if (f.rosette) for (let i = 0; i < 9; i++) {                    // pyloric rosette: folds converging on the opening
    const th = a0 + i / 9 * TAU, r1 = e.R * (1 - f.prot) * e.k, r2 = e.R * 0.97 * e.k;
    c.strokeStyle = `rgba(120,40,40,${0.5 * b})`; c.lineWidth = Math.max(1, 2 * Math.min(2, e.k / 90)); c.beginPath(); c.moveTo(e.x + Math.cos(th) * r1, e.y + Math.sin(th) * r1); c.lineTo(e.x + Math.cos(th) * r2, e.y + Math.sin(th) * r2); c.stroke();
  }
}
function drawEndCap(c) {                                         // D1 ends at the superior duodenal flexure: the lumen turns away out of sight
  const d = WMAX - cam.z; if (d > 9.5) return;
  const e = edge(WMAX), b = bright(d), base = AN.palette[AN.palette.length - 1].c, rad = e.R * e.k;
  c.fillStyle = `rgb(${base[0] * b * 0.9 | 0},${base[1] * b * 0.9 | 0},${base[2] * b * 0.9 | 0})`; c.beginPath(); c.arc(e.x, e.y, rad, 0, TAU); c.fill();
  c.fillStyle = 'rgba(14,4,4,.7)'; c.beginPath(); c.ellipse(e.x + rad * 0.32, e.y - rad * 0.12, rad * 0.5, rad * 0.62, 0.3, 0, TAU); c.fill();
}
function render() {
  const c = ctx; c.setTransform(scale, 0, 0, scale, 0, 0);
  c.fillStyle = '#060304'; c.fillRect(0, 0, W, W);
  setCam(); drawEndCap(c);
  const kNear = Math.max(0, Math.floor((cam.z + NEAR) / DELTA)), kFar = Math.min(NB - 1, Math.floor((Math.min(cam.z + 8.5, WMAX) - 1e-6) / DELTA));
  for (let k = kFar; k >= kNear; k--) drawStrip(c, k);
  drawInstrument(c);
  const g = c.createRadialGradient(CX, CY, RV * 0.5, CX, CY, RV);
  g.addColorStop(0, 'rgba(0,0,0,0)'); g.addColorStop(1, 'rgba(0,0,0,0.6)'); c.fillStyle = g; c.fillRect(0, 0, W, W);
  c.fillStyle = '#000'; c.beginPath(); c.rect(0, 0, W, W); c.arc(CX, CY, RV, 0, TAU, true); c.fill('evenodd');
  c.textAlign = 'center'; c.fillStyle = 'rgba(225,235,242,.9)'; c.font = '600 12px system-ui,sans-serif'; c.fillText(regionAt(z).name.toUpperCase(), CX, 44);
  c.fillStyle = 'rgba(170,190,205,.8)'; c.font = '11px system-ui,sans-serif'; c.fillText(cmAt(z) + ' cm from incisors', CX, 59);
  c.fillText('tip at ' + (Math.round(((tipAng / DEG + 90) % 360) / 30) % 12 || 12) + ' o\'clock', CX, 73);
}
function drawInstrument(c) {
  const tool = TOOLS[toolIdx], col = tool.color, rad = tool.radius();
  const lead = angDiff(tipAng, probeAng), ex = CX + Math.cos(probeAng) * 440, ey = CY + Math.sin(probeAng) * 440;
  const mx = (ex + TIP.x) / 2 - Math.sin(probeAng) * lead * 150, my = (ey + TIP.y) / 2 + Math.cos(probeAng) * lead * 150;   // shaft bows while the tip leads
  c.lineCap = 'round';
  const shaft = (w, s) => { c.strokeStyle = s; c.lineWidth = w; c.beginPath(); c.moveTo(ex, ey); c.quadraticCurveTo(mx, my, TIP.x, TIP.y); c.stroke(); };
  if (tool.id === 'wash') { shaft(15, '#5b7080'); shaft(11, '#a9c0cf'); shaft(4, 'rgba(190,230,250,.8)'); }
  else if (tool.id === 'cautery') { shaft(13, '#14181b'); shaft(9, '#2b3338'); shaft(2.5, 'rgba(255,255,255,.18)'); c.fillStyle = '#d9dde0'; c.beginPath(); c.arc(TIP.x, TIP.y, 5.5, 0, TAU); c.fill(); c.strokeStyle = '#7d868c'; c.lineWidth = 1.5; c.stroke(); }
  else { shaft(16, '#6b6f78'); shaft(11, '#b9bcc6'); }

  const live = state === 'play', aligned = fx.aligned;
  // footprint ring: dashed when idle, solid + glow when the tip is in range of the lesion
  c.save(); c.lineWidth = aligned ? 3 : 1.8; c.strokeStyle = col; c.globalAlpha = aligned ? 1 : 0.65; if (!aligned) c.setLineDash([5, 5]);
  if (aligned) { c.shadowColor = col; c.shadowBlur = 12; }
  c.beginPath(); c.arc(TIP.x, TIP.y, rad, 0, TAU); c.stroke(); c.restore();
  if (tool.id === 'banding') {                                      // translucent cap
    c.fillStyle = 'rgba(190,175,255,0.10)'; c.beginPath(); c.arc(TIP.x, TIP.y, rad, 0, TAU); c.fill();
    c.strokeStyle = 'rgba(210,200,255,.55)'; c.lineWidth = 3; c.beginPath(); c.arc(TIP.x, TIP.y, rad - 4, 0, TAU); c.stroke();
    if (fx.bandFlash > 0) { const q = 1 - fx.bandFlash / 0.6; c.strokeStyle = `rgba(255,255,255,${1 - q})`; c.lineWidth = 3; c.beginPath(); c.arc(TIP.x, TIP.y, rad * (1 - 0.6 * Math.sin(q * Math.PI)), 0, TAU); c.stroke(); }
  }
  if (fx.washing && live) {
    c.fillStyle = 'rgba(120,205,255,0.16)'; c.beginPath(); c.arc(TIP.x, TIP.y, rad, 0, TAU); c.fill();
    for (let i = 0; i < 26; i++) {
      const ph = (clock * 2.4 + i * 0.137) % 1, an = i * 2.399, rr = ph * rad * (0.55 + 0.45 * hash(i, 3));
      c.fillStyle = `rgba(215,240,255,${0.9 * (1 - ph)})`; c.beginPath(); c.arc(TIP.x + Math.cos(an) * rr, TIP.y + Math.sin(an) * rr, 1.4 + 2 * (1 - ph), 0, TAU); c.fill();
    }
  }
  if (fx.cauteryOn && live) {
    const gr = c.createRadialGradient(TIP.x, TIP.y, 0, TIP.x, TIP.y, 26); gr.addColorStop(0, 'rgba(255,230,150,.95)'); gr.addColorStop(0.4, 'rgba(255,150,50,.55)'); gr.addColorStop(1, 'rgba(255,120,30,0)');
    c.fillStyle = gr; c.beginPath(); c.arc(TIP.x, TIP.y, 26, 0, TAU); c.fill();
    for (let i = 0; i < 6; i++) { const an = hash(Math.floor(clock * 30), i) * TAU, l = 6 + hash(i, 8) * 12; c.strokeStyle = 'rgba(255,225,140,.9)'; c.lineWidth = 1.5; c.beginPath(); c.moveTo(TIP.x + Math.cos(an) * 6, TIP.y + Math.sin(an) * 6); c.lineTo(TIP.x + Math.cos(an) * (6 + l), TIP.y + Math.sin(an) * (6 + l)); c.stroke(); }
    if (fx.aligned) for (let i = 0; i < 4; i++) { const ph = (clock * 1.3 + i * 0.25) % 1; c.fillStyle = `rgba(235,235,235,${0.35 * (1 - ph)})`; c.beginPath(); c.arc(TIP.x + (hash(i, 5) - 0.5) * 16, TIP.y - ph * 34, 4 + ph * 8, 0, TAU); c.fill(); }
  }
  if (tool.id === 'cautery' && cauteryProg > 0.01) {                     // contact progress ring
    const pr = clamp(cauteryProg / CONFIG.cauteryContactSec, 0, 1);
    c.strokeStyle = 'rgba(255,179,71,.95)'; c.lineWidth = 5; c.lineCap = 'butt'; c.beginPath(); c.arc(TIP.x, TIP.y, rad + 9, -Math.PI / 2, -Math.PI / 2 + pr * TAU); c.stroke();
  }
}

/* =====================================================================
   Ending, debrief, replay
   ===================================================================== */
function gradeSignOff() {
  const cover = coverAtLesion();
  if (bleeding) return 'unsafe';
  const verified = stats.bestClearView >= CONFIG.clearViewSecToSignOff && cover < CONFIG.clearCover + 0.07;
  return verified ? 'good' : 'unverified';
}
function signOff() { if (state === 'play') end('signoff'); }
function end(reason) {
  if (state === 'ended') return;
  closeSeg();
  const grade = reason === 'haemorrhage' ? 'haemorrhage' : gradeSignOff();
  ev(reason === 'signoff' ? 'signoff' : 'death', reason === 'signoff' ? 'Signed off the site' : 'Patient decompensated from blood loss');
  state = 'ended'; fx.washing = fx.cauteryOn = false; hideCue();
  showDebrief(grade, reason);
}
const OUT = {
  good:        { b: 'good', badge: 'SAFE SIGN-OFF', title: 'Haemostasis secured', lead: 'You controlled the bleed, washed the site clear and re-inspected it before signing off.' },
  unverified:  { b: 'warn', badge: 'SIGN-OFF NOT VERIFIED', title: 'Bleeding stopped, but you could not confirm it', lead: 'Flow had stopped, but the site was not seen clear and stable for long enough. Residual blood can hide a re-bleed.' },
  unsafe:      { b: 'bad',  badge: 'UNSAFE SIGN-OFF', title: 'Signed off while still bleeding', lead: 'Fresh blood was still coming from the lesion when you ended the procedure.' },
  haemorrhage: { b: 'bad',  badge: 'PATIENT UNSTABLE', title: 'Decompensated from blood loss', lead: 'Blood loss reached ' + CONFIG.lossLimitMl + ' mL before the bleed was controlled.' },
};
function feedback(grade) {
  const out = [], s = stats, f1 = n => n.toFixed(1), N = CASE.notes, src = CASE.sourceName;
  const li = (tag, txt, cls) => out.push(`<li><span class="tag" style="color:${cls}">${tag}</span> ${txt}</li>`);
  const found = s.tFound === null ? 'You never brought a tool tip onto the lesion.' : `You got the ${src} under the tip at ${mmss(s.tFound)}.`;
  li('Finding the bleed', found + (s.overshoots ? ` You overshot ${s.overshoots}×; short taps of ↑/↓ and ←/→ give finer control near the source.` : ' No overshoots.'), '#e8eef2');
  // wash
  if (s.toolsUsed.wash) {
    li('Wash', `${f1(s.washTime)} s of washing, ${f1(s.washOnSrc)} s directed at the source. ` +
      (s.washRevealed ? N.wash : (s.washOnSrc > 0 ? 'You washed the site but never saw it bleed before treating.' : 'The jet never reached the source, so it stayed hidden.')) +
      (s.washAfterControl > 0.5 ? ` ${f1(s.washAfterControl)} s of washing after control cleared the residual blood.` : (controlledAt !== null ? ' You did not wash after control, so old blood still covered the site.' : '')), TOOLS[0].color);
  } else li('Wash', `You never washed. Blood covered the ${src}, so you could not see the source or confirm control. Wash first, treat, then wash again.`, TOOLS[0].color);
  // the case's own treatment first, then the unsuitable one
  const cautery = () => {
    if (CASE.suitable.cautery) {
      if (s.toolsUsed.cautery) {
        const to = controlledBy === 'cautery' && s.firstCautery !== null ? f1(controlledAt - s.firstCautery) : null;
        li('Cautery', `${f1(s.cauteryOn)} s on target (the case needs ${CONFIG.cauteryContactSec} s of continuous contact)` +
          (s.cauteryOff > 0.2 ? `, ${f1(s.cauteryOff)} s off target (${burns.filter(b => b.kind === 'burn').length} stray burn mark${burns.filter(b => b.kind === 'burn').length === 1 ? '' : 's'})` : '') + '. ' +
          (controlledBy === 'cautery' ? `Flow stopped ${to} s after you first fired the probe. ${N.cautery}` : 'The lesion was still bleeding when you finished.') +
          (s.overtreat > 1 ? ` ${f1(s.overtreat)} s of extra cautery after control adds perforation risk.` : ''), TOOLS[1].color);
      } else li('Cautery', controlledAt === null ? `You never used cautery, which is the intended treatment for this ${src}.` : 'No cautery used.', TOOLS[1].color);
    } else if (s.toolsUsed.cautery) li('Cautery', `${f1(s.cauteryWrong)} s of cautery on the ${src}. ${N.cautery}`, TOOLS[1].color);
  };
  const banding = () => {
    if (CASE.suitable.banding) {
      if (s.bandAttempts) li('Banding', `${s.bandsUsed} band${s.bandsUsed === 1 ? '' : 's'} on the varix` + (s.bandsWasted ? `, ${s.bandsWasted} wasted away from it (each leaves a nodule of normal mucosa)` : '') + '. ' +
        (controlledBy === 'banding' ? `Bleeding stopped at ${mmss(controlledAt)}. ${N.banding}` : 'The varix was still bleeding when you finished.'), TOOLS[2].color);
      else li('Banding', `You never banded. ${N.banding}`, TOOLS[2].color);
    } else if (s.bandAttempts) li('Banding', `${s.bandAttempts} attempt${s.bandAttempts > 1 ? 's' : ''}` + (s.bandOnTarget ? ` on the ${src}. ${N.banding} Switching straight back to cautery is the right call.` : ', none of them on the lesion.'), TOOLS[2].color);
  };
  if (CASE.intended === 'banding') { banding(); cautery(); } else { cautery(); banding(); }
  // sign-off
  if (grade === 'good') li('Sign-off', `You saw the site clear for ${f1(s.bestClearView)} s after control, then signed off. That is the reinspection step.` +
    (totalBlood() > 8 ? ' Some old blood still pools along the wall; a wider wash would leave a cleaner field.' : ''), '#5fd38d');
  else if (grade === 'unverified') li('Sign-off', controlledAt !== null && s.bestClearView > 0 ? `You saw the clear site for only ${f1(s.bestClearView)} s (need ${CONFIG.clearViewSecToSignOff} s). Wash again, look, and wait for stability.` : `Blood still hid the ${src} at sign-off. After treatment, wash the site and watch it for a few seconds before leaving.`, '#ffc857');
  else if (grade === 'unsafe') li('Sign-off', 'Never sign off an actively bleeding lesion. Use the wash to confirm the source, then treat it.', '#ff5d57');
  else li('Time', `This bleed costs about ${CASE.bleedMl ?? CONFIG.bleedMlPerSec} mL every second. Wash to find the source quickly, then go straight to ${CASE.intended}.`, '#ff5d57');
  if (Object.keys(s.toolsUsed).length === 0 && !s.bandAttempts) li('Tools', 'You did not use any attachment.', '#e8eef2');
  return out.join('');
}
let rp = { play: false, pos: 1, raf: 0, last: 0 };
function showDebrief(grade, reason) {
  const o = OUT[grade], s = stats;
  $('dbBadge').className = 'badge ' + o.b; $('dbBadge').textContent = o.badge; $('dbTitle').textContent = o.title; $('dbLead').textContent = o.lead;
  const cells = [['Case', `${caseIdx + 1}. ${CASE.short}`], ['Time', mmss(t)], ['Blood loss', Math.round(loss) + ' mL'], ['Bleed control', controlledAt === null ? 'not achieved' : mmss(controlledAt)],
    ['Source found', s.tFound === null ? 'never' : mmss(s.tFound)], ['Tools used', Object.keys(s.toolsUsed).map(k => k[0].toUpperCase() + k.slice(1)).concat(s.bandAttempts ? ['Banding'] : []).join(', ') || 'none'], ['Overshoots', s.overshoots]];
  $('dbStats').innerHTML = cells.map(([a, b]) => `<div class="stat"><small>${a}</small><b>${b}</b></div>`).join('');
  $('dbFeedback').innerHTML = feedback(grade);
  const usedIds = new Set(Object.keys(s.toolsUsed)); if (s.bandAttempts) usedIds.add('banding');
  $('dbLegend').innerHTML = TOOLS.filter(x => usedIds.has(x.id)).map(x => `<span><i style="background:${x.color}"></i>${x.name}</span>`).join('') +
    '<span><i style="background:#ff5d57"></i>Bleeding</span><span><i style="background:rgba(95,211,141,.5)"></i>Lesion in range</span>';
  $('debrief').hidden = false; $('debrief').scrollTop = 0;
  rp.pos = 1; rp.play = false; $('rpPlay').textContent = '▶ Replay'; $('rpScrub').value = 1000; drawReplay(1);
  $('retryBtn').focus({ preventScroll: true });
}
function drawReplay(pos) {
  const cv2 = $('replay'), c = cv2.getContext('2d'), w = cv2.width, h = cv2.height, L = 40, Rr = 10, T = 26, B = 24, tEnd = Math.max(t, 1), pt = pos * tEnd;
  c.clearRect(0, 0, w, h);
  const zT = LES.w - S * Rof(LES.w) / TIP_R;
  const zs = samples.map(q => q.z).concat([CASE.start.z, zT]), zLo = Math.max(0, Math.min(...zs) - 0.6), zHi = Math.min(ZMAX, Math.max(...zs) + 0.6);
  const X = tt => L + (w - L - Rr) * tt / tEnd, Y = zz => T + (h - T - B) * (zz - zLo) / (zHi - zLo);
  c.fillStyle = 'rgba(95,211,141,.14)'; c.fillRect(L, Y(zT - 0.25), w - L - Rr, Y(zT + 0.25) - Y(zT - 0.25));
  c.setLineDash([3, 4]); c.strokeStyle = '#3a4854'; c.font = '10px system-ui,sans-serif';           // anatomical landmarks in range
  for (const lm of AN.landmarks) if (lm.w > zLo && lm.w < zHi) { c.beginPath(); c.moveTo(L, Y(lm.w)); c.lineTo(w - Rr, Y(lm.w)); c.stroke(); c.fillStyle = '#6f808c'; c.textAlign = 'right'; c.fillText(lm.name.split(' (')[0], w - Rr - 2, Y(lm.w) - 3); }
  c.setLineDash([]);
  c.strokeStyle = '#26323c'; c.lineWidth = 1; c.fillStyle = '#8c9aa5'; c.font = '11px system-ui,sans-serif'; c.textAlign = 'center';
  const step = tEnd > 90 ? 20 : tEnd > 40 ? 10 : 5;
  for (let s2 = 0; s2 <= tEnd; s2 += step) { c.beginPath(); c.moveTo(X(s2), T); c.lineTo(X(s2), h - B); c.stroke(); c.fillText(mmss(s2), X(s2), h - 8); }
  c.textAlign = 'right'; c.fillText('higher', L - 5, T + 8); c.fillText('deeper', L - 5, h - B);
  c.fillStyle = '#ff5d57'; c.fillRect(L, 8, X(controlledAt === null ? tEnd : controlledAt) - L, 6);
  c.fillStyle = '#8c9aa5'; c.textAlign = 'left'; c.fillText(controlledAt === null ? 'bleeding throughout' : 'bleeding until control', L + 4, 22);
  c.lineCap = 'round'; c.lineJoin = 'round';
  for (let i = 1; i < samples.length; i++) {
    const a = samples[i - 1], b = samples[i]; if (a.t > pt) break;
    c.strokeStyle = b.use ? TOOLS[b.tool].color : '#9fb0bc'; c.lineWidth = b.use ? 5 : 2;
    c.beginPath(); c.moveTo(X(a.t), Y(a.z)); c.lineTo(X(Math.min(b.t, pt)), Y(a.z + (b.z - a.z) * (b.t > pt ? (pt - a.t) / (b.t - a.t) : 1))); c.stroke();
  }
  for (const e of events) {
    if (e.t > pt) continue;
    const s2 = samples.reduce((m, q) => Math.abs(q.t - e.t) < Math.abs(m.t - e.t) ? q : m, samples[0] || { t: 0, z: 0 });
    const x = X(e.t), y = Y(s2.z);
    if (e.kind === 'control') { c.fillStyle = '#5fd38d'; c.beginPath(); c.moveTo(x, y - 8); c.lineTo(x + 7, y); c.lineTo(x, y + 8); c.lineTo(x - 7, y); c.closePath(); c.fill(); }
    else if (e.kind === 'band' || e.kind === 'landmark') { c.strokeStyle = e.kind === 'band' ? TOOLS[2].color : '#6f808c'; c.lineWidth = 2.5; c.beginPath(); c.moveTo(x - 5, y - 5); c.lineTo(x + 5, y + 5); c.moveTo(x + 5, y - 5); c.lineTo(x - 5, y + 5); c.stroke(); }
    else if (e.kind === 'reveal') { c.fillStyle = '#ff5d57'; c.beginPath(); c.arc(x, y, 5, 0, TAU); c.fill(); }
    else if (e.kind === 'signoff' || e.kind === 'death') { c.strokeStyle = '#e8eef2'; c.lineWidth = 2; c.beginPath(); c.moveTo(x, T - 4); c.lineTo(x, h - B); c.stroke(); }
  }
  c.strokeStyle = '#e8eef2'; c.lineWidth = 1.5; c.beginPath(); c.moveTo(X(pt), T - 6); c.lineTo(X(pt), h - B); c.stroke();
  const last = events.filter(e => e.t <= pt + 1e-6).pop();
  $('rpCap').textContent = last ? `${mmss(last.t)} · ${last.text}` : '';
}
$('rpScrub').addEventListener('input', e => { rp.play = false; rp.pos = e.target.value / 1000; $('rpPlay').textContent = '▶ Replay'; drawReplay(rp.pos); });
$('rpPlay').addEventListener('click', e => {
  e.currentTarget.blur();
  if (rp.play) { rp.play = false; e.currentTarget.textContent = '▶ Replay'; return; }
  if (rp.pos >= 0.999) rp.pos = 0; rp.play = true; rp.last = performance.now(); e.currentTarget.textContent = '❚❚ Pause';
  const stepR = now => { if (!rp.play) return; rp.pos = Math.min(1, rp.pos + (now - rp.last) / 1000 / Math.max(6, Math.min(14, t / 4))); rp.last = now;
    $('rpScrub').value = Math.round(rp.pos * 1000); drawReplay(rp.pos);
    if (rp.pos >= 1) { rp.play = false; $('rpPlay').textContent = '▶ Replay'; return; } requestAnimationFrame(stepR); };
  requestAnimationFrame(stepR);
});

/* =====================================================================
   Main loop (+ a tiny hook so the case can be driven deterministically in tests)
   ===================================================================== */
let last = performance.now(), manual = false;
function frame(now) {
  const dt = Math.min(0.05, (now - last) / 1000); last = now;
  if (!manual) update(dt);
  render(); requestAnimationFrame(frame);
}
setCase(0); reset(false);
requestAnimationFrame(frame);
window.__duo = {
  manual(on) { manual = on; },
  tick(sec) { for (let i = 0; i < Math.round(sec * 60); i++) update(1 / 60); render(); },
  snapshot: () => ({ state, t, z, v, tipAng, tip: { x: TIP.x, y: TIP.y }, probeAng, steer: tipAng, tool: TOOLS[toolIdx].id, bleeding, controlledAt, loss, cauteryProg, armed: input.armed, use: input.use, cover: coverAtLesion(), caseIdx, region: regionAt(z).id, cm: cmAt(z), bands: bandsLeft, controlledBy, zMax: ZMAX,
    stats: JSON.parse(JSON.stringify(stats)), lesion: lesionScreen(), burns: burns.length, clearView, washing: fx.washing, cauteryOn: fx.cauteryOn, totalBlood: totalBlood() }),
  blood: () => Array.from(amt), CONFIG, ANATOMY: AN, get CASE() { return CASE; }, reset, loadCase,
};
})();
