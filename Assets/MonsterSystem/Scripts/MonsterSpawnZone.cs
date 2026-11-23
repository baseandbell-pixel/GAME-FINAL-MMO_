using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Fusion;
using UnityEngine;

public enum MonsterZoneRespawnMode
{
    /// <summary>MMO standard for open-world mobs: each monster slot tracks its own randomized respawn timer.</summary>
    IndividualStaggered = 0,
    /// <summary>Wave / Camp style: respawns the entire cluster together once all monsters in the zone are dead.</summary>
    CampClearWave = 1
}

/// <summary>
/// Production-hardened Plug-and-Play MMO Monster Spawn Zone (100% self-contained in Assets/MonsterSystem).
/// Drop <c>Zone_Orc.prefab</c> (or any custom monster zone) into ANY scene without modifying
/// the scene's existing spawner or scripts!
///
/// Production Safeguards:
///   - Diegetic / Invisible in Game View (zero floor discs or floating signs; clean Gizmos in Scene View only).
///   - Terrain & Slope Conforming: raycasts every spawn slot onto the real ground surface (works on hills/valleys).
///   - Shared Mode Host-Migration Safe: adopts existing live monsters matching ZoneSignature before spawning
///     so changing Master Client NEVER double-spawns monsters!
///   - Staggered Individual Respawn (default 35-50s per slot) + Anti-Pop-In player proximity check.
///   - Wide default spacing so solo players can pull 1 monster at a time cleanly.
/// </summary>
public class MonsterSpawnZone : MonoBehaviour
{
    /// <summary>All active MonsterSpawnZones currently loaded in the scene (read by MapManager & MinimapUI).</summary>
    public static readonly List<MonsterSpawnZone> All = new List<MonsterSpawnZone>();

    /// <summary>Authoritative per-zone damage ledgers keyed by ZoneSignature (for participation-locked LootChest &amp; zero-freeloading Gold).</summary>
    private static readonly Dictionary<int, Dictionary<PlayerRef, float>> s_zoneDamageLedgers =
        new Dictionary<int, Dictionary<PlayerRef, float>>();

    private class SpawnSlotState
    {
        public int SlotIndex;
        public Vector3 AnchorPosition;
        public EnemyNPC ActiveInstance;
        public bool WaitingRespawn;
        public float RespawnAt;
    }

    [Header("1. มอนสเตอร์และจำนวนที่เกิดในโซนนี้")]
    [Tooltip("Prefab มอนสเตอร์ของโซนนี้ (เช่น Enemy_Orc.prefab)")]
    [SerializeField] private EnemyNPC monsterPrefab = null;
    [Tooltip("จำนวนมอนสเตอร์สูงสุดในโซนนี้")]
    [Min(1)] [SerializeField] private int spawnCount = 4;
    [Tooltip("จุดเกิดแบบกำหนดเอง (ถ้าเว้นว่าง ระบบจะกระจายจุดเกิดรอบโซนตามระยะห่างให้อัตโนมัติ)")]
    [SerializeField] private Transform[] customSpawnPoints = null;
    [Tooltip("ระยะห่างระหว่างแต่ละตัว (เมตร) — ค่ากว้างจะช่วยให้ลากตีทีละตัวได้ง่าย")]
    [Min(4f)] [SerializeField] private float slotSpacing = 12f;
    [Tooltip("รัศมีเขตโซนที่แสดงเป็นวงสีเขียวใน Scene View และวงเขตบน World Map (เมตร)")]
    [Min(4f)] [SerializeField] private float territoryRadius = 15f;

    [Header("2. กติกาการเกิดใหม่ & เลเวลประจำโซน (MMO Respawn)")]
    [Tooltip("เปิดใช้เลเวลคงที่ประจำโซนแบบ MMORPG (เหมือนระบบโซนของเพื่อนบน TMRKKxOG) — ถ้าติ๊กออกจะสเกลตามผู้เล่นเลเวลสูงสุด")]
    [SerializeField] private bool useFixedZoneLevel = true;
    [Min(1)] [SerializeField] private int zoneMinLevel = 1;
    [Min(1)] [SerializeField] private int zoneMaxLevel = 2;
    [Tooltip("CampClearWave = ตายยกแคมป์ -> ดรอปกล่องสมบัติ -> เกิดใหม่พร้อมกันทั้งแคมป์ (แนะนำ) | IndividualStaggered = ตายตัวไหนเกิดตัวนั้น")]
    [SerializeField] private MonsterZoneRespawnMode respawnMode = MonsterZoneRespawnMode.CampClearWave;
    [Tooltip("เวลาเกิดใหม่ต่ำสุด (วินาที)")]
    [Min(5f)] [SerializeField] private float respawnMinSeconds = 35f;
    [Tooltip("เวลาเกิดใหม่สูงสุด (วินาที)")]
    [Min(5f)] [SerializeField] private float respawnMaxSeconds = 50f;
    [Tooltip("กันเกิดทับหัวผู้เล่น: ถ้าผู้เล่นยืนใกล้จุดเกิดกว่านี้จะรอจนผู้เล่นเดินออกห่างก่อน")]
    [Min(1f)] [SerializeField] private float antiPopInPlayerRadius = 6.0f;
    [Min(0.5f)] [SerializeField] private float antiPopInRetryDelay = 2.0f;

    [Header("2.5 แผนที่ (World Map) & กล่องสมบัติเมื่อเคลียร์แคมป์")]
    [Tooltip("หมายเลขแคมป์ที่จะโชว์บนแผนที่ (0 = รันเลขต่อจากแคมป์ที่มีให้อัตโนมัติ เช่น Camp 5, Camp 6, Camp 7...)")]
    [Min(0)] [SerializeField] private int campNumber = 0;
    [Tooltip("ชื่อแคมป์ที่โชว์บนแผนที่ (ถ้าเว้นว่างจะใช้ชื่อมอนสเตอร์อัตโนมัติ เช่น Slime, TurtleShell)")]
    [SerializeField] private string campDisplayName = "";
    [Tooltip("แสดงหมุด Camp + วงกลมขอบเขต + เวลานับถอยหลังเกิดใหม่บนหน้าต่าง World Map (M)")]
    [SerializeField] private bool showOnWorldMap = true;
    [Tooltip("เสกกล่องสมบัติ (LootChest) ตรงกลางโซนทันทีเมื่อเคลียร์มอนครบทุกตัวในโซนนี้ (พร้อมขึ้นไอคอนหีบบนแผนที่)")]
    [SerializeField] private bool spawnRewardChestOnZoneClear = true;
    [Tooltip("แจกโบนัส Gold เมื่อเคลียร์โซนนี้ครบทุกตัว (สเกลตามเลเวลของโซน)")]
    [SerializeField] private bool awardClearGold = true;

    [Header("3. สีเส้นเขตใน Scene View & Log")]
    [SerializeField] private Color gizmoTerritoryColor = new Color(0.35f, 0.85f, 0.35f, 0.75f);
    [SerializeField] private Color gizmoAnchorColor = new Color(1.0f, 0.55f, 0.15f, 0.85f);
    [Tooltip("ปิดไว้ (แนะนำสำหรับ Multiplayer): ให้เฉพาะ MasterClient เสกมอน เพื่อกันไม่ให้เพื่อนที่เข้าห้องตอนแคมป์เพิ่งถูกเคลียร์เสกมอนเบิ้ลซ้อนกัน")]
    [SerializeField] private bool allowNonMasterFallbackSpawn = false;
    [Tooltip("ติ๊กเมื่ออยากดูข้อความแจ้งเตือน Spawn/Respawn ใน Console")]
    [SerializeField] private bool verboseLogs = false;

    private readonly List<SpawnSlotState> _slots = new List<SpawnSlotState>();

    private int _zoneSignature;
    private bool _initialized;
    private bool _initialSpawnDone;
    private bool _campWaveWaiting;
    private float _campWaveRespawnAt;
    private float _nonMasterWaitStart = -1f;
    private bool _actingAsFallbackSpawner;
    private bool _everSawLivingMonster;
    private bool _zoneClearRewarded;
    private float _authoritativeClearAt = -10f;
    private float _zeroLivingDetectedAt = -1f;
    private float _clientEstimatedRespawnAt = -1f;
    private LootChest _activeRewardChest;
    private MonsterSystemManager _parentManager;

    public EnemyNPC MonsterPrefab => monsterPrefab;
    public int SpawnCount => spawnCount;
    public int TotalSlotCount => _slots.Count > 0 ? _slots.Count : spawnCount;
    public float TerritoryRadius => territoryRadius;
    public int ZoneMinLevel => Mathf.Max(1, Mathf.Min(zoneMinLevel, zoneMaxLevel));
    public int ZoneMaxLevel => Mathf.Max(ZoneMinLevel, Mathf.Max(zoneMinLevel, zoneMaxLevel));
    public bool ShowOnWorldMap => showOnWorldMap;
    public bool IsBossZone => monsterPrefab is MonsterEnemyNPC mNpc && mNpc.IsConfiguredAsBoss;
    public bool IsZoneCleared => _everSawLivingMonster && GetNetworkLivingCount() == 0;

    /// <summary>
    /// Deterministic Camp number resolution: uses explicit <see cref="campNumber"/> if > 0;
    /// otherwise sorts all auto-numbered zones deterministically by (X, Z, Name) so Host &amp; Clients always match 100%,
    /// starting right after <see cref="SandboxWorldSpawner"/>'s camps (or from Camp 1 if SandboxWorldSpawner is removed!).
    /// </summary>
    public int ResolvedCampNumber
    {
        get
        {
            if (campNumber > 0) return campNumber;
            int baseCount = (SandboxWorldSpawner.Instance != null && SandboxWorldSpawner.Instance.Camps != null)
                ? SandboxWorldSpawner.Instance.Camps.Count
                : 0;

            int rankBeforeMe = 0;
            for (int i = 0; i < All.Count; i++)
            {
                MonsterSpawnZone other = All[i];
                if (other == null || other == this || other.campNumber != 0) continue;
                if (CompareZoneOrder(other, this) < 0)
                    rankBeforeMe++;
            }

            int candidate = baseCount + 1 + rankBeforeMe;
            bool collision = true;
            while (collision)
            {
                collision = false;
                for (int i = 0; i < All.Count; i++)
                {
                    if (All[i] != null && All[i] != this && All[i].campNumber == candidate)
                    {
                        candidate++;
                        collision = true;
                        break;
                    }
                }
            }
            return candidate;
        }
    }

    private static int CompareZoneOrder(MonsterSpawnZone a, MonsterSpawnZone b)
    {
        int cmpX = Mathf.RoundToInt(a.transform.position.x * 10f).CompareTo(Mathf.RoundToInt(b.transform.position.x * 10f));
        if (cmpX != 0) return cmpX;
        int cmpZ = Mathf.RoundToInt(a.transform.position.z * 10f).CompareTo(Mathf.RoundToInt(b.transform.position.z * 10f));
        if (cmpZ != 0) return cmpZ;
        return string.CompareOrdinal(a.gameObject.name, b.gameObject.name);
    }

    public string ResolvedDisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(campDisplayName))
                return campDisplayName.Trim();
            if (monsterPrefab != null)
            {
                string n = monsterPrefab.name;
                if (n.StartsWith("Enemy_")) n = n.Substring(6);
                return n;
            }
            string zn = gameObject.name;
            if (zn.StartsWith("Zone_")) zn = zn.Substring(5);
            return zn;
        }
    }

    public float NextRespawnRemainingSeconds
    {
        get
        {
            float now = Time.time;
            if (respawnMode == MonsterZoneRespawnMode.CampClearWave && _campWaveWaiting)
                return Mathf.Max(0f, _campWaveRespawnAt - now);

            float shortest = float.MaxValue;
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].WaitingRespawn)
                {
                    float rem = Mathf.Max(0f, _slots[i].RespawnAt - now);
                    if (rem < shortest) shortest = rem;
                }
            }
            if (shortest < float.MaxValue) return shortest;

            // Non-Master client live countdown estimate when the zone is cleared!
            if (IsZoneCleared && _clientEstimatedRespawnAt > now)
                return Mathf.Max(0f, _clientEstimatedRespawnAt - now);

            return 0f;
        }
    }

    public string GetFormattedRespawnTime()
    {
        int totalSec = Mathf.CeilToInt(Mathf.Max(0f, NextRespawnRemainingSeconds));
        int mins = totalSec / 60;
        int secs = totalSec % 60;
        return $"{mins:00}:{secs:00}";
    }

    /// <summary>
    /// Returns the world position for the World Map / Minimap marker.
    /// For Boss zones with a living boss, tracks the boss's real live position (matching SandboxWorldSpawner);
    /// otherwise returns the zone center.
    /// </summary>
    public Vector3 GetMapMarkerPosition()
    {
        if (IsBossZone && _zoneSignature != 0)
        {
            for (int i = 0; i < EnemyNPC.All.Count; i++)
            {
                MonsterEnemyNPC m = EnemyNPC.All[i] as MonsterEnemyNPC;
                if (m != null && m.IsSpawned && m.IsAlive && m.ZoneSignature == _zoneSignature)
                    return m.transform.position;
            }
        }
        return transform.position;
    }

    public string GetMapMarkerLabel()
    {
        int num = ResolvedCampNumber;
        string custom = !string.IsNullOrWhiteSpace(campDisplayName) ? campDisplayName.Trim() : string.Empty;
        string mob = ResolvedDisplayName;
        bool customHasPrefix = !string.IsNullOrEmpty(custom) &&
                               (custom.StartsWith("Camp", System.StringComparison.OrdinalIgnoreCase) ||
                                custom.StartsWith("Boss", System.StringComparison.OrdinalIgnoreCase) ||
                                custom.StartsWith("แคมป์") ||
                                custom.StartsWith("บอส"));

        string title = customHasPrefix
            ? custom
            : (IsBossZone ? $"Boss {mob}" : $"Camp {num} {mob}");

        if (IsZoneCleared && NextRespawnRemainingSeconds > 0.5f)
            return customHasPrefix
                ? $"{custom} ({GetFormattedRespawnTime()})"
                : (IsBossZone ? $"Boss {mob} ({GetFormattedRespawnTime()})" : $"Camp {num} ({GetFormattedRespawnTime()})");

        string lvlTag = ZoneMinLevel == ZoneMaxLevel ? $"Lv.{ZoneMinLevel}" : $"Lv.{ZoneMinLevel}-{ZoneMaxLevel}";
        return $"{title} ({lvlTag})";
    }

    /// <summary>
    /// Allows <see cref="PlayerStatusHud"/> to display the Boss Respawn Countdown Banner even when
    /// <see cref="SandboxWorldSpawner"/> is deleted from the scene and Bosses are spawned via <c>Zone_Boss*</c>!
    /// </summary>
    public static bool TryGetNearbyRespawningBossBanner(Vector3 playerPos, out string bannerText)
    {
        for (int i = 0; i < All.Count; i++)
        {
            MonsterSpawnZone z = All[i];
            if (z == null || !z.isActiveAndEnabled || !z.IsBossZone) continue;
            if (!z.IsZoneCleared || z.NextRespawnRemainingSeconds <= 0.5f) continue;

            float dist = Vector2.Distance(
                new Vector2(playerPos.x, playerPos.z),
                new Vector2(z.transform.position.x, z.transform.position.z));
            // ขยายระยะตรวจจับแบนเนอร์นับถอยหลังบอสให้ครอบคลุมลานบอสขนาดใหญ่บนแผนที่ FinalTestv1
            if (dist <= Mathf.Max(60f, z.TerritoryRadius + 10f))
            {
                bannerText = $"Boss {z.ResolvedDisplayName} respawns in {z.GetFormattedRespawnTime()}";
                return true;
            }
        }
        bannerText = string.Empty;
        return false;
    }

    public int AliveCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < _slots.Count; i++)
            {
                var inst = _slots[i].ActiveInstance;
                if (inst != null && inst.IsSpawned && inst.IsAlive) count++;
            }
            return count;
        }
    }

    public int RespawningCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < _slots.Count; i++)
                if (_slots[i].WaitingRespawn) count++;
            return count;
        }
    }

    private void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
    }

    private void OnDisable()
    {
        All.Remove(this);
        if (_zoneSignature != 0)
            s_zoneDamageLedgers.Remove(_zoneSignature);
    }

    private void Awake()
    {
        // Standalone drop-in guarantee: ensure PlayerSaveManager exists even if SandboxWorldSpawner is removed!
        PlayerSaveManager.EnsureExists();
        _parentManager = GetComponentInParent<MonsterSystemManager>();
        _zoneSignature = ComputeZoneSignature();
        BuildSlots();
    }

    private void Start()
    {
        // Standalone drop-in guarantee: if user deleted SandboxWorldSpawner from Dev_Kulpat (or a custom map) and no SafeZone exists,
        // automatically create the Comfort Safe Zone at (0, 0, -60) so player respawn/safe haven/map work 100% identically!
        if (SandboxWorldSpawner.Instance == null && SafeZone.Instance == null)
        {
            MonsterSystemManager mgr = _parentManager != null ? _parentManager : MonsterSystemManager.Active;
            Vector3 center = mgr != null ? mgr.fallbackSafeZoneCenter : new Vector3(0f, 0f, -60f);
            float radius = mgr != null ? mgr.fallbackSafeZoneRadius : 12f;
            Vector3? reconnectOffset = mgr != null ? mgr.fallbackSafeZoneReconnectOffset : (Vector3?)null;
            bool showVisuals = mgr != null && mgr.showProceduralSafeZoneVisuals;
            SafeZone.EnsureAt(center, radius, reconnectOffset, showVisuals);
        }
    }

    private int ComputeZoneSignature()
    {
        unchecked
        {
            int hash = (int)2166136261;
            string key = $"{gameObject.scene.name}|{gameObject.name}|{Mathf.RoundToInt(transform.position.x * 2f)}|{Mathf.RoundToInt(transform.position.z * 2f)}";
            for (int i = 0; i < key.Length; i++)
            {
                hash ^= key[i];
                hash *= 16777619;
            }
            return hash == 0 ? 1 : hash;
        }
    }

    private void BuildSlots()
    {
        if (_initialized) return;
        _initialized = true;
        _slots.Clear();

        if (customSpawnPoints != null && customSpawnPoints.Length > 0)
        {
            int idx = 0;
            for (int i = 0; i < customSpawnPoints.Length; i++)
            {
                if (customSpawnPoints[i] == null) continue;
                Vector3 grounded = ProjectPointOntoGround(customSpawnPoints[i].position);
                _slots.Add(new SpawnSlotState { SlotIndex = idx++, AnchorPosition = grounded });
            }
        }
        else
        {
            Vector3[] generated = GenerateSpacedPositions(transform.position, spawnCount, slotSpacing);
            for (int i = 0; i < generated.Length; i++)
            {
                Vector3 grounded = ProjectPointOntoGround(generated[i]);
                _slots.Add(new SpawnSlotState { SlotIndex = i, AnchorPosition = grounded });
            }
        }
    }

    /// <summary>
    /// แนบจุดเกิดของมอนสเตอร์เข้ากับระดับพื้นผิวจริงที่ใกล้ rawPos.y ที่สุดผ่าน SandboxWorldSpawner.SampleGroundY
    /// ป้องกันมอนสเตอร์ไปเกิดบนยอดต้นไม้ หลังคาเต็นท์ หรือผิวน้ำบนแผนที่ภูมิประเทศขนาดใหญ่ (FinalTestv1)
    /// </summary>
    private static Vector3 ProjectPointOntoGround(Vector3 rawPos)
    {
        float groundY = SandboxWorldSpawner.SampleGroundY(rawPos, rawPos.y);
        return new Vector3(rawPos.x, groundY + 1.2f, rawPos.z);
    }

    private static Vector3[] GenerateSpacedPositions(Vector3 center, int count, float spacing)
    {
        var pts = new Vector3[Mathf.Max(1, count)];
        if (count == 1)
        {
            pts[0] = new Vector3(center.x, center.y + 1.2f, center.z);
            return pts;
        }

        float half = spacing * 0.5f;
        if (count == 4)
        {
            pts[0] = new Vector3(center.x - half, center.y + 1.2f, center.z + half);
            pts[1] = new Vector3(center.x + half, center.y + 1.2f, center.z + half);
            pts[2] = new Vector3(center.x - half, center.y + 1.2f, center.z - half);
            pts[3] = new Vector3(center.x + half, center.y + 1.2f, center.z - half);
            return pts;
        }

        // General polygon ring ensuring consecutive points are at least ~spacing apart
        float radius = Mathf.Max(half, spacing / (2f * Mathf.Sin(Mathf.PI / count)));
        for (int i = 0; i < count; i++)
        {
            float ang = (i / (float)count) * Mathf.PI * 2f + 0.35f;
            pts[i] = new Vector3(center.x + Mathf.Cos(ang) * radius, center.y + 1.2f, center.z + Mathf.Sin(ang) * radius);
        }
        return pts;
    }

    private void Update()
    {
        if (monsterPrefab == null) return;

        MonsterSystemManager mgr = _parentManager != null ? _parentManager : MonsterSystemManager.Active;
        if (mgr != null && !mgr.enableAllZones) return;

        PlayerCombat local = PlayerCombat.Local;
        if (local == null || !local.IsSpawned) return;

        if (PlayerSaveManager.Instance != null && !PlayerSaveManager.Instance.HasLoadedOrInitialized)
            return;

        NetworkRunner runner = local.Runner;
        if (runner == null || !runner.IsRunning) return;

        // Track living monsters & trigger Zone Clear Reward Chest + Gold across all peers!
        TickZoneClearRewardTracker(local, mgr);

        bool isMaster = runner.IsSharedModeMasterClient;
        if (!isMaster)
        {
            if (!allowNonMasterFallbackSpawn) return;

            if (!_actingAsFallbackSpawner)
            {
                if (_nonMasterWaitStart < 0f)
                    _nonMasterWaitStart = Time.time;

                if (Time.time - _nonMasterWaitStart < 2.5f)
                    return;

                if (AdoptExistingZoneMonsters() > 0)
                {
                    _initialSpawnDone = true;
                    return;
                }

                _actingAsFallbackSpawner = true;
                if (verboseLogs) Debug.Log($"[MonsterSpawnZone] '{name}' non-Master fallback enabled (no monsters spawned by Master after 2.5s).");
            }
        }
        else
        {
            _actingAsFallbackSpawner = false;
            _nonMasterWaitStart = -1f;
        }

        if (!_initialSpawnDone)
        {
            _initialSpawnDone = true;

            // Host-Migration Safeguard: adopt any already-spawned monsters belonging to this zone first!
            int adopted = AdoptExistingZoneMonsters();

            if (adopted > 0)
            {
                if (verboseLogs) Debug.Log($"[MonsterSpawnZone] '{name}' adopted {adopted} existing monsters after Master Client handoff (zero duplicates).");
                // Only fill genuinely empty slots (if any died before handoff, respawn timer will handle them naturally)
                return;
            }

            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].ActiveInstance == null)
                    TrySpawnIntoSlot(runner, _slots[i], ignoreAntiPopIn: true);
            }
            return;
        }

        if (respawnMode == MonsterZoneRespawnMode.IndividualStaggered)
            TickIndividualRespawns(runner);
        else
            TickCampWaveRespawn(runner);
    }

    private int GetNetworkLivingCount()
    {
        int living = 0;
        for (int i = 0; i < EnemyNPC.All.Count; i++)
        {
            MonsterEnemyNPC m = EnemyNPC.All[i] as MonsterEnemyNPC;
            if (m == null || !m.IsSpawned || !m.IsAlive) continue;
            if (m.ZoneSignature == _zoneSignature)
                living++;
        }
        return living;
    }

    public static void RecordZoneDamageFromAuthority(int zoneSignature, PlayerRef attacker, float damageAmount)
    {
        if (zoneSignature == 0 || attacker == PlayerRef.None || damageAmount <= 0f) return;
        if (!s_zoneDamageLedgers.TryGetValue(zoneSignature, out var ledger))
        {
            ledger = new Dictionary<PlayerRef, float>();
            s_zoneDamageLedgers[zoneSignature] = ledger;
        }
        if (ledger.TryGetValue(attacker, out float prev))
            ledger[attacker] = prev + damageAmount;
        else
            ledger[attacker] = damageAmount;
    }

    public static void ClearZoneDamageLedger(int zoneSignature)
    {
        if (zoneSignature != 0 && s_zoneDamageLedgers.TryGetValue(zoneSignature, out var ledger))
            ledger.Clear();
    }

    public static string BuildZoneContributorPayload(int zoneSignature, bool isBoss = false)
    {
        if (zoneSignature == 0 || !s_zoneDamageLedgers.TryGetValue(zoneSignature, out var ledger) || ledger.Count == 0)
            return string.Empty;

        LootDropSystem.AnalyzeDamageContribution(
            ledger,
            PlayerRef.None,
            isBoss,
            out float totalDamage,
            out PlayerRef topDps,
            out List<PlayerRef> participants);

        if (participants.Count == 0 || totalDamage <= 0f) return string.Empty;

        participants.Sort((a, b) =>
        {
            float da = ledger.TryGetValue(a, out float va) ? va : 0f;
            float db = ledger.TryGetValue(b, out float vb) ? vb : 0f;
            return db.CompareTo(da);
        });

        // รองรับผู้เล่นช่วยตีพร้อมกันสูงสุด 10 คนภายใน NetworkString<_128> (ไม่เกินขีดจำกัด 512 ไบต์ของ Photon Fusion)
        var sb = new StringBuilder(128);
        for (int i = 0; i < participants.Count; i++)
        {
            PlayerRef p = participants[i];
            if (!ledger.TryGetValue(p, out float dealt) || dealt <= 0f) continue;

            float share = Mathf.Clamp01(dealt / totalDamage);
            int topFlag = p == topDps ? 1 : 0;
            string token = $"{p.RawEncoded.ToString(CultureInfo.InvariantCulture)}:{share.ToString("0.##", CultureInfo.InvariantCulture)}:{topFlag}";
            int needed = (sb.Length > 0 ? 1 : 0) + token.Length;
            if (sb.Length + needed > 124) break;

            if (sb.Length > 0) sb.Append(';');
            sb.Append(token);
        }
        return sb.ToString();
    }

    public static void HandleAuthoritativeZoneCleared(int zoneSignature, string contributorPayload)
    {
        if (zoneSignature == 0) return;
        for (int i = 0; i < All.Count; i++)
        {
            MonsterSpawnZone z = All[i];
            if (z != null && z.isActiveAndEnabled && z._zoneSignature == zoneSignature)
            {
                z.HandleZoneClearedInternal(contributorPayload);
                break;
            }
        }
    }

    private void TickZoneClearRewardTracker(PlayerCombat localPlayer, MonsterSystemManager mgr)
    {
        int living = GetNetworkLivingCount();
        if (living > 0)
        {
            // Keep a short 2.5s sync grace period right after an authoritative clear RPC so lag never destroys the freshly spawned chest
            if (!_zoneClearRewarded || (Time.unscaledTime - _authoritativeClearAt >= 2.5f))
            {
                _everSawLivingMonster = true;
                if (_zoneClearRewarded)
                {
                    _zoneClearRewarded = false;
                    _zeroLivingDetectedAt = -1f;
                    _clientEstimatedRespawnAt = -1f;
                    ClearZoneDamageLedger(_zoneSignature);

                    // Destroy any leftover unopened reward chest when the camp respawns!
                    if (_activeRewardChest != null)
                    {
                        Destroy(_activeRewardChest.gameObject);
                        _activeRewardChest = null;
                    }
                }
            }
            return;
        }

        if (!_everSawLivingMonster || _zoneClearRewarded) return;

        // Wait briefly (0.65s) for RPC_NotifyMonsterZoneCleared from StateAuthority before using strict fallback
        if (_zeroLivingDetectedAt < 0f)
        {
            _zeroLivingDetectedAt = Time.unscaledTime;
            return;
        }
        if (Time.unscaledTime - _zeroLivingDetectedAt < 0.65f)
            return;

        NetworkRunner runner = localPlayer != null ? localPlayer.Runner : null;
        bool isMaster = runner != null && runner.IsRunning && runner.IsSharedModeMasterClient;
        string fallbackPayload = isMaster ? BuildZoneContributorPayload(_zoneSignature, IsBossZone) : string.Empty;
        HandleZoneClearedInternal(fallbackPayload);
    }

    private void HandleZoneClearedInternal(string contributorPayload)
    {
        if (_zoneClearRewarded) return;
        _zoneClearRewarded = true;
        _authoritativeClearAt = Time.unscaledTime;
        _zeroLivingDetectedAt = -1f;

        float avgRespawn = ((Mathf.Min(respawnMinSeconds, respawnMaxSeconds) + Mathf.Max(respawnMinSeconds, respawnMaxSeconds)) * 0.5f) / GetRespawnSpeedMultiplier();
        _clientEstimatedRespawnAt = Time.time + Mathf.Max(2f, avgRespawn);

        var contributors = SandboxWorldSpawner.ParseContributorPayload(contributorPayload);

        PlayerCombat localPlayer = PlayerCombat.Local;
        PlayerRef localRef = PlayerRef.None;
        if (localPlayer != null && localPlayer.IsSpawned)
        {
            if (localPlayer.Object != null && localPlayer.Object.IsValid && localPlayer.Object.StateAuthority != PlayerRef.None)
                localRef = localPlayer.Object.StateAuthority;
            else if (localPlayer.Runner != null && localPlayer.Runner.IsRunning)
                localRef = localPlayer.Runner.LocalPlayer;
        }

        bool localParticipated = localRef != PlayerRef.None &&
                                 contributors.TryGetValue(localRef.RawEncoded, out var localContrib) &&
                                 localContrib.damageShare > 0f;

        int campNum = ResolvedCampNumber;
        bool isBoss = IsBossZone;
        MonsterSystemManager mgr = _parentManager != null ? _parentManager : MonsterSystemManager.Active;

        // 1. Spawn teammate's LootChest at zone center & configure real participant permissions (unlocks for fighters, locks out freeloaders!)
        if (spawnRewardChestOnZoneClear)
        {
            Vector3 groundedCenter = ProjectPointOntoGround(transform.position);
            float groundY = groundedCenter.y - 1.2f;
            WorldItem itemPrefab = mgr != null ? mgr.sharedWorldItemPrefab : null;
            // Use unique per-zone key derived from _zoneSignature (never collides with SandboxWorldSpawner Camps 1-4/99 or duplicate campNumbers!)
            int chestCampKey = 100000 + (_zoneSignature & 0x0FFFFFFF);
            _activeRewardChest = LootChest.SpawnAt(new Vector3(transform.position.x, groundY, transform.position.z), chestCampKey, itemPrefab, isBossChest: isBoss, replaceExisting: true);
            if (_activeRewardChest != null)
            {
                _activeRewardChest.transform.position = new Vector3(transform.position.x, groundY, transform.position.z);
                _activeRewardChest.ConfigureParticipation(contributors);
            }
        }

        // 2. Award Camp Clear Gold bonus & floating celebration popup ONLY to real battle participants (Zero-Freeloading!)
        if (localParticipated && localPlayer != null && localPlayer.IsSpawned && !localPlayer.IsDead)
        {
            if (awardClearGold)
            {
                // ปรับสมดุลโบนัส Gold เคลียร์แคมป์ให้สัมพันธ์กับราคาตีบวกและอัป Stat ใหม่ (Camp 1 ~90G, Camp 4 ~205G, Camp 6 ~330G, Boss 900..1,400G)
                int avgLvl = Mathf.Max(1, (ZoneMinLevel + ZoneMaxLevel) / 2);
                int minGold;
                int maxGold;
                if (isBoss)
                {
                    float bossScale = 1f + Mathf.Max(0, avgLvl - 20) * 0.08f;
                    minGold = Mathf.RoundToInt(900f * bossScale);
                    maxGold = Mathf.RoundToInt(1400f * bossScale);
                }
                else
                {
                    float scale = 1f + (avgLvl - 1) * 0.18f;
                    minGold = Mathf.RoundToInt(55f * scale);
                    maxGold = Mathf.RoundToInt(100f * scale);
                }
                int goldReward = Random.Range(minGold, maxGold + 1);
                localPlayer.AddGold(goldReward, $"Camp {campNum} ({ResolvedDisplayName})");
            }

            string banner = isBoss ? $"BOSS {ResolvedDisplayName.ToUpper()} CLEARED!" : $"CAMP {campNum} CLEARED!";
            FloatingDamagePopup.SpawnWarning(localPlayer.transform.position + Vector3.up * 2.4f, banner, new Color(1.0f, 0.85f, 0.20f, 1f));
        }

        if (verboseLogs)
            Debug.Log($"[MonsterSpawnZone] Camp {campNum} ({ResolvedDisplayName}) CLEARED! EligibleContributors={contributors.Count}, LocalParticipated={localParticipated}.");
    }

    private int AdoptExistingZoneMonsters()
    {
        int adopted = 0;
        for (int i = 0; i < EnemyNPC.All.Count; i++)
        {
            MonsterEnemyNPC m = EnemyNPC.All[i] as MonsterEnemyNPC;
            if (m == null || !m.IsSpawned) continue;
            if (m.ZoneSignature != _zoneSignature) continue;

            int sIdx = m.ZoneSlotIndex;
            if (sIdx >= 0 && sIdx < _slots.Count && _slots[sIdx].ActiveInstance == null)
            {
                _slots[sIdx].ActiveInstance = m;
                _slots[sIdx].WaitingRespawn = false;
                adopted++;
                continue;
            }

            // Fallback: bind to first unassigned slot
            for (int s = 0; s < _slots.Count; s++)
            {
                if (_slots[s].ActiveInstance == null)
                {
                    _slots[s].ActiveInstance = m;
                    _slots[s].WaitingRespawn = false;
                    adopted++;
                    break;
                }
            }
        }
        return adopted;
    }

    private void TickIndividualRespawns(NetworkRunner runner)
    {
        float now = Time.time;

        for (int i = 0; i < _slots.Count; i++)
        {
            SpawnSlotState slot = _slots[i];
            bool aliveOrPresent = slot.ActiveInstance != null && slot.ActiveInstance.IsSpawned && slot.ActiveInstance.IsAlive;

            if (aliveOrPresent)
            {
                slot.WaitingRespawn = false;
                continue;
            }

            // Wait until the corpse has despawned before starting / finishing respawn
            bool corpseStillPresent = slot.ActiveInstance != null && slot.ActiveInstance.IsSpawned;
            if (corpseStillPresent) continue;

            if (!slot.WaitingRespawn)
            {
                slot.ActiveInstance = null;
                slot.WaitingRespawn = true;
                float minS = Mathf.Min(respawnMinSeconds, respawnMaxSeconds);
                float maxS = Mathf.Max(respawnMinSeconds, respawnMaxSeconds);
                float speedMult = GetRespawnSpeedMultiplier();
                float wait = Mathf.Max(2f, Random.Range(minS, maxS) / speedMult);
                slot.RespawnAt = now + wait;
                if (verboseLogs) Debug.Log($"[MonsterSpawnZone] '{name}' slot #{i + 1} cleared -> respawning in {wait:0.0}s.");
            }
            else if (now >= slot.RespawnAt)
            {
                if (IsAnyPlayerTooClose(slot.AnchorPosition, antiPopInPlayerRadius))
                {
                    slot.RespawnAt = now + antiPopInRetryDelay;
                    continue;
                }

                if (TrySpawnIntoSlot(runner, slot, ignoreAntiPopIn: false))
                {
                    slot.WaitingRespawn = false;
                    if (verboseLogs) Debug.Log($"[MonsterSpawnZone] '{name}' slot #{i + 1} respawned '{monsterPrefab.name}'.");
                }
            }
        }
    }

    private void TickCampWaveRespawn(NetworkRunner runner)
    {
        int living = 0;
        for (int i = 0; i < _slots.Count; i++)
        {
            var inst = _slots[i].ActiveInstance;
            if (inst != null && inst.IsSpawned && inst.IsAlive) living++;
        }

        if (living > 0)
        {
            _campWaveWaiting = false;
            return;
        }

        float now = Time.time;
        if (!_campWaveWaiting)
        {
            _campWaveWaiting = true;
            float minS = Mathf.Min(respawnMinSeconds, respawnMaxSeconds);
            float maxS = Mathf.Max(respawnMinSeconds, respawnMaxSeconds);
            float speedMult = GetRespawnSpeedMultiplier();
            float wait = Mathf.Max(2f, Random.Range(minS, maxS) / speedMult);
            _campWaveRespawnAt = now + wait;
            if (verboseLogs) Debug.Log($"[MonsterSpawnZone] '{name}' wave cleared -> respawning wave in {wait:0.0}s.");
        }
        else if (now >= _campWaveRespawnAt)
        {
            if (IsAnyPlayerTooClose(transform.position, antiPopInPlayerRadius))
            {
                _campWaveRespawnAt = now + antiPopInRetryDelay;
                return;
            }

            for (int i = 0; i < _slots.Count; i++)
                TrySpawnIntoSlot(runner, _slots[i], ignoreAntiPopIn: false);

            _campWaveWaiting = false;
        }
    }

    private bool TrySpawnIntoSlot(NetworkRunner runner, SpawnSlotState slot, bool ignoreAntiPopIn)
    {
        if (runner == null || !runner.IsRunning || monsterPrefab == null) return false;
        if (!ignoreAntiPopIn && IsAnyPlayerTooClose(slot.AnchorPosition, antiPopInPlayerRadius)) return false;

        Vector3 spawnPos = slot.AnchorPosition;
        if (SafeZone.IsPointInAnySafeZone(spawnPos, 1.0f)) return false;

        int scaleLvl = ResolveSpawnLevel();
        Quaternion rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        int sig = _zoneSignature;
        int slotIdx = slot.SlotIndex;

        EnemyNPC spawned = runner.Spawn(monsterPrefab, spawnPos, rot, null, (r, obj) =>
        {
            var monsterNpc = obj.GetComponent<MonsterEnemyNPC>();
            bool configuredBoss = monsterNpc != null && monsterNpc.IsConfiguredAsBoss;

            var npc = obj.GetComponent<EnemyNPC>();
            if (npc != null)
            {
                npc.CampId = 0; // Individual solo-pull zone (no shared camp aggro)
                npc.IsBoss = configuredBoss;
                npc.ScaledLevel = scaleLvl;
                npc.ApplyZoneLevel(scaleLvl);
            }
            if (monsterNpc != null)
            {
                monsterNpc.ZoneSignature = sig;
                monsterNpc.ZoneSlotIndex = slotIdx;
                if (configuredBoss)
                    monsterNpc.SuppressBaseBossPrimitives();
            }
        });

        if (spawned != null)
        {
            slot.ActiveInstance = spawned;
            return true;
        }
        return false;
    }

    private float GetRespawnSpeedMultiplier()
    {
        MonsterSystemManager mgr = _parentManager != null ? _parentManager : MonsterSystemManager.Active;
        return mgr != null ? Mathf.Max(0.1f, mgr.globalRespawnSpeedMultiplier) : 1.0f;
    }

    private static bool IsAnyPlayerTooClose(Vector3 point, float minDistance)
    {
        float sqrMin = minDistance * minDistance;
        for (int i = 0; i < PlayerCombat.All.Count; i++)
        {
            PlayerCombat p = PlayerCombat.All[i];
            if (p == null || !p.IsSpawned || p.IsDead) continue;
            Vector3 diff = p.transform.position - point;
            diff.y = 0f;
            if (diff.sqrMagnitude <= sqrMin) return true;
        }
        return false;
    }

    private int ResolveSpawnLevel()
    {
        if (useFixedZoneLevel)
        {
            int minL = Mathf.Max(1, Mathf.Min(zoneMinLevel, zoneMaxLevel));
            int maxL = Mathf.Max(minL, Mathf.Max(zoneMinLevel, zoneMaxLevel));
            return Random.Range(minL, maxL + 1);
        }
        return GetHighestPlayerLevel();
    }

    private static int GetHighestPlayerLevel()
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

    // ------------------------------------------------------------------ Scene-View Gizmos Only (Invisible in Game)

    private void OnDrawGizmos()
    {
        Gizmos.color = gizmoTerritoryColor;
        DrawFlatCircle(transform.position + Vector3.up * 0.08f, territoryRadius, 40);

        Vector3[] anchors = (customSpawnPoints != null && customSpawnPoints.Length > 0)
            ? ExtractValidPoints(customSpawnPoints)
            : GenerateSpacedPositions(transform.position, spawnCount, slotSpacing);

        Gizmos.color = gizmoAnchorColor;
        for (int i = 0; i < anchors.Length; i++)
        {
            Vector3 p = new Vector3(anchors[i].x, transform.position.y + 0.15f, anchors[i].z);
            Gizmos.DrawWireSphere(p, 0.65f);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Vector3[] anchors = (customSpawnPoints != null && customSpawnPoints.Length > 0)
            ? ExtractValidPoints(customSpawnPoints)
            : GenerateSpacedPositions(transform.position, spawnCount, slotSpacing);

        Gizmos.color = new Color(1f, 0.35f, 0.2f, 0.45f);
        for (int i = 0; i < anchors.Length; i++)
        {
            Vector3 p = new Vector3(anchors[i].x, transform.position.y + 0.12f, anchors[i].z);
            DrawFlatCircle(p, 5.5f, 24);
        }
    }

    private static Vector3[] ExtractValidPoints(Transform[] transforms)
    {
        var list = new List<Vector3>(transforms.Length);
        for (int i = 0; i < transforms.Length; i++)
            if (transforms[i] != null) list.Add(transforms[i].position);
        return list.ToArray();
    }

    private static void DrawFlatCircle(Vector3 center, float radius, int segments)
    {
        Vector3 prev = center + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float ang = (i / (float)segments) * Mathf.PI * 2f;
            Vector3 next = center + new Vector3(Mathf.Cos(ang) * radius, 0f, Mathf.Sin(ang) * radius);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }
}
