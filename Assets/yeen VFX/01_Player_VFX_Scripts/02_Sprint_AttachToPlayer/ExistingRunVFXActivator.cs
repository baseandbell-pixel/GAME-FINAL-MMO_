using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// สคริปต์ปลุกและควบคุมเอฟเฟกต์วิ่งที่มีอยู่แล้วในตัวละคร (Sprint Wind, Body Slipstream, Foot Trails)
/// สำหรับ Unity 6 (URP) - ค้นหาอัตโนมัติ ซ่อมบั๊ก Particle Velocity Curve และสั่งเปิด-ปิดทันทีเมื่อกด Shift
/// </summary>
[AddComponentMenu("yeen VFX/02D. [Player] ตัวเปิดเอฟเฟกต์วิ่งเดิม (Run VFX Activator)")]
[DisallowMultipleComponent]
public class ExistingRunVFXActivator : MonoBehaviour
{
    [Header("=== 1. รายการเอฟเฟกต์วิ่งในตัวละคร (ลากใส่เองได้ หรือปล่อยว่างให้ค้นหาอัตโนมัติ) ===")]
    [Tooltip("ลาก GameObject เอฟเฟกต์วิ่งใส่ช่องนี้ได้ตามต้องการ หรือปล่อยว่างไว้ สคริปต์จะกวาดหาในตัวละครให้อัตโนมัติตอนเริ่มเกม")]
    public List<GameObject> manualVFXObjects = new List<GameObject>();

    [Tooltip("คำค้นหาชื่อ GameObject เอฟเฟกต์วิ่งทั้งหมดใน Hierarchy ของตัวละคร")]
    public string[] autoSearchKeywords = new string[]
    {
        "Sprint",
        "Wind",
        "Trail",
        "Run",
        "Slipstream",
        "SonicRing"
    };

    [Tooltip("ชื่อกระดูกเท้าซ้ายและขวาสำหรับกวาดหาเอฟเฟกต์ใต้ข้อเท้าโดยตรง")]
    public string leftFootBoneName = "Bip001 L Foot";
    public string rightFootBoneName = "Bip001 R Foot";

    [Header("=== 2. โหมดการสั่งเปิดเอฟเฟกต์เมื่อกดปุ่ม Shift วิ่ง (Trigger Modes) ===")]
    [Tooltip("หากเอาติ๊กออก (false = ค่าเริ่มต้น): ต้องกด Shift ค้างร่วมกับตัวละครขยับวิ่ง หรือเล่นอนิเมชั่นวิ่งเท่านั้น (เดินปกติจะไม่ขึ้นเอฟเฟกต์)\nหากติ๊กถูก (true): แค่กด Shift ยืนเฉยๆ เอฟเฟกต์ก็จะขึ้นทันที")]
    public bool triggerOnShiftOnly = false;

    [Tooltip("ให้เอฟเฟกต์ทำงานอัตโนมัติเมื่อ Animator อยู่ในท่าวิ่ง (BlendTree MoveXY >= 1.55)")]
    public bool alsoTriggerOnAnimatorRunState = true;

    [Tooltip("ความเร็วขั้นต่ำ (m/s) ที่ถือว่าตัวละครกำลังขยับเคลื่อนที่")]
    [Range(0.05f, 4.5f)]
    public float minMoveSpeedThreshold = 0.25f;

    [Tooltip("ความเร็วขั้นต่ำ (m/s) ที่ถือว่าตัวละครกำลังวิ่งเต็มสปีด")]
    [Range(3.8f, 12.0f)]
    public float fullRunSpeedThreshold = 4.2f;

    [Tooltip("เวลาหน่วงสั้นๆ ก่อนปิดเอฟเฟกต์เมื่อปล่อยปุ่ม Shift (วินาที) ช่วยให้สายลมค่อยๆ จางหายอย่างนุ่มนวล")]
    [Range(0.0f, 0.5f)]
    public float stopGraceDuration = 0.12f;

    [Header("=== 3. การซ่อมและปรับตั้งค่า Particle อัตโนมัติตอนเริ่มเกม (Runtime Fixer) ===")]
    [Tooltip("บังคับซ่อม Velocity Over Lifetime ทุกแกน (X,Y,Z) ให้เป็นโหมด Constant เหมือนกัน เพื่อแก้บั๊ก Particle ค้างใน Unity 6")]
    public bool fixVelocityCurveModeErrors = true;

    [Tooltip("บังคับเปลี่ยน Simulation Space เป็น World เพื่อให้สายลมลอยทิ้งไว้ในอากาศตามเส้นทางที่วิ่งผ่าน")]
    public bool forceWorldSimulationSpace = true;

    [Tooltip("กรณีที่เอฟเฟกต์เดิมในตัวละครมี Rate Over Time = 0 ให้สคริปต์เติมค่าอัตราการพ่นขั้นต่ำให้อัตโนมัติเพื่อให้มีเม็ดลมพุ่งออกมา")]
    public float fallbackEmissionRateOverTime = 45f;

    [Header("=== 4. หน้าจอ Debug HUD มุมซ้ายบน & ปุ่มลัดทดสอบ F1 ===")]
    [Tooltip("แสดงกรอบข้อมูล Debug เล็กๆ มุมซ้ายบนของจอเกม (เอาติ๊กออกเพื่อซ่อนเมื่อใช้งานจริง)")]
    public bool showDebugHUD = true;

    [Tooltip("ติ๊กถูก (หรือกดปุ่ม F1 ขณะรันเกม) เพื่อบังคับเปิดเอฟเฟกต์วิ่งทั้งหมดค้างไว้ทันที 100%")]
    public bool forceTestActive = false;

    [Tooltip("ปุ่มลัดสำหรับสลับบังคับเปิด/ปิดเอฟเฟกต์ขณะอยู่ใน Play Mode")]
    public KeyCode toggleForceTestKey = KeyCode.F1;

    [Header("=== 5. สถานะที่ตรวจพบในระบบ (Read-Only Diagnostics) ===")]
    [SerializeField] private bool isVFXCurrentlyActive = false;
    [SerializeField] private bool isShiftKeyPressed = false;
    [SerializeField] private bool isCharacterMoving = false;
    [SerializeField] private bool isAnimatorInRunState = false;
    [SerializeField] private float currentHorizontalSpeed = 0f;
    [SerializeField] private int discoveredObjectCount = 0;
    [SerializeField] private int discoveredParticleCount = 0;
    [SerializeField] private int discoveredTrailCount = 0;
    [SerializeField] private List<ParticleSystem> controlledParticles = new List<ParticleSystem>();
    [SerializeField] private List<TrailRenderer> controlledTrails = new List<TrailRenderer>();

    private Animator cachedAnimator;
    private CharacterController cachedCharacterController;
    private Rigidbody cachedRigidbody;
    private Vector3 previousWorldPosition;
    private bool hasInitializedPosition = false;
    private float graceTimer = 0f;

    private static readonly string[] ExcludedSwordKeywords = new string[]
    {
        "Sword",
        "Slash",
        "Blade",
        "Weapon",
        "Scabbard",
        "Attack",
        "Hit"
    };

    private static readonly string[] AnimatorRunKeywords = new string[]
    {
        "Run",
        "Sprint",
        "FastRun",
        "Dash",
        "Running",
        "Sprinting"
    };

    private void Awake()
    {
        cachedAnimator = GetComponentInParent<Animator>();
        if (cachedAnimator == null) cachedAnimator = GetComponentInChildren<Animator>(true);

        cachedCharacterController = GetComponentInParent<CharacterController>();
        if (cachedCharacterController == null) cachedCharacterController = GetComponentInChildren<CharacterController>(true);

        cachedRigidbody = GetComponentInParent<Rigidbody>();
        if (cachedRigidbody == null) cachedRigidbody = GetComponentInChildren<Rigidbody>(true);

        // ปิดสคริปต์ควบคุมลมวิ่งตัวเก่าที่อาจซ้ำซ้อนบนตัวละครเดียวกัน เพื่อไม่ให้แย่งกันสั่งปิด Emission
        DisableConflictingLegacySprintScripts();

        // กฎเหล็กข้อที่ 1 & 2 & 3: ค้นหาเอฟเฟกต์ที่มีอยู่แล้วทั้งหมด ปลุก GameObject และซ่อม Particle/TrailRenderer
        DiscoverAndWakeUpAllExistingRunVFX();

        previousWorldPosition = transform.position;
        hasInitializedPosition = true;
    }

    private void Start()
    {
        // เริ่มต้นเกมด้วยการเตรียมให้ Particle เล่นค้างไว้แต่ปิดหัวฉีด Emission ไว้รอรับคำสั่งกด Shift ทันทีในเฟรมที่ 0
        SetAllRunVFXActive(false, true);
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0.00001f)
        {
            return;
        }

        // 1. ตรวจสอบปุ่มลัด F1 สำหรับสลับโหมดบังคับเปิดทดสอบทันที
        CheckForceTestHotkey();

        // 2. คำนวณความเร็วเคลื่อนที่จริงในโลก 3D (Horizontal Speed) หลัง CharacterController อัปเดตแล้ว
        UpdateHorizontalSpeed(dt);

        // 3. กฎเหล็กข้อที่ 4: ตรวจจับปุ่ม Left Shift / Right Shift ทั้งจากระบบใหม่และระบบเก่าพร้อมกัน
        bool moveInputHeld;
        isShiftKeyPressed = DetectShiftAndMovementInput(out moveInputHeld);
        isCharacterMoving = moveInputHeld || (currentHorizontalSpeed >= minMoveSpeedThreshold);
        isAnimatorInRunState = CheckAnimatorRunStateOrParameters();

        // 4. ประมวลผลเงื่อนไขการเปิดเอฟเฟกต์ตามโหมดที่เลือก
        bool shouldActivate = false;

        if (forceTestActive)
        {
            shouldActivate = true;
        }
        else if (triggerOnShiftOnly)
        {
            // โหมดที่ 1: กด Shift ค้างปุ๊บ เอฟเฟกต์ทำงานปั๊บ ไม่ต้องรอเช็กเงื่อนไขอื่น
            shouldActivate = isShiftKeyPressed;
        }
        else
        {
            // โหมดที่ 2 (ค่าเริ่มต้น): เดินปกติจะไม่ขึ้นเอฟเฟกต์เด็ดขาด! จะทำงานเฉพาะตอนกด Shift + วิ่งเคลื่อนที่ หรือ Animator อยู่ในท่าวิ่ง
            if (isShiftKeyPressed && isCharacterMoving)
            {
                shouldActivate = true;
            }
            else if (alsoTriggerOnAnimatorRunState && isAnimatorInRunState)
            {
                shouldActivate = true;
            }
        }

        // 5. สั่งเปิด-ปิดหัวฉีด Emission และ TrailRenderer อย่างนุ่มนวล
        if (shouldActivate)
        {
            graceTimer = stopGraceDuration;
            if (!isVFXCurrentlyActive)
            {
                SetAllRunVFXActive(true, false);
            }
            else
            {
                EnsureActiveVFXStaysEmitting();
            }
        }
        else
        {
            if (graceTimer > 0f)
            {
                graceTimer -= dt;
            }
            else if (isVFXCurrentlyActive)
            {
                SetAllRunVFXActive(false, false);
            }
        }
    }

    // ================================================================================================
    // กฎเหล็กข้อที่ 1 & 2 & 3: ค้นหาเอฟเฟกต์ที่มีอยู่แล้วอัตโนมัติ ปลุกทุกเลเยอร์ และซ่อมบั๊ก Particle
    // ================================================================================================
    [ContextMenu("ค้นหาและปลุกเอฟเฟกต์วิ่งทั้งหมดในตัวละครทันที (Discover & Wake Up Run VFX)")]
    public void DiscoverAndWakeUpAllExistingRunVFX()
    {
        HashSet<GameObject> candidateRoots = new HashSet<GameObject>();
        controlledParticles.Clear();
        controlledTrails.Clear();

        // 1.1 ดึงรายการจากช่อง Manual ที่ผู้ใช้ลากใส่เอง (ถ้ามี)
        for (int i = 0; i < manualVFXObjects.Count; i++)
        {
            if (manualVFXObjects[i] != null)
            {
                candidateRoots.Add(manualVFXObjects[i]);
            }
        }

        // 1.2 ค้นหาใต้กระดูกข้อเท้าซ้าย (Bip001 L Foot) และข้อเท้าขวา (Bip001 R Foot) โดยตรง
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

        CollectVFXChildrenUnderBone(leftFoot, candidateRoots);
        CollectVFXChildrenUnderBone(rightFoot, candidateRoots);

        // 1.3 กวาดหาลูกหลานทั้งหมดในตัวละครที่มีชื่อตรงกับคำว่า Sprint, Wind, Trail, Run, Slipstream
        Transform[] allChildren = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < allChildren.Length; i++)
        {
            Transform child = allChildren[i];
            if (child == null || child == transform) continue;

            if (MatchesRunWindName(child.name))
            {
                candidateRoots.Add(child.gameObject);
            }
        }

        // 2. ปลุก GameObject แม่-ลูกทุกชั้น และรวบรวม ParticleSystem + TrailRenderer ทั้งหมดมาควบคุม
        foreach (GameObject vfxRoot in candidateRoots)
        {
            if (vfxRoot == null) continue;

            // เปิด SetActive(true) ย้อนขึ้นไปหาตัวแม่จนถึงตัวละครหลัก เพื่อให้แน่ใจว่าไม่มีโหนดไหนถูกปิดซ่อนอยู่
            ActivateHierarchyChain(vfxRoot.transform, transform);

            ParticleSystem[] particles = vfxRoot.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particles.Length; i++)
            {
                ParticleSystem ps = particles[i];
                if (ps != null && !controlledParticles.Contains(ps))
                {
                    ActivateHierarchyChain(ps.transform, transform);
                    RepairAndPrepareParticleSystem(ps);
                    controlledParticles.Add(ps);
                }
            }

            TrailRenderer[] trails = vfxRoot.GetComponentsInChildren<TrailRenderer>(true);
            for (int i = 0; i < trails.Length; i++)
            {
                TrailRenderer tr = trails[i];
                if (tr != null && !controlledTrails.Contains(tr))
                {
                    ActivateHierarchyChain(tr.transform, transform);
                    PrepareTrailRenderer(tr);
                    controlledTrails.Add(tr);
                }
            }
        }

        // 2.1 หากใน manualVFXObjects ยังว่างอยู่ ให้ใส่รายการที่ค้นพบลงไปเพื่อให้ผู้ใช้มองเห็นใน Inspector ชัดเจน
        foreach (GameObject root in candidateRoots)
        {
            if (root != null && !manualVFXObjects.Contains(root))
            {
                manualVFXObjects.Add(root);
            }
        }

        discoveredObjectCount = candidateRoots.Count;
        discoveredParticleCount = controlledParticles.Count;
        discoveredTrailCount = controlledTrails.Count;

        // 3. หากในตัวละครที่ถูก Spawn ออกมายังไม่มีเอฟเฟกต์ติดอยู่เลย (0 ชิ้น เพราะลืมกด Apply Overrides ลง Prefab)
        // ให้เรียก RunWindSlipstream สร้างเอฟเฟกต์สายลม 3 เลเยอร์ขึ้นมาให้โดยอัตโนมัติ เพื่อให้มีเอฟเฟกต์วิ่งแสดงผล 100%
        if (Application.isPlaying && discoveredParticleCount == 0 && discoveredTrailCount == 0)
        {
            RunWindSlipstream autoGenerator = GetComponent<RunWindSlipstream>();
            if (autoGenerator == null)
            {
                autoGenerator = gameObject.AddComponent<RunWindSlipstream>();
            }
            autoGenerator.enabled = true;
        }
    }

    private void CollectVFXChildrenUnderBone(Transform bone, HashSet<GameObject> outRoots)
    {
        if (bone == null) return;

        ParticleSystem[] boneParticles = bone.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < boneParticles.Length; i++)
        {
            if (boneParticles[i] != null)
            {
                outRoots.Add(boneParticles[i].gameObject);
            }
        }

        TrailRenderer[] boneTrails = bone.GetComponentsInChildren<TrailRenderer>(true);
        for (int i = 0; i < boneTrails.Length; i++)
        {
            if (boneTrails[i] != null)
            {
                outRoots.Add(boneTrails[i].gameObject);
            }
        }
    }

    private bool MatchesRunWindName(string objName)
    {
        if (string.IsNullOrEmpty(objName)) return false;

        // ข้ามเอฟเฟกต์ของดาบหรือการฟันอาวุธ เพื่อไม่ให้เอฟเฟกต์ฟันดาบติดขึ้นมาตอนวิ่ง
        for (int i = 0; i < ExcludedSwordKeywords.Length; i++)
        {
            if (objName.IndexOf(ExcludedSwordKeywords[i], StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }
        }

        if (autoSearchKeywords == null) return false;

        for (int i = 0; i < autoSearchKeywords.Length; i++)
        {
            string kw = autoSearchKeywords[i];
            if (!string.IsNullOrEmpty(kw) && objName.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static void ActivateHierarchyChain(Transform target, Transform stopAtRoot)
    {
        Transform curr = target;
        while (curr != null && curr != stopAtRoot)
        {
            if (!curr.gameObject.activeSelf)
            {
                curr.gameObject.SetActive(true);
            }
            curr = curr.parent;
        }
    }

    // ================================================================================================
    // กฎเหล็กข้อที่ 3: ซ่อมข้อผิดพลาดของ Particle อัตโนมัติตอนรันไทม์ (Velocity Curve Modes & World Space)
    // ================================================================================================
    private void RepairAndPrepareParticleSystem(ParticleSystem ps)
    {
        if (ps == null) return;

        // 3.1 ซ่อมบั๊ก "Particle Velocity curves must all be in the same mode" ใน Unity 6
        if (fixVelocityCurveModeErrors)
        {
            ParticleSystem.VelocityOverLifetimeModule vel = ps.velocityOverLifetime;
            if (vel.enabled)
            {
                float constX = ExtractConstantVelocityValue(vel.x);
                float constY = ExtractConstantVelocityValue(vel.y);
                float constZ = ExtractConstantVelocityValue(vel.z);

                ParticleSystem.MinMaxCurve cX = new ParticleSystem.MinMaxCurve(constX);
                cX.mode = ParticleSystemCurveMode.Constant;
                ParticleSystem.MinMaxCurve cY = new ParticleSystem.MinMaxCurve(constY);
                cY.mode = ParticleSystemCurveMode.Constant;
                ParticleSystem.MinMaxCurve cZ = new ParticleSystem.MinMaxCurve(constZ);
                cZ.mode = ParticleSystemCurveMode.Constant;

                vel.x = cX;
                vel.y = cY;
                vel.z = cZ;

                ParticleSystem.MinMaxCurve zeroConst = new ParticleSystem.MinMaxCurve(0f);
                zeroConst.mode = ParticleSystemCurveMode.Constant;
                vel.orbitalX = zeroConst;
                vel.orbitalY = zeroConst;
                vel.orbitalZ = zeroConst;
                vel.orbitalOffsetX = zeroConst;
                vel.orbitalOffsetY = zeroConst;
                vel.orbitalOffsetZ = zeroConst;
                vel.radial = zeroConst;

                ParticleSystem.MinMaxCurve oneConst = new ParticleSystem.MinMaxCurve(1f);
                oneConst.mode = ParticleSystemCurveMode.Constant;
                vel.speedModifier = oneConst;
            }
        }

        // 3.2 บังคับให้เป็น Loop และเปลี่ยน Simulation Space เป็น World ตามกฎเหล็กข้อที่ 3
        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        if (forceWorldSimulationSpace)
        {
            main.simulationSpace = ParticleSystemSimulationSpace.World;
        }

        // 3.3 ตรวจสอบว่ามีอัตราการปล่อยเม็ดลม (Rate Over Time / Rate Over Distance) หรือไม่
        // หากเดิมถูกตั้งเป็น 0 ทั้งคู่ ให้เติมค่า fallbackEmissionRateOverTime เพื่อไม่ให้ Particle พ่นไม่ออก
        ParticleSystem.EmissionModule emission = ps.emission;
        float currentRateTime = emission.rateOverTime.constant;
        float currentRateDist = emission.rateOverDistance.constant;
        if (currentRateTime <= 0.01f && currentRateDist <= 0.01f && emission.burstCount == 0)
        {
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(fallbackEmissionRateOverTime);
        }

        // 3.4 ตรวจสอบ Material ของ ParticleRenderer หากสูญหายหรือ Shader ล่องหน ให้ซ่อมด้วย Sprites/Default ทันที
        ParticleSystemRenderer psRenderer = ps.GetComponent<ParticleSystemRenderer>();
        if (psRenderer != null)
        {
            EnsureValidVisibleMaterial(psRenderer);
        }

        // กฎเหล็กข้อที่ 2: สั่ง ps.Play(true) ค้างไว้ และปิดหัวฉีด emission.enabled = false รอไว้
        emission.enabled = false;
        if (!ps.isPlaying)
        {
            ps.Play(true);
        }
    }

    private static float ExtractConstantVelocityValue(ParticleSystem.MinMaxCurve curve)
    {
        switch (curve.mode)
        {
            case ParticleSystemCurveMode.Constant:
                return curve.constant;
            case ParticleSystemCurveMode.TwoConstants:
                return (curve.constantMin + curve.constantMax) * 0.5f;
            default:
                return curve.constant;
        }
    }

    private void PrepareTrailRenderer(TrailRenderer tr)
    {
        if (tr == null) return;

        tr.autodestruct = false;
        tr.emitting = false;
        tr.Clear();

        if (tr.time <= 0.01f)
        {
            tr.time = 0.28f;
        }
        if (tr.widthMultiplier <= 0.005f)
        {
            tr.widthMultiplier = 0.25f;
        }

        EnsureValidVisibleMaterial(tr);
    }

    private static void EnsureValidVisibleMaterial(Renderer rend)
    {
        if (rend == null) return;

        if (rend.sharedMaterial == null || rend.sharedMaterial.shader == null)
        {
            Shader fallbackShader = Shader.Find("Sprites/Default");
            if (fallbackShader == null) fallbackShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (fallbackShader != null)
            {
                Material mat = new Material(fallbackShader);
                mat.name = "MAT_AutoRepairedRunWind";
                mat.color = new Color(1f, 1f, 1f, 0.85f);
                rend.sharedMaterial = mat;
            }
        }
    }

    // ================================================================================================
    // กฎเหล็กข้อที่ 2: ควบคุมการเปิด-ปิดด้วย emission.enabled และ trailRenderer.emitting
    // ================================================================================================
    public void SetAllRunVFXActive(bool active, bool clearOnStop)
    {
        isVFXCurrentlyActive = active;

        for (int i = 0; i < controlledParticles.Count; i++)
        {
            ParticleSystem ps = controlledParticles[i];
            if (ps == null) continue;

            if (!ps.gameObject.activeInHierarchy)
            {
                ActivateHierarchyChain(ps.transform, transform);
            }

            if (!ps.isPlaying)
            {
                ps.Play(true);
            }

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = active;

            // หากมี Burst ตั้งไว้ใน Particle (เช่น Sonic Ring ตอนออกตัว) ให้สั่ง Emit ทันทีที่เริ่มกด Shift
            if (active && emission.burstCount > 0)
            {
                ParticleSystem.Burst burst = emission.GetBurst(0);
                int burstCount = Mathf.Max(1, (int)burst.count.constant);
                ps.Emit(burstCount);
            }

            if (!active && clearOnStop)
            {
                ps.Clear(true);
            }
        }

        for (int i = 0; i < controlledTrails.Count; i++)
        {
            TrailRenderer tr = controlledTrails[i];
            if (tr == null) continue;

            if (!tr.gameObject.activeInHierarchy)
            {
                ActivateHierarchyChain(tr.transform, transform);
            }

            tr.emitting = active;
            if (!active && clearOnStop)
            {
                tr.Clear();
            }
        }
    }

    private void EnsureActiveVFXStaysEmitting()
    {
        for (int i = 0; i < controlledParticles.Count; i++)
        {
            ParticleSystem ps = controlledParticles[i];
            if (ps == null) continue;

            if (!ps.isPlaying)
            {
                ps.Play(true);
            }

            ParticleSystem.EmissionModule emission = ps.emission;
            if (!emission.enabled)
            {
                emission.enabled = true;
            }
        }

        for (int i = 0; i < controlledTrails.Count; i++)
        {
            TrailRenderer tr = controlledTrails[i];
            if (tr != null && !tr.emitting)
            {
                tr.emitting = true;
            }
        }
    }

    // ================================================================================================
    // กฎเหล็กข้อที่ 4: รองรับการกดปุ่ม Shift และท่าวิ่งทุกรูปแบบใน Unity 6
    // ================================================================================================
    private bool DetectShiftAndMovementInput(out bool moveInputHeld)
    {
        bool shiftNew = false;
        bool moveNew = false;

        if (Keyboard.current != null)
        {
            shiftNew = Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed;
            moveNew =
                Keyboard.current.wKey.isPressed ||
                Keyboard.current.aKey.isPressed ||
                Keyboard.current.sKey.isPressed ||
                Keyboard.current.dKey.isPressed ||
                Keyboard.current.upArrowKey.isPressed ||
                Keyboard.current.downArrowKey.isPressed ||
                Keyboard.current.leftArrowKey.isPressed ||
                Keyboard.current.rightArrowKey.isPressed;
        }

        bool shiftLegacy = false;
        bool moveLegacy = false;

        try
        {
            shiftLegacy = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            moveLegacy =
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
            // ป้องกัน Error หากโปรเจกต์ปิด Legacy Input Manager
        }

        moveInputHeld = moveNew || moveLegacy;
        return shiftNew || shiftLegacy;
    }

    private void CheckForceTestHotkey()
    {
        bool f1Down = false;
        if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
        {
            f1Down = true;
        }

        try
        {
            if (Input.GetKeyDown(toggleForceTestKey))
            {
                f1Down = true;
            }
        }
        catch
        {
            // Ignore if legacy input is disabled
        }

        if (f1Down)
        {
            forceTestActive = !forceTestActive;
        }
    }

    private void UpdateHorizontalSpeed(float dt)
    {
        if (!hasInitializedPosition)
        {
            previousWorldPosition = transform.position;
            hasInitializedPosition = true;
        }

        Vector3 currentPos = transform.position;
        Vector3 horizontalDelta = new Vector3(currentPos.x - previousWorldPosition.x, 0f, currentPos.z - previousWorldPosition.z);
        float deltaSpeed = horizontalDelta.magnitude / dt;
        previousWorldPosition = currentPos;

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

        float rawSpeed = Mathf.Max(deltaSpeed, Mathf.Max(ccSpeed, rbSpeed));
        currentHorizontalSpeed = Mathf.Lerp(currentHorizontalSpeed, rawSpeed, Mathf.Clamp01(dt * 18f));
    }

    private bool CheckAnimatorRunStateOrParameters()
    {
        if (cachedAnimator == null || !cachedAnimator.isActiveAndEnabled || cachedAnimator.runtimeAnimatorController == null)
        {
            return false;
        }

        // 1. ตรวจจับจาก 2D BlendTree (Locomotion / Great Sword Locomotion)
        // ในโปรเจกต์นี้ เดินตรง (Walk) = 1.0, เดินเฉียง = 1.414 และท่าวิ่ง (Run) = 2.0 (จึงต้องใช้เกณฑ์ >= 1.55)
        AnimatorControllerParameter[] parameters = cachedAnimator.parameters;
        bool hasMoveX = false;
        bool hasMoveY = false;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter p = parameters[i];
            if (p.type == AnimatorControllerParameterType.Float)
            {
                if (p.name == "MoveX") hasMoveX = true;
                else if (p.name == "MoveY") hasMoveY = true;
                else if (p.name == "Speed" && cachedAnimator.GetFloat("Speed") >= 1.55f) return true;
            }
            else if (p.type == AnimatorControllerParameterType.Bool)
            {
                if ((p.name == "IsSprinting" || p.name == "Sprint" || p.name == "Run") && cachedAnimator.GetBool(p.name))
                {
                    return true;
                }
            }
        }

        if (hasMoveX && hasMoveY)
        {
            float mx = cachedAnimator.GetFloat("MoveX");
            float my = cachedAnimator.GetFloat("MoveY");
            float blendRadius = Mathf.Sqrt(mx * mx + my * my);
            if (blendRadius >= 1.55f)
            {
                return true;
            }
        }

        // 2. ตรวจจับจากชื่อ State โดยตรง
        int layers = cachedAnimator.layerCount;
        for (int layer = 0; layer < layers; layer++)
        {
            AnimatorStateInfo current = cachedAnimator.GetCurrentAnimatorStateInfo(layer);
            for (int k = 0; k < AnimatorRunKeywords.Length; k++)
            {
                string kw = AnimatorRunKeywords[k];
                if (current.IsName(kw) || current.IsName("Base Layer." + kw) || current.IsName("Locomotion." + kw))
                {
                    return true;
                }
            }

            if (cachedAnimator.IsInTransition(layer))
            {
                AnimatorStateInfo next = cachedAnimator.GetNextAnimatorStateInfo(layer);
                for (int k = 0; k < AnimatorRunKeywords.Length; k++)
                {
                    string kw = AnimatorRunKeywords[k];
                    if (next.IsName(kw) || next.IsName("Base Layer." + kw) || next.IsName("Locomotion." + kw))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private void DisableConflictingLegacySprintScripts()
    {
        MonoBehaviour[] allScripts = GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < allScripts.Length; i++)
        {
            MonoBehaviour mb = allScripts[i];
            if (mb == null || mb == this) continue;

            string scriptName = mb.GetType().Name;
            if (scriptName == "SimpleSprintTrail" || scriptName == "RunWindSlipstream")
            {
                mb.enabled = false;
            }
        }
    }

    // ================================================================================================
    // กฎเหล็กข้อที่ 4: หน้าจอ Debug HUD เล็กๆ มุมซ้ายบน (OnGUI)
    // ================================================================================================
    private void OnGUI()
    {
        if (!showDebugHUD || !Application.isPlaying)
        {
            return;
        }

        GUIStyle boxStyle = new GUIStyle(GUI.skin.box);
        boxStyle.alignment = TextAnchor.UpperLeft;
        boxStyle.fontSize = 13;
        boxStyle.normal.textColor = Color.white;

        GUI.color = new Color(0.05f, 0.08f, 0.12f, 0.86f);
        GUI.Box(new Rect(14f, 14f, 420f, 126f), GUIContent.none, boxStyle);
        GUI.color = Color.white;

        GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.fontSize = 13;
        labelStyle.richText = true;
        labelStyle.normal.textColor = Color.white;

        int totalFound = discoveredParticleCount + discoveredTrailCount;
        string foundBadge = totalFound > 0
            ? "<color=#55FF77><b>" + totalFound + " ชิ้น</b></color> (Particle: " + discoveredParticleCount + ", Trail: " + discoveredTrailCount + ")"
            : "<color=#FF6666><b>0 ชิ้น (ไม่พบเอฟเฟกต์)</b></color>";

        string shiftBadge = isShiftKeyPressed
            ? "<color=#55EEFF><b>กำลังกด Shift (HELD)</b></color>"
            : "<color=#AAAAAA>ไม่ได้กด (Released)</color>";

        string activeBadge = isVFXCurrentlyActive
            ? "<color=#55FF77><b>ทำงานอยู่ (ON)</b></color>"
            : "<color=#FF6666><b>หยุดพัก (OFF)</b></color>";

        string f1Badge = forceTestActive
            ? "<color=#FFE066><b>ON (กำลังบังคับเปิด)</b></color>"
            : "<color=#AAAAAA>OFF (กดปุ่ม F1 เพื่อบังคับเปิดทดสอบ)</color>";

        string hudContent =
            "<b>[ExistingRunVFXActivator - ตัวปลุกเอฟเฟกต์วิ่งที่มีอยู่แล้ว]</b>\n" +
            "• <b>พบเอฟเฟกต์:</b> " + foundBadge + "\n" +
            "• <b>สถานะปุ่ม Shift:</b> " + shiftBadge + "  |  <b>ความเร็ว:</b> " + currentHorizontalSpeed.ToString("F2") + " m/s\n" +
            "• <b>สถานะเอฟเฟกต์วิ่ง:</b> " + activeBadge + "\n" +
            "• <b>ปุ่มลัด F1 (บังคับเปิด-ปิดทดสอบ):</b> " + f1Badge;

        GUI.Label(new Rect(24f, 20f, 400f, 114f), hudContent, labelStyle);
    }

    private static Transform FindDeepChildByName(Transform parent, string targetName)
    {
        if (parent == null || string.IsNullOrEmpty(targetName)) return null;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name == targetName)
            {
                return child;
            }

            Transform found = FindDeepChildByName(child, targetName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }
}
