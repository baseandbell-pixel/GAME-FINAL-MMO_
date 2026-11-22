using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// แผงสถานะผู้เล่นมุมขวาบน (Top-Right Player Status HUD) และแบนเนอร์นับถอยหลังบอสเกิดใหม่ (Top-Center Boss Respawn Banner):
///   - เชื่อมโยงรูปโปรไฟล์ตัวละครมุมขวาบนเข้ากับรูป Avatar ที่เลือกมาจากหน้า Lobby (Avatar_0..3) โดยรักษาสัดส่วนภาพ (preserveAspect) ไม่ให้ยืดเบี้ยว
///   - แสดงป้ายเลเวล, สถานะพร้อมรบ (Combat Ready / In Combat / Safe Zone), หลอด HP, Stamina, EXP และจำนวน Gold
///   - แสดงแบนเนอร์นับถอยหลังเวลาเกิดของบอสตรงกลางด้านบนเฉพาะเมื่อผู้เล่นอยู่ในรัศมีลานบอส
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
    public Sprite CurrentAvatarSprite => _avatarSilhouette != null ? _avatarSilhouette.sprite : null;
    public bool IsAvatarPreserveAspect => _avatarSilhouette != null && _avatarSilhouette.preserveAspect;

    private PlayerCombat _combat;
    private PlayerCapsule _capsule;
    private Font _font;

    // คอมโพเนนต์ของการ์ดสถานะมุมขวาบน
    private RectTransform _statusCard;
    private Image _avatarFrameBorder;
    private Image _avatarBg;
    private Image _avatarSilhouette;
    private int _displayedAvatarIndex = -1;
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

    // แบนเนอร์แจ้งเตือนเขตปลอดภัย (Safe Zone) และข้อความแจ้งเตือนชั่วคราว (Toast)
    private GameObject _safeZoneBanner;
    private Text _safeZoneBannerText;
    private GameObject _toastBanner;
    private RectTransform _toastRect;
    private Image _toastBorder;
    private Text _toastText;
    private float _toastTimer;

    // แบนเนอร์นับถอยหลังบอสเกิดใหม่ตรงกลางด้านบน
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

    /// <summary>
    /// สร้างโครงสร้าง UI ทั้งหมดของการ์ดสถานะมุมขวาบนและแบนเนอร์แจ้งเตือน
    /// </summary>
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
        // 1. การ์ดสถานะผู้เล่นมุมขวาบน (ขยายขนาดให้คมชัด อ่านง่าย 410 x 154)
        // ============================================================
        var outer = MakeImage(canvasGo.transform, "TopRightStatusCard", new Color(0.58f, 0.48f, 0.24f, 0.95f));
        outer.raycastTarget = false;
        _statusCard = outer.rectTransform;
        _statusCard.anchorMin = _statusCard.anchorMax = _statusCard.pivot = new Vector2(1f, 1f);
        _statusCard.anchoredPosition = new Vector2(-18f, -18f);
        _statusCard.sizeDelta = new Vector2(410f, 154f);

        var inner = MakeImage(_statusCard, "InnerPanel", new Color(0.05f, 0.06f, 0.09f, 0.95f));
        inner.raycastTarget = false;
        FillParentWithPadding(inner.rectTransform, 2f);

        // กรอบรูปโปรไฟล์ตัวละครจากฉาก Lobby (ขนาด 84x84 รักษาสัดส่วนภาพไม่ให้ยืด)
        _avatarFrameBorder = MakeImage(inner.transform, "AvatarFrame", new Color(0.75f, 0.62f, 0.28f, 1f));
        PlaceTopLeft(_avatarFrameBorder.rectTransform, 10f, 12f, 84f, 84f);
        _avatarBg = MakeImage(_avatarFrameBorder.transform, "AvatarBg", new Color(0.09f, 0.11f, 0.16f, 1f));
        _avatarBg.gameObject.AddComponent<RectMask2D>();
        FillParentWithPadding(_avatarBg.rectTransform, 2f);

        _avatarSilhouette = MakeImage(_avatarBg.transform, "AvatarPortrait", Color.white);
        _avatarSilhouette.sprite = HudIcons.GetLobbyAvatarSprite(0);
        _avatarSilhouette.preserveAspect = true;
        FillParentWithPadding(_avatarSilhouette.rectTransform, 2f);

        // ป้ายเลเวล (Level Badge - ขยายความกว้างเป็น 76px เพื่อรองรับข้อความ "Lv.30 MAX" ได้ชัดเจน)
        var lvlBg = MakeImage(_avatarFrameBorder.transform, "LevelBadge", new Color(0.12f, 0.10f, 0.04f, 0.96f));
        PlaceTopLeft(lvlBg.rectTransform, 4f, 68f, 76f, 22f);
        _levelBadgeText = MakeText(lvlBg.transform, "LevelText", "Lv.1", 12, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Color(1f, 0.84f, 0.25f, 1f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        // ชื่อผู้เล่นและสถานะความพร้อมรบ
        _playerNameText = MakeText(inner.transform, "PlayerName", "Player", 16, FontStyle.Bold, TextAnchor.MiddleLeft,
            Color.white, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(104f, -10f), new Vector2(142f, 24f));

        var readyBg = MakeImage(inner.transform, "ReadinessPill", new Color(0.08f, 0.12f, 0.14f, 0.95f));
        PlaceTopLeft(readyBg.rectTransform, 250f, 10f, 146f, 22f);

        _readinessDot = MakeImage(readyBg.transform, "Dot", new Color(0.20f, 0.90f, 0.45f, 1f));
        _readinessDot.sprite = HudIcons.GetSprite("circle");
        PlaceTopLeft(_readinessDot.rectTransform, 8f, 7f, 8f, 8f);

        _readinessText = MakeText(readyBg.transform, "ReadinessText", "COMBAT READY", 11, FontStyle.Bold, TextAnchor.MiddleLeft,
            new Color(0.40f, 0.95f, 0.65f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(22f, 0f), new Vector2(120f, 22f));

        // หลอด HP
        CreateBar(inner.transform, "HPBar", 104f, 40f, 292f, 24f, new Color(0.16f, 0.05f, 0.07f, 1f),
            new Color(0.22f, 0.85f, 0.36f, 1f), 13, out _hpFill, out _hpText);

        // หลอด Stamina
        CreateBar(inner.transform, "StaminaBar", 104f, 68f, 292f, 21f, new Color(0.06f, 0.12f, 0.16f, 1f),
            new Color(0.18f, 0.78f, 0.98f, 1f), 12, out _staFill, out _staText);

        // หลอด EXP และจำนวน Gold
        CreateBar(inner.transform, "ExpBar", 10f, 102f, 266f, 19f, new Color(0.10f, 0.08f, 0.16f, 1f),
            new Color(0.62f, 0.36f, 0.96f, 1f), 11, out _expFill, out _expText);

        var goldPill = MakeImage(inner.transform, "GoldPill", new Color(0.18f, 0.14f, 0.04f, 0.98f));
        PlaceTopLeft(goldPill.rectTransform, 282f, 102f, 114f, 19f);
        var coinDot = MakeImage(goldPill.transform, "CoinIcon", new Color(1f, 0.84f, 0.22f, 1f));
        coinDot.sprite = HudIcons.GetSprite("circle");
        PlaceTopLeft(coinDot.rectTransform, 7f, 4.5f, 10f, 10f);
        _goldText = MakeText(goldPill.transform, "GoldText", "0 G", 12, FontStyle.Bold, TextAnchor.MiddleRight,
            new Color(1f, 0.88f, 0.35f, 1f), Vector2.zero, Vector2.one, new Vector2(20f, 0f), new Vector2(-8f, 0f));

        // ข้อความแนะนำคีย์ลัดและแต้มอัปเกรดด้านล่างการ์ด
        _pointsNoticeText = MakeText(inner.transform, "HudHint", "[I / Tab] Gear & Blacksmith  |  [M] Map  |  [J] Item Guide", 11, FontStyle.Normal,
            TextAnchor.MiddleLeft, new Color(0.76f, 0.80f, 0.88f, 0.95f),
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(10f, -126f), new Vector2(386f, 20f));

        // ============================================================
        // 1B. แบนเนอร์แจ้งเตือนเมื่ออยู่ในเขตปลอดภัย (Safe Zone Banner)
        // ============================================================
        var szOuter = MakeImage(canvasGo.transform, "SafeZoneBanner", new Color(0.18f, 0.90f, 0.58f, 0.95f));
        _safeZoneBanner = szOuter.gameObject;
        var szrt = szOuter.rectTransform;
        szrt.anchorMin = szrt.anchorMax = szrt.pivot = new Vector2(1f, 1f);
        szrt.anchoredPosition = new Vector2(-18f, -178f);
        szrt.sizeDelta = new Vector2(410f, 32f);

        var szInner = MakeImage(szrt, "Inner", new Color(0.04f, 0.12f, 0.09f, 0.95f));
        FillParentWithPadding(szInner.rectTransform, 2f);

        var szIcon = MakeImage(szInner.transform, "ShieldIcon", new Color(0.24f, 0.96f, 0.66f, 1f));
        szIcon.sprite = HudIcons.GetSprite("shield");
        PlaceTopLeft(szIcon.rectTransform, 10f, 5f, 18f, 18f);

        _safeZoneBannerText = MakeText(szInner.transform, "SafeZoneText", "Safe Zone - Combat Disabled", 13, FontStyle.Bold,
            TextAnchor.MiddleLeft, new Color(0.32f, 0.98f, 0.70f, 1f),
            Vector2.zero, Vector2.one, new Vector2(34f, 0f), new Vector2(-10f, 0f));
        _safeZoneBanner.SetActive(false);

        // ============================================================
        // 1C. แบนเนอร์แจ้งเตือนการได้รับ Gold / ไอเทม (Toast Notification)
        // ============================================================
        var toastOuter = MakeImage(canvasGo.transform, "HudToastBanner", new Color(1f, 0.84f, 0.25f, 0.95f));
        _toastBorder = toastOuter;
        _toastBanner = toastOuter.gameObject;
        _toastRect = toastOuter.rectTransform;
        _toastRect.anchorMin = _toastRect.anchorMax = _toastRect.pivot = new Vector2(1f, 1f);
        _toastRect.anchoredPosition = new Vector2(-18f, -178f);
        _toastRect.sizeDelta = new Vector2(410f, 34f);

        var toastInner = MakeImage(_toastRect, "Inner", new Color(0.06f, 0.07f, 0.10f, 0.95f));
        FillParentWithPadding(toastInner.rectTransform, 2f);

        _toastText = MakeText(toastInner.transform, "ToastText", "", 13, FontStyle.Bold,
            TextAnchor.MiddleCenter, new Color(1f, 0.90f, 0.35f, 1f),
            Vector2.zero, Vector2.one, new Vector2(8f, 0f), new Vector2(-8f, 0f));
        _toastBanner.SetActive(false);

        // ============================================================
        // 2. แบนเนอร์นับถอยหลังเวลาบอสเกิดใหม่ตรงกลางด้านบน
        // ============================================================
        var bannerOuter = MakeImage(canvasGo.transform, "BossRespawnBanner", new Color(0.85f, 0.22f, 0.18f, 0.92f));
        bannerOuter.raycastTarget = false;
        _bossBanner = bannerOuter.gameObject;
        var brt = bannerOuter.rectTransform;
        brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 1f);
        brt.anchoredPosition = new Vector2(0f, -18f);
        brt.sizeDelta = new Vector2(360f, 44f);

        var bannerInner = MakeImage(brt, "Inner", new Color(0.10f, 0.04f, 0.06f, 0.94f));
        FillParentWithPadding(bannerInner.rectTransform, 2f);

        var skullIcon = MakeImage(bannerInner.transform, "BossIcon", new Color(1f, 0.42f, 0.24f, 1f));
        skullIcon.sprite = HudIcons.GetSprite("boss");
        PlaceTopLeft(skullIcon.rectTransform, 14f, 9f, 22f, 22f);

        _bossBannerText = MakeText(bannerInner.transform, "CountdownText", "Boss respawns in 04:00", 16, FontStyle.Bold,
            TextAnchor.MiddleCenter, new Color(1f, 0.86f, 0.35f, 1f),
            Vector2.zero, Vector2.one, new Vector2(18f, 0f), new Vector2(-18f, 0f));

        _bossBanner.SetActive(false);
    }

    /// <summary>
    /// แสดงข้อความแจ้งเตือนชั่วคราวใต้การ์ดสถานะมุมขวาบน
    /// </summary>
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

        // 1. อัปเดตข้อมูลเลเวล (แสดง MAX เมื่อถึง Lv.30), ชื่อ และรูปโปรไฟล์ Lobby Avatar
        int lvl = Mathf.Clamp(_combat.NetworkLevel, 1, PlayerCombat.MaxLevel);
        bool isMaxLevel = _combat.IsMaxLevel;
        if (_levelBadgeText != null)
        {
            _levelBadgeText.text = isMaxLevel ? $"Lv.{PlayerCombat.MaxLevel} MAX" : $"Lv.{lvl}";
            _levelBadgeText.color = isMaxLevel ? new Color(1.0f, 0.88f, 0.28f, 1f) : UITheme.BrushedGoldBright;
        }
        if (_goldText != null) _goldText.text = $"{_combat.NetworkGold:N0} G";

        if (_playerNameText != null && _capsule != null)
        {
            string nick = _capsule.Nick.Value;
            _playerNameText.text = string.IsNullOrEmpty(nick) ? "Player" : nick;
        }

        // ซิงก์รูปโปรไฟล์ Avatar จาก Lobby/PlayerCapsule และปรับสีกรอบให้ตรงกับสีประจำตัว
        if (_avatarSilhouette != null && _capsule != null)
        {
            int avatarIdx = Mathf.Clamp(_capsule.AvatarIndex, 0, 3);
            if (_displayedAvatarIndex != avatarIdx || _avatarSilhouette.sprite == null)
            {
                _displayedAvatarIndex = avatarIdx;
                Sprite lobbySprite = HudIcons.GetLobbyAvatarSprite(avatarIdx);
                _avatarSilhouette.sprite = lobbySprite;
                _avatarSilhouette.preserveAspect = true;
                _avatarSilhouette.color = Color.white;
            }
            if (_avatarFrameBorder != null)
                _avatarFrameBorder.color = AvatarPalette.Get(avatarIdx);
        }

        // 2. อัปเดตสถานะเขตปลอดภัย (Safe Zone) และสถานะการต่อสู้
        bool inSafeZone = (_combat.IsInSafeZone || SafeZone.IsPlayerInAnySafeZone(_combat)) && !_combat.IsDead;
        bool inCombat = _combat.IsInCombat;

        if (_safeZoneBanner != null && _safeZoneBanner.activeSelf != inSafeZone)
            _safeZoneBanner.SetActive(inSafeZone);

        if (_toastRect != null)
            _toastRect.anchoredPosition = new Vector2(-18f, inSafeZone ? -216f : -178f);

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
            else if (!_combat.IsWeaponDrawn)
            {
                _readinessDot.color = new Color(0.35f, 0.76f, 0.98f, 1f);
                _readinessText.color = new Color(0.58f, 0.88f, 1f, 1f);
                _readinessText.text = "EXPLORATION";
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

        // 3. อัปเดตหลอด HP, Stamina และ EXP
        int maxHp = Mathf.Max(1, _combat.MaxHealth);
        float hpFrac = Mathf.Clamp01(_combat.NetworkHealth / (float)maxHp);
        if (_hpFill != null)
        {
            _hpFill.rectTransform.anchorMax = new Vector2(hpFrac, 1f);
            _hpFill.color = Color.Lerp(new Color(0.86f, 0.15f, 0.18f), new Color(0.20f, 0.84f, 0.36f), hpFrac);
        }
        if (_hpText != null) _hpText.text = $"HP   {_combat.NetworkHealth} / {maxHp}";

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

        // แสดงแถบ EXP เต็มพร้อมข้อความ MAX เมื่อตัวละครถึงเพดานเลเวลสูงสุด (Lv.30) ด้วยสีทองเข้มตัดตัวอักษรขาวให้อ่านชัดเจน
        if (isMaxLevel)
        {
            if (_expFill != null)
            {
                _expFill.rectTransform.anchorMax = new Vector2(1f, 1f);
                _expFill.color = new Color(0.62f, 0.44f, 0.08f, 1f);
            }
            if (_expText != null)
            {
                _expText.color = Color.white;
                _expText.text = $"EXP  MAX  (Lv.{PlayerCombat.MaxLevel})";
            }
        }
        else
        {
            int nextExp = Mathf.Max(1, _combat.ExpToNextLevel);
            float expFrac = Mathf.Clamp01(_combat.NetworkExperience / (float)nextExp);
            if (_expFill != null)
            {
                _expFill.rectTransform.anchorMax = new Vector2(expFrac, 1f);
                _expFill.color = UITheme.BrushedGoldBright;
            }
            if (_expText != null)
            {
                _expText.color = Color.white;
                _expText.text = $"EXP  {_combat.NetworkExperience} / {nextExp}  ({Mathf.RoundToInt(expFrac * 100f)}%)";
            }
        }

        if (_pointsNoticeText != null)
        {
            if (_combat.IsParryCounterReady)
            {
                _pointsNoticeText.text = "★ PARRY COUNTER READY!  Next Slash = Guaranteed CRIT!";
                _pointsNoticeText.color = new Color(1f, 0.90f, 0.28f, 1f);
            }
            else if (_combat.NetworkUnspentStatPoints > 0)
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
                _pointsNoticeText.text = "GUARD ACTIVE [F] (75% Damage Reduction / Parry)";
                _pointsNoticeText.color = new Color(0.32f, 0.88f, 1f, 1f);
            }
            else if (_combat.ActiveBuffDamageBonus > 0)
            {
                _pointsNoticeText.text = $"BLADE BUFF: +{_combat.ActiveBuffDamageBonus} ATK ({_combat.ActiveBuffRemainingSeconds:0}s)";
                _pointsNoticeText.color = new Color(0.35f, 0.95f, 0.72f, 1f);
            }
            else if (!_combat.IsWeaponDrawn)
            {
                _pointsNoticeText.text = "Explore • [Space] Jump | [1] Draw Sword | [M] Map";
                _pointsNoticeText.color = new Color(0.62f, 0.88f, 1f, 0.98f);
            }
            else
            {
                _pointsNoticeText.text = "[Space] Jump | [F] Block | [MMB] Lock | [1] Sheathe";
                _pointsNoticeText.color = new Color(0.76f, 0.80f, 0.88f, 0.95f);
            }
        }

        // 4. อัปเดตแบนเนอร์นับถอยหลังเวลาบอสเกิดใหม่ (รองรับทั้ง SandboxWorldSpawner และ MonsterSpawnZone แบบอิสระ)
        var spawner = SandboxWorldSpawner.Instance;
        bool showBoss = spawner != null && spawner.ShouldShowBossRespawnBanner;
        string bossBannerText = showBoss ? spawner.GetBossRespawnCountdownText() : null;
        if (!showBoss && MonsterSpawnZone.TryGetNearbyRespawningBossBanner(_combat.transform.position, out string zoneBossBanner))
        {
            showBoss = true;
            bossBannerText = zoneBossBanner;
        }
        if (_bossBanner != null)
        {
            if (_bossBanner.activeSelf != showBoss)
                _bossBanner.SetActive(showBoss);

            if (showBoss && _bossBannerText != null && !string.IsNullOrEmpty(bossBannerText))
                _bossBannerText.text = bossBannerText;
        }
    }

    // ------------------------------------------------------------------ ฟังก์ชันช่วยสร้าง uGUI

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
