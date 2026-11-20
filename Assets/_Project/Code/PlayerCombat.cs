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
/// Networked melee combat, stamina, block VFX, combo critical hits, EXP/Level progression,
/// Stat Point allocation, and 4-slot Equipment stat aggregation (Photon Fusion 2, SHARED MODE).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class PlayerCombat : NetworkBehaviour
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

    [Header("Block & Dodge")]
    [Range(0.1f, 1f)] [SerializeField] private float blockMoveSpeedMultiplier = 0.45f;
    [Range(0f, 0.95f)] [SerializeField] private float blockDamageReduction = 0.75f;
    [Min(0.1f)] [SerializeField] private float dodgeCooldown = 0.75f;
    [Min(0.1f)] [SerializeField] private float dodgeDuration = 0.45f;
    [Min(0.5f)] [SerializeField] private float dodgeDistance = 4.2f;

    [Header("Stamina")]
    [Min(10f)] [SerializeField] private float maxStamina = DefaultMaxStamina;
    [Min(1f)] [SerializeField] private float sprintStaminaPerSecond = 15f;
    [Min(1f)] [SerializeField] private float attackStaminaCost = 15f;
    [Min(1f)] [SerializeField] private float dodgeStaminaCost = 25f;
    [Min(1f)] [SerializeField] private float blockHitStaminaCost = 12f;
    [Min(1f)] [SerializeField] private float staminaRegenRate = 18f;
    [Min(0f)] [SerializeField] private float staminaRegenDelay = 1.2f;

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
    [SerializeField] private bool showDebugHud = false;

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
    public bool CanAttack => !IsDead && !IsInSafeZone && !SafeZone.IsPointInAnySafeZone(transform.position) && CurrentStamina > 0f;
    public bool CanDodge => !IsDead && CurrentStamina > 0f;
    public bool IsDead => _isSpawned && NetworkHealth <= 0;
    public bool IsHitWindowOpen { get; private set; }
    public bool IsDodging => Time.time < _dodgeInvulnerableUntil;
    public Collider WeaponHitbox => weaponHitbox;
    public GameObject BlockVfxObject => _blockVfxRoot;
    public bool IsBlockVfxActive => _blockVfxRoot != null && _blockVfxRoot.activeSelf;

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
    private float _bufferedUntil = -1f;
    private float _nextLocalDodgeTime;
    private float _overrideBlockUntil;
    private bool _localBlockingSent;

    // Authority side
    private float _nextAuthorityAttackTime;
    private float _lastAuthorityAttackTime = -100f;
    private float _lastCombatActivityTime = -100f;
    private int _authorityComboStep;
    private int _sequenceCounter;
    private float _dodgeInvulnerableUntil;
    private float _nextStaminaRegenTime;
    private bool _sprintExhaustedCooldown;
    private int _attackBuffBonus;
    private float _attackBuffUntil;
    private Coroutine _respawnRoutine;

    // Block VFX
    private GameObject _blockVfxRoot;
    private Transform _blockDome;
    private Transform _blockRing;

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

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");

        GameObject dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        dome.name = "ShieldDome";
        var domeCol = dome.GetComponent<Collider>();
        if (domeCol != null) Destroy(domeCol);
        dome.transform.SetParent(_blockVfxRoot.transform, false);
        dome.transform.localPosition = new Vector3(0f, 0.15f, 0.25f);
        dome.transform.localScale = new Vector3(1.35f, 1.65f, 1.15f);
        var domeRenderer = dome.GetComponent<Renderer>();
        if (domeRenderer != null)
        {
            domeRenderer.shadowCastingMode = ShadowCastingMode.Off;
            domeRenderer.receiveShadows = false;
            if (shader != null)
            {
                var mat = new Material(shader);
                mat.color = new Color(0.18f, 0.78f, 1.0f, 0.26f);
                domeRenderer.sharedMaterial = mat;
            }
        }
        _blockDome = dome.transform;

        GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "ShieldRing";
        var ringCol = ring.GetComponent<Collider>();
        if (ringCol != null) Destroy(ringCol);
        ring.transform.SetParent(_blockVfxRoot.transform, false);
        ring.transform.localPosition = new Vector3(0f, -0.92f, 0f);
        ring.transform.localScale = new Vector3(1.65f, 0.02f, 1.65f);
        var ringRenderer = ring.GetComponent<Renderer>();
        if (ringRenderer != null)
        {
            ringRenderer.shadowCastingMode = ShadowCastingMode.Off;
            ringRenderer.receiveShadows = false;
            if (shader != null)
            {
                var mat = new Material(shader);
                mat.color = new Color(0.25f, 0.90f, 1.0f, 0.58f);
                ringRenderer.sharedMaterial = mat;
            }
        }
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
            EnsureLocalPlayerVFXControllers();
        }

        if (Object.HasStateAuthority)
        {
            if (NetworkLevel < 1) NetworkLevel = 1;
            NetworkHealth = MaxHealth;
            NetworkStamina = MaxStamina;
        }

        ApplyHealthState(forceNotify: true);
        UpdateBlockVfxVisual(IsBlocking && !IsDead);
    }

    private void EnsureLocalPlayerVFXControllers()
    {
        var healVfx = GetComponent<MMOHealingPotionVFX>();
        if (healVfx == null)
        {
            healVfx = gameObject.AddComponent<MMOHealingPotionVFX>();
            healVfx.showDebugHUD = false;
        }

        var staminaVfx = GetComponent<MMOStaminaPotionVFX>();
        if (staminaVfx == null)
        {
            staminaVfx = gameObject.AddComponent<MMOStaminaPotionVFX>();
            staminaVfx.showDebugHUD = false;
        }

        var levelUpVfx = GetComponent<MMOLevelUpController>();
        if (levelUpVfx == null)
        {
            levelUpVfx = gameObject.AddComponent<MMOLevelUpController>();
            levelUpVfx.allowLeftClick = false;
        }

        var deathBloodVfx = GetComponent<MMOMonsterDeathBloodVFX>();
        if (deathBloodVfx == null)
        {
            deathBloodVfx = gameObject.AddComponent<MMOMonsterDeathBloodVFX>();
            deathBloodVfx.showTestHUD = false;
        }

        var heartbeatComboUi = GetComponent<MMOHeartbeatComboUI>();
        if (heartbeatComboUi == null)
        {
            heartbeatComboUi = gameObject.AddComponent<MMOHeartbeatComboUI>();
            heartbeatComboUi.countOnAttackInputForTest = false;
        }
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
            RPC_PlayLevelUpVFX(NetworkLevel);
            Debug.Log($"[PlayerCombat] LEVEL UP! {name} reached Level {NetworkLevel} (+{pointsGained} Stat Points, total unspent={NetworkUnspentStatPoints}).");
        }
        ProgressionChanged?.Invoke();
        PlayerSaveManager.RequestAutoSave(leveledUp ? "LevelUp" : "AddExperience");
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_PlayLevelUpVFX(int newLevel)
    {
        MMOLevelUpController.PlayLevelUpVFX(transform);
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
        IsInSafeZone = SafeZone.IsPointInAnySafeZone(transform.position);
        TickStaminaRegen(Runner.DeltaTime);
    }

    private void Update()
    {
        if (_blockVfxRoot != null && _blockVfxRoot.activeSelf)
        {
            if (_blockDome != null)
            {
                float s = 1f + Mathf.Sin(Time.time * 6f) * 0.035f;
                _blockDome.localScale = new Vector3(1.35f * s, 1.65f * s, 1.15f * s);
            }
            if (_blockRing != null)
            {
                float r = 1.65f + Mathf.Sin(Time.time * 4.5f) * 0.08f;
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

        if (PollDodgePressed())
        {
            TryRequestDodge();
        }

        if (Application.isFocused && Time.time >= _overrideBlockUntil)
        {
            bool wantBlock = !IsDodging && CurrentStamina > 0f && PollBlockHeld();
            SetBlockingInput(wantBlock);
        }

        if (PollAttackPressed()) _bufferedUntil = Time.time + inputBufferSeconds;

        if (_bufferedUntil >= Time.time && Time.time >= _nextLocalAttackTime && !IsDodging)
        {
            _bufferedUntil = -1f;
            if (CanAttack)
            {
                if (_localBlockingSent) SetBlockingInput(false);
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
        if (!_isSpawned) return true;
        if (IsDead) return false;
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
        MMOStaminaPotionVFX.UseStaminaPotion();
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

    private static bool PollAttackPressed()
    {
        if (!Application.isFocused || GameplayInput.IsBlocked) return false;

        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return false;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return false;

        return true;
    }

    private static bool PollBlockHeld()
    {
        if (!Application.isFocused || GameplayInput.IsBlocked) return false;

        var kb = Keyboard.current;
        if (kb != null && kb.leftAltKey.isPressed) return true;

        var mouse = Mouse.current;
        if (mouse != null && mouse.rightButton.isPressed)
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return false;
            return true;
        }

        return false;
    }

    private static bool PollDodgePressed()
    {
        if (!Application.isFocused || GameplayInput.IsBlocked) return false;

        var kb = Keyboard.current;
        return kb != null && (kb.spaceKey.wasPressedThisFrame || kb.leftCtrlKey.wasPressedThisFrame);
    }

    public bool TryRequestAttack()
    {
        if (!_isSpawned || !IsLocallyControlled || IsDead) return false;
        if (IsInSafeZone || SafeZone.IsPointInAnySafeZone(transform.position))
        {
            Debug.Log("[PlayerCombat] Attack prevented: Inside Comfort Zone (Safe Haven).");
            return false;
        }
        if (CurrentStamina <= 0f)
        {
            Debug.Log("[PlayerCombat] Attack prevented: Stamina depleted.");
            return false;
        }
        if (Time.time < _nextLocalAttackTime) return false;

        _nextLocalAttackTime = Time.time + attackCooldown;
        _lastCombatActivityTime = Time.time;
        RPC_RequestAttack();
        return true;
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
        if (IsDead || CurrentStamina <= 0f) blocking = false;
        if (holdSeconds > 0f) _overrideBlockUntil = Time.time + holdSeconds;

        if (_capsule != null)
            _capsule.SpeedMultiplier = blocking ? blockMoveSpeedMultiplier : 1f;

        if (_localBlockingSent == blocking) return;
        _localBlockingSent = blocking;
        if (Object.HasStateAuthority)
        {
            IsBlocking = !IsDead && blocking;
            UpdateBlockVfxVisual(IsBlocking);
            RPC_BroadcastBlocking(IsBlocking);
        }
        else
        {
            RPC_SetBlocking(blocking);
        }
    }

    // ------------------------------------------------------------------ 2. RPC pipeline

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RequestAttack()
    {
        if (!Object.HasStateAuthority) return;
        if (IsDead) return;
        if (IsInSafeZone || SafeZone.IsPointInAnySafeZone(transform.position)) return;
        if (NetworkStamina <= 0f) return;

        float now = Time.time;
        if (now < _nextAuthorityAttackTime - cooldownTolerance) return;
        float swingStaminaCost = _authorityComboStep <= 1 ? attackStaminaCost : (_authorityComboStep == 2 ? attackStaminaCost + 4f : attackStaminaCost + 8f);
        if (!TryConsumeAuthorityStamina(swingStaminaCost)) return;

        _nextAuthorityAttackTime = now + attackCooldown;
        _lastCombatActivityTime = now;

        if (IsBlocking)
        {
            IsBlocking = false;
            UpdateBlockVfxVisual(false);
        }

        if (now - _lastAuthorityAttackTime <= comboResetSeconds)
            _authorityComboStep = (_authorityComboStep % 3) + 1;
        else
            _authorityComboStep = 1;

        _lastAuthorityAttackTime = now;
        ComboStep = _authorityComboStep;

        _sequenceCounter++;
        RPC_BroadcastAttack(_sequenceCounter, _authorityComboStep);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_BroadcastAttack(int sequence, int comboStep = 1)
    {
        CurrentSequence = sequence;
        ComboStep = comboStep;
        _lastCombatActivityTime = Time.time;

        if (_animationDriver != null)
            _animationDriver.PlayAttack(comboStep);
        else if (animator != null && _attackTriggerHash != 0)
            animator.SetTrigger(_attackTriggerHash);

        if (attackVfx != null) attackVfx.Play();
        onAttackStarted?.Invoke();

        if (_hitWindowRoutine != null) StopCoroutine(_hitWindowRoutine);
        _hitWindowRoutine = StartCoroutine(HitWindowRoutine());
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

        var smokeVfx = GetComponent<MMODodgeSmokeController>();
        if (smokeVfx != null) smokeVfx.TriggerSmokeRoll();
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SetBlocking(NetworkBool blocking)
    {
        if (!Object.HasStateAuthority) return;
        IsBlocking = !IsDead && NetworkStamina > 0f && blocking;
        UpdateBlockVfxVisual(IsBlocking);
        RPC_BroadcastBlocking(IsBlocking);
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
        var aaaBlockParry = GetComponent<MMOBlockParryController>();
        if (aaaBlockParry != null)
        {
            if (_blockVfxRoot != null && _blockVfxRoot.activeSelf)
                _blockVfxRoot.SetActive(false);
            aaaBlockParry.SetGuardStanceActive(active);
            return;
        }

        if (_blockVfxRoot == null) BuildBlockShieldVfx();
        if (_blockVfxRoot != null && _blockVfxRoot.activeSelf != active)
            _blockVfxRoot.SetActive(active);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_BroadcastBlockedHitSoundwave()
    {
        var aaaBlockParry = GetComponent<MMOBlockParryController>();
        if (aaaBlockParry != null)
        {
            aaaBlockParry.TriggerBlockAbsorb();
        }
    }

    private void OnStaminaChanged()
    {
        if (!_isSpawned) return;
        StaminaChanged?.Invoke(NetworkStamina, MaxStamina);
    }

    // ------------------------------------------------------------------ 3. hit window

    private IEnumerator HitWindowRoutine()
    {
        CloseHitWindow();

        if (useAnimationEvents)
        {
            yield return new WaitForSeconds(hitWindowSafetyTimeout);
        }
        else
        {
            if (hitWindowDelay > 0f) yield return new WaitForSeconds(hitWindowDelay);
            OpenHitWindow();
            yield return new WaitForSeconds(Mathf.Min(hitWindowDuration, hitWindowSafetyTimeout));
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
        bool isComboCrit = ComboStep >= 2;
        bool isStatCrit = !isComboCrit && UnityEngine.Random.value < TotalCritChance;
        bool isCrit = isComboCrit || isStatCrit;

        float mult = ComboStep switch
        {
            2 => comboStep2CritMultiplier,
            3 => comboStep3CritMultiplier,
            _ => isStatCrit ? 1.5f : 1f
        };
        int finalDamage = Mathf.Max(1, Mathf.RoundToInt(totalAtk * mult));
        _lastCombatActivityTime = Time.time;

        Debug.Log($"[PlayerCombat] Swing #{CurrentSequence} (Combo {ComboStep}{(isCrit ? " CRIT!" : "")}) hit NPC {npc.name} (id={id}) for {finalDamage} (TotalATK={totalAtk}).");
        npc.RPC_TakeDamageWithCrit(finalDamage, isCrit, ComboStep);
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
        RPC_TakeDamage(incoming);
    }

    public void HealSelf(int amount)
    {
        if (!_isSpawned || !Object.HasStateAuthority || IsDead || amount <= 0) return;
        int before = NetworkHealth;
        NetworkHealth = Mathf.Min(MaxHealth, NetworkHealth + amount);
        MMOHealingPotionVFX.UseHealPotion();
        Debug.Log($"[PlayerCombat] Healed {NetworkHealth - before} ({before} -> {NetworkHealth}/{MaxHealth}).");
    }

    // ------------------------------------------------------------------ 5. health, defense & block stamina drain

    [Rpc(RpcSources.StateAuthority, RpcTargets.StateAuthority)]
    public void RPC_TakeDamage(int damage)
    {
        if (!Object.HasStateAuthority) return;
        if (IsDead || IsDodging) return;
        if (IsInSafeZone || SafeZone.IsPointInAnySafeZone(transform.position)) return;

        _lastCombatActivityTime = Time.time;
        damage = Mathf.Clamp(damage, 0, maxDamagePerHit);

        // Apply Defense from Equipped Gear (Armor / Shield / Pants)
        if (TotalDefense > 0)
            damage = Mathf.Max(1, damage - TotalDefense);

        if (IsBlocking && NetworkStamina > 0f)
        {
            damage = Mathf.Max(1, Mathf.RoundToInt(damage * (1f - blockDamageReduction)));
            TryConsumeAuthorityStamina(blockHitStaminaCost);

            // เมื่อถูกโจมตีขณะบล็อกอยู่ ให้ตัวโล่สว่างวาบรับแรงไว้บนหน้าโล่อย่างมั่นคง (ไม่สะท้อน/ไม่กระเด็น)
            var aaaBlockParry = GetComponent<MMOBlockParryController>();
            if (aaaBlockParry != null)
            {
                aaaBlockParry.TriggerBlockAbsorb();
            }
            RPC_BroadcastBlockedHitSoundwave();

            if (NetworkStamina <= 0f)
            {
                IsBlocking = false;
                _localBlockingSent = false;
                if (_capsule != null) _capsule.SpeedMultiplier = 1f;
                UpdateBlockVfxVisual(false);
                RPC_BroadcastBlocking(false);
                Debug.Log("[PlayerCombat] Guard broken! Stamina depleted from blocking hit.");
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
        if (_rb != null)
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
            _animationDriver?.PlayHitReaction();
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
            if (Object.HasStateAuthority && IsBlocking) IsBlocking = false;
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

    public void SimulateDodgeForTest()
    {
        TryConsumeAuthorityStamina(dodgeStaminaCost);
    }

    public void SimulateBlockInputForTest(bool blocking)
    {
        IsBlocking = blocking;
        UpdateBlockVfxVisual(blocking);
    }

    public void ClearStatsForTest()
    {
        NetworkLevel = 1;
        NetworkExperience = 0;
        NetworkGold = 0;
        NetworkUnspentStatPoints = 0;
        StatPointsSTR = 0;
        StatPointsVIT = 0;
        StatPointsEND = 0;
        StatPointsAGI = 0;
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

/// <summary>
/// World-space 3D floating damage number &amp; Combo Critical Hit popup.
/// </summary>
public sealed class FloatingDamagePopup : MonoBehaviour
{
    public static readonly List<FloatingDamagePopup> ActivePopups = new List<FloatingDamagePopup>();

    public int DamageAmount { get; private set; }
    public bool IsCritical { get; private set; }
    public int ComboStep { get; private set; }

    private TextMeshPro _tmp;
    private Vector3 _startPos;
    private float _spawnTime;
    private const float Lifetime = 1.05f;

    public static FloatingDamagePopup Spawn(Vector3 worldPos, int amount, bool isCrit, int comboStep = 1)
    {
        var go = new GameObject(isCrit ? $"FloatingCritPopup_{amount}" : $"FloatingDamagePopup_{amount}");
        Vector2 jitter = UnityEngine.Random.insideUnitCircle * 0.25f;
        go.transform.position = worldPos + new Vector3(jitter.x, 0.35f, jitter.y);

        var popup = go.AddComponent<FloatingDamagePopup>();
        popup.Init(amount, isCrit, comboStep);
        return popup;
    }

    public static FloatingDamagePopup SpawnWarning(Vector3 worldPos, string text, Color color)
    {
        var go = new GameObject("FloatingWarningPopup");
        go.transform.position = worldPos;
        var popup = go.AddComponent<FloatingDamagePopup>();
        popup.InitText(text, color, fontSize: 4.5f, isCrit: true);
        return popup;
    }

    private void InitText(string text, Color color, float fontSize, bool isCrit)
    {
        DamageAmount = 0;
        IsCritical = isCrit;
        ComboStep = 1;
        _startPos = transform.position;
        _spawnTime = Time.unscaledTime;

        _tmp = gameObject.AddComponent<TextMeshPro>();
        _tmp.alignment = TextAlignmentOptions.Center;
        _tmp.textWrappingMode = TextWrappingModes.NoWrap;
        _tmp.fontStyle = FontStyles.Bold;
        _tmp.text = text;
        _tmp.fontSize = fontSize;
        _tmp.color = color;

        if (!ActivePopups.Contains(this)) ActivePopups.Add(this);
    }

    private void Init(int amount, bool isCrit, int comboStep)
    {
        DamageAmount = amount;
        IsCritical = isCrit;
        ComboStep = comboStep;
        _startPos = transform.position;
        _spawnTime = Time.unscaledTime;

        _tmp = gameObject.AddComponent<TextMeshPro>();
        _tmp.alignment = TextAlignmentOptions.Center;
        _tmp.textWrappingMode = TextWrappingModes.NoWrap;
        _tmp.fontStyle = FontStyles.Bold;

        if (isCrit)
        {
            _tmp.text = $"CRIT!\n-{amount}";
            _tmp.fontSize = comboStep >= 3 ? 5.2f : 4.4f;
            _tmp.color = comboStep >= 3
                ? new Color(1.0f, 0.38f, 0.08f, 1f)
                : new Color(1.0f, 0.78f, 0.12f, 1f);
        }
        else
        {
            _tmp.text = $"-{amount}";
            _tmp.fontSize = 3.2f;
            _tmp.color = new Color(1.0f, 0.95f, 0.78f, 1f);
        }

        if (!ActivePopups.Contains(this)) ActivePopups.Add(this);
    }

    private void OnDestroy()
    {
        ActivePopups.Remove(this);
    }

    private void Update()
    {
        float age = Time.unscaledTime - _spawnTime;
        if (age >= Lifetime)
        {
            Destroy(gameObject);
            return;
        }

        float t = Mathf.Clamp01(age / Lifetime);
        float rise = (IsCritical ? 1.35f : 0.95f) * t;
        transform.position = _startPos + Vector3.up * rise;

        float scale = IsCritical
            ? (t < 0.18f ? Mathf.Lerp(0.6f, 1.35f, t / 0.18f) : Mathf.Lerp(1.35f, 1.0f, (t - 0.18f) / 0.82f))
            : 1.0f;
        transform.localScale = Vector3.one * scale;

        var cam = Camera.main;
        if (cam != null)
            transform.rotation = cam.transform.rotation;

        if (_tmp != null && t > 0.6f)
        {
            Color c = _tmp.color;
            c.a = Mathf.Clamp01(1f - (t - 0.6f) / 0.4f);
            _tmp.color = c;
        }
    }
}

/// <summary>
/// Local player HUD (HP bar, Stamina bar, Combo counter, Crosshair, and screen-space damage/crit popups).
/// </summary>
public sealed class CombatHud : MonoBehaviour
{
    private struct Popup
    {
        public Transform target;
        public string label;
        public Color color;
        public bool isCrit;
        public float startedAt;
    }

    private const float PopupLifetime = 1.05f;
    private const float ComboTimeout = 2.5f;
    private static CombatHud _localHud;
    private readonly List<Popup> _popups = new List<Popup>(16);
    private PlayerCombat _combat;
    private TargetLockSystem _lock;
    private int _lastHealth = -1;
    private int _combo;
    private float _lastComboTime = -100f;

    public static void EnsureFor(PlayerCombat combat)
    {
        if (combat == null || !combat.IsLocallyControlled) return;
        if (combat.GetComponent<CombatHud>() == null) combat.gameObject.AddComponent<CombatHud>();
        PlayerStatusHud.EnsureFor(combat);
    }

    public static void NotifyGoldGained(int amount, string source)
    {
        if (_localHud == null || amount <= 0) return;
        var local = PlayerCombat.Local;
        if (local != null)
            _localHud.AddTextPopup(local.transform, $"+{amount} GOLD", new Color(1f, 0.86f, 0.18f), isCrit: true);
        PlayerStatusHud.Instance?.ShowToast($"+{amount} Gold ({source})", new Color(1f, 0.86f, 0.22f));
    }

    public static void NotifyEnemyDamaged(EnemyNPC enemy, int amount, PlayerRef attacker, bool isCrit = false, int comboStep = 1)
    {
        if (_localHud == null || enemy == null || amount <= 0) return;

        Color c = isCrit
            ? (comboStep >= 3 ? new Color(1f, 0.42f, 0.10f) : new Color(1f, 0.82f, 0.12f))
            : new Color(1f, 0.94f, 0.72f);

        _localHud.AddTextPopup(enemy.LockPoint, isCrit ? $"CRIT! -{amount}" : $"-{amount}", c, isCrit);

        if (isCrit)
        {
            Vector3 hitPos = enemy.LockPoint != null ? enemy.LockPoint.position : enemy.transform.position + Vector3.up * 1.2f;
            float critScale = comboStep >= 3 ? 1.2f : -1f;
            MMOCriticalHitVFX.SpawnCriticalHit(hitPos, critScale);
        }

        var local = PlayerCombat.Local;
        if (local != null && local.Runner != null && local.Runner.LocalPlayer == attacker)
        {
            _localHud._combo = Time.unscaledTime - _localHud._lastComboTime <= ComboTimeout ? _localHud._combo + 1 : 1;
            _localHud._lastComboTime = Time.unscaledTime;
            MMOHeartbeatComboUI.RegisterHit();
            Debug.Log($"[CombatHud] Damage dealt: {amount}{(isCrit ? " (CRIT!)" : "")}; combo {_localHud._combo}.");
        }
    }

    private void Awake()
    {
        _combat = GetComponent<PlayerCombat>();
        _lock = GetComponent<TargetLockSystem>();
    }

    private void OnEnable()
    {
        if (_combat != null) _combat.HealthChanged += OnHealthChanged;
    }

    private void Start()
    {
        _localHud = this;
        if (_combat != null) _lastHealth = _combat.NetworkHealth;
        Debug.Log("[CombatHud] Local tactical crosshair, HP & Stamina bars, and combat text enabled.");
    }

    private void OnDisable()
    {
        if (_combat != null) _combat.HealthChanged -= OnHealthChanged;
        if (_localHud == this) _localHud = null;
    }

    private void Update()
    {
        if (_combo > 0 && Time.unscaledTime - _lastComboTime > ComboTimeout) _combo = 0;
        for (int i = _popups.Count - 1; i >= 0; i--)
            if (_popups[i].target == null || Time.unscaledTime - _popups[i].startedAt > PopupLifetime) _popups.RemoveAt(i);
    }

    private void OnHealthChanged(int current, int max)
    {
        if (_lastHealth >= 0 && current < _lastHealth)
        {
            int damage = _lastHealth - current;
            AddTextPopup(transform, $"-{damage}", new Color(1f, 0.25f, 0.25f), isCrit: false);
            Debug.Log($"[CombatHud] Local player received {damage} damage ({current}/{max}).");
        }
        _lastHealth = current;
    }

    private void AddTextPopup(Transform target, string label, Color color, bool isCrit)
    {
        _popups.Add(new Popup { target = target, label = label, isCrit = isCrit, color = color, startedAt = Time.unscaledTime });
    }

    public static bool IsCrosshairVisible =>
        (MapManager.Instance == null || !MapManager.Instance.IsWorldMapOpen) && !GameplayInput.IsBlocked;

    private void OnGUI()
    {
        if (_localHud != this || Event.current.type != EventType.Repaint) return;
        DrawCrosshair();
        if (!GameplayInput.IsBlocked)
        {
            DrawPopups();
            DrawCombo();
        }
    }

    private void DrawCrosshair()
    {
        if (!IsCrosshairVisible) return;

        bool locked = _lock != null && _lock.IsLocked;
        Color color = locked ? new Color(1f, 0.72f, 0.12f) : new Color(0.9f, 0.95f, 1f);
        float radius = locked ? 15f : 10f;
        float thickness = locked ? 3f : 2f;
        float x = Screen.width * .5f, y = Screen.height * .5f;
        var old = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(new Rect(x - radius - 7f, y - thickness * .5f, 6f, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(x + radius + 1f, y - thickness * .5f, 6f, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(x - thickness * .5f, y - radius - 7f, thickness, 6f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(x - thickness * .5f, y + radius + 1f, thickness, 6f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(x - 1.5f, y - 1.5f, 3f, 3f), Texture2D.whiteTexture);

        // Minimal diegetic/crosshair stamina & exhaustion feedback (only visible when stamina is not full)
        if (_combat != null && _combat.IsSpawned && _combat.CurrentStamina < _combat.MaxStamina - 0.5f)
        {
            float frac = Mathf.Clamp01(_combat.CurrentStamina / Mathf.Max(1f, _combat.MaxStamina));
            const float barW = 56f, barH = 4f;
            float bx = x - barW * 0.5f;
            float by = y + radius + 11f;

            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(new Rect(bx - 1f, by - 1f, barW + 2f, barH + 2f), Texture2D.whiteTexture);

            GUI.color = _combat.CurrentStamina <= 15f
                ? new Color(1f, 0.32f, 0.12f, 0.92f)
                : new Color(0.22f, 0.88f, 0.98f, 0.88f);
            GUI.DrawTexture(new Rect(bx, by, barW * frac, barH), Texture2D.whiteTexture);

            if (_combat.CurrentStamina <= 0.5f)
            {
                GUI.color = new Color(1f, 0.35f, 0.15f, 0.95f);
                GUIStyle exStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.UpperCenter,
                    fontSize = 11,
                    fontStyle = FontStyle.Bold
                };
                GUI.Label(new Rect(x - 60f, by + 5f, 120f, 18f), "EXHAUSTED", exStyle);
            }
        }

        GUI.color = old;
    }

    private void DrawPopups()
    {
        var cam = Camera.main;
        if (cam == null) return;
        var old = GUI.color;
        for (int i = 0; i < _popups.Count; i++)
        {
            Popup popup = _popups[i];
            float t = Mathf.Clamp01((Time.unscaledTime - popup.startedAt) / PopupLifetime);
            Vector3 screen = cam.WorldToScreenPoint(popup.target.position + Vector3.up * (1.5f + t * .9f));
            if (screen.z <= 0f) continue;

            GUI.color = new Color(popup.color.r, popup.color.g, popup.color.b, 1f - t);
            GUIStyle style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = popup.isCrit ? 26 : 21,
                fontStyle = FontStyle.Bold
            };
            GUI.Label(new Rect(screen.x - 95f, Screen.height - screen.y - 20f, 190f, 36f), popup.label, style);
        }
        GUI.color = old;
    }

    private void DrawCombo()
    {
        if (MMOHeartbeatComboUI.Instance != null) return;
        if (_combo <= 1) return;
        float age = Time.unscaledTime - _lastComboTime;
        GUI.color = new Color(1f, 0.85f, .2f, Mathf.Clamp01(1f - age / ComboTimeout));
        GUIStyle style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 23, fontStyle = FontStyle.Bold };
        GUI.Label(new Rect(Screen.width * .5f - 110f, Screen.height * .5f + 45f, 220f, 35f), $"COMBO x{_combo}", style);
        GUI.color = Color.white;
    }
}
