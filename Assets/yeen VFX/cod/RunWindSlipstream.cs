using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// ระบบเอฟเฟกต์ "สายลมแหวกอากาศผ่านลำตัวและเส้นลมติดข้อเท้าซ้าย-ขวา" ระดับ AAA สำหรับ Unity 6 (URP)
/// ออกแบบสไตล์ Genshin Impact / Wuthering Waves / Naraka: Bladepoint:
/// - Layer 1: เส้นสายลมเรียวคมโปร่งแสงโค้งพริ้วตามแรงอากาศ (Silky Aerodynamic Speed Lines + Organic Noise)
/// - Layer 2: วงแหวนแหวกอากาศจังหวะออกตัววิ่ง + เข็มลมพุ่งถอยหลัง (Crisp Sprint Launch Air-Ring & Gust)
/// - Layer 3: ริบบิ้นสายลมไหมคู่พริ้วติดข้อเท้าซ้าย-ขวา (Graceful Dual-Filament Ankle Silk Ribbons)
/// - Layer 4: ละอองลมและประกายอากาศลอยหมุนรอบตัว (Delicate Wind Motes & Ground Air Draft)
/// ทำงานเฉพาะตอนวิ่ง (กด Shift + เคลื่อนที่ หรืออนิเมชั่นเข้าท่าวิ่ง) เดินปกติจะไม่แสดงเอฟเฟกต์
/// </summary>
[DisallowMultipleComponent]
public class RunWindSlipstream : MonoBehaviour
{
    [Header("=== 1. ระบบสลับสีสายลมแบบเรียลไทม์ (Real-Time Wind Color Palette) ===")]
    [Tooltip("ติ๊กถูก = สีเขียวอ่อนสายลม (Light Mint Wind) / เอาติ๊กออก = สีขาวใสสายลมบริสุทธิ์ (Pure Aerial White)")]
    public bool useLightGreenWind = false;

    [Tooltip("สีเขียวอ่อนสายลม (Light Mint Wind)")]
    public Color lightGreenWindColor = new Color(0.62f, 1.0f, 0.86f, 0.72f);

    [Tooltip("สีขาวใสสายลมบริสุทธิ์ (Pure Aerial White)")]
    public Color pureWhiteWindColor = new Color(0.93f, 0.98f, 1.0f, 0.68f);

    [Header("=== 2. การตรวจจับอนิเมชั่นวิ่ง & ปุ่ม Shift (Animator BlendTree & Shift Detection) ===")]
    [Tooltip("เปิดใช้งานอัตโนมัติทันทีเมื่ออนิเมชั่นของตัวละครเข้าสู่ท่าวิ่ง (BlendTree MoveX/MoveY >= 1.55)")]
    public bool triggerOnRunAnimation = true;

    [Tooltip("ค่าเกณฑ์ BlendTree (MoveX, MoveY) ที่ถือว่าเป็นท่าวิ่ง (เดินตรง = 1.0, เดินเฉียง = 1.414, วิ่ง = 2.0)")]
    [Range(1.45f, 1.95f)]
    public float blendTreeRunThreshold = 1.55f;

    [Tooltip("ติ๊กถูก = ต้องกด Shift ค้างร่วมกับตัวละครขยับวิ่งหรือเล่นท่าวิ่งเอฟเฟกต์ถึงจะทำงาน (เดินปกติไม่ขึ้น)")]
    public bool requireMovementWithShift = true;

    [Tooltip("ความเร็วขั้นต่ำ (m/s) ที่ถือว่าตัวละครกำลังขยับใน LateUpdate")]
    [Range(0.05f, 4.0f)]
    public float minMoveSpeedThreshold = 0.25f;

    [Tooltip("ความเร็วขั้นต่ำอ้างอิง (m/s)")]
    [Range(3.8f, 12.0f)]
    public float worldRunSpeedThreshold = 4.2f;

    [Tooltip("เวลาหน่วงสั้นๆ ก่อนหยุดปล่อยสายลมเมื่อหยุดวิ่ง (วินาที)")]
    [Range(0.0f, 0.4f)]
    public float stopGraceTime = 0.14f;

    [Header("=== 3. ปรับแต่งความสวยงามและความหนาแน่นของสายลม (AAA Wind Tuning) ===")]
    [Tooltip("ความหนาแน่นของเส้นสายลมแหวกอากาศผ่านลำตัว (ค่าแนะนำ 45 - 65 เส้น/วินาที ให้ความรู้สึกพริ้วคมไม่รกตา)")]
    [Range(20f, 140f)]
    public float windDensity = 54f;

    [Tooltip("ความโปร่งใสของสายลม (ค่าแนะนำ 0.62 - 0.78 เพื่อความใสพริ้วเหมือนสายลมจริง)")]
    [Range(0.15f, 1.0f)]
    public float windOpacity = 0.68f;

    [Header("=== 4. ชื่อกระดูกข้อเท้าซ้าย-ขวา (Biped Foot Bones) ===")]
    public string leftFootBoneName = "Bip001 L Foot";
    public string rightFootBoneName = "Bip001 R Foot";

    [Header("=== 5. สถานะการทำงานปัจจุบัน (Read-Only) ===")]
    [SerializeField] private bool isWindActive = false;
    [SerializeField] private bool isPlayingRunAnimation = false;
    [SerializeField] private bool isShiftHeld = false;
    [SerializeField] private float currentBlendTreeMagnitude = 0f;
    [SerializeField] private float currentHorizontalSpeed = 0f;
    [SerializeField] private string activeTriggerSource = "Idle";

    // Runtime Generated Textures & Materials
    private Mesh runtimeQuadMesh;
    private Texture2D runtimeWindStreakTex;
    private Texture2D runtimeFootRibbonTex;
    private Texture2D runtimeAirRingTex;
    private Texture2D runtimeWindMoteTex;

    private Material runtimeStreakMat;
    private Material runtimeRibbonMat;
    private Material runtimeAirRingMat;
    private Material runtimeMoteMat;

    // 4-Layer AAA Wind VFX
    private ParticleSystem bodyWindPS;
    private ParticleSystem launchAirRingPS;
    private ParticleSystem launchGustNeedlesPS;
    private ParticleSystem windMotesPS;
    private readonly List<ParticleSystem> existingChildParticles = new List<ParticleSystem>();
    private readonly List<TrailRenderer> footTrailRenderers = new List<TrailRenderer>();

    // Cached Components
    private Animator cachedAnimator;
    private CharacterController cachedCharacterController;
    private Rigidbody cachedRigidbody;
    private NetworkCharacter cachedNetCharacter;
    private PlayerCapsule cachedPlayerCapsule;

    private Vector3 previousWorldPos;
    private bool hasInitializedPos = false;
    private float graceTimer = 0f;
    private float launchRingCooldown = 0f;

    private bool hasMoveXParam = false;
    private bool hasMoveYParam = false;
    private bool hasSpeedParam = false;
    private bool hasIsSprintingParam = false;
    private bool hasMovingParam = false;
    private bool animatorParamsCached = false;

    // Change Detection
    private bool lastUseLightGreen = false;
    private Color lastLightGreenColor;
    private Color lastPureWhiteColor;
    private float lastWindDensity = -1f;
    private float lastWindOpacity = -1f;

    private void Awake()
    {
        ResolveCharacterComponents();
        DisableLegacyDuplicateScripts();

        GenerateRuntimeWindAssets();

        // ปลุกเอฟเฟกต์ Sprint Wind เดิม (ถ้ามีวางไว้ใน Scene)
        DiscoverAndWakeUpExistingChildVFX();

        // สร้างโครงสร้างเอฟเฟกต์สายลมวิ่งระดับ AAA ทั้ง 4 เลเยอร์
        BuildLayer1SilkyBodySlipstream();
        BuildLayer2SprintLaunchAirRingAndGust();
        BuildLayer3DualAnkleSilkRibbons();
        BuildLayer4WindMotesAndAirDraft();

        previousWorldPos = transform.position;
        hasInitializedPos = true;
    }

    private void Start()
    {
        CacheAnimatorParameterList();

        StopLoopEmitter(bodyWindPS);
        StopLoopEmitter(windMotesPS);

        if (launchAirRingPS != null)
        {
            ParticleSystem.EmissionModule em = launchAirRingPS.emission;
            em.enabled = false;
            launchAirRingPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        if (launchGustNeedlesPS != null)
        {
            ParticleSystem.EmissionModule em = launchGustNeedlesPS.emission;
            em.enabled = false;
            launchGustNeedlesPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        for (int i = 0; i < existingChildParticles.Count; i++)
        {
            StopLoopEmitter(existingChildParticles[i]);
        }

        for (int i = 0; i < footTrailRenderers.Count; i++)
        {
            if (footTrailRenderers[i] != null)
            {
                footTrailRenderers[i].emitting = false;
                footTrailRenderers[i].Clear();
            }
        }

        ApplyRealTimeColorAndVisualUpdates(true);
    }

    private static void StopLoopEmitter(ParticleSystem ps)
    {
        if (ps == null) return;
        ParticleSystem.EmissionModule em = ps.emission;
        em.enabled = false;
        if (!ps.isPlaying) ps.Play(true);
    }

    private void ResolveCharacterComponents()
    {
        cachedAnimator = GetComponentInChildren<Animator>(true);
        if (cachedAnimator == null) cachedAnimator = GetComponentInParent<Animator>();

        cachedCharacterController = GetComponentInParent<CharacterController>();
        if (cachedCharacterController == null) cachedCharacterController = GetComponentInChildren<CharacterController>(true);

        cachedRigidbody = GetComponentInParent<Rigidbody>();
        if (cachedRigidbody == null) cachedRigidbody = GetComponentInChildren<Rigidbody>(true);

        cachedNetCharacter = GetComponentInParent<NetworkCharacter>();
        if (cachedNetCharacter == null) cachedNetCharacter = GetComponentInChildren<NetworkCharacter>(true);

        cachedPlayerCapsule = GetComponentInParent<PlayerCapsule>();
        if (cachedPlayerCapsule == null) cachedPlayerCapsule = GetComponentInChildren<PlayerCapsule>(true);
    }

    private void CacheAnimatorParameterList()
    {
        if (cachedAnimator == null)
        {
            cachedAnimator = GetComponentInChildren<Animator>(true);
            if (cachedAnimator == null) cachedAnimator = GetComponentInParent<Animator>();
        }

        if (cachedAnimator == null || cachedAnimator.runtimeAnimatorController == null)
        {
            return;
        }

        hasMoveXParam = false;
        hasMoveYParam = false;
        hasSpeedParam = false;
        hasIsSprintingParam = false;
        hasMovingParam = false;

        AnimatorControllerParameter[] parameters = cachedAnimator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            string pName = parameters[i].name;
            if (pName == "MoveX") hasMoveXParam = true;
            else if (pName == "MoveY") hasMoveYParam = true;
            else if (pName == "Speed") hasSpeedParam = true;
            else if (pName == "IsSprinting") hasIsSprintingParam = true;
            else if (pName == "Moving") hasMovingParam = true;
        }

        animatorParamsCached = true;
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0.00001f)
        {
            return;
        }

        if (launchRingCooldown > 0f)
        {
            launchRingCooldown -= dt;
        }

        ApplyRealTimeColorAndVisualUpdates(false);
        UpdateRealHorizontalSpeed(dt);

        bool movementKeyPressed;
        isShiftHeld = DetectUniversalShiftAndMovementInput(out movementKeyPressed);
        bool isMovingNow = movementKeyPressed || (currentHorizontalSpeed >= minMoveSpeedThreshold);

        string animReason;
        isPlayingRunAnimation = EvaluateRunAnimationAndNetworkState(out animReason);

        // เดินปกติ (Walk) ต้องไม่ขึ้นเอฟเฟกต์เด็ดขาด จะขึ้นเฉพาะตอนกด Shift วิ่ง หรือ Animator อยู่ในท่าวิ่งจริงเท่านั้น
        bool shouldActivateWind = false;

        if (isShiftHeld && (requireMovementWithShift ? isMovingNow : true))
        {
            shouldActivateWind = true;
            activeTriggerSource = "Shift Key + Running";
        }
        else if (triggerOnRunAnimation && isPlayingRunAnimation)
        {
            shouldActivateWind = true;
            activeTriggerSource = animReason;
        }

        if (shouldActivateWind)
        {
            graceTimer = stopGraceTime;
            if (!isWindActive)
            {
                SetWindEmissionState(true);
            }
            else
            {
                KeepActiveWindAlive();
            }
        }
        else
        {
            if (graceTimer > 0f)
            {
                graceTimer -= dt;
            }
            else if (isWindActive)
            {
                activeTriggerSource = "Idle / Walk";
                SetWindEmissionState(false);
            }
        }
    }

    private bool EvaluateRunAnimationAndNetworkState(out string reason)
    {
        reason = "None";

        if (cachedNetCharacter != null && cachedNetCharacter.IsSpawned)
        {
            if (cachedNetCharacter.AnimState == NetworkCharacter.STATE_RUN)
            {
                reason = "NetworkCharacter STATE_RUN";
                return true;
            }
        }

        if (cachedPlayerCapsule != null)
        {
            if (cachedPlayerCapsule.EffectiveSprinting && cachedPlayerCapsule.EffectiveMove.sqrMagnitude > 0.01f)
            {
                reason = "PlayerCapsule EffectiveSprinting";
                return true;
            }
        }

        if (cachedAnimator == null)
        {
            cachedAnimator = GetComponentInChildren<Animator>(true);
        }

        if (cachedAnimator != null && cachedAnimator.isActiveAndEnabled && cachedAnimator.runtimeAnimatorController != null)
        {
            if (!animatorParamsCached)
            {
                CacheAnimatorParameterList();
            }

            float effectiveThreshold = Mathf.Max(1.55f, blendTreeRunThreshold);
            if (hasMoveXParam && hasMoveYParam)
            {
                float mx = cachedAnimator.GetFloat("MoveX");
                float my = cachedAnimator.GetFloat("MoveY");
                currentBlendTreeMagnitude = Mathf.Sqrt(mx * mx + my * my);

                if (currentBlendTreeMagnitude >= effectiveThreshold)
                {
                    reason = "Animator BlendTree Run (MoveXY=" + currentBlendTreeMagnitude.ToString("F2") + ")";
                    return true;
                }
            }

            if (hasIsSprintingParam && cachedAnimator.GetBool("IsSprinting"))
            {
                bool moving = !hasMovingParam || cachedAnimator.GetBool("Moving") || currentHorizontalSpeed > 0.25f;
                if (moving)
                {
                    reason = "Animator IsSprinting = true";
                    return true;
                }
            }

            if (hasSpeedParam)
            {
                float spd = cachedAnimator.GetFloat("Speed");
                if (spd >= effectiveThreshold)
                {
                    reason = "Animator Speed (" + spd.ToString("F2") + ")";
                    return true;
                }
            }
        }

        return false;
    }

    private void DiscoverAndWakeUpExistingChildVFX()
    {
        existingChildParticles.Clear();

        Transform[] allTransforms = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < allTransforms.Length; i++)
        {
            Transform t = allTransforms[i];
            if (t == null || t == transform) continue;

            string n = t.name;
            if (n.IndexOf("Sword", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Slash", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Weapon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Guard", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Parry", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Smoke", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("VFX_Runtime_", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                continue;
            }

            if (n.IndexOf("Sprint", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Slipstream", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (!t.gameObject.activeSelf)
                {
                    t.gameObject.SetActive(true);
                }

                ParticleSystem[] psArray = t.GetComponentsInChildren<ParticleSystem>(true);
                for (int p = 0; p < psArray.Length; p++)
                {
                    ParticleSystem ps = psArray[p];
                    if (ps != null && !existingChildParticles.Contains(ps))
                    {
                        FixParticleVelocityCurveMode(ps);
                        existingChildParticles.Add(ps);
                    }
                }

                TrailRenderer[] trArray = t.GetComponentsInChildren<TrailRenderer>(true);
                for (int r = 0; r < trArray.Length; r++)
                {
                    TrailRenderer tr = trArray[r];
                    if (tr != null && !footTrailRenderers.Contains(tr))
                    {
                        footTrailRenderers.Add(tr);
                    }
                }
            }
        }
    }

    private static void FixParticleVelocityCurveMode(ParticleSystem ps)
    {
        ParticleSystem.VelocityOverLifetimeModule vel = ps.velocityOverLifetime;
        if (!vel.enabled) return;

        ParticleSystem.MinMaxCurve cX = new ParticleSystem.MinMaxCurve(vel.x.constant);
        cX.mode = ParticleSystemCurveMode.Constant;
        ParticleSystem.MinMaxCurve cY = new ParticleSystem.MinMaxCurve(vel.y.constant);
        cY.mode = ParticleSystemCurveMode.Constant;
        ParticleSystem.MinMaxCurve cZ = new ParticleSystem.MinMaxCurve(vel.z.constant);
        cZ.mode = ParticleSystemCurveMode.Constant;

        vel.x = cX;
        vel.y = cY;
        vel.z = cZ;

        ParticleSystem.MinMaxCurve zero = new ParticleSystem.MinMaxCurve(0f);
        zero.mode = ParticleSystemCurveMode.Constant;
        vel.orbitalX = zero;
        vel.orbitalY = zero;
        vel.orbitalZ = zero;
        vel.orbitalOffsetX = zero;
        vel.orbitalOffsetY = zero;
        vel.orbitalOffsetZ = zero;
        vel.radial = zero;

        ParticleSystem.MinMaxCurve one = new ParticleSystem.MinMaxCurve(1f);
        one.mode = ParticleSystemCurveMode.Constant;
        vel.speedModifier = one;
    }

    private void SetWindEmissionState(bool active)
    {
        isWindActive = active;

        SetLoopEmitterActive(bodyWindPS, active);
        SetLoopEmitterActive(windMotesPS, active);

        // Layer 2: จังหวะเริ่มออกตัววิ่ง ปล่อยวงแหวนแหวกอากาศ + เข็มลมพุ่งถอยหลัง 1 ครั้ง
        if (active && launchRingCooldown <= 0f)
        {
            if (launchAirRingPS != null)
            {
                launchAirRingPS.Play(true);
                launchAirRingPS.Emit(1);
            }
            if (launchGustNeedlesPS != null)
            {
                launchGustNeedlesPS.Play(true);
                launchGustNeedlesPS.Emit(12);
            }
            launchRingCooldown = 0.35f;
        }

        for (int i = 0; i < existingChildParticles.Count; i++)
        {
            SetLoopEmitterActive(existingChildParticles[i], active);
        }

        for (int i = 0; i < footTrailRenderers.Count; i++)
        {
            TrailRenderer tr = footTrailRenderers[i];
            if (tr != null)
            {
                tr.emitting = active;
            }
        }
    }

    private static void SetLoopEmitterActive(ParticleSystem ps, bool active)
    {
        if (ps == null) return;
        if (!ps.isPlaying) ps.Play(true);
        ParticleSystem.EmissionModule em = ps.emission;
        em.enabled = active;
    }

    private void KeepActiveWindAlive()
    {
        SetLoopEmitterActive(bodyWindPS, true);
        SetLoopEmitterActive(windMotesPS, true);

        for (int i = 0; i < existingChildParticles.Count; i++)
        {
            SetLoopEmitterActive(existingChildParticles[i], true);
        }

        for (int i = 0; i < footTrailRenderers.Count; i++)
        {
            TrailRenderer tr = footTrailRenderers[i];
            if (tr != null && !tr.emitting)
            {
                tr.emitting = true;
            }
        }
    }

    private bool DetectUniversalShiftAndMovementInput(out bool movementKeyPressed)
    {
        bool shiftFromNewInput = false;
        bool moveFromNewInput = false;

        if (Keyboard.current != null)
        {
            shiftFromNewInput = Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed;
            moveFromNewInput =
                Keyboard.current.wKey.isPressed ||
                Keyboard.current.aKey.isPressed ||
                Keyboard.current.sKey.isPressed ||
                Keyboard.current.dKey.isPressed ||
                Keyboard.current.upArrowKey.isPressed ||
                Keyboard.current.downArrowKey.isPressed ||
                Keyboard.current.leftArrowKey.isPressed ||
                Keyboard.current.rightArrowKey.isPressed;
        }

        bool shiftFromLegacyInput = false;
        bool moveFromLegacyInput = false;

        try
        {
            shiftFromLegacyInput = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            moveFromLegacyInput =
                Input.GetKey(KeyCode.W) ||
                Input.GetKey(KeyCode.A) ||
                Input.GetKey(KeyCode.S) ||
                Input.GetKey(KeyCode.D) ||
                Input.GetKey(KeyCode.UpArrow) ||
                Input.GetKey(KeyCode.DownArrow) ||
                Input.GetKey(KeyCode.LeftArrow) ||
                Input.GetKey(KeyCode.RightArrow) ||
                Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.1f ||
                Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.1f;
        }
        catch
        {
            // Ignore if legacy input is disabled
        }

        movementKeyPressed = moveFromNewInput || moveFromLegacyInput;
        return shiftFromNewInput || shiftFromLegacyInput;
    }

    private void UpdateRealHorizontalSpeed(float dt)
    {
        if (!hasInitializedPos)
        {
            previousWorldPos = transform.position;
            hasInitializedPos = true;
        }

        Vector3 currentPos = transform.position;
        Vector3 horizontalDelta = new Vector3(currentPos.x - previousWorldPos.x, 0f, currentPos.z - previousWorldPos.z);
        float deltaSpeed = horizontalDelta.magnitude / dt;
        previousWorldPos = currentPos;

        float ccSpeed = 0f;
        if (cachedCharacterController != null)
        {
            Vector3 v = cachedCharacterController.velocity;
            ccSpeed = new Vector3(v.x, 0f, v.z).magnitude;
        }

        float rbSpeed = 0f;
        if (cachedRigidbody != null && !cachedRigidbody.isKinematic)
        {
            Vector3 v = cachedRigidbody.linearVelocity;
            rbSpeed = new Vector3(v.x, 0f, v.z).magnitude;
        }

        float measuredSpeed = Mathf.Max(deltaSpeed, Mathf.Max(ccSpeed, rbSpeed));
        currentHorizontalSpeed = Mathf.Lerp(currentHorizontalSpeed, measuredSpeed, Mathf.Clamp01(dt * 20f));
    }

    // ================================================================================================
    // สร้าง Procedural Textures & Materials ระดับ AAA (เส้นสายลมเรียวคมพริ้วไหว ไม่แข็งทื่อเป็นแท่ง)
    // ================================================================================================
    private void GenerateRuntimeWindAssets()
    {
        GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        runtimeQuadMesh = q.GetComponent<MeshFilter>().sharedMesh;
        Destroy(q);

        runtimeWindStreakTex = CreateSilkyWindStreakTexture();
        runtimeFootRibbonTex = CreateDualFilamentFootRibbonTexture();
        runtimeAirRingTex = CreateAerodynamicAirRingTexture();
        runtimeWindMoteTex = CreateSoftWindMoteTexture();

        runtimeStreakMat = CreateURPWindMaterial("MAT_AAA_WindStreak_Silky", runtimeWindStreakTex);
        runtimeRibbonMat = CreateURPWindMaterial("MAT_AAA_FootRibbon_Silk", runtimeFootRibbonTex);
        runtimeAirRingMat = CreateURPWindMaterial("MAT_AAA_SprintAirRing", runtimeAirRingTex);
        runtimeMoteMat = CreateURPWindMaterial("MAT_AAA_WindMotes", runtimeWindMoteTex);
    }

    private static Texture2D CreateSilkyWindStreakTexture()
    {
        int width = 256;
        int height = 64;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.name = "T_AAA_SilkyWindStreak";
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            float ny = ((y / (float)(height - 1)) * 2f) - 1f;
            // แกนกลางเรียวคม + รัศมีลมโปร่งบาง + เส้นใยลมรองบางๆ ด้านข้าง
            float razorCore = Mathf.Exp(-ny * ny * 34.0f) * 0.95f;
            float softVeil = Mathf.Exp(-ny * ny * 7.5f) * 0.42f;
            float twinWhisper = Mathf.Exp(-Mathf.Pow(Mathf.Abs(ny) - 0.36f, 2f) * 42.0f) * 0.28f;

            for (int x = 0; x < width; x++)
            {
                float nx = x / (float)(width - 1);
                float headNeedle = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(nx / 0.16f));
                float tailDissolve = Mathf.Pow(Mathf.Clamp01((1f - nx) / 0.84f), 1.85f);

                float alpha = Mathf.Clamp01((razorCore + softVeil + twinWhisper) * headNeedle * tailDissolve);
                pixels[y * width + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply(false, false);
        return tex;
    }

    private static Texture2D CreateDualFilamentFootRibbonTexture()
    {
        int width = 256;
        int height = 64;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.name = "T_AAA_DualSilkFootRibbon";
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            float ny = ((y / (float)(height - 1)) * 2f) - 1f;
            for (int x = 0; x < width; x++)
            {
                float nx = x / (float)(width - 1);
                // เส้นไหมลมคู่ที่พลิ้วคลื่นเล็กน้อยตามความยาวริบบิ้น
                float wave = Mathf.Sin(nx * Mathf.PI * 3.0f) * 0.12f;
                float strandA = Mathf.Exp(-Mathf.Pow(ny - (0.26f + wave), 2f) * 32.0f) * 0.82f;
                float strandB = Mathf.Exp(-Mathf.Pow(ny + (0.26f - wave * 0.7f), 2f) * 36.0f) * 0.65f;
                float silkyBody = Mathf.Exp(-ny * ny * 5.2f) * 0.34f;

                float lengthFade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(nx / 0.12f)) *
                                   Mathf.Pow(Mathf.Clamp01((1f - nx) / 0.88f), 1.45f);
                float alpha = Mathf.Clamp01((strandA + strandB + silkyBody) * lengthFade);
                pixels[y * width + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply(false, false);
        return tex;
    }

    private static Texture2D CreateAerodynamicAirRingTexture()
    {
        int size = 256;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.name = "T_AAA_SprintAirRing";
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            float ny = ((y / (float)(size - 1)) * 2f) - 1f;
            for (int x = 0; x < size; x++)
            {
                float nx = ((x / (float)(size - 1)) * 2f) - 1f;
                float r = Mathf.Sqrt(nx * nx + ny * ny);
                float angle = Mathf.Atan2(ny, nx);

                // วงแหวนแหวกอากาศบางคม + รอยตัดสายลมตามแนวรอบวงให้ดูพริ้วไม่เป็นวงกลมแข็ง
                float windBreak = 0.65f + 0.35f * Mathf.Sin(angle * 5f + 0.7f) * Mathf.Cos(angle * 3f);
                float mainRing = Mathf.Exp(-Mathf.Pow(r - 0.74f, 2f) / (2f * 0.036f * 0.036f));
                float innerTrail = Mathf.Exp(-Mathf.Pow(r - 0.62f, 2f) / (2f * 0.055f * 0.055f)) * 0.38f;
                float mask = 1f - Mathf.SmoothStep(0.84f, 0.96f, r);

                float alpha = Mathf.Clamp01((mainRing + innerTrail) * windBreak * mask);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply(false, false);
        return tex;
    }

    private static Texture2D CreateSoftWindMoteTexture()
    {
        int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.name = "T_AAA_SoftWindMote";
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            float ny = ((y / (float)(size - 1)) * 2f) - 1f;
            for (int x = 0; x < size; x++)
            {
                float nx = ((x / (float)(size - 1)) * 2f) - 1f;
                float r = Mathf.Sqrt(nx * nx + ny * ny);
                float glow = Mathf.Exp(-r * r * 6.5f);
                float star = Mathf.Exp(-(Mathf.Abs(nx) + Mathf.Abs(ny)) * 4.5f) * 0.65f;
                float mask = 1f - Mathf.SmoothStep(0.75f, 1.0f, r);
                pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01((glow + star) * mask));
            }
        }

        tex.SetPixels(pixels);
        tex.Apply(false, false);
        return tex;
    }

    private static Material CreateURPWindMaterial(string matName, Texture2D tex)
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Particles/Standard Unlit");

        Material mat = new Material(shader);
        mat.name = matName;
        mat.mainTexture = tex;

        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)CullMode.Off);
        if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);

        mat.renderQueue = (int)RenderQueue.Transparent;
        return mat;
    }

    // ================================================================================================
    // LAYER 1: Sleek Aerodynamic Body Wind Slipstream (เส้นลมเรียวคมพริ้วโอบรอบตัว + Noise Turbulence)
    // ================================================================================================
    private void BuildLayer1SilkyBodySlipstream()
    {
        DestroyChildIfExists(transform, "VFX_Runtime_BodyWindSlipstream");

        GameObject slipObj = new GameObject("VFX_Runtime_BodyWindSlipstream");
        slipObj.transform.SetParent(transform, false);
        slipObj.transform.localPosition = new Vector3(0f, 1.02f, 1.35f);
        slipObj.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        slipObj.transform.localScale = Vector3.one;

        bodyWindPS = slipObj.AddComponent<ParticleSystem>();
        bodyWindPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        Color activeColor = GetCurrentSelectedWindColor();

        ParticleSystem.MainModule main = bodyWindPS.main;
        main.duration = 1.0f;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.20f, 0.30f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(12f, 18f);
        // ขนาดเรียวคมสง่างามแบบเกม AAA ไม่หนาเป็นแท่งทึบ
        main.startSize = new ParticleSystem.MinMaxCurve(0.028f, 0.062f);
        main.startColor = activeColor;
        main.gravityModifier = 0f;
        main.maxParticles = 240;

        ParticleSystem.EmissionModule emission = bodyWindPS.emission;
        emission.enabled = false;
        emission.rateOverTime = new ParticleSystem.MinMaxCurve(windDensity);
        emission.rateOverDistance = new ParticleSystem.MinMaxCurve(2.5f);

        ParticleSystem.ShapeModule shape = bodyWindPS.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 10f;
        shape.radius = 0.54f;
        shape.radiusThickness = 0.92f; // ล้อมโอบขอบลำตัว แก้ม และไหล่โดยไม่บังกลางอก

        ParticleSystem.VelocityOverLifetimeModule vel = bodyWindPS.velocityOverLifetime;
        vel.enabled = false;

        // เพิ่มความพริ้วไหวของกระแสลม (Organic Air Turbulence) ให้เส้นลมโค้งลู่ไปตามอากาศจริง
        ParticleSystem.NoiseModule noise = bodyWindPS.noise;
        noise.enabled = true;
        noise.strength = new ParticleSystem.MinMaxCurve(0.45f);
        noise.frequency = 2.0f;
        noise.scrollSpeed = 1.6f;
        noise.damping = true;
        noise.quality = ParticleSystemNoiseQuality.Medium;

        ParticleSystem.ColorOverLifetimeModule col = bodyWindPS.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(BuildWindColorGradient(activeColor, 1.0f));

        ParticleSystem.SizeOverLifetimeModule sol = bodyWindPS.sizeOverLifetime;
        sol.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve(
            new Keyframe(0.0f, 0.25f),
            new Keyframe(0.22f, 1.0f),
            new Keyframe(0.75f, 0.72f),
            new Keyframe(1.0f, 0.05f)
        );
        sol.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        ParticleSystemRenderer psRenderer = slipObj.GetComponent<ParticleSystemRenderer>();
        psRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        psRenderer.cameraVelocityScale = 0f;
        psRenderer.velocityScale = 0.08f;
        psRenderer.lengthScale = 8.5f;
        psRenderer.sharedMaterial = runtimeStreakMat;
        psRenderer.shadowCastingMode = ShadowCastingMode.Off;
        psRenderer.receiveShadows = false;
        psRenderer.sortingOrder = 5;
    }

    // ================================================================================================
    // LAYER 2: Crisp Sprint Launch Air-Ring & Gust Needles (วงแหวนแหวกอากาศ + เข็มลมตอนเริ่มออกวิ่ง)
    // ================================================================================================
    private void BuildLayer2SprintLaunchAirRingAndGust()
    {
        DestroyChildIfExists(transform, "VFX_Runtime_DashStartBurst");
        DestroyChildIfExists(transform, "VFX_Runtime_SprintStartAirRing");
        DestroyChildIfExists(transform, "VFX_Runtime_SprintStartGust");

        Color activeColor = GetCurrentSelectedWindColor();

        // 2A: วงแหวนแหวกอากาศรอบเอวตอนดีดตัวออกวิ่ง
        GameObject ringObj = new GameObject("VFX_Runtime_SprintStartAirRing");
        ringObj.transform.SetParent(transform, false);
        ringObj.transform.localPosition = new Vector3(0f, 0.95f, 0.15f);
        ringObj.transform.localRotation = Quaternion.identity;
        ringObj.transform.localScale = Vector3.one;

        launchAirRingPS = ringObj.AddComponent<ParticleSystem>();
        launchAirRingPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule rMain = launchAirRingPS.main;
        rMain.duration = 0.35f;
        rMain.loop = false;
        rMain.playOnAwake = false;
        rMain.simulationSpace = ParticleSystemSimulationSpace.World;
        rMain.startLifetime = new ParticleSystem.MinMaxCurve(0.26f);
        rMain.startSpeed = new ParticleSystem.MinMaxCurve(0f);
        rMain.startSize = new ParticleSystem.MinMaxCurve(2.15f);
        rMain.startColor = activeColor;
        rMain.gravityModifier = 0f;
        rMain.maxParticles = 6;

        ParticleSystem.EmissionModule rEm = launchAirRingPS.emission;
        rEm.enabled = false;

        ParticleSystem.ShapeModule rShape = launchAirRingPS.shape;
        rShape.enabled = false;

        ParticleSystem.SizeOverLifetimeModule rSol = launchAirRingPS.sizeOverLifetime;
        rSol.enabled = true;
        rSol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0.0f, 0.28f, 0f, 4.8f),
            new Keyframe(0.45f, 1.05f, 0.9f, 0.6f),
            new Keyframe(1.0f, 1.38f, 0.2f, 0f)
        ));

        ParticleSystem.ColorOverLifetimeModule rCol = launchAirRingPS.colorOverLifetime;
        rCol.enabled = true;
        rCol.color = new ParticleSystem.MinMaxGradient(BuildWindColorGradient(activeColor, 0.92f));

        ParticleSystemRenderer rPsr = ringObj.GetComponent<ParticleSystemRenderer>();
        rPsr.renderMode = ParticleSystemRenderMode.Mesh;
        rPsr.mesh = runtimeQuadMesh;
        rPsr.alignment = ParticleSystemRenderSpace.Local;
        rPsr.sharedMaterial = runtimeAirRingMat;
        rPsr.shadowCastingMode = ShadowCastingMode.Off;
        rPsr.receiveShadows = false;
        rPsr.sortingOrder = 6;

        // 2B: เข็มลมเรียวคมพุ่งถอยหลังตอนดีดตัวออกวิ่ง
        GameObject gustObj = new GameObject("VFX_Runtime_SprintStartGust");
        gustObj.transform.SetParent(transform, false);
        gustObj.transform.localPosition = new Vector3(0f, 0.85f, 0.20f);
        gustObj.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        gustObj.transform.localScale = Vector3.one;

        launchGustNeedlesPS = gustObj.AddComponent<ParticleSystem>();
        launchGustNeedlesPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule gMain = launchGustNeedlesPS.main;
        gMain.duration = 0.35f;
        gMain.loop = false;
        gMain.playOnAwake = false;
        gMain.simulationSpace = ParticleSystemSimulationSpace.World;
        gMain.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.26f);
        gMain.startSpeed = new ParticleSystem.MinMaxCurve(9f, 15f);
        gMain.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.068f);
        gMain.startColor = activeColor;
        gMain.gravityModifier = 0f;
        gMain.maxParticles = 32;

        ParticleSystem.EmissionModule gEm = launchGustNeedlesPS.emission;
        gEm.enabled = false;

        ParticleSystem.ShapeModule gShape = launchGustNeedlesPS.shape;
        gShape.enabled = true;
        gShape.shapeType = ParticleSystemShapeType.Cone;
        gShape.angle = 22f;
        gShape.radius = 0.42f;
        gShape.radiusThickness = 0.9f;

        ParticleSystem.ColorOverLifetimeModule gCol = launchGustNeedlesPS.colorOverLifetime;
        gCol.enabled = true;
        gCol.color = new ParticleSystem.MinMaxGradient(BuildWindColorGradient(activeColor, 0.95f));

        ParticleSystemRenderer gPsr = gustObj.GetComponent<ParticleSystemRenderer>();
        gPsr.renderMode = ParticleSystemRenderMode.Stretch;
        gPsr.cameraVelocityScale = 0f;
        gPsr.velocityScale = 0.07f;
        gPsr.lengthScale = 6.2f;
        gPsr.sharedMaterial = runtimeStreakMat;
        gPsr.shadowCastingMode = ShadowCastingMode.Off;
        gPsr.receiveShadows = false;
        gPsr.sortingOrder = 5;
    }

    // ================================================================================================
    // LAYER 3: Graceful Dual Ankle Silk Ribbons (ริบบิ้นสายลมไหมคู่พริ้วติดข้อเท้าซ้าย-ขวา)
    // ================================================================================================
    private void BuildLayer3DualAnkleSilkRibbons()
    {
        Transform leftFoot = FindDeepChildByName(transform, leftFootBoneName);
        Transform rightFoot = FindDeepChildByName(transform, rightFootBoneName);

        if (leftFoot == null || rightFoot == null)
        {
            if (cachedAnimator != null && cachedAnimator.isHuman)
            {
                if (leftFoot == null) leftFoot = cachedAnimator.GetBoneTransform(HumanBodyBones.LeftFoot);
                if (rightFoot == null) rightFoot = cachedAnimator.GetBoneTransform(HumanBodyBones.RightFoot);
            }
        }

        if (leftFoot != null)
        {
            TrailRenderer trL = CreateSilkFootTrailOnBone(leftFoot, "VFX_Runtime_FootWindRibbon_L");
            if (trL != null && !footTrailRenderers.Contains(trL)) footTrailRenderers.Add(trL);
        }

        if (rightFoot != null)
        {
            TrailRenderer trR = CreateSilkFootTrailOnBone(rightFoot, "VFX_Runtime_FootWindRibbon_R");
            if (trR != null && !footTrailRenderers.Contains(trR)) footTrailRenderers.Add(trR);
        }
    }

    private TrailRenderer CreateSilkFootTrailOnBone(Transform footBone, string nodeName)
    {
        DestroyChildIfExists(footBone, nodeName);

        GameObject ribbonObj = new GameObject(nodeName);
        ribbonObj.transform.SetParent(footBone, false);
        ribbonObj.transform.localPosition = new Vector3(0f, 0.05f, 0f);
        ribbonObj.transform.localRotation = Quaternion.identity;
        ribbonObj.transform.localScale = Vector3.one;

        TrailRenderer trail = ribbonObj.AddComponent<TrailRenderer>();
        trail.time = 0.26f;
        trail.minVertexDistance = 0.015f;
        trail.autodestruct = false;
        trail.emitting = false;
        trail.numCornerVertices = 6;
        trail.numCapVertices = 4;
        trail.alignment = LineAlignment.View;
        trail.textureMode = LineTextureMode.Stretch;
        trail.shadowCastingMode = ShadowCastingMode.Off;
        trail.receiveShadows = false;
        trail.sharedMaterial = runtimeRibbonMat;
        trail.sortingOrder = 5;

        // ริบบิ้นเรียวพริ้วสง่างาม (0.13 -> 0.075 -> 0) ไม่หนาเทอะทะ
        AnimationCurve widthCurve = new AnimationCurve(
            new Keyframe(0.0f, 0.13f),
            new Keyframe(0.35f, 0.075f),
            new Keyframe(1.0f, 0.0f)
        );
        trail.widthMultiplier = 1.0f;
        trail.widthCurve = widthCurve;
        trail.colorGradient = BuildWindColorGradient(GetCurrentSelectedWindColor(), 0.82f);

        return trail;
    }

    // ================================================================================================
    // LAYER 4: Delicate Wind Motes & Ground Air Draft (ละอองลมและประกายอากาศลอยหมุนรอบตัวขณะวิ่ง)
    // ================================================================================================
    private void BuildLayer4WindMotesAndAirDraft()
    {
        DestroyChildIfExists(transform, "VFX_Runtime_WindMotes");

        GameObject moteObj = new GameObject("VFX_Runtime_WindMotes");
        moteObj.transform.SetParent(transform, false);
        moteObj.transform.localPosition = new Vector3(0f, 0.55f, 0.10f);
        moteObj.transform.localRotation = Quaternion.identity;
        moteObj.transform.localScale = Vector3.one;

        windMotesPS = moteObj.AddComponent<ParticleSystem>();
        windMotesPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        Color activeColor = GetCurrentSelectedWindColor();

        ParticleSystem.MainModule main = windMotesPS.main;
        main.duration = 1.0f;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.62f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.032f, 0.072f);
        main.startColor = activeColor;
        main.gravityModifier = -0.08f; // ลอยล่องเบาๆ ขึ้นในอากาศ
        main.maxParticles = 120;

        ParticleSystem.EmissionModule emission = windMotesPS.emission;
        emission.enabled = false;
        emission.rateOverTime = new ParticleSystem.MinMaxCurve(18f);
        emission.rateOverDistance = new ParticleSystem.MinMaxCurve(4.5f);

        ParticleSystem.ShapeModule shape = windMotesPS.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(0.85f, 1.15f, 0.55f);

        ParticleSystem.NoiseModule noise = windMotesPS.noise;
        noise.enabled = true;
        noise.strength = new ParticleSystem.MinMaxCurve(0.85f);
        noise.frequency = 1.8f;
        noise.scrollSpeed = 1.4f;
        noise.quality = ParticleSystemNoiseQuality.Medium;

        ParticleSystem.SizeOverLifetimeModule sol = windMotesPS.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0.0f, 0.0f),
            new Keyframe(0.25f, 1.0f),
            new Keyframe(1.0f, 0.0f)
        ));

        ParticleSystem.ColorOverLifetimeModule col = windMotesPS.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(BuildWindColorGradient(activeColor, 0.85f));

        ParticleSystemRenderer psr = moteObj.GetComponent<ParticleSystemRenderer>();
        psr.renderMode = ParticleSystemRenderMode.Billboard;
        psr.sharedMaterial = runtimeMoteMat;
        psr.shadowCastingMode = ShadowCastingMode.Off;
        psr.receiveShadows = false;
        psr.sortingOrder = 5;
    }

    private Color GetCurrentSelectedWindColor()
    {
        Color baseCol = useLightGreenWind ? lightGreenWindColor : pureWhiteWindColor;
        return new Color(baseCol.r, baseCol.g, baseCol.b, Mathf.Clamp01(windOpacity));
    }

    private static Gradient BuildWindColorGradient(Color windColor, float alphaScale)
    {
        Color rgbOnly = new Color(windColor.r, windColor.g, windColor.b, 1f);
        float peakAlpha = Mathf.Clamp01(windColor.a * alphaScale);

        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(Color.white, 0.0f),
                new GradientColorKey(rgbOnly, 0.35f),
                new GradientColorKey(rgbOnly, 1.0f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0.0f, 0.0f),
                new GradientAlphaKey(peakAlpha, 0.14f),
                new GradientAlphaKey(peakAlpha * 0.65f, 0.58f),
                new GradientAlphaKey(0.0f, 1.0f)
            }
        );
        return grad;
    }

    private void ApplyRealTimeColorAndVisualUpdates(bool forceUpdate)
    {
        bool changed = forceUpdate ||
                       (lastUseLightGreen != useLightGreenWind) ||
                       (lastLightGreenColor != lightGreenWindColor) ||
                       (lastPureWhiteColor != pureWhiteWindColor) ||
                       !Mathf.Approximately(lastWindDensity, windDensity) ||
                       !Mathf.Approximately(lastWindOpacity, windOpacity);

        if (!changed) return;

        lastUseLightGreen = useLightGreenWind;
        lastLightGreenColor = lightGreenWindColor;
        lastPureWhiteColor = pureWhiteWindColor;
        lastWindDensity = windDensity;
        lastWindOpacity = windOpacity;

        Color activeColor = GetCurrentSelectedWindColor();
        Gradient streakGrad = BuildWindColorGradient(activeColor, 1.0f);
        Gradient ribbonGrad = BuildWindColorGradient(activeColor, 0.82f);

        if (bodyWindPS != null)
        {
            ParticleSystem.MainModule main = bodyWindPS.main;
            main.startColor = activeColor;
            ParticleSystem.EmissionModule em = bodyWindPS.emission;
            em.rateOverTime = new ParticleSystem.MinMaxCurve(windDensity);
            ParticleSystem.ColorOverLifetimeModule col = bodyWindPS.colorOverLifetime;
            col.color = new ParticleSystem.MinMaxGradient(streakGrad);
        }

        if (launchAirRingPS != null)
        {
            ParticleSystem.MainModule main = launchAirRingPS.main;
            main.startColor = activeColor;
            ParticleSystem.ColorOverLifetimeModule col = launchAirRingPS.colorOverLifetime;
            col.color = new ParticleSystem.MinMaxGradient(BuildWindColorGradient(activeColor, 0.92f));
        }

        if (launchGustNeedlesPS != null)
        {
            ParticleSystem.MainModule main = launchGustNeedlesPS.main;
            main.startColor = activeColor;
            ParticleSystem.ColorOverLifetimeModule col = launchGustNeedlesPS.colorOverLifetime;
            col.color = new ParticleSystem.MinMaxGradient(streakGrad);
        }

        if (windMotesPS != null)
        {
            ParticleSystem.MainModule main = windMotesPS.main;
            main.startColor = activeColor;
            ParticleSystem.ColorOverLifetimeModule col = windMotesPS.colorOverLifetime;
            col.color = new ParticleSystem.MinMaxGradient(BuildWindColorGradient(activeColor, 0.85f));
        }

        for (int i = 0; i < footTrailRenderers.Count; i++)
        {
            TrailRenderer tr = footTrailRenderers[i];
            if (tr != null)
            {
                tr.colorGradient = ribbonGrad;
            }
        }
    }

    private void DisableLegacyDuplicateScripts()
    {
        MonoBehaviour[] allBehaviours = GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < allBehaviours.Length; i++)
        {
            MonoBehaviour mb = allBehaviours[i];
            if (mb == null || mb == this) continue;

            if (mb.GetType().Name == "SimpleSprintTrail")
            {
                mb.enabled = false;
            }
        }
    }

    private static void DestroyChildIfExists(Transform parent, string childName)
    {
        if (parent == null) return;
        Transform t = parent.Find(childName);
        if (t != null) Destroy(t.gameObject);
    }

    private static Transform FindDeepChildByName(Transform parent, string targetName)
    {
        if (parent == null || string.IsNullOrEmpty(targetName)) return null;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name == targetName) return child;

            Transform found = FindDeepChildByName(child, targetName);
            if (found != null) return found;
        }

        return null;
    }

    private void OnDestroy()
    {
        if (runtimeStreakMat != null) Destroy(runtimeStreakMat);
        if (runtimeRibbonMat != null) Destroy(runtimeRibbonMat);
        if (runtimeAirRingMat != null) Destroy(runtimeAirRingMat);
        if (runtimeMoteMat != null) Destroy(runtimeMoteMat);

        if (runtimeWindStreakTex != null) Destroy(runtimeWindStreakTex);
        if (runtimeFootRibbonTex != null) Destroy(runtimeFootRibbonTex);
        if (runtimeAirRingTex != null) Destroy(runtimeAirRingTex);
        if (runtimeWindMoteTex != null) Destroy(runtimeWindMoteTex);
    }
}
