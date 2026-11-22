using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// ระบบควบคุมเอฟเฟกต์ "กลิ้งหลบแบบควันขาว / พุ่งตัวเป็นหมอกควันระดับ AAA (White Smoke Dodge Roll & Mist Dash VFX)"
/// สำหรับ Unity 6 (URP) รองรับทั้ง:
/// 1. การสร้าง Texture ก้อนเมฆควัน 2x2 (512x512 px) + เส้นลม + โหนด Particle ทั้ง 4 เลเยอร์ในหน่วยความจำอัตโนมัติ (Self-Contained Runtime Generator)
/// 2. การกดปุ่มกลิ้ง (Space, C, Left Ctrl) ทั้งจาก New Input System และ Legacy Input
/// 3. การตรวจจับชื่อท่ากลิ้งจาก Animator อัตโนมัติ (Roll, Great Sword Roll, Dodge, Dash, Evade, Tumble)
/// 4. การซิงก์กับ PlayerController (StartRoll), NetworkCharacter (STATE_ROLL / ACT_ROLL) และ PlayerCombat (RPC_BroadcastDodge / IsDashing)
/// 5. การเรียกใช้งานผ่าน Animation Event ด้วยฟังก์ชัน Public TriggerSmokeRoll()
/// </summary>
[AddComponentMenu("yeen VFX/04. [Player] กลิ้งหลบควันขาว (Dodge Roll Smoke)")]
[DisallowMultipleComponent]
public class MMODodgeSmokeController : MonoBehaviour
{
    [Header("=== 1. ปุ่มกดและการตรวจจับท่ากลิ้งอัตโนมัติ (Input & Animator Sync) ===")]
    [Tooltip("เปิดใช้งานการตรวจจับชื่อ State ท่ากลิ้งจาก Animator อัตโนมัติ (เช่น Roll, Great Sword Roll, Dodge, Dash)")]
    public bool autoDetectAnimatorRollState = true;

    [Tooltip("เปิดใช้งานการกดปุ่มคีย์บอร์ด (Space / C / Left Ctrl) เพื่อสั่งปล่อยควันกลิ้งหลบ")]
    public bool enableDirectKeyTrigger = true;

    [Tooltip("ปุ่มหลักสำหรับกดกลิ้งปล่อยควันขาว (ค่าเริ่มต้น = C)")]
    public KeyCode primaryRollKey = KeyCode.C;

    [Tooltip("ปุ่มรองสำหรับกดกลิ้งปล่อยควันขาว (ค่าเริ่มต้น = LeftControl)")]
    public KeyCode secondaryRollKey = KeyCode.LeftControl;

    [Tooltip("ซิงก์กับปุ่ม Space (ปุ่ม Roll มาตรฐานของเกม) และสถานะกลิ้งของ PlayerController / PlayerCapsule / NetworkCharacter อัตโนมัติ")]
    public bool syncWithCharacterRollAndDash = true;

    [Tooltip("รายชื่อ State ใน Animator ที่ถือว่าเป็นท่ากลิ้งหลบ")]
    public string[] rollStateNames = new string[]
    {
        "Roll",
        "Great Sword Roll",
        "Dodge",
        "Dash",
        "Evade",
        "Tumble",
        "Slide"
    };

    [Header("=== 2. การตั้งค่าจังหวะเวลาควันกลิ้ง (Smoke Roll Timing) ===")]
    [Tooltip("ระยะเวลาที่ตัวละครกลิ้งและปล่อยทางควันต่อเนื่อง (Layer 2 & Layer 3) ค่ามาตรฐาน = 0.42 วินาที")]
    [Range(0.15f, 1.20f)]
    public float rollDuration = 0.42f;

    [Tooltip("คูลดาวน์ขั้นต่ำระหว่างการจุดควันกลิ้งแต่ละครั้ง (วินาที) ป้องกันการทริกเกอร์ซ้ำในท่ากลิ้งเดียว")]
    [Range(0.10f, 1.00f)]
    public float minTriggerInterval = 0.35f;

    [Header("=== 3. โครงสร้างเอฟเฟกต์ควันกลิ้ง 4 เลเยอร์ (4-Layer White Smoke VFX) ===")]
    [Tooltip("โหนดแม่ของเอฟเฟกต์ควันกลิ้งทั้งหมด")]
    public Transform smokeVfxRoot;

    [Tooltip("Layer 1 (0.00s - 0.12s): กลุ่มควันขาวหนานุ่มระเบิดออกเป็นวงกลมติดพื้นจังหวะถีบตัวออกกลิ้ง (Burst 16 ก้อน)")]
    public ParticleSystem l1LaunchSmokeBurst;

    [Tooltip("Layer 2 (0.02s - 0.42s): กลุ่มเมฆควันขาวต่อเนื่องทิ้งรอยควันตามหลังตัวละครตลอดเส้นทางที่กลิ้งไป")]
    public ParticleSystem l2RollingSmokeTrail;

    [Tooltip("Layer 3 (0.05s - 0.35s): เส้นสายลมสีขาวโปร่งใสแหวกมวลควันไปด้านหลัง (Stretched Billboard)")]
    public ParticleSystem l3WindSpeedStreaks;

    [Tooltip("Layer 4 (0.42s - 0.85s): กลุ่มควันจางๆ กระจายตัวออกรอบตัวหลังกลิ้งจบและลุกขึ้นยืน (Burst 10 ก้อน)")]
    public ParticleSystem l4RecoverySmokeRing;

    [Header("=== 4. หน้าต่างแสดงสถานะบนหน้าจอ (On-Screen Debug HUD) ===")]
    [Tooltip("แสดงกล่องสถานะมุมซ้ายล่างของหน้าจอขณะทดสอบ (ค่าเริ่มต้นปิดไว้เพื่อให้หน้าจอสะอาด)")]
    public bool showDebugHUD = false;

    [Header("=== 5. สถานะการทำงานแบบ Real-Time (Read-Only) ===")]
    [SerializeField] private bool isRollingSmokeActive = false;
    [SerializeField] private int totalSmokeRollsTriggered = 0;
    [SerializeField] private string lastTriggerReason = "พร้อมใช้งาน (Ready)";

    private Animator cachedAnimator;
    private PlayerCapsule cachedPlayerCapsule;
    private PlayerCombat cachedPlayerCombat;
    private NetworkCharacter cachedNetCharacter;

    private Coroutine activeRollCoroutine;
    private float cooldownTimer = 0f;
    private bool wasInAnimatorRollStateLastFrame = false;
    private bool wasCapsuleDashingLastFrame = false;
    private bool wasCombatDodgingLastFrame = false;
    private bool wasNetRollingLastFrame = false;

    // Runtime Procedural Assets (สร้างในหน่วยความจำอัตโนมัติหากยังไม่มีโหนดลูก)
    private Texture2D runtimeSmokeAtlasTex;
    private Texture2D runtimeWindStreakTex;
    private Material runtimeSmokeMat;
    private Material runtimeWindStreakMat;

    private void Awake()
    {
        ResolveCharacterComponents();
        AutoBindLayerReferences();

        // หากตัวละครที่ถูก Spawn ออกมายังไม่มีโหนดเอฟเฟกต์ควันกลิ้งทั้ง 4 เลเยอร์
        // สคริปต์จะวาด Texture ควัน 2x2 และสร้าง Particle ทั้ง 4 เลเยอร์ให้ในหน่วยความจำโดยอัตโนมัติทันที 100%!
        if (l1LaunchSmokeBurst == null || l2RollingSmokeTrail == null || l3WindSpeedStreaks == null || l4RecoverySmokeRing == null)
        {
            BuildRuntime4LayerWhiteSmokeRollHierarchy();
        }

        InitializeParticlesForZeroCutOff();
    }

    private void ResolveCharacterComponents()
    {
        cachedAnimator = GetComponentInChildren<Animator>(true);
        if (cachedAnimator == null) cachedAnimator = GetComponentInParent<Animator>();

        cachedPlayerCapsule = GetComponentInParent<PlayerCapsule>();
        if (cachedPlayerCapsule == null) cachedPlayerCapsule = GetComponentInChildren<PlayerCapsule>(true);

        cachedPlayerCombat = GetComponentInParent<PlayerCombat>();
        if (cachedPlayerCombat == null) cachedPlayerCombat = GetComponentInChildren<PlayerCombat>(true);

        cachedNetCharacter = GetComponentInParent<NetworkCharacter>();
        if (cachedNetCharacter == null) cachedNetCharacter = GetComponentInChildren<NetworkCharacter>(true);
    }

    public void AutoBindLayerReferences()
    {
        if (smokeVfxRoot == null)
        {
            smokeVfxRoot = FindDeepChild(transform, "VFX_AAA_WhiteSmokeRoll_Root");
        }

        Transform searchRoot = smokeVfxRoot != null ? smokeVfxRoot : transform;

        if (l1LaunchSmokeBurst == null)
        {
            Transform t = FindDeepChild(searchRoot, "L1_Launch_SmokeBurst");
            if (t != null) l1LaunchSmokeBurst = t.GetComponent<ParticleSystem>();
        }

        if (l2RollingSmokeTrail == null)
        {
            Transform t = FindDeepChild(searchRoot, "L2_Rolling_SmokeTrail");
            if (t != null) l2RollingSmokeTrail = t.GetComponent<ParticleSystem>();
        }

        if (l3WindSpeedStreaks == null)
        {
            Transform t = FindDeepChild(searchRoot, "L3_Wind_SpeedStreaks");
            if (t != null) l3WindSpeedStreaks = t.GetComponent<ParticleSystem>();
        }

        if (l4RecoverySmokeRing == null)
        {
            Transform t = FindDeepChild(searchRoot, "L4_Recovery_SmokeRing");
            if (t != null) l4RecoverySmokeRing = t.GetComponent<ParticleSystem>();
        }
    }

    private void InitializeParticlesForZeroCutOff()
    {
        if (l1LaunchSmokeBurst != null)
        {
            ParticleSystem.EmissionModule em1 = l1LaunchSmokeBurst.emission;
            em1.enabled = false;
            l1LaunchSmokeBurst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        if (l4RecoverySmokeRing != null)
        {
            ParticleSystem.EmissionModule em4 = l4RecoverySmokeRing.emission;
            em4.enabled = false;
            l4RecoverySmokeRing.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        if (l2RollingSmokeTrail != null)
        {
            ParticleSystem.EmissionModule em2 = l2RollingSmokeTrail.emission;
            em2.enabled = false;
            if (!l2RollingSmokeTrail.isPlaying)
            {
                l2RollingSmokeTrail.Play(true);
            }
        }

        if (l3WindSpeedStreaks != null)
        {
            ParticleSystem.EmissionModule em3 = l3WindSpeedStreaks.emission;
            em3.enabled = false;
            if (!l3WindSpeedStreaks.isPlaying)
            {
                l3WindSpeedStreaks.Play(true);
            }
        }
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0.00001f) return;

        if (cooldownTimer > 0f)
        {
            cooldownTimer -= dt;
        }

        // 1. ตรวจจับการเปลี่ยนเข้าสู่ท่ากลิ้งใน Animator อัตโนมัติ (Roll, Great Sword Roll, Dodge, Dash)
        if (autoDetectAnimatorRollState)
        {
            string matchedState;
            bool inRollStateNow = CheckAnimatorInRollState(out matchedState);
            if (inRollStateNow && !wasInAnimatorRollStateLastFrame)
            {
                lastTriggerReason = "Animator State [" + matchedState + "]";
                TriggerSmokeRoll();
            }
            wasInAnimatorRollStateLastFrame = inRollStateNow;
        }

        // 2. ตรวจจับสถานะกลิ้งจากระบบตัวละครในโปรเจกต์ (NetworkCharacter.STATE_ROLL, PlayerCapsule.IsDashing, PlayerCombat.IsDodging)
        if (syncWithCharacterRollAndDash)
        {
            if (cachedNetCharacter != null && cachedNetCharacter.IsSpawned)
            {
                bool netRollingNow = cachedNetCharacter.AnimState == NetworkCharacter.STATE_ROLL;
                if (netRollingNow && !wasNetRollingLastFrame)
                {
                    lastTriggerReason = "NetworkCharacter STATE_ROLL";
                    TriggerSmokeRoll();
                }
                wasNetRollingLastFrame = netRollingNow;
            }

            if (cachedPlayerCapsule != null)
            {
                bool dashingNow = cachedPlayerCapsule.IsDashing;
                if (dashingNow && !wasCapsuleDashingLastFrame)
                {
                    lastTriggerReason = "PlayerCapsule IsDashing";
                    TriggerSmokeRoll();
                }
                wasCapsuleDashingLastFrame = dashingNow;
            }

            if (cachedPlayerCombat != null)
            {
                bool dodgingNow = cachedPlayerCombat.IsDodging;
                if (dodgingNow && !wasCombatDodgingLastFrame)
                {
                    lastTriggerReason = "PlayerCombat IsDodging";
                    TriggerSmokeRoll();
                }
                wasCombatDodgingLastFrame = dodgingNow;
            }
        }

        // 3. ตรวจจับการกดปุ่ม C, Left Ctrl หรือ Space บนคีย์บอร์ดทั้งจาก New Input System และ Legacy Input
        if (enableDirectKeyTrigger && ReadUniversalRollKeyPressed(out string keyName))
        {
            lastTriggerReason = "Key Pressed [" + keyName + "]";
            TriggerSmokeRoll();
        }
    }

    /// <summary>
    /// ฟังก์ชัน Public สำหรับสั่งจุดเอฟเฟกต์กลิ้งเป็นควันขาวทั้ง 4 เลเยอร์ทันที
    /// </summary>
    [ContextMenu("ทดสอบกลิ้งปล่อยควันขาวทันที (Trigger Smoke Roll)")]
    public void TriggerSmokeRoll()
    {
        if (cooldownTimer > 0f)
        {
            return;
        }

        cooldownTimer = minTriggerInterval;
        totalSmokeRollsTriggered++;

        if (activeRollCoroutine != null)
        {
            StopCoroutine(activeRollCoroutine);
        }

        activeRollCoroutine = StartCoroutine(SmokeRoll4LayerSequenceRoutine());
    }

    public void AnimEvent_SmokeRoll()
    {
        lastTriggerReason = "Animation Event";
        TriggerSmokeRoll();
    }

    private IEnumerator SmokeRoll4LayerSequenceRoutine()
    {
        isRollingSmokeActive = true;

        // LAYER 1 (0.00s - 0.12s): Launch Smoke Burst (ระเบิดควันขาว 16 ก้อนติดพื้นจังหวะถีบตัวออกกลิ้ง)
        if (l1LaunchSmokeBurst != null)
        {
            l1LaunchSmokeBurst.Play(true);
            l1LaunchSmokeBurst.Emit(16);
        }

        // LAYER 2 & LAYER 3 (0.02s - 0.42s): เปิดหัวฉีดควันต่อเนื่อง (Rolling Smoke Trail) และเส้นลม (Wind Speed Streaks)
        if (l2RollingSmokeTrail != null)
        {
            if (!l2RollingSmokeTrail.isPlaying)
            {
                l2RollingSmokeTrail.Play(true);
            }
            ParticleSystem.EmissionModule em2 = l2RollingSmokeTrail.emission;
            em2.enabled = true;
            // พ่นก้อนควันนำร่องทันที 4 ก้อนเพื่อให้เห็นมวลควันตั้งแต่วินาทีที่ 0.00 โดยไม่ต้องรอระยะทางก้าวแรก
            l2RollingSmokeTrail.Emit(4);
        }

        if (l3WindSpeedStreaks != null)
        {
            if (!l3WindSpeedStreaks.isPlaying)
            {
                l3WindSpeedStreaks.Play(true);
            }
            ParticleSystem.EmissionModule em3 = l3WindSpeedStreaks.emission;
            em3.enabled = true;
            l3WindSpeedStreaks.Emit(6);
        }

        float elapsed = 0f;
        float windStreakDuration = Mathf.Max(0.10f, rollDuration * 0.82f);
        bool windStreaksStopped = false;

        while (elapsed < rollDuration)
        {
            elapsed += Time.deltaTime;

            if (!windStreaksStopped && elapsed >= windStreakDuration)
            {
                windStreaksStopped = true;
                if (l3WindSpeedStreaks != null)
                {
                    ParticleSystem.EmissionModule em3 = l3WindSpeedStreaks.emission;
                    em3.enabled = false;
                }
            }

            yield return null;
        }

        if (l2RollingSmokeTrail != null)
        {
            ParticleSystem.EmissionModule em2 = l2RollingSmokeTrail.emission;
            em2.enabled = false;
        }

        if (l3WindSpeedStreaks != null)
        {
            ParticleSystem.EmissionModule em3 = l3WindSpeedStreaks.emission;
            em3.enabled = false;
        }

        // LAYER 4 (0.42s - 0.85s): Recovery Smoke Ring (ปล่อยควันจางๆ 10 ก้อนรอบตัวจังหวะกลิ้งจบและลุกขึ้นยืน)
        if (l4RecoverySmokeRing != null)
        {
            l4RecoverySmokeRing.Play(true);
            l4RecoverySmokeRing.Emit(10);
        }

        isRollingSmokeActive = false;
        activeRollCoroutine = null;
    }

    private bool CheckAnimatorInRollState(out string matchedStateName)
    {
        matchedStateName = string.Empty;
        if (cachedAnimator == null)
        {
            cachedAnimator = GetComponentInChildren<Animator>(true);
        }
        if (cachedAnimator == null || !cachedAnimator.isActiveAndEnabled || cachedAnimator.runtimeAnimatorController == null)
        {
            return false;
        }

        if (rollStateNames == null || rollStateNames.Length == 0)
        {
            return false;
        }

        int layerCount = cachedAnimator.layerCount;
        for (int layer = 0; layer < layerCount; layer++)
        {
            AnimatorStateInfo currentInfo = cachedAnimator.GetCurrentAnimatorStateInfo(layer);
            for (int i = 0; i < rollStateNames.Length; i++)
            {
                string stateName = rollStateNames[i];
                if (string.IsNullOrEmpty(stateName)) continue;

                if (currentInfo.IsName(stateName) || currentInfo.IsName("Base Layer." + stateName))
                {
                    matchedStateName = stateName;
                    return true;
                }
            }

            if (cachedAnimator.IsInTransition(layer))
            {
                AnimatorStateInfo nextInfo = cachedAnimator.GetNextAnimatorStateInfo(layer);
                for (int i = 0; i < rollStateNames.Length; i++)
                {
                    string stateName = rollStateNames[i];
                    if (string.IsNullOrEmpty(stateName)) continue;

                    if (nextInfo.IsName(stateName) || nextInfo.IsName("Base Layer." + stateName))
                    {
                        matchedStateName = stateName;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private bool ReadUniversalRollKeyPressed(out string pressedKeyLabel)
    {
        pressedKeyLabel = string.Empty;

        if (Keyboard.current != null)
        {
            if (primaryRollKey == KeyCode.C && Keyboard.current.cKey.wasPressedThisFrame)
            {
                pressedKeyLabel = "C";
                return true;
            }

            if (secondaryRollKey == KeyCode.LeftControl &&
                (Keyboard.current.leftCtrlKey.wasPressedThisFrame || Keyboard.current.rightCtrlKey.wasPressedThisFrame))
            {
                pressedKeyLabel = "Left Ctrl";
                return true;
            }
        }

        try
        {
            if (Input.GetKeyDown(primaryRollKey))
            {
                pressedKeyLabel = primaryRollKey.ToString();
                return true;
            }

            if (Input.GetKeyDown(secondaryRollKey))
            {
                pressedKeyLabel = secondaryRollKey.ToString();
                return true;
            }
        }
        catch
        {
            // Ignore if legacy input is disabled
        }

        return false;
    }

    // ================================================================================================
    // สร้าง Texture ควัน 2x2 และโหนดเอฟเฟกต์ควันกลิ้ง 4 เลเยอร์อัตโนมัติในหน่วยความจำ (Runtime Procedural Generator)
    // ================================================================================================
    private void BuildRuntime4LayerWhiteSmokeRollHierarchy()
    {
        runtimeSmokeAtlasTex = GenerateRuntimeSoftWhiteSmoke2x2Texture();
        runtimeWindStreakTex = GenerateRuntimeRollWindStreakTexture();

        runtimeSmokeMat = CreateRuntimeURPMaterial("MAT_RuntimeSoftWhiteSmoke_2x2", runtimeSmokeAtlasTex, new Color(1.0f, 1.0f, 1.0f, 0.90f));
        runtimeWindStreakMat = CreateRuntimeURPMaterial("MAT_RuntimeRollWindStreak", runtimeWindStreakTex, new Color(1.0f, 1.0f, 1.0f, 0.85f));

        if (smokeVfxRoot == null)
        {
            GameObject rootObj = new GameObject("VFX_AAA_WhiteSmokeRoll_Root");
            rootObj.transform.SetParent(transform, false);
            rootObj.transform.localPosition = Vector3.zero;
            rootObj.transform.localRotation = Quaternion.identity;
            rootObj.transform.localScale = Vector3.one;
            smokeVfxRoot = rootObj.transform;
        }

        // Layer 1: Launch Smoke Burst
        if (l1LaunchSmokeBurst == null)
        {
            GameObject l1Obj = new GameObject("L1_Launch_SmokeBurst");
            l1Obj.transform.SetParent(smokeVfxRoot, false);
            l1Obj.transform.localPosition = new Vector3(0f, 0.15f, 0f);
            l1Obj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            l1LaunchSmokeBurst = l1Obj.AddComponent<ParticleSystem>();
            SetupRuntimeLayer1LaunchSmokeBurst(l1LaunchSmokeBurst, runtimeSmokeMat);
        }

        // Layer 2: Rolling Smoke Trail
        if (l2RollingSmokeTrail == null)
        {
            GameObject l2Obj = new GameObject("L2_Rolling_SmokeTrail");
            l2Obj.transform.SetParent(smokeVfxRoot, false);
            l2Obj.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            l2RollingSmokeTrail = l2Obj.AddComponent<ParticleSystem>();
            SetupRuntimeLayer2RollingSmokeTrail(l2RollingSmokeTrail, runtimeSmokeMat);
        }

        // Layer 3: Wind Speed Streaks
        if (l3WindSpeedStreaks == null)
        {
            GameObject l3Obj = new GameObject("L3_Wind_SpeedStreaks");
            l3Obj.transform.SetParent(smokeVfxRoot, false);
            l3Obj.transform.localPosition = new Vector3(0f, 0.65f, 0.35f);
            l3Obj.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            l3WindSpeedStreaks = l3Obj.AddComponent<ParticleSystem>();
            SetupRuntimeLayer3WindSpeedStreaks(l3WindSpeedStreaks, runtimeWindStreakMat);
        }

        // Layer 4: Recovery Smoke Ring
        if (l4RecoverySmokeRing == null)
        {
            GameObject l4Obj = new GameObject("L4_Recovery_SmokeRing");
            l4Obj.transform.SetParent(smokeVfxRoot, false);
            l4Obj.transform.localPosition = new Vector3(0f, 0.25f, 0f);
            l4Obj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            l4RecoverySmokeRing = l4Obj.AddComponent<ParticleSystem>();
            SetupRuntimeLayer4RecoverySmokeRing(l4RecoverySmokeRing, runtimeSmokeMat);
        }
    }

    private static void SetupRuntimeLayer1LaunchSmokeBurst(ParticleSystem ps, Material smokeMat)
    {
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = ps.main;
        main.duration = 0.5f;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.72f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(3.5f, 6.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.75f, 1.25f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new Color(1.0f, 1.0f, 1.0f, 0.85f);
        main.gravityModifier = -0.08f;
        main.maxParticles = 60;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = false;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.35f;
        shape.radiusThickness = 0.85f;

        ParticleSystem.LimitVelocityOverLifetimeModule limitVel = ps.limitVelocityOverLifetime;
        limitVel.enabled = true;
        limitVel.limit = 0.65f;
        limitVel.dampen = 0.28f;

        ParticleSystem.RotationOverLifetimeModule rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-1.2f, 1.2f);

        ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0.0f, 0.45f, 0f, 2.2f),
            new Keyframe(0.35f, 1.0f, 0.6f, 0.6f),
            new Keyframe(1.0f, 1.45f, 0.2f, 0f)
        ));

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(BuildRuntimeSmokeGradient(0.85f));

        SetupRuntime2x2TextureSheet(ps);

        ParticleSystemRenderer psr = ps.GetComponent<ParticleSystemRenderer>();
        psr.renderMode = ParticleSystemRenderMode.Billboard;
        psr.sharedMaterial = smokeMat;
        psr.shadowCastingMode = ShadowCastingMode.Off;
        psr.receiveShadows = false;
        psr.sortingOrder = 6;
    }

    private static void SetupRuntimeLayer2RollingSmokeTrail(ParticleSystem ps, Material smokeMat)
    {
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = ps.main;
        main.duration = 1.0f;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.48f, 0.78f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.45f, 1.35f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.85f, 1.45f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new Color(1.0f, 1.0f, 1.0f, 0.82f);
        main.gravityModifier = -0.12f;
        main.maxParticles = 200;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = false;
        emission.rateOverTime = 35f;
        emission.rateOverDistance = 12f;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.38f;

        ParticleSystem.LimitVelocityOverLifetimeModule limitVel = ps.limitVelocityOverLifetime;
        limitVel.enabled = true;
        limitVel.limit = 0.5f;
        limitVel.dampen = 0.22f;

        ParticleSystem.RotationOverLifetimeModule rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-1.6f, 1.6f);

        ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0.0f, 0.42f, 0f, 2.0f),
            new Keyframe(0.35f, 1.05f, 0.8f, 0.8f),
            new Keyframe(1.0f, 1.65f, 0.3f, 0f)
        ));

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(BuildRuntimeSmokeGradient(0.82f));

        SetupRuntime2x2TextureSheet(ps);

        ParticleSystemRenderer psr = ps.GetComponent<ParticleSystemRenderer>();
        psr.renderMode = ParticleSystemRenderMode.Billboard;
        psr.sharedMaterial = smokeMat;
        psr.shadowCastingMode = ShadowCastingMode.Off;
        psr.receiveShadows = false;
        psr.sortingOrder = 7;
    }

    private static void SetupRuntimeLayer3WindSpeedStreaks(ParticleSystem ps, Material windStreakMat)
    {
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = ps.main;
        main.duration = 1.0f;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.30f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(8.5f, 14.0f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.09f, 0.18f);
        main.startColor = new Color(1.0f, 1.0f, 1.0f, 0.88f);
        main.gravityModifier = 0f;
        main.maxParticles = 120;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = false;
        emission.rateOverTime = 48f;
        emission.rateOverDistance = 6f;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 14f;
        shape.radius = 0.45f;
        shape.radiusThickness = 0.80f;

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(BuildRuntimeSmokeGradient(0.88f));

        ParticleSystemRenderer psr = ps.GetComponent<ParticleSystemRenderer>();
        psr.renderMode = ParticleSystemRenderMode.Stretch;
        psr.cameraVelocityScale = 0f;
        psr.velocityScale = 0.06f;
        psr.lengthScale = 4.5f;
        psr.sharedMaterial = windStreakMat;
        psr.shadowCastingMode = ShadowCastingMode.Off;
        psr.receiveShadows = false;
        psr.sortingOrder = 8;
    }

    private static void SetupRuntimeLayer4RecoverySmokeRing(ParticleSystem ps, Material smokeMat)
    {
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = ps.main;
        main.duration = 0.6f;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.75f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.8f, 3.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.65f, 1.15f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new Color(1.0f, 1.0f, 1.0f, 0.68f);
        main.gravityModifier = -0.06f;
        main.maxParticles = 40;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = false;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.40f;
        shape.radiusThickness = 1.0f;

        ParticleSystem.LimitVelocityOverLifetimeModule limitVel = ps.limitVelocityOverLifetime;
        limitVel.enabled = true;
        limitVel.limit = 0.45f;
        limitVel.dampen = 0.25f;

        ParticleSystem.RotationOverLifetimeModule rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-1.0f, 1.0f);

        ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0.0f, 0.55f),
            new Keyframe(0.4f, 1.10f),
            new Keyframe(1.0f, 1.55f)
        ));

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(BuildRuntimeSmokeGradient(0.68f));

        SetupRuntime2x2TextureSheet(ps);

        ParticleSystemRenderer psr = ps.GetComponent<ParticleSystemRenderer>();
        psr.renderMode = ParticleSystemRenderMode.Billboard;
        psr.sharedMaterial = smokeMat;
        psr.shadowCastingMode = ShadowCastingMode.Off;
        psr.receiveShadows = false;
        psr.sortingOrder = 6;
    }

    private static void SetupRuntime2x2TextureSheet(ParticleSystem ps)
    {
        ParticleSystem.TextureSheetAnimationModule tsa = ps.textureSheetAnimation;
        tsa.enabled = true;
        tsa.mode = ParticleSystemAnimationMode.Grid;
        tsa.numTilesX = 2;
        tsa.numTilesY = 2;
        tsa.animation = ParticleSystemAnimationType.WholeSheet;
        tsa.timeMode = ParticleSystemAnimationTimeMode.Lifetime;

        ParticleSystem.MinMaxCurve randomFrame = new ParticleSystem.MinMaxCurve(0f, 1f);
        randomFrame.mode = ParticleSystemCurveMode.TwoConstants;
        tsa.frameOverTime = randomFrame;
        tsa.startFrame = new ParticleSystem.MinMaxCurve(0f);
        tsa.cycleCount = 1;
    }

    private static Gradient BuildRuntimeSmokeGradient(float peakAlpha)
    {
        float clamped = Mathf.Clamp01(peakAlpha);
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(1.0f, 1.0f, 1.0f), 0.0f),
                new GradientColorKey(new Color(0.95f, 0.97f, 1.0f), 0.5f),
                new GradientColorKey(new Color(0.90f, 0.93f, 0.98f), 1.0f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0.0f, 0.0f),
                new GradientAlphaKey(clamped, 0.16f),
                new GradientAlphaKey(clamped * 0.62f, 0.55f),
                new GradientAlphaKey(0.0f, 1.0f)
            }
        );
        return grad;
    }

    private static Texture2D GenerateRuntimeSoftWhiteSmoke2x2Texture()
    {
        int totalSize = 256;
        int cellSize = 128;
        Texture2D tex = new Texture2D(totalSize, totalSize, TextureFormat.RGBA32, false);
        tex.name = "T_RuntimeSoftWhiteSmoke_2x2";
        tex.wrapMode = TextureWrapMode.Clamp;
        Color[] pixels = new Color[totalSize * totalSize];

        Vector2[] cellSeeds = new Vector2[]
        {
            new Vector2(12.34f, 45.67f),
            new Vector2(78.91f, 23.45f),
            new Vector2(142.7f, 89.12f),
            new Vector2(215.4f, 167.8f)
        };

        for (int cy = 0; cy < 2; cy++)
        {
            for (int cx = 0; cx < 2; cx++)
            {
                Vector2 seed = cellSeeds[cy * 2 + cx];
                for (int ly = 0; ly < cellSize; ly++)
                {
                    float ny = ((ly / (float)(cellSize - 1)) * 2f) - 1f;
                    for (int lx = 0; lx < cellSize; lx++)
                    {
                        float nx = ((lx / (float)(cellSize - 1)) * 2f) - 1f;
                        float r = Mathf.Sqrt(nx * nx + ny * ny);

                        float warp = Mathf.PerlinNoise(seed.x + nx * 1.8f, seed.y + ny * 1.8f) * 0.28f;
                        float radialMask = 1f - Mathf.SmoothStep(0.18f, 0.88f, r - warp * 0.45f);
                        float hardBorderGuard = 1f - Mathf.SmoothStep(0.78f, 0.96f, r);

                        float n1 = Mathf.PerlinNoise(seed.x + nx * 2.2f, seed.y + ny * 2.2f);
                        float n2 = Mathf.PerlinNoise(seed.x * 1.7f + nx * 4.5f, seed.y * 1.7f + ny * 4.5f) * 0.5f;
                        float n3 = Mathf.PerlinNoise(seed.x * 2.3f + nx * 9.0f, seed.y * 2.3f + ny * 9.0f) * 0.25f;
                        float fbm = (n1 + n2 + n3) / 1.75f;

                        float billowCore = Mathf.SmoothStep(0.22f, 0.85f, fbm);
                        float alpha = Mathf.Clamp01(radialMask * hardBorderGuard * (0.35f + billowCore * 0.85f));
                        float shade = Mathf.Lerp(0.88f, 1.0f, billowCore);

                        int gx = cx * cellSize + lx;
                        int gy = cy * cellSize + ly;
                        pixels[gy * totalSize + gx] = new Color(shade, shade, 1.0f, alpha);
                    }
                }
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    private static Texture2D GenerateRuntimeRollWindStreakTexture()
    {
        int width = 128;
        int height = 32;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.name = "T_RuntimeRollWindStreak";
        tex.wrapMode = TextureWrapMode.Clamp;
        Color[] pixels = new Color[width * height];

        for (int y = 0; y < height; y++)
        {
            float ny = ((y / (float)(height - 1)) * 2f) - 1f;
            float outerSoft = Mathf.Exp(-ny * ny * 4.5f);
            float innerCore = Mathf.Exp(-ny * ny * 18.0f) * 0.85f;

            for (int x = 0; x < width; x++)
            {
                float nx = x / (float)(width - 1);
                float headFade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(nx / 0.16f));
                float tailFade = Mathf.Pow(Mathf.Clamp01((1f - nx) / 0.84f), 1.3f);
                float alpha = Mathf.Clamp01((outerSoft + innerCore) * headFade * tailFade);
                pixels[y * width + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    private static Material CreateRuntimeURPMaterial(string matName, Texture2D tex, Color tint)
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        Material mat = new Material(shader);
        mat.name = matName;
        mat.mainTexture = tex;
        mat.color = tint;
        mat.renderQueue = (int)RenderQueue.Transparent;
        return mat;
    }

    private void OnGUI()
    {
        if (!showDebugHUD || !Application.isPlaying)
        {
            return;
        }

        GUIStyle boxStyle = new GUIStyle(GUI.skin.box);
        boxStyle.alignment = TextAnchor.UpperLeft;
        boxStyle.fontSize = 14;
        boxStyle.normal.textColor = Color.white;

        float boxHeight = 122f;
        float boxY = Mathf.Max(14f, Screen.height - boxHeight - 18f);

        GUI.color = new Color(0.05f, 0.08f, 0.12f, 0.86f);
        GUI.Box(new Rect(14f, boxY, 445f, boxHeight), GUIContent.none, boxStyle);
        GUI.color = Color.white;

        GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.fontSize = 14;
        labelStyle.richText = true;
        labelStyle.normal.textColor = Color.white;

        string statusBadge = isRollingSmokeActive
            ? "<color=#55FF77><b>กำลังกลิ้ง! (ROLLING SMOKE ON)</b></color>"
            : "<color=#66E0FF><b>พร้อมใช้งาน (READY)</b></color>";

        string hudText =
            "<b>[AAA White Smoke Dodge Roll & Mist Dash VFX]</b>\n" +
            "• <b>สถานะควันกลิ้ง:</b> " + statusBadge + "  <i>(Duration: " + rollDuration.ToString("F2") + "s)</i>\n" +
            "• <b>ปุ่มทดสอบ:</b> กดปุ่ม <b>[Space]</b> หรือ <b>[C]</b> หรือ <b>[Left Ctrl]</b>\n" +
            "• <b>ทริกเกอร์ล่าสุด:</b> " + lastTriggerReason + " (รวม: " + totalSmokeRollsTriggered + " ครั้ง)";

        GUI.Label(new Rect(24f, boxY + 6f, 425f, boxHeight - 12f), hudText, labelStyle);
    }

    private static Transform FindDeepChild(Transform parent, string targetName)
    {
        if (parent == null || string.IsNullOrEmpty(targetName)) return null;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name == targetName)
            {
                return child;
            }

            Transform found = FindDeepChild(child, targetName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private void OnDestroy()
    {
        if (runtimeSmokeMat != null) Destroy(runtimeSmokeMat);
        if (runtimeWindStreakMat != null) Destroy(runtimeWindStreakMat);
        if (runtimeSmokeAtlasTex != null) Destroy(runtimeSmokeAtlasTex);
        if (runtimeWindStreakTex != null) Destroy(runtimeWindStreakTex);
    }
}
