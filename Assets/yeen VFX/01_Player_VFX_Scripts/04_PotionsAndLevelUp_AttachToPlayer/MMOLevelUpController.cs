using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// ระบบควบคุมเอฟเฟกต์ "เลเวลอัพ (Level Up Golden Pillar & Aura VFX)" ระดับ AAA สำหรับ Unity 6 (URP)
/// - ทำงานอัตโนมัติทันทีที่ตัวละครเลเวลอัพผ่าน PlayerCombat.ApplyExperienceAuthority -> RPC_PlayLevelUpVFX
/// - หากยังไม่ได้ลากใส่ levelUpRootVFX จะสร้างชุดเอฟเฟกต์เสาแสงทอง + วงแหวนพื้น + ละอองดาวเกลียว 4 เลเยอร์ให้อัตโนมัติตอน Runtime
/// - ตรวจจับโหนด CharacterVisual อัตโนมัติเพื่อวางวงแหวนให้แนบพื้นใต้ฝ่าเท้าพอดี (ไม่ลอยกลางเอว)
/// - รองรับปุ่มทดสอบ L ทั้ง New Input System และ Legacy Input (ปิด allowLeftClick เป็นค่าเริ่มต้นเพื่อไม่ให้ชนการฟันดาบ)
/// </summary>
[AddComponentMenu("yeen VFX/07. [Player] เลเวลอัพเสาแสงทอง (Level Up Golden Pillar)")]
[DisallowMultipleComponent]
public class MMOLevelUpController : MonoBehaviour
{
    public static MMOLevelUpController Instance { get; private set; }

    [Header("=== ปุ่มทดสอบ Level Up ===")]
    public bool enableTestHotkey = true;
    public KeyCode triggerKey = KeyCode.L;
    public bool allowLeftClick = false;

    [Header("=== อ้างอิงระบบ Particle หลัก ===")]
    public ParticleSystem levelUpRootVFX;
    public Transform characterMesh;

    [Tooltip("ระยะเวลาปล่อยเอฟเฟกต์เลเวลอัพก่อนค่อยๆ จางหาย (วินาที)")]
    [Range(1.2f, 5.0f)]
    public float effectDuration = 2.6f;

    private Transform _vfxMount;
    private ParticleSystem _l1GroundRing;
    private ParticleSystem _l2GoldenPillar;
    private ParticleSystem _l3RisingSparkles;
    private ParticleSystem _l4CrownFlash;
    private Light _levelUpLight;

    private Coroutine _activeRoutine;
    private float _popTimer = 0f;
    private bool _isPopping = false;
    private Vector3 _baseScale = Vector3.one;

    // แคชทรัพยากร Procedural ระดับ Runtime
    private static Mesh s_PillarConeMesh;
    private static Texture2D s_RingTex;
    private static Texture2D s_RaysTex;
    private static Texture2D s_StarTex;
    private static Material s_MatRing;
    private static Material s_MatPillar;
    private static Material s_MatSparkles;

    private void Awake()
    {
        if (Instance == null) Instance = this;

        if (characterMesh == null)
        {
            Transform visual = transform.Find("CharacterVisual");
            if (visual != null) characterMesh = visual;
        }
        if (characterMesh != null) _baseScale = characterMesh.localScale;

        EnsureLevelUpVFXBuilt();
        StopLevelUpVFX(true);
    }

    private void OnEnable()
    {
        if (Instance == null) Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (enableTestHotkey && ReadUniversalTrigger())
        {
            TriggerLevelUp();
        }

        if (_isPopping && characterMesh != null)
        {
            _popTimer += Time.deltaTime * 6f;
            float scaleBoost = 1f + Mathf.Sin(Mathf.Clamp01(_popTimer) * Mathf.PI) * 0.10f;
            characterMesh.localScale = new Vector3(
                _baseScale.x * scaleBoost,
                _baseScale.y * scaleBoost,
                _baseScale.z * scaleBoost);

            if (_popTimer >= 1f)
            {
                characterMesh.localScale = _baseScale;
                _isPopping = false;
            }
        }
    }

    /// <summary>
    /// ฟังก์ชัน Static Public สำหรับเรียกเล่นเอฟเฟกต์เลเวลอัพจากสคริปต์ระบบ EXP (PlayerCombat) ได้ทันที
    /// </summary>
    public static void PlayLevelUpVFX(Transform targetCharacter = null)
    {
        MMOLevelUpController controller = null;

        if (targetCharacter != null)
        {
            controller = targetCharacter.GetComponent<MMOLevelUpController>();
            if (controller == null)
            {
                controller = targetCharacter.gameObject.AddComponent<MMOLevelUpController>();
                controller.enableTestHotkey = false;
                controller.allowLeftClick = false;
            }
        }
        else
        {
            controller = Instance != null ? Instance : FindFirstObjectByType<MMOLevelUpController>();
            if (controller == null && PlayerCombat.Local != null)
            {
                controller = PlayerCombat.Local.GetComponent<MMOLevelUpController>();
                if (controller == null)
                {
                    controller = PlayerCombat.Local.gameObject.AddComponent<MMOLevelUpController>();
                }
            }
        }

        if (controller != null)
        {
            controller.TriggerLevelUp();
        }
    }

    /// <summary>
    /// สั่งเล่นเอฟเฟกต์เลเวลอัพบนตัวละครนี้ทันที
    /// </summary>
    [ContextMenu("ทดสอบเอฟเฟกต์เลเวลอัพ (Trigger Level Up)")]
    public void TriggerLevelUp()
    {
        EnsureLevelUpVFXBuilt();
        if (!gameObject.activeInHierarchy) return;

        if (_activeRoutine != null)
        {
            StopCoroutine(_activeRoutine);
        }

        _activeRoutine = StartCoroutine(LevelUpSequenceRoutine());
    }

    private IEnumerator LevelUpSequenceRoutine()
    {
        _isPopping = true;
        _popTimer = 0f;

        if (levelUpRootVFX != null)
        {
            levelUpRootVFX.gameObject.SetActive(true);
            levelUpRootVFX.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            levelUpRootVFX.Play(true);
        }

        if (_vfxMount != null) _vfxMount.gameObject.SetActive(true);
        PlayLayer(_l1GroundRing);
        PlayLayer(_l2GoldenPillar);
        PlayLayer(_l3RisingSparkles);
        PlayLayer(_l4CrownFlash);

        float elapsed = 0f;
        float duration = Mathf.Max(1.0f, effectDuration);
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            if (_levelUpLight != null)
            {
                _levelUpLight.enabled = true;
                float t = Mathf.Clamp01(elapsed / duration);
                float fadeIn = Mathf.Clamp01(t / 0.12f);
                float fadeOut = t < 0.60f ? 1f : 1f - Mathf.Clamp01((t - 0.60f) / 0.40f);
                _levelUpLight.intensity = 5.5f * fadeIn * fadeOut;
            }
            yield return null;
        }

        StopLevelUpVFX(false);
        if (_levelUpLight != null)
        {
            _levelUpLight.intensity = 0f;
            _levelUpLight.enabled = false;
        }

        _activeRoutine = null;
    }

    private void StopLevelUpVFX(bool clearImmediately)
    {
        ParticleSystemStopBehavior mode = clearImmediately
            ? ParticleSystemStopBehavior.StopEmittingAndClear
            : ParticleSystemStopBehavior.StopEmitting;

        if (levelUpRootVFX != null)
        {
            levelUpRootVFX.Stop(true, mode);
        }

        StopLayer(_l1GroundRing, mode);
        StopLayer(_l2GoldenPillar, mode);
        StopLayer(_l3RisingSparkles, mode);
        StopLayer(_l4CrownFlash, mode);

        if (_levelUpLight != null && clearImmediately)
        {
            _levelUpLight.intensity = 0f;
            _levelUpLight.enabled = false;
        }
    }

    private static void PlayLayer(ParticleSystem ps)
    {
        if (ps == null) return;
        ps.gameObject.SetActive(true);
        var em = ps.emission;
        em.enabled = true;
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.Play(true);
    }

    private static void StopLayer(ParticleSystem ps, ParticleSystemStopBehavior mode)
    {
        if (ps == null) return;
        var em = ps.emission;
        em.enabled = false;
        ps.Stop(true, mode);
    }

    private bool ReadUniversalTrigger()
    {
        bool pressedNew = false;
        if (Keyboard.current != null && triggerKey == KeyCode.L && Keyboard.current.lKey.wasPressedThisFrame)
        {
            pressedNew = true;
        }
        if (allowLeftClick && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            pressedNew = true;
        }

        bool pressedLegacy = false;
        try
        {
            if (Input.GetKeyDown(triggerKey) || (allowLeftClick && Input.GetMouseButtonDown(0)))
            {
                pressedLegacy = true;
            }
        }
        catch
        {
            // ป้องกัน Error ในโปรเจกต์ที่ใช้ New Input System อย่างเดียว
        }

        return pressedNew || pressedLegacy;
    }

    // ================================================================================================
    // RUNTIME SELF-HEALING 4-LAYER GOLDEN LEVEL-UP VFX BUILDER
    // ================================================================================================
    private void EnsureLevelUpVFXBuilt()
    {
        if (levelUpRootVFX != null) return;
        if (_l1GroundRing != null && _l2GoldenPillar != null && _l3RisingSparkles != null && _l4CrownFlash != null) return;

        EnsureSharedAssets();

        if (_vfxMount == null)
        {
            Transform existing = transform.Find("VFX_LevelUp_Mount");
            if (existing != null)
            {
                _vfxMount = existing;
            }
            else
            {
                GameObject mount = new GameObject("VFX_LevelUp_Mount");
                mount.transform.SetParent(transform, false);
                mount.transform.localPosition = ResolveFeetLocalOffset();
                mount.transform.localRotation = Quaternion.identity;
                _vfxMount = mount.transform;
            }
        }

        if (_l1GroundRing == null) _l1GroundRing = CreateLayer1GroundRing(_vfxMount, s_MatRing);
        if (_l2GoldenPillar == null) _l2GoldenPillar = CreateLayer2GoldenPillar(_vfxMount, s_PillarConeMesh, s_MatPillar);
        if (_l3RisingSparkles == null) _l3RisingSparkles = CreateLayer3Sparkles(_vfxMount, s_MatSparkles);
        if (_l4CrownFlash == null) _l4CrownFlash = CreateLayer4CrownFlash(_vfxMount, s_MatSparkles);

        if (_levelUpLight == null)
        {
            GameObject lightObj = new GameObject("LevelUp_GoldenLight");
            lightObj.transform.SetParent(_vfxMount, false);
            lightObj.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            _levelUpLight = lightObj.AddComponent<Light>();
            _levelUpLight.type = LightType.Point;
            _levelUpLight.color = new Color(1.0f, 0.84f, 0.32f, 1f);
            _levelUpLight.range = 8.0f;
            _levelUpLight.intensity = 0f;
            _levelUpLight.enabled = false;
        }
    }

    private Vector3 ResolveFeetLocalOffset()
    {
        Transform visual = transform.Find("CharacterVisual");
        if (visual != null)
        {
            return new Vector3(0f, visual.localPosition.y, 0f);
        }
        return Vector3.zero;
    }

    private static void EnsureSharedAssets()
    {
        if (s_PillarConeMesh == null) s_PillarConeMesh = BuildPillarCylinderMesh();
        if (s_RingTex == null) s_RingTex = BuildRingTex(256, 256);
        if (s_RaysTex == null) s_RaysTex = BuildRaysTex(256, 256);
        if (s_StarTex == null) s_StarTex = BuildStarTex(128, 128);

        if (s_MatRing == null) s_MatRing = CreateAdditiveMat(s_RingTex, new Color(1.8f, 1.4f, 0.45f, 1f));
        if (s_MatPillar == null) s_MatPillar = CreateAdditiveMat(s_RaysTex, new Color(1.9f, 1.45f, 0.48f, 1f));
        if (s_MatSparkles == null) s_MatSparkles = CreateAdditiveMat(s_StarTex, new Color(2.0f, 1.65f, 0.72f, 1f));
    }

    private static Material CreateAdditiveMat(Texture2D tex, Color baseColor)
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
        mat.renderQueue = (int)RenderQueue.Transparent + 20;
        return mat;
    }

    private static ParticleSystem CreateLayer1GroundRing(Transform parent, Material mat)
    {
        GameObject obj = new GameObject("L1_LevelUp_GoldenRing");
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = new Vector3(0f, 0.06f, 0f);

        ParticleSystem ps = obj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 1.4f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = 1.35f;
        main.startSpeed = 0f;
        main.startSize = 3.2f;
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(3.6f, 2.75f, 0.85f, 1f));
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 4;

        var em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0.0f, 2) });
        var sh = ps.shape;
        sh.enabled = false;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.20f, 3.5f, 3.5f),
            new Keyframe(0.45f, 0.95f, 0.4f, 0.4f),
            new Keyframe(1f, 1.20f, 0f, 0f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(new Color(1f, 0.96f, 0.8f), 0f), new GradientColorKey(new Color(1f, 0.76f, 0.22f), 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.75f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);

        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        r.sharedMaterial = mat;
        return ps;
    }

    private static ParticleSystem CreateLayer2GoldenPillar(Transform parent, Mesh mesh, Material mat)
    {
        GameObject obj = new GameObject("L2_LevelUp_GoldenPillar");
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = Vector3.zero;

        ParticleSystem ps = obj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 2.0f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = 1.9f;
        main.startSpeed = 0f;
        main.startSize3D = true;
        main.startSizeX = 1.35f;
        main.startSizeY = 4.6f;
        main.startSizeZ = 1.35f;
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(3.2f, 2.35f, 0.72f, 1f));
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 2;

        var em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0.0f, 1) });
        var sh = ps.shape;
        sh.enabled = false;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.separateAxes = true;
        AnimationCurve xz = new AnimationCurve(new Keyframe(0f, 0.65f), new Keyframe(0.20f, 1.05f), new Keyframe(1f, 1.25f));
        AnimationCurve y = new AnimationCurve(new Keyframe(0f, 0.12f, 7f, 7f), new Keyframe(0.16f, 1.0f), new Keyframe(1f, 1.12f));
        sol.x = new ParticleSystem.MinMaxCurve(1f, xz);
        sol.y = new ParticleSystem.MinMaxCurve(1f, y);
        sol.z = new ParticleSystem.MinMaxCurve(1f, xz);

        var rol = ps.rotationOverLifetime;
        rol.enabled = true;
        rol.separateAxes = true;
        rol.x = new ParticleSystem.MinMaxCurve(0f);
        rol.y = new ParticleSystem.MinMaxCurve(50f * Mathf.Deg2Rad);
        rol.z = new ParticleSystem.MinMaxCurve(0f);

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(new Color(1f, 0.96f, 0.82f), 0f), new GradientColorKey(new Color(1f, 0.78f, 0.25f), 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.95f, 0.12f), new GradientAlphaKey(0.80f, 0.62f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);

        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Mesh;
        r.mesh = mesh;
        r.alignment = ParticleSystemRenderSpace.Local;
        r.sharedMaterial = mat;
        return ps;
    }

    private static ParticleSystem CreateLayer3Sparkles(Transform parent, Material mat)
    {
        GameObject obj = new GameObject("L3_LevelUp_GoldenSparkles");
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = new Vector3(0f, 0.15f, 0f);

        ParticleSystem ps = obj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 2.4f;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.55f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.34f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(3.6f, 2.95f, 1.15f, 1f),
            new Color(4.5f, 3.9f, 2.0f, 1f));
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 120;

        var em = ps.emission;
        em.rateOverTime = 28f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0.0f, 24) });

        var sh = ps.shape;
        sh.enabled = true;
        sh.shapeType = ParticleSystemShapeType.Circle;
        sh.radius = 0.85f;
        sh.radiusThickness = 0.45f;
        sh.rotation = new Vector3(-90f, 0f, 0f);

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = new ParticleSystem.MinMaxCurve(0f);
        vel.y = new ParticleSystem.MinMaxCurve(1.45f);
        vel.z = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalX = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalY = new ParticleSystem.MinMaxCurve(3.4f);
        vel.orbitalZ = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalOffsetX = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalOffsetY = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalOffsetZ = new ParticleSystem.MinMaxCurve(0f);
        vel.radial = new ParticleSystem.MinMaxCurve(0f);
        vel.speedModifier = new ParticleSystem.MinMaxCurve(1f);

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.18f, 1f), new Keyframe(0.7f, 0.85f), new Keyframe(1f, 0f)));

        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.sharedMaterial = mat;
        return ps;
    }

    private static ParticleSystem CreateLayer4CrownFlash(Transform parent, Material mat)
    {
        GameObject obj = new GameObject("L4_LevelUp_StarFlash");
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = new Vector3(0f, 1.35f, 0f);

        ParticleSystem ps = obj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 0.8f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = 0.75f;
        main.startSpeed = 0f;
        main.startSize = 2.8f;
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(4.6f, 3.8f, 1.6f, 1f));
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
            new Keyframe(0f, 0.08f, 10f, 10f), new Keyframe(0.16f, 1.15f), new Keyframe(1f, 0f)));

        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.sharedMaterial = mat;
        return ps;
    }

    private static Mesh BuildPillarCylinderMesh()
    {
        const int radSegs = 36;
        const int ySegs = 12;
        int ring = radSegs + 1;
        Vector3[] verts = new Vector3[ring * (ySegs + 1)];
        Vector2[] uvs = new Vector2[verts.Length];
        Color[] cols = new Color[verts.Length];
        int vi = 0;

        for (int yi = 0; yi <= ySegs; yi++)
        {
            float v = (float)yi / ySegs;
            float r = Mathf.Lerp(0.62f, 0.88f, v);
            float alpha = SmoothStep(0f, 0.14f, v) * Mathf.Pow(1f - SmoothStep(0.32f, 1f, v), 1.4f);
            Color c = new Color(1f, 0.92f, 0.65f, alpha);
            for (int ri = 0; ri <= radSegs; ri++)
            {
                float u = (float)ri / radSegs;
                float a = u * Mathf.PI * 2f;
                verts[vi] = new Vector3(Mathf.Cos(a) * r, v, Mathf.Sin(a) * r);
                uvs[vi] = new Vector2(u * 2f, v);
                cols[vi] = c;
                vi++;
            }
        }

        int[] tris = new int[radSegs * ySegs * 6];
        int ti = 0;
        for (int yi = 0; yi < ySegs; yi++)
        {
            for (int ri = 0; ri < radSegs; ri++)
            {
                int cur = yi * ring + ri;
                int nxt = cur + 1;
                int up = (yi + 1) * ring + ri;
                int upNxt = up + 1;
                tris[ti++] = cur; tris[ti++] = up; tris[ti++] = nxt;
                tris[ti++] = nxt; tris[ti++] = up; tris[ti++] = upNxt;
            }
        }

        Mesh m = new Mesh { name = "Mesh_LevelUpPillar_Runtime" };
        m.vertices = verts; m.uv = uvs; m.colors = cols; m.triangles = tris;
        m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    private static Texture2D BuildRingTex(int w, int h)
    {
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        Color[] px = new Color[w * h];
        for (int y = 0; y < h; y++)
        {
            float ny = ((float)y / (h - 1)) * 2f - 1f;
            for (int x = 0; x < w; x++)
            {
                float nx = ((float)x / (w - 1)) * 2f - 1f;
                float d = Mathf.Sqrt(nx * nx + ny * ny);
                float ring = Mathf.Exp(-Mathf.Pow((d - 0.74f) / 0.065f, 2f)) + Mathf.Exp(-Mathf.Pow((d - 0.52f) / 0.08f, 2f)) * 0.45f;
                float val = Mathf.Clamp01(ring * (1f - SmoothStep(0.88f, 0.99f, d)));
                px[y * w + x] = new Color(1f, 0.90f, 0.52f, val);
            }
        }
        tex.SetPixels(px); tex.Apply();
        return tex;
    }

    private static Texture2D BuildRaysTex(int w, int h)
    {
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp };
        Color[] px = new Color[w * h];
        for (int y = 0; y < h; y++)
        {
            float v = (float)y / (h - 1);
            float vMask = SmoothStep(0f, 0.12f, v) * Mathf.Pow(1f - SmoothStep(0.2f, 1f, v), 1.4f);
            for (int x = 0; x < w; x++)
            {
                float a = ((float)x / w) * Mathf.PI * 2f;
                float rays = 0.30f + Mathf.Pow(0.5f + 0.5f * Mathf.Sin(a * 8f), 2.5f) * 0.65f + Mathf.Pow(0.5f + 0.5f * Mathf.Cos(a * 16f), 4f) * 0.35f;
                float val = Mathf.Clamp01(rays * vMask);
                px[y * w + x] = new Color(1f, 0.92f, 0.62f, val);
            }
        }
        tex.SetPixels(px); tex.Apply();
        return tex;
    }

    private static Texture2D BuildStarTex(int w, int h)
    {
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        Color[] px = new Color[w * h];
        for (int y = 0; y < h; y++)
        {
            float ny = ((float)y / (h - 1)) * 2f - 1f;
            for (int x = 0; x < w; x++)
            {
                float nx = ((float)x / (w - 1)) * 2f - 1f;
                float d = Mathf.Sqrt(nx * nx + ny * ny);
                float core = Mathf.Exp(-d * d * 15f);
                float spikes = Mathf.Exp(-Mathf.Abs(ny) * 20f) * Mathf.Exp(-Mathf.Abs(nx) * 2.5f)
                             + Mathf.Exp(-Mathf.Abs(nx) * 20f) * Mathf.Exp(-Mathf.Abs(ny) * 2.5f);
                float val = Mathf.Clamp01((core + spikes * 0.9f) * (1f - SmoothStep(0.75f, 0.98f, d)));
                px[y * w + x] = new Color(1f, 0.96f, 0.78f, val);
            }
        }
        tex.SetPixels(px); tex.Apply();
        return tex;
    }

    private static float SmoothStep(float e0, float e1, float x)
    {
        float t = Mathf.Clamp01((x - e0) / Mathf.Max(0.00001f, e1 - e0));
        return t * t * (3f - 2f * t);
    }
}
