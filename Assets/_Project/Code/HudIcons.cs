using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generates lightweight, crisp runtime Sprite icons for HUD portraits, 4-slot equipment,
/// consumables, and World Map markers without external texture asset dependencies.
/// </summary>
public static class HudIcons
{
    private static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCache() => _cache.Clear();

    public static Sprite GetForItem(ItemData item)
    {
        if (item == null) return GetSprite("empty");
        if (item.icon != null) return item.icon;

        return item.type switch
        {
            ItemType.Weapon => GetSprite("sword"),
            ItemType.Shield => GetSprite("shield"),
            ItemType.Armor => GetSprite("armor"),
            ItemType.Equipment => GetSprite("armor"),
            ItemType.Pants => GetSprite("pants"),
            ItemType.Consumable => GetSprite("potion"),
            _ => GetSprite("material")
        };
    }

    public static Sprite GetForEquipmentSlot(EquipmentSlotType slot)
    {
        return slot switch
        {
            EquipmentSlotType.Sword => GetSprite("sword"),
            EquipmentSlotType.Shield => GetSprite("shield"),
            EquipmentSlotType.Armor => GetSprite("armor"),
            EquipmentSlotType.Pants => GetSprite("pants"),
            _ => GetSprite("empty")
        };
    }

    public static Sprite GetSprite(string kind)
    {
        if (_cache.TryGetValue(kind, out Sprite cached) && cached != null)
            return cached;

        Texture2D tex = BuildTexture(kind, 32);
        Sprite sp = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        sp.name = "HudIcon_" + kind;
        _cache[kind] = sp;
        return sp;
    }

    private static Texture2D BuildTexture(string kind, int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        Color clear = new Color(0f, 0f, 0f, 0f);
        Color[] pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;

        float half = (size - 1) * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = (x - half) / half; // -1..1
                float ny = (y - half) / half; // -1..1
                bool fill = false;

                switch (kind)
                {
                    case "circle":
                        fill = (nx * nx + ny * ny) <= 0.85f;
                        break;

                    case "diamond":
                        fill = (Mathf.Abs(nx) + Mathf.Abs(ny)) <= 0.88f;
                        break;

                    case "player_arrow":
                        fill = (ny >= -0.7f && Mathf.Abs(nx) <= (0.85f - ny) * 0.52f) ||
                               (ny < -0.7f && Mathf.Abs(nx) <= 0.15f);
                        break;

                    case "waypoint":
                        float dy = ny - 0.28f;
                        bool head = (nx * nx + dy * dy) <= 0.24f;
                        bool needle = ny < 0.28f && ny >= -0.85f && Mathf.Abs(nx) <= (ny + 0.85f) * 0.42f;
                        fill = head || needle;
                        break;

                    case "chest":
                        bool boxBody = Mathf.Abs(nx) <= 0.78f && ny >= -0.68f && ny <= 0.48f;
                        bool lidGap = Mathf.Abs(ny - 0.10f) <= 0.06f && Mathf.Abs(nx) > 0.18f;
                        bool lockKnob = Mathf.Abs(nx) <= 0.16f && ny >= -0.05f && ny <= 0.22f;
                        fill = (boxBody && !lidGap) || lockKnob;
                        break;

                    case "boss":
                        // Crown + Skull emblem
                        bool skull = (nx * nx * 1.1f + (ny + 0.18f) * (ny + 0.18f)) <= 0.36f;
                        bool crownBase = ny >= 0.25f && ny <= 0.46f && Mathf.Abs(nx) <= 0.65f;
                        bool crownSpikes = ny > 0.46f && ny <= 0.82f &&
                            (Mathf.Abs(nx) <= 0.14f || Mathf.Abs(Mathf.Abs(nx) - 0.52f) <= 0.14f);
                        bool eyes = (Mathf.Abs(Mathf.Abs(nx) - 0.20f) <= 0.08f && Mathf.Abs(ny + 0.12f) <= 0.08f);
                        fill = (skull && !eyes) || crownBase || crownSpikes;
                        break;

                    case "camp":
                        // Crossed banner / camp ring
                        float r2 = nx * nx + ny * ny;
                        bool ring = r2 <= 0.86f && r2 >= 0.48f;
                        bool core = r2 <= 0.18f;
                        fill = ring || core;
                        break;

                    case "avatar":
                        bool headCircle = (nx * nx + (ny - 0.30f) * (ny - 0.30f)) <= 0.14f;
                        bool shoulders = ny >= -0.78f && ny <= 0.02f && Mathf.Abs(nx) <= 0.68f * (1f - (ny + 0.78f) * 0.35f);
                        fill = headCircle || shoulders;
                        break;

                    case "sword":
                        float dDiag = Mathf.Abs(nx - ny);
                        bool blade = dDiag <= 0.15f && (nx + ny) >= -0.35f && (nx + ny) <= 1.35f;
                        bool guard = Mathf.Abs(nx + ny + 0.25f) <= 0.12f && Mathf.Abs(nx - ny) <= 0.52f;
                        bool hilt = dDiag <= 0.10f && (nx + ny) < -0.35f && (nx + ny) >= -1.15f;
                        fill = blade || guard || hilt;
                        break;

                    case "shield":
                        float maxW = ny >= 0f ? 0.68f : Mathf.Lerp(0.68f, 0.05f, (-ny) / 0.82f);
                        bool insideShield = ny <= 0.72f && ny >= -0.82f && Mathf.Abs(nx) <= maxW;
                        bool emblemCut = Mathf.Abs(nx) <= 0.08f && Mathf.Abs(ny) <= 0.35f;
                        fill = insideShield && !emblemCut;
                        break;

                    case "armor":
                        bool torso = Mathf.Abs(nx) <= 0.52f && ny >= -0.75f && ny <= 0.55f;
                        bool shoulder = ny >= 0.22f && ny <= 0.68f && Mathf.Abs(nx) <= 0.78f;
                        bool neckCut = ny >= 0.42f && Mathf.Abs(nx) <= 0.24f;
                        fill = (torso || shoulder) && !neckCut;
                        break;

                    case "pants":
                        bool waist = ny >= 0.32f && ny <= 0.70f && Mathf.Abs(nx) <= 0.62f;
                        bool legs = ny >= -0.78f && ny < 0.32f && Mathf.Abs(nx) <= 0.62f && Mathf.Abs(nx) >= 0.14f;
                        fill = waist || legs;
                        break;

                    case "potion":
                        bool flask = (nx * nx + (ny + 0.22f) * (ny + 0.22f)) <= 0.34f;
                        bool neck = ny >= 0.22f && ny <= 0.68f && Mathf.Abs(nx) <= 0.20f;
                        bool cork = ny > 0.68f && ny <= 0.84f && Mathf.Abs(nx) <= 0.26f;
                        fill = flask || neck || cork;
                        break;

                    case "material":
                        fill = Mathf.Abs(nx) <= 0.58f && Mathf.Abs(ny) <= 0.48f && (Mathf.Abs(nx) + Mathf.Abs(ny) <= 0.86f);
                        break;

                    default:
                        bool border = Mathf.Abs(nx) >= 0.75f || Mathf.Abs(ny) >= 0.75f;
                        fill = border;
                        break;
                }

                if (fill)
                    pixels[y * size + x] = Color.white;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }
}
