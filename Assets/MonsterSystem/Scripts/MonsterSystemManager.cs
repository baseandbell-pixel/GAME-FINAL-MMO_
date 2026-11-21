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

    [Header("Master Control & Shared Loot Prefab")]
    [Tooltip("Uncheck to pause all managed monster zones in this scene at once.")]
    public bool enableAllZones = true;
    [Tooltip("Optional shared WorldItem prefab for all monsters in this scene (falls back to each monster prefab's WorldItem if empty).")]
    public WorldItem sharedWorldItemPrefab = null;

    [Header("Global Scene Multipliers (Quick Balancing / Events)")]
    [Tooltip("Multiplies MaxHealth of all MonsterSystem monsters in this scene.")]
    [Min(0.1f)] public float globalHealthMultiplier = 1.0f;
    [Tooltip("Multiplies AttackDamage of all MonsterSystem monsters in this scene.")]
    [Min(0.1f)] public float globalDamageMultiplier = 1.0f;
    [Tooltip("Speeds up or slows down respawn timers across all managed zones (e.g. 3.0 = 3x faster respawns for testing).")]
    [Range(0.1f, 10f)] public float globalRespawnSpeedMultiplier = 1.0f;
    [Tooltip("Multiplies loot drop rates across all managed monsters (e.g. 2.0 = 2x Drop Event, 0 = disable drops).")]
    [Range(0f, 5f)] public float globalDropRateMultiplier = 1.0f;

    [Header("Managed Monster Zones (Auto-Collected from Children if Empty)")]
    public List<MonsterSpawnZone> managedZones = new List<MonsterSpawnZone>();

    [Header("Loose-Coupled UnityEvents (Plug Friend's Quest / Reward Systems Here Later)")]
    public UnityEvent<MonsterEnemyNPC, PlayerCombat> onAnyMonsterKilled;
    public UnityEvent<MonsterEnemyNPC> onAnyMonsterSpawned;

    private void Awake()
    {
        Active = this;
        CollectChildZonesIfEmpty();
    }

    private void OnEnable()
    {
        if (Active == null) Active = this;
    }

    private void OnDisable()
    {
        if (Active == this) Active = null;
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
