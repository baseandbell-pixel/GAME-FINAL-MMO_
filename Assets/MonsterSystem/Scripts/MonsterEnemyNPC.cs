using System.Collections;
using System.Collections.Generic;
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
    [Tooltip("โดนตีสะดุ้งครบกี่ครั้งแล้วมอนสเตอร์จะยกการ์ดอัตโนมัติ (ทำงานครบทั้ง 7 สายพันธุ์)")]
    [Min(1)] [SerializeField] private int hitsBeforeGuard = 2;
    [Min(0.5f)] [SerializeField] private float comboResetWindow = 3.2f;
    [Min(0.4f)] [SerializeField] private float guardDuration = 1.75f;
    [Min(0.5f)] [SerializeField] private float guardCooldown = 2.8f;
    [Range(0f, 1f)] [SerializeField] private float reactiveGuardChanceOnCooldown = 0.40f;
    [Tooltip("เปิดให้มอนยกการ์ดฉุกเฉินเมื่อเลือดลดต่ำกว่า 55% หลังโดนฮิตแรก (เหมาะกับสาย Warrior / Brute / Boss; ปิดไว้สำหรับ Slime / Goblin เพื่อฟาร์มลื่น)")]
    [SerializeField] private bool allowLowHpEmergencyGuard = true;
    [Tooltip("พลังป้องกันเพิ่มขณะยกการ์ด (ป้องกันการถูกตีทะลุการ์ดตายในฮิตเดียว เพื่อให้เกิด BLOCKED! / COUNTER! หรือ GUARD BREAK! จริงเสมอ)")]
    [Min(5)] [SerializeField] private int guardBonusDefense = 999;
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

    [Header("6.5 ระบบบอส (ทำงานเมื่อ Config.isBoss = true)")]
    [Tooltip("คูลดาวน์ท่าทุบพื้น AoE Ground Slam ของบอส (วินาที)")]
    [SerializeField] private float bossSlamCooldown = 11.0f;
    [Tooltip("เวลาแสดงวงแดงเตือนบนพื้นก่อนบอสทุบลงมา (ให้ผู้เล่นกลิ้งหลบได้)")]
    [SerializeField] private float bossSlamTelegraphSeconds = 1.20f;
    [Tooltip("รัศมีวงแดงท่าทุบพื้น AoE ของบอส (เมตร)")]
    [SerializeField] private float bossSlamRadius = 4.60f;
    [Tooltip("ตัวคูณดาเมจท่าทุบพื้น AoE ของบอส")]
    [SerializeField] private float bossSlamDamageMultiplier = 1.55f;
    [Tooltip("สัดส่วนเลือดที่จะกระตุ้น Phase 2 Enrage (0.50 = ต่ำกว่า 50%)")]
    [Range(0.15f, 0.80f)] [SerializeField] private float bossEnrageHpFraction = 0.50f;
    [Tooltip("คูลดาวน์ท่าฟาดกวาดรอบตัว 360° Cleave ของบอส (อยู่กับที่ ทำงานใน Phase 2 หรือเมื่อมีผู้เล่นอ้อมตีด้านหลัง)")]
    [SerializeField] private float bossCleaveCooldown = 10.5f;
    [Tooltip("เวลาแสดงวงแดงเตือนก่อนบอสฟาดกวาด 360° (วินาที — กลิ้งหลบหรือกด [F] Perfect Parry ได้)")]
    [SerializeField] private float bossCleaveTelegraphSeconds = 0.85f;
    [Tooltip("รัศมีวงกวาดรอบตัว 360° Cleave ของบอส (เมตร)")]
    [SerializeField] private float bossCleaveRadius = 5.20f;
    [Tooltip("ตัวคูณดาเมจท่าฟาดกวาดรอบตัว 360° Cleave ของบอส")]
    [SerializeField] private float bossCleaveDamageMultiplier = 1.30f;

    [Header("7. การตรวจสอบ (Debug)")]
    [SerializeField] private bool verboseLogs = false;

    // ---- Networked Tactical & Zone Ownership State (all peers) ----
    [Networked, OnChangedRender(nameof(OnGuardVisualStateChanged))]
    public NetworkBool IsGuarding { get; set; }

    [Networked, OnChangedRender(nameof(OnStunVisualStateChanged))]
    public NetworkBool IsStunnedNet { get; set; }

    [Networked, OnChangedRender(nameof(OnEnrageVisualStateChanged))]
    public NetworkBool IsEnragedNet { get; set; }

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
    private static readonly MethodInfo BaseTickBossCombatAndRegenMethod =
        typeof(EnemyNPC).GetMethod("TickBossCombatAndRegen", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly PropertyInfo BaseTelegraphNameProp =
        typeof(EnemyNPC).GetProperty("ActiveTelegraphName", BindingFlags.Instance | BindingFlags.Public);
    private static readonly PropertyInfo BaseTelegraphDurationProp =
        typeof(EnemyNPC).GetProperty("ActiveTelegraphDuration", BindingFlags.Instance | BindingFlags.Public);
    private static readonly PropertyInfo BaseTelegraphEndTimeProp =
        typeof(EnemyNPC).GetProperty("ActiveTelegraphEndTime", BindingFlags.Instance | BindingFlags.Public);

    // ---- Shared Static VFX Materials (zero material leaks across infinite respawns) ----
    private static Material s_guardBarrierMat;
    private static Material s_guardRingMat;
    private static Material s_stunOrbMat;
    private static Material s_parryGlintMat;
    private static Material s_bruteTelegraphMat;
    private static Material s_slamOuterRingMat;
    private static Material s_slamInnerFillMat;

    // ---- Runtime Private State ----
    private int _baseDefense;
    private int _consecutiveHits;
    private float _lastHitStamp = -100f;
    private float _lastDamageDealtToPlayerStamp = -100f;
    private float _nextAttackAt;
    private float _nextBossSlamAt;
    private float _nextBossCleaveAt;
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
    private bool _isBossSlamming;
    private bool _listenersWired;
    private bool _speciesStatsApplied;
    private bool _lootDropped;
    private bool _zoneLedgerMerged;
    private bool _capturedInitialScale;
    private Vector3 _initialPrefabScale = Vector3.one;
    private int _avoidDirSign;
    private float _avoidSignUntil;
    private int _strafeDirSign = 1;
    private float _nextStrafeFlipAt;
    private int _lastReactiveGuardSwingSeq = -1;
    private float _vfxHeightScale = 1.0f;
    private readonly object[] _bossTickArgs = new object[1];
    private PlayerCombat _lastKnownAttacker;

    public bool IsConfiguredAsBoss => animatorBridge != null && animatorBridge.Config != null && animatorBridge.Config.isBoss;

    private Coroutine _swingRoutine;
    private Coroutine _parryGlintRoutine;
    private Coroutine _bossSlamRoutine;
    private Coroutine _slamVfxRoutine;
    private GameObject _activeSlamTelegraphGo;
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
        if (!_capturedInitialScale)
        {
            _initialPrefabScale = transform.localScale;
            _capturedInitialScale = true;
        }
        DisconnectBaseAnimatorAndRenderer();
        EnsureBridgeAndListeners();
        EnsureAudioSource();
        BuildCombatVfx();
    }

    private void OnDisable()
    {
        RemoveListeners();
        StopAllCombatRoutines();
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
        _zoneLedgerMerged = false;
        _lastKnownAttacker = null;

        if (Object.HasStateAuthority && IsConfiguredAsBoss)
            IsBoss = true;

        // Disable NavMeshAgent on center-pivoted MonsterEnemyNPC before base.Spawned() so it never warps pivot into floor
        BaseUseNavMeshField?.SetValue(this, false);
        base.Spawned();

        // Prevent base EnemyNPC.ApplyBossVisualAndCore() from inflating our rigged 3D Boss or spawning primitive crown cubes
        SuppressBaseBossPrimitives();

        // Disable any NavMeshAgent if already attached
        var nav = GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (nav != null) nav.enabled = false;

        // Clean display name (removes "(Clone)" so Lock-On, QuestNPC & EnemyWorldHealthBar display e.g. "Orc" or "BossOgre")
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
            IsEnragedNet = false;
            _consecutiveHits = 0;
            _isSwinging = false;
            _isBossSlamming = false;
            _nextBossSlamAt = Time.time + Random.Range(4.5f, 7.0f);
            _returnStuckTimer = 0f;
            _lastReturnCheckPos = transform.position;
            _idleTimer = Random.Range(personalIdleWait.x, personalIdleWait.y);
        }

        if (animatorBridge != null)
        {
            animatorBridge.ResetForPoolSpawn();
            MonsterArchetype arch = GetArchetype();
            animatorBridge.SetSuperArmor(IsBoss || arch == MonsterArchetype.Brute);
            animatorBridge.SetPersistentEnrageGlow(IsEnragedNet);
            animatorBridge.SetSpeedScale(IsEnragedNet ? 1.18f : 1.0f);
        }
        _strafeDirSign = Random.value < 0.5f ? -1 : 1;
        _nextStrafeFlipAt = Time.time + Random.Range(1.8f, 3.2f);
        SetGuardVfxVisible(false);
        SetStunVfxVisible(false);
        SetParryGlintVisible(false);
        SetBruteRingVisible(false);
        ClearActiveSlamTelegraph();
        MonsterSystemManager.NotifyMonsterSpawned(this);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        StopAllCombatRoutines();
        SetGuardVfxVisible(false);
        SetStunVfxVisible(false);
        SetParryGlintVisible(false);
        SetBruteRingVisible(false);
        base.Despawned(runner, hasState);
    }

    private void StopAllCombatRoutines()
    {
        if (_swingRoutine != null) { StopCoroutine(_swingRoutine); _swingRoutine = null; }
        if (_parryGlintRoutine != null) { StopCoroutine(_parryGlintRoutine); _parryGlintRoutine = null; }
        if (_bossSlamRoutine != null) { StopCoroutine(_bossSlamRoutine); _bossSlamRoutine = null; }
        if (_slamVfxRoutine != null) { StopCoroutine(_slamVfxRoutine); _slamVfxRoutine = null; }
        _isSwinging = false;
        _isBossSlamming = false;
        ClearActiveSlamTelegraph();
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

    public void SuppressBaseBossPrimitives()
    {
        if (_capturedInitialScale && transform.localScale != _initialPrefabScale)
            transform.localScale = _initialPrefabScale;

        Transform baseCrown = transform.Find("BossVisualCrown");
        if (baseCrown != null && baseCrown.gameObject.activeSelf)
            baseCrown.gameObject.SetActive(false);

        if (animatorBridge != null && animatorBridge.Config != null && !string.IsNullOrWhiteSpace(animatorBridge.Config.monsterName))
            gameObject.name = animatorBridge.Config.monsterName;
    }

    private void LateUpdate()
    {
        if (IsSpawned && IsBoss && _capturedInitialScale && transform.localScale != _initialPrefabScale)
            SuppressBaseBossPrimitives();
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
    /// Host-Migration safe canonical Defense calculation matching TMRKKxOG's 3-Tier Zone Scaling so StateAuthority transfer mid-Guard never resets Defense to 0.
    /// </summary>
    private int GetCanonicalBaseDefense()
    {
        int lvl = Mathf.Max(1, ScaledLevel);
        int baseDef;
        if (IsBoss)
        {
            ComputeBossBaseStats(lvl, out _, out _, out baseDef, out _);
            baseDef *= Mathf.Max(1, CoopPeakPlayerCount);
        }
        else
        {
            ComputeNormalEnemyBaseStats(lvl, out _, out _, out baseDef, out _);
        }
        int speciesDef = animatorBridge != null && animatorBridge.Config != null ? animatorBridge.Config.defenseBonus : 0;
        return Mathf.Max(0, baseDef + speciesDef);
    }

    private void CancelActiveMeleeSwing()
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
        SetParryGlintVisible(false);
        SetBruteRingVisible(false);
    }

    /// <summary>
    /// Allows ALL 7 monster species to reactively raise Guard once per incoming player swing when Guard is off cooldown.
    /// </summary>
    private bool TryReactiveGuardAgainstIncomingSwing(PlayerCombat target, float dist)
    {
        if (target == null || target.IsDead || !target.IsHitWindowOpen) return false;
        if (IsGuarding || IsStunnedNet || _isSwinging || _isBossSlamming) return false;
        if (Time.time < _nextGuardAllowedAt) return false;
        if (dist > AttackRange + 1.85f) return false;

        int swingSeq = target.CurrentSequence;
        if (swingSeq == _lastReactiveGuardSwingSeq) return false;
        _lastReactiveGuardSwingSeq = swingSeq;

        if (Random.value < reactiveGuardChanceOnCooldown)
        {
            FaceInstant(target.transform.position);
            EnterGuardAuthority(guardDuration * 0.90f);
            return true;
        }
        return false;
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

        // Run base EnemyNPC's Boss Wipe 10s Cooldown & +25%/3s Regen lifecycle so BossOgre supports full Co-op Boss HUD!
        if (IsBoss && BaseTickBossCombatAndRegenMethod != null)
        {
            _bossTickArgs[0] = dt;
            BaseTickBossCombatAndRegenMethod.Invoke(this, _bossTickArgs);
            // เมื่อบอสฟื้นฟูเลือดกลับเต็ม 100% หลังจบไฟต์ ให้ปลดสถานะ Enrage กลับเป็นปกติ
            if (IsEnragedNet && CurrentHealth >= MaxHealth && State != NpcState.Chasing && State != NpcState.Attacking)
                IsEnragedNet = false;
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

        // Animate Guard VFX pulse (properly scaled by _vfxHeightScale so Slime shield stays small and BossOgre shield stays huge!)
        if (_guardVfxRoot != null && _guardVfxRoot.activeSelf)
        {
            float hs = Mathf.Clamp(_vfxHeightScale, 0.50f, 1.50f);
            if (_guardShieldMesh != null)
            {
                float pulse = 1f + Mathf.Sin(Time.time * 7f) * 0.04f;
                _guardShieldMesh.localScale = new Vector3(1.10f * hs * pulse, 1.25f * hs * pulse, 0.35f * hs * pulse);
            }
            if (_guardRingMesh != null)
            {
                float r = (1.60f + Mathf.Sin(Time.time * 5f) * 0.08f) * hs;
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
        CancelActiveMeleeSwing();
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
            // สำหรับบอสในโหมด Co-op: หากเป้าหมายหลักตายหรือเข้า Safe Zone ให้สลับไปหาผู้ร่วมสู้คนถัดไปที่ยังมีชีวิตก่อน (ไม่ยกเลิกไฟต์และไม่รีเซ็ตเลือด)
            if (IsBoss && TrySwitchBossTargetLocal(out PlayerCombat nextTarget))
            {
                target = nextTarget;
            }
            else
            {
                BeginReturnAuthority();
                return;
            }
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

        // Allow ALL 7 monsters to reactively raise Guard when player swings in close range!
        if (TryReactiveGuardAgainstIncomingSwing(target, dist))
            return;

        // Skirmisher Archetype (Goblin / Slime): Gap-Closer Lunge when player kites in mid-range!
        MonsterArchetype arch = GetArchetype();
        if (arch == MonsterArchetype.Skirmisher &&
            Time.time >= _nextAttackAt &&
            Time.time >= _nextLungeAllowedAt &&
            dist > AttackRange * 1.15f &&
            dist <= AttackRange + 3.2f &&
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

        if (IsBoss && !IsGuarding && !IsStunnedNet && !_isBossSlamming &&
            Time.time >= _nextBossSlamAt && dist <= bossSlamRadius + 2.5f && HasClearLineLocal(target.transform.position))
        {
            StartBossSlamAuthority(target);
            return;
        }

        float chaseSpd = arch == MonsterArchetype.Skirmisher ? runMoveSpeed * 1.22f
                       : (arch == MonsterArchetype.Brute ? runMoveSpeed * 0.85f : runMoveSpeed);
        if (IsEnragedNet) chaseSpd *= 1.18f;
        MoveTowardsLocal(target.transform.position, chaseSpd, dt);
    }

    private void TickAttackingAuthority(float dt)
    {
        PlayerCombat target = ResolveCurrentTarget();
        if (target == null || target.IsDead || SafeZone.IsPlayerInAnySafeZone(target))
        {
            // สำหรับบอสในโหมด Co-op: สลับเป้าหมายไปยังผู้ร่วมสู้คนอื่นที่ยังมีชีวิตในลานบอสแทนการยกเลิกไฟต์
            if (IsBoss && TrySwitchBossTargetLocal(out PlayerCombat nextTarget))
            {
                target = nextTarget;
            }
            else
            {
                BeginReturnAuthority();
                return;
            }
        }

        // Strict Anti-Slide: plant feet until Attack, Boss Slam & Hit_React finish completely!
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

        // All 7 monsters can reactively raise Guard when the player swings (even during attack cooldown!)
        if (TryReactiveGuardAgainstIncomingSwing(target, dist))
            return;

        MonsterArchetype arch = GetArchetype();

        if (IsBoss && !IsGuarding && !IsStunnedNet && !_isBossSlamming &&
            Time.time >= _nextBossCleaveAt &&
            ((IsEnragedNet && dist <= bossCleaveRadius) || HasPlayerFlankingBehind(bossCleaveRadius)))
        {
            StartBossCleaveAuthority();
            return;
        }

        if (IsBoss && !IsGuarding && !IsStunnedNet && !_isBossSlamming && Time.time >= _nextBossSlamAt)
        {
            StartBossSlamAuthority(target);
            return;
        }

        if (Time.time >= _nextAttackAt)
        {
            FaceInstant(target.transform.position);
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

        // Tangent circle-step + spacing correction well inside player's 2.65m / 3.25m melee reach!
        Vector3 tangent = new Vector3(-dirToTarget.z, 0f, dirToTarget.x) * _strafeDirSign;
        float maxPlayerReach = IsBoss ? 2.55f : 2.05f;
        float idealDist = Mathf.Min(AttackRange * 0.80f, maxPlayerReach);
        float radialAdjust = Mathf.Clamp((currentDist - idealDist) * 0.65f, -0.45f, 0.45f);
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

        // Smooth gap-closing lunge stop right inside AttackRange (capped to 1.8m so player can always react and Block/Parry!)
        Vector3 toTarget = target.transform.position - transform.position;
        toTarget.y = 0f;
        float d = toTarget.magnitude;
        if (d > AttackRange * 0.85f)
        {
            Vector3 dir = toTarget / d;
            float lungeDist = Mathf.Min(d - AttackRange * 0.72f, 1.8f);
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
        // สำหรับบอส: หากระหว่างเดินกลับยังมีผู้ร่วมสู้ที่ยังมีชีวิตอยู่ในลานบอส ให้หันกลับไปสู้ต่อทันที
        if (IsBoss && CountActiveBossParticipants() > 0 && TrySwitchBossTargetLocal(out _))
        {
            State = NpcState.Chasing;
            return;
        }

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
        // มอนสเตอร์ทั่วไปรีเซ็ตเลือดเต็มเมื่อกลับถึงจุดเกิด ส่วนบอสปล่อยให้ระบบ Boss 10s Cooldown + Gradual Regen (+25%/3s) ของ EnemyNPC จัดการ
        if (!IsBoss)
        {
            CurrentHealth = MaxHealth;
            if (IsEnragedNet) IsEnragedNet = false;
        }
        TargetId = default;
        _consecutiveHits = 0;
        _returnStuckTimer = 0f;
        _idleTimer = Random.Range(personalIdleWait.x, personalIdleWait.y);
        State = NpcState.Idle;
    }

    /// <summary>
    /// ค้นหาผู้เล่นที่ยังมีชีวิตและอยู่นอก Safe Zone ภายในลานบอส เพื่อสลับเป้าหมายในโหมด Co-op โดยไม่รีเซ็ตเลือดบอส
    /// </summary>
    private bool TrySwitchBossTargetLocal(out PlayerCombat nextTarget)
    {
        nextTarget = null;
        if (!IsBoss) return false;

        ResolveBossArenaBounds(out Vector3 arenaCenter, out float arenaRadius);
        float limit = Mathf.Max(personalLeashRadius, arenaRadius + 4.0f);
        float bestScore = float.NegativeInfinity;

        for (int i = 0; i < PlayerCombat.All.Count; i++)
        {
            var p = PlayerCombat.All[i];
            if (p == null || !p.IsSpawned || p.IsDead || SafeZone.IsPlayerInAnySafeZone(p)) continue;
            if (PlanarDistance(p.transform.position, arenaCenter) > limit) continue;

            PlayerRef pRef = p.Object != null && p.Object.IsValid ? p.Object.StateAuthority : PlayerRef.None;
            float share = pRef != PlayerRef.None ? GetPlayerDamageShare(pRef) : 0f;
            float dist = PlanarDistance(transform.position, p.transform.position);
            float score = (share * 500f) - dist;
            if (score > bestScore)
            {
                bestScore = score;
                nextTarget = p;
            }
        }

        if (nextTarget != null)
        {
            TargetId = nextTarget.Object.Id;
            return true;
        }
        return false;
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
        if (_isSwinging || _isBossSlamming || Time.time < _attackLockUntil || Time.time < _hitFlinchUntil)
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
        float cdScale = IsEnragedNet ? 0.78f : 1.0f;
        _attackLockUntil = Time.time + (minAttackAnimLock * cdScale);
        _nextAttackAt = Time.time + ((AttackCooldown + (isHeavyBrute ? 0.55f : 0f)) * cdScale);
        // Only lock un-interruptible hyper-armor during Counter-Attack so Brutes/Boss can still raise Guard after hitsBeforeGuard hits!
        if (isCounter)
            _hyperArmorUntil = Time.time + 1.15f;

        RPC_MonsterPlayAttack(attackIndex, isCounter);
        _swingRoutine = StartCoroutine(MeleeSwingImpactRoutine(target, isCounter, isHeavyBrute));
    }

    // ------------------------------------------------------------------ Boss AoE Ground Slam (Telegraphed & Dodgeable)

    private void StartBossSlamAuthority(PlayerCombat target)
    {
        if (!IsAlive || !IsBoss || _isBossSlamming || target == null) return;
        if (_swingRoutine != null) { StopCoroutine(_swingRoutine); _swingRoutine = null; _isSwinging = false; }
        if (_bossSlamRoutine != null) StopCoroutine(_bossSlamRoutine);

        _isBossSlamming = true;
        float cd = IsEnragedNet ? Mathf.Max(5.5f, bossSlamCooldown * 0.72f) : bossSlamCooldown;
        _nextBossSlamAt = Time.time + cd;
        _nextAttackAt = Time.time + bossSlamTelegraphSeconds + 0.9f;
        _hyperArmorUntil = Time.time + bossSlamTelegraphSeconds + 0.65f;

        FaceInstant(target.transform.position);
        Vector3 slamCenter = Vector3.Lerp(transform.position, target.transform.position, 0.35f);
        SnapToGround(ref slamCenter, searchUp: 4.0f, searchDown: 15.0f);

        _bossSlamRoutine = StartCoroutine(BossSlamAuthorityRoutine(slamCenter));
    }

    private IEnumerator BossSlamAuthorityRoutine(Vector3 groundCenter)
    {
        float telegraphDur = IsEnragedNet ? Mathf.Max(0.85f, bossSlamTelegraphSeconds * 0.85f) : bossSlamTelegraphSeconds;
        RPC_BossSlamTelegraph(groundCenter, bossSlamRadius, telegraphDur);

        float endAt = Time.time + telegraphDur;
        while (Time.time < endAt && IsAlive && !IsStunnedNet && State != NpcState.Staggered)
            yield return null;

        if (IsAlive && !IsStunnedNet && State != NpcState.Staggered && Object != null && Object.IsValid && Object.HasStateAuthority)
        {
            RPC_BossSlamImpact(groundCenter, bossSlamRadius);

            int rawSlam = Mathf.RoundToInt(AttackDamage * bossSlamDamageMultiplier);
            int slamDmg = Mathf.Min(rawSlam, AttackDamage * 2);
            _lastDamageDealtToPlayerStamp = Time.time;

            for (int i = 0; i < PlayerCombat.All.Count; i++)
            {
                PlayerCombat p = PlayerCombat.All[i];
                if (p == null || !p.IsSpawned || p.IsDead) continue;
                if (SafeZone.IsPlayerInAnySafeZone(p)) continue;

                float dist = PlanarDistance(groundCenter, p.transform.position);
                if (dist <= bossSlamRadius)
                {
                    p.RPC_ReceiveNpcDamage(Object.Id, slamDmg);
                }
            }
        }

        yield return new WaitForSeconds(0.55f);
        _isBossSlamming = false;
        _bossSlamRoutine = null;
    }

    // ------------------------------------------------------------------ Boss Stationary 360° Cleave (Phase 2 / Anti-Backhug, Parryable & Zero Wall-Clip)

    private bool HasPlayerFlankingBehind(float maxRadius)
    {
        for (int i = 0; i < PlayerCombat.All.Count; i++)
        {
            var p = PlayerCombat.All[i];
            if (p == null || !p.IsSpawned || p.IsDead || SafeZone.IsPlayerInAnySafeZone(p)) continue;
            Vector3 toP = p.transform.position - transform.position;
            toP.y = 0f;
            float d = toP.magnitude;
            if (d > 0.2f && d <= maxRadius)
            {
                if (Vector3.Dot(transform.forward, toP / d) < -0.15f)
                    return true;
            }
        }
        return false;
    }

    /// <summary>ตัวช่วยทดสอบใน Play Mode: สั่งให้บอสออกท่า 360° Cleave ทันทีบน State Authority</summary>
    public bool TryTriggerBossCleaveForTest()
    {
        if (!IsSpawned || !Object.HasStateAuthority || !IsAlive || !IsBoss || _isBossSlamming) return false;
        StartBossCleaveAuthority();
        return true;
    }

    private void StartBossCleaveAuthority()
    {
        if (!IsAlive || !IsBoss || _isBossSlamming) return;
        if (_swingRoutine != null) { StopCoroutine(_swingRoutine); _swingRoutine = null; _isSwinging = false; }
        if (_bossSlamRoutine != null) StopCoroutine(_bossSlamRoutine);

        _isBossSlamming = true;
        float cd = IsEnragedNet ? Mathf.Max(6.5f, bossCleaveCooldown * 0.78f) : bossCleaveCooldown;
        _nextBossCleaveAt = Time.time + cd;
        // เว้นระยะห่างจาก Slam อย่างน้อย 3.8 วินาที เพื่อไม่ให้บอสออกท่าใหญ่ 2 ท่าติดกันจนผู้เล่นหนีไม่ทัน
        _nextBossSlamAt = Mathf.Max(_nextBossSlamAt, Time.time + 3.8f);
        _nextAttackAt = Time.time + bossCleaveTelegraphSeconds + 0.85f;
        _hyperArmorUntil = Time.time + bossCleaveTelegraphSeconds + 0.50f;

        Vector3 groundCenter = transform.position;
        SnapToGround(ref groundCenter, searchUp: 3.0f, searchDown: 12.0f);
        _bossSlamRoutine = StartCoroutine(BossCleaveAuthorityRoutine(groundCenter));
    }

    private IEnumerator BossCleaveAuthorityRoutine(Vector3 groundCenter)
    {
        float telegraphDur = IsEnragedNet ? Mathf.Max(0.70f, bossCleaveTelegraphSeconds * 0.90f) : bossCleaveTelegraphSeconds;
        RPC_BossCleaveTelegraph(groundCenter, bossCleaveRadius, telegraphDur);

        // ยิงแสงเตือน Parry Glint ช่วง 0.22 วินาทีสุดท้ายก่อนฟาดกวาด เพื่อให้ผู้เล่นกด [F] Perfect Parry สวนได้จริง
        float glintLead = Mathf.Clamp(parryTelegraphLeadTime, 0.16f, Mathf.Max(0.16f, telegraphDur - 0.15f));
        float glintAt = Time.time + Mathf.Max(0f, telegraphDur - glintLead);
        bool glintSent = false;

        float endAt = Time.time + telegraphDur;
        while (Time.time < endAt && IsAlive && !IsStunnedNet && State != NpcState.Staggered)
        {
            if (!glintSent && Time.time >= glintAt)
            {
                glintSent = true;
                RPC_MonsterParryTelegraph(glintLead, isHeavyBrute: true);
            }
            yield return null;
        }

        if (IsAlive && !IsStunnedNet && State != NpcState.Staggered && Object != null && Object.IsValid && Object.HasStateAuthority)
        {
            RPC_BossCleaveImpact(groundCenter, bossCleaveRadius);

            int rawCleave = Mathf.RoundToInt(AttackDamage * bossCleaveDamageMultiplier);
            int cleaveDmg = Mathf.Min(rawCleave, AttackDamage * 2);
            _lastDamageDealtToPlayerStamp = Time.time;

            for (int i = 0; i < PlayerCombat.All.Count; i++)
            {
                PlayerCombat p = PlayerCombat.All[i];
                if (p == null || !p.IsSpawned || p.IsDead) continue;
                if (SafeZone.IsPlayerInAnySafeZone(p)) continue;

                // ฟาดกวาดรอบตัว 360° ภายในรัศมี bossCleaveRadius (หลบด้วย Ctrl, บล็อก หรือ Perfect Parry ด้วย [F] ได้!)
                float dist = PlanarDistance(transform.position, p.transform.position);
                if (dist <= bossCleaveRadius)
                {
                    p.RPC_ReceiveNpcDamage(Object.Id, cleaveDmg);
                }
            }
        }

        yield return new WaitForSeconds(0.50f);
        _isBossSlamming = false;
        _bossSlamRoutine = null;
    }

    private IEnumerator MeleeSwingImpactRoutine(PlayerCombat target, bool isCounter, bool isHeavyBrute)
    {
        // Enforce human-reactable minimum impact delay (>= 0.40s) so players can reliably Block or Perfect Parry all 7 monsters!
        float baseImpactDelay = Mathf.Max(0.40f, attackImpactDelay);
        float baseCounterDelay = Mathf.Max(0.36f, counterImpactDelay);
        float delay = isCounter ? baseCounterDelay : (isHeavyBrute ? baseImpactDelay + 0.24f : baseImpactDelay);

        // Ensure Counter impact never lands inside PlayerCombat's 0.4 * AttackCooldown anti-spam rejection window!
        if (isCounter)
        {
            float safeTime = _lastDamageDealtToPlayerStamp + (AttackCooldown * 0.42f) + 0.06f;
            float neededDelay = safeTime - Time.time;
            if (neededDelay > delay)
                delay = Mathf.Min(neededDelay, 0.72f);
        }

        // Fire Parry Telegraph Glint (~0.22s before impact, matching friend's 0.25s Perfect Parry window!)
        float glintLead = Mathf.Clamp(parryTelegraphLeadTime, 0.14f, Mathf.Max(0.14f, delay - 0.12f));
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
            bool inFront = dist < 0.85f || Vector3.Dot(transform.forward, toTarget / dist) >= (isHeavyBrute ? -0.10f : 0.10f);
            float reach = AttackRange + Mathf.Max(0.95f, impactReachSlack) + (isHeavyBrute ? 0.35f : 0f);

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

        // Boss Phase 2 Enrage check (< 50% HP while alive)
        if (IsBoss && !IsEnragedNet && CurrentHealth > 0 &&
            CurrentHealth <= Mathf.RoundToInt(MaxHealth * bossEnrageHpFraction))
        {
            IsEnragedNet = true;
            _nextBossSlamAt = Mathf.Min(_nextBossSlamAt, Time.time + 2.2f);
            _nextBossCleaveAt = Mathf.Min(_nextBossCleaveAt, Time.time + 1.4f);
            RPC_BossEnrageFeedback();
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

        // Species-tuned Anti-Stunlock & Optional Low-HP Emergency Guard (ALL 7 species supported with distinct personalities!)
        int guardThreshold = Mathf.Max(1, hitsBeforeGuard);
        bool lowHpEmergencyGuard = allowLowHpEmergencyGuard &&
                                   _consecutiveHits >= 1 &&
                                   CurrentHealth > 0 &&
                                   CurrentHealth <= Mathf.RoundToInt(MaxHealth * 0.55f);
        if ((_consecutiveHits >= guardThreshold || lowHpEmergencyGuard) &&
            Time.time >= _nextGuardAllowedAt &&
            Time.time >= _hyperArmorUntil &&
            !_isBossSlamming)
        {
            _consecutiveHits = 0;
            CancelActiveMeleeSwing();
            if (attacker != null) FaceInstant(attacker.transform.position);
            EnterGuardAuthority(guardDuration);
            RPC_MonsterHitFeedback(flinch: false);
            return;
        }

        // Hyper Armor (mid-counter, Boss, or Brute Archetype) -> no flinch on single hits, just hit flash
        if (Time.time < _hyperArmorUntil || (animatorBridge != null && !animatorBridge.CanFlinch))
        {
            RPC_MonsterHitFeedback(flinch: false);
            return;
        }

        // Micro Knockback on Heavy / Combo 3 / Stealth Backstab hits for tactile impact feel!
        if (IsGuardBreakingAttack(attacker))
            ApplyMicroKnockbackAuthority(attacker, microKnockbackDistance);

        // Standard Hit_React flinch (cancel any unfinished normal melee swing so it never deals phantom damage while flinching!)
        CancelActiveMeleeSwing();
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
        StopAllCombatRoutines();

        // ลบ FindLikelyAttacker(12f) ทิ้ง เพื่อป้องกันคนยืนดูเฉยๆ (Bystander) ได้สิทธิ์ของดรอปหรือทองฟรี
        PlayerCombat killer = _lastKnownAttacker ?? ResolveCurrentTarget();

        // รวมตารางดาเมจของมอนตัวนี้เข้า Zone Ledger บนทุกเครื่อง (Host-Migration Safe: หาก Host หลุดกลางแคมป์ MasterClient คนใหม่จะยังมีข้อมูลครบ)
        MergeMonsterDamageIntoZoneLedgerOnce(killer);

        if (Object != null && Object.IsValid && Object.HasStateAuthority)
        {
            if (IsGuarding) ExitGuardAuthority();
            IsStunnedNet = false;
            IsEnragedNet = false;
            RollAndSpawnConfiguredLoot();
            RecordAndCheckZoneClearOnAuthority(killer);
        }

        SetGuardVfxVisible(false);
        SetStunVfxVisible(false);
        SetParryGlintVisible(false);
        SetBruteRingVisible(false);
        animatorBridge?.SetDead(true);
        PlayClip(animatorBridge?.Config?.deathClip);

        // Trigger teammate's Death Blood Pool VFX scaled to monster height!
        float modelHeight = animatorBridge?.Config != null ? Mathf.Max(0.8f, animatorBridge.Config.targetWorldHeight) : 2.15f;
        float bloodScale = Mathf.Clamp(modelHeight / 2.15f, 0.55f, 1.55f);
        Vector3 bloodPos = new Vector3(transform.position.x, transform.position.y - GetGroundPivotHeight() + 0.05f, transform.position.z);
        MMOMonsterDeathBloodVFX.SpawnDeathBlood(bloodPos, bloodScale);

        MonsterSystemManager.NotifyMonsterKilled(this, killer);
    }

    /// <summary>
    /// รวมยอดดาเมจของมอนสเตอร์ที่เพิ่งตายเข้าตาราง Zone Ledger เพียงครั้งเดียวต่อเครื่อง (รองรับทั้ง Authority และ Remote Peer เพื่อกัน Host Migration)
    /// </summary>
    private void MergeMonsterDamageIntoZoneLedgerOnce(PlayerCombat fallbackKiller)
    {
        if (_zoneLedgerMerged) return;
        int sig = ZoneSignature;
        if (sig == 0) return;
        _zoneLedgerMerged = true;

        bool recordedAny = false;
        IReadOnlyDictionary<PlayerRef, float> dmgMap = DamageDealtMap;
        if (dmgMap != null && dmgMap.Count > 0)
        {
            foreach (var kv in dmgMap)
            {
                if (kv.Key != PlayerRef.None && kv.Value > 0f)
                {
                    MonsterSpawnZone.RecordZoneDamageFromAuthority(sig, kv.Key, kv.Value);
                    recordedAny = true;
                }
            }
        }

        // เฉพาะกรณีพิเศษที่ไม่มีบันทึกใน DamageDealtMap จริงๆ (เช่น สั่งฆ่าในการทดสอบ) และรู้ตัวผู้โจมตีชัดเจนเท่านั้น
        if (!recordedAny && fallbackKiller != null && fallbackKiller.IsSpawned)
        {
            PlayerRef killerRef = PlayerRef.None;
            if (fallbackKiller.Object != null && fallbackKiller.Object.IsValid && fallbackKiller.Object.StateAuthority != PlayerRef.None)
                killerRef = fallbackKiller.Object.StateAuthority;
            else if (Runner != null && Runner.IsRunning)
                killerRef = Runner.LocalPlayer;

            if (killerRef != PlayerRef.None)
                MonsterSpawnZone.RecordZoneDamageFromAuthority(sig, killerRef, Mathf.Max(1f, ScaledMaxHealth));
        }
    }

    /// <summary>
    /// On State Authority, merges this dying monster's <see cref="EnemyNPC.DamageDealtMap"/> into its
    /// <see cref="MonsterSpawnZone"/> damage ledger and, if this was the last living monster in the zone,
    /// broadcasts the compact contributor payload to all peers so their LootChest unlocks for real participants!
    /// </summary>
    private void RecordAndCheckZoneClearOnAuthority(PlayerCombat fallbackKiller)
    {
        int sig = ZoneSignature;
        if (sig == 0) return;

        MergeMonsterDamageIntoZoneLedgerOnce(fallbackKiller);

        // Check if any other monster in this same MonsterSpawnZone is still alive
        for (int i = 0; i < EnemyNPC.All.Count; i++)
        {
            MonsterEnemyNPC other = EnemyNPC.All[i] as MonsterEnemyNPC;
            if (other == null || other == this || !other.IsSpawned || !other.IsAlive) continue;
            if (other.ZoneSignature == sig)
                return;
        }

        string payload = MonsterSpawnZone.BuildZoneContributorPayload(sig, IsBoss);
        RPC_NotifyMonsterZoneCleared(sig, payload);
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

        // 1. แจก Gold รายตัวจากมอนสเตอร์ผ่าน Targeted RPC ให้ผู้เล่นที่มีส่วนร่วมทำดาเมจจริงทุกคน (แก้บั๊ก Remote Client ไม่ได้รับทองใน Shared Mode)
        if (cfg.goldDropChancePercent > 0f && dropRateMult > 0f)
        {
            float effectiveGoldChance = Mathf.Clamp(cfg.goldDropChancePercent * dropRateMult, 0f, 100f);
            if (Random.value * 100f < effectiveGoldChance)
            {
                int minG = Mathf.Min(cfg.minGold, cfg.maxGold);
                int maxG = Mathf.Max(cfg.minGold, cfg.maxGold);
                int baseGoldAmount = Random.Range(minG, maxG + 1);
                if (baseGoldAmount > 0)
                {
                    PlayerRef fallbackKillerRef = ResolveKillerPlayerRefStrict();
                    LootDropSystem.AnalyzeDamageContribution(
                        DamageDealtMap,
                        fallbackKillerRef,
                        IsBoss,
                        out float totalDmg,
                        out _,
                        out List<PlayerRef> eligiblePlayers);

                    string cleanSource = string.IsNullOrWhiteSpace(cfg.monsterName) ? name : cfg.monsterName;
                    if (cleanSource.Length > 28) cleanSource = cleanSource.Substring(0, 28);

                    for (int pIdx = 0; pIdx < eligiblePlayers.Count; pIdx++)
                    {
                        PlayerRef targetRef = eligiblePlayers[pIdx];
                        if (targetRef == PlayerRef.None) continue;

                        float dealt = (DamageDealtMap != null && DamageDealtMap.TryGetValue(targetRef, out float d)) ? d : totalDmg;
                        float share = totalDmg > 0f ? Mathf.Clamp01(dealt / totalDmg) : 1f;
                        // ผู้ร่วมตีทุกคนที่ผ่านเกณฑ์ได้รับ Gold อย่างน้อย 65% ถึง 100% ตามสัดส่วนดาเมจ (Solo = 100%)
                        int award = Mathf.Max(1, Mathf.RoundToInt(baseGoldAmount * Mathf.Lerp(0.65f, 1.0f, share)));
                        RPC_GrantMonsterGold(targetRef, award, cleanSource);
                        if (verboseLogs) Debug.Log($"[MonsterEnemyNPC] {name} sent Targeted Gold RPC ({award}G) to {targetRef} (share={share * 100f:0}%).");
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
        // ผูกสิทธิ์เจ้าของไอเทม (AllowedLooter) ให้ Top DPS จาก DamageDealtMap (ไม่ใช้ FindLikelyAttacker 12m เพื่อกันคนยืนดูได้ของฟรี)
        PlayerRef killerRef = ResolveTopDpsPlayerRefStrict();

        WorldItem.Spawn(Runner, worldItemPrefab, dropPos, entry.item, qty, allowedLooter: killerRef);
    }

    private PlayerRef ResolveKillerPlayerRefStrict()
    {
        PlayerCombat killer = _lastKnownAttacker ?? ResolveCurrentTarget();
        if (killer != null && killer.IsSpawned && killer.Object != null && killer.Object.IsValid)
            return killer.Object.StateAuthority != PlayerRef.None ? killer.Object.StateAuthority : killer.Object.InputAuthority;
        return PlayerRef.None;
    }

    private PlayerRef ResolveTopDpsPlayerRefStrict()
    {
        PlayerRef fallback = ResolveKillerPlayerRefStrict();
        LootDropSystem.AnalyzeDamageContribution(DamageDealtMap, fallback, IsBoss, out _, out PlayerRef topDps, out _);
        return topDps != PlayerRef.None ? topDps : fallback;
    }

    private Vector3 ResolveLootGroundPoint(Vector3 candidate)
    {
        float footY = SandboxWorldSpawner.SampleGroundY(candidate, transform.position.y - GetGroundPivotHeight());
        return new Vector3(candidate.x, footY + 0.45f, candidate.z);
    }

    private void EnterGuardAuthority(float duration)
    {
        if (!Object.HasStateAuthority || !IsAlive || IsGuarding) return;
        CancelActiveMeleeSwing();
        _baseDefense = GetCanonicalBaseDefense();
        // High flat Guard defense absorbs the blocked blow (1 scratch dmg) so low-level monsters never die through an active Guard Shield!
        int scaledGuardDef = Mathf.Max(guardBonusDefense, 999, Mathf.RoundToInt(AttackDamage * 4.0f));
        Defense = _baseDefense + scaledGuardDef;
        IsGuarding = true;
        _guardUntil = Time.time + duration;
        _nextGuardAllowedAt = Time.time + duration + guardCooldown;
        _hyperArmorUntil = Time.time + duration;
        animatorBridge?.SetGuarding(true);
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
            animatorBridge?.SetGuarding(false);
        }
    }

    private Vector3 GetOverheadPopupPosition()
    {
        float modelHeight = animatorBridge?.Config != null ? Mathf.Max(0.8f, animatorBridge.Config.targetWorldHeight) : 2.15f;
        float footY = transform.position.y - GetGroundPivotHeight();
        return new Vector3(transform.position.x, footY + modelHeight + 0.35f, transform.position.z);
    }

    // ------------------------------------------------------------------ RPCs (All Peers)

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_MonsterPlayAttack(int attackIndex, NetworkBool isCounter)
    {
        animatorBridge?.SetGuarding(false);
        animatorBridge?.TriggerAttack(attackIndex);
        PlayRandomClip(animatorBridge?.Config?.attackSwingClips);
        if (isCounter)
        {
            FloatingDamagePopup.SpawnWarning(GetOverheadPopupPosition(), "COUNTER!", new Color(1.0f, 0.42f, 0.12f, 1f));
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
        Vector3 topPos = GetOverheadPopupPosition();

        switch (kind)
        {
            case MonsterGuardFeedback.GuardRaised:
                SetGuardVfxVisible(true);
                animatorBridge?.SetGuarding(true);
                PlayClip(animatorBridge?.Config?.guardRaiseClip);
                FloatingDamagePopup.SpawnWarning(topPos, "GUARD!", new Color(1.0f, 0.78f, 0.22f, 1f));
                break;

            case MonsterGuardFeedback.Blocked:
                animatorBridge?.SetGuarding(false);
                animatorBridge?.PlayBlockFlash();
                PlayClip(animatorBridge?.Config?.guardBlockClip);
                FloatingDamagePopup.SpawnWarning(topPos, "BLOCKED!", new Color(0.95f, 0.85f, 0.25f, 1f));
                break;

            case MonsterGuardFeedback.GuardBreak:
                SetGuardVfxVisible(false);
                SetStunVfxVisible(true);
                animatorBridge?.SetGuarding(false);
                animatorBridge?.PlayHitFlash();
                animatorBridge?.SetStunned(true);
                PlayClip(animatorBridge?.Config?.guardBreakClip);
                FloatingDamagePopup.SpawnWarning(topPos, "GUARD BREAK!", new Color(1.0f, 0.30f, 0.15f, 1f));
                break;
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_BossSlamTelegraph(Vector3 groundCenter, float radius, float duration)
    {
        animatorBridge?.StartSkillCast(0);
        // Sync with teammate's BossHudUI Cast Bar so "GROUND SLAM" fills live on the top Boss HUD!
        float safeDur = Mathf.Max(0.10f, duration);
        BaseTelegraphNameProp?.SetValue(this, "GROUND SLAM");
        BaseTelegraphDurationProp?.SetValue(this, safeDur);
        BaseTelegraphEndTimeProp?.SetValue(this, Time.time + safeDur);
        FloatingDamagePopup.SpawnWarning(GetOverheadPopupPosition(), "SLAM!", new Color(1.0f, 0.22f, 0.12f, 1f));
        if (_slamVfxRoutine != null) StopCoroutine(_slamVfxRoutine);
        _slamVfxRoutine = StartCoroutine(BossSlamTelegraphRoutine(groundCenter, radius, duration));
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_BossSlamImpact(Vector3 groundCenter, float radius)
    {
        ClearActiveSlamTelegraph();
        animatorBridge?.ReleaseSkill();
        PlayRandomClip(animatorBridge?.Config?.attackSwingClips);
        if (_slamVfxRoutine != null) StopCoroutine(_slamVfxRoutine);
        _slamVfxRoutine = StartCoroutine(BossSlamShockwaveRoutine(groundCenter, radius));
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_BossCleaveTelegraph(Vector3 groundCenter, float radius, float duration)
    {
        animatorBridge?.StartSkillCast(0);
        // ซิงก์ขึ้น BossHudUI Cast Bar ให้ผู้เล่นทุกคนเห็นคำว่า "360° CLEAVE" บนหัวจอแบบเรียลไทม์
        float safeDur = Mathf.Max(0.10f, duration);
        BaseTelegraphNameProp?.SetValue(this, "360° CLEAVE");
        BaseTelegraphDurationProp?.SetValue(this, safeDur);
        BaseTelegraphEndTimeProp?.SetValue(this, Time.time + safeDur);
        FloatingDamagePopup.SpawnWarning(GetOverheadPopupPosition() + Vector3.up * 0.65f, "CLEAVE!", new Color(1.0f, 0.32f, 0.12f, 1f));
        if (_slamVfxRoutine != null) StopCoroutine(_slamVfxRoutine);
        _slamVfxRoutine = StartCoroutine(BossSlamTelegraphRoutine(groundCenter, radius, duration));
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_BossCleaveImpact(Vector3 groundCenter, float radius)
    {
        ClearActiveSlamTelegraph();
        animatorBridge?.ReleaseSkill();
        int swingIdx = animatorBridge != null && animatorBridge.AttackVariantCount >= 2 ? 1 : 0;
        animatorBridge?.TriggerAttack(swingIdx);
        PlayRandomClip(animatorBridge?.Config?.attackSwingClips);
        if (_slamVfxRoutine != null) StopCoroutine(_slamVfxRoutine);
        _slamVfxRoutine = StartCoroutine(BossCleaveSpinAndWaveRoutine(groundCenter, radius));
    }

    private IEnumerator BossCleaveSpinAndWaveRoutine(Vector3 groundCenter, float radius)
    {
        EnsureSharedVfxMaterials();
        GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "BossCleave360WaveVFX";
        var col = ring.GetComponent<Collider>();
        if (col != null) Destroy(col);
        ring.transform.position = new Vector3(groundCenter.x, groundCenter.y + 0.85f, groundCenter.z);
        var wr = ring.GetComponent<Renderer>();
        if (wr != null)
        {
            wr.shadowCastingMode = ShadowCastingMode.Off;
            wr.receiveShadows = false;
            if (s_slamInnerFillMat != null) wr.sharedMaterial = s_slamInnerFillMat;
        }

        // หมุนตัวบอสอยู่กับที่ 360° (ไม่เคลื่อนที่ จึงไม่ทะลุกำแพงและไม่กระตุกข้ามเน็ต) พร้อมวงคลื่นดาบกวาดรอบตัว
        Quaternion startRot = transform.rotation;
        const float spinDur = 0.28f;
        float elapsed = 0f;
        while (elapsed < spinDur && ring != null)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / spinDur);
            if (Object != null && Object.IsValid && Object.HasStateAuthority && IsAlive)
                transform.rotation = startRot * Quaternion.Euler(0f, 360f * t, 0f);

            float diam = Mathf.Lerp(radius * 0.45f, radius * 2.15f, t);
            float h = Mathf.Lerp(0.18f, 0.02f, t);
            ring.transform.localScale = new Vector3(diam, h, diam);
            yield return null;
        }

        if (Object != null && Object.IsValid && Object.HasStateAuthority && IsAlive)
            transform.rotation = startRot;
        if (ring != null) Destroy(ring);
        _slamVfxRoutine = null;
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_BossEnrageFeedback()
    {
        animatorBridge?.SetPersistentEnrageGlow(true);
        animatorBridge?.SetSpeedScale(1.18f);
        FloatingDamagePopup.SpawnWarning(GetOverheadPopupPosition(), "ENRAGE!", new Color(1.0f, 0.14f, 0.14f, 1f));
    }

    /// <summary>
    /// Targeted RPC ส่ง Gold รายตัวของมอนสเตอร์ตรงไปยังผู้เล่นที่มีส่วนร่วมทำดาเมจ (ทำงานได้ถูกต้องทั้ง Host และ Remote Client ใน Shared Mode)
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_GrantMonsterGold([RpcTarget] PlayerRef targetPlayer, int amount, NetworkString<_32> source)
    {
        if (amount <= 0 || Runner == null) return;
        if (targetPlayer != PlayerRef.None && Runner.LocalPlayer != targetPlayer) return;

        var local = PlayerCombat.Local;
        if (local != null && local.IsSpawned)
        {
            string src = source.ToString();
            local.AddGold(amount, string.IsNullOrEmpty(src) ? "Monster" : src);
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_NotifyMonsterZoneCleared(int clearedZoneSignature, NetworkString<_128> contributorPayload)
    {
        MonsterSpawnZone.HandleAuthoritativeZoneCleared(clearedZoneSignature, contributorPayload.ToString());
    }

    private void OnGuardVisualStateChanged()
    {
        bool guarding = IsGuarding && IsAlive;
        SetGuardVfxVisible(guarding);
        animatorBridge?.SetGuarding(guarding);
    }

    private void OnStunVisualStateChanged()
    {
        bool stunned = IsStunnedNet && IsAlive;
        animatorBridge?.SetStunned(stunned);
        SetStunVfxVisible(stunned);
    }

    private void OnEnrageVisualStateChanged()
    {
        bool enraged = IsEnragedNet && IsAlive;
        animatorBridge?.SetPersistentEnrageGlow(enraged);
        animatorBridge?.SetSpeedScale(enraged ? 1.18f : 1.0f);
    }

    // ------------------------------------------------------------------ Shared-Material Combat VFX (Guard + Stun + Parry + Boss Slam)

    private static void EnsureSharedVfxMaterials()
    {
        if (s_guardBarrierMat != null && s_guardRingMat != null && s_stunOrbMat != null &&
            s_parryGlintMat != null && s_bruteTelegraphMat != null &&
            s_slamOuterRingMat != null && s_slamInnerFillMat != null) return;

        // Use teammate's Resources-backed URP Transparent Material so Unity NEVER strips the shader in Standalone (.exe) Builds!
        Material urpTransparentTemplate = UrpMaterialHelper.GetUnlitTransparentMaterial();
        Shader fallbackShader = urpTransparentTemplate == null
            ? (Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color"))
            : null;
        if (urpTransparentTemplate == null && fallbackShader == null) return;

        if (s_guardBarrierMat == null)
            s_guardBarrierMat = CreateSharedTransparentVfxMaterial(urpTransparentTemplate, fallbackShader, "Mat_MonsterGuardBarrier_Shared", new Color(1.0f, 0.68f, 0.16f, 0.30f));
        if (s_guardRingMat == null)
            s_guardRingMat = CreateSharedTransparentVfxMaterial(urpTransparentTemplate, fallbackShader, "Mat_MonsterGuardRing_Shared", new Color(1.0f, 0.78f, 0.22f, 0.55f));
        if (s_stunOrbMat == null)
            s_stunOrbMat = CreateSharedTransparentVfxMaterial(urpTransparentTemplate, fallbackShader, "Mat_MonsterStunOrb_Shared", new Color(1.0f, 0.90f, 0.25f, 0.95f));
        if (s_parryGlintMat == null)
            s_parryGlintMat = CreateSharedTransparentVfxMaterial(urpTransparentTemplate, fallbackShader, "Mat_MonsterParryGlint_Shared", new Color(1.0f, 0.34f, 0.12f, 0.92f));
        if (s_bruteTelegraphMat == null)
            s_bruteTelegraphMat = CreateSharedTransparentVfxMaterial(urpTransparentTemplate, fallbackShader, "Mat_MonsterBruteRing_Shared", new Color(0.95f, 0.16f, 0.10f, 0.45f));
        if (s_slamOuterRingMat == null)
            s_slamOuterRingMat = CreateSharedTransparentVfxMaterial(urpTransparentTemplate, fallbackShader, "Mat_BossSlamOuter_Shared", new Color(1.0f, 0.15f, 0.10f, 0.42f));
        if (s_slamInnerFillMat == null)
            s_slamInnerFillMat = CreateSharedTransparentVfxMaterial(urpTransparentTemplate, fallbackShader, "Mat_BossSlamInner_Shared", new Color(1.0f, 0.32f, 0.10f, 0.55f));
    }

    private static Material CreateSharedTransparentVfxMaterial(Material urpTemplate, Shader fallbackShader, string matName, Color col)
    {
        Material mat = urpTemplate != null ? new Material(urpTemplate) : new Material(fallbackShader);
        mat.name = matName;
        if (urpTemplate != null)
            UrpMaterialHelper.ConfigureUrpTransparent(mat);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", col);
        return mat;
    }

    private void ClearActiveSlamTelegraph()
    {
        if (_activeSlamTelegraphGo != null)
        {
            Destroy(_activeSlamTelegraphGo);
            _activeSlamTelegraphGo = null;
        }
    }

    private IEnumerator BossSlamTelegraphRoutine(Vector3 groundCenter, float radius, float duration)
    {
        EnsureSharedVfxMaterials();
        ClearActiveSlamTelegraph();

        GameObject root = new GameObject("BossSlamTelegraphVFX");
        root.transform.position = new Vector3(groundCenter.x, groundCenter.y + 0.06f, groundCenter.z);
        _activeSlamTelegraphGo = root;

        GameObject outerDisc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        outerDisc.name = "OuterBoundary";
        var oc = outerDisc.GetComponent<Collider>();
        if (oc != null) Destroy(oc);
        outerDisc.transform.SetParent(root.transform, false);
        outerDisc.transform.localScale = new Vector3(radius * 2f, 0.015f, radius * 2f);
        var or = outerDisc.GetComponent<Renderer>();
        if (or != null)
        {
            or.shadowCastingMode = ShadowCastingMode.Off;
            or.receiveShadows = false;
            if (s_slamOuterRingMat != null) or.sharedMaterial = s_slamOuterRingMat;
        }

        GameObject innerFill = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        innerFill.name = "InnerFill";
        var ic = innerFill.GetComponent<Collider>();
        if (ic != null) Destroy(ic);
        innerFill.transform.SetParent(root.transform, false);
        innerFill.transform.localPosition = new Vector3(0f, 0.02f, 0f);
        innerFill.transform.localScale = new Vector3(0.2f, 0.02f, 0.2f);
        var ir = innerFill.GetComponent<Renderer>();
        if (ir != null)
        {
            ir.shadowCastingMode = ShadowCastingMode.Off;
            ir.receiveShadows = false;
            if (s_slamInnerFillMat != null) ir.sharedMaterial = s_slamInnerFillMat;
        }

        float start = Time.time;
        float safeDur = Mathf.Max(0.1f, duration);
        while (Time.time - start < safeDur && _activeSlamTelegraphGo != null && IsAlive)
        {
            float t = Mathf.Clamp01((Time.time - start) / safeDur);
            float d = Mathf.Lerp(0.3f, radius * 2f, t);
            innerFill.transform.localScale = new Vector3(d, 0.02f, d);
            yield return null;
        }

        ClearActiveSlamTelegraph();
        _slamVfxRoutine = null;
    }

    private IEnumerator BossSlamShockwaveRoutine(Vector3 groundCenter, float radius)
    {
        EnsureSharedVfxMaterials();
        GameObject wave = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        wave.name = "BossSlamShockwaveVFX";
        var wc = wave.GetComponent<Collider>();
        if (wc != null) Destroy(wc);
        wave.transform.position = new Vector3(groundCenter.x, groundCenter.y + 0.12f, groundCenter.z);
        var wr = wave.GetComponent<Renderer>();
        if (wr != null)
        {
            wr.shadowCastingMode = ShadowCastingMode.Off;
            wr.receiveShadows = false;
            if (s_slamInnerFillMat != null) wr.sharedMaterial = s_slamInnerFillMat;
        }

        float start = Time.time;
        const float waveDur = 0.28f;
        while (Time.time - start < waveDur && wave != null)
        {
            float t = (Time.time - start) / waveDur;
            float diam = Mathf.Lerp(radius * 0.6f, radius * 2.25f, t);
            float h = Mathf.Lerp(0.28f, 0.01f, t);
            wave.transform.localScale = new Vector3(diam, h, diam);
            yield return null;
        }

        if (wave != null) Destroy(wave);
        _slamVfxRoutine = null;
    }

    private void BuildCombatVfx()
    {
        EnsureSharedVfxMaterials();

        // Dynamically calibrate VFX height & size to this monster's real world height (Slime 1.1m .. BossOgre 3.2m!)
        float modelHeight = animatorBridge?.Config != null ? Mathf.Max(0.8f, animatorBridge.Config.targetWorldHeight) : 2.15f;
        float rootScaleY = Mathf.Max(0.1f, transform.localScale.y);
        float groundLocalY = -GetGroundPivotHeight() / rootScaleY;
        float heightScale = Mathf.Clamp(modelHeight / 2.15f, 0.55f, 1.45f);
        _vfxHeightScale = heightScale;
        float chestLocalY = groundLocalY + (modelHeight * 0.50f / rootScaleY);
        float headLocalY = groundLocalY + (modelHeight / rootScaleY) + (0.22f / rootScaleY);

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
                barrier.transform.localPosition = new Vector3(0f, chestLocalY, 0.55f * heightScale);
                barrier.transform.localScale = new Vector3(1.10f * heightScale, 1.25f * heightScale, 0.35f * heightScale);
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
                ring.transform.localPosition = new Vector3(0f, groundLocalY + 0.04f, 0f);
                ring.transform.localScale = new Vector3(1.60f * heightScale, 0.02f, 1.60f * heightScale);
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

        // 2. Orbiting Stun Halo VFX (auto-positioned right above the monster's real head height!)
        if (_stunVfxRoot == null)
        {
            Transform existingStun = transform.Find("MonsterStunVFX");
            if (existingStun != null)
            {
                _stunVfxRoot = existingStun.gameObject;
                _stunOrbitPivot = existingStun.Find("OrbitPivot") ?? existingStun;
                _stunVfxRoot.transform.localPosition = new Vector3(0f, headLocalY, 0f);
                _stunVfxRoot.SetActive(false);
            }
            else
            {
                _stunVfxRoot = new GameObject("MonsterStunVFX");
                _stunVfxRoot.transform.SetParent(transform, false);
                _stunVfxRoot.transform.localPosition = new Vector3(0f, headLocalY, 0f);

                var pivotGo = new GameObject("OrbitPivot");
                pivotGo.transform.SetParent(_stunVfxRoot.transform, false);
                _stunOrbitPivot = pivotGo.transform;

                const int orbCount = 3;
                float orbitRadius = 0.42f * Mathf.Clamp(heightScale, 0.75f, 1.35f);
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
                glint.transform.localPosition = new Vector3(0.15f * heightScale, chestLocalY + 0.20f, 0.65f * heightScale);
                glint.transform.localRotation = Quaternion.Euler(45f, 45f, 0f);
                glint.transform.localScale = Vector3.one * (0.30f * heightScale);
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
                ring.transform.localPosition = new Vector3(0f, groundLocalY + 0.05f, 0.65f * heightScale);
                ring.transform.localScale = new Vector3(1.5f * heightScale, 0.02f, 1.5f * heightScale);
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
        // เลือกพื้นผิวที่เดินได้ซึ่งอยู่ใกล้ระดับเท้าปัจจุบันที่สุด (ป้องกันมอนสเตอร์ดีดขึ้นไปบนยอดต้นไม้ หลังคาเต็นท์ หรือยืนบนผิวน้ำ)
        float currentFootY = pos.y - GetGroundPivotHeight();
        Vector3 origin = new Vector3(pos.x, pos.y + searchUp, pos.z);
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, s_monsterCastBuf, searchUp + searchDown, ~0, QueryTriggerInteraction.Ignore);
        float bestY = currentFootY;
        float bestDelta = float.MaxValue;
        bool found = false;

        for (int i = 0; i < count; i++)
        {
            RaycastHit h = s_monsterCastBuf[i];
            if (h.collider == null || h.collider == BodyCollider || h.normal.y < 0.45f) continue;
            if (h.collider.GetComponentInParent<EnemyNPC>() != null) continue;
            if (h.collider.GetComponentInParent<PlayerCombat>() != null) continue;
            if (h.collider.name.IndexOf("Water", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;

            float delta = Mathf.Abs(h.point.y - currentFootY);
            if (!found || delta < bestDelta)
            {
                bestDelta = delta;
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
