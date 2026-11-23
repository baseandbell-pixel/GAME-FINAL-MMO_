using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
#if UNITY_6000_3_OR_NEWER
using EnemyIdKey = UnityEngine.EntityId;
#else
using EnemyIdKey = System.Int32;
#endif

/// <summary>
/// ระบบเอฟเฟกต์ "โจมตีโดนตัวมอนสเตอร์อัตโนมัติพร้อมเอฟเฟกต์มอนสเตอร์กระพริบแสงตอนโดนตีระดับ AAA"
/// (Weapon-to-Monster Hit Impact & Enemy Hit-Flash VFX) สำหรับ Unity 6 (URP - Universal Render Pipeline)
/// - ทำงานได้ทั้ง 2 รูปแบบ:
///   (1) เรียกผ่านโค้ดตอนรับดาเมจ: MMOWeaponHitImpactVFX.SpawnOnMonster(hitPosition, enemyObject);
///   (2) ติดบนตัวดาบที่มี Collider (Is Trigger) เพื่อตรวจจับการฟันโดนศัตรูอัตโนมัติผ่าน OnTriggerEnter
/// - หันหน้าเอฟเฟกต์เข้าหากล้อง + สุ่มหมุนแกน Z (0-360 องศา) และทำลายอัตโนมัติใน 1.2 วินาที
/// - สั่ง FlashEnemyMaterials เปลี่ยนสี Material มอนสเตอร์ทุกชิ้นส่วน (_BaseColor / _Color) เป็นสีแดง-ส้ม 0.09 วินาทีแล้วคืนสีเดิมเป๊ะๆ
/// - เชื่อมต่อ MMOHeartbeatComboUI.RegisterHit(1) อัตโนมัติ และรองรับปุ่มทดสอบ [Y] ทั้ง New Input System & Legacy Input
/// </summary>
[AddComponentMenu("yeen VFX/12. [Weapon Hit] โจมตีโดนมอนสเตอร์และกระพริบแสง (Weapon Hit Impact VFX)")]
[DisallowMultipleComponent]
public class MMOWeaponHitImpactVFX : MonoBehaviour
{
    public static MMOWeaponHitImpactVFX Instance { get; private set; }

    [Header("=== 1. พรีแฟบและขนาดเอฟเฟกต์ (Hit Impact Prefab Settings) ===")]
    [Tooltip("Prefab เอฟเฟกต์ 4 เลเยอร์ (VFX_WeaponHitImpact.prefab) สร้างจากเมนู VFX Mentor/18")]
    public GameObject hitImpactPrefab;

    [Tooltip("ขนาดพื้นฐานของเอฟเฟกต์เมื่อไม่ได้ส่ง customScale")]
    [Min(0.1f)]
    public float defaultScale = 1.0f;

    [Tooltip("เวลาทำลาย GameObject เอฟเฟกต์อัตโนมัติ (วินาที)")]
    [Min(0.4f)]
    public float autoDestroyDelay = 1.2f;

    [Header("=== 2. ระบบตรวจจับการฟันโดนอัตโนมัติบนตัวดาบ (Weapon Trigger Detection) ===")]
    [Tooltip("เปิดใช้งาน OnTriggerEnter เมื่อนำสคริปต์นี้ไปติดบนตัวดาบที่มี Collider (Is Trigger)")]
    public bool enableTriggerDetection = true;

    [Tooltip("Tag ของมอนสเตอร์ที่ต้องการตรวจจับเมื่อฟันโดน (ค่าเริ่มต้น = Enemy)")]
    public string enemyTag = "Enemy";

    [Tooltip("คูลดาวน์ขั้นต่ำต่อมอนสเตอร์ 1 ตัว (วินาที) ป้องกันการเกิดเอฟเฟกต์ซ้ำรัวในการฟันครั้งเดียว")]
    [Range(0.03f, 0.5f)]
    public float perEnemyHitCooldown = 0.14f;

    [Header("=== 3. ระบบมอนสเตอร์กระพริบแสงตอนโดนตี (Enemy Hit-Flash) ===")]
    [Tooltip("เปิดใช้งานการกระพริบสี Material ของมอนสเตอร์เมื่อโดนโจมตี")]
    public bool enableEnemyHitFlash = true;

    [Tooltip("สีแดง-ส้มที่สว่างวาบบนตัวมอนสเตอร์ตอนโดนตี")]
    public Color hitFlashColor = new Color(1.0f, 0.28f, 0.08f, 1.0f);

    [Tooltip("ระยะเวลาที่มอนสเตอร์กระพริบสี (ค่ามาตรฐาน AAA = 0.09 วินาที)")]
    [Range(0.03f, 0.25f)]
    public float hitFlashDuration = 0.09f;

    [Header("=== 4. การเชื่อมระบบคอมโบและปุ่มทดสอบ (Combo Sync & Test Key Y) ===")]
    [Tooltip("ส่งแต้มคอมโบเข้า MMOHeartbeatComboUI.RegisterHit(1) อัตโนมัติทุกครั้งที่ฟันโดน")]
    public bool autoRegisterCombo = true;

    [Tooltip("เปิดใช้งานปุ่มทดสอบ [Y] ในโหมด Play")]
    public bool enableTestHotkey = true;

    [Tooltip("ปุ่มคีย์บอร์ดสำหรับทดสอบ (ค่าเริ่มต้น = Y)")]
    public KeyCode testKey = KeyCode.Y;

    [Tooltip("ระยะห่างด้านหน้าตัวละครเมื่อกดปุ่ม [Y] ทดสอบ")]
    public float testSpawnDistance = 1.85f;

    [Tooltip("ความสูงจุดปะทะเมื่อกดปุ่ม [Y] ทดสอบ")]
    public float testSpawnHeight = 1.15f;

    [Tooltip("แสดงหน้าต่าง OnGUI แสดงสถานะมุมซ้ายล่างของหน้าจอ")]
    public bool showDebugHUD = true;

    // Runtime cooldown & safe multi-hit Hit-Flash tracking
    private readonly Dictionary<EnemyIdKey, float> lastHitTimeByEnemyId = new Dictionary<EnemyIdKey, float>();
    private static readonly Dictionary<EnemyIdKey, EnemyFlashRecord> activeEnemyFlashes = new Dictionary<EnemyIdKey, EnemyFlashRecord>();

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private static int totalHitsSpawned = 0;
    private static float lastHitTimestamp = -99f;
    private static string lastHitTargetName = "-";
    private static bool lastFlashSucceeded = false;

    // Runtime Self-Healing Fallback Assets (ทำงานได้ 100% แม้ยังไม่ได้ลาก Prefab ใส่ช่อง)
    private static Mesh fallbackQuadMesh;
    private static Texture2D fallbackSlashTex;
    private static Texture2D fallbackCoreTex;
    private static Texture2D fallbackWaveTex;
    private static Material fallbackCoreMat;
    private static Material fallbackSlashMat;
    private static Material fallbackSparkMat;
    private static Material fallbackWaveMat;

    private sealed class EnemyFlashRecord
    {
        public MonoBehaviour Owner;
        public Coroutine Coroutine;
        public readonly List<MaterialColorSnapshot> Snapshots = new List<MaterialColorSnapshot>();
    }

    private struct MaterialColorSnapshot
    {
        public Material Material;
        public bool HasBaseColor;
        public Color OriginalBaseColor;
        public bool HasColor;
        public Color OriginalColor;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticsOnPlay()
    {
        Instance = null;
        activeEnemyFlashes.Clear();
        totalHitsSpawned = 0;
        lastHitTimestamp = -99f;
        lastHitTargetName = "-";
        lastFlashSucceeded = false;
        fallbackQuadMesh = null;
        fallbackSlashTex = null;
        fallbackCoreTex = null;
        fallbackWaveTex = null;
        fallbackCoreMat = null;
        fallbackSlashMat = null;
        fallbackSparkMat = null;
        fallbackWaveMat = null;
    }

    private static EnemyIdKey GetEnemyIdKey(GameObject enemy)
    {
#if UNITY_6000_3_OR_NEWER
        return enemy.GetEntityId();
#else
        return enemy.GetInstanceID();
#endif
    }

    private void Awake()
    {
        if (Instance == null || Instance == this)
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

    private void OnDisable()
    {
        RestoreActiveFlashesOwnedByThis();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnDestroy()
    {
        RestoreActiveFlashesOwnedByThis();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        if (!enableTestHotkey) return;

        if (IsTestKeyPressedThisFrame())
        {
            TriggerTestHitInFront();
        }
    }

    /// <summary>
    /// ตรวจจับการฟันโดนมอนสเตอร์อัตโนมัติเมื่อนำสคริปต์นี้ไปติดบนตัวดาบที่มี Collider (Is Trigger)
    /// </summary>
    private void OnTriggerEnter(Collider other)
    {
        if (!enableTriggerDetection || other == null) return;

        // ป้องกันการฟันโดนตัวละครเจ้าของอาวุธเอง
        if (other.transform.root == transform.root) return;

        // ตรวจสอบ Tag ของมอนสเตอร์ (รองรับทั้ง Collider ที่ตัวลูกและโหนดแม่ของมอนสเตอร์)
        GameObject enemyRoot = ResolveTaggedEnemyRoot(other);
        if (enemyRoot == null) return;

        // ตรวจสอบ Cooldown กันการเกิดเอฟเฟกต์ซ้ำถี่เกินไปในการฟัน 1 ครั้ง
        EnemyIdKey enemyId = GetEnemyIdKey(enemyRoot);
        float now = Time.time;
        if (lastHitTimeByEnemyId.TryGetValue(enemyId, out float prevHitTime) &&
            (now - prevHitTime) < perEnemyHitCooldown)
        {
            return;
        }
        lastHitTimeByEnemyId[enemyId] = now;

        // คำนวณจุดสัมผัสแม่นยำที่สุดบนผิว Collider ของมอนสเตอร์
        Vector3 contactPoint = other.ClosestPoint(transform.position);
        SpawnImpactInternal(contactPoint, enemyRoot, -1f, true);
    }

    private static bool SafeHasTag(Transform t, string tagName)
    {
        if (t == null || string.IsNullOrEmpty(tagName)) return false;
        try
        {
            return t.CompareTag(tagName);
        }
        catch (UnityException)
        {
            return string.Equals(t.tag, tagName, StringComparison.Ordinal);
        }
    }

    private GameObject ResolveTaggedEnemyRoot(Collider other)
    {
        if (string.IsNullOrEmpty(enemyTag))
        {
            return other.transform.root.gameObject;
        }

        if (SafeHasTag(other.transform, enemyTag))
        {
            return other.gameObject;
        }

        Transform root = other.transform.root;
        if (SafeHasTag(root, enemyTag))
        {
            return root.gameObject;
        }

        // ค้นหาไล่ขึ้นไปตามลำดับชั้น (กรณี Collider อยู่ที่กระดูกแขน/ขาของมอนสเตอร์)
        Transform cur = other.transform.parent;
        while (cur != null)
        {
            if (SafeHasTag(cur, enemyTag))
            {
                return cur.gameObject;
            }
            cur = cur.parent;
        }

        // Fallback อัตโนมัติสำหรับโปรเจกต์ที่มอนสเตอร์ยังไม่ได้ตั้ง Tag "Enemy" แต่มีคอมโพเนนต์ EnemyNPC
        MonoBehaviour[] behaviours = other.GetComponentsInParent<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] != null && behaviours[i].GetType().Name == "EnemyNPC")
            {
                return behaviours[i].gameObject;
            }
        }

        return null;
    }

    /// <summary>
    /// ฟังก์ชัน Static Public หลักสำหรับให้สคริปต์รับดาเมจของมอนสเตอร์เรียกใช้งานได้ทันทีใน 1 บรรทัด
    /// ตัวอย่าง: MMOWeaponHitImpactVFX.SpawnOnMonster(hitPoint, gameObject);
    /// </summary>
    public static GameObject SpawnOnMonster(Vector3 hitPosition, GameObject enemyObject = null, float customScale = -1f)
    {
        MMOWeaponHitImpactVFX manager = ResolveOrCreateManager();
        return manager.SpawnImpactInternal(hitPosition, enemyObject, customScale, true);
    }

    /// <summary>
    /// Overload สำหรับระบบต่อสู้ที่นับคอมโบแยกไว้อยู่แล้ว (เช่น CombatHud) เพื่อไม่ให้นับคอมโบเบิ้ล
    /// </summary>
    public static GameObject SpawnOnMonster(Vector3 hitPosition, GameObject enemyObject, float customScale, bool registerComboHit)
    {
        MMOWeaponHitImpactVFX manager = ResolveOrCreateManager();
        return manager.SpawnImpactInternal(hitPosition, enemyObject, customScale, registerComboHit);
    }

    /// <summary>
    /// สั่งกระพริบแสงบนตัวมอนสเตอร์อย่างเดียว (ใช้ร่วมกับ Critical Hit VFX ได้ทันที)
    /// </summary>
    public static void FlashEnemy(GameObject enemyObject)
    {
        if (enemyObject == null) return;
        MMOWeaponHitImpactVFX manager = ResolveOrCreateManager();
        if (manager != null && manager.enableEnemyHitFlash && manager.isActiveAndEnabled)
        {
            manager.FlashEnemyMaterials(enemyObject);
        }
    }

    /// <summary>
    /// ทดสอบเสกเอฟเฟกต์ปะทะด้านหน้าตัวละครทันที (เรียกจากปุ่ม Y หรือ ContextMenu)
    /// </summary>
    [ContextMenu("ทดสอบเอฟเฟกต์ฟันโดนมอนสเตอร์ (Trigger Test Hit Y)")]
    public GameObject TriggerTestHitInFront()
    {
        float feetY = 0f;
        Transform visual = transform.Find("CharacterVisual");
        if (visual != null) feetY = visual.localPosition.y;

        Vector3 spawnPos = transform.position
            + Vector3.up * (feetY + testSpawnHeight)
            + transform.forward * testSpawnDistance;

        // ค้นหามอนสเตอร์ใกล้ที่สุดในระยะ 4 เมตรเพื่อให้เห็นเอฟเฟกต์กระพริบแสงบนตัวมอนสเตอร์จริงด้วย (ถ้ามี)
        GameObject nearbyEnemy = FindNearestEnemyForTest(spawnPos, 4.0f);
        if (nearbyEnemy != null)
        {
            spawnPos = nearbyEnemy.transform.position + Vector3.up * 1.15f;
        }

        return SpawnImpactInternal(spawnPos, nearbyEnemy, -1f, true);
    }

    private GameObject SpawnImpactInternal(
        Vector3 hitPosition,
        GameObject enemyObject,
        float customScale,
        bool registerComboHit)
    {
        // 1. หันหน้าเอฟเฟกต์เข้าหากล้อง + สุ่มหมุนแกน Z (0 - 360 องศา) เพื่อให้รอยตัดไม่ซ้ำซาก
        Camera cam = Camera.main;
        Quaternion faceCamera = cam != null
            ? Quaternion.LookRotation(cam.transform.forward, Vector3.up)
            : Quaternion.identity;
        float randomRollZ = UnityEngine.Random.Range(0f, 360f);
        Quaternion spawnRotation = faceCamera * Quaternion.Euler(0f, 0f, randomRollZ);

        // 2. เสก Prefab 4 เลเยอร์ (หรือสร้างชุด Runtime 4 เลเยอร์สำรองหากยังไม่ได้ผูก Prefab)
        GameObject fxInstance;
        if (hitImpactPrefab != null)
        {
            fxInstance = Instantiate(hitImpactPrefab, hitPosition, spawnRotation);
        }
        else
        {
            fxInstance = BuildRuntime4LayerHitImpactFallback(hitPosition, spawnRotation);
        }

        // 3. ปรับขนาดตาม customScale
        float finalScale = customScale > 0f ? customScale : defaultScale;
        if (!Mathf.Approximately(finalScale, 1f))
        {
            fxInstance.transform.localScale = Vector3.one * finalScale;
        }

        ParticleSystem[] systems = fxInstance.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < systems.Length; i++)
        {
            systems[i].Clear(true);
            systems[i].Play(true);
        }

        // 4. ทำลายตัวเองอัตโนมัติใน 1.2 วินาที
        Destroy(fxInstance, Mathf.Max(0.4f, autoDestroyDelay));

        // 5. สั่งมอนสเตอร์กระพริบแสงสีแดง-ส้ม 0.09 วินาทีแล้วคืนสีเดิมเป๊ะๆ
        lastFlashSucceeded = false;
        if (enableEnemyHitFlash && enemyObject != null && isActiveAndEnabled)
        {
            FlashEnemyMaterials(enemyObject);
        }

        // 6. เชื่อมต่อระบบคอมโบ MMOHeartbeatComboUI.RegisterHit(1) อัตโนมัติหากมีในฉาก
        if (autoRegisterCombo && registerComboHit)
        {
            if (MMOHeartbeatComboUI.Instance != null || FindAnyObjectByType<MMOHeartbeatComboUI>() != null)
            {
                MMOHeartbeatComboUI.RegisterHit(1);
            }
        }

        totalHitsSpawned++;
        lastHitTimestamp = Time.time;
        lastHitTargetName = enemyObject != null ? enemyObject.name : "Test Point (ไม่มีเป้าหมาย)";

        return fxInstance;
    }

    // =========================================================================
    // ระบบมอนสเตอร์กระพริบแสง (FlashEnemyMaterials — รองรับ URP _BaseColor & Standard _Color)
    // =========================================================================

    /// <summary>
    /// สั่งให้ Material ของมอนสเตอร์ทุกชิ้นส่วนสว่างวาบเป็นสีแดง-ส้มเป็นเวลา 0.09 วินาทีแล้วคืนสีเดิมเป๊ะๆ
    /// ปลอดภัยต่อการฟันรัวซ้อนกัน (เก็บบันทึกสีดั้งเดิมจริงไว้เสมอ ไม่เพี้ยนค้าง)
    /// </summary>
    public void FlashEnemyMaterials(GameObject enemy)
    {
        if (enemy == null || !gameObject.activeInHierarchy) return;

        EnemyIdKey enemyId = GetEnemyIdKey(enemy);
        EnemyFlashRecord record;

        if (activeEnemyFlashes.TryGetValue(enemyId, out record))
        {
            // หากมอนสเตอร์กำลังกระพริบอยู่แล้ว ให้หยุด Coroutine เก่าบน Owner เดิมโดยยังคงใช้สีดั้งเดิม (Original Snapshots) เดิมไว้
            if (record.Owner != null && record.Coroutine != null)
            {
                record.Owner.StopCoroutine(record.Coroutine);
            }
        }
        else
        {
            record = new EnemyFlashRecord();
            Renderer[] renderers = enemy.GetComponentsInChildren<Renderer>(true);
            HashSet<Material> seenMaterials = new HashSet<Material>();

            for (int r = 0; r < renderers.Length; r++)
            {
                Renderer rend = renderers[r];
                if (rend == null || rend is ParticleSystemRenderer || rend is TrailRenderer || rend is LineRenderer)
                {
                    continue;
                }

                // ใช้ rend.materials เพื่อแยกอินสแตนซ์เฉพาะมอนสเตอร์ตัวนี้ ไม่ให้มอนสเตอร์ตัวอื่นที่ใช้ Material เดียวกันกระพริบตาม
                Material[] mats = rend.materials;
                for (int m = 0; m < mats.Length; m++)
                {
                    Material mat = mats[m];
                    if (mat == null || !seenMaterials.Add(mat)) continue;

                    bool hasBase = mat.HasProperty(BaseColorId);
                    bool hasCol = mat.HasProperty(ColorId);
                    if (!hasBase && !hasCol) continue;

                    record.Snapshots.Add(new MaterialColorSnapshot
                    {
                        Material = mat,
                        HasBaseColor = hasBase,
                        OriginalBaseColor = hasBase ? mat.GetColor(BaseColorId) : Color.white,
                        HasColor = hasCol,
                        OriginalColor = hasCol ? mat.GetColor(ColorId) : Color.white
                    });
                }
            }

            if (record.Snapshots.Count == 0)
            {
                return;
            }

            activeEnemyFlashes[enemyId] = record;
        }

        lastFlashSucceeded = true;
        record.Owner = this;
        record.Coroutine = StartCoroutine(FlashEnemyMaterialsRoutine(enemyId, record, hitFlashColor, hitFlashDuration));
    }

    private IEnumerator FlashEnemyMaterialsRoutine(EnemyIdKey enemyId, EnemyFlashRecord record, Color flashCol, float duration)
    {
        // เปลี่ยนสี Material ทุกชิ้นส่วนของมอนสเตอร์เป็นสีแดง-ส้มทันที
        for (int i = 0; i < record.Snapshots.Count; i++)
        {
            MaterialColorSnapshot snap = record.Snapshots[i];
            if (snap.Material == null) continue;

            if (snap.HasBaseColor)
            {
                Color c = new Color(flashCol.r, flashCol.g, flashCol.b, snap.OriginalBaseColor.a);
                snap.Material.SetColor(BaseColorId, c);
            }
            if (snap.HasColor)
            {
                Color c = new Color(flashCol.r, flashCol.g, flashCol.b, snap.OriginalColor.a);
                snap.Material.SetColor(ColorId, c);
            }
        }

        yield return new WaitForSeconds(Mathf.Max(0.02f, duration));

        RestoreSnapshotColors(record);
        activeEnemyFlashes.Remove(enemyId);
    }

    private static void RestoreSnapshotColors(EnemyFlashRecord record)
    {
        if (record == null) return;
        for (int i = 0; i < record.Snapshots.Count; i++)
        {
            MaterialColorSnapshot snap = record.Snapshots[i];
            if (snap.Material == null) continue;

            if (snap.HasBaseColor)
            {
                snap.Material.SetColor(BaseColorId, snap.OriginalBaseColor);
            }
            if (snap.HasColor)
            {
                snap.Material.SetColor(ColorId, snap.OriginalColor);
            }
        }
    }

    private void RestoreActiveFlashesOwnedByThis()
    {
        if (activeEnemyFlashes.Count == 0) return;

        List<EnemyIdKey> ownedKeys = null;
        foreach (KeyValuePair<EnemyIdKey, EnemyFlashRecord> kv in activeEnemyFlashes)
        {
            if (kv.Value != null && kv.Value.Owner == this)
            {
                if (ownedKeys == null) ownedKeys = new List<EnemyIdKey>();
                ownedKeys.Add(kv.Key);
            }
        }

        if (ownedKeys == null) return;
        for (int i = 0; i < ownedKeys.Count; i++)
        {
            if (activeEnemyFlashes.TryGetValue(ownedKeys[i], out EnemyFlashRecord rec))
            {
                RestoreSnapshotColors(rec);
                activeEnemyFlashes.Remove(ownedKeys[i]);
            }
        }
    }

    private GameObject FindNearestEnemyForTest(Vector3 aroundPos, float radius)
    {
        GameObject best = null;
        float bestSqr = radius * radius;

        if (!string.IsNullOrEmpty(enemyTag))
        {
            try
            {
                GameObject[] enemies = GameObject.FindGameObjectsWithTag(enemyTag);
                for (int i = 0; i < enemies.Length; i++)
                {
                    if (enemies[i] == null || !enemies[i].activeInHierarchy) continue;
                    float sqr = (enemies[i].transform.position - aroundPos).sqrMagnitude;
                    if (sqr <= bestSqr)
                    {
                        bestSqr = sqr;
                        best = enemies[i];
                    }
                }
            }
            catch (UnityException)
            {
            }
        }

        if (best != null) return best;

        // Fallback: ค้นหา Collider ของมอนสเตอร์ใกล้เคียงที่มีคอมโพเนนต์ EnemyNPC
        Collider[] cols = Physics.OverlapSphere(aroundPos, radius);
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] == null || cols[i].transform.root == transform.root) continue;
            MonoBehaviour[] behaviours = cols[i].GetComponentsInParent<MonoBehaviour>(true);
            for (int b = 0; b < behaviours.Length; b++)
            {
                if (behaviours[b] != null && behaviours[b].GetType().Name == "EnemyNPC")
                {
                    float sqr = (behaviours[b].transform.position - aroundPos).sqrMagnitude;
                    if (sqr <= bestSqr)
                    {
                        bestSqr = sqr;
                        best = behaviours[b].gameObject;
                    }
                    break;
                }
            }
        }

        return best;
    }

    private bool IsTestKeyPressedThisFrame()
    {
        bool pressed = false;

#if ENABLE_INPUT_SYSTEM
        try
        {
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (testKey == KeyCode.Y && kb.yKey.wasPressedThisFrame)
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
            }
        }

        return pressed;
    }

    private static MMOWeaponHitImpactVFX ResolveOrCreateManager()
    {
        if (Instance != null) return Instance;

        MMOWeaponHitImpactVFX existing = FindAnyObjectByType<MMOWeaponHitImpactVFX>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        GameObject host = new GameObject("MMOWeaponHitImpactVFX_RuntimeManager");
        MMOWeaponHitImpactVFX created = host.AddComponent<MMOWeaponHitImpactVFX>();
        created.showDebugHUD = false;
        created.enableTestHotkey = false;
        created.enableTriggerDetection = false;
        Instance = created;
        return created;
    }

    private void OnGUI()
    {
        if (!showDebugHUD) return;

        const float boxWidth = 415f;
        const float boxHeight = 136f;
        Rect panelRect = new Rect(16f, Screen.height - boxHeight - 16f, boxWidth, boxHeight);

        Color prevBg = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.05f, 0.09f, 0.14f, 0.86f);
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

        string prefabStatus = hitImpactPrefab != null
            ? $"<color=#5EF2A8>พร้อมใช้งาน ({hitImpactPrefab.name})</color>"
            : "<color=#FFD166>โหมดสำรองอัตโนมัติ (Runtime 4-Layer Fallback)</color>";

        string comboStatus = (MMOHeartbeatComboUI.Instance != null)
            ? $"<color=#54D8FF>เชื่อมต่อแล้ว (Combo: {MMOHeartbeatComboUI.Instance.CurrentCombo})</color>"
            : "<color=#B8C4D4>พร้อมเชื่อมต่ออัตโนมัติเมื่อมีในฉาก</color>";

        string flashBadge = (Time.time - lastHitTimestamp < 0.8f)
            ? " <color=#FFE066>[HIT IMPACT!]</color>"
            : string.Empty;

        GUI.Label(
            new Rect(panelRect.x + 12f, panelRect.y + 8f, boxWidth - 24f, 22f),
            $"<color=#4CE0FF><b>[Weapon Hit Impact VFX]</b></color> โจมตีโดนมอนสเตอร์ 4 เลเยอร์{flashBadge}",
            titleStyle);

        GUI.Label(
            new Rect(panelRect.x + 12f, panelRect.y + 32f, boxWidth - 24f, 20f),
            $"Prefab: {prefabStatus}",
            bodyStyle);

        GUI.Label(
            new Rect(panelRect.x + 12f, panelRect.y + 54f, boxWidth - 24f, 20f),
            $"Combo UI: {comboStatus}",
            bodyStyle);

        GUI.Label(
            new Rect(panelRect.x + 12f, panelRect.y + 76f, boxWidth - 24f, 20f),
            $"ฟันโดนรวม: <b>{totalHitsSpawned}</b>  |  เป้าหมายล่าสุด: <b>{lastHitTargetName}</b>  |  Flash: <b>{(lastFlashSucceeded ? "ON" : "-")}</b>",
            bodyStyle);

        GUI.Label(
            new Rect(panelRect.x + 12f, panelRect.y + 104f, boxWidth - 24f, 22f),
            $"<color=#FFE066>กดปุ่ม [{testKey}] ทดสอบเอฟเฟกต์ หรือฟันใส่เป้าหมาย Tag \"{enemyTag}\"</color>",
            bodyStyle);
    }

    // =========================================================================
    // RUNTIME SELF-HEALING 4-LAYER FALLBACK (ทำงานได้ทันทีแม้ยังไม่ได้ลาก Prefab)
    // =========================================================================

    private static GameObject BuildRuntime4LayerHitImpactFallback(Vector3 position, Quaternion rotation)
    {
        EnsureFallbackAssets();

        GameObject root = new GameObject("VFX_WeaponHitImpact_Runtime");
        root.transform.SetPositionAndRotation(position, rotation);

        // Layer 1: Core Contact Flare (0.00s - 0.11s, Billboard, Start Size = 1.75, HDR 4.5x)
        ParticleSystem l1 = CreateChildPS(root.transform, "L1_CoreContactFlare");
        var m1 = l1.main;
        m1.duration = 0.15f;
        m1.loop = false;
        m1.playOnAwake = true;
        m1.startDelay = 0.00f;
        m1.startLifetime = 0.11f;
        m1.startSpeed = 0f;
        m1.startSize = 1.75f;
        m1.startColor = Color.white; // HDR 4.5x ขับผ่าน Material._BaseColor
        m1.simulationSpace = ParticleSystemSimulationSpace.Local;
        m1.scalingMode = ParticleSystemScalingMode.Hierarchy;
        m1.maxParticles = 4;

        var e1 = l1.emission;
        e1.rateOverTime = 0f;
        e1.SetBursts(new[] { new ParticleSystem.Burst(0.00f, 1) });
        var sh1 = l1.shape;
        sh1.enabled = false;

        var sol1 = l1.sizeOverLifetime;
        sol1.enabled = true;
        sol1.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0.00f, 0.18f, 8.2f, 8.2f),
            new Keyframe(0.24f, 1.05f, 0.4f, 0.4f),
            new Keyframe(1.00f, 0.00f, -1.8f, -1.8f)));

        var col1 = l1.colorOverLifetime;
        col1.enabled = true;
        Gradient g1 = new Gradient();
        g1.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1.00f, 0.99f, 0.84f), 0.00f),
                new GradientColorKey(new Color(1.00f, 0.86f, 0.36f), 0.55f),
                new GradientColorKey(new Color(1.00f, 0.62f, 0.15f), 1.00f)
            },
            new[]
            {
                new GradientAlphaKey(1.00f, 0.00f),
                new GradientAlphaKey(0.95f, 0.35f),
                new GradientAlphaKey(0.00f, 1.00f)
            });
        col1.color = new ParticleSystem.MinMaxGradient(g1);

        var r1 = l1.GetComponent<ParticleSystemRenderer>();
        r1.renderMode = ParticleSystemRenderMode.Billboard;
        r1.sharedMaterial = fallbackCoreMat;
        r1.shadowCastingMode = ShadowCastingMode.Off;
        r1.receiveShadows = false;
        r1.sortingOrder = 12;

        // Layer 2: Directional Impact Slash Cut (0.01s - 0.20s, Mesh Quad Local, Start Size = 2.4, HDR 3.8x)
        ParticleSystem l2 = CreateChildPS(root.transform, "L2_DirectionalImpactSlash");
        var m2 = l2.main;
        m2.duration = 0.25f;
        m2.loop = false;
        m2.playOnAwake = true;
        m2.startDelay = 0.01f;
        m2.startLifetime = 0.19f;
        m2.startSpeed = 0f;
        m2.startSize = 2.4f;
        m2.startColor = Color.white; // HDR 3.8x ขับผ่าน Material._BaseColor
        m2.simulationSpace = ParticleSystemSimulationSpace.Local;
        m2.scalingMode = ParticleSystemScalingMode.Hierarchy;
        m2.maxParticles = 4;

        var e2 = l2.emission;
        e2.rateOverTime = 0f;
        e2.SetBursts(new[] { new ParticleSystem.Burst(0.00f, 1) });
        var sh2 = l2.shape;
        sh2.enabled = false;

        // Size over Lifetime (Separate Axes): ยืดความยาว X อย่างเร็วใน 0.04s แรก และบีบความหนา Y ให้เรียวแหลมหายไป
        var sol2 = l2.sizeOverLifetime;
        sol2.enabled = true;
        sol2.separateAxes = true;
        sol2.x = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0.00f, 0.22f, 9.5f, 9.5f),
            new Keyframe(0.21f, 1.24f, 0.6f, 0.6f),
            new Keyframe(1.00f, 1.48f, 0.1f, 0.1f)));
        sol2.y = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0.00f, 0.72f, 2.2f, 2.2f),
            new Keyframe(0.16f, 0.92f, -0.5f, -0.5f),
            new Keyframe(0.58f, 0.24f, -1.2f, -1.2f),
            new Keyframe(1.00f, 0.00f, -0.4f, -0.4f)));
        sol2.z = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0.00f, 1.0f),
            new Keyframe(1.00f, 1.0f)));

        var col2 = l2.colorOverLifetime;
        col2.enabled = true;
        Gradient g2 = new Gradient();
        g2.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.92f, 0.99f, 1.00f), 0.00f),
                new GradientColorKey(new Color(0.22f, 0.88f, 1.00f), 0.35f),
                new GradientColorKey(new Color(0.06f, 0.48f, 1.00f), 1.00f)
            },
            new[]
            {
                new GradientAlphaKey(0.00f, 0.00f),
                new GradientAlphaKey(1.00f, 0.08f),
                new GradientAlphaKey(0.88f, 0.50f),
                new GradientAlphaKey(0.00f, 1.00f)
            });
        col2.color = new ParticleSystem.MinMaxGradient(g2);

        var r2 = l2.GetComponent<ParticleSystemRenderer>();
        r2.renderMode = ParticleSystemRenderMode.Mesh;
        r2.mesh = fallbackQuadMesh;
        r2.alignment = ParticleSystemRenderSpace.Local;
        r2.sharedMaterial = fallbackSlashMat;
        r2.shadowCastingMode = ShadowCastingMode.Off;
        r2.receiveShadows = false;
        r2.sortingOrder = 11;

        // Layer 3: High-Velocity Kinetic Hit Sparks (0.02s - 0.36s, Stretched Billboard World, Gravity 0.9, Burst 24)
        ParticleSystem l3 = CreateChildPS(root.transform, "L3_KineticHitSparks");
        var m3 = l3.main;
        m3.duration = 0.42f;
        m3.loop = false;
        m3.playOnAwake = true;
        m3.startDelay = 0.02f;
        m3.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.34f);
        m3.startSpeed = new ParticleSystem.MinMaxCurve(6.8f, 14.5f);
        m3.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
        m3.startColor = Color.white; // HDR 4.2x ขับผ่าน Material._BaseColor
        m3.gravityModifier = 0.9f;
        m3.simulationSpace = ParticleSystemSimulationSpace.World;
        m3.scalingMode = ParticleSystemScalingMode.Hierarchy;
        m3.maxParticles = 36;

        var e3 = l3.emission;
        e3.rateOverTime = 0f;
        e3.SetBursts(new[] { new ParticleSystem.Burst(0.00f, 24) });

        var sh3 = l3.shape;
        sh3.enabled = true;
        sh3.shapeType = ParticleSystemShapeType.Sphere;
        sh3.radius = 0.16f;

        var col3 = l3.colorOverLifetime;
        col3.enabled = true;
        Gradient g3 = new Gradient();
        g3.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1.00f, 0.96f, 0.72f), 0.00f),
                new GradientColorKey(new Color(1.00f, 0.58f, 0.10f), 0.42f),
                new GradientColorKey(new Color(0.95f, 0.22f, 0.02f), 1.00f)
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
        r3.velocityScale = 0.055f;
        r3.lengthScale = 4.0f;
        r3.sharedMaterial = fallbackSparkMat;
        r3.shadowCastingMode = ShadowCastingMode.Off;
        r3.receiveShadows = false;
        r3.sortingOrder = 13;

        // Layer 4: Air Pressure Shockwave Ring (0.03s - 0.24s, Mesh Quad Local, Start Size = 2.15, HDR 2.8x)
        ParticleSystem l4 = CreateChildPS(root.transform, "L4_AirPressureShockwave");
        var m4 = l4.main;
        m4.duration = 0.30f;
        m4.loop = false;
        m4.playOnAwake = true;
        m4.startDelay = 0.03f;
        m4.startLifetime = 0.21f;
        m4.startSpeed = 0f;
        m4.startSize = 2.15f;
        m4.startColor = Color.white; // HDR 2.8x ขับผ่าน Material._BaseColor
        m4.simulationSpace = ParticleSystemSimulationSpace.Local;
        m4.scalingMode = ParticleSystemScalingMode.Hierarchy;
        m4.maxParticles = 4;

        var e4 = l4.emission;
        e4.rateOverTime = 0f;
        e4.SetBursts(new[] { new ParticleSystem.Burst(0.00f, 1) });
        var sh4 = l4.shape;
        sh4.enabled = false;

        var sol4 = l4.sizeOverLifetime;
        sol4.enabled = true;
        sol4.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0.00f, 0.18f, 4.2f, 4.2f),
            new Keyframe(0.42f, 0.92f, 0.9f, 0.9f),
            new Keyframe(1.00f, 1.22f, 0.1f, 0.1f)));

        var col4 = l4.colorOverLifetime;
        col4.enabled = true;
        Gradient g4 = new Gradient();
        g4.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.80f, 0.97f, 1.00f), 0.00f),
                new GradientColorKey(new Color(0.32f, 0.84f, 1.00f), 0.45f),
                new GradientColorKey(new Color(0.14f, 0.62f, 1.00f), 1.00f)
            },
            new[]
            {
                new GradientAlphaKey(0.95f, 0.00f),
                new GradientAlphaKey(0.75f, 0.35f),
                new GradientAlphaKey(0.00f, 1.00f)
            });
        col4.color = new ParticleSystem.MinMaxGradient(g4);

        var r4 = l4.GetComponent<ParticleSystemRenderer>();
        r4.renderMode = ParticleSystemRenderMode.Mesh;
        r4.mesh = fallbackQuadMesh;
        r4.alignment = ParticleSystemRenderSpace.Local;
        r4.sharedMaterial = fallbackWaveMat;
        r4.shadowCastingMode = ShadowCastingMode.Off;
        r4.receiveShadows = false;
        r4.sortingOrder = 10;

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
            if (fallbackQuadMesh == null) fallbackQuadMesh = CreateProceduralQuad();
        }

        if (fallbackSlashTex == null) fallbackSlashTex = CreateProceduralSlashTexture(512, 256);
        if (fallbackCoreTex == null) fallbackCoreTex = CreateProceduralCoreFlareTexture(256, 256);
        if (fallbackWaveTex == null) fallbackWaveTex = CreateProceduralShockwaveTexture(256, 256);

        if (fallbackCoreMat == null) fallbackCoreMat = CreateURPAdditiveMaterial("Mat_Runtime_WeaponHitCore", fallbackCoreTex, 4.5f);
        if (fallbackSlashMat == null) fallbackSlashMat = CreateURPAdditiveMaterial("Mat_Runtime_WeaponHitSlash", fallbackSlashTex, 3.8f);
        if (fallbackSparkMat == null) fallbackSparkMat = CreateURPAdditiveMaterial("Mat_Runtime_WeaponHitSparks", fallbackCoreTex, 4.2f);
        if (fallbackWaveMat == null) fallbackWaveMat = CreateURPAdditiveMaterial("Mat_Runtime_WeaponHitWave", fallbackWaveTex, 2.8f);
    }

    private static Mesh CreateProceduralQuad()
    {
        Mesh m = new Mesh { name = "Runtime_HitQuad" };
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

    private static Texture2D CreateProceduralSlashTexture(int width, int height)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.name = "T_WeaponHitSlash_Runtime";
        tex.wrapMode = TextureWrapMode.Clamp;

        Color[] pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            float ny = (y / (float)(height - 1)) * 2f - 1f;
            for (int x = 0; x < width; x++)
            {
                float nx = (x / (float)(width - 1)) * 2f - 1f;
                float ax = Mathf.Abs(nx);
                if (ax >= 0.99f)
                {
                    pixels[y * width + x] = Color.clear;
                    continue;
                }

                // เรียวแหลมคมกริบทั้ง 2 ด้าน (Double-Tapered Razor Cut)
                float lengthProfile = Mathf.Pow(Mathf.Clamp01(1f - ax), 0.72f);
                float thickness = Mathf.Max(0.012f, 0.20f * lengthProfile);
                float razorCore = Mathf.Exp(-(ny * ny) / (thickness * thickness * 0.18f));
                float cyanHalo = Mathf.Exp(-(ny * ny) / (thickness * thickness * 1.15f)) * 0.65f;
                float tipMask = 1f - SmoothStep(0.84f, 0.99f, ax);

                float val = Mathf.Clamp01((razorCore + cyanHalo) * tipMask);
                Color col = Color.Lerp(new Color(0.55f, 0.92f, 1.0f, 1f), Color.white, Mathf.Clamp01(razorCore * 1.25f));
                col.a = val;
                pixels[y * width + x] = col;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    private static Texture2D CreateProceduralCoreFlareTexture(int width, int height)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.name = "T_WeaponHitCore_Runtime";
        tex.wrapMode = TextureWrapMode.Clamp;

        Color[] pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            float ny = (y / (float)(height - 1)) * 2f - 1f;
            for (int x = 0; x < width; x++)
            {
                float nx = (x / (float)(width - 1)) * 2f - 1f;
                float r = Mathf.Sqrt(nx * nx + ny * ny);
                float edgeMask = 1f - SmoothStep(0.76f, 0.98f, r);

                float sphereCore = Mathf.Exp(-r * r * 14.0f);
                float softHalo = Mathf.Exp(-r * r * 4.2f) * 0.48f;
                // แฉกแสงแนวนอนสไตล์ Anamorphic Contact Flare
                float horizontalStreak = Mathf.Exp(-Mathf.Abs(ny) * 16.0f) * Mathf.Exp(-Mathf.Abs(nx) * 2.1f) * 0.92f;
                float verticalGlint = Mathf.Exp(-Mathf.Abs(nx) * 20.0f) * Mathf.Exp(-Mathf.Abs(ny) * 5.2f) * 0.35f;

                float val = Mathf.Clamp01((sphereCore + softHalo + horizontalStreak + verticalGlint) * edgeMask);
                pixels[y * width + x] = new Color(val, val, val, val);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    private static Texture2D CreateProceduralShockwaveTexture(int width, int height)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.name = "T_WeaponHitWave_Runtime";
        tex.wrapMode = TextureWrapMode.Clamp;

        Color[] pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            float ny = (y / (float)(height - 1)) * 2f - 1f;
            for (int x = 0; x < width; x++)
            {
                float nx = (x / (float)(width - 1)) * 2f - 1f;
                float r = Mathf.Sqrt(nx * nx + ny * ny);
                if (r >= 0.98f)
                {
                    pixels[y * width + x] = Color.clear;
                    continue;
                }

                float outerCut = 1f - SmoothStep(0.84f, 0.95f, r);
                float innerFade = SmoothStep(0.54f, 0.80f, r);
                float crispRim = Mathf.Exp(-Mathf.Pow((r - 0.82f) / 0.042f, 2f));
                float val = Mathf.Clamp01((innerFade * outerCut * 0.65f + crispRim * 0.98f) * outerCut);
                pixels[y * width + x] = new Color(val, val, val, val);
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
