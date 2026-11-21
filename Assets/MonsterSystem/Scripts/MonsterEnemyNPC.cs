using System.Collections;
using System.Reflection;
using Fusion;
using UnityEngine;
using UnityEngine.Rendering;

public enum MonsterGuardFeedback : byte
{
    GuardRaised = 0,
    Blocked = 1,
    GuardBreak = 2,
    CounterSwing = 3
}

/// <summary>
/// Production-hardened, self-contained Tactical Monster AI inheriting from <see cref="EnemyNPC"/> so it works
/// seamlessly with the project's existing <see cref="PlayerCombat"/>, <see cref="TargetLockSystem"/>,
/// <see cref="SafeZone"/>, and Photon Fusion 2 Shared Mode with ZERO edits to any existing scripts!
///
/// Key Production Safeguards:
///   1. Zero-Hijack Isolation: cleanly disconnects base EnemyNPC's private animator/bodyRenderer after Awake()
///      so base RPCs never bypass Hyper Armor/Guard or tint PBR materials.
///   2. Terrain & Elevation Safe: all obstacle/line-of-sight/penetration checks operate relative to foot height
///      and explicitly ignore TerrainCollider & walkable ground slabs, plus stuck-recovery when returning home.
///   3. Host-Migration Safe: tracks networked ZoneSignature & ZoneSlotIndex so new Master Clients adopt living
///      monsters without double-spawning.
///   4. Species Stat Scaling: applies MonsterAnimationConfig multipliers on top of level scaling and strips "(Clone)".
///   5. Reliable Counter & Co-op Guard Break: respects PlayerCombat's hit cooldown gate and prioritizes active
///      swinging players in Co-op.
///   6. Shared Static VFX Materials + Orbiting Stun Halo + Optional 3D Spatial Audio (zero material memory leaks).
/// </summary>
public class MonsterEnemyNPC : EnemyNPC
{
    [Header("Monster System References")]
    [SerializeField] private MonsterAnimatorBridge animatorBridge;

    [Header("Solo-Pull & Movement")]
    [Min(1f)] [SerializeField] private float personalAggroRadius = 5.5f;
    [Min(2f)] [SerializeField] private float personalLeashRadius = 20f;
    [Min(0.5f)] [SerializeField] private float personalPatrolRadius = 2.0f;
    [SerializeField] private Vector2 personalIdleWait = new Vector2(2.0f, 4.5f);
    [Min(0.5f)] [SerializeField] private float walkMoveSpeed = 1.6f;
    [Min(1f)] [SerializeField] private float runMoveSpeed = 4.2f;
    [Min(1f)] [SerializeField] private float returnMoveSpeed = 4.4f;
    [Min(1f)] [SerializeField] private float rotationSpeed = 10f;
    [Min(1f)] [SerializeField] private float corpseDespawnDelay = 4.0f;

    [Header("Melee Swing Timing (Anti-Slide & Dodgeable)")]
    [Tooltip("Delay from attack start to actual arm-swing impact (seconds). Player can dodge/backstep during this window!")]
    [Min(0.05f)] [SerializeField] private float attackImpactDelay = 0.40f;
    [Min(0.05f)] [SerializeField] private float counterImpactDelay = 0.36f;
    [Tooltip("Extra reach slack at the moment of impact.")]
    [Min(0f)] [SerializeField] private float impactReachSlack = 0.85f;
    [Tooltip("Minimum safety lock while waiting for Animator to enter the Attack state.")]
    [Min(0.1f)] [SerializeField] private float minAttackAnimLock = 0.55f;

    [Header("Anti-Stunlock Poise, Block & Counter")]
    [Tooltip("After taking this many consecutive flinching hits, the monster automatically raises Guard.")]
    [Min(1)] [SerializeField] private int hitsBeforeGuard = 2;
    [Min(0.5f)] [SerializeField] private float comboResetWindow = 3.0f;
    [Min(0.4f)] [SerializeField] private float guardDuration = 1.6f;
    [Min(0.5f)] [SerializeField] private float guardCooldown = 3.5f;
    [Range(0f, 1f)] [SerializeField] private float reactiveGuardChanceOnCooldown = 0.28f;
    [Tooltip("Minimum flat Defense added while Guarding (automatically scales up at higher levels).")]
    [Min(5)] [SerializeField] private int guardBonusDefense = 24;
    [Tooltip("Damage multiplier on Counter-Attack when the player spams into Guard.")]
    [Min(1f)] [SerializeField] private float counterDamageMultiplier = 1.25f;
    [Tooltip("Duration of Stun when player shatters Guard with Combo 3 Finisher.")]
    [Min(0.5f)] [SerializeField] private float guardBreakStunDuration = 1.5f;
    [Min(0.1f)] [SerializeField] private float hitFlinchPause = 0.35f;

    [Header("Obstacle, Terrain & Separation")]
    [SerializeField] private float obstacleProbeDist = 2.2f;
    [SerializeField] private float obstacleProbeRad = 0.55f;
    [SerializeField] private float allySepRadius = 1.6f;
    [Tooltip("Max vertical step height considered walkable floor instead of an obstacle wall.")]
    [Min(0.1f)] [SerializeField] private float walkableStepHeight = 0.40f;

    [Header("Debug")]
    [SerializeField] private bool verboseLogs = false;

    // ---- Networked Tactical & Zone Ownership State (all peers) ----
    [Networked, OnChangedRender(nameof(OnGuardVisualStateChanged))]
    public NetworkBool IsGuarding { get; set; }

    [Networked, OnChangedRender(nameof(OnStunVisualStateChanged))]
    public NetworkBool IsStunnedNet { get; set; }

    [Networked]
    public int ZoneSignature { get; set; }

    [Networked]
    public int ZoneSlotIndex { get; set; } = -1;

    // ---- Cached Base Field Reflection (isolates EnemyNPC private animator/bodyRenderer without editing EnemyNPC.cs) ----
    private static readonly FieldInfo BaseAnimatorField =
        typeof(EnemyNPC).GetField("animator", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo BaseBodyRendererField =
        typeof(EnemyNPC).GetField("bodyRenderer", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo BaseLootPrefabField =
        typeof(EnemyNPC).GetField("lootPrefab", BindingFlags.Instance | BindingFlags.NonPublic);

    // ---- Shared Static VFX Materials (zero material leaks across infinite respawns) ----
    private static Material s_guardBarrierMat;
    private static Material s_guardRingMat;
    private static Material s_stunOrbMat;

    // ---- Runtime Private State ----
    private int _baseDefense;
    private int _consecutiveHits;
    private float _lastHitStamp = -100f;
    private float _lastDamageDealtToPlayerStamp = -100f;
    private float _nextAttackAt;
    private float _attackLockUntil;
    private float _hitFlinchUntil;
    private float _hyperArmorUntil;
    private float _guardUntil;
    private float _nextGuardAllowedAt;
    private float _stunUntil;
    private float _idleTimer;
    private float _deathStamp;
    private float _currentMoveSpeed;
    private float _returnStuckTimer;
    private Vector3 _lastReturnCheckPos;
    private bool _hasPatrolDest;
    private Vector3 _patrolDest;
    private bool _isSwinging;
    private bool _listenersWired;
    private bool _speciesStatsApplied;
    private bool _lootDropped;
    private int _avoidDirSign;
    private float _avoidSignUntil;
    private PlayerCombat _lastKnownAttacker;

    private Coroutine _swingRoutine;
    private AudioSource _audioSource;
    private GameObject _guardVfxRoot;
    private Transform _guardShieldMesh;
    private Transform _guardRingMesh;
    private GameObject _stunVfxRoot;
    private Transform _stunOrbitPivot;

    private static readonly Collider[] s_monsterOverlapBuf = new Collider[24];
    private static readonly RaycastHit[] s_monsterCastBuf = new RaycastHit[16];

    // NOTE: Do NOT define Awake() here so Unity executes EnemyNPC's private Awake() cleanly!

    private void OnEnable()
    {
        DisconnectBaseAnimatorAndRenderer();
        EnsureBridgeAndListeners();
        EnsureAudioSource();
        BuildCombatVfx();
    }

    private void OnDisable()
    {
        RemoveListeners();
        if (_swingRoutine != null)
        {
            StopCoroutine(_swingRoutine);
            _swingRoutine = null;
        }
        _isSwinging = false;
        SetGuardVfxVisible(false);
        SetStunVfxVisible(false);
    }

    public override void Spawned()
    {
        DisconnectBaseAnimatorAndRenderer();
        EnsureBridgeAndListeners();
        EnsureAudioSource();
        BuildCombatVfx();

        _speciesStatsApplied = false;
        _lootDropped = false;
        _lastKnownAttacker = null;
        base.Spawned();

        // Clean display name (removes "(Clone)" so Lock-On HUD displays e.g. "[Lv.1] Orc")
        if (animatorBridge != null && animatorBridge.Config != null && !string.IsNullOrWhiteSpace(animatorBridge.Config.monsterName))
            gameObject.name = animatorBridge.Config.monsterName;
        else if (gameObject.name.EndsWith("(Clone)"))
            gameObject.name = gameObject.name.Replace("(Clone)", string.Empty).Trim();

        ApplySpeciesStatMultipliers();

        if (Object.HasStateAuthority)
        {
            // Force CampId = 0 so base EnemyNPC.BroadcastCampAggro never pulls other monsters (clean 1-by-1 pulling!)
            CampId = 0;

            Vector3 grounded = transform.position;
            if (SnapToGround(ref grounded, searchUp: 5.0f, searchDown: 25.0f))
                transform.position = grounded;

            HomePosition = transform.position;
            CurrentHealth = MaxHealth;
            _baseDefense = Defense;
            IsGuarding = false;
            IsStunnedNet = false;
            _consecutiveHits = 0;
            _isSwinging = false;
            _returnStuckTimer = 0f;
            _lastReturnCheckPos = transform.position;
            _idleTimer = Random.Range(personalIdleWait.x, personalIdleWait.y);
        }

        if (animatorBridge != null)
        {
            animatorBridge.ResetForPoolSpawn();
            animatorBridge.SetSuperArmor(IsBoss);
        }
        SetGuardVfxVisible(false);
        SetStunVfxVisible(false);
        MonsterSystemManager.NotifyMonsterSpawned(this);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (_swingRoutine != null)
        {
            StopCoroutine(_swingRoutine);
            _swingRoutine = null;
        }
        _isSwinging = false;
        SetGuardVfxVisible(false);
        SetStunVfxVisible(false);
        base.Despawned(runner, hasState);
    }

    /// <summary>
    /// Nulls base EnemyNPC's private animator & bodyRenderer after EnemyNPC.Awake() runs so
    /// base RPC_HitFeedback/RPC_Died never fire rogue triggers bypassing Hyper Armor/Guard
    /// and base RefreshVisuals never tints PBR meshes.
    /// </summary>
    private void DisconnectBaseAnimatorAndRenderer()
    {
        BaseAnimatorField?.SetValue(this, null);
        BaseBodyRendererField?.SetValue(this, null);
    }

    private void ApplySpeciesStatMultipliers()
    {
        if (_speciesStatsApplied) return;
        _speciesStatsApplied = true;

        MonsterAnimationConfig cfg = animatorBridge != null ? animatorBridge.Config : null;
        MonsterSystemManager mgr = MonsterSystemManager.Active;
        float globalHpMult = mgr != null ? Mathf.Max(0.1f, mgr.globalHealthMultiplier) : 1.0f;
        float globalDmgMult = mgr != null ? Mathf.Max(0.1f, mgr.globalDamageMultiplier) : 1.0f;

        float hpMult = (cfg != null ? cfg.healthMultiplier : 1.0f) * globalHpMult;
        float dmgMult = (cfg != null ? cfg.damageMultiplier : 1.0f) * globalDmgMult;
        int defBonus = cfg != null ? cfg.defenseBonus : 0;

        MaxHealth = Mathf.Max(20, Mathf.RoundToInt(MaxHealth * hpMult));
        AttackDamage = Mathf.Max(2, Mathf.RoundToInt(AttackDamage * dmgMult));
        Defense = Mathf.Max(0, Defense + defBonus);

        if (cfg != null)
        {
            if (cfg.attackRange >= 1.0f) AttackRange = cfg.attackRange;
            if (cfg.attackImpactDelay >= 0.05f) attackImpactDelay = cfg.attackImpactDelay;
            if (cfg.counterImpactDelay >= 0.05f) counterImpactDelay = cfg.counterImpactDelay;
        }
    }

    // ------------------------------------------------------------------ Tactical AI Loop (State Authority)

    public override void FixedUpdateNetwork()
    {
        if (!IsSpawned || !Object.HasStateAuthority) return;

        float dt = Runner.DeltaTime;
        _currentMoveSpeed = 0f;

        if (State == NpcState.Dead)
        {
            if (_deathStamp <= 0f) _deathStamp = Time.time;
            if (IsGuarding) ExitGuardAuthority();
            if (IsStunnedNet) IsStunnedNet = false;

            if (Time.time - _deathStamp >= corpseDespawnDelay)
                Runner.Despawn(Object);
            return;
        }

        // 1. Stunned (from Guard Break)
        if (IsStunnedNet)
        {
            if (Time.time >= _stunUntil)
                IsStunnedNet = false;
            return;
        }

        // 2. Active Guard Stance
        if (IsGuarding)
        {
            PlayerCombat guardTarget = ResolveCurrentTarget();
            if (guardTarget != null && !guardTarget.IsDead)
                FaceTowards(guardTarget.transform.position, dt);

            if (Time.time >= _guardUntil || guardTarget == null || guardTarget.IsDead || SafeZone.IsPlayerInAnySafeZone(guardTarget))
                ExitGuardAuthority();
            return;
        }

        // 3. Normal States
        switch (State)
        {
            case NpcState.Idle: TickIdleAuthority(dt); break;
            case NpcState.Patrol: TickPatrolAuthority(dt); break;
            case NpcState.Chasing: TickChasingAuthority(dt); break;
            case NpcState.Attacking: TickAttackingAuthority(dt); break;
            case NpcState.Returning: TickReturningAuthority(dt); break;
        }

        if (IsSpawned && Object != null && Object.IsValid && State != NpcState.Dead)
        {
            Vector3 pos = transform.position;
            if (ResolveObstaclePenetrationLocal(ref pos))
            {
                SnapToGround(ref pos, searchUp: 2.5f, searchDown: 10.0f);
                transform.position = pos;
            }
        }
    }

    public override void Render()
    {
        if (!IsSpawned) return;

        // Animate Guard VFX pulse
        if (_guardVfxRoot != null && _guardVfxRoot.activeSelf)
        {
            if (_guardShieldMesh != null)
            {
                float pulse = 1f + Mathf.Sin(Time.time * 7f) * 0.04f;
                _guardShieldMesh.localScale = new Vector3(1.15f * pulse, 1.35f * pulse, 0.35f * pulse);
            }
            if (_guardRingMesh != null)
            {
                float r = 1.65f + Mathf.Sin(Time.time * 5f) * 0.08f;
                _guardRingMesh.localScale = new Vector3(r, 0.02f, r);
            }
        }

        // Animate Stun halo orbit above head so Guard Break is unmistakably clear
        if (_stunVfxRoot != null && _stunVfxRoot.activeSelf && _stunOrbitPivot != null)
        {
            _stunOrbitPivot.Rotate(0f, 240f * Time.deltaTime, 0f, Space.Self);
        }

        if (animatorBridge == null) return;

        bool dead = State == NpcState.Dead;
        bool inCombat = !dead && (State == NpcState.Chasing || State == NpcState.Attacking || IsGuarding);

        animatorBridge.SetCombat(inCombat);
        animatorBridge.SetStunned(!dead && IsStunnedNet);
        if (dead && !animatorBridge.IsDead)
            animatorBridge.SetDead(true);

        if (dead || IsGuarding || IsStunnedNet || _isSwinging)
        {
            animatorBridge.SetWorldSpeed(0f);
            return;
        }

        if (Object.HasStateAuthority)
        {
            bool movingState = State == NpcState.Patrol || State == NpcState.Chasing || State == NpcState.Returning;
            animatorBridge.SetWorldSpeed(movingState ? _currentMoveSpeed : 0f);
        }
        else
        {
            if (State == NpcState.Idle || State == NpcState.Attacking)
                animatorBridge.SetWorldSpeed(0f);
            else
                animatorBridge.UpdateSpeedFromTransform(Time.deltaTime);
        }
    }

    private void TickIdleAuthority(float dt)
    {
        if (TryAcquireSoloTarget()) return;

        _idleTimer -= dt;
        if (_idleTimer <= 0f && personalPatrolRadius > 0.4f)
        {
            Vector2 rnd = Random.insideUnitCircle * personalPatrolRadius;
            Vector3 candidate = HomePosition + new Vector3(rnd.x, 0f, rnd.y);
            SnapToGround(ref candidate, searchUp: 3.5f, searchDown: 12.0f);
            if (!SafeZone.IsPointInAnySafeZone(candidate, 0.8f))
            {
                _patrolDest = candidate;
                _hasPatrolDest = true;
                State = NpcState.Patrol;
            }
            else
            {
                _idleTimer = 1.5f;
            }
        }
    }

    private void TickPatrolAuthority(float dt)
    {
        if (TryAcquireSoloTarget()) return;
        if (IsMovementAnimationLocked()) return;

        if (!_hasPatrolDest)
        {
            State = NpcState.Idle;
            return;
        }

        if (MoveTowardsLocal(_patrolDest, walkMoveSpeed, dt) <= 0.35f)
        {
            _hasPatrolDest = false;
            _idleTimer = Random.Range(personalIdleWait.x, personalIdleWait.y);
            State = NpcState.Idle;
        }
    }

    private void TickChasingAuthority(float dt)
    {
        PlayerCombat target = ResolveCurrentTarget();
        if (target == null || target.IsDead || SafeZone.IsPlayerInAnySafeZone(target))
        {
            BeginReturnAuthority();
            return;
        }

        // Strict Anti-Slide: NEVER move feet while Attack or Hit_React animation is still playing!
        if (IsMovementAnimationLocked())
        {
            FaceTowards(target.transform.position, dt * 0.45f);
            return;
        }

        if (PlanarDistance(transform.position, HomePosition) > personalLeashRadius)
        {
            BeginReturnAuthority();
            return;
        }

        float dist = PlanarDistance(transform.position, target.transform.position);
        if (dist <= AttackRange && HasClearLineLocal(target.transform.position))
        {
            State = NpcState.Attacking;
            return;
        }

        MoveTowardsLocal(target.transform.position, runMoveSpeed, dt);
    }

    private void TickAttackingAuthority(float dt)
    {
        PlayerCombat target = ResolveCurrentTarget();
        if (target == null || target.IsDead || SafeZone.IsPlayerInAnySafeZone(target))
        {
            BeginReturnAuthority();
            return;
        }

        // Strict Anti-Slide: plant feet until Attack & Hit_React finish completely!
        if (IsMovementAnimationLocked())
        {
            FaceTowards(target.transform.position, dt * 0.40f);
            return;
        }

        float dist = PlanarDistance(transform.position, target.transform.position);
        if (dist > AttackRange * 1.25f || !HasClearLineLocal(target.transform.position))
        {
            State = NpcState.Chasing;
            return;
        }

        FaceTowards(target.transform.position, dt);

        if (Time.time >= _nextAttackAt)
        {
            // Occasional tactical Guard before swinging if player is actively aggressive in close range
            if (Time.time >= _nextGuardAllowedAt && target.IsHitWindowOpen && Random.value < reactiveGuardChanceOnCooldown)
            {
                EnterGuardAuthority(guardDuration * 0.85f);
                return;
            }

            int idx = animatorBridge != null ? animatorBridge.PickNextAttackIndex() : 0;
            StartMeleeSwingAuthority(target, idx, isCounter: false);
        }
    }

    private void TickReturningAuthority(float dt)
    {
        if (IsMovementAnimationLocked()) return;

        float remaining = MoveTowardsLocal(HomePosition, returnMoveSpeed, dt);
        if (remaining <= 0.45f)
        {
            FinishReturnHome();
            return;
        }

        // Stuck-recovery: if trapped behind U-shaped geometry on friend's map for > 3.5s, snap home cleanly
        if (PlanarDistance(transform.position, _lastReturnCheckPos) < 0.25f)
        {
            _returnStuckTimer += dt;
            if (_returnStuckTimer >= 3.5f)
            {
                Vector3 homePos = HomePosition;
                SnapToGround(ref homePos, searchUp: 4.0f, searchDown: 15.0f);
                transform.position = homePos;
                FinishReturnHome();
            }
        }
        else
        {
            _returnStuckTimer = 0f;
            _lastReturnCheckPos = transform.position;
        }
    }

    private void FinishReturnHome()
    {
        CurrentHealth = MaxHealth;
        TargetId = default;
        _consecutiveHits = 0;
        _returnStuckTimer = 0f;
        _idleTimer = Random.Range(personalIdleWait.x, personalIdleWait.y);
        State = NpcState.Idle;
    }

    private void BeginReturnAuthority()
    {
        if (IsGuarding) ExitGuardAuthority();
        TargetId = default;
        _consecutiveHits = 0;
        _returnStuckTimer = 0f;
        _lastReturnCheckPos = transform.position;
        State = NpcState.Returning;
    }

    private bool IsMovementAnimationLocked()
    {
        if (_isSwinging || Time.time < _attackLockUntil || Time.time < _hitFlinchUntil)
            return true;
        if (animatorBridge != null && (animatorBridge.IsInAttackState || animatorBridge.IsInHitState))
            return true;
        return false;
    }

    // ------------------------------------------------------------------ Melee Swing (Impact-Timed & Dodgeable)

    private void StartMeleeSwingAuthority(PlayerCombat target, int attackIndex, bool isCounter)
    {
        if (!IsAlive || target == null) return;
        if (_swingRoutine != null) StopCoroutine(_swingRoutine);

        _isSwinging = true;
        _attackLockUntil = Time.time + minAttackAnimLock;
        _nextAttackAt = Time.time + AttackCooldown;
        if (isCounter)
            _hyperArmorUntil = Time.time + 1.45f;

        RPC_MonsterPlayAttack(attackIndex, isCounter);
        _swingRoutine = StartCoroutine(MeleeSwingImpactRoutine(target, isCounter));
    }

    private IEnumerator MeleeSwingImpactRoutine(PlayerCombat target, bool isCounter)
    {
        float delay = isCounter ? counterImpactDelay : attackImpactDelay;

        // Ensure Counter impact never lands inside PlayerCombat's 0.4 * AttackCooldown anti-spam rejection window!
        if (isCounter)
        {
            float safeTime = _lastDamageDealtToPlayerStamp + (AttackCooldown * 0.42f) + 0.06f;
            float neededDelay = safeTime - Time.time;
            if (neededDelay > delay)
                delay = Mathf.Min(neededDelay, 0.72f);
        }

        yield return new WaitForSeconds(delay);

        // Apply damage ONLY at actual swing impact, and only if player is still in strike cone & range!
        if (IsAlive && !IsStunnedNet && target != null && target.IsSpawned && !target.IsDead && !SafeZone.IsPlayerInAnySafeZone(target))
        {
            Vector3 toTarget = target.transform.position - transform.position;
            toTarget.y = 0f;
            float dist = toTarget.magnitude;
            bool inFront = dist < 0.6f || Vector3.Dot(transform.forward, toTarget / dist) >= 0.20f;

            if (dist <= AttackRange + impactReachSlack && inFront)
            {
                // Clamp Counter damage within PlayerCombat's accepted ceiling (ScaledAttackDamage + 4 for normal mobs)
                int rawCounter = Mathf.RoundToInt(AttackDamage * counterDamageMultiplier);
                int maxAccepted = IsBoss ? AttackDamage * 2 : AttackDamage + 4;
                int dmg = isCounter ? Mathf.Min(rawCounter, maxAccepted) : AttackDamage;

                _lastDamageDealtToPlayerStamp = Time.time;
                target.RPC_ReceiveNpcDamage(Object.Id, dmg);
            }
        }

        // Keep feet planted until the Animator finishes the Attack state (safety cap 1.35s)
        float waitEnd = Time.time + 1.35f;
        while (Time.time < waitEnd && IsAlive && !IsStunnedNet)
        {
            if (Time.time >= _attackLockUntil && (animatorBridge == null || !animatorBridge.IsInAttackState))
                break;
            yield return null;
        }

        _isSwinging = false;
        _swingRoutine = null;
    }

    // ------------------------------------------------------------------ Anti-Stunlock Poise, Guard, Counter & Guard Break

    private void HandleBaseOnHit()
    {
        if (!IsSpawned || !IsAlive) return;

        // Only State Authority decides Guard / Counter / Guard Break / Poise; RPCs sync visuals to all peers
        if (!Object.HasStateAuthority) return;

        PlayerCombat attacker = FindLikelyAttacker(7.5f);
        if (attacker != null)
            _lastKnownAttacker = attacker;
        if (attacker != null && !TargetId.IsValid)
        {
            TargetId = attacker.Object.Id;
            if (State == NpcState.Idle || State == NpcState.Patrol)
                State = NpcState.Chasing;
        }

        // Case 1: Player hit the monster while Guarding!
        if (IsGuarding)
        {
            bool isComboFinisher = attacker != null && attacker.ComboStep >= 3;
            if (isComboFinisher)
            {
                // GUARD BREAK! Combo 3 shatters Guard -> Stun 1.5s + Orbiting Stun VFX!
                ExitGuardAuthority();
                IsStunnedNet = true;
                _stunUntil = Time.time + guardBreakStunDuration;
                _consecutiveHits = 0;
                RPC_MonsterGuardFeedback((byte)MonsterGuardFeedback.GuardBreak);
                if (verboseLogs) Debug.Log($"[MonsterEnemyNPC] {name} GUARD BROKEN by Combo 3! Stunned for {guardBreakStunDuration:0.0}s.");
            }
            else
            {
                // BLOCKED! Spamming Combo 1/2 into Guard triggers immediate Counter-Attack!
                ExitGuardAuthority();
                RPC_MonsterGuardFeedback((byte)MonsterGuardFeedback.Blocked);
                if (attacker != null && !attacker.IsDead && !SafeZone.IsPlayerInAnySafeZone(attacker))
                {
                    FaceInstant(attacker.transform.position);
                    int counterIdx = animatorBridge != null && animatorBridge.AttackVariantCount >= 2 ? 1 : 0;
                    StartMeleeSwingAuthority(attacker, counterIdx, isCounter: true);
                    if (verboseLogs) Debug.Log($"[MonsterEnemyNPC] {name} BLOCKED hit and launched COUNTER-ATTACK!");
                }
            }
            return;
        }

        // Case 2: Hit while Stunned -> always flinch & flash
        if (IsStunnedNet)
        {
            RPC_MonsterHitFeedback(flinch: true);
            return;
        }

        // Track consecutive hits for Anti-Stunlock Poise
        if (Time.time - _lastHitStamp > comboResetWindow)
            _consecutiveHits = 0;
        _lastHitStamp = Time.time;
        _consecutiveHits++;

        // Hyper Armor (mid-counter or Boss Super Armor) -> no flinch, just hit flash
        if (Time.time < _hyperArmorUntil || (animatorBridge != null && !animatorBridge.CanFlinch))
        {
            RPC_MonsterHitFeedback(flinch: false);
            return;
        }

        // Anti-Stunlock Threshold reached -> Raise Guard instead of getting spam-locked!
        if (_consecutiveHits >= hitsBeforeGuard && Time.time >= _nextGuardAllowedAt && !_isSwinging)
        {
            _consecutiveHits = 0;
            EnterGuardAuthority(guardDuration);
            RPC_MonsterHitFeedback(flinch: false);
            return;
        }

        // Standard Hit_React flinch (with brief pause so it doesn't slide)
        _hitFlinchUntil = Time.time + hitFlinchPause;
        RPC_MonsterHitFeedback(flinch: true);
    }

    private void HandleBaseOnDeath()
    {
        _deathStamp = Time.time;
        _isSwinging = false;
        if (_swingRoutine != null)
        {
            StopCoroutine(_swingRoutine);
            _swingRoutine = null;
        }

        if (Object != null && Object.IsValid && Object.HasStateAuthority)
        {
            if (IsGuarding) ExitGuardAuthority();
            IsStunnedNet = false;
            RollAndSpawnConfiguredLoot();
        }

        SetGuardVfxVisible(false);
        SetStunVfxVisible(false);
        animatorBridge?.SetDead(true);
        PlayClip(animatorBridge?.Config?.deathClip);

        PlayerCombat killer = _lastKnownAttacker ?? ResolveCurrentTarget() ?? FindLikelyAttacker(12f);
        MonsterSystemManager.NotifyMonsterKilled(this, killer);
    }

    /// <summary>
    /// Rolls this monster's loose-coupled <see cref="MonsterAnimationConfig.lootDrops"/> table on State Authority,
    /// supporting IndependentPerItem / SinglePickTable modes, genuine "No Drop" rolls, Raycasted ground placement
    /// (never sinks under hills/terrain), and optional Gold drops.
    /// </summary>
    private void RollAndSpawnConfiguredLoot()
    {
        if (_lootDropped || Runner == null || !Runner.IsRunning) return;
        _lootDropped = true;

        MonsterAnimationConfig cfg = animatorBridge != null ? animatorBridge.Config : null;
        if (cfg == null) return;

        MonsterSystemManager mgr = MonsterSystemManager.Active;
        float dropRateMult = mgr != null ? Mathf.Max(0f, mgr.globalDropRateMultiplier) : 1.0f;

        // 1. Optional Gold Drop (0% = disabled by default)
        if (cfg.goldDropChancePercent > 0f && dropRateMult > 0f)
        {
            float effectiveGoldChance = Mathf.Clamp(cfg.goldDropChancePercent * dropRateMult, 0f, 100f);
            if (Random.value * 100f < effectiveGoldChance)
            {
                int minG = Mathf.Min(cfg.minGold, cfg.maxGold);
                int maxG = Mathf.Max(cfg.minGold, cfg.maxGold);
                int goldAmount = Random.Range(minG, maxG + 1);
                if (goldAmount > 0)
                {
                    PlayerCombat killer = _lastKnownAttacker ?? ResolveCurrentTarget() ?? FindLikelyAttacker(12f);
                    if (killer != null && killer.IsSpawned)
                    {
                        killer.AddGold(goldAmount, name);
                        if (verboseLogs) Debug.Log($"[MonsterEnemyNPC] {name} awarded {goldAmount}G to {killer.name}.");
                    }
                }
            }
        }

        // 2. Item Loot Table (ItemData chosen by user in Config_<Monster>.asset)
        if (cfg.lootDrops == null || cfg.lootDrops.Count == 0 || dropRateMult <= 0f)
            return;

        WorldItem worldItemPrefab = (mgr != null && mgr.sharedWorldItemPrefab != null)
            ? mgr.sharedWorldItemPrefab
            : BaseLootPrefabField?.GetValue(this) as WorldItem;

        if (worldItemPrefab == null)
        {
            if (verboseLogs) Debug.LogWarning($"[MonsterEnemyNPC] {name} has loot configured but no WorldItem prefab assigned.");
            return;
        }

        int spawnedCount = 0;

        if (cfg.lootRollMode == MonsterLootRollMode.SinglePickTable)
        {
            float roll = Random.Range(0f, 100f);
            float cursor = 0f;
            for (int i = 0; i < cfg.lootDrops.Count; i++)
            {
                MonsterLootDropEntry entry = cfg.lootDrops[i];
                if (entry == null || entry.item == null || entry.dropRatePercent <= 0f) continue;

                float effectiveRate = Mathf.Max(0f, entry.dropRatePercent * dropRateMult);
                cursor += effectiveRate;
                if (roll < cursor)
                {
                    SpawnSingleLootDrop(worldItemPrefab, entry, spawnedCount);
                    spawnedCount++;
                    break;
                }
            }
        }
        else
        {
            int maxCap = cfg.maxDropsPerKill > 0 ? cfg.maxDropsPerKill : int.MaxValue;
            for (int i = 0; i < cfg.lootDrops.Count; i++)
            {
                if (spawnedCount >= maxCap) break;
                MonsterLootDropEntry entry = cfg.lootDrops[i];
                if (entry == null || entry.item == null || entry.dropRatePercent <= 0f) continue;

                float effectiveRate = Mathf.Clamp(entry.dropRatePercent * dropRateMult, 0f, 100f);
                if (Random.value * 100f < effectiveRate)
                {
                    SpawnSingleLootDrop(worldItemPrefab, entry, spawnedCount);
                    spawnedCount++;
                }
            }
        }

        if (verboseLogs)
            Debug.Log($"[MonsterEnemyNPC] {name} loot roll finished -> {spawnedCount} item(s) dropped.");
    }

    private void SpawnSingleLootDrop(WorldItem worldItemPrefab, MonsterLootDropEntry entry, int dropIndex)
    {
        int minQty = Mathf.Max(1, Mathf.Min(entry.minAmount, entry.maxAmount));
        int maxQty = Mathf.Max(minQty, Mathf.Max(entry.minAmount, entry.maxAmount));
        int qty = Random.Range(minQty, maxQty + 1);

        // Gentle radial scatter so multiple drops never stack directly on top of each other
        float angle = (dropIndex * 2.1f) + Random.Range(0f, 1.2f);
        float radius = 0.65f + (dropIndex * 0.22f) + Random.Range(0f, 0.35f);
        Vector3 candidate = new Vector3(
            transform.position.x + Mathf.Cos(angle) * radius,
            transform.position.y,
            transform.position.z + Mathf.Sin(angle) * radius);

        Vector3 dropPos = ResolveLootGroundPoint(candidate);
        WorldItem.Spawn(Runner, worldItemPrefab, dropPos, entry.item, qty);
    }

    private Vector3 ResolveLootGroundPoint(Vector3 candidate)
    {
        Vector3 origin = new Vector3(candidate.x, candidate.y + 3.0f, candidate.z);
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, s_monsterCastBuf, 14.0f, ~0, QueryTriggerInteraction.Ignore);
        float bestY = float.MinValue;
        bool found = false;

        for (int i = 0; i < count; i++)
        {
            RaycastHit h = s_monsterCastBuf[i];
            if (h.collider == null || h.collider == BodyCollider || h.normal.y < 0.45f) continue;
            if (h.collider.GetComponentInParent<EnemyNPC>() != null) continue;
            if (h.collider.GetComponentInParent<PlayerCombat>() != null) continue;

            if (h.point.y > bestY)
            {
                bestY = h.point.y;
                found = true;
            }
        }

        float footY = found ? bestY : (transform.position.y - GetGroundPivotHeight());
        return new Vector3(candidate.x, footY + 0.45f, candidate.z);
    }

    private void EnterGuardAuthority(float duration)
    {
        if (!Object.HasStateAuthority || !IsAlive || IsGuarding) return;
        _baseDefense = Defense;
        int scaledGuardDef = Mathf.Max(guardBonusDefense, Mathf.RoundToInt(AttackDamage * 1.6f));
        Defense = _baseDefense + scaledGuardDef;
        IsGuarding = true;
        _guardUntil = Time.time + duration;
        _nextGuardAllowedAt = Time.time + duration + guardCooldown;
        _hyperArmorUntil = Time.time + duration;
        RPC_MonsterGuardFeedback((byte)MonsterGuardFeedback.GuardRaised);
    }

    private void ExitGuardAuthority()
    {
        if (!Object.HasStateAuthority) return;
        if (IsGuarding)
        {
            IsGuarding = false;
            Defense = _baseDefense;
        }
    }

    // ------------------------------------------------------------------ RPCs (All Peers)

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_MonsterPlayAttack(int attackIndex, NetworkBool isCounter)
    {
        animatorBridge?.TriggerAttack(attackIndex);
        PlayRandomClip(animatorBridge?.Config?.attackSwingClips);
        if (isCounter)
        {
            FloatingDamagePopup.SpawnWarning(transform.position + Vector3.up * 2.4f, "COUNTER!", new Color(1.0f, 0.42f, 0.12f, 1f));
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_MonsterHitFeedback(NetworkBool flinch)
    {
        if (animatorBridge == null) return;
        if (flinch) animatorBridge.TriggerHit();
        animatorBridge.PlayHitFlash();
        PlayRandomClip(animatorBridge.Config?.hitReactClips);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_MonsterGuardFeedback(byte feedbackByte)
    {
        var kind = (MonsterGuardFeedback)feedbackByte;
        Vector3 topPos = transform.position + Vector3.up * 2.45f;

        switch (kind)
        {
            case MonsterGuardFeedback.GuardRaised:
                SetGuardVfxVisible(true);
                PlayClip(animatorBridge?.Config?.guardRaiseClip);
                FloatingDamagePopup.SpawnWarning(topPos, "GUARD!", new Color(1.0f, 0.78f, 0.22f, 1f));
                break;

            case MonsterGuardFeedback.Blocked:
                animatorBridge?.PlayBlockFlash();
                PlayClip(animatorBridge?.Config?.guardBlockClip);
                FloatingDamagePopup.SpawnWarning(topPos, "BLOCKED!", new Color(0.95f, 0.85f, 0.25f, 1f));
                break;

            case MonsterGuardFeedback.GuardBreak:
                SetGuardVfxVisible(false);
                SetStunVfxVisible(true);
                animatorBridge?.PlayHitFlash();
                animatorBridge?.SetStunned(true);
                PlayClip(animatorBridge?.Config?.guardBreakClip);
                FloatingDamagePopup.SpawnWarning(topPos, "GUARD BREAK!", new Color(1.0f, 0.30f, 0.15f, 1f));
                break;
        }
    }

    private void OnGuardVisualStateChanged()
    {
        SetGuardVfxVisible(IsGuarding && IsAlive);
    }

    private void OnStunVisualStateChanged()
    {
        bool stunned = IsStunnedNet && IsAlive;
        animatorBridge?.SetStunned(stunned);
        SetStunVfxVisible(stunned);
    }

    // ------------------------------------------------------------------ Shared-Material Combat VFX (Guard + Stun)

    private static void EnsureSharedVfxMaterials()
    {
        if (s_guardBarrierMat != null && s_guardRingMat != null && s_stunOrbMat != null) return;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) return;

        if (s_guardBarrierMat == null)
        {
            s_guardBarrierMat = new Material(shader) { name = "Mat_MonsterGuardBarrier_Shared" };
            s_guardBarrierMat.color = new Color(1.0f, 0.68f, 0.16f, 0.30f);
        }
        if (s_guardRingMat == null)
        {
            s_guardRingMat = new Material(shader) { name = "Mat_MonsterGuardRing_Shared" };
            s_guardRingMat.color = new Color(1.0f, 0.78f, 0.22f, 0.55f);
        }
        if (s_stunOrbMat == null)
        {
            s_stunOrbMat = new Material(shader) { name = "Mat_MonsterStunOrb_Shared" };
            s_stunOrbMat.color = new Color(1.0f, 0.90f, 0.25f, 0.95f);
        }
    }

    private void BuildCombatVfx()
    {
        EnsureSharedVfxMaterials();

        // 1. Guard Shield VFX
        if (_guardVfxRoot == null)
        {
            Transform existing = transform.Find("MonsterGuardVFX");
            if (existing != null)
            {
                _guardVfxRoot = existing.gameObject;
                _guardShieldMesh = existing.Find("GuardBarrier");
                _guardRingMesh = existing.Find("GuardRing");
                _guardVfxRoot.SetActive(false);
            }
            else
            {
                _guardVfxRoot = new GameObject("MonsterGuardVFX");
                _guardVfxRoot.transform.SetParent(transform, false);
                _guardVfxRoot.transform.localPosition = Vector3.zero;

                GameObject barrier = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                barrier.name = "GuardBarrier";
                var col = barrier.GetComponent<Collider>();
                if (col != null) Destroy(col);
                barrier.transform.SetParent(_guardVfxRoot.transform, false);
                barrier.transform.localPosition = new Vector3(0f, 0.10f, 0.55f);
                barrier.transform.localScale = new Vector3(1.15f, 1.35f, 0.35f);
                var br = barrier.GetComponent<Renderer>();
                if (br != null)
                {
                    br.shadowCastingMode = ShadowCastingMode.Off;
                    br.receiveShadows = false;
                    if (s_guardBarrierMat != null) br.sharedMaterial = s_guardBarrierMat;
                }
                _guardShieldMesh = barrier.transform;

                GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                ring.name = "GuardRing";
                var rcol = ring.GetComponent<Collider>();
                if (rcol != null) Destroy(rcol);
                ring.transform.SetParent(_guardVfxRoot.transform, false);
                ring.transform.localPosition = new Vector3(0f, -0.96f, 0f);
                ring.transform.localScale = new Vector3(1.65f, 0.02f, 1.65f);
                var rr = ring.GetComponent<Renderer>();
                if (rr != null)
                {
                    rr.shadowCastingMode = ShadowCastingMode.Off;
                    rr.receiveShadows = false;
                    if (s_guardRingMat != null) rr.sharedMaterial = s_guardRingMat;
                }
                _guardRingMesh = ring.transform;

                _guardVfxRoot.SetActive(false);
            }
        }

        // 2. Orbiting Stun Halo VFX (makes Guard Break Stun unmistakable even without a custom Stun animation clip)
        if (_stunVfxRoot == null)
        {
            Transform existingStun = transform.Find("MonsterStunVFX");
            if (existingStun != null)
            {
                _stunVfxRoot = existingStun.gameObject;
                _stunOrbitPivot = existingStun.Find("OrbitPivot") ?? existingStun;
                _stunVfxRoot.SetActive(false);
            }
            else
            {
                _stunVfxRoot = new GameObject("MonsterStunVFX");
                _stunVfxRoot.transform.SetParent(transform, false);
                float headLocalY = 1.15f;
                _stunVfxRoot.transform.localPosition = new Vector3(0f, headLocalY, 0f);

                var pivotGo = new GameObject("OrbitPivot");
                pivotGo.transform.SetParent(_stunVfxRoot.transform, false);
                _stunOrbitPivot = pivotGo.transform;

                const int orbCount = 3;
                const float orbitRadius = 0.42f;
                for (int i = 0; i < orbCount; i++)
                {
                    float ang = (i / (float)orbCount) * Mathf.PI * 2f;
                    GameObject orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    orb.name = $"StunStar_{i}";
                    var ocol = orb.GetComponent<Collider>();
                    if (ocol != null) Destroy(ocol);
                    orb.transform.SetParent(_stunOrbitPivot, false);
                    orb.transform.localPosition = new Vector3(Mathf.Cos(ang) * orbitRadius, 0f, Mathf.Sin(ang) * orbitRadius);
                    orb.transform.localScale = Vector3.one * 0.14f;
                    var or = orb.GetComponent<Renderer>();
                    if (or != null)
                    {
                        or.shadowCastingMode = ShadowCastingMode.Off;
                        or.receiveShadows = false;
                        if (s_stunOrbMat != null) or.sharedMaterial = s_stunOrbMat;
                    }
                }

                _stunVfxRoot.SetActive(false);
            }
        }
    }

    private void SetGuardVfxVisible(bool visible)
    {
        if (_guardVfxRoot != null && _guardVfxRoot.activeSelf != visible)
            _guardVfxRoot.SetActive(visible);
    }

    private void SetStunVfxVisible(bool visible)
    {
        if (_stunVfxRoot != null && _stunVfxRoot.activeSelf != visible)
            _stunVfxRoot.SetActive(visible);
    }

    // ------------------------------------------------------------------ Optional 3D Spatial Audio

    private void EnsureAudioSource()
    {
        if (_audioSource != null) return;
        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.spatialBlend = 1.0f;
            _audioSource.minDistance = 3.0f;
            _audioSource.maxDistance = 22.0f;
            _audioSource.rolloffMode = AudioRolloffMode.Linear;
        }
    }

    private void PlayClip(AudioClip clip)
    {
        if (clip == null || _audioSource == null) return;
        float vol = animatorBridge?.Config != null ? animatorBridge.Config.audioVolume : 0.85f;
        _audioSource.pitch = Random.Range(0.94f, 1.06f);
        _audioSource.PlayOneShot(clip, vol);
    }

    private void PlayRandomClip(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0) return;
        AudioClip pick = clips[Random.Range(0, clips.Length)];
        PlayClip(pick);
    }

    // ------------------------------------------------------------------ Targeting, Movement & Terrain-Safe Grounding

    private bool TryAcquireSoloTarget()
    {
        PlayerCombat best = null;
        float bestDist = personalAggroRadius;
        for (int i = 0; i < PlayerCombat.All.Count; i++)
        {
            var p = PlayerCombat.All[i];
            if (p == null || !p.IsSpawned || p.IsDead) continue;
            if (SafeZone.IsPlayerInAnySafeZone(p)) continue;

            float d = PlanarDistance(transform.position, p.transform.position);
            if (d <= bestDist) { bestDist = d; best = p; }
        }
        if (best == null) return false;

        TargetId = best.Object.Id;
        State = NpcState.Chasing;
        return true;
    }

    private PlayerCombat ResolveCurrentTarget()
    {
        if (!TargetId.IsValid) return null;
        for (int i = 0; i < PlayerCombat.All.Count; i++)
        {
            var p = PlayerCombat.All[i];
            if (p != null && p.IsSpawned && p.Object.Id == TargetId) return p;
        }
        return null;
    }

    /// <summary>
    /// Co-op accurate attacker resolver: prioritizes players currently in their active melee hit window
    /// or facing this monster with a chained combo, falling back to nearest living player.
    /// </summary>
    private PlayerCombat FindLikelyAttacker(float maxDist)
    {
        PlayerCombat bestSwing = null;
        float bestSwingScore = float.MinValue;

        PlayerCombat nearest = null;
        float nearestDist = maxDist;

        for (int i = 0; i < PlayerCombat.All.Count; i++)
        {
            var p = PlayerCombat.All[i];
            if (p == null || !p.IsSpawned || p.IsDead) continue;

            float d = PlanarDistance(transform.position, p.transform.position);
            if (d > maxDist) continue;

            if (d < nearestDist)
            {
                nearestDist = d;
                nearest = p;
            }

            Vector3 toMe = transform.position - p.transform.position;
            toMe.y = 0f;
            float facingDot = toMe.sqrMagnitude > 0.01f ? Vector3.Dot(p.transform.forward, toMe.normalized) : 1f;

            float score = 0f;
            if (p.IsHitWindowOpen) score += 100f;
            if (p.ComboStep >= 3) score += 25f;
            else if (p.ComboStep == 2) score += 10f;
            if (facingDot > 0.25f) score += 15f * facingDot;
            score -= d * 2f;

            if (p.IsHitWindowOpen && score > bestSwingScore)
            {
                bestSwingScore = score;
                bestSwing = p;
            }
        }

        if (bestSwing != null) return bestSwing;
        return nearest ?? ResolveCurrentTarget();
    }

    private float MoveTowardsLocal(Vector3 destination, float speed, float dt)
    {
        Vector3 pos = transform.position;
        Vector3 delta = destination - pos;
        delta.y = 0f;
        float dist = delta.magnitude;
        if (dist < 0.0001f) return 0f;

        Vector3 desiredDir = delta / dist;
        Vector3 steered = ComputeAvoidanceDir(pos, desiredDir, Mathf.Min(dist, obstacleProbeDist));
        Vector3 sep = ComputeSeparation(pos);
        Vector3 finalDir = steered + sep * 0.55f;
        finalDir.y = 0f;
        finalDir = finalDir.sqrMagnitude > 0.0001f ? finalDir.normalized : steered;

        float step = Mathf.Min(speed * dt, dist);
        Vector3 nextPos = pos + finalDir * step;

        ResolveObstaclePenetrationLocal(ref nextPos);
        EnforceSafeZoneLocal(ref nextPos);
        nextPos.y = pos.y;
        SnapToGround(ref nextPos, searchUp: 3.0f, searchDown: 12.0f);

        transform.position = nextPos;
        if (dt > 0.0001f)
            _currentMoveSpeed = PlanarDistance(pos, nextPos) / dt;

        FaceDirection(finalDir, dt);
        return PlanarDistance(nextPos, destination);
    }

    private float GetGroundPivotHeight()
    {
        if (BodyCollider is CapsuleCollider cap)
            return Mathf.Max(0.2f, (cap.height * 0.5f - cap.center.y) * Mathf.Abs(transform.lossyScale.y));
        return Mathf.Max(0.2f, 1.0f * Mathf.Abs(transform.lossyScale.y));
    }

    private bool SnapToGround(ref Vector3 pos, float searchUp = 3.0f, float searchDown = 12.0f)
    {
        Vector3 origin = new Vector3(pos.x, pos.y + searchUp, pos.z);
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, s_monsterCastBuf, searchUp + searchDown, ~0, QueryTriggerInteraction.Ignore);
        float bestY = float.MinValue;
        bool found = false;

        for (int i = 0; i < count; i++)
        {
            RaycastHit h = s_monsterCastBuf[i];
            if (h.collider == null || h.collider == BodyCollider) continue;
            if (h.collider.GetComponentInParent<EnemyNPC>() != null) continue;
            if (h.collider.GetComponentInParent<PlayerCombat>() != null) continue;

            if (h.point.y > bestY && h.normal.y >= 0.45f)
            {
                bestY = h.point.y;
                found = true;
            }
        }

        if (found)
        {
            pos.y = bestY + GetGroundPivotHeight();
            return true;
        }
        return false;
    }

    private static void EnforceSafeZoneLocal(ref Vector3 candidatePos)
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

    private Vector3 ComputeAvoidanceDir(Vector3 pos, Vector3 desiredDir, float checkDist)
    {
        if (!ProbeObstacleLocal(pos, desiredDir, checkDist, out RaycastHit directHit))
            return desiredDir;

        if (Time.time > _avoidSignUntil || _avoidDirSign == 0)
        {
            Vector3 right = new Vector3(desiredDir.z, 0f, -desiredDir.x);
            _avoidDirSign = Vector3.Dot(directHit.normal, right) >= 0f ? 1 : -1;
            _avoidSignUntil = Time.time + 1.15f;
        }

        float[] angles = { 30f, 55f, 80f, 110f, 140f };
        int[] signs = { _avoidDirSign, -_avoidDirSign };

        for (int s = 0; s < signs.Length; s++)
        {
            for (int a = 0; a < angles.Length; a++)
            {
                Vector3 candidate = Quaternion.Euler(0f, angles[a] * signs[s], 0f) * desiredDir;
                candidate.y = 0f;
                candidate.Normalize();
                if (!ProbeObstacleLocal(pos, candidate, checkDist, out _))
                {
                    _avoidDirSign = signs[s];
                    _avoidSignUntil = Time.time + 0.9f;
                    return candidate;
                }
            }
        }
        return desiredDir;
    }

    private bool ProbeObstacleLocal(Vector3 pos, Vector3 dir, float distance, out RaycastHit closest)
    {
        closest = default;
        float footY = pos.y - GetGroundPivotHeight();
        Vector3 origin = new Vector3(pos.x, footY + 0.95f, pos.z);
        int count = Physics.SphereCastNonAlloc(origin, obstacleProbeRad, dir, s_monsterCastBuf, distance, ~0, QueryTriggerInteraction.Ignore);
        float bestDist = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit h = s_monsterCastBuf[i];
            if (!IsObstacleCollider(h.collider, footY)) continue;
            if (h.distance < bestDist) { bestDist = h.distance; closest = h; found = true; }
        }
        return found;
    }

    private bool HasClearLineLocal(Vector3 targetPos)
    {
        float footY = transform.position.y - GetGroundPivotHeight();
        Vector3 origin = new Vector3(transform.position.x, footY + 1.0f, transform.position.z);
        Vector3 end = new Vector3(targetPos.x, targetPos.y + 0.1f, targetPos.z);
        Vector3 delta = end - origin;
        float dist = delta.magnitude;
        if (dist < 0.05f) return true;

        int count = Physics.RaycastNonAlloc(origin, delta / dist, s_monsterCastBuf, dist, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (IsObstacleCollider(s_monsterCastBuf[i].collider, footY))
                return false;
        }
        return true;
    }

    private Vector3 ComputeSeparation(Vector3 pos)
    {
        Vector3 push = Vector3.zero;
        for (int i = 0; i < All.Count; i++)
        {
            EnemyNPC other = All[i];
            if (other == null || other == this || !other.IsAlive) continue;
            Vector3 diff = pos - other.transform.position;
            diff.y = 0f;
            float d = diff.magnitude;
            if (d > 0.001f && d < allySepRadius)
                push += (diff / d) * ((allySepRadius - d) / allySepRadius);
        }
        return push;
    }

    private bool ResolveObstaclePenetrationLocal(ref Vector3 candidatePos)
    {
        if (BodyCollider == null) return false;
        float footY = candidatePos.y - GetGroundPivotHeight();
        Vector3 center = new Vector3(candidatePos.x, footY + 1.0f, candidatePos.z);
        int count = Physics.OverlapSphereNonAlloc(center, 1.4f, s_monsterOverlapBuf, ~0, QueryTriggerInteraction.Ignore);
        bool pushed = false;

        for (int i = 0; i < count; i++)
        {
            Collider col = s_monsterOverlapBuf[i];
            if (!IsObstacleCollider(col, footY)) continue;

            if (Physics.ComputePenetration(
                BodyCollider, candidatePos, transform.rotation,
                col, col.transform.position, col.transform.rotation,
                out Vector3 penDir, out float penDist))
            {
                penDir.y = 0f;
                if (penDir.sqrMagnitude > 0.0001f)
                {
                    candidatePos += penDir.normalized * (penDist + 0.02f);
                    pushed = true;
                }
            }
        }
        return pushed;
    }

    /// <summary>
    /// Elevation-safe obstacle check: ignores TerrainCollider and any floor slab at/below step height
    /// relative to the monster's current foot position (works on hills, bridges, and upper floors!).
    /// </summary>
    private bool IsObstacleCollider(Collider col, float referenceFootY)
    {
        if (col == null || col.isTrigger || col == BodyCollider) return false;
        if (col is TerrainCollider) return false;
        if (col.GetComponentInParent<EnemyNPC>() != null) return false;
        if (col.GetComponentInParent<PlayerCombat>() != null) return false;
        return col.bounds.max.y > referenceFootY + walkableStepHeight;
    }

    private void FaceTowards(Vector3 worldPoint, float dt)
    {
        Vector3 d = worldPoint - transform.position;
        d.y = 0f;
        if (d.sqrMagnitude > 0.0001f) FaceDirection(d.normalized, dt);
    }

    private void FaceInstant(Vector3 worldPoint)
    {
        Vector3 d = worldPoint - transform.position;
        d.y = 0f;
        if (d.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
    }

    private void FaceDirection(Vector3 dir, float dt)
    {
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir, Vector3.up), 1f - Mathf.Exp(-rotationSpeed * dt));
    }

    private static float PlanarDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private void EnsureBridgeAndListeners()
    {
        if (animatorBridge == null) animatorBridge = GetComponent<MonsterAnimatorBridge>();
        if (animatorBridge == null) animatorBridge = GetComponentInChildren<MonsterAnimatorBridge>();

        if (!_listenersWired)
        {
            _listenersWired = true;
            onHit.RemoveListener(HandleBaseOnHit);
            onHit.AddListener(HandleBaseOnHit);
            onDeath.RemoveListener(HandleBaseOnDeath);
            onDeath.AddListener(HandleBaseOnDeath);
        }
    }

    private void RemoveListeners()
    {
        if (!_listenersWired) return;
        _listenersWired = false;
        onHit.RemoveListener(HandleBaseOnHit);
        onDeath.RemoveListener(HandleBaseOnDeath);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.35f, 0.15f, 0.7f);
        Gizmos.DrawWireSphere(transform.position, personalAggroRadius);
        Gizmos.color = new Color(1f, 0.85f, 0.15f, 0.65f);
        Gizmos.DrawWireSphere(transform.position, AttackRange);
    }
}
