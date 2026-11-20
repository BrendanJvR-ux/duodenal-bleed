using UnityEngine;

/// <summary>Player settings in PlayerPrefs.</summary>
public static class GameSettings
{
    public static float MasterVolume { get => PlayerPrefs.GetFloat("set.master", 0.8f); set { PlayerPrefs.SetFloat("set.master", value); PlayerPrefs.Save(); } }
    public static float SfxVolume { get => PlayerPrefs.GetFloat("set.sfx", 0.9f); set { PlayerPrefs.SetFloat("set.sfx", value); PlayerPrefs.Save(); } }
    public static float AmbienceVolume { get => PlayerPrefs.GetFloat("set.amb", 0.5f); set { PlayerPrefs.SetFloat("set.amb", value); PlayerPrefs.Save(); } }
    public static bool Effects { get => PlayerPrefs.GetInt("set.fx", 1) == 1; set { PlayerPrefs.SetInt("set.fx", value ? 1 : 0); PlayerPrefs.Save(); } }
    public static bool ShowTouchPad { get => PlayerPrefs.GetInt("set.pad", -1) switch { 1 => true, 0 => false, _ => UnityEngine.InputSystem.Touchscreen.current != null }; set { PlayerPrefs.SetInt("set.pad", value ? 1 : 0); PlayerPrefs.Save(); } }
    public static float ScopeSpeedScale { get => PlayerPrefs.GetFloat("set.speed", 1f); set { PlayerPrefs.SetFloat("set.speed", value); PlayerPrefs.Save(); } }
}
