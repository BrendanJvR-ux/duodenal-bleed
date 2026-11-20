using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Headless gameplay checks: builds the world in an empty scene, drives a Procedure deterministically through each case
/// with scripted inputs, and asserts the rules (find, wash, treat with the right tool, re-inspect, sign off, scoring).
///   Unity -batchmode -quit -projectPath UpperGIBleed -executeMethod SelfTest.Run -logFile -
/// </summary>
public static class SelfTest
{
    static readonly List<string> fails = new List<string>();
    static int passed;
    static void Check(bool ok, string what) { if (ok) passed++; else { fails.Add(what); Debug.LogError("SELFTEST FAIL: " + what); } }

    public static void Run()
    {
        try { RunInner(); }
        catch (Exception e) { Debug.LogError("SELFTEST EXCEPTION: " + e); EditorApplication.Exit(2); return; }
        Debug.Log($"SELFTEST: {passed} passed, {fails.Count} failed");
        foreach (var f in fails) Debug.Log("  ✗ " + f);
        EditorApplication.Exit(fails.Count == 0 ? 0 : 1);
    }

    static void RunInner()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
        var camGo = new GameObject("Cam", typeof(Camera)); var cam = camGo.GetComponent<Camera>(); cam.fieldOfView = 100; cam.nearClipPlane = 0.02f; cam.aspect = 16f / 9f;
        var tract = Tract.Create();
        Check(tract != null && tract.Mesh != null, "tract loaded");
        var b = tract.Mesh.bounds; var wb = tract.Collider.bounds;
        Check(Mathf.Abs(wb.size.z - Anatomy.End) < 0.2f, $"tract runs along z over {Anatomy.End} units (got {wb.size.z:F2})");
        Check(tract.Raycast(Anatomy.Axis(3f), Vector3.right, out var h1, 3f) && h1.distance < 0.8f && h1.distance > 0.3f, $"oesophagus wall near the axis ({(tract.Raycast(Anatomy.Axis(3f), Vector3.right, out var hh, 3f) ? hh.distance : -1):F2})");
        Check(tract.Raycast(Anatomy.Axis(11f), Vector3.right, out var h2, 4f) && h2.distance > 1.3f, $"stomach is wide ({(tract.Raycast(Anatomy.Axis(11f), Vector3.right, out var hh2, 4f) ? hh2.distance : -1):F2})");
        Check(tract.Raycast(Anatomy.Axis(20f), Vector3.up, out var h3, 4f) && Vector3.Dot(h3.normal, Vector3.down) > 0.5f, "wall normals point into the lumen");

        for (int ci = 0; ci < Cases.All.Length; ci++)
        {
            var c = Cases.All[ci];
            var p = new GameObject("Proc" + ci).AddComponent<Procedure>(); p.manualInput = true;
            p.Begin(c, tract, cam, 1f);
            Check(p.state == Procedure.State.Intro && p.bleeding && p.blood.Total() > 3, c.id + ": starts live with a blood trail");
            Check(Anatomy.RegionAt(p.z).id == (ci == 0 ? "oes" : ci == 1 ? "body" : "d1"), c.id + ": starts in its region");
            string lesionRegion = Anatomy.RegionAt(c.lesionW).id;
            Check(ci == 0 ? lesionRegion == "oes" : ci == 1 ? lesionRegion == "body" : lesionRegion == "d1", c.id + ": lesion in the right organ (" + lesionRegion + ")");
            p.Start_();
            // scope limit
            p.input.up = true; Tick(p, 40); p.input.up = false;
            Check(Mathf.Abs(p.z - Anatomy.ScopeMax) < 0.01f, c.id + ": the scope stops at the end of D1");
            Check(Anatomy.RegionAt(p.z).id == "d1", c.id + ": still in D1 at the limit");
            p.ResetCase(); p.Start_();
            // aim with the arrows only
            int taps = Align(p, 240);
            Check(p.lesionDist <= GameConfig.CauteryTol + c.lesionRadius * 0.5f, $"{c.id}: aimed at the lesion with arrows (dist {p.lesionDist:F2} after {taps} taps, z {p.z:F2} tip {p.tipAng * Mathf.Rad2Deg:F0}°)");
            // wash reveals
            Hold(p, 1.2f); Check(p.stats.washRevealed, c.id + ": wash reveals the source");
            // wrong tool / right tool
            if (c.intended == Tool.Cautery)
            {
                p.ChangeTool(1); p.ChangeTool(1); Check(p.tool == Tool.Banding, c.id + ": Tab x2 -> banding");
                p.UseDown(); p.UseUp(); Tick(p, 0.1f); Check(p.bleeding && p.stats.bandAttempts == 1, c.id + ": banding does not treat an ulcer");
                p.ChangeTool(-1); Check(p.tool == Tool.Cautery, c.id + ": Shift+Tab -> cautery");
                Hold(p, GameConfig.CauteryContactSec * 0.5f); Check(p.bleeding, c.id + ": half the contact interval is not enough");
                Hold(p, GameConfig.CauteryContactSec * 0.75f); Check(!p.bleeding && p.controlledBy == Tool.Cautery, c.id + ": cautery controls the bleed");
                p.ChangeTool(-1);
            }
            else
            {
                p.ChangeTool(1); Hold(p, 3f); Check(p.bleeding && p.stats.cauteryWrong > 2, c.id + ": cautery is the wrong tool for a varix");
                p.ChangeTool(1); Check(p.tool == Tool.Banding && p.bandsLeft == GameConfig.BandsLoaded, c.id + ": banding selected with a full cap");
                p.UseDown(); p.UseUp(); Tick(p, 0.1f); Check(!p.bleeding && p.controlledBy == Tool.Banding && p.bandsLeft == GameConfig.BandsLoaded - 1, c.id + ": a band on the varix stops the bleed");
                p.ChangeTool(1);
            }
            Check(p.tool == Tool.Wash, c.id + ": back on wash");
            Hold(p, 2.0f); Tick(p, 4.5f);
            Check(p.stats.bestClearView >= GameConfig.ClearViewSecToSignOff, $"{c.id}: site seen clear for {p.stats.bestClearView:F1} s (cover {p.Cover:F2})");
            p.SignOff();
            Check(p.state == Procedure.State.Ended && p.grade == Grade.Good, c.id + ": safe sign-off (" + p.grade + ")");
            var r = p.result.Value;
            Check(r.score > 300 && r.score <= 1000 && r.tokens >= 1 && r.tokens <= 10 && r.tokens == Mathf.Clamp(Mathf.RoundToInt(r.score / 100f), 1, 10), $"{c.id}: score {r.score}, tokens {r.tokens}");
            var fb = p.Feedback(); Check(fb.Count >= 3, c.id + ": debrief has feedback lines");
            if (ci == 0) Check(fb.Exists(x => x.tag == "Banding" && x.text.Contains("Bleeding stopped")), c.id + ": banding feedback");
            else Check(fb.Exists(x => x.tag == "Cautery" && x.text.Contains("Flow stopped")), c.id + ": cautery feedback");

            // fail paths
            p.ResetCase(); p.Start_(); Tick(p, 1); p.SignOff(); Check(p.grade == Grade.Unsafe && p.result.Value.tokens == 0 && p.result.Value.score == 0, c.id + ": unsafe sign-off = fail, 0 tokens");
            p.ResetCase(); p.Start_(); Tick(p, 200); Check(p.state == Procedure.State.Ended && p.grade == Grade.Haemorrhage && p.result.Value.tokens == 0, c.id + ": uncontrolled bleed ends in decompensation with 0 tokens");
            // switching tools while Space is held stops the action and needs a fresh press
            p.ResetCase(); p.Start_(); p.UseDown(); Tick(p, 0.3f); Check(p.washing, c.id + ": washing while held");
            p.ChangeTool(1); Tick(p, 0.3f); Check(!p.washing && !p.cauteryOn && !p.armed, c.id + ": nothing active after a switch with Space held");
            p.UseUp(); p.UseDown(); Tick(p, 0.2f); Check(p.cauteryOn, c.id + ": fresh press activates"); p.UseUp();
            UnityEngine.Object.DestroyImmediate(p.gameObject);
        }
        // scoring table
        var s1 = Scoring.Score(Grade.Good, 30, 200, 50); var s2 = Scoring.Score(Grade.Good, 100, 1400, 50); var s3 = Scoring.Score(Grade.Unverified, 30, 200, 50);
        Check(s1.score > 900 && s1.tokens == 9, $"fast clean run scores ~940 ({s1.score}) and 9 tokens");
        var s0 = Scoring.Score(Grade.Good, 20, 0, 50); Check(s0.score == 1000 && s0.tokens == 10, "a perfect run scores 1000 and 10 tokens");
        Check(s2.score < 100 && s2.tokens == 1, $"slow bloody run scores low ({s2.score}) but still 1 token");
        Check(s3.score == Mathf.RoundToInt(s1.score * GameConfig.UnverifiedMultiplier), "unverified keeps 60%");
        // leaderboard + profile
        Leaderboard.Clear(); Leaderboard.Add(new RunRecord { name = "A", score = 500, tokens = 5, cases = 1 }); Leaderboard.Add(new RunRecord { name = "B", score = 900, tokens = 9, cases = 2 });
        var lb = Leaderboard.Load(); Check(lb.Count == 2 && lb[0].name == "B", "leaderboard sorts by score"); Leaderboard.Clear();
        Check(Profile.AvatarSprite(1) != null && Profile.AvatarSprite(6) != null, "avatar sprites load");
        Check(Resources.Load<GameObject>("Art/props")?.transform.Find("Ulcer") != null, "props.fbx has the Ulcer object");
    }

    static void Tick(Procedure p, float sec) { int n = Mathf.RoundToInt(sec * 60); for (int i = 0; i < n; i++) p.Step(1f / 60); }
    static void Hold(Procedure p, float sec) { p.UseDown(); Tick(p, sec); p.UseUp(); }

    /// <summary>Aim using only the arrows: ↑/↓ until the lesion sits at the tip's distance from the view centre, ←/→ until the tip points at it.</summary>
    static int Align(Procedure p, int maxTaps)
    {
        int n = 0;
        while (p.lesionDist > GameConfig.CauteryTol * 0.8f && n++ < maxTaps)
        {
            p.PlaceCamera();
            var vp = p.cam.WorldToViewportPoint(p.lesionPos);
            var tipVp = p.TipViewport(p.tipAng);
            // radial error: how far the lesion's distance from centre is from the tip circle (in height units)
            float aspect = p.cam.aspect;
            var lc = new Vector2((vp.x - 0.5f) * aspect, vp.y - 0.5f); var tc = new Vector2((tipVp.x - 0.5f) * aspect, tipVp.y - 0.5f);
            float rErr = lc.magnitude - tc.magnitude;                   // >0: lesion further out than the tip => advance (it slides outward as we approach)
            float aErr = Mathf.DeltaAngle(p.tipAng * Mathf.Rad2Deg, Mathf.Atan2(lc.y, lc.x) * Mathf.Rad2Deg);
            p.input = default;
            if (Mathf.Abs(rErr) > 0.01f) { if (rErr > 0) { p.input.down = true; p.Tap(1); } else { p.input.up = true; p.Tap(0); } }
            if (Mathf.Abs(aErr) > 2f) { if (aErr > 0) { p.input.left = true; p.Tap(2); } else { p.input.right = true; p.Tap(3); } }
            Tick(p, Mathf.Clamp(Mathf.Max(Mathf.Abs(rErr) * 1.2f, Mathf.Abs(aErr) / 300f), 0.04f, 0.25f));
            p.input = default; Tick(p, 0.45f);
        }
        return n;
    }
}
