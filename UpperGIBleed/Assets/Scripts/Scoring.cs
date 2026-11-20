using UnityEngine;

public enum Grade { Good, Unverified, Unsafe, Haemorrhage }

public struct CaseResult
{
    public Grade grade;
    public float time, loss, par;
    public int score, tokens;
    public bool Fail => grade == Grade.Unsafe || grade == Grade.Haemorrhage;
    public float TimeFactor => Mathf.Clamp01(1 - Mathf.Max(0, time - par) / par);
    public float BloodFactor => Mathf.Clamp01(1 - loss / GameConfig.LossLimitMl);
}

/// <summary>
/// Score 0-1000 per case: full time marks until the par time (zero at twice par) and full blood marks at no loss
/// (zero at the decompensation limit). An unverified sign-off keeps 60%. A fail (signed off while bleeding, or the
/// patient decompensated) scores 0 and earns 0 coffee tokens; otherwise 1-10 tokens, one per 100 points.
/// </summary>
public static class Scoring
{
    public static CaseResult Score(Grade grade, float time, float loss, float par)
    {
        var r = new CaseResult { grade = grade, time = time, loss = loss, par = par };
        if (r.Fail) { r.score = 0; r.tokens = 0; return r; }
        float s = GameConfig.ScoreTimeWeight * r.TimeFactor + GameConfig.ScoreBloodWeight * r.BloodFactor;
        if (grade == Grade.Unverified) s *= GameConfig.UnverifiedMultiplier;
        r.score = Mathf.RoundToInt(s);
        r.tokens = Mathf.Clamp(Mathf.RoundToInt(r.score / 100f), 1, 10);
        return r;
    }
}
