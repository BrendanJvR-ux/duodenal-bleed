using UnityEngine;

/// <summary>The player's name and avatar, kept in PlayerPrefs.</summary>
public static class Profile
{
    public const int AvatarCount = 6;
    public static string Name { get => PlayerPrefs.GetString("profile.name", ""); set { PlayerPrefs.SetString("profile.name", value); PlayerPrefs.Save(); } }
    public static int Avatar { get => Mathf.Clamp(PlayerPrefs.GetInt("profile.avatar", 1), 1, AvatarCount); set { PlayerPrefs.SetInt("profile.avatar", Mathf.Clamp(value, 1, AvatarCount)); PlayerPrefs.Save(); } }
    public static bool IsSet => !string.IsNullOrWhiteSpace(Name);
    public static int Tokens { get => PlayerPrefs.GetInt("profile.tokens", 0); set { PlayerPrefs.SetInt("profile.tokens", value); PlayerPrefs.Save(); } }

    static Sprite[] sprites;
    public static Sprite AvatarSprite(int n)
    {
        if (sprites == null) sprites = new Sprite[AvatarCount + 1];
        n = Mathf.Clamp(n, 1, AvatarCount);
        if (sprites[n] == null)
        {
            var tex = Resources.Load<Texture2D>("Art/avatars/avatar_" + n);
            if (tex != null) sprites[n] = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100);
        }
        return sprites[n];
    }
}
