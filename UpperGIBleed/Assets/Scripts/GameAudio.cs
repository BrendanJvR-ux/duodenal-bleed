using UnityEngine;

/// <summary>All sounds are synthesised at start (no audio files): wash hiss, cautery buzz, band click, UI, heartbeat, alarm, and a low theatre hum.</summary>
public class GameAudio : MonoBehaviour
{
    public static GameAudio I { get; private set; }
    AudioSource sfx, loopWash, loopCautery, ambience, heart;
    AudioClip clipWash, clipCautery, clipBand, clipClick, clipGood, clipBad, clipBeat, clipAlarm, clipAmb, clipToken;
    float heartTimer, heartRate = 76;
    const int SR = 22050;

    void Awake()
    {
        I = this;
        sfx = Add(false); loopWash = Add(true); loopCautery = Add(true); ambience = Add(true); heart = Add(false);
        clipWash = Make("wash", 1.0f, (t, i) => Noise(i) * 0.35f * (0.7f + 0.3f * Mathf.Sin(t * 37)), lowpass: 0.25f);
        clipCautery = Make("cautery", 0.5f, (t, i) => (Mathf.Sin(t * 2 * Mathf.PI * 170) * 0.5f + Mathf.Sign(Mathf.Sin(t * 2 * Mathf.PI * 340)) * 0.12f + Noise(i) * 0.25f) * 0.5f);
        clipBand = Make("band", 0.18f, (t, i) => (Mathf.Sin(t * 2 * Mathf.PI * 900) * Mathf.Exp(-t * 40) + Noise(i) * Mathf.Exp(-t * 60) * 0.6f) * 0.8f);
        clipClick = Make("click", 0.06f, (t, i) => Mathf.Sin(t * 2 * Mathf.PI * 1400) * Mathf.Exp(-t * 90) * 0.5f);
        clipGood = Make("good", 0.5f, (t, i) => (Mathf.Sin(t * 2 * Mathf.PI * 660) * Mathf.Exp(-t * 6) * (t < 0.2f ? 1 : 0) + Mathf.Sin(t * 2 * Mathf.PI * 880) * Mathf.Exp(-(t - 0.18f) * 6) * (t >= 0.18f ? 1 : 0)) * 0.4f);
        clipBad = Make("bad", 0.45f, (t, i) => Mathf.Sin(t * 2 * Mathf.PI * (220 - 80 * t)) * Mathf.Exp(-t * 5) * 0.5f);
        clipBeat = Make("beat", 0.16f, (t, i) => Mathf.Sin(t * 2 * Mathf.PI * 55) * Mathf.Exp(-t * 28) * 0.9f);
        clipAlarm = Make("alarm", 0.25f, (t, i) => Mathf.Sin(t * 2 * Mathf.PI * 1200) * (t < 0.12f ? 0.35f : 0));
        clipToken = Make("token", 0.35f, (t, i) => (Mathf.Sin(t * 2 * Mathf.PI * 1046) + 0.5f * Mathf.Sin(t * 2 * Mathf.PI * 1568)) * Mathf.Exp(-t * 9) * 0.35f);
        clipAmb = Make("amb", 2.0f, (t, i) => (Mathf.Sin(t * 2 * Mathf.PI * 50) * 0.5f + Mathf.Sin(t * 2 * Mathf.PI * 100.5f) * 0.25f + Noise(i) * 0.08f) * 0.18f, lowpass: 0.08f);
        loopWash.clip = clipWash; loopCautery.clip = clipCautery; ambience.clip = clipAmb;
        ApplyVolumes();
        ambience.Play();
    }

    AudioSource Add(bool loop) { var s = gameObject.AddComponent<AudioSource>(); s.loop = loop; s.playOnAwake = false; return s; }
    static System.Random rng = new System.Random(7);
    static float Noise(int i) => (float)(rng.NextDouble() * 2 - 1);
    static AudioClip Make(string name, float sec, System.Func<float, int, float> f, float lowpass = 0)
    {
        int n = (int)(sec * SR); var d = new float[n]; float lp = 0;
        for (int i = 0; i < n; i++) { float v = f((float)i / SR, i); if (lowpass > 0) { lp += (v - lp) * lowpass; v = lp; } d[i] = Mathf.Clamp(v, -1, 1); }
        var c = AudioClip.Create(name, n, 1, SR, false); c.SetData(d, 0); return c;
    }

    public void ApplyVolumes()
    {
        AudioListener.volume = GameSettings.MasterVolume;
        float s = GameSettings.SfxVolume; sfx.volume = s; loopWash.volume = s * 0.7f; loopCautery.volume = s * 0.6f; heart.volume = s * 0.5f;
        ambience.volume = GameSettings.AmbienceVolume * 0.6f;
    }

    public void Wash(bool on) { if (on && !loopWash.isPlaying) loopWash.Play(); else if (!on && loopWash.isPlaying) loopWash.Stop(); }
    public void Cautery(bool on) { if (on && !loopCautery.isPlaying) loopCautery.Play(); else if (!on && loopCautery.isPlaying) loopCautery.Stop(); }
    public void Band() => sfx.PlayOneShot(clipBand);
    public void Click() => sfx.PlayOneShot(clipClick, 0.6f);
    public void Good() => sfx.PlayOneShot(clipGood);
    public void Bad() => sfx.PlayOneShot(clipBad);
    public void Token() => sfx.PlayOneShot(clipToken);
    public void Alarm() => sfx.PlayOneShot(clipAlarm, 0.8f);
    public void StopLoops() { Wash(false); Cautery(false); }

    /// <summary>Call each frame during a procedure: a heartbeat at the patient's rate, with an alarm when tachycardic.</summary>
    public void Heartbeat(float bpm, float dt, bool alarm)
    {
        heartRate = bpm; heartTimer += dt;
        if (heartTimer >= 60f / Mathf.Max(40, bpm)) { heartTimer = 0; heart.PlayOneShot(clipBeat); if (alarm) sfx.PlayOneShot(clipAlarm, 0.3f); }
    }
}
