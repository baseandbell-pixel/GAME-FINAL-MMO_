using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class SimpleSprintTrail : MonoBehaviour
{
    [Header("=== 1. ระบบตรวจจับสถานะวิ่ง 5 ช่องทาง (5-Layer Universal Detection) ===")]
    [Tooltip("ช่องทางที่ 1: ตรวจจับการกดปุ่ม Left Shift / Right Shift ร่วมกับการเคลื่อนที่")]
    public bool detectShiftKey = true;

    [Tooltip("หากเปิดไว้ แค่กดปุ่ม Shift ค้างไว้เอฟเฟกต์ก็จะทำงานทันที (แม้ตัวละครยืนอยู่กับที่) เหมาะสำหรับเช็กว่าปุ่มทำงานหรือไม่")]
    public bool triggerOnShiftImmediately = false;

    [Tooltip("ช่องทางที่ 2: ตรวจจับค่าตัวแปรใน Animator (Speed, MoveSpeed, Velocity, MoveX/MoveY, IsSprinting, Run)")]
    public bool detectAnimatorParameters = true;

    [Tooltip("ช่องทางที่ 3: ตรวจจับชื่อท่าอนิเมชั่นใน Animator State / Sub-State (Run, Sprint, FastRun, Dash)")]
    public bool detectAnimatorStateName = true;

    [Tooltip("ช่องทางที่ 4: ตรวจจับความเร็วการเคลื่อนที่จริงในโลก 3D (ปิดไว้เป็นค่าเริ่มต้นเพื่อไม่ให้เดินปกติใน PlayerCapsule แล้วติดเอฟเฟกต์)")]
    public bool detectTransformVelocity = false;

    [Header("=== 2. ค่าเกณฑ์ความเร็วขั้นต่ำที่ถือว่า 'กำลังวิ่ง' (Speed Thresholds) ===")]
    [Tooltip("ความเร็วเคลื่อนที่จริงขั้นต่ำ (m/s) ที่ถือว่ากำลังวิ่ง (ปิดการเช็กความเร็วโลกเป็นค่าเริ่มต้นเพื่อไม่ให้เดินปกติใน PlayerCapsule แล้วติดเอฟเฟกต์)")]
    public float minRunSpeedThreshold = 11.0f;

    [Tooltip("ค่า Float Parameter ใน Animator ขั้นต่ำที่ถือว่าวิ่ง (ใน PlayerAnimator BlendTree เดินตรง = 1.0, เดินเฉียง = 1.414, วิ่ง = 2.0 ดังนั้นค่า 1.55 จึงแยกเดินกับวิ่งได้เด็ดขาด)")]
    public float animatorFloatRunThreshold = 1.55f;

    [Tooltip("ค่า Normalized BlendTree Threshold (สำหรับ Animator ที่ส่งค่า MoveY หรือ Speed ในช่วง 0.0 - 1.0)")]
    public float normalizedBlendRunThreshold = 0.75f;

    [Tooltip("เวลาหน่วงก่อนปิดเอฟเฟกต์เมื่อหยุดวิ่ง (วินาที) ช่วยให้สายลมค่อยๆ จางหายอย่างนุ่มนวล ไม่ตัดหายทันที")]
    public float stopGraceDuration = 0.15f;

    [Header("=== 3. โหมดบังคับเปิดทดสอบ & หน้าต่าง Debug (Channel 5 & OnGUI) ===")]
    [Tooltip("ช่องทางที่ 5: ติ๊กถูกเพื่อบังคับเปิดเอฟเฟกต์สายลมตลอดเวลา")]
    public bool forceTestOn = false;

    [Tooltip("ปุ่มลัดบนคีย์บอร์ดสำหรับสลับเปิด/ปิดโหมด Force Test On ขณะอยู่ใน Play Mode")]
    public KeyCode toggleForceTestKey = KeyCode.None;

    [Tooltip("แสดงกรอบข้อมูล Debug มุมซ้ายบนของหน้าจอขณะรันเกม")]
    public bool showDebugOverlay = false;

    [Header("=== 4. ชื่อท่าและตัวแปรใน Animator ที่ใช้ตรวจจับ ===")]
    public string[] runStateKeywords = new string[]
    {
        "Run",
        "Sprint",
        "FastRun",
        "Dash",
        "Running",
        "Sprinting",
        "Great Sword Run"
    };

    public string[] animatorSpeedFloatNames = new string[]
    {
        "Speed",
        "MoveSpeed",
        "Velocity",
        "RunSpeed",
        "ForwardSpeed"
    };

    public string[] animatorRunBoolNames = new string[]
    {
        "Sprint",
        "IsSprinting",
        "IsRunning",
        "Run",
        "Sprinting"
    };

    [Header("=== 5. รายการเอฟเฟกต์สายลมทั้ง 4 ชั้น (4-Layer Pure White Wind VFX) ===")]
    [Tooltip("Layer 1: วงแหวนคลื่นอัดอากาศรอบเอวจังหวะพุ่งตัวออกวิ่ง (One-Shot Burst)")]
    public ParticleSystem runStartSonicRingParticle;

    [Tooltip("Layer 2: สายลมสีขาวบริสุทธิ์แหวกอากาศผ่านลำตัว ใบหน้า และไหล่ (Loop Particle)")]
    public ParticleSystem bodySlipstreamParticle;

    [Tooltip("Layer 3: เส้นริบบิ้นสายลมสีขาวพริ้วติดข้อเท้าซ้าย-ขวา (Bip001 L Foot / R Foot)")]
    public TrailRenderer[] footWindTrails;

    [Tooltip("Layer 4: ละอองลมสีขาวฟุ้งกระจายใต้ฝ่าเท้าซ้าย-ขวาตามระยะก้าววิ่ง (Loop Particle)")]
    public ParticleSystem[] footWindParticles;

    [Header("=== 6. สถานะการทำงานแบบ Real-Time (Read-Only) ===")]
    [SerializeField] private bool isRunWindActive = false;
    [SerializeField] private float currentHorizontalSpeed = 0f;
    [SerializeField] private float smoothedHorizontalSpeed = 0f;
    [SerializeField] private bool isShiftHeld = false;
    [SerializeField] private bool isMovementInputHeld = false;
    [SerializeField] private string activeAnimatorStateInfo = "None";
    [SerializeField] private string activeTriggerReason = "Idle (Waiting for Run/Shift)";

    private Animator cachedAnimator;
    private CharacterController cachedCharacterController;
    private Rigidbody cachedRigidbody;
    private Vector3 previousWorldPosition;
    private bool hasInitializedPosition = false;
    private float graceTimer = 0f;
    private float burstCooldownTimer = 0f;

    private readonly HashSet<string> existingFloatParams = new HashSet<string>();
    private readonly HashSet<string> existingBoolParams = new HashSet<string>();
    private bool hasMoveXParam = false;
    private bool hasMoveYParam = false;

    private void Awake()
    {
        AutoBindReferences();
        CacheAnimatorParameters();

        previousWorldPosition = transform.position;
        hasInitializedPosition = true;
        isRunWindActive = false;

        // กฎเหล็กข้อที่ 3: ห้ามใช้ StopEmittingAndClear แล้วปล่อยให้ Particle หยุดทำงาน
        // เราจะสั่งให้ Loop Particle อยู่ในสถานะ Play() ตลอดเวลา แต่ปิดหัวฉีด Emission.enabled = false ไว้
        // เพื่อให้จังหวะที่กด Shift วิ่ง ลูกอนุภาคสามารถพ่นออกมาในเฟรมที่ 0 ได้ทันทีโดยไม่มีอาการค้าง!
        InitializeParticlesForZeroLatency();
    }

    private void OnEnable()
    {
        previousWorldPosition = transform.position;
        hasInitializedPosition = true;
    }

    public void AutoBindReferences()
    {
        if (cachedAnimator == null)
        {
            cachedAnimator = GetComponentInParent<Animator>();
            if (cachedAnimator == null)
            {
                cachedAnimator = GetComponentInChildren<Animator>(true);
            }
        }

        if (cachedCharacterController == null)
        {
            cachedCharacterController = GetComponentInParent<CharacterController>();
            if (cachedCharacterController == null)
            {
                cachedCharacterController = GetComponentInChildren<CharacterController>(true);
            }
        }

        if (cachedRigidbody == null)
        {
            cachedRigidbody = GetComponentInParent<Rigidbody>();
            if (cachedRigidbody == null)
            {
                cachedRigidbody = GetComponentInChildren<Rigidbody>(true);
            }
        }

        if (runStartSonicRingParticle == null)
        {
            Transform ringTr = FindDeepChild(transform, "VFX_RunStart_SonicRing");
            if (ringTr != null)
            {
                runStartSonicRingParticle = ringTr.GetComponent<ParticleSystem>();
            }
        }

        if (bodySlipstreamParticle == null)
        {
            Transform bodyTr = FindDeepChild(transform, "VFX_BodyWind_Slipstream");
            if (bodyTr != null)
            {
                bodySlipstreamParticle = bodyTr.GetComponent<ParticleSystem>();
            }
        }

        if (footWindTrails == null || footWindTrails.Length == 0)
        {
            footWindTrails = GetComponentsInChildren<TrailRenderer>(true);
        }

        if (footWindParticles == null || footWindParticles.Length == 0)
        {
            ParticleSystem[] allParticles = GetComponentsInChildren<ParticleSystem>(true);
            List<ParticleSystem> footList = new List<ParticleSystem>();
            for (int i = 0; i < allParticles.Length; i++)
            {
                ParticleSystem ps = allParticles[i];
                if (ps == null) continue;
                if (ps == runStartSonicRingParticle || ps == bodySlipstreamParticle) continue;
                footList.Add(ps);
            }
            footWindParticles = footList.ToArray();
        }
    }

    private void CacheAnimatorParameters()
    {
        existingFloatParams.Clear();
        existingBoolParams.Clear();
        hasMoveXParam = false;
        hasMoveYParam = false;

        if (cachedAnimator == null || cachedAnimator.runtimeAnimatorController == null)
        {
            return;
        }

        AnimatorControllerParameter[] parameters = cachedAnimator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter param = parameters[i];
            if (param.type == AnimatorControllerParameterType.Float)
            {
                existingFloatParams.Add(param.name);
                if (param.name == "MoveX") hasMoveXParam = true;
                if (param.name == "MoveY") hasMoveYParam = true;
            }
            else if (param.type == AnimatorControllerParameterType.Bool)
            {
                existingBoolParams.Add(param.name);
            }
        }
    }

    private void InitializeParticlesForZeroLatency()
    {
        // Layer 1: วงแหวน Sonic Ring เป็น One-Shot Burst จึงปิด emission ไว้และรอสั่ง Emit() ตอนเริ่มวิ่ง
        if (runStartSonicRingParticle != null)
        {
            ParticleSystem.EmissionModule ringEmission = runStartSonicRingParticle.emission;
            ringEmission.enabled = false;
            runStartSonicRingParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        // Layer 2: สายลมผ่านลำตัว ให้ Play() ค้างไว้ แต่ปิดหัวฉีด emission.enabled = false
        if (bodySlipstreamParticle != null)
        {
            ParticleSystem.EmissionModule bodyEmission = bodySlipstreamParticle.emission;
            bodyEmission.enabled = false;
            if (!bodySlipstreamParticle.isPlaying)
            {
                bodySlipstreamParticle.Play(true);
            }
        }

        // Layer 3: เส้นริบบิ้นติดข้อเท้า ปิด emitting = false
        if (footWindTrails != null)
        {
            for (int i = 0; i < footWindTrails.Length; i++)
            {
                if (footWindTrails[i] != null)
                {
                    footWindTrails[i].emitting = false;
                    footWindTrails[i].Clear();
                }
            }
        }

        // Layer 4: ละอองลมใต้ฝ่าเท้า ให้ Play() ค้างไว้ แต่ปิดหัวฉีด emission.enabled = false
        if (footWindParticles != null)
        {
            for (int i = 0; i < footWindParticles.Length; i++)
            {
                ParticleSystem ps = footWindParticles[i];
                if (ps != null)
                {
                    ParticleSystem.EmissionModule footEmission = ps.emission;
                    footEmission.enabled = false;
                    if (!ps.isPlaying)
                    {
                        ps.Play(true);
                    }
                }
            }
        }
    }

    // กฎเหล็กข้อที่ 1 (ช่องทางที่ 4): คำนวณความเร็วและสถานะทั้งหมดใน LateUpdate()
    // เพื่อให้มั่นใจว่า PlayerController, CharacterController หรือ NetworkTransform ได้อัปเดตตำแหน่งในเฟรมนี้เสร็จเรียบร้อยแล้ว
    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0.00001f)
        {
            return;
        }

        if (burstCooldownTimer > 0f)
        {
            burstCooldownTimer -= dt;
        }

        // ตรวจสอบปุ่มลัด F1 สำหรับสลับโหมด Force Test On ทันทีในขณะรันเกม
        CheckForceTestToggleHotkey();

        // 1. คำนวณความเร็วเคลื่อนที่จริงในแนวราบ (3D Horizontal Velocity) หลัง CharacterController ขยับแล้ว
        UpdateRealHorizontalSpeed(dt);

        // 2. อ่านค่าปุ่ม Shift และปุ่มเดิน WASD ทั้งจาก New Input System และ Legacy Input
        ReadShiftAndMovementInputs();

        // 3. ประมวลผลระบบตรวจจับการวิ่งทั้ง 5 ช่องทาง
        string detectedReason;
        bool shouldSprintNow = Evaluate5LayerRunDetection(out detectedReason);

        if (shouldSprintNow)
        {
            graceTimer = stopGraceDuration;
            activeTriggerReason = detectedReason;
            if (!isRunWindActive)
            {
                SetRunWindState(true);
            }
            else
            {
                EnsureLoopParticlesRemainAlive();
            }
        }
        else
        {
            if (graceTimer > 0f)
            {
                graceTimer -= dt;
            }
            else
            {
                activeTriggerReason = "Idle / Walk (Below Threshold)";
                if (isRunWindActive)
                {
                    SetRunWindState(false);
                }
            }
        }
    }

    private void CheckForceTestToggleHotkey()
    {
        if (toggleForceTestKey == KeyCode.None)
        {
            return;
        }

        try
        {
            if (Input.GetKeyDown(toggleForceTestKey))
            {
                forceTestOn = !forceTestOn;
            }
        }
        catch
        {
            // Ignore if legacy Input Manager is disabled in Player Settings
        }
    }

    private void UpdateRealHorizontalSpeed(float dt)
    {
        if (!hasInitializedPosition)
        {
            previousWorldPosition = transform.position;
            hasInitializedPosition = true;
        }

        Vector3 currentPos = transform.position;
        Vector3 horizontalDelta = new Vector3(currentPos.x - previousWorldPosition.x, 0f, currentPos.z - previousWorldPosition.z);
        float transformSpeed = horizontalDelta.magnitude / dt;
        previousWorldPosition = currentPos;

        float controllerSpeed = 0f;
        if (cachedCharacterController != null)
        {
            Vector3 ccVel = cachedCharacterController.velocity;
            controllerSpeed = new Vector3(ccVel.x, 0f, ccVel.z).magnitude;
        }

        float rigidbodySpeed = 0f;
        if (cachedRigidbody != null && !cachedRigidbody.isKinematic)
        {
            Vector3 rbVel = cachedRigidbody.linearVelocity;
            rigidbodySpeed = new Vector3(rbVel.x, 0f, rbVel.z).magnitude;
        }

        currentHorizontalSpeed = Mathf.Max(transformSpeed, Mathf.Max(controllerSpeed, rigidbodySpeed));
        smoothedHorizontalSpeed = Mathf.Lerp(smoothedHorizontalSpeed, currentHorizontalSpeed, Mathf.Clamp01(dt * 18f));
    }

    private void ReadShiftAndMovementInputs()
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

        bool shiftFromLegacy = false;
        bool moveFromLegacy = false;
        try
        {
            shiftFromLegacy = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            moveFromLegacy =
                Input.GetKey(KeyCode.W) ||
                Input.GetKey(KeyCode.A) ||
                Input.GetKey(KeyCode.S) ||
                Input.GetKey(KeyCode.D) ||
                Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.1f ||
                Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.1f;
        }
        catch
        {
            // Ignore if legacy Input is disabled
        }

        isShiftHeld = shiftFromNewInput || shiftFromLegacy;
        isMovementInputHeld = moveFromNewInput || moveFromLegacy;
    }

    private bool Evaluate5LayerRunDetection(out string reason)
    {
        // อัปเดตข้อมูลชื่อ State ปัจจุบันไว้แสดงบน Debug Overlay เสมอ
        bool stateMatchesRun = InspectAnimatorState(out activeAnimatorStateInfo);

        // ช่องทางที่ 5: โหมดบังคับเปิดทดสอบ (Force Test On หรือปุ่ม F1)
        if (forceTestOn)
        {
            reason = "Channel 5: Force Test ON (F1)";
            return true;
        }

        // ช่องทางที่ 1: ตรวจจับการกดปุ่ม Left Shift / Right Shift
        if (detectShiftKey && isShiftHeld)
        {
            if (triggerOnShiftImmediately)
            {
                reason = "Channel 1: Shift Key Pressed (Immediate Mode)";
                return true;
            }

            if (isMovementInputHeld || smoothedHorizontalSpeed > 0.15f)
            {
                reason = "Channel 1: Shift Key + Moving (" + smoothedHorizontalSpeed.ToString("F2") + " m/s)";
                return true;
            }
        }

        // ช่องทางที่ 2: ตรวจจับค่า Parameter ใน Animator (Speed, MoveSpeed, MoveX/MoveY, IsSprinting)
        if (detectAnimatorParameters && cachedAnimator != null && cachedAnimator.isActiveAndEnabled)
        {
            if (existingFloatParams.Count == 0 && existingBoolParams.Count == 0)
            {
                CacheAnimatorParameters();
            }

            // 2.1 ตรวจจับ Bool Parameters เช่น Sprint, IsSprinting, IsRunning
            for (int i = 0; i < animatorRunBoolNames.Length; i++)
            {
                string boolName = animatorRunBoolNames[i];
                if (existingBoolParams.Contains(boolName) && cachedAnimator.GetBool(boolName))
                {
                    reason = "Channel 2: Animator Bool [" + boolName + " = true]";
                    return true;
                }
            }

            // 2.2 ตรวจจับ Float Parameters เช่น Speed, MoveSpeed, Velocity
            for (int i = 0; i < animatorSpeedFloatNames.Length; i++)
            {
                string floatName = animatorSpeedFloatNames[i];
                if (existingFloatParams.Contains(floatName))
                {
                    float val = cachedAnimator.GetFloat(floatName);
                    if (val >= animatorFloatRunThreshold)
                    {
                        reason = "Channel 2: Animator Float [" + floatName + " = " + val.ToString("F2") + "]";
                        return true;
                    }
                }
            }

            // 2.3 ตรวจจับ 2D BlendTree Parameters (MoveX, MoveY) ที่ใช้ใน GreatSwordGirl / PlayerAnimator
            if (hasMoveXParam && hasMoveYParam)
            {
                float mx = cachedAnimator.GetFloat("MoveX");
                float my = cachedAnimator.GetFloat("MoveY");
                float blendMag = Mathf.Sqrt(mx * mx + my * my);

                // หาก BlendTree ส่งค่าเกินเกณฑ์วิ่ง หรือกด Shift ร่วมกับ BlendTree ขยับ
                if (blendMag >= animatorFloatRunThreshold || (isShiftHeld && blendMag > 0.25f) || (blendMag >= normalizedBlendRunThreshold && smoothedHorizontalSpeed >= minRunSpeedThreshold))
                {
                    reason = "Channel 2: BlendTree MoveXY (" + blendMag.ToString("F2") + ")";
                    return true;
                }
            }
        }

        // ช่องทางที่ 3: ตรวจจับชื่อท่าอนิเมชั่นใน Animator State / Next State
        if (detectAnimatorStateName && stateMatchesRun)
        {
            reason = "Channel 3: Animator State [" + activeAnimatorStateInfo + "]";
            return true;
        }

        // ช่องทางที่ 4: ตรวจจับความเร็วการเคลื่อนที่จริงในโลก 3D (Horizontal Velocity m/s)
        if (detectTransformVelocity && smoothedHorizontalSpeed >= minRunSpeedThreshold)
        {
            reason = "Channel 4: 3D Velocity (" + smoothedHorizontalSpeed.ToString("F2") + " >= " + minRunSpeedThreshold.ToString("F2") + " m/s)";
            return true;
        }

        reason = "None";
        return false;
    }

    private bool InspectAnimatorState(out string stateSummary)
    {
        stateSummary = "No Animator";
        if (cachedAnimator == null || !cachedAnimator.isActiveAndEnabled || cachedAnimator.runtimeAnimatorController == null)
        {
            return false;
        }

        int layerCount = cachedAnimator.layerCount;
        for (int layer = 0; layer < layerCount; layer++)
        {
            AnimatorStateInfo currentInfo = cachedAnimator.GetCurrentAnimatorStateInfo(layer);
            if (layer == 0)
            {
                stateSummary = GetReadableStateLabel(currentInfo);
            }

            if (MatchesAnyRunKeyword(currentInfo))
            {
                stateSummary = GetReadableStateLabel(currentInfo);
                return true;
            }

            if (cachedAnimator.IsInTransition(layer))
            {
                AnimatorStateInfo nextInfo = cachedAnimator.GetNextAnimatorStateInfo(layer);
                if (MatchesAnyRunKeyword(nextInfo))
                {
                    stateSummary = "Transition -> " + GetReadableStateLabel(nextInfo);
                    return true;
                }
            }
        }

        return false;
    }

    private bool MatchesAnyRunKeyword(AnimatorStateInfo stateInfo)
    {
        if (runStateKeywords == null) return false;

        for (int i = 0; i < runStateKeywords.Length; i++)
        {
            string kw = runStateKeywords[i];
            if (string.IsNullOrEmpty(kw)) continue;

            if (stateInfo.IsName(kw) ||
                stateInfo.IsName("Base Layer." + kw) ||
                stateInfo.IsName("Locomotion." + kw) ||
                stateInfo.IsName("Great Sword Locomotion." + kw))
            {
                return true;
            }
        }

        return false;
    }

    private string GetReadableStateLabel(AnimatorStateInfo stateInfo)
    {
        if (runStateKeywords != null)
        {
            for (int i = 0; i < runStateKeywords.Length; i++)
            {
                string kw = runStateKeywords[i];
                if (!string.IsNullOrEmpty(kw) && stateInfo.IsName(kw))
                {
                    return kw;
                }
            }
        }

        if (stateInfo.IsName("Locomotion")) return "Locomotion (BlendTree)";
        if (stateInfo.IsName("Great Sword Locomotion")) return "Great Sword Locomotion (BlendTree)";
        if (stateInfo.IsName("Idle")) return "Idle";
        if (stateInfo.IsName("Walk")) return "Walk";

        return "StateHash:" + stateInfo.shortNameHash;
    }

    // กฎเหล็กข้อที่ 3: สั่งเปิด/ปิดด้วย EmissionModule.enabled และ TrailRenderer.emitting
    // เพื่อให้แสดงผลทันทีในเฟรมแรกที่กด Shift วิ่งโดยไม่มีอาการ Particle ค้างหรือดับถาวร
    public void SetRunWindState(bool active)
    {
        isRunWindActive = active;

        // Layer 1: วงแหวนคลื่นอัดอากาศรอบเอว (One-Shot Burst จังหวะออกตัววิ่ง)
        if (runStartSonicRingParticle != null)
        {
            if (active && burstCooldownTimer <= 0f)
            {
                runStartSonicRingParticle.Play(true);
                runStartSonicRingParticle.Emit(1);
                burstCooldownTimer = 0.25f;
            }
        }

        // Layer 2: สายลมสีขาวแหวกอากาศผ่านลำตัว ใบหน้า และไหล่
        if (bodySlipstreamParticle != null)
        {
            if (!bodySlipstreamParticle.isPlaying)
            {
                bodySlipstreamParticle.Play(true);
            }
            ParticleSystem.EmissionModule bodyEmission = bodySlipstreamParticle.emission;
            bodyEmission.enabled = active;
        }

        // Layer 3: เส้นริบบิ้นสายลมติดข้อเท้าซ้าย-ขวา (Bip001 L Foot / R Foot)
        if (footWindTrails != null)
        {
            for (int i = 0; i < footWindTrails.Length; i++)
            {
                TrailRenderer trail = footWindTrails[i];
                if (trail != null)
                {
                    trail.emitting = active;
                }
            }
        }

        // Layer 4: ละอองลมสีขาวฟุ้งใต้ฝ่าเท้าซ้าย-ขวา
        if (footWindParticles != null)
        {
            for (int i = 0; i < footWindParticles.Length; i++)
            {
                ParticleSystem ps = footWindParticles[i];
                if (ps != null)
                {
                    if (!ps.isPlaying)
                    {
                        ps.Play(true);
                    }
                    ParticleSystem.EmissionModule footEmission = ps.emission;
                    footEmission.enabled = active;
                }
            }
        }
    }

    private void EnsureLoopParticlesRemainAlive()
    {
        if (bodySlipstreamParticle != null)
        {
            if (!bodySlipstreamParticle.isPlaying)
            {
                bodySlipstreamParticle.Play(true);
            }
            ParticleSystem.EmissionModule bodyEmission = bodySlipstreamParticle.emission;
            if (!bodyEmission.enabled)
            {
                bodyEmission.enabled = true;
            }
        }

        if (footWindParticles != null)
        {
            for (int i = 0; i < footWindParticles.Length; i++)
            {
                ParticleSystem ps = footWindParticles[i];
                if (ps != null)
                {
                    if (!ps.isPlaying)
                    {
                        ps.Play(true);
                    }
                    ParticleSystem.EmissionModule footEmission = ps.emission;
                    if (!footEmission.enabled)
                    {
                        footEmission.enabled = true;
                    }
                }
            }
        }

        if (footWindTrails != null)
        {
            for (int i = 0; i < footWindTrails.Length; i++)
            {
                if (footWindTrails[i] != null && !footWindTrails[i].emitting)
                {
                    footWindTrails[i].emitting = true;
                }
            }
        }
    }

    // ลบหน้าต่าง OnGUI Debug Overlay ออกตามคำขอเพื่อให้หน้าจอเกมสะอาด 100%

    private static Transform FindDeepChild(Transform parent, string targetName)
    {
        if (parent == null) return null;

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
}
