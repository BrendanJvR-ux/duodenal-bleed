using System;
using UnityEngine;

/// <summary>
/// The upper GI tract as one continuous tube along +Z (depth w = z, in scope units).
/// Oesophagus -> gastro-oesophageal junction -> stomach -> pylorus -> first part of the duodenum (D1).
/// Mirrors art/blender/tract.py (which builds the mesh) and web/config/anatomy.js.
/// </summary>
public static class Anatomy
{
    public const float End = 22.8f;        // end of D1: the superior duodenal flexure, where the lumen turns away
    public const float ScopeMax = 21.7f;   // the scope cannot be advanced beyond the first part of the duodenum
    public const float ZLine = 7.0f;       // squamocolumnar junction = gastro-oesophageal junction

    static readonly float[,] RadiusTab = { { 0, 0.58f }, { 5.5f, 0.60f }, { 6.7f, 0.68f }, { 7.15f, 1.25f }, { 8.3f, 1.75f }, { 13.5f, 1.8f }, { 16.5f, 1.3f }, { 17.9f, 1.0f }, { 18.4f, 0.74f }, { 18.9f, 0.96f }, { 19.6f, 0.98f }, { 22.8f, 0.76f } };
    static readonly float[,] PathXTab = { { 0, 0 }, { 14.5f, 0 }, { 16.2f, 0.35f }, { 17.5f, 0.55f }, { 18.4f, 0.1f }, { 19.0f, 0 }, { 21.5f, 0 }, { 22.8f, 1.3f } };
    static readonly float[,] PathYTab = { { 0, 0 }, { 14.5f, 0 }, { 16.2f, -0.25f }, { 17.5f, -0.15f }, { 18.4f, 0 }, { 22.0f, 0 }, { 22.8f, 0.25f } };
    static readonly float[,] CmTab = { { 0, 20 }, { 7.0f, 40 }, { 18.4f, 66 }, { 22.8f, 72 } };

    public struct Region { public string id, name; public float to; }
    public static readonly Region[] Regions =
    {
        new Region { id = "oes", name = "Oesophagus", to = 7.0f },
        new Region { id = "cardia", name = "Gastric cardia", to = 8.8f },
        new Region { id = "body", name = "Gastric body", to = 14.6f },
        new Region { id = "antrum", name = "Gastric antrum", to = 18.4f },
        new Region { id = "d1", name = "Duodenal bulb (D1)", to = 22.9f },
    };
    public struct Landmark { public float w; public string name; }
    public static readonly Landmark[] Landmarks =
    {
        new Landmark { w = 7.0f, name = "Gastro-oesophageal junction (Z-line)" },
        new Landmark { w = 14.9f, name = "Incisura angularis" },
        new Landmark { w = 18.4f, name = "Pylorus" },
    };

    public static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }
    static float Tab(float[,] t, float w)
    {
        int n = t.GetLength(0);
        if (w <= t[0, 0]) return t[0, 1];
        for (int i = 1; i < n; i++) if (w <= t[i, 0]) return Mathf.Lerp(t[i - 1, 1], t[i, 1], Smooth(t[i - 1, 0], t[i, 0], w));
        return t[n - 1, 1];
    }

    /// <summary>Base lumen radius (before folds and the oesophageal star).</summary>
    public static float Radius(float w) => Tab(RadiusTab, w);
    public static float PathX(float w) => Tab(PathXTab, w);
    public static float PathY(float w) => Tab(PathYTab, w);
    public static Vector3 Axis(float w) => new Vector3(PathX(w), PathY(w), w);
    public static Vector3 Tangent(float w) { float h = 0.05f; return (Axis(w + h) - Axis(w - h)).normalized; }
    public static int Cm(float w) => Mathf.RoundToInt(Tab(CmTab, w));
    public static Region RegionAt(float w) { foreach (var r in Regions) if (w < r.to) return r; return Regions[Regions.Length - 1]; }

    /// <summary>Angle round the wall for a world point at depth w (0 = +X, +90° = +Y, i.e. towards the top of the view).</summary>
    public static float AngleOf(Vector3 p) => Mathf.Atan2(p.y - PathY(p.z), p.x - PathX(p.z));

    /// <summary>Case files give lesion angles in the web prototype's clock convention (0 = 3 o'clock, 90 = bottom). Convert to this tube's (y up).</summary>
    public static float ClockToRad(float deg) => -deg * Mathf.Deg2Rad;
}
