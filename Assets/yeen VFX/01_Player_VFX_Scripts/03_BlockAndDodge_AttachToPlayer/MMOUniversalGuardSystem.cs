using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// ระบบบล็อคค้างและตีบล็อคแตกแบบใช้ได้ทั้งเพลย์เยอร์และมอนสเตอร์ระดับ AAA
/// (Universal Guard Stance & Guard Break Shatter VFX) สำหรับ Unity 6 (URP - Universal Render Pipeline)
/// - ใช้สคริปต์เดียวได้ทั้งฝั่ง Player และ Monster ผ่าน EntityRole
/// - กางโล่พลังงานทรงโดม (VFX_ActiveGuardShield) เฉพาะตอนอยู่ในสถานะกดบล็อคค้าง (isBlocking == true)
/// - ฟังก์ชัน ReceiveIncomingAttack(guardDamage, hitWorldPos, attackerForwardDir) ตรวจเงื่อนไข isBlocking && !isGuardBroken
///   หากไม่ได้กดบล็อคค้างอยู่จะคืนค่า false และไม่เกิดเอฟเฟกต์บล็อคแตก
/// - เมื่อค่าเกราะ (currentGuardHP) หมดลง จะปิดโล่ เสกเอฟเฟกต์ 4 เลเยอร์ (VFX_UniversalGuardBreak) สั่นกล้องเบาๆ
///   และติดสถานะมึนงง (Staggered / isGuardBroken) นาน 2.0 วินาที ก่อนฟื้นฟูเกราะอัตโนมัติ
/// - ออกแบบเป็น Standalone 100% ไม่กระทบโค้ดระบบเกมหลักของเพื่อน และรองรับทั้ง New Input System & Legacy Input
/// </summary>
[AddComponentMenu("yeen VFX/03C. [Universal] บล็อคค้างและตีบล็อคแตก 4 เลเยอร์ (Player & Monster)")]
[DisallowMultipleComponent]
[DefaultExecutionOrder(200)]
public class MMOUniversalGuardSystem : MonoBehaviour
{
    public enum EntityRole
    {
        Player,
        Monster
    }

    [Header("=== 1. บทบาทและค่าพลังเกราะป้องกัน (Role & Guard HP) ===")]
    [Tooltip("เลือกบทบาทของตัวละครนี้: Player (อ่านปุ่มบล็อคค้างจากผู้เล่น) หรือ Monster (สั่งยกโล่จาก AI ผ่าน SetBlockingState)")]
    public EntityRole entityRole = EntityRole.Player;

    [Tooltip("ค่าพลังเกราะป้องกันสูงสุด (ค่าเริ่มต้น 100 — กดปุ่ม B ทดสอบ 40 ดาเมจ 3 ครั้งเกราะจะแตกพอดี)")]
    [Min(1f)]
    public float maxGuardHP = 100f;

    [Tooltip("ค่าพลังเกราะป้องกันปัจจุบัน")]
    public float currentGuardHP = 100f;

    [Tooltip("ระยะเวลาที่ติดสถานะมึนงง (Staggered) หลังบล็อคแตก ก่อนฟื้นฟูเกราะอัตโนมัติ (วินาที)")]
    [Range(0.5f, 6.0f)]
    public float guardBreakStaggerDuration = 2.0f;

    [Tooltip("ฟื้นฟูเกราะอัตโนมัติช้าๆ เมื่อไม่ได้ถูกโจมตีระยะหนึ่ง")]
    public bool autoRegenerateGuardHP = true;

    [Tooltip("หน่วงเวลาหลังถูกตีครั้งล่าสุดก่อนเริ่มฟื้นฟูเกราะ (วินาที)")]
    [Min(0.5f)]
    public float guardRegenDelay = 3.5f;

    [Tooltip("อัตราการฟื้นฟูเกราะต่อวินาที")]
    [Min(1f)]
    public float guardRegenRate = 18f;

    [Header("=== 2. โล่พลังงานและพรีแฟบบล็อคแตก 4 เลเยอร์ (Shield & Shatter VFX) ===")]
    [Tooltip("โหนดโล่พลังงานโค้งทรงโดมด้านหน้าตัวละคร (VFX_ActiveGuardShield) สร้างจากเมนู VFX Mentor/19")]
    public GameObject activeGuardShieldObject;

    [Tooltip("Prefab เอฟเฟกต์ตีบล็อคแตก 4 เลเยอร์ (VFX_UniversalGuardBreak.prefab)")]
    public GameObject guardBreakShatterPrefab;

    [Tooltip("ตำแหน่งเยื้องด้านหน้าตัวละครสำหรับวางโล่และจุดระเบิด (Local Offset)")]
    public Vector3 shieldLocalOffset = new Vector3(0f, 1.15f, 0.85f);

    [Tooltip("ขนาดเอฟเฟกต์บล็อคแตก")]
    [Min(0.1f)]
    public float shatterVfxScale = 1.0f;

    [Tooltip("เวลาทำลายเอฟเฟกต์บล็อคแตกอัตโนมัติหลังระเบิด (วินาที)")]
    [Min(0.8f)]
    public float shatterAutoDestroyDelay = 2.0f;

    [Header("=== 3. ปุ่มควบคุมฝั่ง Player และแรงสั่นกล้อง (Input & Camera Shake) ===")]
    [Tooltip("ฝั่ง Player: อนุญาตให้กดคลิกขวาเมาส์ค้างไว้เพื่อกางโล่")]
    public bool allowRightMouseBlock = true;

    [Tooltip("ฝั่ง Player: ปุ่มคีย์บอร์ดสำหรับกดค้างเพื่อกางโล่ (ค่าเริ่มต้น = F)")]
    public KeyCode playerBlockKey = KeyCode.F;

    [Tooltip("สั่นกล้องเบาๆ เมื่อเกิดจังหวะบล็อคแตกเพื่อเพิ่มน้ำหนักการปะทะ")]
    public bool enableCameraShake = true;

    [Tooltip("ระยะเวลาสั่นกล้อง (วินาที)")]
    [Range(0.05f, 0.5f)]
    public float cameraShakeDuration = 0.20f;

    [Tooltip("ความแรงของการสั่นกล้อง")]
    [Range(0.01f, 0.5f)]
    public float cameraShakeMagnitude = 0.14f;

    [Header("=== 4. ระบบทดสอบและหน้าต่างสถานะ (Testing [B] & OnGUI HUD) ===")]
    [Tooltip("เปิดใช้งานปุ่มทดสอบ [B] จำลองการถูกโจมตี 40 ดาเมจ")]
    public bool enableTestHitKey = true;

    [Tooltip("ปุ่มทดสอบจำลองโดนโจมตี (ค่าเริ่มต้น = B)")]
    public KeyCode testHitKey = KeyCode.B;

    [Tooltip("ดาเมจเกราะต่อการกดปุ่มทดสอบ [B] 1 ครั้ง (40 ดาเมจ x 3 ครั้ง = แตก)")]
    public float testHitGuardDamage = 40f;

    [Tooltip("แสดงหน้าต่าง OnGUI หลอดเกราะ (Player มุมซ้ายล่าง / Monster มุมขวาล่าง ไม่ทับกัน)")]
    public bool showDebugHUD = true;

    [Header("=== 5. สถานะ Real-Time (Read-Only Inspector) ===")]
    [SerializeField] private bool isBlocking = false;
    [SerializeField] private bool isGuardBroken = false;
    [SerializeField] private float staggerTimeRemaining = 0f;

    // Events สำหรับให้สคริปต์แอนิเมชัน/เสียง/AI มาเชื่อมต่อภายหลังได้ทันทีโดยไม่ต้องแก้โค้ดนี้
    public event Action<float, float> OnGuardDamaged;
    public event Action<Vector3, Vector3> OnGuardBroken;
    public event Action OnGuardRecovered;

    public bool IsBlocking => isBlocking && !isGuardBroken;
    public bool IsGuardBroken => isGuardBroken;
    public float GuardRatio => maxGuardHP > 0f ? Mathf.Clamp01(currentGuardHP / maxGuardHP) : 0f;

    // Internal runtime state
    private bool monsterRequestedBlock = false;
    private float lastGuardHitTime = -99f;
    private float shieldImpactFlash = 0f;
    private float shieldDeployProgress = 0f;
    private Vector3 shieldBaseLocalScale = Vector3.one;
    private Renderer shieldRenderer;
    private Material shieldMaterialInstance;
    private Color shieldOriginalColor = new Color(0.24f, 0.85f, 1.0f, 0.85f);
    private Coroutine recoveryCoroutine;

    // Delta-tracked Camera Shake (ปลอดภัยต่อ PlayerFollowCamera.LateUpdate)
    private float shakeTimeRemaining = 0f;
    private float shakeTotalDuration = 0.20f;
    private float shakeActiveMagnitude = 0.14f;
    private Vector3 lastShakeWorldOffset = Vector3.zero;
    private Vector3 lastPostShakeCameraPos = Vector3.zero;
    private Camera lastShakenCamera;

    private string lastStatusMessage = "พร้อมใช้งาน";
    private float lastEventFlashTime = -99f;

    // Per-frame HUD stack tracking (กัน Monster หลายตัววาดกล่อง OnGUI ทับกันเอง)
    private static int hudFrameMarker = -1;
    private static int playerHudDrawIndex = 0;
    private static int monsterHudDrawIndex = 0;

    // Runtime Self-Healing 4-Layer Fallback Assets (ทำงานได้ 100% แม้ยังไม่ได้ลาก Prefab)
    private static Mesh fallbackQuadMesh;
    private static Mesh fallbackShardMesh;
    private static Mesh fallbackShieldDomeMesh;
    private static Texture2D fallbackHexTex;
    private static Texture2D fallbackCrackTex;
    private static Texture2D fallbackGlowTex;
    private static Texture2D fallbackWaveTex;
    private static Material fallbackShieldMat;
    private static Material fallbackCrackMat;
    private static Material fallbackShardMat;
    private static Material fallbackSparkMat;
    private static Material fallbackWaveMat;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticsOnPlay()
    {
        hudFrameMarker = -1;
        playerHudDrawIndex = 0;
        monsterHudDrawIndex = 0;
        fallbackQuadMesh = null;
        fallbackShardMesh = null;
        fallbackShieldDomeMesh = null;
        fallbackHexTex = null;
        fallbackCrackTex = null;
        fallbackGlowTex = null;
        fallbackWaveTex = null;
        fallbackShieldMat = null;
        fallbackCrackMat = null;
        fallbackShardMat = null;
        fallbackSparkMat = null;
        fallbackWaveMat = null;
    }

    private void Awake()
    {
        currentGuardHP = Mathf.Clamp(currentGuardHP, 0f, maxGuardHP);
        if (currentGuardHP <= 0f) currentGuardHP = maxGuardHP;

        EnsureActiveGuardShieldSetup();

        // หาก Monster ถูกตั้งค่า isBlocking = true ไว้ใน Inspector (เช่น หุ่นทดสอบจากเมนู Generator) ให้จำสถานะไว้
        if (entityRole == EntityRole.Monster && isBlocking)
        {
            monsterRequestedBlock = true;
        }
    }

    private void OnEnable()
    {
        UpdateShieldVisualImmediate();
    }

    private void OnDisable()
    {
        RestorePendingCameraShake();
    }

    private void OnDestroy()
    {
        RestorePendingCameraShake();
        if (shieldMaterialInstance != null)
        {
            Destroy(shieldMaterialInstance);
            shieldMaterialInstance = null;
        }
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        // 1. อัปเดตสถานะบล็อคค้างตามบทบาท (Player อ่านปุ่มค้าง / Monster อ่านคำสั่งจาก AI)
        if (isGuardBroken)
        {
            isBlocking = false;
            staggerTimeRemaining = Mathf.Max(0f, staggerTimeRemaining - dt);
        }
        else if (entityRole == EntityRole.Player)
        {
            isBlocking = CheckPlayerBlockInput();
        }
        else
        {
            isBlocking = monsterRequestedBlock;
        }

        // 2. ฟื้นฟูเกราะป้องกันอัตโนมัติเมื่อไม่ได้ถูกโจมตีเกิน guardRegenDelay
        if (autoRegenerateGuardHP && !isGuardBroken && currentGuardHP < maxGuardHP && (Time.time - lastGuardHitTime) >= guardRegenDelay)
        {
            currentGuardHP = Mathf.Min(maxGuardHP, currentGuardHP + guardRegenRate * dt);
        }

        // 3. อัปเดตแอนิเมชันกาง/หุบและแสงสะท้อนรับแรงของโล่พลังงานทรงโดม
        UpdateActiveShieldVisual(dt);

        // 4. ปุ่มทดสอบ [B] จำลองการถูกโจมตี 40 ดาเมจ (ต้องบล็อคค้างไว้และกด B 3 ครั้งจึงจะแตก)
        if (enableTestHitKey && IsKeyPressedThisFrame(testHitKey))
        {
            Vector3 simulatedHitPos = GetShieldWorldPosition();
            Vector3 simulatedAttackerDir = -transform.forward;
            bool blocked = ReceiveIncomingAttack(testHitGuardDamage, simulatedHitPos, simulatedAttackerDir);
            if (!blocked && !isGuardBroken)
            {
                lastStatusMessage = entityRole == EntityRole.Player
                    ? "ไม่เกิดบล็อคแตก (กรุณากดคลิกขวา หรือ F ค้างไว้ก่อนกด B)"
                    : "มอนสเตอร์ไม่ได้ยกโล่ (เรียก SetBlockingState(true) ก่อน)";
                lastEventFlashTime = Time.time;
            }
        }
    }

    private void LateUpdate()
    {
        UpdateCameraShakeLate(Time.unscaledDeltaTime);
    }

    // =========================================================================
    // PUBLIC API สำหรับฝั่ง Monster AI และสคริปต์โจมตีจริง
    // =========================================================================

    /// <summary>
    /// ฝั่ง Monster: ให้สคริปต์ AI ของมอนสเตอร์เรียกเพื่อยกโล่บล็อคค้าง (true) หรือลดโล่ลง (false)
    /// </summary>
    public void SetBlockingState(bool active)
    {
        monsterRequestedBlock = active;
        if (!isGuardBroken)
        {
            isBlocking = active;
        }
        lastStatusMessage = active ? "AI สั่งยกโล่ป้องกันค้างไว้" : "AI ลดโล่ป้องกันลง";
    }

    /// <summary>
    /// ฟังก์ชันหลักสำหรับให้ระบบโจมตีเรียกเมื่อการโจมตีปะทะโดนตัวละครนี้
    /// - คืนค่า true หากกำลังกดบล็อคค้างอยู่ (ป้องกันดาเมจเข้าเกราะ และระเบิดบล็อคแตกทันทีเมื่อเกราะ <= 0)
    /// - คืนค่า false หากไม่ได้กดบล็อคค้างอยู่หรือเกราะแตกไปแล้ว (ไม่เกิดเอฟเฟกต์บล็อคแตก และให้ดาเมจเข้าตัวตามปกติ)
    /// </summary>
    public bool ReceiveIncomingAttack(float guardDamage, Vector3 hitWorldPos, Vector3 attackerForwardDir)
    {
        if (!isBlocking || isGuardBroken)
        {
            return false;
        }

        float dmg = Mathf.Max(0f, guardDamage);
        lastGuardHitTime = Time.time;
        currentGuardHP = Mathf.Max(0f, currentGuardHP - dmg);
        shieldImpactFlash = 1.0f;
        lastEventFlashTime = Time.time;

        OnGuardDamaged?.Invoke(currentGuardHP, maxGuardHP);

        if (currentGuardHP <= 0f)
        {
            TriggerGuardBreak(hitWorldPos, attackerForwardDir);
        }
        else
        {
            lastStatusMessage = $"รับแรงกระแทก -{dmg:0} (เกราะเหลือ {currentGuardHP:0}/{maxGuardHP:0})";
        }

        return true;
    }

    /// <summary>
    /// สั่งระเบิดเอฟเฟกต์ตีบล็อคแตก 4 เลเยอร์ ปิดโล่ลงชั่วคราว สั่นกล้อง และติดสถานะมึนงง (Staggered) 2.0 วินาที
    /// </summary>
    public void TriggerGuardBreak(Vector3 hitWorldPos, Vector3 attackerForwardDir)
    {
        if (isGuardBroken) return;

        isGuardBroken = true;
        isBlocking = false;
        currentGuardHP = 0f;
        staggerTimeRemaining = guardBreakStaggerDuration;
        shieldDeployProgress = 0f;
        lastStatusMessage = $"GUARD BROKEN! มึนงง ({guardBreakStaggerDuration:0.0}s)";
        lastEventFlashTime = Time.time;

        // 1. ปิดโล่พลังงานทันที
        if (activeGuardShieldObject != null)
        {
            activeGuardShieldObject.SetActive(false);
        }

        // 2. คำนวณตำแหน่งและทิศทางหันหน้าออกของเอฟเฟกต์ระเบิดโล่
        Vector3 spawnPos = hitWorldPos != Vector3.zero ? hitWorldPos : GetShieldWorldPosition();
        Vector3 outwardDir = transform.forward;
        if (attackerForwardDir.sqrMagnitude > 0.001f)
        {
            // หันหน้าตัดโล่รับทิศพุ่งชน หรือพุ่งสะท้อนตามหน้าโล่ตัวละคร
            Vector3 flatOpposite = new Vector3(-attackerForwardDir.x, 0f, -attackerForwardDir.z);
            if (flatOpposite.sqrMagnitude > 0.001f)
            {
                outwardDir = flatOpposite.normalized;
            }
        }
        Quaternion spawnRot = Quaternion.LookRotation(outwardDir.sqrMagnitude > 0.001f ? outwardDir : Vector3.forward, Vector3.up);

        // 3. เสก Prefab ตีบล็อคแตก 4 เลเยอร์ (หรือสร้างชุด Runtime 4 เลเยอร์สำรองหากยังไม่ได้ผูก Prefab)
        GameObject fxInstance;
        if (guardBreakShatterPrefab != null)
        {
            fxInstance = Instantiate(guardBreakShatterPrefab, spawnPos, spawnRot);
        }
        else
        {
            fxInstance = BuildRuntime4LayerGuardBreakFallback(spawnPos, spawnRot);
        }

        if (!Mathf.Approximately(shatterVfxScale, 1f))
        {
            fxInstance.transform.localScale = Vector3.one * shatterVfxScale;
        }

        ParticleSystem[] systems = fxInstance.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < systems.Length; i++)
        {
            systems[i].Clear(true);
            systems[i].Play(true);
        }

        Destroy(fxInstance, Mathf.Max(0.8f, shatterAutoDestroyDelay));

        // 4. สั่นกล้องเบาๆ เพิ่มน้ำหนักการปะทะ
        if (enableCameraShake)
        {
            TriggerMicroCameraShake(cameraShakeDuration, cameraShakeMagnitude);
        }

        OnGuardBroken?.Invoke(spawnPos, outwardDir);

        // 5. เริ่มนับเวลาฟื้นฟูเกราะอัตโนมัติหลังพ้นสถานะ Staggered
        if (recoveryCoroutine != null)
        {
            StopCoroutine(recoveryCoroutine);
        }
        recoveryCoroutine = StartCoroutine(RecoverGuardAfterStaggerRoutine());
    }

    private IEnumerator RecoverGuardAfterStaggerRoutine()
    {
        yield return new WaitForSeconds(Mathf.Max(0.2f, guardBreakStaggerDuration));

        isGuardBroken = false;
        staggerTimeRemaining = 0f;
        currentGuardHP = maxGuardHP;
        lastStatusMessage = "ฟื้นฟูเกราะป้องกันเต็ม 100% แล้ว";
        lastEventFlashTime = Time.time;

        if (entityRole == EntityRole.Monster)
        {
            isBlocking = monsterRequestedBlock;
        }

        recoveryCoroutine = null;
        OnGuardRecovered?.Invoke();
    }

    // =========================================================================
    // INPUT DETECTION (รองรับทั้ง New Input System และ Legacy Input Manager)
    // =========================================================================

    /// <summary>
    /// ฝั่ง Player: ตรวจสอบว่ากำลังกดคลิกขวาเมาส์หรือปุ่ม F ค้างไว้หรือไม่
    /// </summary>
    public bool CheckPlayerBlockInput()
    {
        bool held = false;

#if ENABLE_INPUT_SYSTEM
        try
        {
            if (allowRightMouseBlock && Mouse.current != null && Mouse.current.rightButton.isPressed)
            {
                held = true;
            }

            if (!held && Keyboard.current != null)
            {
                if (playerBlockKey == KeyCode.F && Keyboard.current.fKey.isPressed)
                {
                    held = true;
                }
                else if (Enum.TryParse(playerBlockKey.ToString(), true, out Key mappedKey) && mappedKey != Key.None)
                {
                    var keyCtrl = Keyboard.current[mappedKey];
                    if (keyCtrl != null && keyCtrl.isPressed)
                    {
                        held = true;
                    }
                }
            }
        }
        catch (Exception)
        {
        }
#endif

        if (!held)
        {
            try
            {
                if ((allowRightMouseBlock && Input.GetMouseButton(1)) || Input.GetKey(playerBlockKey))
                {
                    held = true;
                }
            }
            catch (InvalidOperationException)
            {
            }
        }

        return held;
    }

    private static bool IsKeyPressedThisFrame(KeyCode keyCode)
    {
        bool pressed = false;

#if ENABLE_INPUT_SYSTEM
        try
        {
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (keyCode == KeyCode.B && kb.bKey.wasPressedThisFrame)
                {
                    pressed = true;
                }
                else if (Enum.TryParse(keyCode.ToString(), true, out Key mappedKey) && mappedKey != Key.None)
                {
                    var keyCtrl = kb[mappedKey];
                    if (keyCtrl != null && keyCtrl.wasPressedThisFrame)
                    {
                        pressed = true;
                    }
                }
            }
        }
        catch (Exception)
        {
        }
#endif

        if (!pressed)
        {
            try
            {
                if (Input.GetKeyDown(keyCode))
                {
                    pressed = true;
                }
            }
            catch (InvalidOperationException)
            {
            }
        }

        return pressed;
    }

    // =========================================================================
    // ACTIVE GUARD SHIELD DOME VISUALS
    // =========================================================================

    private void EnsureActiveGuardShieldSetup()
    {
        if (activeGuardShieldObject == null)
        {
            Transform existing = transform.Find("VFX_ActiveGuardShield");
            if (existing != null)
            {
                activeGuardShieldObject = existing.gameObject;
            }
        }

        if (activeGuardShieldObject == null)
        {
            EnsureFallbackAssets();
            activeGuardShieldObject = new GameObject("VFX_ActiveGuardShield");
            activeGuardShieldObject.transform.SetParent(transform, false);
            activeGuardShieldObject.transform.localPosition = shieldLocalOffset;
            activeGuardShieldObject.transform.localRotation = Quaternion.identity;
            activeGuardShieldObject.transform.localScale = Vector3.one;

            MeshFilter mf = activeGuardShieldObject.AddComponent<MeshFilter>();
            mf.sharedMesh = fallbackShieldDomeMesh;

            MeshRenderer mr = activeGuardShieldObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = fallbackShieldMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sortingOrder = 10;
        }

        shieldBaseLocalScale = activeGuardShieldObject.transform.localScale;
        if (shieldBaseLocalScale.sqrMagnitude < 0.001f)
        {
            shieldBaseLocalScale = Vector3.one;
        }

        shieldRenderer = activeGuardShieldObject.GetComponentInChildren<Renderer>(true);
        if (shieldRenderer != null && shieldRenderer.sharedMaterial != null)
        {
            shieldMaterialInstance = new Material(shieldRenderer.sharedMaterial);
            shieldRenderer.material = shieldMaterialInstance;
            if (shieldMaterialInstance.HasProperty(BaseColorId))
            {
                shieldOriginalColor = shieldMaterialInstance.GetColor(BaseColorId);
            }
            else if (shieldMaterialInstance.HasProperty(ColorId))
            {
                shieldOriginalColor = shieldMaterialInstance.GetColor(ColorId);
            }
        }
    }

    private void UpdateShieldVisualImmediate()
    {
        if (activeGuardShieldObject == null) return;
        bool shouldShow = isBlocking && !isGuardBroken;
        shieldDeployProgress = shouldShow ? 1f : 0f;
        activeGuardShieldObject.SetActive(shouldShow);
        if (shouldShow)
        {
            activeGuardShieldObject.transform.localScale = shieldBaseLocalScale;
        }
    }

    private void UpdateActiveShieldVisual(float dt)
    {
        if (activeGuardShieldObject == null) return;

        bool shouldShow = isBlocking && !isGuardBroken;
        float target = shouldShow ? 1f : 0f;
        shieldDeployProgress = Mathf.MoveTowards(shieldDeployProgress, target, dt * 14f);

        if (shieldImpactFlash > 0f)
        {
            shieldImpactFlash = Mathf.Max(0f, shieldImpactFlash - dt * 6.5f);
        }

        if (shieldDeployProgress <= 0.001f)
        {
            if (activeGuardShieldObject.activeSelf)
            {
                activeGuardShieldObject.SetActive(false);
            }
            return;
        }

        if (!activeGuardShieldObject.activeSelf)
        {
            activeGuardShieldObject.SetActive(true);
        }

        float pulse = 1.0f + Mathf.Sin(Time.time * 4.5f) * 0.018f + shieldImpactFlash * 0.055f;
        activeGuardShieldObject.transform.localScale = shieldBaseLocalScale * (shieldDeployProgress * pulse);

        if (shieldMaterialInstance != null)
        {
            // ยิ่งเกราะใกล้แตก สีโล่จะค่อยๆ ไล่จากสีฟ้าพลังงาน -> เหลืองทอง/ส้มเตือนว่าใกล้แตก
            float damageStress = 1f - GuardRatio;
            Color baseTint = entityRole == EntityRole.Monster
                ? new Color(1.0f, 0.42f, 0.18f, 1.0f) * 2.1f
                : shieldOriginalColor;
            Color warningTint = new Color(1.0f, 0.56f, 0.14f, 1.0f) * 2.6f;
            Color hitTint = new Color(1.0f, 0.94f, 0.72f, 1.0f) * 4.2f;

            Color c = Color.Lerp(baseTint, warningTint, damageStress * 0.85f);
            c = Color.Lerp(c, hitTint, shieldImpactFlash);
            c.a = shieldOriginalColor.a;

            if (shieldMaterialInstance.HasProperty(BaseColorId)) shieldMaterialInstance.SetColor(BaseColorId, c);
            if (shieldMaterialInstance.HasProperty(ColorId)) shieldMaterialInstance.SetColor(ColorId, c);
        }
    }

    public Vector3 GetShieldWorldPosition()
    {
        if (activeGuardShieldObject != null && activeGuardShieldObject.activeInHierarchy)
        {
            return activeGuardShieldObject.transform.position;
        }
        return transform.TransformPoint(shieldLocalOffset);
    }

    // =========================================================================
    // MICRO CAMERA SHAKE (Delta-tracked ใน LateUpdate ไม่ตีกับ PlayerFollowCamera)
    // =========================================================================

    private void TriggerMicroCameraShake(float duration, float magnitude)
    {
        shakeTotalDuration = Mathf.Max(0.05f, duration);
        shakeTimeRemaining = shakeTotalDuration;
        shakeActiveMagnitude = Mathf.Max(0.01f, magnitude);
    }

    private void UpdateCameraShakeLate(float dt)
    {
        if (shakeTimeRemaining <= 0f)
        {
            RestorePendingCameraShake();
            return;
        }

        Camera cam = Camera.main;
        if (cam == null)
        {
            shakeTimeRemaining = 0f;
            lastShakeWorldOffset = Vector3.zero;
            return;
        }

        if (lastShakenCamera != null && lastShakenCamera != cam)
        {
            RestorePendingCameraShake();
        }

        // คืนค่า Offset เฉพาะกรณีที่กล้องยังไม่ได้ถูกรีเซ็ตตำแหน่งโดย PlayerFollowCamera ในเฟรมนี้
        if (lastShakenCamera == cam && lastShakeWorldOffset.sqrMagnitude > 0f)
        {
            if ((cam.transform.position - lastPostShakeCameraPos).sqrMagnitude < 0.0001f)
            {
                cam.transform.position -= lastShakeWorldOffset;
            }
        }

        shakeTimeRemaining = Mathf.Max(0f, shakeTimeRemaining - dt);
        float damper = shakeTotalDuration > 0f ? (shakeTimeRemaining / shakeTotalDuration) : 0f;
        Vector3 newOffset = UnityEngine.Random.insideUnitSphere * (shakeActiveMagnitude * damper * damper);

        cam.transform.position += newOffset;
        lastShakeWorldOffset = newOffset;
        lastPostShakeCameraPos = cam.transform.position;
        lastShakenCamera = cam;
    }

    private void RestorePendingCameraShake()
    {
        if (lastShakenCamera != null && lastShakeWorldOffset.sqrMagnitude > 0f)
        {
            if ((lastShakenCamera.transform.position - lastPostShakeCameraPos).sqrMagnitude < 0.0001f)
            {
                lastShakenCamera.transform.position -= lastShakeWorldOffset;
            }
        }
        lastShakeWorldOffset = Vector3.zero;
        lastShakenCamera = null;
    }

    // =========================================================================
    // OnGUI HUD (แยกตำแหน่งซ้าย/ขวา ไม่ทับกันระหว่าง Player และ Monster)
    // =========================================================================

    private void OnGUI()
    {
        if (!showDebugHUD) return;

        if (hudFrameMarker != Time.frameCount)
        {
            hudFrameMarker = Time.frameCount;
            playerHudDrawIndex = 0;
            monsterHudDrawIndex = 0;
        }

        const float boxW = 380f;
        const float boxH = 142f;
        const float margin = 16f;

        int stackIndex = entityRole == EntityRole.Player ? playerHudDrawIndex++ : monsterHudDrawIndex++;
        float x = entityRole == EntityRole.Player ? margin : (Screen.width - boxW - margin);
        float y = Screen.height - boxH - margin - stackIndex * (boxH + 10f);
        Rect panel = new Rect(x, y, boxW, boxH);

        Color prevBg = GUI.backgroundColor;
        GUI.backgroundColor = entityRole == EntityRole.Player
            ? new Color(0.05f, 0.11f, 0.18f, 0.88f)
            : new Color(0.18f, 0.07f, 0.06f, 0.88f);
        GUI.Box(panel, string.Empty);
        GUI.backgroundColor = prevBg;

        GUIStyle titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, richText = true };
        GUIStyle bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true };

        string roleHeader = entityRole == EntityRole.Player
            ? $"<color=#4DE1FF>[PLAYER GUARD]</color> {gameObject.name}"
            : $"<color=#FF8855>[MONSTER GUARD]</color> {gameObject.name}";

        string stateBadge = isGuardBroken
            ? $"<color=#FF4D4D><b>STAGGERED ({staggerTimeRemaining:0.0}s)</b></color>"
            : (isBlocking
                ? "<color=#5EF2A8><b>BLOCKING (กางโล่ค้าง)</b></color>"
                : "<color=#B8C4D4>IDLE (ไม่ได้บล็อค)</color>");

        GUI.Label(new Rect(panel.x + 12f, panel.y + 8f, boxW - 24f, 22f), roleHeader, titleStyle);
        GUI.Label(new Rect(panel.x + 12f, panel.y + 30f, boxW - 24f, 20f), $"สถานะ: {stateBadge}", bodyStyle);

        // หลอดเกราะ Guard HP Bar
        Rect barBg = new Rect(panel.x + 12f, panel.y + 54f, boxW - 24f, 18f);
        Color prevColor = GUI.color;
        GUI.color = new Color(0.12f, 0.14f, 0.18f, 0.95f);
        GUI.DrawTexture(barBg, Texture2D.whiteTexture);

        float ratio = GuardRatio;
        Color fillCol = isGuardBroken
            ? new Color(0.95f, 0.22f, 0.22f, 1f)
            : Color.Lerp(new Color(1.0f, 0.45f, 0.12f, 1f), new Color(0.22f, 0.85f, 1.0f, 1f), ratio);
        GUI.color = fillCol;
        GUI.DrawTexture(new Rect(barBg.x + 2f, barBg.y + 2f, (barBg.width - 4f) * ratio, barBg.height - 4f), Texture2D.whiteTexture);
        GUI.color = prevColor;

        GUI.Label(new Rect(barBg.x + 6f, barBg.y - 1f, barBg.width - 12f, 20f),
            $"<b>Guard HP: {currentGuardHP:0} / {maxGuardHP:0}</b>", bodyStyle);

        string msgColor = (Time.time - lastEventFlashTime < 0.9f) ? "#FFE066" : "#DCE6F2";
        GUI.Label(new Rect(panel.x + 12f, panel.y + 78f, boxW - 24f, 22f),
            $"<color={msgColor}>{lastStatusMessage}</color>", bodyStyle);

        string hint = entityRole == EntityRole.Player
            ? $"<color=#FFE066>กด [คลิกขวา / {playerBlockKey}] ค้างเพื่อกางโล่ + กด [{testHitKey}] 3 ครั้งให้แตก</color>"
            : $"<color=#FFE066>มอนสเตอร์ยกโล่ผ่าน SetBlockingState(true) + กด [{testHitKey}] 3 ครั้งให้แตก</color>";
        GUI.Label(new Rect(panel.x + 12f, panel.y + 106f, boxW - 24f, 24f), hint, bodyStyle);
    }

    // =========================================================================
    // RUNTIME SELF-HEALING 4-LAYER FALLBACK (ทำงานได้ทันทีแม้ยังไม่ได้ลาก Prefab)
    // =========================================================================

    private static GameObject BuildRuntime4LayerGuardBreakFallback(Vector3 position, Quaternion rotation)
    {
        EnsureFallbackAssets();

        GameObject root = new GameObject("VFX_UniversalGuardBreak_Runtime");
        root.transform.SetPositionAndRotation(position, rotation);

        // Layer 1: Overload Crack Web Flash (0.00s - 0.24s, Mesh Quad Local, Start Size 2.4, HDR 4.8x)
        ParticleSystem l1 = CreateChildPS(root.transform, "L1_OverloadCrackWeb");
        var m1 = l1.main;
        m1.duration = 0.30f;
        m1.loop = false;
        m1.playOnAwake = true;
        m1.startDelay = 0.00f;
        m1.startLifetime = 0.24f;
        m1.startSpeed = 0f;
        m1.startSize = 2.4f;
        m1.startColor = Color.white; // HDR 4.8x ขับผ่าน Material._BaseColor
        m1.simulationSpace = ParticleSystemSimulationSpace.Local;
        m1.scalingMode = ParticleSystemScalingMode.Hierarchy;
        m1.maxParticles = 4;

        var e1 = l1.emission;
        e1.rateOverTime = 0f;
        e1.SetBursts(new[] { new ParticleSystem.Burst(0.00f, 1) });
        var sh1 = l1.shape;
        sh1.enabled = false;

        var col1 = l1.colorOverLifetime;
        col1.enabled = true;
        Gradient g1 = new Gradient();
        g1.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1.00f, 0.84f, 0.24f), 0.00f),
                new GradientColorKey(new Color(1.00f, 0.96f, 0.78f), 0.22f),
                new GradientColorKey(new Color(0.18f, 0.88f, 1.00f), 0.62f),
                new GradientColorKey(new Color(0.06f, 0.56f, 1.00f), 1.00f)
            },
            new[]
            {
                new GradientAlphaKey(0.00f, 0.00f),
                new GradientAlphaKey(1.00f, 0.09f),
                new GradientAlphaKey(1.00f, 0.50f),
                new GradientAlphaKey(0.00f, 1.00f)
            });
        col1.color = new ParticleSystem.MinMaxGradient(g1);

        var sol1 = l1.sizeOverLifetime;
        sol1.enabled = true;
        sol1.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0.00f, 0.60f, 3.8f, 3.8f),
            new Keyframe(0.18f, 1.00f, 0.45f, 0.45f),
            new Keyframe(1.00f, 1.18f, 0.12f, 0.12f)));

        var r1 = l1.GetComponent<ParticleSystemRenderer>();
        r1.renderMode = ParticleSystemRenderMode.Mesh;
        r1.mesh = fallbackQuadMesh;
        r1.alignment = ParticleSystemRenderSpace.Local;
        r1.sharedMaterial = fallbackCrackMat;
        r1.shadowCastingMode = ShadowCastingMode.Off;
        r1.receiveShadows = false;
        r1.sortingOrder = 10;

        // Layer 2: 3D Shattered Shield Shards (0.02s - 0.95s, Burst 26, World, 3D Rot -360..+360, Gravity 1.65)
        ParticleSystem l2 = CreateChildPS(root.transform, "L2_ShatteredShieldShards3D");
        var m2 = l2.main;
        m2.duration = 1.00f;
        m2.loop = false;
        m2.playOnAwake = true;
        m2.startDelay = 0.02f;
        m2.startLifetime = new ParticleSystem.MinMaxCurve(0.68f, 0.93f);
        m2.startSpeed = new ParticleSystem.MinMaxCurve(4.6f, 10.4f);
        m2.startSize = new ParticleSystem.MinMaxCurve(0.24f, 0.52f);
        m2.startColor = Color.white; // HDR 3.2x ขับผ่าน Material._BaseColor
        m2.gravityModifier = 1.65f;
        m2.simulationSpace = ParticleSystemSimulationSpace.World;
        m2.scalingMode = ParticleSystemScalingMode.Hierarchy;
        m2.maxParticles = 40;
        m2.startRotation3D = true;
        m2.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        m2.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        m2.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

        var e2 = l2.emission;
        e2.rateOverTime = 0f;
        e2.SetBursts(new[] { new ParticleSystem.Burst(0.00f, 26) });

        var sh2 = l2.shape;
        sh2.enabled = true;
        sh2.shapeType = ParticleSystemShapeType.Circle;
        sh2.radius = 0.78f;
        sh2.radiusThickness = 1.0f;
        sh2.randomDirectionAmount = 0.35f;

        var vol2 = l2.velocityOverLifetime;
        vol2.enabled = true;
        vol2.space = ParticleSystemSimulationSpace.Local;
        vol2.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        vol2.y = new ParticleSystem.MinMaxCurve(0.6f, 1.8f);
        vol2.z = new ParticleSystem.MinMaxCurve(1.4f, 3.2f);

        var rol2 = l2.rotationOverLifetime;
        rol2.enabled = true;
        rol2.separateAxes = true;
        float rad360 = 360f * Mathf.Deg2Rad;
        rol2.x = new ParticleSystem.MinMaxCurve(-rad360, rad360);
        rol2.y = new ParticleSystem.MinMaxCurve(-rad360, rad360);
        rol2.z = new ParticleSystem.MinMaxCurve(-rad360, rad360);

        var col2 = l2.colorOverLifetime;
        col2.enabled = true;
        Gradient g2 = new Gradient();
        g2.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.76f, 0.98f, 1.00f), 0.00f),
                new GradientColorKey(new Color(0.22f, 0.80f, 1.00f), 0.42f),
                new GradientColorKey(new Color(0.08f, 0.38f, 0.90f), 1.00f)
            },
            new[]
            {
                new GradientAlphaKey(1.00f, 0.00f),
                new GradientAlphaKey(1.00f, 0.68f),
                new GradientAlphaKey(0.00f, 1.00f)
            });
        col2.color = new ParticleSystem.MinMaxGradient(g2);

        var sol2 = l2.sizeOverLifetime;
        sol2.enabled = true;
        sol2.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0.00f, 1.00f),
            new Keyframe(0.66f, 0.92f),
            new Keyframe(1.00f, 0.14f)));

        var r2 = l2.GetComponent<ParticleSystemRenderer>();
        r2.renderMode = ParticleSystemRenderMode.Mesh;
        r2.mesh = fallbackShardMesh;
        r2.sharedMaterial = fallbackShardMat;
        r2.shadowCastingMode = ShadowCastingMode.Off;
        r2.receiveShadows = false;
        r2.sortingOrder = 11;

        // Layer 3: High-Velocity Shatter Sparks (0.02s - 0.48s, Stretched Billboard World, Vel 0.06, Len 3.8, Burst 24)
        ParticleSystem l3 = CreateChildPS(root.transform, "L3_ShatterSparks");
        var m3 = l3.main;
        m3.duration = 0.55f;
        m3.loop = false;
        m3.playOnAwake = true;
        m3.startDelay = 0.02f;
        m3.startLifetime = new ParticleSystem.MinMaxCurve(0.24f, 0.46f);
        m3.startSpeed = new ParticleSystem.MinMaxCurve(7.5f, 15.5f);
        m3.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.17f);
        m3.startColor = Color.white; // HDR 4.5x ขับผ่าน Material._BaseColor
        m3.gravityModifier = 0.95f;
        m3.simulationSpace = ParticleSystemSimulationSpace.World;
        m3.scalingMode = ParticleSystemScalingMode.Hierarchy;
        m3.maxParticles = 40;

        var e3 = l3.emission;
        e3.rateOverTime = 0f;
        e3.SetBursts(new[] { new ParticleSystem.Burst(0.00f, 24) });

        var sh3 = l3.shape;
        sh3.enabled = true;
        sh3.shapeType = ParticleSystemShapeType.Cone;
        sh3.angle = 68f;
        sh3.radius = 0.45f;

        var col3 = l3.colorOverLifetime;
        col3.enabled = true;
        Gradient g3 = new Gradient();
        g3.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1.00f, 0.94f, 0.62f), 0.00f),
                new GradientColorKey(new Color(1.00f, 0.45f, 0.06f), 0.34f),
                new GradientColorKey(new Color(0.92f, 0.15f, 0.02f), 1.00f)
            },
            new[]
            {
                new GradientAlphaKey(1.00f, 0.00f),
                new GradientAlphaKey(0.92f, 0.42f),
                new GradientAlphaKey(0.00f, 1.00f)
            });
        col3.color = new ParticleSystem.MinMaxGradient(g3);

        var r3 = l3.GetComponent<ParticleSystemRenderer>();
        r3.renderMode = ParticleSystemRenderMode.Stretch;
        r3.velocityScale = 0.06f;
        r3.lengthScale = 3.8f;
        r3.sharedMaterial = fallbackSparkMat;
        r3.shadowCastingMode = ShadowCastingMode.Off;
        r3.receiveShadows = false;
        r3.sortingOrder = 12;

        // Layer 4: Guard Break Shockwave Ring (0.03s - 0.34s, Mesh Quad Local, Start Size 3.4, HDR 3.8x)
        ParticleSystem l4 = CreateChildPS(root.transform, "L4_GuardBreakShockwave");
        var m4 = l4.main;
        m4.duration = 0.40f;
        m4.loop = false;
        m4.playOnAwake = true;
        m4.startDelay = 0.03f;
        m4.startLifetime = 0.31f;
        m4.startSpeed = 0f;
        m4.startSize = 3.4f;
        m4.startColor = Color.white; // HDR 3.8x ขับผ่าน Material._BaseColor
        m4.simulationSpace = ParticleSystemSimulationSpace.Local;
        m4.scalingMode = ParticleSystemScalingMode.Hierarchy;
        m4.maxParticles = 4;

        var e4 = l4.emission;
        e4.rateOverTime = 0f;
        e4.SetBursts(new[] { new ParticleSystem.Burst(0.00f, 1) });
        var sh4 = l4.shape;
        sh4.enabled = false;

        var col4 = l4.colorOverLifetime;
        col4.enabled = true;
        Gradient g4 = new Gradient();
        g4.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.68f, 0.98f, 1.00f), 0.00f),
                new GradientColorKey(new Color(0.16f, 0.78f, 1.00f), 0.36f),
                new GradientColorKey(new Color(0.06f, 0.42f, 0.96f), 1.00f)
            },
            new[]
            {
                new GradientAlphaKey(1.00f, 0.00f),
                new GradientAlphaKey(0.85f, 0.30f),
                new GradientAlphaKey(0.00f, 1.00f)
            });
        col4.color = new ParticleSystem.MinMaxGradient(g4);

        var sol4 = l4.sizeOverLifetime;
        sol4.enabled = true;
        sol4.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0.00f, 0.18f, 3.4f, 3.4f),
            new Keyframe(0.36f, 0.86f, 0.95f, 0.95f),
            new Keyframe(1.00f, 1.24f, 0.14f, 0.14f)));

        var r4 = l4.GetComponent<ParticleSystemRenderer>();
        r4.renderMode = ParticleSystemRenderMode.Mesh;
        r4.mesh = fallbackQuadMesh;
        r4.alignment = ParticleSystemRenderSpace.Local;
        r4.sharedMaterial = fallbackWaveMat;
        r4.shadowCastingMode = ShadowCastingMode.Off;
        r4.receiveShadows = false;
        r4.sortingOrder = 9;

        return root;
    }

    private static ParticleSystem CreateChildPS(Transform parent, string childName)
    {
        GameObject go = new GameObject(childName);
        go.transform.SetParent(parent, false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        return ps;
    }

    private static void EnsureFallbackAssets()
    {
        if (fallbackQuadMesh == null) fallbackQuadMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        if (fallbackShardMesh == null) fallbackShardMesh = BuildShardMeshRuntime();
        if (fallbackShieldDomeMesh == null) fallbackShieldDomeMesh = BuildShieldDomeMeshRuntime();

        if (fallbackHexTex == null) fallbackHexTex = BuildHexBarrierTexRuntime(256);
        if (fallbackCrackTex == null) fallbackCrackTex = BuildCrackWebTexRuntime(256);
        if (fallbackGlowTex == null) fallbackGlowTex = BuildShardGlowTexRuntime(128);
        if (fallbackWaveTex == null) fallbackWaveTex = BuildShockwaveTexRuntime(256);

        if (fallbackShieldMat == null) fallbackShieldMat = CreateURPAdditiveMaterial("Mat_Runtime_UniversalShield", fallbackHexTex, new Color(0.28f, 0.88f, 1.0f, 0.85f) * 2.2f);
        if (fallbackCrackMat == null) fallbackCrackMat = CreateURPAdditiveMaterial("Mat_Runtime_UniversalCrack", fallbackCrackTex, new Color(4.8f, 4.8f, 4.8f, 1f));
        if (fallbackShardMat == null) fallbackShardMat = CreateURPAdditiveMaterial("Mat_Runtime_UniversalShard", fallbackGlowTex, new Color(3.2f, 3.2f, 3.2f, 1f));
        if (fallbackSparkMat == null) fallbackSparkMat = CreateURPAdditiveMaterial("Mat_Runtime_UniversalSpark", fallbackGlowTex, new Color(4.5f, 4.5f, 4.5f, 1f));
        if (fallbackWaveMat == null) fallbackWaveMat = CreateURPAdditiveMaterial("Mat_Runtime_UniversalWave", fallbackWaveTex, new Color(3.8f, 3.8f, 3.8f, 1f));
    }

    private static Mesh BuildShardMeshRuntime()
    {
        Mesh m = new Mesh { name = "Mesh_UniversalShard3D_Runtime" };
        m.vertices = new[]
        {
            new Vector3( 0.00f,  0.58f,  0.00f),
            new Vector3(-0.44f, -0.38f,  0.00f),
            new Vector3( 0.48f, -0.30f,  0.00f),
            new Vector3( 0.02f, -0.02f,  0.14f),
            new Vector3( 0.02f, -0.02f, -0.14f)
        };
        m.triangles = new[] { 0, 1, 3, 1, 2, 3, 2, 0, 3, 0, 4, 1, 1, 4, 2, 2, 4, 0 };
        m.uv = new[]
        {
            new Vector2(0.50f, 0.96f),
            new Vector2(0.08f, 0.10f),
            new Vector2(0.94f, 0.16f),
            new Vector2(0.50f, 0.50f),
            new Vector2(0.50f, 0.50f)
        };
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    private static Mesh BuildShieldDomeMeshRuntime()
    {
        Mesh m = new Mesh { name = "Mesh_UniversalShieldDome_Runtime" };
        const int rings = 10;
        const int segs = 28;
        const float radius = 0.78f;
        const float bulgeDepth = 0.24f;

        Vector3[] verts = new Vector3[1 + rings * segs];
        Vector2[] uvs = new Vector2[verts.Length];
        int[] tris = new int[segs * 3 + (rings - 1) * segs * 6];

        verts[0] = new Vector3(0f, 0f, bulgeDepth);
        uvs[0] = new Vector2(0.5f, 0.5f);

        int vIdx = 1;
        for (int r = 1; r <= rings; r++)
        {
            float t = r / (float)rings;
            float z = Mathf.Cos(t * Mathf.PI * 0.5f) * bulgeDepth;
            float ringRad = t * radius;
            for (int s = 0; s < segs; s++)
            {
                float ang = (s / (float)segs) * Mathf.PI * 2f;
                float cx = Mathf.Cos(ang);
                float sy = Mathf.Sin(ang);
                verts[vIdx] = new Vector3(cx * ringRad, sy * ringRad, z);
                uvs[vIdx] = new Vector2(0.5f + cx * t * 0.5f, 0.5f + sy * t * 0.5f);
                vIdx++;
            }
        }

        int tIdx = 0;
        for (int s = 0; s < segs; s++)
        {
            int next = (s + 1) % segs;
            tris[tIdx++] = 0;
            tris[tIdx++] = 1 + next;
            tris[tIdx++] = 1 + s;
        }
        for (int r = 1; r < rings; r++)
        {
            int ringStart = 1 + (r - 1) * segs;
            int nextRingStart = 1 + r * segs;
            for (int s = 0; s < segs; s++)
            {
                int next = (s + 1) % segs;
                tris[tIdx++] = ringStart + s;
                tris[tIdx++] = ringStart + next;
                tris[tIdx++] = nextRingStart + s;
                tris[tIdx++] = ringStart + next;
                tris[tIdx++] = nextRingStart + next;
                tris[tIdx++] = nextRingStart + s;
            }
        }

        m.vertices = verts;
        m.uv = uvs;
        m.triangles = tris;
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    private static Texture2D BuildHexBarrierTexRuntime(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] px = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            float ny = (y / (float)(size - 1)) * 2f - 1f;
            for (int x = 0; x < size; x++)
            {
                float nx = (x / (float)(size - 1)) * 2f - 1f;
                float r = Mathf.Sqrt(nx * nx + ny * ny);
                if (r >= 0.99f) { px[y * size + x] = Color.clear; continue; }

                float rim = Mathf.Exp(-Mathf.Pow((r - 0.88f) / 0.045f, 2f)) * 1.1f;
                float outerFade = 1f - SmoothStep(0.90f, 0.98f, r);
                float hx = nx * 6.5f;
                float hy = ny * 6.5f;
                float hexWave = Mathf.Abs(Mathf.Sin(hx * 1.732f) * Mathf.Cos(hy) + Mathf.Sin(hy * 2f) * 0.5f);
                float hexGrid = SmoothStep(0.72f, 0.96f, hexWave) * 0.55f;
                float fill = (0.16f + hexGrid + rim) * outerFade;
                float v = Mathf.Clamp01(fill);
                px[y * size + x] = new Color(v, v, v, v);
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    private static Texture2D BuildCrackWebTexRuntime(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        const int rays = 11;
        float[] angs = new float[rays];
        for (int i = 0; i < rays; i++)
            angs[i] = (i / (float)rays) * Mathf.PI * 2f + (Mathf.PerlinNoise(i * 3.71f, 4.19f) - 0.5f) * 0.28f;

        Color[] px = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            float ny = (y / (float)(size - 1)) * 2f - 1f;
            for (int x = 0; x < size; x++)
            {
                float nx = (x / (float)(size - 1)) * 2f - 1f;
                float r = Mathf.Sqrt(nx * nx + ny * ny);
                if (r >= 0.99f) { px[y * size + x] = Color.clear; continue; }

                float ang = Mathf.Atan2(ny, nx);
                if (ang < 0f) ang += Mathf.PI * 2f;
                float edgeMask = 1f - SmoothStep(0.76f, 0.98f, r);
                float jag = (Mathf.PerlinNoise(nx * 5.2f + 11.4f, ny * 5.2f + 3.7f) - 0.5f) * 0.24f;
                float crack = 0f;
                for (int i = 0; i < rays; i++)
                {
                    float dRad = Mathf.Abs(Mathf.DeltaAngle(ang * Mathf.Rad2Deg, (angs[i] + jag * r) * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
                    float dist = dRad * Mathf.Max(r, 0.04f);
                    float thick = Mathf.Lerp(0.026f, 0.008f, r);
                    crack = Mathf.Max(crack, Mathf.Exp(-(dist * dist) / (thick * thick)));
                }
                float r1 = Mathf.Exp(-Mathf.Pow((r - 0.34f) / 0.016f, 2f)) * 0.90f;
                float r2 = Mathf.Exp(-Mathf.Pow((r - 0.64f) / 0.014f, 2f)) * 0.76f;
                float core = Mathf.Exp(-(r * r) / (0.14f * 0.14f)) * 1.1f;
                float val = Mathf.Clamp01(Mathf.Max(Mathf.Max(crack, core), Mathf.Max(r1, r2)) * edgeMask);
                px[y * size + x] = new Color(val, val, val, val);
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    private static Texture2D BuildShardGlowTexRuntime(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] px = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            float ny = (y / (float)(size - 1)) * 2f - 1f;
            for (int x = 0; x < size; x++)
            {
                float nx = (x / (float)(size - 1)) * 2f - 1f;
                float inv = Mathf.Clamp01(1f - Mathf.Sqrt(nx * nx + ny * ny));
                float v = Mathf.Clamp01(Mathf.Pow(inv, 1.85f) * 0.72f + Mathf.Pow(inv, 4.5f) * 0.48f);
                px[y * size + x] = new Color(v, v, v, v);
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    private static Texture2D BuildShockwaveTexRuntime(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] px = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            float ny = (y / (float)(size - 1)) * 2f - 1f;
            for (int x = 0; x < size; x++)
            {
                float nx = (x / (float)(size - 1)) * 2f - 1f;
                float r = Mathf.Sqrt(nx * nx + ny * ny);
                if (r >= 0.98f) { px[y * size + x] = Color.clear; continue; }
                float outer = 1f - SmoothStep(0.84f, 0.94f, r);
                float inner = SmoothStep(0.50f, 0.80f, r);
                float rim = Mathf.Exp(-Mathf.Pow((r - 0.82f) / 0.042f, 2f));
                float v = Mathf.Clamp01((inner * outer * 0.72f + rim * 0.96f) * outer);
                px[y * size + x] = new Color(v, v, v, v);
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    private static Material CreateURPAdditiveMaterial(string name, Texture2D mainTex, Color hdrColor)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        Material mat = new Material(shader) { name = name };
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", mainTex);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", mainTex);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", hdrColor);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", hdrColor);
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 2f);
        if (mat.HasProperty("_ColorMode")) mat.SetFloat("_ColorMode", 0f);
        if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)BlendMode.One);
        if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)CullMode.Off);

        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.EnableKeyword("_BLENDMODE_ADD");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.renderQueue = (int)RenderQueue.Transparent;
        return mat;
    }

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp01((x - edge0) / Mathf.Max(0.0001f, edge1 - edge0));
        return t * t * (3f - 2f * t);
    }
}
