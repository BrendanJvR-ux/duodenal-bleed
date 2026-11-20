using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[Serializable]
public class RunRecord
{
    public string name; public int avatar;
    public int score, tokens, cases;
    public string ended;       // "quit" | "haemorrhage" | "unsafe"
    public string date;
}

[Serializable] class RunList { public List<RunRecord> runs = new List<RunRecord>(); }

/// <summary>Top runs on this device, stored as JSON in PlayerPrefs.</summary>
public static class Leaderboard
{
    const string Key = "leaderboard.v1";
    const int Max = 20;

    public static List<RunRecord> Load()
    {
        try { var l = JsonUtility.FromJson<RunList>(PlayerPrefs.GetString(Key, "")); return l?.runs ?? new List<RunRecord>(); }
        catch { return new List<RunRecord>(); }
    }

    public static int Add(RunRecord r)
    {
        var runs = Load(); runs.Add(r);
        runs = runs.OrderByDescending(x => x.score).ThenByDescending(x => x.tokens).ThenByDescending(x => x.cases).Take(Max).ToList();
        PlayerPrefs.SetString(Key, JsonUtility.ToJson(new RunList { runs = runs })); PlayerPrefs.Save();
        return runs.IndexOf(r);     // -1 if it did not make the board
    }

    public static void Clear() { PlayerPrefs.DeleteKey(Key); PlayerPrefs.Save(); }
}
