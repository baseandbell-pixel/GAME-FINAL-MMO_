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

    private Vector3 defaultShieldLocalScale = new Vector3(1.12f, 1.28f, 1.0f);
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
                 guardShieldDomeRenderer.sharedMaterial.mainTexture.name != "T_HeraldicEnergyShield_512")
        {
            // อัปเกรด Material และ Mesh ให้เป็นรูปโล่คมชัดทันที
            ApplySculptedShieldVisualToExistingRenderer();
        }

        if (guardShieldDomeTransform != null)
        {
            defaultShieldLocalScale = guardShieldDomeTransform.localScale;
            if (defaultShieldLocalScale.sqrMagnitude < 0.001f)
            {
                defaultShieldLocalScale = new Vector3(1.12f, 1.28f, 1.0f);
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

        // 1. ตรวจจับสถานะการบล็อกแบบแยกเฉพาะตัวละครนี้ (Multiplayer-Safe):
        // หากตัวละครถูก Spawn บนเครือข่าย Fusion ให้ยึดค่า Networked PlayerCombat.IsBlocking ของตัวละครนี้เป็นแหล่งความจริงเดียวเท่านั้น
        // เพื่อป้องกันปัญหาเมื่อ Host กด [F] หรือคลิกขวาแล้วโล่ไปขึ้นบนตัวผู้เล่นคนอื่นในห้องทุกคน
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

    public void SetGuardStanceActive(bool active)
    {
        isGuarding = active;

        if (active)
        {
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

        if (guardVfxMount == null)
        {
            GameObject rootObj = new GameObject("VFX_AAA_GuardAndParry_Root");
            rootObj.transform.SetParent(transform, false);
            rootObj.transform.localPosition = new Vector3(0f, 1.05f, 0.65f);
            rootObj.transform.localRotation = Quaternion.identity;
            rootObj.transform.localScale = Vector3.one;
            guardVfxMount = rootObj.transform;
        }

        // โหนดหลัก: รูปโล่พลังงานโค้ง 3 มิติชัดเจน
        GameObject shieldObj = new GameObject("L1_GuardShieldShape_Mesh");
        shieldObj.transform.SetParent(guardVfxMount, false);
        shieldObj.transform.localPosition = Vector3.zero;
        shieldObj.transform.localRotation = Quaternion.identity;
        shieldObj.transform.localScale = new Vector3(1.12f, 1.28f, 1.0f);

        MeshFilter mf = shieldObj.AddComponent<MeshFilter>();
        mf.sharedMesh = runtimeShieldMesh;

        guardShieldDomeRenderer = shieldObj.AddComponent<MeshRenderer>();
        guardShieldDomeRenderer.sharedMaterial = runtimeShieldMat;
        guardShieldDomeRenderer.shadowCastingMode = ShadowCastingMode.Off;
        guardShieldDomeRenderer.receiveShadows = false;
        guardShieldDomeRenderer.sortingOrder = 12;
        guardShieldDomeTransform = shieldObj.transform;

        // โหนดรอง: แสงเรืองขอบด้านหลังรูปโล่เพื่อขับทรงโล่ให้โดดเด่นขึ้น
        GameObject glowObj = new GameObject("L1_GuardShieldGlow_Back");
        glowObj.transform.SetParent(guardVfxMount, false);
        glowObj.transform.localPosition = new Vector3(0f, 0f, -0.02f);
        glowObj.transform.localRotation = Quaternion.identity;
        glowObj.transform.localScale = new Vector3(1.20f, 1.37f, 1.0f);

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

        MeshFilter mf = guardShieldDomeTransform.GetComponent<MeshFilter>();
        if (mf != null)
        {
            mf.sharedMesh = runtimeShieldMesh;
        }
        guardShieldDomeRenderer.sharedMaterial = runtimeShieldMat;
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
    /// สร้าง 3D Mesh แผ่นโค้งรองรับรูปโล่ (Vertex Color = White เต็ม 1.0 เพื่อให้ขอบรูปโล่จาก SDF Texture คมชัดทุกมุม)
    /// </summary>
    public static Mesh CreateCurvedShieldMesh()
    {
        int cols = 24;
        int rows = 24;
        float width = 1.18f;
        float height = 1.38f;
        float curvatureDepth = 0.22f;

        Vector3[] vertices = new Vector3[(cols + 1) * (rows + 1)];
        Vector2[] uvs = new Vector2[vertices.Length];
        Color[] colors = new Color[vertices.Length];
        int[] triangles = new int[cols * rows * 6];

        int vIdx = 0;
        for (int r = 0; r <= rows; r++)
        {
            float v = r / (float)rows;
            float ny = (v - 0.5f) * 2f;
            for (int c = 0; c <= cols; c++)
            {
                float u = c / (float)cols;
                float nx = (u - 0.5f) * 2f;

                // โค้งรับด้านหน้าเล็กน้อยเหมือนโล่จริง
                float bend = Mathf.Clamp01(nx * nx * 0.75f + ny * ny * 0.25f);
                float zOffset = (1f - bend) * curvatureDepth;

                vertices[vIdx] = new Vector3(nx * (width * 0.5f), ny * (height * 0.5f), zOffset);
                uvs[vIdx] = new Vector2(u, v);
                colors[vIdx] = Color.white;
                vIdx++;
            }
        }

        int tIdx = 0;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int i0 = r * (cols + 1) + c;
                int i1 = i0 + 1;
                int i2 = i0 + (cols + 1);
                int i3 = i2 + 1;
                triangles[tIdx++] = i0; triangles[tIdx++] = i2; triangles[tIdx++] = i1;
                triangles[tIdx++] = i1; triangles[tIdx++] = i2; triangles[tIdx++] = i3;
            }
        }

        Mesh m = new Mesh();
        m.name = "Mesh_GuardShieldShape";
        m.vertices = vertices;
        m.uv = uvs;
        m.colors = colors;
        m.triangles = triangles;
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    /// <summary>
    /// ฟังก์ชันคณิตศาสตร์คำนวณระยะขอบ "รูปทรงโล่อัศวิน (Heraldic Heater Shield SDF)"
    /// ค่า < 0 อยู่ภายในรูปโล่, ค่า = 0 คือเส้นขอบรูปโล่พอดี, ค่า > 0 อยู่ภายนอกรูปโล่
    /// </summary>
    public static float EvaluateShieldSDF(float nx, float ny)
    {
        float ax = Mathf.Abs(nx);

        // ความกว้างของรูปโล่ในแต่ละความสูง ny (-0.88 = ปลายแหลมล่าง, +0.86 = ขอบโค้งบน)
        float halfWidth = GetShieldHalfWidthAtY(ny);
        float eps = 0.005f;
        float dwDy = (GetShieldHalfWidthAtY(ny + eps) - GetShieldHalfWidthAtY(ny - eps)) / (2f * eps);
        float dSide = (ax - halfWidth) / Mathf.Sqrt(1f + dwDy * dwDy);

        // ขอบบนของโล่โค้งมนสง่างาม (Arched Crown)
        float crownArch = 0.84f - 0.10f * (ax * ax / (0.76f * 0.76f));
        float dTop = ny - crownArch;

        // ปลายแหลมด้านล่างของโล่
        float dBottom = -0.88f - ny;

        return Mathf.Max(dSide, Mathf.Max(dTop, dBottom));
    }

    private static float GetShieldHalfWidthAtY(float ny)
    {
        if (ny <= -0.88f) return 0f;

        if (ny < 0.14f)
        {
            float u = Mathf.Clamp01((ny + 0.88f) / 1.02f);
            return 0.74f * Mathf.Pow(Mathf.Sin(u * Mathf.PI * 0.5f), 0.76f);
        }

        float v = Mathf.Clamp01((ny - 0.14f) / 0.70f);
        return Mathf.Lerp(0.74f, 0.78f, v);
    }

    /// <summary>
    /// วาด Texture "รูปโล่พลังงานอัศวิน (Heraldic Energy Shield)" ความละเอียดสูง 512x512 px
    /// มีขอบโล่เรืองแสง 2 ชั้นคมกริบ, ตารางรังผึ้งพลังงานโปร่งแสงภายใน และตราสัญลักษณ์โล่เรืองแสงตรงกลาง
    /// </summary>
    public static Texture2D CreateHeraldicShieldEmblemTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.name = "T_HeraldicEnergyShield_512";
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

                float sdf = EvaluateShieldSDF(nx, ny);

                // มาสก์ตัดขอบรูปโล่ให้คมชัด (Anti-Aliased Edge)
                float insideMask = 1f - Mathf.SmoothStep(-0.012f, 0.012f, sdf);
                // แสงเรืองรอบนอกขอบโล่เล็กน้อย
                float outerRimGlow = Mathf.Exp(-Mathf.Max(0f, sdf) * 28f) * 0.45f;

                if (insideMask <= 0.001f && outerRimGlow <= 0.005f)
                {
                    pixels[y * size + x] = new Color(1f, 1f, 1f, 0f);
                    continue;
                }

                // 1. ขอบรูปโล่ชั้นนอก (หนาและสว่างชัดเพื่อให้มองเห็นเป็นรูปโล่ทันที)
                float outerFrame = Mathf.Exp(-Mathf.Pow(sdf + 0.032f, 2f) / (2f * 0.022f * 0.022f));

                // 2. เส้นขอบรูนชั้นในขนานตามทรงโล่
                float innerTrim = Mathf.Exp(-Mathf.Pow(sdf + 0.115f, 2f) / (2f * 0.010f * 0.010f)) * 0.80f;

                // 3. พื้นลายรังผึ้งพลังงานโปร่งแสงภายในตัวโล่
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
                float glassFill = 0.24f;

                // 4. ตราสัญลักษณ์ตรงกลางรูปโล่ (Guardian Diamond & Cross Crest + สันกลางโล่)
                float cy = ny - 0.06f;
                float diamondDist = Mathf.Abs(nx) * 1.15f + Mathf.Abs(cy) * 0.85f;
                float centerDiamondFill = (1f - Mathf.SmoothStep(0.16f, 0.20f, diamondDist)) * 0.60f;
                float centerDiamondRim = Mathf.Exp(-Mathf.Pow(diamondDist - 0.26f, 2f) / (2f * 0.014f * 0.014f)) * 0.85f;

                float vertRidge = Mathf.Exp(-nx * nx * 650f) * (1f - Mathf.SmoothStep(0.0f, 0.68f, Mathf.Abs(cy))) * 0.65f;
                float horizBar = Mathf.Exp(-cy * cy * 650f) * (1f - Mathf.SmoothStep(0.0f, 0.48f, Mathf.Abs(nx))) * 0.60f;
                float crest = Mathf.Clamp01(centerDiamondFill + centerDiamondRim + vertRidge + horizBar);

                float interiorAlpha = (glassFill + hexLines + outerFrame * 0.95f + innerTrim + crest * 0.85f) * insideMask;
                float finalAlpha = Mathf.Clamp01(Mathf.Max(interiorAlpha, outerRimGlow));

                // ไล่สีขาวสว่างตรงขอบโล่และตรากลาง เพื่อให้โดดเด่นมีมิติ
                float highlight = Mathf.Clamp01(outerFrame * 0.85f + innerTrim * 0.65f + crest * 0.75f);
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
        tex.name = "T_HeraldicShieldBackGlow";
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            float ny = ((y / (float)(size - 1)) * 2f) - 1f;
            for (int x = 0; x < size; x++)
            {
                float nx = ((x / (float)(size - 1)) * 2f) - 1f;
                float sdf = EvaluateShieldSDF(nx, ny);
                float halo = Mathf.Exp(-sdf * sdf * 45f) * 0.85f;
                float softFill = (1f - Mathf.SmoothStep(-0.05f, 0.14f, sdf)) * 0.35f;
                float alpha = Mathf.Clamp01(Mathf.Max(halo, softFill));
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
