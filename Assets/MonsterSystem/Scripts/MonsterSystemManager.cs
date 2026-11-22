using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Single-object Scene Director / Hub for the self-contained Monster System (<c>Assets/MonsterSystem/</c>).
/// Drop <c>MonsterSystem_Manager.prefab</c> into ANY friend's scene to organize all monster zones
/// (<c>Zone_Orc</c>, <c>Zone_Goblin</c>, etc.) under one clean Hierarchy object with flexible global multipliers
/// and loose-coupled kill/spawn event hooks!
///
/// NOTE: Individual <see cref="MonsterSpawnZone"/> prefabs also remain 100% playable standalone outside this manager.
/// </summary>
[DisallowMultipleComponent]
public class MonsterSystemManager : MonoBehaviour
{
    public static MonsterSystemManager Active { get; private set; }

    /// <summary>Global C# event raised whenever any MonsterEnemyNPC is killed (ideal for future Quest/Achievement hooks).</summary>
    public static event Action<MonsterEnemyNPC, PlayerCombat> AnyMonsterKilled;

    /// <summary>Global C# event raised whenever any MonsterEnemyNPC spawns in the scene.</summary>
    public static event Action<MonsterEnemyNPC> AnyMonsterSpawned;

    [Header("1. ควบคุมหลัก & พรีแฟบของดรอปกลาง")]
    [Tooltip("ติ๊กออกเพื่อหยุดการเกิดของมอนสเตอร์ทุกโซนในฉากนี้ชั่วคราว")]
    public bool enableAllZones = true;
    [Tooltip("ใส่ WorldItem.prefab ของเพื่อนไว้ที่นี่ที่เดียว ทุกโซนจะใช้ร่วมกัน")]
    public WorldItem sharedWorldItemPrefab = null;

    [Header("2. ตัวคูณปรับทั้งแมป (ปรับสมดุลหรือจัดอีเวนต์ได้จากจุดเดียว)")]
    [Tooltip("ตัวคูณเลือดของมอนสเตอร์ทั้งหมดในฉากนี้")]
    [Min(0.1f)] public float globalHealthMultiplier = 1.0f;
    [Tooltip("ตัวคูณพลังโจมตีของมอนสเตอร์ทั้งหมดในฉากนี้")]
    [Min(0.1f)] public float globalDamageMultiplier = 1.0f;
    [Tooltip("ความเร็วในการเกิดใหม่ของทุกโซน (เช่น 3.0 = เกิดเร็วขึ้น 3 เท่าไว้เทส)")]
    [Range(0.1f, 10f)] public float globalRespawnSpeedMultiplier = 1.0f;
    [Tooltip("ตัวคูณเรทดรอปของทั้งฉาก (เช่น 2.0 = อีเวนต์คูณดรอป 2 เท่า | 0 = ปิดดรอป)")]
    [Range(0f, 5f)] public float globalDropRateMultiplier = 1.0f;

    [Header("3. รายชื่อโซนที่คุมอยู่ (รวบอัตโนมัติจากลูกใต้ตัวนี้)")]
    [Tooltip("เปิดไว้ (แนะนำ): อัปเดตรายชื่อโซนลูกอัตโนมัติทุกครั้งที่ลากโซนเข้า/ออกใต้ตัวคุมนี้")]
    public bool autoSyncChildZones = true;
    public List<MonsterSpawnZone> managedZones = new List<MonsterSpawnZone>();

    [Header("4. โซนปลอดภัยสำรอง (ทำงานอัตโนมัติทันทีถ้าลบ SandboxWorldSpawner ออก)")]
    [Tooltip("เปิดไว้ (แนะนำ): ถ้าฉากไม่มี SandboxWorldSpawner หรือ SafeZone อยู่ ระบบจะสร้าง Comfort Safe Zone ให้อัตโนมัติ")]
    public bool ensureFallbackSafeZoneIfMissing = true;
    public Vector3 fallbackSafeZoneCenter = new Vector3(0f, 0f, -60f);
    [Min(3f)] public float fallbackSafeZoneRadius = 12f;

    [Header("5. ช่องเสียบ Event เผื่อเชื่อมระบบเควส/รางวัลของเพื่อนในอนาคต")]
    public UnityEvent<MonsterEnemyNPC, PlayerCombat> onAnyMonsterKilled;
    public UnityEvent<MonsterEnemyNPC> onAnyMonsterSpawned;

    private void Awake()
    {
        Active = this;
        PlayerSaveManager.EnsureExists();
        if (autoSyncChildZones)
            RefreshChildZones();
        else
            CollectChildZonesIfEmpty();
    }

    private void Start()
    {
        if (ensureFallbackSafeZoneIfMissing && SafeZone.Instance == null)
        {
            SafeZone.EnsureAt(fallbackSafeZoneCenter, fallbackSafeZoneRadius);
        }
    }

    private void OnTransformChildrenChanged()
    {
        if (autoSyncChildZones)
            RefreshChildZones();
    }

    private void OnEnable()
    {
        if (Active == null) Active = this;
    }

    private void OnDisable()
    {
        if (Active == this) Active = null;
    }

    private void OnDrawGizmosSelected()
    {
        if (!ensureFallbackSafeZoneIfMissing) return;
        Gizmos.color = new Color(0.22f, 0.92f, 0.64f, 0.75f);
        const int segs = 36;
        Vector3 prev = fallbackSafeZoneCenter + new Vector3(fallbackSafeZoneRadius, 0.15f, 0f);
        for (int i = 1; i <= segs; i++)
        {
            float a = (i / (float)segs) * Mathf.PI * 2f;
            Vector3 next = fallbackSafeZoneCenter + new Vector3(Mathf.Cos(a) * fallbackSafeZoneRadius, 0.15f, Mathf.Sin(a) * fallbackSafeZoneRadius);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }

    /// <summary>Scans child objects for <see cref="MonsterSpawnZone"/> components if the list is empty.</summary>
    public void CollectChildZonesIfEmpty()
    {
        if (managedZones == null) managedZones = new List<MonsterSpawnZone>();
        managedZones.RemoveAll(z => z == null);

        if (managedZones.Count == 0)
            RefreshChildZones();
    }

    /// <summary>Rebuilds <see cref="managedZones"/> from all child <see cref="MonsterSpawnZone"/> objects.</summary>
    public void RefreshChildZones()
    {
        if (managedZones == null) managedZones = new List<MonsterSpawnZone>();
        managedZones.Clear();
        MonsterSpawnZone[] children = GetComponentsInChildren<MonsterSpawnZone>(includeInactive: true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] != null && !managedZones.Contains(children[i]))
                managedZones.Add(children[i]);
        }
    }

    public bool IsZoneAllowedToOperate(MonsterSpawnZone zone)
    {
        if (!enableAllZones) return false;
        if (zone == null) return false;
        return zone.isActiveAndEnabled;
    }

    public static void NotifyMonsterSpawned(MonsterEnemyNPC monster)
    {
        if (monster == null) return;
        try { AnyMonsterSpawned?.Invoke(monster); }
        catch (Exception ex) { Debug.LogException(ex); }

        if (Active != null)
        {
            try { Active.onAnyMonsterSpawned?.Invoke(monster); }
            catch (Exception ex) { Debug.LogException(ex); }
        }
    }

    public static void NotifyMonsterKilled(MonsterEnemyNPC monster, PlayerCombat killer)
    {
        if (monster == null) return;
        try { AnyMonsterKilled?.Invoke(monster, killer); }
        catch (Exception ex) { Debug.LogException(ex); }

        if (Active != null)
        {
            try { Active.onAnyMonsterKilled?.Invoke(monster, killer); }
            catch (Exception ex) { Debug.LogException(ex); }
        }
    }
}
