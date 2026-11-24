using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// ระบบควบคุมเอฟเฟกต์ "โล่ป้องกันทรงโล่ 3 มิติ (Sculpted Energy Guard Shield VFX)" สำหรับ Unity 6 (URP)
/// ออกแบบให้เห็นเป็น "รูปโล่ป้องกัน" ชัดเจนด้านหน้าตัวละครขณะกดบล็อกค้างไว้
/// และเมื่อถูกโจมตีขณะบล็อกอยู่ ตัวโล่จะสว่างวาบรับแรงโจมตีไว้บนพื้นผิวโล่อย่างนิ่งมั่นคง (ไม่สะท้อนคลื่น ไม่กระเด็น และไม่มีสะเก็ดไฟกระเด็น)
/// </summary>
[AddComponentMenu("yeen VFX/03. [Player] บล็อคโล่แสง 3 มิติ (Block Guard Shield)")]
[DisallowMultipleComponent]
public class MMOBlockParryController : MonoBehaviour
{
    [Header("=== 1. ปุ่มควบคุมการบล็อก (Guard Input Settings) ===")]
    [Tooltip("อนุญาตให้กดคลิกขวาเมาส์ (Right Mouse Button) ค้างไว้เพื่อกางโล่บล็อก")]
    public bool allowRightMouseGuard = true;

    [Tooltip("ปุ่มคีย์บอร์ดสำหรับกดค้างเพื่อกางโล่บล็อก (ค่าเริ่มต้น = F และรองรับ Left Alt ของระบบเกมเดิมด้วย)")]
    public KeyCode guardHoldKey = KeyCode.F;

    [Tooltip("ซิงก์กับสถานะ IsBlocking ของสคริปต์ PlayerCombat ในตัวละครโดยอัตโนมัติ")]
    public bool syncWithPlayerCombat = true;

    [Header("=== 2. รูปลักษณ์และสีของโล่ป้องกัน (Shield Shape & Visual Settings) ===")]
    [Tooltip("สีหลักของม่านโล่พลังงาน (ค่าเริ่มต้น = สีฟ้าใสพลังงาน Guardian Cyan)")]
    public Color baseShieldColor = new Color(0.32f, 0.84f, 1.0f, 0.86f);

    [Tooltip("สีของโล่ในเสี้ยววินาทีที่รับการโจมตี (สว่างวาบนุ่มนวลบนหน้าโล่เพื่อบอกว่ากันไว้ได้ ไม่มีสะท้อนกระเด็น)")]
    public Color absorbImpactColor = new Color(0.82f, 0.97f, 1.0f, 1.0f);

    [Tooltip("ความเร็วในการกางและหุบรูปโล่พลังงาน")]
    [Range(8f, 35f)]
    public float shieldDeploySpeed = 24f;

    [Tooltip("ความถี่การกระเพื่อมเบาๆ ของพลังงานบนตัวโล่ขณะกดบล็อกค้าง")]
    public float shieldPulseFrequency = 4.2f;

    [Header("=== 3. โหนดเอฟเฟกต์รูปโล่ป้องกัน (Guard Shield References) ===")]
    [Tooltip("โหนดแม่ของเอฟเฟกต์โล่ป้องกัน (ตั้งอยู่ด้านหน้าอกตัวละคร)")]
    public Transform guardVfxMount;

    /// <summary>
    /// GameObject หลักของม่านโล่ป้องกัน (ใช้ร่วมกับ MMOGuardBreakVFX เพื่อสั่งปิดโล่ทันทีเมื่อโดนตีบล็อคแตก)
    /// </summary>
    public GameObject guardBarrierGroup => guardVfxMount != null
        ? guardVfxMount.gameObject
        : (guardShieldDomeTransform != null ? guardShieldDomeTransform.gameObject : null);

    [Tooltip("3D Mesh รูปโล่โค้งป้องกันด้านหน้าตัวละคร (L1_GuardShieldShape_Mesh)")]
    public Transform guardShieldDomeTransform;
    public MeshRenderer guardShieldDomeRenderer;

    [Tooltip("แสงเรืองขอบด้านหลังรูปโล่ (L1_GuardShieldGlow_Back)")]
    public Transform guardShieldGlowTransform;
    public MeshRenderer guardShieldGlowRenderer;

    [Header("=== 4. หน้าต่างแสดงสถานะบนหน้าจอ (Optional Debug HUD) ===")]
    [Tooltip("แสดงกล่องสถานะมุมซ้ายบนของจอเกม (ค่าเริ่มต้นปิดไว้เพื่อให้หน้าจอสะอาด)")]
    public bool showDebugHUD = false;

    [Header("=== 5. สถานะการทำงานแบบ Real-Time (Read-Only) ===")]
    [SerializeField] private bool isGuarding = false;
    [SerializeField] private float currentShieldScaleFactor = 0f;
    [SerializeField] private int totalBlockedHits = 0;

    private Vector3 defaultShieldLocalScale = new Vector3(1.18f, 1.18f, 1.0f);
    private Vector3 defaultShieldLocalPos = Vector3.zero;
    private float shieldAbsorbFlashTimer = 0f;
    private float absorbCooldownTimer = 0f;
    private bool wasGuardHeldLastFrame = false;

    // Runtime Procedural Shield Assets (สร้างรูปโล่และลวดลายคมชัดในหน่วยความจำอัตโนมัติ)
    private Mesh runtimeShieldMesh;
    private Texture2D runtimeShieldEmblemTex;
    private Texture2D runtimeShieldGlowTex;
    private Material runtimeShieldMat;
    private Material runtimeShieldGlowMat;
    private Material activeShieldMatInstance;

    private PlayerCombat cachedPlayerCombat;

    private void Awake()
    {
        cachedPlayerCombat = GetComponentInParent<PlayerCombat>();
        if (cachedPlayerCombat == null) cachedPlayerCombat = GetComponentInChildren<PlayerCombat>(true);

        // ลบโหนดสะท้อน/สะเก็ดไฟกระเด็นรุ่นเก่าออก (ถ้าเคยสร้างไว้ใน Scene หรือ Prefab)
        CleanupLegacyReflectAndSparkNodes();

        AutoBindLayerReferences();

        if (guardShieldDomeTransform == null || guardShieldDomeRenderer == null)
        {
            BuildRuntimeSculptedShieldHierarchy();
        }
        else if (guardShieldDomeRenderer.sharedMaterial == null ||
                 guardShieldDomeRenderer.sharedMaterial.mainTexture == null ||
                 guardShieldDomeRenderer.sharedMaterial.mainTexture.name != "T_CircularEnergyShield_512")
        {
            // อัปเกรด Material และ Mesh ให้เป็นโล่วงกลมคมชัดทันที
            ApplySculptedShieldVisualToExistingRenderer();
        }

        if (guardShieldDomeTransform != null)
        {
            defaultShieldLocalScale = guardShieldDomeTransform.localScale;
            if (defaultShieldLocalScale.sqrMagnitude < 0.001f)
            {
                defaultShieldLocalScale = new Vector3(1.18f, 1.18f, 1.0f);
            }
            defaultShieldLocalPos = guardShieldDomeTransform.localPosition;
            guardShieldDomeTransform.localScale = Vector3.zero;
        }

        if (guardShieldDomeRenderer != null)
        {
            activeShieldMatInstance = guardShieldDomeRenderer.material;
            guardShieldDomeRenderer.enabled = false;
        }

        if (guardShieldGlowTransform != null)
        {
            guardShieldGlowTransform.localScale = Vector3.zero;
        }

        if (guardShieldGlowRenderer != null)
        {
            guardShieldGlowRenderer.enabled = false;
        }
    }

    private void CleanupLegacyReflectAndSparkNodes()
    {
        string[] legacyNames = new string[]
        {
            "L1_Guard_RuneAuraRing",
            "L2_Parry_DiamondStarFlash",
            "L3_Deflect_MetalSparks",
            "L4_Parry_ShockwaveRing",
            "L4B_Acoustic_SoundwaveSphere"
        };

        for (int i = 0; i < legacyNames.Length; i++)
        {
            Transform oldNode = FindDeepChild(transform, legacyNames[i]);
            if (oldNode != null)
            {
                Destroy(oldNode.gameObject);
            }
        }
    }

    public void AutoBindLayerReferences()
    {
        if (guardVfxMount == null)
        {
            guardVfxMount = FindDeepChild(transform, "VFX_AAA_GuardAndParry_Root");
        }

        Transform searchRoot = guardVfxMount != null ? guardVfxMount : transform;

        if (guardShieldDomeTransform == null)
        {
            guardShieldDomeTransform = FindDeepChild(searchRoot, "L1_GuardShieldShape_Mesh");
            if (guardShieldDomeTransform == null)
            {
                guardShieldDomeTransform = FindDeepChild(searchRoot, "L1_GuardShieldDome_Mesh");
            }
        }

        if (guardShieldDomeRenderer == null && guardShieldDomeTransform != null)
        {
            guardShieldDomeRenderer = guardShieldDomeTransform.GetComponent<MeshRenderer>();
        }

        if (guardShieldGlowTransform == null)
        {
            guardShieldGlowTransform = FindDeepChild(searchRoot, "L1_GuardShieldGlow_Back");
        }

        if (guardShieldGlowRenderer == null && guardShieldGlowTransform != null)
        {
            guardShieldGlowRenderer = guardShieldGlowTransform.GetComponent<MeshRenderer>();
        }
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0.00001f) return;

        if (absorbCooldownTimer > 0f)
        {
            absorbCooldownTimer -= dt;
        }

        if (shieldAbsorbFlashTimer > 0f)
        {
            shieldAbsorbFlashTimer = Mathf.Max(0f, shieldAbsorbFlashTimer - dt * 6.0f);
        }

        // 1. ตรวจสอบสถานะการบล็อกของตัวละครนี้ (Multiplayer-Safe):
        // หากตัวละครถูก Spawn บนเครือข่าย Fusion ให้ยึดค่า Networked PlayerCombat.IsBlocking ของตัวละครนี้เป็นแหล่งความจริงเดียวเท่านั้น
        // เพื่อป้องกันปัญหาเมื่อ Host กด [F] หรือคลิกขวาแล้วโล่ไปขึ้นบนตัวผู้เล่นคนอื่นในห้องทุกคน รวมถึงเคารพ SafeZone/Stamina/ชักดาบ
        bool guardInputHeld = false;
        if (syncWithPlayerCombat && cachedPlayerCombat != null && cachedPlayerCombat.IsSpawned)
        {
            guardInputHeld = cachedPlayerCombat.IsBlocking && !cachedPlayerCombat.IsDead;
        }
        else if (IsLocallyOwnedForFallbackInput())
        {
            guardInputHeld = ReadUniversalGuardHoldInput();
        }

        if (guardInputHeld && !wasGuardHeldLastFrame)
        {
            SetGuardStanceActive(true);
        }
        else if (!guardInputHeld && wasGuardHeldLastFrame)
        {
            SetGuardStanceActive(false);
        }

        wasGuardHeldLastFrame = guardInputHeld;
        isGuarding = guardInputHeld;

        // 2. อัปเดตการกาง/หุบและแสงรับแรงของรูปโล่ป้องกัน
        UpdateGuardShieldVisualAnimation(dt);
    }

    private bool IsLocallyOwnedForFallbackInput()
    {
        if (cachedPlayerCombat != null)
        {
            if (cachedPlayerCombat.IsSpawned)
                return cachedPlayerCombat.IsLocallyControlled;
            if (cachedPlayerCombat.Object != null && cachedPlayerCombat.Object.IsValid)
                return cachedPlayerCombat.Object.HasStateAuthority || cachedPlayerCombat.Object.HasInputAuthority;
        }

        var capsule = GetComponentInParent<PlayerCapsule>();
        if (capsule != null && capsule.Object != null && capsule.Object.IsValid)
            return capsule.Object.HasStateAuthority || capsule.Object.HasInputAuthority;

        return true;
    }

    public void SetGuardStanceActive(bool active)
    {
        isGuarding = active;

        if (active)
        {
            if (guardVfxMount != null && !guardVfxMount.gameObject.activeSelf)
            {
                guardVfxMount.gameObject.SetActive(true);
            }
            if (guardShieldDomeRenderer != null) guardShieldDomeRenderer.enabled = true;
            if (guardShieldGlowRenderer != null) guardShieldGlowRenderer.enabled = true;
        }
    }

    /// <summary>
    /// เรียกเมื่อตัวละครถูกโจมตีขณะบล็อกอยู่:
    /// ตัวโล่จะสว่างวาบนุ่มนวลเพื่อรับแรงโจมตีไว้บนพื้นผิวโล่โดยตรง (ไม่สะท้อนคลื่น ไม่กระเด็น และไม่มีสะเก็ดไฟกระเด็น)
    /// </summary>
    public void TriggerBlockAbsorb()
    {
        if (absorbCooldownTimer > 0f) return;

        absorbCooldownTimer = 0.08f;
        totalBlockedHits++;
        shieldAbsorbFlashTimer = 1.0f;

        if (guardShieldDomeRenderer != null)
        {
            guardShieldDomeRenderer.enabled = true;
        }
    }

    /// <summary>
    /// คงชื่อเมธอดเดิมไว้เพื่อความเข้ากันได้กับ PlayerCombat และสคริปต์อื่น (ส่งต่อไปที่ TriggerBlockAbsorb แบบไม่สะท้อน/ไม่กระเด็น)
    /// </summary>
    public void TriggerParryDeflect()
    {
        TriggerBlockAbsorb();
    }

    private void UpdateGuardShieldVisualAnimation(float dt)
    {
        float targetFactor = isGuarding ? 1f : 0f;
        currentShieldScaleFactor = Mathf.MoveTowards(currentShieldScaleFactor, targetFactor, shieldDeploySpeed * dt);

        if (guardShieldDomeTransform != null)
        {
            if (currentShieldScaleFactor <= 0.001f)
            {
                guardShieldDomeTransform.localScale = Vector3.zero;
                if (guardShieldDomeRenderer != null && guardShieldDomeRenderer.enabled)
                {
                    guardShieldDomeRenderer.enabled = false;
                }
                if (guardShieldGlowTransform != null)
                {
                    guardShieldGlowTransform.localScale = Vector3.zero;
                }
                if (guardShieldGlowRenderer != null && guardShieldGlowRenderer.enabled)
                {
                    guardShieldGlowRenderer.enabled = false;
                }
            }
            else
            {
                if (guardShieldDomeRenderer != null && !guardShieldDomeRenderer.enabled)
                {
                    guardShieldDomeRenderer.enabled = true;
                }
                if (guardShieldGlowRenderer != null && !guardShieldGlowRenderer.enabled)
                {
                    guardShieldGlowRenderer.enabled = true;
                }

                // กางโล่อย่างมั่นคงและมีจังหวะหายใจเบาๆ โดยไม่กระเด็นถอยหลัง
                float gentlePulse = 1f + Mathf.Sin(Time.time * shieldPulseFrequency) * 0.02f;
                Vector3 finalScale = defaultShieldLocalScale * (currentShieldScaleFactor * gentlePulse);

                guardShieldDomeTransform.localScale = finalScale;
                guardShieldDomeTransform.localPosition = defaultShieldLocalPos;

                if (guardShieldGlowTransform != null)
                {
                    guardShieldGlowTransform.localScale = finalScale * (1.07f + shieldAbsorbFlashTimer * 0.04f);
                    guardShieldGlowTransform.localPosition = defaultShieldLocalPos + new Vector3(0f, 0f, -0.02f);
                }

                if (activeShieldMatInstance != null)
                {
                    Color shieldColor = Color.Lerp(baseShieldColor, absorbImpactColor, shieldAbsorbFlashTimer);
                    activeShieldMatInstance.color = shieldColor;
                    if (activeShieldMatInstance.HasProperty("_BaseColor"))
                    {
                        activeShieldMatInstance.SetColor("_BaseColor", shieldColor);
                    }
                }
            }
        }
    }

    private bool ReadUniversalGuardHoldInput()
    {
        bool heldNew = false;

        if (allowRightMouseGuard && Mouse.current != null && Mouse.current.rightButton.isPressed)
        {
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (!overUi)
            {
                heldNew = true;
            }
        }

        if (Keyboard.current != null)
        {
            if (guardHoldKey == KeyCode.F && Keyboard.current.fKey.isPressed)
            {
                heldNew = true;
            }
            else if (Keyboard.current.leftAltKey.isPressed)
            {
                heldNew = true;
            }
        }

        bool heldLegacy = false;
        try
        {
            if (allowRightMouseGuard && Input.GetMouseButton(1))
            {
                bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
                if (!overUi)
                {
                    heldLegacy = true;
                }
            }

            if (Input.GetKey(guardHoldKey) || Input.GetKey(KeyCode.LeftAlt))
            {
                heldLegacy = true;
            }
        }
        catch
        {
            // Ignore if legacy input is disabled
        }

        return heldNew || heldLegacy;
    }

    // ================================================================================================
    // สร้าง 3D Mesh รูปโล่โค้ง และ Texture รูปโล่อัศวิน/โล่พลังงานคมชัด (512x512 px) อัตโนมัติ
    // ================================================================================================
    private void BuildRuntimeSculptedShieldHierarchy()
    {
        EnsureRuntimeShieldAssets();

        Transform visual = transform.Find("CharacterVisual");
        float feetY = visual != null ? visual.localPosition.y : 0f;

        if (guardVfxMount == null)
        {
            GameObject rootObj = new GameObject("VFX_AAA_GuardAndParry_Root");
            rootObj.transform.SetParent(transform, false);
            rootObj.transform.localPosition = new Vector3(0f, feetY + 1.12f, 0.72f);
            rootObj.transform.localRotation = Quaternion.identity;
            rootObj.transform.localScale = Vector3.one;
            guardVfxMount = rootObj.transform;
        }

        // โหนดหลัก: โล่พลังงานทรงกลมโค้ง 3 มิติ (Circular Energy Barrier Disc)
        GameObject shieldObj = new GameObject("L1_GuardShieldShape_Mesh");
        shieldObj.transform.SetParent(guardVfxMount, false);
        shieldObj.transform.localPosition = Vector3.zero;
        shieldObj.transform.localRotation = Quaternion.identity;
        shieldObj.transform.localScale = new Vector3(1.18f, 1.18f, 1.0f);

        MeshFilter mf = shieldObj.AddComponent<MeshFilter>();
        mf.sharedMesh = runtimeShieldMesh;

        guardShieldDomeRenderer = shieldObj.AddComponent<MeshRenderer>();
        guardShieldDomeRenderer.sharedMaterial = runtimeShieldMat;
        guardShieldDomeRenderer.shadowCastingMode = ShadowCastingMode.Off;
        guardShieldDomeRenderer.receiveShadows = false;
        guardShieldDomeRenderer.sortingOrder = 12;
        guardShieldDomeTransform = shieldObj.transform;

        // โหนดรอง: แสงเรืองขอบวงกลมด้านหลังเพื่อขับทรงโล่วงกลมให้โดดเด่นขึ้น
        GameObject glowObj = new GameObject("L1_GuardShieldGlow_Back");
        glowObj.transform.SetParent(guardVfxMount, false);
        glowObj.transform.localPosition = new Vector3(0f, 0f, -0.02f);
        glowObj.transform.localRotation = Quaternion.identity;
        glowObj.transform.localScale = new Vector3(1.28f, 1.28f, 1.0f);

        MeshFilter glowMf = glowObj.AddComponent<MeshFilter>();
        glowMf.sharedMesh = runtimeShieldMesh;

        guardShieldGlowRenderer = glowObj.AddComponent<MeshRenderer>();
        guardShieldGlowRenderer.sharedMaterial = runtimeShieldGlowMat;
        guardShieldGlowRenderer.shadowCastingMode = ShadowCastingMode.Off;
        guardShieldGlowRenderer.receiveShadows = false;
        guardShieldGlowRenderer.sortingOrder = 11;
        guardShieldGlowTransform = glowObj.transform;
    }

    private void ApplySculptedShieldVisualToExistingRenderer()
    {
        EnsureRuntimeShieldAssets();

        if (guardShieldDomeTransform != null)
        {
            guardShieldDomeTransform.localScale = new Vector3(1.18f, 1.18f, 1.0f);
            MeshFilter mf = guardShieldDomeTransform.GetComponent<MeshFilter>();
            if (mf != null) mf.sharedMesh = runtimeShieldMesh;
        }
        if (guardShieldDomeRenderer != null)
        {
            guardShieldDomeRenderer.sharedMaterial = runtimeShieldMat;
        }

        if (guardShieldGlowTransform != null)
        {
            guardShieldGlowTransform.localScale = new Vector3(1.28f, 1.28f, 1.0f);
            MeshFilter glowMf = guardShieldGlowTransform.GetComponent<MeshFilter>();
            if (glowMf != null) glowMf.sharedMesh = runtimeShieldMesh;
        }
        if (guardShieldGlowRenderer != null)
        {
            guardShieldGlowRenderer.sharedMaterial = runtimeShieldGlowMat;
        }
    }

    private void EnsureRuntimeShieldAssets()
    {
        if (runtimeShieldMesh == null)
        {
            runtimeShieldMesh = CreateCurvedShieldMesh();
        }
        if (runtimeShieldEmblemTex == null)
        {
            runtimeShieldEmblemTex = CreateHeraldicShieldEmblemTexture(512);
        }
        if (runtimeShieldGlowTex == null)
        {
            runtimeShieldGlowTex = CreateShieldBackGlowTexture(256);
        }
        if (runtimeShieldMat == null)
        {
            runtimeShieldMat = CreateRuntimeURPMaterial("MAT_RuntimeHeraldicShield", runtimeShieldEmblemTex, baseShieldColor);
        }
        if (runtimeShieldGlowMat == null)
        {
            runtimeShieldGlowMat = CreateRuntimeURPMaterial("MAT_RuntimeHeraldicShieldGlow", runtimeShieldGlowTex, new Color(0.25f, 0.78f, 1.0f, 0.42f));
        }
    }

    /// <summary>
    /// สร้าง 3D Mesh โล่พลังงานทรงวงกลมโค้ง (Circular Dome Disc) แท้ๆ ไม่มีมุมสี่เหลี่ยม
    /// </summary>
    public static Mesh CreateCurvedShieldMesh()
    {
        const int radialSegments = 48;
        const int ringCount = 10;
        const float maxRadius = 0.68f;
        const float curvatureDepth = 0.20f;

        int vertCount = 1 + ringCount * radialSegments;
        Vector3[] vertices = new Vector3[vertCount];
        Vector2[] uvs = new Vector2[vertCount];
        Color[] colors = new Color[vertCount];

        // จุดศูนย์กลางโดมวงกลม
        vertices[0] = new Vector3(0f, 0f, curvatureDepth);
        uvs[0] = new Vector2(0.5f, 0.5f);
        colors[0] = Color.white;

        int vIdx = 1;
        for (int ring = 1; ring <= ringCount; ring++)
        {
            float rFrac = ring / (float)ringCount;
            float rWorld = rFrac * maxRadius;
            float zOffset = (1f - rFrac * rFrac) * curvatureDepth;

            for (int seg = 0; seg < radialSegments; seg++)
            {
                float angle = (seg / (float)radialSegments) * Mathf.PI * 2f;
                float cosA = Mathf.Cos(angle);
                float sinA = Mathf.Sin(angle);

                vertices[vIdx] = new Vector3(cosA * rWorld, sinA * rWorld, zOffset);
                uvs[vIdx] = new Vector2(0.5f + cosA * rFrac * 0.5f, 0.5f + sinA * rFrac * 0.5f);
                colors[vIdx] = Color.white;
                vIdx++;
            }
        }

        int triCount = radialSegments + (ringCount - 1) * radialSegments * 2;
        int[] triangles = new int[triCount * 3];
        int tIdx = 0;

        // วงในสุดเชื่อมกับจุดศูนย์กลาง
        for (int seg = 0; seg < radialSegments; seg++)
        {
            int cur = 1 + seg;
            int nxt = 1 + ((seg + 1) % radialSegments);
            triangles[tIdx++] = 0;
            triangles[tIdx++] = nxt;
            triangles[tIdx++] = cur;
        }

        // วงชั้นนอกเชื่อมต่อกันจนเต็มแผ่นวงกลม
        for (int ring = 1; ring < ringCount; ring++)
        {
            int innerStart = 1 + (ring - 1) * radialSegments;
            int outerStart = 1 + ring * radialSegments;
            for (int seg = 0; seg < radialSegments; seg++)
            {
                int nextSeg = (seg + 1) % radialSegments;
                int i0 = innerStart + seg;
                int i1 = innerStart + nextSeg;
                int o0 = outerStart + seg;
                int o1 = outerStart + nextSeg;

                triangles[tIdx++] = i0;
                triangles[tIdx++] = i1;
                triangles[tIdx++] = o1;

                triangles[tIdx++] = i0;
                triangles[tIdx++] = o1;
                triangles[tIdx++] = o0;
            }
        }

        Mesh m = new Mesh();
        m.name = "Mesh_CircularGuardShield";
        m.vertices = vertices;
        m.uv = uvs;
        m.colors = colors;
        m.triangles = triangles;
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    /// <summary>
    /// ฟังก์ชันคณิตศาสตร์คำนวณระยะขอบ "โล่วงกลมพลังงาน (Circular Shield SDF)"
    /// ค่า < 0 อยู่ภายในวงกลม, ค่า = 0 คือเส้นขอบวงกลมพอดี, ค่า > 0 อยู่ภายนอกวงกลม
    /// </summary>
    public static float EvaluateShieldSDF(float nx, float ny)
    {
        return Mathf.Sqrt(nx * nx + ny * ny) - 0.86f;
    }

    /// <summary>
    /// วาด Texture "โล่พลังงานวงกลม (Circular Energy Shield)" ความละเอียดสูง 512x512 px
    /// มีขอบวงกลมเรืองแสง 2 ชั้นคมกริบ, ตารางรังผึ้งพลังงานโปร่งแสง, ลายรัศมี 8 แฉก และวงแกนพลังงานตรงกลาง
    /// </summary>
    public static Texture2D CreateHeraldicShieldEmblemTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.name = "T_CircularEnergyShield_512";
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[size * size];
        float hexFreq = 11.0f;

        for (int y = 0; y < size; y++)
        {
            float v = y / (float)(size - 1);
            float ny = (v * 2f) - 1f;

            for (int x = 0; x < size; x++)
            {
                float u = x / (float)(size - 1);
                float nx = (u * 2f) - 1f;

                float r = Mathf.Sqrt(nx * nx + ny * ny);
                float sdf = r - 0.86f;

                // มาสก์ตัดขอบวงกลมให้คมชัดและใสสนิทนอกวงกลม (ไม่มีขอบสี่เหลี่ยม)
                float insideMask = 1f - Mathf.SmoothStep(-0.012f, 0.012f, sdf);
                float outerRimGlow = Mathf.Exp(-Mathf.Max(0f, sdf) * 26f) * 0.45f * (1f - Mathf.SmoothStep(0.92f, 0.99f, r));

                if (insideMask <= 0.001f && outerRimGlow <= 0.005f)
                {
                    pixels[y * size + x] = new Color(1f, 1f, 1f, 0f);
                    continue;
                }

                // 1. ขอบวงกลมชั้นนอกสว่างคมกริบ
                float outerFrame = Mathf.Exp(-Mathf.Pow(r - 0.835f, 2f) / (2f * 0.022f * 0.022f));

                // 2. เส้นวงแหวนรูนชั้นใน
                float innerTrim = Mathf.Exp(-Mathf.Pow(r - 0.72f, 2f) / (2f * 0.011f * 0.011f)) * 0.82f;

                // 3. ลายตารางรังผึ้งพลังงานโปร่งแสงภายในวงกลม
                float px = u * hexFreq;
                float py = v * hexFreq * 1.1547f;
                float row = Mathf.Floor(py);
                float col = Mathf.Floor(px + (Mathf.Abs(row % 2f) * 0.5f));
                float fx = (px + (Mathf.Abs(row % 2f) * 0.5f)) - col - 0.5f;
                float fy = py - row - 0.5f;
                float hexDist = Mathf.Max(
                    Mathf.Abs(fx),
                    Mathf.Max(Mathf.Abs(fx * 0.5f + fy * 0.866f), Mathf.Abs(fx * -0.5f + fy * 0.866f))
                );
                float hexLines = Mathf.SmoothStep(0.39f, 0.48f, hexDist) * 0.42f;
                float glassFill = 0.22f + Mathf.Pow(Mathf.Clamp01(r / 0.86f), 2.2f) * 0.14f;

                // 4. วงแหวนแกนกลาง + รัศมีพลังงาน 8 ทิศทาง (Circular Arcane Barrier Emblem)
                float angle = Mathf.Atan2(ny, nx);
                float centerRing = Mathf.Exp(-Mathf.Pow(r - 0.24f, 2f) / (2f * 0.015f * 0.015f)) * 0.88f;
                float centerCore = Mathf.Exp(-(r * r) / (2f * 0.11f * 0.11f)) * 0.58f;
                float spokeWave = Mathf.Pow(Mathf.Abs(Mathf.Cos(angle * 4f)), 18f);
                float spokeMask = Mathf.SmoothStep(0.24f, 0.30f, r) * (1f - Mathf.SmoothStep(0.70f, 0.74f, r));
                float radialSpokes = spokeWave * spokeMask * 0.55f;
                float crest = Mathf.Clamp01(centerRing + centerCore + radialSpokes);

                float interiorAlpha = (glassFill + hexLines + outerFrame * 0.96f + innerTrim + crest * 0.85f) * insideMask;
                float finalAlpha = Mathf.Clamp01(Mathf.Max(interiorAlpha, outerRimGlow));

                float highlight = Mathf.Clamp01(outerFrame * 0.88f + innerTrim * 0.65f + crest * 0.78f);
                Color colOut = Color.Lerp(new Color(0.68f, 0.92f, 1.0f, 1f), Color.white, highlight);
                colOut.a = finalAlpha;

                pixels[y * size + x] = colOut;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply(false, false);
        return tex;
    }

    public static Texture2D CreateShieldBackGlowTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.name = "T_CircularShieldBackGlow";
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
                float sdf = r - 0.86f;
                float halo = Mathf.Exp(-sdf * sdf * 42f) * 0.85f;
                float softFill = (1f - Mathf.SmoothStep(0.0f, 0.94f, r)) * 0.38f;
                float outerCut = 1f - Mathf.SmoothStep(0.90f, 0.99f, r);
                float alpha = Mathf.Clamp01(Mathf.Max(halo, softFill) * outerCut);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply(false, false);
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
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)CullMode.Off);
        if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
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

        GUI.color = new Color(0.04f, 0.08f, 0.14f, 0.86f);
        GUI.Box(new Rect(14f, 14f, 410f, 92f), GUIContent.none, boxStyle);
        GUI.color = Color.white;

        GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.fontSize = 14;
        labelStyle.richText = true;
        labelStyle.normal.textColor = Color.white;

        string guardBadge = isGuarding
            ? "<color=#55EEFF><b>กำลังบล็อก (SHIELD ON)</b></color>"
            : "<color=#AAAAAA><b>ปกติ (OFF)</b></color>";

        string hudText =
            "<b>[Sculpted Energy Guard Shield VFX]</b>\n" +
            "• <b>สถานะป้องกัน:</b> " + guardBadge + "\n" +
            "• <b>จำนวนครั้งที่บล็อกรับแรงได้:</b> " + totalBlockedHits + " ครั้ง";

        GUI.Label(new Rect(24f, 20f, 390f, 80f), hudText, labelStyle);
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
        if (activeShieldMatInstance != null && activeShieldMatInstance != runtimeShieldMat) Destroy(activeShieldMatInstance);
        if (runtimeShieldMat != null) Destroy(runtimeShieldMat);
        if (runtimeShieldGlowMat != null) Destroy(runtimeShieldGlowMat);
        if (runtimeShieldEmblemTex != null) Destroy(runtimeShieldEmblemTex);
        if (runtimeShieldGlowTex != null) Destroy(runtimeShieldGlowTex);
        if (runtimeShieldMesh != null) Destroy(runtimeShieldMesh);
    }
}
