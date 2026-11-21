using System;
using System.Collections;
using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

public enum PlayerStatType
{
    STR = 0, // +4 Attack Damage per point
    VIT = 1, // +15 Max HP per point
    END = 2, // +12 Max Stamina per point
    AGI = 3  // +3% Crit Chance & +0.18 Move Speed per point
}

/// <summary>
/// ระบบต่อสู้ระยะประชิดและจัดการค่าพลังผู้เล่นผ่าน Photon Fusion Shared Mode (MMORPG Combat Core):
///   - รองรับ IClassCombatHandler เพื่อขยายคลาส Magic ในอนาคตโดยไม่ต้องรื้อโค้ดหลัก
///   - ขับเคลื่อนท่าโจมตีครบทุกชุดของ GreatSword (Light Combo 1..3, Heavy Slash 1..3, Run Attack, Jump Attack, Stealth Assassination)
///   - ระบบ Stamina, การตั้งการ์ดป้องกัน (ลดดาเมจ 75% + Block Impact + Guard Break), การกลิ้งหลบ, Buff และ Hitbox ใบดาบ
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class PlayerCombat : NetworkBehaviour, IClassCombatHandler
{
    private const int DefaultMaxHealth = 100;
    private const float DefaultMaxStamina = 100f;
    public const int StatPointsPerLevel = 3;

    // ------------------------------------------------------------------ inspector

    [Header("References")]
    [Tooltip("Trigger collider on the weapon blade (Sword_Hand/WeaponHitbox), enabled only during the active attack window.")]
    [SerializeField] private Collider weaponHitbox;
    [Tooltip("Optional. Receives the attack trigger on every peer.")]
    [SerializeField] private Animator animator;
    [SerializeField] private string attackTriggerName = "Attack";
    [Tooltip("Optional. Played on every peer when an attack starts.")]
    [SerializeField] private ParticleSystem attackVfx;
    [Tooltip("Deprecated debug renderer. Always kept hidden during Play Mode so the swing hitbox is invisible.")]
    [SerializeField] private Renderer hitboxDebugVisual;

    [Header("Attack & Combo Criticals")]
    [Min(0.05f)] [SerializeField] private float attackCooldown = 0.6f;
    [Tooltip("A click made this early during the cooldown is remembered and fired when it ends.")]
    [Min(0f)] [SerializeField] private float inputBufferSeconds = 0.2f;
    [Tooltip("The authority accepts requests this much earlier than the cooldown (jitter tolerance).")]
    [Min(0f)] [SerializeField] private float cooldownTolerance = 0.1f;
    [Min(1)] [SerializeField] private int attackDamage = 20;
    [Tooltip("Time window after a swing to chain into the next combo step (1 -> 2 -> 3).")]
    [Min(0.2f)] [SerializeField] private float comboResetSeconds = 1.45f;
    [Tooltip("Damage multiplier applied on chained combo step 2 (Critical Hit).")]
    [Min(1f)] [SerializeField] private float comboStep2CritMultiplier = 1.5f;
    [Tooltip("Damage multiplier applied on chained combo finisher step 3 (Critical Hit).")]
    [Min(1f)] [SerializeField] private float comboStep3CritMultiplier = 2.0f;

    [Header("Block & Dodge (ตั้งการ์ดป้องกันและกลิ้งหลบ)")]
    [Range(0.1f, 1f)] [SerializeField] private float blockMoveSpeedMultiplier = 0.45f;
    // ลดดาเมจที่ได้รับลง 75% ขณะตั้งการ์ดป้องกันด้วยดาบใหญ่
    [Range(0f, 0.95f)] [SerializeField] private float blockDamageReduction = 0.75f;
    [Min(0.1f)] [SerializeField] private float dodgeCooldown = 0.75f;
    [Min(0.1f)] [SerializeField] private float dodgeDuration = 0.45f;
    [Min(0.5f)] [SerializeField] private float dodgeDistance = 4.2f;

    [Header("Timed Parry (ปัดป้องสมบูรณ์ / Perfect Block)")]
    // หน้าต่างเวลาหลังจากกดเริ่มตั้งการ์ดที่จะนับเป็น Perfect Parry (รับดาเมจ 0 + ศัตรูติด Stagger)
    [Range(0.05f, 0.50f)] [SerializeField] private float parryWindowSeconds = 0.25f;
    // คูลดาวน์ขั้นต่ำระหว่างการเปิดหน้าต่าง Parry แต่ละครั้ง เพื่อป้องกันการกดย้ำปุ่มบล็อกรัวๆ
    [Range(0.10f, 1.50f)] [SerializeField] private float parryCooldownSeconds = 0.45f;
    // ระยะเวลาที่มอนสเตอร์ทั่วไปจะติดสถานะ Stagger เมื่อถูก Perfect Parry
    [Range(0.3f, 3.0f)] [SerializeField] private float parryEnemyStaggerDuration = 1.45f;
    // ระยะเวลาที่บอสจะติดสถานะ Stagger เมื่อถูก Perfect Parry ในระยะประชิด
    [Range(0.2f, 2.0f)] [SerializeField] private float parryBossStaggerDuration = 0.70f;
    // โบนัส Stamina ที่คืนให้เมื่อปัดป้องสมบูรณ์สำเร็จ
    [Min(0f)] [SerializeField] private float parryStaminaReward = 12f;

    [Header("Combat Feel (Hit-Stop & Camera Shake)")]
    // เปิด/ปิดการชะลอภาพอนิเมชันชั่วคราว (Micro Hit-Stop) เฉพาะฝั่งผู้เล่นเมื่อฟันโดนหรือ Parry สำเร็จ
    [SerializeField] private bool enableCombatHitStop = true;
    // เปิด/ปิดแรงสั่นกล้องตามน้ำหนักการปะทะ
    [SerializeField] private bool enableCameraShake = true;

    [Header("Stamina")]
    [Min(10f)] [SerializeField] private float maxStamina = DefaultMaxStamina;
    [Min(1f)] [SerializeField] private float sprintStaminaPerSecond = 15f;
    [Min(1f)] [SerializeField] private float attackStaminaCost = 15f;
    [Min(1f)] [SerializeField] private float dodgeStaminaCost = 25f;
    [Min(1f)] [SerializeField] private float blockHitStaminaCost = 18f;
    [Min(1f)] [SerializeField] private float staminaRegenRate = 22f;
    [Min(0f)] [SerializeField] private float staminaRegenDelay = 1.1f;

    [Header("Hit window")]
    [Tooltip("ON: Animation Events call AnimEvent_OpenHitWindow / AnimEvent_CloseHitWindow. OFF: timed coroutine.")]
    [SerializeField] private bool useAnimationEvents;
    [Min(0f)] [SerializeField] private float hitWindowDelay = 0.08f;
    [Min(0.01f)] [SerializeField] private float hitWindowDuration = 0.30f;
    [Tooltip("Hard cap: the window is force-closed after this long, even if an animation event never arrives.")]
    [Min(0.05f)] [SerializeField] private float hitWindowSafetyTimeout = 1f;

    [Header("Validation (applied by the victim's State Authority)")]
    [Tooltip("Upper bound for any single damage application.")]
    [Min(1)] [SerializeField] private int maxDamagePerHit = 400;
    [Tooltip("Attacker and victim must be at most this far apart (generous: remote proxies lag).")]
    [Min(0.5f)] [SerializeField] private float maxHitDistance = 5f;

    [Header("Health")]
    [Min(1)] [SerializeField] private int maxHealth = DefaultMaxHealth;
    [Tooltip("Seconds until a dead player is restored at its spawn point. 0 or less = stay dead.")]
    [SerializeField] private float respawnDelaySeconds = 3f;

    [Header("Debug")]
#pragma warning disable CS0414
    [SerializeField] private bool showDebugHud = false;
#pragma warning restore CS0414

    [Header("Events (all peers)")]
    public UnityEvent onAttackStarted;

    // ------------------------------------------------------------------ networked state

    /// <summary>Replicated current health.</summary>
    [Networked, OnChangedRender(nameof(OnHealthChanged))]
    public int NetworkHealth { get; set; } = DefaultMaxHealth;

    /// <summary>Replicated player stamina (0..MaxStamina).</summary>
    [Networked, OnChangedRender(nameof(OnStaminaChanged))]
    public float NetworkStamina { get; set; } = DefaultMaxStamina;

    /// <summary>Replicated blocking stance.</summary>
    [Networked, OnChangedRender(nameof(OnBlockingChanged))]
    public NetworkBool IsBlocking { get; set; }

    /// <summary>
    /// สถานะชักดาบ/เก็บดาบที่ซิงก์ผ่าน Photon Fusion ไปยังทุกเครื่องในห้อง (true = ถือดาบพร้อมสู้, false = เก็บดาบไว้กลางหลังในโหมดสำรวจ)
    /// </summary>
    [Networked, OnChangedRender(nameof(OnWeaponDrawnChanged))]
    public NetworkBool IsWeaponDrawn { get; set; }

    /// <summary>Replicated current combo step (1..3).</summary>
    [Networked]
    public int ComboStep { get; set; } = 1;

    // ---- Networked Progression & Stat Allocation ----
    [Networked, OnChangedRender(nameof(OnProgressionNetChanged))]
    public int NetworkLevel { get; set; } = 1;

    [Networked, OnChangedRender(nameof(OnProgressionNetChanged))]
    public int NetworkExperience { get; set; } = 0;

    [Networked, OnChangedRender(nameof(OnProgressionNetChanged))]
    public int NetworkUnspentStatPoints { get; set; } = 0;

    [Networked, OnChangedRender(nameof(OnProgressionNetChanged))]
    public int StatPointsSTR { get; set; } = 0;

    [Networked, OnChangedRender(nameof(OnProgressionNetChanged))]
    public int StatPointsVIT { get; set; } = 0;

    [Networked, OnChangedRender(nameof(OnProgressionNetChanged))]
    public int StatPointsEND { get; set; } = 0;

    [Networked, OnChangedRender(nameof(OnProgressionNetChanged))]
    public int StatPointsAGI { get; set; } = 0;

    // ---- Networked Economy & Safe Zone ----
    [Networked, OnChangedRender(nameof(OnProgressionNetChanged))]
    public int NetworkGold { get; set; } = 0;

    [Networked]
    public NetworkBool IsInSafeZone { get; set; }

    // ---- Networked Equipped Gear Aggregated Bonuses ----
    [Networked, OnChangedRender(nameof(OnProgressionNetChanged))]
    public int NetworkGearDamageBonus { get; set; } = 0;

    [Networked, OnChangedRender(nameof(OnProgressionNetChanged))]
    public int NetworkGearMaxHealthBonus { get; set; } = 0;

    [Networked, OnChangedRender(nameof(OnProgressionNetChanged))]
    public int NetworkGearMaxStaminaBonus { get; set; } = 0;

    [Networked, OnChangedRender(nameof(OnProgressionNetChanged))]
    public int NetworkGearDefenseBonus { get; set; } = 0;

    [Networked, OnChangedRender(nameof(OnProgressionNetChanged))]
    public float NetworkGearMoveSpeedBonus { get; set; } = 0f;

    [Networked, OnChangedRender(nameof(OnProgressionNetChanged))]
    public float NetworkGearCritBonus { get; set; } = 0f;

    // ------------------------------------------------------------------ public API (Aggregated Stats)

    public int BaseMaxHealth => maxHealth;
    public float BaseMaxStamina => maxStamina;
    public int BaseAttackDamage => attackDamage;

    /// <summary>Total Max HP = Base + Level Bonus (+10/lv) + VIT (+15/pt) + Equipped Gear.</summary>
    public int MaxHealth => _isSpawned
        ? maxHealth + Mathf.Max(0, NetworkLevel - 1) * 10 + StatPointsVIT * 15 + NetworkGearMaxHealthBonus
        : maxHealth;

    /// <summary>Total Max Stamina = Base + Level Bonus (+5/lv) + END (+12/pt) + Equipped Gear.</summary>
    public float MaxStamina => _isSpawned
        ? maxStamina + Mathf.Max(0, NetworkLevel - 1) * 5f + StatPointsEND * 12f + NetworkGearMaxStaminaBonus
        : maxStamina;

    /// <summary>Total Attack Damage = Base + Level Bonus (+2/lv) + STR (+4/pt) + Equipped Gear + Active Buff.</summary>
    public int TotalAttackDamage => _isSpawned
        ? attackDamage + Mathf.Max(0, NetworkLevel - 1) * 2 + StatPointsSTR * 4 + NetworkGearDamageBonus + ActiveBuffDamageBonus
        : attackDamage;

    /// <summary>Total Defense (flat damage reduction from Armor/Shield/Gear).</summary>
    public int TotalDefense => _isSpawned ? NetworkGearDefenseBonus : 0;

    /// <summary>Total bonus movement speed from AGI (+0.18/pt) and Equipped Pants/Gear.</summary>
    public float TotalMoveSpeedBonus => _isSpawned ? (StatPointsAGI * 0.18f + NetworkGearMoveSpeedBonus) : 0f;

    /// <summary>Total critical hit chance (Base 5% + AGI 3%/pt + Equipped Gear).</summary>
    public float TotalCritChance => _isSpawned
        ? Mathf.Clamp01(0.05f + StatPointsAGI * 0.03f + NetworkGearCritBonus)
        : 0.05f;

    public int ExpToNextLevel => GetExpRequiredForNextLevel(_isSpawned ? NetworkLevel : 1);
    public bool IsInCombat => _isSpawned && (Time.time - _lastCombatActivityTime < 6f);

    public float CurrentStamina => _isSpawned ? NetworkStamina : maxStamina;
    public bool CanSprint => !IsDead && CurrentStamina > 0.5f && !_sprintExhaustedCooldown;
    // ต้องชักดาบออกมาก่อน (IsWeaponDrawn) และอยู่นอกเขตปลอดภัยจึงจะโจมตีได้
    public bool CanAttack => !IsDead && IsWeaponDrawn && !IsInSafeZone && !SafeZone.IsPointInAnySafeZone(transform.position) && CurrentStamina > 0f;
    public bool CanDodge => !IsDead && CurrentStamina > 0f;
    // ข้อความแจ้งเตือนเมื่อผู้เล่นพยายามโจมตีหรือป้องกันขณะยังเก็บดาบอยู่กลางหลัง
    public const string DrawWeaponPromptMessage = "Press [1] to Draw Weapon";
    public bool IsDead => _isSpawned && NetworkHealth <= 0;
    public bool IsHitWindowOpen { get; private set; }
    public bool IsDodging => Time.time < _dodgeInvulnerableUntil;
    public Collider WeaponHitbox => weaponHitbox;
    public GameObject BlockVfxObject => _blockVfxRoot;
    public bool IsBlockVfxActive => _blockVfxRoot != null && _blockVfxRoot.activeSelf;
    // อัตราการลดดาเมจขณะตั้งการ์ดปกติ (เช่น 0.75 = 75%)
    public float BlockDamageReduction => blockDamageReduction;
    // ช่วงเวลาหน้าต่างปัดป้องสมบูรณ์ (วินาที)
    public float ParryWindowSeconds => parryWindowSeconds;
    // ตรวจสอบว่าผู้เล่นกำลังอยู่ในหน้าต่างเวลา Perfect Parry หรือไม่
    public bool IsInParryWindow => IsBlocking && (Time.time - _blockStartedTime <= parryWindowSeconds);

    // อ้างอิงรูปแบบโจมตีปัจจุบัน (สำหรับสั่งเล่นอนิเมชันและคำนวณตัวคูณดาเมจ)
    public PlayerAttackKind CurrentAttackKind { get; private set; } = PlayerAttackKind.LightCombo;

    // ------------------------------------------------------------------ IClassCombatHandler (รองรับการเพิ่มคลาส Magic ในอนาคต)

    // คลาสต่อสู้ปัจจุบันของผู้เล่น (GreatSword)
    public CombatClassType ActiveClass => CombatClassType.GreatSword;

    // ประเภทอาวุธที่สวมใส่อยู่ปัจจุบัน
    public WeaponArchetype ActiveWeaponArchetype
    {
        get
        {
            var inv = InventoryManager.Local;
            if (inv != null)
            {
                var eqWeapon = inv.GetEquippedItem(EquipmentSlotType.Sword);
                if (eqWeapon != null) return eqWeapon.weaponArchetype;
            }
            return WeaponArchetype.GreatSword;
        }
    }

    public bool CanPerformPrimaryAttack() => CanAttack;
    public bool TryPerformPrimaryAttack() => TryRequestAttack();
    public bool TryPerformSecondaryAction() => TryRequestAttackKind(ResolveContextualHeavyAttackKind());

    /// <summary>Sequence number of the swing this peer last saw start (identifies a swing for de-duplication).</summary>
    public int CurrentSequence { get; private set; }

    /// <summary>(current, max) - raised on every peer when health changes.</summary>
    public event Action<int, int> HealthChanged;

    /// <summary>(current, max) - raised on every peer when stamina changes.</summary>
    public event Action<float, float> StaminaChanged;

    /// <summary>Raised when Level, EXP, Stat Points, or Equipped Gear stats change.</summary>
    public event Action ProgressionChanged;

    /// <summary>true = died, false = revived. Raised on every peer.</summary>
    public event Action<bool> DeathStateChanged;

    // ---- อีเวนต์การต่อสู้สำหรับทีม Audio / VFX / UI (Team Handoff Contract) ----
    // ยิงบนทุก Peer เมื่อตัวละครเริ่มออกท่าโจมตี (รูปแบบท่า, จังหวะคอมโบ 1..3)
    public event Action<PlayerAttackKind, int> AttackPerformed;
    // ยิงเมื่อดาบฟันโดนมอนสเตอร์/บอส (เป้าหมาย, ดาเมจสุทธิ, ติดคริหรือไม่)
    public event Action<EnemyNPC, int, bool> HitLanded;
    // ยิงบนทุก Peer เมื่อทำ Perfect Parry สำเร็จ (ส่งศัตรูผู้โจมตีมาด้วยถ้ามี)
    public event Action<EnemyNPC> ParrySucceeded;
    // ยิงบนทุก Peer เมื่อตั้งการ์ดรับดาเมจปกติสำเร็จ (ดาเมจหลังหักลดแล้ว)
    public event Action<int> BlockImpacted;
    // ยิงบนทุก Peer เมื่อ Stamina หมดขณะตั้งการ์ดจนการ์ดแตก
    public event Action GuardBroken;
    // ยิงบนทุก Peer เมื่อผู้เล่นชักดาบ (true) หรือเก็บดาบ (false)
    public event Action<bool> WeaponDrawnChanged;

    /// <summary>Every spawned player on this peer (local + remote proxies). Used by NPC AI and the map.</summary>
    public static readonly List<PlayerCombat> All = new List<PlayerCombat>();

    /// <summary>The player this peer controls.</summary>
    public static PlayerCombat Local { get; private set; }

    public bool IsSpawned => _isSpawned;

    /// <summary>Legacy setter compatibility for WeaponDamageBonus.</summary>
    public int WeaponDamageBonus
    {
        get => _isSpawned ? NetworkGearDamageBonus : 0;
        set
        {
            if (_isSpawned && Object.HasStateAuthority)
                NetworkGearDamageBonus = value;
        }
    }

    /// <summary>Temporary attack buff from consumables (e.g. Reinforced GreatSword Oil).</summary>
    public int ActiveBuffDamageBonus => Time.time < _attackBuffUntil ? _attackBuffBonus : 0;
    public float ActiveBuffRemainingSeconds => Mathf.Max(0f, _attackBuffUntil - Time.time);

    public static int GetExpRequiredForNextLevel(int currentLevel)
    {
        return 100 + Mathf.Max(0, currentLevel - 1) * 75;
    }

    // ------------------------------------------------------------------ private state

    private PlayerCapsule _capsule;
    private PlayerAnimationDriver _animationDriver;
    private Rigidbody _rb;
    private Collider _bodyCollider;
    private NetworkTransform _networkTransform;
    private int _attackTriggerHash;

    private bool _isSpawned;
    private bool _lastDeadState;
    private int _lastHealthValue = DefaultMaxHealth;
    private Vector3 _spawnPosition;

    // Local (input side)
    private float _nextLocalAttackTime;
    private float _lastLocalAttackTime = -100f;
    private int _localPredictedComboStep;
    private float _bufferedUntil = -1f;
    private bool _bufferedHeavy;
    private float _nextLocalDodgeTime;
    private float _overrideBlockUntil;
    private bool _localBlockingSent;
    // ตัวหน่วงคูลดาวน์การสลับชัก/เก็บดาบ (ปุ่ม 1) และเวลาล่าสุดที่แสดงข้อความเตือนแต่ละประเภทแยกกัน
    private float _nextWeaponToggleTime;
    private float _lastSheathedPromptTime = -10f;
    private float _lastSafeZonePromptTime = -10f;

    // Authority side
    private float _nextAuthorityAttackTime;
    private float _lastAuthorityAttackTime = -100f;
    private float _lastCombatActivityTime = -100f;
    private int _authorityComboStep;
    private int _sequenceCounter;
    private float _dodgeInvulnerableUntil;
    private float _blockStartedTime = -100f;
    private float _nextParryReadyTime = -100f;
    private float _parryFlashUntil = -100f;
    private float _nextStaminaRegenTime;
    private bool _sprintExhaustedCooldown;
    private int _attackBuffBonus;
    private float _attackBuffUntil;
    private Coroutine _respawnRoutine;

    // Block VFX
    private GameObject _blockVfxRoot;
    private Transform _blockDome;
    private Transform _blockRing;
    private Renderer _blockDomeRenderer;
    private MaterialPropertyBlock _blockDomeMpb;

    // Shared: running hit window
    private Coroutine _hitWindowRoutine;

    // Victim side: last swing that already damaged us, per attacker.
    private readonly Dictionary<NetworkId, int> _lastHitSequenceByAttacker = new Dictionary<NetworkId, int>();

    // ------------------------------------------------------------------ lifecycle

    private void Awake()
    {
        _capsule = GetComponent<PlayerCapsule>();
        _animationDriver = GetComponent<PlayerAnimationDriver>();
        _rb = GetComponent<Rigidbody>();
        _bodyCollider = GetComponent<Collider>();
        _networkTransform = GetComponent<NetworkTransform>();
        _attackTriggerHash = string.IsNullOrEmpty(attackTriggerName) ? 0 : Animator.StringToHash(attackTriggerName);

        if (animator == null)
        {
            if (_animationDriver != null && _animationDriver.Animator != null)
                animator = _animationDriver.Animator;
            else
                animator = GetComponentInChildren<Animator>();
        }

        EnsureBladeHitboxAlignedAndClean();
        BuildBlockShieldVfx();
    }

    private void EnsureBladeHitboxAlignedAndClean()
    {
        if (hitboxDebugVisual != null)
        {
            hitboxDebugVisual.enabled = false;
            hitboxDebugVisual.gameObject.SetActive(false);
            hitboxDebugVisual = null;
        }

        Transform swordHand = null;
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "Sword_Hand")
            {
                swordHand = t;
                break;
            }
            if (t.name == "WeaponHitboxVisual")
            {
                var r = t.GetComponent<Renderer>();
                if (r != null) r.enabled = false;
                t.gameObject.SetActive(false);
            }
        }

        if (weaponHitbox == null)
        {
            Transform existingWh = null;
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "WeaponHitbox") { existingWh = t; break; }
            }
            if (existingWh != null) weaponHitbox = existingWh.GetComponent<Collider>();
        }

        if (swordHand != null && weaponHitbox != null)
        {
            Transform whTransform = weaponHitbox.transform;
            foreach (Renderer r in whTransform.GetComponentsInChildren<Renderer>(true))
            {
                r.enabled = false;
                if (r.gameObject != whTransform.gameObject)
                    r.gameObject.SetActive(false);
            }

            if (whTransform.parent != swordHand)
            {
                whTransform.SetParent(swordHand, false);
                whTransform.localPosition = Vector3.zero;
                whTransform.localRotation = Quaternion.identity;
                whTransform.localScale = Vector3.one;
            }

            if (weaponHitbox is SphereCollider oldSphere)
            {
                Destroy(oldSphere);
                var box = whTransform.GetComponent<BoxCollider>();
                if (box == null) box = whTransform.gameObject.AddComponent<BoxCollider>();
                box.center = Vector3.zero;
                box.size = new Vector3(1.6f, 0.5f, 8.6f);
                box.isTrigger = true;
                weaponHitbox = box;
            }
            else if (weaponHitbox is BoxCollider box)
            {
                box.center = Vector3.zero;
                box.size = new Vector3(1.6f, 0.5f, 8.6f);
                box.isTrigger = true;
            }
        }

        if (weaponHitbox == null && swordHand != null)
        {
            // สร้าง BoxCollider สำหรับใบดาบอัตโนมัติบน Sword_Hand หากยังไม่ได้ผูกไว้ใน Prefab
            var hitboxObj = new GameObject("WeaponHitbox");
            hitboxObj.transform.SetParent(swordHand, false);
            hitboxObj.transform.localPosition = Vector3.zero;
            hitboxObj.transform.localRotation = Quaternion.identity;
            hitboxObj.transform.localScale = Vector3.one;
            var autoBox = hitboxObj.AddComponent<BoxCollider>();
            autoBox.center = Vector3.zero;
            autoBox.size = new Vector3(1.6f, 0.5f, 8.6f);
            autoBox.isTrigger = true;
            autoBox.enabled = false;
            weaponHitbox = autoBox;
        }

        if (weaponHitbox == null)
        {
            Debug.LogError($"[PlayerCombat] '{name}': weaponHitbox is not assigned - attacks will never hit.", this);
            return;
        }

        weaponHitbox.isTrigger = true;
        SetHitbox(false);
    }

    private void BuildBlockShieldVfx()
    {
        Transform existing = transform.Find("BlockShieldVFX");
        if (existing != null)
        {
            _blockVfxRoot = existing.gameObject;
            _blockDome = existing.Find("ShieldDome");
            _blockRing = existing.Find("ShieldRing");
            _blockVfxRoot.SetActive(false);
            return;
        }

        _blockVfxRoot = new GameObject("BlockShieldVFX");
        _blockVfxRoot.transform.SetParent(transform, false);
        _blockVfxRoot.transform.localPosition = Vector3.zero;

        // ปรับระดับความสูงของโล่พลังงาน Block VFX ให้ตรงกับตัวละครที่มีจุดหมุนอยู่ที่เท้า (GreatSwordGirl) vs กึ่งกลางตัว
        float yOffset = (_capsule != null && _capsule.CharacterVisual == transform) ? 1.0f : 0f;

        GameObject dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        dome.name = "ShieldDome";
        var domeCol = dome.GetComponent<Collider>();
        if (domeCol != null) Destroy(domeCol);
        dome.transform.SetParent(_blockVfxRoot.transform, false);
        dome.transform.localPosition = new Vector3(0f, 0.15f + yOffset, 0.25f);
        dome.transform.localScale = new Vector3(1.35f, 1.65f, 1.15f);
        // ใช้ URP Unlit Transparent ถาวรแทน Shader.Find("Sprites/Default") เพื่อป้องกันการถูก Strip ใน Standalone Build
        UrpMaterialHelper.ApplyTransparentColor(dome.GetComponent<Renderer>(), new Color(0.18f, 0.78f, 1.0f, 0.26f), disableShadows: true);
        _blockDome = dome.transform;

        GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "ShieldRing";
        var ringCol = ring.GetComponent<Collider>();
        if (ringCol != null) Destroy(ringCol);
        ring.transform.SetParent(_blockVfxRoot.transform, false);
        ring.transform.localPosition = new Vector3(0f, -0.92f + yOffset, 0f);
        ring.transform.localScale = new Vector3(1.65f, 0.02f, 1.65f);
        UrpMaterialHelper.ApplyTransparentColor(ring.GetComponent<Renderer>(), new Color(0.25f, 0.90f, 1.0f, 0.58f), disableShadows: true);
        _blockRing = ring.transform;

        _blockVfxRoot.SetActive(false);
    }

    public override void Spawned()
    {
        _isSpawned = true;
        _spawnPosition = transform.position;

        if (!All.Contains(this)) All.Add(this);
        if (IsLocallyControlled)
        {
            Local = this;
            Debug.Log($"[PlayerCombat] Local player registered ({name}, id={Object.Id}).");
            CombatHud.EnsureFor(this);
            BossHudUI.EnsureFor(this);
        }

        if (Object.HasStateAuthority)
        {
            if (NetworkLevel < 1) NetworkLevel = 1;
            NetworkHealth = MaxHealth;
            NetworkStamina = MaxStamina;
            bool startInSafeZone = SafeZone.IsPointInAnySafeZone(transform.position);
            IsInSafeZone = startInSafeZone;
            // เริ่มต้นแบบเก็บดาบไว้กลางหลังเสมอ (โหมดสำรวจ/เขตปลอดภัย) ผู้เล่นกด [1] นอกเขตปลอดภัยเพื่อชักดาบ
            IsWeaponDrawn = false;
        }

        ApplyHealthState(forceNotify: true);
        UpdateBlockVfxVisual(IsBlocking && !IsDead);
        _animationDriver?.SyncWeaponDrawnImmediate(IsWeaponDrawn && !IsInSafeZone);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (IsLocallyControlled && PlayerSaveManager.Instance != null)
            PlayerSaveManager.Instance.SaveNow("OnNetworkDespawn");

        _isSpawned = false;
        All.Remove(this);
        if (Local == this) Local = null;
        StopAllCoroutines();
        _hitWindowRoutine = null;
        _respawnRoutine = null;
        CloseHitWindow();
        UpdateBlockVfxVisual(false);
        _lastHitSequenceByAttacker.Clear();
    }

    // ------------------------------------------------------------------ EXP, Gold, Leveling, Scaled Stat Allocation & Persistence

    public int GetStatPointValue(PlayerStatType statType)
    {
        if (!_isSpawned) return 0;
        return statType switch
        {
            PlayerStatType.STR => StatPointsSTR,
            PlayerStatType.VIT => StatPointsVIT,
            PlayerStatType.END => StatPointsEND,
            PlayerStatType.AGI => StatPointsAGI,
            _ => 0
        };
    }

    public static int GetAttributeUpgradePointCost(int currentStatValue)
    {
        return currentStatValue >= 10 ? 2 : 1;
    }

    public static int GetAttributeUpgradeGoldCost(int currentStatValue)
    {
        if (currentStatValue < 5) return 0;
        if (currentStatValue < 10) return 50;
        if (currentStatValue < 15) return 150;
        return 300 + (currentStatValue - 15) * 60;
    }

    public void AddGold(int amount, string source = "Reward")
    {
        if (!_isSpawned || amount <= 0) return;
        if (Object.HasStateAuthority)
        {
            NetworkGold += amount;
            ProgressionChanged?.Invoke();
            if (IsLocallyControlled) CombatHud.NotifyGoldGained(amount, source);
            PlayerSaveManager.RequestAutoSave($"AddGold_{source}");
            Debug.Log($"[PlayerCombat] +{amount} Gold from {source} (total={NetworkGold}).");
        }
        else if (IsLocallyControlled)
        {
            RPC_AddGold(amount, source);
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_AddGold(int amount, string source)
    {
        if (!Object.HasStateAuthority || amount <= 0) return;
        AddGold(amount, source);
    }

    public bool TrySpendGold(int amount)
    {
        if (!_isSpawned || amount < 0) return false;
        if (amount == 0) return true;
        if (NetworkGold < amount) return false;

        if (Object.HasStateAuthority)
        {
            NetworkGold = Mathf.Max(0, NetworkGold - amount);
            ProgressionChanged?.Invoke();
            PlayerSaveManager.RequestAutoSave("SpendGold");
            return true;
        }
        if (IsLocallyControlled)
        {
            RPC_SpendGold(amount);
            return true;
        }
        return false;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SpendGold(int amount)
    {
        if (!Object.HasStateAuthority || amount <= 0 || NetworkGold < amount) return;
        NetworkGold = Mathf.Max(0, NetworkGold - amount);
        ProgressionChanged?.Invoke();
        PlayerSaveManager.RequestAutoSave("SpendGold");
    }

    /// <summary>Awards EXP and automatically processes Level-Ups (+3 Stat Points per level).</summary>
    public void AddExperience(int amount)
    {
        if (!_isSpawned || amount <= 0) return;
        if (Object.HasStateAuthority)
        {
            ApplyExperienceAuthority(amount);
        }
        else if (IsLocallyControlled)
        {
            RPC_AddExperience(amount);
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_AddExperience(int amount)
    {
        if (!Object.HasStateAuthority || amount <= 0) return;
        ApplyExperienceAuthority(amount);
    }

    private void ApplyExperienceAuthority(int amount)
    {
        int exp = NetworkExperience + amount;
        int lvl = Mathf.Max(1, NetworkLevel);
        int pointsGained = 0;

        while (exp >= GetExpRequiredForNextLevel(lvl))
        {
            exp -= GetExpRequiredForNextLevel(lvl);
            lvl++;
            pointsGained += StatPointsPerLevel;
        }

        bool leveledUp = lvl > NetworkLevel;
        NetworkExperience = exp;
        if (leveledUp)
        {
            NetworkLevel = lvl;
            NetworkUnspentStatPoints += pointsGained;
            NetworkHealth = MaxHealth;
            NetworkStamina = MaxStamina;
            Debug.Log($"[PlayerCombat] LEVEL UP! {name} reached Level {NetworkLevel} (+{pointsGained} Stat Points, total unspent={NetworkUnspentStatPoints}).");
        }
        ProgressionChanged?.Invoke();
        PlayerSaveManager.RequestAutoSave(leveledUp ? "LevelUp" : "AddExperience");
    }

    /// <summary>Allocates Stat Points + scaled Gold cost into STR, VIT, END, or AGI.</summary>
    public bool AllocateStatPoint(PlayerStatType statType)
    {
        if (!_isSpawned) return false;
        int curVal = GetStatPointValue(statType);
        int ptCost = GetAttributeUpgradePointCost(curVal);
        int goldCost = GetAttributeUpgradeGoldCost(curVal);
        if (NetworkUnspentStatPoints < ptCost || NetworkGold < goldCost) return false;

        if (Object.HasStateAuthority)
        {
            return ApplyAllocateStatAuthority(statType);
        }
        if (IsLocallyControlled)
        {
            RPC_AllocateStatPoint((int)statType);
            return true;
        }
        return false;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_AllocateStatPoint(int statTypeInt)
    {
        if (!Object.HasStateAuthority) return;
        ApplyAllocateStatAuthority((PlayerStatType)statTypeInt);
    }

    private bool ApplyAllocateStatAuthority(PlayerStatType statType)
    {
        int curVal = GetStatPointValue(statType);
        int ptCost = GetAttributeUpgradePointCost(curVal);
        int goldCost = GetAttributeUpgradeGoldCost(curVal);

        if (NetworkUnspentStatPoints < ptCost || NetworkGold < goldCost) return false;

        int prevMaxHp = MaxHealth;
        float prevMaxSta = MaxStamina;

        NetworkUnspentStatPoints -= ptCost;
        NetworkGold -= goldCost;

        switch (statType)
        {
            case PlayerStatType.STR: StatPointsSTR++; break;
            case PlayerStatType.VIT: StatPointsVIT++; break;
            case PlayerStatType.END: StatPointsEND++; break;
            case PlayerStatType.AGI: StatPointsAGI++; break;
            default:
                NetworkUnspentStatPoints += ptCost;
                NetworkGold += goldCost;
                return false;
        }

        int hpDelta = MaxHealth - prevMaxHp;
        if (hpDelta > 0 && !IsDead) NetworkHealth = Mathf.Min(MaxHealth, NetworkHealth + hpDelta);

        float staDelta = MaxStamina - prevMaxSta;
        if (staDelta > 0f && !IsDead) NetworkStamina = Mathf.Min(MaxStamina, NetworkStamina + staDelta);

        Debug.Log($"[PlayerCombat] Allocated +1 {statType} (cost {ptCost}pt + {goldCost}g). STR={StatPointsSTR}, VIT={StatPointsVIT}, END={StatPointsEND}, AGI={StatPointsAGI}.");
        ProgressionChanged?.Invoke();
        PlayerSaveManager.RequestAutoSave($"Allocate_{statType}");
        return true;
    }

    public void ExportSaveData(PlayerSaveData data)
    {
        if (data == null || !_isSpawned) return;
        data.level = Mathf.Max(1, NetworkLevel);
        data.experience = Mathf.Max(0, NetworkExperience);
        data.unspentStatPoints = Mathf.Max(0, NetworkUnspentStatPoints);
        data.statSTR = StatPointsSTR;
        data.statVIT = StatPointsVIT;
        data.statEND = StatPointsEND;
        data.statAGI = StatPointsAGI;
        data.gold = Mathf.Max(0, NetworkGold);
        data.currentHealth = NetworkHealth;
        data.currentStamina = NetworkStamina;
    }

    public void ApplySaveData(PlayerSaveData data)
    {
        if (data == null || !_isSpawned) return;
        if (!Object.HasStateAuthority) return;

        NetworkLevel = Mathf.Max(1, data.level);
        NetworkExperience = Mathf.Max(0, data.experience);
        NetworkUnspentStatPoints = Mathf.Max(0, data.unspentStatPoints);
        StatPointsSTR = Mathf.Max(0, data.statSTR);
        StatPointsVIT = Mathf.Max(0, data.statVIT);
        StatPointsEND = Mathf.Max(0, data.statEND);
        StatPointsAGI = Mathf.Max(0, data.statAGI);
        NetworkGold = Mathf.Max(0, data.gold);
        ProgressionChanged?.Invoke();
    }

    public void ApplySaveHealthAndStamina(int savedHp, float savedStamina)
    {
        if (!_isSpawned || !Object.HasStateAuthority) return;
        NetworkHealth = savedHp > 0 ? Mathf.Clamp(savedHp, 1, MaxHealth) : MaxHealth;
        NetworkStamina = savedStamina > 0f ? Mathf.Clamp(savedStamina, 1f, MaxStamina) : MaxStamina;
        HealthChanged?.Invoke(NetworkHealth, MaxHealth);
        StaminaChanged?.Invoke(NetworkStamina, MaxStamina);
    }

    /// <summary>Syncs total equipped gear bonuses (Sword + Shield + Armor + Pants) over Photon Fusion.</summary>
    public void ApplyEquipmentBonuses(int dmgBonus, int maxHpBonus, int maxStaBonus, int defBonus, float moveSpeedBonus, float critBonus)
    {
        if (!_isSpawned) return;
        if (Object.HasStateAuthority)
        {
            SetEquipmentBonusesAuthority(dmgBonus, maxHpBonus, maxStaBonus, defBonus, moveSpeedBonus, critBonus);
        }
        else if (IsLocallyControlled)
        {
            RPC_SyncEquipmentBonuses(dmgBonus, maxHpBonus, maxStaBonus, defBonus, moveSpeedBonus, critBonus);
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SyncEquipmentBonuses(int dmgBonus, int maxHpBonus, int maxStaBonus, int defBonus, float moveSpeedBonus, float critBonus)
    {
        if (!Object.HasStateAuthority) return;
        SetEquipmentBonusesAuthority(dmgBonus, maxHpBonus, maxStaBonus, defBonus, moveSpeedBonus, critBonus);
    }

    private void SetEquipmentBonusesAuthority(int dmgBonus, int maxHpBonus, int maxStaBonus, int defBonus, float moveSpeedBonus, float critBonus)
    {
        int prevMaxHp = MaxHealth;
        float prevMaxSta = MaxStamina;

        NetworkGearDamageBonus = dmgBonus;
        NetworkGearMaxHealthBonus = maxHpBonus;
        NetworkGearMaxStaminaBonus = maxStaBonus;
        NetworkGearDefenseBonus = defBonus;
        NetworkGearMoveSpeedBonus = moveSpeedBonus;
        NetworkGearCritBonus = critBonus;

        int hpDiff = MaxHealth - prevMaxHp;
        if (hpDiff > 0 && !IsDead)
            NetworkHealth = Mathf.Min(MaxHealth, NetworkHealth + hpDiff);
        else if (NetworkHealth > MaxHealth)
            NetworkHealth = MaxHealth;

        float staDiff = MaxStamina - prevMaxSta;
        if (staDiff > 0f && !IsDead)
            NetworkStamina = Mathf.Min(MaxStamina, NetworkStamina + staDiff);
        else if (NetworkStamina > MaxStamina)
            NetworkStamina = MaxStamina;

        ProgressionChanged?.Invoke();
    }

    private void OnProgressionNetChanged()
    {
        if (!_isSpawned) return;
        ProgressionChanged?.Invoke();
        HealthChanged?.Invoke(NetworkHealth, MaxHealth);
        StaminaChanged?.Invoke(NetworkStamina, MaxStamina);
    }

    // ------------------------------------------------------------------ 1. local input & stamina tick

    public bool IsLocallyControlled =>
        Object.HasInputAuthority || (Runner != null && Runner.GameMode == GameMode.Shared && Object.HasStateAuthority);

    public override void FixedUpdateNetwork()
    {
        if (!_isSpawned || !Object.HasStateAuthority) return;
        bool inSafe = SafeZone.IsPointInAnySafeZone(transform.position);
        IsInSafeZone = inSafe;

        // เมื่อเดินเข้าสู่เขตปลอดภัย (Comfort Zone) ให้เก็บดาบและปิดสถานะต่อสู้อัตโนมัติทันที
        if (inSafe && (IsWeaponDrawn || IsBlocking))
        {
            SetAuthorityWeaponDrawn(false);
        }

        TickStaminaRegen(Runner.DeltaTime);
    }

    private void Update()
    {
        if (_blockVfxRoot != null && _blockVfxRoot.activeSelf)
        {
            bool parryFlash = Time.time < _parryFlashUntil;
            if (_blockDome != null)
            {
                float s = parryFlash ? 1.22f : (1f + Mathf.Sin(Time.time * 6f) * 0.035f);
                _blockDome.localScale = new Vector3(1.35f * s, 1.65f * s, 1.15f * s);
                if (_blockDomeRenderer == null) _blockDomeRenderer = _blockDome.GetComponent<Renderer>();
                if (_blockDomeRenderer != null)
                {
                    _blockDomeMpb ??= new MaterialPropertyBlock();
                    Color shieldColor = parryFlash
                        ? new Color(1.0f, 0.92f, 0.35f, 0.55f)
                        : (IsInParryWindow ? new Color(0.32f, 0.98f, 1.0f, 0.34f) : new Color(0.18f, 0.72f, 1.0f, 0.24f));
                    _blockDomeMpb.SetColor("_BaseColor", shieldColor);
                    _blockDomeMpb.SetColor("_Color", shieldColor);
                    _blockDomeRenderer.SetPropertyBlock(_blockDomeMpb);
                }
            }
            if (_blockRing != null)
            {
                float r = parryFlash ? 2.25f : (1.65f + Mathf.Sin(Time.time * 4.5f) * 0.08f);
                _blockRing.localScale = new Vector3(r, 0.02f, r);
            }
        }

        if (!_isSpawned) return;

        if (Object.HasStateAuthority && IsHitWindowOpen && !IsDead)
        {
            PollActiveWeaponHits();
        }

        if (!IsLocallyControlled) return;

        if (IsDead)
        {
            _bufferedUntil = -1f;
            SetBlockingInput(false);
            return;
        }

        // ตรวจจับปุ่ม [1] สำหรับชักดาบ / เก็บดาบด้วยตนเองนอกเขตปลอดภัย
        if (PollWeaponTogglePressed())
        {
            TryToggleWeaponDraw();
        }

        if (PollDodgePressed())
        {
            TryRequestDodge();
        }

        bool inSafeNow = IsInSafeZone || SafeZone.IsPointInAnySafeZone(transform.position);

        if (Application.isFocused && Time.time >= _overrideBlockUntil)
        {
            bool blockPressedNow = PollBlockJustPressed();
            bool wantBlock = !IsDodging && !inSafeNow && IsWeaponDrawn && CurrentStamina > 0f && PollBlockHeld();
            if (blockPressedNow && !inSafeNow && !IsWeaponDrawn)
            {
                NotifyWeaponMustBeDrawn();
            }
            SetBlockingInput(wantBlock);
        }

        // รองรับทั้งโจมตีปกติ (LMB -> Light / Run / Jump / Stealth Assassination) และโจมตีหนัก (R -> Heavy / RunHeavy / JumpHeavy)
        if (PollAttackPressed())
        {
            if (inSafeNow)
            {
                NotifySafeZoneWeaponsDisabled();
            }
            else if (!IsWeaponDrawn)
            {
                NotifyWeaponMustBeDrawn();
            }
            else
            {
                _bufferedUntil = Time.time + inputBufferSeconds;
                _bufferedHeavy = false;
            }
        }
        else if (PollHeavyAttackPressed())
        {
            if (inSafeNow)
            {
                NotifySafeZoneWeaponsDisabled();
            }
            else if (!IsWeaponDrawn)
            {
                NotifyWeaponMustBeDrawn();
            }
            else
            {
                _bufferedUntil = Time.time + inputBufferSeconds;
                _bufferedHeavy = true;
            }
        }

        if (_bufferedUntil >= Time.time && Time.time >= _nextLocalAttackTime && !IsDodging)
        {
            _bufferedUntil = -1f;
            if (CanAttack)
            {
                if (_localBlockingSent) SetBlockingInput(false);
                if (_bufferedHeavy)
                    TryRequestAttackKind(ResolveContextualHeavyAttackKind());
                else
                    TryRequestAttack();
            }
        }
    }

    private void TickStaminaRegen(float dt)
    {
        if (IsDead) return;

        bool sprinting = _capsule != null && _capsule.NetworkSprinting;
        if (sprinting || IsDodging || IsHitWindowOpen) return;
        if (Time.time < _nextStaminaRegenTime) return;

        float cap = MaxStamina;
        if (NetworkStamina < cap)
        {
            NetworkStamina = Mathf.Min(cap, NetworkStamina + staminaRegenRate * dt);
            if (_sprintExhaustedCooldown && NetworkStamina >= 12f)
                _sprintExhaustedCooldown = false;
        }
    }

    public bool ConsumeSprintStamina(float dt)
    {
        if (!_isSpawned || IsDead) return false;
        if (_sprintExhaustedCooldown || NetworkStamina <= 0f)
        {
            _sprintExhaustedCooldown = true;
            return false;
        }

        if (Object.HasStateAuthority)
        {
            float cost = sprintStaminaPerSecond * dt;
            NetworkStamina = Mathf.Max(0f, NetworkStamina - cost);
            _nextStaminaRegenTime = Time.time + staminaRegenDelay;
            if (NetworkStamina <= 0.01f)
            {
                NetworkStamina = 0f;
                _sprintExhaustedCooldown = true;
                return false;
            }
        }
        return NetworkStamina > 0f;
    }

    private bool TryConsumeAuthorityStamina(float cost)
    {
        if (!Object.HasStateAuthority) return false;
        if (NetworkStamina <= 0f) return false;

        NetworkStamina = Mathf.Max(0f, NetworkStamina - cost);
        _nextStaminaRegenTime = Time.time + staminaRegenDelay;
        if (NetworkStamina <= 0.01f)
        {
            NetworkStamina = 0f;
            _sprintExhaustedCooldown = true;
        }
        return true;
    }

    public void SetStamina(float value)
    {
        if (!_isSpawned || !Object.HasStateAuthority) return;
        NetworkStamina = Mathf.Clamp(value, 0f, MaxStamina);
        if (NetworkStamina <= 0f)
        {
            _sprintExhaustedCooldown = true;
            _nextStaminaRegenTime = Time.time + staminaRegenDelay;
        }
        else if (NetworkStamina >= 12f)
        {
            _sprintExhaustedCooldown = false;
        }
    }

    public void RestoreStamina(float amount)
    {
        if (!_isSpawned || !Object.HasStateAuthority || IsDead || amount <= 0f) return;
        float before = NetworkStamina;
        NetworkStamina = Mathf.Min(MaxStamina, NetworkStamina + amount);
        _sprintExhaustedCooldown = false;
        Debug.Log($"[PlayerCombat] Restored {NetworkStamina - before:0} Stamina ({before:0} -> {NetworkStamina:0}/{MaxStamina:0}).");
    }

    public void ApplyAttackBuff(int bonusDamage, float durationSeconds)
    {
        if (bonusDamage <= 0 || durationSeconds <= 0f) return;
        _attackBuffBonus = bonusDamage;
        _attackBuffUntil = Time.time + durationSeconds;
        ProgressionChanged?.Invoke();
        Debug.Log($"[PlayerCombat] Applied +{bonusDamage} Attack Buff for {durationSeconds:0}s.");
    }

    /// <summary>
    /// ตรวจจับการกดปุ่มโจมตีปกติ (คลิกซ้าย Mouse Left Button)
    /// </summary>
    private static bool PollAttackPressed()
    {
        if (!Application.isFocused || GameplayInput.IsBlocked) return false;

        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return false;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return false;

        return true;
    }

    /// <summary>
    /// ตรวจจับการกดปุ่มโจมตีหนัก (ปุ่ม R) เพื่อสั่งท่า Heavy Slash / Run Heavy / Jump Heavy
    /// </summary>
    private static bool PollHeavyAttackPressed()
    {
        if (!Application.isFocused || GameplayInput.IsBlocked) return false;

        var kb = Keyboard.current;
        if (kb == null || !kb.rKey.wasPressedThisFrame) return false;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return false;

        return true;
    }

    // ปุ่มควบคุมมาตรฐานของระบบต่อสู้ (Block/Parry = F เท่านั้น ตัด Alt ออก, Dodge = Left Ctrl เพื่อไม่ชนกับ Spacebar กระโดด)
    public const Key BlockParryKey = Key.F;
    public const Key DodgeKey = Key.LeftCtrl;

    /// <summary>
    /// ตรวจจับการกดค้างปุ่มตั้งการ์ด/ปัดป้อง (ปุ่ม F เท่านั้น — ตัด LeftAlt / RightAlt และปุ่มเมาส์ออกทั้งหมด)
    /// </summary>
    private static bool PollBlockHeld()
    {
        if (!Application.isFocused || GameplayInput.IsBlocked || IsAnyTextFieldFocused()) return false;

        var kb = Keyboard.current;
        return kb != null && kb.fKey.isPressed;
    }

    /// <summary>
    /// ตรวจจับปุ่มกลิ้งหลบ (Left Ctrl เท่านั้น — ยกเลิก Spacebar เพราะย้าย Spacebar ไปเป็นปุ่มกระโดด Jump แล้ว)
    /// </summary>
    private static bool PollDodgePressed()
    {
        if (!Application.isFocused || GameplayInput.IsBlocked || IsAnyTextFieldFocused()) return false;

        var kb = Keyboard.current;
        return kb != null && kb.leftCtrlKey.wasPressedThisFrame;
    }

    /// <summary>
    /// ตรวจจับปุ่มเลข [1] สำหรับสลับชักดาบ / เก็บดาบ (บล็อกอัตโนมัติเมื่อเปิดหน้าต่าง UI หรือกำลังพิมพ์ข้อความ)
    /// </summary>
    private static bool PollWeaponTogglePressed()
    {
        if (!Application.isFocused || GameplayInput.IsBlocked || IsAnyTextFieldFocused()) return false;
        if (InventoryUI.Instance != null && InventoryUI.Instance.IsOpen) return false;
        if (MapManager.Instance != null && MapManager.Instance.IsWorldMapOpen) return false;

        var kb = Keyboard.current;
        return kb != null && (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame);
    }

    /// <summary>
    /// ตรวจจับจังหวะกดปุ่มบล็อก [F] ครั้งแรกในเฟรมนี้ (ตัด Alt ออกทั้งหมด) เพื่อแจ้งเตือนให้ชักดาบหากยังเก็บดาบอยู่
    /// </summary>
    private static bool PollBlockJustPressed()
    {
        if (!Application.isFocused || GameplayInput.IsBlocked || IsAnyTextFieldFocused()) return false;
        var kb = Keyboard.current;
        return kb != null && kb.fKey.wasPressedThisFrame;
    }

    /// <summary>
    /// ตรวจสอบว่าผู้เล่นกำลังพิมพ์ในช่อง InputField หรือไม่ เพื่อไม่ให้ปุ่มลัดทำงานชนกับการพิมพ์
    /// </summary>
    private static bool IsAnyTextFieldFocused()
    {
        if (EventSystem.current == null) return false;
        var selected = EventSystem.current.currentSelectedGameObject;
        if (selected == null) return false;
        return selected.GetComponent<TMP_InputField>() != null || selected.GetComponent<UnityEngine.UI.InputField>() != null;
    }

    /// <summary>
    /// แสดงข้อความแจ้งเตือนแบบหน่วงเวลาให้กด [1] ชักดาบก่อนโจมตีหรือตั้งการ์ด
    /// </summary>
    public void NotifyWeaponMustBeDrawn()
    {
        if (Time.unscaledTime - _lastSheathedPromptTime < 1.4f) return;
        _lastSheathedPromptTime = Time.unscaledTime;
        PlayerStatusHud.Instance?.ShowToast(DrawWeaponPromptMessage, new Color(1f, 0.84f, 0.25f), 2.2f);
        Debug.Log($"[PlayerCombat] Combat action blocked while sheathed: {DrawWeaponPromptMessage}");
    }

    private void NotifySafeZoneWeaponsDisabled()
    {
        if (Time.unscaledTime - _lastSafeZonePromptTime < 1.4f) return;
        _lastSafeZonePromptTime = Time.unscaledTime;
        PlayerStatusHud.Instance?.ShowToast("Comfort Zone: Weapons Disabled", new Color(0.32f, 0.96f, 0.72f), 2.0f);
    }

    /// <summary>
    /// สลับสถานะชักดาบ / เก็บดาบด้วยตนเอง (เรียกจากปุ่ม [1] หรือสคริปต์ทดสอบ)
    /// </summary>
    public bool TryToggleWeaponDraw()
    {
        return TrySetWeaponDrawn(!IsWeaponDrawn);
    }

    /// <summary>
    /// สั่งเปลี่ยนสถานะชักดาบ (true) หรือเก็บดาบ (false) พร้อมซิงก์ผ่าน Photon Fusion ไปยังทุกเครื่อง
    /// </summary>
    public bool TrySetWeaponDrawn(bool drawn)
    {
        if (!_isSpawned || !IsLocallyControlled || IsDead) return false;
        if (Time.time < _nextWeaponToggleTime) return false;

        bool inSafe = IsInSafeZone || SafeZone.IsPointInAnySafeZone(transform.position);
        if (drawn && inSafe)
        {
            NotifySafeZoneWeaponsDisabled();
            return false;
        }

        if (IsWeaponDrawn == drawn) return false;

        _nextWeaponToggleTime = Time.time + 0.32f;
        _bufferedUntil = -1f;
        if (!drawn && _localBlockingSent) SetBlockingInput(false);

        if (Object.HasStateAuthority)
            SetAuthorityWeaponDrawn(drawn);
        else
            RPC_SetWeaponDrawn(drawn);

        return true;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SetWeaponDrawn(NetworkBool drawn)
    {
        if (!Object.HasStateAuthority) return;
        SetAuthorityWeaponDrawn(drawn);
    }

    /// <summary>
    /// อัปเดตสถานะการชักดาบฝั่ง State Authority (บังคับเก็บดาบเสมอหากอยู่ในเขตปลอดภัยหรือตาย)
    /// </summary>
    private void SetAuthorityWeaponDrawn(bool wantDrawn)
    {
        if (!Object.HasStateAuthority) return;

        bool inSafe = IsInSafeZone || SafeZone.IsPointInAnySafeZone(transform.position);
        bool effectiveDrawn = !IsDead && !inSafe && wantDrawn;

        if (!effectiveDrawn)
        {
            if (IsBlocking) SetAuthorityBlockingState(false);
            if (IsHitWindowOpen) CloseHitWindow();
        }

        if (IsWeaponDrawn == effectiveDrawn) return;

        IsWeaponDrawn = effectiveDrawn;
        Debug.Log($"[PlayerCombat] Weapon stance changed -> {(effectiveDrawn ? "DRAWN (Combat Ready)" : "SHEATHED (Exploration Mode)")}.");
    }

    /// <summary>
    /// Callback บนทุกเครื่องเมื่อค่า Networked IsWeaponDrawn เปลี่ยนแปลง (อัปเดตแอนิเมชันและแจ้ง Event ให้ทีม UI/Audio)
    /// </summary>
    private void OnWeaponDrawnChanged()
    {
        if (!_isSpawned) return;
        bool drawn = IsWeaponDrawn && !IsInSafeZone && !IsDead;
        _animationDriver?.NotifyWeaponDrawnChanged(drawn);
        WeaponDrawnChanged?.Invoke(drawn);
    }

    /// <summary>
    /// สั่งโจมตีปกติแบบฉลาดตามบริบท (Stealth Assassination / Jump Attack / Run Attack / Light Combo)
    /// </summary>
    public bool TryRequestAttack()
    {
        return TryRequestAttackKind(ResolveContextualLightAttackKind());
    }

    /// <summary>
    /// คำนวณระยะเวลา Cooldown และช่วงเปิด Hitbox ของแต่ละรูปแบบท่าโจมตีให้สอดคล้องกับความเร็วอนิเมชันจริง
    /// </summary>
    public void GetAttackTiming(PlayerAttackKind kind, int comboStep, out float cooldown, out float hitDelay, out float hitDuration)
    {
        float baseCd = Mathf.Max(0.15f, attackCooldown);
        float baseDelay = Mathf.Max(0.02f, hitWindowDelay);
        float baseDur = Mathf.Max(0.08f, hitWindowDuration);

        switch (kind)
        {
            case PlayerAttackKind.HeavyCombo:
                cooldown = baseCd * 1.28f;
                hitDelay = Mathf.Max(0.14f, baseDelay * 1.9f);
                hitDuration = baseDur * 1.15f;
                break;

            case PlayerAttackKind.RunAttack:
                cooldown = baseCd * 0.95f;
                hitDelay = baseDelay;
                hitDuration = baseDur;
                break;

            case PlayerAttackKind.RunHeavyAttack:
                cooldown = baseCd * 1.22f;
                hitDelay = Mathf.Max(0.13f, baseDelay * 1.7f);
                hitDuration = baseDur * 1.15f;
                break;

            case PlayerAttackKind.JumpAttack:
                cooldown = baseCd * 0.92f;
                hitDelay = Mathf.Max(0.05f, baseDelay * 0.8f);
                hitDuration = baseDur;
                break;

            case PlayerAttackKind.JumpHeavyAttack:
            case PlayerAttackKind.RunJumpHeavyAttack:
                cooldown = baseCd * 1.35f;
                hitDelay = Mathf.Max(0.16f, baseDelay * 2.1f);
                hitDuration = baseDur * 1.25f;
                break;

            case PlayerAttackKind.StealthAssassination:
                cooldown = baseCd * 1.35f;
                hitDelay = baseDelay * 1.2f;
                hitDuration = baseDur * 1.2f;
                break;

            case PlayerAttackKind.SpellCast:
                cooldown = baseCd * 1.05f;
                hitDelay = Mathf.Max(0.14f, baseDelay * 1.8f);
                hitDuration = baseDur * 0.75f;
                break;

            default:
                // LightCombo: จังหวะที่ 1-2 ออกไว, จังหวะปิดคอมโบที่ 3 หนักแน่นขึ้นเล็กน้อย
                if (comboStep >= 3)
                {
                    cooldown = baseCd * 1.05f;
                    hitDelay = baseDelay * 1.15f;
                    hitDuration = baseDur;
                }
                else
                {
                    cooldown = baseCd * 0.85f;
                    hitDelay = baseDelay;
                    hitDuration = baseDur * 0.85f;
                }
                break;
        }
    }

    /// <summary>
    /// สั่งโจมตีตามรูปแบบท่าที่ระบุ (ส่งผ่าน Photon Fusion RPC เพื่อซิงก์ทุกเครื่อง)
    /// </summary>
    public bool TryRequestAttackKind(PlayerAttackKind kind)
    {
        if (!_isSpawned || !IsLocallyControlled || IsDead) return false;
        if (IsInSafeZone || SafeZone.IsPointInAnySafeZone(transform.position))
        {
            Debug.Log("[PlayerCombat] Attack prevented: Inside Comfort Zone (Safe Haven).");
            return false;
        }
        if (!IsWeaponDrawn)
        {
            NotifyWeaponMustBeDrawn();
            return false;
        }
        if (CurrentStamina <= 0f)
        {
            Debug.Log("[PlayerCombat] Attack prevented: Stamina depleted.");
            return false;
        }
        if (Time.time < _nextLocalAttackTime) return false;

        int predictedStep = (Time.time - _lastLocalAttackTime <= comboResetSeconds)
            ? (_localPredictedComboStep % 3) + 1
            : 1;
        _localPredictedComboStep = predictedStep;
        _lastLocalAttackTime = Time.time;

        GetAttackTiming(kind, predictedStep, out float attackCd, out _, out _);
        _nextLocalAttackTime = Time.time + attackCd;
        _lastCombatActivityTime = Time.time;

        if (kind == PlayerAttackKind.LightCombo)
            RPC_RequestAttack();
        else
            RPC_RequestAttackKind((byte)kind);
        return true;
    }

    /// <summary>
    /// วิเคราะห์บริบทการโจมตีเบา (LMB): ลอบสังหารด้านหลัง -> กลางอากาศ -> วิ่งฟัน -> คอมโบปกติ
    /// </summary>
    public PlayerAttackKind ResolveContextualLightAttackKind()
    {
        if (ActiveWeaponArchetype != WeaponArchetype.GreatSword)
            return PlayerAttackKind.SpellCast;

        for (int i = 0; i < EnemyNPC.All.Count; i++)
        {
            var npc = EnemyNPC.All[i];
            if (npc != null && npc.IsSpawned && npc.IsAlive && npc.CanBeAssassinatedBy(transform))
                return PlayerAttackKind.StealthAssassination;
        }

        if (_capsule != null && !_capsule.IsGrounded)
            return PlayerAttackKind.JumpAttack;

        if (_capsule != null && _capsule.NetworkSprinting)
            return PlayerAttackKind.RunAttack;

        return PlayerAttackKind.LightCombo;
    }

    /// <summary>
    /// วิเคราะห์บริบทการโจมตีหนัก (ปุ่ม R): วิ่งกระโดดฟัน -> กระโดดฟันหนัก -> วิ่งฟันหนัก -> คอมโบฟันหนัก
    /// </summary>
    public PlayerAttackKind ResolveContextualHeavyAttackKind()
    {
        if (ActiveWeaponArchetype != WeaponArchetype.GreatSword)
            return PlayerAttackKind.SpellCast;

        bool sprinting = _capsule != null && _capsule.NetworkSprinting;
        bool airborne = _capsule != null && !_capsule.IsGrounded;

        if (airborne && sprinting) return PlayerAttackKind.RunJumpHeavyAttack;
        if (airborne) return PlayerAttackKind.JumpHeavyAttack;
        if (sprinting) return PlayerAttackKind.RunHeavyAttack;
        return PlayerAttackKind.HeavyCombo;
    }

    /// <summary>
    /// สั่งเล่นอนิเมชัน Buff เมื่อผู้เล่นใช้ไอเทมยาฟื้นฟูหรือยาเพิ่มพลังโจมตี
    /// </summary>
    public void PlayConsumableBuffEffect()
    {
        if (!_isSpawned || IsDead) return;
        if (Object.HasStateAuthority)
            RPC_BroadcastConsumableBuff();
        else if (IsLocallyControlled)
            RPC_RequestConsumableBuff();
    }

    public bool TryRequestDodge()
    {
        if (!_isSpawned || !IsLocallyControlled || IsDead) return false;
        if (CurrentStamina <= 0f)
        {
            Debug.Log("[PlayerCombat] Dodge prevented: Stamina depleted.");
            return false;
        }
        if (Time.time < _nextLocalDodgeTime) return false;

        _nextLocalDodgeTime = Time.time + dodgeCooldown;
        SetBlockingInput(false);

        Vector3 worldDir = _capsule != null ? _capsule.DesiredMoveWorld() : Vector3.zero;
        if (worldDir.sqrMagnitude < 0.001f)
            worldDir = transform.forward;
        worldDir.Normalize();

        if (_capsule != null)
            _capsule.StartDash(worldDir, dodgeDistance, dodgeDuration);

        Vector3 localDir = Quaternion.Inverse(transform.rotation) * worldDir;
        Vector2 roll2D = new Vector2(localDir.x, localDir.z);
        if (roll2D.sqrMagnitude < 0.001f) roll2D = Vector2.up;
        else roll2D.Normalize();

        RPC_RequestDodge(roll2D.x, roll2D.y);
        return true;
    }

    public void SetBlockingInput(bool blocking, float holdSeconds = 0f)
    {
        if (!_isSpawned || !IsLocallyControlled) return;
        bool inSafe = IsInSafeZone || SafeZone.IsPointInAnySafeZone(transform.position);
        if (IsDead || inSafe || !IsWeaponDrawn || CurrentStamina <= 0f) blocking = false;
        if (holdSeconds > 0f) _overrideBlockUntil = Time.time + holdSeconds;

        if (_capsule != null)
            _capsule.SpeedMultiplier = blocking ? blockMoveSpeedMultiplier : 1f;

        if (_localBlockingSent == blocking) return;
        _localBlockingSent = blocking;
        if (Object.HasStateAuthority)
        {
            SetAuthorityBlockingState(!IsDead && blocking);
        }
        else
        {
            RPC_SetBlocking(blocking);
        }
    }

    /// <summary>
    /// อัปเดตสถานะการตั้งการ์ดฝั่ง State Authority พร้อมบันทึกเวลาเริ่มตั้งการ์ดสำหรับหน้าต่าง Perfect Parry
    /// </summary>
    private void SetAuthorityBlockingState(bool wantBlock)
    {
        if (!Object.HasStateAuthority) return;

        bool inSafe = IsInSafeZone || SafeZone.IsPointInAnySafeZone(transform.position);
        bool effectiveBlock = !IsDead && !inSafe && IsWeaponDrawn && NetworkStamina > 0f && wantBlock;
        bool wasBlocking = IsBlocking;
        IsBlocking = effectiveBlock;

        if (effectiveBlock && !wasBlocking)
        {
            // เปิดหน้าต่าง Perfect Parry เฉพาะเมื่อพ้นคูลดาวน์ Parry แล้ว (ป้องกันการกดบล็อกรัวๆ เพื่อโกง Parry)
            if (Time.time >= _nextParryReadyTime)
            {
                _blockStartedTime = Time.time;
                _nextParryReadyTime = Time.time + parryCooldownSeconds;
            }
            else
            {
                _blockStartedTime = -100f;
            }
        }
        else if (!effectiveBlock)
        {
            _blockStartedTime = -100f;
        }

        UpdateBlockVfxVisual(IsBlocking);
        RPC_BroadcastBlocking(IsBlocking);
    }

    // ------------------------------------------------------------------ 2. RPC pipeline

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RequestAttack()
    {
        ExecuteAuthorityAttack(PlayerAttackKind.LightCombo);
    }

    /// <summary>
    /// รับคำขอโจมตีระบุประเภทท่าทางจาก Input Authority ไปยัง State Authority เพื่อซิงก์ทุกเครื่อง
    /// </summary>
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RequestAttackKind(byte attackKindByte)
    {
        ExecuteAuthorityAttack((PlayerAttackKind)attackKindByte);
    }

    /// <summary>
    /// ประมวลผลการโจมตีฝั่ง State Authority: หัก Stamina, คำนวณ Combo และกระจาย RPC ไปยังผู้เล่นทุกคน
    /// </summary>
    private void ExecuteAuthorityAttack(PlayerAttackKind kind)
    {
        if (!Object.HasStateAuthority) return;
        if (IsDead) return;
        if (IsInSafeZone || SafeZone.IsPointInAnySafeZone(transform.position)) return;
        if (!IsWeaponDrawn) return;
        if (NetworkStamina <= 0f) return;

        float now = Time.time;
        if (now < _nextAuthorityAttackTime - cooldownTolerance) return;

        float staminaCost = (kind == PlayerAttackKind.HeavyCombo || kind == PlayerAttackKind.RunHeavyAttack ||
                             kind == PlayerAttackKind.JumpHeavyAttack || kind == PlayerAttackKind.RunJumpHeavyAttack)
            ? attackStaminaCost * 1.25f
            : attackStaminaCost;
        if (!TryConsumeAuthorityStamina(staminaCost)) return;

        if (now - _lastAuthorityAttackTime <= comboResetSeconds)
            _authorityComboStep = (_authorityComboStep % 3) + 1;
        else
            _authorityComboStep = 1;

        GetAttackTiming(kind, _authorityComboStep, out float attackCd, out _, out _);
        _nextAuthorityAttackTime = now + attackCd;
        _lastCombatActivityTime = now;

        if (IsBlocking)
        {
            IsBlocking = false;
            _blockStartedTime = -100f;
            UpdateBlockVfxVisual(false);
        }

        _lastAuthorityAttackTime = now;
        ComboStep = _authorityComboStep;

        _sequenceCounter++;
        if (kind == PlayerAttackKind.LightCombo)
            RPC_BroadcastAttack(_sequenceCounter, _authorityComboStep);
        else
            RPC_BroadcastAttackKind(_sequenceCounter, _authorityComboStep, (byte)kind);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_BroadcastAttack(int sequence, int comboStep = 1)
    {
        ApplyBroadcastAttack(sequence, comboStep, PlayerAttackKind.LightCombo);
    }

    /// <summary>
    /// กระจายคำสั่งเล่นท่าโจมตีพิเศษ (Heavy, Run, Jump, Stealth Assassination, SpellCast) ไปยังทุกเครื่อง
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_BroadcastAttackKind(int sequence, int comboStep, byte attackKindByte)
    {
        ApplyBroadcastAttack(sequence, comboStep, (PlayerAttackKind)attackKindByte);
    }

    private void ApplyBroadcastAttack(int sequence, int comboStep, PlayerAttackKind kind)
    {
        CurrentSequence = sequence;
        ComboStep = comboStep;
        CurrentAttackKind = kind;
        _lastCombatActivityTime = Time.time;

        if (_animationDriver != null)
            _animationDriver.PlayAttackKind(kind, comboStep);
        else if (animator != null && _attackTriggerHash != 0)
            animator.SetTrigger(_attackTriggerHash);

        if (attackVfx != null) attackVfx.Play();
        onAttackStarted?.Invoke();
        AttackPerformed?.Invoke(kind, comboStep);

        if (_hitWindowRoutine != null) StopCoroutine(_hitWindowRoutine);
        _hitWindowRoutine = StartCoroutine(HitWindowRoutine(kind, comboStep));
    }

    /// <summary>
    /// ซิงก์อนิเมชันรับแรงกระแทกขณะตั้งการ์ดปกติ (Block Hit) ข้ามเครือข่ายพร้อมแจ้ง Event
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_BroadcastBlockHit(int reducedDamage = 0)
    {
        if (!IsDead) _animationDriver?.PlayBlockImpact();
        if (enableCameraShake && IsLocallyControlled)
            PlayerFollowCamera.ShakeActive(0.10f, 0.10f);
        BlockImpacted?.Invoke(reducedDamage);
    }

    /// <summary>
    /// ซิงก์เอฟเฟกต์ปัดป้องสมบูรณ์ (Perfect Parry) ไปยังทุกเครื่อง พร้อม Hit-Stop, Camera Shake และ Event
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_BroadcastParrySuccess(NetworkId attackerNpcId)
    {
        if (IsDead) return;
        _parryFlashUntil = Time.time + 0.25f;
        _animationDriver?.PlayParryImpact();

        EnemyNPC attackerNpc = null;
        if (Runner != null && attackerNpcId.IsValid && Runner.TryFindObject(attackerNpcId, out NetworkObject npcObj) && npcObj != null)
            attackerNpc = npcObj.GetComponent<EnemyNPC>();

        FloatingDamagePopup.SpawnWarning(transform.position + Vector3.up * 2.25f, "PERFECT PARRY!", new Color(0.28f, 0.96f, 1.0f, 1f));

        if (IsLocallyControlled)
        {
            if (enableCombatHitStop) _animationDriver?.TriggerHitStop(0.065f, 0.05f);
            if (enableCameraShake) PlayerFollowCamera.ShakeActive(0.22f, 0.14f);
        }

        ParrySucceeded?.Invoke(attackerNpc);
    }

    /// <summary>
    /// ซิงก์อนิเมชันการ์ดแตก (Guard Break / Stunned) เมื่อ Stamina หมดขณะตั้งการ์ด
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_BroadcastGuardBreak()
    {
        if (!IsDead) _animationDriver?.PlayGuardBreak();
        if (enableCameraShake && IsLocallyControlled)
            PlayerFollowCamera.ShakeActive(0.22f, 0.18f);
        GuardBroken?.Invoke();
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RequestConsumableBuff()
    {
        if (!Object.HasStateAuthority || IsDead) return;
        RPC_BroadcastConsumableBuff();
    }

    /// <summary>
    /// ซิงก์อนิเมชันใช้ยา/บัฟ (Buff) ข้ามเครือข่ายไปยังผู้เล่นทุกคน
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_BroadcastConsumableBuff()
    {
        if (!IsDead) _animationDriver?.PlayBuff();
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RequestDodge(float rollX, float rollY)
    {
        if (!Object.HasStateAuthority || IsDead) return;
        if (!TryConsumeAuthorityStamina(dodgeStaminaCost)) return;

        if (IsBlocking)
        {
            IsBlocking = false;
            UpdateBlockVfxVisual(false);
        }
        _dodgeInvulnerableUntil = Time.time + dodgeDuration;
        RPC_BroadcastDodge(rollX, rollY);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_BroadcastDodge(float rollX, float rollY)
    {
        _dodgeInvulnerableUntil = Time.time + dodgeDuration;
        if (_hitWindowRoutine != null) { StopCoroutine(_hitWindowRoutine); _hitWindowRoutine = null; }
        CloseHitWindow();
        UpdateBlockVfxVisual(false);

        if (_animationDriver != null)
            _animationDriver.PlayDodge(rollX, rollY);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SetBlocking(NetworkBool blocking)
    {
        SetAuthorityBlockingState(blocking);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_BroadcastBlocking(NetworkBool blocking)
    {
        UpdateBlockVfxVisual(blocking && !IsDead);
        if (_animationDriver != null)
            _animationDriver.SetBlocking(blocking);
    }

    private void OnBlockingChanged()
    {
        UpdateBlockVfxVisual(IsBlocking && !IsDead);
        if (_animationDriver != null)
            _animationDriver.SetBlocking(IsBlocking);
    }

    private void UpdateBlockVfxVisual(bool active)
    {
        if (_blockVfxRoot == null) BuildBlockShieldVfx();
        if (_blockVfxRoot != null && _blockVfxRoot.activeSelf != active)
            _blockVfxRoot.SetActive(active);
    }

    private void OnStaminaChanged()
    {
        if (!_isSpawned) return;
        StaminaChanged?.Invoke(NetworkStamina, MaxStamina);
    }

    // ------------------------------------------------------------------ 3. hit window

    private IEnumerator HitWindowRoutine(PlayerAttackKind kind, int comboStep)
    {
        CloseHitWindow();

        if (useAnimationEvents)
        {
            yield return new WaitForSeconds(hitWindowSafetyTimeout);
        }
        else
        {
            GetAttackTiming(kind, comboStep, out _, out float delay, out float duration);
            if (delay > 0f) yield return new WaitForSeconds(delay);
            OpenHitWindow();
            yield return new WaitForSeconds(Mathf.Min(duration, hitWindowSafetyTimeout));
        }

        CloseHitWindow();
        _hitWindowRoutine = null;
    }

    public void AnimEvent_OpenHitWindow() { if (useAnimationEvents) OpenHitWindow(); }
    public void AnimEvent_CloseHitWindow() { if (useAnimationEvents) CloseHitWindow(); }

    private void OpenHitWindow()
    {
        if (!_isSpawned || IsDead) return;
        IsHitWindowOpen = true;
        SetHitbox(true);
        if (Object.HasStateAuthority) PollActiveWeaponHits();
    }

    private void CloseHitWindow()
    {
        IsHitWindowOpen = false;
        SetHitbox(false);
    }

    private void SetHitbox(bool active)
    {
        if (weaponHitbox != null) weaponHitbox.enabled = active;
        if (hitboxDebugVisual != null) hitboxDebugVisual.enabled = false;
    }

    // ------------------------------------------------------------------ 4. hit resolution

    private void OnTriggerEnter(Collider other)
    {
        HandleTriggerOverlap(other);
    }

    private void OnTriggerStay(Collider other)
    {
        HandleTriggerOverlap(other);
    }

    private void HandleTriggerOverlap(Collider other)
    {
        if (!_isSpawned || !Object.HasStateAuthority) return;
        if (IsDead || other == null) return;

        var npc = other.GetComponentInParent<EnemyNPC>();
        if (npc != null)
        {
            TryHitNpc(npc, other);
            return;
        }

        var attacker = other.GetComponentInParent<PlayerCombat>();
        if (attacker == null || attacker == this || !attacker._isSpawned) return;

        if (other != attacker.weaponHitbox || !attacker.IsHitWindowOpen || attacker.IsDead) return;
        if (!WeaponOverlapsBody(attacker)) return;
        if ((attacker.transform.position - transform.position).sqrMagnitude > maxHitDistance * maxHitDistance) return;

        NetworkId attackerId = attacker.Object.Id;
        if (_lastHitSequenceByAttacker.TryGetValue(attackerId, out int lastSeq) && lastSeq == attacker.CurrentSequence) return;
        _lastHitSequenceByAttacker[attackerId] = attacker.CurrentSequence;

        RPC_TakeDamage(attacker.TotalAttackDamage);
    }

    private void PollActiveWeaponHits()
    {
        if (!IsHitWindowOpen || weaponHitbox == null) return;
        Physics.SyncTransforms();

        for (int i = 0; i < EnemyNPC.All.Count; i++)
        {
            EnemyNPC npc = EnemyNPC.All[i];
            if (npc == null || !npc.IsSpawned || !npc.IsAlive || npc.BodyCollider == null) continue;
            TryHitNpc(npc, npc.BodyCollider);
        }
    }

    private bool WeaponOverlapsBody(PlayerCombat attacker)
    {
        if (_bodyCollider == null || attacker.weaponHitbox == null) return false;

        Transform w = attacker.weaponHitbox.transform;
        Transform b = _bodyCollider.transform;
        return Physics.ComputePenetration(
            attacker.weaponHitbox, w.position, w.rotation,
            _bodyCollider, b.position, b.rotation,
            out _, out _);
    }

    // ------------------------------------------------------------------ NPC interaction

    private readonly Dictionary<NetworkId, int> _lastNpcHitSequence = new Dictionary<NetworkId, int>();
    private readonly Dictionary<NetworkId, float> _lastNpcAttackTime = new Dictionary<NetworkId, float>();
    private const float NpcDamageRangeSlack = 3.5f;

    private void TryHitNpc(EnemyNPC npc, Collider other)
    {
        if (!IsHitWindowOpen || weaponHitbox == null) return;
        if (IsInSafeZone || SafeZone.IsPointInAnySafeZone(transform.position)) return;
        if (!npc.IsSpawned || !npc.IsAlive || npc.BodyCollider == null || other != npc.BodyCollider) return;

        Transform w = weaponHitbox.transform;
        Transform b = npc.BodyCollider.transform;
        bool bladeOverlap = Physics.ComputePenetration(weaponHitbox, w.position, w.rotation, npc.BodyCollider, b.position, b.rotation, out _, out _);

        if (!bladeOverlap)
        {
            Vector3 toNpc = npc.transform.position - transform.position;
            toNpc.y = 0f;
            float d = toNpc.magnitude;
            float reach = npc.IsBoss ? 3.25f : 2.65f;
            if (d > reach) return;
            if (d > 0.65f && Vector3.Dot(transform.forward, toNpc / d) < 0.15f) return;
        }

        NetworkId id = npc.Object.Id;
        if (_lastNpcHitSequence.TryGetValue(id, out int seq) && seq == CurrentSequence) return;
        _lastNpcHitSequence[id] = CurrentSequence;

        int totalAtk = TotalAttackDamage;
        bool isStealthStrike = CurrentAttackKind == PlayerAttackKind.StealthAssassination && !npc.IsBoss;
        bool isHeavyStrike = CurrentAttackKind == PlayerAttackKind.HeavyCombo ||
                             CurrentAttackKind == PlayerAttackKind.RunHeavyAttack ||
                             CurrentAttackKind == PlayerAttackKind.JumpHeavyAttack ||
                             CurrentAttackKind == PlayerAttackKind.RunJumpHeavyAttack;

        bool isComboCrit = ComboStep >= 2;
        bool isStatCrit = !isComboCrit && UnityEngine.Random.value < TotalCritChance;
        bool isCrit = isStealthStrike || isHeavyStrike || isComboCrit || isStatCrit;

        float mult;
        if (isStealthStrike)
        {
            // ท่าลอบสังหารจากด้านหลังทำความเสียหายวิกฤต 3.0 เท่า
            mult = 3.0f;
        }
        else if (isHeavyStrike)
        {
            mult = ComboStep switch
            {
                2 => comboStep2CritMultiplier * 1.25f,
                3 => comboStep3CritMultiplier * 1.25f,
                _ => 1.4f
            };
        }
        else
        {
            mult = ComboStep switch
            {
                2 => comboStep2CritMultiplier,
                3 => comboStep3CritMultiplier,
                _ => isStatCrit ? 1.5f : 1f
            };
        }

        bool isStaggerCounter = npc.IsStaggered;
        if (isStaggerCounter)
        {
            // โจมตีสวนกลับใส่ศัตรูที่ติดสถานะ Stagger จาก Perfect Parry -> โบนัสความเสียหาย 1.25x และติดคริติคอลเสมอ
            mult *= 1.25f;
            isCrit = true;
        }

        int finalDamage = Mathf.Max(1, Mathf.RoundToInt(totalAtk * mult));
        _lastCombatActivityTime = Time.time;

        Debug.Log($"[PlayerCombat] Swing #{CurrentSequence} ({CurrentAttackKind}, Combo {ComboStep}{(isCrit ? " CRIT!" : "")}{(isStaggerCounter ? " COUNTER!" : "")}) hit NPC {npc.name} (id={id}) for {finalDamage} (TotalATK={totalAtk}).");
        npc.RPC_TakeDamageWithCrit(finalDamage, isCrit, ComboStep);
        HitLanded?.Invoke(npc, finalDamage, isCrit);

        // ตอบสนองความหนักแน่นของการฟันฝั่งเครื่องผู้เล่น (Network-Safe Hit-Stop & Camera Shake)
        if (IsLocallyControlled)
        {
            if (enableCombatHitStop)
            {
                float hitStopDur = isStealthStrike ? 0.08f
                    : (isHeavyStrike || isStaggerCounter || ComboStep >= 3 ? 0.055f : 0.03f);
                _animationDriver?.TriggerHitStop(hitStopDur, 0.06f);
            }
            if (enableCameraShake)
            {
                float shakeAmp = isStealthStrike ? 0.28f
                    : (isHeavyStrike || isStaggerCounter || ComboStep >= 3 ? 0.17f : (isCrit ? 0.11f : 0.065f));
                PlayerFollowCamera.ShakeActive(shakeAmp, isStealthStrike ? 0.18f : 0.12f);
            }
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_ReceiveNpcDamage(NetworkId npcId, int patternDamageOverride = 0)
    {
        if (!Object.HasStateAuthority || IsDead) return;
        if (IsInSafeZone || SafeZone.IsPointInAnySafeZone(transform.position))
        {
            Debug.Log("[PlayerCombat] NPC damage blocked: player is inside Comfort Zone.");
            return;
        }

        if (Runner == null || !Runner.TryFindObject(npcId, out NetworkObject npcObject) || npcObject == null)
        {
            Debug.LogWarning($"[PlayerCombat] NPC damage rejected: object {npcId} not found.");
            return;
        }

        var npc = npcObject.GetComponent<EnemyNPC>();
        if (npc == null || !npc.IsAlive)
        {
            Debug.LogWarning($"[PlayerCombat] NPC damage rejected: {npcId} is not a living EnemyNPC.");
            return;
        }

        float limit = npc.IsBoss ? 22f : npc.AttackRange + NpcDamageRangeSlack;
        if ((npc.transform.position - transform.position).sqrMagnitude > limit * limit)
        {
            Debug.LogWarning($"[PlayerCombat] NPC damage rejected: {npc.name} out of range.");
            return;
        }

        float now = Time.time;
        if (_lastNpcAttackTime.TryGetValue(npcId, out float last) && now - last < npc.AttackCooldown * 0.4f)
        {
            return;
        }
        _lastNpcAttackTime[npcId] = now;
        _lastCombatActivityTime = now;

        int maxAllowed = npc.IsBoss ? npc.ScaledAttackDamage * 2 : npc.ScaledAttackDamage + 4;
        int incoming = patternDamageOverride > 0
            ? Mathf.Clamp(patternDamageOverride, 1, maxAllowed)
            : npc.CurrentAttackDamage;
        Debug.Log($"[PlayerCombat] {npc.name} hits {name} for {incoming}.");
        ApplyIncomingDamageAuthority(incoming, npc);
    }

    public void HealSelf(int amount)
    {
        if (!_isSpawned || !Object.HasStateAuthority || IsDead || amount <= 0) return;
        int before = NetworkHealth;
        NetworkHealth = Mathf.Min(MaxHealth, NetworkHealth + amount);
        Debug.Log($"[PlayerCombat] Healed {NetworkHealth - before} ({before} -> {NetworkHealth}/{MaxHealth}).");
    }

    // ------------------------------------------------------------------ 5. health, defense, timed parry & block stamina drain

    [Rpc(RpcSources.StateAuthority, RpcTargets.StateAuthority)]
    public void RPC_TakeDamage(int damage)
    {
        ApplyIncomingDamageAuthority(damage, attackerNpc: null);
    }

    /// <summary>
    /// ประมวลผลดาเมจขาเข้าฝั่ง State Authority: ตรวจสอบ i-frame, Perfect Parry, Sustained Block, Defense และสถานะตาย
    /// </summary>
    private void ApplyIncomingDamageAuthority(int damage, EnemyNPC attackerNpc)
    {
        if (!Object.HasStateAuthority) return;
        if (IsDead || IsDodging) return;
        if (IsInSafeZone || SafeZone.IsPointInAnySafeZone(transform.position)) return;

        _lastCombatActivityTime = Time.time;
        damage = Mathf.Clamp(damage, 0, maxDamagePerHit);

        // 1. ตรวจสอบ Perfect Parry ก่อน (กดตั้งการ์ดทันเวลาภายใน parryWindowSeconds -> รับดาเมจ 0 + คืน Stamina + ทำให้ศัตรู Stagger)
        if (IsBlocking && NetworkStamina > 0f && (Time.time - _blockStartedTime <= parryWindowSeconds))
        {
            if (parryStaminaReward > 0f)
            {
                NetworkStamina = Mathf.Min(MaxStamina, NetworkStamina + parryStaminaReward);
                _sprintExhaustedCooldown = false;
            }
            // รีเซ็ตคูลดาวน์ Parry สั้นลงเมื่อปัดป้องสำเร็จ เพื่อเปิดโอกาสปัดป้องท่าต่อเนื่องได้
            _nextParryReadyTime = Time.time + 0.15f;

            NetworkId attackerNetId = (attackerNpc != null && attackerNpc.Object != null && attackerNpc.Object.IsValid)
                ? attackerNpc.Object.Id
                : default;
            RPC_BroadcastParrySuccess(attackerNetId);

            if (attackerNpc != null && attackerNpc.IsAlive)
            {
                float distToAttacker = Vector3.Distance(transform.position, attackerNpc.transform.position);
                bool canStagger = !attackerNpc.IsBoss || distToAttacker <= 7.5f;
                if (canStagger)
                {
                    float staggerDur = attackerNpc.IsBoss ? parryBossStaggerDuration : parryEnemyStaggerDuration;
                    attackerNpc.RPC_ApplyParryStagger(staggerDur);
                }
            }

            Debug.Log($"[PlayerCombat] PERFECT PARRY! Blocked {damage} dmg cleanly from {(attackerNpc != null ? attackerNpc.name : "attacker")}.");
            return;
        }

        // 2. หักค่าป้องกันจากอุปกรณ์สวมใส่ (Armor / Shield / Pants)
        if (TotalDefense > 0)
            damage = Mathf.Max(1, damage - TotalDefense);

        // 3. ตั้งการ์ดป้องกันปกติ (Sustained Block: ลดดาเมจ 75% และใช้ Stamina)
        if (IsBlocking && NetworkStamina > 0f)
        {
            damage = Mathf.Max(1, Mathf.RoundToInt(damage * (1f - blockDamageReduction)));
            TryConsumeAuthorityStamina(blockHitStaminaCost);
            if (NetworkStamina <= 0f)
            {
                IsBlocking = false;
                _blockStartedTime = -100f;
                _localBlockingSent = false;
                if (_capsule != null) _capsule.SpeedMultiplier = 1f;
                UpdateBlockVfxVisual(false);
                RPC_BroadcastBlocking(false);
                RPC_BroadcastGuardBreak();
                Debug.Log("[PlayerCombat] Guard broken! Stamina depleted from blocking hit.");
            }
            else
            {
                // เล่นอนิเมชันรับแรงปะทะขณะตั้งการ์ด (Block Hit) บนทุกเครื่อง
                RPC_BroadcastBlockHit(damage);
            }
        }
        if (damage <= 0) return;

        NetworkHealth = Mathf.Max(0, NetworkHealth - damage);

        if (NetworkHealth <= 0 && respawnDelaySeconds > 0f && _respawnRoutine == null)
            _respawnRoutine = StartCoroutine(RespawnRoutine());
    }

    private IEnumerator RespawnRoutine()
    {
        yield return new WaitForSeconds(respawnDelaySeconds);
        _respawnRoutine = null;

        if (!_isSpawned || !Object.HasStateAuthority) yield break;

        TeleportToSpawn();
        _lastHitSequenceByAttacker.Clear();
        NetworkStamina = MaxStamina;
        _sprintExhaustedCooldown = false;
        NetworkHealth = MaxHealth;
    }

    private void TeleportToSpawn()
    {
        Vector3 dest = SafeZone.Instance != null ? SafeZone.Instance.ReconnectSpawnPosition : _spawnPosition;
        TeleportTo(dest);
    }

    public void TeleportTo(Vector3 targetPosition)
    {
        if (_capsule != null)
        {
            _capsule.TeleportCharacter(targetPosition);
            return;
        }
        if (_rb != null && !_rb.isKinematic)
        {
            _rb.position = targetPosition;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }
        transform.position = targetPosition;
        if (_networkTransform != null) _networkTransform.Teleport(targetPosition, transform.rotation);
    }

    private void OnHealthChanged()
    {
        ApplyHealthState(forceNotify: false);
    }

    private void ApplyHealthState(bool forceNotify)
    {
        if (!_isSpawned) return;

        int health = NetworkHealth;
        bool dead = health <= 0;

        if (!forceNotify && health < _lastHealthValue && !dead)
        {
            _animationDriver?.PlayHitReaction();
            if (enableCameraShake && IsLocallyControlled)
                PlayerFollowCamera.ShakeActive(0.13f, 0.11f);
        }
        _lastHealthValue = health;

        HealthChanged?.Invoke(health, MaxHealth);

        if (!forceNotify && dead == _lastDeadState) return;
        _lastDeadState = dead;

        if (_capsule != null)
        {
            _capsule.MovementLocked = dead;
            if (dead) _capsule.SpeedMultiplier = 1f;
        }

        if (dead)
        {
            if (Object.HasStateAuthority)
            {
                if (IsBlocking) IsBlocking = false;
                if (IsWeaponDrawn) IsWeaponDrawn = false;
            }
            UpdateBlockVfxVisual(false);
            if (_hitWindowRoutine != null) { StopCoroutine(_hitWindowRoutine); _hitWindowRoutine = null; }
            CloseHitWindow();
            _bufferedUntil = -1f;
        }

        if (_capsule != null) _capsule.SetBodyVisible(!dead);
        _animationDriver?.SetDead(dead);

        DeathStateChanged?.Invoke(dead);
    }

    // ------------------------------------------------------------------ debug

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private void OnGUI()
    {
        if (!showDebugHud || !_isSpawned || !IsLocallyControlled) return;

        float cd = Mathf.Max(0f, _nextLocalAttackTime - Time.time);
        GUI.Box(new Rect(10f, 10f, 310f, 96f), GUIContent.none);
        GUI.Label(new Rect(18f, 14f, 296f, 88f),
            $"Lv.{NetworkLevel} (EXP {NetworkExperience}/{ExpToNextLevel})  StatPts: {NetworkUnspentStatPoints}\n" +
            $"HP {NetworkHealth}/{MaxHealth}   STA {NetworkStamina:0}/{MaxStamina:0}   ATK {TotalAttackDamage}   DEF {TotalDefense}\n" +
            $"STR {StatPointsSTR}  VIT {StatPointsVIT}  END {StatPointsEND}  AGI {StatPointsAGI}  |  {(IsInCombat ? "IN COMBAT" : "COMBAT READY")}\n" +
            $"Cooldown {cd:0.00}s   Combo {ComboStep}   Block {(IsBlocking ? "ON" : "off")}");
    }

    private void OnDrawGizmosSelected()
    {
        if (weaponHitbox == null) return;
        Gizmos.color = IsHitWindowOpen ? Color.red : new Color(1f, 0.6f, 0f, 0.6f);
        Gizmos.matrix = weaponHitbox.transform.localToWorldMatrix;
        if (weaponHitbox is SphereCollider s) Gizmos.DrawWireSphere(s.center, s.radius);
        else if (weaponHitbox is BoxCollider bx) Gizmos.DrawWireCube(bx.center, bx.size);
    }
#endif
}

