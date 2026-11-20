using System;
using System.Collections;
using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

public enum NpcState : byte
{
    Idle,
    Patrol,
    Chasing,
    Attacking,
    Returning,
    Dead
}

public enum BossAttackPattern : byte
{
    None = 0,
    HeavyCleave = 1,
    ShockwaveSpinAoE = 2,
    DarkVolleyAndCharge = 3
}

[Serializable]
public class LootEntry
{
    public ItemData item;
    [Min(1)] public int minAmount = 1;
    [Min(1)] public int maxAmount = 1;
    [Range(0f, 1f)] public float chance = 1f;
}

/// <summary>
/// Hostile NPC &amp; Multi-Pattern Solo Arena Boss for Photon Fusion SHARED MODE.
/// Features:
///   - Dynamic Level &amp; Stat Scaling relative to Player Level (Enemies &amp; Boss).
///   - Safe / Comfort Zone combat immunity &amp; boundary leash.
///   - Obstacle avoidance &amp; penetration resolution.
///   - Challenging Level 1 Boss baseline + 3 distinct telegraphed Boss Attack Patterns:
///       1) Heavy Cleave (1.5x single-target punish)
///       2) 360° Ground Shockwave AoE (6.2m telegraphed ring)
///       3) Dark Energy Volley + Charge Lunge (anti-kite 3-projectile fan)
/// </summary>
[RequireComponent(typeof(Collider))]
public class EnemyNPC : NetworkBehaviour, ILockTarget
{
    public static readonly List<EnemyNPC> All = new List<EnemyNPC>();

    [Header("Base Combat Stats (Level 1 Baseline)")]
    public int MaxHealth = 120;
    public int AttackDamage = 10;
    public int Defense = 0;
    public float AttackCooldown = 2.2f;
    public float AttackRange = 2.4f;

    [Header("AI")]
    [Min(1f)] [SerializeField] private float aggroRadius = 6.8f;
    [Min(2f)] [SerializeField] private float leashRadius = 22f;
    [SerializeField] private float patrolRadius = 3.5f;
    [SerializeField] private Vector2 idleWaitRange = new Vector2(1.5f, 4f);
    [SerializeField] private float patrolSpeed = 1.4f;
    [SerializeField] private float chaseSpeed = 3.5f;
    [SerializeField] private float returnSpeed = 3.8f;
    [SerializeField] private float turnSpeed = 10f;
    [Min(0f)] [SerializeField] private float despawnDelayAfterDeath = 2.5f;

    [Header("Obstacle navigation")]
    [SerializeField] private float obstacleCheckDistance = 2.4f;
    [SerializeField] private float obstacleProbeRadius = 0.55f;
    [SerializeField] private float allySeparationRadius = 1.35f;

    [Header("Damage validation (authority side)")]
    [SerializeField] private int maxDamagePerHit = 500;
    [SerializeField] private float maxHitDistance = 6.5f;
    [SerializeField] private float minSecondsBetweenHitsPerPlayer = 0.16f;

    [Header("Rewards")]
    [SerializeField] private int experienceReward = 50;
    [SerializeField] private WorldItem lootPrefab;
    [SerializeField] private LootEntry[] lootTable;

    [Header("Optional feedback")]
    [SerializeField] private Animator animator;
    [SerializeField] private Renderer bodyRenderer;
    public UnityEvent onHit;
    public UnityEvent onDeath;

    // ---- networked state ----
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

    // ---- public API ----
    public bool IsSpawned { get; private set; }
    public bool IsAlive => IsSpawned && Object != null && Object.IsValid && State != NpcState.Dead && CurrentHealth > 0;
    public Collider BodyCollider { get; private set; }
    public float AggroRadius => aggroRadius;
    public float ChaseSpeed => chaseSpeed;
    public float PatrolSpeed => patrolSpeed;
    public int ScaledMaxHealth => MaxHealth;
    public int ScaledAttackDamage => AttackDamage;
    public int ScaledDefense => Defense;
    public int ExperienceReward => experienceReward;
    public int CurrentAttackDamage => AttackDamage;

    public BossAttackPattern LastBossPattern { get; private set; }
    public int BossAttackExecutions { get; private set; }
    public int DistinctBossPatternsUsed => _usedPatterns.Count;
    public bool IsTelegraphing => _isTelegraphing;

    public Transform LockPoint => _lockPoint != null ? _lockPoint : transform;
    public bool IsLockable => IsAlive;
    public string LockName => IsBoss ? $"[Lv.{ScaledLevel} BOSS] Sovereign Golem" : $"[Lv.{ScaledLevel}] {name}";

    // ---- private ----
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

    // ------------------------------------------------------------------ lifecycle

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

        if (Object.HasStateAuthority)
        {
            if (ScaledLevel < 1) ScaledLevel = DetermineTargetScaleLevel();
            ApplyDynamicScaling(ScaledLevel);
            CurrentHealth = MaxHealth;
            HomePosition = transform.position;
            State = NpcState.Idle;
            TargetId = default;
            Debug.Log($"[EnemyNPC] {name} Lv.{ScaledLevel} (Boss={IsBoss}, Camp={CampId}) spawned. HP={CurrentHealth}/{MaxHealth}, ATK={AttackDamage}, DEF={Defense}, EXP={experienceReward}.");
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
            maxDamagePerHit = 1200;
        }
        else
        {
            MaxHealth = 120 + (targetLevel - 1) * 45;
            AttackDamage = 10 + (targetLevel - 1) * 2;
            Defense = (targetLevel - 1) * 2;
            experienceReward = 50 + (targetLevel - 1) * 18;
            maxDamagePerHit = 600;
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
        patrolRadius = 3.5f;
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
        Debug.Log($"[EnemyNPC] {name} despawned.");
    }

    // ------------------------------------------------------------------ AI (State Authority only)

    public override void FixedUpdateNetwork()
    {
        if (!IsSpawned || !Object.HasStateAuthority) return;

        float dt = Runner.DeltaTime;
        switch (State)
        {
            case NpcState.Idle: TickIdle(); break;
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

    private void TickIdle()
    {
        if (TryAcquireNearestPlayer()) return;

        _stateTimer -= Time.deltaTime;
        if (_stateTimer <= 0f && patrolRadius > 0.5f)
        {
            Vector2 c = UnityEngine.Random.insideUnitCircle * patrolRadius;
            Vector3 candidate = HomePosition + new Vector3(c.x, 0f, c.y);
            if (!SafeZone.IsPointInAnySafeZone(candidate, 0.8f))
            {
                _patrolPoint = candidate;
                _hasPatrolPoint = true;
                ChangeState(NpcState.Patrol, "wandering");
            }
            else
            {
                _stateTimer = 1.5f;
            }
        }
    }

    private void TickPatrol(float dt)
    {
        if (TryAcquireNearestPlayer()) return;

        if (!_hasPatrolPoint) { ChangeState(NpcState.Idle, "no patrol point"); return; }

        if (MoveTowards(_patrolPoint, patrolSpeed, dt) <= 0.35f)
        {
            _hasPatrolPoint = false;
            _stateTimer = UnityEngine.Random.Range(idleWaitRange.x, idleWaitRange.y);
            ChangeState(NpcState.Idle, "reached patrol point");
        }
    }

    private void TickChasing(float dt)
    {
        PlayerCombat target = ResolveTarget();
        if (target == null || target.IsDead) { BeginReturn(target == null ? "target lost" : "target died"); return; }
        if (SafeZone.IsPlayerInAnySafeZone(target)) { BeginReturn("target entered Comfort Safe Zone"); return; }

        if (PlanarDistance(transform.position, HomePosition) > leashRadius) { BeginReturn("leash radius exceeded"); return; }

        float dist = PlanarDistance(transform.position, target.transform.position);

        // Boss ranged / charge anti-kite check while chasing beyond melee range!
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
            ChangeState(NpcState.Chasing, "target moved out of range or behind obstacle");
            return;
        }

        FaceTowards(target.transform.position, dt);

        if (Time.time >= _nextAttackTime)
        {
            if (IsBoss)
            {
                BossAttackPattern pattern = ChooseNextBossPattern(dist);
                StartBossPatternAttack(target, pattern);
            }
            else
            {
                _nextAttackTime = Time.time + AttackCooldown;
                Debug.Log($"[EnemyNPC] {name} attacks {target.name} (dmg {AttackDamage}).");
                RPC_PlayAttackFx();
                target.RPC_ReceiveNpcDamage(Object.Id, AttackDamage);
            }
        }
    }

    private BossAttackPattern ChooseNextBossPattern(float distToTarget)
    {
        _bossPatternCycle++;
        if (distToTarget > AttackRange * 1.1f)
            return BossAttackPattern.DarkVolleyAndCharge;

        return (_bossPatternCycle % 3) switch
        {
            1 => BossAttackPattern.HeavyCleave,
            2 => BossAttackPattern.ShockwaveSpinAoE,
            _ => BossAttackPattern.DarkVolleyAndCharge
        };
    }

    /// <summary>Executes one of the Boss's 3 multi-attack patterns with telegraph cues.</summary>
    public void TriggerBossPatternForTest(BossAttackPattern pattern, PlayerCombat target)
    {
        if (!IsSpawned || !Object.HasStateAuthority || !IsBoss || target == null) return;
        StartBossPatternAttack(target, pattern);
    }

    private void StartBossPatternAttack(PlayerCombat target, BossAttackPattern pattern)
    {
        if (_bossAttackRoutine != null) StopCoroutine(_bossAttackRoutine);
        _nextAttackTime = Time.time + AttackCooldown + 0.65f;
        _bossAttackRoutine = StartCoroutine(BossPatternRoutine(target, pattern));
    }

    private IEnumerator BossPatternRoutine(PlayerCombat target, BossAttackPattern pattern)
    {
        _isTelegraphing = true;
        LastBossPattern = pattern;
        _usedPatterns.Add(pattern);
        BossAttackExecutions++;

        Vector3 aimDir = target != null ? (target.transform.position - transform.position) : transform.forward;
        aimDir.y = 0f;
        if (aimDir.sqrMagnitude < 0.001f) aimDir = transform.forward;
        aimDir.Normalize();

        float windup = pattern == BossAttackPattern.ShockwaveSpinAoE ? 1.0f : 0.85f;
        RPC_BossTelegraph((byte)pattern, transform.position, aimDir, windup);

        yield return new WaitForSeconds(windup);

        _isTelegraphing = false;
        _bossAttackRoutine = null;

        if (!IsAlive || target == null || target.IsDead || SafeZone.IsPlayerInAnySafeZone(target))
            yield break;

        if (pattern == BossAttackPattern.HeavyCleave)
        {
            float d = PlanarDistance(transform.position, target.transform.position);
            if (d <= AttackRange + 1.4f && !SafeZone.IsPlayerInAnySafeZone(target))
            {
                int cleaveDmg = Mathf.RoundToInt(AttackDamage * 1.65f);
                Debug.Log($"[BOSS AI] Pattern 1 HEAVY CLEAVE hits {target.name} for {cleaveDmg} DMG!");
                target.RPC_ReceiveNpcDamage(Object.Id, cleaveDmg);
            }
        }
        else if (pattern == BossAttackPattern.ShockwaveSpinAoE)
        {
            const float aoeRadius = 4.5f;
            int aoeDmg = Mathf.RoundToInt(AttackDamage * 1.25f);
            RPC_PlayBossShockwaveRelease(transform.position, aoeRadius);

            for (int i = 0; i < PlayerCombat.All.Count; i++)
            {
                var p = PlayerCombat.All[i];
                if (p == null || !p.IsSpawned || p.IsDead || SafeZone.IsPlayerInAnySafeZone(p)) continue;
                if (PlanarDistance(transform.position, p.transform.position) <= aoeRadius)
                {
                    Debug.Log($"[BOSS AI] Pattern 2 360° SHOCKWAVE AoE hits {p.name} for {aoeDmg} DMG!");
                    p.RPC_ReceiveNpcDamage(Object.Id, aoeDmg);
                }
            }
        }
        else if (pattern == BossAttackPattern.DarkVolleyAndCharge)
        {
            Vector3 currentAim = target.transform.position - transform.position;
            currentAim.y = 0f;
            if (currentAim.sqrMagnitude < 0.001f) currentAim = aimDir;
            currentAim.Normalize();

            // Lunge forward safely (never entering Comfort Zone)
            Vector3 lungeTarget = transform.position + currentAim * 2.8f;
            if (!SafeZone.IsPointInAnySafeZone(lungeTarget, 0.8f))
            {
                ResolveObstaclePenetration(ref lungeTarget);
                lungeTarget.y = transform.position.y;
                transform.position = lungeTarget;
            }

            int projDmg = Mathf.RoundToInt(AttackDamage * 0.95f);
            RPC_SpawnBossVolley(transform.position + Vector3.up * 1.3f, currentAim, projDmg);
            Debug.Log($"[BOSS AI] Pattern 3 DARK ENERGY VOLLEY + CHARGE fired 3 projectiles ({projDmg} DMG each)!");
        }
    }

    private void TickReturning(float dt)
    {
        if (MoveTowards(HomePosition, returnSpeed, dt) <= 0.35f)
        {
            CurrentHealth = MaxHealth;
            TargetId = default;
            _stateTimer = UnityEngine.Random.Range(idleWaitRange.x, idleWaitRange.y);
            ChangeState(NpcState.Idle, "arrived home, health restored");
        }
    }

    private void TickDead()
    {
        if (Time.time - _deathTime >= despawnDelayAfterDeath)
        {
            Debug.Log($"[EnemyNPC] {name} corpse timeout, despawning.");
            Runner.Despawn(Object);
        }
    }

    private bool TryAcquireNearestPlayer()
    {
        PlayerCombat best = null;
        float bestDist = aggroRadius;
        for (int i = 0; i < PlayerCombat.All.Count; i++)
        {
            var p = PlayerCombat.All[i];
            if (p == null || !p.IsSpawned || p.IsDead) continue;
            if (SafeZone.IsPlayerInAnySafeZone(p)) continue;

            float d = PlanarDistance(transform.position, p.transform.position);
            if (d <= bestDist) { bestDist = d; best = p; }
        }
        if (best == null) return false;

        Debug.Log($"[EnemyNPC] {name} aggro: {best.name} entered zone at {bestDist:0.0}m.");
        SetTarget(best);
        ChangeState(NpcState.Chasing, "player entered aggro zone");
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
        Debug.Log($"[EnemyNPC] {name} state {State} -> {next} ({reason}).");
        State = next;
    }

    // ------------------------------------------------------------------ obstacle & SafeZone steering

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
        Vector3 finalDir = steeredDir + separation * 0.55f;
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
        {
            return desiredDir;
        }

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

        if (bestDir.sqrMagnitude > 0.0001f)
            return bestDir.normalized;

        return desiredDir;
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

    // ------------------------------------------------------------------ damage in

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
        if (!IsSpawned || !Object.HasStateAuthority) return;
        if (!IsAlive) return;

        PlayerCombat attacker = FindPlayerBySource(info.Source);
        if (attacker == null || attacker.IsDead) return;
        if (SafeZone.IsPlayerInAnySafeZone(attacker)) return;

        float dist = PlanarDistance(transform.position, attacker.transform.position);
        float limit = IsBoss ? maxHitDistance + 1.5f : maxHitDistance;
        if (dist > limit)
        {
            Debug.LogWarning($"[EnemyNPC] {name}: damage rejected, attacker {dist:0.0}m away (max {limit}).");
            return;
        }

        float now = Time.time;
        if (_lastDamageTime.TryGetValue(info.Source, out float last) && now - last < minSecondsBetweenHitsPerPlayer)
            return;
        _lastDamageTime[info.Source] = now;

        // Apply Enemy/Boss Scaled Defense mitigation (minimum 1 damage)
        int raw = Mathf.Clamp(damage, 1, maxDamagePerHit);
        int applied = Defense > 0 ? Mathf.Max(1, raw - Defense) : raw;

        int before = CurrentHealth;
        CurrentHealth = Mathf.Max(0, before - applied);
        Debug.Log($"[EnemyNPC] {name} (Lv.{ScaledLevel}, DEF={Defense}) takes {applied}{(isCrit ? " (CRIT!)" : "")} from {attacker.name}: {before} -> {CurrentHealth}/{MaxHealth}.");

        RPC_HitFeedback(applied, isCrit, comboStep, info.Source);

        if (CurrentHealth <= 0)
        {
            Die(info.Source);
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

    private PlayerCombat FindPlayerBySource(PlayerRef source)
    {
        for (int i = 0; i < PlayerCombat.All.Count; i++)
        {
            var p = PlayerCombat.All[i];
            if (p != null && p.IsSpawned && p.Object.StateAuthority == source) return p;
        }
        return null;
    }

    private void Die(PlayerRef killer)
    {
        TargetId = default;
        if (_bossAttackRoutine != null) { StopCoroutine(_bossAttackRoutine); _bossAttackRoutine = null; }
        _isTelegraphing = false;
        _deathTime = Time.time;
        ChangeState(NpcState.Dead, $"killed by {killer}");
        SpawnLoot();
        RPC_Died(killer, experienceReward);
    }

    private void SpawnLoot()
    {
        if (lootTable == null || lootTable.Length == 0 || lootPrefab == null) return;

        foreach (var entry in lootTable)
        {
            if (entry == null || entry.item == null) continue;
            if (UnityEngine.Random.value > entry.chance) continue;

            int qty = UnityEngine.Random.Range(entry.minAmount, Mathf.Max(entry.minAmount, entry.maxAmount) + 1);
            Vector2 o = UnityEngine.Random.insideUnitCircle * 1.2f;
            Vector3 pos = new Vector3(transform.position.x + o.x, 0.4f, transform.position.z + o.y);

            WorldItem.Spawn(Runner, lootPrefab, pos, entry.item, qty);
        }
    }

    // ------------------------------------------------------------------ feedback & Boss Pattern VFX (all peers)

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
        // Glowing arena telegraph marker on ground + floating warning text
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

        float diameter = pattern == BossAttackPattern.ShockwaveSpinAoE ? 9.0f : 5.0f;
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

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_Died(PlayerRef killer, int exp)
    {
        Debug.Log($"[EnemyNPC] {name} (Lv.{ScaledLevel}) died (killer {killer}, exp={exp}).");
        if (animator != null) animator.SetTrigger(DieTrigger);
        try
        {
            MMOMonsterDeathBloodVFX.SpawnDeathBlood(transform.position, IsBoss ? 1.85f : -1f);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[EnemyNPC] Death blood VFX skipped: {ex.Message}");
        }
        onDeath?.Invoke();

        if (Runner != null && Runner.LocalPlayer == killer)
        {
            if (InventoryManager.Local != null) InventoryManager.Local.AddExperience(exp);
            else if (PlayerCombat.Local != null) PlayerCombat.Local.AddExperience(exp);
        }
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
        if (BodyCollider != null) BodyCollider.enabled = State != NpcState.Dead;
        if (_flashing) return;

        Color c;
        if (IsBoss)
        {
            switch (State)
            {
                case NpcState.Chasing: c = new Color(0.88f, 0.08f, 0.22f); break;
                case NpcState.Attacking: c = new Color(1.0f, 0.38f, 0.05f); break;
                case NpcState.Returning: c = new Color(0.95f, 0.78f, 0.15f); break;
                case NpcState.Dead: c = new Color(0.22f, 0.18f, 0.20f); break;
                default: c = new Color(0.52f, 0.08f, 0.18f); break;
            }
        }
        else
        {
            switch (State)
            {
                case NpcState.Chasing: c = new Color(1f, 0.15f, 0.1f); break;
                case NpcState.Attacking: c = new Color(1f, 0.5f, 0f); break;
                case NpcState.Returning: c = new Color(0.85f, 0.85f, 0.2f); break;
                case NpcState.Dead: c = new Color(0.25f, 0.25f, 0.25f); break;
                default: c = new Color(0.6f, 0.2f, 0.2f); break;
            }
        }
        SetColor(c);
    }

    private void SetColor(Color c)
    {
        if (bodyRenderer == null) return;
        bodyRenderer.GetPropertyBlock(_mpb);
        _mpb.SetColor(BaseColorId, c);
        bodyRenderer.SetPropertyBlock(_mpb);
    }

    // ------------------------------------------------------------------ debug HUD

    private void OnGUI()
    {
        if (!IsSpawned || GameplayInput.IsBlocked || Event.current.type != EventType.Repaint) return;
        var cam = Camera.main;
        if (cam == null) return;

        float heightOffset = IsBoss ? 2.85f : 1.6f;
        Vector3 world = transform.position + Vector3.up * heightOffset;
        Vector3 sp = cam.WorldToScreenPoint(world);
        if (sp.z <= 0f || sp.z > 45f) return;

        float y = Screen.height - sp.y;
        float w = IsBoss ? 130f : 74f;
        float frac = MaxHealth > 0 ? Mathf.Clamp01(CurrentHealth / (float)MaxHealth) : 0f;

        var prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.75f);
        GUI.DrawTexture(new Rect(sp.x - w * 0.5f - 1f, y - 1f, w + 2f, IsBoss ? 11f : 8f), Texture2D.whiteTexture);
        GUI.color = IsBoss ? Color.Lerp(new Color(0.8f, 0.1f, 0.2f), new Color(1f, 0.75f, 0.15f), frac) : Color.Lerp(Color.red, Color.green, frac);
        GUI.DrawTexture(new Rect(sp.x - w * 0.5f, y, w * frac, IsBoss ? 9f : 6f), Texture2D.whiteTexture);
        GUI.color = prev;
        string label = IsBoss ? $"[Lv.{ScaledLevel} BOSS] {CurrentHealth}/{MaxHealth}" : $"Lv.{ScaledLevel} {State}";
        GUI.Label(new Rect(sp.x - 95f, y - 20f, 190f, 20f), label);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, aggroRadius);
        Gizmos.color = new Color(1f, 0.8f, 0f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, AttackRange);
    }
}

/// <summary>
/// Ranged Dark Energy Orb launched during the Boss's Pattern 3 Dark Volley attack.
/// Moves across the arena, respects Safe Zones and dodges, and lets players block or evade!
/// </summary>
public sealed class BossDarkProjectile : MonoBehaviour
{
    private EnemyNPC _owner;
    private Vector3 _dir;
    private int _damage;
    private float _spawnTime;
    private const float Speed = 11.5f;
    private const float Lifetime = 2.0f;
    private const float HitRadius = 0.95f;

    public static void Spawn(EnemyNPC owner, Vector3 origin, Vector3 direction, int damage)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "BossDarkOrb";
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);
        go.transform.position = origin;
        go.transform.localScale = Vector3.one * 0.55f;

        var r = go.GetComponent<Renderer>();
        if (r != null)
        {
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(Shader.PropertyToID("_BaseColor"), new Color(0.85f, 0.12f, 0.95f, 1f));
            mpb.SetColor(Shader.PropertyToID("_Color"), new Color(0.85f, 0.12f, 0.95f, 1f));
            r.SetPropertyBlock(mpb);
        }

        var proj = go.AddComponent<BossDarkProjectile>();
        proj._owner = owner;
        direction.y = 0f;
        proj._dir = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.forward;
        proj._damage = Mathf.Max(1, damage);
        proj._spawnTime = Time.time;
    }

    private void Update()
    {
        if (Time.time - _spawnTime >= Lifetime)
        {
            Destroy(gameObject);
            return;
        }

        transform.position += _dir * (Speed * Time.deltaTime);

        if (SafeZone.IsPointInAnySafeZone(transform.position))
        {
            Destroy(gameObject);
            return;
        }

        var local = PlayerCombat.Local;
        if (local != null && local.IsSpawned && !local.IsDead && !SafeZone.IsPlayerInAnySafeZone(local))
        {
            Vector3 pPos = local.transform.position + Vector3.up * 1.0f;
            if (Vector3.Distance(transform.position, pPos) <= HitRadius)
            {
                if (_owner != null && _owner.IsSpawned)
                    local.RPC_ReceiveNpcDamage(_owner.Object.Id, _damage);
                Destroy(gameObject);
            }
        }
    }
}
