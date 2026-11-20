using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// ระบบเสกและควบคุมเอฟเฟกต์ "คริติคอลฮิตสีแดงขนาดใหญ่และดุดันระดับ AAA"
/// (Giant Crimson Critical Hit Slash & Impact VFX) สำหรับ Unity 6 (URP)
/// - เรียกใช้ได้จากทุกที่ผ่าน MMOCriticalHitVFX.SpawnCriticalHit(worldHitPosition, customScale)
/// - หันหน้าเข้าหากล้องผู้เล่นเสมอ (LookAt Camera.main) พร้อมสุ่มหมุนแกน Z (-18° ถึง +18°)
/// - เสกป้าย 3D TextMesh คำว่า "CRITICAL!" ตัวหนาเอียง (BoldItalic) เด้ง Pop-In Scale (2.2x -> 1.0x)
/// - มี MicroCameraShake สั่นกล้อง 0.12 วินาที และปุ่มทดสอบ T หรือคลิกกลางเมาส์ (รองรับ New + Legacy Input)
/// </summary>
[DisallowMultipleComponent]
public class MMOCriticalHitVFX : MonoBehaviour
{
    public static MMOCriticalHitVFX Instance { get; private set; }

    [Header("=== 1. อ้างอิง Prefab เอฟเฟกต์คริติคอล 4 เลเยอร์ ===")]
    [Tooltip("Prefab เอฟเฟกต์คริติคอลสีแดงขนาดใหญ่ (VFX_CrimsonCriticalHit.prefab)")]
    public GameObject criticalVfxPrefab;

    [Header("=== 2. ปรับขนาดและตำแหน่งจุดปะทะ (Scale & Offset) ===")]
    [Tooltip("ปรับขนาดรวมของเอฟเฟกต์คริติคอล (1.0 = ขนาดมาตรฐาน AAA 3.8 เมตร)")]
    [Range(0.3f, 3.0f)]
    public float criticalScale = 1.0f;

    [Tooltip("ระยะขยับเอฟเฟกต์เข้าหากล้องเล็กน้อยเพื่อป้องกันรอยฟันจมเข้าไปในตัวศัตรู")]
    [Range(0f, 1.0f)]
    public float towardCameraOffset = 0.22f;

    [Tooltip("ระยะเวลาทำลาย Prefab เอฟเฟกต์อัตโนมัติ (วินาที)")]
    public float autoDestroySeconds = 2.0f;

    [Header("=== 3. ป้าย 3D CRITICAL! และแรงสั่นกล้อง (3D Pop Text & Micro Camera Shake) ===")]
    [Tooltip("เปิดใช้งานป้าย 3D TextMesh คำว่า CRITICAL! เด้งขึ้นเหนือจุดปะทะ")]
    public bool spawnCriticalTextMesh = true;

    [Tooltip("ความสูงที่ป้าย CRITICAL! จะโผล่ขึ้นเหนือจุดปะทะ")]
    public float criticalTextHeightOffset = 0.75f;

    [Tooltip("เปิดใช้งานแรงสั่นกล้องเบาๆ (Micro Camera Shake) เพิ่มความหนักแน่น")]
    public bool enableMicroCameraShake = true;

    [Tooltip("ระยะเวลาสั่นกล้อง (วินาที - ค่ามาตรฐาน AAA = 0.12 วินาที)")]
    [Range(0.04f, 0.35f)]
    public float cameraShakeDuration = 0.12f;

    [Tooltip("ความแรงในการสั่นกล้อง")]
    [Range(0.02f, 0.45f)]
    public float cameraShakeMagnitude = 0.14f;

    [Header("=== 4. ปุ่มทดสอบเอฟเฟกต์ในโหมด Play (Test Controls) ===")]
    [Tooltip("เปิดให้กดปุ่ม T หรือคลิกกลางเมาส์เพื่อทดสอบเสกคริติคอลด้านหน้าตัวละครได้ทันที")]
    public bool enableTestHotkeys = true;

    [Tooltip("ระยะห่างด้านหน้าตัวละครตอนกดปุ่มทดสอบ")]
    public float testSpawnDistance = 2.2f;

    [Tooltip("ความสูงจุดปะทะตอนกดปุ่มทดสอบ")]
    public float testSpawnHeight = 1.35f;

    [Tooltip("แสดงกรอบคำแนะนำการทดสอบมุมซ้ายล่างของหน้าจอ")]
    public bool showTestHUD = false;

    // สำหรับจัดการ MicroCameraShake แบบไม่ให้กล้องค้างหรือเพี้ยน
    private Coroutine _activeShakeCoroutine;
    private Vector3 _lastAppliedShakeOffset = Vector3.zero;
    private Transform _shakenCameraTransform;

    // แคชทรัพยากร Procedural สำรองระดับ Runtime (รับประกันว่า SpawnCriticalHit ทำงานได้ 100% แม้ยังไม่ได้ลาก Prefab)
    private static Mesh s_RuntimeQuadMesh;
    private static Texture2D s_RuntimeCrossSlashTex;
    private static Texture2D s_RuntimeCoreFlashTex;
    private static Texture2D s_RuntimeShockwaveTex;
    private static Material s_RuntimeMatCoreFlash;
    private static Material s_RuntimeMatCrossSlash;
    private static Material s_RuntimeMatSparks;
    private static Material s_RuntimeMatShockwave;

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
        RestoreCameraShakeOffset();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        if (!enableTestHotkeys) return;

        if (ReadUniversalTestTrigger())
        {
            Vector3 testPos = transform.position + transform.forward * testSpawnDistance + Vector3.up * testSpawnHeight;
            SpawnCriticalHitInternal(testPos, criticalScale);
        }
    }

    /// <summary>
    /// กฎเหล็กข้อที่ 2: ฟังก์ชัน Static Public สำหรับเสกเอฟเฟกต์คริติคอลสีแดงขนาดใหญ่จากทุกสคริปต์ในโปรเจกต์
    /// ตัวอย่างการเรียกใช้: MMOCriticalHitVFX.SpawnCriticalHit(enemy.transform.position + Vector3.up * 1.2f);
    /// </summary>
    public static void SpawnCriticalHit(Vector3 worldHitPosition, float customScale = -1f)
    {
        MMOCriticalHitVFX controller = Instance;
        if (controller == null)
        {
            controller = FindFirstObjectByType<MMOCriticalHitVFX>();
            if (controller != null)
            {
                Instance = controller;
            }
        }

        if (controller != null)
        {
            float finalScale = customScale > 0f ? customScale : controller.criticalScale;
            controller.SpawnCriticalHitInternal(worldHitPosition, finalScale);
        }
        else
        {
            // กรณีเรียกใช้งานจากฉากที่ยังไม่มี MMOCriticalHitVFX แปะอยู่ ให้สร้างโฮสต์ชั่วคราวทำงานแทนทันที
            GameObject autoHost = new GameObject("MMOCriticalHitVFX_RuntimeHost");
            controller = autoHost.AddComponent<MMOCriticalHitVFX>();
            controller.enableTestHotkeys = false;
            controller.showTestHUD = false;
            float finalScale = customScale > 0f ? customScale : 1.0f;
            controller.SpawnCriticalHitInternal(worldHitPosition, finalScale);
        }
    }

    /// <summary>
    /// ฟังก์ชันภายในสำหรับเสก Prefab เอฟเฟกต์ หันหน้าเข้าหากล้อง สุ่มหมุนแกน Z เสกป้าย 3D CRITICAL! และสั่งสั่นกล้อง
    /// </summary>
    public void SpawnCriticalHitInternal(Vector3 worldHitPosition, float scaleMultiplier)
    {
        float safeScale = Mathf.Max(0.1f, scaleMultiplier);
        Camera mainCam = Camera.main;

        Vector3 spawnPos = worldHitPosition;
        if (mainCam != null && towardCameraOffset > 0f)
        {
            Vector3 dirToCam = (mainCam.transform.position - worldHitPosition).normalized;
            spawnPos += dirToCam * towardCameraOffset;
        }

        // 1. เสก Prefab เอฟเฟกต์คริติคอล 4 เลเยอร์ (หรือสร้างแบบ Procedural Runtime อัตโนมัติหากยังไม่มี Prefab)
        GameObject vfxInstance;
        if (criticalVfxPrefab != null)
        {
            vfxInstance = Instantiate(criticalVfxPrefab, spawnPos, Quaternion.identity);
        }
        else
        {
            vfxInstance = BuildRuntimeFallbackCriticalVFX(spawnPos);
        }

        vfxInstance.name = "VFX_CrimsonCriticalHit_Instance";
        vfxInstance.transform.position = spawnPos;
        vfxInstance.transform.localScale = Vector3.one * safeScale;

        // 2. กฎเหล็กข้อที่ 2: สั่ง LookAt(Camera.main.transform) ให้รอยฟันกากบาทหันเข้าหากล้องผู้เล่นเสมอ
        //    และสุ่มหมุนแกน Z (-18° ถึง +18°) เพื่อไม่ให้รอยฟันซ้ำซากเมื่อตีคริติคอลรัวๆ
        if (mainCam != null)
        {
            vfxInstance.transform.LookAt(mainCam.transform);
            vfxInstance.transform.Rotate(0f, 0f, Random.Range(-18f, 18f), Space.Self);
        }
        else
        {
            vfxInstance.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(-18f, 18f));
        }

        // สั่งเล่น ParticleSystem ทุกเลเยอร์ให้แน่นอน
        ParticleSystem[] systems = vfxInstance.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < systems.Length; i++)
        {
            if (systems[i] == null) continue;
            systems[i].gameObject.SetActive(true);
            systems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            systems[i].Play(true);
        }

        // ทำลายตัวเองอัตโนมัติใน 2 วินาที
        Destroy(vfxInstance, Mathf.Max(0.5f, autoDestroySeconds));

        // 3. กฎเหล็กข้อที่ 2: เสกตัวหนังสือ 3D TextMesh คำว่า "CRITICAL!" สีแดงสดตัวหนาเอียง Pop-In (2.2x -> 1.0x)
        if (spawnCriticalTextMesh)
        {
            Spawn3DCriticalText(worldHitPosition + Vector3.up * (criticalTextHeightOffset * safeScale), safeScale);
        }

        // 4. กฎเหล็กข้อที่ 2: สั่ง MicroCameraShake สั่นกล้อง 0.12 วินาที เพิ่ม Impact Punch
        if (enableMicroCameraShake && gameObject.activeInHierarchy)
        {
            TriggerMicroCameraShake(cameraShakeDuration, cameraShakeMagnitude * Mathf.Clamp(safeScale, 0.7f, 1.5f));
        }
    }

    private void Spawn3DCriticalText(Vector3 startPos, float scaleMultiplier)
    {
        GameObject textRoot = new GameObject("CRITICAL_3D_PopText");
        textRoot.transform.position = startPos;

        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            textRoot.transform.rotation = mainCam.transform.rotation;
        }

        // เงาขอบดำด้านหลังเล็กน้อยเพื่อให้คำว่า CRITICAL! เด่นชัดตัดกับแสงแฟลชสีแดงสว่างจ้า
        GameObject shadowObj = new GameObject("Shadow");
        shadowObj.transform.SetParent(textRoot.transform, false);
        shadowObj.transform.localPosition = new Vector3(0.03f, -0.03f, 0.02f);
        TextMesh shadowMesh = shadowObj.AddComponent<TextMesh>();
        shadowMesh.text = "CRITICAL!";
        shadowMesh.fontSize = 64;
        shadowMesh.characterSize = 0.095f;
        shadowMesh.anchor = TextAnchor.MiddleCenter;
        shadowMesh.alignment = TextAlignment.Center;
        shadowMesh.fontStyle = FontStyle.BoldAndItalic;
        shadowMesh.color = new Color(0.12f, 0.0f, 0.02f, 0.92f);

        // ข้อความหลัก "CRITICAL!" สีแดงสดตัวหนาเอียง (BoldItalic)
        TextMesh mainMesh = textRoot.AddComponent<TextMesh>();
        mainMesh.text = "CRITICAL!";
        mainMesh.fontSize = 64;
        mainMesh.characterSize = 0.095f;
        mainMesh.anchor = TextAnchor.MiddleCenter;
        mainMesh.alignment = TextAlignment.Center;
        mainMesh.fontStyle = FontStyle.BoldAndItalic;
        mainMesh.color = new Color(1.0f, 0.14f, 0.14f, 1.0f);

        CriticalPopTextAnimator popAnim = textRoot.AddComponent<CriticalPopTextAnimator>();
        popAnim.Initialize(mainMesh, shadowMesh, scaleMultiplier);
    }

    public void TriggerMicroCameraShake(float duration = 0.12f, float magnitude = 0.14f)
    {
        if (_activeShakeCoroutine != null)
        {
            StopCoroutine(_activeShakeCoroutine);
            RestoreCameraShakeOffset();
        }

        _activeShakeCoroutine = StartCoroutine(MicroCameraShakeRoutine(duration, magnitude));
    }

    private IEnumerator MicroCameraShakeRoutine(float duration, float magnitude)
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            _activeShakeCoroutine = null;
            yield break;
        }

        _shakenCameraTransform = cam.transform;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (_shakenCameraTransform == null) break;

            // ถอนค่าสั่นเฟรมก่อนออกก่อน แล้วค่อยบวกค่าสั่นเฟรมใหม่ (ปลอดภัยกับสคริปต์กล้องตามผู้เล่น)
            RestoreCameraShakeOffset();

            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, duration));
            float damper = (1f - progress) * (1f - progress);

            Vector3 randomDir = Random.insideUnitSphere;
            randomDir.z *= 0.35f;
            Vector3 offset = randomDir * (magnitude * damper);

            _shakenCameraTransform.localPosition += offset;
            _lastAppliedShakeOffset = offset;

            yield return null;
        }

        RestoreCameraShakeOffset();
        _activeShakeCoroutine = null;
    }

    private void RestoreCameraShakeOffset()
    {
        if (_shakenCameraTransform != null && _lastAppliedShakeOffset != Vector3.zero)
        {
            _shakenCameraTransform.localPosition -= _lastAppliedShakeOffset;
        }
        _lastAppliedShakeOffset = Vector3.zero;
    }

    // ================================================================================================
    // กฎเหล็กข้อที่ 2: รองรับปุ่มทดสอบ T และคลิกกลางเมาส์ทั้ง New Input System และ Legacy Input
    // ================================================================================================
    private static bool ReadUniversalTestTrigger()
    {
        bool pressedNew = false;
        if (Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame)
        {
            pressedNew = true;
        }
        if (Mouse.current != null && Mouse.current.middleButton.wasPressedThisFrame)
        {
            pressedNew = true;
        }

        bool pressedLegacy = false;
        try
        {
            if (Input.GetKeyDown(KeyCode.T) || Input.GetMouseButtonDown(2))
            {
                pressedLegacy = true;
            }
        }
        catch
        {
            // ป้องกัน Error เมื่อโปรเจกต์ตั้ง Active Input Handling เป็น Input System Package (New) อย่างเดียว
        }

        return pressedNew || pressedLegacy;
    }

    private void OnGUI()
    {
        if (!showTestHUD || !Application.isPlaying) return;

        GUI.color = new Color(0.12f, 0.02f, 0.03f, 0.84f);
        GUI.Box(new Rect(14f, Screen.height - 96f, 430f, 82f), GUIContent.none);
        GUI.color = Color.white;

        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = 13;
        style.richText = true;
        style.normal.textColor = Color.white;

        string hud =
            "<color=#FF4444><b>[AAA Giant Crimson Critical Hit VFX]</b></color>\n" +
            "• กดปุ่ม <b>[T]</b> หรือ <b>คลิกกลางเมาส์</b> เพื่อทดสอบเสกคริติคอลด้านหน้าตัวละคร\n" +
            "• <b>Critical Scale:</b> " + criticalScale.ToString("F2") + "x  |  <b>Micro Shake:</b> " + cameraShakeDuration.ToString("F2") + "s";

        GUI.Label(new Rect(24f, Screen.height - 90f, 412f, 70f), hud, style);
    }

    // ================================================================================================
    // RUNTIME PROCEDURAL FALLBACK (ทำงานอัตโนมัติหากยังไม่ได้ผูก Prefab เพื่อไม่ให้เอฟเฟกต์หาย)
    // ================================================================================================
    private static GameObject BuildRuntimeFallbackCriticalVFX(Vector3 spawnPos)
    {
        EnsureRuntimeAssets();

        GameObject root = new GameObject("VFX_CrimsonCriticalHit_Runtime");
        root.transform.position = spawnPos;

        CreateRuntimeLayer1CoreFlash(root.transform, s_RuntimeMatCoreFlash);
        CreateRuntimeLayer2CrossSlash(root.transform, s_RuntimeQuadMesh, s_RuntimeMatCrossSlash);
        CreateRuntimeLayer3Sparks(root.transform, s_RuntimeMatSparks);
        CreateRuntimeLayer4Shockwave(root.transform, s_RuntimeQuadMesh, s_RuntimeMatShockwave);

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
        if (s_RuntimeCrossSlashTex == null) s_RuntimeCrossSlashTex = BuildRuntimeCrossSlashTex(512, 512);
        if (s_RuntimeCoreFlashTex == null) s_RuntimeCoreFlashTex = BuildRuntimeCoreFlashTex(256, 256);
        if (s_RuntimeShockwaveTex == null) s_RuntimeShockwaveTex = BuildRuntimeShockwaveTex(256, 256);

        if (s_RuntimeMatCoreFlash == null) s_RuntimeMatCoreFlash = CreateRuntimeAdditiveMat(s_RuntimeCoreFlashTex, new Color(1.5f, 0.35f, 0.28f, 1f));
        if (s_RuntimeMatCrossSlash == null) s_RuntimeMatCrossSlash = CreateRuntimeAdditiveMat(s_RuntimeCrossSlashTex, new Color(1.6f, 0.25f, 0.20f, 1f));
        if (s_RuntimeMatSparks == null) s_RuntimeMatSparks = CreateRuntimeAdditiveMat(s_RuntimeCoreFlashTex, new Color(1.8f, 0.45f, 0.16f, 1f));
        if (s_RuntimeMatShockwave == null) s_RuntimeMatShockwave = CreateRuntimeAdditiveMat(s_RuntimeShockwaveTex, new Color(1.4f, 0.12f, 0.15f, 1f));
    }

    private static Material CreateRuntimeAdditiveMat(Texture2D tex, Color baseColor)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        Material mat = new Material(shader);
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseColor);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", baseColor);
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 2f);
        if (mat.HasProperty("_ColorMode")) mat.SetFloat("_ColorMode", 0f);
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
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
        mat.renderQueue = (int)RenderQueue.Transparent + 50;
        return mat;
    }

    private static Mesh BuildProceduralQuadMesh()
    {
        Mesh m = new Mesh { name = "Quad_Runtime" };
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

    private static void CreateRuntimeLayer1CoreFlash(Transform parent, Material mat)
    {
        GameObject obj = new GameObject("L1_Crimson_CoreFlash");
        obj.transform.SetParent(parent, false);
        ParticleSystem ps = obj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 0.20f;
        main.loop = false;
        main.playOnAwake = true;
        main.startLifetime = 0.16f;
        main.startSpeed = 0f;
        main.startSize = 2.6f;
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(5.2f, 0.85f, 0.65f, 1f));
        main.maxParticles = 2;

        var em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0.0f, 1) });
        var sh = ps.shape;
        sh.enabled = false;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.12f, 9.0f, 9.0f),
            new Keyframe(0.22f, 1.12f, 0.4f, 0.4f),
            new Keyframe(1f, 0.0f, -2.0f, -2.0f)));

        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.sharedMaterial = mat;
    }

    private static void CreateRuntimeLayer2CrossSlash(Transform parent, Mesh quad, Material mat)
    {
        GameObject obj = new GameObject("L2_Giant_CrimsonXCrossSlash");
        obj.transform.SetParent(parent, false);
        ParticleSystem ps = obj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 0.30f;
        main.loop = false;
        main.playOnAwake = true;
        main.startDelay = 0.02f;
        main.startLifetime = 0.26f;
        main.startSpeed = 0f;
        main.startSize = 3.8f;
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(4.5f, 0.36f, 0.24f, 1f));
        main.maxParticles = 2;

        var em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0.0f, 1) });
        var sh = ps.shape;
        sh.enabled = false;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.18f, 11.0f, 11.0f),
            new Keyframe(0.23f, 1.05f, 0.4f, 0.4f),
            new Keyframe(0.75f, 1.08f, 0.0f, 0.0f),
            new Keyframe(1f, 0.92f, -0.8f, -0.8f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1.0f, 0.92f, 0.88f), 0f),
                new GradientColorKey(new Color(1.0f, 0.18f, 0.14f), 0.35f),
                new GradientColorKey(new Color(0.70f, 0.02f, 0.04f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.08f),
                new GradientAlphaKey(0.95f, 0.52f),
                new GradientAlphaKey(0f, 1f)
            });
        col.color = new ParticleSystem.MinMaxGradient(g);

        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Mesh;
        r.mesh = quad;
        r.alignment = ParticleSystemRenderSpace.Local;
        r.sharedMaterial = mat;
    }

    private static void CreateRuntimeLayer3Sparks(Transform parent, Material mat)
    {
        GameObject obj = new GameObject("L3_HighVelocity_CrimsonSparks");
        obj.transform.SetParent(parent, false);
        ParticleSystem ps = obj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 0.45f;
        main.loop = false;
        main.playOnAwake = true;
        main.startDelay = 0.05f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.37f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(9f, 18f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(4.8f, 0.55f, 0.18f, 1f),
            new Color(5.2f, 1.30f, 0.45f, 1f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 64;

        var em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0.0f, 32) });

        var sh = ps.shape;
        sh.enabled = true;
        sh.shapeType = ParticleSystemShapeType.Sphere;
        sh.radius = 0.18f;

        var lv = ps.limitVelocityOverLifetime;
        lv.enabled = true;
        lv.limit = 4.5f;
        lv.dampen = 0.22f;

        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.06f;
        r.lengthScale = 4.5f;
        r.sharedMaterial = mat;
    }

    private static void CreateRuntimeLayer4Shockwave(Transform parent, Mesh quad, Material mat)
    {
        GameObject obj = new GameObject("L4_Crimson_ShockwaveRing");
        obj.transform.SetParent(parent, false);
        ParticleSystem ps = obj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 0.35f;
        main.loop = false;
        main.playOnAwake = true;
        main.startDelay = 0.10f;
        main.startLifetime = 0.24f;
        main.startSpeed = 0f;
        main.startSize = 3.4f;
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(3.0f, 0.14f, 0.16f, 1f));
        main.maxParticles = 2;

        var em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0.0f, 1) });
        var sh = ps.shape;
        sh.enabled = false;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.18f, 6.2f, 6.2f),
            new Keyframe(0.45f, 0.88f, 0.9f, 0.9f),
            new Keyframe(1f, 1.12f, 0.0f, 0.0f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1.0f, 0.45f, 0.35f), 0f),
                new GradientColorKey(new Color(0.85f, 0.06f, 0.09f), 0.6f),
                new GradientColorKey(new Color(0.45f, 0.01f, 0.03f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.14f),
                new GradientAlphaKey(0.65f, 0.62f),
                new GradientAlphaKey(0f, 1f)
            });
        col.color = new ParticleSystem.MinMaxGradient(g);

        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Mesh;
        r.mesh = quad;
        r.alignment = ParticleSystemRenderSpace.Local;
        r.sharedMaterial = mat;
    }

    private static Texture2D BuildRuntimeCrossSlashTex(int width, int height)
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

                float u1 = (nx + ny) * 0.70710678f;
                float v1 = (nx - ny) * 0.70710678f;
                float slashA = EvalTaperedBlade(u1, v1, 1.15f, 0.072f);

                float u2 = (nx - ny) * 0.70710678f;
                float v2 = (nx + ny) * 0.70710678f;
                float slashB = EvalTaperedBlade(u2, v2, 1.15f, 0.068f);

                float horizCut = EvalTaperedBlade(nx, ny, 0.96f, 0.024f) * 0.85f;
                float coreBurst = Mathf.Exp(-(nx * nx + ny * ny) * 26f) * 0.72f;

                float val = Mathf.Clamp01(Mathf.Max(Mathf.Max(slashA, slashB), horizCut) + coreBurst * 0.55f);
                float edgeMask = 1f - SmoothStep(0.86f, 0.99f, Mathf.Max(Mathf.Abs(nx), Mathf.Abs(ny)));
                val *= edgeMask;

                Color col = Color.Lerp(
                    new Color(1.0f, 0.08f, 0.10f, val),
                    new Color(1.0f, 0.96f, 0.92f, val),
                    Mathf.Pow(val, 2.2f));
                col.a = val;
                pixels[y * width + x] = col;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    private static Texture2D BuildRuntimeCoreFlashTex(int width, int height)
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
                float edgeMask = 1f - SmoothStep(0.75f, 0.98f, dist);

                float core = Mathf.Exp(-dist * dist * 14f);
                float glow = Mathf.Exp(-dist * dist * 3.8f) * 0.65f;
                float val = Mathf.Clamp01((core + glow) * edgeMask);

                Color col = Color.Lerp(
                    new Color(1.0f, 0.12f, 0.14f, val),
                    new Color(1.0f, 0.95f, 0.94f, val),
                    Mathf.Pow(val, 1.5f));
                col.a = val;
                pixels[y * width + x] = col;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    private static Texture2D BuildRuntimeShockwaveTex(int width, int height)
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
                float outerCutoff = 1f - SmoothStep(0.88f, 0.99f, dist);

                float crispRing = Mathf.Exp(-Mathf.Pow((dist - 0.76f) / 0.048f, 2f));
                float innerFade = Mathf.Exp(-Mathf.Pow((dist - 0.64f) / 0.11f, 2f)) * 0.42f * SmoothStep(0.36f, 0.68f, dist);
                float val = Mathf.Clamp01((crispRing + innerFade) * outerCutoff);

                Color col = Color.Lerp(
                    new Color(0.88f, 0.04f, 0.08f, val),
                    new Color(1.0f, 0.72f, 0.65f, val),
                    Mathf.Pow(crispRing, 1.8f));
                col.a = val;
                pixels[y * width + x] = col;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    private static float EvalTaperedBlade(float alongAxis, float perpAxis, float halfLen, float maxHalfWidth)
    {
        float normAlong = Mathf.Abs(alongAxis) / Mathf.Max(0.001f, halfLen);
        if (normAlong >= 1f) return 0f;

        float taper = Mathf.Pow(1f - normAlong * normAlong, 1.65f);
        float localWidth = Mathf.Max(0.002f, maxHalfWidth * taper);
        float d = Mathf.Abs(perpAxis) / localWidth;

        float coreBlade = Mathf.Exp(-d * d * 2.4f);
        float rimGlow = Mathf.Exp(-d * d * 0.65f) * 0.45f;
        return Mathf.Clamp01((coreBlade + rimGlow) * (1f - SmoothStep(0.80f, 1.0f, normAlong)));
    }

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp01((x - edge0) / Mathf.Max(0.00001f, edge1 - edge0));
        return t * t * (3f - 2f * t);
    }
}

/// <summary>
/// คอมโพเนนต์ควบคุมแอนิเมชันป้าย 3D "CRITICAL!" ให้เด้งกระแทก Pop-In Scale (2.2x -> 1.0x)
/// ลอยขึ้นเหนือจุดปะทะ หันเข้าหากล้องตลอดเวลา แล้วค่อยๆ จางหายและทำลายตัวเองอัตโนมัติ
/// </summary>
public class CriticalPopTextAnimator : MonoBehaviour
{
    private TextMesh _mainText;
    private TextMesh _shadowText;
    private Color _baseMainColor;
    private Color _baseShadowColor;
    private float _baseScale = 1f;
    private float _elapsed = 0f;

    private const float PopDuration = 0.14f;
    private const float TotalLifetime = 0.92f;
    private const float RiseSpeed = 1.25f;

    public void Initialize(TextMesh mainText, TextMesh shadowText, float scaleMultiplier)
    {
        _mainText = mainText;
        _shadowText = shadowText;
        _baseMainColor = mainText != null ? mainText.color : Color.red;
        _baseShadowColor = shadowText != null ? shadowText.color : Color.black;
        _baseScale = Mathf.Clamp(scaleMultiplier, 0.5f, 2.5f);
        transform.localScale = Vector3.one * (_baseScale * 2.2f);
    }

    private void Update()
    {
        _elapsed += Time.deltaTime;
        float lifeT = Mathf.Clamp01(_elapsed / TotalLifetime);

        // Pop-In Scale (2.2x -> 1.0x) ด้วย Ease-Out หนักแน่น
        float popT = Mathf.Clamp01(_elapsed / PopDuration);
        float easedPop = 1f - Mathf.Pow(1f - popT, 3f);
        float scaleFactor = Mathf.Lerp(2.2f, 1.0f, easedPop);
        transform.localScale = Vector3.one * (_baseScale * scaleFactor);

        // ลอยขึ้นด้านบนแบบชะลอความเร็วลงเรื่อยๆ
        float speedCurve = Mathf.Lerp(RiseSpeed, RiseSpeed * 0.25f, lifeT);
        transform.position += Vector3.up * (speedCurve * Time.deltaTime);

        // หันหน้าเข้าหากล้องผู้เล่นเสมอ (ไม่กลับด้าน)
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            transform.rotation = mainCam.transform.rotation;
        }

        // ค่อยๆ จางหายในช่วง 40% สุดท้าย
        float alpha = lifeT < 0.58f ? 1f : 1f - Mathf.Clamp01((lifeT - 0.58f) / 0.42f);
        if (_mainText != null)
        {
            _mainText.color = new Color(_baseMainColor.r, _baseMainColor.g, _baseMainColor.b, _baseMainColor.a * alpha);
        }
        if (_shadowText != null)
        {
            _shadowText.color = new Color(_baseShadowColor.r, _baseShadowColor.g, _baseShadowColor.b, _baseShadowColor.a * alpha);
        }

        if (_elapsed >= TotalLifetime)
        {
            Destroy(gameObject);
        }
    }
}
