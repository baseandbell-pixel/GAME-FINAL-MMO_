using System;
using System.Collections;
using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

// สถานะพฤติกรรม AI ของมอนสเตอร์และบอส
public enum NpcState : byte
{
    Idle,      // ยืนรอก่อนเดินลาดตระเวนรอบถัดไป
    Patrol,    // เดินลาดตระเวนอัตโนมัติในอาณาเขตแคมป์
    Chasing,   // ไล่ล่าผู้เล่นที่ตรวจพบ
    Attacking, // โจมตีผู้เล่นในระยะ
    Returning, // วิ่งกลับจุดเกิดเมื่อผู้เล่นออกนอกระยะหรือเข้า Safe Zone
    Dead       // ตายและรอเวลาสลายตัว
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
public class EnemyNPC : NetworkBehaviour, ILockTarget
{
    // รายการมอนสเตอร์และบอสทั้งหมดที่เกิดอยู่ในฉาก
    public static readonly List<EnemyNPC> All = new List<EnemyNPC>();

    [Header("Base Combat Stats (ค่าพลังพื้นฐานที่เลเวล 1)")]
    public int MaxHealth = 120;
    public int AttackDamage = 8;
    public int Defense = 0;
    public float AttackCooldown = 2.1f;
    public float AttackRange = 2.4f;

    [Header("AI & Territory Patrol (ระบบเดินลาดตระเวนและมุมมองสายตา)")]
    [Min(1f)] [SerializeField] private float aggroRadius = 6.8f;
    [Min(2f)] [SerializeField] private float leashRadius = 22f;
    // รัศมีเดินลาดตระเวนในอาณาเขตแคมป์ (เพิ่มเป็น 5.5 เมตรให้เดินทั่วแคมป์อย่างมีชีวิตชีวา)
    [SerializeField] private float patrolRadius = 5.5f;
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

    // ---- ข้อมูลที่ซิงก์ผ่าน Network (Networked State) ----
    [Networked, OnChangedRender(nameof(OnHealthChanged))]
    public int CurrentHealth { get; set; } = 120;

    [Networked, OnChangedRender(nameof(OnStateChanged))]
    public NpcState State { get; set; }

    [Networked] public NetworkId TargetId { get; set; }
    [Networked] public Vector3 HomePosition { get; set; }
    [Networked] public int CampId { get; set; }
    [Networked, OnChangedRender(nameof(OnBossFlagChanged))]
    public NetworkBool IsBoss { get; set; }
    [Networked] public int ScaledLevel { get; set; } = 1;

    // ---- คุณสมบัติเปิดอ่านจากภายนอก (Public API) ----
    public bool IsSpawned { get; private set; }
    public bool IsAlive => IsSpawned && Object != null && Object.IsValid && State != NpcState.Dead && CurrentHealth > 0;
    // ตรวจสอบว่ามอนสเตอร์รู้ตัวและกำลังต่อสู้อยู่หรือไม่ (ใช้เช็คเงื่อนไขลอบสังหาร)
    public bool IsAlerted => State == NpcState.Chasing || State == NpcState.Attacking;
    public Collider BodyCollider { get; private set; }
    public float AggroRadius => aggroRadius;
    public float ChaseSpeed => chaseSpeed;
    public float PatrolSpeed => patrolSpeed;
    public float PatrolRadius => patrolRadius;
    public Vector3 CurrentPatrolWaypoint => _patrolPoint;
    public int ScaledMaxHealth => MaxHealth;
    public int ScaledAttackDamage => AttackDamage;
    public int ScaledDefense => Defense;
    public int ExperienceReward => experienceReward;
    public int CurrentAttackDamage => AttackDamage;

    public BossAttackPattern LastBossPattern { get; private set; }
    public int BossAttackExecutions { get; private set; }
    public int DistinctBossPatternsUsed => _usedPatterns.Count;
    public bool IsTelegraphing => _isTelegraphing;

    // ตารางเก็บสถิติดาเมจสะสมของผู้เล่นแต่ละคน (ใช้คำนวณ Personal Loot & Damage-Weighted Drop Rate)
    public IReadOnlyDictionary<PlayerRef, float> DamageDealtMap => _damageDealtMap;

    public Transform LockPoint => _lockPoint != null ? _lockPoint : transform;
    public bool IsLockable => IsAlive;
    public string LockName => IsBoss ? $"[Lv.{ScaledLevel} BOSS] Sovereign Golem" : $"[Lv.{ScaledLevel}] {name}";

    // ---- ตัวแปรภายใน (Private State) ----
    private Transform _lockPoint;
    private GameObject _bossCrownRoot;
    private MaterialPropertyBlock _mpb;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int HitTrigger = Animator.StringToHash("Hit");
    private static readonly int AttackTrigger = Animator.StringToHash("Attack");
    private static readonly int DieTrigger = Animator.StringToHash("Die");

    private float _nextAttackTime;
    private float _stateTimer;
    private float _patrolMoveElapsed;
    private float _deathTime;
    private bool _hasPatrolPoint;
    private Vector3 _patrolPoint;
    private bool _flashing;
    private bool _bossConfigured;
    private bool _scalingApplied;
    private bool _isTelegraphing;
    private int _bossPatternCycle;
    private int _avoidSign;
    private float _avoidSignUntil;
    private Coroutine _bossAttackRoutine;
    private readonly HashSet<BossAttackPattern> _usedPatterns = new HashSet<BossAttackPattern>();
    private static readonly Collider[] s_overlapBuffer = new Collider[24];
    private static readonly RaycastHit[] s_castBuffer = new RaycastHit[16];
    private readonly Dictionary<PlayerRef, float> _lastDamageTime = new Dictionary<PlayerRef, float>();

    // ตารางบันทึกดาเมจที่ผู้เล่นแต่ละคนทำได้ในการต่อสู้กับมอนสเตอร์/บอสตัวนี้
    private readonly Dictionary<PlayerRef, float> _damageDealtMap = new Dictionary<PlayerRef, float>();

    // ------------------------------------------------------------------ วงจรการทำงาน (Lifecycle)

    private void Awake()
    {
        BodyCollider = GetComponent<Collider>();
        if (bodyRenderer == null) bodyRenderer = GetComponentInChildren<Renderer>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        _mpb = new MaterialPropertyBlock();

        var lp = new GameObject("LockPoint").transform;
        lp.SetParent(transform, false);
        lp.localPosition = new Vector3(0f, 0.6f, 0f);
        _lockPoint = lp;
    }

    public override void Spawned()
    {
        IsSpawned = true;
        if (!All.Contains(this)) All.Add(this);
        _damageDealtMap.Clear();

        if (Object.HasStateAuthority)
        {
            if (ScaledLevel < 1) ScaledLevel = DetermineTargetScaleLevel();
            ApplyDynamicScaling(ScaledLevel);
            CurrentHealth = MaxHealth;
            HomePosition = transform.position;
            TargetId = default;

            // ให้มอนสเตอร์แต่ละตัวเริ่มเดินลาดตระเวนทันทีแบบเหลื่อมเวลากันเล็กน้อย เพื่อไม่ให้ยืนนิ่งเป็นหุ่นปั้น
            _stateTimer = UnityEngine.Random.Range(0.15f, 0.85f);
            State = NpcState.Idle;
            Debug.Log($"[EnemyNPC] {name} Lv.{ScaledLevel} (Boss={IsBoss}, Camp={CampId}) spawned. HP={CurrentHealth}/{MaxHealth}, ATK={AttackDamage}, DEF={Defense}.");
        }
        else
        {
            ApplyDynamicScaling(Mathf.Max(1, ScaledLevel));
        }

        RefreshVisuals();
    }

    private static int DetermineTargetScaleLevel()
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

    private void OnBossFlagChanged()
    {
        if (IsSpawned && IsBoss)
        {
            ApplyBossConfiguration();
            RefreshVisuals();
        }
    }

    /// <summary>
    /// ปรับสเกลค่าพลังของมอนสเตอร์และบอสตามเลเวลของผู้เล่นอัตโนมัติ
    /// </summary>
    public void ApplyDynamicScaling(int targetLevel)
    {
        targetLevel = Mathf.Max(1, targetLevel);
        if (_scalingApplied && ScaledLevel == targetLevel) return;
        _scalingApplied = true;
        if (Object != null && Object.IsValid && Object.HasStateAuthority)
            ScaledLevel = targetLevel;

        if (IsBoss)
        {
            ApplyBossVisualAndCore();
            MaxHealth = 750 + (targetLevel - 1) * 220;
            AttackDamage = 24 + (targetLevel - 1) * 5;
            Defense = 2 + (targetLevel - 1) * 3;
            experienceReward = 300 + (targetLevel - 1) * 80;
            maxDamagePerHit = 1500;
        }
        else
        {
            MaxHealth = 120 + (targetLevel - 1) * 45;
            AttackDamage = 8 + (targetLevel - 1) * 2;
            Defense = (targetLevel - 1) * 2;
            experienceReward = 50 + (targetLevel - 1) * 18;
            maxDamagePerHit = 900;
        }

        if (IsSpawned && State != NpcState.Dead && CurrentHealth > 0)
            CurrentHealth = MaxHealth;
    }

    public void ApplyBossConfiguration()
    {
        ApplyBossVisualAndCore();
        ApplyDynamicScaling(Mathf.Max(1, ScaledLevel));
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

        transform.localScale = new Vector3(2.2f, 2.3f, 2.2f);
        if (_lockPoint != null) _lockPoint.localPosition = new Vector3(0f, 0.85f, 0f);

        if (_bossCrownRoot == null)
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
        var r = part.GetComponent<Renderer>();
        if (r != null)
        {
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, color);
            mpb.SetColor(ColorId, color);
            r.SetPropertyBlock(mpb);
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        IsSpawned = false;
        All.Remove(this);
        StopAllCoroutines();
        _bossAttackRoutine = null;
        _isTelegraphing = false;
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
        switch (State)
        {
            case NpcState.Idle: TickIdle(dt); break;
            case NpcState.Patrol: TickPatrol(dt); break;
            case NpcState.Chasing: TickChasing(dt); break;
            case NpcState.Attacking: TickAttacking(dt); break;
            case NpcState.Returning: TickReturning(dt); break;
            case NpcState.Dead: TickDead(); return;
        }

        if (IsSpawned && Object != null && Object.IsValid && State != NpcState.Dead)
        {
            Vector3 pos = transform.position;
            if (ResolveObstaclePenetration(ref pos))
                transform.position = pos;
        }
    }

    private void TickIdle(float dt)
    {
        if (TryAcquireNearestPlayer()) return;

        _stateTimer -= dt;
        if (_stateTimer <= 0f && patrolRadius > 0.5f)
        {
            if (TryPickPatrolWaypoint(out Vector3 waypoint))
            {
                _patrolPoint = waypoint;
                _hasPatrolPoint = true;
                _patrolMoveElapsed = 0f;
                ChangeState(NpcState.Patrol, "territory patrol");
            }
            else
            {
                _stateTimer = 0.75f;
            }
        }
    }

    /// <summary>
    /// สุ่มหาจุดเดินลาดตระเวนในอาณาเขตแคมป์ โดยเว้นระยะห่างจากจุดเดิมและไม่เข้าเขต Safe Zone
    /// </summary>
    private bool TryPickPatrolWaypoint(out Vector3 waypoint)
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            float dist = UnityEngine.Random.Range(patrolRadius * 0.35f, patrolRadius);
            Vector3 candidate = HomePosition + new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);
            candidate.y = transform.position.y;

            if (SafeZone.IsPointInAnySafeZone(candidate, 1.0f)) continue;
            if (PlanarDistance(transform.position, candidate) < 1.2f) continue;

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

        _patrolMoveElapsed += dt;
        float remaining = MoveTowards(_patrolPoint, patrolSpeed, dt);
        // หากเดินถึงจุดหมาย หรือใช้เวลาเดินนานเกิน 4.5 วินาที ให้เปลี่ยนจุดลาดตระเวนใหม่เพื่อไม่ให้ติดกำแพง
        if (remaining <= 0.4f || _patrolMoveElapsed >= 4.5f)
        {
            _hasPatrolPoint = false;
            _stateTimer = UnityEngine.Random.Range(idleWaitRange.x, idleWaitRange.y);
            ChangeState(NpcState.Idle, "reached patrol waypoint");
        }
    }

    private void TickChasing(float dt)
    {
        PlayerCombat target = ResolveTarget();
        if (target == null || target.IsDead) { BeginReturn(target == null ? "target lost" : "target died"); return; }
        if (SafeZone.IsPlayerInAnySafeZone(target)) { BeginReturn("target entered Comfort Safe Zone"); return; }

        if (PlanarDistance(transform.position, HomePosition) > leashRadius) { BeginReturn("leash radius exceeded"); return; }

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
        if (target == null || target.IsDead) { BeginReturn(target == null ? "target lost" : "target died"); return; }
        if (SafeZone.IsPlayerInAnySafeZone(target)) { BeginReturn("target entered Comfort Safe Zone"); return; }

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
        if (MoveTowards(HomePosition, returnSpeed, dt) <= 0.35f)
        {
            CurrentHealth = MaxHealth;
            TargetId = default;
            _damageDealtMap.Clear();
            _stateTimer = UnityEngine.Random.Range(idleWaitRange.x, idleWaitRange.y);
            ChangeState(NpcState.Idle, "arrived home, health & aggro reset");
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

    private void BroadcastCampAggro(PlayerCombat target)
    {
        if (target == null || !target.IsSpawned || CampId <= 0 || IsBoss || SafeZone.IsPlayerInAnySafeZone(target)) return;

        for (int i = 0; i < All.Count; i++)
        {
            EnemyNPC ally = All[i];
            if (ally == null || !ally.IsSpawned || !ally.IsAlive || ally == this || ally.CampId != CampId) continue;
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
        nextPos.y = pos.y;

        transform.position = nextPos;
        FaceDirection(finalDir, dt);
        return PlanarDistance(nextPos, destination);
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
    /// บันทึกดาเมจสะสมของผู้เล่นแต่ละคนลงใน _damageDealtMap เพื่อใช้คำนวณอัตราดรอป Personal Loot ตอนตาย
    /// </summary>
    public void RecordPlayerDamage(PlayerRef player, float amount)
    {
        if (player == PlayerRef.None || amount <= 0f) return;
        if (_damageDealtMap.TryGetValue(player, out float current))
            _damageDealtMap[player] = current + amount;
        else
            _damageDealtMap[player] = amount;
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

        if (State == NpcState.Returning) return;
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

    private void Die(PlayerRef killer)
    {
        TargetId = default;
        if (_bossAttackRoutine != null) { StopCoroutine(_bossAttackRoutine); _bossAttackRoutine = null; }
        _isTelegraphing = false;
        _deathTime = Time.time;
        ChangeState(NpcState.Dead, $"killed by {killer}");

        // คำนวณส่วนแบ่งดาเมจและดรอปไอเทมส่วนตัว (Personal Instanced Loot) ให้ผู้เล่นทุกคนที่มีส่วนร่วม
        SpawnPersonalInstancedLoot(killer);
    }

    /// <summary>
    /// สร้างไอเทมดรอปส่วนตัว (Personal Instanced Loot) แยกให้ผู้เล่นแต่ละคนที่มีส่วนร่วมทำดาเมจ
    /// โดยใช้อัตราดรอปที่ถ่วงน้ำหนักตามสัดส่วนดาเมจ (Top DPS ได้โบนัสโอกาสออก Epic/Legendary สูงสุด)
    /// </summary>
    private void SpawnPersonalInstancedLoot(PlayerRef fallbackKiller)
    {
        LootDropSystem.AnalyzeDamageContribution(
            _damageDealtMap,
            fallbackKiller,
            out float totalDamage,
            out PlayerRef topDpsPlayer,
            out List<PlayerRef> participants);

        WorldItem prefabToUse = lootPrefab;
        if (prefabToUse == null && InventoryManager.Local != null)
            prefabToUse = InventoryManager.Local.WorldItemPrefab;

        for (int i = 0; i < participants.Count; i++)
        {
            PlayerRef participant = participants[i];
            float dealt = _damageDealtMap.TryGetValue(participant, out float d) ? d : totalDamage;
            float share = totalDamage > 0f ? Mathf.Clamp01(dealt / totalDamage) : 1f;
            bool isTopDps = participant == topDpsPlayer;

            // สุ่มไอเทมส่วนตัวตามสัดส่วนดาเมจของผู้เล่นคนนี้
            ItemData rolledItem = LootDropSystem.RollDamageWeightedLoot(
                IsBoss,
                share,
                isTopDps,
                out ItemTier rolledTier,
                out LootDropSystem.WeightedTierOdds odds);

            if (rolledItem != null && prefabToUse != null && Runner != null && Runner.IsRunning)
            {
                Vector2 offset2D = UnityEngine.Random.insideUnitCircle * 1.15f;
                Vector3 dropPos = new Vector3(transform.position.x + offset2D.x, 0.4f, transform.position.z + offset2D.y);

                WorldItem.Spawn(
                    Runner,
                    prefabToUse,
                    dropPos,
                    rolledItem,
                    1,
                    allowedLooter: participant,
                    isPlayerDropped: false,
                    enhancementLevel: 0);

                Debug.Log($"[PersonalLoot] {name} dropped personal [{ItemData.GetTierDisplayName(rolledTier)}] '{rolledItem.displayName}' for {participant} (Share={share * 100f:0}%, TopDPS={isTopDps}, EpicOdds={odds.epicChance * 100f:0.0}%, LegOdds={odds.legendaryChance * 100f:0.0}%).");
            }

            // มอบค่าประสบการณ์ (EXP) ให้ผู้เล่นทุกคนที่มีส่วนร่วม
            int expShare = Mathf.Max(10, Mathf.RoundToInt(experienceReward * Mathf.Lerp(0.6f, 1.0f, share)));
            RPC_AwardParticipantReward(participant, expShare, share, isTopDps, rolledItem != null ? rolledItem.displayName : "");
        }
    }

    // ------------------------------------------------------------------ Network RPCs สำหรับแสดงผลทุกเครื่อง

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_AwardParticipantReward(PlayerRef recipient, int expAmount, float damageShare, NetworkBool isTopDps, string droppedItemName)
    {
        if (Runner == null) return;
        if (Runner.LocalPlayer == recipient || recipient == PlayerRef.None)
        {
            if (InventoryManager.Local != null) InventoryManager.Local.AddExperience(expAmount);
            else if (PlayerCombat.Local != null) PlayerCombat.Local.AddExperience(expAmount);

            string rankTag = isTopDps ? "TOP DPS ★" : $"{damageShare * 100f:0}% DMG";
            if (!string.IsNullOrEmpty(droppedItemName))
            {
                PlayerStatusHud.Instance?.ShowToast($"{rankTag} Personal Drop: {droppedItemName} (+{expAmount} EXP)",
                    isTopDps ? new Color(1f, 0.84f, 0.22f) : new Color(0.45f, 0.90f, 1f), 3.0f);
            }
        }

        if (animator != null) animator.SetTrigger(DieTrigger);
        onDeath?.Invoke();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_HitFeedback(int damage, NetworkBool isCrit, int comboStep, PlayerRef attacker)
    {
        if (animator != null) animator.SetTrigger(HitTrigger);
        onHit?.Invoke();
        if (!_flashing && gameObject.activeInHierarchy) StartCoroutine(FlashRoutine());

        FloatingDamagePopup.Spawn(LockPoint != null ? LockPoint.position : transform.position + Vector3.up * 1.4f, damage, isCrit, comboStep);
        CombatHud.NotifyEnemyDamaged(this, damage, attacker, isCrit, comboStep);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_PlayAttackFx()
    {
        if (animator != null) animator.SetTrigger(AttackTrigger);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_BossTelegraph(byte patternByte, Vector3 centerPos, Vector3 dir, float duration)
    {
        if (!gameObject.activeInHierarchy) return;
        if (animator != null) animator.SetTrigger(AttackTrigger);
        StartCoroutine(TelegraphVisualRoutine((BossAttackPattern)patternByte, centerPos, dir, duration));
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

        var r = disc.GetComponent<Renderer>();
        if (r != null)
        {
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, new Color(1f, 0.16f, 0.08f, 0.65f));
            mpb.SetColor(ColorId, new Color(1f, 0.16f, 0.08f, 0.65f));
            r.SetPropertyBlock(mpb);
        }

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
        var r = ring.GetComponent<Renderer>();
        if (r != null)
        {
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, new Color(1f, 0.48f, 0.10f, 0.85f));
            mpb.SetColor(ColorId, new Color(1f, 0.48f, 0.10f, 0.85f));
            r.SetPropertyBlock(mpb);
        }

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
        RefreshVisuals();
    }

    private void OnHealthChanged() { }

    private void OnStateChanged()
    {
        if (!IsSpawned) return;
        RefreshVisuals();
    }

    private void RefreshVisuals()
    {
        bool dead = State == NpcState.Dead;
        if (BodyCollider != null) BodyCollider.enabled = !dead;

        if (IsBoss)
        {
            SetColor(dead ? new Color(0.22f, 0.18f, 0.20f)
                : State == NpcState.Attacking ? new Color(1.0f, 0.15f, 0.08f)
                : State == NpcState.Chasing ? new Color(0.88f, 0.12f, 0.22f)
                : new Color(0.62f, 0.08f, 0.18f));
        }
        else
        {
            SetColor(dead ? new Color(0.35f, 0.35f, 0.35f)
                : State == NpcState.Attacking ? new Color(0.95f, 0.2f, 0.2f)
                : State == NpcState.Chasing ? new Color(0.95f, 0.55f, 0.15f)
                : State == NpcState.Returning ? new Color(0.4f, 0.7f, 0.95f)
                : new Color(0.85f, 0.3f, 0.3f));
        }
    }

    private void SetColor(Color c)
    {
        if (bodyRenderer == null) return;
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

        float topOffset = IsBoss ? 2.8f : 1.5f;
        Vector3 sp = cam.WorldToScreenPoint(transform.position + Vector3.up * topOffset);
        if (sp.z <= 0f || sp.z > (IsBoss ? 48f : 32f)) return;

        float w = IsBoss ? 148f : 72f;
        float h = IsBoss ? 11f : 7f;
        float x = sp.x - w * 0.5f;
        float y = Screen.height - sp.y - h * 0.5f;
        float frac = Mathf.Clamp01(CurrentHealth / (float)Mathf.Max(1, MaxHealth));

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
            fontStyle = FontStyle.Bold
        };
        GUI.color = IsBoss ? new Color(1f, 0.85f, 0.25f) : new Color(0.92f, 0.94f, 0.98f);
        string title = IsBoss ? $"★ Lv.{ScaledLevel} BOSS Sovereign Golem ({CurrentHealth}/{MaxHealth})" : $"Lv.{ScaledLevel}";
        GUI.Label(new Rect(sp.x - 110f, y - 18f, 220f, 18f), title, labelStyle);

        // แสดงป้ายแจ้งเตือนเมื่อผู้เล่นย่องเข้าด้านหลังในระยะลอบสังหาร (Stealth Assassination Prompt)
        var local = PlayerCombat.Local;
        if (local != null && local.IsSpawned && !local.IsDead && CanBeAssassinatedBy(local.transform))
        {
            GUI.color = new Color(0.08f, 0.02f, 0.04f, 0.88f);
            Rect backstabRect = new Rect(sp.x - 115f, y - 42f, 230f, 22f);
            GUI.DrawTexture(backstabRect, Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.30f, 0.22f, 1f);
            GUI.Label(backstabRect, "[LMB] STEALTH ASSASSINATE (3.0x)", labelStyle);
        }

        GUI.color = prev;
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

        var r = orb.GetComponent<Renderer>();
        if (r != null)
        {
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            Color darkPurple = new Color(0.75f, 0.14f, 0.95f, 1f);
            mpb.SetColor("_BaseColor", darkPurple);
            mpb.SetColor("_Color", darkPurple);
            r.SetPropertyBlock(mpb);
        }

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
