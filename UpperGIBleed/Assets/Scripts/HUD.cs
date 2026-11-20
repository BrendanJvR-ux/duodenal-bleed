using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>The in-procedure interface: vitals, attachment selector, cues, pause, case intro, debrief and the touch pad.</summary>
public class HUD : MonoBehaviour
{
    Procedure p; Canvas canvas; GameFlow flow;
    UIKit.Txt tTime, tHR, tBP, tLoss, tToolLabel, tCue, tRegion, tCase, tScore;
    Image cuePanel, hrChip, bpChip, lossChip; Image[] chips = new Image[3]; UIKit.Txt[] chipTxt = new UIKit.Txt[3];
    GameObject intro, pause, debrief, pad, progressRing; Image progressFill;
    float cueTimer; Image vignette; CanvasGroup debriefGroup;
    RectTransform padRoot;

    public static HUD Create(Procedure p, GameFlow flow)
    {
        var go = new GameObject("HUD"); var h = go.AddComponent<HUD>(); h.p = p; h.flow = flow; h.Build(); return h;
    }

    void Build()
    {
        canvas = UIKit.CreateCanvas("HUDCanvas", 10); canvas.transform.SetParent(transform, false);
        var root = canvas.transform;
        // vignette: pulses red when the patient is unstable
        vignette = UIKit.PanelEl(root, "Vignette", new Color(0.4f, 0, 0, 0), false); vignette.raycastTarget = false;
        // top bar
        var top = new GameObject("Top", typeof(RectTransform)); top.transform.SetParent(root, false);
        UIKit.Rect(top, new Vector2(0, 1), new Vector2(1, 1), new Vector2(16, -52), new Vector2(-16, -8));
        tCase = UIKit.Label(top.transform, "", 17, UIKit.Fg, TextAlignmentOptions.Left, true); UIKit.Rect(tCase.go, new Vector2(0, 0), new Vector2(0.55f, 1), Vector2.zero, Vector2.zero);
        var chipRow = UIKit.HStack(top.transform, 6, TextAnchor.MiddleRight); UIKit.Rect(chipRow.gameObject, new Vector2(0.45f, 0), new Vector2(1, 1), Vector2.zero, Vector2.zero);
        Image Chip(string label, out UIKit.Txt val, float w)
        {
            var c = UIKit.PanelEl(chipRow.transform, label, UIKit.Panel); UIKit.Outline(c, UIKit.Line); UIKit.Size(c.gameObject, w, 30);
            var l = UIKit.Label(c.transform, label, 10, UIKit.Muted, TextAlignmentOptions.Left); UIKit.Rect(l.go, Vector2.zero, Vector2.one, new Vector2(9, 0), new Vector2(-4, 0));
            val = UIKit.Label(c.transform, "", 13, UIKit.Fg, TextAlignmentOptions.Right, true); UIKit.Rect(val.go, Vector2.zero, Vector2.one, new Vector2(4, 0), new Vector2(-9, 0));
            return c;
        }
        Chip("TIME", out tTime, 92); hrChip = Chip("HR", out tHR, 76); bpChip = Chip("BP", out tBP, 104); lossChip = Chip("LOSS", out tLoss, 110);
        var sc = UIKit.PanelEl(chipRow.transform, "Score", UIKit.Panel); UIKit.Outline(sc, UIKit.Line); UIKit.Size(sc.gameObject, 118, 30);
        tScore = UIKit.Label(sc.transform, "", 12, UIKit.Coffee, TextAlignmentOptions.Center, true);
        // region label (top centre of the view)
        tRegion = UIKit.Label(root, "", 13, UIKit.A(UIKit.Fg, 0.85f), TextAlignmentOptions.Center, true); UIKit.Rect(tRegion.go, new Vector2(0.3f, 1), new Vector2(0.7f, 1), new Vector2(0, -92), new Vector2(0, -58));
        // cue
        cuePanel = UIKit.PanelEl(root, "Cue", UIKit.A(UIKit.Bg, 0.88f)); UIKit.Outline(cuePanel, UIKit.Line);
        UIKit.Rect(cuePanel.gameObject, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-260, 112), new Vector2(260, 160));
        tCue = UIKit.Label(cuePanel.transform, "", 14, UIKit.Fg, TextAlignmentOptions.Center); UIKit.Fill(tCue.go, 8); cuePanel.gameObject.SetActive(false);
        // progress ring for cautery contact (a simple bar under the cue area)
        progressRing = UIKit.PanelEl(root, "Progress", UIKit.A(UIKit.Panel2, 0.8f)).gameObject; UIKit.Rect(progressRing, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-90, 166), new Vector2(90, 174));
        progressFill = UIKit.PanelEl(progressRing.transform, "Fill", UIKit.Cautery); progressFill.rectTransform.anchorMax = new Vector2(0, 1); progressRing.SetActive(false);
        // bottom: tool selector
        var bottom = new GameObject("Bottom", typeof(RectTransform)); bottom.transform.SetParent(root, false);
        UIKit.Rect(bottom, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-300, 14), new Vector2(300, 100));
        var tools = UIKit.HStack(bottom.transform, 8, TextAnchor.MiddleCenter, true); UIKit.Rect(tools.gameObject, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -40), new Vector2(0, 0));
        string[] names = { "Wash", "Cautery", "Banding" }; Color[] cols = { UIKit.Wash, UIKit.Cautery, UIKit.Banding };
        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            chips[i] = UIKit.PanelEl(tools.transform, names[i], UIKit.Panel); UIKit.Outline(chips[i], UIKit.Line);
            var b = chips[i].gameObject.AddComponent<Button>(); b.targetGraphic = chips[i]; b.onClick.AddListener(() => { if (p.state == Procedure.State.Play && idx != p.toolIdx) p.ChangeTool(idx - p.toolIdx); });
            chipTxt[i] = UIKit.Label(chips[i].transform, "<color=#" + ColorUtility.ToHtmlStringRGB(cols[i]) + ">●</color>  " + names[i], 14, UIKit.Muted, TextAlignmentOptions.Center, true);
        }
        tToolLabel = UIKit.Label(bottom.transform, "", 15, UIKit.Wash, TextAlignmentOptions.Center, true); UIKit.Rect(tToolLabel.go, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 22), new Vector2(0, 46));
        var hint = UIKit.Label(bottom.transform, "↑↓ forward / back · ←→ rotate tip · Tab attachment · Space use · Enter sign off · P pause", 11, UIKit.Muted, TextAlignmentOptions.Center); UIKit.Rect(hint.go, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 20));
        // pause overlay
        pause = Overlay(root, "Paused", "The bleeding is frozen while paused.", ("Resume (P)", () => p.TogglePause(), UIKit.Ok), ("Quit to menu", () => flow.QuitRun("quit"), UIKit.Bad));
        pause.SetActive(false);
        // intro card
        intro = new GameObject("Intro", typeof(RectTransform)); intro.transform.SetParent(root, false); UIKit.Fill(intro);
        var ibg = UIKit.PanelEl(intro.transform, "Dim", new Color(0, 0, 0, 0.55f), false);
        var card = UIKit.PanelEl(intro.transform, "Card", UIKit.A(UIKit.Panel, 0.97f)); UIKit.Outline(card, UIKit.Line); UIKit.Place(card.gameObject, new Vector2(0.5f, 0.5f), new Vector2(560, 300));
        var iv = UIKit.VStack(card.transform, 8, new RectOffset(22, 22, 18, 18));
        var c = p.Case;
        UIKit.Size(UIKit.Label(iv.transform, $"Case {flow.CaseNumber}: {c.title}", 22, UIKit.Fg, TextAlignmentOptions.Center, true).go, -1, 32);
        UIKit.Size(UIKit.Label(iv.transform, c.where, 13, UIKit.Ok, TextAlignmentOptions.Center).go, -1, 22);
        UIKit.Size(UIKit.Label(iv.transform, c.vignette, 14, UIKit.A(UIKit.Fg, 0.9f), TextAlignmentOptions.Center).go, -1, 110);
        UIKit.Size(UIKit.Label(iv.transform, flow.Loop > 0 ? $"Round {flow.Loop + 1}: the bleeding is brisker this time." : "Find the bleed, treat it with the right attachment, wash and re-inspect, then sign off.", 12, UIKit.Muted, TextAlignmentOptions.Center).go, -1, 24);
        var startBtn = UIKit.Button(iv.transform, "Start (Space)", () => p.Start_(), UIKit.Hex("1f6f54"), UIKit.Fg, 17); UIKit.Size(startBtn.gameObject, -1, 44);
        // touch pad
        BuildPad(root);
        // events
        p.OnCue += ShowCue; p.OnToolChanged += RefreshTool; p.OnStateChanged += RefreshState; p.OnEnded += ShowDebrief;
        RefreshTool(); RefreshState(); tCase.text = $"Case {flow.CaseNumber} · {c.title}";
    }

    GameObject Overlay(Transform root, string title, string body, params (string label, Action act, Color col)[] buttons)
    {
        var go = new GameObject(title, typeof(RectTransform)); go.transform.SetParent(root, false); UIKit.Fill(go);
        UIKit.PanelEl(go.transform, "Dim", new Color(0, 0, 0, 0.6f), false);
        var card = UIKit.PanelEl(go.transform, "Card", UIKit.A(UIKit.Panel, 0.97f)); UIKit.Outline(card, UIKit.Line); UIKit.Place(card.gameObject, new Vector2(0.5f, 0.5f), new Vector2(380, 190));
        var v = UIKit.VStack(card.transform, 10, new RectOffset(20, 20, 16, 16));
        UIKit.Size(UIKit.Label(v.transform, title, 22, UIKit.Fg, TextAlignmentOptions.Center, true).go, -1, 30);
        UIKit.Size(UIKit.Label(v.transform, body, 13, UIKit.Muted, TextAlignmentOptions.Center).go, -1, 40);
        var row = UIKit.HStack(v.transform, 10, TextAnchor.MiddleCenter, true); UIKit.Size(row.gameObject, -1, 42);
        foreach (var (label, act, col) in buttons) UIKit.Button(row.transform, label, act, col == UIKit.Ok ? UIKit.Hex("1f6f54") : col == UIKit.Bad ? UIKit.Hex("7a2a28") : UIKit.Panel2, UIKit.Fg, 15);
        return go;
    }

    void BuildPad(Transform root)
    {
        pad = new GameObject("Pad", typeof(RectTransform)); pad.transform.SetParent(root, false); UIKit.Fill(pad);
        Button PadBtn(string label, Vector2 anchor, Vector2 pos, Vector2 size, Action down, Action up)
        {
            var b = UIKit.Button(pad.transform, label, null, UIKit.A(UIKit.Panel2, 0.85f), UIKit.Fg, 20); UIKit.Place(b.gameObject, anchor, size, pos);
            UIKit.Hold(b.gameObject, down, up); return b;
        }
        // left cluster: scope. right cluster: tools + use
        PadBtn("▲", new Vector2(0, 0), new Vector2(86, 150), new Vector2(60, 56), () => { p.input.up = true; p.Tap(0); }, () => p.input.up = false);
        PadBtn("▼", new Vector2(0, 0), new Vector2(86, 30), new Vector2(60, 56), () => { p.input.down = true; p.Tap(1); }, () => p.input.down = false);
        PadBtn("◀", new Vector2(0, 0), new Vector2(22, 90), new Vector2(60, 56), () => { p.input.left = true; p.Tap(2); }, () => p.input.left = false);
        PadBtn("▶", new Vector2(0, 0), new Vector2(150, 90), new Vector2(60, 56), () => { p.input.right = true; p.Tap(3); }, () => p.input.right = false);
        PadBtn("Tool", new Vector2(1, 0), new Vector2(-170, 150), new Vector2(84, 50), () => p.ChangeTool(1), () => { });
        PadBtn("USE", new Vector2(1, 0), new Vector2(-30, 30), new Vector2(120, 110), () => p.UseDown(), () => p.UseUp());
        PadBtn("Sign off", new Vector2(1, 0), new Vector2(-170, 30), new Vector2(84, 50), () => { }, () => p.SignOff());
        PadBtn("II", new Vector2(1, 0), new Vector2(-170, 90), new Vector2(84, 50), () => { }, () => p.TogglePause());
        pad.SetActive(GameSettings.ShowTouchPad);
        p.manualInput = false;      // keyboard still works alongside the pad
    }

    void ShowCue(string text, string kind, float sec)
    {
        tCue.text = text; cuePanel.gameObject.SetActive(true); cueTimer = sec;
        UIKit.Outline(cuePanel, kind == "bad" ? UIKit.Bad : kind == "warn" ? UIKit.Warn : kind == "good" ? UIKit.Ok : UIKit.Line);
        if (kind == "bad") GameAudio.I?.Bad(); else if (kind == "warn") GameAudio.I?.Alarm();
    }

    void RefreshTool()
    {
        Color[] cols = { UIKit.Wash, UIKit.Cautery, UIKit.Banding }; string[] verbs = { "Hold", "Hold", "Tap" };
        for (int i = 0; i < 3; i++)
        {
            bool sel = i == p.toolIdx;
            chips[i].color = sel ? Color.Lerp(UIKit.Panel, cols[i], 0.25f) : UIKit.Panel; UIKit.Outline(chips[i], sel ? cols[i] : UIKit.Line, sel ? 2 : 1.5f);
            chipTxt[i].color = sel ? UIKit.Fg : UIKit.Muted;
        }
        tToolLabel.color = cols[p.toolIdx];
        tToolLabel.text = p.tool.ToString().ToUpper() + " · " + verbs[p.toolIdx] + " Space" + (p.tool == Tool.Banding && p.Case.bandingOk ? $" · {p.bandsLeft} band{(p.bandsLeft == 1 ? "" : "s")} left" : "");
    }

    void RefreshState()
    {
        intro.SetActive(p.state == Procedure.State.Intro);
        pause.SetActive(p.state == Procedure.State.Paused);
        if (debrief) debrief.SetActive(p.state == Procedure.State.Ended);
        if (pad) pad.SetActive(GameSettings.ShowTouchPad && p.state == Procedure.State.Play);
    }

    void Update()
    {
        if (p == null) return;
        var vs = p.Vitals();
        tTime.text = Procedure.Mmss(p.t); tHR.text = vs.hr.ToString(); tBP.text = $"{vs.sbp}/{vs.dbp}"; tLoss.text = $"{Mathf.RoundToInt(p.loss)} mL";
        UIKit.Outline(hrChip, vs.hr > 130 ? UIKit.Bad : vs.hr > 105 ? UIKit.Warn : UIKit.Line); tHR.color = vs.hr > 130 ? UIKit.Bad : vs.hr > 105 ? UIKit.Warn : UIKit.Fg;
        UIKit.Outline(bpChip, vs.sbp < 80 ? UIKit.Bad : vs.sbp < 100 ? UIKit.Warn : UIKit.Line); tBP.color = vs.sbp < 80 ? UIKit.Bad : vs.sbp < 100 ? UIKit.Warn : UIKit.Fg;
        UIKit.Outline(lossChip, p.loss > 1000 ? UIKit.Bad : p.loss > 500 ? UIKit.Warn : UIKit.Line); tLoss.color = p.loss > 1000 ? UIKit.Bad : p.loss > 500 ? UIKit.Warn : UIKit.Fg;
        tScore.text = $"☕ {flow.RunTokens}  ·  {flow.RunScore} pts";
        var reg = Anatomy.RegionAt(p.z);
        int clock = Mathf.RoundToInt(Mathf.Repeat(90 - p.tipAng * Mathf.Rad2Deg, 360) / 30); if (clock == 0) clock = 12;
        tRegion.text = $"{reg.name.ToUpper()}\n<size=11><color=#aabccd>{Anatomy.Cm(p.z)} cm from incisors · tip at {clock} o'clock</color></size>";
        if (cueTimer > 0) { cueTimer -= Time.deltaTime; if (cueTimer <= 0) cuePanel.gameObject.SetActive(false); }
        bool showProg = p.tool == Tool.Cautery && p.cauteryProg > 0.01f;
        progressRing.SetActive(showProg); if (showProg) progressFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(p.cauteryProg / GameConfig.CauteryContactSec), 1);
        if (p.state == Procedure.State.Play)
        {
            GameAudio.I?.Heartbeat(vs.hr, Time.deltaTime, vs.hr > 130);
            float unstable = GameSettings.Effects ? Mathf.Clamp01((p.loss - 700) / 800f) : 0;
            vignette.color = new Color(0.5f, 0, 0, unstable * (0.25f + 0.2f * Mathf.Sin(Time.time * (vs.hr / 60f) * 2 * Mathf.PI)));
        }
        else vignette.color = Color.clear;
    }

    void ShowDebrief(Grade grade)
    {
        var r = p.result.Value; var o = grade switch
        {
            Grade.Good => ("SAFE SIGN-OFF", "Haemostasis secured", "You controlled the bleed, washed the site clear and re-inspected it before signing off.", UIKit.Ok),
            Grade.Unverified => ("SIGN-OFF NOT VERIFIED", "Bleeding stopped, but you could not confirm it", "Flow had stopped, but the site was not seen clear and stable for long enough. Residual blood can hide a re-bleed.", UIKit.Warn),
            Grade.Unsafe => ("UNSAFE SIGN-OFF", "Signed off while still bleeding", "Fresh blood was still coming from the lesion when you ended the procedure. The patient re-bled on the ward.", UIKit.Bad),
            _ => ("PATIENT UNSTABLE", "Decompensated from blood loss", $"Blood loss reached {GameConfig.LossLimitMl:0} mL before the bleed was controlled.", UIKit.Bad),
        };
        if (r.Fail) GameAudio.I?.Bad(); else GameAudio.I?.Token();
        debrief = new GameObject("Debrief", typeof(RectTransform)); debrief.transform.SetParent(canvas.transform, false); UIKit.Fill(debrief);
        UIKit.PanelEl(debrief.transform, "Dim", new Color(0.02f, 0.03f, 0.04f, 0.9f), false);
        var card = UIKit.PanelEl(debrief.transform, "Card", UIKit.Panel); UIKit.Outline(card, UIKit.Line);
        UIKit.Rect(card.gameObject, new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(-350, 12), new Vector2(350, -12));
        card.gameObject.AddComponent<RectMask2D>();
        var vl = card.gameObject.AddComponent<VerticalLayoutGroup>(); vl.spacing = 7; vl.padding = new RectOffset(22, 22, 16, 16); vl.childAlignment = TextAnchor.UpperCenter;
        vl.childControlWidth = true; vl.childControlHeight = true; vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
        var v = vl;
        var badge = UIKit.PanelEl(v.transform, "Badge", UIKit.A(o.Item4, 0.18f)); UIKit.Size(badge.gameObject, -1, 26); UIKit.Label(badge.transform, o.Item1, 12, o.Item4, TextAlignmentOptions.Center, true);
        UIKit.Size(UIKit.Label(v.transform, o.Item2, 22, UIKit.Fg, TextAlignmentOptions.Left, true).go, -1, 32);
        UIKit.Size(UIKit.Label(v.transform, o.Item3, 13, UIKit.A(UIKit.Fg, 0.85f)).go, -1, 40);
        // score row
        var srow = UIKit.HStack(v.transform, 8, TextAnchor.MiddleCenter, true); UIKit.Size(srow.gameObject, -1, 64);
        void Stat(string label, string val, Color col)
        {
            var box = UIKit.PanelEl(srow.transform, label, UIKit.Panel2); var l = UIKit.Label(box.transform, label, 11, UIKit.Muted, TextAlignmentOptions.Top); UIKit.Fill(l.go, 6);
            var t = UIKit.Label(box.transform, val, 20, col, TextAlignmentOptions.Bottom, true); UIKit.Fill(t.go, 6);
        }
        Stat("Case score", $"{r.score} / 1000", r.Fail ? UIKit.Bad : UIKit.Fg); Stat("Coffee tokens", r.Fail ? "☕ 0" : $"☕ {r.tokens}", r.Fail ? UIKit.Bad : UIKit.Coffee);
        Stat("Time", $"{Procedure.Mmss(p.t)} (par {Procedure.Mmss(r.par)})", r.TimeFactor > 0.5f ? UIKit.Fg : UIKit.Warn); Stat("Blood loss", $"{Mathf.RoundToInt(p.loss)} mL", r.BloodFactor > 0.5f ? UIKit.Fg : UIKit.Warn);
        var grid = UIKit.HStack(v.transform, 8, TextAnchor.MiddleCenter, true); UIKit.Size(grid.gameObject, -1, 50);
        void Small(string label, string val) { var box = UIKit.PanelEl(grid.transform, label, UIKit.Panel2); var l = UIKit.Label(box.transform, label, 10, UIKit.Muted, TextAlignmentOptions.Top); UIKit.Fill(l.go, 5); var t = UIKit.Label(box.transform, val, 14, UIKit.Fg, TextAlignmentOptions.Bottom, true); UIKit.Fill(t.go, 5); }
        Small("Bleed control", p.controlledAt == null ? "not achieved" : Procedure.Mmss(p.controlledAt.Value)); Small("Source found", p.stats.tFound == null ? "never" : Procedure.Mmss(p.stats.tFound.Value));
        Small("Tools used", string.Join(", ", p.stats.toolsUsed) + (p.stats.bandAttempts > 0 ? (p.stats.toolsUsed.Count > 0 ? ", " : "") + "Banding" : "") is var tu && tu.Length > 0 ? tu : "none"); Small("Overshoots", p.stats.overshoots.ToString());
        foreach (var (tag, text, col) in p.Feedback())
        {
            var l = UIKit.Label(v.transform, $"<b><color=#{ColorUtility.ToHtmlStringRGB(col)}>{tag}</color></b>  {text}", 13, UIKit.A(UIKit.Fg, 0.92f));
            var le = l.go.AddComponent<LayoutElement>(); le.preferredHeight = Mathf.Max(22, 18 * Mathf.CeilToInt((text.Length + tag.Length) / 88f));
        }
        var tl = UIKit.Label(v.transform, "<b>Timeline</b>  " + string.Join("  ·  ", p.events.ConvertAll(e => $"{Procedure.Mmss(e.t)} {e.text}")), 11, UIKit.Muted); var tle = tl.go.AddComponent<LayoutElement>(); tle.preferredHeight = 16 * Mathf.CeilToInt(p.events.Count / 3f + 1);
        var brow = UIKit.HStack(v.transform, 10, TextAnchor.MiddleCenter, true); UIKit.Size(brow.gameObject, -1, 44);
        if (r.Fail) UIKit.Button(brow.transform, "Run summary", () => flow.EndRun(grade == Grade.Haemorrhage ? "haemorrhage" : "unsafe"), UIKit.Hex("1f6f54"), UIKit.Fg, 15);
        else { UIKit.Button(brow.transform, "Next case (N)", () => flow.NextCase(), UIKit.Hex("1f6f54"), UIKit.Fg, 15); UIKit.Button(brow.transform, "Quit to menu", () => flow.QuitRun("quit"), UIKit.Hex("7a2a28"), UIKit.Fg, 15); }
        debriefGroup = debrief.AddComponent<CanvasGroup>();
    }

    void LateUpdate()
    {
        if (p != null && p.state == Procedure.State.Ended && Keyboard.current != null)
        {
            if (Keyboard.current.nKey.wasPressedThisFrame && p.result != null && !p.result.Value.Fail) flow.NextCase();
            if (Keyboard.current.enterKey.wasPressedThisFrame && p.result != null) { if (p.result.Value.Fail) flow.EndRun(p.grade == Grade.Haemorrhage ? "haemorrhage" : "unsafe"); else flow.NextCase(); }
        }
    }
}
