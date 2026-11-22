using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// ระบบตรวจจับพื้นผิวและเสกเอฟเฟกต์ "มอนสเตอร์ตายพร้อมกองเลือดไหลนองบนพื้น 4 เลเยอร์ระดับ AAA"
/// (Monster Death Crimson Blood Pool & Splatter VFX) สำหรับ Unity 6 (URP)
/// - เรียกใช้ได้จากทุกที่ผ่าน MMOMonsterDeathBloodVFX.SpawnDeathBlood(monsterPosition, customScale)
/// - ยิง Physics.Raycast ตรวจจับพื้นผิวด้านล่างอัตโนมัติ (ข้ามคอลไลเดอร์ตัวละคร/มอนสเตอร์)
/// - วางแนบสนิทที่ hit.point + hit.normal * 0.02f ป้องกัน Z-Fighting พร้อมเอียงตามเนิน/พื้นลาดเอียงและสุ่มหมุนแกน Y (0-360°)
/// - รองรับปุ่มทดสอบ K ทั้ง New Input System และ Legacy Input พร้อมทำลายตัวเองอัตโนมัติใน 5.5 วินาที
/// </summary>
[AddComponentMenu("yeen VFX/10. [Enemy Death] มอนตายเลือดนองพื้น (Monster Death Blood Pool)")]
[DisallowMultipleComponent]
public class MMOMonsterDeathBloodVFX : MonoBehaviour
{
    public static MMOMonsterDeathBloodVFX Instance { get; private set; }

    [Header("=== 1. อ้างอิง Prefab กองเลือดมอนสเตอร์ตาย 4 เลเยอร์ ===")]
    [Tooltip("Prefab เอฟเฟกต์เลือดมอนสเตอร์ตาย (VFX_MonsterDeathBloodPool.prefab)")]
    public GameObject deathBloodVfxPrefab;

    [Header("=== 2. การตรวจจับพื้นผิวและขนาด (Ground Raycast & Scale) ===")]
    [Tooltip("ขนาดรวมของกองเลือดและละอองเลือด (1.0 = ขนาดมาตรฐาน 2.2 เมตร)")]
    [Range(0.4f, 3.0f)]
    public float bloodPoolScale = 1.0f;

    [Tooltip("ความสูงเหนือตำแหน่งมอนสเตอร์ที่ใช้เป็นจุดเริ่มยิง Raycast ลงพื้น")]
    public float raycastStartHeight = 1.5f;

    [Tooltip("ระยะยิง Raycast ลงด้านล่างสูงสุด (เมตร)")]
    public float raycastMaxDistance = 12.0f;

    [Tooltip("ระยะยกแอ่งเลือดขึ้นเหนือพื้นผิวตามแนว Normal (0.06 เมตร พ้นพื้นสนามบอสและป้องกันอาการภาพกระพริบ Z-Fighting)")]
    public float surfaceNormalOffset = 0.06f;

    [Tooltip("Layer ที่นับเป็นพื้นผิวสำหรับวางแอ่งเลือด (ค่าเริ่มต้น = ทุก Layer โดยสคริปต์จะข้ามคอลไลเดอร์ตัวละครและมอนสเตอร์ให้อัตโนมัติ)")]
    public LayerMask groundLayerMask = ~0;

    [Tooltip("ระยะเวลาทำลาย Object ทิ้งอัตโนมัติหลังเลือดจางหมดแล้ว (วินาที)")]
    public float autoDestroySeconds = 5.5f;

    [Header("=== 3. ปุ่มทดสอบเอฟเฟกต์ในโหมด Play (Test Controls) ===")]
    [Tooltip("เปิดให้กดปุ่ม K เพื่อทดสอบเสกเอฟเฟกต์มอนสเตอร์ตายและกองเลือดด้านหน้าตัวละครได้ทันที")]
    public bool enableTestHotkey = true;

    [Tooltip("ระยะห่างด้านหน้าตัวละครเมื่อกดปุ่มทดสอบ K")]
    public float testSpawnDistance = 2.0f;

    [Tooltip("แสดงกรอบคำแนะนำการทดสอบมุมซ้ายล่างของหน้าจอ")]
    public bool showTestHUD = false;

    // แคชทรัพยากร Procedural ระดับ Runtime เพื่อให้ SpawnDeathBlood ทำงานได้ 100% แม้ยังไม่ได้ลาก Prefab
    private static Mesh s_RuntimeQuadMesh;
    private static Texture2D s_RuntimeBloodPoolTex;
    private static Texture2D s_RuntimeBloodDropTex;
    private static Material s_RuntimeMatBloodPool;
    private static Material s_RuntimeMatBloodSplatter;
    private static Material s_RuntimeMatBloodMist;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
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
    }

    private void Update()
    {
        if (!enableTestHotkey) return;

        if (ReadUniversalTestKeyK())
        {
            Vector3 testPos = transform.position + transform.forward * testSpawnDistance;
            SpawnDeathBloodInternal(testPos, bloodPoolScale);
        }
    }

    /// <summary>
    /// กฎเหล็กข้อที่ 2: ฟังก์ชัน Static Public สำหรับเรียกเสกกองเลือดมอนสเตอร์ตายจากสคริปต์ศัตรู (EnemyNPC)
    /// ตัวอย่าง: MMOMonsterDeathBloodVFX.SpawnDeathBlood(transform.position);
    /// </summary>
    public static void SpawnDeathBlood(Vector3 monsterPosition, float customScale = -1f)
    {
        MMOMonsterDeathBloodVFX controller = Instance;
        if (controller == null)
        {
            controller = FindFirstObjectByType<MMOMonsterDeathBloodVFX>();
            if (controller != null)
            {
                Instance = controller;
            }
        }

        if (controller != null)
        {
            float finalScale = customScale > 0f ? customScale : controller.bloodPoolScale;
            controller.SpawnDeathBloodInternal(monsterPosition, finalScale);
        }
        else
        {
            GameObject autoHost = new GameObject("MMOMonsterDeathBloodVFX_RuntimeHost");
            controller = autoHost.AddComponent<MMOMonsterDeathBloodVFX>();
            controller.enableTestHotkey = false;
            controller.showTestHUD = false;
            float finalScale = customScale > 0f ? customScale : 1.0f;
            controller.SpawnDeathBloodInternal(monsterPosition, finalScale);
        }
    }

    /// <summary>
    /// ตรวจจับพื้นผิวด้วย Raycast วางตำแหน่งแนบพื้น เอียงตามความลาดชัน สุ่มหมุนแกน Y 0-360 องศา และสั่งทำลายใน 5.5 วินาที
    /// </summary>
    public void SpawnDeathBloodInternal(Vector3 monsterPosition, float scaleMultiplier)
    {
        float safeScale = Mathf.Max(0.2f, scaleMultiplier);

        // 1. กฎเหล็กข้อที่ 2: ยิง Physics.Raycast หาพื้นผิวด้านล่าง (รองรับพื้นราบ เนินเขา และพื้นเอียง)
        Vector3 groundPoint;
        Vector3 groundNormal;
        FindGroundSurfaceBelow(monsterPosition, out groundPoint, out groundNormal);

        // ป้องกันอาการภาพกระพริบ Z-Fighting ด้วย hit.point + hit.normal * 0.02f
        Vector3 finalSpawnPos = groundPoint + groundNormal * surfaceNormalOffset;

        // ปรับองศาเอียงตาม hit.normal ของพื้นผิว + สุ่มหมุนแกน Y รอบแกน Normal (0 - 360 องศา)
        Quaternion surfaceAlignment = Quaternion.FromToRotation(Vector3.up, groundNormal);
        Quaternion randomYawSpin = Quaternion.AngleAxis(Random.Range(0f, 360f), groundNormal);
        Quaternion finalRotation = randomYawSpin * surfaceAlignment;

        // 2. เสก Prefab กองเลือด 4 เลเยอร์ (หรือสร้างแบบ Procedural Runtime อัตโนมัติหากยังไม่ได้กำหนด Prefab)
        GameObject vfxInstance;
        if (deathBloodVfxPrefab != null)
        {
            vfxInstance = Instantiate(deathBloodVfxPrefab, finalSpawnPos, finalRotation);
        }
        else
        {
            vfxInstance = BuildRuntimeFallbackBloodVFX(finalSpawnPos, finalRotation);
        }

        vfxInstance.name = "VFX_MonsterDeathBloodPool_Instance";
        vfxInstance.transform.SetPositionAndRotation(finalSpawnPos, finalRotation);
        vfxInstance.transform.localScale = Vector3.one * safeScale;

        // สั่งเล่น ParticleSystem ครบทั้ง 4 เลเยอร์
        ParticleSystem[] systems = vfxInstance.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < systems.Length; i++)
        {
            if (systems[i] == null) continue;
            systems[i].gameObject.SetActive(true);
            systems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            systems[i].Play(true);
        }

        // 3. กฎเหล็กข้อที่ 2: ทำลาย Object ทิ้งอัตโนมัติใน 5.5 วินาทีหลังเลือดจางหมดแล้ว
        Destroy(vfxInstance, Mathf.Max(1.0f, autoDestroySeconds));
    }

    private void FindGroundSurfaceBelow(Vector3 monsterPosition, out Vector3 hitPoint, out Vector3 hitNormal)
    {
        Vector3 rayOrigin = monsterPosition + Vector3.up * Mathf.Max(0.25f, raycastStartHeight);
        RaycastHit[] hits = Physics.RaycastAll(
            rayOrigin,
            Vector3.down,
            Mathf.Max(1.0f, raycastMaxDistance),
            groundLayerMask,
            QueryTriggerInteraction.Ignore);

        if (hits != null && hits.Length > 0)
        {
            // เรียงลำดับจุดชนจากใกล้ไปไกลแล้วเลือกพื้นผิวจริงชิ้นแรกที่ไม่ใช่ตัวละครหรือมอนสเตอร์
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            for (int i = 0; i < hits.Length; i++)
            {
                Collider col = hits[i].collider;
                if (col == null) continue;
                if (IsCharacterOrDynamicEntityCollider(col)) continue;

                hitPoint = hits[i].point;
                hitNormal = hits[i].normal.sqrMagnitude > 0.001f ? hits[i].normal.normalized : Vector3.up;
                return;
            }
        }

        // หากฉากไม่มี Floor Collider ด้านล่าง ให้วางที่ระดับพื้นของตำแหน่งมอนสเตอร์
        hitPoint = monsterPosition;
        hitNormal = Vector3.up;
    }

    private static bool IsCharacterOrDynamicEntityCollider(Collider col)
    {
        if (col is CharacterController) return true;
        if (col.GetComponentInParent<EnemyNPC>() != null) return true;
        if (col.GetComponentInParent<PlayerCombat>() != null) return true;
        if (col.GetComponentInParent<LootChest>() != null) return true;
        return false;
    }

    // ================================================================================================
    // กฎเหล็กข้อที่ 2: รองรับปุ่มทดสอบ K ทั้ง New Input System และ Legacy Input
    // ================================================================================================
    private static bool ReadUniversalTestKeyK()
    {
        bool pressedNew = false;
        if (Keyboard.current != null && Keyboard.current.kKey.wasPressedThisFrame)
        {
            pressedNew = true;
        }

        bool pressedLegacy = false;
        try
        {
            if (Input.GetKeyDown(KeyCode.K))
            {
                pressedLegacy = true;
            }
        }
        catch
        {
            // ป้องกัน Error หากโปรเจกต์ตั้ง Active Input Handling เป็น Input System Package (New) อย่างเดียว
        }

        return pressedNew || pressedLegacy;
    }

    private void OnGUI()
    {
        if (!showTestHUD || !Application.isPlaying) return;

        GUI.color = new Color(0.10f, 0.01f, 0.02f, 0.84f);
        GUI.Box(new Rect(14f, Screen.height - 92f, 435f, 78f), GUIContent.none);
        GUI.color = Color.white;

        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = 13;
        style.richText = true;
        style.normal.textColor = Color.white;

        string hud =
            "<color=#CC2233><b>[AAA Monster Death Crimson Blood Pool VFX]</b></color>\n" +
            "• กดปุ่ม <b>[K]</b> เพื่อทดสอบเสกกองเลือดมอนสเตอร์ตายแนบพื้นด้านหน้าตัวละคร\n" +
            "• <b>Blood Pool Scale:</b> " + bloodPoolScale.ToString("F2") + "x  |  <b>Auto Destroy:</b> " + autoDestroySeconds.ToString("F1") + "s";

        GUI.Label(new Rect(24f, Screen.height - 86f, 415f, 66f), hud, style);
    }

    // ================================================================================================
    // RUNTIME PROCEDURAL FALLBACK (สำหรับเรียก SpawnDeathBlood ได้ทันทีแม้ยังไม่ได้ลาก Prefab)
    // ================================================================================================
    private static GameObject BuildRuntimeFallbackBloodVFX(Vector3 pos, Quaternion rot)
    {
        EnsureRuntimeAssets();

        GameObject root = new GameObject("VFX_MonsterDeathBloodPool_Runtime");
        root.transform.SetPositionAndRotation(pos, rot);

        CreateRuntimeLayer1Splatter(root.transform, s_RuntimeMatBloodSplatter);
        CreateRuntimeLayer2GroundPool(root.transform, s_RuntimeQuadMesh, s_RuntimeMatBloodPool);
        CreateRuntimeLayer3GroundDroplets(root.transform, s_RuntimeQuadMesh, s_RuntimeMatBloodPool);
        CreateRuntimeLayer4SoulMist(root.transform, s_RuntimeMatBloodMist);

        return root;
    }

    private static void EnsureRuntimeAssets()
    {
        if (s_RuntimeQuadMesh == null)
        {
            s_RuntimeQuadMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            if (s_RuntimeQuadMesh == null)
            {
                s_RuntimeQuadMesh = BuildProceduralQuadMesh();
            }
        }
        if (s_RuntimeBloodPoolTex == null) s_RuntimeBloodPoolTex = BuildRuntimeOrganicBloodPoolTexture(256, 256);
        if (s_RuntimeBloodDropTex == null) s_RuntimeBloodDropTex = BuildRuntimeBloodDropTexture(128, 128);

        if (s_RuntimeMatBloodPool == null) s_RuntimeMatBloodPool = CreateRuntimeAlphaBlendMat(s_RuntimeBloodPoolTex, Color.white);
        if (s_RuntimeMatBloodSplatter == null) s_RuntimeMatBloodSplatter = CreateRuntimeAlphaBlendMat(s_RuntimeBloodDropTex, Color.white);
        if (s_RuntimeMatBloodMist == null) s_RuntimeMatBloodMist = CreateRuntimeAlphaBlendMat(s_RuntimeBloodDropTex, Color.white);
    }

    private static Material CreateRuntimeAlphaBlendMat(Texture2D tex, Color tint)
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Particles/Standard Unlit");

        Material mat = new Material(shader);
        mat.mainTexture = tex;
        mat.color = tint;
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
        if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
        if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        mat.renderQueue = (int)RenderQueue.Transparent;
        return mat;
    }

    private static Mesh BuildProceduralQuadMesh()
    {
        Mesh m = new Mesh { name = "BloodQuad_Runtime" };
        m.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3( 0.5f, -0.5f, 0f),
            new Vector3(-0.5f,  0.5f, 0f),
            new Vector3( 0.5f,  0.5f, 0f)
        };
        m.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f)
        };
        m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        m.RecalculateNormals();
        return m;
    }

    private static void CreateRuntimeLayer1Splatter(Transform parent, Material mat)
    {
        GameObject obj = new GameObject("L1_Death_CrimsonSplatterBurst");
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = new Vector3(0f, 0.95f, 0f);

        ParticleSystem ps = obj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 0.70f;
        main.loop = false;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.65f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 6.0f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.19f);
        main.gravityModifier = 1.8f;
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.78f, 0.03f, 0.05f, 0.96f),
            new Color(0.95f, 0.07f, 0.09f, 0.98f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 48;

        var em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0.0f, 28) });

        var sh = ps.shape;
        sh.enabled = true;
        sh.shapeType = ParticleSystemShapeType.Sphere;
        sh.radius = 0.32f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.92f, 0.06f, 0.08f), 0f),
                new GradientColorKey(new Color(0.42f, 0.01f, 0.02f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0.98f, 0f),
                new GradientAlphaKey(0.88f, 0.65f),
                new GradientAlphaKey(0.0f, 1f)
            });
        col.color = new ParticleSystem.MinMaxGradient(g);

        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.08f;
        r.lengthScale = 2.4f;
        r.sharedMaterial = mat;
    }

    private static void CreateRuntimeLayer2GroundPool(Transform parent, Mesh quad, Material mat)
    {
        GameObject obj = new GameObject("L2_Expanding_GroundBloodPool");
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = Vector3.zero;
        obj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        ParticleSystem ps = obj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 4.90f;
        main.loop = false;
        main.playOnAwake = true;
        main.startDelay = 0.08f;
        main.startLifetime = 4.72f;
        main.startSpeed = 0f;
        main.startSize = 2.2f;
        main.startColor = new ParticleSystem.MinMaxGradient(Color.white);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 2;

        var em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0.0f, 1) });
        var sh = ps.shape;
        sh.enabled = false;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0.00f, 0.15f, 5.2f, 5.2f),
            new Keyframe(0.254f, 1.15f, 0.25f, 0.25f),
            new Keyframe(1.00f, 1.18f, 0.0f, 0.0f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.92f, 0.05f, 0.07f), 0.00f),
                new GradientColorKey(new Color(0.58f, 0.02f, 0.03f), 0.30f),
                new GradientColorKey(new Color(0.36f, 0.01f, 0.02f), 1.00f)
            },
            new[]
            {
                new GradientAlphaKey(0.00f, 0.00f),
                new GradientAlphaKey(0.92f, 0.06f),
                new GradientAlphaKey(0.92f, 0.60f),
                new GradientAlphaKey(0.00f, 1.00f)
            });
        col.color = new ParticleSystem.MinMaxGradient(g);

        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Mesh;
        r.mesh = quad;
        r.alignment = ParticleSystemRenderSpace.Local;
        r.sharedMaterial = mat;
    }

    private static void CreateRuntimeLayer3GroundDroplets(Transform parent, Mesh quad, Material mat)
    {
        GameObject obj = new GameObject("L3_Peripheral_GroundDroplets");
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = new Vector3(0f, 0.004f, 0f);
        obj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        ParticleSystem ps = obj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 4.70f;
        main.loop = false;
        main.playOnAwake = true;
        main.startDelay = 0.12f;
        main.startLifetime = 4.48f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.26f, 0.56f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(Color.white);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 16;

        var em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0.0f, 8) });

        var sh = ps.shape;
        sh.enabled = true;
        sh.shapeType = ParticleSystemShapeType.Circle;
        sh.radius = 0.85f;
        sh.radiusThickness = 0.65f;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0.00f, 0.25f, 4.0f, 4.0f),
            new Keyframe(0.18f, 1.00f, 0.1f, 0.1f),
            new Keyframe(1.00f, 1.02f, 0.0f, 0.0f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.72f, 0.03f, 0.05f), 0.00f),
                new GradientColorKey(new Color(0.30f, 0.01f, 0.02f), 0.45f),
                new GradientColorKey(new Color(0.24f, 0.01f, 0.015f), 1.00f)
            },
            new[]
            {
                new GradientAlphaKey(0.00f, 0.00f),
                new GradientAlphaKey(0.88f, 0.08f),
                new GradientAlphaKey(0.88f, 0.60f),
                new GradientAlphaKey(0.00f, 1.00f)
            });
        col.color = new ParticleSystem.MinMaxGradient(g);

        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Mesh;
        r.mesh = quad;
        r.alignment = ParticleSystemRenderSpace.Local;
        r.sharedMaterial = mat;
    }

    private static void CreateRuntimeLayer4SoulMist(Transform parent, Material mat)
    {
        GameObject obj = new GameObject("L4_Rising_CrimsonSoulMist");
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = new Vector3(0f, 0.12f, 0f);
        obj.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

        ParticleSystem ps = obj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 1.60f;
        main.loop = false;
        main.playOnAwake = true;
        main.startDelay = 0.10f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.00f, 1.40f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.70f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.55f, 1.15f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.70f, 0.06f, 0.09f, 0.34f),
            new Color(0.88f, 0.14f, 0.16f, 0.42f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 24;

        var em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0.0f, 12) });

        var sh = ps.shape;
        sh.enabled = true;
        sh.shapeType = ParticleSystemShapeType.Circle;
        sh.radius = 0.62f;

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(0f);
        vel.y = new ParticleSystem.MinMaxCurve(0.55f);
        vel.z = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalX = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalY = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalZ = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalOffsetX = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalOffsetY = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalOffsetZ = new ParticleSystem.MinMaxCurve(0f);
        vel.radial = new ParticleSystem.MinMaxCurve(0.12f);
        vel.speedModifier = new ParticleSystem.MinMaxCurve(1f);

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.55f),
            new Keyframe(1f, 1.55f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.85f, 0.10f, 0.12f), 0f),
                new GradientColorKey(new Color(0.45f, 0.03f, 0.05f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.42f, 0.22f),
                new GradientAlphaKey(0f, 1f)
            });
        col.color = new ParticleSystem.MinMaxGradient(g);

        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.sharedMaterial = mat;
    }

    private static Texture2D BuildRuntimeOrganicBloodPoolTexture(int width, int height)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color[] pixels = new Color[width * height];

        for (int y = 0; y < height; y++)
        {
            float ny = ((float)y / (height - 1)) * 2f - 1f;
            for (int x = 0; x < width; x++)
            {
                float nx = ((float)x / (width - 1)) * 2f - 1f;
                float dist = Mathf.Sqrt(nx * nx + ny * ny);
                float angle = Mathf.Atan2(ny, nx);

                // สมการ Perlin Noise 2 ความถี่รอบจุดศูนย์กลางเพื่อสร้างขอบแอ่งเลือดธรรมชาติไร้รอยต่อ
                float pxLow = Mathf.Cos(angle) * 1.35f + 4.2f;
                float pyLow = Mathf.Sin(angle) * 1.35f + 2.7f;
                float lowFreq = Mathf.PerlinNoise(pxLow, pyLow);

                float pxHigh = Mathf.Cos(angle) * 3.8f + 11.5f;
                float pyHigh = Mathf.Sin(angle) * 3.8f + 8.3f;
                float highFreq = Mathf.PerlinNoise(pxHigh, pyHigh);

                float organicRadius = 0.56f + (lowFreq - 0.5f) * 0.34f + (highFreq - 0.5f) * 0.14f;
                float alpha = 1.0f - SmoothStep(organicRadius - 0.045f, organicRadius + 0.025f, dist);

                float internalNoise = Mathf.PerlinNoise(nx * 3.2f + 7.1f, ny * 3.2f + 5.4f);
                float depthShade = Mathf.Clamp01(1.0f - (dist / Mathf.Max(0.1f, organicRadius)));
                float brightness = Mathf.Lerp(0.95f, 0.52f, depthShade * 0.72f + internalNoise * 0.28f);

                // สีขาวอมเทาเพื่อคูณกับ Color over Lifetime อย่างสมจริง (กลางแอ่งเข้มข้น ขอบแอ่งใสกว่าเล็กน้อย)
                Color col = new Color(brightness, brightness * 0.85f, brightness * 0.85f, alpha);
                pixels[y * width + x] = col;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    private static Texture2D BuildRuntimeBloodDropTexture(int width, int height)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color[] pixels = new Color[width * height];

        for (int y = 0; y < height; y++)
        {
            float ny = ((float)y / (height - 1)) * 2f - 1f;
            for (int x = 0; x < width; x++)
            {
                float nx = ((float)x / (width - 1)) * 2f - 1f;
                float dist = Mathf.Sqrt(nx * nx + ny * ny);
                float alpha = Mathf.Pow(1.0f - SmoothStep(0.15f, 0.96f, dist), 1.35f);
                float shade = Mathf.Lerp(0.72f, 1.0f, SmoothStep(0.0f, 0.7f, dist));
                pixels[y * width + x] = new Color(shade, shade * 0.9f, shade * 0.9f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp01((x - edge0) / Mathf.Max(0.00001f, edge1 - edge0));
        return t * t * (3f - 2f * t);
    }
}
