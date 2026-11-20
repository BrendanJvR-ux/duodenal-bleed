using System.Collections;
using System.IO;
using UnityEngine;

/// <summary>
/// Plays a scripted sequence and saves screenshots (used by the Editor's Snapshot command to check the look headlessly).
/// Enabled when PlayerPrefs "demo.snapshots" is set; writes to the folder in "demo.dir".
/// </summary>
public class DemoDriver : MonoBehaviour
{
    string dir; Camera cam;

    void Start()
    {
        if (PlayerPrefs.GetInt("demo.snapshots", 0) != 1) { Destroy(this); return; }
        dir = PlayerPrefs.GetString("demo.dir", Path.Combine(Application.dataPath, "..", "..", "shots"));
        Directory.CreateDirectory(dir);
        cam = Camera.main;
        StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        yield return null; yield return null;
        var flow = GameFlow.I;
        Shot("01_menu");
        Profile.Name = "Demo"; Profile.Avatar = 3;
        var menu = FindFirstObjectByType<MenuUI>(); menu.ShowProfile(() => { }); yield return null; Shot("02_profile");
        menu.ShowSettings(); yield return null; Shot("03_settings");
        flow.StartRun(); yield return null; yield return null;
        var p = FindFirstObjectByType<Procedure>(); p.manualInput = true;
        Shot("04_case1_intro");
        p.Start_(); yield return Advance(p, 1.0f); Shot("05_case1_oesophagus");
        yield return Aim(p); Shot("06_case1_aimed_wash");
        p.UseDown(); yield return Advance(p, 0.8f); Shot("07_case1_washing"); p.UseUp();
        p.ChangeTool(1); p.UseDown(); yield return Advance(p, 0.6f); Shot("08_case1_cautery_wrong"); p.UseUp();
        p.ChangeTool(1); yield return Advance(p, 0.2f); Shot("09_case1_banding_cap");
        p.UseDown(); p.UseUp(); yield return Advance(p, 0.5f); Shot("10_case1_banded");
        p.ChangeTool(1); p.UseDown(); yield return Advance(p, 2.0f); p.UseUp(); yield return Advance(p, 4.5f); p.SignOff(); yield return null; yield return null; Shot("11_case1_debrief");
        flow.NextCase(); yield return null; yield return null; p = FindFirstObjectByType<Procedure>(); p.manualInput = true; p.Start_();
        yield return Advance(p, 0.5f); Shot("12_case2_stomach");
        yield return Aim(p); p.ChangeTool(1); p.UseDown(); yield return Advance(p, 1.0f); Shot("13_case2_cautery"); yield return Advance(p, 1.0f); p.UseUp();
        p.ChangeTool(-1); p.UseDown(); yield return Advance(p, 1.5f); p.UseUp(); yield return Advance(p, 4f); Shot("14_case2_controlled"); p.SignOff(); yield return null;
        flow.NextCase(); yield return null; yield return null; p = FindFirstObjectByType<Procedure>(); p.manualInput = true; p.Start_();
        yield return Advance(p, 0.3f); Shot("15_case3_duodenum");
        p.input.down = true; yield return Advance(p, 2.2f); p.input.down = false; yield return Advance(p, 0.5f); Shot("16_case3_pylorus_from_d1");
        p.input.up = true; yield return Advance(p, 3.0f); p.input.up = false; yield return Advance(p, 0.3f); Shot("17_case3_overshoot");
        flow.QuitRun("quit"); yield return null; Shot("18_run_summary");
        menu.ShowLeaderboard(); yield return null; Shot("19_leaderboard");
        PlayerPrefs.DeleteKey("demo.snapshots"); PlayerPrefs.Save();
        Debug.Log("DEMO DONE " + dir);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    IEnumerator Advance(Procedure p, float sec) { int n = Mathf.RoundToInt(sec * 30); for (int i = 0; i < n; i++) { p.Step(1f / 30); yield return null; } }

    IEnumerator Aim(Procedure p)
    {
        for (int n = 0; n < 240 && p.lesionDist > GameConfig.CauteryTol * 0.8f; n++)
        {
            p.PlaceCamera();
            var vp = cam.WorldToViewportPoint(p.lesionPos); var tipVp = p.TipViewport(p.tipAng); float aspect = cam.aspect;
            var lc = new Vector2((vp.x - 0.5f) * aspect, vp.y - 0.5f); var tc = new Vector2((tipVp.x - 0.5f) * aspect, tipVp.y - 0.5f);
            float rErr = lc.magnitude - tc.magnitude, aErr = Mathf.DeltaAngle(p.tipAng * Mathf.Rad2Deg, Mathf.Atan2(lc.y, lc.x) * Mathf.Rad2Deg);
            p.input = default;
            if (Mathf.Abs(rErr) > 0.01f) { if (rErr > 0) { p.input.down = true; p.Tap(1); } else { p.input.up = true; p.Tap(0); } }
            if (Mathf.Abs(aErr) > 2f) { if (aErr > 0) { p.input.left = true; p.Tap(2); } else { p.input.right = true; p.Tap(3); } }
            yield return Advance(p, Mathf.Clamp(Mathf.Max(Mathf.Abs(rErr) * 1.2f, Mathf.Abs(aErr) / 300f), 0.04f, 0.25f));
            p.input = default; yield return Advance(p, 0.45f);
        }
    }

    void Shot(string name)
    {
        int w = 1280, h = 720;
        var rt = new RenderTexture(w, h, 24); var prev = cam.targetTexture;
        foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None)) { c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = cam; c.planeDistance = 0.5f; }
        cam.targetTexture = rt; cam.aspect = (float)w / h; cam.Render();
        var old = RenderTexture.active; RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        RenderTexture.active = old; cam.targetTexture = prev;
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
        Destroy(tex); rt.Release(); Destroy(rt);
        foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None)) c.renderMode = RenderMode.ScreenSpaceOverlay;
        Debug.Log("shot " + name);
    }
}
