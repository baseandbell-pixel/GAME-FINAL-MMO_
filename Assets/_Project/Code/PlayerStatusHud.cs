using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Minimal HUD &amp; Safe Zone Indicator.
/// In accordance with UI cleanup rules, all cluttered top-right cards, avatar portraits,
/// and redundant stat bars have been removed to keep the screen clean and minimal.
/// This component provides strictly:
///   - Clean minimal badge: "Safe Zone - Combat Disabled" when inside the Comfort Zone.
///   - Top-center Boss respawn countdown banner when inside the Boss Arena cooldown.
/// </summary>
public class PlayerStatusHud : MonoBehaviour
{
    public static PlayerStatusHud Instance { get; private set; }

    public bool IsSafeZoneBannerVisible => _safeZoneBanner != null && _safeZoneBanner.activeSelf;
    public string SafeZoneBannerText => _safeZoneBannerText != null ? _safeZoneBannerText.text : string.Empty;
    public bool IsBossBannerVisible => _bossBanner != null && _bossBanner.activeSelf;
    public string BossBannerText => _bossBannerText != null ? _bossBannerText.text : string.Empty;
    public string CombatReadinessText => string.Empty;
    public string LevelBadgeText => _combat != null ? $"Lv.{_combat.NetworkLevel}" : "Lv.1";
    public string GoldBadgeText => _combat != null ? $"{_combat.NetworkGold} G" : "0 G";
    public bool IsToastVisible => false;
    public string ToastText => string.Empty;

    public void ShowToast(string message, Color? color = null) { }

    private PlayerCombat _combat;
    private Font _font;

    private GameObject _safeZoneBanner;
    private Text _safeZoneBannerText;
    private GameObject _bossBanner;
    private Text _bossBannerText;

    public static PlayerStatusHud EnsureFor(PlayerCombat combat)
    {
        if (combat == null || !combat.IsLocallyControlled) return null;
        if (Instance != null) return Instance;
        return combat.gameObject.AddComponent<PlayerStatusHud>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
        _combat = GetComponent<PlayerCombat>();
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildCleanMinimalHud();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void BuildCleanMinimalHud()
    {
        var canvasGo = new GameObject("MinimalPlayerHudCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // -------------------------------------------------------------
        // 1. Clean Minimal Safe Zone Badge (Top Center / Near Crosshair)
        // -------------------------------------------------------------
        _safeZoneBanner = new GameObject("SafeZoneBadge", typeof(RectTransform));
        _safeZoneBanner.transform.SetParent(canvasGo.transform, false);
        var szRt = (RectTransform)_safeZoneBanner.transform;
        szRt.anchorMin = szRt.anchorMax = szRt.pivot = new Vector2(0.5f, 1f);
        szRt.anchoredPosition = new Vector2(0f, -24f);
        szRt.sizeDelta = new Vector2(360f, 38f);

        var bgImg = MakeImage(_safeZoneBanner.transform, "Bg", new Color(0.04f, 0.16f, 0.12f, 0.90f));
        FillParent(bgImg.rectTransform);

        var borderImg = MakeImage(_safeZoneBanner.transform, "Border", new Color(0.20f, 0.88f, 0.62f, 0.95f));
        FillParent(borderImg.rectTransform);
        var innerBorder = MakeImage(borderImg.transform, "Inner", new Color(0.04f, 0.16f, 0.12f, 0.90f));
        FillParentWithPadding(innerBorder.rectTransform, 1.5f);

        _safeZoneBannerText = MakeText(innerBorder.transform, "Text", "Safe Zone - Combat Disabled", 15,
            FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.35f, 1f, 0.75f, 1f));
        _safeZoneBanner.SetActive(false);

        // -------------------------------------------------------------
        // 2. Top-Center Boss Respawn Countdown Banner
        // -------------------------------------------------------------
        _bossBanner = new GameObject("BossRespawnBanner", typeof(RectTransform));
        _bossBanner.transform.SetParent(canvasGo.transform, false);
        var bbRt = (RectTransform)_bossBanner.transform;
        bbRt.anchorMin = bbRt.anchorMax = bbRt.pivot = new Vector2(0.5f, 1f);
        bbRt.anchoredPosition = new Vector2(0f, -68f);
        bbRt.sizeDelta = new Vector2(420f, 34f);

        var bBg = MakeImage(_bossBanner.transform, "Bg", new Color(0.18f, 0.04f, 0.06f, 0.88f));
        FillParent(bBg.rectTransform);

        _bossBannerText = MakeText(bBg.transform, "Text", "Boss respawns in 00:00", 14,
            FontStyle.Bold, TextAnchor.MiddleCenter, new Color(1f, 0.35f, 0.35f, 1f));
        _bossBanner.SetActive(false);
    }

    private void Update()
    {
        if (_combat == null || !_combat.IsSpawned || !_combat.IsLocallyControlled) return;

        bool inSafeZone = SafeZone.IsPlayerInAnySafeZone(_combat);
        if (_safeZoneBanner != null && _safeZoneBanner.activeSelf != inSafeZone)
        {
            _safeZoneBanner.SetActive(inSafeZone);
        }

        var spawner = SandboxWorldSpawner.Instance;
        bool showBoss = spawner != null && spawner.ShouldShowBossRespawnBanner;
        if (_bossBanner != null)
        {
            if (_bossBanner.activeSelf != showBoss)
                _bossBanner.SetActive(showBoss);

            if (showBoss && _bossBannerText != null)
                _bossBannerText.text = spawner.GetBossRespawnCountdownText();
        }
    }

    private static Image MakeImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private Text MakeText(Transform parent, string name, string text, int size, FontStyle style,
        TextAnchor align, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.transform.SetParent(parent, false);
        FillParent((RectTransform)go.transform);
        var t = go.GetComponent<Text>();
        t.font = _font;
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = align;
        t.color = color;
        t.text = text;
        t.raycastTarget = false;
        return t;
    }

    private static void FillParent(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void FillParentWithPadding(RectTransform rt, float pad)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(pad, pad);
        rt.offsetMax = new Vector2(-pad, -pad);
    }
}
