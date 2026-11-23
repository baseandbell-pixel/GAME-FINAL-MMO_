using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// ระบบควบคุมเอฟเฟกต์ "ใช้ยาฟื้นสเตมิน่าแล้วมีฝูงโลมาแสงสีฟ้าครามแหวกว่ายบินวนรอบตัว 4 เลเยอร์ระดับ AAA"
/// (Stamina Potion Cyan Dolphin Spirits & Hydro-Wind Aura VFX) สำหรับ Unity 6 (URP)
/// - เรียกใช้จากระบบกระเป๋าไอเทมได้ทันทีผ่าน MMOStaminaPotionVFX.UseStaminaPotion()
/// - รองรับปุ่มกดทดสอบ V หรือหมายเลข 2 ทั้ง New Input System และ Legacy Input พร้อมกันโดยไม่เกิด Error
/// - ใช้ Coroutine คุมจังหวะฟื้นพลัง 2.8 วินาที (กดซ้ำเพื่อรีเซ็ตจังหวะได้ทันที) และจางหายอย่างพลิ้วไหว
/// - มีหน้าต่าง OnGUI() แสดงสถานะ (กำลังฟื้นฟู Stamina! / พร้อมใช้งาน) พร้อมสวิตช์เปิด-ปิด showDebugHUD
/// </summary>
[AddComponentMenu("yeen VFX/06. [Player] ยาฟื้นสเตมิน่าผีเสื้อฟ้า (Stamina Potion Butterflies)")]
[DisallowMultipleComponent]
public class MMOStaminaPotionVFX : MonoBehaviour
{
    public static MMOStaminaPotionVFX Instance { get; private set; }

    [Header("=== 1. โหนดและเอฟเฟกต์ยาฟื้นสเตมิน่า 4 เลเยอร์ (4-Layer Cyan Dolphin VFX) ===")]
    [Tooltip("โหนดจุดยึดเอฟเฟกต์ยาฟื้นสเตมิน่าบนตัวละคร (VFX_StaminaDolphin_Mount)")]
    public Transform vfxMountRoot;

    [Tooltip("Layer 1 (0.00s - 0.85s): วงคลื่นน้ำแสงสีฟ้าคราม Horizontal Billboard ขยายใต้ฝ่าเท้า")]
    public ParticleSystem l1HydroCyanGroundRipple;

    [Tooltip("Layer 2 (0.05s - 2.60s): ฝูงวิญญาณโลมาแสงสีฟ้าครามแหวกว่ายเป็นเกลียวคลื่นรอบตัวละคร")]
    public ParticleSystem l2LeapingCyanDolphins;

    [Tooltip("Layer 3 (0.10s - 2.50s): ฟองแสงสีฟ้าใสและละอองดาวสายลมลอยควงสว่านตามเส้นทางโลมา")]
    public ParticleSystem l3AquaSparklesAndBubbles;

    [Tooltip("Layer 4 (0.10s - 2.20s): ม่านละอองแสงสีฟ้าน้ำทะเลโปร่งแสงโอบล้อมกลางลำตัว")]
    public ParticleSystem l4RefreshingCyanBodyMist;

    [Tooltip("ไฟ Point Light สีฟ้าครามเสริมบรรยากาศรอบตัว (ไม่บังคับ)")]
    public Light staminaCyanPointLight;

    [Header("=== 2. ตั้งค่าจังหวะการฟื้นฟูและปุ่มกดทดสอบ (Timing & Input) ===")]
    [Tooltip("ระยะเวลาปล่อยเอฟเฟกต์ฟื้นสเตมิน่าต่อเนื่อง (วินาที - ค่ามาตรฐาน AAA = 2.8 วินาที)")]
    [Range(1.0f, 6.0f)]
    public float staminaEffectDuration = 2.8f;

    [Tooltip("เปิดให้กดปุ่ม V หรือหมายเลข 2 บนคีย์บอร์ดเพื่อทดสอบดื่มยาฟื้นสเตมิน่าได้ทันที")]
    public bool enableTestHotkeys = true;

    [Tooltip("ความสว่างสูงสุดของไฟสีฟ้าครามเสริมรอบตัว")]
    public float maxStaminaLightIntensity = 3.4f;

    [Header("=== 3. หน้าต่างสถานะขณะทดสอบ (On-Screen Debug HUD) ===")]
    [Tooltip("แสดงกรอบสถานะมุมซ้ายล่างของหน้าจอ (กำลังฟื้นฟู Stamina! / พร้อมใช้งาน)")]
    public bool showDebugHUD = true;

    [Header("=== 4. สถานะปัจจุบัน (Read-Only) ===")]
    [SerializeField] private bool isStaminaRestoring = false;
    [SerializeField] private float remainingEffectTime = 0f;

    private Coroutine _activeStaminaCoroutine;
    private float _lastTriggerUnscaledTime = -10f;

    // แคชทรัพยากร Procedural ระดับ Runtime เพื่อรับประกันว่า UseStaminaPotion() ทำงานได้ 100% แม้ยังไม่ได้กดเมนูสร้าง
    private static Texture2D s_RuntimeDolphinTex;
    private static Texture2D s_RuntimeAquaOrbTex;
    private static Texture2D s_RuntimeGroundRippleTex;
    private static Material s_RuntimeMatGroundRipple;
    private static Material s_RuntimeMatDolphin;
    private static Material s_RuntimeMatAquaSparkles;
    private static Material s_RuntimeMatBodyMist;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }

        AutoBindReferences();
        EnsureVFXRigBuilt();
        StopAllStaminaLayers(true);
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
            vfxMountRoot = FindDeepChild(transform, "VFX_StaminaDolphin_Mount");
        }

        Transform searchRoot = vfxMountRoot != null ? vfxMountRoot : transform;

        if (l1HydroCyanGroundRipple == null)
        {
            Transform t = FindDeepChild(searchRoot, "L1_HydroCyan_GroundRipple");
            if (t != null) l1HydroCyanGroundRipple = t.GetComponent<ParticleSystem>();
        }

        if (l2LeapingCyanDolphins == null)
        {
            Transform t = FindDeepChild(searchRoot, "L2_Leaping_CyanDolphinSpirits");
            if (t != null) l2LeapingCyanDolphins = t.GetComponent<ParticleSystem>();
        }

        if (l3AquaSparklesAndBubbles == null)
        {
            Transform t = FindDeepChild(searchRoot, "L3_Aqua_SparklesAndBubbles");
            if (t != null) l3AquaSparklesAndBubbles = t.GetComponent<ParticleSystem>();
        }

        if (l4RefreshingCyanBodyMist == null)
        {
            Transform t = FindDeepChild(searchRoot, "L4_Refreshing_CyanBodyMist");
            if (t != null) l4RefreshingCyanBodyMist = t.GetComponent<ParticleSystem>();
        }

        if (staminaCyanPointLight == null)
        {
            Transform t = FindDeepChild(searchRoot, "Stamina_CyanPointLight");
            if (t != null) staminaCyanPointLight = t.GetComponent<Light>();
        }
    }

    private void Update()
    {
        if (!enableTestHotkeys) return;

        if (ReadUniversalStaminaTrigger())
        {
            TriggerStaminaEffect();
        }
    }

    /// <summary>
    /// กฎเหล็กข้อที่ 2: ฟังก์ชัน Static Public สำหรับให้สคริปต์ระบบกระเป๋าไอเทม (Inventory)
    /// หรือสคริปต์ตัวละครเรียกใช้งานได้ทันทีเมื่อผู้เล่นดื่มยาฟื้นสเตมิน่า
    /// ตัวอย่าง: MMOStaminaPotionVFX.UseStaminaPotion();
    /// </summary>
    public static void UseStaminaPotion()
    {
        MMOStaminaPotionVFX controller = null;

        // ให้ความสำคัญกับตัวละครผู้เล่นจริงในฉาก (PlayerCombat.Local) ก่อนเสมอ
        // เพื่อป้องกันกรณีมีตัวทดสอบเก่าในฉากแย่ง Instance ไปแสดงเอฟเฟกต์ผิดตำแหน่ง
        if (PlayerCombat.Local != null)
        {
            controller = PlayerCombat.Local.GetComponent<MMOStaminaPotionVFX>();
            if (controller == null)
            {
                controller = PlayerCombat.Local.gameObject.AddComponent<MMOStaminaPotionVFX>();
                controller.showDebugHUD = false;
            }
            Instance = controller;
        }

        if (controller == null)
        {
            controller = Instance != null ? Instance : FindAnyObjectByType<MMOStaminaPotionVFX>();
            if (controller != null)
            {
                Instance = controller;
            }
        }

        if (controller != null)
        {
            controller.TriggerStaminaEffect();
        }
    }

    /// <summary>
    /// สั่งเล่นเอฟเฟกต์ยาฟื้นสเตมิน่าโลมาแสงสีฟ้าครามครบทั้ง 4 เลเยอร์เป็นเวลา 2.8 วินาที (กดซ้ำรีเซ็ตได้ทันที)
    /// </summary>
    [ContextMenu("ทดสอบใช้ยาฟื้นสเตมิน่า (Trigger Stamina Dolphin VFX)")]
    public void TriggerStaminaEffect()
    {
        // ป้องกันการเรียกซ้ำในเฟรมเดียวกัน (เช่น เรียกทั้งจาก InventoryManager และ RestoreStamina)
        if (Time.unscaledTime - _lastTriggerUnscaledTime < 0.05f) return;
        _lastTriggerUnscaledTime = Time.unscaledTime;

        EnsureVFXRigBuilt();

        if (!gameObject.activeInHierarchy) return;

        if (_activeStaminaCoroutine != null)
        {
            StopCoroutine(_activeStaminaCoroutine);
        }

        _activeStaminaCoroutine = StartCoroutine(StaminaSequenceRoutine());
    }

    private IEnumerator StaminaSequenceRoutine()
    {
        isStaminaRestoring = true;
        remainingEffectTime = staminaEffectDuration;

        if (vfxMountRoot != null && !vfxMountRoot.gameObject.activeSelf)
        {
            vfxMountRoot.gameObject.SetActive(true);
        }

        // เริ่มต้นทั้ง 4 เลเยอร์พร้อมกัน (รีเซ็ตจังหวะทันทีหากผู้เล่นกดซ้ำ)
        PlayLayerFresh(l1HydroCyanGroundRipple);
        PlayLayerFresh(l2LeapingCyanDolphins);
        PlayLayerFresh(l3AquaSparklesAndBubbles);
        PlayLayerFresh(l4RefreshingCyanBodyMist);

        float elapsed = 0f;
        float duration = Mathf.Max(0.5f, staminaEffectDuration);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            remainingEffectTime = Mathf.Max(0f, duration - elapsed);

            // คุมความสว่าง Point Light สีฟ้าครามให้พลิ้วไหวแล้วค่อยๆ หรี่ลงอย่างนุ่มนวล
            if (staminaCyanPointLight != null)
            {
                staminaCyanPointLight.enabled = true;
                float t = Mathf.Clamp01(elapsed / duration);
                float fadeIn = Mathf.Clamp01(t / 0.14f);
                float fadeOut = t < 0.65f ? 1f : 1f - Mathf.Clamp01((t - 0.65f) / 0.35f);
                float wavePulse = 0.88f + 0.12f * Mathf.Sin(elapsed * 6.0f);
                staminaCyanPointLight.intensity = maxStaminaLightIntensity * fadeIn * fadeOut * wavePulse;
            }

            yield return null;
        }

        // หยุดปล่อยอนุภาคใหม่แบบ StopEmitting เพื่อให้ฝูงโลมาและฟองแสงที่แหวกว่ายอยู่ค่อยๆ จางหายในอากาศ
        StopAllStaminaLayers(false);

        if (staminaCyanPointLight != null)
        {
            staminaCyanPointLight.intensity = 0f;
            staminaCyanPointLight.enabled = false;
        }

        remainingEffectTime = 0f;
        isStaminaRestoring = false;
        _activeStaminaCoroutine = null;
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

    private void StopAllStaminaLayers(bool clearImmediately)
    {
        ParticleSystemStopBehavior stopMode = clearImmediately
            ? ParticleSystemStopBehavior.StopEmittingAndClear
            : ParticleSystemStopBehavior.StopEmitting;

        StopSingleLayer(l1HydroCyanGroundRipple, stopMode);
        StopSingleLayer(l2LeapingCyanDolphins, stopMode);
        StopSingleLayer(l3AquaSparklesAndBubbles, stopMode);
        StopSingleLayer(l4RefreshingCyanBodyMist, stopMode);

        if (staminaCyanPointLight != null && clearImmediately)
        {
            staminaCyanPointLight.intensity = 0f;
            staminaCyanPointLight.enabled = false;
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
    // กฎเหล็กข้อที่ 2: รองรับปุ่มทดสอบ V หรือหมายเลข 2 ทั้ง New Input System และ Legacy Input
    // ================================================================================================
    private static bool ReadUniversalStaminaTrigger()
    {
        bool pressedNew = false;
        if (Keyboard.current != null)
        {
            if (Keyboard.current.vKey.wasPressedThisFrame ||
                Keyboard.current.digit2Key.wasPressedThisFrame ||
                Keyboard.current.numpad2Key.wasPressedThisFrame)
            {
                pressedNew = true;
            }
        }

        bool pressedLegacy = false;
        try
        {
            if (Input.GetKeyDown(KeyCode.V) || Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
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
    // กฎเหล็กข้อที่ 2: หน้าต่าง OnGUI() แสดงสถานะการฟื้นสเตมิน่า (กำลังฟื้นฟู Stamina! / พร้อมใช้งาน)
    // ================================================================================================
    private void OnGUI()
    {
        if (!showDebugHUD || !Application.isPlaying) return;

        GUI.color = new Color(0.02f, 0.08f, 0.14f, 0.86f);
        GUI.Box(new Rect(14f, Screen.height - 196f, 460f, 86f), GUIContent.none);
        GUI.color = Color.white;

        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = 14;
        style.richText = true;
        style.normal.textColor = Color.white;

        string statusBadge = isStaminaRestoring
            ? "<color=#44EEFF><b>กำลังฟื้นฟู Stamina! (" + remainingEffectTime.ToString("F1") + "s)</b></color>"
            : "<color=#B8E6FF><b>พร้อมใช้งาน (READY)</b></color>";

        string hudText =
            "<color=#33DDFF><b>[AAA Stamina Potion Cyan Butterfly Spirits VFX]</b></color>\n" +
            "• <b>สถานะยาฟื้นพลัง:</b> " + statusBadge + "\n" +
            "• <b>วิธีทดสอบ:</b> กดปุ่ม <b>[V]</b> หรือหมายเลข <b>[2]</b> เพื่อปล่อยฝูงผีเสื้อแสงสีฟ้าบินวนล้อมตัว";

        GUI.Label(new Rect(24f, Screen.height - 190f, 440f, 74f), hudText, style);
    }

    // ================================================================================================
    // RUNTIME SELF-HEALING VFX BUILDER (สร้างโหนดทั้ง 4 เลเยอร์อัตโนมัติหากยังไม่มีบนตัวละคร)
    // ================================================================================================
    public void EnsureVFXRigBuilt()
    {
        AutoBindReferences();

        bool missingAny =
            l1HydroCyanGroundRipple == null ||
            l2LeapingCyanDolphins == null ||
            l3AquaSparklesAndBubbles == null ||
            l4RefreshingCyanBodyMist == null;

        if (!missingAny) return;

        EnsureRuntimeSharedAssets();

        if (vfxMountRoot == null)
        {
            GameObject mountObj = new GameObject("VFX_StaminaDolphin_Mount");
            mountObj.transform.SetParent(transform, false);
            Transform visual = transform.Find("CharacterVisual");
            mountObj.transform.localPosition = visual != null ? new Vector3(0f, visual.localPosition.y, 0f) : Vector3.zero;
            mountObj.transform.localRotation = Quaternion.identity;
            vfxMountRoot = mountObj.transform;
        }

        if (l1HydroCyanGroundRipple == null)
            l1HydroCyanGroundRipple = CreateRuntimeLayer1GroundRipple(vfxMountRoot, s_RuntimeMatGroundRipple);
        if (l2LeapingCyanDolphins == null)
            l2LeapingCyanDolphins = CreateRuntimeLayer2CyanDolphins(vfxMountRoot, s_RuntimeMatDolphin);
        if (l3AquaSparklesAndBubbles == null)
            l3AquaSparklesAndBubbles = CreateRuntimeLayer3AquaSparkles(vfxMountRoot, s_RuntimeMatAquaSparkles);
        if (l4RefreshingCyanBodyMist == null)
            l4RefreshingCyanBodyMist = CreateRuntimeLayer4CyanBodyMist(vfxMountRoot, s_RuntimeMatBodyMist);

        if (staminaCyanPointLight == null)
        {
            GameObject lightObj = new GameObject("Stamina_CyanPointLight");
            lightObj.transform.SetParent(vfxMountRoot, false);
            lightObj.transform.localPosition = new Vector3(0f, 1.05f, 0f);
            staminaCyanPointLight = lightObj.AddComponent<Light>();
            staminaCyanPointLight.type = LightType.Point;
            staminaCyanPointLight.color = new Color(0.18f, 0.86f, 1.0f, 1f);
            staminaCyanPointLight.range = 6.2f;
            staminaCyanPointLight.intensity = 0f;
            staminaCyanPointLight.enabled = false;
        }
    }

    private static void EnsureRuntimeSharedAssets()
    {
        if (s_RuntimeDolphinTex == null) s_RuntimeDolphinTex = BuildRuntimeButterflySpiritTex(256, 256);
        if (s_RuntimeAquaOrbTex == null) s_RuntimeAquaOrbTex = BuildRuntimeAquaOrbTex(128, 128);
        if (s_RuntimeGroundRippleTex == null) s_RuntimeGroundRippleTex = BuildRuntimeHydroRippleTex(256, 256);

        if (s_RuntimeMatGroundRipple == null) s_RuntimeMatGroundRipple = CreateRuntimeURPAdditiveMat(s_RuntimeGroundRippleTex, new Color(0.28f, 1.45f, 1.85f, 1f));
        if (s_RuntimeMatDolphin == null) s_RuntimeMatDolphin = CreateRuntimeURPAdditiveMat(s_RuntimeDolphinTex, new Color(0.40f, 1.70f, 2.20f, 1f));
        if (s_RuntimeMatAquaSparkles == null) s_RuntimeMatAquaSparkles = CreateRuntimeURPAdditiveMat(s_RuntimeAquaOrbTex, new Color(0.45f, 1.75f, 2.10f, 1f));
        if (s_RuntimeMatBodyMist == null) s_RuntimeMatBodyMist = CreateRuntimeURPAdditiveMat(s_RuntimeAquaOrbTex, new Color(0.24f, 1.25f, 1.55f, 0.52f));
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

    private static ParticleSystem CreateRuntimeLayer1GroundRipple(Transform parent, Material mat)
    {
        GameObject obj = new GameObject("L1_HydroCyan_GroundRipple");
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
        main.startSize = 1.85f;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.28f, 2.75f, 3.4f, 1f));
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
            new Keyframe(0f, 0.22f, 2.9f, 2.9f),
            new Keyframe(0.55f, 0.95f, 0.45f, 0.45f),
            new Keyframe(1f, 1.18f, 0f, 0f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.82f, 0.98f, 1.0f), 0f),
                new GradientColorKey(new Color(0.14f, 0.86f, 1.0f), 0.55f),
                new GradientColorKey(new Color(0.06f, 0.52f, 0.92f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.95f, 0.14f),
                new GradientAlphaKey(0.65f, 0.58f),
                new GradientAlphaKey(0f, 1f)
            });
        col.color = new ParticleSystem.MinMaxGradient(g);

        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        r.sharedMaterial = mat;
        return ps;
    }

    private static ParticleSystem CreateRuntimeLayer2CyanDolphins(Transform parent, Material mat)
    {
        GameObject obj = new GameObject("L2_Leaping_CyanDolphinSpirits");
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = new Vector3(0f, 0.16f, 0f);
        obj.transform.localRotation = Quaternion.identity;

        ParticleSystem ps = obj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 2.60f;
        main.loop = true;
        main.playOnAwake = false;
        main.startDelay = 0.05f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.85f, 2.65f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.09f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.28f); // ผีเสื้อแสงสีฟ้าขนาดเล็กน่ารักกำลังดี
        main.startRotation = new ParticleSystem.MinMaxCurve(-22f * Mathf.Deg2Rad, 22f * Mathf.Deg2Rad);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.32f, 2.65f, 3.60f, 1f),
            new Color(0.82f, 3.30f, 4.10f, 1f));
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 90;

        var em = ps.emission;
        em.rateOverTime = 12f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0.0f, 8) });

        var sh = ps.shape;
        sh.enabled = true;
        sh.shapeType = ParticleSystemShapeType.Circle;
        sh.radius = 0.68f;
        sh.radiusThickness = 0.35f;
        sh.rotation = new Vector3(-90f, 0f, 0f);

        // ทุกแกนใน Velocity over Lifetime ต้องเป็นโหมด Constant เหมือนกันทั้งหมด 100% (Unity 6 Safe)
        // ปรับให้ผีเสื้อบินวนรอบตัวช้าลงและลอยขึ้นอย่างนุ่มนวลพลิ้วไหว
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = new ParticleSystem.MinMaxCurve(0f);
        vel.y = new ParticleSystem.MinMaxCurve(0.42f);       // ลอยขึ้นช้าๆ นุ่มนวล
        vel.z = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalX = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalY = new ParticleSystem.MinMaxCurve(1.35f); // บินวนรอบตัวช้าลงแบบผ่อนคลาย
        vel.orbitalZ = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalOffsetX = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalOffsetY = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalOffsetZ = new ParticleSystem.MinMaxCurve(0f);
        vel.radial = new ParticleSystem.MinMaxCurve(0f);
        vel.speedModifier = new ParticleSystem.MinMaxCurve(1f);

        // Noise อ่อนโยนให้ผีเสื้อบินกระพือส่ายไปมาเหมือนผีเสื้อจริง
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.28f;
        noise.frequency = 1.2f;
        noise.scrollSpeed = 0.55f;
        noise.damping = true;
        noise.octaveCount = 2;

        // ขยับเอียงปีกเบาๆ ขณะบิน (Constant Mode เสมอกันทุกแกน)
        var rol = ps.rotationOverLifetime;
        rol.enabled = true;
        rol.separateAxes = true;
        rol.x = new ParticleSystem.MinMaxCurve(0f);
        rol.y = new ParticleSystem.MinMaxCurve(0f);
        rol.z = new ParticleSystem.MinMaxCurve(16f * Mathf.Deg2Rad);

        // จำลองจังหวะกระพือปีกเบาๆ ด้วยกราฟ Size over Lifetime แบบนุ่มนวล
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0.00f, 0.15f),
            new Keyframe(0.16f, 1.00f),
            new Keyframe(0.32f, 0.84f),
            new Keyframe(0.48f, 1.02f),
            new Keyframe(0.64f, 0.86f),
            new Keyframe(0.80f, 0.92f),
            new Keyframe(1.00f, 0.00f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.88f, 0.99f, 1.0f), 0f),
                new GradientColorKey(new Color(0.22f, 0.90f, 1.0f), 0.55f),
                new GradientColorKey(new Color(0.10f, 0.58f, 0.98f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.16f),
                new GradientAlphaKey(0.92f, 0.72f),
                new GradientAlphaKey(0f, 1f)
            });
        col.color = new ParticleSystem.MinMaxGradient(g);

        // ใช้ Billboard เพื่อให้รูปปีกผีเสื้อคมชัดสมมาตรไม่ยืดเบี้ยว
        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.sharedMaterial = mat;
        return ps;
    }

    private static ParticleSystem CreateRuntimeLayer3AquaSparkles(Transform parent, Material mat)
    {
        GameObject obj = new GameObject("L3_Aqua_SparklesAndBubbles");
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = new Vector3(0f, 0.14f, 0f);
        obj.transform.localRotation = Quaternion.identity;

        ParticleSystem ps = obj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 2.50f;
        main.loop = true;
        main.playOnAwake = false;
        main.startDelay = 0.10f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.60f, 2.35f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.10f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.13f); // ละอองฟองแสงเล็กละมุนตามผีเสื้อ
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.42f, 2.85f, 3.60f, 1f),
            new Color(0.95f, 3.45f, 4.10f, 1f));
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 140;

        var em = ps.emission;
        em.rateOverTime = 24f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0.0f, 10) });

        var sh = ps.shape;
        sh.enabled = true;
        sh.shapeType = ParticleSystemShapeType.Circle;
        sh.radius = 0.68f;
        sh.radiusThickness = 0.45f;
        sh.rotation = new Vector3(-90f, 0f, 0f);

        // ทุกแกนใน Velocity over Lifetime ต้องเป็น Constant Mode เสมอกัน (Unity 6 Safe)
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = new ParticleSystem.MinMaxCurve(0f);
        vel.y = new ParticleSystem.MinMaxCurve(0.48f);       // ลอยขึ้นช้าๆ ตามจังหวะผีเสื้อ
        vel.z = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalX = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalY = new ParticleSystem.MinMaxCurve(1.15f); // วนช้าลงให้ละมุนตา
        vel.orbitalZ = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalOffsetX = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalOffsetY = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalOffsetZ = new ParticleSystem.MinMaxCurve(0f);
        vel.radial = new ParticleSystem.MinMaxCurve(0f);
        vel.speedModifier = new ParticleSystem.MinMaxCurve(1f);

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.18f;
        noise.frequency = 1.2f;
        noise.damping = true;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.18f, 1f),
            new Keyframe(0.55f, 0.78f),
            new Keyframe(1f, 0f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.88f, 0.99f, 1.0f), 0f),
                new GradientColorKey(new Color(0.24f, 0.88f, 1.0f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.95f, 0.16f),
                new GradientAlphaKey(0.82f, 0.70f),
                new GradientAlphaKey(0f, 1f)
            });
        col.color = new ParticleSystem.MinMaxGradient(g);

        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.sharedMaterial = mat;
        return ps;
    }

    private static ParticleSystem CreateRuntimeLayer4CyanBodyMist(Transform parent, Material mat)
    {
        GameObject obj = new GameObject("L4_Refreshing_CyanBodyMist");
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
        main.startSize = 1.15f; // ลดขนาดม่านออร่าให้โปร่งสบายตาขึ้น
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.24f, 1.65f, 2.25f, 0.40f));
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
            new Keyframe(0.36f, 1.05f),
            new Keyframe(1f, 1.22f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.65f, 0.96f, 1.0f), 0f),
                new GradientColorKey(new Color(0.14f, 0.78f, 1.0f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.38f, 0.22f),
                new GradientAlphaKey(0.26f, 0.68f),
                new GradientAlphaKey(0f, 1f)
            });
        col.color = new ParticleSystem.MinMaxGradient(g);

        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.sharedMaterial = mat;
        return ps;
    }

    // วาดผีเสื้อวิญญาณแสงสีฟ้าคราม (256x256) ปีกบน-ล่างสมมาตร ลายเส้นปีกเรืองแสง หนวดโค้ง และประกายละออง
    private static Texture2D BuildRuntimeButterflySpiritTex(int width, int height)
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

                EvalButterflyPixel(nx, ny, out float alpha, out float coreBrightness);
                float edgeFade = 1f - SmoothStep(0.90f, 0.99f, Mathf.Max(Mathf.Abs(nx), Mathf.Abs(ny)));
                alpha = Mathf.Clamp01(alpha * edgeFade);

                Color col = Color.Lerp(
                    new Color(0.10f, 0.86f, 1.0f, alpha),
                    new Color(0.95f, 0.99f, 1.0f, alpha),
                    Mathf.Clamp01(coreBrightness));
                col.a = alpha;
                pixels[y * width + x] = col;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    private static void EvalButterflyPixel(float nx, float ny, out float alpha, out float coreBrightness)
    {
        float ax = Mathf.Abs(nx); // สมมาตรซ้าย-ขวา

        // 1. ปีกบน (Forewing) - ทรงปีกผีเสื้อโค้งกว้างเฉียงขึ้นด้านข้างอย่างสง่างาม
        float fx = ax - 0.43f;
        float fy = ny - 0.25f;
        float cosF = Mathf.Cos(-0.42f);
        float sinF = Mathf.Sin(-0.42f);
        float rfx = fx * cosF - fy * sinF;
        float rfy = fx * sinF + fy * cosF;
        // ปรับให้ปลายปีกด้านนอกบานมนสวยและโคนปีกเรียวเข้าหาลำตัว
        float foreScaleY = 0.29f + 0.08f * Mathf.Clamp(rfx, -0.4f, 0.4f);
        float dFore = Mathf.Sqrt((rfx * rfx) / (0.44f * 0.44f) + (rfy * rfy) / (foreScaleY * foreScaleY)) - 1.0f;

        // 2. ปีกล่าง (Hindwing) - ทรงหยดน้ำโค้งอ่อนช้อยลงด้านล่าง
        float hx = ax - 0.30f;
        float hy = ny + 0.27f;
        float cosH = Mathf.Cos(0.48f);
        float sinH = Mathf.Sin(0.48f);
        float rhx = hx * cosH - hy * sinH;
        float rhy = hx * sinH + hy * cosH;
        float dHind = Mathf.Sqrt((rhx * rhx) / (0.31f * 0.31f) + (rhy * rhy) / (0.23f * 0.23f)) - 1.0f;

        // รวมปีกบนและปีกล่างด้วย Smooth Min ให้รอยต่อปีกนุ่มธรรมชาติ
        float dWing = SmoothMin(dFore, dHind, 0.14f);

        // เยื่อปีกโปร่งแสง + ขอบปีกเรืองแสงสว่างคมชัด
        float wingFill = (1f - SmoothStep(-0.08f, 0.06f, dWing)) * SmoothStep(0.02f, 0.09f, ax);
        float wingRim = Mathf.Exp(-Mathf.Pow(dWing / 0.075f, 2f)) * SmoothStep(0.03f, 0.10f, ax);

        // เส้นลายปีกแผ่ออกจากลำตัว
        float angleFromThorax = Mathf.Atan2(ny - 0.04f, ax + 0.02f);
        float veinPattern = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(angleFromThorax * 14f), 5f) * wingFill * 0.45f;

        // จุดประกายดาวเล็กๆ บนปลายปีกบน
        float spotDist = Mathf.Sqrt((ax - 0.58f) * (ax - 0.58f) + (ny - 0.36f) * (ny - 0.36f));
        float wingSpot = Mathf.Exp(-spotDist * spotDist * 90f) * 0.85f;

        // 3. ลำตัวผีเสื้อเพรียวเรืองแสงตรงกลาง (Head + Thorax + Abdomen)
        float bodyDist = SdSegment(ax, ny, 0f, 0.26f, 0f, -0.44f);
        float bodyRadius = Mathf.Lerp(0.056f, 0.024f, Mathf.InverseLerp(0.26f, -0.44f, ny));
        float bodyMask = 1f - SmoothStep(bodyRadius * 0.65f, bodyRadius * 1.45f, bodyDist);

        // 4. หนวดผีเสื้อโค้งอ่อนช้อย 2 เส้น
        float antDistA = SdSegment(ax, ny, 0.01f, 0.28f, 0.10f, 0.52f);
        float antDistB = SdSegment(ax, ny, 0.10f, 0.52f, 0.20f, 0.64f);
        float antMask = (1f - SmoothStep(0.014f, 0.036f, Mathf.Min(antDistA, antDistB))) * 0.88f;

        // ออร่าฟุ้งอ่อนๆ รอบผีเสื้อ
        float outerGlow = Mathf.Exp(-Mathf.Max(0f, dWing) * 5.5f) * 0.30f;

        alpha = Mathf.Clamp01(wingFill * 0.68f + wingRim * 0.72f + veinPattern + wingSpot + bodyMask + antMask + outerGlow);
        coreBrightness = Mathf.Clamp01(bodyMask * 0.95f + wingRim * 0.65f + wingSpot * 0.85f + veinPattern * 0.5f);
    }

    private static float SmoothMin(float a, float b, float k)
    {
        float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / Mathf.Max(0.0001f, k));
        return Mathf.Lerp(b, a, h) - k * h * (1f - h);
    }

    private static Texture2D BuildRuntimeAquaOrbTex(int width, int height)
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
                float bubbleRim = Mathf.Exp(-Mathf.Pow((dist - 0.52f) / 0.14f, 2f)) * 0.35f;
                float softHalo = Mathf.Exp(-dist * dist * 3.8f) * 0.58f;
                float val = Mathf.Clamp01((brightCore + bubbleRim + softHalo) * mask);

                Color col = Color.Lerp(
                    new Color(0.14f, 0.86f, 1.0f, val),
                    new Color(0.94f, 0.99f, 1.0f, val),
                    Mathf.Pow(val, 1.4f));
                col.a = val;
                pixels[y * width + x] = col;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    private static Texture2D BuildRuntimeHydroRippleTex(int width, int height)
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
                float cutoff = 1f - SmoothStep(0.86f, 0.99f, dist);

                float outerWave = Mathf.Exp(-Mathf.Pow((dist - 0.74f) / 0.068f, 2f));
                float innerWave = Mathf.Exp(-Mathf.Pow((dist - 0.50f) / 0.095f, 2f)) * 0.52f;
                float waterFlow = 0.88f + 0.12f * Mathf.Sin(angle * 8f);
                float val = Mathf.Clamp01((outerWave + innerWave) * waterFlow * cutoff);

                Color col = Color.Lerp(
                    new Color(0.12f, 0.82f, 1.0f, val),
                    new Color(0.88f, 0.99f, 1.0f, val),
                    Mathf.Pow(outerWave, 1.45f));
                col.a = val;
                pixels[y * width + x] = col;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    private static float SdSegment(float px, float py, float ax, float ay, float bx, float by)
    {
        float pax = px - ax;
        float pay = py - ay;
        float bax = bx - ax;
        float bay = by - ay;
        float denom = Mathf.Max(0.00001f, bax * bax + bay * bay);
        float h = Mathf.Clamp01((pax * bax + pay * bay) / denom);
        float dx = pax - bax * h;
        float dy = pay - bay * h;
        return Mathf.Sqrt(dx * dx + dy * dy);
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
