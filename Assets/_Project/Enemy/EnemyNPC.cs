using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

// สถานะพฤติกรรม AI ของมอนสเตอร์และบอส
public enum NpcState : byte
{
    Idle = 0,      // ยืนรอก่อนเดินลาดตระเวนรอบถัดไป
    Patrol = 1,    // เดินลาดตระเวนอัตโนมัติในอาณาเขตแคมป์
    Chasing = 2,   // ไล่ล่าผู้เล่นที่ตรวจพบ
    Attacking = 3, // โจมตีผู้เล่นในระยะ
    Returning = 4, // วิ่งกลับจุดเกิดเมื่อผู้เล่นออกนอกระยะหรือเข้า Safe Zone
    Dead = 5,      // ตายและรอเวลาสลายตัว
    Staggered = 6  // สถานะชะงัก/เสียหลักจากการถูกผู้เล่นปัดป้องสมบูรณ์ (Perfect Parry)
}

// รูปแบบท่าโจมตีพิเศษทั้ง 3 ท่าของบอส (Sovereign Golem)
public enum BossAttackPattern : byte
{
    None = 0,
    HeavyCleave = 1,         // ท่าที่ 1: ฟาดหนักด้านหน้า 1.5x ดาเมจ
    ShockwaveSpinAoE = 2,    // ท่าที่ 2: กระแทกคลื่นพลัง 360 องศา รอบตัว
    DarkVolleyAndCharge = 3  // ท่าที่ 3: ยิงกระสุนพลังงานมืด 3 ลูก + พุ่งชาร์จระยะไกล
}

// โครงสร้างตารางไอเทมดรอปเพิ่มเติมบน Inspector
[Serializable]
public class LootEntry
{
    public ItemData item;
    [Min(1)] public int minAmount = 1;
    [Min(1)] public int maxAmount = 1;
    [Range(0f, 1f)] public float chance = 1f;
}

/// <summary>
/// ระบบ AI ของมอนสเตอร์ประจำแคมป์และบอสใหญ่ (EnemyNPC &amp; Multi-Pattern Boss) สำหรับ Photon Fusion Shared Mode:
///   1. ระบบเดินลาดตระเวนอัตโนมัติในอาณาเขตแคมป์ (Autonomous Territory Patrol) ไม่ยืนนิ่งเป็นหุ่นปั้น
///   2. ระบบลอบสังหารจากด้านหลัง (Stealth Assassination &amp; Vision Cone 110 องศา):
///      - หากผู้เล่นย่องเข้าทางด้านหลังนอกมุมมองสายตา จะไม่ถูกตรวจพบทันทีและสามารถกดโจมตีลอบสังหารทำดาเมจ 3.0x ได้
///   3. ระบบดรอปไอเทมส่วนตัวและคำนวณโอกาสออกของตามสัดส่วนดาเมจ (Personal Instanced Loot &amp; Damage-Contribution Weighted Drops):
///      - เก็บสถิติดาเมจของผู้เล่นแต่ละคนใน Dictionary&lt;PlayerRef, float&gt;
///      - เมื่อตาย ผู้เล่นทุกคนที่ร่วมสู้จะได้ไอเทมดรอปส่วนตัวแยกกัน (คนอื่นมองไม่เห็นและแย่งเก็บไม่ได้)
///      - ผู้ทำดาเมจสูงสุด (Top DPS) ได้โบนัสโอกาสออกไอเทม Epic / Legendary สูงสุด ส่วนผู้ทำดาเมจรองลงมายังมีโอกาสได้ลุ้นจริงตามสัดส่วน
/// </summary>
[RequireComponent(typeof(Collider))]
public class EnemyNPC : NetworkBehaviour, ILockTarget, IStateAuthorityChanged
{
    // รายการมอนสเตอร์และบอสทั้งหมดที่เกิดอยู่ในฉาก
    public static readonly List<EnemyNPC> All = new List<EnemyNPC>();

    [Header("Base Combat Stats (ค่าพลังพื้นฐานที่เลเวล 1)")]
    public int MaxHealth = 120;
    public int AttackDamage = 8;
    public int Defense = 0;
    public float AttackCooldown = 2.1f;
    public float AttackRange = 2.4f;

    [Header("AI & Territory Patrol (ระบบเดินลาดตระเวนโซนส่วนตัวและมุมมองสายตา)")]
    [Min(1f)] [SerializeField] private float aggroRadius = 6.8f;
    [Min(2f)] [SerializeField] private float leashRadius = 22f;
    // รัศมีเดินลาดตระเวนประจำตัวของมอนสเตอร์แต่ละตัว (ตั้งให้เล็กกว่าครึ่งหนึ่งของระยะห่างระหว่างจุดเกิด เพื่อไม่ให้โซนทับซ้อนและเดินกระจุกรวมกัน)
    [SerializeField] private float patrolRadius = 3.4f;
    // ระยะเรียกเพื่อนร่วมแคมป์เมื่อถูกโจมตี (เรียกเฉพาะตัวที่อยู่ใกล้ในระยะ 8 เมตร ไม่ดึงทั้งแคมป์จากระยะไกล)
    [Min(2f)] [SerializeField] private float campAllyAlertRadius = 8.0f;
    // เวลารอที่จุดลาดตระเวนก่อนเดินไปยังจุดถัดไป
    [SerializeField] private Vector2 idleWaitRange = new Vector2(0.8f, 2.2f);
    [SerializeField] private float patrolSpeed = 1.85f;
    [SerializeField] private float chaseSpeed = 3.4f;
    [SerializeField] private float returnSpeed = 4.0f;
    [SerializeField] private float turnSpeed = 10f;
    [Min(0f)] [SerializeField] private float despawnDelayAfterDeath = 2.5f;

    [Header("Stealth & Vision Cone (มุมมองสายตาและการลอบสังหาร)")]
    // มุมมองสายตาด้านหน้า (องศา) หากผู้เล่นอยู่ด้านหลังนอกมุมนี้จะสามารถย่องเข้าลอบสังหารได้
    [Range(45f, 180f)] [SerializeField] private float visionConeAngle = 110f;
    // ระยะได้ยินเสียงฝีเท้าด้านหลัง (หากเข้าใกล้เกินระยะนี้แม้จะอยู่ด้านหลังก็จะรู้ตัว)
    [SerializeField] private float backHearingRadius = 1.15f;
    // ระยะที่ผู้เล่นสามารถกดโจมตีลอบสังหารจากด้านหลังได้
    [SerializeField] private float stealthAssassinateRange = 3.3f;

    [Header("Obstacle navigation (ระบบหลบหลีกสิ่งกีดขวาง)")]
    [SerializeField] private float obstacleCheckDistance = 2.4f;
    [SerializeField] private float obstacleProbeRadius = 0.55f;
    [SerializeField] private float allySeparationRadius = 1.55f;

    [Header("Damage validation (การตรวจสอบความถูกต้องของดาเมจ)")]
    [SerializeField] private int maxDamagePerHit = 800;
    [SerializeField] private float maxHitDistance = 6.5f;
    [SerializeField] private float minSecondsBetweenHitsPerPlayer = 0.14f;

    [Header("Rewards (รางวัล EXP และไอเทมดรอป)")]
    [SerializeField] private int experienceReward = 50;
    [SerializeField] private WorldItem lootPrefab;
    [SerializeField] private LootEntry[] lootTable;

    [Header("Optional feedback")]
    [SerializeField] private Animator animator;
    [SerializeField] private Renderer bodyRenderer;
    public UnityEvent onHit;
    public UnityEvent onDeath;

    [Header("Visual & Rigged Model (โมเดล 3D และแอนิเมชัน)")]
    // เปิดอัตโนมัติเมื่อ Prefab มี SkinnedMeshRenderer (เช่น Boss Golem) เพื่อไม่ให้ทับ Scale/สี หรือสร้างมงกุฎ Primitive
    [SerializeField] private bool hasRiggedModel = false;
    // ความสูงจุด Lock-on สำหรับโมเดล 3D ที่มีจุดหมุนอยู่ที่เท้า (Feet Pivot)
    [SerializeField] private float riggedLockPointHeight = 1.85f;
    // ความสูงหลอดเลือดลอยเหนือศีรษะสำหรับโมเดล 3D
    [SerializeField] private float riggedOverheadBarHeight = 3.75f;

    [Header("Hybrid Navigation (NavMesh + ระบบหลบสิ่งกีดขวางสำรอง)")]
    // เปิดใช้งาน NavMeshAgent อัตโนมัติหากฉากมีการ Bake NavMesh ไว้แล้ว (หากยังไม่ Bake จะใช้ระบบเดิมโดยไม่เกิด Warning)
    [SerializeField] private bool useNavMeshWhenAvailable = true;
    // ปรับความสูงตามพื้น/ทางลาด/สะพานอัตโนมัติในโหมด Fallback
    [SerializeField] private bool snapToGround = true;
    [SerializeField] private LayerMask groundSnapMask = ~0;

    // ---- อีเวนต์สำหรับทีม UI / Audio / VFX มาเชื่อมต่อโดยไม่ต้องแก้โค้ด AI ----
    // แจ้งเตือนทุก Peer เมื่อศัตรูได้รับดาเมจ (enemy, appliedDamage, isCrit)
    public event Action<EnemyNPC, int, bool> Damaged;
    // แจ้งเตือนทุก Peer เมื่อศัตรูถูก Perfect Parry จนติดสถานะชะงัก (enemy, durationSeconds)
    public event Action<EnemyNPC, float> Staggered;
    // แจ้งเตือนทุก Peer เมื่อศัตรูตาย
    public event Action<EnemyNPC> Died;

    // ---- ข้อมูลที่ซิงก์ผ่าน Network (Networked State) ----
    [Networked, OnChangedRender(nameof(OnHealthChanged))]
    public int CurrentHealth { get; set; } = 120;

    // ค่าพลังชีวิตสูงสุดที่ซิงก์ผ่าน Photon Fusion (จำเป็นสำหรับบอสที่มีระบบสเกลพลังชีวิตแบบ Co-op +1x ต่อผู้เล่น)
    [Networked]
    public int NetworkMaxHealth { get; set; } = 120;

    // จำนวนผู้เล่นสูงสุดที่เคยทำดาเมจใส่บอสในรอบการต่อสู้ปัจจุบัน (1 = 1x, 2 = 2x, 3 = 3x, 4 = 4x — สเกลเมื่อตีโดนเท่านั้น และห้ามลดกลางคัน)
    [Networked, OnChangedRender(nameof(OnCoopScaleChanged))]
    public int CoopPeakPlayerCount { get; set; } = 1;

    // จำนวนผู้เล่นที่ยังมีชีวิตและยืนอยู่ในลานบอส ณ ปัจจุบัน (ใช้แสดงผลเท่านั้น ไม่ใช้ทริกเกอร์สเกลพลังบอส)
    [Networked]
    public int ActivePlayersInBossZone { get; set; } = 0;

    // จำนวนผู้เล่นไม่ซ้ำหน้า (Unique Attackers) ที่ตีโดนบอสจริงในรอบการต่อสู้ปัจจุบัน
    [Networked]
    public int UniqueBossAttackerCount { get; set; } = 0;

    // เวลานับถอยหลัง 10 วินาทีหลังผู้เล่นทั้งหมดตาย/ออกจากการต่อสู้ ก่อนที่บอสจะเริ่มฟื้นฟูเลือดทีละ 25% (< 0 หมายถึงไม่ได้นับถอยหลัง)
    [Networked]
    public float BossResetCooldownRemaining { get; set; } = -1f;

    // สถานะว่าบอสกำลังค่อยๆ ฟื้นฟูเลือดทีละ 25% ของ MaxHP ทุก 3 วินาทีอยู่หรือไม่
    [Networked]
    public NetworkBool IsBossRegenerating { get; set; } = false;

    [Networked, OnChangedRender(nameof(OnStateChanged))]
    public NpcState State { get; set; }

    [Networked] public NetworkId TargetId { get; set; }
    [Networked] public Vector3 HomePosition { get; set; }
    // รัศมีโซนลาดตระเวนส่วนตัวของมอนสเตอร์ตัวนี้ (ซิงก์ผ่าน Network เพื่อให้ตรวจสอบและแสดงผลได้ตรงกันทุกเครื่อง)
    [Networked] public float PersonalPatrolRadius { get; set; } = 3.4f;
    // ลำดับโซนย่อยภายในแคมป์ (0..3) เพื่อแยกพื้นที่เดินของแต่ละตัวออกจากกันอย่างชัดเจน
    [Networked] public int PersonalZoneIndex { get; set; } = 0;
    [Networked] public int CampId { get; set; }
    [Networked, OnChangedRender(nameof(OnBossFlagChanged))]
    public NetworkBool IsBoss { get; set; }
    [Networked] public int ScaledLevel { get; set; } = 1;

    // ---- คุณสมบัติเปิดอ่านจากภายนอก (Public API) ----
    public bool IsSpawned { get; private set; }
    public bool IsAlive => IsSpawned && Object != null && Object.IsValid && State != NpcState.Dead && CurrentHealth > 0;
    // ตรวจสอบว่ามอนสเตอร์กำลังติดสถานะชะงักจาก Perfect Parry หรือไม่
    public bool IsStaggered => IsSpawned && State == NpcState.Staggered;
    // ตรวจสอบว่า Prefab นี้ใช้โมเดล 3D จริงหรือไม่
    public bool HasRiggedModel => hasRiggedModel;
    // ตรวจสอบว่ากำลังใช้ NavMeshAgent นำทางอยู่ในฉากนี้หรือไม่
    public bool IsUsingNavMesh => _navAgent != null && _navAgent.enabled && _navAgent.isOnNavMesh;
    // ตรวจสอบว่ามอนสเตอร์รู้ตัวและกำลังต่อสู้อยู่หรือไม่ (ใช้เช็คเงื่อนไขลอบสังหาร)
    public bool IsAlerted => State == NpcState.Chasing || State == NpcState.Attacking || State == NpcState.Staggered;
    public Collider BodyCollider { get; private set; }
    public float AggroRadius => aggroRadius;
    public float ChaseSpeed => chaseSpeed;
    public float PatrolSpeed => patrolSpeed;
    // รัศมีลาดตระเวนจริงที่ใช้ในปัจจุบัน (อ้างอิงจากโซนส่วนตัวของมอนสเตอร์ตัวนี้)
    public float PatrolRadius => (IsSpawned && PersonalPatrolRadius > 0.25f) ? PersonalPatrolRadius : patrolRadius;
    // จุดศูนย์กลางโซนลาดตระเวนส่วนตัว (ใช้ทั้งเป็นจุดเดินลาดตระเวนและจุด Leash กลับบ้านของมอนสเตอร์ตัวนี้)
    public Vector3 PersonalZoneAnchor => IsSpawned ? HomePosition : transform.position;
    public Vector3 CurrentPatrolWaypoint => _patrolPoint;
    public int ScaledMaxHealth => (IsSpawned && NetworkMaxHealth > 0) ? NetworkMaxHealth : MaxHealth;
    public int ScaledAttackDamage => AttackDamage;
    public int ScaledDefense => Defense;
    // ค่าตัวคูณ Co-op ปัจจุบันของบอส (1.0x สำหรับ 1 คนที่ตีโดน, +1.0x ต่อผู้เล่นคนใหม่ที่ตีโดนเพิ่ม)
    public float CoopStatMultiplier => IsBoss ? Mathf.Max(1f, CoopPeakPlayerCount) : 1f;
    // จำนวนผู้เล่นไม่ซ้ำหน้าที่ตีโดนบอสจริงในรอบการต่อสู้ปัจจุบัน
    public int UniqueAttackerCount => IsSpawned ? Mathf.Max(_uniqueDamageDealers.Count, UniqueBossAttackerCount) : _uniqueDamageDealers.Count;
    // จำนวนผู้เล่นที่ร่วมตีบอสและยังมีชีวิตสู้ต่ออยู่ในลานบอส ณ ปัจจุบัน
    public int ActiveBossParticipantCount => IsBoss ? CountActiveBossParticipants() : 0;
    // ค่าคงที่เวลาคูลดาวน์และอัตราฟื้นฟูเลือดบอสหลังผู้เล่นทั้งหมดตายหรือออกจากการต่อสู้
    public const float BossResetCooldownDuration = 10.0f;
    public const float BossRegenIntervalSeconds = 3.0f;
    public const float BossRegenFractionPerTick = 0.25f;
    // ค่าพลังพื้นฐานที่ 1.0x ของบอสก่อนคูณ Co-op (สำหรับตรวจสอบและรีเซ็ตหลังจบการต่อสู้)
    public int BaseMaxHealth => _baseMaxHealth > 0 ? _baseMaxHealth : MaxHealth;
    public int BaseAttackDamage => _baseAttackDamage > 0 ? _baseAttackDamage : AttackDamage;
    public int BaseDefense => _baseDefense;
    // ข้อมูลท่าโจมตีที่บอสกำลังร่ายอยู่ (ใช้แสดง Cast Bar บน Top-Center Boss HUD)
    public string ActiveTelegraphName { get; private set; } = string.Empty;
    public float ActiveTelegraphEndTime { get; private set; } = -1f;
    public float ActiveTelegraphDuration { get; private set; } = 0f;
    public int ExperienceReward => experienceReward;
    public int CurrentAttackDamage => AttackDamage;

    public BossAttackPattern LastBossPattern { get; private set; }
    public int BossAttackExecutions { get; private set; }
    public int DistinctBossPatternsUsed => _usedPatterns.Count;
    public bool IsTelegraphing => _isTelegraphing;

    // โครงสร้างข้อมูลอันดับดาเมจผู้เล่นที่ซิงก์ข้ามเครื่องสำหรับแสดงผลบน Top-Center Boss HUD (Damage Meter)
    public struct BossDamageEntry
    {
        public int playerRawEncoded;
        public string playerName;
        public int damageDealt;
        public float damageShare;
        public bool isLocalPlayer;
    }

    // ตารางเก็บสถิติดาเมจสะสมของผู้เล่นแต่ละคน (ใช้คำนวณ Personal Loot & Damage-Weighted Drop Rate)
    public IReadOnlyDictionary<PlayerRef, float> DamageDealtMap => _damageDealtMap;
    // รายชื่ออันดับดาเมจของบอสที่ซิงก์ไปยังทุกเครื่องแล้ว (เรียงจากดาเมจมากไปน้อย)
    public IReadOnlyList<BossDamageEntry> SyncedBossDamageBoard => _syncedBossDamageBoard;

    public Transform LockPoint => _lockPoint != null ? _lockPoint : transform;
    public bool IsLockable => IsAlive;
    public string LockName => IsBoss ? $"[Lv.{ScaledLevel} BOSS] Sovereign Golem" : $"[Lv.{ScaledLevel}] {name}";

    // ---- ตัวแปรภายใน (Private State) ----
    private Transform _lockPoint;
    private GameObject _bossCrownRoot;
    private NavMeshAgent _navAgent;
    private MaterialPropertyBlock _mpb;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    // แคช Hash ของพารามิเตอร์ Animator สำหรับมอนสเตอร์และบอส Golem
    private static readonly int SpeedParam = Animator.StringToHash("Speed");
    private static readonly int IsMovingParam = Animator.StringToHash("IsMoving");
    private static readonly int IsDeadParam = Animator.StringToHash("IsDead");
    private static readonly int IsStaggeredParam = Animator.StringToHash("IsStaggered");
    private static readonly int HitTrigger = Animator.StringToHash("Hit");
    private static readonly int AttackTrigger = Animator.StringToHash("Attack");
    private static readonly int HeavyCleaveTrigger = Animator.StringToHash("HeavyCleave");
    private static readonly int ShockwaveSpinTrigger = Animator.StringToHash("ShockwaveSpin");
    private static readonly int DarkVolleyTrigger = Animator.StringToHash("DarkVolley");
    private static readonly int TelegraphTrigger = Animator.StringToHash("Telegraph");
    private static readonly int StaggerTrigger = Animator.StringToHash("Stagger");
    private static readonly int DieTrigger = Animator.StringToHash("Die");

    private bool _hasSpeedParam;
    private bool _hasIsMovingParam;
    private bool _hasIsDeadParam;
    private bool _hasIsStaggeredParam;
    private bool _hasHitParam;
    private bool _hasAttackParam;
    private bool _hasHeavyCleaveParam;
    private bool _hasShockwaveSpinParam;
    private bool _hasDarkVolleyParam;
    private bool _hasTelegraphParam;
    private bool _hasStaggerParam;
    private bool _hasDieParam;
    private bool _deathAnimPlayed;

    private float _nextAttackTime;
    private float _stateTimer;
    private float _staggerEndTime;
    private float _patrolMoveElapsed;
    private float _deathTime;
    private bool _hasPatrolPoint;
    private Vector3 _patrolPoint;
    private Vector3 _lastWorldPos;
    private float _smoothedAnimSpeed;
    private bool _flashing;
    private bool _bossConfigured;
    // เก็บค่าโซนลาดตระเวนส่วนตัวที่ SandboxWorldSpawner กำหนดมาก่อน Spawned()
    private bool _hasConfiguredPersonalZone;
    private Vector3 _configuredZoneAnchor;
    private float _configuredPatrolRadius;
    private int _configuredZoneIndex;
    private bool _scalingApplied;
    private bool _isTelegraphing;
    // ค่าพลังพื้นฐานระดับ 1.0x (1 ผู้เล่น) ก่อนคูณตัวคูณ Co-op เพื่อป้องกันการคูณซ้ำซ้อน
    private int _baseMaxHealth;
    private int _baseAttackDamage;
    private int _baseDefense;
    // ชุดเก็บรายชื่อผู้เล่นไม่ซ้ำหน้าที่ตีโดนบอสจริงในรอบนี้ (ใช้สเกลพลังบอสแบบ On-Hit และตรวจว่ายังมีผู้ร่วมสู้รอดชีวิตอยู่หรือไม่)
    private readonly HashSet<PlayerRef> _uniqueDamageDealers = new HashSet<PlayerRef>();
    // ชุดผู้เล่นจำลองที่ยังมีชีวิตอยู่ในลานบอสสำหรับใช้ทดสอบ Co-op ใน Play Mode
    private readonly HashSet<PlayerRef> _simulatedAliveParticipants = new HashSet<PlayerRef>();
    // สถานะนับถอยหลัง 10 วินาที และตัวจับเวลาฟื้นเลือดทีละ 25% ทุก 3 วินาที
    private bool _bossResetPending;
    private float _bossRegenTickTimer;
    // ตัวแปรเสริมสำหรับจำลองจำนวนผู้เล่นเพิ่มเติมในลานบอสระหว่างการทดสอบอัตโนมัติ (ค่าปกติ = 0)
    private int _simulatedExtraBossPlayers;
    // ตัวหน่วงเวลาป้องกันการส่ง RequestStateAuthority ซ้ำถี่เกินไปเมื่อ Host เดิมหลุดออกจากห้อง
    private float _nextAuthorityRecoveryAt;
    // จำนวนครั้งที่มอนสเตอร์ตัวนี้โอนย้าย StateAuthority สำเร็จ (ใช้ตรวจสอบในการทดสอบ Host-Leave)
    public int AuthorityTakeoverCount { get; private set; }
    // เส้นขอบเขตวงกลมบนพื้นแสดงอาณาเขตโซนลาดตระเวนส่วนตัวของมอนสเตอร์ตัวนี้ (ใช้ URP Unlit LineRenderer)
    private LineRenderer _personalZoneRing;
    private Vector3 _lastRingAnchor;
    private float _lastRingRadius = -1f;
    // หลอดเลือดแบบ World-Space UI เหนือศีรษะสำหรับมอนสเตอร์ทั่วไป
    private EnemyWorldHealthBar _worldHealthBar;
    // คุณสมบัติตรวจสอบจากภายนอกว่าเส้นวงกลมโซนส่วนตัวและ World-Space HP Bar ถูกสร้างแล้ว
    public LineRenderer PersonalZoneRing => _personalZoneRing;
    public EnemyWorldHealthBar WorldHealthBar => _worldHealthBar;
    private int _bossPatternCycle;
    private int _avoidSign;
    private float _avoidSignUntil;
    private Coroutine _bossAttackRoutine;
    private readonly HashSet<BossAttackPattern> _usedPatterns = new HashSet<BossAttackPattern>();
    private static readonly Collider[] s_overlapBuffer = new Collider[24];
    private static readonly RaycastHit[] s_castBuffer = new RaycastHit[16];
    private static readonly RaycastHit[] s_groundRayBuffer = new RaycastHit[8];
    private readonly Dictionary<PlayerRef, float> _lastDamageTime = new Dictionary<PlayerRef, float>();

    // ตารางบันทึกดาเมจที่ผู้เล่นแต่ละคนทำได้ในการต่อสู้กับมอนสเตอร์/บอสตัวนี้
    private readonly Dictionary<PlayerRef, float> _damageDealtMap = new Dictionary<PlayerRef, float>();
    // รายการอันดับดาเมจที่ซิงก์ไปยังทุกเครื่องแล้วสำหรับ BossHudUI
    private readonly List<BossDamageEntry> _syncedBossDamageBoard = new List<BossDamageEntry>(6);
    // ชื่อจำลองเสริมเฉพาะสำหรับทดสอบ Multi-player Damage Meter ใน Play Mode
    private readonly Dictionary<int, string> _simulatedNameOverrides = new Dictionary<int, string>();

    // ------------------------------------------------------------------ วงจรการทำงาน (Lifecycle)

    private void Awake()
    {
        BodyCollider = GetComponent<Collider>();
        if (bodyRenderer == null) bodyRenderer = GetComponentInChildren<Renderer>();
        if (animator == null) animator = GetComponentInChildren<Animator>();

        // ตรวจจับอัตโนมัติว่าใช้โมเดล 3D ที่มีโครงกระดูกจริงหรือไม่ (เช่น Boss Golem)
        if (!hasRiggedModel && GetComponentInChildren<SkinnedMeshRenderer>() != null)
            hasRiggedModel = true;

        if (animator != null)
        {
            animator.applyRootMotion = false;
            CacheAnimatorParameters();
        }

        _navAgent = GetComponent<NavMeshAgent>();
        if (_navAgent != null) _navAgent.enabled = false;

        _mpb = new MaterialPropertyBlock();

        var lp = new GameObject("LockPoint").transform;
        lp.SetParent(transform, false);
        lp.localPosition = new Vector3(0f, hasRiggedModel ? riggedLockPointHeight : 0.6f, 0f);
        _lockPoint = lp;
    }

    /// <summary>
    /// ตรวจสอบและแคชพารามิเตอร์ที่มีอยู่จริงใน Animator Controller เพื่อป้องกัน Warning ตอนรัน
    /// </summary>
    private void CacheAnimatorParameters()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        foreach (var p in animator.parameters)
        {
            int h = p.nameHash;
            if (h == SpeedParam) _hasSpeedParam = true;
            else if (h == IsMovingParam) _hasIsMovingParam = true;
            else if (h == IsDeadParam) _hasIsDeadParam = true;
            else if (h == IsStaggeredParam) _hasIsStaggeredParam = true;
            else if (h == HitTrigger) _hasHitParam = true;
            else if (h == AttackTrigger) _hasAttackParam = true;
            else if (h == HeavyCleaveTrigger) _hasHeavyCleaveParam = true;
            else if (h == ShockwaveSpinTrigger) _hasShockwaveSpinParam = true;
            else if (h == DarkVolleyTrigger) _hasDarkVolleyParam = true;
            else if (h == TelegraphTrigger) _hasTelegraphParam = true;
            else if (h == StaggerTrigger) _hasStaggerParam = true;
            else if (h == DieTrigger) _hasDieParam = true;
        }
    }

    public override void Spawned()
    {
        IsSpawned = true;
        if (!All.Contains(this)) All.Add(this);
        _damageDealtMap.Clear();
        _deathAnimPlayed = false;
        _lastWorldPos = transform.position;
        _smoothedAnimSpeed = 0f;

        if (Object.HasStateAuthority)
        {
            // ใช้เลเวลคงที่ของโซนที่ SandboxWorldSpawner กำหนดมา (ไม่สเกลตามผู้เล่นเลเวลสูงสุดในห้อง)
            if (ScaledLevel < 1) ScaledLevel = ResolveFallbackZoneLevel();
            CoopPeakPlayerCount = 1;
            ActivePlayersInBossZone = 0;
            UniqueBossAttackerCount = 0;
            BossResetCooldownRemaining = -1f;
            IsBossRegenerating = false;
            _bossResetPending = false;
            _bossRegenTickTimer = 0f;
            _uniqueDamageDealers.Clear();
            _simulatedAliveParticipants.Clear();
            _simulatedExtraBossPlayers = 0;
            ApplyZoneLevel(ScaledLevel);
            NetworkMaxHealth = MaxHealth;
            CurrentHealth = MaxHealth;

            // กำหนดจุดศูนย์กลางและรัศมีโซนลาดตระเวนส่วนตัวของมอนสเตอร์ตัวนี้ (ไม่ใช้จุดกลางแคมป์ร่วมกับตัวอื่น)
            HomePosition = _hasConfiguredPersonalZone ? _configuredZoneAnchor : transform.position;
            PersonalPatrolRadius = _hasConfiguredPersonalZone && _configuredPatrolRadius > 0.25f
                ? _configuredPatrolRadius
                : patrolRadius;
            PersonalZoneIndex = _hasConfiguredPersonalZone ? _configuredZoneIndex : 0;
            TargetId = default;

            // เชื่อมต่อ NavMeshAgent อัตโนมัติเฉพาะเมื่อฉากมีการ Bake NavMesh แล้วเท่านั้น
            TryInitNavMeshAgent();

            // ให้มอนสเตอร์แต่ละตัวเริ่มเดินลาดตระเวนทันทีแบบเหลื่อมเวลากันเล็กน้อย เพื่อไม่ให้ยืนนิ่งเป็นหุ่นปั้น
            _stateTimer = UnityEngine.Random.Range(0.15f, 0.85f);
            State = NpcState.Idle;
            Debug.Log($"[EnemyNPC] {name} Lv.{ScaledLevel} (Boss={IsBoss}, Rigged={hasRiggedModel}, NavMesh={IsUsingNavMesh}, Camp={CampId}, ZoneIdx={PersonalZoneIndex}, ZoneR={PersonalPatrolRadius:0.0}m) spawned at {HomePosition}. HP={CurrentHealth}/{MaxHealth}, ATK={AttackDamage}, DEF={Defense}.");
        }
        else
        {
            // เครื่อง Remote Proxy ต้องปิด NavMeshAgent เพื่อไม่ให้ชนกับการซิงก์ตำแหน่งของ NetworkTransform
            if (_navAgent != null) _navAgent.enabled = false;
            ApplyZoneLevel(Mathf.Max(1, ScaledLevel));
            ApplyCoopStatsFromPeak();
        }

        RefreshVisuals();

        // สร้างหลอดเลือด World-Space UI และเส้นขอบเขตวงกลมบนพื้นสำหรับมอนสเตอร์ทั่วไป (บอสใช้ BossHudUI + BossArenaBoundaryRing)
        if (!IsBoss)
        {
            _worldHealthBar = EnemyWorldHealthBar.AttachTo(this);
            EnsurePersonalZoneGroundRing();
        }
    }

    /// <summary>
    /// สร้างหรืออัปเดตเส้นวงกลมขอบเขตบนพื้น (LineRenderer + URP Unlit Material) รอบจุดศูนย์กลางโซนลาดตระเวนส่วนตัวของมอนสเตอร์
    /// </summary>
    private void EnsurePersonalZoneGroundRing()
    {
        if (IsBoss || !IsSpawned) return;
        Vector3 anchor = PersonalZoneAnchor;
        float radius = Mathf.Max(1.0f, PatrolRadius);

        if (_personalZoneRing == null)
        {
            Color ringColor = ScaledLevel <= 10
                ? new Color(0.98f, 0.56f, 0.18f, 0.58f)   // สีส้มอำพันสำหรับโซน Starter (Tier 1: Lv.1–10)
                : new Color(0.95f, 0.24f, 0.22f, 0.65f);  // สีแดงส้มสำหรับโซน Mid-Field Frontier (Tier 2: Lv.11–22)

            Transform ringParent = SandboxWorldSpawner.Instance != null ? SandboxWorldSpawner.Instance.transform : null;
            _personalZoneRing = UrpMaterialHelper.CreateGroundCircleRing(
                ringParent,
                $"EnemyZoneRing_C{CampId}_Z{PersonalZoneIndex}",
                anchor,
                radius,
                lineWidth: 0.13f,
                color: ringColor,
                segments: 56,
                yHeight: 0.06f);
            _lastRingAnchor = anchor;
            _lastRingRadius = radius;
        }
        else if ((anchor - _lastRingAnchor).sqrMagnitude > 0.04f || Mathf.Abs(radius - _lastRingRadius) > 0.05f)
        {
            UrpMaterialHelper.UpdateGroundCircleRing(_personalZoneRing, anchor, radius, segments: 56, yHeight: 0.06f);
            _lastRingAnchor = anchor;
            _lastRingRadius = radius;
        }

        if (_personalZoneRing != null)
            _personalZoneRing.gameObject.SetActive(IsAlive);
    }

    /// <summary>
    /// ลบเส้นขอบเขตวงกลมบนพื้นเมื่อมอนสเตอร์ตายหรือถูก Despawn
    /// </summary>
    private void DestroyPersonalZoneGroundRing()
    {
        if (_personalZoneRing != null)
        {
            Destroy(_personalZoneRing.gameObject);
            _personalZoneRing = null;
        }
    }

    /// <summary>
    /// ตรวจสอบว่ามีพื้นที่ NavMesh ใต้ตัวมอนสเตอร์หรือไม่ หากมีจะเปิดใช้งาน NavMeshAgent แบบ Hybrid
    /// </summary>
    private void TryInitNavMeshAgent()
    {
        if (!useNavMeshWhenAvailable)
        {
            if (_navAgent != null) _navAgent.enabled = false;
            return;
        }

        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 3.0f, NavMesh.AllAreas))
        {
            if (_navAgent == null) _navAgent = gameObject.AddComponent<NavMeshAgent>();
            _navAgent.updateRotation = false;
            _navAgent.stoppingDistance = 0.25f;
            _navAgent.acceleration = 24f;
            _navAgent.angularSpeed = 360f;
            _navAgent.radius = IsBoss ? 1.0f : 0.45f;
            _navAgent.height = IsBoss ? 3.2f : 1.9f;
            _navAgent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            _navAgent.enabled = true;
            _navAgent.Warp(hit.position);
        }
        else if (_navAgent != null)
        {
            _navAgent.enabled = false;
        }
    }

    private void StopNavAgent()
    {
        if (IsUsingNavMesh)
        {
            _navAgent.isStopped = true;
            if (_navAgent.hasPath) _navAgent.ResetPath();
        }
    }

    /// <summary>
    /// คืนค่าเลเวลพื้นฐานประจำโซนกรณีไม่ได้เกิดผ่าน SandboxWorldSpawner (มอนทั่วไป Lv.2, บอส Endgame Lv.25)
    /// ยกเลิกระบบสเกลตามผู้เล่นเลเวลสูงสุดในห้องเพื่อรองรับโครงสร้าง Central Server MMORPG
    /// </summary>
    private int ResolveFallbackZoneLevel()
    {
        return IsBoss ? 25 : 2;
    }

    private void OnBossFlagChanged()
    {
        if (IsSpawned && IsBoss)
        {
            ApplyBossConfiguration();
            RefreshVisuals();
        }
    }

    /// <summary>
    /// คำนวณค่าพลังฐาน (Base Stats 1.0x) ของบอสตามเลเวลโซน Endgame (Lv.20–30)
    /// สูตรที่ Lv.25: MaxHP = 2,200, ATK = 50, DEF = 15, EXP = 1,200 (และสเกล +1.0x ต่อผู้โจมตีที่ตีโดนจริงเพิ่มแต่ละคน)
    /// </summary>
    public static void ComputeBossBaseStats(int zoneLevel, out int baseHp, out int baseAtk, out int baseDef, out int expReward)
    {
        int lvl = Mathf.Clamp(zoneLevel, 20, PlayerCombat.MaxLevel);
        int over15 = Mathf.Max(0, lvl - 15);
        baseHp = 1400 + over15 * 80;
        baseAtk = 40 + over15 * 1;
        baseDef = 10 + (over15 / 2);
        expReward = 700 + over15 * 50;
    }

    /// <summary>
    /// คำนวณค่าพลังของมอนสเตอร์ทั่วไปแบบ 3-Tier Progression (Lv.1–30):
    ///   - Tier 1 Starter Zone (Lv.1–10, Camps 1–2): HP 80..170, ATK 10..19, DEF 0..3 -> ผู้เล่นใหม่ Lv.1 ฟันตายใน ~4–5 ฮิต
    ///   - Tier 2 Mid-Field Frontier (Lv.11–22, Camps 3–4): HP 320..540 (~2.5x–3.0x ของ Starter), ATK 24..46, DEF 6..11 -> ป้องกัน One-Shot และบังคับใช้ Combo Finisher / Parry
    /// </summary>
    public static void ComputeNormalEnemyBaseStats(int zoneLevel, out int baseHp, out int baseAtk, out int baseDef, out int expReward)
    {
        int lvl = Mathf.Clamp(zoneLevel, 1, PlayerCombat.MaxLevel);
        if (lvl <= 10)
        {
            // Tier 1: Starter Zone (Camps 1 & 2 สำหรับ Lv.1–10)
            int step = lvl - 1;
            baseHp = 80 + step * 10;
            baseAtk = 10 + step * 1;
            baseDef = step / 3;
            expReward = 55 + step * 14;
        }
        else
        {
            // Tier 2: Mid-Field Frontier (Camps 3 & 4 สำหรับ Lv.11–22 — HP สูงกว่าโซนเริ่มต้น ~2.5x–3.0x)
            int step = lvl - 11;
            baseHp = 320 + step * 20;
            baseAtk = 24 + step * 2;
            baseDef = 6 + (step / 2);
            expReward = 175 + step * 22;
        }
    }

    /// <summary>
    /// กำหนดค่าพลังของมอนสเตอร์และบอสตามเลเวลคงที่ประจำพื้นที่ (3-Tier Zone-Based Level สำหรับ Lv.1–30)
    /// พารามิเตอร์ preserveCurrentHealth ใช้เมื่อโอนย้าย StateAuthority เพื่อไม่ให้เลือดเดิมถูกรีเซ็ตกลับเต็ม 100%
    /// </summary>
    public void ApplyZoneLevel(int zoneLevel, bool preserveCurrentHealth = false)
    {
        zoneLevel = Mathf.Clamp(zoneLevel, 1, PlayerCombat.MaxLevel);
        if (_scalingApplied && ScaledLevel == zoneLevel && !preserveCurrentHealth) return;
        _scalingApplied = true;
        if (Object != null && Object.IsValid && Object.HasStateAuthority)
            ScaledLevel = zoneLevel;

        if (IsBoss)
        {
            ApplyBossVisualAndCore();
            ComputeBossBaseStats(zoneLevel, out _baseMaxHealth, out _baseAttackDamage, out _baseDefense, out experienceReward);
            maxDamagePerHit = 1500;
        }
        else
        {
            ComputeNormalEnemyBaseStats(zoneLevel, out _baseMaxHealth, out _baseAttackDamage, out _baseDefense, out experienceReward);
            maxDamagePerHit = 900;
        }

        int mult = (IsBoss && IsSpawned) ? Mathf.Max(1, CoopPeakPlayerCount) : 1;
        MaxHealth = _baseMaxHealth * mult;
        AttackDamage = _baseAttackDamage * mult;
        Defense = _baseDefense * mult;

        if (Object != null && Object.IsValid && Object.HasStateAuthority)
            NetworkMaxHealth = MaxHealth;

        if (!preserveCurrentHealth && IsSpawned && State != NpcState.Dead && CurrentHealth > 0)
            CurrentHealth = MaxHealth;
    }

    /// <summary>
    /// อัปเดตค่าพลังบอสตามตัวคูณ CoopPeakPlayerCount บนทุกเครื่อง (ซิงก์ทั้ง Host และ Remote Clients)
    /// </summary>
    private void OnCoopScaleChanged()
    {
        if (!IsSpawned || !IsBoss) return;
        ApplyCoopStatsFromPeak();
    }

    private void ApplyCoopStatsFromPeak()
    {
        if (!IsBoss) return;
        if (_baseMaxHealth <= 0)
        {
            int lvl = Mathf.Clamp(ScaledLevel > 0 ? ScaledLevel : 25, 20, PlayerCombat.MaxLevel);
            ComputeBossBaseStats(lvl, out _baseMaxHealth, out _baseAttackDamage, out _baseDefense, out experienceReward);
        }

        int mult = Mathf.Max(1, CoopPeakPlayerCount);
        MaxHealth = _baseMaxHealth * mult;
        AttackDamage = _baseAttackDamage * mult;
        Defense = _baseDefense * mult;
    }

    /// <summary>
    /// ลงทะเบียนผู้เล่นที่โจมตีโดนบอสจริง (On-Hit Scaling):
    ///   - สเกลพลังบอสเฉพาะเมื่อมีผู้เล่นคนใหม่ตีโดนบอสจริง (1 คน = 1x, 2 คน = 2x, 3 คน = 3x, 4 คน = 4x)
    ///     ไม่สเกลจากการแค่เดินเข้ามาในลานบอส
    ///   - ยกเลิกการนับถอยหลัง 10 วินาทีและการฟื้นฟูเลือดบอสทันทีเมื่อถูกตีซ้ำระหว่างพักฟื้น
    /// </summary>
    private void RegisterBossAttackerOnHit(PlayerRef attackerRef)
    {
        if (!IsBoss || !Object.HasStateAuthority || attackerRef == PlayerRef.None) return;

        // หากบอสกำลังนับถอยหลัง 10 วินาที หรือกำลังค่อยๆ ฟื้นเลือดทีละ 25% ให้หยุดทันทีและคงเลือดปัจจุบันไว้
        if (_bossResetPending || BossResetCooldownRemaining >= 0f || IsBossRegenerating)
        {
            CancelBossResetAndRegen($"interrupted by hit from {attackerRef}");
        }

        bool isNewAttacker = _uniqueDamageDealers.Add(attackerRef);
        UniqueBossAttackerCount = _uniqueDamageDealers.Count;

        if (isNewAttacker)
        {
            int desiredMult = Mathf.Max(1, _uniqueDamageDealers.Count);
            if (desiredMult > CoopPeakPlayerCount)
            {
                ScaleBossUpToMultiplier(desiredMult, _uniqueDamageDealers.Count);
            }
        }
    }

    /// <summary>
    /// ยกเลิกสถานะคูลดาวน์ 10 วินาทีและการฟื้นฟูเลือดแบบค่อยเป็นค่อยไปของบอสทันทีโดยไม่รีเซ็ตเลือดหรือตัวคูณ Co-op
    /// </summary>
    private void CancelBossResetAndRegen(string reason)
    {
        bool wasActive = _bossResetPending || BossResetCooldownRemaining >= 0f || IsBossRegenerating;
        _bossResetPending = false;
        _bossRegenTickTimer = 0f;
        if (Object != null && Object.IsValid && Object.HasStateAuthority)
        {
            BossResetCooldownRemaining = -1f;
            IsBossRegenerating = false;
        }
        if (wasActive)
        {
            Debug.Log($"[EnemyNPC] BOSS RESET/REGEN CANCELLED ({reason}) -> Combat resumed at HP={CurrentHealth}/{MaxHealth} ({CoopPeakPlayerCount}x).");
        }
    }

    /// <summary>
    /// ตรวจสอบว่าผู้เล่นที่ระบุยังมีชีวิตและอยู่ในระยะการต่อสู้ของลานบอสหรือไม่
    /// </summary>
    private bool IsPlayerActiveBossCombatant(PlayerRef pRef, out PlayerCombat resolvedPlayer)
    {
        resolvedPlayer = null;
        if (pRef == PlayerRef.None) return false;

        // รองรับผู้เล่นจำลองที่ยังมีชีวิตสำหรับการทดสอบ Co-op ใน Play Mode
        if (_simulatedAliveParticipants.Contains(pRef))
            return true;

        PlayerCombat p = FindPlayerBySource(pRef);
        if (p == null || !p.IsSpawned || p.IsDead) return false;
        if (SafeZone.IsPlayerInAnySafeZone(p)) return false;

        Vector3 arenaCenter = SandboxWorldSpawner.Instance != null
            ? SandboxWorldSpawner.Instance.BossArenaCenter
            : HomePosition;
        float arenaRadius = SandboxWorldSpawner.Instance != null
            ? SandboxWorldSpawner.Instance.BossArenaProximityRadius
            : 18f;
        float combatLimit = Mathf.Max(leashRadius, arenaRadius + 4.0f);

        if (PlanarDistance(p.transform.position, arenaCenter) > combatLimit)
            return false;

        resolvedPlayer = p;
        return true;
    }

    /// <summary>
    /// นับจำนวนผู้เล่นที่เคยทำดาเมจใส่บอสและยังคงมีชีวิตสู้ต่ออยู่ในลานบอส
    /// </summary>
    public int CountActiveBossParticipants()
    {
        if (!IsBoss) return 0;
        int count = 0;
        foreach (PlayerRef pRef in _uniqueDamageDealers)
        {
            if (IsPlayerActiveBossCombatant(pRef, out _))
                count++;
        }
        return count;
    }

    /// <summary>
    /// สลับเป้าหมายบอสไปยังผู้เล่นร่วมสู้คนถัดไปที่ยังมีชีวิตอยู่ในลานบอส (แก้บัค Player 1 ตายแล้วบอสรีเซ็ตเลือดทั้งที่ Player 2 ยังสู้ต่ออยู่)
    /// คืนค่า true หากยังมีผู้ร่วมสู้เหลืออยู่ (ไม่ให้บอสหลุดจากโหมดต่อสู้หรือรีเซ็ตเลือด)
    /// </summary>
    private bool TrySwitchBossToNextLivingParticipant(out PlayerCombat nextTarget)
    {
        nextTarget = null;
        if (!IsBoss) return false;

        float bestScore = float.NegativeInfinity;
        bool anySimulatedAlive = false;

        foreach (PlayerRef pRef in _uniqueDamageDealers)
        {
            if (!IsPlayerActiveBossCombatant(pRef, out PlayerCombat candidate)) continue;
            if (candidate == null)
            {
                anySimulatedAlive = true;
                continue;
            }

            // ให้ความสำคัญกับผู้ร่วมสู้ที่ทำดาเมจสูงและอยู่ใกล้ตัวบอส
            float dealt = _damageDealtMap.TryGetValue(pRef, out float d) ? d : 0f;
            float dist = PlanarDistance(transform.position, candidate.transform.position);
            float score = dealt - dist * 8f;
            if (score > bestScore)
            {
                bestScore = score;
                nextTarget = candidate;
            }
        }

        if (nextTarget != null)
        {
            SetTarget(nextTarget);
            return true;
        }

        if (anySimulatedAlive)
        {
            // ยังมีผู้เล่นจำลองในชุดผู้ร่วมสู้ที่ยังมีชีวิตอยู่ -> คงสถานะต่อสู้ไว้ ห้ามรีเซ็ตเลือดบอส
            TargetId = default;
            return true;
        }

        // กรณียังไม่มีผู้ทำดาเมจเลย (บอสไล่ตามจากการมองเห็นก่อนโดนตี) ให้ตรวจผู้เล่นที่ยังมีชีวิตในลานบอส
        if (_uniqueDamageDealers.Count == 0)
        {
            Vector3 arenaCenter = SandboxWorldSpawner.Instance != null
                ? SandboxWorldSpawner.Instance.BossArenaCenter
                : HomePosition;
            float arenaRadius = SandboxWorldSpawner.Instance != null
                ? SandboxWorldSpawner.Instance.BossArenaProximityRadius
                : 18f;
            float nearestDist = float.MaxValue;
            for (int i = 0; i < PlayerCombat.All.Count; i++)
            {
                var p = PlayerCombat.All[i];
                if (p == null || !p.IsSpawned || p.IsDead || SafeZone.IsPlayerInAnySafeZone(p)) continue;
                if (PlanarDistance(p.transform.position, arenaCenter) > arenaRadius) continue;
                float d = PlanarDistance(transform.position, p.transform.position);
                if (d < nearestDist)
                {
                    nearestDist = d;
                    nextTarget = p;
                }
            }
            if (nextTarget != null)
            {
                SetTarget(nextTarget);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// ประมวลผลระบบ Co-op และการฟื้นฟูเลือดแบบหน่วงเวลาของบอสฝั่ง State Authority ทุก Tick:
    ///   - ไม่สเกลพลังบอสจากการแค่เดินเข้าโซน (สเกลเฉพาะตอนโดนตีผ่าน RegisterBossAttackerOnHit เท่านั้น)
    ///   - หากยังมีผู้ร่วมตีบอสรอดชีวิตอย่างน้อย 1 คน จะไม่รีเซ็ตและไม่ฟื้นฟูเลือดเด็ดขาด
    ///   - หากผู้ร่วมสู้ทุกคนตายหรือออกจากลานครบ (0 คน):
    ///       1. รอคูลดาวน์ 10.0 วินาที (คงเลือดปัจจุบันไว้ ไม่เด้งเต็มทันที)
    ///       2. หลังครบ 10 วินาที ค่อยๆ ฟื้นฟูเลือดทีละ 1/4 (25% ของ MaxHP) ทุก 3.0 วินาที
    ///       3. หากถูกตีระหว่างรอ 10 วิหรือระหว่างฟื้นเลือด จะยกเลิกการฟื้นทันทีและสู้ต่อจากเลือดปัจจุบัน
    ///       4. เมื่อฟื้นจนครบ 100% โดยไม่ถูกรบกวน จึงรีเซ็ตสเกลกลับ 1.0x และล้างรายชื่อผู้โจมตี
    /// </summary>
    private void TickBossCombatAndRegen(float dt)
    {
        if (!IsBoss || !IsAlive || !Object.HasStateAuthority) return;

        // อัปเดตจำนวนผู้เล่นในลานเพื่อแสดงผล (ไม่ใช้ทริกเกอร์สเกลพลังบอสแล้ว)
        ActivePlayersInBossZone = CountLivingPlayersInBossZone();

        int activeParticipants = CountActiveBossParticipants();
        if (activeParticipants > 0)
        {
            if (_bossResetPending || BossResetCooldownRemaining >= 0f || IsBossRegenerating)
            {
                CancelBossResetAndRegen("active combat participant still alive");
            }
            return;
        }

        // ไม่มีผู้ร่วมตีบอสที่ยังมีชีวิตเหลืออยู่ (0 active participants)
        bool needsRecovery = CurrentHealth < MaxHealth || CoopPeakPlayerCount > 1 || _uniqueDamageDealers.Count > 0 || _damageDealtMap.Count > 0;
        if (!needsRecovery)
        {
            _bossResetPending = false;
            _bossRegenTickTimer = 0f;
            BossResetCooldownRemaining = -1f;
            IsBossRegenerating = false;
            return;
        }

        // หากบอสยังค้างอยู่ในสถานะไล่ล่าหรือโจมตีแต่ไม่มีผู้ร่วมสู้เหลือแล้ว ให้สั่งเดินกลับจุดเกิด (โดยไม่รีเซ็ตเลือดทันที!)
        if (State == NpcState.Chasing || State == NpcState.Attacking)
        {
            PlayerCombat currentTarget = ResolveTarget();
            bool hasNonParticipantInRange = _uniqueDamageDealers.Count == 0 &&
                                            currentTarget != null && !currentTarget.IsDead &&
                                            !SafeZone.IsPlayerInAnySafeZone(currentTarget);
            if (!hasNonParticipantInRange)
            {
                BeginReturn("all boss participants dead or disengaged");
            }
        }

        // เริ่มนับคูลดาวน์ 10 วินาทีหลังผู้เล่นทั้งหมดตาย/ออกจากการต่อสู้
        if (!_bossResetPending)
        {
            _bossResetPending = true;
            BossResetCooldownRemaining = BossResetCooldownDuration;
            IsBossRegenerating = false;
            _bossRegenTickTimer = 0f;
            Debug.Log($"[EnemyNPC] BOSS WIPE/DISENGAGE -> Starting {BossResetCooldownDuration:0.0}s reset cooldown at HP={CurrentHealth}/{MaxHealth} ({CoopPeakPlayerCount}x).");
            return;
        }

        // ระยะที่ 1: นับถอยหลัง 10 วินาที (คงเลือดและตัวคูณไว้คงที่)
        if (BossResetCooldownRemaining > 0f)
        {
            BossResetCooldownRemaining = Mathf.Max(0f, BossResetCooldownRemaining - dt);
            if (BossResetCooldownRemaining <= 0f)
            {
                if (CurrentHealth >= MaxHealth)
                {
                    ResetBossCoopScalingToBaseline("10s reset cooldown elapsed at full HP");
                }
                else
                {
                    IsBossRegenerating = true;
                    _bossRegenTickTimer = BossRegenIntervalSeconds;
                    Debug.Log($"[EnemyNPC] BOSS 10s cooldown finished -> Starting gradual regen (+25% MaxHP every {BossRegenIntervalSeconds:0.0}s) from {CurrentHealth}/{MaxHealth}.");
                }
            }
            return;
        }

        // ระยะที่ 2: ฟื้นฟูเลือดทีละ 1/4 (25% ของ MaxHP) ทุก 3.0 วินาที จนกว่าจะเต็ม 100%
        if (IsBossRegenerating)
        {
            _bossRegenTickTimer -= dt;
            if (_bossRegenTickTimer <= 0f)
            {
                _bossRegenTickTimer += BossRegenIntervalSeconds;
                int healAmount = Mathf.Max(1, Mathf.CeilToInt(MaxHealth * BossRegenFractionPerTick));
                int before = CurrentHealth;
                CurrentHealth = Mathf.Min(MaxHealth, CurrentHealth + healAmount);
                Debug.Log($"[EnemyNPC] BOSS GRADUAL REGEN (+25% MaxHP = +{healAmount}): {before} -> {CurrentHealth}/{MaxHealth}.");

                if (CurrentHealth >= MaxHealth)
                {
                    ResetBossCoopScalingToBaseline("gradual regeneration reached 100% HP undisturbed");
                }
            }
        }
    }

    /// <summary>
    /// นับจำนวนผู้เล่นที่ยังมีชีวิตและยืนอยู่ภายในรัศมีลานบอส (ใช้แสดงผลเท่านั้น ไม่ใช้ทริกเกอร์สเกลพลังบอส)
    /// </summary>
    public int CountLivingPlayersInBossZone()
    {
        Vector3 arenaCenter = SandboxWorldSpawner.Instance != null
            ? SandboxWorldSpawner.Instance.BossArenaCenter
            : HomePosition;
        float arenaRadius = SandboxWorldSpawner.Instance != null
            ? SandboxWorldSpawner.Instance.BossArenaProximityRadius
            : 18f;

        int count = 0;
        for (int i = 0; i < PlayerCombat.All.Count; i++)
        {
            var p = PlayerCombat.All[i];
            if (p == null || !p.IsSpawned || p.IsDead) continue;
            if (SafeZone.IsPlayerInAnySafeZone(p)) continue;
            if (PlanarDistance(p.transform.position, arenaCenter) <= arenaRadius)
                count++;
        }
        return Mathf.Max(0, count + _simulatedExtraBossPlayers);
    }

    /// <summary>
    /// ขยายค่าพลังบอสขึ้นเป็นตัวคูณใหม่ (newMultiplier เท่า) โดยคงสัดส่วน % พลังชีวิตปัจจุบันไว้เท่าเดิม
    /// </summary>
    private void ScaleBossUpToMultiplier(int newMultiplier, int uniqueAttackerCount)
    {
        if (!IsBoss || !Object.HasStateAuthority) return;
        newMultiplier = Mathf.Max(1, newMultiplier);
        if (newMultiplier <= CoopPeakPlayerCount) return;

        if (_baseMaxHealth <= 0)
        {
            int lvl = Mathf.Clamp(ScaledLevel > 0 ? ScaledLevel : 25, 20, PlayerCombat.MaxLevel);
            ComputeBossBaseStats(lvl, out _baseMaxHealth, out _baseAttackDamage, out _baseDefense, out experienceReward);
        }

        float prevMax = Mathf.Max(1, MaxHealth);
        float hpFraction = Mathf.Clamp01(CurrentHealth / prevMax);

        CoopPeakPlayerCount = newMultiplier;
        MaxHealth = _baseMaxHealth * newMultiplier;
        NetworkMaxHealth = MaxHealth;
        AttackDamage = _baseAttackDamage * newMultiplier;
        Defense = _baseDefense * newMultiplier;
        CurrentHealth = Mathf.Clamp(Mathf.RoundToInt(MaxHealth * hpFraction), 1, MaxHealth);

        Debug.Log($"[EnemyNPC] BOSS ON-HIT CO-OP SCALING UP -> {newMultiplier}x (UniqueAttackers={uniqueAttackerCount}): HP={CurrentHealth}/{MaxHealth} ({hpFraction * 100f:0}%), ATK={AttackDamage}, DEF={Defense}.");
    }

    /// <summary>
    /// รีเซ็ตค่าพลังและตัวคูณ Co-op ของบอสกลับสู่ระดับพื้นฐาน 1.0x (เรียกเฉพาะเมื่อฟื้นเลือดครบ 100% โดยไม่ถูกรบกวนหรือเกิดใหม่)
    /// </summary>
    public void ResetBossCoopScalingToBaseline(string reason)
    {
        if (!IsBoss || !Object.HasStateAuthority) return;
        if (_baseMaxHealth <= 0)
        {
            int lvl = Mathf.Clamp(ScaledLevel > 0 ? ScaledLevel : 25, 20, PlayerCombat.MaxLevel);
            ComputeBossBaseStats(lvl, out _baseMaxHealth, out _baseAttackDamage, out _baseDefense, out experienceReward);
        }

        CoopPeakPlayerCount = 1;
        UniqueBossAttackerCount = 0;
        BossResetCooldownRemaining = -1f;
        IsBossRegenerating = false;
        _bossResetPending = false;
        _bossRegenTickTimer = 0f;
        MaxHealth = _baseMaxHealth;
        NetworkMaxHealth = _baseMaxHealth;
        AttackDamage = _baseAttackDamage;
        Defense = _baseDefense;
        CurrentHealth = MaxHealth;
        _uniqueDamageDealers.Clear();
        _simulatedAliveParticipants.Clear();
        _damageDealtMap.Clear();
        _syncedBossDamageBoard.Clear();
        _simulatedNameOverrides.Clear();
        if (IsSpawned && Object != null && Object.IsValid && Object.HasStateAuthority)
            RPC_SyncBossDamageBoard(default);
        Debug.Log($"[EnemyNPC] BOSS CO-OP RESET -> 1x baseline ({reason}): HP={CurrentHealth}/{MaxHealth}, ATK={AttackDamage}, DEF={Defense}.");
    }

    /// <summary>
    /// ตัวช่วยทดสอบสำหรับ Play Mode: จำลองจำนวนผู้เล่นเพิ่มเติมที่เดินเข้ามาในลานบอสโดยยังไม่ตีบอส (เพื่อยืนยันว่าไม่สเกลเมื่อแค่เดินเข้าโซน)
    /// </summary>
    public void SetSimulatedExtraBossPlayersForTest(int extraPlayers)
    {
        if (!IsBoss || !Object.HasStateAuthority) return;
        _simulatedExtraBossPlayers = Mathf.Max(0, extraPlayers);
        ActivePlayersInBossZone = CountLivingPlayersInBossZone();
    }

    /// <summary>
    /// ตัวช่วยทดสอบสำหรับ Play Mode: จำลองการโจมตีบอสจากผู้เล่นหลายคนที่ระบุ PlayerRef พร้อมกำหนดสถานะมีชีวิตอยู่ในลานบอส
    /// ผ่านขั้นตอน RegisterBossAttackerOnHit + คำนวณดาเมจจริงทุกบรรทัดเหมือนการเล่น Co-op จริง
    /// </summary>
    public void SimulateBossHitForTest(PlayerRef simulatedAttacker, string displayName, int rawDamage, bool aliveInArena = true)
    {
        if (!IsBoss || !IsSpawned || !Object.HasStateAuthority || !IsAlive || simulatedAttacker == PlayerRef.None) return;
        if (!string.IsNullOrEmpty(displayName))
            _simulatedNameOverrides[simulatedAttacker.RawEncoded] = displayName;
        if (aliveInArena)
            _simulatedAliveParticipants.Add(simulatedAttacker);
        else
            _simulatedAliveParticipants.Remove(simulatedAttacker);

        // 1. ลงทะเบียน On-Hit Scaling และยกเลิก Reset/Regen ทันทีหากกำลังฟื้นเลือดอยู่
        RegisterBossAttackerOnHit(simulatedAttacker);

        // 2. คำนวณและหักเลือดตาม Defense ปัจจุบันหลังสเกล
        int raw = Mathf.Clamp(rawDamage, 1, maxDamagePerHit);
        int applied = Defense > 0 ? Mathf.Max(1, raw - Defense) : raw;
        RecordPlayerDamage(simulatedAttacker, applied);
        CurrentHealth = Mathf.Max(1, CurrentHealth - applied);
        if (State == NpcState.Returning || State == NpcState.Idle || State == NpcState.Patrol)
            ChangeState(NpcState.Chasing, "simulated co-op boss hit");
    }

    /// <summary>
    /// ตัวช่วยทดสอบสำหรับ Play Mode: อัปเดตสถานะว่าผู้เล่นจำลองยังมีชีวิตอยู่ในลานบอสหรือไม่ (ใช้จำลอง P1 ตาย / P2 ตาย)
    /// </summary>
    public void SetSimulatedBossParticipantAliveForTest(PlayerRef simulatedAttacker, bool aliveInArena)
    {
        if (!IsBoss || !Object.HasStateAuthority || simulatedAttacker == PlayerRef.None) return;
        if (aliveInArena)
            _simulatedAliveParticipants.Add(simulatedAttacker);
        else
            _simulatedAliveParticipants.Remove(simulatedAttacker);
    }

    /// <summary>
    /// ตัวช่วยทดสอบสำหรับ Play Mode: เดินเวลาของระบบ Boss Reset Cooldown (10s) และ Gradual Regen (+25% ทุก 3s) แบบแม่นยำทีละขั้น
    /// </summary>
    public void AdvanceBossCombatRegenForTest(float totalSeconds, float step = 0.25f)
    {
        if (!IsBoss || !Object.HasStateAuthority) return;
        float remaining = Mathf.Max(0f, totalSeconds);
        float dt = Mathf.Clamp(step, 0.05f, 1.0f);
        while (remaining > 0.0001f)
        {
            float slice = Mathf.Min(dt, remaining);
            TickBossCombatAndRegen(slice);
            remaining -= slice;
        }
    }

    /// <summary>
    /// เมธอดเดิมสำหรับความเข้ากันได้ย้อนหลัง (ส่งต่อให้ ApplyZoneLevel โดยตรง)
    /// </summary>
    public void ApplyDynamicScaling(int targetLevel)
    {
        ApplyZoneLevel(targetLevel);
    }

    /// <summary>
    /// กำหนดจุดศูนย์กลางและรัศมีโซนลาดตระเวนส่วนตัว (Individual Patrol Zone) ให้กับมอนสเตอร์แต่ละตัวแยกกัน
    /// เพื่อไม่ให้มอนสเตอร์ในแคมป์เดียวกันเดินกระจุกรวมกัน โดยยังคงสังกัด CampId เดิมสำหรับหีบสมบัติและการเกิดใหม่
    /// </summary>
    public void ConfigurePersonalPatrolZone(Vector3 anchorPosition, float personalRadius, int zoneIndex = 0)
    {
        float clampedRadius = Mathf.Clamp(personalRadius, 1.5f, 8.0f);
        patrolRadius = clampedRadius;
        _hasConfiguredPersonalZone = true;
        _configuredZoneAnchor = anchorPosition;
        _configuredPatrolRadius = clampedRadius;
        _configuredZoneIndex = zoneIndex;

        if (Object != null && Object.IsValid && Object.HasStateAuthority)
        {
            HomePosition = anchorPosition;
            PersonalPatrolRadius = clampedRadius;
            PersonalZoneIndex = zoneIndex;
        }
    }

    public void ApplyBossConfiguration()
    {
        ApplyBossVisualAndCore();
        int bossLvl = Mathf.Clamp(ScaledLevel >= 20 ? ScaledLevel : 25, 20, PlayerCombat.MaxLevel);
        ApplyZoneLevel(bossLvl);
    }

    private void ApplyBossVisualAndCore()
    {
        if (_bossConfigured) return;
        _bossConfigured = true;

        gameObject.name = "Boss_SovereignGolem";
        AttackCooldown = 1.75f;
        AttackRange = 3.2f;
        aggroRadius = 11.5f;
        leashRadius = 21.0f;
        patrolRadius = 4.2f;
        chaseSpeed = 3.4f;
        returnSpeed = 4.5f;
        maxHitDistance = 7.5f;

        transform.localScale = hasRiggedModel ? transform.localScale : new Vector3(2.2f, 2.3f, 2.2f);
        if (_lockPoint != null) _lockPoint.localPosition = new Vector3(0f, hasRiggedModel ? riggedLockPointHeight : 0.85f, 0f);

        // สร้างมงกุฎและไหล่ Primitive เฉพาะกรณีที่ใช้ Capsule สำรองเท่านั้น (หากเป็นโมเดล Golem 3D จริงไม่ต้องสร้างทับ)
        if (!hasRiggedModel && _bossCrownRoot == null)
        {
            _bossCrownRoot = new GameObject("BossVisualCrown");
            _bossCrownRoot.transform.SetParent(transform, false);
            _bossCrownRoot.transform.localPosition = Vector3.zero;

            Color gold = new Color(1.0f, 0.78f, 0.14f, 1f);
            Color darkCrimson = new Color(0.35f, 0.04f, 0.08f, 1f);

            CreateBossPart(_bossCrownRoot.transform, "CrownRing", PrimitiveType.Cylinder,
                new Vector3(0f, 1.05f, 0f), new Vector3(0.68f, 0.08f, 0.68f), gold);
            CreateBossPart(_bossCrownRoot.transform, "ShoulderLeft", PrimitiveType.Cube,
                new Vector3(-0.55f, 0.42f, 0f), new Vector3(0.38f, 0.28f, 0.48f), darkCrimson);
            CreateBossPart(_bossCrownRoot.transform, "ShoulderRight", PrimitiveType.Cube,
                new Vector3(0.55f, 0.42f, 0f), new Vector3(0.38f, 0.28f, 0.48f), darkCrimson);
        }
    }

    private static void CreateBossPart(Transform parent, string partName, PrimitiveType type, Vector3 localPos, Vector3 localScale, Color color)
    {
        var part = GameObject.CreatePrimitive(type);
        part.name = partName;
        var col = part.GetComponent<Collider>();
        if (col != null) Destroy(col);
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPos;
        part.transform.localScale = localScale;
        UrpMaterialHelper.ApplyLitColor(part.GetComponent<Renderer>(), color);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        IsSpawned = false;
        All.Remove(this);
        StopAllCoroutines();
        if (_navAgent != null) _navAgent.enabled = false;
        _bossAttackRoutine = null;
        _isTelegraphing = false;
        DestroyPersonalZoneGroundRing();
    }

    private void OnDestroy()
    {
        DestroyPersonalZoneGroundRing();
    }

    // ------------------------------------------------------------------ ระบบกู้คืนสิทธิ์ StateAuthority เมื่อ Host หลุด (แก้บัค Camp 2 & 4 ค้าง)

    /// <summary>
    /// ทำงานอัตโนมัติเมื่อ Photon Fusion โอนย้าย StateAuthority ของมอนสเตอร์หรือบอสตัวนี้มายังเครื่องปัจจุบัน (หรือโอนออก)
    /// รักษาค่า CurrentHealth, Co-op Scaling และเปิดระบบ AI / NavMesh ต่อได้ทันทีโดยไม่ค้างและไม่รีเซ็ตเลือด
    /// </summary>
    public void StateAuthorityChanged()
    {
        if (!IsSpawned || Object == null || !Object.IsValid) return;

        if (Object.HasStateAuthority)
        {
            AuthorityTakeoverCount++;
            // คงค่าเลือดปัจจุบันไว้ห้ามรีเซ็ตเต็ม 100% เมื่อรับช่วง StateAuthority ต่อจาก Host เดิม
            ApplyZoneLevel(Mathf.Max(1, ScaledLevel), preserveCurrentHealth: true);
            if (IsBoss) ApplyCoopStatsFromPeak();

            TryInitNavMeshAgent();
            _hasPatrolPoint = false;
            _stateTimer = UnityEngine.Random.Range(0.15f, 0.55f);

            // หากค้างอยู่กลางการร่ายท่าบอสจากเครื่องเดิม ให้ล้างธงค้างเพื่อเริ่มวงจร AI ต่อได้ทันที
            _isTelegraphing = false;
            if (_bossAttackRoutine != null)
            {
                StopCoroutine(_bossAttackRoutine);
                _bossAttackRoutine = null;
            }

            Debug.Log($"[EnemyNPC] StateAuthority TAKEN OVER -> Camp {CampId} ({name}, Boss={IsBoss}) now owned by {Runner?.LocalPlayer}. HP={CurrentHealth}/{MaxHealth}, State={State}.");
        }
        else
        {
            // เครื่อง Remote Proxy ปิด NavMeshAgent เพื่อไม่ให้ชนกับ NetworkTransform
            if (_navAgent != null) _navAgent.enabled = false;
            if (_bossAttackRoutine != null)
            {
                StopCoroutine(_bossAttackRoutine);
                _bossAttackRoutine = null;
            }
            _isTelegraphing = false;
        }
    }

    /// <summary>
    /// ตรวจสอบว่า PlayerRef ที่ระบุยังคงเชื่อมต่อและอยู่ในห้อง Shared Mode ปัจจุบันจริงหรือไม่
    /// </summary>
    public static bool IsPlayerActiveInRunner(NetworkRunner runner, PlayerRef player)
    {
        if (runner == null || !runner.IsRunning || player == PlayerRef.None) return false;
        if (!runner.IsPlayerValid(player)) return false;
        foreach (PlayerRef active in runner.ActivePlayers)
        {
            if (active == player) return true;
        }
        return false;
    }

    /// <summary>
    /// ตรวจสอบว่าเครื่องปัจจุบันควรเป็นผู้รับช่วง StateAuthority แบบ Deterministic หรือไม่
    /// (ให้สิทธิ์ SharedModeMasterClient เป็นอันดับแรก หรือผู้เล่นที่มี RawEncoded ต่ำสุดที่ยังออนไลน์อยู่เป็นตัวสำรอง)
    /// </summary>
    public static bool ShouldLocalClientTakeOverAuthority(NetworkRunner runner)
    {
        if (runner == null || !runner.IsRunning || runner.LocalPlayer == PlayerRef.None) return false;
        if (runner.IsSharedModeMasterClient) return true;

        int lowestRaw = int.MaxValue;
        foreach (PlayerRef active in runner.ActivePlayers)
        {
            if (active != PlayerRef.None && active.RawEncoded < lowestRaw)
                lowestRaw = active.RawEncoded;
        }
        return runner.LocalPlayer.RawEncoded == lowestRaw;
    }

    /// <summary>
    /// ตรวจสอบว่ามอนสเตอร์ตัวนี้ขาดเจ้าของ StateAuthority (เช่น Host เดิมหลุดออกจากห้อง) หรือไม่
    /// </summary>
    public bool IsAuthorityOrphaned()
    {
        if (!IsSpawned || Object == null || !Object.IsValid || Runner == null || !Runner.IsRunning) return false;
        PlayerRef auth = Object.StateAuthority;
        return auth == PlayerRef.None || !IsPlayerActiveInRunner(Runner, auth);
    }

    /// <summary>
    /// พยายามดึงสิทธิ์ StateAuthority กลับมาที่เครื่อง MasterClient / Lowest Active Player อัตโนมัติเมื่อตรวจพบว่ามอนสเตอร์ขาดเจ้าของ
    /// แก้ปัญหามอนสเตอร์ใน Camp 2 และ Camp 4 ค้างนิ่งและตีไม่เข้าเมื่อ Host ออกจากห้อง
    /// </summary>
    public bool TryRecoverOrphanedAuthority(bool ignoreThrottle = false)
    {
        if (!IsSpawned || Object == null || !Object.IsValid || Runner == null || !Runner.IsRunning) return false;
        if (Object.HasStateAuthority) return false;
        if (!IsAuthorityOrphaned()) return false;
        if (!ShouldLocalClientTakeOverAuthority(Runner)) return false;

        if (!ignoreThrottle && Time.unscaledTime < _nextAuthorityRecoveryAt) return false;
        _nextAuthorityRecoveryAt = Time.unscaledTime + 0.45f;

        Object.RequestStateAuthority();
        Debug.Log($"[EnemyNPC] Orphaned authority detected on Camp {CampId} ({name}) [PrevAuth={Object.StateAuthority}] -> Requesting StateAuthority for {Runner.LocalPlayer}.");
        return true;
    }

    private void Update()
    {
        if (!IsSpawned) return;

        // ตรวจสอบและกู้คืน StateAuthority อัตโนมัติทันทีหาก Host เดิมหลุดออกจากห้อง
        if (Object != null && Object.IsValid && !Object.HasStateAuthority)
        {
            TryRecoverOrphanedAuthority();
        }

        // ซิงก์ตำแหน่ง/รัศมีของเส้นวงกลมโซนส่วนตัวบนพื้น (รองรับทั้ง Host และ Remote Client หลัง Network Sync)
        if (!IsBoss)
        {
            EnsurePersonalZoneGroundRing();
        }

        if (animator == null || animator.runtimeAnimatorController == null) return;

        float dt = Mathf.Max(0.0001f, Time.deltaTime);
        Vector3 pos = transform.position;
        Vector3 delta = pos - _lastWorldPos;
        delta.y = 0f;
        _lastWorldPos = pos;

        float rawSpeed = State == NpcState.Dead || State == NpcState.Staggered ? 0f : (delta.magnitude / dt);
        _smoothedAnimSpeed = Mathf.Lerp(_smoothedAnimSpeed, rawSpeed, 1f - Mathf.Exp(-10f * dt));

        if (_hasSpeedParam) animator.SetFloat(SpeedParam, _smoothedAnimSpeed);
        if (_hasIsMovingParam) animator.SetBool(IsMovingParam, _smoothedAnimSpeed > 0.15f);
        if (_hasIsDeadParam) animator.SetBool(IsDeadParam, State == NpcState.Dead);
        if (_hasIsStaggeredParam) animator.SetBool(IsStaggeredParam, State == NpcState.Staggered);
    }

    // ------------------------------------------------------------------ ระบบตรวจสอบการลอบสังหาร (Stealth Assassination & Vision Cone)

    /// <summary>
    /// ตรวจสอบว่าผู้เล่นอยู่ในมุมมองสายตา (Vision Cone) หรือระยะได้ยินเสียงของมอนสเตอร์หรือไม่
    /// </summary>
    public bool CanDetectPlayer(PlayerCombat player)
    {
        if (player == null || !player.IsSpawned || player.IsDead) return false;
        if (SafeZone.IsPlayerInAnySafeZone(player)) return false;

        Vector3 toPlayer = player.transform.position - transform.position;
        toPlayer.y = 0f;
        float dist = toPlayer.magnitude;
        if (dist > aggroRadius) return false;

        // บอสมีประสาทสัมผัสรอบทิศทางในลานประลอง
        if (IsBoss) return true;

        // หากเข้าใกล้ด้านหลังเกินระยะได้ยินเสียงฝีเท้า (1.15m) มอนสเตอร์จะรู้ตัวทันที
        if (dist <= backHearingRadius) return true;

        // ตรวจสอบมุมมองสายตาด้านหน้า (Vision Cone 110 องศา)
        Vector3 dirToPlayer = toPlayer / Mathf.Max(0.0001f, dist);
        float angle = Vector3.Angle(transform.forward, dirToPlayer);
        return angle <= (visionConeAngle * 0.5f);
    }

    /// <summary>
    /// ตรวจสอบว่าผู้เล่นสามารถใช้ท่าลอบสังหารจากด้านหลัง (Stealth Assassination Strike) ใส่มอนสเตอร์ตัวนี้ได้หรือไม่
    /// เงื่อนไข: มอนสเตอร์ยังมีชีวิต, ไม่ใช่บอส, ยังไม่รู้ตัว (Idle หรือ Patrol), และผู้เล่นอยู่ด้านหลังในระยะลอบสังหาร
    /// </summary>
    public bool CanBeAssassinatedBy(Transform attackerTransform)
    {
        if (attackerTransform == null || !IsAlive || IsBoss || IsAlerted) return false;
        if (State == NpcState.Returning || State == NpcState.Dead) return false;

        Vector3 toAttacker = attackerTransform.position - transform.position;
        toAttacker.y = 0f;
        float dist = toAttacker.magnitude;
        if (dist > stealthAssassinateRange || dist < 0.1f) return false;

        // ผู้เล่นต้องอยู่ด้านหลังมอนสเตอร์ (Dot Product กับทิศด้านหน้าของมอนสเตอร์น้อยกว่า 0.05)
        Vector3 dirToAttacker = toAttacker / dist;
        float dot = Vector3.Dot(transform.forward, dirToAttacker);
        return dot <= 0.05f;
    }

    // ------------------------------------------------------------------ ระบบ AI หลัก (State Authority)

    public override void FixedUpdateNetwork()
    {
        if (!IsSpawned || !Object.HasStateAuthority) return;

        float dt = Runner.DeltaTime;

        // อัปเดตระบบผู้ร่วมสู้บอส คูลดาวน์ 10 วินาที และฟื้นฟูเลือดทีละ 25% ทุก 3 วินาที
        if (IsBoss && State != NpcState.Dead)
        {
            TickBossCombatAndRegen(dt);
        }
        switch (State)
        {
            case NpcState.Idle:
                StopNavAgent();
                TickIdle(dt);
                break;
            case NpcState.Patrol:
                TickPatrol(dt);
                break;
            case NpcState.Chasing:
                TickChasing(dt);
                break;
            case NpcState.Attacking:
                StopNavAgent();
                TickAttacking(dt);
                break;
            case NpcState.Returning:
                TickReturning(dt);
                break;
            case NpcState.Staggered:
                StopNavAgent();
                TickStaggered(dt);
                break;
            case NpcState.Dead:
                StopNavAgent();
                TickDead();
                return;
        }

        if (IsSpawned && Object != null && Object.IsValid && State != NpcState.Dead && !IsUsingNavMesh)
        {
            Vector3 pos = transform.position;
            if (ResolveObstaclePenetration(ref pos))
                transform.position = pos;
        }
    }

    /// <summary>
    /// ประมวลผลช่วงเวลาที่มอนสเตอร์/บอสติดสถานะชะงักจากการถูกผู้เล่น Perfect Parry
    /// </summary>
    private void TickStaggered(float dt)
    {
        if (Time.time < _staggerEndTime) return;

        PlayerCombat target = ResolveTarget();
        if (target != null && !target.IsDead && !SafeZone.IsPlayerInAnySafeZone(target))
        {
            ChangeState(NpcState.Chasing, "recovered from parry stagger");
        }
        else
        {
            _stateTimer = 0.4f;
            ChangeState(NpcState.Idle, "recovered from parry stagger");
        }
    }

    private void TickIdle(float dt)
    {
        if (TryAcquireNearestPlayer()) return;

        float activePatrolRadius = PatrolRadius;
        _stateTimer -= dt;
        if (_stateTimer <= 0f && activePatrolRadius > 0.5f)
        {
            if (TryPickPatrolWaypoint(out Vector3 waypoint))
            {
                _patrolPoint = waypoint;
                _hasPatrolPoint = true;
                _patrolMoveElapsed = 0f;
                ChangeState(NpcState.Patrol, "personal zone patrol");
            }
            else
            {
                _stateTimer = 0.75f;
            }
        }
    }

    /// <summary>
    /// สุ่มหาจุดเดินลาดตระเวนภายในโซนส่วนตัว (HomePosition + PersonalPatrolRadius) ของมอนสเตอร์ตัวนี้เท่านั้น
    /// พร้อมตรวจไม่ให้ล้ำเข้าไปใน Safe Zone หรือทับซ้อนกับโซนส่วนตัวของมอนสเตอร์ตัวอื่น เพื่อป้องกันการเดินกระจุกตัว
    /// </summary>
    private bool TryPickPatrolWaypoint(out Vector3 waypoint)
    {
        float activeRadius = Mathf.Max(1.2f, PatrolRadius);
        for (int attempt = 0; attempt < 10; attempt++)
        {
            float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            float dist = UnityEngine.Random.Range(activeRadius * 0.35f, activeRadius);
            Vector3 candidate = HomePosition + new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);
            candidate.y = transform.position.y;

            if (SafeZone.IsPointInAnySafeZone(candidate, 1.0f)) continue;
            if (PlanarDistance(transform.position, candidate) < 1.1f) continue;

            // ตรวจสอบไม่ให้จุดเดินล้ำเข้าเขตโซนส่วนตัวของมอนสเตอร์ตัวอื่น
            bool overlapsOtherZone = false;
            for (int i = 0; i < All.Count; i++)
            {
                EnemyNPC other = All[i];
                if (other == null || other == this || !other.IsSpawned || !other.IsAlive || other.IsBoss) continue;
                float minClearance = other.PatrolRadius + 0.5f;
                if (PlanarDistance(candidate, other.HomePosition) < minClearance)
                {
                    overlapsOtherZone = true;
                    break;
                }
            }
            if (overlapsOtherZone) continue;

            waypoint = candidate;
            return true;
        }
        waypoint = HomePosition;
        return false;
    }

    private void TickPatrol(float dt)
    {
        if (TryAcquireNearestPlayer()) return;

        if (!_hasPatrolPoint)
        {
            _stateTimer = UnityEngine.Random.Range(idleWaitRange.x, idleWaitRange.y);
            ChangeState(NpcState.Idle, "no patrol point");
            return;
        }

        // หากมอนสเตอร์หลุดออกนอกรัศมีโซนส่วนตัวของตัวเอง ให้เบี่ยงเป้าหมายกลับเข้าจุด HomePosition ทันที
        float activeRadius = PatrolRadius;
        if (PlanarDistance(transform.position, HomePosition) > activeRadius + 1.0f)
        {
            _patrolPoint = HomePosition;
        }

        _patrolMoveElapsed += dt;
        float remaining = MoveTowards(_patrolPoint, patrolSpeed, dt);
        // หากเดินถึงจุดหมาย หรือใช้เวลาเดินนานเกิน 4.5 วินาที ให้เปลี่ยนจุดลาดตระเวนใหม่เพื่อไม่ให้ติดกำแพง
        if (remaining <= 0.4f || _patrolMoveElapsed >= 4.5f)
        {
            _hasPatrolPoint = false;
            _stateTimer = UnityEngine.Random.Range(idleWaitRange.x, idleWaitRange.y);
            ChangeState(NpcState.Idle, "reached personal patrol waypoint");
        }
    }

    private void TickChasing(float dt)
    {
        PlayerCombat target = ResolveTarget();
        bool targetInvalid = target == null || target.IsDead || SafeZone.IsPlayerInAnySafeZone(target);
        if (targetInvalid)
        {
            // สำหรับบอส: หากเป้าหมายหลักตายหรือเข้า Safe Zone แต่ยังมีผู้ร่วมตีบอสคนอื่นรอดชีวิต ให้สลับเป้าหมายทันทีโดยไม่รีเซ็ตเลือด!
            if (IsBoss && TrySwitchBossToNextLivingParticipant(out PlayerCombat nextParticipant))
            {
                if (nextParticipant == null) return;
                target = nextParticipant;
            }
            else
            {
                BeginReturn(target == null ? "target lost" : (target.IsDead ? "target died" : "target entered Comfort Safe Zone"));
                return;
            }
        }

        if (PlanarDistance(transform.position, HomePosition) > leashRadius)
        {
            if (IsBoss && TrySwitchBossToNextLivingParticipant(out PlayerCombat altTarget) && altTarget != null &&
                PlanarDistance(altTarget.transform.position, HomePosition) <= leashRadius)
            {
                target = altTarget;
            }
            else
            {
                BeginReturn("leash radius exceeded");
                return;
            }
        }

        float dist = PlanarDistance(transform.position, target.transform.position);

        if (IsBoss && !_isTelegraphing && Time.time >= _nextAttackTime && dist > AttackRange * 1.1f && dist <= 13.5f)
        {
            StartBossPatternAttack(target, BossAttackPattern.DarkVolleyAndCharge);
            return;
        }

        if (dist <= AttackRange && HasClearMeleeLine(target.transform.position))
        {
            ChangeState(NpcState.Attacking, $"in range ({dist:0.0}m)");
            return;
        }
        MoveTowards(target.transform.position, chaseSpeed, dt);
    }

    private void TickAttacking(float dt)
    {
        PlayerCombat target = ResolveTarget();
        bool targetInvalid = target == null || target.IsDead || SafeZone.IsPlayerInAnySafeZone(target);
        if (targetInvalid)
        {
            // สำหรับบอส: สลับเป้าหมายไปยังผู้ร่วมสู้ที่ยังมีชีวิตอยู่แทนการยกเลิกไฟต์และรีเซ็ตเลือด
            if (IsBoss && TrySwitchBossToNextLivingParticipant(out PlayerCombat nextParticipant))
            {
                if (nextParticipant == null) return;
                target = nextParticipant;
            }
            else
            {
                BeginReturn(target == null ? "target lost" : (target.IsDead ? "target died" : "target entered Comfort Safe Zone"));
                return;
            }
        }

        if (_isTelegraphing)
        {
            FaceTowards(target.transform.position, dt);
            return;
        }

        float dist = PlanarDistance(transform.position, target.transform.position);
        if (IsBoss && Time.time >= _nextAttackTime && dist > AttackRange * 1.3f && dist <= 13.5f)
        {
            StartBossPatternAttack(target, BossAttackPattern.DarkVolleyAndCharge);
            return;
        }

        if (dist > AttackRange * 1.25f || !HasClearMeleeLine(target.transform.position))
        {
            ChangeState(NpcState.Chasing, $"target moved to {dist:0.0}m");
            return;
        }

        FaceTowards(target.transform.position, dt);

        if (Time.time < _nextAttackTime) return;

        if (IsBoss)
        {
            BossAttackPattern nextPattern = (_bossPatternCycle % 3) switch
            {
                0 => BossAttackPattern.HeavyCleave,
                1 => BossAttackPattern.ShockwaveSpinAoE,
                _ => BossAttackPattern.DarkVolleyAndCharge
            };
            _bossPatternCycle++;
            StartBossPatternAttack(target, nextPattern);
            return;
        }

        _nextAttackTime = Time.time + AttackCooldown;
        RPC_PlayAttackFx();
        target.RPC_ReceiveNpcDamage(Object.Id, AttackDamage);
    }

    public void TriggerBossPatternForTest(BossAttackPattern pattern, PlayerCombat target)
    {
        if (!IsSpawned || !Object.HasStateAuthority || target == null) return;
        StartBossPatternAttack(target, pattern, fastTestTelegraph: true);
    }

    private void StartBossPatternAttack(PlayerCombat target, BossAttackPattern pattern, bool fastTestTelegraph = false)
    {
        if (_bossAttackRoutine != null) StopCoroutine(_bossAttackRoutine);
        _bossAttackRoutine = StartCoroutine(ExecuteBossPatternRoutine(target, pattern, fastTestTelegraph));
    }

    private IEnumerator ExecuteBossPatternRoutine(PlayerCombat target, BossAttackPattern pattern, bool fastTestTelegraph)
    {
        _isTelegraphing = true;
        LastBossPattern = pattern;
        _usedPatterns.Add(pattern);
        BossAttackExecutions++;

        Vector3 origin = transform.position;
        Vector3 aimDir = target != null ? (target.transform.position - origin) : transform.forward;
        aimDir.y = 0f;
        if (aimDir.sqrMagnitude < 0.001f) aimDir = transform.forward;
        else aimDir.Normalize();

        float windup = fastTestTelegraph ? 0.12f : (pattern == BossAttackPattern.ShockwaveSpinAoE ? 0.85f : 0.60f);
        _nextAttackTime = Time.time + windup + AttackCooldown;

        RPC_BossTelegraph((byte)pattern, origin, aimDir, windup);
        yield return new WaitForSeconds(windup);

        _isTelegraphing = false;
        _bossAttackRoutine = null;

        if (!IsAlive || !IsSpawned || !Object.HasStateAuthority) yield break;

        if (pattern == BossAttackPattern.HeavyCleave)
        {
            int cleaveDmg = Mathf.RoundToInt(AttackDamage * 1.5f);
            if (target != null && target.IsSpawned && !target.IsDead && !SafeZone.IsPlayerInAnySafeZone(target))
            {
                float d = PlanarDistance(transform.position, target.transform.position);
                if (d <= AttackRange + 2.4f)
                {
                    target.RPC_ReceiveNpcDamage(Object.Id, cleaveDmg);
                }
            }
        }
        else if (pattern == BossAttackPattern.ShockwaveSpinAoE)
        {
            int aoeDmg = Mathf.RoundToInt(AttackDamage * 1.35f);
            const float shockwaveRadius = 6.2f;
            RPC_PlayBossShockwaveRelease(transform.position, shockwaveRadius);

            for (int i = 0; i < PlayerCombat.All.Count; i++)
            {
                var p = PlayerCombat.All[i];
                if (p == null || !p.IsSpawned || p.IsDead || SafeZone.IsPlayerInAnySafeZone(p)) continue;
                if (PlanarDistance(transform.position, p.transform.position) <= shockwaveRadius)
                {
                    p.RPC_ReceiveNpcDamage(Object.Id, aoeDmg);
                }
            }
        }
        else if (pattern == BossAttackPattern.DarkVolleyAndCharge)
        {
            if (target != null && target.IsSpawned && !target.IsDead && !SafeZone.IsPlayerInAnySafeZone(target))
            {
                Vector3 toTarget = target.transform.position - transform.position;
                toTarget.y = 0f;
                float dist = toTarget.magnitude;
                if (dist > 2.2f)
                {
                    Vector3 lungeDir = toTarget / dist;
                    float lungeStep = Mathf.Min(3.4f, dist - 1.8f);
                    Vector3 lungedPos = transform.position + lungeDir * lungeStep;
                    ResolveObstaclePenetration(ref lungedPos);
                    EnforceSafeZoneExclusion(ref lungedPos);
                    lungedPos.y = transform.position.y;
                    transform.position = lungedPos;
                    FaceDirection(lungeDir, 1f);
                }
            }

            Vector3 currentAim = target != null ? (target.transform.position - transform.position) : aimDir;
            currentAim.y = 0f;
            if (currentAim.sqrMagnitude < 0.001f) currentAim = transform.forward;
            else currentAim.Normalize();

            int projDmg = Mathf.RoundToInt(AttackDamage * 0.95f);
            RPC_SpawnBossVolley(transform.position + Vector3.up * 1.3f, currentAim, projDmg);
        }
    }

    private void TickReturning(float dt)
    {
        // สำหรับบอส: หากระหว่างเดินกลับมีผู้ร่วมสู้ที่ยังมีชีวิตอยู่ในลานบอส ให้สลับกลับไปสู้ต่อทันทีโดยไม่รีเซ็ตเลือด
        if (IsBoss && CountActiveBossParticipants() > 0 && TrySwitchBossToNextLivingParticipant(out PlayerCombat activeFighter))
        {
            if (activeFighter != null)
            {
                ChangeState(NpcState.Chasing, "active boss participant detected during return");
                return;
            }
        }

        if (MoveTowards(HomePosition, returnSpeed, dt) <= 0.35f)
        {
            TargetId = default;
            _stateTimer = UnityEngine.Random.Range(idleWaitRange.x, idleWaitRange.y);

            if (IsBoss)
            {
                // สำหรับบอส: ห้ามเด้งเลือดเต็ม 100% ทันทีและห้ามล้างผู้ร่วมสู้!
                // ปล่อยให้ TickBossCombatAndRegen รอคูลดาวน์ 10 วินาที แล้วค่อยๆ ฟื้นเลือดทีละ 25% ทุก 3 วินาที
            }
            else
            {
                CurrentHealth = MaxHealth;
                _damageDealtMap.Clear();
            }

            ChangeState(NpcState.Idle, IsBoss ? "arrived home, awaiting 10s cooldown before gradual regen" : "arrived home, health & aggro reset");
        }
    }

    private void TickDead()
    {
        if (Time.time - _deathTime >= despawnDelayAfterDeath)
        {
            Runner.Despawn(Object);
        }
    }

    /// <summary>
    /// ค้นหาผู้เล่นที่อยู่ในระยะสายตา (Vision Cone) เพื่อเริ่มไล่ล่า (หากผู้เล่นย่องมาด้านหลังนอก Vision Cone จะยังไม่ถูกพบ)
    /// </summary>
    private bool TryAcquireNearestPlayer()
    {
        PlayerCombat best = null;
        float bestDist = aggroRadius;
        for (int i = 0; i < PlayerCombat.All.Count; i++)
        {
            var p = PlayerCombat.All[i];
            if (!CanDetectPlayer(p)) continue;

            float d = PlanarDistance(transform.position, p.transform.position);
            if (d <= bestDist) { bestDist = d; best = p; }
        }
        if (best == null) return false;

        SetTarget(best);
        ChangeState(NpcState.Chasing, "player spotted in vision cone");
        BroadcastCampAggro(best);
        return true;
    }

    /// <summary>
    /// ส่งสัญญาณเรียกเพื่อนร่วมแคมป์เฉพาะตัวที่อยู่ใกล้ในระยะ campAllyAlertRadius (ป้องกันมอนสเตอร์ที่อยู่ห่างกันคนละโซนส่วนตัววิ่งกรูมารวมกัน)
    /// </summary>
    private void BroadcastCampAggro(PlayerCombat target)
    {
        if (target == null || !target.IsSpawned || CampId <= 0 || IsBoss || SafeZone.IsPlayerInAnySafeZone(target)) return;

        for (int i = 0; i < All.Count; i++)
        {
            EnemyNPC ally = All[i];
            if (ally == null || !ally.IsSpawned || !ally.IsAlive || ally == this || ally.CampId != CampId) continue;
            if (PlanarDistance(transform.position, ally.transform.position) > campAllyAlertRadius) continue;
            ally.RPC_JoinCampAggro(target.Object.Id);
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_JoinCampAggro(NetworkId targetId)
    {
        if (!IsSpawned || !Object.HasStateAuthority || !IsAlive || CampId <= 0) return;
        if (State == NpcState.Returning || State == NpcState.Dead) return;

        PlayerCombat target = FindPlayerByNetworkId(targetId);
        if (target == null || target.IsDead || SafeZone.IsPlayerInAnySafeZone(target)) return;

        SetTarget(target);
        if (State == NpcState.Idle || State == NpcState.Patrol)
            ChangeState(NpcState.Chasing, "camp ally aggro");
    }

    private void BeginReturn(string reason)
    {
        TargetId = default;
        if (_bossAttackRoutine != null) { StopCoroutine(_bossAttackRoutine); _bossAttackRoutine = null; }
        _isTelegraphing = false;
        ChangeState(NpcState.Returning, reason);
    }

    private void SetTarget(PlayerCombat p)
    {
        TargetId = p.Object.Id;
    }

    private PlayerCombat ResolveTarget()
    {
        if (!TargetId.IsValid) return null;
        for (int i = 0; i < PlayerCombat.All.Count; i++)
        {
            var p = PlayerCombat.All[i];
            if (p != null && p.IsSpawned && p.Object.Id == TargetId) return p;
        }
        return null;
    }

    private static PlayerCombat FindPlayerByNetworkId(NetworkId id)
    {
        if (!id.IsValid) return null;
        for (int i = 0; i < PlayerCombat.All.Count; i++)
        {
            PlayerCombat player = PlayerCombat.All[i];
            if (player != null && player.IsSpawned && player.Object.Id == id) return player;
        }
        return null;
    }

    private void ChangeState(NpcState next, string reason)
    {
        if (State == next) return;
        State = next;
    }

    // ------------------------------------------------------------------ การเคลื่อนที่และหลบสิ่งกีดขวาง

    private float MoveTowards(Vector3 destination, float speed, float dt)
    {
        Vector3 pos = transform.position;
        Vector3 delta = destination - pos;
        delta.y = 0f;
        float dist = delta.magnitude;
        if (dist < 0.0001f) return 0f;

        // โหมด Hybrid NavMesh: ใช้ NavMeshAgent นำทางอัตโนมัติเมื่อฉากมีการ Bake NavMesh แล้ว
        if (IsUsingNavMesh)
        {
            Vector3 navDest = destination;
            EnforceSafeZoneExclusion(ref navDest);
            _navAgent.speed = speed;
            _navAgent.isStopped = false;
            _navAgent.SetDestination(navDest);

            Vector3 velDir = _navAgent.desiredVelocity;
            velDir.y = 0f;
            if (velDir.sqrMagnitude > 0.01f)
                FaceDirection(velDir.normalized, dt);
            else
                FaceDirection(delta / dist, dt);

            return PlanarDistance(transform.position, destination);
        }

        Vector3 desiredDir = delta / dist;
        Vector3 steeredDir = ComputeObstacleAvoidanceDir(pos, desiredDir, Mathf.Min(dist, obstacleCheckDistance));

        Vector3 separation = ComputeAllySeparation(pos);
        Vector3 finalDir = steeredDir + separation * 0.65f;
        finalDir.y = 0f;
        if (finalDir.sqrMagnitude > 0.0001f)
            finalDir.Normalize();
        else
            finalDir = steeredDir;

        float step = Mathf.Min(speed * dt, dist);
        Vector3 nextPos = pos + finalDir * step;

        ResolveObstaclePenetration(ref nextPos);
        EnforceSafeZoneExclusion(ref nextPos);
        if (snapToGround)
            SnapPositionToGround(ref nextPos, dt);
        else
            nextPos.y = pos.y;

        transform.position = nextPos;
        FaceDirection(finalDir, dt);
        return PlanarDistance(nextPos, destination);
    }

    /// <summary>
    /// ยิง Raycast ลงพื้นแบบไม่จัดสรรหน่วยความจำใหม่ เพื่อปรับความสูง Y ให้เดินขึ้นเนิน/สะพานได้โดยไม่ลอยหรือจมพื้น
    /// </summary>
    private void SnapPositionToGround(ref Vector3 pos, float dt)
    {
        float originOffset = hasRiggedModel ? 1.5f : 0.9f;
        Vector3 origin = new Vector3(pos.x, pos.y + originOffset, pos.z);
        int hits = Physics.RaycastNonAlloc(origin, Vector3.down, s_groundRayBuffer, originOffset + 3.5f, groundSnapMask, QueryTriggerInteraction.Ignore);

        bool found = false;
        float bestY = float.NegativeInfinity;
        for (int i = 0; i < hits; i++)
        {
            var col = s_groundRayBuffer[i].collider;
            if (col == null) continue;
            if (col.transform.IsChildOf(transform)) continue;
            if (col.attachedRigidbody != null) continue;
            if (col.GetComponentInParent<PlayerCombat>() != null) continue;
            if (col.GetComponentInParent<EnemyNPC>() != null) continue;

            float y = s_groundRayBuffer[i].point.y;
            if (y > bestY)
            {
                bestY = y;
                found = true;
            }
        }

        if (!found) return;

        float targetY = bestY;
        if (!hasRiggedModel && BodyCollider is CapsuleCollider cap)
        {
            targetY += cap.height * 0.5f * Mathf.Abs(transform.lossyScale.y);
        }
        pos.y = Mathf.MoveTowards(pos.y, targetY, 14f * dt);
    }

    private static void EnforceSafeZoneExclusion(ref Vector3 candidatePos)
    {
        var zone = SafeZone.Instance;
        if (zone == null) return;
        Vector3 diff = candidatePos - zone.Center;
        diff.y = 0f;
        float minDist = zone.Radius + 0.6f;
        float d = diff.magnitude;
        if (d < minDist)
        {
            Vector3 pushDir = d > 0.001f ? diff / d : Vector3.forward;
            candidatePos.x = zone.Center.x + pushDir.x * minDist;
            candidatePos.z = zone.Center.z + pushDir.z * minDist;
        }
    }

    private Vector3 ComputeObstacleAvoidanceDir(Vector3 pos, Vector3 desiredDir, float checkDist)
    {
        if (!ProbeObstacle(pos, desiredDir, checkDist, out RaycastHit directHit))
            return desiredDir;

        if (Time.time > _avoidSignUntil || _avoidSign == 0)
        {
            Vector3 tangentRight = new Vector3(desiredDir.z, 0f, -desiredDir.x);
            float dot = Vector3.Dot(directHit.normal, tangentRight);
            _avoidSign = dot >= 0f ? 1 : -1;
            _avoidSignUntil = Time.time + 1.15f;
        }

        float[] angles = { 30f, 50f, 70f, 90f, 115f, 140f };
        int[] signs = { _avoidSign, -_avoidSign };

        Vector3 bestDir = Vector3.ProjectOnPlane(desiredDir, directHit.normal);
        bestDir.y = 0f;
        float bestClearDist = -1f;

        for (int s = 0; s < signs.Length; s++)
        {
            int sign = signs[s];
            for (int a = 0; a < angles.Length; a++)
            {
                Vector3 candidate = Quaternion.Euler(0f, angles[a] * sign, 0f) * desiredDir;
                candidate.y = 0f;
                candidate.Normalize();

                if (!ProbeObstacle(pos, candidate, checkDist, out RaycastHit hit))
                {
                    _avoidSign = sign;
                    _avoidSignUntil = Time.time + 0.9f;
                    return candidate;
                }

                if (hit.distance > bestClearDist)
                {
                    bestClearDist = hit.distance;
                    bestDir = candidate;
                }
            }
        }

        return bestDir.sqrMagnitude > 0.0001f ? bestDir.normalized : desiredDir;
    }

    private bool ProbeObstacle(Vector3 pos, Vector3 dir, float distance, out RaycastHit closestObstacleHit)
    {
        closestObstacleHit = default;
        Vector3 origin = new Vector3(pos.x, 0.9f, pos.z);
        int count = Physics.SphereCastNonAlloc(origin, obstacleProbeRadius, dir, s_castBuffer, distance, ~0, QueryTriggerInteraction.Ignore);

        float bestDist = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit h = s_castBuffer[i];
            if (!IsSolidObstacle(h.collider)) continue;
            if (h.distance < bestDist)
            {
                bestDist = h.distance;
                closestObstacleHit = h;
                found = true;
            }
        }
        return found;
    }

    private bool HasClearMeleeLine(Vector3 targetPos)
    {
        Vector3 origin = new Vector3(transform.position.x, 1.0f, transform.position.z);
        Vector3 end = new Vector3(targetPos.x, 1.0f, targetPos.z);
        Vector3 delta = end - origin;
        float dist = delta.magnitude;
        if (dist < 0.05f) return true;

        int count = Physics.RaycastNonAlloc(origin, delta / dist, s_castBuffer, dist, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (IsSolidObstacle(s_castBuffer[i].collider))
                return false;
        }
        return true;
    }

    private Vector3 ComputeAllySeparation(Vector3 pos)
    {
        Vector3 push = Vector3.zero;
        for (int i = 0; i < All.Count; i++)
        {
            EnemyNPC other = All[i];
            if (other == null || other == this || !other.IsAlive) continue;
            Vector3 diff = pos - other.transform.position;
            diff.y = 0f;
            float d = diff.magnitude;
            if (d > 0.001f && d < allySeparationRadius)
            {
                push += (diff / d) * ((allySeparationRadius - d) / allySeparationRadius);
            }
        }
        return push;
    }

    private bool ResolveObstaclePenetration(ref Vector3 candidatePos)
    {
        if (BodyCollider == null) return false;

        Vector3 checkCenter = new Vector3(candidatePos.x, 1.0f, candidatePos.z);
        int count = Physics.OverlapSphereNonAlloc(checkCenter, 1.4f, s_overlapBuffer, ~0, QueryTriggerInteraction.Ignore);
        bool pushed = false;

        for (int i = 0; i < count; i++)
        {
            Collider col = s_overlapBuffer[i];
            if (!IsSolidObstacle(col)) continue;

            if (Physics.ComputePenetration(
                BodyCollider, candidatePos, transform.rotation,
                col, col.transform.position, col.transform.rotation,
                out Vector3 penDir, out float penDist))
            {
                penDir.y = 0f;
                if (penDir.sqrMagnitude > 0.0001f)
                {
                    penDir.Normalize();
                    candidatePos += penDir * (penDist + 0.02f);
                    pushed = true;
                }
            }
        }
        return pushed;
    }

    private bool IsSolidObstacle(Collider col)
    {
        if (col == null || col.isTrigger || col == BodyCollider) return false;
        if (col.GetComponentInParent<EnemyNPC>() != null) return false;
        if (col.GetComponentInParent<PlayerCombat>() != null) return false;

        Bounds b = col.bounds;
        if (b.max.y <= 0.25f) return false;
        return true;
    }

    private void FaceTowards(Vector3 worldPoint, float dt)
    {
        Vector3 d = worldPoint - transform.position;
        d.y = 0f;
        if (d.sqrMagnitude > 0.0001f) FaceDirection(d.normalized, dt);
    }

    private void FaceDirection(Vector3 dir, float dt)
    {
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir, Vector3.up), 1f - Mathf.Exp(-turnSpeed * dt));
    }

    private static float PlanarDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }

    // ------------------------------------------------------------------ ระบบรับดาเมจและบันทึกสัดส่วนดาเมจรายบุคคล

    /// <summary>
    /// บันทึกดาเมจสะสมของผู้เล่นแต่ละคนลงใน _damageDealtMap และรายงานต่อไปยังตารางดาเมจสะสมประจำแคมป์ (SandboxWorldSpawner)
    /// </summary>
    public void RecordPlayerDamage(PlayerRef player, float amount)
    {
        if (player == PlayerRef.None || amount <= 0f) return;
        if (_damageDealtMap.TryGetValue(player, out float current))
            _damageDealtMap[player] = current + amount;
        else
            _damageDealtMap[player] = amount;

        // สะสมดาเมจรวมทั้งแคมป์เพื่อใช้ล็อกหีบสมบัติเฉพาะผู้มีส่วนร่วมในการต่อสู้
        SandboxWorldSpawner.Instance?.RecordCampDamage(CampId, IsBoss, player, amount);

        // หากเป็นบอส ให้กระจายตารางอันดับดาเมจล่าสุดไปยังทุกเครื่องเพื่อแสดงบน BossHudUI แบบเรียลไทม์
        if (IsBoss && IsSpawned && Object != null && Object.IsValid && Object.HasStateAuthority)
        {
            BroadcastBossDamageBoard();
        }
    }

    /// <summary>
    /// ตัวช่วยทดสอบสำหรับ Play Mode: จำลองผู้เล่นคนที่ 2 ทำดาเมจใส่บอสพร้อมชื่อแสดงผลเพื่อตรวจสอบ Damage Meter หลายผู้เล่น
    /// </summary>
    public void RecordSimulatedBossDamageForTest(PlayerRef simulatedPlayer, string displayName, float amount)
    {
        if (!IsBoss || simulatedPlayer == PlayerRef.None || amount <= 0f) return;
        if (!string.IsNullOrEmpty(displayName))
            _simulatedNameOverrides[simulatedPlayer.RawEncoded] = displayName;
        RecordPlayerDamage(simulatedPlayer, amount);
    }

    /// <summary>
    /// บีบอัดตารางดาเมจบอสเป็นข้อความขนาดเล็ก ("raw:dmg;...") ไม่เกิน 62 ตัวอักษรแล้วส่งผ่าน RPC ไปยังทุกเครื่อง
    /// </summary>
    private void BroadcastBossDamageBoard()
    {
        if (_damageDealtMap.Count == 0)
        {
            RPC_SyncBossDamageBoard(default);
            return;
        }

        var list = new List<KeyValuePair<PlayerRef, float>>(_damageDealtMap);
        list.Sort((a, b) => b.Value.CompareTo(a.Value));

        var sb = new StringBuilder(64);
        for (int i = 0; i < list.Count; i++)
        {
            int dmgInt = Mathf.Max(1, Mathf.RoundToInt(list[i].Value));
            string token = $"{list[i].Key.RawEncoded.ToString(CultureInfo.InvariantCulture)}:{dmgInt.ToString(CultureInfo.InvariantCulture)}";
            int needed = (sb.Length > 0 ? 1 : 0) + token.Length;
            if (sb.Length + needed > 62) break;
            if (sb.Length > 0) sb.Append(';');
            sb.Append(token);
        }

        RPC_SyncBossDamageBoard(sb.ToString());
    }

    /// <summary>
    /// รับข้อมูลตารางอันดับดาเมจของบอสบนทุกเครื่อง (Host และ Client) และแปลงเป็นรายการพร้อมชื่อผู้เล่น + % ดาเมจ
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_SyncBossDamageBoard(NetworkString<_64> payload)
    {
        _syncedBossDamageBoard.Clear();
        string rawStr = payload.ToString();
        if (string.IsNullOrEmpty(rawStr)) return;

        var parsed = new List<(int rawEncoded, int dmg)>(6);
        int totalDamage = 0;

        string[] tokens = rawStr.Split(';');
        for (int i = 0; i < tokens.Length; i++)
        {
            if (string.IsNullOrEmpty(tokens[i])) continue;
            string[] parts = tokens[i].Split(':');
            if (parts.Length < 2) continue;
            if (int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int rawEnc) &&
                int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int dmg) &&
                dmg > 0)
            {
                parsed.Add((rawEnc, dmg));
                totalDamage += dmg;
            }
        }

        if (totalDamage <= 0) return;

        parsed.Sort((a, b) => b.dmg.CompareTo(a.dmg));
        int localRaw = (Runner != null && Runner.IsRunning && Runner.LocalPlayer != PlayerRef.None)
            ? Runner.LocalPlayer.RawEncoded
            : -1;

        for (int i = 0; i < parsed.Count; i++)
        {
            int rawEnc = parsed[i].rawEncoded;
            int dmg = parsed[i].dmg;
            _syncedBossDamageBoard.Add(new BossDamageEntry
            {
                playerRawEncoded = rawEnc,
                playerName = ResolvePlayerDisplayName(rawEnc),
                damageDealt = dmg,
                damageShare = Mathf.Clamp01(dmg / (float)totalDamage),
                isLocalPlayer = rawEnc == localRaw
            });
        }
    }

    /// <summary>
    /// ค้นหาชื่อจริง (Nickname) ของผู้เล่นจาก PlayerRef RawEncoded เพื่อแสดงใน Damage Meter ของบอส
    /// </summary>
    private string ResolvePlayerDisplayName(int rawEncoded)
    {
        if (_simulatedNameOverrides.TryGetValue(rawEncoded, out string simName) && !string.IsNullOrEmpty(simName))
            return simName;

        PlayerRef pRef = PlayerRef.FromEncoded(rawEncoded);
        for (int i = 0; i < PlayerCombat.All.Count; i++)
        {
            var pc = PlayerCombat.All[i];
            if (pc == null || !pc.IsSpawned || pc.Object == null || !pc.Object.IsValid) continue;
            if (pc.Object.StateAuthority == pRef || pc.Object.InputAuthority == pRef)
            {
                var cap = pc.GetComponent<PlayerCapsule>();
                if (cap != null && !string.IsNullOrEmpty(cap.Nick.Value))
                    return cap.Nick.Value;
            }
        }
        return $"Player {pRef.PlayerId}";
    }

    /// <summary>
    /// คืนค่าสัดส่วนดาเมจ (0..1) ของผู้เล่นที่ระบุเมื่อเทียบกับดาเมจรวมทั้งหมด
    /// </summary>
    public float GetPlayerDamageShare(PlayerRef player)
    {
        if (player == PlayerRef.None || !_damageDealtMap.TryGetValue(player, out float dmg) || dmg <= 0f)
            return 0f;

        float total = 0f;
        foreach (var kv in _damageDealtMap) total += Mathf.Max(0f, kv.Value);
        return total > 0f ? Mathf.Clamp01(dmg / total) : 0f;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_TakeDamage(int damage, RpcInfo info = default)
    {
        ApplyIncomingDamage(damage, isCrit: false, comboStep: 1, info);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_TakeDamageWithCrit(int damage, NetworkBool isCrit, int comboStep, RpcInfo info = default)
    {
        ApplyIncomingDamage(damage, isCrit, comboStep, info);
    }

    private void ApplyIncomingDamage(int damage, bool isCrit, int comboStep, RpcInfo info)
    {
        if (!IsSpawned || !Object.HasStateAuthority || !IsAlive) return;

        PlayerRef sourceRef = info.Source;
        if (sourceRef == PlayerRef.None && Runner != null)
            sourceRef = Runner.LocalPlayer;

        PlayerCombat attacker = FindPlayerBySource(sourceRef);
        if (attacker == null || attacker.IsDead) return;
        if (SafeZone.IsPlayerInAnySafeZone(attacker)) return;

        float dist = PlanarDistance(transform.position, attacker.transform.position);
        float limit = IsBoss ? maxHitDistance + 1.5f : maxHitDistance;
        if (dist > limit) return;

        float now = Time.time;
        if (_lastDamageTime.TryGetValue(sourceRef, out float last) && now - last < minSecondsBetweenHitsPerPlayer)
            return;
        _lastDamageTime[sourceRef] = now;

        // สำหรับบอส: ลงทะเบียน On-Hit Co-op Scaling (+1x ต่อผู้เล่นคนใหม่ที่ตีโดนจริง)
        // และยกเลิกคูลดาวน์ 10 วินาที / การฟื้นฟูเลือดทีละ 25% ทันทีเมื่อถูกโจมตี
        if (IsBoss)
        {
            RegisterBossAttackerOnHit(sourceRef);
        }

        int raw = Mathf.Clamp(damage, 1, maxDamagePerHit);
        int applied = Defense > 0 ? Mathf.Max(1, raw - Defense) : raw;

        // บันทึกดาเมจของผู้เล่นคนนี้ลงในตารางเพื่อคำนวณโอกาสได้ของตอนตาย
        RecordPlayerDamage(sourceRef, applied);

        int before = CurrentHealth;
        CurrentHealth = Mathf.Max(0, before - applied);
        Debug.Log($"[EnemyNPC] {name} (Lv.{ScaledLevel}, DEF={Defense}) takes {applied}{(isCrit ? " (CRIT!)" : "")} from {attacker.name} ({sourceRef}): {before} -> {CurrentHealth}/{MaxHealth}.");

        RPC_HitFeedback(applied, isCrit, comboStep, sourceRef);

        if (CurrentHealth <= 0)
        {
            Die(sourceRef);
            return;
        }

        // สำหรับบอส: หากถูกตีระหว่างเดินกลับจุดเกิด (Returning) ให้ยกเลิกการเดินกลับและหันกลับมาสู้ต่อทันที
        if (State == NpcState.Returning)
        {
            if (!IsBoss) return;
            SetTarget(attacker);
            ChangeState(NpcState.Chasing, "boss re-engaged while returning");
            return;
        }

        if (!TargetId.IsValid) SetTarget(attacker);
        if (State == NpcState.Idle || State == NpcState.Patrol)
        {
            ChangeState(NpcState.Chasing, "attacked");
            BroadcastCampAggro(attacker);
        }
    }

    /// <summary>
    /// ค้นหาผู้เล่นเจ้าของ Source PlayerRef อย่างปลอดภัย (ป้องกันการปัดสิทธิ์ข้ามไปยัง Local Player หากผู้เล่นเดิมหลุดออก)
    /// </summary>
    private PlayerCombat FindPlayerBySource(PlayerRef source)
    {
        for (int i = 0; i < PlayerCombat.All.Count; i++)
        {
            var p = PlayerCombat.All[i];
            if (p != null && p.IsSpawned && (p.Object.StateAuthority == source || p.Object.InputAuthority == source))
                return p;
        }
        return (source == PlayerRef.None || (Runner != null && source == Runner.LocalPlayer)) ? PlayerCombat.Local : null;
    }

    /// <summary>
    /// รับคำสั่งชะงัก/เสียหลัก (Stagger) จากผู้เล่นที่ทำ Perfect Parry สำเร็จ (ส่งข้ามเครื่องไปยัง State Authority ของศัตรู)
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_ApplyParryStagger(float durationSeconds)
    {
        if (!IsSpawned || !Object.HasStateAuthority || !IsAlive) return;

        float dur = Mathf.Clamp(durationSeconds, 0.25f, 3.0f);
        if (_bossAttackRoutine != null)
        {
            StopCoroutine(_bossAttackRoutine);
            _bossAttackRoutine = null;
        }
        _isTelegraphing = false;
        StopNavAgent();

        _staggerEndTime = Time.time + dur;
        _nextAttackTime = Mathf.Max(_nextAttackTime, _staggerEndTime + 0.35f);
        ChangeState(NpcState.Staggered, $"parried ({dur:0.00}s)");
        RPC_PlayStaggerFx(dur);
    }

    /// <summary>
    /// ซิงก์อนิเมชันและป้ายแจ้งเตือนเมื่อศัตรูติดสถานะ Stagger บนทุกเครื่อง
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_PlayStaggerFx(float durationSeconds)
    {
        if (animator != null)
        {
            if (_hasStaggerParam) animator.SetTrigger(StaggerTrigger);
            if (_hasIsStaggeredParam) animator.SetBool(IsStaggeredParam, true);
        }
        Vector3 popupPos = LockPoint != null ? LockPoint.position + Vector3.up * 0.75f : transform.position + Vector3.up * 2.1f;
        FloatingDamagePopup.SpawnWarning(popupPos, "STAGGERED!", new Color(1f, 0.84f, 0.18f, 1f));
        Staggered?.Invoke(this, durationSeconds);
    }

    private void Die(PlayerRef killer)
    {
        TargetId = default;
        if (_bossAttackRoutine != null) { StopCoroutine(_bossAttackRoutine); _bossAttackRoutine = null; }
        _isTelegraphing = false;
        if (_navAgent != null) _navAgent.enabled = false;
        _deathTime = Time.time;
        ChangeState(NpcState.Dead, $"killed by {killer}");

        // 1. ส่ง Targeted RPC สร้างไอเทมดรอปส่วนตัวแบบ Client-Local เฉพาะผู้ที่มีส่วนร่วมทำดาเมจ (damage > 0)
        SpawnPersonalInstancedLoot(killer);

        // 2. ตรวจสอบฝั่ง State Authority ว่าการตายครั้งนี้ทำให้แคมป์หรือบอสถูกเคลียร์สมบูรณ์หรือไม่ เพื่อซิงก์สิทธิ์หีบสมบัติให้ทุกเครื่อง
        NotifyCampClearIfLastSurvivor();
    }

    /// <summary>
    /// ตรวจสอบว่าเป็นมอนสเตอร์ตัวสุดท้ายของแคมป์/บอสหรือไม่ หากใช่จะส่งรายชื่อผู้มีส่วนร่วมทำดาเมจทั้งหมดไปยังทุกเครื่องเพื่อสร้างหีบสมบัติแบบล็อกสิทธิ์
    /// </summary>
    private void NotifyCampClearIfLastSurvivor()
    {
        if (!Object.HasStateAuthority || SandboxWorldSpawner.Instance == null) return;

        for (int i = 0; i < All.Count; i++)
        {
            EnemyNPC other = All[i];
            if (other == null || other == this || !other.IsSpawned || !other.IsAlive) continue;
            if (IsBoss)
            {
                if (other.IsBoss || other.CampId == SandboxWorldSpawner.BossCampId) return;
            }
            else if (!other.IsBoss && other.CampId == CampId)
            {
                return;
            }
        }

        string payload = SandboxWorldSpawner.Instance.BuildCampContributorPayload(CampId, IsBoss);
        Vector3 chestPos = IsBoss
            ? SandboxWorldSpawner.Instance.BossArenaCenter + new Vector3(0f, 0f, 2.2f)
            : SandboxWorldSpawner.Instance.GetCampCenter(CampId);

        RPC_NotifyCampCleared(CampId, IsBoss, chestPos, payload);
    }

    /// <summary>
    /// ส่ง Targeted RPC (Rpc_SpawnLocalLoot) ไปยังผู้เล่นแต่ละคนที่มีส่วนร่วมทำดาเมจ (damage > 0) เท่านั้น
    /// เพื่อให้แต่ละเครื่องสร้างไอเทมตกแบบ Client-Local บนจอตัวเอง 100% (ไม่มี Ghost Object บนจอคนอื่น)
    /// </summary>
    private void SpawnPersonalInstancedLoot(PlayerRef fallbackKiller)
    {
        LootDropSystem.AnalyzeDamageContribution(
            _damageDealtMap,
            fallbackKiller,
            out float totalDamage,
            out PlayerRef topDpsPlayer,
            out List<PlayerRef> participants);

        if (Runner == null || !Runner.IsRunning) return;

        for (int i = 0; i < participants.Count; i++)
        {
            PlayerRef participant = participants[i];
            if (participant == PlayerRef.None) continue;

            float dealt = _damageDealtMap.TryGetValue(participant, out float d) ? d : totalDamage;
            if (dealt <= 0f) continue;

            float share = totalDamage > 0f ? Mathf.Clamp01(dealt / totalDamage) : 1f;
            bool isTopDps = participant == topDpsPlayer;

            // ตรวจสอบโอกาสดรอปไอเทมสำหรับมอนสเตอร์ทั่วไป (~26%–34% ตามสัดส่วนดาเมจ ส่วนบอสการันตีดรอปเสมอ)
            // แม้ไม่ดรอปไอเทม ผู้เล่นทุกคนที่ร่วมสู้ยังคงได้รับ EXP ตามปกติ
            ItemData rolledItem = null;
            ItemTier rolledTier = ItemTier.Normal;
            LootDropSystem.WeightedTierOdds odds = default;
            bool shouldDropItem = IsBoss || LootDropSystem.RollNormalEnemyShouldDrop(share, isTopDps);

            if (shouldDropItem)
            {
                rolledItem = LootDropSystem.RollDamageWeightedLoot(
                    IsBoss,
                    share,
                    isTopDps,
                    out rolledTier,
                    out odds,
                    isChest: false);
            }

            Vector2 offset2D = UnityEngine.Random.insideUnitCircle * 1.15f;
            Vector3 dropPos = new Vector3(transform.position.x + offset2D.x, 0.4f, transform.position.z + offset2D.y);
            int expShare = Mathf.Max(10, Mathf.RoundToInt(experienceReward * Mathf.Lerp(0.6f, 1.0f, share)));
            string itemId = rolledItem != null ? rolledItem.itemId : "";

            // ส่ง Targeted RPC เฉพาะเจาะจงไปยัง PlayerRef เจ้าของสิทธิ์เท่านั้น
            Rpc_SpawnLocalLoot(participant, itemId, 1, 0, dropPos, expShare, share, isTopDps);

            if (rolledItem != null)
            {
                Debug.Log($"[PersonalLoot] Sent targeted client-local drop [{ItemData.GetTierDisplayName(rolledTier)}] '{rolledItem.displayName}' to {participant} (Share={share * 100f:0}%, TopDPS={isTopDps}, EpicOdds={odds.epicChance * 100f:0.0}%, LegOdds={odds.legendaryChance * 100f:0.0}%).");
            }
            else
            {
                Debug.Log($"[PersonalLoot] Normal enemy no-item roll (DropRate={LootDropSystem.GetNormalEnemyDropChance(share, isTopDps) * 100f:0}%) -> Granted +{expShare} EXP only to {participant} (Share={share * 100f:0}%).");
            }
        }
    }

    // ------------------------------------------------------------------ Network RPCs สำหรับ Personal Loot และ Camp Clear

    /// <summary>
    /// Targeted RPC ส่งตรงเฉพาะผู้เล่นที่มีส่วนร่วมทำดาเมจเท่านั้น เพื่อสร้าง LocalPersonalLoot แบบ Client-Local และมอบ EXP
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void Rpc_SpawnLocalLoot(
        [RpcTarget] PlayerRef targetPlayer,
        NetworkString<_32> itemId,
        int quantity,
        int enhancementLevel,
        Vector3 dropPos,
        int expAmount,
        float damageShare,
        NetworkBool isTopDps)
    {
        if (Runner == null) return;
        if (targetPlayer != PlayerRef.None && Runner.LocalPlayer != targetPlayer) return;

        string idStr = itemId.ToString();
        ItemData itemData = !string.IsNullOrEmpty(idStr) ? ItemRegistry.Get(idStr) : null;
        if (itemData != null)
        {
            string sourceTag = IsBoss ? "Boss Personal Loot" : "Personal Loot";
            LocalPersonalLoot.SpawnLocal(dropPos, itemData, Mathf.Max(1, quantity), Mathf.Max(0, enhancementLevel), LocalPersonalLoot.DefaultLifetimeSeconds, sourceTag);
        }

        if (expAmount > 0)
        {
            if (InventoryManager.Local != null) InventoryManager.Local.AddExperience(expAmount);
            else if (PlayerCombat.Local != null) PlayerCombat.Local.AddExperience(expAmount);
        }

        string rankTag = isTopDps ? "TOP DPS ★" : $"{damageShare * 100f:0}% DMG";
        if (itemData != null)
        {
            PlayerStatusHud.Instance?.ShowToast($"{rankTag} Personal Drop: {itemData.displayName} (+{expAmount} EXP)",
                isTopDps ? new Color(1f, 0.84f, 0.22f) : new Color(0.45f, 0.90f, 1f), 3.0f);
        }
        else if (expAmount > 0)
        {
            PlayerStatusHud.Instance?.ShowToast($"{rankTag} +{expAmount} EXP", new Color(0.45f, 0.90f, 1f), 2.5f);
        }
    }

    /// <summary>
    /// กระจายผลสรุปการเคลียร์แคมป์/บอสพร้อมรายชื่อผู้ร่วมทำดาเมจจาก State Authority ไปยังทุกเครื่องเพื่อสร้างและล็อกสิทธิ์ LootChest
    /// ใช้ NetworkString&lt;_64&gt; เพื่อให้ขนาด Payload รวมต่ำกว่าขีดจำกัด 512 ไบต์ของ Photon Fusion 2 RPC เสมอ
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_NotifyCampCleared(int clearedCampId, NetworkBool isBossCamp, Vector3 chestPos, NetworkString<_64> contributorPayload)
    {
        SandboxWorldSpawner.Instance?.HandleAuthoritativeCampCleared(clearedCampId, isBossCamp, chestPos, contributorPayload.ToString());
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_HitFeedback(int damage, NetworkBool isCrit, int comboStep, PlayerRef attacker)
    {
        if (animator != null && _hasHitParam) animator.SetTrigger(HitTrigger);
        onHit?.Invoke();
        Damaged?.Invoke(this, damage, isCrit);
        if (!_flashing && gameObject.activeInHierarchy) StartCoroutine(FlashRoutine());

        FloatingDamagePopup.Spawn(LockPoint != null ? LockPoint.position : transform.position + Vector3.up * 1.4f, damage, isCrit, comboStep);
        CombatHud.NotifyEnemyDamaged(this, damage, attacker, isCrit, comboStep);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_PlayAttackFx()
    {
        if (animator != null && _hasAttackParam) animator.SetTrigger(AttackTrigger);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_BossTelegraph(byte patternByte, Vector3 centerPos, Vector3 dir, float duration)
    {
        if (!gameObject.activeInHierarchy) return;
        BossAttackPattern pattern = (BossAttackPattern)patternByte;
        if (animator != null)
        {
            // สั่งเล่นอนิเมชันเฉพาะของแต่ละท่าบอสใน BossGolemAnimator (ถ้ามี) หรือ Fallback ไป Trigger ทั่วไป
            if (pattern == BossAttackPattern.HeavyCleave && _hasHeavyCleaveParam)
                animator.SetTrigger(HeavyCleaveTrigger);
            else if (pattern == BossAttackPattern.ShockwaveSpinAoE && _hasShockwaveSpinParam)
                animator.SetTrigger(ShockwaveSpinTrigger);
            else if (pattern == BossAttackPattern.DarkVolleyAndCharge && _hasDarkVolleyParam)
                animator.SetTrigger(DarkVolleyTrigger);
            else if (_hasTelegraphParam)
                animator.SetTrigger(TelegraphTrigger);
            else if (_hasAttackParam)
                animator.SetTrigger(AttackTrigger);
        }
        StartCoroutine(TelegraphVisualRoutine(pattern, centerPos, dir, duration));
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_PlayBossShockwaveRelease(Vector3 centerPos, float maxRadius)
    {
        if (!gameObject.activeInHierarchy) return;
        StartCoroutine(BossShockwaveReleaseRoutine(centerPos, maxRadius));
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_SpawnBossVolley(Vector3 origin, Vector3 aimDir, int damagePerOrb)
    {
        float[] spreadAngles = { -20f, 0f, 20f };
        for (int i = 0; i < spreadAngles.Length; i++)
        {
            Vector3 shotDir = Quaternion.Euler(0f, spreadAngles[i], 0f) * aimDir;
            BossDarkProjectile.Spawn(this, origin, shotDir, damagePerOrb);
        }
    }

    private IEnumerator TelegraphVisualRoutine(BossAttackPattern pattern, Vector3 centerPos, Vector3 dir, float duration)
    {
        string castName = pattern switch
        {
            BossAttackPattern.HeavyCleave => "Heavy Cleave",
            BossAttackPattern.ShockwaveSpinAoE => "360° Shockwave",
            _ => "Dark Volley"
        };
        ActiveTelegraphName = castName;
        ActiveTelegraphDuration = Mathf.Max(0.05f, duration);
        ActiveTelegraphEndTime = Time.time + ActiveTelegraphDuration;

        string warnText = pattern switch
        {
            BossAttackPattern.HeavyCleave => "! HEAVY CLEAVE !",
            BossAttackPattern.ShockwaveSpinAoE => "! 360° SHOCKWAVE !",
            _ => "! DARK VOLLEY !"
        };
        FloatingDamagePopup.SpawnWarning(transform.position + Vector3.up * 3.1f, warnText, new Color(1f, 0.32f, 0.15f, 1f));

        var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disc.name = "BossTelegraphRing";
        var col = disc.GetComponent<Collider>();
        if (col != null) Destroy(col);

        float diameter = pattern == BossAttackPattern.ShockwaveSpinAoE ? 12.4f : 5.2f;
        Vector3 targetPos = pattern == BossAttackPattern.HeavyCleave
            ? centerPos + dir * 2.1f
            : centerPos;
        targetPos.y = 0.06f;
        disc.transform.position = targetPos;
        disc.transform.localScale = new Vector3(0.5f, 0.02f, 0.5f);

        // ใช้ URP Unlit Transparent ถาวรสำหรับวงแจ้งเตือนท่าบอส (ป้องกันสีชมพูใน Standalone Build)
        UrpMaterialHelper.ApplyTransparentColor(disc.GetComponent<Renderer>(), new Color(1f, 0.16f, 0.08f, 0.55f));

        float t = 0f;
        while (t < duration && disc != null)
        {
            t += Time.deltaTime;
            float s = Mathf.Lerp(1.0f, diameter, Mathf.Clamp01(t / Mathf.Max(0.05f, duration)));
            disc.transform.localScale = new Vector3(s, 0.02f, s);
            yield return null;
        }
        if (disc != null) Destroy(disc);
    }

    private IEnumerator BossShockwaveReleaseRoutine(Vector3 centerPos, float maxRadius)
    {
        var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "BossShockwaveBurst";
        var col = ring.GetComponent<Collider>();
        if (col != null) Destroy(col);
        ring.transform.position = new Vector3(centerPos.x, 0.10f, centerPos.z);
        UrpMaterialHelper.ApplyTransparentColor(ring.GetComponent<Renderer>(), new Color(1f, 0.48f, 0.10f, 0.72f));

        float elapsed = 0f;
        while (elapsed < 0.30f && ring != null)
        {
            elapsed += Time.deltaTime;
            float s = Mathf.Lerp(1.5f, maxRadius * 2f, elapsed / 0.30f);
            ring.transform.localScale = new Vector3(s, 0.04f, s);
            yield return null;
        }
        if (ring != null) Destroy(ring);
    }

    private IEnumerator FlashRoutine()
    {
        _flashing = true;
        SetColor(Color.white);
        yield return new WaitForSeconds(0.1f);
        _flashing = false;
        if (hasRiggedModel && bodyRenderer != null)
        {
            // คืนค่า Material URP เดิมของโมเดล 3D Golem โดยไม่ทาสีทับพื้นผิว
            bodyRenderer.SetPropertyBlock(null);
        }
        else
        {
            RefreshVisuals();
        }
    }

    private void OnHealthChanged() { }

    private void OnStateChanged()
    {
        if (!IsSpawned) return;
        if (State == NpcState.Dead && !_deathAnimPlayed)
        {
            _deathAnimPlayed = true;
            if (animator != null)
            {
                if (_hasIsDeadParam) animator.SetBool(IsDeadParam, true);
                if (_hasDieParam) animator.SetTrigger(DieTrigger);
            }
            onDeath?.Invoke();
            Died?.Invoke(this);
        }
        if (animator != null && _hasIsStaggeredParam)
        {
            animator.SetBool(IsStaggeredParam, State == NpcState.Staggered);
        }
        RefreshVisuals();
    }

    private void RefreshVisuals()
    {
        bool dead = State == NpcState.Dead;
        if (BodyCollider != null) BodyCollider.enabled = !dead;

        // สำหรับโมเดล 3D จริง (เช่น Boss Golem) ให้รักษาสีและ Texture URP เดิมเอาไว้
        if (hasRiggedModel) return;

        if (IsBoss)
        {
            SetColor(dead ? new Color(0.22f, 0.18f, 0.20f)
                : State == NpcState.Staggered ? new Color(1.0f, 0.85f, 0.18f)
                : State == NpcState.Attacking ? new Color(1.0f, 0.15f, 0.08f)
                : State == NpcState.Chasing ? new Color(0.88f, 0.12f, 0.22f)
                : new Color(0.62f, 0.08f, 0.18f));
        }
        else
        {
            SetColor(dead ? new Color(0.35f, 0.35f, 0.35f)
                : State == NpcState.Staggered ? new Color(1.0f, 0.85f, 0.18f)
                : State == NpcState.Attacking ? new Color(0.95f, 0.2f, 0.2f)
                : State == NpcState.Chasing ? new Color(0.95f, 0.55f, 0.15f)
                : State == NpcState.Returning ? new Color(0.4f, 0.7f, 0.95f)
                : new Color(0.85f, 0.3f, 0.3f));
        }
    }

    private void SetColor(Color c)
    {
        if (bodyRenderer == null) return;
        UrpMaterialHelper.EnsureValidUrpMaterial(bodyRenderer);
        bodyRenderer.GetPropertyBlock(_mpb);
        _mpb.SetColor(BaseColorId, c);
        _mpb.SetColor(ColorId, c);
        bodyRenderer.SetPropertyBlock(_mpb);
    }

    // ------------------------------------------------------------------ หลอดเลือดลอยเหนือหัวมอนสเตอร์และป้ายลอบสังหาร

    private void OnGUI()
    {
        // ซ่อนหลอดเลือดมอนสเตอร์เมื่อผู้เล่นเปิดหน้าต่างเมนู (Inventory / World Map / Blacksmith) เพื่อไม่ให้วาดทับ UI
        if (!IsAlive || GameplayInput.IsBlocked || Event.current.type != EventType.Repaint) return;
        var cam = Camera.main;
        if (cam == null) return;

        float topOffset = hasRiggedModel ? riggedOverheadBarHeight : (IsBoss ? 2.8f : 1.5f);
        Vector3 sp = cam.WorldToScreenPoint(transform.position + Vector3.up * topOffset);
        if (sp.z <= 0f || sp.z > (IsBoss ? 48f : 32f)) return;

        // สำหรับบอส: เมื่อ Top-Center BossHudUI แสดงผลอยู่แล้ว ไม่ต้องวาดหลอดเลือดลอยซ้อนบนหัวบอส (แสดงเฉพาะป้าย Staggered ถ้าติด Stagger)
        if (IsBoss && BossHudUI.IsDisplayingBoss(this))
        {
            if (State == NpcState.Staggered)
            {
                var prevCol = GUI.color;
                float sy = Screen.height - sp.y;
                var stagStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    wordWrap = false
                };
                GUI.color = new Color(0.14f, 0.08f, 0.0f, 0.85f);
                Rect stagBox = new Rect(sp.x - 122f, sy - 16f, 244f, 22f);
                GUI.DrawTexture(stagBox, Texture2D.whiteTexture);
                GUI.color = new Color(1f, 0.88f, 0.20f, 1f);
                GUI.Label(stagBox, "STAGGERED (COUNTER CRIT 1.75x)", stagStyle);
                GUI.color = prevCol;
            }
            return;
        }

        // สำหรับมอนสเตอร์ทั่วไปที่มี World-Space Health Bar แล้ว ให้ข้ามการวาดหลอดเลือด OnGUI เดิม (แสดงเฉพาะป้าย Staggered / Stealth Assassinate)
        if (!IsBoss && _worldHealthBar != null && _worldHealthBar.IsBarBuilt)
        {
            var prevCol = GUI.color;
            float sy = Screen.height - sp.y;
            var promptStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                wordWrap = false
            };

            if (State == NpcState.Staggered)
            {
                GUI.color = new Color(0.14f, 0.08f, 0.0f, 0.85f);
                Rect stagRect = new Rect(sp.x - 110f, sy - 42f, 220f, 20f);
                GUI.DrawTexture(stagRect, Texture2D.whiteTexture);
                GUI.color = new Color(1f, 0.88f, 0.20f, 1f);
                GUI.Label(stagRect, "★ STAGGERED (COUNTER CRIT 1.75x)", promptStyle);
            }

            var localPlayer = PlayerCombat.Local;
            if (localPlayer != null && localPlayer.IsSpawned && !localPlayer.IsDead && CanBeAssassinatedBy(localPlayer.transform))
            {
                GUI.color = new Color(0.08f, 0.02f, 0.04f, 0.88f);
                Rect backstabRect = new Rect(sp.x - 124f, sy - 64f, 248f, 22f);
                GUI.DrawTexture(backstabRect, Texture2D.whiteTexture);
                GUI.color = new Color(1f, 0.30f, 0.22f, 1f);
                GUI.Label(backstabRect, "[LMB] STEALTH BACKSTAB (2.5x CRIT)", promptStyle);
            }

            GUI.color = prevCol;
            return;
        }

        int displayMaxHp = Mathf.Max(1, ScaledMaxHealth);
        float w = IsBoss ? 168f : 72f;
        float h = IsBoss ? 10f : 7f;
        float x = sp.x - w * 0.5f;
        float y = Screen.height - sp.y - h * 0.5f;
        float frac = Mathf.Clamp01(CurrentHealth / (float)displayMaxHp);

        var prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.75f);
        GUI.DrawTexture(new Rect(x - 1f, y - 1f, w + 2f, h + 2f), Texture2D.whiteTexture);
        GUI.color = IsBoss
            ? Color.Lerp(new Color(0.95f, 0.15f, 0.12f), new Color(1.0f, 0.65f, 0.12f), frac)
            : Color.Lerp(new Color(0.9f, 0.2f, 0.2f), new Color(0.2f, 0.85f, 0.3f), frac);
        GUI.DrawTexture(new Rect(x, y, w * frac, h), Texture2D.whiteTexture);

        var labelStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = IsBoss ? 12 : 10,
            fontStyle = FontStyle.Bold,
            wordWrap = false
        };
        string title = IsBoss ? $"Lv.{ScaledLevel} Sovereign Golem" : $"Lv.{ScaledLevel}";
        float titleW = IsBoss ? 220f : 120f;
        Rect titleRect = new Rect(sp.x - titleW * 0.5f, y - 22f, titleW, 20f);
        if (IsBoss)
        {
            GUI.color = new Color(0.05f, 0.04f, 0.06f, 0.72f);
            GUI.DrawTexture(titleRect, Texture2D.whiteTexture);
        }
        GUI.color = IsBoss ? new Color(1f, 0.88f, 0.28f) : new Color(0.92f, 0.94f, 0.98f);
        GUI.Label(titleRect, title, labelStyle);

        // แสดงป้ายแจ้งเตือนสถานะ Stagger เมื่อถูก Perfect Parry
        if (State == NpcState.Staggered)
        {
            GUI.color = new Color(0.14f, 0.08f, 0.0f, 0.85f);
            Rect stagRect = new Rect(sp.x - 110f, y - 39f, 220f, 20f);
            GUI.DrawTexture(stagRect, Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.88f, 0.20f, 1f);
            GUI.Label(stagRect, "★ STAGGERED (COUNTER CRIT 1.75x)", labelStyle);
        }

        // แสดงป้ายแจ้งเตือนเมื่อผู้เล่นย่องเข้าด้านหลังในระยะลอบสังหาร (Stealth Assassination Prompt)
        var local = PlayerCombat.Local;
        if (local != null && local.IsSpawned && !local.IsDead && CanBeAssassinatedBy(local.transform))
        {
            GUI.color = new Color(0.08f, 0.02f, 0.04f, 0.88f);
            Rect backstabRect = new Rect(sp.x - 124f, y - 42f, 248f, 22f);
            GUI.DrawTexture(backstabRect, Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.30f, 0.22f, 1f);
            GUI.Label(backstabRect, "[LMB] STEALTH BACKSTAB (2.5x CRIT)", labelStyle);
        }

        GUI.color = prev;
    }

    /// <summary>
    /// วาดเส้นขอบเขตโซนลาดตระเวนส่วนตัว (สีฟ้า), ระยะตรวจจับ (สีเหลือง), และระยะ Leash กลับบ้าน (สีแดง) ในหน้าต่าง Scene View
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        Vector3 anchor = PersonalZoneAnchor;
        Gizmos.color = new Color(0.20f, 0.85f, 1.0f, 0.75f);
        Gizmos.DrawWireSphere(anchor, PatrolRadius);
        Gizmos.color = new Color(1.0f, 0.85f, 0.20f, 0.45f);
        Gizmos.DrawWireSphere(transform.position, aggroRadius);
        Gizmos.color = new Color(1.0f, 0.25f, 0.20f, 0.25f);
        Gizmos.DrawWireSphere(anchor, leashRadius);
    }
}

/// <summary>
/// ลูกไฟพลังงานมืดวิถีโค้งของบอสในท่าที่ 3 (Dark Volley &amp; Charge)
/// </summary>
public sealed class BossDarkProjectile : MonoBehaviour
{
    private EnemyNPC _owner;
    private Vector3 _dir;
    private int _damage;
    private float _speed = 11.5f;
    private float _expireAt;

    public static void Spawn(EnemyNPC owner, Vector3 origin, Vector3 direction, int damage)
    {
        var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        orb.name = "BossDarkVolleyOrb";
        var col = orb.GetComponent<Collider>();
        if (col != null) Destroy(col);
        orb.transform.position = origin;
        orb.transform.localScale = new Vector3(0.58f, 0.58f, 0.58f);

        // ใช้ URP Unlit ถาวรสำหรับลูกไฟพลังงานมืดของบอส (ไม่เป็นสีชมพูใน Standalone Build)
        UrpMaterialHelper.ApplyUnlitColor(orb.GetComponent<Renderer>(), new Color(0.75f, 0.14f, 0.95f, 1f));

        var proj = orb.AddComponent<BossDarkProjectile>();
        proj._owner = owner;
        proj._dir = direction.normalized;
        proj._damage = Mathf.Max(1, damage);
        proj._expireAt = Time.time + 2.2f;
    }

    private void Update()
    {
        if (Time.time >= _expireAt)
        {
            Destroy(gameObject);
            return;
        }

        transform.position += _dir * (_speed * Time.deltaTime);

        if (SafeZone.IsPointInAnySafeZone(transform.position))
        {
            Destroy(gameObject);
            return;
        }

        var local = PlayerCombat.Local;
        if (local != null && local.IsSpawned && !local.IsDead && !SafeZone.IsPlayerInAnySafeZone(local))
        {
            Vector3 diff = local.transform.position - transform.position;
            diff.y = 0f;
            if (diff.sqrMagnitude <= 1.15f * 1.15f)
            {
                if (_owner != null && _owner.IsSpawned && _owner.Object != null && _owner.Object.IsValid)
                {
                    local.RPC_ReceiveNpcDamage(_owner.Object.Id, _damage);
                }
                Destroy(gameObject);
            }
        }
    }
}
