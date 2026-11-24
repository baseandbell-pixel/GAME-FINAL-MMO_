using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// ระบบเอฟเฟกต์ "สายลมวิ่งพริ้วไหวระดับ AAA (Silky Wuxia Sprint Wind VFX)" สำหรับ Unity 6 (URP)
/// ออกแบบสไตล์ Where Winds Meet / Wuthering Waves (สะอาด หรูหรา พริ้วไหว ไม่มีใบไม้และไม่มีวงแหวนแหวกลม):
/// - Layer 1: ริบบิ้นสายลมโค้งพริ้วไหวรอบลำตัว (Curving Silky Wind Ribbons via Particle Trails + Organic Curl Noise)
/// - Layer 2: เส้นสายลมพู่กันบางคม + แรงลมพุ่งออกตัว (Painterly Aerodynamic Speed Wisps & Launch Gust)
/// - Layer 3: ริบบิ้นสายลมไหมคู่พริ้วติดข้อเท้าซ้าย-ขวา + หมอกฝุ่นเท้าจางๆ (Dual-Filament Ankle Silk Ribbons & Ground Step Mist)
/// - Layer 4: ละอองลมปราณเรืองแสงอ่อนๆ ลอยตามแรงลม (Luminous Wuxia Qi Wind Motes)
/// ทำงานเฉพาะตอนวิ่ง (กด Shift + เคลื่อนที่ หรืออนิเมชั่นเข้าท่าวิ่ง) เดินปกติจะไม่แสดงเอฟเฟกต์
/// </summary>
[AddComponentMenu("yeen VFX/02. [Player] วิ่งลมผ่านตัว (Run Wind Slipstream)")]
[DisallowMultipleComponent]
public class RunWindSlipstream : MonoBehaviour
{
    [Header("=== 1. ระบบสลับสีสายลมแบบเรียลไทม์ (Real-Time Wind Color Palette) ===")]
    [Tooltip("ติ๊กถูก = สีหยกมรกตอ่อนสายลม (Celadon Jade Wind) / เอาติ๊กออก = สีขาวจันทร์สายลมบริสุทธิ์ (Moonlight Aerial White)")]
    public bool useLightGreenWind = true;

    [Tooltip("สีหยกอ่อนสายลมสไตล์จอมยุทธ์ (Celadon Jade Wind)")]
    public Color lightGreenWindColor = new Color(0.68f, 1.0f, 0.90f, 0.78f);

    [Tooltip("สีขาวจันทร์สายลมบริสุทธิ์ (Moonlight Aerial White)")]
    public Color pureWhiteWindColor = new Color(0.92f, 0.98f, 1.0f, 0.76f);

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
    [Tooltip("ความหนาแน่นของเส้นสายลมแหวกอากาศผ่านลำตัว (ค่าแนะนำ 24 - 40 เส้น/วินาที)")]
    [Range(8f, 90f)]
    public float windDensity = 30f;

    [Tooltip("ความสว่างโปร่งแสงของสายลม (ค่าแนะนำ 0.58 - 0.80 พริ้วคมชัดไม่บังตัวละคร)")]
    [Range(0.15f, 1.0f)]
    public float windOpacity = 0.70f;

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
    private Texture2D runtimeWindStreakTex;
    private Texture2D runtimeFootRibbonTex;
    private Texture2D runtimeStepMistTex;

    private Material runtimeStreakMat;
    private Material runtimeRibbonMat;
    private Material runtimeStepMistMat;

    // 3-Layer Silky Wuxia Sprint Wind VFX (ไม่มีใบไม้ วงแหวนแหวกลม และละออง)
    private ParticleSystem flowingRibbonsPS;
    private ParticleSystem bodyWindPS;
    private ParticleSystem launchGustNeedlesPS;
    private ParticleSystem footStepMistPS;
    private readonly List<ParticleSystem> existingChildParticles = new List<ParticleSystem>();
    private readonly List<TrailRenderer> footTrailRenderers = new List<TrailRenderer>();

    // Cached Components
    private Animator cachedAnimator;
    private CharacterController cachedCharacterController;
    private Rigidbody cachedRigidbody;
    private NetworkCharacter cachedNetCharacter;
    private PlayerCapsule cachedPlayerCapsule;
    private PlayerCombat cachedPlayerCombat;

    private Vector3 previousWorldPos;
    private Vector3 currentMoveDirection = Vector3.forward;
    private bool hasInitializedPos = false;
    private float graceTimer = 0f;
    private float launchBurstCooldown = 0f;

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
        SilenceLegacyDuplicateSprintParticles();

        // ล้างโหนดรุ่นเก่าที่ไม่ต้องการ (ไม่มีใบไม้ วงแหวนแหวกลม และละออง)
        DestroyChildIfExists(transform, "VFX_Runtime_WWM_BambooLeaves");
        DestroyChildIfExists(transform, "VFX_Runtime_SprintStartAirRing");
        DestroyChildIfExists(transform, "VFX_Runtime_QinggongFootRipple");
        DestroyChildIfExists(transform, "VFX_Runtime_DashStartBurst");
        DestroyChildIfExists(transform, "VFX_Runtime_WindMotes");

        // สร้างโครงสร้างสายลมวิ่งพริ้วไหวระดับ AAA
        BuildLayer1FlowingSilkRibbons();
        BuildLayer2BodyWindAndLaunchGust();
        BuildLayer3DualAnkleRibbonsAndFootMist();

        previousWorldPos = transform.position;
        hasInitializedPos = true;
    }

    private void Start()
    {
        CacheAnimatorParameterList();

        StopLoopEmitter(flowingRibbonsPS);
        StopLoopEmitter(bodyWindPS);
        StopLoopEmitter(footStepMistPS);

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

        cachedPlayerCombat = GetComponentInParent<PlayerCombat>();
        if (cachedPlayerCombat == null) cachedPlayerCombat = GetComponentInChildren<PlayerCombat>(true);
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
        if (dt <= 0.00001f) return;

        if (launchBurstCooldown > 0f)
        {
            launchBurstCooldown -= dt;
        }

        ApplyRealTimeColorAndVisualUpdates(false);
        UpdateRealHorizontalSpeed(dt);
        AlignWindRigToMovementDirection();

        // ตรวจสอบว่าเป็นตัวละครที่ Spawn บนเครือข่าย Fusion หรือไม่ เพื่อแยกเอฟเฟกต์วิ่งเฉพาะตัวละครนี้ (ไม่ไหลไปติดเพื่อนในห้อง)
        bool hasNetworkCapsule = cachedPlayerCapsule != null && cachedPlayerCapsule.Object != null && cachedPlayerCapsule.Object.IsValid;
        bool shouldActivateWind = false;

        if (hasNetworkCapsule)
        {
            bool alive = cachedPlayerCombat == null || !cachedPlayerCombat.IsSpawned || !cachedPlayerCombat.IsDead;
            bool movingNet = cachedPlayerCapsule.NetworkMove.sqrMagnitude > 0.01f;
            bool sprintingNet = alive && cachedPlayerCapsule.IsSprinting && movingNet;

            isShiftHeld = sprintingNet;
            isPlayingRunAnimation = sprintingNet;
            if (sprintingNet)
            {
                shouldActivateWind = true;
                activeTriggerSource = "PlayerCapsule NetworkSprinting";
            }
        }
        else
        {
            bool isLocal = IsLocallyOwnedPlayer();
            bool movementKeyPressed = false;
            isShiftHeld = isLocal && DetectUniversalShiftAndMovementInput(out movementKeyPressed);
            bool isMovingNow = movementKeyPressed || (currentHorizontalSpeed >= minMoveSpeedThreshold);

            string animReason;
            isPlayingRunAnimation = EvaluateRunAnimationAndNetworkState(out animReason);

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

    private bool IsLocallyOwnedPlayer()
    {
        if (cachedPlayerCombat != null && cachedPlayerCombat.IsSpawned)
            return cachedPlayerCombat.IsLocallyControlled;
        if (cachedPlayerCapsule != null && cachedPlayerCapsule.Object != null && cachedPlayerCapsule.Object.IsValid)
            return cachedPlayerCapsule.Object.HasStateAuthority || cachedPlayerCapsule.Object.HasInputAuthority;
        if (cachedNetCharacter != null && cachedNetCharacter.IsSpawned && cachedNetCharacter.Object != null && cachedNetCharacter.Object.IsValid)
            return cachedNetCharacter.Object.HasStateAuthority || cachedNetCharacter.Object.HasInputAuthority;
        return true;
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

        if (cachedPlayerCapsule != null && cachedPlayerCapsule.Object != null && cachedPlayerCapsule.Object.IsValid)
        {
            if (cachedPlayerCapsule.IsSprinting && cachedPlayerCapsule.NetworkMove.sqrMagnitude > 0.01f)
            {
                reason = "PlayerCapsule IsSprinting";
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

    private float GetFeetLocalY()
    {
        Transform visual = transform.Find("CharacterVisual");
        return visual != null ? visual.localPosition.y : 0f;
    }

    private void SilenceLegacyDuplicateSprintParticles()
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
                ParticleSystem[] psArray = t.GetComponentsInChildren<ParticleSystem>(true);
                for (int p = 0; p < psArray.Length; p++)
                {
                    if (psArray[p] != null)
                    {
                        ParticleSystem.EmissionModule em = psArray[p].emission;
                        em.enabled = false;
                        psArray[p].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    }
                }
            }
        }
    }

    private void SetWindEmissionState(bool active)
    {
        isWindActive = active;

        SetLoopEmitterActive(flowingRibbonsPS, active);
        SetLoopEmitterActive(bodyWindPS, active);
        SetLoopEmitterActive(footStepMistPS, active);

        // จังหวะเริ่มออกตัววิ่ง ปล่อยริบบิ้นลมโค้ง + เข็มลมเรียวพุ่งถอยหลังแบบนุ่มนวล (ไม่มีวงแหวนแหวกลมและใบไม้)
        if (active && launchBurstCooldown <= 0f)
        {
            if (launchGustNeedlesPS != null)
            {
                launchGustNeedlesPS.Play(true);
                launchGustNeedlesPS.Emit(12);
            }
            if (flowingRibbonsPS != null)
            {
                flowingRibbonsPS.Emit(3);
            }
            if (footStepMistPS != null)
            {
                footStepMistPS.Emit(4);
            }
            launchBurstCooldown = 0.55f;
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
        SetLoopEmitterActive(flowingRibbonsPS, true);
        SetLoopEmitterActive(bodyWindPS, true);
        SetLoopEmitterActive(footStepMistPS, true);

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
        float deltaDist = horizontalDelta.magnitude;
        float deltaSpeed = deltaDist / dt;
        if (deltaDist > 0.002f)
        {
            currentMoveDirection = Vector3.Slerp(currentMoveDirection, horizontalDelta / deltaDist, Mathf.Clamp01(dt * 18f));
        }
        else if (transform.forward.sqrMagnitude > 0.01f)
        {
            Vector3 flatFwd = new Vector3(transform.forward.x, 0f, transform.forward.z);
            if (flatFwd.sqrMagnitude > 0.001f)
                currentMoveDirection = flatFwd.normalized;
        }
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

    private void AlignWindRigToMovementDirection()
    {
        if (currentMoveDirection.sqrMagnitude < 0.001f) return;

        Quaternion backwardRot = Quaternion.LookRotation(-currentMoveDirection, Vector3.up);
        float feetY = GetFeetLocalY();

        if (flowingRibbonsPS != null)
        {
            flowingRibbonsPS.transform.position = transform.position + Vector3.up * (feetY + 0.30f) + currentMoveDirection * 0.22f;
            flowingRibbonsPS.transform.rotation = backwardRot;
        }

        if (bodyWindPS != null)
        {
            bodyWindPS.transform.position = transform.position + Vector3.up * (feetY + 0.28f) + currentMoveDirection * 0.26f;
            bodyWindPS.transform.rotation = backwardRot;
        }

        if (launchGustNeedlesPS != null)
        {
            launchGustNeedlesPS.transform.position = transform.position + Vector3.up * (feetY + 0.24f) + currentMoveDirection * 0.12f;
            launchGustNeedlesPS.transform.rotation = backwardRot;
        }

        if (footStepMistPS != null)
        {
            footStepMistPS.transform.position = transform.position + Vector3.up * (feetY + 0.10f) - currentMoveDirection * 0.10f;
            footStepMistPS.transform.rotation = backwardRot;
        }
    }

    // ================================================================================================
    // สร้าง Procedural Textures & Materials ระดับ AAA (พู่กันสายลมไหม, หมอกฝุ่นเท้า)
    // ================================================================================================
    private void GenerateRuntimeWindAssets()
    {
        runtimeWindStreakTex = CreateSilkyWindStreakTexture();
        runtimeFootRibbonTex = CreateDualFilamentSilkRibbonTexture();
        runtimeStepMistTex = CreateSoftFootMistTexture();

        runtimeStreakMat = CreateURPWindMaterial("MAT_WWM_CalligraphyWindStreak", runtimeWindStreakTex, new Color(1.65f, 1.92f, 1.88f, 1f));
        runtimeRibbonMat = CreateURPWindMaterial("MAT_WWM_FlowingSilkRibbon", runtimeFootRibbonTex, new Color(1.55f, 1.85f, 1.80f, 1f));
        runtimeStepMistMat = CreateSoftAlphaMistMaterial("MAT_WWM_SprintFootMist", runtimeStepMistTex);
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
            for (int x = 0; x < width; x++)
            {
                float nx = x / (float)(width - 1);
                // เส้นพู่กันโค้งอ่อนช้อยเล็กน้อยตามความยาว (Organic Calligraphy Taper)
                float brushCurve = Mathf.Sin(nx * Mathf.PI * 1.6f) * 0.10f;
                float cy = ny - brushCurve;

                float razorCore = Mathf.Exp(-cy * cy * 36.0f) * 0.96f;
                float silkyVeil = Mathf.Exp(-cy * cy * 7.8f) * 0.48f;
                float twinWhisper = Mathf.Exp(-Mathf.Pow(Mathf.Abs(cy) - 0.34f, 2f) * 44.0f) * 0.26f;

                float headNeedle = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(nx / 0.14f));
                float tailDissolve = Mathf.Pow(Mathf.Clamp01((1f - nx) / 0.86f), 1.75f);

                float alpha = Mathf.Clamp01((razorCore + silkyVeil + twinWhisper) * headNeedle * tailDissolve);
                pixels[y * width + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply(false, false);
        return tex;
    }

    private static Texture2D CreateDualFilamentSilkRibbonTexture()
    {
        int width = 256;
        int height = 64;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.name = "T_AAA_DualSilkRibbon";
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            float ny = ((y / (float)(height - 1)) * 2f) - 1f;
            for (int x = 0; x < width; x++)
            {
                float nx = x / (float)(width - 1);
                // ริบบิ้นผ้าไหมลมคู่ถักทอคลื่นอ่อนช้อย (Silky Twin-Strand Weave)
                float waveA = Mathf.Sin(nx * Mathf.PI * 3.5f) * 0.14f;
                float waveB = Mathf.Cos(nx * Mathf.PI * 2.5f) * 0.11f;
                float strandA = Mathf.Exp(-Mathf.Pow(ny - (0.24f + waveA), 2f) * 34.0f) * 0.88f;
                float strandB = Mathf.Exp(-Mathf.Pow(ny + (0.24f - waveB), 2f) * 38.0f) * 0.72f;
                float centralSilk = Mathf.Exp(-ny * ny * 5.8f) * 0.42f;
                float edgeGuard = 1f - Mathf.SmoothStep(0.76f, 0.98f, Mathf.Abs(ny));

                float lengthFade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(nx / 0.10f)) *
                                   Mathf.Pow(Mathf.Clamp01((1f - nx) / 0.90f), 1.35f);
                float alpha = Mathf.Clamp01((strandA + strandB + centralSilk) * edgeGuard * lengthFade);
                pixels[y * width + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply(false, false);
        return tex;
    }

    private static Texture2D CreateSoftFootMistTexture()
    {
        int size = 128;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.name = "T_AAA_SprintFootMist";
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

                float n1 = Mathf.PerlinNoise(nx * 2.4f + 5.3f, ny * 2.4f + 9.1f);
                float n2 = Mathf.PerlinNoise(nx * 5.2f + 13.7f, ny * 5.2f + 2.6f) * 0.5f;
                float fbm = (n1 + n2) / 1.5f;

                float radial = 1f - Mathf.SmoothStep(0.16f, 0.92f, r - (fbm - 0.5f) * 0.24f);
                float guard = 1f - Mathf.SmoothStep(0.76f, 0.97f, r);
                float alpha = Mathf.Clamp01(radial * guard * (0.40f + fbm * 0.72f));

                pixels[y * size + x] = new Color(0.96f, 0.99f, 1.0f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply(false, false);
        return tex;
    }

    private static Material CreateURPWindMaterial(string matName, Texture2D tex, Color hdrTint)
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

    private static Material CreateSoftAlphaMistMaterial(string matName, Texture2D tex)
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");

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

    // ================================================================================================
    // LAYER 1: Curving Silky Wind Ribbons (ริบบิ้นสายลมโค้งพริ้วไหวด้วย Particle Trails + Curl Noise)
    // ================================================================================================
    private void BuildLayer1FlowingSilkRibbons()
    {
        DestroyChildIfExists(transform, "VFX_Runtime_FlowingWindRibbons");

        float feetY = GetFeetLocalY();
        GameObject ribObj = new GameObject("VFX_Runtime_FlowingWindRibbons");
        ribObj.transform.SetParent(transform, false);
        ribObj.transform.localPosition = new Vector3(0f, feetY + 0.30f, 0.22f);
        ribObj.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        flowingRibbonsPS = ribObj.AddComponent<ParticleSystem>();
        flowingRibbonsPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        Color activeColor = GetCurrentSelectedWindColor();

        var main = flowingRibbonsPS.main;
        main.duration = 1.0f;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.28f, 0.44f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(4.2f, 7.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.055f, 0.115f);
        main.startColor = Color.white;
        main.gravityModifier = -0.02f;
        main.maxParticles = 48;

        var emission = flowingRibbonsPS.emission;
        emission.enabled = false;
        emission.rateOverTime = new ParticleSystem.MinMaxCurve(11f);
        emission.rateOverDistance = new ParticleSystem.MinMaxCurve(1.4f);

        var shape = flowingRibbonsPS.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(0.44f, 0.22f, 0.14f); // กระจุกพริ้วรอบช่วงขาและหน้าแข้ง ไม่ลอยขึ้นบังลำตัว

        // Noise โค้งนุ่มๆ ให้ริบบิ้นลมพริ้วเป็นเส้นโค้งรอบช่วงขา
        var noise = flowingRibbonsPS.noise;
        noise.enabled = true;
        noise.strength = new ParticleSystem.MinMaxCurve(0.72f);
        noise.frequency = 1.45f;
        noise.scrollSpeed = 1.85f;
        noise.damping = true;
        noise.quality = ParticleSystemNoiseQuality.Medium;

        var trails = flowingRibbonsPS.trails;
        trails.enabled = true;
        trails.mode = ParticleSystemTrailMode.PerParticle;
        trails.ratio = 1.0f;
        trails.lifetime = new ParticleSystem.MinMaxCurve(0.55f);
        trails.minVertexDistance = 0.04f;
        trails.worldSpace = true;
        trails.dieWithParticles = false;
        trails.textureMode = ParticleSystemTrailTextureMode.Stretch;
        trails.sizeAffectsWidth = true;
        trails.sizeAffectsLifetime = false;
        trails.inheritParticleColor = false;
        trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0.0f, 0.20f),
            new Keyframe(0.20f, 1.00f),
            new Keyframe(0.65f, 0.48f),
            new Keyframe(1.0f, 0.00f)
        ));
        trails.colorOverLifetime = new ParticleSystem.MinMaxGradient(BuildWindColorGradient(activeColor, 0.88f));
        trails.colorOverTrail = new ParticleSystem.MinMaxGradient(BuildTrailTailFadeGradient());

        var psr = ribObj.GetComponent<ParticleSystemRenderer>();
        psr.renderMode = ParticleSystemRenderMode.None; // โชว์เฉพาะเส้นริบบิ้นพริ้วโค้งล้วนๆ ไม่มีจุดเม็ดหัว
        psr.trailMaterial = runtimeRibbonMat;
        psr.shadowCastingMode = ShadowCastingMode.Off;
        psr.receiveShadows = false;
        psr.sortingOrder = 6;
    }

    // ================================================================================================
    // LAYER 2: Painterly Aerodynamic Speed Wisps & Launch Gust (เส้นลมพู่กันบางคม + ลมดีดออกตัว)
    // ================================================================================================
    private void BuildLayer2BodyWindAndLaunchGust()
    {
        DestroyChildIfExists(transform, "VFX_Runtime_BodyWindSlipstream");
        DestroyChildIfExists(transform, "VFX_Runtime_SprintStartGust");

        float feetY = GetFeetLocalY();
        Color activeColor = GetCurrentSelectedWindColor();

        // 2A. เส้นสายลมพู่กันบางคมพริ้วรอบช่วงขา
        GameObject slipObj = new GameObject("VFX_Runtime_BodyWindSlipstream");
        slipObj.transform.SetParent(transform, false);
        slipObj.transform.localPosition = new Vector3(0f, feetY + 0.28f, 0.26f);
        slipObj.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        bodyWindPS = slipObj.AddComponent<ParticleSystem>();
        bodyWindPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = bodyWindPS.main;
        main.duration = 1.0f;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.20f, 0.32f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(7.5f, 12.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.018f, 0.036f);
        main.startColor = Color.white;
        main.gravityModifier = 0f;
        main.maxParticles = 160;

        var emission = bodyWindPS.emission;
        emission.enabled = false;
        emission.rateOverTime = new ParticleSystem.MinMaxCurve(windDensity);
        emission.rateOverDistance = new ParticleSystem.MinMaxCurve(1.8f);

        var shape = bodyWindPS.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(0.46f, 0.26f, 0.18f); // พริ้วรอบช่วงขาและข้อเท้า

        var noise = bodyWindPS.noise;
        noise.enabled = true;
        noise.strength = new ParticleSystem.MinMaxCurve(0.32f);
        noise.frequency = 1.35f;
        noise.scrollSpeed = 1.4f;
        noise.damping = true;
        noise.quality = ParticleSystemNoiseQuality.Medium;

        var col = bodyWindPS.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(BuildWindColorGradient(activeColor, 0.78f));

        var sol = bodyWindPS.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0.0f, 0.15f),
            new Keyframe(0.20f, 1.0f),
            new Keyframe(0.75f, 0.62f),
            new Keyframe(1.0f, 0.0f)
        ));

        var psr = slipObj.GetComponent<ParticleSystemRenderer>();
        psr.renderMode = ParticleSystemRenderMode.Stretch;
        psr.cameraVelocityScale = 0f;
        psr.velocityScale = 0.065f;
        psr.lengthScale = 8.2f;
        psr.sharedMaterial = runtimeStreakMat;
        psr.shadowCastingMode = ShadowCastingMode.Off;
        psr.receiveShadows = false;
        psr.sortingOrder = 5;

        // 2B. แรงลมดีดออกตัวระดับขา (Launch Gust Wisps — ไม่มีวงแหวนแหวกลม)
        GameObject gustObj = new GameObject("VFX_Runtime_SprintStartGust");
        gustObj.transform.SetParent(transform, false);
        gustObj.transform.localPosition = new Vector3(0f, feetY + 0.24f, 0.12f);
        gustObj.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        launchGustNeedlesPS = gustObj.AddComponent<ParticleSystem>();
        launchGustNeedlesPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var gMain = launchGustNeedlesPS.main;
        gMain.duration = 0.30f;
        gMain.loop = false;
        gMain.playOnAwake = false;
        gMain.simulationSpace = ParticleSystemSimulationSpace.World;
        gMain.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.28f);
        gMain.startSpeed = new ParticleSystem.MinMaxCurve(8.0f, 13.0f);
        gMain.startSize = new ParticleSystem.MinMaxCurve(0.022f, 0.044f);
        gMain.startColor = Color.white;
        gMain.gravityModifier = 0f;
        gMain.maxParticles = 32;

        var gEm = launchGustNeedlesPS.emission;
        gEm.enabled = false;

        var gShape = launchGustNeedlesPS.shape;
        gShape.enabled = true;
        gShape.shapeType = ParticleSystemShapeType.Cone;
        gShape.angle = 12f;
        gShape.radius = 0.28f;
        gShape.radiusThickness = 0.7f;

        var gCol = launchGustNeedlesPS.colorOverLifetime;
        gCol.enabled = true;
        gCol.color = new ParticleSystem.MinMaxGradient(BuildWindColorGradient(activeColor, 0.92f));

        var gPsr = gustObj.GetComponent<ParticleSystemRenderer>();
        gPsr.renderMode = ParticleSystemRenderMode.Stretch;
        gPsr.cameraVelocityScale = 0f;
        gPsr.velocityScale = 0.06f;
        gPsr.lengthScale = 6.2f;
        gPsr.sharedMaterial = runtimeStreakMat;
        gPsr.shadowCastingMode = ShadowCastingMode.Off;
        gPsr.receiveShadows = false;
        gPsr.sortingOrder = 5;
    }

    // ================================================================================================
    // LAYER 3: Dual Ankle Silk Ribbons + Soft Ground Footstep Mist (ริบบิ้นข้อเท้าคู่ + หมอกฝุ่นเท้า)
    // ================================================================================================
    private void BuildLayer3DualAnkleRibbonsAndFootMist()
    {
        footTrailRenderers.Clear();

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
            if (trL != null) footTrailRenderers.Add(trL);
        }

        if (rightFoot != null)
        {
            TrailRenderer trR = CreateSilkFootTrailOnBone(rightFoot, "VFX_Runtime_FootWindRibbon_R");
            if (trR != null) footTrailRenderers.Add(trR);
        }

        // หมอกฝุ่นบางเบาติดพื้นหลังปลายเท้าตอนวิ่ง (เพิ่มน้ำหนักฝีเท้าแบบวิชาตัวเบา)
        DestroyChildIfExists(transform, "VFX_Runtime_SprintFootMist");

        float feetY = GetFeetLocalY();
        GameObject mistObj = new GameObject("VFX_Runtime_SprintFootMist");
        mistObj.transform.SetParent(transform, false);
        mistObj.transform.localPosition = new Vector3(0f, feetY + 0.12f, -0.12f);
        mistObj.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        footStepMistPS = mistObj.AddComponent<ParticleSystem>();
        footStepMistPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var mMain = footStepMistPS.main;
        mMain.duration = 1.0f;
        mMain.loop = true;
        mMain.playOnAwake = false;
        mMain.simulationSpace = ParticleSystemSimulationSpace.World;
        mMain.startLifetime = new ParticleSystem.MinMaxCurve(0.32f, 0.50f);
        mMain.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.5f);
        mMain.startSize = new ParticleSystem.MinMaxCurve(0.28f, 0.48f);
        mMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        mMain.startColor = Color.white;
        mMain.gravityModifier = -0.04f;
        mMain.maxParticles = 45;

        var mEm = footStepMistPS.emission;
        mEm.enabled = false;
        mEm.rateOverTime = new ParticleSystem.MinMaxCurve(6f);
        mEm.rateOverDistance = new ParticleSystem.MinMaxCurve(1.9f);

        var mShape = footStepMistPS.shape;
        mShape.enabled = true;
        mShape.shapeType = ParticleSystemShapeType.Box;
        mShape.scale = new Vector3(0.46f, 0.08f, 0.20f);

        var mRot = footStepMistPS.rotationOverLifetime;
        mRot.enabled = true;
        mRot.z = new ParticleSystem.MinMaxCurve(-0.85f, 0.85f);

        var mSol = footStepMistPS.sizeOverLifetime;
        mSol.enabled = true;
        mSol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0.0f, 0.42f),
            new Keyframe(0.45f, 1.00f),
            new Keyframe(1.0f, 1.48f)
        ));

        var mCol = footStepMistPS.colorOverLifetime;
        mCol.enabled = true;
        mCol.color = new ParticleSystem.MinMaxGradient(BuildFootMistGradient(GetCurrentSelectedWindColor()));

        var mPsr = mistObj.GetComponent<ParticleSystemRenderer>();
        mPsr.renderMode = ParticleSystemRenderMode.Billboard;
        mPsr.sharedMaterial = runtimeStepMistMat;
        mPsr.shadowCastingMode = ShadowCastingMode.Off;
        mPsr.receiveShadows = false;
        mPsr.sortingOrder = 4;
    }

    private TrailRenderer CreateSilkFootTrailOnBone(Transform footBone, string nodeName)
    {
        DestroyChildIfExists(footBone, nodeName);

        GameObject ribbonObj = new GameObject(nodeName);
        ribbonObj.transform.SetParent(footBone, false);
        ribbonObj.transform.localPosition = new Vector3(0f, 0.06f, 0f);
        ribbonObj.transform.localRotation = Quaternion.identity;
        ribbonObj.transform.localScale = Vector3.one;

        TrailRenderer trail = ribbonObj.AddComponent<TrailRenderer>();
        trail.time = 0.34f;
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
        trail.sortingOrder = 6;

        AnimationCurve widthCurve = new AnimationCurve(
            new Keyframe(0.0f, 0.095f),
            new Keyframe(0.32f, 0.052f),
            new Keyframe(0.74f, 0.018f),
            new Keyframe(1.0f, 0.0f)
        );
        trail.widthMultiplier = 1.0f;
        trail.widthCurve = widthCurve;
        trail.colorGradient = BuildWindColorGradient(GetCurrentSelectedWindColor(), 0.92f);

        return trail;
    }

    private Color GetCurrentSelectedWindColor()
    {
        Color baseCol = useLightGreenWind ? lightGreenWindColor : pureWhiteWindColor;
        return new Color(baseCol.r, baseCol.g, baseCol.b, Mathf.Clamp01(windOpacity));
    }

    private static Gradient BuildWindColorGradient(Color windColor, float alphaScale)
    {
        Color rgbOnly = new Color(windColor.r, windColor.g, windColor.b, 1f);
        Color tailTint = Color.Lerp(rgbOnly, new Color(0.55f, 0.90f, 0.96f, 1f), 0.30f);
        float peakAlpha = Mathf.Clamp01(windColor.a * alphaScale);

        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(Color.white, 0.0f),
                new GradientColorKey(rgbOnly, 0.34f),
                new GradientColorKey(tailTint, 1.0f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0.0f, 0.0f),
                new GradientAlphaKey(peakAlpha, 0.14f),
                new GradientAlphaKey(peakAlpha * 0.68f, 0.56f),
                new GradientAlphaKey(0.0f, 1.0f)
            }
        );
        return grad;
    }

    private static Gradient BuildTrailTailFadeGradient()
    {
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(Color.white, 0.0f),
                new GradientColorKey(new Color(0.82f, 0.97f, 0.96f), 0.55f),
                new GradientColorKey(new Color(0.62f, 0.88f, 0.92f), 1.0f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0.0f, 0.0f),
                new GradientAlphaKey(1.0f, 0.14f),
                new GradientAlphaKey(0.55f, 0.60f),
                new GradientAlphaKey(0.0f, 1.0f)
            }
        );
        return grad;
    }

    private static Gradient BuildFootMistGradient(Color windColor)
    {
        Color softMist = Color.Lerp(new Color(0.92f, 0.96f, 0.97f), new Color(windColor.r, windColor.g, windColor.b), 0.30f);
        float peakAlpha = Mathf.Clamp01(windColor.a * 0.42f);

        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(0.97f, 1.0f, 0.99f), 0.0f),
                new GradientColorKey(softMist, 0.55f),
                new GradientColorKey(new Color(0.76f, 0.85f, 0.88f), 1.0f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0.0f, 0.0f),
                new GradientAlphaKey(peakAlpha, 0.18f),
                new GradientAlphaKey(peakAlpha * 0.45f, 0.62f),
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
        Gradient streakGrad = BuildWindColorGradient(activeColor, 0.78f);
        Gradient ribbonGrad = BuildWindColorGradient(activeColor, 0.90f);

        if (flowingRibbonsPS != null)
        {
            var trails = flowingRibbonsPS.trails;
            trails.colorOverLifetime = new ParticleSystem.MinMaxGradient(ribbonGrad);
        }

        if (bodyWindPS != null)
        {
            var em = bodyWindPS.emission;
            em.rateOverTime = new ParticleSystem.MinMaxCurve(windDensity);
            var col = bodyWindPS.colorOverLifetime;
            col.color = new ParticleSystem.MinMaxGradient(streakGrad);
        }

        if (launchGustNeedlesPS != null)
        {
            var col = launchGustNeedlesPS.colorOverLifetime;
            col.color = new ParticleSystem.MinMaxGradient(BuildWindColorGradient(activeColor, 0.92f));
        }

        if (footStepMistPS != null)
        {
            var col = footStepMistPS.colorOverLifetime;
            col.color = new ParticleSystem.MinMaxGradient(BuildFootMistGradient(activeColor));
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
        if (runtimeStepMistMat != null) Destroy(runtimeStepMistMat);

        if (runtimeWindStreakTex != null) Destroy(runtimeWindStreakTex);
        if (runtimeFootRibbonTex != null) Destroy(runtimeFootRibbonTex);
        if (runtimeStepMistTex != null) Destroy(runtimeStepMistTex);
    }
}
