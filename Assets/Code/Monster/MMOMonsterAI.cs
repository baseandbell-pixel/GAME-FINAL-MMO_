using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Full MMORPG Monster AI, Territory Zone (Aggro & Leash), Combat, and Overhead Health Bar controller.
/// Works seamlessly with MonsterAnimatorBridge (AC_Monster_Base) and Targetable (CapsLock lock-on).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(MonsterAnimatorBridge))]
public sealed class MMOMonsterAI : MonoBehaviour
{
    public enum AIState
    {
        IdlePatrol = 0,
        AggroChase = 1,
        Attacking = 2,
        EvadeReturn = 3,
        Dead = 4
    }

    private static readonly List<MMOMonsterAI> ActiveMonsters = new List<MMOMonsterAI>();
    public static IReadOnlyList<MMOMonsterAI> All => ActiveMonsters;

    // =========================================================
    // INSPECTOR SETTINGS
    // =========================================================

    [Header("Monster Identity & Stats")]
    public string displayName = "Ancient Warrior";
    [Min(1)] public int level = 5;
    [Min(10)] public int maxHealth = 1000;
    [Min(1)] public int minAttackDamage = 45;
    [Min(1)] public int maxAttackDamage = 70;
    [Range(0f, 1f)] public float monsterCritChance = 0.15f;
    [Min(10)] public int expReward = 250;

    [Header("Territory & Aggro Zone (เขตของมอนสเตอร์)")]
    [Tooltip("Radius where the monster detects and aggros onto the player automatically.")]
    [Min(2f)] public float aggroRadius = 7.5f;

    [Tooltip("Maximum territory radius from spawn point. Leaving this radius triggers EVADE (Leash reset).")]
    [Min(4f)] public float territoryRadius = 14.0f;

    [Tooltip("Radius around the territory center where the monster casually patrols when idle.")]
    [Min(1f)] public float patrolRadius = 3.8f;

    [Tooltip("Show the glowing MMO Territory & Aggro rings on the ground.")]
    public bool showTerritoryRings = true;

    [Header("Movement & Combat Timing")]
    [Min(0.5f)] public float patrolWalkSpeed = 1.5f;
    [Min(1.0f)] public float chaseRunSpeed = 3.8f;
    [Min(1.0f)] public float evadeRunSpeed = 5.2f;
    [Min(1.2f)] public float attackRange = 2.35f;
    [Min(0.5f)] public float attackCooldown = 2.0f;
    [Range(0.1f, 1.2f)] public float attackHitDelay = 0.45f;
    [Min(1.0f)] public float respawnDelay = 5.5f;

    // =========================================================
    // RUNTIME STATE
    // =========================================================

    private MonsterAnimatorBridge _bridge;
    private CharacterController _characterController;
    private CapsuleCollider _capsuleCollider;
    private Renderer[] _renderers;

    private Vector3 _territoryCenter;
    private Quaternion _spawnRotation;
    private int _currentHealth;
    private float _displayedHealthRatio = 1f;
    private AIState _state = AIState.IdlePatrol;

    private Transform _targetPlayer;
    private Vector3 _patrolPoint;
    private float _patrolWaitTimer;
    private float _nextAttackAllowedTime;
    private float _stunRemainingTimer;
    private Coroutine _activeAttackCoroutine;
    private Coroutine _hitFlashCoroutine;

    // Ground Territory Visuals
    private GameObject _territoryRoot;
    private LineRenderer _aggroRingRenderer;
    private LineRenderer _leashRingRenderer;
    private Material _ringMaterial;

    // Overhead UI
    private Transform _overheadRoot;
    private TextMeshPro _nameLabel;
    private Transform _hpFillBar;
    private MeshRenderer _hpFillRenderer;

    // =========================================================
    // PUBLIC PROPERTIES
    // =========================================================

    public AIState CurrentState => _state;
    public int CurrentHealth => _currentHealth;
    public int MaxHealth => maxHealth;
    public float HealthRatio => maxHealth > 0 ? Mathf.Clamp01((float)_currentHealth / maxHealth) : 0f;
    public bool IsDead => _state == AIState.Dead;
    public bool IsEvading => _state == AIState.EvadeReturn;
    public Vector3 TerritoryCenter => _territoryCenter;
    public MonsterAnimatorBridge Bridge => _bridge;

    // =========================================================
    // UNITY LIFECYCLE
    // =========================================================

    private void OnEnable()
    {
        if (!ActiveMonsters.Contains(this))
        {
            ActiveMonsters.Add(this);
        }
    }

    private void OnDisable()
    {
        ActiveMonsters.Remove(this);
    }

    private void OnDestroy()
    {
        if (_territoryRoot != null)
        {
            Destroy(_territoryRoot);
        }
    }

    private void Awake()
    {
        EnsureReferencesInitialized();
    }

    public void EnsureReferencesInitialized()
    {
        if (_bridge == null) _bridge = GetComponent<MonsterAnimatorBridge>();
        if (_capsuleCollider == null) _capsuleCollider = GetComponent<CapsuleCollider>();
        if (_characterController == null) _characterController = GetComponent<CharacterController>();
        if (_renderers == null || _renderers.Length == 0) _renderers = GetComponentsInChildren<Renderer>(true);

        if (_currentHealth <= 0 && _state != AIState.Dead)
        {
            _territoryCenter = transform.position;
            _spawnRotation = transform.rotation;
            _currentHealth = maxHealth;
            _displayedHealthRatio = 1f;
        }

        if (_bridge != null && _bridge.Config != null && !string.IsNullOrEmpty(_bridge.Config.monsterName))
        {
            if (string.IsNullOrEmpty(displayName) || displayName == "Enemy")
            {
                displayName = _bridge.Config.monsterName;
            }
        }
    }

    private void Start()
    {
        SnapToGround();
        _territoryCenter = transform.position;
        PickNewPatrolPoint();
        _patrolWaitTimer = Random.Range(1.0f, 2.5f);

        if (showTerritoryRings)
        {
            CreateTerritoryRings();
        }

        CreateOverheadHealthBar();

        if (_bridge != null)
        {
            _bridge.OnAttackHitEvent += OnAnimationAttackHit;
        }
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        if (_stunRemainingTimer > 0f && _state != AIState.Dead)
        {
            _stunRemainingTimer -= dt;
            _bridge.SetWorldSpeed(0f);
            if (_stunRemainingTimer <= 0f)
            {
                _bridge.SetStunned(false);
            }
            UpdateOverheadUI(dt);
            UpdateTerritoryRingsVisual(dt);
            return;
        }

        switch (_state)
        {
            case AIState.IdlePatrol:
                UpdateIdlePatrol(dt);
                break;

            case AIState.AggroChase:
                UpdateAggroChase(dt);
                break;

            case AIState.Attacking:
                UpdateAttacking(dt);
                break;

            case AIState.EvadeReturn:
                UpdateEvadeReturn(dt);
                break;

            case AIState.Dead:
                _bridge.SetWorldSpeed(0f);
                break;
        }

        UpdateOverheadUI(dt);
        UpdateTerritoryRingsVisual(dt);
    }

    // =========================================================
    // AI STATE LOGIC
    // =========================================================

    private void UpdateIdlePatrol(float dt)
    {
        // 1. Check if any player entered the Aggro Territory Zone!
        Transform detectedPlayer = FindNearestPlayerInAggroZone();
        if (detectedPlayer != null)
        {
            EnterAggroState(detectedPlayer);
            return;
        }

        // 2. Casual patrol inside territory
        if (_patrolWaitTimer > 0f)
        {
            _patrolWaitTimer -= dt;
            _bridge.SetWorldSpeed(0f);
            return;
        }

        Vector3 toPatrol = _patrolPoint - transform.position;
        toPatrol.y = 0f;

        if (toPatrol.magnitude <= 0.35f)
        {
            _bridge.SetWorldSpeed(0f);
            _patrolWaitTimer = Random.Range(2.2f, 4.2f);
            PickNewPatrolPoint();
            return;
        }

        MoveMonsterTowards(_patrolPoint, patrolWalkSpeed, dt);
    }

    private void UpdateAggroChase(float dt)
    {
        if (!IsTargetPlayerValid())
        {
            EnterEvadeState();
            return;
        }

        // Check Territory Leash boundary
        float distFromHome = PlanarDistance(transform.position, _territoryCenter);
        float playerDistFromHome = PlanarDistance(_targetPlayer.position, _territoryCenter);

        if (distFromHome > territoryRadius || playerDistFromHome > territoryRadius + 2.5f)
        {
            EnterEvadeState();
            return;
        }

        float distToPlayer = PlanarDistance(transform.position, _targetPlayer.position);
        if (distToPlayer <= attackRange)
        {
            _state = AIState.Attacking;
            _bridge.SetWorldSpeed(0f);
            return;
        }

        MoveMonsterTowards(_targetPlayer.position, chaseRunSpeed, dt);
    }

    private void UpdateAttacking(float dt)
    {
        if (!IsTargetPlayerValid())
        {
            EnterEvadeState();
            return;
        }

        float distFromHome = PlanarDistance(transform.position, _territoryCenter);
        if (distFromHome > territoryRadius)
        {
            EnterEvadeState();
            return;
        }

        float distToPlayer = PlanarDistance(transform.position, _targetPlayer.position);

        // Smoothly face the player
        RotateTowards(_targetPlayer.position, 10f, dt);
        _bridge.SetWorldSpeed(0f);

        // If player ran out of melee range and we aren't mid-swing, resume chasing
        if (distToPlayer > attackRange + 0.55f && !_bridge.IsActionInProgress)
        {
            _state = AIState.AggroChase;
            return;
        }

        // Execute attack when cooldown is ready
        if (Time.time >= _nextAttackAllowedTime)
        {
            _nextAttackAllowedTime = Time.time + attackCooldown;
            _bridge.TriggerRandomAttack();

            if (_activeAttackCoroutine != null)
            {
                StopCoroutine(_activeAttackCoroutine);
            }
            _activeAttackCoroutine = StartCoroutine(DeliverAttackDamageRoutine());
        }
    }

    private void UpdateEvadeReturn(float dt)
    {
        float distToHome = PlanarDistance(transform.position, _territoryCenter);
        if (distToHome <= 0.45f)
        {
            transform.position = new Vector3(_territoryCenter.x, transform.position.y, _territoryCenter.z);
            _bridge.SetWorldSpeed(0f);
            _bridge.SetEvading(false);
            _bridge.SetCombat(false);
            _currentHealth = maxHealth;
            _state = AIState.IdlePatrol;
            _patrolWaitTimer = 1.5f;
            PickNewPatrolPoint();
            return;
        }

        MoveMonsterTowards(_territoryCenter, evadeRunSpeed, dt);
    }

    // =========================================================
    // STATE TRANSITIONS
    // =========================================================

    public void EnterAggroState(Transform playerTransform)
    {
        EnsureReferencesInitialized();
        if (_state == AIState.Dead || _state == AIState.EvadeReturn || playerTransform == null)
        {
            return;
        }

        bool wasIdle = _state == AIState.IdlePatrol;
        _targetPlayer = playerTransform;
        _state = AIState.AggroChase;
        if (_bridge != null && Application.isPlaying)
        {
            _bridge.SetCombat(true);
        }

        if (wasIdle && Application.isPlaying)
        {
            MMOCombatVFX.SpawnStatusPopup(
                transform.position + Vector3.up * 2.4f,
                "! AGGRO !",
                new Color(1f, 0.25f, 0.15f, 1f),
                scale: 1.15f);

            MMOCombatVFX.SpawnAggroPulse(transform.position);
        }
    }

    public void EnterEvadeState()
    {
        EnsureReferencesInitialized();
        if (_state == AIState.Dead || _state == AIState.EvadeReturn)
        {
            return;
        }

        if (_activeAttackCoroutine != null && Application.isPlaying)
        {
            StopCoroutine(_activeAttackCoroutine);
            _activeAttackCoroutine = null;
        }

        _targetPlayer = null;
        _state = AIState.EvadeReturn;
        _currentHealth = maxHealth;
        if (_bridge != null && Application.isPlaying)
        {
            _bridge.SetEvading(true);
        }

        if (Application.isPlaying)
        {
            MMOCombatVFX.SpawnStatusPopup(
                transform.position + Vector3.up * 2.4f,
                "EVADE (Reset)",
                new Color(0.25f, 0.9f, 1f, 1f),
                scale: 1.0f);
        }
    }

    // =========================================================
    // COMBAT: RECEIVING & DEALING DAMAGE
    // =========================================================

    /// <summary>
    /// Applies damage from a player attack, spawns Floating Damage / Critical Hit numbers & VFX,
    /// triggers Hit_React flinch (if allowed), or kills the monster.
    /// </summary>
    public bool TakeDamage(int amount, bool isCritical, Transform attacker, Vector3 hitWorldPoint)
    {
        EnsureReferencesInitialized();
        if (_state == AIState.Dead)
        {
            return false;
        }

        if (_state == AIState.EvadeReturn)
        {
            if (Application.isPlaying)
            {
                MMOCombatVFX.SpawnStatusPopup(hitWorldPoint, "EVADING", new Color(0.4f, 0.9f, 1f), 0.85f);
            }
            return false;
        }

        int finalDamage = Mathf.Max(1, amount);
        _currentHealth = Mathf.Max(0, _currentHealth - finalDamage);

        if (Application.isPlaying)
        {
            // Spawn Hit Sparks & Floating Damage Popup
            MMOCombatVFX.SpawnHitEffect(hitWorldPoint, isCritical);
            MMOCombatVFX.SpawnDamagePopup(hitWorldPoint + Vector3.up * 0.35f, finalDamage, isCritical, isPlayerDamage: false);

            // Flash monster material briefly
            TriggerHitMaterialFlash(isCritical);
        }

        // Aggro onto the attacker if not already in combat
        if (_state == AIState.IdlePatrol && attacker != null)
        {
            float attackerDistFromHome = PlanarDistance(attacker.position, _territoryCenter);
            if (attackerDistFromHome <= territoryRadius + 1.5f)
            {
                EnterAggroState(attacker);
            }
            else
            {
                EnterEvadeState();
                return true;
            }
        }

        // Check for Death
        if (_currentHealth <= 0)
        {
            Die(attacker);
            return true;
        }

        // Trigger Hit_React animation (respects Boss Super Armor via MonsterAnimationConfig.canFlinch)
        if (_bridge != null && Application.isPlaying)
        {
            bool flinched = _bridge.TriggerHit();
            if (flinched)
            {
                _nextAttackAllowedTime = Mathf.Max(_nextAttackAllowedTime, Time.time + 0.65f);
            }
        }

        return true;
    }

    /// <summary>
    /// Stuns the monster for the given duration (unless it is a Boss with Super Armor).
    /// </summary>
    public bool ApplyStun(float duration)
    {
        if (_state == AIState.Dead || _state == AIState.EvadeReturn)
        {
            return false;
        }

        if (_bridge != null && _bridge.Config != null && _bridge.Config.isBossOrElite)
        {
            MMOCombatVFX.SpawnStatusPopup(
                transform.position + Vector3.up * 2.3f,
                "RESIST (BOSS)",
                new Color(1f, 0.65f, 0.15f),
                0.9f);
            return false;
        }

        if (_activeAttackCoroutine != null)
        {
            StopCoroutine(_activeAttackCoroutine);
            _activeAttackCoroutine = null;
        }

        _stunRemainingTimer = Mathf.Max(_stunRemainingTimer, duration);
        _bridge.SetStunned(true);

        MMOCombatVFX.SpawnStatusPopup(
            transform.position + Vector3.up * 2.3f,
            "STUNNED!",
            new Color(1f, 0.92f, 0.2f),
            1.05f);

        return true;
    }

    private IEnumerator DeliverAttackDamageRoutine()
    {
        yield return new WaitForSeconds(attackHitDelay);

        if (_state != AIState.Dead && _state != AIState.EvadeReturn && _stunRemainingTimer <= 0f)
        {
            TryDamageTargetPlayer();
        }
        _activeAttackCoroutine = null;
    }

    private void OnAnimationAttackHit()
    {
        // If the FBX clip also has an embedded OnAttackHit AnimationEvent, avoid double-hitting
        if (_activeAttackCoroutine != null)
        {
            StopCoroutine(_activeAttackCoroutine);
            _activeAttackCoroutine = null;
        }
        TryDamageTargetPlayer();
    }

    private void TryDamageTargetPlayer()
    {
        if (!IsTargetPlayerValid())
        {
            return;
        }

        float dist = PlanarDistance(transform.position, _targetPlayer.position);
        if (dist > attackRange + 0.85f)
        {
            return;
        }

        MMOPlayerCombat playerCombat = _targetPlayer.GetComponent<MMOPlayerCombat>();
        if (playerCombat != null)
        {
            bool isCrit = Random.value < monsterCritChance;
            int dmg = Random.Range(minAttackDamage, maxAttackDamage + 1);
            if (isCrit)
            {
                dmg = Mathf.RoundToInt(dmg * 1.6f);
            }

            playerCombat.TakeDamageFromMonster(dmg, isCrit, this);
        }
    }

    private void Die(Transform killer)
    {
        _state = AIState.Dead;
        _stunRemainingTimer = 0f;

        if (_activeAttackCoroutine != null && Application.isPlaying)
        {
            StopCoroutine(_activeAttackCoroutine);
            _activeAttackCoroutine = null;
        }

        if (_bridge != null && Application.isPlaying)
        {
            _bridge.SetDead(true);
        }

        if (_capsuleCollider != null)
        {
            _capsuleCollider.enabled = false;
        }

        if (Application.isPlaying)
        {
            MMOCombatVFX.SpawnStatusPopup(
                transform.position + Vector3.up * 2.2f,
                $"SLAIN! +{expReward} EXP",
                new Color(1f, 0.88f, 0.22f),
                1.15f);

            if (killer != null)
            {
                MMOPlayerCombat pc = killer.GetComponent<MMOPlayerCombat>();
                if (pc != null)
                {
                    pc.AwardKillExp(expReward, this);
                }
            }

            StartCoroutine(RespawnRoutine());
        }
    }

    private IEnumerator RespawnRoutine()
    {
        yield return new WaitForSeconds(respawnDelay);

        // Respawn at Territory Center
        transform.SetPositionAndRotation(_territoryCenter, _spawnRotation);
        SnapToGround();

        _currentHealth = maxHealth;
        _displayedHealthRatio = 1f;
        _targetPlayer = null;
        _state = AIState.IdlePatrol;
        _patrolWaitTimer = 1.5f;
        PickNewPatrolPoint();

        if (_capsuleCollider != null)
        {
            _capsuleCollider.enabled = true;
        }

        if (_bridge != null)
        {
            _bridge.ResetForPoolSpawn();
        }

        MMOCombatVFX.SpawnRespawnEffect(transform.position);
        MMOCombatVFX.SpawnStatusPopup(
            transform.position + Vector3.up * 2.2f,
            "RESPAWNED",
            new Color(0.3f, 1f, 0.65f),
            0.95f);
    }

    // =========================================================
    // MOVEMENT & DETECTION HELPERS
    // =========================================================

    private void MoveMonsterTowards(Vector3 targetWorldPos, float speed, float dt)
    {
        Vector3 delta = targetWorldPos - transform.position;
        delta.y = 0f;

        if (delta.sqrMagnitude < 0.001f)
        {
            _bridge.SetWorldSpeed(0f);
            return;
        }

        Vector3 dir = delta.normalized;
        RotateTowards(targetWorldPos, 9f, dt);

        Vector3 step = dir * (speed * dt);
        if (_characterController != null && _characterController.enabled)
        {
            _characterController.Move(step + Vector3.down * (9.81f * dt));
        }
        else
        {
            transform.position += step;
            SnapToGround();
        }

        _bridge.SetWorldSpeed(speed);
    }

    private void RotateTowards(Vector3 lookAtPoint, float turnSpeed, float dt)
    {
        Vector3 dir = lookAtPoint - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f)
        {
            return;
        }

        Quaternion targetRot = Quaternion.LookRotation(dir.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, turnSpeed * dt);
    }

    private void SnapToGround()
    {
        Vector3 rayOrigin = transform.position + Vector3.up * 3.0f;
        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 10f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (!hit.transform.IsChildOf(transform))
            {
                Vector3 pos = transform.position;
                pos.y = hit.point.y;
                transform.position = pos;
            }
        }
    }

    private void PickNewPatrolPoint()
    {
        Vector2 circle = Random.insideUnitCircle * patrolRadius;
        _patrolPoint = _territoryCenter + new Vector3(circle.x, 0f, circle.y);
    }

    private Transform FindNearestPlayerInAggroZone()
    {
        MMOPlayerController[] players = FindObjectsByType<MMOPlayerController>();
        Transform best = null;
        float bestDist = aggroRadius;

        for (int i = 0; i < players.Length; i++)
        {
            MMOPlayerController p = players[i];
            if (p == null || !p.gameObject.activeInHierarchy)
            {
                continue;
            }

            float distToMonster = PlanarDistance(transform.position, p.transform.position);
            float distToHome = PlanarDistance(_territoryCenter, p.transform.position);

            if (distToMonster <= bestDist && distToHome <= territoryRadius)
            {
                bestDist = distToMonster;
                best = p.transform;
            }
        }

        return best;
    }

    private bool IsTargetPlayerValid()
    {
        return _targetPlayer != null && _targetPlayer.gameObject.activeInHierarchy;
    }

    private static float PlanarDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    // =========================================================
    // TERRITORY RINGS (VISUAL GROUND BOUNDARY)
    // =========================================================

    private void CreateTerritoryRings()
    {
        if (_territoryRoot != null)
        {
            Destroy(_territoryRoot);
        }

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        _ringMaterial = new Material(shader);

        _territoryRoot = new GameObject($"{gameObject.name}_TerritoryZone");
        _territoryRoot.transform.position = _territoryCenter + Vector3.up * 0.05f;

        _aggroRingRenderer = CreateRingLineRenderer(
            _territoryRoot.transform,
            "AggroZone_Ring",
            aggroRadius,
            width: 0.07f,
            segments: 64);

        _leashRingRenderer = CreateRingLineRenderer(
            _territoryRoot.transform,
            "TerritoryLeash_Ring",
            territoryRadius,
            width: 0.09f,
            segments: 80);
    }

    private LineRenderer CreateRingLineRenderer(
        Transform parent,
        string ringName,
        float radius,
        float width,
        int segments)
    {
        GameObject go = new GameObject(ringName);
        go.transform.SetParent(parent, false);

        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.loop = true;
        lr.positionCount = segments;
        lr.startWidth = width;
        lr.endWidth = width;
        lr.sharedMaterial = _ringMaterial;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;

        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            lr.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
        }

        return lr;
    }

    private void UpdateTerritoryRingsVisual(float dt)
    {
        if (_territoryRoot == null)
        {
            return;
        }

        bool ringsActive = _state != AIState.Dead;
        if (_territoryRoot.activeSelf != ringsActive)
        {
            _territoryRoot.SetActive(ringsActive);
        }

        if (!ringsActive)
        {
            return;
        }

        float pulse = 0.55f + 0.25f * Mathf.Sin(Time.time * 3.5f);

        Color aggroColor;
        Color leashColor;

        switch (_state)
        {
            case AIState.AggroChase:
            case AIState.Attacking:
                aggroColor = new Color(1f, 0.18f, 0.12f, pulse);
                leashColor = new Color(1f, 0.55f, 0.12f, 0.45f);
                break;

            case AIState.EvadeReturn:
                aggroColor = new Color(0.2f, 0.85f, 1f, 0.5f);
                leashColor = new Color(0.2f, 0.85f, 1f, 0.65f);
                break;

            default:
                aggroColor = new Color(1f, 0.78f, 0.2f, 0.38f * pulse);
                leashColor = new Color(0.25f, 0.85f, 0.95f, 0.25f);
                break;
        }

        if (_aggroRingRenderer != null)
        {
            _aggroRingRenderer.startColor = aggroColor;
            _aggroRingRenderer.endColor = aggroColor;
        }

        if (_leashRingRenderer != null)
        {
            _leashRingRenderer.startColor = leashColor;
            _leashRingRenderer.endColor = leashColor;
        }
    }

    // =========================================================
    // OVERHEAD WORLD HEALTH BAR & HIT FLASH
    // =========================================================

    private void CreateOverheadHealthBar()
    {
        GameObject root = new GameObject("MonsterOverheadUI");
        root.transform.SetParent(transform, false);
        root.transform.localPosition = new Vector3(0f, 2.35f, 0f);
        _overheadRoot = root.transform;

        // Nameplate TextMeshPro
        GameObject textGo = new GameObject("NameLabel");
        textGo.transform.SetParent(_overheadRoot, false);
        textGo.transform.localPosition = new Vector3(0f, 0.28f, 0f);

        _nameLabel = textGo.AddComponent<TextMeshPro>();
        _nameLabel.alignment = TextAlignmentOptions.Center;
        _nameLabel.fontSize = 2.8f;
        _nameLabel.fontStyle = FontStyles.Bold;
        _nameLabel.textWrappingMode = TextWrappingModes.NoWrap;

        // Health Bar Background
        GameObject bgQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        bgQuad.name = "HP_BG";
        Destroy(bgQuad.GetComponent<Collider>());
        bgQuad.transform.SetParent(_overheadRoot, false);
        bgQuad.transform.localPosition = Vector3.zero;
        bgQuad.transform.localScale = new Vector3(1.5f, 0.16f, 1f);

        Shader unlitShader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
        Material bgMat = new Material(unlitShader) { color = new Color(0.08f, 0.08f, 0.1f, 0.88f) };
        bgQuad.GetComponent<MeshRenderer>().sharedMaterial = bgMat;

        // Health Bar Fill Pivot (Left-aligned)
        GameObject fillPivot = new GameObject("HP_FillPivot");
        fillPivot.transform.SetParent(_overheadRoot, false);
        fillPivot.transform.localPosition = new Vector3(-0.72f, 0f, -0.01f);
        _hpFillBar = fillPivot.transform;

        GameObject fillQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        fillQuad.name = "HP_Fill";
        Destroy(fillQuad.GetComponent<Collider>());
        fillQuad.transform.SetParent(_hpFillBar, false);
        fillQuad.transform.localPosition = new Vector3(0.72f, 0f, 0f);
        fillQuad.transform.localScale = new Vector3(1.44f, 0.12f, 1f);

        _hpFillRenderer = fillQuad.GetComponent<MeshRenderer>();
        _hpFillRenderer.sharedMaterial = new Material(unlitShader)
        {
            color = new Color(0.92f, 0.20f, 0.18f, 1f)
        };
    }

    private void UpdateOverheadUI(float dt)
    {
        if (_overheadRoot == null)
        {
            return;
        }

        Camera cam = Camera.main;
        if (cam != null)
        {
            _overheadRoot.rotation = cam.transform.rotation;
        }

        _displayedHealthRatio = Mathf.Lerp(_displayedHealthRatio, HealthRatio, 12f * dt);
        if (_hpFillBar != null)
        {
            _hpFillBar.localScale = new Vector3(Mathf.Max(0.001f, _displayedHealthRatio), 1f, 1f);
        }

        if (_nameLabel != null)
        {
            bool isBoss = _bridge != null && _bridge.Config != null && _bridge.Config.isBossOrElite;
            string bossPrefix = isBoss ? "<color=#FFD700>[BOSS]</color> " : "";
            string stateBadge = _state switch
            {
                AIState.AggroChase => " <color=#FF4433>[AGGRO!]</color>",
                AIState.Attacking => " <color=#FF2222>[COMBAT]</color>",
                AIState.EvadeReturn => " <color=#44DDFF>[EVADE]</color>",
                AIState.Dead => " <color=#888888>[DEAD]</color>",
                _ => ""
            };

            if (_stunRemainingTimer > 0f && _state != AIState.Dead)
            {
                stateBadge = " <color=#FFEE33>[STUNNED]</color>";
            }

            _nameLabel.text = $"{bossPrefix}Lv.{level} {displayName}{stateBadge}\n<size=72%>{_currentHealth}/{maxHealth}</size>";
        }
    }

    private void TriggerHitMaterialFlash(bool isCrit)
    {
        if (!Application.isPlaying) return;
        EnsureReferencesInitialized();
        if (_renderers == null || _renderers.Length == 0) return;

        if (_hitFlashCoroutine != null)
        {
            StopCoroutine(_hitFlashCoroutine);
        }
        _hitFlashCoroutine = StartCoroutine(HitFlashRoutine(isCrit));
    }

    private IEnumerator HitFlashRoutine(bool isCrit)
    {
        Color flashColor = isCrit
            ? new Color(1f, 0.65f, 0.2f, 1f)
            : new Color(1f, 0.35f, 0.35f, 1f);

        MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        for (int i = 0; i < _renderers.Length; i++)
        {
            Renderer r = _renderers[i];
            if (r == null || r is ParticleSystemRenderer || r.GetComponent<TextMeshPro>() != null)
            {
                continue;
            }
            r.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", flashColor);
            mpb.SetColor("_Color", flashColor);
            r.SetPropertyBlock(mpb);
        }

        yield return new WaitForSeconds(0.11f);

        Targetable targetable = GetComponent<Targetable>();
        bool isCameraLocked =
            MMOCameraController.Instance != null &&
            MMOCameraController.Instance.LockedTarget == targetable;

        if (isCameraLocked && targetable != null)
        {
            targetable.SetCameraLocked(true);
        }
        else
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer r = _renderers[i];
                if (r == null || r is ParticleSystemRenderer || r.GetComponent<TextMeshPro>() != null)
                {
                    continue;
                }
                r.SetPropertyBlock(null);
            }
        }

        _hitFlashCoroutine = null;
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 center = Application.isPlaying ? _territoryCenter : transform.position;

        Gizmos.color = new Color(1f, 0.25f, 0.15f, 0.85f);
        Gizmos.DrawWireSphere(center, aggroRadius);

        Gizmos.color = new Color(0.25f, 0.85f, 1f, 0.75f);
        Gizmos.DrawWireSphere(center, territoryRadius);
    }
}
