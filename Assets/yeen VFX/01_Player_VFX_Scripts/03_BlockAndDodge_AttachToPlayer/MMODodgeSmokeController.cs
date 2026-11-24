using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// ระบบควบคุมเอฟเฟกต์ "กลิ้งหลบหมอกหยกและริบบิ้นสายลมระดับ AAA (Cloud-Step Mist & Silky Wind Dodge Roll VFX)"
/// สำหรับ Unity 6 (URP) สไตล์ Where Winds Meet / Wuthering Waves (สะอาด หรูหรา พริ้วไหว ไม่มีใบไม้และไม่มีวงแหวนแหวกลม):
/// - Layer 1 (0.00s - 0.12s): หมอกหยกพู่กันเนื้อนุ่มพุ่งกระจายติดพื้นจังหวะถีบตัวออกกลิ้ง (Launch Cloud-Step Mist)
/// - Layer 2 (0.02s - 0.42s): หางหมอกหยกขาวจันทร์ลอยต่ำพริ้วตามแนวกลิ้งจริง (Rolling Cloud-Step Mist Trail)
/// - Layer 3 (0.02s - 0.36s): ริบบิ้นสายลมโค้งพริ้วไหว + เส้นลมบางคม + ละอองลมปราณหมุนตามทิศกลิ้งจริง (Curving Silk Trails, Wind Wisps & Qi Motes)
/// - Layer 4 (0.42s - 0.85s): หมอกนุ่มติดพื้นจังหวะลงเท้าตั้งท่าหลังกลิ้งจบ (Soft Recovery Ground Mist)
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

    [Header("=== 3. โครงสร้างเอฟเฟกต์ควันกลิ้ง 4 เลเยอร์ (4-Layer Cloud-Step Dodge VFX) ===")]
    [Tooltip("โหนดแม่ของเอฟเฟกต์ควันกลิ้งทั้งหมด")]
    public Transform smokeVfxRoot;

    [Tooltip("Layer 1 (0.00s - 0.12s): หมอกหยกพู่กันนุ่มติดพื้นจังหวะถีบตัวออกกลิ้ง")]
    public ParticleSystem l1LaunchSmokeBurst;

    [Tooltip("Layer 2 (0.02s - 0.42s): หางหมอกหยกขาวจันทร์ลอยต่ำตามแนวกลิ้ง")]
    public ParticleSystem l2RollingSmokeTrail;

    [Tooltip("Layer 3 (0.02s - 0.36s): เส้นสายลมบางคมแหวกอากาศตามทิศกลิ้ง")]
    public ParticleSystem l3WindSpeedStreaks;

    [Tooltip("Layer 4 (0.42s - 0.85s): หมอกนุ่มติดพื้นจังหวะลงเท้าตั้งท่าหลังกลิ้งจบ")]
    public ParticleSystem l4RecoverySmokeRing;

    [Header("=== 4. หน้าต่างแสดงสถานะบนหน้าจอ (On-Screen Debug HUD) ===")]
    [Tooltip("แสดงกล่องสถานะมุมซ้ายล่างของหน้าจอขณะทดสอบ (ค่าเริ่มต้นปิดไว้เพื่อให้หน้าจอสะอาด)")]
    public bool showDebugHUD = false;

    [Header("=== 5. สถานะการทำงานแบบ Real-Time (Read-Only) ===")]
    [SerializeField] private bool isRollingSmokeActive = false;
    [SerializeField] private int totalSmokeRollsTriggered = 0;
    [SerializeField] private string lastTriggerReason = "พร้อมใช้งาน (Ready)";

    // เลเยอร์เสริมระดับ AAA: ริบบิ้นสายลมโค้งพริ้วไหว (ไม่มีใบไม้ วงแหวนแหวกลม และละออง)
    private ParticleSystem l3SilkRibbonsPS;

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

    private Vector3 previousWorldPos;
    private Vector3 currentMoveDirection = Vector3.forward;
    private bool hasInitializedPos = false;

    // Runtime Procedural Assets
    private Texture2D runtimeSmokeAtlasTex;
    private Texture2D runtimeWindStreakTex;
    private Texture2D runtimeSilkRibbonTex;

    private Material runtimeSmokeMat;
    private Material runtimeWindStreakMat;
    private Material runtimeSilkRibbonMat;

    private void Awake()
    {
        ResolveCharacterComponents();
        AutoBindLayerReferences();

        if (l1LaunchSmokeBurst == null || l2RollingSmokeTrail == null || l3WindSpeedStreaks == null || l4RecoverySmokeRing == null)
        {
            BuildRuntime4LayerWhiteSmokeRollHierarchy();
        }
        else
        {
            EnsureSilkRibbonAndQiMoteEnhancements();
        }

        InitializeParticlesForZeroCutOff();
        previousWorldPos = transform.position;
        hasInitializedPos = true;
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
        StopBurstEmitter(l1LaunchSmokeBurst);
        StopBurstEmitter(l4RecoverySmokeRing);
        StopLoopEmitter(l2RollingSmokeTrail);
        StopLoopEmitter(l3WindSpeedStreaks);
        StopLoopEmitter(l3SilkRibbonsPS);
    }

    private static void StopBurstEmitter(ParticleSystem ps)
    {
        if (ps == null) return;
        var em = ps.emission;
        em.enabled = false;
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private static void StopLoopEmitter(ParticleSystem ps)
    {
        if (ps == null) return;
        var em = ps.emission;
        em.enabled = false;
        if (!ps.isPlaying) ps.Play(true);
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0.00001f) return;

        UpdateMoveDirectionAndAlignWindLayers(dt);

        if (cooldownTimer > 0f)
        {
            cooldownTimer -= dt;
        }

        // 1. ตรวจจับการเปลี่ยนเข้าสู่ท่ากลิ้งใน Animator อัตโนมัติ
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

        // 2. ตรวจจับสถานะกลิ้งจากระบบตัวละครในโปรเจกต์
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

        // 3. โหมดทดสอบเดี่ยวเฉพาะเครื่องเจ้าของตัวละครที่ไม่ได้ซิงก์ผ่าน Fusion Network เท่านั้น
        // เพื่อป้องกันเมื่อ Host กดปุ่มกลิ้งแล้วตัวละครผู้เล่นคนอื่นในห้องปล่อยเอฟเฟกต์พร้อมกัน
        bool hasNetworkCharacterSync = (cachedPlayerCombat != null && cachedPlayerCombat.IsSpawned)
            || (cachedPlayerCapsule != null && cachedPlayerCapsule.Object != null && cachedPlayerCapsule.Object.IsValid)
            || (cachedNetCharacter != null && cachedNetCharacter.IsSpawned);

        if (enableDirectKeyTrigger && !hasNetworkCharacterSync && IsLocallyOwnedCharacter() && ReadUniversalRollKeyPressed(out string keyName))
        {
            lastTriggerReason = "Key Pressed [" + keyName + "]";
            TriggerSmokeRoll();
        }
    }

    private bool IsLocallyOwnedCharacter()
    {
        if (cachedPlayerCombat != null && cachedPlayerCombat.IsSpawned)
            return cachedPlayerCombat.IsLocallyControlled;
        if (cachedPlayerCapsule != null && cachedPlayerCapsule.Object != null && cachedPlayerCapsule.Object.IsValid)
            return cachedPlayerCapsule.Object.HasStateAuthority || cachedPlayerCapsule.Object.HasInputAuthority;
        if (cachedNetCharacter != null && cachedNetCharacter.IsSpawned && cachedNetCharacter.Object != null && cachedNetCharacter.Object.IsValid)
            return cachedNetCharacter.Object.HasStateAuthority || cachedNetCharacter.Object.HasInputAuthority;
        return true;
    }

    private void UpdateMoveDirectionAndAlignWindLayers(float dt)
    {
        if (!hasInitializedPos)
        {
            previousWorldPos = transform.position;
            hasInitializedPos = true;
        }

        Vector3 currentPos = transform.position;
        Vector3 delta = new Vector3(currentPos.x - previousWorldPos.x, 0f, currentPos.z - previousWorldPos.z);
        float dist = delta.magnitude;
        if (dist > 0.002f)
        {
            currentMoveDirection = Vector3.Slerp(currentMoveDirection, delta / dist, Mathf.Clamp01(dt * 24f));
        }
        else
        {
            Vector3 flatFwd = new Vector3(transform.forward.x, 0f, transform.forward.z);
            if (flatFwd.sqrMagnitude > 0.001f)
            {
                currentMoveDirection = flatFwd.normalized;
            }
        }
        previousWorldPos = currentPos;

        if (currentMoveDirection.sqrMagnitude < 0.001f) return;

        // หันริบบิ้นลมและเส้นลมให้พัดสวนทิศกลิ้งจริง (รองรับกลิ้งหน้า หลัง ซ้าย ขวา และเฉียง 360 องศา)
        Quaternion backwardRot = Quaternion.LookRotation(-currentMoveDirection, Vector3.up);
        float feetY = GetFeetLocalY();

        if (l3WindSpeedStreaks != null)
        {
            l3WindSpeedStreaks.transform.position = transform.position + Vector3.up * (feetY + 0.26f) + currentMoveDirection * 0.20f;
            l3WindSpeedStreaks.transform.rotation = backwardRot;
        }

        if (l3SilkRibbonsPS != null)
        {
            l3SilkRibbonsPS.transform.position = transform.position + Vector3.up * (feetY + 0.25f) + currentMoveDirection * 0.16f;
            l3SilkRibbonsPS.transform.rotation = backwardRot;
        }
    }

    [ContextMenu("ทดสอบกลิ้งปล่อยควันขาวทันที (Trigger Smoke Roll)")]
    public void TriggerSmokeRoll()
    {
        if (cooldownTimer > 0f) return;

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

        if (smokeVfxRoot != null && !smokeVfxRoot.gameObject.activeSelf)
        {
            smokeVfxRoot.gameObject.SetActive(true);
        }

        // LAYER 1 (0.00s - 0.12s): หมอกหยกพู่กันนุ่มติดพื้น 11 ก้อนจังหวะถีบตัวออกกลิ้ง (ไม่มีใบไม้และวงแหวนแหวกลม)
        if (l1LaunchSmokeBurst != null)
        {
            l1LaunchSmokeBurst.Play(true);
            l1LaunchSmokeBurst.Emit(11);
        }

        // LAYER 2 & LAYER 3 (0.02s - 0.42s): หางหมอกหยกขาวจันทร์ + ริบบิ้นสายลมโค้งพริ้ว + เส้นลมบางคม
        EnableLoopEmissionAndBurst(l2RollingSmokeTrail, 4);
        EnableLoopEmissionAndBurst(l3WindSpeedStreaks, 6);
        EnableLoopEmissionAndBurst(l3SilkRibbonsPS, 4);

        float elapsed = 0f;
        float windStreakDuration = Mathf.Max(0.12f, rollDuration * 0.84f);
        bool windStreaksStopped = false;

        while (elapsed < rollDuration)
        {
            elapsed += Time.deltaTime;

            if (!windStreaksStopped && elapsed >= windStreakDuration)
            {
                windStreaksStopped = true;
                DisableLoopEmission(l3WindSpeedStreaks);
                DisableLoopEmission(l3SilkRibbonsPS);
            }

            yield return null;
        }

        DisableLoopEmission(l2RollingSmokeTrail);
        DisableLoopEmission(l3WindSpeedStreaks);
        DisableLoopEmission(l3SilkRibbonsPS);

        // LAYER 4 (0.42s - 0.85s): หมอกนุ่มติดพื้น 8 ก้อนจังหวะลงเท้าตั้งท่าหลังกลิ้งจบ
        if (l4RecoverySmokeRing != null)
        {
            l4RecoverySmokeRing.Play(true);
            l4RecoverySmokeRing.Emit(8);
        }

        isRollingSmokeActive = false;
        activeRollCoroutine = null;
    }

    private static void EnableLoopEmissionAndBurst(ParticleSystem ps, int burstCount)
    {
        if (ps == null) return;
        if (!ps.isPlaying) ps.Play(true);
        var em = ps.emission;
        em.enabled = true;
        if (burstCount > 0) ps.Emit(burstCount);
    }

    private static void DisableLoopEmission(ParticleSystem ps)
    {
        if (ps == null) return;
        var em = ps.emission;
        em.enabled = false;
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
        }

        return false;
    }

    private float GetFeetLocalY()
    {
        Transform visual = transform.Find("CharacterVisual");
        return visual != null ? visual.localPosition.y : 0f;
    }

    // ================================================================================================
    // สร้าง Texture หมอกพู่กันหยก 2x2 + ริบบิ้นไหมสายลม + ละอองลมปราณ และโครงสร้าง 4 เลเยอร์อัตโนมัติ
    // ================================================================================================
    private void EnsureRuntimeMaterials()
    {
        if (runtimeSmokeAtlasTex == null) runtimeSmokeAtlasTex = GeneratePainterlyJadeMist2x2Texture();
        if (runtimeWindStreakTex == null) runtimeWindStreakTex = GenerateSilkyRollWindStreakTexture();
        if (runtimeSilkRibbonTex == null) runtimeSilkRibbonTex = GenerateDodgeSilkRibbonTexture();

        if (runtimeSmokeMat == null)
            runtimeSmokeMat = CreateRuntimeAlphaSmokeMaterial("MAT_WWM_CloudStepMist_2x2", runtimeSmokeAtlasTex);
        if (runtimeWindStreakMat == null)
            runtimeWindStreakMat = CreateRuntimeURPAdditiveMaterial("MAT_WWM_DodgeWindStreak", runtimeWindStreakTex, new Color(1.65f, 1.98f, 1.92f, 1f));
        if (runtimeSilkRibbonMat == null)
            runtimeSilkRibbonMat = CreateRuntimeURPAdditiveMaterial("MAT_WWM_DodgeSilkRibbon", runtimeSilkRibbonTex, new Color(1.55f, 1.92f, 1.88f, 1f));
    }

    private void BuildRuntime4LayerWhiteSmokeRollHierarchy()
    {
        EnsureRuntimeMaterials();
        float feetY = GetFeetLocalY();

        if (smokeVfxRoot == null)
        {
            GameObject rootObj = new GameObject("VFX_AAA_WhiteSmokeRoll_Root");
            rootObj.transform.SetParent(transform, false);
            rootObj.transform.localPosition = new Vector3(0f, feetY, 0f);
            rootObj.transform.localRotation = Quaternion.identity;
            rootObj.transform.localScale = Vector3.one;
            smokeVfxRoot = rootObj.transform;
        }

        // ลบโหนดวงแหวนแหวกลมและใบไม้รุ่นเก่าออกให้หมด (ถ้าเคยมีติดอยู่)
        DestroyChildIfExists(smokeVfxRoot, "L1B_Launch_GroundAirRing");
        DestroyChildIfExists(smokeVfxRoot, "L3B_WWM_DodgeBambooLeaves");

        // Layer 1: Launch Cloud-Step Mist Burst (หมอกหยกพู่กันเนื้อนุ่มติดพื้นช่วงขา/เท้าจังหวะถีบตัว)
        if (l1LaunchSmokeBurst == null)
        {
            GameObject l1Obj = new GameObject("L1_Launch_SmokeBurst");
            l1Obj.transform.SetParent(smokeVfxRoot, false);
            l1Obj.transform.localPosition = new Vector3(0f, 0.10f, -0.05f);
            l1Obj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            l1LaunchSmokeBurst = l1Obj.AddComponent<ParticleSystem>();
            SetupRuntimeLayer1LaunchSmokeBurst(l1LaunchSmokeBurst, runtimeSmokeMat);
        }

        // Layer 2: Rolling Cloud-Step Mist Trail (หางหมอกหยกขาวจันทร์ลอยต่ำช่วงขา/ข้อเท้า)
        if (l2RollingSmokeTrail == null)
        {
            GameObject l2Obj = new GameObject("L2_Rolling_SmokeTrail");
            l2Obj.transform.SetParent(smokeVfxRoot, false);
            l2Obj.transform.localPosition = new Vector3(0f, 0.18f, -0.05f);
            l2RollingSmokeTrail = l2Obj.AddComponent<ParticleSystem>();
            SetupRuntimeLayer2RollingSmokeTrail(l2RollingSmokeTrail, runtimeSmokeMat);
        }

        // Layer 3: Sleek Calligraphy Wind Streaks (เส้นสายลมบางคมระดับช่วงขาตามทิศกลิ้งจริง)
        if (l3WindSpeedStreaks == null)
        {
            GameObject l3Obj = new GameObject("L3_Wind_SpeedStreaks");
            l3Obj.transform.SetParent(smokeVfxRoot, false);
            l3Obj.transform.localPosition = new Vector3(0f, 0.26f, 0.20f);
            l3Obj.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            l3WindSpeedStreaks = l3Obj.AddComponent<ParticleSystem>();
            SetupRuntimeLayer3WindSpeedStreaks(l3WindSpeedStreaks, runtimeWindStreakMat);
        }

        // Layer 4: Soft Recovery Ground Mist (หมอกนุ่มติดพื้นจังหวะลงเท้าตั้งท่าหลังกลิ้งจบ)
        if (l4RecoverySmokeRing == null)
        {
            GameObject l4Obj = new GameObject("L4_Recovery_SmokeRing");
            l4Obj.transform.SetParent(smokeVfxRoot, false);
            l4Obj.transform.localPosition = new Vector3(0f, 0.08f, 0.05f);
            l4Obj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            l4RecoverySmokeRing = l4Obj.AddComponent<ParticleSystem>();
            SetupRuntimeLayer4RecoverySmokeRing(l4RecoverySmokeRing, runtimeSmokeMat);
        }

        EnsureSilkRibbonAndQiMoteEnhancements();
    }

    private void EnsureSilkRibbonAndQiMoteEnhancements()
    {
        EnsureRuntimeMaterials();
        Transform parentRoot = smokeVfxRoot != null ? smokeVfxRoot : transform;

        DestroyChildIfExists(parentRoot, "L1B_Launch_GroundAirRing");
        DestroyChildIfExists(parentRoot, "L3B_WWM_DodgeBambooLeaves");
        DestroyChildIfExists(parentRoot, "L3B_Dodge_SilkRibbons");
        DestroyChildIfExists(parentRoot, "L3C_Dodge_QiMotes");

        // Layer 3B: Curving Silk Wind Ribbons (ริบบิ้นลมโค้งพริ้วไหวระดับช่วงขาตอนกลิ้งหลบ)
        GameObject ribObj = new GameObject("L3B_Dodge_SilkRibbons");
        ribObj.transform.SetParent(parentRoot, false);
        ribObj.transform.localPosition = new Vector3(0f, 0.25f, 0.16f);
        ribObj.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        l3SilkRibbonsPS = ribObj.AddComponent<ParticleSystem>();
        SetupRuntimeLayer3BSilkRibbons(l3SilkRibbonsPS, runtimeSilkRibbonMat);
    }

    private static void SetupRuntimeLayer1LaunchSmokeBurst(ParticleSystem ps, Material smokeMat)
    {
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = 0.45f;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.38f, 0.60f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2.2f, 4.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.46f, 0.78f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = Color.white;
        main.gravityModifier = -0.05f;
        main.maxParticles = 40;

        var emission = ps.emission;
        emission.enabled = false;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.28f;
        shape.radiusThickness = 0.85f;

        var limitVel = ps.limitVelocityOverLifetime;
        limitVel.enabled = true;
        limitVel.limit = 0.36f;
        limitVel.dampen = 0.32f;

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.22f;
        noise.frequency = 1.25f;
        noise.scrollSpeed = 0.85f;
        noise.damping = true;

        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-1.1f, 1.1f);

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0.0f, 0.42f, 0f, 2.4f),
            new Keyframe(0.35f, 1.02f, 0.5f, 0.4f),
            new Keyframe(1.0f, 1.46f, 0.15f, 0f)
        ));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(BuildRuntimeCloudMistGradient(0.62f));

        SetupRuntime2x2TextureSheet(ps);

        var psr = ps.GetComponent<ParticleSystemRenderer>();
        psr.renderMode = ParticleSystemRenderMode.Billboard;
        psr.sharedMaterial = smokeMat;
        psr.shadowCastingMode = ShadowCastingMode.Off;
        psr.receiveShadows = false;
        psr.sortingOrder = 5;
    }

    private static void SetupRuntimeLayer2RollingSmokeTrail(ParticleSystem ps, Material smokeMat)
    {
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = 1.0f;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.42f, 0.64f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.28f, 0.85f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.42f, 0.72f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = Color.white;
        main.gravityModifier = -0.06f;
        main.maxParticles = 110;

        var emission = ps.emission;
        emission.enabled = false;
        emission.rateOverTime = 20f;
        emission.rateOverDistance = 6.2f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.26f;

        var limitVel = ps.limitVelocityOverLifetime;
        limitVel.enabled = true;
        limitVel.limit = 0.30f;
        limitVel.dampen = 0.26f;

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.34f;
        noise.frequency = 1.35f;
        noise.scrollSpeed = 0.95f;
        noise.damping = true;

        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-1.2f, 1.2f);

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0.0f, 0.44f, 0f, 1.9f),
            new Keyframe(0.38f, 1.00f, 0.55f, 0.55f),
            new Keyframe(1.0f, 1.48f, 0.20f, 0f)
        ));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(BuildRuntimeCloudMistGradient(0.54f));

        SetupRuntime2x2TextureSheet(ps);

        var psr = ps.GetComponent<ParticleSystemRenderer>();
        psr.renderMode = ParticleSystemRenderMode.Billboard;
        psr.sharedMaterial = smokeMat;
        psr.shadowCastingMode = ShadowCastingMode.Off;
        psr.receiveShadows = false;
        psr.sortingOrder = 6;
    }

    private static void SetupRuntimeLayer3WindSpeedStreaks(ParticleSystem ps, Material windStreakMat)
    {
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = 1.0f;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.28f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(7.0f, 11.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.022f, 0.044f);
        main.startColor = Color.white;
        main.gravityModifier = 0f;
        main.maxParticles = 80;

        var emission = ps.emission;
        emission.enabled = false;
        emission.rateOverTime = 24f;
        emission.rateOverDistance = 3.2f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(0.42f, 0.22f, 0.16f);

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.30f;
        noise.frequency = 1.35f;
        noise.scrollSpeed = 1.4f;
        noise.damping = true;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(BuildRuntimeWindStreakGradient(0.84f));

        var psr = ps.GetComponent<ParticleSystemRenderer>();
        psr.renderMode = ParticleSystemRenderMode.Stretch;
        psr.cameraVelocityScale = 0f;
        psr.velocityScale = 0.065f;
        psr.lengthScale = 7.4f;
        psr.sharedMaterial = windStreakMat;
        psr.shadowCastingMode = ShadowCastingMode.Off;
        psr.receiveShadows = false;
        psr.sortingOrder = 7;
    }

    private static void SetupRuntimeLayer3BSilkRibbons(ParticleSystem ps, Material silkRibbonMat)
    {
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = 1.0f;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.28f, 0.42f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(4.2f, 7.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.075f, 0.145f);
        main.startColor = Color.white;
        main.gravityModifier = -0.04f;
        main.maxParticles = 32;

        var emission = ps.emission;
        emission.enabled = false;
        emission.rateOverTime = 16f;
        emission.rateOverDistance = 2.2f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(0.40f, 0.20f, 0.14f);

        // เส้นริบบิ้นหมุนพริ้วโอบช่วงขาขณะกลิ้งหลบ
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.85f;
        noise.frequency = 1.55f;
        noise.scrollSpeed = 2.1f;
        noise.damping = true;
        noise.quality = ParticleSystemNoiseQuality.Medium;

        var trails = ps.trails;
        trails.enabled = true;
        trails.mode = ParticleSystemTrailMode.PerParticle;
        trails.ratio = 1.0f;
        trails.lifetime = new ParticleSystem.MinMaxCurve(0.58f);
        trails.minVertexDistance = 0.035f;
        trails.worldSpace = true;
        trails.dieWithParticles = false;
        trails.textureMode = ParticleSystemTrailTextureMode.Stretch;
        trails.sizeAffectsWidth = true;
        trails.sizeAffectsLifetime = false;
        trails.inheritParticleColor = false;
        trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0.0f, 0.18f),
            new Keyframe(0.22f, 1.00f),
            new Keyframe(0.65f, 0.50f),
            new Keyframe(1.0f, 0.00f)
        ));
        trails.colorOverLifetime = new ParticleSystem.MinMaxGradient(BuildRuntimeWindStreakGradient(0.92f));
        trails.colorOverTrail = new ParticleSystem.MinMaxGradient(BuildTrailFadeGradient());

        var psr = ps.GetComponent<ParticleSystemRenderer>();
        psr.renderMode = ParticleSystemRenderMode.None;
        psr.trailMaterial = silkRibbonMat;
        psr.shadowCastingMode = ShadowCastingMode.Off;
        psr.receiveShadows = false;
        psr.sortingOrder = 8;
    }

    private static void SetupRuntimeLayer4RecoverySmokeRing(ParticleSystem ps, Material smokeMat)
    {
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = 0.5f;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.36f, 0.54f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.3f, 2.7f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.36f, 0.60f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = Color.white;
        main.gravityModifier = -0.04f;
        main.maxParticles = 32;

        var emission = ps.emission;
        emission.enabled = false;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.30f;
        shape.radiusThickness = 1.0f;

        var limitVel = ps.limitVelocityOverLifetime;
        limitVel.enabled = true;
        limitVel.limit = 0.30f;
        limitVel.dampen = 0.28f;

        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-0.9f, 0.9f);

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0.0f, 0.48f),
            new Keyframe(0.40f, 1.05f),
            new Keyframe(1.0f, 1.42f)
        ));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(BuildRuntimeCloudMistGradient(0.46f));

        SetupRuntime2x2TextureSheet(ps);

        var psr = ps.GetComponent<ParticleSystemRenderer>();
        psr.renderMode = ParticleSystemRenderMode.Billboard;
        psr.sharedMaterial = smokeMat;
        psr.shadowCastingMode = ShadowCastingMode.Off;
        psr.receiveShadows = false;
        psr.sortingOrder = 5;
    }

    private static void SetupRuntime2x2TextureSheet(ParticleSystem ps)
    {
        var tsa = ps.textureSheetAnimation;
        tsa.enabled = true;
        tsa.mode = ParticleSystemAnimationMode.Grid;
        tsa.numTilesX = 2;
        tsa.numTilesY = 2;
        tsa.animation = ParticleSystemAnimationType.WholeSheet;
        tsa.timeMode = ParticleSystemAnimationTimeMode.Lifetime;
        tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, 0.99f);
        tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
        tsa.cycleCount = 1;
    }

    private static Gradient BuildRuntimeCloudMistGradient(float peakAlpha)
    {
        // หมอกหยกขาวจันทร์โปร่งสะอาด (Pearlescent Moonlight -> Soft Celadon Mist — ไม่หม่นดำทึบ)
        float clamped = Mathf.Clamp01(peakAlpha);
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(0.96f, 1.00f, 0.99f), 0.0f),
                new GradientColorKey(new Color(0.82f, 0.94f, 0.93f), 0.48f),
                new GradientColorKey(new Color(0.64f, 0.78f, 0.82f), 1.0f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0.0f, 0.0f),
                new GradientAlphaKey(clamped, 0.14f),
                new GradientAlphaKey(clamped * 0.56f, 0.56f),
                new GradientAlphaKey(0.0f, 1.0f)
            }
        );
        return grad;
    }

    private static Gradient BuildRuntimeWindStreakGradient(float peakAlpha)
    {
        float clamped = Mathf.Clamp01(peakAlpha);
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(Color.white, 0.0f),
                new GradientColorKey(new Color(0.74f, 0.98f, 0.92f), 0.42f),
                new GradientColorKey(new Color(0.48f, 0.86f, 0.90f), 1.0f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0.0f, 0.0f),
                new GradientAlphaKey(clamped, 0.14f),
                new GradientAlphaKey(clamped * 0.58f, 0.58f),
                new GradientAlphaKey(0.0f, 1.0f)
            }
        );
        return grad;
    }

    private static Gradient BuildTrailFadeGradient()
    {
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(Color.white, 0.0f),
                new GradientColorKey(new Color(0.78f, 0.98f, 0.94f), 0.52f),
                new GradientColorKey(new Color(0.55f, 0.88f, 0.92f), 1.0f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0.0f, 0.0f),
                new GradientAlphaKey(1.0f, 0.14f),
                new GradientAlphaKey(0.55f, 0.58f),
                new GradientAlphaKey(0.0f, 1.0f)
            }
        );
        return grad;
    }

    private static Texture2D GeneratePainterlyJadeMist2x2Texture()
    {
        int totalSize = 512;
        int cellSize = 256;
        Texture2D tex = new Texture2D(totalSize, totalSize, TextureFormat.RGBA32, false);
        tex.name = "T_WWM_PainterlyCloudMist_2x2";
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

                        // Domain Warping ให้ก้อนหมอกเป็นเกลียวพริ้วแบบเมฆพู่กันจีน (ไม่ใช่ก้อนกลมแข็ง)
                        float wx = (Mathf.PerlinNoise(seed.x + nx * 1.9f, seed.y + ny * 1.9f) - 0.5f) * 0.42f;
                        float wy = (Mathf.PerlinNoise(seed.x + 9.4f + nx * 1.9f, seed.y + 5.1f + ny * 1.9f) - 0.5f) * 0.42f;
                        float u = nx + wx;
                        float v = ny + wy;

                        float n1 = Mathf.PerlinNoise(seed.x + u * 2.1f, seed.y + v * 2.1f);
                        float n2 = Mathf.PerlinNoise(seed.x * 1.7f + u * 4.4f, seed.y * 1.7f + v * 4.4f) * 0.50f;
                        float n3 = Mathf.PerlinNoise(seed.x * 2.3f + u * 8.8f, seed.y * 2.3f + v * 8.8f) * 0.25f;
                        float fbm = (n1 + n2 + n3) / 1.75f;

                        float warpedR = Mathf.Sqrt(u * u + v * v);
                        float radialMask = 1f - Mathf.SmoothStep(0.14f, 0.86f, warpedR);
                        float hardBorderGuard = 1f - Mathf.SmoothStep(0.74f, 0.96f, r);

                        float billow = Mathf.SmoothStep(0.24f, 0.84f, fbm);
                        float alpha = Mathf.Clamp01(radialMask * hardBorderGuard * (0.26f + billow * 0.92f));
                        float shade = Mathf.Lerp(0.92f, 1.0f, billow);

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

    private static Texture2D GenerateSilkyRollWindStreakTexture()
    {
        int width = 256;
        int height = 64;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.name = "T_WWM_RollWindStreak";
        tex.wrapMode = TextureWrapMode.Clamp;
        Color[] pixels = new Color[width * height];

        for (int y = 0; y < height; y++)
        {
            float ny = ((y / (float)(height - 1)) * 2f) - 1f;
            for (int x = 0; x < width; x++)
            {
                float nx = x / (float)(width - 1);
                float curve = Mathf.Sin(nx * Mathf.PI * 1.5f) * 0.09f;
                float cy = ny - curve;

                float core = Mathf.Exp(-cy * cy * 34.0f) * 0.95f;
                float softVeil = Mathf.Exp(-cy * cy * 7.2f) * 0.46f;
                float headFade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(nx / 0.14f));
                float tailFade = Mathf.Pow(Mathf.Clamp01((1f - nx) / 0.86f), 1.65f);

                float alpha = Mathf.Clamp01((core + softVeil) * headFade * tailFade);
                pixels[y * width + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    private static Texture2D GenerateDodgeSilkRibbonTexture()
    {
        int width = 256;
        int height = 64;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.name = "T_WWM_DodgeSilkRibbon";
        tex.wrapMode = TextureWrapMode.Clamp;
        Color[] pixels = new Color[width * height];

        for (int y = 0; y < height; y++)
        {
            float ny = ((y / (float)(height - 1)) * 2f) - 1f;
            for (int x = 0; x < width; x++)
            {
                float nx = x / (float)(width - 1);
                float waveA = Mathf.Sin(nx * Mathf.PI * 3.0f) * 0.14f;
                float waveB = Mathf.Cos(nx * Mathf.PI * 2.5f) * 0.10f;

                float strandA = Mathf.Exp(-Mathf.Pow(ny - (0.24f + waveA), 2f) * 32.0f) * 0.90f;
                float strandB = Mathf.Exp(-Mathf.Pow(ny + (0.24f - waveB), 2f) * 36.0f) * 0.74f;
                float silkBody = Mathf.Exp(-ny * ny * 5.4f) * 0.44f;
                float edgeGuard = 1f - Mathf.SmoothStep(0.75f, 0.98f, Mathf.Abs(ny));

                float lengthFade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(nx / 0.10f)) *
                                   Mathf.Pow(Mathf.Clamp01((1f - nx) / 0.90f), 1.30f);

                float alpha = Mathf.Clamp01((strandA + strandB + silkBody) * edgeGuard * lengthFade);
                pixels[y * width + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    private static Material CreateRuntimeAlphaSmokeMaterial(string matName, Texture2D tex)
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Particles/Standard Unlit");

        Material mat = new Material(shader);
        mat.name = matName;
        mat.mainTexture = tex;
        mat.color = Color.white;
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
        mat.renderQueue = (int)RenderQueue.Transparent;
        return mat;
    }

    private static Material CreateRuntimeURPAdditiveMaterial(string matName, Texture2D tex, Color hdrTint)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        Material mat = new Material(shader);
        mat.name = matName;
        mat.mainTexture = tex;
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", hdrTint);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", hdrTint);
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 2f); // Additive
        if (mat.HasProperty("_ColorMode")) mat.SetFloat("_ColorMode", 0f);
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)CullMode.Off);
        if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
        if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)BlendMode.One);
        if (mat.HasProperty("_SrcBlendAlpha")) mat.SetInt("_SrcBlendAlpha", (int)BlendMode.One);
        if (mat.HasProperty("_DstBlendAlpha")) mat.SetInt("_DstBlendAlpha", (int)BlendMode.One);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.EnableKeyword("_BLENDMODE_ADD");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.DisableKeyword("_ALPHAMODULATE_ON");
        mat.renderQueue = (int)RenderQueue.Transparent;
        return mat;
    }

    private void OnGUI()
    {
        if (!showDebugHUD || !Application.isPlaying) return;

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
            ? "<color=#55FF77><b>กำลังกลิ้ง! (CLOUD-STEP DODGE ON)</b></color>"
            : "<color=#66E0FF><b>พร้อมใช้งาน (READY)</b></color>";

        string hudText =
            "<b>[AAA Cloud-Step Mist & Silky Wind Dodge VFX]</b>\n" +
            "• <b>สถานะกลิ้งหลบ:</b> " + statusBadge + "  <i>(Duration: " + rollDuration.ToString("F2") + "s)</i>\n" +
            "• <b>ปุ่มทดสอบ:</b> กดปุ่ม <b>[Space]</b> หรือ <b>[C]</b> หรือ <b>[Left Ctrl]</b>\n" +
            "• <b>ทริกเกอร์ล่าสุด:</b> " + lastTriggerReason + " (รวม: " + totalSmokeRollsTriggered + " ครั้ง)";

        GUI.Label(new Rect(24f, boxY + 6f, 425f, boxHeight - 12f), hudText, labelStyle);
    }

    private static void DestroyChildIfExists(Transform parent, string childName)
    {
        if (parent == null) return;
        Transform t = parent.Find(childName);
        if (t != null) Destroy(t.gameObject);
    }

    private static Transform FindDeepChild(Transform parent, string targetName)
    {
        if (parent == null || string.IsNullOrEmpty(targetName)) return null;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name == targetName) return child;

            Transform found = FindDeepChild(child, targetName);
            if (found != null) return found;
        }

        return null;
    }

    private void OnDestroy()
    {
        if (runtimeSmokeMat != null) Destroy(runtimeSmokeMat);
        if (runtimeWindStreakMat != null) Destroy(runtimeWindStreakMat);
        if (runtimeSilkRibbonMat != null) Destroy(runtimeSilkRibbonMat);

        if (runtimeSmokeAtlasTex != null) Destroy(runtimeSmokeAtlasTex);
        if (runtimeWindStreakTex != null) Destroy(runtimeWindStreakTex);
        if (runtimeSilkRibbonTex != null) Destroy(runtimeSilkRibbonTex);
    }
}
