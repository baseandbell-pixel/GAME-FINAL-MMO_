using System;
using System.Collections;
using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Networked melee combat for the player capsule (Photon Fusion 2, SHARED MODE).
///
/// ── Authority model ────────────────────────────────────────────────────────────────
/// Shared Mode has no dedicated server: every client is State Authority (SA) of the
/// objects it spawned, and Input Authority (IA) of that same object is the same peer.
/// Therefore the only party that can be trusted to change a player's health is that
/// player's own SA. This component is VICTIM-AUTHORITATIVE:
///
///   1. Owner client polls the mouse (IA only) -> cooldown + buffer -> RPC_RequestAttack (IA -> SA)
///   2. SA validates (alive, cooldown) -> RPC_BroadcastAttack (SA -> All): animation, VFX,
///      and the timed opening of the weapon trigger on EVERY peer.
///   3. On the victim's peer the attacker's (proxy) weapon trigger overlaps the victim's
///      body. OnTriggerEnter only acts when THIS object has State Authority, i.e. a peer
///      only ever resolves hits against its own player.
///   4. The victim derives the damage from its OWN copy of the attacker's config (a client
///      can never send an arbitrary number) and applies it through RPC_TakeDamage (SA -> SA).
///   5. [Networked] NetworkHealth replicates to everybody; OnChangedRender drives UI/death.
///
/// SECURITY NOTE: with no trusted server, a modified client can still decline to take
/// damage. The checks here stop accidental desyncs, spam and trivially forged input; they
/// are not a substitute for Host/Server mode or a Photon plugin if cheating matters.
/// ──────────────────────────────────────────────────────────────────────────────────
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class PlayerCombat : NetworkBehaviour
{
    private const int DefaultMaxHealth = 100;

    // ------------------------------------------------------------------ inspector

    [Header("References")]
    [Tooltip("Trigger collider on a CHILD object, enabled only during the active attack window.")]
    [SerializeField] private Collider weaponHitbox;
    [Tooltip("Optional. Receives the attack trigger on every peer.")]
    [SerializeField] private Animator animator;
    [SerializeField] private string attackTriggerName = "Attack";
    [Tooltip("Optional. Played on every peer when an attack starts.")]
    [SerializeField] private ParticleSystem attackVfx;
    [Tooltip("Optional. Renderer shown only while the hit window is open (dev feedback).")]
    [SerializeField] private Renderer hitboxDebugVisual;

    [Header("Attack")]
    [Min(0.05f)] [SerializeField] private float attackCooldown = 0.6f;
    [Tooltip("A click made this early during the cooldown is remembered and fired when it ends.")]
    [Min(0f)] [SerializeField] private float inputBufferSeconds = 0.2f;
    [Tooltip("The authority accepts requests this much earlier than the cooldown (jitter tolerance).")]
    [Min(0f)] [SerializeField] private float cooldownTolerance = 0.1f;
    [Min(1)] [SerializeField] private int attackDamage = 20;

    [Header("Hit window")]
    [Tooltip("ON: Animation Events call AnimEvent_OpenHitWindow / AnimEvent_CloseHitWindow. OFF: timed coroutine.")]
    [SerializeField] private bool useAnimationEvents;
    [Min(0f)] [SerializeField] private float hitWindowDelay = 0.1f;
    [Min(0.01f)] [SerializeField] private float hitWindowDuration = 0.25f;
    [Tooltip("Hard cap: the window is force-closed after this long, even if an animation event never arrives.")]
    [Min(0.05f)] [SerializeField] private float hitWindowSafetyTimeout = 1f;

    [Header("Validation (applied by the victim's State Authority)")]
    [Tooltip("Upper bound for any single damage application.")]
    [Min(1)] [SerializeField] private int maxDamagePerHit = 50;
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

    // ------------------------------------------------------------------ public API

    public int MaxHealth => maxHealth;
    public bool IsDead => _isSpawned && NetworkHealth <= 0;
    public bool IsHitWindowOpen { get; private set; }

    /// <summary>Sequence number of the swing this peer last saw start (identifies a swing for de-duplication).</summary>
    public int CurrentSequence { get; private set; }

    /// <summary>(current, max) - raised on every peer when health changes.</summary>
    public event Action<int, int> HealthChanged;

    /// <summary>true = died, false = revived. Raised on every peer.</summary>
    public event Action<bool> DeathStateChanged;

    /// <summary>Every spawned player on this peer (local + remote proxies). Used by NPC AI and the map.</summary>
    public static readonly List<PlayerCombat> All = new List<PlayerCombat>();

    /// <summary>The player this peer controls.</summary>
    public static PlayerCombat Local { get; private set; }

    public bool IsSpawned => _isSpawned;

    /// <summary>Extra damage from equipped gear. Applied to NPC hits only (PvP reads the shared prefab config).</summary>
    public int WeaponDamageBonus { get; set; }

    // ------------------------------------------------------------------ private state

    private PlayerCapsule _capsule;
    private Rigidbody _rb;
    private Collider _bodyCollider;
    private NetworkTransform _networkTransform;
    private int _attackTriggerHash;

    private bool _isSpawned;
    private bool _lastDeadState;
    private Vector3 _spawnPosition;

    // Local (input side)
    private float _nextLocalAttackTime;
    private float _bufferedUntil = -1f;

    // Authority side
    private float _nextAuthorityAttackTime;
    private int _sequenceCounter;
    private Coroutine _respawnRoutine;

    // Shared: running hit window
    private Coroutine _hitWindowRoutine;

    // Victim side: last swing that already damaged us, per attacker.
    private readonly Dictionary<NetworkId, int> _lastHitSequenceByAttacker = new Dictionary<NetworkId, int>();

    // ------------------------------------------------------------------ lifecycle

    private void Awake()
    {
        _capsule = GetComponent<PlayerCapsule>();
        _rb = GetComponent<Rigidbody>();
        _bodyCollider = GetComponent<Collider>();           // the root capsule, NOT the weapon child
        _networkTransform = GetComponent<NetworkTransform>();
        _attackTriggerHash = string.IsNullOrEmpty(attackTriggerName) ? 0 : Animator.StringToHash(attackTriggerName);

        if (animator == null) animator = GetComponentInChildren<Animator>();

        if (weaponHitbox == null)
        {
            Debug.LogError($"[PlayerCombat] '{name}': weaponHitbox is not assigned - attacks will never hit.", this);
            return;
        }
        if (weaponHitbox.gameObject == gameObject)
        {
            Debug.LogError($"[PlayerCombat] '{name}': weaponHitbox must live on a CHILD object, not the root.", this);
            weaponHitbox = null;
            return;
        }
        if (!(weaponHitbox is SphereCollider || weaponHitbox is BoxCollider || weaponHitbox is CapsuleCollider))
            Debug.LogWarning("[PlayerCombat] weaponHitbox should be a Sphere/Box/Capsule collider (needed for exact overlap checks).", this);

        weaponHitbox.isTrigger = true;
        SetHitbox(false);
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
            // Config-driven max health (the property initializer only knows the constant).
            NetworkHealth = maxHealth;
        }

        // Late joiners may see an already-dead player; callbacks are not guaranteed for
        // initial values, so apply the current state explicitly.
        ApplyHealthState(forceNotify: true);
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
        _lastHitSequenceByAttacker.Clear();
    }

    // ------------------------------------------------------------------ 1. local input

    public bool IsLocallyControlled =>
        Object.HasInputAuthority || (Runner != null && Runner.GameMode == GameMode.Shared && Object.HasStateAuthority);

    private void Update()
    {
        // Input is polled ONLY for the peer that controls this object.
        if (!_isSpawned || !IsLocallyControlled) return;

        if (IsDead)
        {
            _bufferedUntil = -1f;
            return;
        }

        if (PollAttackPressed()) _bufferedUntil = Time.time + inputBufferSeconds;

        // Fire as soon as the cooldown allows, if a click is still buffered.
        if (_bufferedUntil >= Time.time && Time.time >= _nextLocalAttackTime)
        {
            _bufferedUntil = -1f;
            TryRequestAttack();
        }
    }

    private static bool PollAttackPressed()
    {
        if (!Application.isFocused || GameplayInput.IsBlocked) return false;

        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return false;

        // Do not swing when the click was meant for a UI element.
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return false;

        return true;
    }

    /// <summary>Client-side gate (cooldown) in front of the request RPC. Safe to call from other systems.</summary>
    public bool TryRequestAttack()
    {
        if (!_isSpawned || !IsLocallyControlled || IsDead) return false;
        if (Time.time < _nextLocalAttackTime) return false;

        _nextLocalAttackTime = Time.time + attackCooldown;
        RPC_RequestAttack();
        return true;
    }

    // ------------------------------------------------------------------ 2. RPC pipeline

    /// <summary>IA -> SA. Ask the authority to perform an attack. Carries no data: nothing to forge.</summary>
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RequestAttack()
    {
        if (!Object.HasStateAuthority) return;
        if (IsDead) return;

        // Authoritative rate limit - the real defence against RPC spam.
        float now = Time.time;
        if (now < _nextAuthorityAttackTime - cooldownTolerance) return;
        _nextAuthorityAttackTime = now + attackCooldown;

        _sequenceCounter++;
        RPC_BroadcastAttack(_sequenceCounter);
    }

    /// <summary>SA -> All. Cosmetic start of the swing and the timed hit window on every peer.</summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_BroadcastAttack(int sequence)
    {
        CurrentSequence = sequence;

        if (animator != null && _attackTriggerHash != 0) animator.SetTrigger(_attackTriggerHash);
        if (attackVfx != null) attackVfx.Play();
        onAttackStarted?.Invoke();

        if (_hitWindowRoutine != null) StopCoroutine(_hitWindowRoutine);
        _hitWindowRoutine = StartCoroutine(HitWindowRoutine());
    }

    // ------------------------------------------------------------------ 3. hit window

    private IEnumerator HitWindowRoutine()
    {
        CloseHitWindow();

        if (useAnimationEvents)
        {
            // Animation Events open/close the window; this is only the safety net.
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

    /// <summary>Animation Event hook. NOTE: Animation Events only reach components on the Animator's own GameObject.</summary>
    public void AnimEvent_OpenHitWindow() { if (useAnimationEvents) OpenHitWindow(); }

    /// <summary>Animation Event hook.</summary>
    public void AnimEvent_CloseHitWindow() { if (useAnimationEvents) CloseHitWindow(); }

    private void OpenHitWindow()
    {
        if (!_isSpawned || IsDead) return;
        IsHitWindowOpen = true;
        SetHitbox(true);
    }

    private void CloseHitWindow()
    {
        IsHitWindowOpen = false;
        SetHitbox(false);
    }

    private void SetHitbox(bool active)
    {
        if (weaponHitbox != null) weaponHitbox.enabled = active;
        if (hitboxDebugVisual != null) hitboxDebugVisual.enabled = active;
    }

    // ------------------------------------------------------------------ 4. hit resolution (victim side)

    /// <summary>
    /// Unity delivers trigger events of every collider on this Rigidbody to this root object.
    /// That includes our OWN weapon touching things, so everything not "a foreign, open weapon
    /// overlapping MY body" is filtered out below.
    /// </summary>
    private void OnTriggerEnter(Collider other)
    {
        // Authority gate: a peer only ever resolves hits against the player it owns.
        if (!_isSpawned || !Object.HasStateAuthority) return;
        if (IsDead || other == null) return;

        // Our own weapon touching an enemy NPC (NPCs are damaged via RPC to THEIR authority).
        var npc = other.GetComponentInParent<EnemyNPC>();
        if (npc != null)
        {
            TryHitNpc(npc, other);
            return;
        }

        var attacker = other.GetComponentInParent<PlayerCombat>();
        if (attacker == null || attacker == this || !attacker._isSpawned) return;

        // Must be the attacker's weapon, and the window must really be open (as seen here).
        if (other != attacker.weaponHitbox || !attacker.IsHitWindowOpen || attacker.IsDead) return;

        // Weapon-vs-weapon contact also raises this event; require a REAL overlap with our body.
        if (!WeaponOverlapsBody(attacker)) return;

        // Sanity range check (guards against forged/desynced positions).
        if ((attacker.transform.position - transform.position).sqrMagnitude > maxHitDistance * maxHitDistance) return;

        // One hit per swing per attacker.
        NetworkId attackerId = attacker.Object.Id;
        if (_lastHitSequenceByAttacker.TryGetValue(attackerId, out int lastSeq) && lastSeq == attacker.CurrentSequence) return;
        _lastHitSequenceByAttacker[attackerId] = attacker.CurrentSequence;

        // Damage comes from OUR copy of the attacker's config - never from the network.
        RPC_TakeDamage(attacker.attackDamage);
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

    /// <summary>Attacker side: our (State Authority) weapon overlapped an NPC body -> ask the NPC's authority to apply damage.</summary>
    private void TryHitNpc(EnemyNPC npc, Collider other)
    {
        if (!IsHitWindowOpen || weaponHitbox == null) return;
        if (!npc.IsSpawned || !npc.IsAlive || npc.BodyCollider == null || other != npc.BodyCollider) return;

        Transform w = weaponHitbox.transform;
        Transform b = npc.BodyCollider.transform;
        if (!Physics.ComputePenetration(weaponHitbox, w.position, w.rotation, npc.BodyCollider, b.position, b.rotation, out _, out _)) return;

        NetworkId id = npc.Object.Id;
        if (_lastNpcHitSequence.TryGetValue(id, out int seq) && seq == CurrentSequence) return;   // one hit per swing
        _lastNpcHitSequence[id] = CurrentSequence;

        int damage = attackDamage + WeaponDamageBonus;
        Debug.Log($"[PlayerCombat] Swing #{CurrentSequence} hit NPC {npc.name} (id={id}) for {damage} (base {attackDamage} + gear {WeaponDamageBonus})."); 
        npc.RPC_TakeDamage(damage);
    }

    /// <summary>
    /// NPC authority -> this player's State Authority. The amount is NOT taken from the caller: it is read from
    /// the NPC's own config here, after checking that the NPC is alive, in range and not spamming.
    /// </summary>
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

    // ------------------------------------------------------------------ 5. health

    /// <summary>
    /// SA -> SA. The single place health is reduced. Only the owner of this object ever executes it,
    /// and the amount is re-validated here regardless of who called.
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.StateAuthority)]
    public void RPC_TakeDamage(int damage)
    {
        if (!Object.HasStateAuthority) return;
        if (IsDead) return;

        damage = Mathf.Clamp(damage, 0, maxDamagePerHit);
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
        NetworkHealth = maxHealth;           // replicates -> everybody un-hides the body
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

    /// <summary>Fusion change-detection callback: runs on every peer when NetworkHealth changes.</summary>
    private void OnHealthChanged()
    {
        ApplyHealthState(forceNotify: false);
    }

    private void ApplyHealthState(bool forceNotify)
    {
        if (!_isSpawned) return;

        int health = NetworkHealth;
        bool dead = health <= 0;

        HealthChanged?.Invoke(health, maxHealth);

        if (!forceNotify && dead == _lastDeadState) return;
        _lastDeadState = dead;

        // Freeze movement through the explicit hook on PlayerCapsule (no-op if absent).
        if (_capsule != null) _capsule.MovementLocked = dead;

        if (dead)
        {
            if (_hitWindowRoutine != null) { StopCoroutine(_hitWindowRoutine); _hitWindowRoutine = null; }
            CloseHitWindow();
            _bufferedUntil = -1f;
        }

        // Hide the body while dead. Enabling/disabling the renderer does not interfere with
        // PlayerCapsule's MaterialPropertyBlock colouring.
        if (_capsule != null && _capsule.bodyRenderer != null) _capsule.bodyRenderer.enabled = !dead;

        DeathStateChanged?.Invoke(dead);
    }

    // ------------------------------------------------------------------ debug

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private void OnGUI()
    {
        if (!showDebugHud || !_isSpawned || !IsLocallyControlled) return;

        float cd = Mathf.Max(0f, _nextLocalAttackTime - Time.time);
        GUI.Box(new Rect(10f, 10f, 260f, 64f), GUIContent.none);
        GUI.Label(new Rect(18f, 14f, 250f, 58f),
            $"HP  {NetworkHealth}/{maxHealth}   {(IsDead ? "DEAD" : "ALIVE")}\n" +
            $"Cooldown  {cd:0.00}s   Swing #{CurrentSequence}\n" +
            $"Hit window  {(IsHitWindowOpen ? "OPEN" : "closed")}");
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
/// Local-only combat presentation. It has no gameplay authority: PlayerCombat and EnemyNPC
/// remain responsible for the Fusion RPCs and networked health. Attached at runtime to the
/// locally controlled player so no prefab or scene wiring is required.
/// </summary>
public sealed class CombatHud : MonoBehaviour
{
    private struct Popup
    {
        public Transform target;
        public int amount;
        public Color color;
        public float startedAt;
    }

    private const float PopupLifetime = 0.9f;
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

    /// <summary>Called on all peers by EnemyNPC; only the attacker sees their local combo increment.</summary>
    public static void NotifyEnemyDamaged(EnemyNPC enemy, int amount, PlayerRef attacker)
    {
        if (_localHud == null || enemy == null || amount <= 0) return;
        var local = PlayerCombat.Local;
        if (local == null || local.Runner == null || local.Runner.LocalPlayer != attacker) return;

        _localHud.AddPopup(enemy.LockPoint, amount, new Color(1f, 0.82f, 0.18f));
        _localHud._combo = Time.unscaledTime - _localHud._lastComboTime <= ComboTimeout ? _localHud._combo + 1 : 1;
        _localHud._lastComboTime = Time.unscaledTime;
        Debug.Log($"[CombatHud] Damage dealt: {amount}; combo {_localHud._combo}.");
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
        Debug.Log("[CombatHud] Local tactical crosshair and combat text enabled.");
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
            AddPopup(transform, damage, new Color(1f, 0.25f, 0.25f));
            Debug.Log($"[CombatHud] Local player received {damage} damage ({current}/{max}).");
        }
        _lastHealth = current;
    }

    private void AddPopup(Transform target, int amount, Color color)
    {
        _popups.Add(new Popup { target = target, amount = amount, color = color, startedAt = Time.unscaledTime });
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
        const float width = 260f, height = 26f;
        float x = 28f, y = Screen.height - 62f;
        float fraction = Mathf.Clamp01(_combat.NetworkHealth / (float)Mathf.Max(1, _combat.MaxHealth));
        var old = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, .72f);
        GUI.DrawTexture(new Rect(x - 3f, y - 3f, width + 6f, height + 28f), Texture2D.whiteTexture);
        GUI.color = new Color(.18f, .05f, .06f, 1f);
        GUI.DrawTexture(new Rect(x, y, width, height), Texture2D.whiteTexture);
        GUI.color = Color.Lerp(new Color(.85f, .12f, .16f), new Color(.18f, .86f, .35f), fraction);
        GUI.DrawTexture(new Rect(x, y, width * fraction, height), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUIStyle label = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
        GUI.Label(new Rect(x + 8f, y + 1f, width - 16f, height), $"HP  {_combat.NetworkHealth} / {_combat.MaxHealth}", label);
        GUIStyle caption = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.UpperLeft };
        GUI.Label(new Rect(x, y + height + 4f, width, 18f), "ACTION STATUS", caption);
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
            Vector3 screen = cam.WorldToScreenPoint(popup.target.position + Vector3.up * (1.45f + t * .8f));
            if (screen.z <= 0f) continue;
            GUI.color = new Color(popup.color.r, popup.color.g, popup.color.b, 1f - t);
            GUIStyle style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 22, fontStyle = FontStyle.Bold };
            GUI.Label(new Rect(screen.x - 55f, Screen.height - screen.y - 16f, 110f, 32f), $"-{popup.amount}", style);
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
