(() => {
'use strict';

const CONFIG = window.CONFIG;   // see config/config.js

const CASE = window.CASE;       // see cases/duodenal-ulcer.js

const TOOLS = [
  { id: 'wash',    name: 'Wash',    verb: 'Hold',  color: '#4fc3f7', radius: () => CONFIG.washRadius },
  { id: 'cautery', name: 'Cautery', verb: 'Hold',  color: '#ffb347', radius: () => CONFIG.cauteryContactRadius },
  { id: 'banding', name: 'Banding', verb: 'Tap',   color: '#b69cff', radius: () => CONFIG.bandRadius },
];

/* =====================================================================
   Geometry. The duodenum is a tube of radius ~1 along depth w.
   The camera sits at depth z; a wall point (theta, w) projects to
   radius S*R/(w-z). The instrument tip is fixed on screen, so moving
   the scope forward/back slides the tissue radially past it, and steering
   (left/right) shifts the whole picture sideways. Hitting the lesion needs both.
   ===================================================================== */
const W = 640, CX = 320, CY = 320, RV = 314, S = 230, DELTA = 0.25, WMAX = 16, ZMAX = 8.0, NEAR = 0.2;
const NA = 36, NB = Math.ceil(WMAX / DELTA), TAU = Math.PI * 2, DEG = Math.PI / 180;
const LES = CASE.lesion, LES_TH = LES.deg * DEG;
const TIP = { x: 410, y: 420 };                                  // instrument tip: fixed on screen, lower right
const TIP_TH = Math.atan2(TIP.y - CY, TIP.x - CX);              // direction the instrument enters from
const STEER_PX = S;                                             // screen px per unit of steering (angulation)

const clamp = (x, a, b) => Math.max(a, Math.min(b, x));
const lerp = (a, b, t) => a + (b - a) * t;
const smooth = (a, b, x) => { const t = clamp((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t); };
function hash(a, b) { let h = (Math.imul(a | 0, 374761393) + Math.imul(b | 0, 668265263)) | 0; h = Math.imul(h ^ (h >>> 13), 1274126177); return ((h ^ (h >>> 16)) >>> 0) / 4294967295; }

const Rof  = w => 1 + 0.32 * (1 - smooth(0.2, 2.6, w));                 // wider bulb, narrower beyond
const bend = w => smooth(LES.w + 0.6, LES.w + 3.0, w);                  // lumen curves away after the lesion
const pxOf = w => 0.5 * Math.sin(0.6 * (w - LES.w)) * bend(w);
const pyOf = w => 0.25 * Math.sin(0.37 * (w - LES.w)) * bend(w);
const bright = d => clamp(1.3 / (1 + 0.18 * d * d), 0.06, 1);

const cam = { z: 0, px: 0, py: 0, ox: 0 };
function setCam() { cam.z = z; cam.ox = -steer * STEER_PX; cam.px = pxOf(z) + 0.012 * Math.sin(clock * 1.3); cam.py = pyOf(z) + 0.01 * Math.sin(clock * 0.93 + 1); }
const P = { x: 0, y: 0, d: 0 };
function proj(th, w, R) {
  const d = w - cam.z, k = S / d; if (R === undefined) R = Rof(w);
  P.x = CX + cam.ox + ((pxOf(w) - cam.px) + R * Math.cos(th)) * k;
  P.y = CY + ((pyOf(w) - cam.py) + R * Math.sin(th)) * k; P.d = d; return P;
}
const COS = [], SIN = [];
for (let a = 0; a <= NA; a++) { COS.push(Math.cos(a * TAU / NA)); SIN.push(Math.sin(a * TAU / NA)); }

/* Circular folds: {w, a0, sweep (deg), prot}. The first one sits just in front of the lesion. */
const FOLDS = [
  { w: LES.w - 0.55, a0: -75, sweep: 100, prot: 0.36 },
  { w: 1.5, a0: 120, sweep: 150, prot: 0.26 },
  { w: LES.w + 1.9, a0: -30, sweep: 250, prot: 0.40 },
];
for (let i = 0, w = LES.w + 3.0; w < WMAX - 0.5; i++, w += 0.85 + hash(i, 5) * 0.4)
  FOLDS.push({ w, a0: Math.round(hash(i, 9) * 360), sweep: 190 + Math.round(hash(i, 3) * 140), prot: 0.34 + hash(i, 7) * 0.14 });
const FOLD_BY_K = new Map();
FOLDS.forEach(f => { const k = Math.floor(f.w / DELTA); (FOLD_BY_K.get(k) || FOLD_BY_K.set(k, []).get(k)).push(f); });

/* =====================================================================
   State
   ===================================================================== */
let state = 'intro';                    // intro | play | paused | ended
let t = 0, clock = 0, z = 0, v = 0, steer = 0, vs = 0, toolIdx = 0, loss = 0;
let bleeding = true, controlledAt = null, cauteryProg = 0, bandCool = 0, offAccum = 0, bloodAcc = 0, sampleAcc = 0;
let stats, events, samples, burns, seg, fx, overshootActive, clearView, lastCue = {};
const input = { up: false, down: false, left: false, right: false, use: false, armed: false };
const tapHold = { up: 0, down: 0, left: 0, right: 0 };   // a very quick tap still moves the scope a little (min. 90 ms of motion)

const amt = new Float32Array(NB * NA), fr = new Float32Array(NB * NA);
const dAmt = new Float32Array(NB * NA), dMass = new Float32Array(NB * NA);
const SRC_J = Math.floor(LES.w / DELTA), SRC_A = Math.floor((((LES.deg % 360) + 360) % 360) / (360 / NA));

function freshStats() {
  return { washTime: 0, washOnSrc: 0, washRevealed: false, washAfterControl: 0, cauteryTime: 0, cauteryOn: 0, cauteryOff: 0,
    overtreat: 0, firstCautery: null, bandAttempts: 0, bandOnTarget: 0, overshoots: 0, tFound: null, toolSwitches: 0,
    toolsUsed: {}, bestClearView: 0, sawLimit: 0 };
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
    const e = CONFIG.emitPerSec * dt * (0.8 + 0.2 * Math.sin(t * 7.2));
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
    if (j > 0) { const k = i - NA; dAmt[k] += prox; dMass[k] += prox * f; } // at j=0 it drains back to the stomach
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
  // blood thickness over the crater itself: its own cell, the two beside it, and (weighted) the cells above and below
  const at = (j, a) => amt[j * NA + ((a + NA) % NA)];
  return Math.max(at(SRC_J, SRC_A), at(SRC_J, SRC_A - 1), at(SRC_J, SRC_A + 1), 0.8 * at(SRC_J - 1, SRC_A), 0.8 * at(SRC_J + 1, SRC_A));
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
function reset(startNow) {
  t = 0; z = 0; v = 0; steer = 0; vs = 0; toolIdx = 0; loss = 0; bleeding = true; controlledAt = null; cauteryProg = 0; bandCool = 0; offAccum = 0;
  bloodAcc = 0; sampleAcc = 0; stats = freshStats(); events = []; samples = []; burns = []; seg = null; overshootActive = false; clearView = 0; lastCue = {};
  fx = { washing: false, cauteryOn: false, aligned: false, bandFlash: 0, bandOk: false };
  amt.fill(0); fr.fill(0); input.armed = false;
  for (let i = 0; i < CONFIG.trailWarmupTicks; i++) { t = i * 0.1; bloodTick(0.1); }        // give the case a trail before the first frame
  t = 0; setCam();
  ev('start', 'Scope in the duodenal bulb; fresh blood trail ahead');
  ev('tool', 'Wash selected');
  $('debrief').hidden = true; $('pauseOv').hidden = true;
  state = startNow ? 'play' : 'intro'; $('intro').hidden = !!startNow;
  hideCue(); updateToolUI(); updateHUD(); $('pauseBtn').textContent = 'Pause (P)';
}

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
}, { passive: false });
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
  L.textContent = tl.name.toUpperCase() + ' · ' + tl.verb + ' ' + (touchUI ? 'USE' : 'Space');
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
  bandCool = CONFIG.bandCooldown; stats.bandAttempts++;
  const L = lesionScreen(), on = L.vis && Math.hypot(L.x - TIP.x, L.y - TIP.y) <= CONFIG.bandRadius + 8;
  fx.bandFlash = 0.6; fx.bandOk = false;
  if (on) stats.bandOnTarget++;
  if (on && CASE.suitable.banding) { fx.bandOk = true; ev('band', 'Band deployed on the lesion'); showCue('Band deployed.', 'good'); }   // reserved for a future suitable case
  else { ev('band', on ? 'Banding attempted on the ulcer: not capturable' : 'Banding attempted away from the lesion'); showCue(on ? CASE.bandingCue : CASE.bandingMissCue, 'warn', 3200); }
}
function control() {
  bleeding = false; controlledAt = t; cauteryProg = 0;
  ev('control', 'Bleeding controlled by cautery');
  showCue('Contact complete. Fresh flow has stopped.', 'good', 3000);
}
function addBurn() {
  const p = wallPointUnderTip(); burns.push({ th: p.th + (hash(burns.length, 2) - 0.5) * 0.05, w: p.w }); if (burns.length > 14) burns.shift();
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
  if (nz <= 0) { nz = 0; v = 0; if (tgt < 0) cueOnce('lim0', 3, 'Back at the pylorus: you cannot withdraw further.', '', 1800); }
  else if (nz >= ZMAX) { nz = ZMAX; v = 0; if (tgt > 0) cueOnce('limZ', 3, 'End of the practice segment. Withdraw to return to the bulb.', '', 1800); }
  z = nz;
  const lOn = input.left || tapHold.left > 0, rOn = input.right || tapHold.right > 0, ds = CONFIG.steerAccel * dt;
  tapHold.left = Math.max(0, tapHold.left - dt); tapHold.right = Math.max(0, tapHold.right - dt);
  const tgtS = (rOn ? 1 : 0) - (lOn ? 1 : 0);
  vs += clamp(tgtS * CONFIG.steerSpeed - vs, -ds, ds);
  let ns = steer + vs * dt;
  if (ns <= -CONFIG.steerMax) { ns = -CONFIG.steerMax; vs = 0; if (tgtS < 0) cueOnce('limL', 3, 'Scope fully angled left.', '', 1500); }
  else if (ns >= CONFIG.steerMax) { ns = CONFIG.steerMax; vs = 0; if (tgtS > 0) cueOnce('limR', 3, 'Scope fully angled right.', '', 1500); }
  steer = ns; setCam();

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
        if (!stats.washRevealed) { stats.washRevealed = true; ev('reveal', 'Fresh blood welled up from the crater: active bleed confirmed'); }
        cueOnce('well', 5, 'Fresh blood wells up from the crater: this is the source.', 'bad', 2600);
      }
    }
  } else if (cur === 'cautery') {
    fx.cauteryOn = true; stats.cauteryTime += dt; if (stats.firstCautery === null) stats.firstCautery = t;
    if (aimed) {
      stats.cauteryOn += dt; seg.on += dt;
      if (bleeding) { cauteryProg += dt; if (cauteryProg >= CONFIG.cauteryContactSec) control(); }
      else { stats.overtreat += dt; if (stats.overtreat > 2.5) cueOnce('over-t', 4, 'Enough: further cautery risks deep thermal injury.', 'warn', 2200); }
    } else {
      stats.cauteryOff += dt; seg.off += dt; offAccum += dt; cauteryProg = Math.max(0, cauteryProg - dt * CONFIG.cauteryDecay);
      if (offAccum > 0.5) { offAccum = 0; addBurn(); }
      if (bleeding) cueOnce('off', 4, 'Cautery away from the lesion: it is still bleeding.', 'warn', 2200);
    }
  }
  if (cur !== 'cautery') cauteryProg = Math.max(0, cauteryProg - dt * CONFIG.cauteryDecay);
  fx.aligned = tool === 'wash' ? dist <= CONFIG.washRadius : tool === 'cautery' ? aimed : dist <= CONFIG.bandRadius;

  // --- blood + patient
  bloodAcc += dt; while (bloodAcc >= 0.1) { bloodTick(0.1); bloodAcc -= 0.1; }
  if (bleeding) loss += CONFIG.bleedMlPerSec * dt;

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
function edge(w) { const d = w - cam.z, k = S / d; return { x: CX + cam.ox + (pxOf(w) - cam.px) * k, y: CY + (pyOf(w) - cam.py) * k, k, R: Rof(w), d }; }
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
  const w0 = Math.max(k * DELTA, cam.z + NEAR), w1 = (k + 1) * DELTA + 0.03; if (w1 <= w0 + 1e-3) return;   // +0.03 overlaps the farther strip so no seam shows
  const e0 = edge(w0), e1 = edge(w1), b = bright((w0 + w1) / 2 - cam.z);
  c.save();
  c.beginPath(); c.moveTo(e0.x + e0.R * e0.k, e0.y); c.arc(e0.x, e0.y, e0.R * e0.k, 0, TAU);
  c.moveTo(e1.x + e1.R * e1.k, e1.y); c.arc(e1.x, e1.y, e1.R * e1.k, 0, TAU); c.clip('evenodd');
  for (let a = 0; a < NA; a++) {
    const h = hash(k, a);
    const nz = 0.5 * Math.sin(a * 0.85 + k * 0.41) + 0.5 * Math.sin(a * 2.3 - k * 0.77 + 1.3);       // smooth mottling, not a checkerboard
    const vv = 0.98 + 0.045 * nz + 0.03 * (h - 0.5);
    const col = `rgb(${Math.min(255, 218 * b * vv) | 0},${Math.min(255, (118 + 8 * nz) * b * vv) | 0},${Math.min(255, 98 * b * vv) | 0})`;
    c.fillStyle = col; c.strokeStyle = col; c.lineWidth = 1;
    c.beginPath();
    c.moveTo(e0.x + e0.R * COS[a] * e0.k, e0.y + e0.R * SIN[a] * e0.k);
    c.lineTo(e0.x + e0.R * COS[a + 1] * e0.k, e0.y + e0.R * SIN[a + 1] * e0.k);
    c.lineTo(e1.x + e1.R * COS[a + 1] * e1.k, e1.y + e1.R * SIN[a + 1] * e1.k);
    c.lineTo(e1.x + e1.R * COS[a] * e1.k, e1.y + e1.R * SIN[a] * e1.k);
    c.closePath(); c.fill(); c.stroke();
    if (h > 0.93) {                                   // wet specular glints
      const mx = (e0.x + e0.R * COS[a] * e0.k + e1.x + e1.R * COS[a + 1] * e1.k) / 2, my = (e0.y + e0.R * SIN[a] * e0.k + e1.y + e1.R * SIN[a + 1] * e1.k) / 2;
      c.fillStyle = `rgba(255,235,225,${0.22 * b})`; c.beginPath(); c.ellipse(mx, my, 3 + 5 * e0.k / 100, 1.6 + 2.4 * e0.k / 100, 0, 0, TAU); c.fill();
    } else if (h < 0.07) {                            // small vessels
      const mx = (e0.x + e0.R * COS[a] * e0.k), my = (e0.y + e0.R * SIN[a] * e0.k), nx = (e1.x + e1.R * COS[a] * e1.k), ny = (e1.y + e1.R * SIN[a] * e1.k);
      c.strokeStyle = `rgba(150,40,45,${0.35 * b})`; c.lineWidth = 1.2; c.beginPath(); c.moveTo(mx, my); c.lineTo(nx, ny); c.stroke();
    }
  }
  const lo = k * DELTA, hi = (k + 1) * DELTA;
  if (hi > LES.w - 0.32 && lo < LES.w + 0.32) drawLesion(c);
  for (const bm of burns) if (bm.w + 0.1 > lo && bm.w - 0.1 < hi) drawBurn(c, bm);
  for (let j = Math.max(0, k - 1); j <= Math.min(NB - 1, k + 1); j++) for (let a = 0; a < NA; a++) { const i = j * NA + a; if (amt[i] > 0.03) drawBlob(c, j, a, i); }
  c.restore();
  const fl = FOLD_BY_K.get(k); if (fl) for (const f of fl) drawFold(c, f);
}
function drawBlob(c, j, a, i) {
  const [th, w] = cellCentre(j, a), m = amt[i], f = fr[i], hth = 0.15 + 0.035 * Math.min(m, 1.2), hw = 0.2 + 0.05 * Math.min(m, 1.2);
  if (w - cam.z < 0.12) return;
  patchPath(c, th + (hash(i, 31) - 0.5) * 0.05, w + (hash(i, 32) - 0.5) * 0.06, hth, hw, 12, 0.35, i);
  const al = (1 - Math.exp(-1.5 * m)) * 0.74, r = lerp(88, 228, f), g = lerp(14, 20, f), bl = lerp(18, 26, f);
  c.fillStyle = `rgba(${r | 0},${g | 0},${bl | 0},${al})`; c.fill();
  if (f > 0.55 && m > 0.8) { const p = proj(th - 0.03, w - 0.03); c.fillStyle = `rgba(255,190,190,${0.22 * f})`; c.beginPath(); c.ellipse(p.x, p.y, 2.5, 1.5, 0, 0, TAU); c.fill(); }
}
function drawLesion(c) {
  const R = Rof(LES.w), r = LES.radius, hth = r / R;
  patchPath(c, LES_TH, LES.w, hth * 1.7, r * 1.7, 22, 0.25, 71); c.fillStyle = bleeding ? 'rgba(200,50,55,0.42)' : 'rgba(205,90,80,0.32)'; c.fill();     // hyperaemic edge
  patchPath(c, LES_TH, LES.w, hth, r, 22, 0.3, 72);
  if (bleeding) { c.fillStyle = 'rgb(238,214,160)'; c.fill(); c.lineWidth = 2; c.strokeStyle = 'rgba(120,35,35,.8)'; c.stroke(); }
  else { c.fillStyle = 'rgb(214,190,150)'; c.fill(); c.lineWidth = 2; c.strokeStyle = 'rgba(120,70,50,.8)'; c.stroke(); }
  patchPath(c, LES_TH, LES.w, hth * 0.55, r * 0.55, 16, 0.3, 73);
  c.fillStyle = bleeding ? 'rgb(206,160,110)' : 'rgb(78,52,40)'; c.fill();                        // base / eschar after cautery
  if (!bleeding) { patchPath(c, LES_TH, LES.w, hth * 0.3, r * 0.3, 12, 0.3, 74); c.fillStyle = 'rgb(38,26,22)'; c.fill(); }
  if (bleeding) {                                                                                    // welling point + ripple
    const ph = (clock * 1.7) % 1, pulse = 0.5 + 0.5 * Math.sin(clock * 7.2);
    patchPath(c, LES_TH, LES.w, hth * (0.22 + 0.1 * pulse), r * (0.22 + 0.1 * pulse), 14, 0.2, 75); c.fillStyle = 'rgb(225,22,28)'; c.fill();
    patchPath(c, LES_TH, LES.w, hth * (0.3 + 0.9 * ph), r * (0.3 + 0.9 * ph), 20, 0.2, 76); c.strokeStyle = `rgba(230,30,35,${0.7 * (1 - ph)})`; c.lineWidth = 2; c.stroke();
  }
}
function drawBurn(c, bm) {
  patchPath(c, bm.th, bm.w, 0.12, 0.10, 10, 0.4, 91); c.fillStyle = 'rgba(225,200,170,0.55)'; c.fill();
  patchPath(c, bm.th, bm.w, 0.07, 0.06, 10, 0.4, 92); c.fillStyle = 'rgba(60,36,28,0.9)'; c.fill();
}
function drawFold(c, f) {
  const d = f.w - cam.z; if (d < 0.22) return;
  const e = edge(f.w), b = bright(d), a0 = f.a0 * DEG, sw = f.sweep * DEG, n = 22;
  c.beginPath();
  for (let i = 0; i <= n; i++) { const th = a0 + sw * i / n; const x = e.x + e.R * Math.cos(th) * e.k, y = e.y + e.R * Math.sin(th) * e.k; i ? c.lineTo(x, y) : c.moveTo(x, y); }
  for (let i = n; i >= 0; i--) {
    const u = i / n, th = a0 + sw * u, pr = f.prot * Math.pow(Math.sin(Math.PI * u), 0.7), rr = e.R * (1 - pr) * e.k;
    c.lineTo(e.x + Math.cos(th) * rr, e.y + Math.sin(th) * rr);
  }
  c.closePath();
  c.fillStyle = `rgb(${Math.min(255, 238 * b) | 0},${Math.min(255, 150 * b) | 0},${Math.min(255, 128 * b) | 0})`; c.fill();
  c.lineJoin = 'round'; c.strokeStyle = `rgba(255,205,185,${0.75 * b})`; c.lineWidth = Math.max(1.2, 2.4 * Math.min(2, e.k / 90)); c.stroke();
}
function render() {
  const c = ctx; c.setTransform(scale, 0, 0, scale, 0, 0);
  c.fillStyle = '#060304'; c.fillRect(0, 0, W, W);
  setCam();
  const kNear = Math.max(0, Math.floor((cam.z + NEAR) / DELTA)), kFar = Math.min(NB - 1, Math.floor((cam.z + 8.5) / DELTA));
  for (let k = kFar; k >= kNear; k--) drawStrip(c, k);
  drawInstrument(c);
  const g = c.createRadialGradient(CX, CY, RV * 0.5, CX, CY, RV);
  g.addColorStop(0, 'rgba(0,0,0,0)'); g.addColorStop(1, 'rgba(0,0,0,0.6)'); c.fillStyle = g; c.fillRect(0, 0, W, W);
  c.fillStyle = '#000'; c.beginPath(); c.rect(0, 0, W, W); c.arc(CX, CY, RV, 0, TAU, true); c.fill('evenodd');
  c.fillStyle = 'rgba(210,225,235,.8)'; c.font = '600 13px system-ui,sans-serif'; c.textAlign = 'left';
  c.fillText(cam.z + 0 < 2.2 ? 'D1 · BULB' : cam.z < 5 ? 'D1 · POST-BULB' : 'D2', 70, 70);
  c.fillStyle = 'rgba(160,180,195,.7)'; c.font = '12px system-ui,sans-serif'; c.fillText(Math.round(58 + z * 2.5) + ' cm', 70, 87);
}
function drawInstrument(c) {
  const tool = TOOLS[toolIdx], col = tool.color, rad = tool.radius();
  const ex = CX + Math.cos(TIP_TH + 0.16) * 440, ey = CY + Math.sin(TIP_TH + 0.16) * 440;
  const mx = (ex + TIP.x) / 2 + 14, my = (ey + TIP.y) / 2 - 8;
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
  const out = [], s = stats, used = Object.keys(s.toolsUsed), f1 = n => n.toFixed(1);
  const li = (tag, txt, cls) => out.push(`<li><span class="tag" style="color:${cls}">${tag}</span> ${txt}</li>`);
  const found = s.tFound === null ? 'You never brought a tool tip onto the lesion.' : `You got the lesion under the tip at ${mmss(s.tFound)}.`;
  li('Finding the bleed', found + (s.overshoots ? ` You overshot ${s.overshoots}×; short taps of ↑/↓ and ←/→ give finer control near the source.` : ' No overshoots.'), '#e8eef2');
  if (s.toolsUsed.wash) {
    li('Wash', `${f1(s.washTime)} s of washing, ${f1(s.washOnSrc)} s directed at the source. ` +
      (s.washRevealed ? 'Fresh blood welling back from the crater showed it was still actively bleeding.' : (s.washOnSrc > 0 ? 'You washed the site but never saw it well up before treating.' : 'The jet never reached the crater, so the source stayed hidden.')) +
      (s.washAfterControl > 0.5 ? ` ${f1(s.washAfterControl)} s of washing after control cleared the residual blood.` : (controlledAt !== null ? ' You did not wash after control, so old blood still covered the site.' : '')), TOOLS[0].color);
  } else li('Wash', 'You never washed. Blood covered the crater, so you could not see the source or confirm control. Wash first, treat, then wash again.', TOOLS[0].color);
  if (s.toolsUsed.cautery) {
    const to = controlledAt !== null && s.firstCautery !== null ? f1(controlledAt - s.firstCautery) : null;
    li('Cautery', `${f1(s.cauteryOn)} s on target (the case needs ${CONFIG.cauteryContactSec} s of continuous contact)` +
      (s.cauteryOff > 0.2 ? `, ${f1(s.cauteryOff)} s off target (${burns.length} stray burn mark${burns.length === 1 ? '' : 's'})` : '') + '. ' +
      (controlledAt !== null ? `Flow stopped ${to} s after you first fired the probe.` : 'The lesion was still bleeding when you finished.') +
      (s.overtreat > 1 ? ` ${f1(s.overtreat)} s of extra cautery after control adds perforation risk.` : ''), TOOLS[1].color);
  } else li('Cautery', controlledAt === null ? 'You never used cautery, which is the intended treatment for this flat fibrotic ulcer.' : 'No cautery used.', TOOLS[1].color);
  if (s.bandAttempts) li('Banding', `${s.bandAttempts} attempt${s.bandAttempts > 1 ? 's' : ''}` + (s.bandOnTarget ? ' on the ulcer. A flat, fibrotic crater cannot be sucked into the cap, so banding was never going to work here. It suits a raised lesion or a vessel that can be captured. Switching back to cautery quickly is the right call.' : ', none of them on the lesion.') , TOOLS[2].color);
  if (grade === 'good') li('Sign-off', `You saw the site clear for ${f1(s.bestClearView)} s after control, then signed off. That is the reinspection step.` +
    (totalBlood() > 8 ? ' Some old blood still pools along the wall; a wider wash would leave a cleaner field.' : ''), '#5fd38d');
  else if (grade === 'unverified') li('Sign-off', controlledAt !== null && s.bestClearView > 0 ? `You saw the clear site for only ${f1(s.bestClearView)} s (need ${CONFIG.clearViewSecToSignOff} s). Wash again, look, and wait for stability.` : 'Blood still hid the lesion at sign-off. After cautery, wash the site and watch it for a few seconds before leaving.', '#ffc857');
  else if (grade === 'unsafe') li('Sign-off', 'Never sign off an actively bleeding lesion. Use the wash to confirm the source, then treat it.', '#ff5d57');
  else li('Time', 'Brisk bleeding costs about ' + CONFIG.bleedMlPerSec + ' mL every second. Wash to find the source quickly, then go straight to cautery.', '#ff5d57');
  if (used.length === 0) li('Tools', 'You did not use any attachment.', '#e8eef2');
  return out.join('');
}
let rp = { play: false, pos: 1, raf: 0, last: 0 };
function showDebrief(grade, reason) {
  const o = OUT[grade], s = stats;
  $('dbBadge').className = 'badge ' + o.b; $('dbBadge').textContent = o.badge; $('dbTitle').textContent = o.title; $('dbLead').textContent = o.lead;
  const cells = [['Time', mmss(t)], ['Blood loss', Math.round(loss) + ' mL'], ['Bleed control', controlledAt === null ? 'not achieved' : mmss(controlledAt)],
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
  const X = tt => L + (w - L - Rr) * tt / tEnd, Y = zz => T + (h - T - B) * zz / ZMAX;
  const zT = LES.w - S * Math.sin(LES_TH) / (TIP.y - CY);
  c.fillStyle = 'rgba(95,211,141,.14)'; c.fillRect(L, Y(zT - 0.25), w - L - Rr, Y(zT + 0.25) - Y(zT - 0.25));
  c.strokeStyle = '#26323c'; c.lineWidth = 1; c.fillStyle = '#8c9aa5'; c.font = '11px system-ui,sans-serif'; c.textAlign = 'center';
  const step = tEnd > 90 ? 20 : tEnd > 40 ? 10 : 5;
  for (let s2 = 0; s2 <= tEnd; s2 += step) { c.beginPath(); c.moveTo(X(s2), T); c.lineTo(X(s2), h - B); c.stroke(); c.fillText(mmss(s2), X(s2), h - 8); }
  c.textAlign = 'right'; c.fillText('bulb', L - 5, Y(0) + 4); c.fillText('deeper', L - 5, Y(ZMAX) + 2);
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
    else if (e.kind === 'band') { c.strokeStyle = TOOLS[2].color; c.lineWidth = 2.5; c.beginPath(); c.moveTo(x - 5, y - 5); c.lineTo(x + 5, y + 5); c.moveTo(x + 5, y - 5); c.lineTo(x - 5, y + 5); c.stroke(); }
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
reset(false);
requestAnimationFrame(frame);
window.__duo = {
  manual(on) { manual = on; },
  tick(sec) { for (let i = 0; i < Math.round(sec * 60); i++) update(1 / 60); render(); },
  snapshot: () => ({ state, t, z, v, steer, tool: TOOLS[toolIdx].id, bleeding, controlledAt, loss, cauteryProg, armed: input.armed, use: input.use, cover: coverAtLesion(),
    stats: JSON.parse(JSON.stringify(stats)), lesion: lesionScreen(), burns: burns.length, clearView, washing: fx.washing, cauteryOn: fx.cauteryOn, totalBlood: totalBlood() }),
  blood: () => Array.from(amt), CONFIG, CASE, reset,
};
})();
