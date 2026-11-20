using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// One case, played in real time. Rules mirror the web prototype (web/js/game.js): the scope advances and withdraws along
/// the tract, the instrument tip rotates round the wall, Space uses the selected attachment, and the lesion must be found,
/// treated with the right attachment, washed and re-inspected before sign-off. Blood loss runs the whole time.
/// </summary>
public class Procedure : MonoBehaviour
{
    public enum State { Intro, Play, Paused, Ended }
    public class Ev { public float t; public string kind, text; }
    public class Sample { public float t, z; public int tool, use, bleed, on; }
    public class Burn { public Vector3 pos, normal; public string kind; }
    public class Stats
    {
        public float washTime, washOnSrc, washAfterControl, cauteryTime, cauteryOn, cauteryOff, cauteryWrong, overtreat, bestClearView;
        public bool washRevealed; public float? firstCautery, tFound;
        public int bandAttempts, bandOnTarget, bandsUsed, bandsWasted, overshoots, toolSwitches;
        public HashSet<Tool> toolsUsed = new HashSet<Tool>();
    }
    public struct Inputs { public bool up, down, left, right, use; }

    public CaseDef Case { get; private set; }
    public float Difficulty { get; private set; } = 1f;     // bleed-rate multiplier for later loops
    public State state { get; private set; } = State.Intro;
    public float t, z, v, tipAng, tipVel, probeAng, loss, cauteryProg, bandCool, clearView;
    public bool bleeding = true;
    public float? controlledAt; public Tool? controlledBy;
    public int toolIdx, bandsLeft;
    public Stats stats = new Stats();
    public List<Ev> events = new List<Ev>();
    public List<Sample> samples = new List<Sample>();
    public List<Burn> burns = new List<Burn>();
    public BloodSim blood;
    public Tract tract;
    public Vector3 lesionPos, lesionNormal; public float lesionTh;
    public Vector3 tipPoint, tipNormal; public bool tipOnWall; public float tipDist = Mathf.Infinity, lesionDist = Mathf.Infinity;
    public bool aimed, washing, cauteryOn; public float bandFlash; public bool bandOk;
    public Inputs input; public bool armed;
    public Grade? grade;
    public CaseResult? result;
    public Tool tool => (Tool)toolIdx;

    public event Action<string, string, float> OnCue;      // text, kind (""|warn|bad|good), seconds
    public event Action OnToolChanged, OnStateChanged, OnControlled, OnBand, OnBurn;
    public event Action<Grade> OnEnded;

    float offAccum, bloodAcc, sampleAcc, sinceCue; bool overshootActive;
    readonly Dictionary<string, float> lastCue = new Dictionary<string, float>();
    readonly HashSet<string> landmarkSeen = new HashSet<string>();
    readonly float[] tapHold = new float[4];
    class Seg { public Tool tool; public float t0, dur, on, off; }
    Seg seg;
    public bool manualInput;      // when true, Update does not read the keyboard (tests and the touch pad drive `input`)
    public Camera cam;
    public float lossMultiplier = 1f;

    public void Begin(CaseDef c, Tract tract, Camera cam, float difficulty)
    {
        Case = c; this.tract = tract; this.cam = cam; Difficulty = difficulty;
        lesionTh = Anatomy.ClockToRad(c.lesionDeg);
        tract.WallPoint(lesionTh, c.lesionW, out lesionPos, out lesionNormal);
        blood = new BloodSim(c.lesionW, lesionTh, c.flowDir, c.emit * difficulty);
        tract.SetBloodTexture(blood.tex);
        Reset();
    }

    public void Reset()
    {
        t = 0; z = Case.startZ; v = 0; tipAng = Anatomy.ClockToRad(Case.startTipDeg); tipVel = 0; probeAng = tipAng; toolIdx = 0; loss = 0;
        bleeding = true; controlledAt = null; controlledBy = null; cauteryProg = 0; bandCool = 0; offAccum = 0; bandsLeft = GameConfig.BandsLoaded;
        bloodAcc = 0; sampleAcc = 0; stats = new Stats(); events.Clear(); samples.Clear(); burns.Clear(); seg = null; overshootActive = false; clearView = 0; lastCue.Clear(); landmarkSeen.Clear();
        washing = cauteryOn = aimed = false; bandFlash = 0; grade = null; result = null; armed = false; input = default;
        Array.Clear(blood.amt, 0, blood.amt.Length); Array.Clear(blood.fr, 0, blood.fr.Length); blood.t = 0;
        for (int i = 0; i < GameConfig.TrailWarmupTicks; i++) blood.Tick(0.1f, true);
        blood.UploadTexture();
        Log("start", $"Scope in the {Anatomy.RegionAt(z).name.ToLower()} ({Anatomy.Cm(z)} cm); fresh blood trail ahead");
        Log("tool", "Wash selected");
        state = State.Intro; OnStateChanged?.Invoke(); OnToolChanged?.Invoke();
    }

    public void Start_() { if (state == State.Intro) { state = State.Play; OnStateChanged?.Invoke(); } }
    public void TogglePause()
    {
        if (state == State.Play) { state = State.Paused; CloseSeg(); OnStateChanged?.Invoke(); }
        else if (state == State.Paused) { state = State.Play; armed = false; OnStateChanged?.Invoke(); }
    }
    public void ChangeTool(int dir)
    {
        if (state != State.Play) return;
        toolIdx = (toolIdx + dir + 3) % 3; armed = false; stats.toolSwitches++;
        Log("tool", tool + " selected"); CloseSeg(); OnToolChanged?.Invoke();
    }
    public void UseDown() { if (input.use) return; input.use = true; armed = true; if (state == State.Play && tool == Tool.Banding) AttemptBand(); }
    public void UseUp() { input.use = false; armed = false; }
    public void SignOff() { if (state == State.Play) End(false); }
    public void Tap(int which) { tapHold[which] = GameConfig.TapHold; }   // 0 up 1 down 2 left 3 right

    void Log(string kind, string text) => events.Add(new Ev { t = Mathf.Round(t * 100) / 100, kind = kind, text = text });
    void Cue(string text, string kind = "", float sec = 2.6f) => OnCue?.Invoke(text, kind, sec);
    void CueOnce(string key, float gap, string text, string kind = "", float sec = 2.6f)
    {
        if (lastCue.TryGetValue(key, out var last) && t - last < gap) return; lastCue[key] = t; Cue(text, kind, sec);
    }

    void Update()
    {
        if (!manualInput) ReadKeyboard();
        Step(Mathf.Min(0.05f, Time.deltaTime));
    }

    void ReadKeyboard()
    {
        var k = Keyboard.current; if (k == null) return;
        if (state == State.Play)
        {
            if (k.upArrowKey.wasPressedThisFrame) Tap(0); if (k.downArrowKey.wasPressedThisFrame) Tap(1);
            if (k.leftArrowKey.wasPressedThisFrame) Tap(2); if (k.rightArrowKey.wasPressedThisFrame) Tap(3);
            input.up = k.upArrowKey.isPressed; input.down = k.downArrowKey.isPressed; input.left = k.leftArrowKey.isPressed; input.right = k.rightArrowKey.isPressed;
            if (k.tabKey.wasPressedThisFrame) ChangeTool(k.shiftKey.isPressed ? -1 : 1);
            if (k.spaceKey.wasPressedThisFrame) UseDown();
            if (k.spaceKey.wasReleasedThisFrame) UseUp();
            if (k.enterKey.wasPressedThisFrame) SignOff();
        }
        else if (state == State.Intro)
        {
            if (k.spaceKey.wasPressedThisFrame || k.enterKey.wasPressedThisFrame) Start_();
        }
        if (k.pKey.wasPressedThisFrame || k.escapeKey.wasPressedThisFrame) { if (state == State.Play || state == State.Paused) TogglePause(); }
    }

    public Vector2 TipViewport(float ang)
    {
        float aspect = cam ? cam.aspect : 16f / 9f, r = GameConfig.TipScreenRadius * 0.5f;
        return new Vector2(0.5f + r * Mathf.Cos(ang) / aspect, 0.5f + r * Mathf.Sin(ang));
    }

    /// <summary>Advance the simulation by dt. Public so tests can drive it deterministically.</summary>
    public void Step(float dt)
    {
        if (state != State.Play) return;
        t += dt;
        // --- scope motion along the lumen
        bool upOn = input.up || tapHold[0] > 0, downOn = input.down || tapHold[1] > 0;
        for (int i = 0; i < 4; i++) tapHold[i] = Mathf.Max(0, tapHold[i] - dt);
        float tgt = (upOn ? 1 : 0) - (downOn ? 1 : 0), dv = GameConfig.ScopeAccel * dt, spd = GameConfig.ScopeSpeed * GameSettings.ScopeSpeedScale;
        v += Mathf.Clamp(tgt * spd - v, -dv, dv);
        float nz = z + v * dt;
        if (nz <= 0) { nz = 0; v = 0; if (tgt < 0) CueOnce("lim0", 3, "Upper oesophagus (20 cm): this simulation goes no higher.", "", 1.8f); }
        else if (nz >= Anatomy.ScopeMax) { nz = Anatomy.ScopeMax; v = 0; if (tgt > 0) CueOnce("limZ", 3, "End of D1 (superior duodenal flexure). The scope cannot go beyond the first part of the duodenum.", "", 2.6f); }
        z = nz;
        // --- tip rotation round the wall; the shaft follows with a lag
        bool lOn = input.left || tapHold[2] > 0, rOn = input.right || tapHold[3] > 0;
        float tgtS = (rOn ? -1 : 0) + (lOn ? 1 : 0);                 // → is clockwise on screen (y up => negative angle)
        float da = GameConfig.TipAccel * dt;
        tipVel += Mathf.Clamp(tgtS * GameConfig.TipSpeed - tipVel, -da, da);
        tipAng = Mathf.Repeat(tipAng + tipVel * dt, 2 * Mathf.PI);
        probeAng += Mathf.DeltaAngle(probeAng * Mathf.Rad2Deg, tipAng * Mathf.Rad2Deg) * Mathf.Deg2Rad * (1 - Mathf.Exp(-dt / GameConfig.ProbeLag));
        foreach (var lm in Anatomy.Landmarks)
            if (!landmarkSeen.Contains(lm.name) && Mathf.Abs(z - lm.w) < 0.3f) { landmarkSeen.Add(lm.name); Log("landmark", lm.name); CueOnce("lm", 1.5f, $"{lm.name} · {Anatomy.Cm(lm.w)} cm", "", 1.7f); }

        // --- where the tip meets the wall
        PlaceCamera();
        var ray = cam.ViewportPointToRay(TipViewport(tipAng));
        tipOnWall = tract.Raycast(ray.origin, ray.direction, out var hit, 8f);
        if (tipOnWall) { tipPoint = hit.point; tipNormal = hit.normal; tipDist = hit.distance; } else { tipPoint = ray.origin + ray.direction * 3f; tipNormal = -ray.direction; tipDist = 3f; }
        lesionDist = tipOnWall ? Vector3.Distance(tipPoint, lesionPos) : Mathf.Infinity;
        float lesionDepth = Case.lesionW - z;
        bool lesionVisible = lesionDepth > 0.25f && lesionDepth < 7f && Vector3.Dot(cam.transform.forward, (lesionPos - cam.transform.position).normalized) > 0.2f;
        aimed = lesionDist <= GameConfig.CauteryTol + Case.lesionRadius * 0.5f;
        if (stats.tFound == null && lesionDist <= GameConfig.CauteryTol * 1.6f + Case.lesionRadius * 0.5f) { stats.tFound = t; Log("found", "Source brought under the instrument tip"); }
        if (!overshootActive && lesionDepth < 0.55f) { overshootActive = true; stats.overshoots++; Log("overshoot", "Passed the lesion"); CueOnce("over", 2, "Passed the lesion. Withdraw (↓) to find it again.", "warn", 2.6f); }
        else if (overshootActive && lesionDepth > 1.1f) overshootActive = false;

        // --- attachments
        bool active = input.use && armed;
        washing = cauteryOn = false; bandCool = Mathf.Max(0, bandCool - dt); bandFlash = Mathf.Max(0, bandFlash - dt);
        float cover = blood.CoverAt(lesionTh, Case.lesionW, Case.lesionRadius);
        Tool? cur = active && (tool == Tool.Wash || tool == Tool.Cautery) ? tool : (Tool?)null;
        if (seg == null || cur == null || seg.tool != cur) { CloseSeg(); if (cur != null) seg = new Seg { tool = cur.Value, t0 = t }; }
        if (cur != null) { seg.dur += dt; stats.toolsUsed.Add(cur.Value); }
        if (cur == Tool.Wash)
        {
            washing = true; stats.washTime += dt;
            if (tipOnWall) blood.Wash(Anatomy.AngleOf(tipPoint), tipPoint.z, GameConfig.WashRadius, dt);
            if (controlledAt != null) stats.washAfterControl += dt;
            if (lesionDist <= GameConfig.WashTol + Case.lesionRadius)
            {
                stats.washOnSrc += dt; seg.on += dt;
                if (bleeding && cover < 0.55f)
                {
                    if (!stats.washRevealed) { stats.washRevealed = true; Log("reveal", "Fresh blood from the " + Case.sourceName + ": active bleed confirmed"); }
                    CueOnce("well", 5, Case.cueReveal, "bad", 2.6f);
                }
            }
        }
        else if (cur == Tool.Cautery)
        {
            cauteryOn = true; stats.cauteryTime += dt; stats.firstCautery ??= t;
            if (aimed && !Case.cauteryOk) { stats.cauteryWrong += dt; seg.off += dt; CueOnce("wrongc", 4, Case.cueCautery, "bad", 3.2f); }
            else if (aimed)
            {
                stats.cauteryOn += dt; seg.on += dt;
                if (bleeding) { cauteryProg += dt; if (cauteryProg >= GameConfig.CauteryContactSec) Control(Tool.Cautery); }
                else { stats.overtreat += dt; if (stats.overtreat > 2.5f) CueOnce("over-t", 4, "Enough: further cautery risks deep thermal injury.", "warn", 2.2f); }
            }
            else
            {
                stats.cauteryOff += dt; seg.off += dt; offAccum += dt; cauteryProg = Mathf.Max(0, cauteryProg - dt * GameConfig.CauteryDecay);
                if (offAccum > 0.5f) { offAccum = 0; AddBurn("burn"); }
                if (bleeding) CueOnce("off", 4, "Cautery away from the lesion: it is still bleeding.", "warn", 2.2f);
            }
        }
        if (cur != Tool.Cautery) cauteryProg = Mathf.Max(0, cauteryProg - dt * GameConfig.CauteryDecay);

        // --- blood and the patient
        bloodAcc += dt; while (bloodAcc >= 0.1f) { blood.Tick(0.1f, bleeding); bloodAcc -= 0.1f; }
        if (bleeding) loss += Case.bleedMl * Difficulty * lossMultiplier * dt;
        if (controlledAt != null && lesionVisible && lesionDepth > 0.8f && cover < GameConfig.ClearCover) { clearView += dt; stats.bestClearView = Mathf.Max(stats.bestClearView, clearView); }
        else clearView = 0;
        sampleAcc += dt; if (sampleAcc >= 0.2f) { sampleAcc = 0; samples.Add(new Sample { t = t, z = z, tool = toolIdx, use = cur != null ? 1 : 0, bleed = bleeding ? 1 : 0, on = aimed ? 1 : 0 }); }
        if (loss >= GameConfig.LossLimitMl) End(true);
    }

    public void PlaceCamera()
    {
        if (!cam) return;
        var pos = Anatomy.Axis(z);
        float c = Time.time;
        pos += new Vector3(0.012f * Mathf.Sin(c * 1.3f), 0.01f * Mathf.Sin(c * 0.93f + 1), 0);
        cam.transform.position = pos;
        cam.transform.rotation = Quaternion.LookRotation(Anatomy.Tangent(z), Vector3.up);
    }

    void CloseSeg()
    {
        if (seg == null) return;
        if (seg.dur > 0.15f)
        {
            string text = seg.tool == Tool.Wash ? $"Wash {seg.dur:F1} s, {seg.on:F1} s on the source"
                : $"Cautery {seg.dur:F1} s, {seg.on:F1} s on target" + (seg.off > 0.2f ? $", {seg.off:F1} s off target" : "");
            events.Add(new Ev { t = Mathf.Round(seg.t0 * 100) / 100, kind = seg.tool == Tool.Wash ? "wash" : "cautery", text = text });
        }
        seg = null;
    }

    void AttemptBand()
    {
        if (bandCool > 0) return;
        bool on = lesionDist <= GameConfig.BandTol + Case.lesionRadius;
        if (Case.bandingOk)
        {
            if (bandsLeft <= 0) { Cue("No bands left on the cap.", "warn"); return; }
            bandCool = GameConfig.BandCooldown; stats.bandAttempts++; bandFlash = 0.6f; OnBand?.Invoke();
            if (on && !bleeding) { Cue("That varix is already banded."); Log("band", "Extra band attempted on an already banded varix"); return; }
            bandsLeft--; OnToolChanged?.Invoke();
            if (on) { stats.bandsUsed++; stats.bandOnTarget++; bandOk = true; Control(Tool.Banding); }
            else { stats.bandsWasted++; AddBurn("band"); Log("band", "Band fired away from the varix: wasted"); Cue(Case.cueBandingMiss, "warn", 3f); }
            return;
        }
        bandCool = GameConfig.BandCooldown; stats.bandAttempts++; bandFlash = 0.6f; bandOk = false; OnBand?.Invoke();
        if (on) stats.bandOnTarget++;
        Log("band", on ? "Banding attempted on the ulcer: not capturable" : "Banding attempted away from the lesion");
        Cue(on ? Case.cueBanding : Case.cueBandingMiss, "warn", 3.2f);
    }

    void Control(Tool by)
    {
        bleeding = false; controlledAt = t; controlledBy = by; cauteryProg = 0;
        Log("control", $"Bleeding controlled by {by.ToString().ToLower()}");
        Cue(by == Tool.Banding ? "Band deployed: the varix is strangulated and the bleeding has stopped." : "Contact complete. Fresh flow has stopped.", "good", 3.2f);
        OnControlled?.Invoke();
    }

    void AddBurn(string kind)
    {
        if (!tipOnWall) return;
        burns.Add(new Burn { pos = tipPoint, normal = tipNormal, kind = kind }); if (burns.Count > 14) burns.RemoveAt(0);
        OnBurn?.Invoke();
    }

    public float Cover => blood.CoverAt(lesionTh, Case.lesionW, Case.lesionRadius);

    Grade GradeSignOff()
    {
        if (bleeding) return Grade.Unsafe;
        bool verified = stats.bestClearView >= GameConfig.ClearViewSecToSignOff && Cover < GameConfig.ClearCover + 0.07f;
        return verified ? Grade.Good : Grade.Unverified;
    }

    void End(bool haemorrhage)
    {
        if (state == State.Ended) return;
        CloseSeg();
        grade = haemorrhage ? Grade.Haemorrhage : GradeSignOff();
        Log(haemorrhage ? "death" : "signoff", haemorrhage ? "Patient decompensated from blood loss" : "Signed off the site");
        state = State.Ended; washing = cauteryOn = false; input = default; armed = false;
        result = Scoring.Score(grade.Value, t, loss, Case.parSec / Mathf.Sqrt(Difficulty));
        OnStateChanged?.Invoke(); OnEnded?.Invoke(grade.Value);
    }

    public (int hr, int sbp, int dbp) Vitals()
    {
        int hr = Mathf.RoundToInt(76 + loss * 0.058f + (bleeding ? 3 : 0)), sbp = Mathf.RoundToInt(124 - loss * 0.043f);
        return (hr, sbp, Mathf.RoundToInt(sbp * 0.62f));
    }
    public static string Mmss(float s) => $"{Mathf.FloorToInt(s / 60)}:{Mathf.FloorToInt(s % 60):00}";

    /// <summary>The debrief lines, adapted to the case and to the attachments actually used.</summary>
    public List<(string tag, string text, Color color)> Feedback()
    {
        var o = new List<(string, string, Color)>(); var s = stats; var c = Case; string src = c.sourceName;
        string f1(float x) => x.ToString("F1");
        string found = s.tFound == null ? "You never brought a tool tip onto the lesion." : $"You got the {src} under the tip at {Mmss(s.tFound.Value)}.";
        o.Add(("Finding the bleed", found + (s.overshoots > 0 ? $" You overshot {s.overshoots}×; short taps of ↑/↓ and ←/→ give finer control near the source." : " No overshoots."), UIKit.Fg));
        if (s.toolsUsed.Contains(Tool.Wash))
            o.Add(("Wash", $"{f1(s.washTime)} s of washing, {f1(s.washOnSrc)} s directed at the source. " +
                (s.washRevealed ? c.noteWash : (s.washOnSrc > 0 ? "You washed the site but never saw it bleed before treating." : "The jet never reached the source, so it stayed hidden.")) +
                (s.washAfterControl > 0.5f ? $" {f1(s.washAfterControl)} s of washing after control cleared the residual blood." : (controlledAt != null ? " You did not wash after control, so old blood still covered the site." : "")), UIKit.Wash));
        else o.Add(("Wash", $"You never washed. Blood covered the {src}, so you could not see the source or confirm control. Wash first, treat, then wash again.", UIKit.Wash));
        void Caut()
        {
            if (c.cauteryOk)
            {
                if (s.toolsUsed.Contains(Tool.Cautery))
                {
                    int burnN = burns.FindAll(b => b.kind == "burn").Count;
                    string to = controlledBy == Tool.Cautery && s.firstCautery != null ? f1(controlledAt.Value - s.firstCautery.Value) : null;
                    o.Add(("Cautery", $"{f1(s.cauteryOn)} s on target (the case needs {GameConfig.CauteryContactSec} s of continuous contact)" +
                        (s.cauteryOff > 0.2f ? $", {f1(s.cauteryOff)} s off target ({burnN} stray burn mark{(burnN == 1 ? "" : "s")})" : "") + ". " +
                        (controlledBy == Tool.Cautery ? $"Flow stopped {to} s after you first fired the probe. {c.noteCautery}" : "The lesion was still bleeding when you finished.") +
                        (s.overtreat > 1 ? $" {f1(s.overtreat)} s of extra cautery after control adds perforation risk." : ""), UIKit.Cautery));
                }
                else o.Add(("Cautery", controlledAt == null ? $"You never used cautery, which is the intended treatment for this {src}." : "No cautery used.", UIKit.Cautery));
            }
            else if (s.toolsUsed.Contains(Tool.Cautery)) o.Add(("Cautery", $"{f1(s.cauteryWrong)} s of cautery on the {src}. {c.noteCautery}", UIKit.Cautery));
        }
        void Band()
        {
            if (c.bandingOk)
            {
                if (s.bandAttempts > 0) o.Add(("Banding", $"{s.bandsUsed} band{(s.bandsUsed == 1 ? "" : "s")} on the varix" + (s.bandsWasted > 0 ? $", {s.bandsWasted} wasted away from it (each leaves a nodule of normal mucosa)" : "") + ". " +
                    (controlledBy == Tool.Banding ? $"Bleeding stopped at {Mmss(controlledAt.Value)}. {c.noteBanding}" : "The varix was still bleeding when you finished."), UIKit.Banding));
                else o.Add(("Banding", $"You never banded. {c.noteBanding}", UIKit.Banding));
            }
            else if (s.bandAttempts > 0) o.Add(("Banding", $"{s.bandAttempts} attempt{(s.bandAttempts > 1 ? "s" : "")}" + (s.bandOnTarget > 0 ? $" on the {src}. {c.noteBanding} Switching straight back to cautery is the right call." : ", none of them on the lesion."), UIKit.Banding));
        }
        if (c.intended == Tool.Banding) { Band(); Caut(); } else { Caut(); Band(); }
        var g = grade ?? Grade.Unsafe;
        if (g == Grade.Good) o.Add(("Sign-off", $"You saw the site clear for {f1(s.bestClearView)} s after control, then signed off. That is the reinspection step." + (blood.Total() > 8 ? " Some old blood still pools along the wall; a wider wash would leave a cleaner field." : ""), UIKit.Ok));
        else if (g == Grade.Unverified) o.Add(("Sign-off", controlledAt != null && s.bestClearView > 0 ? $"You saw the clear site for only {f1(s.bestClearView)} s (need {GameConfig.ClearViewSecToSignOff} s). Wash again, look, and wait for stability." : $"Blood still hid the {src} at sign-off. After treatment, wash the site and watch it for a few seconds before leaving.", UIKit.Warn));
        else if (g == Grade.Unsafe) o.Add(("Sign-off", "Never sign off an actively bleeding lesion. Use the wash to confirm the source, then treat it.", UIKit.Bad));
        else o.Add(("Time", $"This bleed costs about {Mathf.RoundToInt(c.bleedMl * Difficulty)} mL every second. Wash to find the source quickly, then go straight to {c.intended.ToString().ToLower()}.", UIKit.Bad));
        if (s.toolsUsed.Count == 0 && s.bandAttempts == 0) o.Add(("Tools", "You did not use any attachment.", UIKit.Fg));
        return o;
    }
}
