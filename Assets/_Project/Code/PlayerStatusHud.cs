using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Phase 2 Top-Right Player Status HUD &amp; Top-Center Boss Respawn Banner.
/// Replaces the legacy bottom-left bars with a unified MMORPG status frame containing:
///   - Avatar portrait placeholder slot + Level badge
///   - Combat Readiness indicator (Combat Ready / In Combat)
///   - HP, Stamina, and EXP bars (+ Unspent Stat Points notice)
///   - Top-Center Boss Respawn Countdown Banner ("Boss respawns in MM:SS") shown strictly
///     while the local player is inside the Boss Arena cooldown radius.
/// </summary>
public class PlayerStatusHud : MonoBehaviour
{
    public static PlayerStatusHud Instance { get; private set; }

    public bool IsBossBannerVisible => _bossBanner != null && _bossBanner.activeSelf;
    public string BossBannerText => _bossBannerText != null ? _bossBannerText.text : string.Empty;
    public bool IsSafeZoneBannerVisible => _safeZoneBanner != null && _safeZoneBanner.activeSelf;
    public string SafeZoneBannerText => _safeZoneBannerText != null ? _safeZoneBannerText.text : string.Empty;
    public string CombatReadinessText => _readinessText != null ? _readinessText.text : string.Empty;
    public string LevelBadgeText => _levelBadgeText != null ? _levelBadgeText.text : string.Empty;
    public string GoldBadgeText => _goldText != null ? _goldText.text : string.Empty;
    public bool IsToastVisible => _toastBanner != null && _toastBanner.activeSelf;
    public string ToastText => _toastText != null ? _toastText.text : string.Empty;

    private PlayerCombat _combat;
    private PlayerCapsule _capsule;
    private Font _font;

    // Top-Right Player HUD
    private RectTransform _statusCard;
    private Image _avatarBg;
    private Image _avatarSilhouette;
    private Text _levelBadgeText;
    private Text _playerNameText;
    private Image _readinessDot;
    private Text _readinessText;
    private Image _hpFill;
    private Text _hpText;
    private Image _staFill;
    private Text _staText;
    private Image _expFill;
    private Text _expText;
    private Text _goldText;
    private Text _pointsNoticeText;

    // Safe Zone Banner & Dynamic Toast (anchored cleanly under Top-Right HUD)
    private GameObject _safeZoneBanner;
    private Text _safeZoneBannerText;
    private GameObject _toastBanner;
    private RectTransform _toastRect;
    private Image _toastBorder;
    private Text _toastText;
    private float _toastTimer;

    // Top-Center Boss Banner
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
        _capsule = GetComponent<PlayerCapsule>();
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildHud();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void BuildHud()
    {
        var canvasGo = new GameObject("PlayerStatusHudCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 35;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // ============================================================
        // 1. TOP-RIGHT PLAYER STATUS HUD CARD (368 x 140)
        // ============================================================
        var outer = MakeImage(canvasGo.transform, "TopRightStatusCard", new Color(0.55f, 0.45f, 0.22f, 0.92f));
        outer.raycastTarget = false;
        _statusCard = outer.rectTransform;
        _statusCard.anchorMin = _statusCard.anchorMax = _statusCard.pivot = new Vector2(1f, 1f);
        _statusCard.anchoredPosition = new Vector2(-18f, -18f);
        _statusCard.sizeDelta = new Vector2(368f, 140f);

        var inner = MakeImage(_statusCard, "InnerPanel", new Color(0.05f, 0.06f, 0.09f, 0.94f));
        inner.raycastTarget = false;
        FillParentWithPadding(inner.rectTransform, 2f);

        // Avatar Portrait Placeholder Slot (Left 76x76)
        var avatarFrame = MakeImage(inner.transform, "AvatarFrame", new Color(0.32f, 0.28f, 0.18f, 1f));
        PlaceTopLeft(avatarFrame.rectTransform, 10f, 12f, 76f, 76f);
        _avatarBg = MakeImage(avatarFrame.transform, "AvatarBg", new Color(0.11f, 0.13f, 0.18f, 1f));
        FillParentWithPadding(_avatarBg.rectTransform, 2f);

        _avatarSilhouette = MakeImage(_avatarBg.transform, "AvatarPortrait", new Color(0.75f, 0.85f, 1f, 0.95f));
        _avatarSilhouette.sprite = HudIcons.GetSprite("avatar");
        FillParentWithPadding(_avatarSilhouette.rectTransform, 8f);

        // Level Badge Pill (Bottom-center/right of Portrait)
        var lvlBg = MakeImage(avatarFrame.transform, "LevelBadge", new Color(0.14f, 0.11f, 0.05f, 0.96f));
        PlaceTopLeft(lvlBg.rectTransform, 12f, 64f, 52f, 20f);
        _levelBadgeText = MakeText(lvlBg.transform, "LevelText", "Lv.1", 12, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Color(1f, 0.82f, 0.22f, 1f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        // Header Row: Player Name + Combat Readiness Badge
        _playerNameText = MakeText(inner.transform, "PlayerName", "Player", 15, FontStyle.Bold, TextAnchor.MiddleLeft,
            Color.white, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(96f, -10f), new Vector2(114f, 22f));

        var readyBg = MakeImage(inner.transform, "ReadinessPill", new Color(0.08f, 0.12f, 0.14f, 0.95f));
        PlaceTopLeft(readyBg.rectTransform, 214f, 10f, 140f, 20f);

        _readinessDot = MakeImage(readyBg.transform, "Dot", new Color(0.20f, 0.90f, 0.45f, 1f));
        _readinessDot.sprite = HudIcons.GetSprite("circle");
        PlaceTopLeft(_readinessDot.rectTransform, 7f, 6f, 8f, 8f);

        _readinessText = MakeText(readyBg.transform, "ReadinessText", "COMBAT READY", 10, FontStyle.Bold, TextAnchor.MiddleLeft,
            new Color(0.40f, 0.95f, 0.65f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, 0f), new Vector2(118f, 20f));

        // Bar 1: HP Bar
        CreateBar(inner.transform, "HPBar", 96f, 38f, 258f, 22f, new Color(0.16f, 0.05f, 0.07f, 1f),
            new Color(0.22f, 0.85f, 0.36f, 1f), 12, out _hpFill, out _hpText);

        // Bar 2: Stamina Bar
        CreateBar(inner.transform, "StaminaBar", 96f, 64f, 258f, 19f, new Color(0.06f, 0.12f, 0.16f, 1f),
            new Color(0.18f, 0.78f, 0.98f, 1f), 11, out _staFill, out _staText);

        // Bar 3: EXP Bar + Gold Counter Pill
        CreateBar(inner.transform, "ExpBar", 10f, 94f, 236f, 17f, new Color(0.10f, 0.08f, 0.16f, 1f),
            new Color(0.62f, 0.36f, 0.96f, 1f), 10, out _expFill, out _expText);

        var goldPill = MakeImage(inner.transform, "GoldPill", new Color(0.18f, 0.14f, 0.04f, 0.98f));
        PlaceTopLeft(goldPill.rectTransform, 252f, 94f, 102f, 17f);
        var coinDot = MakeImage(goldPill.transform, "CoinIcon", new Color(1f, 0.84f, 0.22f, 1f));
        coinDot.sprite = HudIcons.GetSprite("circle");
        PlaceTopLeft(coinDot.rectTransform, 6f, 3.5f, 10f, 10f);
        _goldText = MakeText(goldPill.transform, "GoldText", "0 G", 11, FontStyle.Bold, TextAnchor.MiddleRight,
            new Color(1f, 0.88f, 0.35f, 1f), Vector2.zero, Vector2.one, new Vector2(18f, 0f), new Vector2(-6f, 0f));

        // Bottom Micro-Hint / Unspent Points Row
        _pointsNoticeText = MakeText(inner.transform, "HudHint", "[I / Tab] Character & Gear   |   [M] World Map", 11, FontStyle.Normal,
            TextAnchor.MiddleLeft, new Color(0.72f, 0.76f, 0.84f, 0.95f),
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(10f, -114f), new Vector2(344f, 18f));

        // ============================================================
        // 1B. SAFE ZONE STATUS INDICATOR BANNER
        // ============================================================
        var szOuter = MakeImage(canvasGo.transform, "SafeZoneBanner", new Color(0.18f, 0.90f, 0.58f, 0.95f));
        _safeZoneBanner = szOuter.gameObject;
        var szrt = szOuter.rectTransform;
        szrt.anchorMin = szrt.anchorMax = szrt.pivot = new Vector2(1f, 1f);
        szrt.anchoredPosition = new Vector2(-18f, -166f);
        szrt.sizeDelta = new Vector2(368f, 30f);

        var szInner = MakeImage(szrt, "Inner", new Color(0.04f, 0.12f, 0.09f, 0.95f));
        FillParentWithPadding(szInner.rectTransform, 2f);

        var szIcon = MakeImage(szInner.transform, "ShieldIcon", new Color(0.24f, 0.96f, 0.66f, 1f));
        szIcon.sprite = HudIcons.GetSprite("shield");
        PlaceTopLeft(szIcon.rectTransform, 10f, 4f, 18f, 18f);

        _safeZoneBannerText = MakeText(szInner.transform, "SafeZoneText", "Safe Zone - Combat Disabled", 13, FontStyle.Bold,
            TextAnchor.MiddleLeft, new Color(0.32f, 0.98f, 0.70f, 1f),
            Vector2.zero, Vector2.one, new Vector2(34f, 0f), new Vector2(-10f, 0f));
        _safeZoneBanner.SetActive(false);

        // ============================================================
        // 1C. GOLD / SYSTEM TOAST BANNER
        // ============================================================
        var toastOuter = MakeImage(canvasGo.transform, "HudToastBanner", new Color(1f, 0.84f, 0.25f, 0.95f));
        _toastBorder = toastOuter;
        _toastBanner = toastOuter.gameObject;
        _toastRect = toastOuter.rectTransform;
        _toastRect.anchorMin = _toastRect.anchorMax = _toastRect.pivot = new Vector2(1f, 1f);
        _toastRect.anchoredPosition = new Vector2(-18f, -166f);
        _toastRect.sizeDelta = new Vector2(368f, 32f);

        var toastInner = MakeImage(_toastRect, "Inner", new Color(0.06f, 0.07f, 0.10f, 0.95f));
        FillParentWithPadding(toastInner.rectTransform, 2f);

        _toastText = MakeText(toastInner.transform, "ToastText", "", 13, FontStyle.Bold,
            TextAnchor.MiddleCenter, new Color(1f, 0.90f, 0.35f, 1f),
            Vector2.zero, Vector2.one, new Vector2(8f, 0f), new Vector2(-8f, 0f));
        _toastBanner.SetActive(false);

        // ============================================================
        // 2. TOP-CENTER BOSS RESPAWN BANNER
        // ============================================================
        var bannerOuter = MakeImage(canvasGo.transform, "BossRespawnBanner", new Color(0.85f, 0.22f, 0.18f, 0.92f));
        bannerOuter.raycastTarget = false;
        _bossBanner = bannerOuter.gameObject;
        var brt = bannerOuter.rectTransform;
        brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 1f);
        brt.anchoredPosition = new Vector2(0f, -18f);
        brt.sizeDelta = new Vector2(340f, 42f);

        var bannerInner = MakeImage(brt, "Inner", new Color(0.10f, 0.04f, 0.06f, 0.94f));
        FillParentWithPadding(bannerInner.rectTransform, 2f);

        var skullIcon = MakeImage(bannerInner.transform, "BossIcon", new Color(1f, 0.42f, 0.24f, 1f));
        skullIcon.sprite = HudIcons.GetSprite("boss");
        PlaceTopLeft(skullIcon.rectTransform, 14f, 8f, 22f, 22f);

        _bossBannerText = MakeText(bannerInner.transform, "CountdownText", "Boss respawns in 04:00", 16, FontStyle.Bold,
            TextAnchor.MiddleCenter, new Color(1f, 0.86f, 0.35f, 1f),
            Vector2.zero, Vector2.one, new Vector2(18f, 0f), new Vector2(-18f, 0f));

        _bossBanner.SetActive(false);
    }

    public void ShowToast(string text, Color color, float duration = 2.8f)
    {
        if (_toastBanner == null || _toastText == null) return;
        _toastText.text = text;
        _toastText.color = color;
        if (_toastBorder != null) _toastBorder.color = color;
        _toastTimer = Mathf.Max(0.5f, duration);
        _toastBanner.SetActive(true);
    }

    private void Update()
    {
        if (_toastTimer > 0f)
        {
            _toastTimer -= Time.unscaledDeltaTime;
            if (_toastTimer <= 0f && _toastBanner != null)
                _toastBanner.SetActive(false);
        }

        if (_combat == null || !_combat.IsSpawned) return;

        // 1. Update Top-Right Status Card
        int lvl = Mathf.Max(1, _combat.NetworkLevel);
        if (_levelBadgeText != null) _levelBadgeText.text = $"Lv.{lvl}";
        if (_goldText != null) _goldText.text = $"{_combat.NetworkGold:N0} G";

        if (_playerNameText != null && _capsule != null)
        {
            string nick = _capsule.Nick.Value;
            _playerNameText.text = string.IsNullOrEmpty(nick) ? "Player" : nick;
        }

        if (_avatarSilhouette != null && _capsule != null)
        {
            _avatarSilhouette.color = AvatarPalette.Get(_capsule.AvatarIndex);
        }

        // Safe Zone & Combat Readiness Status
        bool inSafeZone = (_combat.IsInSafeZone || SafeZone.IsPlayerInAnySafeZone(_combat)) && !_combat.IsDead;
        bool inCombat = _combat.IsInCombat;

        if (_safeZoneBanner != null)
        {
            if (_safeZoneBanner.activeSelf != inSafeZone)
                _safeZoneBanner.SetActive(inSafeZone);
        }

        if (_toastRect != null)
        {
            _toastRect.anchoredPosition = new Vector2(-18f, inSafeZone ? -204f : -166f);
        }

        if (_readinessDot != null && _readinessText != null)
        {
            if (_combat.IsDead)
            {
                _readinessDot.color = new Color(0.55f, 0.55f, 0.6f, 1f);
                _readinessText.color = new Color(0.75f, 0.75f, 0.8f, 1f);
                _readinessText.text = "DEFEATED";
            }
            else if (inSafeZone)
            {
                float pulse = 0.75f + Mathf.Sin(Time.unscaledTime * 3.5f) * 0.25f;
                _readinessDot.color = new Color(0.18f, 0.95f, 0.65f, pulse);
                _readinessText.color = new Color(0.32f, 0.98f, 0.72f, 1f);
                _readinessText.text = "SAFE ZONE";
            }
            else if (inCombat)
            {
                float pulse = 0.7f + Mathf.Sin(Time.unscaledTime * 8f) * 0.3f;
                _readinessDot.color = new Color(1f, 0.25f, 0.18f, pulse);
                _readinessText.color = new Color(1f, 0.45f, 0.25f, 1f);
                _readinessText.text = "IN COMBAT";
            }
            else
            {
                _readinessDot.color = new Color(0.22f, 0.92f, 0.48f, 1f);
                _readinessText.color = new Color(0.42f, 0.96f, 0.68f, 1f);
                _readinessText.text = "COMBAT READY";
            }
        }

        // HP Bar
        int maxHp = Mathf.Max(1, _combat.MaxHealth);
        float hpFrac = Mathf.Clamp01(_combat.NetworkHealth / (float)maxHp);
        if (_hpFill != null)
        {
            _hpFill.rectTransform.anchorMax = new Vector2(hpFrac, 1f);
            _hpFill.color = Color.Lerp(new Color(0.86f, 0.15f, 0.18f), new Color(0.20f, 0.84f, 0.36f), hpFrac);
        }
        if (_hpText != null) _hpText.text = $"HP   {_combat.NetworkHealth} / {maxHp}";

        // Stamina Bar
        float maxSta = Mathf.Max(1f, _combat.MaxStamina);
        float staFrac = Mathf.Clamp01(_combat.CurrentStamina / maxSta);
        if (_staFill != null)
        {
            _staFill.rectTransform.anchorMax = new Vector2(staFrac, 1f);
            _staFill.color = _combat.CurrentStamina <= 15f
                ? new Color(1.0f, 0.38f, 0.12f)
                : Color.Lerp(new Color(0.95f, 0.65f, 0.12f), new Color(0.20f, 0.82f, 0.98f), staFrac);
        }
        if (_staText != null) _staText.text = $"STA  {_combat.CurrentStamina:0} / {maxSta:0}";

        // EXP Bar
        int nextExp = Mathf.Max(1, _combat.ExpToNextLevel);
        float expFrac = Mathf.Clamp01(_combat.NetworkExperience / (float)nextExp);
        if (_expFill != null)
            _expFill.rectTransform.anchorMax = new Vector2(expFrac, 1f);
        if (_expText != null)
            _expText.text = $"EXP  {_combat.NetworkExperience} / {nextExp}  ({Mathf.RoundToInt(expFrac * 100f)}%)";

        // Unspent Stat Points / Buff alert
        if (_pointsNoticeText != null)
        {
            if (_combat.NetworkUnspentStatPoints > 0)
            {
                _pointsNoticeText.text = $"★ {_combat.NetworkUnspentStatPoints} Stat Point(s) Available!  Press [I] to Upgrade";
                _pointsNoticeText.color = new Color(1f, 0.84f, 0.22f, 1f);
            }
            else if (inSafeZone)
            {
                _pointsNoticeText.text = "Sanctuary Active • HP/STA safe & Combat Locked";
                _pointsNoticeText.color = new Color(0.32f, 0.96f, 0.68f, 1f);
            }
            else if (_combat.IsBlocking)
            {
                _pointsNoticeText.text = "GUARD ACTIVE (70% Damage Reduction)";
                _pointsNoticeText.color = new Color(0.32f, 0.88f, 1f, 1f);
            }
            else if (_combat.ActiveBuffDamageBonus > 0)
            {
                _pointsNoticeText.text = $"BLADE BUFF: +{_combat.ActiveBuffDamageBonus} ATK ({_combat.ActiveBuffRemainingSeconds:0}s)";
                _pointsNoticeText.color = new Color(0.35f, 0.95f, 0.72f, 1f);
            }
            else
            {
                _pointsNoticeText.text = "[I / Tab] Character & Gear   |   [M] World Map";
                _pointsNoticeText.color = new Color(0.72f, 0.76f, 0.84f, 0.95f);
            }
        }

        // 2. Update Top-Center Boss Respawn Countdown Banner
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

    // ------------------------------------------------------------------ uGUI builders

    private void CreateBar(Transform parent, string name, float x, float y, float w, float h,
        Color bgColor, Color fillColor, int fontSize, out Image fillImg, out Text label)
    {
        var bg = MakeImage(parent, name, bgColor);
        PlaceTopLeft(bg.rectTransform, x, y, w, h);

        fillImg = MakeImage(bg.transform, "Fill", fillColor);
        var frt = fillImg.rectTransform;
        frt.anchorMin = Vector2.zero;
        frt.anchorMax = Vector2.one;
        frt.offsetMin = Vector2.zero;
        frt.offsetMax = Vector2.zero;

        label = MakeText(bg.transform, "Label", "", fontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
            Color.white, Vector2.zero, Vector2.one, new Vector2(8f, 0f), new Vector2(-8f, 0f));
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
        TextAnchor align, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredOrOffsetMin, Vector2 sizeOrOffsetMax)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        if (anchorMin == anchorMax)
        {
            rt.pivot = anchorMin;
            rt.anchoredPosition = anchoredOrOffsetMin;
            rt.sizeDelta = sizeOrOffsetMax;
        }
        else
        {
            rt.offsetMin = anchoredOrOffsetMin;
            rt.offsetMax = sizeOrOffsetMax;
        }
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

    private static void PlaceTopLeft(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
    }

    private static void FillParentWithPadding(RectTransform rt, float pad)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(pad, pad);
        rt.offsetMax = new Vector2(-pad, -pad);
    }
}
