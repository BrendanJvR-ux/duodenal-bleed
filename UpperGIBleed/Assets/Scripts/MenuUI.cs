using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Main menu, profile (name and avatar), leaderboard, settings and the run summary.</summary>
public class MenuUI : MonoBehaviour
{
    GameFlow flow; Canvas canvas; GameObject screen;

    public static MenuUI Create(GameFlow flow) { var go = new GameObject("Menu"); var m = go.AddComponent<MenuUI>(); m.flow = flow; m.canvas = UIKit.CreateCanvas("MenuCanvas", 20); m.canvas.transform.SetParent(go.transform, false); return m; }

    void Clear() { if (screen) Destroy(screen); screen = new GameObject("Screen", typeof(RectTransform)); screen.transform.SetParent(canvas.transform, false); UIKit.Fill(screen); }
    public void Hide() { if (screen) Destroy(screen); screen = null; }

    Transform Card(string title, float w, float h, string subtitle = null)
    {
        UIKit.PanelEl(screen.transform, "Dim", new Color(0.02f, 0.03f, 0.04f, 0.72f), false);
        var card = UIKit.PanelEl(screen.transform, "Card", UIKit.A(UIKit.Panel, 0.98f)); UIKit.Outline(card, UIKit.Line); UIKit.Place(card.gameObject, new Vector2(0.5f, 0.5f), new Vector2(w, h));
        var v = UIKit.VStack(card.transform, 10, new RectOffset(26, 26, 20, 20));
        UIKit.Size(UIKit.Label(v.transform, title, 26, UIKit.Fg, TextAlignmentOptions.Center, true).go, -1, 36);
        if (subtitle != null) UIKit.Size(UIKit.Label(v.transform, subtitle, 13, UIKit.Muted, TextAlignmentOptions.Center).go, -1, 22);
        return v.transform;
    }

    public void ShowMain()
    {
        Clear();
        var v = Card("Upper GI Bleed", 440, 430, "Endoscopic haemostasis trainer · fictional patients");
        if (Profile.IsSet)
        {
            var row = UIKit.HStack(v.transform, 10, TextAnchor.MiddleCenter); UIKit.Size(row.gameObject, -1, 56);
            var av = UIKit.ImageEl(row.transform, Profile.AvatarSprite(Profile.Avatar)); UIKit.Size(av.gameObject, 52, 52);
            var who = UIKit.Label(row.transform, $"<b>{Profile.Name}</b>\n<size=12><color=#c8955a>☕ {Profile.Tokens} coffee tokens banked</color></size>", 16, UIKit.Fg); UIKit.Size(who.go, 240, 52);
        }
        UIKit.Spacer(v, 4);
        Button B(string label, Action a, Color bg) { var b = UIKit.Button(v.transform, label, a, bg, UIKit.Fg, 18); UIKit.Size(b.gameObject, -1, 48); return b; }
        B("New case", () => { if (Profile.IsSet) flow.StartRun(); else ShowProfile(() => flow.StartRun()); }, UIKit.Hex("1f6f54"));
        B("Leaderboard", ShowLeaderboard, UIKit.Panel2);
        B("Settings", ShowSettings, UIKit.Panel2);
        B(Profile.IsSet ? "Change name / avatar" : "Name / avatar", () => ShowProfile(ShowMain), UIKit.Panel2);
        B("Quit", flow.Quit, UIKit.Hex("7a2a28"));
        UIKit.Size(UIKit.Label(v.transform, "↑↓ scope · ←→ rotate tip · Tab attachment · Space use", 11, UIKit.Muted, TextAlignmentOptions.Center).go, -1, 18);
    }

    public void ShowProfile(Action done)
    {
        Clear();
        var v = Card("Who is scoping today?", 560, 400, "Choose a name and an avatar. They appear on the leaderboard.");
        string name = Profile.Name; int avatar = Profile.Avatar;
        var field = UIKit.TextField(v.transform, name, "Your name", 18, s => name = s); UIKit.Size(field.gameObject, -1, 46);
        var grid = UIKit.HStack(v.transform, 10, TextAnchor.MiddleCenter, true); UIKit.Size(grid.gameObject, -1, 96);
        var frames = new Image[Profile.AvatarCount + 1];
        void Refresh() { for (int i = 1; i <= Profile.AvatarCount; i++) { frames[i].color = i == avatar ? Color.Lerp(UIKit.Panel2, UIKit.Ok, 0.35f) : UIKit.Panel2; UIKit.Outline(frames[i], i == avatar ? UIKit.Ok : UIKit.Line, i == avatar ? 2.5f : 1.5f); } }
        for (int i = 1; i <= Profile.AvatarCount; i++)
        {
            int idx = i; frames[i] = UIKit.PanelEl(grid.transform, "Avatar" + i, UIKit.Panel2);
            var b = frames[i].gameObject.AddComponent<Button>(); b.targetGraphic = frames[i]; b.onClick.AddListener(() => { avatar = idx; GameAudio.I?.Click(); Refresh(); });
            var im = UIKit.ImageEl(frames[i].transform, Profile.AvatarSprite(i)); UIKit.Fill(im.gameObject, 6);
        }
        Refresh();
        var err = UIKit.Label(v.transform, "", 12, UIKit.Bad, TextAlignmentOptions.Center); UIKit.Size(err.go, -1, 20);
        var row = UIKit.HStack(v.transform, 10, TextAnchor.MiddleCenter, true); UIKit.Size(row.gameObject, -1, 46);
        UIKit.Button(row.transform, "Continue", () =>
        {
            if (string.IsNullOrWhiteSpace(name)) { err.text = "Enter a name first."; return; }
            Profile.Name = name.Trim(); Profile.Avatar = avatar; done();
        }, UIKit.Hex("1f6f54"), UIKit.Fg, 16);
        UIKit.Button(row.transform, "Back", ShowMain, UIKit.Panel2, UIKit.Fg, 16);
        if (field is TMP_InputField tf) tf.ActivateInputField(); else if (field is InputField lf) lf.ActivateInputField();
    }

    public void ShowLeaderboard()
    {
        Clear();
        var v = Card("Leaderboard", 620, 480, "Best runs on this device · score, coffee tokens, cases cleared");
        var runs = Leaderboard.Load();
        if (runs.Count == 0) UIKit.Size(UIKit.Label(v.transform, "No runs yet. Start a new case and see how far you get.", 14, UIKit.Muted, TextAlignmentOptions.Center).go, -1, 40);
        var hdr = UIKit.Label(v.transform, "<color=#8c9aa5>#   Player                              Score      ☕      Cases   Ended</color>", 12, UIKit.Muted); UIKit.Size(hdr.go, -1, 18);
        int n = 0;
        foreach (var r in runs)
        {
            if (++n > 10) break;
            var row = UIKit.HStack(v.transform, 8, TextAnchor.MiddleLeft); UIKit.Size(row.gameObject, -1, 30);
            UIKit.Size(UIKit.Label(row.transform, n.ToString(), 13, UIKit.Muted).go, 22, 30);
            var av = UIKit.ImageEl(row.transform, Profile.AvatarSprite(r.avatar)); UIKit.Size(av.gameObject, 28, 28);
            UIKit.Size(UIKit.Label(row.transform, r.name, 14, UIKit.Fg, TextAlignmentOptions.Left, true).go, 190, 30);
            UIKit.Size(UIKit.Label(row.transform, r.score.ToString(), 14, UIKit.Fg).go, 70, 30);
            UIKit.Size(UIKit.Label(row.transform, "☕ " + r.tokens, 14, UIKit.Coffee).go, 60, 30);
            UIKit.Size(UIKit.Label(row.transform, r.cases.ToString(), 14, UIKit.Fg).go, 60, 30);
            UIKit.Size(UIKit.Label(row.transform, r.ended == "quit" ? "quit" : r.ended == "haemorrhage" ? "<color=#ff5d57>patient died</color>" : "<color=#ff5d57>re-bled</color>", 12, UIKit.Muted).go, 100, 30);
        }
        UIKit.Spacer(v, 6);
        var b = UIKit.Button(v.transform, "Back", ShowMain, UIKit.Panel2, UIKit.Fg, 16); UIKit.Size(b.gameObject, -1, 42);
    }

    public void ShowSettings()
    {
        Clear();
        var v = Card("Settings", 480, 470);
        void Row(string label, Func<Transform, GameObject> make)
        {
            var row = UIKit.HStack(v.transform, 12, TextAnchor.MiddleLeft); UIKit.Size(row.gameObject, -1, 36);
            UIKit.Size(UIKit.Label(row.transform, label, 14, UIKit.Fg).go, 180, 36);
            var c = make(row.transform); UIKit.Size(c, 220, 30);
        }
        Row("Master volume", t => UIKit.SliderEl(t, GameSettings.MasterVolume, x => { GameSettings.MasterVolume = x; GameAudio.I?.ApplyVolumes(); }).gameObject);
        Row("Effects volume", t => UIKit.SliderEl(t, GameSettings.SfxVolume, x => { GameSettings.SfxVolume = x; GameAudio.I?.ApplyVolumes(); }).gameObject);
        Row("Theatre ambience", t => UIKit.SliderEl(t, GameSettings.AmbienceVolume, x => { GameSettings.AmbienceVolume = x; GameAudio.I?.ApplyVolumes(); }).gameObject);
        Row("Scope speed", t => UIKit.SliderEl(t, (GameSettings.ScopeSpeedScale - 0.5f) / 1.0f, x => GameSettings.ScopeSpeedScale = 0.5f + x).gameObject);
        Button tog = null; Button pad = null;
        Row("Screen effects", t => { tog = UIKit.Button(t, GameSettings.Effects ? "On" : "Off", () => { GameSettings.Effects = !GameSettings.Effects; tog.GetComponentInChildren<TMP_Text>()?.SetText(GameSettings.Effects ? "On" : "Off"); var lt = tog.GetComponentInChildren<Text>(); if (lt) lt.text = GameSettings.Effects ? "On" : "Off"; }, UIKit.Panel2, UIKit.Fg, 14); return tog.gameObject; });
        Row("On-screen pad", t => { pad = UIKit.Button(t, GameSettings.ShowTouchPad ? "Shown" : "Hidden", () => { GameSettings.ShowTouchPad = !GameSettings.ShowTouchPad; pad.GetComponentInChildren<TMP_Text>()?.SetText(GameSettings.ShowTouchPad ? "Shown" : "Hidden"); var lt = pad.GetComponentInChildren<Text>(); if (lt) lt.text = GameSettings.ShowTouchPad ? "Shown" : "Hidden"; }, UIKit.Panel2, UIKit.Fg, 14); return pad.gameObject; });
        UIKit.Spacer(v, 6);
        var clear = UIKit.Button(v.transform, "Clear leaderboard", () => { Leaderboard.Clear(); }, UIKit.Hex("7a2a28"), UIKit.Fg, 14); UIKit.Size(clear.gameObject, -1, 40);
        var b = UIKit.Button(v.transform, "Back", ShowMain, UIKit.Panel2, UIKit.Fg, 16); UIKit.Size(b.gameObject, -1, 42);
    }

    public void ShowRunSummary(RunRecord rec, int rank)
    {
        Clear();
        var v = Card("Run over", 520, 400, rec.ended == "quit" ? "You signed off and left the list." : rec.ended == "haemorrhage" ? "The patient decompensated from blood loss." : "The patient re-bled after an unsafe sign-off.");
        var row = UIKit.HStack(v.transform, 10, TextAnchor.MiddleCenter); UIKit.Size(row.gameObject, -1, 56);
        var av = UIKit.ImageEl(row.transform, Profile.AvatarSprite(rec.avatar)); UIKit.Size(av.gameObject, 52, 52);
        UIKit.Size(UIKit.Label(row.transform, $"<b>{rec.name}</b>", 18, UIKit.Fg).go, 220, 52);
        var grid = UIKit.HStack(v.transform, 8, TextAnchor.MiddleCenter, true); UIKit.Size(grid.gameObject, -1, 64);
        void Stat(string label, string val, Color col) { var box = UIKit.PanelEl(grid.transform, label, UIKit.Panel2); UIKit.Size(box.gameObject, -1, 62); var l = UIKit.Label(box.transform, label, 11, UIKit.Muted, TextAlignmentOptions.Top); UIKit.Fill(l.go, 6); var t = UIKit.Label(box.transform, val, 22, col, TextAlignmentOptions.Bottom, true); UIKit.Fill(t.go, 6); }
        Stat("Score", rec.score.ToString(), UIKit.Fg); Stat("Coffee tokens", "☕ " + rec.tokens, UIKit.Coffee); Stat("Cases cleared", rec.cases.ToString(), UIKit.Fg);
        UIKit.Size(UIKit.Label(v.transform, rank >= 0 ? $"#{rank + 1} on this device's leaderboard. ☕ {rec.tokens} tokens added to your bank ({Profile.Tokens} total)." : $"☕ {rec.tokens} tokens added to your bank ({Profile.Tokens} total).", 13, UIKit.Muted, TextAlignmentOptions.Center).go, -1, 40);
        var brow = UIKit.HStack(v.transform, 10, TextAnchor.MiddleCenter, true); UIKit.Size(brow.gameObject, -1, 46);
        UIKit.Button(brow.transform, "New run", flow.StartRun, UIKit.Hex("1f6f54"), UIKit.Fg, 16);
        UIKit.Button(brow.transform, "Leaderboard", ShowLeaderboard, UIKit.Panel2, UIKit.Fg, 16);
        UIKit.Button(brow.transform, "Menu", ShowMain, UIKit.Panel2, UIKit.Fg, 16);
    }

    public void ShowQuitNote()
    {
        Clear();
        var v = Card("Thanks for scoping", 420, 200, "In a browser the game cannot close the tab for you. Close it when you are ready.");
        var b = UIKit.Button(v.transform, "Back to menu", ShowMain, UIKit.Panel2, UIKit.Fg, 16); UIKit.Size(b.gameObject, -1, 42);
    }
}
