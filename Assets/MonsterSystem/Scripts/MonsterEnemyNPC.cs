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
    [Header("1. ตัวเชื่อมระบบมอนสเตอร์")]
    [SerializeField] private MonsterAnimatorBridge animatorBridge;

    [Header("2. การลากตีทีละตัว & การเคลื่อนที่")]
    [Min(1f)] [SerializeField] private float personalAggroRadius = 5.5f;
    [Min(2f)] [SerializeField] private float personalLeashRadius = 20f;
    [Min(0.5f)] [SerializeField] private float personalPatrolRadius = 2.0f;
    [SerializeField] private Vector2 personalIdleWait = new Vector2(2.0f, 4.5f);
    [Min(0.5f)] [SerializeField] private float walkMoveSpeed = 1.6f;
    [Min(1f)] [SerializeField] private float runMoveSpeed = 4.2f;
    [Min(1f)] [SerializeField] private float returnMoveSpeed = 4.4f;
    [Min(1f)] [SerializeField] private float rotationSpeed = 10f;
    [Min(1f)] [SerializeField] private float corpseDespawnDelay = 4.0f;

    [Header("3. จังหวะฟันประชิด (กันสไลด์ & หลบ/Parry ได้)")]
    [Tooltip("เวลานับจากเริ่มง้างจนแขนฟันโดน (วินาที) ช่วงนี้ผู้เล่นกลิ้งหลบหรือกด Parry ได้")]
    [Min(0.05f)] [SerializeField] private float attackImpactDelay = 0.40f;
    [Min(0.05f)] [SerializeField] private float counterImpactDelay = 0.36f;
    [Tooltip("ระยะเผื่อตอนแขนฟันโดน (เมตร)")]
    [Min(0f)] [SerializeField] private float impactReachSlack = 0.85f;
    [Tooltip("เวลาล็อกขาขั้นต่ำระหว่างรอ Animator เข้าท่าโจมตี (วินาที)")]
    [Min(0.1f)] [SerializeField] private float minAttackAnimLock = 0.55f;

    [Header("4. ระบบกันสแปมตี ยกการ์ด สวนกลับ & การ์ดแตก")]
    [Tooltip("โดนตีสะดุ้งครบกี่ครั้งแล้วมอนสเตอร์จะยกการ์ดอัตโนมัติ")]
    [Min(1)] [SerializeField] private int hitsBeforeGuard = 3;
    [Min(0.5f)] [SerializeField] private float comboResetWindow = 3.0f;
    [Min(0.4f)] [SerializeField] private float guardDuration = 1.6f;
    [Min(0.5f)] [SerializeField] private float guardCooldown = 3.5f;
    [Range(0f, 1f)] [SerializeField] private float reactiveGuardChanceOnCooldown = 0.25f;
    [Tooltip("พลังป้องกันเพิ่มขณะยกการ์ด (ช่วยลดดาเมจประมาณ 75% และสเกลตามเลเวลอัตโนมัติ)")]
    [Min(5)] [SerializeField] private int guardBonusDefense = 24;
    [Tooltip("ตัวคูณดาเมจท่าสวนกลับ (COUNTER!) เมื่อผู้เล่นสแปมตีใส่การ์ด")]
    [Min(1f)] [SerializeField] private float counterDamageMultiplier = 1.25f;
    [Tooltip("ระยะเวลาติด Stun เมื่อผู้เล่นใช้ท่าตีหนัก / Combo 3 ตีการ์ดแตก (GUARD BREAK!)")]
    [Min(0.5f)] [SerializeField] private float guardBreakStunDuration = 1.5f;
    [Min(0.1f)] [SerializeField] private float hitFlinchPause = 0.35f;

    [Header("4.5 ฟีลลิ่งการต่อสู้: เดินคุมเชิง, แสงเตือน Parry & แรงกระแทก")]
    [Tooltip("เปิดการเดินคุมเชิงวนข้างระหว่างรอคูลดาวน์ฟัน (ไม่ยืนจ้องแข็งทื่อ)")]
    [SerializeField] private bool enableCombatStrafe = true;
    [Range(0.2f, 1.5f)] [SerializeField] private float combatStrafeSpeedFactor = 0.72f;
    [Tooltip("เปิดประกายแสงสีแดง-ทองเตือนก่อนดาเมจลง 0.22 วินาที เพื่อให้ผู้เล่นกะจังหวะ Perfect Parry ได้สนุก")]
    [SerializeField] private bool enableParryTelegraphGlint = true;
    [Tooltip("ระยะเวลาเปิดแสงเตือนก่อนดาเมจลง (วินาที — ตรงกับหน้าต่าง Parry 0.25 วิ ของเพื่อน)")]
    [Range(0.12f, 0.35f)] [SerializeField] private float parryTelegraphLeadTime = 0.22f;
    [Tooltip("ระยะผงะถอยหลังเล็กน้อยเมื่อโดนตีหนักหรือการ์ดแตก (เมตร)")]
    [Range(0f, 0.8f)] [SerializeField] private float microKnockbackDistance = 0.32f;

    [Header("5. การหลบสิ่งกีดขวาง พื้นเนิน & เว้นระยะเพื่อน")]
    [SerializeField] private float obstacleProbeDist = 2.2f;
    [SerializeField] private float obstacleProbeRad = 0.55f;
    [SerializeField] private float allySepRadius = 1.6f;
    [Tooltip("ความสูงขั้นบันได/เนินสูงสุดที่ถือว่าเดินขึ้นได้ ไม่ใช่กำแพง (เมตร)")]
    [Min(0.1f)] [SerializeField] private float walkableStepHeight = 0.40f;

    [Header("6. เชื่อมต่อระบบเพื่อน (Stealth / Parry / หลอดเลือด)")]
    [Tooltip("เปิดมุมมองสายตาด้านหน้า (องศา) เพื่อให้ผู้เล่นย่องเข้าด้านหลังกด Stealth Assassinate (3.0x) ของเพื่อนได้จริง")]
    [Range(45f, 180f)] [SerializeField] private float hybridVisionConeAngle = 110f;
    [Tooltip("ระยะได้ยินเสียงฝีเท้าด้านหลัง (เข้าใกล้กว่านี้จะรู้ตัว)")]
    [Min(0.5f)] [SerializeField] private float hybridBackHearingRadius = 1.15f;
    [Tooltip("ซ่อนวงสีส้มบนพื้นของ EnemyNPC เดิม (ปล่อยปิดเพื่อให้พื้นแมปเนียนแบบ MMO)")]
    [SerializeField] private bool showPersonalZoneGroundRing = false;
    [Tooltip("ความสูง Local Y ของหลอดเลือด World-Space (EnemyWorldHealthBar) เหนือหัวมอนสเตอร์")]
    [SerializeField] private float worldHealthBarLocalY = 1.18f;
    [Tooltip("ความสูง Local Y ของจุดล็อกเป้า (LockPoint) กลางอกมอนสเตอร์")]
    [SerializeField] private float lockPointLocalY = 0.45f;

    [Header("7. การตรวจสอบ (Debug)")]
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

    // ---- Cached Base Field Reflection (isolates EnemyNPC private fields without editing EnemyNPC.cs) ----
    private static readonly FieldInfo BaseAnimatorField =
        typeof(EnemyNPC).GetField("animator", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo BaseBodyRendererField =
        typeof(EnemyNPC).GetField("bodyRenderer", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo BaseLootPrefabField =
        typeof(EnemyNPC).GetField("lootPrefab", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo BaseStaggerEndTimeField =
        typeof(EnemyNPC).GetField("_staggerEndTime", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo BasePersonalZoneRingField =
        typeof(EnemyNPC).GetField("_personalZoneRing", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo BaseUseNavMeshField =
        typeof(EnemyNPC).GetField("useNavMeshWhenAvailable", BindingFlags.Instance | BindingFlags.NonPublic);

    // ---- Shared Static VFX Materials (zero material leaks across infinite respawns) ----
    private static Material s_guardBarrierMat;
    private static Material s_guardRingMat;
    private static Material s_stunOrbMat;
    private static Material s_parryGlintMat;
    private static Material s_bruteTelegraphMat;

    // ---- Runtime Private State ----
    private int _baseDefense;
    private int _consecutiveHits;
    private float _lastHitStamp = -100f;
    private float _lastDamageDealtToPlayerStamp = -100f;
    private float _nextAttackAt;
    private float _nextLungeAllowedAt;
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
    private int _strafeDirSign = 1;
    private float _nextStrafeFlipAt;
    private PlayerCombat _lastKnownAttacker;

    private Coroutine _swingRoutine;
    private Coroutine _parryGlintRoutine;
    private AudioSource _audioSource;
    private GameObject _guardVfxRoot;
    private Transform _guardShieldMesh;
    private Transform _guardRingMesh;
    private GameObject _stunVfxRoot;
    private Transform _stunOrbitPivot;
    private GameObject _parryGlintRoot;
    private GameObject _bruteRingRoot;

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
        if (_parryGlintRoutine != null)
        {
            StopCoroutine(_parryGlintRoutine);
            _parryGlintRoutine = null;
        }
        _isSwinging = false;
        SetGuardVfxVisible(false);
        SetStunVfxVisible(false);
        SetParryGlintVisible(false);
        SetBruteRingVisible(false);
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

        // Disable NavMeshAgent on center-pivoted MonsterEnemyNPC before base.Spawned() so it never warps pivot into floor
        BaseUseNavMeshField?.SetValue(this, false);
        base.Spawned();

        // Disable any NavMeshAgent if already attached
        var nav = GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (nav != null) nav.enabled = false;

        // Clean display name (removes "(Clone)" so Lock-On & EnemyWorldHealthBar display e.g. "Orc")
        if (animatorBridge != null && animatorBridge.Config != null && !string.IsNullOrWhiteSpace(animatorBridge.Config.monsterName))
            gameObject.name = animatorBridge.Config.monsterName;
        else if (gameObject.name.EndsWith("(Clone)"))
            gameObject.name = gameObject.name.Replace("(Clone)", string.Empty).Trim();

        ApplySpeciesStatMultipliers();
        AlignHybridWorldUiAndRings();

        if (Object.HasStateAuthority)
        {
            // Preserve CampId when spawned inside SandboxWorldSpawner (e.g. Camp 1) so Camp Clear Chest & Minimap work!
            // (MonsterSpawnZone explicitly sets CampId = 0 for standalone solo-pull zones.)
            Vector3 grounded = transform.position;
            if (SnapToGround(ref grounded, searchUp: 5.0f, searchDown: 25.0f))
                transform.position = grounded;

            HomePosition = transform.position;
            NetworkMaxHealth = MaxHealth;
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
            MonsterArchetype arch = GetArchetype();
            animatorBridge.SetSuperArmor(IsBoss || arch == MonsterArchetype.Brute);
        }
        _strafeDirSign = Random.value < 0.5f ? -1 : 1;
        _nextStrafeFlipAt = Time.time + Random.Range(1.8f, 3.2f);
        SetGuardVfxVisible(false);
        SetStunVfxVisible(false);
        SetParryGlintVisible(false);
        SetBruteRingVisible(false);
        MonsterSystemManager.NotifyMonsterSpawned(this);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (_swingRoutine != null)
        {
            StopCoroutine(_swingRoutine);
            _swingRoutine = null;
        }
        if (_parryGlintRoutine != null)
        {
            StopCoroutine(_parryGlintRoutine);
            _parryGlintRoutine = null;
        }
        _isSwinging = false;
        SetGuardVfxVisible(false);
        SetStunVfxVisible(false);
        SetParryGlintVisible(false);
        SetBruteRingVisible(false);
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

    /// <summary>
    /// Aligns TMRKKxOG's World-Space Health Bar (EnemyWorldHealthBar) &amp; LockPoint to match
    /// center-pivoted rigged monster proportions, and hides the neon personal-zone floor ring unless enabled.
    /// </summary>
    private void AlignHybridWorldUiAndRings()
    {
        Transform hpCanvas = transform.Find("EnemyWorldHealthBarCanvas");
        if (hpCanvas != null)
            hpCanvas.localPosition = new Vector3(0f, worldHealthBarLocalY, 0f);

        if (LockPoint != null && LockPoint != transform)
            LockPoint.localPosition = new Vector3(0f, lockPointLocalY, 0f);

        SyncPersonalZoneRingVisibility();
    }

    /// <summary>
    /// Disables the LineRenderer instead of destroying it so EnemyNPC.Update() never re-creates it every frame!
    /// </summary>
    private void SyncPersonalZoneRingVisibility()
    {
        if (BasePersonalZoneRingField == null) return;
        var ring = BasePersonalZoneRingField.GetValue(this) as LineRenderer;
        if (ring != null && ring.enabled != showPersonalZoneGroundRing)
            ring.enabled = showPersonalZoneGroundRing;
    }

    private MonsterArchetype GetArchetype()
    {
        return animatorBridge != null && animatorBridge.Config != null
            ? animatorBridge.Config.archetype
            : MonsterArchetype.Warrior;
    }

    /// <summary>
    /// Host-Migration safe canonical Defense calculation so StateAuthority transfer mid-Guard never resets Defense to 0.
    /// </summary>
    private int GetCanonicalBaseDefense()
    {
        int levelDef = Mathf.Max(0, (Mathf.Max(1, ScaledLevel) - 1) * 2);
        int speciesDef = animatorBridge != null && animatorBridge.Config != null ? animatorBridge.Config.defenseBonus : 0;
        return Mathf.Max(0, levelDef + speciesDef);
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

        // 1. Staggered (from Friend's Perfect Parry -> NpcState.Staggered)
        if (State == NpcState.Staggered)
        {
            TickParryStaggeredAuthority();
            return;
        }

        // 2. Stunned (from Combo 3 Guard Break)
        if (IsStunnedNet)
        {
            if (Time.time >= _stunUntil)
                IsStunnedNet = false;
            return;
        }

        // 3. Active Guard Stance
        if (IsGuarding)
        {
            PlayerCombat guardTarget = ResolveCurrentTarget();
            if (guardTarget != null && !guardTarget.IsDead)
                FaceTowards(guardTarget.transform.position, dt);

            if (Time.time >= _guardUntil || guardTarget == null || guardTarget.IsDead || SafeZone.IsPlayerInAnySafeZone(guardTarget))
                ExitGuardAuthority();
            return;
        }

        // 4. Normal States
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

        // Keep base EnemyNPC's orange floor ring hidden every frame unless explicitly enabled
        if (!showPersonalZoneGroundRing)
            SyncPersonalZoneRingVisibility();

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
        bool parriedStagger = !dead && State == NpcState.Staggered;
        bool stunnedOrParried = !dead && (IsStunnedNet || parriedStagger);
        bool inCombat = !dead && (State == NpcState.Chasing || State == NpcState.Attacking || IsGuarding || parriedStagger);

        SetStunVfxVisible(stunnedOrParried);
        animatorBridge.SetCombat(inCombat);
        animatorBridge.SetStunned(stunnedOrParried);
        if (dead && !animatorBridge.IsDead)
            animatorBridge.SetDead(true);

        if (dead || IsGuarding || stunnedOrParried || _isSwinging)
        {
            animatorBridge.SetWorldSpeed(0f);
            return;
        }

        if (Object.HasStateAuthority)
        {
            // Allow Attacking state to blend Walk animation when Combat Strafe is active!
            bool movingState = State == NpcState.Patrol || State == NpcState.Chasing || State == NpcState.Returning || State == NpcState.Attacking;
            animatorBridge.SetWorldSpeed(movingState ? _currentMoveSpeed : 0f);
        }
        else
        {
            if (State == NpcState.Idle)
                animatorBridge.SetWorldSpeed(0f);
            else
                animatorBridge.UpdateSpeedFromTransform(Time.deltaTime);
        }
    }

    private void TickParryStaggeredAuthority()
    {
        // Cancel any active melee swing immediately when Perfect Parried!
        if (_swingRoutine != null)
        {
            StopCoroutine(_swingRoutine);
            _swingRoutine = null;
        }
        _isSwinging = false;
        if (IsGuarding) ExitGuardAuthority();

        float staggerEnd = BaseStaggerEndTimeField != null ? (float)BaseStaggerEndTimeField.GetValue(this) : 0f;
        if (Time.time < staggerEnd) return;

        PlayerCombat target = ResolveCurrentTarget();
        if (target != null && !target.IsDead && !SafeZone.IsPlayerInAnySafeZone(target))
        {
            State = NpcState.Chasing;
        }
        else
        {
            _idleTimer = 0.5f;
            State = NpcState.Idle;
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

        // Skirmisher Archetype (Goblin / Wolf): Gap-Closer Lunge when player kites in mid-range!
        MonsterArchetype arch = GetArchetype();
        if (arch == MonsterArchetype.Skirmisher &&
            Time.time >= _nextAttackAt &&
            Time.time >= _nextLungeAllowedAt &&
            dist > AttackRange * 1.15f &&
            dist <= AttackRange + 4.0f &&
            HasClearLineLocal(target.transform.position))
        {
            _nextLungeAllowedAt = Time.time + 4.5f;
            StartSkirmisherLungeAuthority(target);
            return;
        }

        if (dist <= AttackRange && HasClearLineLocal(target.transform.position))
        {
            State = NpcState.Attacking;
            return;
        }

        float chaseSpd = arch == MonsterArchetype.Skirmisher ? runMoveSpeed * 1.22f
                       : (arch == MonsterArchetype.Brute ? runMoveSpeed * 0.85f : runMoveSpeed);
        MoveTowardsLocal(target.transform.position, chaseSpd, dt);
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
        if (dist > AttackRange * 1.28f || !HasClearLineLocal(target.transform.position))
        {
            State = NpcState.Chasing;
            return;
        }

        MonsterArchetype arch = GetArchetype();

        if (Time.time >= _nextAttackAt)
        {
            FaceTowards(target.transform.position, dt);

            // Warrior Archetype: occasional tactical Guard when player is swinging in close range
            if (arch == MonsterArchetype.Warrior && Time.time >= _nextGuardAllowedAt && target.IsHitWindowOpen && Random.value < reactiveGuardChanceOnCooldown)
            {
                EnterGuardAuthority(guardDuration * 0.85f);
                return;
            }

            int idx = animatorBridge != null ? animatorBridge.PickNextAttackIndex() : 0;
            StartMeleeSwingAuthority(target, idx, isCounter: false, isHeavyBrute: arch == MonsterArchetype.Brute);
            return;
        }

        // Combat Strafe / Footwork during Attack Cooldown (feels alive instead of staring robotically!)
        if (enableCombatStrafe)
        {
            TickCombatStrafeAuthority(target, dist, arch, dt);
        }
        else
        {
            FaceTowards(target.transform.position, dt);
        }
    }

    private void TickCombatStrafeAuthority(PlayerCombat target, float currentDist, MonsterArchetype arch, float dt)
    {
        if (Time.time >= _nextStrafeFlipAt)
        {
            _strafeDirSign = -_strafeDirSign;
            _nextStrafeFlipAt = Time.time + Random.Range(1.8f, 3.2f);
        }

        Vector3 toTarget = target.transform.position - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.01f) return;
        Vector3 dirToTarget = toTarget.normalized;

        // Tangent circle-step + subtle spacing correction around ideal melee distance
        Vector3 tangent = new Vector3(-dirToTarget.z, 0f, dirToTarget.x) * _strafeDirSign;
        float idealDist = AttackRange * 0.88f;
        float radialAdjust = Mathf.Clamp((currentDist - idealDist) * 0.6f, -0.45f, 0.45f);
        Vector3 moveDir = (tangent + dirToTarget * radialAdjust);
        moveDir.y = 0f;
        if (moveDir.sqrMagnitude < 0.001f) return;
        moveDir.Normalize();

        float strafeSpeed = walkMoveSpeed * combatStrafeSpeedFactor * (arch == MonsterArchetype.Skirmisher ? 1.30f : 1.0f);
        Vector3 pos = transform.position;
        if (ProbeObstacleLocal(pos, moveDir, 1.15f, out _))
        {
            // Flip strafe direction cleanly if hitting a rock/wall
            _strafeDirSign = -_strafeDirSign;
            _nextStrafeFlipAt = Time.time + 1.5f;
            FaceTowards(target.transform.position, dt);
            return;
        }

        Vector3 sep = ComputeSeparation(pos);
        Vector3 finalDir = (moveDir + sep * 0.45f);
        finalDir.y = 0f;
        if (finalDir.sqrMagnitude > 0.001f) finalDir.Normalize();

        Vector3 nextPos = pos + finalDir * (strafeSpeed * dt);
        ResolveObstaclePenetrationLocal(ref nextPos);
        EnforceSafeZoneLocal(ref nextPos);
        nextPos.y = pos.y;
        SnapToGround(ref nextPos, searchUp: 3.0f, searchDown: 12.0f);

        transform.position = nextPos;
        if (dt > 0.0001f)
            _currentMoveSpeed = PlanarDistance(pos, nextPos) / dt;

        // Always keep eyes & chest facing the player while circling!
        FaceTowards(target.transform.position, dt * 1.35f);
    }

    private void StartSkirmisherLungeAuthority(PlayerCombat target)
    {
        if (!IsAlive || target == null) return;
        FaceInstant(target.transform.position);
        State = NpcState.Attacking;

        // Smooth gap-closing lunge stop right inside AttackRange
        Vector3 toTarget = target.transform.position - transform.position;
        toTarget.y = 0f;
        float d = toTarget.magnitude;
        if (d > AttackRange * 0.85f)
        {
            Vector3 dir = toTarget / d;
            float lungeDist = Mathf.Min(d - AttackRange * 0.72f, 3.2f);
            Vector3 dest = transform.position + dir * lungeDist;
            if (!SafeZone.IsPointInAnySafeZone(dest, 0.8f))
            {
                ResolveObstaclePenetrationLocal(ref dest);
                SnapToGround(ref dest, searchUp: 3.0f, searchDown: 12.0f);
                transform.position = dest;
            }
        }

        int idx = animatorBridge != null ? animatorBridge.PickNextAttackIndex() : 0;
        StartMeleeSwingAuthority(target, idx, isCounter: false, isHeavyBrute: false);
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

    // ------------------------------------------------------------------ Melee Swing (Impact-Timed, Dodgeable & Parry Telegraph)

    private void StartMeleeSwingAuthority(PlayerCombat target, int attackIndex, bool isCounter, bool isHeavyBrute = false)
    {
        if (!IsAlive || target == null) return;
        if (_swingRoutine != null) StopCoroutine(_swingRoutine);

        _isSwinging = true;
        _attackLockUntil = Time.time + minAttackAnimLock;
        _nextAttackAt = Time.time + AttackCooldown + (isHeavyBrute ? 0.55f : 0f);
        if (isCounter || isHeavyBrute)
            _hyperArmorUntil = Time.time + 1.45f;

        RPC_MonsterPlayAttack(attackIndex, isCounter);
        _swingRoutine = StartCoroutine(MeleeSwingImpactRoutine(target, isCounter, isHeavyBrute));
    }

    private IEnumerator MeleeSwingImpactRoutine(PlayerCombat target, bool isCounter, bool isHeavyBrute)
    {
        float delay = isCounter ? counterImpactDelay : (isHeavyBrute ? attackImpactDelay + 0.28f : attackImpactDelay);

        // Ensure Counter impact never lands inside PlayerCombat's 0.4 * AttackCooldown anti-spam rejection window!
        if (isCounter)
        {
            float safeTime = _lastDamageDealtToPlayerStamp + (AttackCooldown * 0.42f) + 0.06f;
            float neededDelay = safeTime - Time.time;
            if (neededDelay > delay)
                delay = Mathf.Min(neededDelay, 0.72f);
        }

        // Fire Parry Telegraph Glint (~0.22s before impact, matching friend's 0.25s Perfect Parry window!)
        float glintLead = Mathf.Clamp(parryTelegraphLeadTime, 0.12f, Mathf.Max(0.12f, delay - 0.04f));
        float preGlintWait = Mathf.Max(0f, delay - glintLead);
        if (preGlintWait > 0.01f)
            yield return new WaitForSeconds(preGlintWait);

        if (IsAlive && !IsStunnedNet && State != NpcState.Staggered && (enableParryTelegraphGlint || isHeavyBrute))
        {
            RPC_MonsterParryTelegraph(glintLead, isHeavyBrute);
        }

        yield return new WaitForSeconds(glintLead);

        // Apply damage ONLY at actual swing impact, and only if player is still in strike cone & range (and monster wasn't parried/stunned)!
        if (IsAlive && !IsStunnedNet && State != NpcState.Staggered && target != null && target.IsSpawned && !target.IsDead && !SafeZone.IsPlayerInAnySafeZone(target))
        {
            Vector3 toTarget = target.transform.position - transform.position;
            toTarget.y = 0f;
            float dist = toTarget.magnitude;
            bool inFront = dist < 0.6f || Vector3.Dot(transform.forward, toTarget / dist) >= (isHeavyBrute ? 0.0f : 0.20f);
            float reach = AttackRange + impactReachSlack + (isHeavyBrute ? 0.45f : 0f);

            if (dist <= reach && inFront)
            {
                // Clamp boosted damage within PlayerCombat's accepted ceiling (ScaledAttackDamage + 4 for normal mobs)
                float mult = isCounter ? counterDamageMultiplier : (isHeavyBrute ? 1.35f : 1.0f);
                int rawDmg = Mathf.RoundToInt(AttackDamage * mult);
                int maxAccepted = IsBoss ? AttackDamage * 2 : AttackDamage + 4;
                int dmg = (isCounter || isHeavyBrute) ? Mathf.Min(rawDmg, maxAccepted) : AttackDamage;

                _lastDamageDealtToPlayerStamp = Time.time;
                target.RPC_ReceiveNpcDamage(Object.Id, dmg);
            }
        }

        // Keep feet planted until the Animator finishes the Attack state (safety cap 1.35s)
        float waitEnd = Time.time + 1.35f;
        while (Time.time < waitEnd && IsAlive && !IsStunnedNet && State != NpcState.Staggered)
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
            bool isGuardBreaker = IsGuardBreakingAttack(attacker);
            if (isGuardBreaker)
            {
                // GUARD BREAK! Heavy Attack / Stealth Backstab / Combo 3 shatters Guard -> Stun 1.5s + Knockback!
                ExitGuardAuthority();
                IsStunnedNet = true;
                _stunUntil = Time.time + guardBreakStunDuration;
                _consecutiveHits = 0;
                ApplyMicroKnockbackAuthority(attacker, microKnockbackDistance * 1.35f);
                RPC_MonsterGuardFeedback((byte)MonsterGuardFeedback.GuardBreak);
                if (verboseLogs) Debug.Log($"[MonsterEnemyNPC] {name} GUARD BROKEN by Heavy/Combo3/Stealth! Stunned for {guardBreakStunDuration:0.0}s.");
            }
            else
            {
                // BLOCKED! Spamming Light Combo 1/2 into Guard triggers immediate Counter-Attack!
                ExitGuardAuthority();
                RPC_MonsterGuardFeedback((byte)MonsterGuardFeedback.Blocked);
                if (attacker != null && !attacker.IsDead && !SafeZone.IsPlayerInAnySafeZone(attacker))
                {
                    FaceInstant(attacker.transform.position);
                    int counterIdx = animatorBridge != null && animatorBridge.AttackVariantCount >= 2 ? 1 : 0;
                    StartMeleeSwingAuthority(attacker, counterIdx, isCounter: true, isHeavyBrute: false);
                    if (verboseLogs) Debug.Log($"[MonsterEnemyNPC] {name} BLOCKED hit and launched COUNTER-ATTACK!");
                }
            }
            return;
        }

        // Case 2: Hit while Stunned or Parried-Staggered -> always flinch, flash & micro-knockback
        if (IsStunnedNet || State == NpcState.Staggered)
        {
            ApplyMicroKnockbackAuthority(attacker, microKnockbackDistance * 0.75f);
            RPC_MonsterHitFeedback(flinch: true);
            return;
        }

        // Track consecutive hits for Anti-Stunlock Poise
        if (Time.time - _lastHitStamp > comboResetWindow)
            _consecutiveHits = 0;
        _lastHitStamp = Time.time;
        _consecutiveHits++;

        // Hyper Armor (mid-counter, Boss, or Brute Archetype) -> no flinch, just hit flash
        if (Time.time < _hyperArmorUntil || (animatorBridge != null && !animatorBridge.CanFlinch))
        {
            RPC_MonsterHitFeedback(flinch: false);
            return;
        }

        // Warrior Archetype only: Anti-Stunlock Poise raises Guard after N consecutive hits
        MonsterArchetype arch = GetArchetype();
        if (arch == MonsterArchetype.Warrior && _consecutiveHits >= hitsBeforeGuard && Time.time >= _nextGuardAllowedAt && !_isSwinging)
        {
            _consecutiveHits = 0;
            EnterGuardAuthority(guardDuration);
            RPC_MonsterHitFeedback(flinch: false);
            return;
        }

        // Micro Knockback on Heavy / Combo 3 / Stealth Backstab hits for tactile impact feel!
        if (IsGuardBreakingAttack(attacker))
            ApplyMicroKnockbackAuthority(attacker, microKnockbackDistance);

        // Standard Hit_React flinch (with brief pause so it doesn't slide)
        _hitFlinchUntil = Time.time + hitFlinchPause;
        RPC_MonsterHitFeedback(flinch: true);
    }

    private static bool IsGuardBreakingAttack(PlayerCombat attacker)
    {
        if (attacker == null) return false;
        if (attacker.ComboStep >= 3) return true;
        PlayerAttackKind kind = attacker.CurrentAttackKind;
        return kind == PlayerAttackKind.HeavyCombo ||
               kind == PlayerAttackKind.RunHeavyAttack ||
               kind == PlayerAttackKind.JumpHeavyAttack ||
               kind == PlayerAttackKind.RunJumpHeavyAttack ||
               kind == PlayerAttackKind.StealthAssassination;
    }

    private void ApplyMicroKnockbackAuthority(PlayerCombat attacker, float distance)
    {
        if (!Object.HasStateAuthority || attacker == null || distance <= 0.01f) return;
        Vector3 pushDir = transform.position - attacker.transform.position;
        pushDir.y = 0f;
        if (pushDir.sqrMagnitude < 0.001f) pushDir = -transform.forward;
        pushDir.Normalize();

        Vector3 candidate = transform.position + pushDir * distance;
        ResolveObstaclePenetrationLocal(ref candidate);
        EnforceSafeZoneLocal(ref candidate);
        SnapToGround(ref candidate, searchUp: 2.5f, searchDown: 10.0f);
        transform.position = candidate;
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
        _baseDefense = GetCanonicalBaseDefense();
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
            // Always restore canonical Defense so Host Migration mid-Guard never resets Defense to 0!
            Defense = GetCanonicalBaseDefense();
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
    public void RPC_MonsterParryTelegraph(float duration, NetworkBool isHeavyBrute)
    {
        if (!gameObject.activeInHierarchy || !IsAlive) return;
        if (_parryGlintRoutine != null) StopCoroutine(_parryGlintRoutine);
        _parryGlintRoutine = StartCoroutine(ParryGlintRoutine(Mathf.Clamp(duration, 0.12f, 0.50f), isHeavyBrute));
    }

    private IEnumerator ParryGlintRoutine(float duration, bool showBruteRing)
    {
        SetParryGlintVisible(true);
        if (showBruteRing) SetBruteRingVisible(true);
        animatorBridge?.PlayBlockFlash();

        float elapsed = 0f;
        while (elapsed < duration && IsAlive && !IsStunnedNet && State != NpcState.Staggered)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.05f, duration));
            if (_parryGlintRoot != null)
            {
                float s = Mathf.Lerp(0.18f, 0.62f, Mathf.Sin(t * Mathf.PI));
                _parryGlintRoot.transform.localScale = new Vector3(s, s * 1.4f, s);
                _parryGlintRoot.transform.Rotate(0f, 360f * Time.deltaTime, 180f * Time.deltaTime, Space.Self);
            }
            if (showBruteRing && _bruteRingRoot != null)
            {
                float r = Mathf.Lerp(0.8f, AttackRange * 1.75f, t);
                _bruteRingRoot.transform.localScale = new Vector3(r, 0.02f, r);
            }
            yield return null;
        }

        SetParryGlintVisible(false);
        SetBruteRingVisible(false);
        _parryGlintRoutine = null;
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
        if (s_guardBarrierMat != null && s_guardRingMat != null && s_stunOrbMat != null &&
            s_parryGlintMat != null && s_bruteTelegraphMat != null) return;

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
        if (s_parryGlintMat == null)
        {
            s_parryGlintMat = new Material(shader) { name = "Mat_MonsterParryGlint_Shared" };
            s_parryGlintMat.color = new Color(1.0f, 0.34f, 0.12f, 0.92f);
        }
        if (s_bruteTelegraphMat == null)
        {
            s_bruteTelegraphMat = new Material(shader) { name = "Mat_MonsterBruteRing_Shared" };
            s_bruteTelegraphMat.color = new Color(0.95f, 0.16f, 0.10f, 0.45f);
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

        // 3. Parry Telegraph Glint VFX (bright flash 0.22s before swing lands -> tells player "PARRY NOW!")
        if (_parryGlintRoot == null)
        {
            Transform existingGlint = transform.Find("MonsterParryGlintVFX");
            if (existingGlint != null)
            {
                _parryGlintRoot = existingGlint.gameObject;
                _parryGlintRoot.SetActive(false);
            }
            else
            {
                GameObject glint = GameObject.CreatePrimitive(PrimitiveType.Cube);
                glint.name = "MonsterParryGlintVFX";
                var gcol = glint.GetComponent<Collider>();
                if (gcol != null) Destroy(gcol);
                glint.transform.SetParent(transform, false);
                glint.transform.localPosition = new Vector3(0.15f, 0.35f, 0.65f);
                glint.transform.localRotation = Quaternion.Euler(45f, 45f, 0f);
                glint.transform.localScale = Vector3.one * 0.30f;
                var gr = glint.GetComponent<Renderer>();
                if (gr != null)
                {
                    gr.shadowCastingMode = ShadowCastingMode.Off;
                    gr.receiveShadows = false;
                    if (s_parryGlintMat != null) gr.sharedMaterial = s_parryGlintMat;
                }
                _parryGlintRoot = glint;
                _parryGlintRoot.SetActive(false);
            }
        }

        // 4. Brute Ground Telegraph Ring (only activated for Brute archetype heavy slams)
        if (_bruteRingRoot == null)
        {
            Transform existingBrute = transform.Find("MonsterBruteRingVFX");
            if (existingBrute != null)
            {
                _bruteRingRoot = existingBrute.gameObject;
                _bruteRingRoot.SetActive(false);
            }
            else
            {
                GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                ring.name = "MonsterBruteRingVFX";
                var bcol = ring.GetComponent<Collider>();
                if (bcol != null) Destroy(bcol);
                ring.transform.SetParent(transform, false);
                ring.transform.localPosition = new Vector3(0f, -0.94f, 0.65f);
                ring.transform.localScale = new Vector3(1.5f, 0.02f, 1.5f);
                var br = ring.GetComponent<Renderer>();
                if (br != null)
                {
                    br.shadowCastingMode = ShadowCastingMode.Off;
                    br.receiveShadows = false;
                    if (s_bruteTelegraphMat != null) br.sharedMaterial = s_bruteTelegraphMat;
                }
                _bruteRingRoot = ring;
                _bruteRingRoot.SetActive(false);
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

    private void SetParryGlintVisible(bool visible)
    {
        if (_parryGlintRoot != null && _parryGlintRoot.activeSelf != visible)
            _parryGlintRoot.SetActive(visible);
    }

    private void SetBruteRingVisible(bool visible)
    {
        if (_bruteRingRoot != null && _bruteRingRoot.activeSelf != visible)
            _bruteRingRoot.SetActive(visible);
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
        float cosHalfCone = Mathf.Cos(Mathf.Clamp(hybridVisionConeAngle, 45f, 180f) * 0.5f * Mathf.Deg2Rad);

        for (int i = 0; i < PlayerCombat.All.Count; i++)
        {
            var p = PlayerCombat.All[i];
            if (p == null || !p.IsSpawned || p.IsDead) continue;
            if (SafeZone.IsPlayerInAnySafeZone(p)) continue;

            Vector3 toPlayer = p.transform.position - transform.position;
            toPlayer.y = 0f;
            float d = toPlayer.magnitude;
            if (d > bestDist) continue;

            // Stealth Vision Cone (110° front) + Back Hearing Radius (1.15m) so players can Stealth Assassinate (3.0x) from behind!
            bool heardBehind = d <= hybridBackHearingRadius;
            bool seenInFront = d < 0.05f || Vector3.Dot(transform.forward, toPlayer / d) >= cosHalfCone;
            if (!heardBehind && !seenInFront) continue;

            bestDist = d;
            best = p;
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
