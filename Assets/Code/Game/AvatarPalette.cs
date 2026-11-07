using UnityEngine;

/// <summary>
/// The one place that maps an avatar id (0..AvatarCount-1) to a colour, so the
/// lobby card, the capsule in the map and the name label always agree.
/// </summary>
public static class AvatarPalette
{
    static readonly Color[] Colors =
    {
        new Color(0.20f, 0.55f, 0.95f),   // 0 - blue
        new Color(0.90f, 0.30f, 0.30f),   // 1 - red
        new Color(0.25f, 0.75f, 0.35f),   // 2 - green
        new Color(0.95f, 0.75f, 0.20f),   // 3 - yellow
    };

    public static int Count => Colors.Length;

    public static Color Get(int avatarIndex)
    {
        if (Colors.Length == 0) return Color.white;
        return Colors[Mathf.Clamp(avatarIndex, 0, Colors.Length - 1)];
    }
}
