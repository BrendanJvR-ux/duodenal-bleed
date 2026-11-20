using UnityEngine;

/// <summary>
/// Menu -> profile -> a run of cases (1, 2, 3, then round again with brisker bleeding) until the player quits or the
/// patient dies. Each case scores 0-1000 and pays 0-10 coffee tokens; the run total goes on the local leaderboard.
/// </summary>
public class GameFlow : MonoBehaviour
{
    public static GameFlow I { get; private set; }
    Tract tract; Camera cam; MenuUI menu;
    Procedure proc; ProcedureView view; HUD hud;
    public int RunScore { get; private set; }
    public int RunTokens { get; private set; }
    public int CasesCleared { get; private set; }
    public int CaseIndex { get; private set; }
    public int Loop => CaseIndex / Cases.All.Length;
    public int CaseNumber => CaseIndex + 1;
    bool inRun;

    public static GameFlow Create(Tract tract, Camera cam)
    {
        var go = new GameObject("GameFlow"); var f = go.AddComponent<GameFlow>(); I = f; f.tract = tract; f.cam = cam;
        f.menu = MenuUI.Create(f);
        f.ParkCamera();
        f.menu.ShowMain();
        return f;
    }

    void ParkCamera()
    {
        // the menu looks down the gastric body with a slow drift
        cam.transform.position = Anatomy.Axis(9.0f); cam.transform.rotation = Quaternion.LookRotation(Anatomy.Tangent(9.0f), Vector3.up);
        if (!menuLight) { menuLight = new GameObject("MenuLight").AddComponent<Light>(); menuLight.type = LightType.Point; menuLight.range = 7; menuLight.intensity = ProcedureView.ScopeLightIntensity(9f) * 0.8f; menuLight.color = new Color(1f, 0.95f, 0.88f); menuLight.transform.SetParent(cam.transform, false); }
        menuLight.gameObject.SetActive(true);
    }
    Light menuLight; float drift;

    void Update()
    {
        if (!inRun) { drift += Time.deltaTime; cam.transform.position = Anatomy.Axis(9.0f + 0.4f * Mathf.Sin(drift * 0.25f)); cam.transform.rotation = Quaternion.LookRotation(Anatomy.Tangent(9f), Vector3.up) * Quaternion.Euler(3f * Mathf.Sin(drift * 0.37f), 4f * Mathf.Sin(drift * 0.21f), 0); }
    }

    public void StartRun()
    {
        RunScore = 0; RunTokens = 0; CasesCleared = 0; CaseIndex = 0; inRun = true;
        menu.Hide(); menuLight.gameObject.SetActive(false);
        BeginCase();
    }

    void BeginCase()
    {
        TearDownCase();
        var c = Cases.All[CaseIndex % Cases.All.Length];
        float difficulty = 1 + GameConfig.LoopDifficulty * Loop;
        proc = new GameObject("Procedure").AddComponent<Procedure>();
        proc.Begin(c, tract, cam, difficulty);
        view = ProcedureView.Create(proc, cam);
        hud = HUD.Create(proc, this);
        proc.OnEnded += OnCaseEnded;
    }

    void TearDownCase()
    {
        if (hud) Destroy(hud.gameObject); if (view) Destroy(view.gameObject); if (proc) Destroy(proc.gameObject);
        hud = null; view = null; proc = null;
    }

    void OnCaseEnded(Grade g)
    {
        var r = proc.result.Value;
        RunScore += r.score; RunTokens += r.tokens;
        if (!r.Fail) { CasesCleared++; Profile.Tokens += r.tokens; }
    }

    public void NextCase() { CaseIndex++; BeginCase(); }

    public void QuitRun(string reason) => EndRun(reason);

    public void EndRun(string reason)
    {
        var rec = new RunRecord { name = Profile.Name, avatar = Profile.Avatar, score = RunScore, tokens = RunTokens, cases = CasesCleared, ended = reason, date = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") };
        int rank = (RunScore > 0 || CasesCleared > 0) ? Leaderboard.Add(rec) : -1;
        TearDownCase(); inRun = false; ParkCamera();
        menu.ShowRunSummary(rec, rank);
    }

    public void Quit()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        menu.ShowQuitNote();
#elif UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
