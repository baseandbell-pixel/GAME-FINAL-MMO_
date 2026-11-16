using System;
using System.Collections;
using System.Collections.Generic;
using Fusion;
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

[Serializable]
public class LootEntry
{
    public ItemData item;
    [Min(1)] public int minAmount = 1;
    [Min(1)] public int maxAmount = 1;
    [Range(0f, 1f)] public float chance = 1f;
}

/// <summary>
/// Hostile NPC for Photon Fusion SHARED MODE.
///
/// AUTHORITY: whoever spawned the NPC is its State Authority (SA) and is the ONLY peer that runs the AI
/// (FixedUpdateNetwork) and writes its networked state. If that peer leaves, Fusion hands the object to
/// another client and that client simply continues: everything the AI needs lives in [Networked] data
/// (health, state, target, home position). Authority is deliberately NOT moved when a player aggroes the
/// NPC (that would ping-pong ownership); instead every other peer talks to the SA through RPCs.
///
/// DAMAGE IN : RPC_TakeDamage   (any peer -> SA), validated against the attacker's identity, range and rate.
/// DAMAGE OUT: PlayerCombat.RPC_ReceiveNpcDamage (SA -> victim's SA), validated by the victim.
///
/// MOVEMENT: straight-line steering on a flat plane (no NavMesh). Fine for sandbox arenas; swap Move()
/// for a NavMeshAgent if you bake a NavMesh.
/// </summary>
[RequireComponent(typeof(Collider))]
public class EnemyNPC : NetworkBehaviour, ILockTarget
{
    public static readonly List<EnemyNPC> All = new List<EnemyNPC>();

    // ---- stats (spec) ----
    [Header("Combat stats")]
    public int MaxHealth = 200;
    public int AttackDamage = 15;
    public float AttackCooldown = 2.0f;
    public float AttackRange = 2.5f;

    [Header("AI")]
    [Min(1f)] [SerializeField] private float aggroRadius = 10f;
    [Tooltip("If the chased player gets this far from the NPC's home, it gives up and returns.")]
    [Min(2f)] [SerializeField] private float leashRadius = 25f;
    [SerializeField] private float patrolRadius = 5f;
    [SerializeField] private Vector2 idleWaitRange = new Vector2(1.5f, 4f);
    [SerializeField] private float patrolSpeed = 1.8f;
    [SerializeField] private float chaseSpeed = 4.5f;
    [SerializeField] private float returnSpeed = 6f;
    [SerializeField] private float turnSpeed = 10f;
    [Min(0f)] [SerializeField] private float despawnDelayAfterDeath = 4f;

    [Header("Damage validation (authority side)")]
    [SerializeField] private int maxDamagePerHit = 100;
    [SerializeField] private float maxHitDistance = 6f;
    [SerializeField] private float minSecondsBetweenHitsPerPlayer = 0.2f;

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
    public int CurrentHealth { get; set; } = 200;

    [Networked, OnChangedRender(nameof(OnStateChanged))]
    public NpcState State { get; set; }

    [Networked] public NetworkId TargetId { get; set; }
    [Networked] public Vector3 HomePosition { get; set; }
    /// <summary>Spawner-assigned group identifier. NPCs with the same id assist each other.</summary>
    [Networked] public int CampId { get; set; }

    // ---- public API ----
    public bool IsSpawned { get; private set; }
    public bool IsAlive => IsSpawned && State != NpcState.Dead && CurrentHealth > 0;
    public Collider BodyCollider { get; private set; }
    public float AggroRadius => aggroRadius;

    // ILockTarget
    public Transform LockPoint => _lockPoint != null ? _lockPoint : transform;
    public bool IsLockable => IsAlive;
    public string LockName => name;

    // ---- private ----
    private Transform _lockPoint;
    private MaterialPropertyBlock _mpb;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int HitTrigger = Animator.StringToHash("Hit");
    private static readonly int AttackTrigger = Animator.StringToHash("Attack");
    private static readonly int DieTrigger = Animator.StringToHash("Die");

    private float _nextAttackTime;
    private float _stateTimer;
    private float _deathTime;
    private bool _hasPatrolPoint;
    private Vector3 _patrolPoint;
    private bool _flashing;
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
            CurrentHealth = MaxHealth;
            HomePosition = transform.position;
            State = NpcState.Idle;
            TargetId = default;
            Debug.Log($"[EnemyNPC] {name} spawned with authority. HP={CurrentHealth}/{MaxHealth}, home={HomePosition}.");
        }
        else
        {
            Debug.Log($"[EnemyNPC] {name} spawned as proxy (authority = {Object.StateAuthority}).");
        }

        RefreshVisuals();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        IsSpawned = false;
        All.Remove(this);
        StopAllCoroutines();
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
            case NpcState.Dead: TickDead(); break;
        }
    }

    private void TickIdle()
    {
        if (TryAcquireNearestPlayer()) return;

        _stateTimer -= Time.deltaTime;
        if (_stateTimer <= 0f && patrolRadius > 0.5f)
        {
            Vector2 c = UnityEngine.Random.insideUnitCircle * patrolRadius;
            _patrolPoint = HomePosition + new Vector3(c.x, 0f, c.y);
            _hasPatrolPoint = true;
            ChangeState(NpcState.Patrol, "wandering");
        }
    }

    private void TickPatrol(float dt)
    {
        if (TryAcquireNearestPlayer()) return;

        if (!_hasPatrolPoint) { ChangeState(NpcState.Idle, "no patrol point"); return; }

        if (MoveTowards(_patrolPoint, patrolSpeed, dt) <= 0.3f)
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

        if (PlanarDistance(transform.position, HomePosition) > leashRadius) { BeginReturn("leash radius exceeded"); return; }

        float dist = PlanarDistance(transform.position, target.transform.position);
        if (dist <= AttackRange)
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

        float dist = PlanarDistance(transform.position, target.transform.position);
        if (dist > AttackRange * 1.25f) { ChangeState(NpcState.Chasing, "target moved out of range"); return; }

        FaceTowards(target.transform.position, dt);

        if (Time.time >= _nextAttackTime)
        {
            _nextAttackTime = Time.time + AttackCooldown;
            Debug.Log($"[EnemyNPC] {name} attacks {target.name} (dmg {AttackDamage}, cooldown {AttackCooldown}s).");
            RPC_PlayAttackFx();
            target.RPC_ReceiveNpcDamage(Object.Id);
        }
    }

    private void TickReturning(float dt)
    {
        if (MoveTowards(HomePosition, returnSpeed, dt) <= 0.3f)
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
            float d = PlanarDistance(transform.position, p.transform.position);
            if (d <= bestDist) { bestDist = d; best = p; }
        }
        if (best == null) return false;

        Debug.Log($"[EnemyNPC] {name} aggro: {best.name} entered the zone at {bestDist:0.0}m (radius {aggroRadius}).");
        SetTarget(best);
        ChangeState(NpcState.Chasing, "player entered aggro zone");
        BroadcastCampAggro(best);
        return true;
    }

    /// <summary>
    /// Alerts every member of this spawned camp. Each member receives an RPC addressed to
    /// its own State Authority, so the behaviour remains correct after Shared Mode authority
    /// hand-off instead of assuming all NPCs are owned by the original master client.
    /// </summary>
    private void BroadcastCampAggro(PlayerCombat target)
    {
        if (target == null || !target.IsSpawned || CampId <= 0) return;

        int alerted = 0;
        for (int i = 0; i < All.Count; i++)
        {
            EnemyNPC ally = All[i];
            if (ally == null || !ally.IsSpawned || !ally.IsAlive || ally == this || ally.CampId != CampId) continue;
            ally.RPC_JoinCampAggro(target.Object.Id);
            alerted++;
        }
        Debug.Log($"[EnemyNPC] {name} alerted {alerted} ally/allies in camp {CampId} against {target.name}.");
    }

    /// <summary>Routes a camp alert to this NPC's State Authority.</summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_JoinCampAggro(NetworkId targetId)
    {
        if (!IsSpawned || !Object.HasStateAuthority || !IsAlive || CampId <= 0) return;
        if (State == NpcState.Returning || State == NpcState.Dead) return;

        PlayerCombat target = FindPlayerByNetworkId(targetId);
        if (target == null || target.IsDead)
        {
            Debug.LogWarning($"[EnemyNPC] {name} ignored camp aggro: target {targetId} was unavailable.");
            return;
        }

        SetTarget(target);
        if (State == NpcState.Idle || State == NpcState.Patrol)
            ChangeState(NpcState.Chasing, "camp ally aggro");
        Debug.Log($"[EnemyNPC] {name} joined camp {CampId} aggro on {target.name}.");
    }

    private void BeginReturn(string reason)
    {
        TargetId = default;
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

    // ------------------------------------------------------------------ steering

    /// <summary>Moves on the XZ plane; returns the remaining planar distance.</summary>
    private float MoveTowards(Vector3 destination, float speed, float dt)
    {
        Vector3 pos = transform.position;
        Vector3 delta = destination - pos;
        delta.y = 0f;
        float dist = delta.magnitude;
        if (dist < 0.0001f) return 0f;

        Vector3 dir = delta / dist;
        float step = Mathf.Min(speed * dt, dist);
        transform.position = pos + dir * step;
        FaceDirection(dir, dt);
        return dist - step;
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

    /// <summary>Any peer -> State Authority. The attacker identity comes from Fusion (RpcInfo), never from a parameter.</summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_TakeDamage(int damage, RpcInfo info = default)
    {
        if (!IsSpawned || !Object.HasStateAuthority) return;
        if (!IsAlive) return;

        PlayerCombat attacker = FindPlayerBySource(info.Source);
        if (attacker == null)
        {
            Debug.LogWarning($"[EnemyNPC] {name}: damage from {info.Source} rejected (no player object for that peer).");
            return;
        }
        if (attacker.IsDead) { Debug.LogWarning($"[EnemyNPC] {name}: damage rejected, attacker is dead."); return; }

        float dist = PlanarDistance(transform.position, attacker.transform.position);
        if (dist > maxHitDistance)
        {
            Debug.LogWarning($"[EnemyNPC] {name}: damage rejected, attacker {dist:0.0}m away (max {maxHitDistance}).");
            return;
        }

        float now = Time.time;
        if (_lastDamageTime.TryGetValue(info.Source, out float last) && now - last < minSecondsBetweenHitsPerPlayer)
        {
            Debug.LogWarning($"[EnemyNPC] {name}: damage rejected, {info.Source} hitting too fast.");
            return;
        }
        _lastDamageTime[info.Source] = now;

        int applied = Mathf.Clamp(damage, 1, maxDamagePerHit);
        if (applied != damage) Debug.LogWarning($"[EnemyNPC] {name}: damage {damage} clamped to {applied}.");

        int before = CurrentHealth;
        CurrentHealth = Mathf.Max(0, before - applied);
        Debug.Log($"[EnemyNPC] {name} takes {applied} from {attacker.name} ({info.Source}): {before} -> {CurrentHealth}/{MaxHealth}.");

        RPC_HitFeedback(applied, info.Source);

        if (CurrentHealth <= 0)
        {
            Die(info.Source);
            return;
        }

        // Hostility: retaliate against whoever hit us (unless we are in the middle of resetting).
        if (State == NpcState.Returning)
        {
            Debug.Log($"[EnemyNPC] {name} is returning home and ignores the hit.");
            return;
        }
        if (!TargetId.IsValid)
        {
            SetTarget(attacker);
            Debug.Log($"[EnemyNPC] {name} aggro: attacked by {attacker.name}.");
        }
        if (State == NpcState.Idle || State == NpcState.Patrol) ChangeState(NpcState.Chasing, "attacked");
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
        _deathTime = Time.time;
        ChangeState(NpcState.Dead, $"killed by {killer}");
        SpawnLoot();
        RPC_Died(killer, experienceReward);
    }

    private void SpawnLoot()
    {
        if (lootTable == null || lootTable.Length == 0) return;
        if (lootPrefab == null)
        {
            Debug.LogWarning($"[EnemyNPC] {name} has a loot table but no lootPrefab assigned - no drops.");
            return;
        }

        foreach (var entry in lootTable)
        {
            if (entry == null || entry.item == null) continue;
            if (UnityEngine.Random.value > entry.chance) continue;

            int qty = UnityEngine.Random.Range(entry.minAmount, Mathf.Max(entry.minAmount, entry.maxAmount) + 1);
            Vector2 o = UnityEngine.Random.insideUnitCircle * 1.2f;
            Vector3 pos = new Vector3(transform.position.x + o.x, 0.4f, transform.position.z + o.y);

            var drop = WorldItem.Spawn(Runner, lootPrefab, pos, entry.item, qty);
            Debug.Log($"[EnemyNPC] {name} dropped {qty}x {entry.item.displayName} ({(drop != null ? "spawned" : "SPAWN FAILED")}).");
        }
    }

    // ------------------------------------------------------------------ feedback (all peers)

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_HitFeedback(int damage, PlayerRef attacker)
    {
        if (animator != null) animator.SetTrigger(HitTrigger);
        onHit?.Invoke();
        if (!_flashing && gameObject.activeInHierarchy) StartCoroutine(FlashRoutine());
        CombatHud.NotifyEnemyDamaged(this, damage, attacker);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_PlayAttackFx()
    {
        if (animator != null) animator.SetTrigger(AttackTrigger);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_Died(PlayerRef killer, int exp)
    {
        Debug.Log($"[EnemyNPC] {name} died (killer {killer}).");
        if (animator != null) animator.SetTrigger(DieTrigger);
        onDeath?.Invoke();

        // Only the killer's peer receives the experience.
        if (Runner != null && Runner.LocalPlayer == killer)
        {
            if (InventoryManager.Local != null) InventoryManager.Local.AddExperience(exp);
            else Debug.LogWarning("[EnemyNPC] Killer has no local InventoryManager - experience not granted.");
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
        switch (State)
        {
            case NpcState.Chasing: c = new Color(1f, 0.15f, 0.1f); break;
            case NpcState.Attacking: c = new Color(1f, 0.5f, 0f); break;
            case NpcState.Returning: c = new Color(0.85f, 0.85f, 0.2f); break;
            case NpcState.Dead: c = new Color(0.25f, 0.25f, 0.25f); break;
            default: c = new Color(0.6f, 0.2f, 0.2f); break;
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
        if (!IsSpawned || Event.current.type != EventType.Repaint) return;
        var cam = Camera.main;
        if (cam == null) return;

        Vector3 world = transform.position + Vector3.up * 1.6f;
        Vector3 sp = cam.WorldToScreenPoint(world);
        if (sp.z <= 0f || sp.z > 35f) return;

        float y = Screen.height - sp.y;
        const float w = 70f;
        float frac = MaxHealth > 0 ? Mathf.Clamp01(CurrentHealth / (float)MaxHealth) : 0f;

        var prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.7f);
        GUI.DrawTexture(new Rect(sp.x - w * 0.5f - 1f, y - 1f, w + 2f, 8f), Texture2D.whiteTexture);
        GUI.color = Color.Lerp(Color.red, Color.green, frac);
        GUI.DrawTexture(new Rect(sp.x - w * 0.5f, y, w * frac, 6f), Texture2D.whiteTexture);
        GUI.color = prev;
        GUI.Label(new Rect(sp.x - 60f, y - 20f, 120f, 20f), $"{name}  {State}");
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, aggroRadius);
        Gizmos.color = new Color(1f, 0.8f, 0f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, AttackRange);
    }
}
