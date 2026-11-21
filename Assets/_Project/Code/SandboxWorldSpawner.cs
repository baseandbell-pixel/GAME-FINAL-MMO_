using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Fusion;
using UnityEngine;
using UnityEngine.AI;

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
        // ข้อมูลโซนเลเวลคงที่ของแคมป์นี้ (รองรับ Central Server MMORPG ไม่ให้เลเวลกระโดดตามผู้เล่นเลเวลสูง)
        public string ZoneTierName;
        public int MinLevel = 1;
        public int MaxLevel = 2;
        // รัศมีโซนลาดตระเวนส่วนตัวของมอนสเตอร์แต่ละตัวในแคมป์นี้ (Starter 4.2m, Mid-Field 4.8m)
        public float PersonalPatrolRadius = 4.2f;
        // รัศมีเขตภาพรวมของแคมป์สำหรับแสดงบน World Map Overlay
        public float CampAreaRadius = 15.5f;
        public Vector3 Center;
        public Vector3[] SpawnPositions;
        public bool EverPopulated;
        public bool IsCleared;
        public float RespawnRemainingSeconds;
        public LootChest ActiveChest;
        public readonly List<EnemyNPC> ActiveEnemies = new List<EnemyNPC>(4);
        // ตารางสะสมดาเมจของผู้เล่นแต่ละคนต่อมอนสเตอร์ทุกตัวในแคมป์นี้ (ใช้ล็อกสิทธิ์เปิดกล่องสมบัติเฉพาะผู้ร่วมสู้)
        public readonly Dictionary<PlayerRef, float> CampDamageContributors = new Dictionary<PlayerRef, float>();
        public bool AuthoritativeClearHandled;
        public float AuthoritativeClearAt = -10f;
        public float ZeroLivingDetectedAt = -1f;

        public string GetFormattedRespawnTime()
        {
            int totalSec = Mathf.CeilToInt(Mathf.Max(0f, RespawnRemainingSeconds));
            int mins = totalSec / 60;
            int secs = totalSec % 60;
            return $"{mins:00}:{secs:00}";
        }
    }

    [Header("Enemies & Camps (4 แคมป์ แต่ละตัวมีโซนลาดตระเวนส่วนตัวแยกกันไม่เดินกระจุก)")]
    [SerializeField] private EnemyNPC enemyPrefab;
    [Tooltip("พรีแฟบมอนสเตอร์เฉพาะแต่ละแคมป์ (ช่อง 0 = Camp 1, ช่อง 1 = Camp 2, ช่อง 2 = Camp 3, ช่อง 3 = Camp 4) — ช่องที่เว้นว่างจะใช้ enemyPrefab เดิม")]
    [SerializeField] private EnemyNPC[] campEnemyPrefabs = null;
    [Tooltip("Primary camp ID (Camp 1 uses scene enemyPoints only if spaced >= minIndividualZoneSpacing; otherwise uses separated personal patrol zone anchors).")]
#pragma warning disable CS0414
    [SerializeField] private int campId = 1;
#pragma warning restore CS0414
    [SerializeField] private Transform[] enemyPoints;
    // รัศมีโซนลาดตระเวนส่วนตัวของมอนสเตอร์ในโซน Starter (ห่างกัน 14m, รัศมี 4.2m -> มีช่องว่าง 5.6m ไม่ทับซ้อน)
    [Min(1.5f)] [SerializeField] private float individualEnemyPatrolRadius = 4.2f;
    // รัศมีโซนลาดตระเวนส่วนตัวของมอนสเตอร์ในโซน Mid-Field Frontier (ห่างกัน 16m, รัศมี 4.8m -> มีช่องว่าง 6.4m)
    [Min(1.5f)] [SerializeField] private float midFieldEnemyPatrolRadius = 4.8f;
    // ระยะห่างขั้นต่ำระหว่างจุดเกิดของมอนสเตอร์แต่ละตัวในแคมป์เดียวกัน หากจุดในฉากชิดกว่านี้จะใช้พิกัดกระจายโซนอัตโนมัติ
    [Min(6f)] [SerializeField] private float minIndividualZoneSpacing = 11.0f;
    [Min(5f)] [SerializeField] private float campRespawnDelaySeconds = DefaultCampRespawnSeconds;

    [Header("Dedicated Boss Arena (Far North Citadel - 4:00 Respawn & Proximity Zone)")]
    // พรีแฟบเฉพาะของบอส 3D Golem (หากเว้นว่างจะใช้ enemyPrefab สำรองอัตโนมัติ)
    [SerializeField] private EnemyNPC bossPrefab;
    [SerializeField] private Vector3 bossArenaCenter = new Vector3(0f, 0f, 74f);
    [Min(5f)] [SerializeField] private float bossArenaProximityRadius = 24f;
    [Min(5f)] [SerializeField] private float bossRespawnDelaySeconds = DefaultBossRespawnSeconds;

    [Header("Zone-Based Enemy Levels (Central Server Ready)")]
    // กำหนดช่วงเลเวลคงที่ตามโซนบนแผนที่ขยาย 220x220m:
    //   - Near-Field Starter Zones (Camps 1 & 2) = Lv.1–2
    //   - Mid-Field Frontier Zones (Camps 3 & 4) = Lv.3–5
    //   - Far North Citadel Boss Arena = Lv.5+
    [Min(1)] [SerializeField] private int starterCampMinLevel = 1;
    [Min(1)] [SerializeField] private int starterCampMaxLevel = 2;
    [Min(1)] [SerializeField] private int midFieldCampMinLevel = 3;
    [Min(1)] [SerializeField] private int midFieldCampMaxLevel = 5;
    [Min(5)] [SerializeField] private int bossArenaZoneLevel = 5;

    [Header("Comfort Safe Zone (South-Central Hub - Non-Combat & Reconnect Anchor)")]
    [SerializeField] private Vector3 safeZoneCenter = new Vector3(0f, 0f, -60f);
    [Min(3f)] [SerializeField] private float safeZoneRadius = 12.0f;

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
    public float IndividualEnemyPatrolRadius => individualEnemyPatrolRadius;
    public int BossArenaZoneLevel => Mathf.Max(5, bossArenaZoneLevel);

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
    private bool _bossAuthoritativeClearHandled;
    private float _bossAuthoritativeClearAt = -10f;
    private float _bossZeroLivingDetectedAt = -1f;
    private float _bossRespawnRemaining;
    // ตารางสะสมดาเมจของผู้เล่นแต่ละคนที่ทำใส่บอสในรอบปัจจุบัน
    private readonly Dictionary<PlayerRef, float> _bossDamageContributors = new Dictionary<PlayerRef, float>();

    private bool _done;
    private float _nextAttempt;
    private float _masterReadyAt = -1f;
    // บันทึกสถานะ MasterClient เฟรมก่อนหน้าเพื่อตรวจจับการรับตำแหน่งต่อจาก Host ที่หลุดออกทันที
    private bool _wasSharedModeMasterClient;
    private float _nextAuthoritySweepAt;

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
    /// กำหนดผังโซนบนแผนที่ขยาย 220x220 เมตร (Progression จากทิศใต้ขึ้นทิศเหนืออย่างเป็นระบบ):
    ///   - South Hub: Comfort Zone (Safe Haven) ที่ (0, 0, -60) รัศมี 12m
    ///   - Near-Field Outer Ring (Starter Zones, Lv.1–2): Camp 1 (-46, -22) &amp; Camp 2 (46, -22) — ห่างกัน 14m, รัศมีส่วนตัว 4.2m
    ///   - Mid-Field Frontier (Intermediate Zones, Lv.3–5): Camp 3 (-58, 28) &amp; Camp 4 (58, 28) — ห่างกัน 16m, รัศมีส่วนตัว 4.8m
    ///   - Far North Citadel (Boss Arena, Lv.5+): Sovereign Golem ที่ (0, 0, 74) รัศมี 24m
    /// </summary>
    private void InitializeCampDefinitions()
    {
        _camps.Clear();

        int sMin = Mathf.Max(1, starterCampMinLevel);
        int sMax = Mathf.Max(sMin, starterCampMaxLevel);
        int mMin = Mathf.Max(sMax, midFieldCampMinLevel);
        int mMax = Mathf.Max(mMin, midFieldCampMaxLevel);
        int mSplit = Mathf.Clamp(mMin + 1, mMin, mMax);

        float starterZoneR = Mathf.Clamp(individualEnemyPatrolRadius, 2.0f, 6.0f);
        float midZoneR = Mathf.Clamp(midFieldEnemyPatrolRadius, 2.5f, 6.5f);

        // Camp 1: West Starter Outskirts (Near-Field ซ้าย, center -46, 0, -22 — 4 โซนส่วนตัวห่างกัน 14m)
        Vector3[] c1Points = new[]
        {
            new Vector3(-53f, 1.2f, -29f),
            new Vector3(-39f, 1.2f, -29f),
            new Vector3(-53f, 1.2f, -15f),
            new Vector3(-39f, 1.2f, -15f)
        };
        if (HasWellSpacedScenePoints(enemyPoints, ExpectedCampSize, minIndividualZoneSpacing))
        {
            for (int i = 0; i < ExpectedCampSize; i++)
                c1Points[i] = enemyPoints[i].position;
        }
        _camps.Add(BuildCampState(1, "Camp 1 (West Starter)", "Starter Zone", sMin, sMax, starterZoneR, 16f, c1Points));

        // Camp 2: East Starter Outskirts (Near-Field ขวา, center 46, 0, -22 — 4 โซนส่วนตัวห่างกัน 14m)
        _camps.Add(BuildCampState(2, "Camp 2 (East Starter)", "Starter Zone", sMin, sMax, starterZoneR, 16f, new[]
        {
            new Vector3(39f, 1.2f, -29f),
            new Vector3(53f, 1.2f, -29f),
            new Vector3(39f, 1.2f, -15f),
            new Vector3(53f, 1.2f, -15f)
        }));

        // Camp 3: West Mid-Field Frontier (Intermediate ซ้าย, center -58, 0, 28 — 4 โซนส่วนตัวห่างกัน 16m)
        _camps.Add(BuildCampState(3, "Camp 3 (West Frontier)", "Mid-Field", mMin, mSplit, midZoneR, 18.5f, new[]
        {
            new Vector3(-66f, 1.2f, 20f),
            new Vector3(-50f, 1.2f, 20f),
            new Vector3(-66f, 1.2f, 36f),
            new Vector3(-50f, 1.2f, 36f)
        }));

        // Camp 4: East Mid-Field Frontier (Intermediate ขวา, center 58, 0, 28 — 4 โซนส่วนตัวห่างกัน 16m)
        _camps.Add(BuildCampState(4, "Camp 4 (East Frontier)", "Mid-Field", Mathf.Clamp(mMax - 1, mMin, mMax), mMax, midZoneR, 18.5f, new[]
        {
            new Vector3(50f, 1.2f, 20f),
            new Vector3(66f, 1.2f, 20f),
            new Vector3(50f, 1.2f, 36f),
            new Vector3(66f, 1.2f, 36f)
        }));
    }

    /// <summary>
    /// ตรวจสอบว่าจุดเกิดที่วางไว้ในฉากมีระยะห่างเพียงพอสำหรับโซนลาดตระเวนส่วนตัวของมอนสเตอร์แต่ละตัวหรือไม่
    /// </summary>
    private static bool HasWellSpacedScenePoints(Transform[] points, int requiredCount, float minSpacing)
    {
        if (points == null || points.Length != requiredCount) return false;
        for (int i = 0; i < requiredCount; i++)
        {
            if (points[i] == null) return false;
            for (int j = i + 1; j < requiredCount; j++)
            {
                if (points[j] == null) return false;
                Vector3 a = points[i].position;
                Vector3 b = points[j].position;
                a.y = 0f;
                b.y = 0f;
                if (Vector3.Distance(a, b) < minSpacing) return false;
            }
        }
        return true;
    }

    private static CampRuntimeState BuildCampState(int id, string campName, string zoneTierName, int minLevel, int maxLevel, float personalRadius, float campAreaRadius, Vector3[] points)
    {
        Vector3 sum = Vector3.zero;
        for (int i = 0; i < points.Length; i++) sum += points[i];
        Vector3 center = sum / Mathf.Max(1, points.Length);
        center.y = 0f;

        return new CampRuntimeState
        {
            CampId = id,
            CampName = campName,
            ZoneTierName = zoneTierName,
            MinLevel = Mathf.Max(1, minLevel),
            MaxLevel = Mathf.Max(minLevel, maxLevel),
            PersonalPatrolRadius = personalRadius,
            CampAreaRadius = campAreaRadius,
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

    // ------------------------------------------------------------------ ระบบสะสมดาเมจประจำแคมป์และล็อกหีบสมบัติเฉพาะผู้ร่วมต่อสู้

    /// <summary>
    /// บันทึกดาเมจสะสมของผู้เล่นในแต่ละแคมป์หรือบอส (เรียกจากฝั่ง State Authority ของ EnemyNPC)
    /// </summary>
    public void RecordCampDamage(int targetCampId, bool isBoss, PlayerRef attacker, float damageAmount)
    {
        if (attacker == PlayerRef.None || damageAmount <= 0f) return;

        Dictionary<PlayerRef, float> ledger = isBoss ? _bossDamageContributors : FindCampState(targetCampId)?.CampDamageContributors;
        if (ledger == null) return;

        if (ledger.TryGetValue(attacker, out float prev))
            ledger[attacker] = prev + damageAmount;
        else
            ledger[attacker] = damageAmount;
    }

    /// <summary>
    /// แปลงตารางผู้มีส่วนร่วมทำดาเมจประจำแคมป์เป็นข้อความขนาดกะทัดรัดสำหรับส่งผ่าน Network RPC
    /// รูปแบบ: "rawEncoded:share:isTopDps;..." (ใช้ InvariantCulture เพื่อรองรับทุกระบบภาษา)
    /// </summary>
    public string BuildCampContributorPayload(int targetCampId, bool isBoss)
    {
        Dictionary<PlayerRef, float> ledger = isBoss ? _bossDamageContributors : FindCampState(targetCampId)?.CampDamageContributors;
        if (ledger == null || ledger.Count == 0) return string.Empty;

        LootDropSystem.AnalyzeDamageContribution(
            ledger,
            PlayerRef.None,
            out float totalDamage,
            out PlayerRef topDps,
            out List<PlayerRef> participants);

        if (participants.Count == 0 || totalDamage <= 0f) return string.Empty;

        var sb = new StringBuilder(64);
        // เรียงผู้ทำดาเมจจากมากไปน้อยก่อนประกอบข้อความ เพื่อรับประกันว่าไม่เกินขีดจำกัด 64 ตัวอักษรของ NetworkString<_64>
        participants.Sort((a, b) =>
        {
            float da = ledger.TryGetValue(a, out float va) ? va : 0f;
            float db = ledger.TryGetValue(b, out float vb) ? vb : 0f;
            return db.CompareTo(da);
        });

        for (int i = 0; i < participants.Count; i++)
        {
            PlayerRef p = participants[i];
            if (!ledger.TryGetValue(p, out float dealt) || dealt <= 0f) continue;

            float share = Mathf.Clamp01(dealt / totalDamage);
            int topFlag = p == topDps ? 1 : 0;
            string token = $"{p.RawEncoded.ToString(CultureInfo.InvariantCulture)}:{share.ToString("0.##", CultureInfo.InvariantCulture)}:{topFlag}";
            int needed = (sb.Length > 0 ? 1 : 0) + token.Length;
            if (sb.Length + needed > 62) break;

            if (sb.Length > 0) sb.Append(';');
            sb.Append(token);
        }
        return sb.ToString();
    }

    /// <summary>
    /// แปลงข้อความรายชื่อผู้มีส่วนร่วมจาก RPC กลับมาเป็น Dictionary สำหรับตั้งค่าล็อกสิทธิ์ของ LootChest
    /// </summary>
    public static Dictionary<int, LootChest.ParticipantContribution> ParseContributorPayload(string payload)
    {
        var result = new Dictionary<int, LootChest.ParticipantContribution>();
        if (string.IsNullOrEmpty(payload)) return result;

        string[] entries = payload.Split(';');
        for (int i = 0; i < entries.Length; i++)
        {
            if (string.IsNullOrEmpty(entries[i])) continue;
            string[] parts = entries[i].Split(':');
            if (parts.Length < 3) continue;

            if (int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int rawEncoded) &&
                float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float share) &&
                int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int topFlag) &&
                share > 0f)
            {
                result[rawEncoded] = new LootChest.ParticipantContribution
                {
                    playerRawEncoded = rawEncoded,
                    damageShare = Mathf.Clamp01(share),
                    isTopDps = topFlag == 1
                };
            }
        }
        return result;
    }

    /// <summary>
    /// ประมวลผลการเคลียร์แคมป์/บอสที่ส่งมาจาก State Authority อย่างเป็นทางการ:
    ///   - สร้างกล่องสมบัติและล็อกสิทธิ์เฉพาะผู้เล่นที่มีชื่ออยู่ในตารางดาเมจ (damage > 0)
    ///   - แจก Gold รางวัลเคลียร์แคมป์เฉพาะผู้เล่นที่ร่วมต่อสู้จริงเท่านั้น (ห้ามผู้เล่นหรือ Host ที่ยืนดูได้ Gold ฟรี)
    /// </summary>
    public void HandleAuthoritativeCampCleared(int clearedCampId, bool isBossCamp, Vector3 chestPos, string contributorPayload)
    {
        var contributors = ParseContributorPayload(contributorPayload);
        var local = PlayerCombat.Local;
        NetworkRunner runner = local != null ? local.Runner : null;
        PlayerRef localRef = (runner != null && runner.IsRunning) ? runner.LocalPlayer : PlayerRef.None;
        bool localParticipated = localRef != PlayerRef.None &&
                                 contributors.TryGetValue(localRef.RawEncoded, out var localContrib) &&
                                 localContrib.damageShare > 0f;

        if (isBossCamp)
        {
            if (_bossAuthoritativeClearHandled) return;
            _bossCleared = true;
            _bossAuthoritativeClearHandled = true;
            _bossAuthoritativeClearAt = Time.unscaledTime;
            _bossRespawnRemaining = bossRespawnDelaySeconds;
            if (spawnRewardChestOnCampClear)
            {
                _activeBossChest = LootChest.SpawnAt(chestPos, BossCampId, itemPrefab, isBossChest: true, replaceExisting: true);
                _activeBossChest.ConfigureParticipation(contributors);
            }
            if (localParticipated)
                AwardCampClearGold(local, "Boss Sovereign Golem", isBoss: true, zoneLevel: BossArenaZoneLevel);

            Debug.Log($"[WorldSpawner] BOSS DEFEATED (Authoritative)! EligibleContributors={contributors.Count}, LocalParticipated={localParticipated}.");
            return;
        }

        CampRuntimeState camp = FindCampState(clearedCampId);
        if (camp == null || camp.AuthoritativeClearHandled) return;

        camp.IsCleared = true;
        camp.AuthoritativeClearHandled = true;
        camp.AuthoritativeClearAt = Time.unscaledTime;
        camp.RespawnRemainingSeconds = campRespawnDelaySeconds;
        if (spawnRewardChestOnCampClear)
        {
            camp.ActiveChest = LootChest.SpawnAt(chestPos, camp.CampId, itemPrefab, isBossChest: false, replaceExisting: true);
            camp.ActiveChest.ConfigureParticipation(contributors);
        }
        if (localParticipated)
            AwardCampClearGold(local, camp.CampName, isBoss: false, zoneLevel: camp.MaxLevel);

        Debug.Log($"[WorldSpawner] {camp.CampName} CLEARED (Authoritative)! EligibleContributors={contributors.Count}, LocalParticipated={localParticipated}.");
    }

    private CampRuntimeState FindCampState(int targetCampId)
    {
        for (int i = 0; i < _camps.Count; i++)
        {
            if (_camps[i].CampId == targetCampId) return _camps[i];
        }
        return null;
    }

    private void TickCampsAndBossLifecycle(float dt)
    {
        var local = PlayerCombat.Local;
        NetworkRunner runner = local != null ? local.Runner : null;
        bool isMaster = runner != null && runner.IsRunning && runner.IsSharedModeMasterClient;

        // ตรวจจับการเลื่อนขั้นเป็น SharedModeMasterClient (เมื่อ Host เดิมหลุดออกจากห้อง)
        // แล้วกวาดกู้คืน StateAuthority ของมอนสเตอร์ทุกตัวในแคมป์ 1–4 และบอสทันทีเพื่อไม่ให้ Camp 2 & 4 ค้าง
        bool promotedToMaster = isMaster && !_wasSharedModeMasterClient;
        _wasSharedModeMasterClient = isMaster;
        if (runner != null && runner.IsRunning && (promotedToMaster || Time.unscaledTime >= _nextAuthoritySweepAt))
        {
            _nextAuthoritySweepAt = Time.unscaledTime + 0.5f;
            if (promotedToMaster)
            {
                Debug.Log($"[WorldSpawner] Local client promoted to SharedModeMasterClient ({runner.LocalPlayer}) -> Syncing all 4 camps & recovering orphaned enemy authorities.");
                SyncExistingEnemiesToCamps();
            }
            RecoverOrphanedEnemyAuthorities(runner, immediate: promotedToMaster);
        }

        // 1. ตรวจสอบวงจรชีวิตและเวลาเกิดใหม่ของแคมป์มอนสเตอร์ทั้ง 4 แคมป์ (90 วินาที)
        for (int c = 0; c < _camps.Count; c++)
        {
            CampRuntimeState camp = _camps[c];
            int living = CountLivingInCamp(camp.CampId, isBoss: false);

            if (living > 0)
            {
                // เว้นระยะเผื่อซิงก์เครือข่าย 3 วินาทีหลังได้รับ RPC_NotifyCampCleared เพื่อป้องกัน Race Condition กรณี RPC ถึงเครื่องลูกก่อนสถานะตายของมอนตัวสุดท้าย
                if (!camp.AuthoritativeClearHandled || (Time.unscaledTime - camp.AuthoritativeClearAt >= 3.0f))
                {
                    camp.EverPopulated = true;
                    camp.IsCleared = false;
                    camp.AuthoritativeClearHandled = false;
                    camp.ZeroLivingDetectedAt = -1f;
                    camp.RespawnRemainingSeconds = 0f;
                    // ลบกล่องสมบัติเก่าที่ยังเหลือค้างเมื่อมอนสเตอร์ในแคมป์เกิดใหม่แล้ว
                    if (camp.ActiveChest != null)
                    {
                        Destroy(camp.ActiveChest.gameObject);
                        camp.ActiveChest = null;
                    }
                }
            }
            else if (camp.EverPopulated && !camp.IsCleared)
            {
                // รอสัญญาณ RPC_NotifyCampCleared จาก State Authority สั้นๆ (0.8s) ก่อนใช้ Fallback แบบล็อกสิทธิ์เข้มงวด
                if (camp.ZeroLivingDetectedAt < 0f)
                {
                    camp.ZeroLivingDetectedAt = Time.unscaledTime;
                }
                else if (Time.unscaledTime - camp.ZeroLivingDetectedAt >= 0.8f)
                {
                    string fallbackPayload = isMaster ? BuildCampContributorPayload(camp.CampId, isBoss: false) : string.Empty;
                    HandleAuthoritativeCampCleared(camp.CampId, isBossCamp: false, camp.Center, fallbackPayload);
                }
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
                        camp.AuthoritativeClearHandled = false;
                        camp.ZeroLivingDetectedAt = -1f;
                        Debug.Log($"[WorldSpawner] {camp.CampName} RESPAWNED ({spawned} enemies).");
                    }
                }
            }
        }

        // 2. ตรวจสอบวงจรชีวิตและเวลาเกิดใหม่ของบอสใหญ่ใน Boss Arena (240 วินาที)
        int livingBoss = CountLivingInCamp(BossCampId, isBoss: true);
        if (livingBoss > 0)
        {
            // เว้นระยะเผื่อซิงก์เครือข่าย 3 วินาทีหลังได้รับ RPC_NotifyCampCleared ของบอสเช่นเดียวกัน
            if (!_bossAuthoritativeClearHandled || (Time.unscaledTime - _bossAuthoritativeClearAt >= 3.0f))
            {
                _bossEverSpawned = true;
                _bossCleared = false;
                _bossAuthoritativeClearHandled = false;
                _bossZeroLivingDetectedAt = -1f;
                _bossRespawnRemaining = 0f;
                // ลบกล่องบอสเก่าที่ยังเหลือค้างเมื่อบอสเกิดใหม่แล้ว
                if (_activeBossChest != null)
                {
                    Destroy(_activeBossChest.gameObject);
                    _activeBossChest = null;
                }
            }
        }
        else if (_bossEverSpawned && !_bossCleared)
        {
            if (_bossZeroLivingDetectedAt < 0f)
            {
                _bossZeroLivingDetectedAt = Time.unscaledTime;
            }
            else if (Time.unscaledTime - _bossZeroLivingDetectedAt >= 0.8f)
            {
                Vector3 chestPos = bossArenaCenter + new Vector3(0f, 0f, 2.2f);
                string fallbackPayload = isMaster ? BuildCampContributorPayload(BossCampId, isBoss: true) : string.Empty;
                HandleAuthoritativeCampCleared(BossCampId, isBossCamp: true, chestPos, fallbackPayload);
            }
        }
        else if (_bossCleared)
        {
            _bossRespawnRemaining = Mathf.Max(0f, _bossRespawnRemaining - dt);
            if (_bossRespawnRemaining <= 0f && isMaster)
            {
                if (SpawnBoss(runner) != null)
                {
                    _bossCleared = false;
                    _bossAuthoritativeClearHandled = false;
                    _bossZeroLivingDetectedAt = -1f;
                    Debug.Log("[WorldSpawner] BOSS RESPAWNED in Boss Arena!");
                }
            }
        }
    }

    /// <summary>
    /// กวาดตรวจสอบมอนสเตอร์ทุกตัวในแคมป์ 1–4 และบอส หากพบตัวใดขาดเจ้าของ StateAuthority (หลัง Host ออกจากห้อง)
    /// จะสั่งให้ MasterClient หรือผู้เล่นลำดับแรกที่ยังอยู่ในห้องดึงสิทธิ์ StateAuthority กลับมาทันที
    /// </summary>
    public int RecoverOrphanedEnemyAuthorities(NetworkRunner runner, bool immediate = false)
    {
        if (runner == null || !runner.IsRunning) return 0;
        if (!EnemyNPC.ShouldLocalClientTakeOverAuthority(runner)) return 0;

        int requested = 0;
        for (int i = 0; i < EnemyNPC.All.Count; i++)
        {
            EnemyNPC enemy = EnemyNPC.All[i];
            if (enemy == null || !enemy.IsSpawned) continue;
            if (enemy.TryRecoverOrphanedAuthority(ignoreThrottle: immediate))
            {
                requested++;
            }
        }
        return requested;
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

    private EnemyNPC ResolveCampEnemyPrefab(int targetCampId)
    {
        int idx = targetCampId - 1;
        if (campEnemyPrefabs != null && idx >= 0 && idx < campEnemyPrefabs.Length && campEnemyPrefabs[idx] != null)
            return campEnemyPrefabs[idx];
        return enemyPrefab;
    }

    private int SpawnCampEnemies(NetworkRunner runner, CampRuntimeState camp)
    {
        EnemyNPC prefabForCamp = camp != null ? ResolveCampEnemyPrefab(camp.CampId) : enemyPrefab;
        if (runner == null || !runner.IsRunning || prefabForCamp == null || camp == null) return 0;

        camp.ActiveEnemies.Clear();
        camp.CampDamageContributors.Clear();
        camp.AuthoritativeClearHandled = false;
        camp.ZeroLivingDetectedAt = -1f;
        int spawned = 0;
        int assignedCampId = camp.CampId;

        // ใช้ระบบ Zone-Based Level คงที่ตามพื้นที่แคมป์ พร้อมกำหนดโซนลาดตระเวนส่วนตัวแยกทีละตัว (Starter 4.2m, Mid-Field 4.8m)
        float zoneRadius = Mathf.Clamp(camp.PersonalPatrolRadius > 0.5f ? camp.PersonalPatrolRadius : individualEnemyPatrolRadius, 1.5f, 6.5f);
        for (int i = 0; i < camp.SpawnPositions.Length; i++)
        {
            Vector3 pos = camp.SpawnPositions[i];
            int zoneIndex = i;
            int zoneLvl = UnityEngine.Random.Range(camp.MinLevel, camp.MaxLevel + 1);
            var npc = runner.Spawn(prefabForCamp, pos, Quaternion.identity, null, (r, o) =>
            {
                var enemy = o.GetComponent<EnemyNPC>();
                if (enemy != null)
                {
                    enemy.CampId = assignedCampId;
                    enemy.IsBoss = false;
                    enemy.ScaledLevel = zoneLvl;
                    enemy.ConfigurePersonalPatrolZone(pos, zoneRadius, zoneIndex);
                    enemy.ApplyZoneLevel(zoneLvl);
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
        EnemyNPC prefabToSpawn = bossPrefab != null ? bossPrefab : enemyPrefab;
        if (runner == null || !runner.IsRunning || prefabToSpawn == null) return null;

        // บอสใช้เลเวลคงที่ตาม Boss Arena Zone (ค่าเริ่มต้น Lv.5+ ตั้งค่าได้ใน Inspector)
        int bossZoneLvl = BossArenaZoneLevel;
        // โมเดล 3D ที่มี Feet Pivot (เช่น Boss_SovereignGolem) เกิดที่ระดับพื้น Y=0 ส่วน Capsule เก่าเกิดที่ Y=1.35
        float spawnY = prefabToSpawn.HasRiggedModel ? 0f : 1.35f;
        if (Physics.Raycast(new Vector3(bossArenaCenter.x, bossArenaCenter.y + 4f, bossArenaCenter.z), Vector3.down, out RaycastHit groundHit, 12f, ~0, QueryTriggerInteraction.Ignore))
        {
            spawnY = prefabToSpawn.HasRiggedModel ? groundHit.point.y : groundHit.point.y + 1.35f;
        }

        Vector3 spawnPos = new Vector3(bossArenaCenter.x, spawnY, bossArenaCenter.z);
        _activeBoss = runner.Spawn(prefabToSpawn, spawnPos, Quaternion.identity, null, (r, o) =>
        {
            var boss = o.GetComponent<EnemyNPC>();
            if (boss != null)
            {
                boss.CampId = BossCampId;
                boss.IsBoss = true;
                boss.ScaledLevel = bossZoneLvl;
                boss.ApplyBossConfiguration();
            }
        });

        if (_activeBoss != null)
        {
            _bossDamageContributors.Clear();
            _bossEverSpawned = true;
            _bossCleared = false;
            _bossAuthoritativeClearHandled = false;
            _bossZeroLivingDetectedAt = -1f;
            _bossRespawnRemaining = 0f;
        }
        return _activeBoss;
    }

    /// <summary>
    /// แจก Gold รางวัลการเคลียร์แคมป์/บอสโดยอ้างอิงตามเลเวลของโซน (Zone Level) เพื่อสมดุลเศรษฐกิจแบบ Central Server
    /// </summary>
    private static void AwardCampClearGold(PlayerCombat player, string sourceName, bool isBoss, int zoneLevel)
    {
        if (player == null || !player.IsSpawned) return;
        int lvl = Mathf.Max(1, zoneLevel);
        float scale = 1f + (lvl - 1) * 0.15f;
        int minGold = isBoss ? Mathf.RoundToInt(250 * scale) : Mathf.RoundToInt(40 * scale);
        int maxGold = isBoss ? Mathf.RoundToInt(450 * scale) : Mathf.RoundToInt(85 * scale);
        int amount = UnityEngine.Random.Range(minGold, maxGold + 1);
        player.AddGold(amount, sourceName);
    }

    /// <summary>
    /// สร้างสิ่งปลูกสร้างนำสายตาและกำบังยุทธวิธี (Zone Landmarks &amp; Cover Obstacles) กระจายระหว่างโซนต่างๆ บนแผนที่ขยาย 220x220m
    /// พร้อมติด NavMeshObstacle (Carving) ให้มอนสเตอร์เดินอ้อมสิ่งกีดขวางได้ลื่นไหลผ่าน NavMeshAgent
    /// </summary>
    private void CreateTestObstacles()
    {
        const string rootName = "SandboxTestObstacles";
        if (transform.Find(rootName) != null) return;

        Transform root = new GameObject(rootName).transform;
        root.SetParent(transform, false);

        Color hubStone = new Color(0.24f, 0.30f, 0.36f);
        Color frontierStone = new Color(0.28f, 0.24f, 0.30f);
        Color citadelGate = new Color(0.36f, 0.14f, 0.18f);

        // 1. ซุ้มประตูทางออกทิศเหนือจาก Comfort Zone มุ่งสู่ทางแยกโซน Starter และ Citadel
        CreateObstacle(root, "SouthHub_GateLeft", new Vector3(-7f, 2.0f, -40f), new Vector3(1.8f, 4.0f, 1.8f), hubStone);
        CreateObstacle(root, "SouthHub_GateRight", new Vector3(7f, 2.0f, -40f), new Vector3(1.8f, 4.0f, 1.8f), hubStone);

        // 2. กำบังทางเข้าโซน Starter (Camp 1 ซ้าย และ Camp 2 ขวา)
        CreateObstacle(root, "StarterWest_Wall", new Vector3(-24f, 1.1f, -24f), new Vector3(1.2f, 2.2f, 7.0f), hubStone);
        CreateObstacle(root, "StarterWest_Pillar", new Vector3(-28f, 1.6f, -14f), new Vector3(1.6f, 3.2f, 1.6f), hubStone);
        CreateObstacle(root, "StarterEast_Wall", new Vector3(24f, 1.1f, -24f), new Vector3(1.2f, 2.2f, 7.0f), hubStone);
        CreateObstacle(root, "StarterEast_Pillar", new Vector3(28f, 1.6f, -14f), new Vector3(1.6f, 3.2f, 1.6f), hubStone);

        // 3. กำแพงซากปรักหักพังและเสาหินในโซน Mid-Field Frontier (Camp 3 ซ้าย และ Camp 4 ขวา)
        CreateObstacle(root, "FrontierWest_RuinWall", new Vector3(-34f, 1.3f, 26f), new Vector3(1.4f, 2.6f, 9.0f), frontierStone);
        CreateObstacle(root, "FrontierWest_PillarNorth", new Vector3(-58f, 1.8f, 8f), new Vector3(5.5f, 2.2f, 1.2f), frontierStone);
        CreateObstacle(root, "FrontierEast_RuinWall", new Vector3(34f, 1.3f, 26f), new Vector3(1.4f, 2.6f, 9.0f), frontierStone);
        CreateObstacle(root, "FrontierEast_PillarNorth", new Vector3(58f, 1.8f, 8f), new Vector3(5.5f, 2.2f, 1.2f), frontierStone);

        // 4. เสาหลักทางขึ้นสู่ Far North Citadel (Boss Arena)
        CreateObstacle(root, "CitadelApproach_Left", new Vector3(-13f, 2.6f, 42f), new Vector3(2.0f, 5.2f, 2.0f), citadelGate);
        CreateObstacle(root, "CitadelApproach_Right", new Vector3(13f, 2.6f, 42f), new Vector3(2.0f, 5.2f, 2.0f), citadelGate);
    }

    private void CreateBossArenaGeometry()
    {
        const string arenaName = "BossArenaGeometry";
        if (transform.Find(arenaName) != null) return;

        Transform root = new GameObject(arenaName).transform;
        root.SetParent(transform, false);

        // แผ่นพื้นลานบอส (ไม่ชนคอลไลเดอร์)
        GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "BossArenaFloorRing";
        var ringCol = ring.GetComponent<Collider>();
        if (ringCol != null) Destroy(ringCol);
        ring.transform.SetParent(root, true);
        ring.transform.position = new Vector3(bossArenaCenter.x, 0.02f, bossArenaCenter.z);
        ring.transform.localScale = new Vector3(bossArenaProximityRadius * 2.0f, 0.015f, bossArenaProximityRadius * 2.0f);
        // ผูก URP Lit Material ถาวรสำหรับแผ่นพื้นลานบอส (ป้องกันสีชมพูใน Standalone Build)
        UrpMaterialHelper.ApplyLitColor(ring.GetComponent<Renderer>(), new Color(0.24f, 0.05f, 0.08f, 1f));

        // เส้นวงกลมขอบเขตลานบอสสีแดงเข้ม + ขอบทองด้านในด้วย LineRenderer (URP Unlit ถาวร)
        UrpMaterialHelper.CreateGroundCircleRing(
            root,
            "BossArenaBoundaryRing",
            bossArenaCenter,
            bossArenaProximityRadius,
            lineWidth: 0.34f,
            color: new Color(0.95f, 0.18f, 0.16f, 0.90f),
            segments: 96,
            yHeight: 0.09f);

        UrpMaterialHelper.CreateGroundCircleRing(
            root,
            "BossArenaInnerGoldRing",
            bossArenaCenter,
            Mathf.Max(3f, bossArenaProximityRadius - 0.55f),
            lineWidth: 0.12f,
            color: new Color(1.0f, 0.80f, 0.24f, 0.78f),
            segments: 96,
            yHeight: 0.095f);

        // เสาประดับขอบลานบอส 8 ต้นรอบวงกลม (Landmark โดดเด่นชัดเจน)
        float pillarRingRadius = Mathf.Max(10.5f, bossArenaProximityRadius - 1.4f);
        const int pillarCount = 8;
        for (int i = 0; i < pillarCount; i++)
        {
            float angle = (i / (float)pillarCount) * Mathf.PI * 2f + (Mathf.PI * 0.125f);
            Vector3 offset = new Vector3(Mathf.Cos(angle) * pillarRingRadius, 2.5f, Mathf.Sin(angle) * pillarRingRadius);
            CreateObstacle(root, $"BossArenaPillar_{i + 1}", bossArenaCenter + offset, new Vector3(1.6f, 5.0f, 1.6f), new Color(0.38f, 0.10f, 0.14f));
        }
    }

    /// <summary>
    /// สร้างสิ่งกีดขวาง/เสาลานบอสพร้อมผูก URP Lit Material ถาวร และ NavMeshObstacle (Carving) เพื่อรองรับการนำทาง NavMesh
    /// </summary>
    private static void CreateObstacle(Transform parent, string obstacleName, Vector3 position, Vector3 scale, Color color)
    {
        GameObject obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obstacle.name = obstacleName;
        obstacle.transform.SetParent(parent, true);
        obstacle.transform.position = position;
        obstacle.transform.localScale = scale;
        UrpMaterialHelper.ApplyLitColor(obstacle.GetComponent<Renderer>(), color);

        // ติด NavMeshObstacle แบบ Carving เพื่อให้ศัตรูที่ใช้ NavMeshAgent เดินหลบเสาและกำแพงได้อัตโนมัติ
        var navObs = obstacle.AddComponent<NavMeshObstacle>();
        navObs.shape = NavMeshObstacleShape.Box;
        navObs.carving = true;
    }
}
