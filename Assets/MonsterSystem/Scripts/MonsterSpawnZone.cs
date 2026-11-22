using System.Collections.Generic;
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
    [Tooltip("รัศมีเขตโซนที่แสดงเป็นวงสีเขียวใน Scene View (มองไม่เห็นในเกมจริง)")]
    [Min(4f)] [SerializeField] private float territoryRadius = 15f;

    [Header("2. กติกาการเกิดใหม่ (MMO Respawn)")]
    [Tooltip("IndividualStaggered = ตายตัวไหนเกิดใหม่ตัวนั้น | CampClearWave = รอตายยกแก๊งแล้วเกิดใหม่พร้อมกัน")]
    [SerializeField] private MonsterZoneRespawnMode respawnMode = MonsterZoneRespawnMode.IndividualStaggered;
    [Tooltip("เวลาเกิดใหม่ต่ำสุด (วินาที)")]
    [Min(5f)] [SerializeField] private float respawnMinSeconds = 35f;
    [Tooltip("เวลาเกิดใหม่สูงสุด (วินาที)")]
    [Min(5f)] [SerializeField] private float respawnMaxSeconds = 50f;
    [Tooltip("กันเกิดทับหัวผู้เล่น: ถ้าผู้เล่นยืนใกล้จุดเกิดกว่านี้จะรอจนผู้เล่นเดินออกห่างก่อน")]
    [Min(1f)] [SerializeField] private float antiPopInPlayerRadius = 6.0f;
    [Min(0.5f)] [SerializeField] private float antiPopInRetryDelay = 2.0f;

    [Header("3. สีเส้นเขตใน Scene View & Log")]
    [SerializeField] private Color gizmoTerritoryColor = new Color(0.35f, 0.85f, 0.35f, 0.75f);
    [SerializeField] private Color gizmoAnchorColor = new Color(1.0f, 0.55f, 0.15f, 0.85f);
    [Tooltip("เปิดไว้ (แนะนำ): ถ้าเข้าห้อง Shared เดียวกับเพื่อนที่ไม่มีโซนนี้ในฉาก จะช่วยเกิดมอนให้หลังรอ 2.5 วิโดยไม่เกิดซ้ำ")]
    [SerializeField] private bool allowNonMasterFallbackSpawn = true;
    [Tooltip("ติ๊กเมื่ออยากดูข้อความแจ้งเตือน Spawn/Respawn ใน Console")]
    [SerializeField] private bool verboseLogs = false;

    private readonly List<SpawnSlotState> _slots = new List<SpawnSlotState>();
    private static readonly RaycastHit[] s_groundCastBuf = new RaycastHit[16];

    private int _zoneSignature;
    private bool _initialized;
    private bool _initialSpawnDone;
    private bool _campWaveWaiting;
    private float _campWaveRespawnAt;
    private float _nonMasterWaitStart = -1f;
    private bool _actingAsFallbackSpawner;
    private MonsterSystemManager _parentManager;

    public EnemyNPC MonsterPrefab => monsterPrefab;
    public int SpawnCount => spawnCount;
    public int TotalSlotCount => _slots.Count > 0 ? _slots.Count : spawnCount;

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

    private void Awake()
    {
        _parentManager = GetComponentInParent<MonsterSystemManager>();
        _zoneSignature = ComputeZoneSignature();
        BuildSlots();
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

    private static Vector3 ProjectPointOntoGround(Vector3 rawPos)
    {
        Vector3 origin = new Vector3(rawPos.x, rawPos.y + 45f, rawPos.z);
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, s_groundCastBuf, 120f, ~0, QueryTriggerInteraction.Ignore);
        float bestY = float.MinValue;
        bool found = false;

        for (int i = 0; i < count; i++)
        {
            RaycastHit h = s_groundCastBuf[i];
            if (h.collider == null || h.normal.y < 0.45f) continue;
            if (h.collider.GetComponentInParent<EnemyNPC>() != null) continue;
            if (h.collider.GetComponentInParent<PlayerCombat>() != null) continue;

            if (h.point.y > bestY)
            {
                bestY = h.point.y;
                found = true;
            }
        }

        if (found)
            return new Vector3(rawPos.x, bestY + 1.2f, rawPos.z);

        return new Vector3(rawPos.x, rawPos.y + 1.2f, rawPos.z);
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

                // If Master Client already spawned monsters for this zone, do nothing (zero duplicates).
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

        int scaleLvl = GetHighestPlayerLevel();
        Quaternion rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        int sig = _zoneSignature;
        int slotIdx = slot.SlotIndex;

        EnemyNPC spawned = runner.Spawn(monsterPrefab, spawnPos, rot, null, (r, obj) =>
        {
            var npc = obj.GetComponent<EnemyNPC>();
            if (npc != null)
            {
                npc.CampId = 0; // Individual solo-pull zone (no shared camp aggro)
                npc.IsBoss = false;
                npc.ScaledLevel = scaleLvl;
                npc.ApplyDynamicScaling(scaleLvl);
            }
            var monsterNpc = obj.GetComponent<MonsterEnemyNPC>();
            if (monsterNpc != null)
            {
                monsterNpc.ZoneSignature = sig;
                monsterNpc.ZoneSlotIndex = slotIdx;
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
