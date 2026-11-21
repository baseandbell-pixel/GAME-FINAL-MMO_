using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
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
        // ข้อมูลโซนเลเวลคงที่ของแคมป์นี้ (รองรับ Central Server MMORPG ไม่ให้เลเวลกระโดดตามผู้เล่นเลเวลสูง)
        public string ZoneTierName;
        public int MinLevel = 1;
        public int MaxLevel = 2;
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
    [Tooltip("Primary camp ID (Camp 1 uses scene enemyPoints only if spaced >= minIndividualZoneSpacing; otherwise uses separated personal patrol zone anchors).")]
#pragma warning disable CS0414
    [SerializeField] private int campId = 1;
#pragma warning restore CS0414
    [SerializeField] private Transform[] enemyPoints;
    // รัศมีโซนลาดตระเวนส่วนตัวของมอนสเตอร์แต่ละตัว (3.4m x 2 = 6.8m < ระยะห่าง 10m -> ไม่มีทางทับซ้อนกัน)
    [Min(1.5f)] [SerializeField] private float individualEnemyPatrolRadius = 3.4f;
    // ระยะห่างขั้นต่ำระหว่างจุดเกิดของมอนสเตอร์แต่ละตัวในแคมป์เดียวกัน หากจุดในฉากชิดกว่านี้จะใช้พิกัดกระจายโซนอัตโนมัติ
    [Min(6f)] [SerializeField] private float minIndividualZoneSpacing = 8.0f;
    [Min(5f)] [SerializeField] private float campRespawnDelaySeconds = DefaultCampRespawnSeconds;

    [Header("Dedicated Boss Arena (4:00 Respawn & Proximity Zone)")]
    // พรีแฟบเฉพาะของบอส 3D Golem (หากเว้นว่างจะใช้ enemyPrefab สำรองอัตโนมัติ)
    [SerializeField] private EnemyNPC bossPrefab;
    [SerializeField] private Vector3 bossArenaCenter = new Vector3(-26f, 0f, -16f);
    [Min(5f)] [SerializeField] private float bossArenaProximityRadius = 18f;
    [Min(5f)] [SerializeField] private float bossRespawnDelaySeconds = DefaultBossRespawnSeconds;

    [Header("Zone-Based Enemy Levels (Central Server Ready)")]
    // กำหนดช่วงเลเวลคงที่ตามโซนแทนการสเกลตามผู้เล่นสูงสุดในห้อง เพื่อให้ผู้เล่นใหม่ Lv.1 ฟาร์ม Starter Zone ได้เสมอ
    [Min(1)] [SerializeField] private int starterCampMinLevel = 1;
    [Min(1)] [SerializeField] private int starterCampMaxLevel = 2;
    [Min(1)] [SerializeField] private int midFieldCampMinLevel = 4;
    [Min(1)] [SerializeField] private int midFieldCampMaxLevel = 6;
    [Min(5)] [SerializeField] private int bossArenaZoneLevel = 5;

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
    /// กำหนดตำแหน่งโซนลาดตระเวนส่วนตัวแยกทีละตัว (ห่างกัน 10 เมตร รัศมีโซนละ 3.4 เมตร ไม่มีโซนทับซ้อน)
    /// และช่วงเลเวลคงที่ตามพื้นที่ (Zone-Based Levels) ของแคมป์ทั้ง 4 แคมป์:
    ///   - Starter Camps (ใกล้ Comfort Zone: Camp 1 &amp; Camp 4) = Lv.1–2
    ///   - Mid-Field Camps (โซนกลาง/รอบนอก: Camp 2 &amp; Camp 3) = Lv.4–6
    /// </summary>
    private void InitializeCampDefinitions()
    {
        _camps.Clear();

        int sMin = Mathf.Max(1, starterCampMinLevel);
        int sMax = Mathf.Max(sMin, starterCampMaxLevel);
        int mMin = Mathf.Max(sMax, midFieldCampMinLevel);
        int mMax = Mathf.Max(mMin, midFieldCampMaxLevel);
        int mSplit = Mathf.Clamp(mMin + 1, mMin, mMax);

        // Camp 1: East Starter Camp (center 29, 0, 8 — 4 โซนย่อยห่างกัน 10 เมตร พ้นระยะ CampBarrier)
        Vector3[] c1Points = new[]
        {
            new Vector3(24f, 1.2f, 3f),
            new Vector3(34f, 1.2f, 3f),
            new Vector3(24f, 1.2f, 13f),
            new Vector3(34f, 1.2f, 13f)
        };
        if (HasWellSpacedScenePoints(enemyPoints, ExpectedCampSize, minIndividualZoneSpacing))
        {
            for (int i = 0; i < ExpectedCampSize; i++)
                c1Points[i] = enemyPoints[i].position;
        }
        _camps.Add(BuildCampState(1, "Camp 1 (East Starter)", "Starter Zone", sMin, sMax, c1Points));

        // Camp 2: North-East Mid-Field Camp (center 29, 0, 32 — 4 โซนย่อยห่างกัน 10 เมตร)
        _camps.Add(BuildCampState(2, "Camp 2 (North-East Mid)", "Mid-Field", mMin, mSplit, new[]
        {
            new Vector3(24f, 1.2f, 27f),
            new Vector3(34f, 1.2f, 27f),
            new Vector3(24f, 1.2f, 37f),
            new Vector3(34f, 1.2f, 37f)
        }));

        // Camp 3: South-East Mid-Field Camp (center 29, 0, -18 — 4 โซนย่อยห่างกัน 10 เมตร พ้นระยะลานบอส)
        _camps.Add(BuildCampState(3, "Camp 3 (South-East Mid)", "Mid-Field", Mathf.Clamp(mMax - 1, mMin, mMax), mMax, new[]
        {
            new Vector3(24f, 1.2f, -23f),
            new Vector3(34f, 1.2f, -23f),
            new Vector3(24f, 1.2f, -13f),
            new Vector3(34f, 1.2f, -13f)
        }));

        // Camp 4: North-West Starter Camp (center -4, 0, 32 — 4 โซนย่อยห่างกัน 10 เมตร พ้นระยะ Comfort Safe Zone)
        _camps.Add(BuildCampState(4, "Camp 4 (North-West Starter)", "Starter Zone", sMin, sMax, new[]
        {
            new Vector3(-9f, 1.2f, 27f),
            new Vector3(1f, 1.2f, 27f),
            new Vector3(-9f, 1.2f, 37f),
            new Vector3(1f, 1.2f, 37f)
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

    private static CampRuntimeState BuildCampState(int id, string campName, string zoneTierName, int minLevel, int maxLevel, Vector3[] points)
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
        camp.CampDamageContributors.Clear();
        camp.AuthoritativeClearHandled = false;
        camp.ZeroLivingDetectedAt = -1f;
        int spawned = 0;
        int assignedCampId = camp.CampId;

        // ใช้ระบบ Zone-Based Level คงที่ตามพื้นที่แคมป์ พร้อมกำหนดโซนลาดตระเวนส่วนตัวแยกทีละตัว (Individual Patrol Zone)
        float zoneRadius = Mathf.Clamp(individualEnemyPatrolRadius, 1.5f, 6.0f);
        for (int i = 0; i < camp.SpawnPositions.Length; i++)
        {
            Vector3 pos = camp.SpawnPositions[i];
            int zoneIndex = i;
            int zoneLvl = UnityEngine.Random.Range(camp.MinLevel, camp.MaxLevel + 1);
            var npc = runner.Spawn(enemyPrefab, pos, Quaternion.identity, null, (r, o) =>
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
        // ผูก URP Lit Material ถาวรสำหรับแผ่นพื้นลานบอส (ป้องกันสีชมพูใน Standalone Build)
        UrpMaterialHelper.ApplyLitColor(ring.GetComponent<Renderer>(), new Color(0.28f, 0.06f, 0.09f, 1f));

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

    /// <summary>
    /// สร้างสิ่งกีดขวาง/เสาลานบอสพร้อมผูก URP Lit Material ถาวรผ่าน UrpMaterialHelper (ไม่เป็นสีชมพูใน Standalone Build)
    /// </summary>
    private static void CreateObstacle(Transform parent, string obstacleName, Vector3 position, Vector3 scale, Color color)
    {
        GameObject obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obstacle.name = obstacleName;
        obstacle.transform.SetParent(parent, true);
        obstacle.transform.position = position;
        obstacle.transform.localScale = scale;
        UrpMaterialHelper.ApplyLitColor(obstacle.GetComponent<Renderer>(), color);
    }
}
