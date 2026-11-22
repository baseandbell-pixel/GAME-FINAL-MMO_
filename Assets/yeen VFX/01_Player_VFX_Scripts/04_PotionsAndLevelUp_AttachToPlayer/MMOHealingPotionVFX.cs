using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// ระบบควบคุมเอฟเฟกต์ "ใช้ยารักษาแล้วมีละอองแสงหิ้งห้อยสีเขียวมรกตลอยวนรอบตัว 4 เลเยอร์ระดับ AAA"
/// (Healing Potion Emerald Firefly Orbs & Nature Aura VFX) สำหรับ Unity 6 (URP)
/// - เรียกใช้จากระบบกระเป๋าไอเทมได้ทันทีผ่าน MMOHealingPotionVFX.UseHealPotion()
/// - รองรับปุ่มกดทดสอบ H หรือหมายเลข 1 ทั้ง New Input System และ Legacy Input พร้อมกันโดยไม่เกิด Error
/// - ใช้ Coroutine คุมจังหวะการรักษา 2.8 วินาที (กดซ้ำเพื่อรีเซ็ตจังหวะได้ทันที) และจางหายอย่างอ่อนโยน
/// - มีหน้าต่าง OnGUI() แสดงสถานะ (กำลังฟื้นฟู HP! / พร้อมใช้งาน) พร้อมสวิตช์เปิด-ปิด showDebugHUD
/// </summary>
[AddComponentMenu("yeen VFX/05. [Player] ยารักษาหิ้งห้อยเขียว (Healing Potion Fireflies)")]
[DisallowMultipleComponent]
public class MMOHealingPotionVFX : MonoBehaviour
{
    public static MMOHealingPotionVFX Instance { get; private set; }

    [Header("=== 1. โหนดและเอฟเฟกต์ยารักษา 4 เลเยอร์ (4-Layer Healing Firefly VFX) ===")]
    [Tooltip("โหนดจุดยึดเอฟเฟกต์ยารักษาบนตัวละคร (VFX_HealingFirefly_Mount)")]
    public Transform vfxMountRoot;

    [Tooltip("Layer 1 (0.00s - 0.85s): วงแหวนแสงสีเขียวมรกต Horizontal Billboard ขยายใต้ฝ่าเท้า")]
    public ParticleSystem l1GroundEmeraldPulseRing;

    [Tooltip("Layer 2 (0.05s - 2.60s): ฝูงละอองแสงหิ้งห้อยสีเขียวมรกตบินวนส่ายโอบล้อมตัวละครจากเท้าขึ้นศีรษะ")]
    public ParticleSystem l2OrbitingEmeraldFireflies;

    [Tooltip("Layer 3 (0.15s - 2.40s): สัญลักษณ์เครื่องหมายบวกรักษา (+) สีเขียวมินต์ลอยขึ้นช้าๆ รอบลำตัว")]
    public ParticleSystem l3RisingHealingPlusIcons;

    [Tooltip("Layer 4 (0.10s - 2.20s): ม่านละอองแสงสีเขียวอ่อนโปร่งแสงโอบล้อมกลางลำตัว")]
    public ParticleSystem l4SoftGreenBodyAura;

    [Tooltip("ไฟ Point Light สีเขียวมรกตอ่อนโยนเสริมบรรยากาศรอบตัว (ไม่บังคับ)")]
    public Light healEmeraldPointLight;

    [Header("=== 2. ตั้งค่าจังหวะการฟื้นฟูและปุ่มกดทดสอบ (Timing & Input) ===")]
    [Tooltip("ระยะเวลาปล่อยเอฟเฟกต์รักษาต่อเนื่อง (วินาที - ค่ามาตรฐาน AAA = 2.8 วินาที)")]
    [Range(1.0f, 6.0f)]
    public float healEffectDuration = 2.8f;

    [Tooltip("เปิดให้กดปุ่ม H หรือหมายเลข 1 บนคีย์บอร์ดเพื่อทดสอบดื่มยารักษาได้ทันที")]
    public bool enableTestHotkeys = true;

    [Tooltip("ความสว่างสูงสุดของไฟสีเขียวมรกตเสริมรอบตัว")]
    public float maxHealLightIntensity = 3.2f;

    [Header("=== 3. หน้าต่างสถานะขณะทดสอบ (On-Screen Debug HUD) ===")]
    [Tooltip("แสดงกรอบสถานะมุมซ้ายล่างของหน้าจอ (กำลังฟื้นฟู HP! / พร้อมใช้งาน)")]
    public bool showDebugHUD = true;

    [Header("=== 4. สถานะปัจจุบัน (Read-Only) ===")]
    [SerializeField] private bool isHealingActive = false;
    [SerializeField] private float remainingHealTime = 0f;

    private Coroutine _activeHealCoroutine;
    private float _lastTriggerUnscaledTime = -10f;

    // แคชทรัพยากร Procedural ระดับ Runtime เพื่อรับประกันว่า UseHealPotion() ทำงานได้ 100% แม้ยังไม่ได้กดเมนูสร้าง
    private static Texture2D s_RuntimeFireflyOrbTex;
    private static Texture2D s_RuntimePlusIconTex;
    private static Texture2D s_RuntimeGroundRingTex;
    private static Material s_RuntimeMatFirefly;
    private static Material s_RuntimeMatPlusIcon;
    private static Material s_RuntimeMatGroundRing;
    private static Material s_RuntimeMatBodyAura;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }

        AutoBindReferences();
        EnsureVFXRigBuilt();
        StopAllHealingLayers(true);
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

    public void AutoBindReferences()
    {
        if (vfxMountRoot == null)
        {
            vfxMountRoot = FindDeepChild(transform, "VFX_HealingFirefly_Mount");
        }

        Transform searchRoot = vfxMountRoot != null ? vfxMountRoot : transform;

        if (l1GroundEmeraldPulseRing == null)
        {
            Transform t = FindDeepChild(searchRoot, "L1_Ground_EmeraldPulseRing");
            if (t != null) l1GroundEmeraldPulseRing = t.GetComponent<ParticleSystem>();
        }

        if (l2OrbitingEmeraldFireflies == null)
        {
            Transform t = FindDeepChild(searchRoot, "L2_Orbiting_EmeraldFireflies");
            if (t != null) l2OrbitingEmeraldFireflies = t.GetComponent<ParticleSystem>();
        }

        if (l3RisingHealingPlusIcons == null)
        {
            Transform t = FindDeepChild(searchRoot, "L3_Rising_HealingPlusIcons");
            if (t != null) l3RisingHealingPlusIcons = t.GetComponent<ParticleSystem>();
        }

        if (l4SoftGreenBodyAura == null)
        {
            Transform t = FindDeepChild(searchRoot, "L4_SoftGreen_BodyAura");
            if (t != null) l4SoftGreenBodyAura = t.GetComponent<ParticleSystem>();
        }

        if (healEmeraldPointLight == null)
        {
            Transform t = FindDeepChild(searchRoot, "Heal_EmeraldPointLight");
            if (t != null) healEmeraldPointLight = t.GetComponent<Light>();
        }
    }

    private void Update()
    {
        if (!enableTestHotkeys) return;

        if (ReadUniversalHealTrigger())
        {
            TriggerHealEffect();
        }
    }

    /// <summary>
    /// กฎเหล็กข้อที่ 2: ฟังก์ชัน Static Public สำหรับให้สคริปต์ระบบกระเป๋าไอเทม (Inventory)
    /// หรือสคริปต์ตัวละครเรียกใช้งานได้ทันทีเมื่อผู้เล่นดื่มยารักษา
    /// ตัวอย่าง: MMOHealingPotionVFX.UseHealPotion();
    /// </summary>
    public static void UseHealPotion()
    {
        MMOHealingPotionVFX controller = null;

        if (PlayerCombat.Local != null)
        {
            controller = PlayerCombat.Local.GetComponent<MMOHealingPotionVFX>();
            if (controller == null)
            {
                controller = PlayerCombat.Local.gameObject.AddComponent<MMOHealingPotionVFX>();
                controller.showDebugHUD = false;
            }
            Instance = controller;
        }

        if (controller == null)
        {
            controller = Instance != null ? Instance : FindFirstObjectByType<MMOHealingPotionVFX>();
            if (controller != null)
            {
                Instance = controller;
            }
        }

        if (controller != null)
        {
            controller.TriggerHealEffect();
        }
    }

    /// <summary>
    /// สั่งเล่นเอฟเฟกต์ยารักษาหิ้งห้อยสีเขียวครบทั้ง 4 เลเยอร์เป็นเวลา 2.8 วินาที (กดซ้ำเพื่อรีเซ็ตจังหวะได้ทันที)
    /// </summary>
    [ContextMenu("ทดสอบใช้ยารักษา (Trigger Healing Firefly VFX)")]
    public void TriggerHealEffect()
    {
        if (Time.unscaledTime - _lastTriggerUnscaledTime < 0.05f) return;
        _lastTriggerUnscaledTime = Time.unscaledTime;

        EnsureVFXRigBuilt();

        if (!gameObject.activeInHierarchy) return;

        if (_activeHealCoroutine != null)
        {
            StopCoroutine(_activeHealCoroutine);
        }

        _activeHealCoroutine = StartCoroutine(HealingSequenceRoutine());
    }

    private IEnumerator HealingSequenceRoutine()
    {
        isHealingActive = true;
        remainingHealTime = healEffectDuration;

        if (vfxMountRoot != null && !vfxMountRoot.gameObject.activeSelf)
        {
            vfxMountRoot.gameObject.SetActive(true);
        }

        // เริ่มต้นทั้ง 4 เลเยอร์พร้อมกัน (รีเซ็ตจังหวะทันทีหากผู้เล่นกดซ้ำ)
        PlayLayerFresh(l1GroundEmeraldPulseRing);
        PlayLayerFresh(l2OrbitingEmeraldFireflies);
        PlayLayerFresh(l3RisingHealingPlusIcons);
        PlayLayerFresh(l4SoftGreenBodyAura);

        float elapsed = 0f;
        float duration = Mathf.Max(0.5f, healEffectDuration);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            remainingHealTime = Mathf.Max(0f, duration - elapsed);

            // คุมความสว่าง Point Light สีเขียวมรกตให้สว่างนุ่มนวลแล้วค่อยๆ หรี่ลงช่วงท้าย
            if (healEmeraldPointLight != null)
            {
                healEmeraldPointLight.enabled = true;
                float t = Mathf.Clamp01(elapsed / duration);
                float fadeIn = Mathf.Clamp01(t / 0.14f);
                float fadeOut = t < 0.65f ? 1f : 1f - Mathf.Clamp01((t - 0.65f) / 0.35f);
                float gentlePulse = 0.90f + 0.10f * Mathf.Sin(elapsed * 5.2f);
                healEmeraldPointLight.intensity = maxHealLightIntensity * fadeIn * fadeOut * gentlePulse;
            }

            yield return null;
        }

        // หยุดปล่อยอนุภาคใหม่แบบ StopEmitting เพื่อให้หิ้งห้อยและสัญลักษณ์ + ที่ลอยอยู่ค่อยๆ จางหายไปในอากาศอย่างอ่อนโยน
        StopAllHealingLayers(false);

        if (healEmeraldPointLight != null)
        {
            healEmeraldPointLight.intensity = 0f;
            healEmeraldPointLight.enabled = false;
        }

        remainingHealTime = 0f;
        isHealingActive = false;
        _activeHealCoroutine = null;
    }

    private static void PlayLayerFresh(ParticleSystem ps)
    {
        if (ps == null) return;
        ps.gameObject.SetActive(true);
        ParticleSystem.EmissionModule em = ps.emission;
        em.enabled = true;
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.Play(true);
    }

    private void StopAllHealingLayers(bool clearImmediately)
    {
        ParticleSystemStopBehavior stopMode = clearImmediately
            ? ParticleSystemStopBehavior.StopEmittingAndClear
            : ParticleSystemStopBehavior.StopEmitting;

        StopSingleLayer(l1GroundEmeraldPulseRing, stopMode);
        StopSingleLayer(l2OrbitingEmeraldFireflies, stopMode);
        StopSingleLayer(l3RisingHealingPlusIcons, stopMode);
        StopSingleLayer(l4SoftGreenBodyAura, stopMode);

        if (healEmeraldPointLight != null && clearImmediately)
        {
            healEmeraldPointLight.intensity = 0f;
            healEmeraldPointLight.enabled = false;
        }
    }

    private static void StopSingleLayer(ParticleSystem ps, ParticleSystemStopBehavior stopMode)
    {
        if (ps == null) return;
        ParticleSystem.EmissionModule em = ps.emission;
        em.enabled = false;
        ps.Stop(true, stopMode);
    }

    // ================================================================================================
    // กฎเหล็กข้อที่ 2: รองรับปุ่มทดสอบ H หรือหมายเลข 1 ทั้ง New Input System และ Legacy Input
    // ================================================================================================
    private static bool ReadUniversalHealTrigger()
    {
        bool pressedNew = false;
        if (Keyboard.current != null)
        {
            if (Keyboard.current.hKey.wasPressedThisFrame ||
                Keyboard.current.digit1Key.wasPressedThisFrame ||
                Keyboard.current.numpad1Key.wasPressedThisFrame)
            {
                pressedNew = true;
            }
        }

        bool pressedLegacy = false;
        try
        {
            if (Input.GetKeyDown(KeyCode.H) || Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
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

    // ================================================================================================
    // กฎเหล็กข้อที่ 2: หน้าต่าง OnGUI() แสดงสถานะการฟื้นฟู (กำลังฟื้นฟู HP! / พร้อมใช้งาน)
    // ================================================================================================
    private void OnGUI()
    {
        if (!showDebugHUD || !Application.isPlaying) return;

        GUI.color = new Color(0.02f, 0.11f, 0.06f, 0.85f);
        GUI.Box(new Rect(14f, Screen.height - 102f, 445f, 88f), GUIContent.none);
        GUI.color = Color.white;

        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = 14;
        style.richText = true;
        style.normal.textColor = Color.white;

        string statusBadge = isHealingActive
            ? "<color=#55FF99><b>กำลังฟื้นฟู HP! (" + remainingHealTime.ToString("F1") + "s)</b></color>"
            : "<color=#B8E6CC><b>พร้อมใช้งาน (READY)</b></color>";

        string hudText =
            "<color=#44FF99><b>[AAA Healing Potion Emerald Firefly VFX]</b></color>\n" +
            "• <b>สถานะยารักษา:</b> " + statusBadge + "\n" +
            "• <b>วิธีทดสอบ:</b> กดปุ่ม <b>[H]</b> หรือหมายเลข <b>[1]</b> เพื่อปล่อยฝูงหิ้งห้อยสีเขียวล้อมตัว";

        GUI.Label(new Rect(24f, Screen.height - 96f, 425f, 76f), hudText, style);
    }

    // ================================================================================================
    // RUNTIME SELF-HEALING VFX BUILDER (สร้างโหนดทั้ง 4 เลเยอร์อัตโนมัติหากยังไม่มีบนตัวละคร)
    // ================================================================================================
    public void EnsureVFXRigBuilt()
    {
        AutoBindReferences();

        bool missingAny =
            l1GroundEmeraldPulseRing == null ||
            l2OrbitingEmeraldFireflies == null ||
            l3RisingHealingPlusIcons == null ||
            l4SoftGreenBodyAura == null;

        if (!missingAny) return;

        EnsureRuntimeSharedAssets();

        if (vfxMountRoot == null)
        {
            GameObject mountObj = new GameObject("VFX_HealingFirefly_Mount");
            mountObj.transform.SetParent(transform, false);
            Transform visual = transform.Find("CharacterVisual");
            mountObj.transform.localPosition = visual != null ? new Vector3(0f, visual.localPosition.y, 0f) : Vector3.zero;
            mountObj.transform.localRotation = Quaternion.identity;
            vfxMountRoot = mountObj.transform;
        }

        if (l1GroundEmeraldPulseRing == null)
            l1GroundEmeraldPulseRing = CreateRuntimeLayer1GroundRing(vfxMountRoot, s_RuntimeMatGroundRing);
        if (l2OrbitingEmeraldFireflies == null)
            l2OrbitingEmeraldFireflies = CreateRuntimeLayer2Fireflies(vfxMountRoot, s_RuntimeMatFirefly);
        if (l3RisingHealingPlusIcons == null)
            l3RisingHealingPlusIcons = CreateRuntimeLayer3PlusIcons(vfxMountRoot, s_RuntimeMatPlusIcon);
        if (l4SoftGreenBodyAura == null)
            l4SoftGreenBodyAura = CreateRuntimeLayer4BodyAura(vfxMountRoot, s_RuntimeMatBodyAura);

        if (healEmeraldPointLight == null)
        {
            GameObject lightObj = new GameObject("Heal_EmeraldPointLight");
            lightObj.transform.SetParent(vfxMountRoot, false);
            lightObj.transform.localPosition = new Vector3(0f, 1.05f, 0f);
            healEmeraldPointLight = lightObj.AddComponent<Light>();
            healEmeraldPointLight.type = LightType.Point;
            healEmeraldPointLight.color = new Color(0.28f, 1.0f, 0.56f, 1f);
            healEmeraldPointLight.range = 6.0f;
            healEmeraldPointLight.intensity = 0f;
            healEmeraldPointLight.enabled = false;
        }
    }

    private static void EnsureRuntimeSharedAssets()
    {
        if (s_RuntimeFireflyOrbTex == null) s_RuntimeFireflyOrbTex = BuildRuntimeFireflyOrbTex(128, 128);
        if (s_RuntimePlusIconTex == null) s_RuntimePlusIconTex = BuildRuntimePlusIconTex(128, 128);
        if (s_RuntimeGroundRingTex == null) s_RuntimeGroundRingTex = BuildRuntimeGroundRingTex(256, 256);

        if (s_RuntimeMatGroundRing == null) s_RuntimeMatGroundRing = CreateRuntimeURPAdditiveMat(s_RuntimeGroundRingTex, new Color(0.45f, 1.55f, 0.78f, 1f));
        if (s_RuntimeMatFirefly == null) s_RuntimeMatFirefly = CreateRuntimeURPAdditiveMat(s_RuntimeFireflyOrbTex, new Color(0.55f, 1.85f, 0.92f, 1f));
        if (s_RuntimeMatPlusIcon == null) s_RuntimeMatPlusIcon = CreateRuntimeURPAdditiveMat(s_RuntimePlusIconTex, new Color(0.60f, 1.75f, 1.05f, 1f));
        if (s_RuntimeMatBodyAura == null) s_RuntimeMatBodyAura = CreateRuntimeURPAdditiveMat(s_RuntimeFireflyOrbTex, new Color(0.45f, 1.35f, 0.75f, 0.65f));
    }

    private static Material CreateRuntimeURPAdditiveMat(Texture2D tex, Color baseColor)
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
        mat.renderQueue = (int)RenderQueue.Transparent;
        return mat;
    }

    private static ParticleSystem CreateRuntimeLayer1GroundRing(Transform parent, Material mat)
    {
        GameObject obj = new GameObject("L1_Ground_EmeraldPulseRing");
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = new Vector3(0f, 0.05f, 0f);

        ParticleSystem ps = obj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 0.90f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = 0.85f;
        main.startSpeed = 0f;
        main.startSize = 2.4f;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.45f, 3.2f, 1.35f, 1f));
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
            new Keyframe(0f, 0.22f, 2.8f, 2.8f),
            new Keyframe(0.55f, 0.92f, 0.5f, 0.5f),
            new Keyframe(1f, 1.15f, 0f, 0f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.75f, 1.0f, 0.88f), 0f),
                new GradientColorKey(new Color(0.18f, 0.95f, 0.48f), 0.55f),
                new GradientColorKey(new Color(0.08f, 0.72f, 0.35f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.95f, 0.14f),
                new GradientAlphaKey(0.65f, 0.55f),
                new GradientAlphaKey(0f, 1f)
            });
        col.color = new ParticleSystem.MinMaxGradient(g);

        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        r.sharedMaterial = mat;
        return ps;
    }

    private static ParticleSystem CreateRuntimeLayer2Fireflies(Transform parent, Material mat)
    {
        GameObject obj = new GameObject("L2_Orbiting_EmeraldFireflies");
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = new Vector3(0f, 0.12f, 0f);
        obj.transform.localRotation = Quaternion.identity;

        ParticleSystem ps = obj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 2.60f;
        main.loop = true;
        main.playOnAwake = false;
        main.startDelay = 0.05f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.45f, 2.25f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.20f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.30f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.45f, 3.4f, 1.45f, 1f),
            new Color(1.25f, 3.8f, 2.10f, 1f));
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 180;

        var em = ps.emission;
        em.rateOverTime = 32f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0.0f, 18) });

        var sh = ps.shape;
        sh.enabled = true;
        sh.shapeType = ParticleSystemShapeType.Circle;
        sh.radius = 0.68f;
        sh.radiusThickness = 0.35f;
        sh.rotation = new Vector3(-90f, 0f, 0f);

        // ทุกแกนใน Velocity over Lifetime ต้องเป็น Constant Mode เสมอกัน (Unity 6 Safe)
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = new ParticleSystem.MinMaxCurve(0f);
        vel.y = new ParticleSystem.MinMaxCurve(0.75f);
        vel.z = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalX = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalY = new ParticleSystem.MinMaxCurve(2.6f);
        vel.orbitalZ = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalOffsetX = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalOffsetY = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalOffsetZ = new ParticleSystem.MinMaxCurve(0f);
        vel.radial = new ParticleSystem.MinMaxCurve(0f);
        vel.speedModifier = new ParticleSystem.MinMaxCurve(1f);

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.35f;
        noise.frequency = 1.4f;
        noise.scrollSpeed = 0.65f;
        noise.damping = true;
        noise.octaveCount = 2;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0.00f, 0.00f),
            new Keyframe(0.18f, 1.00f),
            new Keyframe(0.45f, 0.72f),
            new Keyframe(0.68f, 1.05f),
            new Keyframe(1.00f, 0.00f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.85f, 1.0f, 0.88f), 0f),
                new GradientColorKey(new Color(0.32f, 1.0f, 0.56f), 0.55f),
                new GradientColorKey(new Color(0.18f, 0.85f, 0.45f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.20f),
                new GradientAlphaKey(0.92f, 0.70f),
                new GradientAlphaKey(0f, 1f)
            });
        col.color = new ParticleSystem.MinMaxGradient(g);

        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.sharedMaterial = mat;
        return ps;
    }

    private static ParticleSystem CreateRuntimeLayer3PlusIcons(Transform parent, Material mat)
    {
        GameObject obj = new GameObject("L3_Rising_HealingPlusIcons");
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = new Vector3(0f, 0.85f, 0f);

        ParticleSystem ps = obj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 2.40f;
        main.loop = true;
        main.playOnAwake = false;
        main.startDelay = 0.15f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.15f, 1.75f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.45f, 0.85f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.32f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 3.4f, 2.0f, 1f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 40;

        var em = ps.emission;
        em.rateOverTime = 8f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0.0f, 3) });

        var sh = ps.shape;
        sh.enabled = true;
        sh.shapeType = ParticleSystemShapeType.Circle;
        sh.radius = 0.65f;
        sh.rotation = new Vector3(-90f, 0f, 0f);

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.15f),
            new Keyframe(0.18f, 1.05f),
            new Keyframe(0.78f, 0.90f),
            new Keyframe(1f, 0.0f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.82f, 1.0f, 0.92f), 0f),
                new GradientColorKey(new Color(0.35f, 0.98f, 0.68f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.95f, 0.18f),
                new GradientAlphaKey(0.80f, 0.72f),
                new GradientAlphaKey(0f, 1f)
            });
        col.color = new ParticleSystem.MinMaxGradient(g);

        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.sharedMaterial = mat;
        return ps;
    }

    private static ParticleSystem CreateRuntimeLayer4BodyAura(Transform parent, Material mat)
    {
        GameObject obj = new GameObject("L4_SoftGreen_BodyAura");
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = new Vector3(0f, 1.0f, 0f);

        ParticleSystem ps = obj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 2.20f;
        main.loop = false;
        main.playOnAwake = false;
        main.startDelay = 0.10f;
        main.startLifetime = 2.10f;
        main.startSpeed = 0f;
        main.startSize = 1.45f;
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.45f, 2.2f, 1.15f, 0.48f));
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 3;

        var em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0.0f, 2) });

        var sh = ps.shape;
        sh.enabled = false;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.65f),
            new Keyframe(0.35f, 1.08f),
            new Keyframe(1f, 1.28f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.65f, 1.0f, 0.80f), 0f),
                new GradientColorKey(new Color(0.25f, 0.90f, 0.55f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.42f, 0.22f),
                new GradientAlphaKey(0.30f, 0.68f),
                new GradientAlphaKey(0f, 1f)
            });
        col.color = new ParticleSystem.MinMaxGradient(g);

        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.sharedMaterial = mat;
        return ps;
    }

    private static Texture2D BuildRuntimeFireflyOrbTex(int width, int height)
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
                float mask = 1f - SmoothStep(0.75f, 0.98f, dist);

                float brightCore = Mathf.Exp(-dist * dist * 16f);
                float softHalo = Mathf.Exp(-dist * dist * 3.8f) * 0.62f;
                float val = Mathf.Clamp01((brightCore + softHalo) * mask);

                Color col = Color.Lerp(
                    new Color(0.22f, 1.0f, 0.52f, val),
                    new Color(0.95f, 1.0f, 0.96f, val),
                    Mathf.Pow(val, 1.4f));
                col.a = val;
                pixels[y * width + x] = col;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    private static Texture2D BuildRuntimePlusIconTex(int width, int height)
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
                float dH = SdRoundedBox(nx, ny, 0.52f, 0.14f, 0.08f);
                float dV = SdRoundedBox(nx, ny, 0.14f, 0.52f, 0.08f);
                float dPlus = Mathf.Min(dH, dV);

                float solidPlus = 1f - SmoothStep(-0.02f, 0.045f, dPlus);
                float outerGlow = Mathf.Exp(-Mathf.Max(0f, dPlus) * 6.5f) * 0.58f;
                float edgeMask = 1f - SmoothStep(0.78f, 0.98f, Mathf.Sqrt(nx * nx + ny * ny));
                float val = Mathf.Clamp01((solidPlus + outerGlow) * edgeMask);

                Color col = Color.Lerp(
                    new Color(0.30f, 1.0f, 0.68f, val),
                    new Color(0.94f, 1.0f, 0.97f, val),
                    solidPlus * 0.82f);
                col.a = val;
                pixels[y * width + x] = col;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    private static Texture2D BuildRuntimeGroundRingTex(int width, int height)
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
                float cutoff = 1f - SmoothStep(0.86f, 0.99f, dist);

                float mainRing = Mathf.Exp(-Mathf.Pow((dist - 0.72f) / 0.075f, 2f));
                float softInner = Mathf.Exp(-Mathf.Pow((dist - 0.56f) / 0.16f, 2f)) * 0.42f * SmoothStep(0.26f, 0.60f, dist);
                float val = Mathf.Clamp01((mainRing + softInner) * cutoff);

                Color col = Color.Lerp(
                    new Color(0.18f, 0.96f, 0.52f, val),
                    new Color(0.88f, 1.0f, 0.92f, val),
                    Mathf.Pow(mainRing, 1.5f));
                col.a = val;
                pixels[y * width + x] = col;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    private static float SdRoundedBox(float px, float py, float bx, float by, float r)
    {
        float qx = Mathf.Abs(px) - bx + r;
        float qy = Mathf.Abs(py) - by + r;
        float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
        float inside = Mathf.Min(Mathf.Max(qx, qy), 0f);
        return outside + inside - r;
    }

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp01((x - edge0) / Mathf.Max(0.00001f, edge1 - edge0));
        return t * t * (3f - 2f * t);
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
}
