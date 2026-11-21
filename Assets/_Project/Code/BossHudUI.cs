using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// แผงสถานะบอสกลางด้านบนหน้าจอ (Top-Center Boss HUD) และตารางอันดับดาเมจผู้มีส่วนร่วมด้านล่างขวาของหลอดเลือดบอส:
///   - แสดงอัตโนมัติเมื่อผู้เล่นอยู่ในลานบอส (Boss Arena) หรือกำลังต่อสู้กับบอส และซ่อนทันทีเมื่อออกจากลานหรือหลังบอสตาย
///   - แสดงป้ายชื่อบอสพร้อมไอคอนด้านซ้าย, ตัวเลขเลือด (CurrentHP / MaxHP), แถบ Cast Skill/Telegraph ของบอส,
///     หลอดเลือดสีแดงเข้มแบบแบ่งช่อง (Segmented Crimson Bar) พร้อมแถบเลือดลดตาม (Damage Trail),
///     และตาราง Damage Contribution เรียลไทม์มุมขวาล่างใต้หลอดเลือด (ชื่อผู้เล่น | ดาเมจ | %) ที่ซิงก์ระหว่าง Host และ Client
/// </summary>
[DisallowMultipleComponent]
public class BossHudUI : MonoBehaviour
{
    public static BossHudUI Instance { get; private set; }

    // จำนวนแถวสูงสุดของตารางอันดับผู้ทำดาเมจใส่บอส
    private const int MaxDamageRows = 5;
    private const float BarWidth = 760f;
    private const float BarHeight = 18f;

    // คุณสมบัติสำหรับให้สคริปต์อื่นและการทดสอบอัตโนมัติตรวจสอบสถานะ HUD
    public bool IsVisible => _root != null && _root.activeSelf;
    public EnemyNPC ActiveBossTarget => _currentBoss;
    public string DisplayedBossName => _bossNameText != null ? _bossNameText.text : string.Empty;
    public string DisplayedBossSubtitle => _bossSubtitleText != null ? _bossSubtitleText.text : string.Empty;
    public string DisplayedHpText => _hpValueText != null ? _hpValueText.text : string.Empty;
    public string DisplayedBadgeText => _rightBadgeText != null ? _rightBadgeText.text : string.Empty;
    public int VisibleDamageRowCount { get; private set; }

    private struct DamageRowUI
    {
        public GameObject root;
        public Image shareFill;
        public Image accentBar;
        public Text nameText;
        public Text statsText;
    }

    private PlayerCombat _localPlayer;
    private EnemyNPC _currentBoss;
    private Font _font;

    // องค์ประกอบ uGUI ของ Top-Center Boss HUD
    private GameObject _root;
    private Text _bossSubtitleText;
    private Text _bossNameText;
    private Text _hpValueText;
    private Text _rightBadgeText;
    private Image _hpFill;
    private Image _hpTrailFill;
    private float _displayedTrailFrac = 1f;

    // แถบแสดงท่าโจมตีพิเศษ (Telegraph / Skill Cast Bar) เหนือหลอดเลือดบอส
    private GameObject _castRoot;
    private Image _castFill;
    private Text _castLabelText;

    // ตารางอันดับผู้ทำดาเมจด้านขวาล่างใต้หลอดเลือดบอส
    private GameObject _damagePanel;
    private readonly DamageRowUI[] _damageRows = new DamageRowUI[MaxDamageRows];

    /// <summary>
    /// สร้างหรือคืนค่าอินสแตนซ์ของ BossHudUI สำหรับผู้เล่น Local
    /// </summary>
    public static BossHudUI EnsureFor(PlayerCombat combat)
    {
        if (combat == null || !combat.IsLocallyControlled) return null;
        if (Instance != null) return Instance;
        return combat.gameObject.AddComponent<BossHudUI>();
    }

    /// <summary>
    /// ตรวจสอบว่า BossHudUI กำลังแสดงผลให้กับบอสตัวที่ระบุอยู่หรือไม่ (ใช้ปิด OnGUI หลอดเลือดบนหัวบอสไม่ให้ซ้อนทับ)
    /// </summary>
    public static bool IsDisplayingBoss(EnemyNPC boss)
    {
        return Instance != null && Instance.IsVisible && Instance._currentBoss == boss;
    }

    /// <summary>
    /// คืนข้อความแถวอันดับดาเมจที่ระบุ (0..MaxDamageRows-1) เพื่อใช้ตรวจสอบใน Play Mode Verification
    /// </summary>
    public string GetDamageRowSummary(int index)
    {
        if (index < 0 || index >= _damageRows.Length || _damageRows[index].root == null || !_damageRows[index].root.activeSelf)
            return string.Empty;
        return $"{_damageRows[index].nameText.text}: {_damageRows[index].statsText.text}";
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
        _localPlayer = GetComponent<PlayerCombat>();
        _font = UITheme.GetFont();
        BuildUI();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// สร้างโครงสร้าง uGUI ของ Top-Center Boss Health Bar และตาราง Damage Meter ให้ตรงกับภาพอ้างอิง
    /// </summary>
    private void BuildUI()
    {
        var canvasGo = new GameObject("BossHudCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 36;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // กรอบหลักยึดกลางด้านบนหน้าจอ (Top-Center)
        var rootGo = new GameObject("BossHudRoot", typeof(RectTransform));
        rootGo.transform.SetParent(canvasGo.transform, false);
        _root = rootGo;
        var rootRt = (RectTransform)rootGo.transform;
        rootRt.anchorMin = rootRt.anchorMax = rootRt.pivot = new Vector2(0.5f, 1f);
        rootRt.anchoredPosition = new Vector2(0f, -14f);
        rootRt.sizeDelta = new Vector2(BarWidth, 260f);

        // ============================================================
        // 1. ป้ายหัวเรื่องบอสตรงกลางด้านบน (Boss Portrait Badge + Subtitle + Name)
        // ============================================================
        var headerBorder = MakeImage(rootRt, "BossHeaderBorder", new Color(0.76f, 0.58f, 0.26f, 0.92f));
        UITheme.ApplySlicedStyle(headerBorder, new Color(0.76f, 0.58f, 0.26f, 0.92f));
        var hbrt = headerBorder.rectTransform;
        hbrt.anchorMin = hbrt.anchorMax = hbrt.pivot = new Vector2(0.5f, 1f);
        hbrt.anchoredPosition = new Vector2(0f, 0f);
        hbrt.sizeDelta = new Vector2(360f, 56f);

        var headerInner = MakeImage(hbrt, "BossHeaderBg", new Color(0.055f, 0.062f, 0.090f, 0.92f));
        UITheme.ApplySlicedStyle(headerInner, new Color(0.055f, 0.062f, 0.090f, 0.92f));
        FillWithPadding(headerInner.rectTransform, 2f);

        // วงกลมไอคอนบอสด้านซ้ายของป้ายหัวเรื่อง
        var badgeRing = MakeImage(headerInner.transform, "BossBadgeRing", new Color(0.86f, 0.24f, 0.18f, 1f));
        badgeRing.sprite = HudIcons.GetSprite("circle");
        PlaceTopLeft(badgeRing.rectTransform, 12f, 6f, 40f, 40f);

        var badgeInner = MakeImage(badgeRing.transform, "BossBadgeInner", new Color(0.10f, 0.05f, 0.07f, 1f));
        badgeInner.sprite = HudIcons.GetSprite("circle");
        FillWithPadding(badgeInner.rectTransform, 3f);

        var badgeIcon = MakeImage(badgeInner.transform, "BossIcon", new Color(1f, 0.84f, 0.34f, 1f));
        badgeIcon.sprite = HudIcons.GetSprite("boss");
        badgeIcon.preserveAspect = true;
        FillWithPadding(badgeIcon.rectTransform, 5f);

        // คำโปรยด้านบน (เช่น "Lv.5 World Boss  •  2x Co-op")
        _bossSubtitleText = MakeText(headerInner.transform, "BossSubtitle", "Lv.5 World Boss", 12, FontStyle.Bold,
            TextAnchor.MiddleLeft, new Color(0.95f, 0.78f, 0.42f, 1f));
        PlaceTopLeft(_bossSubtitleText.rectTransform, 62f, 4f, 286f, 18f);
        _bossSubtitleText.horizontalOverflow = HorizontalWrapMode.Overflow;
        UITheme.AddShadow(_bossSubtitleText.gameObject, 1.2f);

        // ชื่อบอสหลักขนาดใหญ่ คมชัด ไม่ตัดคำ
        _bossNameText = MakeText(headerInner.transform, "BossName", "Sovereign Golem", 21, FontStyle.Bold,
            TextAnchor.MiddleLeft, UITheme.TextPrimary);
        PlaceTopLeft(_bossNameText.rectTransform, 62f, 22f, 286f, 28f);
        _bossNameText.horizontalOverflow = HorizontalWrapMode.Overflow;
        UITheme.AddShadow(_bossNameText.gameObject, 1.5f);

        // ============================================================
        // 2. แถวข้อมูลเหนือหลอดเลือด: [ซ้าย: CurrentHP / MaxHP] [กลาง: Cast Bar] [ขวา: Boss Chest / Co-op Badge]
        // ============================================================
        var hpPill = MakeImage(rootRt, "HpReadoutPill", new Color(0.05f, 0.055f, 0.08f, 0.78f));
        UITheme.ApplySlicedStyle(hpPill, new Color(0.05f, 0.055f, 0.08f, 0.78f));
        PlaceTopLeft(hpPill.rectTransform, 0f, 62f, 196f, 24f);

        _hpValueText = MakeText(hpPill.transform, "HpValueText", "1,630 / 1,630", 14, FontStyle.Bold,
            TextAnchor.MiddleCenter, UITheme.TextPrimary);
        FillWithPadding(_hpValueText.rectTransform, 4f);
        _hpValueText.horizontalOverflow = HorizontalWrapMode.Overflow;
        UITheme.AddShadow(_hpValueText.gameObject, 1.2f);

        // แถบ Cast Skill / Telegraph ตรงกลางเหนือหลอดเลือดบอส (แสดงเฉพาะตอนบอสกำลังชาร์จท่า)
        var castBg = MakeImage(rootRt, "BossCastBarBg", new Color(0.08f, 0.03f, 0.04f, 0.88f));
        UITheme.ApplySlicedStyle(castBg, new Color(0.08f, 0.03f, 0.04f, 0.88f));
        _castRoot = castBg.gameObject;
        var castRt = castBg.rectTransform;
        castRt.anchorMin = castRt.anchorMax = castRt.pivot = new Vector2(0.5f, 1f);
        castRt.anchoredPosition = new Vector2(0f, -64f);
        castRt.sizeDelta = new Vector2(270f, 20f);

        _castFill = MakeImage(castRt, "CastFill", new Color(0.95f, 0.28f, 0.16f, 0.95f));
        _castFill.sprite = UITheme.GetFillSprite();
        _castFill.rectTransform.anchorMin = Vector2.zero;
        _castFill.rectTransform.anchorMax = new Vector2(1f, 1f);
        _castFill.rectTransform.offsetMin = new Vector2(2f, 2f);
        _castFill.rectTransform.offsetMax = new Vector2(-2f, -2f);

        _castLabelText = MakeText(castRt, "CastLabel", "Heavy Cleave  0.5s", 12, FontStyle.Bold,
            TextAnchor.MiddleCenter, Color.white);
        FillWithPadding(_castLabelText.rectTransform, 2f);
        UITheme.AddShadow(_castLabelText.gameObject, 1.2f);
        _castRoot.SetActive(false);

        // ป้ายขวาบนเหนือหลอดเลือด (เช่น "Boss Reward Chest" หรือ "Co-op 2x • Boss Chest")
        var rightBadge = MakeImage(rootRt, "BossRightBadge", new Color(0.06f, 0.065f, 0.095f, 0.86f));
        UITheme.ApplySlicedStyle(rightBadge, new Color(0.06f, 0.065f, 0.095f, 0.86f));
        var rbrt = rightBadge.rectTransform;
        rbrt.anchorMin = rbrt.anchorMax = rbrt.pivot = new Vector2(1f, 1f);
        rbrt.anchoredPosition = new Vector2(0f, -62f);
        rbrt.sizeDelta = new Vector2(215f, 24f);

        var chestIcon = MakeImage(rbrt, "ChestIcon", new Color(1f, 0.84f, 0.30f, 1f));
        chestIcon.sprite = HudIcons.GetSprite("chest");
        chestIcon.preserveAspect = true;
        PlaceTopRight(chestIcon.rectTransform, 8f, 4f, 16f, 16f);

        _rightBadgeText = MakeText(rbrt, "RightBadgeText", "Boss Reward Chest", 12, FontStyle.Bold,
            TextAnchor.MiddleRight, new Color(0.96f, 0.90f, 0.68f, 1f));
        _rightBadgeText.rectTransform.anchorMin = Vector2.zero;
        _rightBadgeText.rectTransform.anchorMax = Vector2.one;
        _rightBadgeText.rectTransform.offsetMin = new Vector2(8f, 0f);
        _rightBadgeText.rectTransform.offsetMax = new Vector2(-30f, 0f);
        _rightBadgeText.horizontalOverflow = HorizontalWrapMode.Overflow;
        UITheme.AddShadow(_rightBadgeText.gameObject, 1.0f);

        // ============================================================
        // 3. หลอดเลือดบอสสีแดงเข้มแบบกว้าง (Wide Segmented Crimson Boss HP Bar)
        // ============================================================
        var barBorder = MakeImage(rootRt, "BossHpBarBorder", new Color(0.18f, 0.06f, 0.07f, 0.98f));
        UITheme.ApplySlicedStyle(barBorder, new Color(0.18f, 0.06f, 0.07f, 0.98f));
        PlaceTopLeft(barBorder.rectTransform, 0f, 90f, BarWidth, BarHeight);

        var barTrack = MakeImage(barBorder.transform, "BossHpTrack", new Color(0.10f, 0.025f, 0.035f, 0.96f));
        FillWithPadding(barTrack.rectTransform, 2f);

        // แถบเลือดลดตาม (Damage Trail สีส้มทอง) เพื่อความรู้สึกทรงพลังตอนทำดาเมจหนัก
        _hpTrailFill = MakeImage(barTrack.transform, "HpTrailFill", new Color(0.98f, 0.62f, 0.22f, 0.92f));
        _hpTrailFill.sprite = UITheme.GetFillSprite();
        _hpTrailFill.rectTransform.anchorMin = Vector2.zero;
        _hpTrailFill.rectTransform.anchorMax = Vector2.one;
        _hpTrailFill.rectTransform.offsetMin = Vector2.zero;
        _hpTrailFill.rectTransform.offsetMax = Vector2.zero;

        // แถบเลือดบอสหลักสีแดงเข้ม (Crimson)
        _hpFill = MakeImage(barTrack.transform, "HpFill", new Color(0.84f, 0.10f, 0.14f, 1f));
        _hpFill.sprite = UITheme.GetFillSprite();
        _hpFill.rectTransform.anchorMin = Vector2.zero;
        _hpFill.rectTransform.anchorMax = Vector2.one;
        _hpFill.rectTransform.offsetMin = Vector2.zero;
        _hpFill.rectTransform.offsetMax = Vector2.zero;

        // เส้นแบ่งหลอดเลือด 5 ส่วน (4 ขีด) ให้สัดส่วนเหมือนภาพอ้างอิงและอ่าน % เลือดได้ง่าย
        const int segments = 5;
        for (int s = 1; s < segments; s++)
        {
            float anchorX = s / (float)segments;
            var tick = MakeImage(barTrack.transform, $"SegTick_{s}", new Color(0.05f, 0.02f, 0.03f, 0.72f));
            var trt = tick.rectTransform;
            trt.anchorMin = new Vector2(anchorX, 0f);
            trt.anchorMax = new Vector2(anchorX, 1f);
            trt.pivot = new Vector2(0.5f, 0.5f);
            trt.anchoredPosition = Vector2.zero;
            trt.sizeDelta = new Vector2(2f, 0f);
        }

        // ============================================================
        // 4. ตารางอันดับดาเมจผู้มีส่วนร่วม (Bottom-Right ใต้ปลายขวาของหลอดเลือดบอส)
        // ============================================================
        var dmgPanelGo = new GameObject("DamageContributionPanel", typeof(RectTransform));
        dmgPanelGo.transform.SetParent(rootRt, false);
        _damagePanel = dmgPanelGo;
        var dprt = (RectTransform)dmgPanelGo.transform;
        dprt.anchorMin = dprt.anchorMax = dprt.pivot = new Vector2(1f, 1f);
        dprt.anchoredPosition = new Vector2(0f, -114f);
        dprt.sizeDelta = new Vector2(280f, MaxDamageRows * 28f);

        for (int i = 0; i < MaxDamageRows; i++)
        {
            var rowBg = MakeImage(dprt, $"DamageRow_{i}", new Color(0.05f, 0.06f, 0.09f, 0.80f));
            UITheme.ApplySlicedStyle(rowBg, new Color(0.05f, 0.06f, 0.09f, 0.80f));
            var rrt = rowBg.rectTransform;
            rrt.anchorMin = rrt.anchorMax = rrt.pivot = new Vector2(1f, 1f);
            rrt.anchoredPosition = new Vector2(0f, -i * 28f);
            rrt.sizeDelta = new Vector2(280f, 24f);

            // แถบสีพื้นหลังบอกสัดส่วน % ดาเมจแบบโปร่งแสง
            var shareFill = MakeImage(rrt, "ShareFill", new Color(0.86f, 0.22f, 0.18f, 0.28f));
            shareFill.rectTransform.anchorMin = Vector2.zero;
            shareFill.rectTransform.anchorMax = new Vector2(0f, 1f);
            shareFill.rectTransform.offsetMin = new Vector2(2f, 2f);
            shareFill.rectTransform.offsetMax = new Vector2(-2f, -2f);

            // เส้นสีนำสายตาด้านซ้ายของแถว (สีทองสำหรับ Top DPS, สีฟ้าสำหรับ Local, สีเทาเงินสำหรับเพื่อนร่วมทีม)
            var accent = MakeImage(rrt, "Accent", UITheme.BrushedGoldBright);
            PlaceTopLeft(accent.rectTransform, 0f, 0f, 4f, 24f);

            // ชื่อผู้เล่นด้านซ้าย
            var nameTxt = MakeText(rrt, "PlayerName", "", 13, FontStyle.Bold, TextAnchor.MiddleLeft, UITheme.TextPrimary);
            nameTxt.rectTransform.anchorMin = Vector2.zero;
            nameTxt.rectTransform.anchorMax = Vector2.one;
            nameTxt.rectTransform.offsetMin = new Vector2(10f, 0f);
            nameTxt.rectTransform.offsetMax = new Vector2(-122f, 0f);
            UITheme.AddShadow(nameTxt.gameObject, 1.0f);

            // ดาเมจและเปอร์เซ็นต์ด้านขวา (เช่น "1,420 (68%)")
            var statTxt = MakeText(rrt, "DamageStats", "", 13, FontStyle.Bold, TextAnchor.MiddleRight, new Color(1f, 0.92f, 0.65f, 1f));
            statTxt.rectTransform.anchorMin = Vector2.zero;
            statTxt.rectTransform.anchorMax = Vector2.one;
            statTxt.rectTransform.offsetMin = new Vector2(126f, 0f);
            statTxt.rectTransform.offsetMax = new Vector2(-10f, 0f);
            statTxt.horizontalOverflow = HorizontalWrapMode.Overflow;
            UITheme.AddShadow(statTxt.gameObject, 1.0f);

            rowBg.gameObject.SetActive(false);
            _damageRows[i] = new DamageRowUI
            {
                root = rowBg.gameObject,
                shareFill = shareFill,
                accentBar = accent,
                nameText = nameTxt,
                statsText = statTxt
            };
        }

        _root.SetActive(false);
    }

    private void Update()
    {
        if (_localPlayer == null || !_localPlayer.IsSpawned)
        {
            SetHudVisible(false);
            return;
        }

        // ซ่อน Boss HUD ชั่วคราวเมื่อเปิดหน้าต่าง Modal (Inventory / World Map) เพื่อไม่ให้ซ้อนทับ UI
        if (GameplayInput.IsBlocked || _localPlayer.IsDead)
        {
            SetHudVisible(false);
            return;
        }

        EnemyNPC boss = ResolveVisibleBoss();
        if (boss == null)
        {
            _currentBoss = null;
            SetHudVisible(false);
            return;
        }

        if (_currentBoss != boss)
        {
            _currentBoss = boss;
            int maxInit = Mathf.Max(1, boss.ScaledMaxHealth);
            _displayedTrailFrac = Mathf.Clamp01(boss.CurrentHealth / (float)maxInit);
        }

        SetHudVisible(true);
        RefreshBossHeaderAndHp(boss);
        RefreshBossCastBar(boss);
        RefreshDamageContributionList(boss);
    }

    /// <summary>
    /// ค้นหาบอสที่ยังมีชีวิตอยู่และตรวจสอบว่าผู้เล่นอยู่ในลานบอสหรือกำลังปะทะกับบอสหรือไม่
    /// </summary>
    private EnemyNPC ResolveVisibleBoss()
    {
        EnemyNPC boss = SandboxWorldSpawner.Instance != null ? SandboxWorldSpawner.Instance.ActiveBoss : null;
        if (boss == null || !boss.IsSpawned || !boss.IsAlive)
        {
            boss = null;
            for (int i = 0; i < EnemyNPC.All.Count; i++)
            {
                var e = EnemyNPC.All[i];
                if (e != null && e.IsSpawned && e.IsBoss && e.IsAlive)
                {
                    boss = e;
                    break;
                }
            }
        }

        if (boss == null) return null;

        bool inArenaZone = SandboxWorldSpawner.Instance != null && SandboxWorldSpawner.Instance.IsLocalPlayerInBossArena;
        if (inArenaZone) return boss;

        // แสดงเช่นกันหากผู้เล่นอยู่ห่างจากตัวบอสไม่เกิน 26 เมตร (เผื่อบอสเดินไล่ใกล้ขอบลาน)
        Vector3 a = _localPlayer.transform.position;
        Vector3 b = boss.transform.position;
        a.y = 0f;
        b.y = 0f;
        if (Vector3.Distance(a, b) <= 26f) return boss;

        return null;
    }

    private void RefreshBossHeaderAndHp(EnemyNPC boss)
    {
        int curHp = Mathf.Max(0, boss.CurrentHealth);
        int maxHp = Mathf.Max(1, boss.ScaledMaxHealth);
        float hpFrac = Mathf.Clamp01(curHp / (float)maxHp);

        if (_displayedTrailFrac > hpFrac)
            _displayedTrailFrac = Mathf.MoveTowards(_displayedTrailFrac, hpFrac, Time.deltaTime * 0.45f);
        else
            _displayedTrailFrac = hpFrac;

        int coopMult = Mathf.Max(1, boss.CoopPeakPlayerCount);
        string staggerSuffix = boss.IsStaggered ? "  •  STAGGERED!" : string.Empty;
        string coopSuffix = coopMult > 1 ? $"  •  Co-op {coopMult}x" : string.Empty;
        string regenSuffix = boss.IsBossRegenerating
            ? "  •  REGEN +25%"
            : (boss.BossResetCooldownRemaining > 0.05f ? $"  •  Reset {boss.BossResetCooldownRemaining:0}s" : string.Empty);

        _bossSubtitleText.text = $"Lv.{boss.ScaledLevel} World Boss{coopSuffix}{staggerSuffix}{regenSuffix}";
        _bossSubtitleText.color = boss.IsStaggered
            ? new Color(1f, 0.90f, 0.25f, 1f)
            : (boss.IsBossRegenerating
                ? new Color(0.42f, 0.96f, 0.62f, 1f)
                : (coopMult > 1 ? new Color(1f, 0.58f, 0.30f, 1f) : new Color(0.95f, 0.78f, 0.42f, 1f)));

        _bossNameText.text = "Sovereign Golem";
        _hpValueText.text = $"{curHp:N0} / {maxHp:N0}";
        _rightBadgeText.text = boss.IsBossRegenerating
            ? "Regenerating (+25%/3s)"
            : (coopMult > 1 ? $"Co-op {coopMult}x  •  Boss Chest" : "Boss Reward Chest");

        _hpFill.rectTransform.anchorMax = new Vector2(hpFrac, 1f);
        _hpTrailFill.rectTransform.anchorMax = new Vector2(_displayedTrailFrac, 1f);
    }

    private void RefreshBossCastBar(EnemyNPC boss)
    {
        float remain = boss.ActiveTelegraphEndTime - Time.time;
        bool showCast = remain > 0.01f && boss.ActiveTelegraphDuration > 0.01f && !string.IsNullOrEmpty(boss.ActiveTelegraphName);
        if (_castRoot.activeSelf != showCast) _castRoot.SetActive(showCast);
        if (!showCast) return;

        float frac = Mathf.Clamp01(1f - (remain / boss.ActiveTelegraphDuration));
        _castFill.rectTransform.anchorMax = new Vector2(frac, 1f);
        _castLabelText.text = $"{boss.ActiveTelegraphName}   {remain:0.0}s";
    }

    /// <summary>
    /// อัปเดตรายชื่อและสัดส่วนดาเมจผู้มีส่วนร่วมแบบเรียลไทม์ใต้ปลายขวาของหลอดเลือดบอส
    /// </summary>
    private void RefreshDamageContributionList(EnemyNPC boss)
    {
        IReadOnlyList<EnemyNPC.BossDamageEntry> entries = boss.SyncedBossDamageBoard;
        int count = entries != null ? Mathf.Min(MaxDamageRows, entries.Count) : 0;
        VisibleDamageRowCount = count;

        if (_damagePanel != null && _damagePanel.activeSelf != (count > 0))
            _damagePanel.SetActive(count > 0);

        for (int i = 0; i < MaxDamageRows; i++)
        {
            var row = _damageRows[i];
            if (i < count)
            {
                EnemyNPC.BossDamageEntry entry = entries[i];
                row.root.SetActive(true);
                int pct = Mathf.RoundToInt(entry.damageShare * 100f);

                row.nameText.text = entry.playerName;
                row.statsText.text = $"{entry.damageDealt:N0} ({pct}%)";
                row.shareFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(entry.damageShare), 1f);

                bool isTop = i == 0;
                Color accent = isTop
                    ? UITheme.BrushedGoldBright
                    : (entry.isLocalPlayer ? UITheme.JadeAccent : new Color(0.60f, 0.68f, 0.80f, 0.9f));
                row.accentBar.color = accent;
                row.nameText.color = entry.isLocalPlayer ? new Color(0.45f, 0.98f, 0.76f, 1f) : (isTop ? UITheme.BrushedGoldBright : UITheme.TextPrimary);
                row.shareFill.color = isTop
                    ? new Color(0.95f, 0.68f, 0.16f, 0.28f)
                    : (entry.isLocalPlayer ? new Color(0.18f, 0.84f, 0.64f, 0.26f) : new Color(0.86f, 0.22f, 0.18f, 0.24f));
            }
            else if (row.root.activeSelf)
            {
                row.root.SetActive(false);
            }
        }
    }

    private void SetHudVisible(bool visible)
    {
        if (_root != null && _root.activeSelf != visible)
            _root.SetActive(visible);
        if (!visible) VisibleDamageRowCount = 0;
    }

    // ------------------------------------------------------------------ ฟังก์ชันช่วยสร้าง uGUI

    private static Image MakeImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private Text MakeText(Transform parent, string name, string content, int fontSize, FontStyle style, TextAnchor align, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.transform.SetParent(parent, false);
        var txt = go.GetComponent<Text>();
        txt.font = _font;
        txt.text = content;
        txt.fontSize = fontSize;
        txt.fontStyle = style;
        txt.alignment = align;
        txt.color = color;
        txt.raycastTarget = false;
        return txt;
    }

    private static void FillWithPadding(RectTransform rt, float pad)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(pad, pad);
        rt.offsetMax = new Vector2(-pad, -pad);
    }

    private static void PlaceTopLeft(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
    }

    private static void PlaceTopRight(RectTransform rt, float xFromRight, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-xFromRight, -y);
        rt.sizeDelta = new Vector2(w, h);
    }
}
