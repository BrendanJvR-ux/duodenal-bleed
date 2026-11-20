using UnityEngine;

/// <summary>
/// Blood on the wall as a grid of cells (depth bin x angle bin). Each cell has an amount and a freshness.
/// Fresh blood wells from the lesion, runs toward the dependent wall and along the lumen, and darkens with age.
/// Rendered as a small texture (u = angle, v = depth) on a transparent overlay of the tract mesh.
/// </summary>
public class BloodSim
{
    public const int NA = 36;
    public readonly int NB;
    public readonly float[] amt, fr;
    readonly float[] dAmt, dMass;
    public readonly Texture2D tex;
    readonly Color32[] pix;
    readonly int srcJ, srcA, downBin;
    readonly int flowDir;
    readonly float emitPerSec;
    public float t;

    public BloodSim(float lesionW, float lesionTh, int flowDir, float emitPerSec)
    {
        NB = Mathf.CeilToInt(Anatomy.End / 0.25f);
        amt = new float[NB * NA]; fr = new float[NB * NA]; dAmt = new float[NB * NA]; dMass = new float[NB * NA];
        srcJ = Mathf.FloorToInt(lesionW / 0.25f);
        srcA = AngleBin(lesionTh);
        downBin = AngleBin(-Mathf.PI / 2);          // the dependent wall is the bottom (-Y)
        this.flowDir = flowDir; this.emitPerSec = emitPerSec;
        tex = new Texture2D(NA, NB, TextureFormat.RGBA32, false) { wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        pix = new Color32[NA * NB];
    }

    public static int AngleBin(float th) { float u = th / (2 * Mathf.PI); u -= Mathf.Floor(u); return ((int)(u * NA)) % NA; }
    public static float BinAngle(int a) => (a + 0.5f) / NA * 2 * Mathf.PI;
    public static float BinDepth(int j) => (j + 0.5f) * 0.25f;

    void Add(int j, int a, float m, float f)
    {
        if (j < 0 || j >= NB) return; a = (a + NA) % NA; int i = j * NA + a;
        float tot = amt[i] + m; fr[i] = (fr[i] * amt[i] + f * m) / (tot > 0 ? tot : 1); amt[i] = Mathf.Min(1.8f, tot);
    }

    public void Tick(float dt, bool bleeding)
    {
        t += dt;
        if (bleeding)
        {
            float e = emitPerSec * dt * (0.8f + 0.2f * Mathf.Sin(t * 7.2f));
            Add(srcJ, srcA, e * 0.5f, 1); Add(srcJ, srcA + 1, e * 0.125f, 1); Add(srcJ, srcA - 1, e * 0.125f, 1);
            Add(srcJ + 1, srcA, e * 0.125f, 1); Add(srcJ - 1, srcA, e * 0.125f, 1);
        }
        System.Array.Clear(dAmt, 0, dAmt.Length); System.Array.Clear(dMass, 0, dMass.Length);
        for (int j = 0; j < NB; j++)
            for (int a = 0; a < NA; a++)
            {
                int i = j * NA + a; float m = amt[i]; if (m < 0.001f) continue;
                int da = Mathf.Min((a - srcA + NA) % NA, (srcA - a + NA) % NA);
                float thr = GameConfig.FlowBase + GameConfig.FlowPerCell * Mathf.Max(Mathf.Abs(j - srcJ), da);
                if (m <= thr) continue;
                float ex = (m - thr) * 0.30f;
                // gravity: towards the dependent wall (shortest way round), and along the lumen
                int d1 = (downBin - a + NA) % NA, d2 = (a - downBin + NA) % NA;
                int dir = d1 == 0 ? 0 : (d1 <= d2 ? 1 : -1);
                float prox = dir == 0 ? ex : ex * 0.55f, ang = ex - prox, f = fr[i];
                dAmt[i] -= ex; dMass[i] -= ex * f;
                int jt = j + flowDir;
                if (jt >= 0 && jt < NB) { int k = jt * NA + a; dAmt[k] += prox; dMass[k] += prox * f; }
                if (ang > 0) { int k = j * NA + ((a + dir + NA) % NA); dAmt[k] += ang; dMass[k] += ang * f; }
            }
        for (int i = 0; i < amt.Length; i++)
        {
            if (dAmt[i] == 0 && fr[i] <= 0) continue;
            float mass = amt[i] * fr[i] + dMass[i], na = Mathf.Clamp(amt[i] + dAmt[i], 0, 1.8f);
            amt[i] = na; fr[i] = na > 1e-4f ? Mathf.Clamp01(mass / na) : 0;
        }
        for (int i = 0; i < fr.Length; i++) if (fr[i] > 0) fr[i] = Mathf.Max(0, fr[i] - 0.0075f);
    }

    /// <summary>Blood thickness over the lesion: the thickest cell within ~1.25 lesion radii (wall distance).</summary>
    public float CoverAt(float lesionTh, float lesionW, float lesionRadius)
    {
        float m = 0, R = Anatomy.Radius(lesionW), lim = Mathf.Max(0.2f, lesionRadius * 1.25f);
        for (int j = Mathf.Max(0, srcJ - 2); j <= Mathf.Min(NB - 1, srcJ + 2); j++)
            for (int da = -3; da <= 3; da++)
            {
                int a = (srcA + da + NA) % NA; float th = BinAngle(a), w = BinDepth(j);
                float dth = Mathf.Abs(Mathf.DeltaAngle(th * Mathf.Rad2Deg, lesionTh * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
                if (Mathf.Sqrt(R * dth * (R * dth) + (w - lesionW) * (w - lesionW)) <= lim) m = Mathf.Max(m, amt[j * NA + a]);
            }
        return m;
    }

    public float Total() { float s = 0; for (int i = 0; i < amt.Length; i++) s += amt[i]; return s; }

    /// <summary>Clear blood within radius (wall distance) of the wall point (th, w).</summary>
    public void Wash(float th, float w, float radius, float dt)
    {
        int j0 = Mathf.Max(0, Mathf.FloorToInt((w - radius) / 0.25f)), j1 = Mathf.Min(NB - 1, Mathf.CeilToInt((w + radius) / 0.25f));
        for (int j = j0; j <= j1; j++)
        {
            float wj = BinDepth(j), R = Anatomy.Radius(wj);
            for (int a = 0; a < NA; a++)
            {
                int i = j * NA + a; if (amt[i] <= 0.002f) continue;
                float dth = Mathf.Abs(Mathf.DeltaAngle(BinAngle(a) * Mathf.Rad2Deg, th * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
                float d = Mathf.Sqrt(R * dth * (R * dth) + (wj - w) * (wj - w));
                if (d > radius) continue;
                float f = 1 - (d / radius) * (d / radius) * 0.7f;
                amt[i] = Mathf.Max(0, amt[i] - GameConfig.WashClearRate * f * dt);
            }
        }
    }

    public void UploadTexture()
    {
        for (int j = 0; j < NB; j++)
            for (int a = 0; a < NA; a++)
            {
                int i = j * NA + a; float m = amt[i], f = fr[i];
                float al = m <= 0.03f ? 0 : (1 - Mathf.Exp(-1.6f * m)) * 0.92f;
                byte r = (byte)Mathf.Lerp(78, 215, f), g = (byte)Mathf.Lerp(10, 16, f), b = (byte)Mathf.Lerp(14, 20, f);
                pix[j * NA + a] = new Color32(r, g, b, (byte)(al * 255));
            }
        tex.SetPixels32(pix); tex.Apply(false);
    }
}
