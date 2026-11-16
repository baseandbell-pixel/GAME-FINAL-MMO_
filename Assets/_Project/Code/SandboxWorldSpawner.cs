using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// Dev-only: populates a sandbox with enemies and loot, and spawns a physical Tiered Reward Treasure Chest
/// at the camp center once all enemies in the camp are defeated.
/// Runs population on the Shared Mode MASTER CLIENT only, once, after the local player has spawned.
/// </summary>
public class SandboxWorldSpawner : MonoBehaviour
{
    private const int ExpectedCampSize = 4;

    [Serializable]
    public class ItemSpawn
    {
        public ItemData item;
        [Min(1)] public int quantity = 1;
        public Transform point;
    }

    [Header("Enemies")]
    [SerializeField] private EnemyNPC enemyPrefab;
    [Tooltip("All four spawn points belong to this shared aggro group.")]
    [SerializeField] private int campId = 1;
    [SerializeField] private Transform[] enemyPoints;

    [Header("Loot & Camp Clear Reward")]
    [SerializeField] private WorldItem itemPrefab;
    [SerializeField] private ItemSpawn[] items;
    [Tooltip("When true, defeating all enemies in this camp spawns a physical Treasure Chest at the camp center.")]
    [SerializeField] private bool spawnRewardChestOnCampClear = true;

    [Tooltip("Seconds to wait after the local player appears. Keep at zero for immediate solo sandbox population.")]
    [SerializeField] private float settleSeconds = 0f;

    [Header("Sandbox collision test geometry")]
    [SerializeField] private bool createTestObstacles = true;

    public LootChest SpawnedRewardChest => _spawnedChest;
    public bool IsCampCleared => _chestSpawned;

    private readonly List<EnemyNPC> _trackedCampEnemies = new List<EnemyNPC>(ExpectedCampSize);
    private LootChest _spawnedChest;
    private bool _done;
    private bool _campEverActive;
    private bool _chestSpawned;
    private float _nextAttempt;
    private float _masterReadyAt = -1f;

    private void Awake()
    {
        if (createTestObstacles) CreateTestObstacles();
    }

    private void Update()
    {
        if (_done)
        {
            MonitorCampClear();
            return;
        }

        if (Time.unscaledTime < _nextAttempt) return;

        var local = PlayerCombat.Local;
        if (local == null || !local.IsSpawned)
        {
            _masterReadyAt = -1f;
            return;
        }

        var runner = local.Runner;
        if (runner == null || !runner.IsRunning) return;

        if (!runner.IsSharedModeMasterClient)
        {
            // Non-master clients still observe camp clear once NPCs replicate
            if (EnemyNPC.All.Count > 0)
            {
                _done = true;
            }
            return;
        }

        if (_masterReadyAt < 0f)
        {
            _masterReadyAt = Time.unscaledTime;
            Debug.Log($"[WorldSpawner] Shared master ready; spawning camp after {settleSeconds:0.00}s (solo safe).");
        }
        if (Time.unscaledTime - _masterReadyAt < settleSeconds) return;

        if (EnemyNPC.All.Count > 0 || WorldItem.All.Count > 0)
        {
            Debug.Log("[WorldSpawner] World already populated - nothing to spawn.");
            _done = true;
            return;
        }

        _done = Populate(runner);
        if (!_done)
        {
            _nextAttempt = Time.unscaledTime + .5f;
            Debug.LogWarning("[WorldSpawner] Population attempt incomplete; retrying in 0.5 seconds.");
        }
    }

    private void MonitorCampClear()
    {
        if (!spawnRewardChestOnCampClear || _chestSpawned) return;

        int livingInCamp = 0;
        for (int i = 0; i < EnemyNPC.All.Count; i++)
        {
            EnemyNPC npc = EnemyNPC.All[i];
            if (npc == null || !npc.IsSpawned) continue;
            if (npc.CampId != campId && npc.CampId != 0) continue;

            if (!_trackedCampEnemies.Contains(npc))
                _trackedCampEnemies.Add(npc);

            if (npc.IsAlive)
            {
                _campEverActive = true;
                livingInCamp++;
            }
        }

        if (!_campEverActive && _trackedCampEnemies.Count > 0)
        {
            _campEverActive = true;
        }

        if (_campEverActive && livingInCamp == 0)
        {
            _chestSpawned = true;
            Vector3 center = GetCampCenter();
            _spawnedChest = LootChest.SpawnAt(center, campId, itemPrefab);
            Debug.Log($"[WorldSpawner] Camp {campId} CLEARED! Spawned Reward Treasure Chest at {center}.");
        }
    }

    public Vector3 GetCampCenter()
    {
        if (enemyPoints != null && enemyPoints.Length > 0)
        {
            Vector3 sum = Vector3.zero;
            int valid = 0;
            for (int i = 0; i < enemyPoints.Length; i++)
            {
                if (enemyPoints[i] == null) continue;
                sum += enemyPoints[i].position;
                valid++;
            }
            if (valid > 0)
            {
                Vector3 avg = sum / valid;
                avg.y = 0f;
                return avg;
            }
        }
        return new Vector3(20f, 0f, 8f);
    }

    private bool Populate(NetworkRunner runner)
    {
        Debug.Log("[WorldSpawner] This client is the master - spawning enemies and loot.");

        int enemies = 0;
        _trackedCampEnemies.Clear();
        if (enemyPrefab == null) Debug.LogWarning("[WorldSpawner] No enemyPrefab assigned.");
        else if (enemyPoints == null || enemyPoints.Length != ExpectedCampSize)
        {
            Debug.LogError($"[WorldSpawner] Expected exactly {ExpectedCampSize} enemy points for camp {campId}, found {(enemyPoints == null ? 0 : enemyPoints.Length)}. Camp spawn aborted to prevent a partial encounter.");
        }
        else
        {
            for (int i = 0; i < ExpectedCampSize; i++)
            {
                var p = enemyPoints[i];
                if (p == null)
                {
                    Debug.LogError($"[WorldSpawner] Camp {campId} point {i + 1} is null. Camp spawn aborted before a partial encounter.");
                    break;
                }

                var npc = runner.Spawn(enemyPrefab, p.position, p.rotation, null, (r, o) =>
                {
                    var enemy = o.GetComponent<EnemyNPC>();
                    if (enemy != null) enemy.CampId = campId;
                });
                if (npc != null)
                {
                    enemies++;
                    _trackedCampEnemies.Add(npc);
                }
                else Debug.LogWarning($"[WorldSpawner] Enemy spawn failed at {p.name}.");
            }
        }

        if (enemies > 0) _campEverActive = true;

        int loot = 0;
        if (itemPrefab == null) Debug.LogWarning("[WorldSpawner] No itemPrefab assigned.");
        else if (items != null)
        {
            foreach (var s in items)
            {
                if (s == null || s.item == null || s.point == null) continue;
                if (WorldItem.Spawn(runner, itemPrefab, s.point.position, s.item, s.quantity) != null) loot++;
            }
        }

        if (enemies != ExpectedCampSize)
            Debug.LogWarning($"[WorldSpawner] Camp {campId} spawned {enemies}/{ExpectedCampSize} NPCs. Check NetworkPrefab registration and the point references.");
        else
            Debug.Log($"[WorldSpawner] Camp {campId} spawned exactly {ExpectedCampSize} hostile NPCs.");
        Debug.Log($"[WorldSpawner] Spawned {enemies} enemies and {loot} loot items.");
        return enemies == ExpectedCampSize;
    }

    /// <summary>Creates deterministic local static geometry on every peer. It is intentionally not networked:
    /// level collision must be identical scene data, not a replicated dynamic object.</summary>
    private void CreateTestObstacles()
    {
        const string rootName = "SandboxTestObstacles";
        if (transform.Find(rootName) != null) return;

        Transform root = new GameObject(rootName).transform;
        root.SetParent(transform, false);
        CreateObstacle(root, "Pillar_West", new Vector3(8f, 1.5f, 3f), new Vector3(1.4f, 3f, 1.4f));
        CreateObstacle(root, "Pillar_East", new Vector3(11f, 1.5f, 7f), new Vector3(1.4f, 3f, 1.4f));
        CreateObstacle(root, "LowWall_South", new Vector3(10f, 1f, 0f), new Vector3(7f, 2f, .65f));
        CreateObstacle(root, "CampBarrier", new Vector3(18f, 1f, 4f), new Vector3(5f, 2f, .65f));
        Debug.Log("[WorldSpawner] Created 4 static sandbox obstacles for movement, camera collision, and melee line testing.");
    }

    private static void CreateObstacle(Transform parent, string obstacleName, Vector3 position, Vector3 scale)
    {
        GameObject obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obstacle.name = obstacleName;
        obstacle.transform.SetParent(parent, true);
        obstacle.transform.position = position;
        obstacle.transform.localScale = scale;
        var renderer = obstacle.GetComponent<Renderer>();
        if (renderer != null)
        {
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor("_BaseColor", new Color(.2f, .25f, .32f));
            block.SetColor("_Color", new Color(.2f, .25f, .32f));
            renderer.SetPropertyBlock(block);
        }
    }
}
