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

/// <summary>
/// Networked melee combat, stamina, block VFX, and combo critical hit system for the player
/// (Photon Fusion 2, SHARED MODE).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class PlayerCombat : NetworkBehaviour
{
    private const int DefaultMaxHealth = 100;
    private const float DefaultMaxStamina = 100f;

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
    [Range(0f, 0.95f)] [SerializeField] private float blockDamageReduction = 0.7f;
    [Min(0.1f)] [SerializeField] private float dodgeCooldown = 0.75f;
    [Min(0.1f)] [SerializeField] private float dodgeDuration = 0.45f;
    [Min(0.5f)] [SerializeField] private float dodgeDistance = 4.2f;

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
    [Min(1)] [SerializeField] private int maxDamagePerHit = 120;
    [Tooltip("Attacker and victim must be at most this far apart (generous: remote proxies lag).")]
    [Min(0.5f)] [SerializeField] private float maxHitDistance = 5f;

    [Header("Health")]
    [Min(1)] [SerializeField] private int maxHealth = DefaultMaxHealth;
    [Tooltip("Seconds until a dead player is restored at its spawn point. 0 or less = stay dead.")]
    [SerializeField] private float respawnDelaySeconds = 3f;

    [Header("Debug")]
    [SerializeField] private bool showDebugHud = true;

    [Header("Events (all peers)")]
    public UnityEvent onAttackStarted;

    // ------------------------------------------------------------------ networked state

    /// <summary>Replicated to every client by Fusion. Only the State Authority may write it.</summary>
    [Networked, OnChangedRender(nameof(OnHealthChanged))]
    public int NetworkHealth { get; set; } = DefaultMaxHealth;

    /// <summary>Replicated player stamina (0..MaxStamina).</summary>
    [Networked, OnChangedRender(nameof(OnStaminaChanged))]
    public float NetworkStamina { get; set; } = DefaultMaxStamina;

    /// <summary>Replicated blocking stance (Right Mouse Button or E / LeftAlt).</summary>
    [Networked, OnChangedRender(nameof(OnBlockingChanged))]
    public NetworkBool IsBlocking { get; set; }

    /// <summary>Replicated current combo step (1..3).</summary>
    [Networked]
    public int ComboStep { get; set; } = 1;

    // ------------------------------------------------------------------ public API

    public int MaxHealth => maxHealth;
    public float MaxStamina => maxStamina;
    public float CurrentStamina => _isSpawned ? NetworkStamina : maxStamina;
    public bool CanSprint => !IsDead && CurrentStamina > 0.5f && !_sprintExhaustedCooldown;
    public bool CanAttack => !IsDead && CurrentStamina > 0f;
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

    /// <summary>true = died, false = revived. Raised on every peer.</summary>
    public event Action<bool> DeathStateChanged;

    /// <summary>Every spawned player on this peer (local + remote proxies). Used by NPC AI and the map.</summary>
    public static readonly List<PlayerCombat> All = new List<PlayerCombat>();

    /// <summary>The player this peer controls.</summary>
    public static PlayerCombat Local { get; private set; }

    public bool IsSpawned => _isSpawned;

    /// <summary>Extra damage from equipped gear. Applied to NPC hits only (PvP reads the shared prefab config).</summary>
    public int WeaponDamageBonus { get; set; }

    /// <summary>Temporary attack buff from consumables (e.g. Reinforced GreatSword Oil).</summary>
    public int ActiveBuffDamageBonus => Time.time < _attackBuffUntil ? _attackBuffBonus : 0;
    public float ActiveBuffRemainingSeconds => Mathf.Max(0f, _attackBuffUntil - Time.time);

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

    /// <summary>
    /// Ensures the weapon hitbox is aligned to the GreatSword blade mesh on Sword_Hand
    /// and hides/removes any debug hitbox visual renderers so swings are completely clean in Play Mode.
    /// </summary>
    private void EnsureBladeHitboxAlignedAndClean()
    {
        // Hide any legacy debug visual renderer unconditionally
        if (hitboxDebugVisual != null)
        {
            hitboxDebugVisual.enabled = false;
            hitboxDebugVisual.gameObject.SetActive(false);
            hitboxDebugVisual = null;
        }

        // Find Sword_Hand on the character model
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
            // Disable any debug renderers on or under WeaponHitbox
            foreach (Renderer r in whTransform.GetComponentsInChildren<Renderer>(true))
            {
                r.enabled = false;
                if (r.gameObject != whTransform.gameObject)
                    r.gameObject.SetActive(false);
            }

            // Parent WeaponHitbox to Sword_Hand and replace SphereCollider with BoxCollider aligned to the blade
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

    /// <summary>
    /// Creates the Right-Click Block Shield VFX (translucent energy dome + glowing parry ring) around the player.
    /// Toggled on all peers via replicated IsBlocking state.
    /// </summary>
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

        // 1. Translucent Frontal Energy Barrier Dome
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

        // 2. Glowing Ground Guard Ring
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
        }

        if (Object.HasStateAuthority)
        {
            NetworkHealth = maxHealth;
            NetworkStamina = maxStamina;
        }

        ApplyHealthState(forceNotify: true);
        UpdateBlockVfxVisual(IsBlocking && !IsDead);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
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

    // ------------------------------------------------------------------ 1. local input & stamina tick

    public bool IsLocallyControlled =>
        Object.HasInputAuthority || (Runner != null && Runner.GameMode == GameMode.Shared && Object.HasStateAuthority);

    public override void FixedUpdateNetwork()
    {
        if (!_isSpawned || !Object.HasStateAuthority) return;
        TickStaminaRegen(Runner.DeltaTime);
    }

    private void Update()
    {
        // Animate active Block VFX pulse on all peers
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

        // While the hit window is open on the State Authority peer, poll active blade overlaps
        // so fast animated sword swings on Sword_Hand never miss between physics ticks.
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

        // Dodge / Roll (Spacebar or LeftControl)
        if (PollDodgePressed())
        {
            TryRequestDodge();
        }

        // Block (Right Mouse Button or LeftAlt)
        if (Application.isFocused && Time.time >= _overrideBlockUntil)
        {
            bool wantBlock = !IsDodging && CurrentStamina > 0f && PollBlockHeld();
            SetBlockingInput(wantBlock);
        }

        if (PollAttackPressed()) _bufferedUntil = Time.time + inputBufferSeconds;

        // Fire as soon as the cooldown allows, if a click is still buffered and stamina > 0.
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

        if (NetworkStamina < maxStamina)
        {
            NetworkStamina = Mathf.Min(maxStamina, NetworkStamina + staminaRegenRate * dt);
            if (_sprintExhaustedCooldown && NetworkStamina >= 12f)
                _sprintExhaustedCooldown = false;
        }
    }

    /// <summary>Consumes stamina while sprinting. Returns true if sprint can continue, false if depleted.</summary>
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

    /// <summary>Directly sets stamina on State Authority (for testing or special mechanics).</summary>
    public void SetStamina(float value)
    {
        if (!_isSpawned || !Object.HasStateAuthority) return;
        NetworkStamina = Mathf.Clamp(value, 0f, maxStamina);
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

    /// <summary>Restores stamina (used by Stamina Elixir consumable).</summary>
    public void RestoreStamina(float amount)
    {
        if (!_isSpawned || !Object.HasStateAuthority || IsDead || amount <= 0f) return;
        float before = NetworkStamina;
        NetworkStamina = Mathf.Min(maxStamina, NetworkStamina + amount);
        _sprintExhaustedCooldown = false;
        Debug.Log($"[PlayerCombat] Restored {NetworkStamina - before:0} Stamina ({before:0} -> {NetworkStamina:0}/{maxStamina:0}).");
    }

    /// <summary>Applies a temporary attack damage buff (used by Reinforced GreatSword Oil).</summary>
    public void ApplyAttackBuff(int bonusDamage, float durationSeconds)
    {
        if (bonusDamage <= 0 || durationSeconds <= 0f) return;
        _attackBuffBonus = bonusDamage;
        _attackBuffUntil = Time.time + durationSeconds;
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

    /// <summary>Client-side gate (cooldown + stamina) in front of the request RPC. Safe to call from other systems.</summary>
    public bool TryRequestAttack()
    {
        if (!_isSpawned || !IsLocallyControlled || IsDead) return false;
        if (CurrentStamina <= 0f)
        {
            Debug.Log("[PlayerCombat] Attack prevented: Stamina depleted.");
            return false;
        }
        if (Time.time < _nextLocalAttackTime) return false;

        _nextLocalAttackTime = Time.time + attackCooldown;
        RPC_RequestAttack();
        return true;
    }

    /// <summary>Initiates a directional dodge roll if stamina > 0 and replicates the animation across all peers.</summary>
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

    /// <summary>Updates blocking state for the locally controlled player and replicates to peers.</summary>
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

    /// <summary>IA -> SA. Ask the authority to perform an attack. Validates cooldown and stamina.</summary>
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RequestAttack()
    {
        if (!Object.HasStateAuthority) return;
        if (IsDead) return;
        if (NetworkStamina <= 0f) return;

        float now = Time.time;
        if (now < _nextAuthorityAttackTime - cooldownTolerance) return;
        if (!TryConsumeAuthorityStamina(attackStaminaCost)) return;

        _nextAuthorityAttackTime = now + attackCooldown;

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

    /// <summary>SA -> All. Cosmetic start of the swing and the timed hit window on every peer.</summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_BroadcastAttack(int sequence, int comboStep = 1)
    {
        CurrentSequence = sequence;
        ComboStep = comboStep;

        if (_animationDriver != null)
            _animationDriver.PlayAttack(comboStep);
        else if (animator != null && _attackTriggerHash != 0)
            animator.SetTrigger(_attackTriggerHash);

        if (attackVfx != null) attackVfx.Play();
        onAttackStarted?.Invoke();

        if (_hitWindowRoutine != null) StopCoroutine(_hitWindowRoutine);
        _hitWindowRoutine = StartCoroutine(HitWindowRoutine());
    }

    /// <summary>IA -> SA. Requests a directional dodge roll.</summary>
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

    /// <summary>SA -> All. Plays the directional roll animation on every peer.</summary>
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

    /// <summary>IA -> SA. Sets replicated blocking state.</summary>
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SetBlocking(NetworkBool blocking)
    {
        if (!Object.HasStateAuthority) return;
        IsBlocking = !IsDead && NetworkStamina > 0f && blocking;
        UpdateBlockVfxVisual(IsBlocking);
        RPC_BroadcastBlocking(IsBlocking);
    }

    /// <summary>SA -> All. Ensures immediate block animation &amp; Block Shield VFX transitions on all peers.</summary>
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
        StaminaChanged?.Invoke(NetworkStamina, maxStamina);
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
        // Debug hitbox visuals stay permanently disabled during Play Mode so the blade swing is clean.
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

        RPC_TakeDamage(attacker.attackDamage);
    }

    /// <summary>
    /// Actively checks blade collider overlaps and forward melee sweep during the open hit window.
    /// Ensures animated bone colliders (Sword_Hand/WeaponHitbox) never miss between FixedUpdate frames.
    /// </summary>
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
    private const float NpcDamageRangeSlack = 3f;

    /// <summary>Attacker side: our blade overlapped an NPC body -> calculate combo/crit damage and notify NPC authority.</summary>
    private void TryHitNpc(EnemyNPC npc, Collider other)
    {
        if (!IsHitWindowOpen || weaponHitbox == null) return;
        if (!npc.IsSpawned || !npc.IsAlive || npc.BodyCollider == null || other != npc.BodyCollider) return;

        Transform w = weaponHitbox.transform;
        Transform b = npc.BodyCollider.transform;
        bool bladeOverlap = Physics.ComputePenetration(weaponHitbox, w.position, w.rotation, npc.BodyCollider, b.position, b.rotation, out _, out _);

        if (!bladeOverlap)
        {
            // Also check if the enemy is right inside the GreatSword's immediate front cleave arc (<= 2.65m and within front 110 deg)
            Vector3 toNpc = npc.transform.position - transform.position;
            toNpc.y = 0f;
            float d = toNpc.magnitude;
            if (d > 2.65f) return;
            if (d > 0.65f && Vector3.Dot(transform.forward, toNpc / d) < 0.15f) return;
        }

        NetworkId id = npc.Object.Id;
        if (_lastNpcHitSequence.TryGetValue(id, out int seq) && seq == CurrentSequence) return;
        _lastNpcHitSequence[id] = CurrentSequence;

        int basePlusGear = attackDamage + WeaponDamageBonus + ActiveBuffDamageBonus;
        bool isCrit = ComboStep >= 2;
        float mult = ComboStep switch
        {
            2 => comboStep2CritMultiplier,
            3 => comboStep3CritMultiplier,
            _ => 1f
        };
        int finalDamage = Mathf.Max(1, Mathf.RoundToInt(basePlusGear * mult));

        Debug.Log($"[PlayerCombat] Swing #{CurrentSequence} (Combo {ComboStep}{(isCrit ? " CRIT!" : "")}) hit NPC {npc.name} (id={id}) for {finalDamage}.");
        npc.RPC_TakeDamageWithCrit(finalDamage, isCrit, ComboStep);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_ReceiveNpcDamage(NetworkId npcId)
    {
        if (!Object.HasStateAuthority || IsDead) return;

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

        float limit = npc.AttackRange + NpcDamageRangeSlack;
        if ((npc.transform.position - transform.position).sqrMagnitude > limit * limit)
        {
            Debug.LogWarning($"[PlayerCombat] NPC damage rejected: {npc.name} out of range.");
            return;
        }

        float now = Time.time;
        if (_lastNpcAttackTime.TryGetValue(npcId, out float last) && now - last < npc.AttackCooldown * 0.5f)
        {
            Debug.LogWarning($"[PlayerCombat] NPC damage rejected: {npc.name} attacking faster than its cooldown.");
            return;
        }
        _lastNpcAttackTime[npcId] = now;

        Debug.Log($"[PlayerCombat] {npc.name} hits {name} for {npc.AttackDamage}.");
        RPC_TakeDamage(npc.AttackDamage);
    }

    /// <summary>Restores health on the owner (State Authority) only. Used by consumables.</summary>
    public void HealSelf(int amount)
    {
        if (!_isSpawned || !Object.HasStateAuthority || IsDead || amount <= 0) return;
        int before = NetworkHealth;
        NetworkHealth = Mathf.Min(maxHealth, NetworkHealth + amount);
        Debug.Log($"[PlayerCombat] Healed {NetworkHealth - before} ({before} -> {NetworkHealth}/{maxHealth}).");
    }

    // ------------------------------------------------------------------ 5. health & block stamina drain

    [Rpc(RpcSources.StateAuthority, RpcTargets.StateAuthority)]
    public void RPC_TakeDamage(int damage)
    {
        if (!Object.HasStateAuthority) return;
        if (IsDead || IsDodging) return;

        damage = Mathf.Clamp(damage, 0, maxDamagePerHit);
        if (IsBlocking && NetworkStamina > 0f)
        {
            damage = Mathf.Max(1, Mathf.RoundToInt(damage * (1f - blockDamageReduction)));
            TryConsumeAuthorityStamina(blockHitStaminaCost);
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
        NetworkStamina = maxStamina;
        _sprintExhaustedCooldown = false;
        NetworkHealth = maxHealth;
    }

    private void TeleportToSpawn()
    {
        if (_rb != null)
        {
            _rb.position = _spawnPosition;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }
        transform.position = _spawnPosition;
        if (_networkTransform != null) _networkTransform.Teleport(_spawnPosition, Quaternion.identity);
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

        HealthChanged?.Invoke(health, maxHealth);

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
        GUI.Box(new Rect(10f, 10f, 280f, 82f), GUIContent.none);
        GUI.Label(new Rect(18f, 14f, 266f, 76f),
            $"HP  {NetworkHealth}/{maxHealth}   STA  {NetworkStamina:0}/{maxStamina:0}\n" +
            $"Cooldown  {cd:0.00}s   Combo {ComboStep}   Swing #{CurrentSequence}\n" +
            $"Block {(IsBlocking ? "ON (VFX)" : "off")}   HitWindow {(IsHitWindowOpen ? "OPEN" : "closed")}");
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
/// Spawned on all peers when an enemy takes damage so floating numbers and "CRIT!" labels are visible in 3D space.
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
                ? new Color(1.0f, 0.38f, 0.08f, 1f)   // Fiery Orange-Gold Finisher Crit
                : new Color(1.0f, 0.78f, 0.12f, 1f);  // Bright Gold Combo Crit
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

        // Punch scale on crit
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
        public int amount;
        public bool isCrit;
        public Color color;
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
    }

    /// <summary>Called on all peers by EnemyNPC when damaged.</summary>
    public static void NotifyEnemyDamaged(EnemyNPC enemy, int amount, PlayerRef attacker, bool isCrit = false, int comboStep = 1)
    {
        if (_localHud == null || enemy == null || amount <= 0) return;

        Color c = isCrit
            ? (comboStep >= 3 ? new Color(1f, 0.42f, 0.10f) : new Color(1f, 0.82f, 0.12f))
            : new Color(1f, 0.94f, 0.72f);

        _localHud.AddPopup(enemy.LockPoint, amount, c, isCrit);

        var local = PlayerCombat.Local;
        if (local != null && local.Runner != null && local.Runner.LocalPlayer == attacker)
        {
            _localHud._combo = Time.unscaledTime - _localHud._lastComboTime <= ComboTimeout ? _localHud._combo + 1 : 1;
            _localHud._lastComboTime = Time.unscaledTime;
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
            AddPopup(transform, damage, new Color(1f, 0.25f, 0.25f), isCrit: false);
            Debug.Log($"[CombatHud] Local player received {damage} damage ({current}/{max}).");
        }
        _lastHealth = current;
    }

    private void AddPopup(Transform target, int amount, Color color, bool isCrit)
    {
        _popups.Add(new Popup { target = target, amount = amount, isCrit = isCrit, color = color, startedAt = Time.unscaledTime });
    }

    private void OnGUI()
    {
        if (_localHud != this || Event.current.type != EventType.Repaint) return;
        DrawCrosshair();
        DrawStatus();
        DrawPopups();
        DrawCombo();
    }

    private void DrawStatus()
    {
        if (_combat == null || !_combat.IsSpawned) return;
        const float width = 268f, hpHeight = 24f, staHeight = 20f;
        float x = 28f, y = Screen.height - 92f;

        float hpFrac = Mathf.Clamp01(_combat.NetworkHealth / (float)Mathf.Max(1, _combat.MaxHealth));
        float staFrac = Mathf.Clamp01(_combat.CurrentStamina / Mathf.Max(1f, _combat.MaxStamina));

        var old = GUI.color;
        // Background frame
        GUI.color = new Color(0f, 0f, 0f, .76f);
        GUI.DrawTexture(new Rect(x - 4f, y - 4f, width + 8f, hpHeight + staHeight + 34f), Texture2D.whiteTexture);

        // 1. HP Bar
        GUI.color = new Color(.18f, .05f, .06f, 1f);
        GUI.DrawTexture(new Rect(x, y, width, hpHeight), Texture2D.whiteTexture);
        GUI.color = Color.Lerp(new Color(.85f, .12f, .16f), new Color(.18f, .86f, .35f), hpFrac);
        GUI.DrawTexture(new Rect(x, y, width * hpFrac, hpHeight), Texture2D.whiteTexture);

        GUI.color = Color.white;
        GUIStyle hpLabel = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
        GUI.Label(new Rect(x + 8f, y, width - 16f, hpHeight), $"HP  {_combat.NetworkHealth} / {_combat.MaxHealth}", hpLabel);

        // 2. Stamina Bar
        float staY = y + hpHeight + 4f;
        GUI.color = new Color(.08f, .12f, .16f, 1f);
        GUI.DrawTexture(new Rect(x, staY, width, staHeight), Texture2D.whiteTexture);
        Color staColor = _combat.CurrentStamina <= 15f
            ? new Color(1.0f, 0.35f, 0.12f)
            : Color.Lerp(new Color(0.95f, 0.65f, 0.12f), new Color(0.20f, 0.82f, 0.98f), staFrac);
        GUI.color = staColor;
        GUI.DrawTexture(new Rect(x, staY, width * staFrac, staHeight), Texture2D.whiteTexture);

        GUI.color = Color.white;
        GUIStyle staLabel = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
        GUI.Label(new Rect(x + 8f, staY, width - 16f, staHeight), $"STAMINA  {_combat.CurrentStamina:0} / {_combat.MaxStamina:0}", staLabel);

        // Status footer (Block / Attack Buff / Exhausted)
        GUIStyle caption = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperLeft };
        string statusText = _combat.IsBlocking
            ? "GUARD ACTIVE (70% DMG REDUCTION)"
            : (_combat.CurrentStamina <= 0.5f ? "EXHAUSTED (RECOVERING STAMINA)" : "ACTION STATUS: READY");
        if (_combat.ActiveBuffDamageBonus > 0)
            statusText += $"  |  +ATK {_combat.ActiveBuffDamageBonus} ({_combat.ActiveBuffRemainingSeconds:0}s)";
        GUI.Label(new Rect(x, staY + staHeight + 3f, width + 80f, 18f), statusText, caption);

        GUI.color = old;
    }

    private void DrawCrosshair()
    {
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
            string text = popup.isCrit ? $"CRIT! -{popup.amount}" : $"-{popup.amount}";
            GUI.Label(new Rect(screen.x - 85f, Screen.height - screen.y - 20f, 170f, 36f), text, style);
        }
        GUI.color = old;
    }

    private void DrawCombo()
    {
        if (_combo <= 1) return;
        float age = Time.unscaledTime - _lastComboTime;
        GUI.color = new Color(1f, 0.85f, .2f, Mathf.Clamp01(1f - age / ComboTimeout));
        GUIStyle style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 23, fontStyle = FontStyle.Bold };
        GUI.Label(new Rect(Screen.width * .5f - 110f, Screen.height * .5f + 45f, 220f, 35f), $"COMBO x{_combo}", style);
        GUI.color = Color.white;
    }
}
