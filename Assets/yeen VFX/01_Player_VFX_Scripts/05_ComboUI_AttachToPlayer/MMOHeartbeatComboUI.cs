using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

/// <summary>
/// AAA Heartbeat Double-Pulse Combo Counter & Dynamic Frame UI (Unity 6)
/// - Zero-Setup Procedural UI Builder: สร้าง Canvas, กรอบเอียง -4 องศา, เส้นเรืองแสง, วงแสง Pulse Glow Ring,
///   ตัวเลขคอมโบ, ป้าย HITS, ป้ายแรงก์ (C -> SSS), และหลอดเวลา Filled Timer Bar อัตโนมัติตอน Runtime
/// - Heartbeat Double-Pulse Animation ("ตึก-ตัก"): กระแทก 2 จังหวะต่อเนื่องทุกครั้งที่ทำคอมโบเพิ่ม
///   พร้อม Idle Heartbeat ที่เต้นถี่ขึ้นเรื่อยๆ เมื่อหลอดเวลาใกล้หมด
/// - เรียกใช้งานจากระบบต่อสู้ได้ทันทีผ่าน: MMOHeartbeatComboUI.RegisterHit();
/// </summary>
[AddComponentMenu("yeen VFX/08. [Player & UI] กรอบคอมโบหัวใจเต้น ตึก-ตัก (Heartbeat Combo UI)")]
[DisallowMultipleComponent]
public class MMOHeartbeatComboUI : MonoBehaviour
{
    public static MMOHeartbeatComboUI Instance { get; private set; }

    [Header("=== Combo Timing Settings ===")]
    [Tooltip("ระยะเวลาก่อนคอมโบจะหมดอายุ (วินาที)")]
    [Min(0.6f)]
    public float comboExpireTime = 3.0f;

    [Tooltip("ช่วงเวลาจางหายอย่างนุ่มนวลก่อนรีเซ็ตคอมโบ (วินาทีสุดท้าย)")]
    [Min(0.1f)]
    public float fadeOutDuration = 0.5f;

    [Header("=== Heartbeat Double-Pulse Settings (ตึก-ตัก) ===")]
    [Tooltip("ขนาดขยายสูงสุดในจังหวะที่ 1 (ตึก!)")]
    public float beat1PeakScale = 1.42f;
    [Tooltip("ระยะเวลาพุ่งขยายจังหวะที่ 1 (วินาที)")]
    public float beat1UpDuration = 0.07f;
    [Tooltip("ขนาดยุบตัวหลังจังหวะที่ 1")]
    public float beat1DipScale = 0.96f;
    [Tooltip("ระยะเวลายุบตัวจังหวะที่ 1 (วินาที)")]
    public float beat1DownDuration = 0.08f;

    [Tooltip("ขนาดกระแทกสะท้อนจังหวะที่ 2 (ตัก!)")]
    public float beat2PeakScale = 1.18f;
    [Tooltip("ระยะเวลาพุ่งสะท้อนจังหวะที่ 2 (วินาที)")]
    public float beat2UpDuration = 0.06f;
    [Tooltip("ระยะเวลาคลายตัวกลับสู่ขนาดปกติ 1.0x (วินาที)")]
    public float beat2SettleDuration = 0.14f;

    [Header("=== Screen Layout (Reference 1920x1080) ===")]
    [Tooltip("ตำแหน่งขอบมุมขวาบนของกรอบคอมโบ (หน่วยพิกเซลบน Canvas 1920x1080)")]
    public Vector2 topRightOffset = new Vector2(-72f, -86f);
    [Tooltip("องศาเอียงโฉบเฉี่ยวของกรอบคอมโบ (ค่ามาตรฐาน -4 องศา)")]
    public float frameTiltAngle = -4f;
    [Tooltip("ลำดับการเรนเดอร์ของ Canvas Overlay")]
    public int canvasSortingOrder = 120;

    [Header("=== Testing & Combat Integration ===")]
    [Tooltip("เปิดไว้เพื่อทดสอบกดคลิกซ้ายหรือปุ่ม J รัวๆ ได้ทันที (ปิดเป็น false เมื่อเชื่อมเข้ากับระบบฟันศัตรูจริง)")]
    public bool countOnAttackInputForTest = true;

    [Header("=== Live Runtime Status (Read-Only) ===")]
    [SerializeField] private int currentCombo = 0;
    [SerializeField] private float remainingComboTime = 0f;
    [SerializeField] private string currentRankLabel = "C  CHAIN";

    // Runtime UI References (สร้างอัตโนมัติใน Awake)
    private Canvas _canvas;
    private RectTransform _frameRootRect;
    private CanvasGroup _frameCanvasGroup;
    private Image _backgroundImage;
    private Image _leftAccentBar;
    private Image _topBorderLine;
    private Image _bottomBorderLine;
    private Image _pulseGlowRingImage;
    private RectTransform _pulseGlowRingRect;
    private Image _rankPillBackground;
    private Image _timerBarTrack;
    private Image _timerBarFill;
    private Text _comboNumberText;
    private Text _hitsLabelText;
    private Text _rankBadgeText;

    // Runtime Procedural Textures & Sprites
    private Texture2D _whiteTexture2x2;
    private Sprite _whiteSprite;
    private Texture2D _glowRingTexture;
    private Sprite _glowRingSprite;

    // Animation & State Fields
    private Coroutine _heartbeatCoroutine;
    private bool _isPlayingHitPulse = false;
    private float _idleBeatPhase = 0f;
    private Color _activeRankColor = new Color(0.15f, 0.88f, 1.00f, 1f);

    public int CurrentCombo => currentCombo;
    public float RemainingComboTime => remainingComboTime;
    public string CurrentRankLabel => currentRankLabel;

    // ================================================================================================
    // STATIC PUBLIC API สำหรับให้ระบบอาวุธหรือระบบรับดาเมจศัตรูเรียกใช้งานได้ทันทีจากทุกที่
    // ================================================================================================
    /// <summary>
    /// เรียกเพิ่มจำนวนคอมโบจากสคริปต์โจมตีหรือสคริปต์ศัตรู เช่น MMOHeartbeatComboUI.RegisterHit();
    /// </summary>
    public static void RegisterHit(int hitCount = 1)
    {
        if (hitCount <= 0) return;

        if (Instance == null)
        {
            Instance = FindFirstObjectByType<MMOHeartbeatComboUI>();
        }

        if (Instance != null)
        {
            Instance.AddComboHit(hitCount);
        }
    }

    /// <summary>
    /// รีเซ็ตคอมโบกลับเป็น 0 และซ่อนกรอบทันที
    /// </summary>
    public static void ResetCombo()
    {
        if (Instance != null)
        {
            Instance.ClearComboImmediate();
        }
    }

    // ================================================================================================
    // UNITY LIFECYCLE
    // ================================================================================================
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }

        BuildProceduralSprites();
        BuildProceduralComboUI();
        ClearComboImmediate();
    }

    private void OnEnable()
    {
        if (Instance == null)
        {
            Instance = this;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        if (_canvas != null)
        {
            Destroy(_canvas.gameObject);
        }

        if (_whiteSprite != null) Destroy(_whiteSprite);
        if (_whiteTexture2x2 != null) Destroy(_whiteTexture2x2);
        if (_glowRingSprite != null) Destroy(_glowRingSprite);
        if (_glowRingTexture != null) Destroy(_glowRingTexture);
    }

    private void Update()
    {
        // โหมดทดสอบ: กดคลิกซ้ายเมาส์ หรือ ปุ่ม J เพื่อเพิ่มคอมโบได้ทันที (รองรับ New Input System + Legacy Input)
        if (countOnAttackInputForTest && ReadUniversalTestAttackInput())
        {
            AddComboHit(1);
        }

        if (currentCombo <= 0) return;

        // นับเวลาถอยหลังหลอดคอมโบ
        remainingComboTime -= Time.deltaTime;

        if (remainingComboTime <= 0f)
        {
            ClearComboImmediate();
            return;
        }

        // อัปเดตหลอด Filled Timer Bar
        float normalizedTime = Mathf.Clamp01(remainingComboTime / Mathf.Max(0.01f, comboExpireTime));
        if (_timerBarFill != null)
        {
            _timerBarFill.fillAmount = normalizedTime;
        }

        // ปรับ Alpha จางหายอย่างนุ่มนวลในช่วง 0.5 วินาทีสุดท้าย (fadeOutDuration)
        if (_frameCanvasGroup != null)
        {
            float effectiveFadeWindow = Mathf.Min(fadeOutDuration, comboExpireTime);
            if (remainingComboTime <= effectiveFadeWindow)
            {
                float fadeProgress = Mathf.Clamp01(remainingComboTime / Mathf.Max(0.001f, effectiveFadeWindow));
                _frameCanvasGroup.alpha = fadeProgress * fadeProgress * (3f - 2f * fadeProgress); // SmoothStep
            }
            else
            {
                _frameCanvasGroup.alpha = 1f;
            }
        }

        // Idle Heartbeat: เต้นชีพจรเบาๆ ระหว่างรอต่อคอมโบ และเต้นเร็วขึ้นเรื่อยๆ เมื่อหลอดเวลาใกล้หมด
        if (!_isPlayingHitPulse && _frameRootRect != null)
        {
            float urgency = 1f - normalizedTime; // 0 = เพิ่งตี, 1 = เวลาใกล้หมด
            float beatsPerSecond = Mathf.Lerp(1.35f, 4.65f, urgency * urgency * 0.65f + urgency * 0.35f);
            _idleBeatPhase = (_idleBeatPhase + Time.unscaledDeltaTime * beatsPerSecond) % 1f;

            // รูปคลื่นชีพจรหัวใจ 2 จังหวะขนาดเล็ก (ตึก-ตัก)
            float lubPeak = Mathf.Exp(-Mathf.Pow((_idleBeatPhase - 0.14f) / 0.055f, 2f));
            float dubPeak = Mathf.Exp(-Mathf.Pow((_idleBeatPhase - 0.32f) / 0.050f, 2f)) * 0.55f;
            float idlePulse = Mathf.Clamp01(lubPeak + dubPeak);

            float idleAmplitude = Mathf.Lerp(0.032f, 0.082f, urgency);
            float scale = 1.0f + idlePulse * idleAmplitude;
            _frameRootRect.localScale = new Vector3(scale, scale, 1f);

            // สะท้อนจังหวะชีพจรเบาๆ ที่วงแสงรอบกรอบเมื่อเวลาใกล้หมด
            if (_pulseGlowRingImage != null && _pulseGlowRingRect != null)
            {
                float ringAlpha = idlePulse * Mathf.Lerp(0.10f, 0.32f, urgency);
                float ringScale = 1.0f + idlePulse * Mathf.Lerp(0.04f, 0.11f, urgency);
                _pulseGlowRingRect.localScale = new Vector3(ringScale, ringScale, 1f);
                _pulseGlowRingImage.color = new Color(_activeRankColor.r, _activeRankColor.g, _activeRankColor.b, ringAlpha);
            }
        }
    }

    // ================================================================================================
    // PUBLIC CORE LOGIC: เพิ่มคอมโบ เปลี่ยนสี/แรงก์ และจุดระเบิดแอนิเมชัน "ตึก-ตัก"
    // ================================================================================================
    public void AddComboHit(int hitCount = 1)
    {
        if (hitCount <= 0) return;

        if (_frameRootRect == null)
        {
            BuildProceduralSprites();
            BuildProceduralComboUI();
        }

        currentCombo += hitCount;
        remainingComboTime = comboExpireTime;
        _idleBeatPhase = 0f;

        if (_frameCanvasGroup != null)
        {
            _frameCanvasGroup.alpha = 1f;
        }

        if (_timerBarFill != null)
        {
            _timerBarFill.fillAmount = 1f;
        }

        // อัปเดตสีกรอบ เส้นขอบ หลอดเวลา ตัวเลข และป้ายแรงก์ตามจำนวนคอมโบ
        ApplyRankStyleForCombo(currentCombo);

        if (_comboNumberText != null)
        {
            _comboNumberText.text = currentCombo.ToString();
        }

        // รีสตาร์ท Coroutine จังหวะหัวใจเต้น "ตึก-ตัก" ทันทีเพื่อให้ตอบสนองคมกริบทุกเฟรมที่ตีโดน
        if (_heartbeatCoroutine != null)
        {
            StopCoroutine(_heartbeatCoroutine);
        }
        _heartbeatCoroutine = StartCoroutine(HeartbeatDoublePulseRoutine());
    }

    public void ClearComboImmediate()
    {
        if (_heartbeatCoroutine != null)
        {
            StopCoroutine(_heartbeatCoroutine);
            _heartbeatCoroutine = null;
        }

        _isPlayingHitPulse = false;
        currentCombo = 0;
        remainingComboTime = 0f;
        _idleBeatPhase = 0f;

        if (_frameCanvasGroup != null)
        {
            _frameCanvasGroup.alpha = 0f;
        }

        if (_frameRootRect != null)
        {
            _frameRootRect.localScale = Vector3.one;
        }

        if (_timerBarFill != null)
        {
            _timerBarFill.fillAmount = 0f;
        }

        if (_pulseGlowRingImage != null && _pulseGlowRingRect != null)
        {
            _pulseGlowRingRect.localScale = Vector3.one;
            _pulseGlowRingImage.color = new Color(_activeRankColor.r, _activeRankColor.g, _activeRankColor.b, 0f);
        }
    }

    // ================================================================================================
    // กฎเหล็กข้อที่ 2: แอนิเมชันจังหวะหัวใจเต้น "ตึก-ตัก" (Heartbeat Double-Pulse Coroutine)
    // ================================================================================================
    private IEnumerator HeartbeatDoublePulseRoutine()
    {
        _isPlayingHitPulse = true;

        Color baseNumberColor = Color.Lerp(_activeRankColor, Color.white, 0.45f);

        // --- จังหวะที่ 1 ("ตึก!"): พุ่งขยาย 1.0x -> 1.42x ใน 0.07s (Ease-Out) + วงแสง Glow Ring ระเบิดออก ---
        float elapsed = 0f;
        float durUp1 = Mathf.Max(0.01f, beat1UpDuration);
        while (elapsed < durUp1)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / durUp1);
            float easeOut = 1f - Mathf.Pow(1f - t, 3f);

            float scale = Mathf.Lerp(1.0f, beat1PeakScale, easeOut);
            _frameRootRect.localScale = new Vector3(scale, scale, 1f);

            // วงแสง Pulse Glow Ring ขยายตัวออกและเรืองแสงสว่างจ้าในจังหวะกระแทก
            float ringScale = Mathf.Lerp(1.00f, 1.24f, easeOut);
            float ringAlpha = Mathf.Lerp(0.92f, 0.62f, easeOut);
            _pulseGlowRingRect.localScale = new Vector3(ringScale, ringScale, 1f);
            _pulseGlowRingImage.color = new Color(_activeRankColor.r, _activeRankColor.g, _activeRankColor.b, ringAlpha);

            // แฟลชตัวเลขเป็นสีขาวสว่างแวบหนึ่งเพิ่มแรงกระแทก
            if (_comboNumberText != null)
            {
                _comboNumberText.color = Color.Lerp(Color.white, baseNumberColor, t * 0.55f);
            }

            yield return null;
        }

        // --- ยุบตัวหลังจังหวะที่ 1: 1.42x -> 0.96x ใน 0.08s พร้อมวงแสงขยายต่อแล้วจางหาย ---
        elapsed = 0f;
        float durDown1 = Mathf.Max(0.01f, beat1DownDuration);
        while (elapsed < durDown1)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / durDown1);
            float smooth = t * t * (3f - 2f * t);

            float scale = Mathf.Lerp(beat1PeakScale, beat1DipScale, smooth);
            _frameRootRect.localScale = new Vector3(scale, scale, 1f);

            float ringScale = Mathf.Lerp(1.24f, 1.44f, t);
            float ringAlpha = Mathf.Lerp(0.62f, 0.0f, t);
            _pulseGlowRingRect.localScale = new Vector3(ringScale, ringScale, 1f);
            _pulseGlowRingImage.color = new Color(_activeRankColor.r, _activeRankColor.g, _activeRankColor.b, ringAlpha);

            if (_comboNumberText != null)
            {
                _comboNumberText.color = Color.Lerp(Color.Lerp(Color.white, baseNumberColor, 0.55f), baseNumberColor, t);
            }

            yield return null;
        }

        // --- จังหวะที่ 2 ("ตัก!"): กระแทกสะท้อนกลับขึ้นมา 0.96x -> 1.18x ใน 0.06s ---
        elapsed = 0f;
        float durUp2 = Mathf.Max(0.01f, beat2UpDuration);
        while (elapsed < durUp2)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / durUp2);
            float easeOut = 1f - Mathf.Pow(1f - t, 2.5f);

            float scale = Mathf.Lerp(beat1DipScale, beat2PeakScale, easeOut);
            _frameRootRect.localScale = new Vector3(scale, scale, 1f);

            // สะท้อนวงแสงจังหวะที่ 2 เบาๆ
            float ringScale = Mathf.Lerp(1.02f, 1.16f, easeOut);
            float ringAlpha = Mathf.Lerp(0.42f, 0.18f, easeOut);
            _pulseGlowRingRect.localScale = new Vector3(ringScale, ringScale, 1f);
            _pulseGlowRingImage.color = new Color(_activeRankColor.r, _activeRankColor.g, _activeRankColor.b, ringAlpha);

            yield return null;
        }

        // --- คลายตัวกลับสู่ขนาดปกติ: 1.18x -> 1.00x ใน 0.14s ---
        elapsed = 0f;
        float durSettle = Mathf.Max(0.01f, beat2SettleDuration);
        while (elapsed < durSettle)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / durSettle);
            float easeOut = 1f - Mathf.Pow(1f - t, 2f);

            float scale = Mathf.Lerp(beat2PeakScale, 1.00f, easeOut);
            _frameRootRect.localScale = new Vector3(scale, scale, 1f);

            float ringScale = Mathf.Lerp(1.16f, 1.28f, easeOut);
            float ringAlpha = Mathf.Lerp(0.18f, 0.00f, easeOut);
            _pulseGlowRingRect.localScale = new Vector3(ringScale, ringScale, 1f);
            _pulseGlowRingImage.color = new Color(_activeRankColor.r, _activeRankColor.g, _activeRankColor.b, ringAlpha);

            yield return null;
        }

        _frameRootRect.localScale = Vector3.one;
        _pulseGlowRingRect.localScale = Vector3.one;
        _pulseGlowRingImage.color = new Color(_activeRankColor.r, _activeRankColor.g, _activeRankColor.b, 0f);
        if (_comboNumberText != null)
        {
            _comboNumberText.color = baseNumberColor;
        }

        _isPlayingHitPulse = false;
        _heartbeatCoroutine = null;
    }

    // ================================================================================================
    // กฎเหล็กข้อที่ 3: ระบบเปลี่ยนสีกรอบและระดับแรงก์ตามความต่อเนื่องของคอมโบ
    // ================================================================================================
    private void ApplyRankStyleForCombo(int combo)
    {
        Color rankColor;
        string rankTitle;

        if (combo >= 50)
        {
            // 50+ Hits: สีแดงชาดคลั่ง
            rankColor = new Color(1.00f, 0.16f, 0.22f, 1f);
            rankTitle = "SSS  APOCALYPSE";
        }
        else if (combo >= 30)
        {
            // 30 - 49 Hits: สีส้มเพลิง
            rankColor = new Color(1.00f, 0.48f, 0.10f, 1f);
            rankTitle = "SS  SAVAGE";
        }
        else if (combo >= 20)
        {
            // 20 - 29 Hits: สีทองคำสว่าง
            rankColor = new Color(1.00f, 0.84f, 0.16f, 1f);
            rankTitle = "S  SUPREME";
        }
        else if (combo >= 10)
        {
            // 10 - 19 Hits: สีเขียวมรกต
            rankColor = new Color(0.14f, 0.94f, 0.54f, 1f);
            rankTitle = "A  ASSAULT";
        }
        else if (combo >= 5)
        {
            // 5 - 9 Hits: สีฟ้าครามคมกริบ
            rankColor = new Color(0.16f, 0.90f, 1.00f, 1f);
            rankTitle = "B  BRUTAL";
        }
        else
        {
            // 1 - 4 Hits: สีฟ้าครามคมกริบ
            rankColor = new Color(0.14f, 0.84f, 1.00f, 1f);
            rankTitle = "C  CHAIN";
        }

        _activeRankColor = rankColor;
        currentRankLabel = rankTitle;

        if (_leftAccentBar != null) _leftAccentBar.color = rankColor;
        if (_topBorderLine != null) _topBorderLine.color = new Color(rankColor.r, rankColor.g, rankColor.b, 0.92f);
        if (_bottomBorderLine != null) _bottomBorderLine.color = new Color(rankColor.r, rankColor.g, rankColor.b, 0.78f);
        if (_timerBarFill != null) _timerBarFill.color = rankColor;
        if (_rankPillBackground != null) _rankPillBackground.color = new Color(rankColor.r * 0.22f, rankColor.g * 0.22f, rankColor.b * 0.22f, 0.85f);

        if (_comboNumberText != null)
        {
            _comboNumberText.color = Color.Lerp(rankColor, Color.white, 0.45f);
        }

        if (_hitsLabelText != null)
        {
            _hitsLabelText.color = Color.Lerp(rankColor, Color.white, 0.72f);
        }

        if (_rankBadgeText != null)
        {
            _rankBadgeText.text = rankTitle;
            _rankBadgeText.color = Color.Lerp(rankColor, Color.white, 0.25f);
        }
    }

    // ================================================================================================
    // กฎเหล็กข้อที่ 1: Zero-Setup Procedural UI Builder (สร้างองค์ประกอบ UI ทั้งหมดใน Runtime)
    // ================================================================================================
    private void BuildProceduralSprites()
    {
        // 1. Sprite สีขาวบริสุทธิ์ 2x2 ในหน่วยความจำ เพื่อให้ Image.Type.Filled ของหลอดเวลาทำงาน 100%
        _whiteTexture2x2 = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        _whiteTexture2x2.wrapMode = TextureWrapMode.Clamp;
        Color[] whitePixels = { Color.white, Color.white, Color.white, Color.white };
        _whiteTexture2x2.SetPixels(whitePixels);
        _whiteTexture2x2.Apply();
        _whiteSprite = Sprite.Create(_whiteTexture2x2, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f), 100f);

        // 2. Sprite วงแสงขอบกรอบเรืองแสง (Pulse Glow Ring) 64x64 แบบกลวงตรงกลาง ขอบฟุ้งเรืองแสงรอบกรอบ
        const int size = 64;
        _glowRingTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        _glowRingTexture.wrapMode = TextureWrapMode.Clamp;
        Color[] ringPixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            float ny = ((float)y / (size - 1)) * 2f - 1f;
            for (int x = 0; x < size; x++)
            {
                float nx = ((float)x / (size - 1)) * 2f - 1f;
                float boxEdge = Mathf.Max(Mathf.Abs(nx), Mathf.Abs(ny));

                // สร้างขอบเรืองแสงรอบสี่เหลี่ยม (โปร่งตรงกลาง คมสว่างที่ระยะ 0.84 และฟุ้งนุ่มออกสู่ 1.0)
                float outerFade = 1f - SmoothStep(0.88f, 1.00f, boxEdge);
                float innerFade = SmoothStep(0.68f, 0.86f, boxEdge);
                float crispRim = Mathf.Exp(-Mathf.Pow((boxEdge - 0.85f) / 0.055f, 2f));
                float alpha = Mathf.Clamp01((innerFade * outerFade * 0.75f) + (crispRim * outerFade * 0.85f));

                ringPixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        _glowRingTexture.SetPixels(ringPixels);
        _glowRingTexture.Apply();
        _glowRingSprite = Sprite.Create(_glowRingTexture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    private void BuildProceduralComboUI()
    {
        Font uiFont = LoadSafeRuntimeFont();

        // 1. สร้าง Canvas (ScreenSpaceOverlay, ScaleWithScreenSize 1920x1080)
        GameObject canvasObj = new GameObject("Canvas_MMOHeartbeatComboUI");
        _canvas = canvasObj.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = canvasSortingOrder;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        // 2. สร้างกล่องกรอบหลักมุมขวาบน เอียงโฉบเฉี่ยว -4 องศา พร้อม CanvasGroup
        GameObject frameObj = new GameObject("ComboFrame_Root");
        frameObj.transform.SetParent(canvasObj.transform, false);

        _frameRootRect = frameObj.AddComponent<RectTransform>();
        _frameRootRect.anchorMin = new Vector2(1f, 1f);
        _frameRootRect.anchorMax = new Vector2(1f, 1f);
        _frameRootRect.pivot = new Vector2(0.5f, 0.5f);
        _frameRootRect.sizeDelta = new Vector2(330f, 152f);
        _frameRootRect.anchoredPosition = new Vector2(
            topRightOffset.x - _frameRootRect.sizeDelta.x * 0.5f,
            topRightOffset.y - _frameRootRect.sizeDelta.y * 0.5f);
        _frameRootRect.localRotation = Quaternion.Euler(0f, 0f, frameTiltAngle);

        _frameCanvasGroup = frameObj.AddComponent<CanvasGroup>();
        _frameCanvasGroup.alpha = 0f;
        _frameCanvasGroup.interactable = false;
        _frameCanvasGroup.blocksRaycasts = false;

        // 3. วงแสงกระแทกรอบกรอบ (Pulse Glow Ring) วางด้านหลังกรอบหลัก
        GameObject glowRingObj = new GameObject("PulseGlowRing");
        glowRingObj.transform.SetParent(_frameRootRect, false);
        _pulseGlowRingRect = glowRingObj.AddComponent<RectTransform>();
        _pulseGlowRingRect.anchorMin = Vector2.zero;
        _pulseGlowRingRect.anchorMax = Vector2.one;
        _pulseGlowRingRect.offsetMin = new Vector2(-24f, -20f);
        _pulseGlowRingRect.offsetMax = new Vector2(24f, 20f);
        _pulseGlowRingImage = glowRingObj.AddComponent<Image>();
        _pulseGlowRingImage.sprite = _glowRingSprite;
        _pulseGlowRingImage.color = new Color(_activeRankColor.r, _activeRankColor.g, _activeRankColor.b, 0f);
        _pulseGlowRingImage.raycastTarget = false;

        // 4. พื้นหลังสีเข้มโปร่งแสงของกรอบคอมโบ
        GameObject bgObj = new GameObject("FrameBackground");
        bgObj.transform.SetParent(_frameRootRect, false);
        RectTransform bgRect = bgObj.AddComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        _backgroundImage = bgObj.AddComponent<Image>();
        _backgroundImage.sprite = _whiteSprite;
        _backgroundImage.color = new Color(0.04f, 0.05f, 0.09f, 0.84f);
        _backgroundImage.raycastTarget = false;

        // 5. แถบเส้นสีเรืองแสงด้านซ้าย (Left Accent Bar)
        _leftAccentBar = CreateBarImage("LeftAccentBar", _frameRootRect,
            new Vector2(0f, 0f), new Vector2(0f, 1f),
            new Vector2(0f, 0f), new Vector2(9f, 0f), _activeRankColor);

        // 6. ขอบเส้นสีเรืองแสงด้านบน (Top Border Line)
        _topBorderLine = CreateBarImage("TopBorderLine", _frameRootRect,
            new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0f, -3.5f), new Vector2(0f, 0f), _activeRankColor);

        // 7. ขอบเส้นสีเรืองแสงด้านล่าง (Bottom Border Line)
        _bottomBorderLine = CreateBarImage("BottomBorderLine", _frameRootRect,
            new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(0f, 0f), new Vector2(0f, 3f), _activeRankColor);

        // 8. พื้นหลังป้ายระดับแรงก์ (Rank Badge Pill) บริเวณด้านบนของกรอบ
        GameObject rankPillObj = new GameObject("RankBadge_BG");
        rankPillObj.transform.SetParent(_frameRootRect, false);
        RectTransform rankPillRect = rankPillObj.AddComponent<RectTransform>();
        rankPillRect.anchorMin = new Vector2(0f, 1f);
        rankPillRect.anchorMax = new Vector2(1f, 1f);
        rankPillRect.offsetMin = new Vector2(18f, -36f);
        rankPillRect.offsetMax = new Vector2(-14f, -9f);
        _rankPillBackground = rankPillObj.AddComponent<Image>();
        _rankPillBackground.sprite = _whiteSprite;
        _rankPillBackground.color = new Color(0.08f, 0.18f, 0.24f, 0.85f);
        _rankPillBackground.raycastTarget = false;

        // 9. ป้ายข้อความระดับแรงก์ (Rank Badge Text เช่น "C  CHAIN" -> "SSS  APOCALYPSE")
        _rankBadgeText = CreateStyledText("RankBadge_Text", rankPillRect, uiFont,
            "C  CHAIN", 19, FontStyle.BoldAndItalic, TextAnchor.MiddleLeft, Color.white);
        RectTransform rankTextRect = _rankBadgeText.rectTransform;
        rankTextRect.anchorMin = Vector2.zero;
        rankTextRect.anchorMax = Vector2.one;
        rankTextRect.offsetMin = new Vector2(10f, 0f);
        rankTextRect.offsetMax = new Vector2(-8f, 0f);

        // 10. ตัวเลขคอมโบขนาดใหญ่ (BoldAndItalic + Shadow + Outline)
        _comboNumberText = CreateStyledText("ComboNumber_Text", _frameRootRect, uiFont,
            "0", 84, FontStyle.BoldAndItalic, TextAnchor.MiddleRight, Color.white);
        RectTransform numRect = _comboNumberText.rectTransform;
        numRect.anchorMin = new Vector2(0f, 0f);
        numRect.anchorMax = new Vector2(0.68f, 1f);
        numRect.offsetMin = new Vector2(14f, 18f);
        numRect.offsetMax = new Vector2(-4f, -34f);

        Outline numOutline = _comboNumberText.gameObject.AddComponent<Outline>();
        numOutline.effectColor = new Color(0f, 0f, 0f, 0.72f);
        numOutline.effectDistance = new Vector2(2f, -2f);

        // 11. ป้ายคำว่า "HITS" ข้างตัวเลขคอมโบ
        _hitsLabelText = CreateStyledText("HitsLabel_Text", _frameRootRect, uiFont,
            "HITS", 24, FontStyle.BoldAndItalic, TextAnchor.LowerLeft, Color.white);
        RectTransform hitsRect = _hitsLabelText.rectTransform;
        hitsRect.anchorMin = new Vector2(0.68f, 0f);
        hitsRect.anchorMax = new Vector2(1f, 1f);
        hitsRect.offsetMin = new Vector2(6f, 34f);
        hitsRect.offsetMax = new Vector2(-12f, -42f);

        // 12. รางพื้นหลังหลอดเวลา (Timer Bar Track)
        _timerBarTrack = CreateBarImage("TimerBar_Track", _frameRootRect,
            new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(18f, 12f), new Vector2(-14f, 22f), new Color(1f, 1f, 1f, 0.14f));

        // 13. หลอดเวลาคอมโบ (Filled Timer Bar ใช้ White Sprite 2x2 ในหน่วยความจำ)
        GameObject fillObj = new GameObject("TimerBar_Fill");
        fillObj.transform.SetParent(_timerBarTrack.rectTransform, false);
        RectTransform fillRect = fillObj.AddComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;

        _timerBarFill = fillObj.AddComponent<Image>();
        _timerBarFill.sprite = _whiteSprite;
        _timerBarFill.type = Image.Type.Filled;
        _timerBarFill.fillMethod = Image.FillMethod.Horizontal;
        _timerBarFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        _timerBarFill.fillAmount = 0f;
        _timerBarFill.color = _activeRankColor;
        _timerBarFill.raycastTarget = false;
    }

    private Image CreateBarImage(string objName, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 offsetMin, Vector2 offsetMax, Color color)
    {
        GameObject obj = new GameObject(objName);
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;

        Image img = obj.AddComponent<Image>();
        img.sprite = _whiteSprite;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private Text CreateStyledText(string objName, RectTransform parent, Font font, string initialText,
        int fontSize, FontStyle style, TextAnchor alignment, Color color)
    {
        GameObject obj = new GameObject(objName);
        obj.transform.SetParent(parent, false);
        obj.AddComponent<RectTransform>();

        Text txt = obj.AddComponent<Text>();
        txt.font = font;
        txt.text = initialText;
        txt.fontSize = fontSize;
        txt.fontStyle = style;
        txt.alignment = alignment;
        txt.color = color;
        txt.horizontalOverflow = HorizontalWrapMode.Overflow;
        txt.verticalOverflow = VerticalWrapMode.Overflow;
        txt.raycastTarget = false;

        Shadow shadow = obj.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
        shadow.effectDistance = new Vector2(3f, -3f);
        return txt;
    }

    private static Font LoadSafeRuntimeFont()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font != null) return font;

        font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (font != null) return font;

        return Font.CreateDynamicFontFromOSFont("Arial", 32);
    }

    // ================================================================================================
    // กฎเหล็กข้อที่ 4: รองรับปุ่มทดสอบ (คลิกซ้ายเมาส์ หรือ J) ทั้ง New Input System และ Legacy Input
    // ================================================================================================
    private static bool ReadUniversalTestAttackInput()
    {
        bool pressedNew = false;
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            pressedNew = true;
        }
        if (Keyboard.current != null && Keyboard.current.jKey.wasPressedThisFrame)
        {
            pressedNew = true;
        }

        bool pressedLegacy = false;
        try
        {
            if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.J))
            {
                pressedLegacy = true;
            }
        }
        catch
        {
            // ป้องกัน Exception หากโปรเจกต์ตั้ง Active Input Handling เป็น Input System Package (New) อย่างเดียว
        }

        return pressedNew || pressedLegacy;
    }

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp01((x - edge0) / Mathf.Max(0.00001f, edge1 - edge0));
        return t * t * (3f - 2f * t);
    }
}
