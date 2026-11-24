using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// ระบบควบคุมและเรียกใช้งานเอฟเฟกต์ "ตีบล็อคแตก / โล่พลังงานแตกละเอียดระดับ AAA (Guard Break & Energy Shield Shatter VFX)"
/// รองรับ Unity 6 (URP - Universal Render Pipeline) เต็มรูปแบบ
/// - เรียกใช้งานจากสคริปต์ต่อสู้ได้ทันทีใน 1 บรรทัดผ่าน MMOGuardBreakVFX.SpawnGuardBreak(...)
/// - ปิดม่านโล่ป้องกัน (guardBarrierGroup) บน MMOBlockParryController อัตโนมัติเมื่อเกราะแตก
/// - สั่นกล้องจังหวะปะทะ (MicroCameraShake 0.18 วินาที) และทำลาย Prefab อัตโนมัติใน 2.0 วินาที
/// - รองรับปุ่มทดสอบ [B] ทั้ง New Input System และ Legacy Input Manager พร้อมกล่องสถานะ OnGUI มุมซ้ายล่าง
/// </summary>
[AddComponentMenu("yeen VFX/03B. [Player] ตีบล็อคแตกโล่กระจาย 4 เลเยอร์ (Guard Break Shatter VFX)")]
[DisallowMultipleComponent]
[DefaultExecutionOrder(200)]
public class MMOGuardBreakVFX : MonoBehaviour
{
    public static MMOGuardBreakVFX Instance { get; private set; }

    [Header("=== 1. พรีแฟบเอฟเฟกต์ตีบล็อคแตก (Guard Break Prefab) ===")]
    [Tooltip("Prefab เอฟเฟกต์ตีบล็อคแตก 4 เลเยอร์ (VFX_GuardBreakShatter.prefab) สร้างอัตโนมัติจากเมนู VFX Mentor/16")]
    public GameObject guardBreakPrefab;

    [Tooltip("ขนาดพื้นฐานของเอฟเฟกต์เมื่อไม่ได้ส่งค่า customScale")]
    [Min(0.1f)]
    public float defaultScale = 1.0f;

    [Tooltip("เวลาทำลาย GameObject ของเอฟเฟกต์อัตโนมัติ (วินาที)")]
    [Min(0.5f)]
    public float autoDestroyDelay = 2.0f;

    [Header("=== 2. จุดเกิดเอฟเฟกต์และระบบสั่งปิดโล่ป้องกัน (Shield Offset & Integration) ===")]
    [Tooltip("ระยะเยื้องหน้าตัวละครเมื่อทดสอบกดปุ่ม B (กรณีไม่พบ guardVfxMount บน MMOBlockParryController)")]
    public Vector3 fallbackShieldLocalOffset = new Vector3(0f, 1.18f, 0.90f);

    [Tooltip("สั่งปิด guardBarrierGroup บน MMOBlockParryController อัตโนมัติเมื่อเกิด Guard Break")]
    public bool autoDisableGuardBarrier = true;

    [Tooltip("รัศมีค้นหา MMOBlockParryController รอบจุดปะทะ (เมตร) เมื่อเรียกผ่าน Static API")]
    [Min(0.5f)]
    public float barrierSearchRadius = 2.5f;

    [Header("=== 3. ระบบสั่นกล้องจังหวะเกราะแตก (Micro Camera Shake) ===")]
    [Tooltip("เปิดการสั่นกล้องเบาๆ ตอนโล่แตกเพื่อเพิ่มความหนักแน่นของจังหวะ Stagger")]
    public bool enableCameraShake = true;

    [Tooltip("ระยะเวลาสั่นกล้อง (ค่ามาตรฐาน AAA = 0.18 วินาที)")]
    [Range(0.05f, 0.5f)]
    public float cameraShakeDuration = 0.18f;

    [Tooltip("ความแรงของการสั่นกล้อง")]
    [Range(0.01f, 0.5f)]
    public float cameraShakeMagnitude = 0.14f;

    [Header("=== 4. ปุ่มทดสอบและหน้าต่างสถานะ (Testing & OnGUI Status) ===")]
    [Tooltip("อนุญาตให้กดปุ่ม B เพื่อทดสอบเอฟเฟกต์ตีบล็อคแตกทันทีในโหมด Play")]
    public bool enableTestHotkey = true;

    [Tooltip("ปุ่มคีย์บอร์ดสำหรับทดสอบ (ค่าเริ่มต้น = B)")]
    public KeyCode testKey = KeyCode.B;

    [Tooltip("แสดงหน้าต่าง OnGUI แสดงสถานะมุมซ้ายล่างของหน้าจอ")]
    public bool showDebugHUD = true;

    [Tooltip("ระบุว่าเป็นตัวละครของผู้เล่นเครื่องนี้ (ในโหมด Multiplayer ตัวละครผู้เล่นอื่นจะปิดอัตโนมัติไม่ให้กดปุ่ม B ชนกัน)")]
    public bool isLocalPlayerOwner = true;

    // Runtime state tracking
    private MMOBlockParryController cachedBlockController;
    private Coroutine activeShakeCoroutine;
    private float shakeTimeRemaining = 0f;
    private float shakeTotalDuration = 0.18f;
    private float shakeActiveMagnitude = 0.14f;
    private Vector3 lastShakeWorldOffset = Vector3.zero;
    private Vector3 lastPostShakeCameraPos = Vector3.zero;
    private Camera lastShakenCamera;

    private static int totalGuardBreakCount = 0;
    private static float lastTriggerTime = -99f;
    private static bool lastDisabledShield = false;

    // Cached runtime fallback assets (ใช้กรณีที่ยังไม่ได้ลาก Prefab ใส่ช่อง Inspector)
    private static Mesh fallbackShardMesh;
    private static Mesh fallbackQuadMesh;
    private static Texture2D fallbackCrackTex;
    private static Texture2D fallbackGlowTex;
    private static Texture2D fallbackWaveTex;
    private static Material fallbackCrackMat;
    private static Material fallbackShardMat;
    private static Material fallbackSparkMat;
    private static Material fallbackWaveMat;

    private void Awake()
    {
        if (Instance == null || Instance == this)
        {
            Instance = this;
        }

        cachedBlockController = GetComponent<MMOBlockParryController>();
        if (cachedBlockController == null) cachedBlockController = GetComponentInParent<MMOBlockParryController>();
        if (cachedBlockController == null) cachedBlockController = GetComponentInChildren<MMOBlockParryController>(true);
    }

    private void OnEnable()
    {
        if (Instance == null)
        {
            Instance = this;
        }
    }

    private void OnDisable()
    {
        RestoreCameraShakeOffset();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void SetAsPrimaryLocalController()
    {
        isLocalPlayerOwner = true;
        Instance = this;
    }

    private void Update()
    {
        // กรณีกล้องอยู่กับที่ (ไม่ได้มีสคริปต์ PlayerFollowCamera เขียนทับตำแหน่งใน LateUpdate)
        // ให้คืน offset ของเฟรมก่อนออกก่อนจะเริ่มอัปเดตเฟรมใหม่ เพื่อไม่ให้กล้องเลื่อนสะสม
        RestoreStaticCameraOffsetIfUnmoved();

        if (!isLocalPlayerOwner || !enableTestHotkey) return;

        if (IsTestKeyPressedThisFrame())
        {
            TriggerGuardBreakOnSelf();
        }
    }

    private void LateUpdate()
    {
        // รันที่ DefaultExecutionOrder(200) หลัง PlayerFollowCamera.LateUpdate()
        // เพื่อให้แรงสั่น MicroCameraShake ปรากฏชัดเจนไม่ถูกกล้องตามตัวละครเขียนทับ
        if (shakeTimeRemaining <= 0f) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        float normalizedElapsed = 1f - Mathf.Clamp01(shakeTimeRemaining / Mathf.Max(0.001f, shakeTotalDuration));
        float damper = (1f - normalizedElapsed) * (1f - normalizedElapsed);
        float shakeX = (Mathf.PerlinNoise(Time.unscaledTime * 68f, 1.7f) * 2f - 1f) * shakeActiveMagnitude * damper;
        float shakeY = (Mathf.PerlinNoise(7.3f, Time.unscaledTime * 68f) * 2f - 1f) * shakeActiveMagnitude * damper;
        float shakeZ = (Mathf.PerlinNoise(Time.unscaledTime * 54f, 13.1f) * 2f - 1f) * shakeActiveMagnitude * 0.35f * damper;

        Vector3 worldOffset = cam.transform.right * shakeX + cam.transform.up * shakeY + cam.transform.forward * shakeZ;
        cam.transform.position += worldOffset;

        lastShakenCamera = cam;
        lastShakeWorldOffset = worldOffset;
        lastPostShakeCameraPos = cam.transform.position;
    }

    /// <summary>
    /// ฟังก์ชัน Static Public หลักสำหรับให้ระบบต่อสู้เรียกใช้งานได้ทันทีเมื่อตีเกราะศัตรูแตกหรือผู้เล่นโดนตีบล็อคแตก
    /// ตัวอย่าง: MMOGuardBreakVFX.SpawnGuardBreak(target.position + Vector3.up * 1.15f, target.forward);
    /// </summary>
    /// <param name="worldPosition">ตำแหน่งโลกตรงหน้าโล่หรือจุดปะทะ</param>
    /// <param name="forwardDirection">ทิศทางพุ่งกระจายของเศษโล่และคลื่นกระแทก</param>
    /// <param name="customScale">ขนาดปรับแต่งพิเศษ (ใส่ -1f เพื่อใช้ค่าเริ่มต้น defaultScale)</param>
    public static GameObject SpawnGuardBreak(Vector3 worldPosition, Vector3 forwardDirection, float customScale = -1f)
    {
        MMOGuardBreakVFX manager = ResolveOrCreateManager();
        return manager.PlayGuardBreakInternal(worldPosition, forwardDirection, customScale, null, true);
    }

    /// <summary>
    /// Overload สำหรับโหมด Multiplayer เพื่อควบคุมการสั่นกล้องเฉพาะผู้เล่นที่โดนตีเกราะแตกจริง
    /// </summary>
    public static GameObject SpawnGuardBreak(Vector3 worldPosition, Vector3 forwardDirection, float customScale, bool triggerCameraShake)
    {
        MMOGuardBreakVFX manager = ResolveOrCreateManager();
        return manager.PlayGuardBreakInternal(worldPosition, forwardDirection, customScale, null, triggerCameraShake);
    }

    /// <summary>
    /// เรียกใช้ Guard Break บนตัวละครที่คอมโพเนนต์นี้ติดอยู่โดยตรง (ใช้ตอนทดสอบปุ่ม B หรือเรียกจากคอมโพเนนต์เดียวกัน)
    /// </summary>
    public GameObject TriggerGuardBreakOnSelf(float customScale = -1f)
    {
        Vector3 spawnPos = ComputeSelfShieldWorldPosition();
        Vector3 forwardDir = transform.forward;
        return PlayGuardBreakInternal(spawnPos, forwardDir, customScale, cachedBlockController, true);
    }

    private GameObject PlayGuardBreakInternal(
        Vector3 worldPosition,
        Vector3 forwardDirection,
        float customScale,
        MMOBlockParryController explicitBlockController,
        bool allowCameraShakeForCall)
    {
        // 1. คำนวณทิศทางหันหน้าเอฟเฟกต์ตามแนวปะทะด้วย Quaternion.LookRotation
        Vector3 safeForward = forwardDirection.sqrMagnitude > 0.0001f
            ? forwardDirection.normalized
            : (transform.forward.sqrMagnitude > 0.0001f ? transform.forward.normalized : Vector3.forward);
        Quaternion spawnRotation = Quaternion.LookRotation(safeForward, Vector3.up);

        // 2. ตรวจสอบและสั่งปิด guardBarrierGroup.SetActive(false) อัตโนมัติหากมี MMOBlockParryController
        lastDisabledShield = false;
        if (autoDisableGuardBarrier)
        {
            MMOBlockParryController targetBlockCtrl = explicitBlockController != null
                ? explicitBlockController
                : FindNearestBlockController(worldPosition, barrierSearchRadius);

            if (targetBlockCtrl != null)
            {
                if (targetBlockCtrl.guardBarrierGroup != null)
                {
                    targetBlockCtrl.guardBarrierGroup.SetActive(false);
                    lastDisabledShield = true;
                }
                targetBlockCtrl.SetGuardStanceActive(false);
            }
        }

        // 3. เสก Prefab 4 เลเยอร์ (หรือสร้างชุด Runtime 4 เลเยอร์สำรองหากยังไม่ได้ผูก Prefab)
        GameObject fxInstance;
        if (guardBreakPrefab != null)
        {
            fxInstance = Instantiate(guardBreakPrefab, worldPosition, spawnRotation);
        }
        else
        {
            fxInstance = BuildRuntimeGuardBreakFallback(worldPosition, spawnRotation);
        }

        // 4. ปรับขนาดตาม customScale (ถ้าส่งค่า > 0)
        float finalScale = customScale > 0f ? customScale : defaultScale;
        if (!Mathf.Approximately(finalScale, 1f))
        {
            fxInstance.transform.localScale = Vector3.one * finalScale;
        }

        // สั่งเล่น ParticleSystem ทุกเลเยอร์ให้มั่นใจว่าเริ่มจากเฟรม 0
        ParticleSystem[] systems = fxInstance.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < systems.Length; i++)
        {
            systems[i].Clear(true);
            systems[i].Play(true);
        }

        // 5. สั่นกล้องเบาๆ 0.18 วินาที (MicroCameraShake)
        if (enableCameraShake && allowCameraShakeForCall && isActiveAndEnabled)
        {
            if (activeShakeCoroutine != null)
            {
                StopCoroutine(activeShakeCoroutine);
                RestoreCameraShakeOffset();
            }
            activeShakeCoroutine = StartCoroutine(MicroCameraShake(cameraShakeDuration, cameraShakeMagnitude));
        }

        // 6. ทำลาย Prefab อัตโนมัติใน 2.0 วินาที
        Destroy(fxInstance, Mathf.Max(0.5f, autoDestroyDelay));

        totalGuardBreakCount++;
        lastTriggerTime = Time.time;

        return fxInstance;
    }

    /// <summary>
    /// ค้นหา MMOBlockParryController ที่อยู่ใกล้จุดปะทะมากที่สุด เพื่อสั่งปิดโล่ป้องกันของเป้าหมายที่โดนตีเกราะแตก
    /// </summary>
    private MMOBlockParryController FindNearestBlockController(Vector3 worldPos, float maxRadius)
    {
        if (cachedBlockController != null)
        {
            float selfDist = Vector3.Distance(cachedBlockController.transform.position, worldPos);
            if (selfDist <= maxRadius + 1.5f)
            {
                return cachedBlockController;
            }
        }

        MMOBlockParryController[] allControllers = FindObjectsByType<MMOBlockParryController>();
        MMOBlockParryController nearest = null;
        float bestSqrDist = maxRadius * maxRadius;

        for (int i = 0; i < allControllers.Length; i++)
        {
            MMOBlockParryController ctrl = allControllers[i];
            if (ctrl == null) continue;

            Vector3 checkPoint = ctrl.guardVfxMount != null
                ? ctrl.guardVfxMount.position
                : ctrl.transform.position + Vector3.up * 1.15f;

            float sqrDist = (checkPoint - worldPos).sqrMagnitude;
            if (sqrDist <= bestSqrDist)
            {
                bestSqrDist = sqrDist;
                nearest = ctrl;
            }
        }

        return nearest;
    }

    /// <summary>
    /// คำนวณจุดกลางหน้าโล่ของตัวละครตัวเอง (รองรับตัวละครที่มี CharacterVisual เยื้องแกน Y ด้วย)
    /// </summary>
    private Vector3 ComputeSelfShieldWorldPosition()
    {
        if (cachedBlockController != null && cachedBlockController.guardVfxMount != null)
        {
            return cachedBlockController.guardVfxMount.position;
        }

        float feetOffsetY = 0f;
        Transform visual = transform.Find("CharacterVisual");
        if (visual != null)
        {
            feetOffsetY = visual.localPosition.y;
        }

        Vector3 localOffset = new Vector3(
            fallbackShieldLocalOffset.x,
            feetOffsetY + fallbackShieldLocalOffset.y,
            fallbackShieldLocalOffset.z);

        return transform.TransformPoint(localOffset);
    }

    /// <summary>
    /// สั่นกล้องแบบ Micro Camera Shake (0.18 วินาที) โดยคุมตัวจับเวลาใน Coroutine
    /// และชดเชยตำแหน่งจริงใน LateUpdate (DefaultExecutionOrder 200) หลัง PlayerFollowCamera
    /// ทำให้สั่นได้ชัดเจนทั้งกล้องตามตัวละครและกล้องนิ่งโดยไม่เกิดการเลื่อนสะสม
    /// </summary>
    private IEnumerator MicroCameraShake(float duration, float magnitude)
    {
        shakeTotalDuration = Mathf.Max(0.01f, duration);
        shakeActiveMagnitude = magnitude;
        shakeTimeRemaining = shakeTotalDuration;

        while (shakeTimeRemaining > 0f)
        {
            shakeTimeRemaining -= Time.unscaledDeltaTime;
            yield return null;
        }

        shakeTimeRemaining = 0f;
        RestoreStaticCameraOffsetIfUnmoved();
        lastShakeWorldOffset = Vector3.zero;
        lastShakenCamera = null;
        activeShakeCoroutine = null;
    }

    private void RestoreStaticCameraOffsetIfUnmoved()
    {
        if (lastShakenCamera != null && lastShakeWorldOffset.sqrMagnitude > 0f)
        {
            // ถ้ากล้องไม่ได้ถูก PlayerFollowCamera เขียนทับตำแหน่งในเฟรมก่อน ให้ถอด offset เดิมออกเพื่อคืนตำแหน่งเป๊ะ
            if ((lastShakenCamera.transform.position - lastPostShakeCameraPos).sqrMagnitude < 0.0001f)
            {
                lastShakenCamera.transform.position -= lastShakeWorldOffset;
            }
            lastShakeWorldOffset = Vector3.zero;
        }
    }

    private void RestoreCameraShakeOffset()
    {
        shakeTimeRemaining = 0f;
        RestoreStaticCameraOffsetIfUnmoved();
        lastShakeWorldOffset = Vector3.zero;
        lastShakenCamera = null;
    }

    /// <summary>
    /// ตรวจจับปุ่มทดสอบ [B] รองรับทั้ง New Input System และ Legacy Input Manager พร้อมกันใน Unity 6
    /// </summary>
    private bool IsTestKeyPressedThisFrame()
    {
        bool pressed = false;

#if ENABLE_INPUT_SYSTEM
        try
        {
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (testKey == KeyCode.B && kb.bKey.wasPressedThisFrame)
                {
                    pressed = true;
                }
                else
                {
                    string keyName = testKey.ToString();
                    if (Enum.TryParse(keyName, true, out Key mappedKey) && mappedKey != Key.None)
                    {
                        var keyControl = kb[mappedKey];
                        if (keyControl != null && keyControl.wasPressedThisFrame)
                        {
                            pressed = true;
                        }
                    }
                }
            }
        }
        catch (Exception)
        {
            // ป้องกันกรณี Input System ยังไม่ได้ initialize
        }
#endif

        if (!pressed)
        {
            try
            {
                if (UnityEngine.Input.GetKeyDown(testKey))
                {
                    pressed = true;
                }
            }
            catch (InvalidOperationException)
            {
                // กรณีโปรเจกต์ตั้ง Active Input Handling = Input System Package (New) Only
            }
        }

        return pressed;
    }

    private static MMOGuardBreakVFX ResolveOrCreateManager()
    {
        if (Instance != null) return Instance;

        MMOGuardBreakVFX existing = FindAnyObjectByType<MMOGuardBreakVFX>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        GameObject host = new GameObject("MMOGuardBreakVFX_RuntimeManager");
        MMOGuardBreakVFX created = host.AddComponent<MMOGuardBreakVFX>();
        created.showDebugHUD = false;
        created.enableTestHotkey = false;
        Instance = created;
        return created;
    }

    private void OnGUI()
    {
        if (!showDebugHUD || !isLocalPlayerOwner) return;

        const float boxWidth = 395f;
        const float boxHeight = 134f;
        Rect panelRect = new Rect(16f, Screen.height - boxHeight - 16f, boxWidth, boxHeight);

        Color prevBg = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.05f, 0.08f, 0.14f, 0.86f);
        GUI.Box(panelRect, string.Empty);
        GUI.backgroundColor = prevBg;

        GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            richText = true
        };

        GUIStyle bodyStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            richText = true
        };

        string prefabState = guardBreakPrefab != null
            ? $"<color=#5EF2A8>พร้อมใช้งาน ({guardBreakPrefab.name})</color>"
            : "<color=#FFD166>โหมดสำรองอัตโนมัติ (Runtime 4-Layer Fallback)</color>";

        string shieldState = cachedBlockController != null
            ? (cachedBlockController.guardBarrierGroup != null && cachedBlockController.guardBarrierGroup.activeInHierarchy
                ? "<color=#54D8FF>เชื่อมต่อแล้ว (โล่กำลังเปิดอยู่)</color>"
                : "<color=#FF9F5A>เชื่อมต่อแล้ว (โล่ปิดอยู่ / แตกแล้ว)</color>")
            : "<color=#B8C4D4>ไม่พบ MMOBlockParryController บนตัวละครนี้</color>";

        string recentFlash = (Time.time - lastTriggerTime < 1.2f)
            ? " <color=#FFE066>[GUARD BROKEN!]</color>"
            : string.Empty;

        GUI.Label(
            new Rect(panelRect.x + 12f, panelRect.y + 8f, boxWidth - 24f, 22f),
            $"<color=#4CE0FF><b>[Guard Break VFX]</b></color> ระบบตีบล็อคแตก 4 เลเยอร์{recentFlash}",
            titleStyle);

        GUI.Label(
            new Rect(panelRect.x + 12f, panelRect.y + 32f, boxWidth - 24f, 20f),
            $"Prefab: {prefabState}",
            bodyStyle);

        GUI.Label(
            new Rect(panelRect.x + 12f, panelRect.y + 54f, boxWidth - 24f, 20f),
            $"Guard Shield: {shieldState}",
            bodyStyle);

        GUI.Label(
            new Rect(panelRect.x + 12f, panelRect.y + 76f, boxWidth - 24f, 20f),
            $"จำนวนครั้งที่แตก: <b>{totalGuardBreakCount}</b>  |  ปิดโล่ล่าสุด: <b>{(lastDisabledShield ? "ปิดสำเร็จ" : "-")}</b>  |  Shake: <b>{cameraShakeDuration:0.00}s</b>",
            bodyStyle);

        GUI.Label(
            new Rect(panelRect.x + 12f, panelRect.y + 102f, boxWidth - 24f, 22f),
            $"<color=#FFE066>กดปุ่ม [{testKey}] เพื่อทดสอบตีบล็อคแตก (Dual Input Unity 6)</color>",
            bodyStyle);
    }

    // =========================================================================
    // RUNTIME SELF-HEALING FALLBACK (สร้าง 4 เลเยอร์ + 3D Shard Mesh อัตโนมัติถ้ายังไม่ได้ผูก Prefab)
    // =========================================================================

    private static GameObject BuildRuntimeGuardBreakFallback(Vector3 position, Quaternion rotation)
    {
        EnsureFallbackAssets();

        GameObject root = new GameObject("VFX_GuardBreakShatter_Runtime");
        root.transform.SetPositionAndRotation(position, rotation);

        // Layer 1: Overload Crack Web Flash (0.00s - 0.24s)
        ParticleSystem l1 = CreateChildPS(root.transform, "L1_OverloadCrackWeb");
        var m1 = l1.main;
        m1.duration = 0.30f;
        m1.loop = false;
        m1.playOnAwake = true;
        m1.startDelay = 0.00f;
        m1.startLifetime = 0.24f;
        m1.startSpeed = 0f;
        m1.startSize = 2.4f;
        m1.startColor = Color.white; // HDR 4.8x ถูกขับจาก Material._BaseColor ไม่ถูก Color32 clamp
        m1.simulationSpace = ParticleSystemSimulationSpace.Local;
        m1.scalingMode = ParticleSystemScalingMode.Hierarchy;
        m1.maxParticles = 4;

        var e1 = l1.emission;
        e1.rateOverTime = 0f;
        e1.SetBursts(new[] { new ParticleSystem.Burst(0.00f, 1) });

        var s1 = l1.shape;
        s1.enabled = false;

        var col1 = l1.colorOverLifetime;
        col1.enabled = true;
        Gradient g1 = new Gradient();
        g1.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1.00f, 0.84f, 0.26f), 0.00f),
                new GradientColorKey(new Color(1.00f, 0.96f, 0.78f), 0.24f),
                new GradientColorKey(new Color(0.18f, 0.86f, 1.00f), 0.65f),
                new GradientColorKey(new Color(0.08f, 0.56f, 1.00f), 1.00f)
            },
            new[]
            {
                new GradientAlphaKey(0.00f, 0.00f),
                new GradientAlphaKey(1.00f, 0.10f),
                new GradientAlphaKey(1.00f, 0.48f),
                new GradientAlphaKey(0.00f, 1.00f)
            });
        col1.color = new ParticleSystem.MinMaxGradient(g1);

        var sol1 = l1.sizeOverLifetime;
        sol1.enabled = true;
        sol1.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0.00f, 0.62f),
            new Keyframe(0.18f, 1.00f),
            new Keyframe(1.00f, 1.18f)));

        var r1 = l1.GetComponent<ParticleSystemRenderer>();
        r1.renderMode = ParticleSystemRenderMode.Mesh;
        r1.mesh = fallbackQuadMesh;
        r1.alignment = ParticleSystemRenderSpace.Local;
        r1.sharedMaterial = fallbackCrackMat;
        r1.shadowCastingMode = ShadowCastingMode.Off;
        r1.receiveShadows = false;
        r1.sortingOrder = 10;

        // Layer 2: 3D Shattered Shield Shards (0.02s - 0.95s)
        ParticleSystem l2 = CreateChildPS(root.transform, "L2_ShatteredShieldShards3D");
        var m2 = l2.main;
        m2.duration = 1.00f;
        m2.loop = false;
        m2.playOnAwake = true;
        m2.startDelay = 0.02f;
        m2.startLifetime = new ParticleSystem.MinMaxCurve(0.68f, 0.93f);
        m2.startSpeed = new ParticleSystem.MinMaxCurve(4.6f, 10.2f);
        m2.startSize = new ParticleSystem.MinMaxCurve(0.24f, 0.52f);
        m2.startColor = Color.white; // HDR 3.2x ถูกขับจาก Material._BaseColor
        m2.gravityModifier = 1.65f;
        m2.simulationSpace = ParticleSystemSimulationSpace.World;
        m2.scalingMode = ParticleSystemScalingMode.Hierarchy;
        m2.startRotation3D = true;
        m2.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        m2.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        m2.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        m2.maxParticles = 40;

        var e2 = l2.emission;
        e2.rateOverTime = 0f;
        e2.SetBursts(new[] { new ParticleSystem.Burst(0.00f, 26) });

        var s2 = l2.shape;
        s2.enabled = true;
        s2.shapeType = ParticleSystemShapeType.Circle;
        s2.radius = 0.78f;
        s2.radiusThickness = 1.0f;
        s2.arc = 360f;
        s2.randomDirectionAmount = 0.35f;

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
                new GradientColorKey(new Color(0.75f, 0.97f, 1.00f), 0.00f),
                new GradientColorKey(new Color(0.20f, 0.80f, 1.00f), 0.42f),
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
            new Keyframe(0.65f, 0.92f),
            new Keyframe(1.00f, 0.14f)));

        var r2 = l2.GetComponent<ParticleSystemRenderer>();
        r2.renderMode = ParticleSystemRenderMode.Mesh;
        r2.mesh = fallbackShardMesh;
        r2.sharedMaterial = fallbackShardMat;
        r2.shadowCastingMode = ShadowCastingMode.Off;
        r2.receiveShadows = false;
        r2.sortingOrder = 11;

        // Layer 3: High-Velocity Shatter Sparks (0.02s - 0.48s)
        ParticleSystem l3 = CreateChildPS(root.transform, "L3_ShatterSparks");
        var m3 = l3.main;
        m3.duration = 0.55f;
        m3.loop = false;
        m3.playOnAwake = true;
        m3.startDelay = 0.02f;
        m3.startLifetime = new ParticleSystem.MinMaxCurve(0.24f, 0.46f);
        m3.startSpeed = new ParticleSystem.MinMaxCurve(7.5f, 15.5f);
        m3.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
        m3.startColor = Color.white; // HDR 4.5x ถูกขับจาก Material._BaseColor
        m3.gravityModifier = 0.95f;
        m3.simulationSpace = ParticleSystemSimulationSpace.World;
        m3.scalingMode = ParticleSystemScalingMode.Hierarchy;
        m3.maxParticles = 40;

        var e3 = l3.emission;
        e3.rateOverTime = 0f;
        e3.SetBursts(new[] { new ParticleSystem.Burst(0.00f, 24) });

        var s3 = l3.shape;
        s3.enabled = true;
        s3.shapeType = ParticleSystemShapeType.Cone;
        s3.angle = 68f;
        s3.radius = 0.45f;

        var col3 = l3.colorOverLifetime;
        col3.enabled = true;
        Gradient g3 = new Gradient();
        g3.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1.00f, 0.93f, 0.62f), 0.00f),
                new GradientColorKey(new Color(1.00f, 0.44f, 0.06f), 0.35f),
                new GradientColorKey(new Color(0.90f, 0.16f, 0.02f), 1.00f)
            },
            new[]
            {
                new GradientAlphaKey(1.00f, 0.00f),
                new GradientAlphaKey(0.90f, 0.45f),
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

        // Layer 4: Guard Break Shockwave Ring (0.03s - 0.34s)
        ParticleSystem l4 = CreateChildPS(root.transform, "L4_GuardBreakShockwave");
        var m4 = l4.main;
        m4.duration = 0.40f;
        m4.loop = false;
        m4.playOnAwake = true;
        m4.startDelay = 0.03f;
        m4.startLifetime = 0.31f;
        m4.startSpeed = 0f;
        m4.startSize = 3.4f;
        m4.startColor = Color.white; // HDR 3.8x ถูกขับจาก Material._BaseColor
        m4.simulationSpace = ParticleSystemSimulationSpace.Local;
        m4.scalingMode = ParticleSystemScalingMode.Hierarchy;
        m4.maxParticles = 4;

        var e4 = l4.emission;
        e4.rateOverTime = 0f;
        e4.SetBursts(new[] { new ParticleSystem.Burst(0.00f, 1) });

        var s4 = l4.shape;
        s4.enabled = false;

        var col4 = l4.colorOverLifetime;
        col4.enabled = true;
        Gradient g4 = new Gradient();
        g4.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.68f, 0.98f, 1.00f), 0.00f),
                new GradientColorKey(new Color(0.16f, 0.78f, 1.00f), 0.36f),
                new GradientColorKey(new Color(0.05f, 0.42f, 0.96f), 1.00f)
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
            new Keyframe(0.00f, 0.18f, 3.2f, 3.2f),
            new Keyframe(0.38f, 0.88f, 0.95f, 0.95f),
            new Keyframe(1.00f, 1.22f, 0.15f, 0.15f)));

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
        if (fallbackQuadMesh == null)
        {
            fallbackQuadMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            if (fallbackQuadMesh == null)
            {
                fallbackQuadMesh = CreateProceduralQuad();
            }
        }

        if (fallbackShardMesh == null) fallbackShardMesh = CreateProceduralShardMesh3D();
        if (fallbackCrackTex == null) fallbackCrackTex = CreateProceduralCrackWebTexture(512);
        if (fallbackGlowTex == null) fallbackGlowTex = CreateProceduralShardGlowTexture(128);
        if (fallbackWaveTex == null) fallbackWaveTex = CreateProceduralShockwaveTexture(256);

        if (fallbackCrackMat == null) fallbackCrackMat = CreateURPAdditiveMaterial("Mat_Runtime_GuardCrackWeb", fallbackCrackTex, 4.8f);
        if (fallbackShardMat == null) fallbackShardMat = CreateURPAdditiveMaterial("Mat_Runtime_ShieldShard3D", fallbackGlowTex, 3.2f);
        if (fallbackSparkMat == null) fallbackSparkMat = CreateURPAdditiveMaterial("Mat_Runtime_ShatterSparks", fallbackGlowTex, 4.5f);
        if (fallbackWaveMat == null) fallbackWaveMat = CreateURPAdditiveMaterial("Mat_Runtime_GuardBreakWave", fallbackWaveTex, 3.8f);
    }

    private static Mesh CreateProceduralQuad()
    {
        Mesh m = new Mesh { name = "Runtime_Quad" };
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
        m.RecalculateBounds();
        return m;
    }

    private static Mesh CreateProceduralShardMesh3D()
    {
        Mesh mesh = new Mesh { name = "Mesh_ShieldShard3D_Runtime" };
        mesh.vertices = new[]
        {
            new Vector3( 0.00f,  0.58f,  0.00f),
            new Vector3(-0.44f, -0.38f,  0.00f),
            new Vector3( 0.48f, -0.30f,  0.00f),
            new Vector3( 0.02f, -0.02f,  0.14f),
            new Vector3( 0.02f, -0.02f, -0.14f)
        };
        mesh.triangles = new[]
        {
            0, 1, 3,
            1, 2, 3,
            2, 0, 3,
            0, 4, 1,
            1, 4, 2,
            2, 4, 0
        };
        mesh.uv = new[]
        {
            new Vector2(0.50f, 0.96f),
            new Vector2(0.08f, 0.10f),
            new Vector2(0.94f, 0.16f),
            new Vector2(0.50f, 0.50f),
            new Vector2(0.50f, 0.50f)
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Texture2D CreateProceduralCrackWebTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.name = "T_ShieldCrackWeb_Runtime";
        tex.wrapMode = TextureWrapMode.Clamp;

        const int rayCount = 11;
        float[] rayAngles = new float[rayCount];
        for (int i = 0; i < rayCount; i++)
        {
            float baseAngle = (i / (float)rayCount) * Mathf.PI * 2f;
            float jitter = (Mathf.PerlinNoise(i * 3.71f, 4.19f) - 0.5f) * 0.28f;
            rayAngles[i] = baseAngle + jitter;
        }

        Color[] pixels = new Color[size * size];
        float invSize = 1f / (size - 1);

        for (int y = 0; y < size; y++)
        {
            float ny = y * invSize * 2f - 1f;
            for (int x = 0; x < size; x++)
            {
                float nx = x * invSize * 2f - 1f;
                float r = Mathf.Sqrt(nx * nx + ny * ny);
                if (r >= 0.99f)
                {
                    pixels[y * size + x] = Color.clear;
                    continue;
                }

                float angle = Mathf.Atan2(ny, nx);
                if (angle < 0f) angle += Mathf.PI * 2f;

                float outerMask = 1f - SmoothStep(0.76f, 0.98f, r);
                float jagNoise = (Mathf.PerlinNoise(nx * 5.2f + 11.4f, ny * 5.2f + 3.7f) - 0.5f) * 0.24f;
                float fineNoise = (Mathf.PerlinNoise(nx * 14.0f + 2.1f, ny * 14.0f + 8.9f) - 0.5f) * 0.08f;

                float intensity = 0f;

                // 11 แฉกรอยร้าวพร้อมรอยหยัก Perlin Noise
                for (int i = 0; i < rayCount; i++)
                {
                    float targetAngle = rayAngles[i] + jagNoise * Mathf.Clamp01(r * 1.4f) + fineNoise;
                    float dAngle = Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, targetAngle * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
                    float arcDist = dAngle * Mathf.Max(r, 0.04f);
                    float lineWidth = Mathf.Lerp(0.026f, 0.008f, r);
                    float rayGlow = Mathf.Exp(-(arcDist * arcDist) / (lineWidth * lineWidth));
                    intensity = Mathf.Max(intensity, rayGlow);
                }

                // วงรอยร้าวเชื่อมระหว่างแฉก 2 ชั้น
                float ringNoise1 = (Mathf.PerlinNoise(Mathf.Cos(angle) * 3.1f + 5f, Mathf.Sin(angle) * 3.1f + 5f) - 0.5f) * 0.09f;
                float ringNoise2 = (Mathf.PerlinNoise(Mathf.Cos(angle) * 4.5f + 17f, Mathf.Sin(angle) * 4.5f + 9f) - 0.5f) * 0.11f;
                float ring1Dist = Mathf.Abs(r - (0.34f + ringNoise1));
                float ring2Dist = Mathf.Abs(r - (0.64f + ringNoise2));
                float ring1 = Mathf.Exp(-(ring1Dist * ring1Dist) / (0.015f * 0.015f)) * 0.92f;
                float ring2 = Mathf.Exp(-(ring2Dist * ring2Dist) / (0.013f * 0.013f)) * 0.78f;

                // จุดศูนย์กลางรับแรงปะทะสว่างวาบ
                float coreFlash = Mathf.Exp(-(r * r) / (0.14f * 0.14f)) * 1.15f;

                float finalVal = Mathf.Clamp01(Mathf.Max(Mathf.Max(intensity, coreFlash), Mathf.Max(ring1, ring2)) * outerMask);
                pixels[y * size + x] = new Color(finalVal, finalVal, finalVal, finalVal);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    private static Texture2D CreateProceduralShardGlowTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.name = "T_ShardGlow_Runtime";
        tex.wrapMode = TextureWrapMode.Clamp;

        Color[] pixels = new Color[size * size];
        float invSize = 1f / (size - 1);

        for (int y = 0; y < size; y++)
        {
            float ny = y * invSize * 2f - 1f;
            for (int x = 0; x < size; x++)
            {
                float nx = x * invSize * 2f - 1f;
                float r = Mathf.Sqrt(nx * nx + ny * ny);
                float falloff = Mathf.Clamp01(1f - r);
                float glow = Mathf.Pow(falloff, 1.85f) * 0.72f + Mathf.Pow(falloff, 4.5f) * 0.45f;
                float a = Mathf.Clamp01(glow);
                pixels[y * size + x] = new Color(a, a, a, a);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    private static Texture2D CreateProceduralShockwaveTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.name = "T_GuardBreakWave_Runtime";
        tex.wrapMode = TextureWrapMode.Clamp;

        Color[] pixels = new Color[size * size];
        float invSize = 1f / (size - 1);

        for (int y = 0; y < size; y++)
        {
            float ny = y * invSize * 2f - 1f;
            for (int x = 0; x < size; x++)
            {
                float nx = x * invSize * 2f - 1f;
                float r = Mathf.Sqrt(nx * nx + ny * ny);
                if (r >= 0.98f)
                {
                    pixels[y * size + x] = Color.clear;
                    continue;
                }

                float angle = Mathf.Atan2(ny, nx);
                float outerCut = 1f - SmoothStep(0.84f, 0.94f, r);
                float innerFade = SmoothStep(0.50f, 0.80f, r);
                float sharpRim = Mathf.Exp(-Mathf.Pow((r - 0.82f) / 0.045f, 2f));
                float energyNoise = 0.82f + 0.18f * Mathf.PerlinNoise(Mathf.Cos(angle) * 6f + 4f, Mathf.Sin(angle) * 6f + 4f);

                float val = Mathf.Clamp01((innerFade * outerCut * 0.75f + sharpRim * 0.95f) * energyNoise * outerCut);
                pixels[y * size + x] = new Color(val, val, val, val);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    private static Material CreateURPAdditiveMaterial(string matName, Texture2D mainTex, float hdrIntensity)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        Color hdrColor = new Color(hdrIntensity, hdrIntensity, hdrIntensity, 1.0f);
        Material mat = new Material(shader) { name = matName };
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
