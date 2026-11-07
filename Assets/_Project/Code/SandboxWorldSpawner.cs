using System;
using Fusion;
using UnityEngine;

/// <summary>
/// Dev-only: populates a sandbox with enemies and loot. Runs on the Shared Mode MASTER CLIENT only, once, after the
/// local player has spawned (which proves the runner/simulation is ready), and only if the world is still empty -
/// so two clients joining the same session never double-spawn.
/// The master becomes State Authority of what it spawns; if it leaves, Fusion hands the objects to another client.
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

    [Header("Loot")]
    [SerializeField] private WorldItem itemPrefab;
    [SerializeField] private ItemSpawn[] items;

    [Tooltip("Seconds to wait after the local player appears. Keep at zero for immediate solo sandbox population.")]
    [SerializeField] private float settleSeconds = 0f;

    [Header("Sandbox collision test geometry")]
    [SerializeField] private bool createTestObstacles = true;

    private bool _done;
    private float _nextAttempt;
    private float _masterReadyAt = -1f;

    private void Awake()
    {
        if (createTestObstacles) CreateTestObstacles();
    }

    private void Update()
    {
        if (_done) return;
        if (Time.unscaledTime < _nextAttempt) return;

        // Fusion reports IsRunning before its scene registration completes in a direct sandbox
        // launch. The locally spawned PlayerCombat is the reliable ready barrier; delay remains
        // zero, so this does NOT wait for other players or a full lobby.
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
            // Do not mark done: a master handoff must allow this peer to take responsibility.
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

    private bool Populate(NetworkRunner runner)
    {
        Debug.Log("[WorldSpawner] This client is the master - spawning enemies and loot.");

        int enemies = 0;
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
                if (npc != null) enemies++;
                else Debug.LogWarning($"[WorldSpawner] Enemy spawn failed at {p.name}.");
            }
        }

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
