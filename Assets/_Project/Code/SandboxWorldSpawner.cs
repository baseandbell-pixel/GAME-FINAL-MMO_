using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// Manages the 4 Normal Enemy Camps (~7-block spacing, 90s / 1:30 respawn timer),
/// the Dedicated Solo Boss Arena (240s / 4:00 respawn timer), Boss Arena Proximity Trigger Zone,
/// and Camp/Boss Reward Treasure Chests in Dev_Kulpat.unity.
/// </summary>
public class SandboxWorldSpawner : MonoBehaviour
{
    public static SandboxWorldSpawner Instance { get; private set; }

    private const int ExpectedCampSize = 4;
    public const int BossCampId = 100;
    public const float DefaultCampRespawnSeconds = 90f;   // 1:30 minutes
    public const float DefaultBossRespawnSeconds = 240f;  // 4:00 minutes

    [Serializable]
    public class ItemSpawn
    {
        public ItemData item;
        [Min(1)] public int quantity = 1;
        public Transform point;
    }

    [Serializable]
    public class CampRuntimeState
    {
        public int CampId;
        public string CampName;
        public Vector3 Center;
        public Vector3[] SpawnPositions;
        public bool EverPopulated;
        public bool IsCleared;
        public float RespawnRemainingSeconds;
        public LootChest ActiveChest;
        public readonly List<EnemyNPC> ActiveEnemies = new List<EnemyNPC>(4);

        public string GetFormattedRespawnTime()
        {
            int totalSec = Mathf.CeilToInt(Mathf.Max(0f, RespawnRemainingSeconds));
            int mins = totalSec / 60;
            int secs = totalSec % 60;
            return $"{mins:00}:{secs:00}";
        }
    }

    [Header("Enemies & Camps (4 Camps, ~7-Block Spacing)")]
    [SerializeField] private EnemyNPC enemyPrefab;
    [Tooltip("Primary camp ID (Camp 1 uses scene enemyPoints if assigned; Camps 2..4 are generated at ~7-block spacing).")]
#pragma warning disable CS0414
    [SerializeField] private int campId = 1;
#pragma warning restore CS0414
    [SerializeField] private Transform[] enemyPoints;
    [Min(5f)] [SerializeField] private float campRespawnDelaySeconds = DefaultCampRespawnSeconds;

    [Header("Dedicated Boss Arena (4:00 Respawn & Proximity Zone)")]
    [SerializeField] private Vector3 bossArenaCenter = new Vector3(-26f, 0f, -16f);
    [Min(5f)] [SerializeField] private float bossArenaProximityRadius = 18f;
    [Min(5f)] [SerializeField] private float bossRespawnDelaySeconds = DefaultBossRespawnSeconds;

    [Header("Comfort Safe Zone (Non-Combat & Reconnect Anchor)")]
    [SerializeField] private Vector3 safeZoneCenter = new Vector3(-14f, 0f, 8f);
    [Min(3f)] [SerializeField] private float safeZoneRadius = 7.5f;

    [Header("Loot & Camp Clear Reward")]
    [SerializeField] private WorldItem itemPrefab;
    [SerializeField] private ItemSpawn[] items;
    [SerializeField] private bool spawnRewardChestOnCampClear = true;

    [Tooltip("Seconds to wait after the local player appears. Keep at zero for immediate solo sandbox population.")]
    [SerializeField] private float settleSeconds = 0f;

    [Header("Sandbox collision test geometry")]
    [SerializeField] private bool createTestObstacles = true;

    // ---- Public Inspection & Phase 2 UI Hooks ----
    public IReadOnlyList<CampRuntimeState> Camps => _camps;
    public Vector3 BossArenaCenter => bossArenaCenter;
    public float BossArenaProximityRadius => bossArenaProximityRadius;
    public float CampRespawnDelaySeconds => campRespawnDelaySeconds;
    public float BossRespawnDelaySeconds => bossRespawnDelaySeconds;

    public EnemyNPC ActiveBoss => _activeBoss;
    public LootChest ActiveBossChest => _activeBossChest;
    public bool IsBossAlive => _activeBoss != null && _activeBoss.IsAlive;
    public bool IsBossRespawning => _bossCleared && _bossRespawnRemaining > 0f;
    public float BossRespawnRemainingSeconds => Mathf.Max(0f, _bossRespawnRemaining);
    public bool IsLocalPlayerInBossArena { get; private set; }
    public bool ShouldShowBossRespawnBanner => IsLocalPlayerInBossArena && IsBossRespawning;

    /// <summary>Raised when the local player enters (true) or exits (false) the Boss Arena proximity zone.</summary>
    public event Action<bool> BossArenaProximityChanged;

    // Legacy compatibility properties for Camp 1
    public LootChest SpawnedRewardChest => _camps.Count > 0 ? _camps[0].ActiveChest : null;
    public bool IsCampCleared => _camps.Count > 0 && _camps[0].IsCleared;

    private readonly List<CampRuntimeState> _camps = new List<CampRuntimeState>(4);
    private EnemyNPC _activeBoss;
    private LootChest _activeBossChest;
    private bool _bossEverSpawned;
    private bool _bossCleared;
    private float _bossRespawnRemaining;

    private bool _done;
    private float _nextAttempt;
    private float _masterReadyAt = -1f;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private void Awake()
    {
        Instance = this;
        PlayerSaveManager.EnsureExists();
        SafeZone.EnsureAt(safeZoneCenter, safeZoneRadius);
        InitializeCampDefinitions();
        if (createTestObstacles)
        {
            CreateTestObstacles();
            CreateBossArenaGeometry();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Configures the 4 normal enemy camps positioned ~7 blocks apart (~18m center-to-center, ~14m edge-to-edge)
    /// so aggro zones never overlap.
    /// </summary>
    private void InitializeCampDefinitions()
    {
        _camps.Clear();

        // Camp 1: East Camp (center 18, 0, 8)
        Vector3[] c1Points = new Vector3[ExpectedCampSize];
        if (enemyPoints != null && enemyPoints.Length == ExpectedCampSize && enemyPoints[0] != null)
        {
            for (int i = 0; i < ExpectedCampSize; i++)
                c1Points[i] = enemyPoints[i] != null ? enemyPoints[i].position : new Vector3(18f, 1.2f, 8f);
        }
        else
        {
            c1Points[0] = new Vector3(16f, 1.2f, 6f);
            c1Points[1] = new Vector3(20f, 1.2f, 6f);
            c1Points[2] = new Vector3(16f, 1.2f, 10f);
            c1Points[3] = new Vector3(20f, 1.2f, 10f);
        }
        _camps.Add(BuildCampState(1, "Camp 1 (East)", c1Points));

        // Camp 2: North-East Camp (~7 blocks north of Camp 1, center 18, 0, 26)
        _camps.Add(BuildCampState(2, "Camp 2 (North-East)", new[]
        {
            new Vector3(16f, 1.2f, 24f),
            new Vector3(20f, 1.2f, 24f),
            new Vector3(16f, 1.2f, 28f),
            new Vector3(20f, 1.2f, 28f)
        }));

        // Camp 3: South-East Camp (~7 blocks south of Camp 1, center 18, 0, -10)
        _camps.Add(BuildCampState(3, "Camp 3 (South-East)", new[]
        {
            new Vector3(16f, 1.2f, -12f),
            new Vector3(20f, 1.2f, -12f),
            new Vector3(16f, 1.2f, -8f),
            new Vector3(20f, 1.2f, -8f)
        }));

        // Camp 4: North-West Camp (~7 blocks west of Camp 2, center 0, 0, 26)
        _camps.Add(BuildCampState(4, "Camp 4 (North-West)", new[]
        {
            new Vector3(-2f, 1.2f, 24f),
            new Vector3(2f, 1.2f, 24f),
            new Vector3(-2f, 1.2f, 28f),
            new Vector3(2f, 1.2f, 28f)
        }));
    }

    private static CampRuntimeState BuildCampState(int id, string campName, Vector3[] points)
    {
        Vector3 sum = Vector3.zero;
        for (int i = 0; i < points.Length; i++) sum += points[i];
        Vector3 center = sum / Mathf.Max(1, points.Length);
        center.y = 0f;

        return new CampRuntimeState
        {
            CampId = id,
            CampName = campName,
            Center = center,
            SpawnPositions = points
        };
    }

    private void Update()
    {
        UpdateBossArenaProximity();

        if (_done)
        {
            TickCampsAndBossLifecycle(Time.deltaTime);
            return;
        }

        if (Time.unscaledTime < _nextAttempt) return;

        var local = PlayerCombat.Local;
        if (local == null || !local.IsSpawned)
        {
            _masterReadyAt = -1f;
            return;
        }

        // Wait until PlayerSaveManager has loaded/initialized the player's level before initial enemy scaling
        if (PlayerSaveManager.Instance != null && !PlayerSaveManager.Instance.HasLoadedOrInitialized)
        {
            return;
        }

        var runner = local.Runner;
        if (runner == null || !runner.IsRunning) return;

        if (!runner.IsSharedModeMasterClient)
        {
            if (EnemyNPC.All.Count > 0)
            {
                SyncExistingEnemiesToCamps();
                _done = true;
            }
            return;
        }

        if (_masterReadyAt < 0f)
        {
            _masterReadyAt = Time.unscaledTime;
            Debug.Log($"[WorldSpawner] Shared master ready; spawning 4 camps + Boss after {settleSeconds:0.00}s.");
        }
        if (Time.unscaledTime - _masterReadyAt < settleSeconds) return;

        if (EnemyNPC.All.Count > 0 || WorldItem.All.Count > 0)
        {
            Debug.Log("[WorldSpawner] World already populated - syncing state.");
            SyncExistingEnemiesToCamps();
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

    private void UpdateBossArenaProximity()
    {
        var local = PlayerCombat.Local;
        bool inZone = false;
        if (local != null && local.IsSpawned && !local.IsDead)
        {
            Vector3 a = local.transform.position;
            Vector3 b = bossArenaCenter;
            a.y = 0f;
            b.y = 0f;
            inZone = Vector3.Distance(a, b) <= bossArenaProximityRadius;
        }

        if (inZone != IsLocalPlayerInBossArena)
        {
            IsLocalPlayerInBossArena = inZone;
            Debug.Log($"[WorldSpawner] Local player {(inZone ? "ENTERED" : "EXITED")} Boss Arena proximity zone (cooldownActive={IsBossRespawning}, text='{GetBossRespawnCountdownText()}').");
            BossArenaProximityChanged?.Invoke(inZone);
        }
    }

    /// <summary>
    /// Returns the formatted English countdown string ("Boss respawns in MM:SS") while the Boss is on respawn cooldown.
    /// </summary>
    public string GetBossRespawnCountdownText()
    {
        if (!IsBossRespawning) return string.Empty;
        int totalSec = Mathf.CeilToInt(Mathf.Max(0f, _bossRespawnRemaining));
        int mins = totalSec / 60;
        int secs = totalSec % 60;
        return $"Boss respawns in {mins:00}:{secs:00}";
    }

    private void TickCampsAndBossLifecycle(float dt)
    {
        var local = PlayerCombat.Local;
        NetworkRunner runner = local != null ? local.Runner : null;
        bool isMaster = runner != null && runner.IsRunning && runner.IsSharedModeMasterClient;

        // 1. Monitor & Respawn the 4 Normal Enemy Camps (90s / 1:30 respawn timer)
        for (int c = 0; c < _camps.Count; c++)
        {
            CampRuntimeState camp = _camps[c];
            int living = CountLivingInCamp(camp.CampId, isBoss: false);

            if (living > 0)
            {
                camp.EverPopulated = true;
                camp.IsCleared = false;
                camp.RespawnRemainingSeconds = 0f;
            }
            else if (camp.EverPopulated && !camp.IsCleared)
            {
                camp.IsCleared = true;
                camp.RespawnRemainingSeconds = campRespawnDelaySeconds;
                if (spawnRewardChestOnCampClear)
                {
                    camp.ActiveChest = LootChest.SpawnAt(camp.Center, camp.CampId, itemPrefab, isBossChest: false, replaceExisting: true);
                }
                AwardCampClearGold(local, camp.CampName, isBoss: false);
                Debug.Log($"[WorldSpawner] {camp.CampName} CLEARED! Reward Chest + Gold awarded. Respawning in {camp.GetFormattedRespawnTime()}.");
            }
            else if (camp.IsCleared)
            {
                camp.RespawnRemainingSeconds = Mathf.Max(0f, camp.RespawnRemainingSeconds - dt);
                if (camp.RespawnRemainingSeconds <= 0f && isMaster)
                {
                    int spawned = SpawnCampEnemies(runner, camp);
                    if (spawned > 0)
                    {
                        camp.IsCleared = false;
                        Debug.Log($"[WorldSpawner] {camp.CampName} RESPAWNED ({spawned} enemies).");
                    }
                }
            }
        }

        // 2. Monitor & Respawn the Solo Boss in Boss Arena (240s / 4:00 respawn timer)
        int livingBoss = CountLivingInCamp(BossCampId, isBoss: true);
        if (livingBoss > 0)
        {
            _bossEverSpawned = true;
            _bossCleared = false;
            _bossRespawnRemaining = 0f;
        }
        else if (!_bossEverSpawned && !_bossCleared && isMaster)
        {
            // รับประกันว่าหากบอสยังไม่เคยถูกเสก (เช่น SyncExistingEnemiesToCamps ทำงานก่อน) จะสั่งเสกบอสทันที
            SpawnBoss(runner);
        }
        else if (_bossEverSpawned && !_bossCleared)
        {
            _bossCleared = true;
            _bossRespawnRemaining = bossRespawnDelaySeconds;
            if (spawnRewardChestOnCampClear)
            {
                Vector3 chestPos = bossArenaCenter + new Vector3(0f, 0f, 2.2f);
                _activeBossChest = LootChest.SpawnAt(chestPos, BossCampId, itemPrefab, isBossChest: true, replaceExisting: true);
            }
            AwardCampClearGold(local, "Boss Sovereign Golem", isBoss: true);
            Debug.Log($"[WorldSpawner] BOSS DEFEATED! Boss Reward Chest + Gold awarded. {GetBossRespawnCountdownText()} ({bossRespawnDelaySeconds:0}s).");
        }
        else if (_bossCleared)
        {
            _bossRespawnRemaining = Mathf.Max(0f, _bossRespawnRemaining - dt);
            if (_bossRespawnRemaining <= 0f && isMaster)
            {
                if (SpawnBoss(runner) != null)
                {
                    _bossCleared = false;
                    Debug.Log("[WorldSpawner] BOSS RESPAWNED in Boss Arena!");
                }
            }
        }
    }

    private int CountLivingInCamp(int targetCampId, bool isBoss)
    {
        int count = 0;
        for (int i = 0; i < EnemyNPC.All.Count; i++)
        {
            EnemyNPC e = EnemyNPC.All[i];
            if (e == null || !e.IsSpawned || !e.IsAlive) continue;
            if (isBoss)
            {
                if (e.IsBoss || e.CampId == BossCampId)
                {
                    _activeBoss = e;
                    count++;
                }
            }
            else if (!e.IsBoss && e.CampId == targetCampId)
            {
                count++;
            }
        }
        return count;
    }

    private void SyncExistingEnemiesToCamps()
    {
        for (int i = 0; i < EnemyNPC.All.Count; i++)
        {
            EnemyNPC e = EnemyNPC.All[i];
            if (e == null || !e.IsSpawned) continue;
            if (e.IsBoss || e.CampId == BossCampId)
            {
                _activeBoss = e;
                _bossEverSpawned = true;
                continue;
            }

            for (int c = 0; c < _camps.Count; c++)
            {
                if (_camps[c].CampId == e.CampId)
                {
                    _camps[c].EverPopulated = true;
                    if (!_camps[c].ActiveEnemies.Contains(e))
                        _camps[c].ActiveEnemies.Add(e);
                }
            }
        }
    }

    /// <summary>Test/Admin helper: adjusts remaining respawn timer on a specific camp (1..4).</summary>
    public void SetCampRespawnTimer(int targetCampId, float remainingSeconds)
    {
        for (int i = 0; i < _camps.Count; i++)
        {
            if (_camps[i].CampId == targetCampId)
            {
                _camps[i].IsCleared = true;
                _camps[i].EverPopulated = true;
                _camps[i].RespawnRemainingSeconds = Mathf.Max(0f, remainingSeconds);
                return;
            }
        }
    }

    /// <summary>Test/Admin helper: adjusts remaining respawn timer on the Boss.</summary>
    public void SetBossRespawnTimer(float remainingSeconds)
    {
        _bossEverSpawned = true;
        _bossCleared = true;
        _bossRespawnRemaining = Mathf.Max(0f, remainingSeconds);
    }

    public Vector3 GetCampCenter(int targetCampId = 1)
    {
        for (int i = 0; i < _camps.Count; i++)
        {
            if (_camps[i].CampId == targetCampId)
                return _camps[i].Center;
        }
        return _camps.Count > 0 ? _camps[0].Center : new Vector3(18f, 0f, 8f);
    }

    private bool Populate(NetworkRunner runner)
    {
        Debug.Log("[WorldSpawner] This client is the master - spawning 4 Enemy Camps (~7-block spacing), 1 Boss Arena, and world loot.");

        if (enemyPrefab == null)
        {
            Debug.LogWarning("[WorldSpawner] No enemyPrefab assigned.");
            return false;
        }

        int totalEnemies = 0;
        for (int c = 0; c < _camps.Count; c++)
        {
            totalEnemies += SpawnCampEnemies(runner, _camps[c]);
        }

        EnemyNPC boss = SpawnBoss(runner);

        int loot = 0;
        if (itemPrefab != null && items != null)
        {
            foreach (var s in items)
            {
                if (s == null || s.item == null || s.point == null) continue;
                if (WorldItem.Spawn(runner, itemPrefab, s.point.position, s.item, s.quantity) != null) loot++;
            }
        }

        Debug.Log($"[WorldSpawner] Spawned {totalEnemies} camp enemies across {_camps.Count} camps, Boss={(boss != null ? boss.name : "NONE")}, and {loot} world items.");
        return totalEnemies >= ExpectedCampSize && boss != null;
    }

    private int SpawnCampEnemies(NetworkRunner runner, CampRuntimeState camp)
    {
        if (runner == null || !runner.IsRunning || enemyPrefab == null || camp == null) return 0;

        camp.ActiveEnemies.Clear();
        int spawned = 0;
        int assignedCampId = camp.CampId;

        int scaleLvl = GetTargetDynamicScaleLevel();
        for (int i = 0; i < camp.SpawnPositions.Length; i++)
        {
            Vector3 pos = camp.SpawnPositions[i];
            var npc = runner.Spawn(enemyPrefab, pos, Quaternion.identity, null, (r, o) =>
            {
                var enemy = o.GetComponent<EnemyNPC>();
                if (enemy != null)
                {
                    enemy.CampId = assignedCampId;
                    enemy.IsBoss = false;
                    enemy.ScaledLevel = scaleLvl;
                    enemy.ApplyDynamicScaling(scaleLvl);
                }
            });

            if (npc != null)
            {
                spawned++;
                camp.ActiveEnemies.Add(npc);
            }
        }

        if (spawned > 0)
        {
            camp.EverPopulated = true;
            camp.IsCleared = false;
            camp.RespawnRemainingSeconds = 0f;
        }
        return spawned;
    }

    private EnemyNPC SpawnBoss(NetworkRunner runner)
    {
        if (runner == null || !runner.IsRunning || enemyPrefab == null) return null;

        int scaleLvl = GetTargetDynamicScaleLevel();
        Vector3 spawnPos = new Vector3(bossArenaCenter.x, 1.35f, bossArenaCenter.z);
        _activeBoss = runner.Spawn(enemyPrefab, spawnPos, Quaternion.identity, null, (r, o) =>
        {
            var boss = o.GetComponent<EnemyNPC>();
            if (boss != null)
            {
                boss.CampId = BossCampId;
                boss.IsBoss = true;
                boss.ScaledLevel = scaleLvl;
                boss.ApplyBossConfiguration();
            }
        });

        if (_activeBoss != null)
        {
            _bossEverSpawned = true;
            _bossCleared = false;
            _bossRespawnRemaining = 0f;
        }
        return _activeBoss;
    }

    private static int GetTargetDynamicScaleLevel()
    {
        int highest = 1;
        for (int i = 0; i < PlayerCombat.All.Count; i++)
        {
            var p = PlayerCombat.All[i];
            if (p != null && p.IsSpawned && p.NetworkLevel > highest)
                highest = p.NetworkLevel;
        }
        return Mathf.Max(1, highest);
    }

    private static void AwardCampClearGold(PlayerCombat player, string sourceName, bool isBoss)
    {
        if (player == null || !player.IsSpawned) return;
        int lvl = Mathf.Max(1, player.NetworkLevel);
        float scale = 1f + (lvl - 1) * 0.15f;
        int minGold = isBoss ? Mathf.RoundToInt(250 * scale) : Mathf.RoundToInt(40 * scale);
        int maxGold = isBoss ? Mathf.RoundToInt(450 * scale) : Mathf.RoundToInt(85 * scale);
        int amount = UnityEngine.Random.Range(minGold, maxGold + 1);
        player.AddGold(amount, sourceName);
    }

    private void CreateTestObstacles()
    {
        const string rootName = "SandboxTestObstacles";
        if (transform.Find(rootName) != null) return;

        Transform root = new GameObject(rootName).transform;
        root.SetParent(transform, false);
        CreateObstacle(root, "Pillar_West", new Vector3(8f, 1.5f, 3f), new Vector3(1.4f, 3f, 1.4f), new Color(.2f, .25f, .32f));
        CreateObstacle(root, "Pillar_East", new Vector3(11f, 1.5f, 7f), new Vector3(1.4f, 3f, 1.4f), new Color(.2f, .25f, .32f));
        CreateObstacle(root, "LowWall_South", new Vector3(10f, 1f, 0f), new Vector3(7f, 2f, .65f), new Color(.2f, .25f, .32f));
        CreateObstacle(root, "CampBarrier", new Vector3(18f, 1f, 4f), new Vector3(5f, 2f, .65f), new Color(.2f, .25f, .32f));
    }

    private void CreateBossArenaGeometry()
    {
        const string arenaName = "BossArenaGeometry";
        if (transform.Find(arenaName) != null) return;

        Transform root = new GameObject(arenaName).transform;
        root.SetParent(transform, false);

        // Floor boundary disc (non-colliding visual marker)
        GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "BossArenaFloorRing";
        var ringCol = ring.GetComponent<Collider>();
        if (ringCol != null) Destroy(ringCol);
        ring.transform.SetParent(root, true);
        ring.transform.position = new Vector3(bossArenaCenter.x, 0.02f, bossArenaCenter.z);
        ring.transform.localScale = new Vector3(bossArenaProximityRadius * 1.4f, 0.015f, bossArenaProximityRadius * 1.4f);
        var r = ring.GetComponent<Renderer>();
        if (r != null)
        {
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, new Color(0.28f, 0.06f, 0.09f, 1f));
            mpb.SetColor(ColorId, new Color(0.28f, 0.06f, 0.09f, 1f));
            r.SetPropertyBlock(mpb);
        }

        // 4 Arena Boundary Pillars
        float rOffset = 10.5f;
        Vector3[] offsets =
        {
            new Vector3(-rOffset, 2.2f, -rOffset),
            new Vector3(rOffset, 2.2f, -rOffset),
            new Vector3(-rOffset, 2.2f, rOffset),
            new Vector3(rOffset, 2.2f, rOffset)
        };
        for (int i = 0; i < offsets.Length; i++)
        {
            CreateObstacle(root, $"BossArenaPillar_{i + 1}", bossArenaCenter + offsets[i], new Vector3(1.5f, 4.4f, 1.5f), new Color(0.38f, 0.10f, 0.14f));
        }
    }

    private static void CreateObstacle(Transform parent, string obstacleName, Vector3 position, Vector3 scale, Color color)
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
            block.SetColor(BaseColorId, color);
            block.SetColor(ColorId, color);
            renderer.SetPropertyBlock(block);
        }
    }
}
