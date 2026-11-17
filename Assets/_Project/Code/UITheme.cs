using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ธีมสีและสไปรต์มาตรฐานสำหรับ UI สไตล์ Action-MMORPG (Where Winds Meet Aesthetic):
///   - โทนสีหลัก: Dark Obsidian + Brushed Gold + Jade Accent
///   - สร้าง 9-Sliced Procedural Sprites สำหรับกรอบหน้าต่าง ช่องไอเทม ปุ่มกด และแถบพลัง โดยไม่ต้องพึ่งไฟล์ภายนอก
/// </summary>
public static class UITheme
{
    // ชุดสีหลักของระบบ UI (Dark Obsidian + Brushed Gold + Jade Accent)
    public static readonly Color ObsidianDeep = new Color(0.042f, 0.048f, 0.070f, 0.97f);
    public static readonly Color ObsidianPanel = new Color(0.068f, 0.078f, 0.110f, 0.98f);
    public static readonly Color ObsidianHeader = new Color(0.085f, 0.098f, 0.140f, 1.00f);
    public static readonly Color ObsidianSlot = new Color(0.095f, 0.108f, 0.148f, 1.00f);
    public static readonly Color ObsidianSlotEmpty = new Color(0.078f, 0.090f, 0.125f, 0.95f);

    public static readonly Color BrushedGold = new Color(0.76f, 0.61f, 0.28f, 1.00f);
    public static readonly Color BrushedGoldDark = new Color(0.45f, 0.36f, 0.17f, 0.96f);
    public static readonly Color BrushedGoldBright = new Color(0.99f, 0.86f, 0.44f, 1.00f);

    public static readonly Color JadeAccent = new Color(0.18f, 0.84f, 0.64f, 1.00f);
    public static readonly Color JadeButton = new Color(0.12f, 0.44f, 0.34f, 1.00f);

    public static readonly Color TextPrimary = new Color(0.95f, 0.96f, 0.98f, 1.00f);
    public static readonly Color TextSecondary = new Color(0.74f, 0.79f, 0.87f, 1.00f);
    public static readonly Color TextMuted = new Color(0.52f, 0.57f, 0.65f, 1.00f);
    public static readonly Color DropShadowColor = new Color(0f, 0f, 0f, 0.65f);

    private static Sprite _slicedFrameSprite;
    private static Sprite _slicedFillSprite;
    private static Font _uiFont;

    /// <summary>
    /// คืนค่าฟอนต์หลักสำหรับ UI ที่อ่านง่ายและคมชัดบนหน้าจอความละเอียดสูง
    /// </summary>
    public static Font GetFont()
    {
        if (_uiFont != null) return _uiFont;
        _uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return _uiFont;
    }

    /// <summary>
    /// สร้างและแคชสไปรต์กรอบ 9-Sliced ขอบมนเล็กน้อยพร้อมไฮไลต์ด้านใน
    /// </summary>
    public static Sprite GetSlicedPanelSprite()
    {
        if (_slicedFrameSprite != null) return _slicedFrameSprite;

        const int size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool corner = (x < 2 || x >= size - 2) && (y < 2 || y >= size - 2);
                if (corner)
                {
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, 0.35f));
                }
                else
                {
                    // ไล่ระดับความสว่างขอบบนเล็กน้อยเพื่อให้ดูมีมิติแบบแผ่นโลหะขัดเงา
                    float grad = Mathf.Lerp(0.90f, 1.00f, y / (float)(size - 1));
                    tex.SetPixel(x, y, new Color(grad, grad, grad, 1f));
                }
            }
        }
        tex.Apply();
        _slicedFrameSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(6, 6, 6, 6));
        return _slicedFrameSprite;
    }

    /// <summary>
    /// สร้างสไปรต์สำหรับแถบ Progress Bar / Fill
    /// </summary>
    public static Sprite GetFillSprite()
    {
        if (_slicedFillSprite != null) return _slicedFillSprite;
        var tex = new Texture2D(8, 8, TextureFormat.RGBA32, false);
        for (int y = 0; y < 8; y++)
        {
            float v = Mathf.Lerp(0.84f, 1.0f, y / 7f);
            for (int x = 0; x < 8; x++)
                tex.SetPixel(x, y, new Color(v, v, v, 1f));
        }
        tex.Apply();
        _slicedFillSprite = Sprite.Create(tex, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f));
        return _slicedFillSprite;
    }

    /// <summary>
    /// ตกแต่ง Image ให้ใช้สไปรต์ 9-Sliced ของธีม
    /// </summary>
    public static void ApplySlicedStyle(Image img, Color color)
    {
        if (img == null) return;
        img.sprite = GetSlicedPanelSprite();
        img.type = Image.Type.Sliced;
        img.color = color;
    }

    /// <summary>
    /// เพิ่มเงา Drop Shadow ให้ข้อความหรือกรอบเพื่อให้โดดเด่นบนพื้นหลังสีเข้ม
    /// </summary>
    public static void AddShadow(GameObject go, float offset = 1.5f)
    {
        if (go == null) return;
        var sh = go.GetComponent<Shadow>() ?? go.AddComponent<Shadow>();
        sh.effectColor = DropShadowColor;
        sh.effectDistance = new Vector2(offset, -offset);
    }
}
