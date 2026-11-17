using System;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// High-performance runtime bridge between Monster AI / Network state and the AC_Monster_Base Animator.
/// - Uses Animator.StringToHash exclusively (zero string allocations in Update).
/// - Smooth-damps NavMeshAgent or networked speed into the 1D Locomotion Blend Tree (0.0 / 1.5 / 4.5).
/// - Prevents stuck triggers when stunned, knocked down, or killed mid-attack.
/// - Manages UpperBody layer weight dynamically for move-and-attack/cast monsters.
/// - Relays Animation Events (OnAttackHit, OnAttackEnd, OnSkillRelease, OnFootstep, OnDeathComplete) via C# events.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public sealed class MonsterAnimatorBridge : MonoBehaviour
{
    // =========================================================
    // ANIMATOR PARAMETER HASHES (AC_Monster_Base Standard)
    // =========================================================

    public static readonly int SpeedHash = Animator.StringToHash("Speed");
    public static readonly int IsCombatHash = Animator.StringToHash("IsCombat");
    public static readonly int AttackHash = Animator.StringToHash("Attack");
    public static readonly int AttackIndexHash = Animator.StringToHash("AttackIndex");
    public static readonly int SkillHash = Animator.StringToHash("Skill");
    public static readonly int SkillIndexHash = Animator.StringToHash("SkillIndex");
    public static readonly int IsCastingHash = Animator.StringToHash("IsCasting");
    public static readonly int HitHash = Animator.StringToHash("Hit");
    public static readonly int IsStunnedHash = Animator.StringToHash("IsStunned");
    public static readonly int IsDeadHash = Animator.StringToHash("IsDead");

    private static readonly int Attack01StateHash = Animator.StringToHash("Attack_01");
    private static readonly int Attack02StateHash = Animator.StringToHash("Attack_02");
    private static readonly int Attack03StateHash = Animator.StringToHash("Attack_03");
    private static readonly int SkillCastStateHash = Animator.StringToHash("Skill_Cast");
    private static readonly int SkillRelease01StateHash = Animator.StringToHash("Skill_Release_01");
    private static readonly int SkillRelease02StateHash = Animator.StringToHash("Skill_Release_02");

    public const string UpperBodyLayerName = "UpperBody";

    // =========================================================
    // INSPECTOR REFERENCES
    // =========================================================

    [Header("Configuration")]
    [SerializeField] private MonsterAnimationConfig config;

    [Header("Components")]
    [SerializeField] private Animator animator;
    [Tooltip("Optional NavMeshAgent. If assigned and autoReadNavMeshSpeed is true, Speed is updated automatically.")]
    [SerializeField] private NavMeshAgent navMeshAgent;
    [SerializeField] private bool autoReadNavMeshSpeed = true;

    [Header("Evade (Return to Spawn)")]
    [Tooltip("Multiplier applied to Speed when the monster is evading/returning to its spawn point.")]
    [SerializeField] private float evadeSpeedMultiplier = 1.35f;

    // =========================================================
    // ANIMATION EVENTS (Subscribed by Combat, AI, SFX, ObjectPool)
    // =========================================================

    /// <summary>Fired on the exact frame the weapon/claw hits the target.</summary>
    public event Action OnAttackHitEvent;

    /// <summary>Fired when the attack recovery finishes so AI can choose its next action.</summary>
    public event Action OnAttackEndEvent;

    /// <summary>Fired on the frame a projectile, AoE, or spell is released.</summary>
    public event Action OnSkillReleaseEvent;

    /// <summary>Fired when a foot touches the ground during Walk/Run.</summary>
    public event Action OnFootstepEvent;

    /// <summary>Fired on the last frame of the Death clip so Object Pooling can despawn/recycle the monster.</summary>
    public event Action OnDeathCompleteEvent;

    // =========================================================
    // RUNTIME STATE
    // =========================================================

    private int _upperBodyLayerIndex = -1;
    private float _currentUpperBodyWeight;
    private float _targetUpperBodyWeight;

    private float _externalWorldSpeed;
    private float _currentBlendSpeed;
    private bool _isCombat;
    private bool _isCasting;
    private bool _isStunned;
    private bool _isDead;
    private bool _isEvading;
    private bool _isActionInProgress;
    private bool _actionEnteredAnimatorState;
    private float _actionStartedAt;
    private float _lastFlinchTime = -999f;
    private float _nextProceduralFootstepTime;
    private const float ActionFallbackTimeout = 1.65f;

    // =========================================================
    // PUBLIC PROPERTIES
    // =========================================================

    public MonsterAnimationConfig Config
    {
        get => config;
        set => config = value;
    }

    public Animator AnimatorComponent => animator;
    public bool IsCombat => _isCombat;
    public bool IsCasting => _isCasting;
    public bool IsStunned => _isStunned;
    public bool IsDead => _isDead;
    public bool IsEvading => _isEvading;
    public bool IsActionInProgress => _isActionInProgress;

    // =========================================================
    // UNITY LIFECYCLE
    // =========================================================

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (navMeshAgent == null)
        {
            navMeshAgent = GetComponent<NavMeshAgent>();
        }

        // Enforce In-Place standard (no Root Motion drift in MMO / NavMesh / Photon Fusion).
        if (animator != null)
        {
            animator.applyRootMotion = false;
            _upperBodyLayerIndex = animator.GetLayerIndex(UpperBodyLayerName);
            if (_upperBodyLayerIndex >= 0)
            {
                animator.SetLayerWeight(_upperBodyLayerIndex, 0f);
            }
        }
    }

    private void Update()
    {
        if (animator == null || !animator.isActiveAndEnabled)
        {
            return;
        }

        UpdateLocomotionSpeed(Time.deltaTime);
        UpdateUpperBodyLayerWeight(Time.deltaTime);
        UpdateActionProgress();
    }

    private void UpdateActionProgress()
    {
        if (!_isActionInProgress || _isCasting || _isDead || _isStunned)
        {
            return;
        }

        bool inActionState = IsInAttackOrSkillState();
        if (inActionState)
        {
            _actionEnteredAnimatorState = true;
            return;
        }

        float elapsed = Time.time - _actionStartedAt;
        if ((_actionEnteredAnimatorState && !animator.IsInTransition(0)) || elapsed >= ActionFallbackTimeout)
        {
            _isActionInProgress = false;
            _actionEnteredAnimatorState = false;
            OnAttackEndEvent?.Invoke();
        }
    }

    private bool IsInAttackOrSkillState()
    {
        AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0);
        if (IsActionStateHash(current.shortNameHash))
        {
            return true;
        }

        if (animator.IsInTransition(0))
        {
            AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(0);
            if (IsActionStateHash(next.shortNameHash))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsActionStateHash(int hash)
    {
        return hash == Attack01StateHash ||
               hash == Attack02StateHash ||
               hash == Attack03StateHash ||
               hash == SkillCastStateHash ||
               hash == SkillRelease01StateHash ||
               hash == SkillRelease02StateHash;
    }

    // =========================================================
    // LOCOMOTION & UPPER BODY LAYER
    // =========================================================

    private void UpdateLocomotionSpeed(float deltaTime)
    {
        float dampTime = config != null ? config.speedDampTime : 0.14f;

        if (_isDead || _isStunned)
        {
            animator.SetFloat(SpeedHash, 0f, dampTime * 0.6f, deltaTime);
            _currentBlendSpeed = animator.GetFloat(SpeedHash);
            return;
        }

        float rawWorldSpeed = _externalWorldSpeed;
        if (autoReadNavMeshSpeed && navMeshAgent != null && navMeshAgent.isOnNavMesh)
        {
            rawWorldSpeed = navMeshAgent.velocity.magnitude;
        }

        if (_isEvading)
        {
            rawWorldSpeed *= evadeSpeedMultiplier;
        }

        // Pause locomotion blending while actively executing a grounded melee swing so feet stay planted.
        if (_isActionInProgress && !(_configAllowsMoveAttack()))
        {
            rawWorldSpeed = 0f;
        }

        float targetBlendTreeSpeed;
        if (config != null)
        {
            targetBlendTreeSpeed = config.EvaluateBlendTreeSpeed(rawWorldSpeed);
        }
        else
        {
            // Fallback mapping: 0 = Idle, 1.5 = Walk, 4.5 = Run
            targetBlendTreeSpeed = Mathf.Clamp(rawWorldSpeed, 0f, 4.5f);
        }

        animator.SetFloat(SpeedHash, targetBlendTreeSpeed, dampTime, deltaTime);
        _currentBlendSpeed = animator.GetFloat(SpeedHash);

        // Emit rhythmic footsteps synced to actual Walk/Run blend speed.
        if (rawWorldSpeed > 0.65f && !_isActionInProgress && Time.time >= _nextProceduralFootstepTime)
        {
            float cadence = Mathf.Lerp(0.56f, 0.34f, Mathf.InverseLerp(1.0f, 4.5f, _currentBlendSpeed));
            _nextProceduralFootstepTime = Time.time + cadence;
            MMOAudioManager.PlayFootstep(transform.position, 0.16f);
        }
    }

    private bool _configAllowsMoveAttack()
    {
        return config != null && config.useUpperBodyLayerWhenMoving;
    }

    private void UpdateUpperBodyLayerWeight(float deltaTime)
    {
        if (_upperBodyLayerIndex < 0)
        {
            return;
        }

        bool shouldUseUpperBody =
            !_isDead &&
            !_isStunned &&
            config != null &&
            config.useUpperBodyLayerWhenMoving &&
            _currentBlendSpeed > 0.15f &&
            (_isActionInProgress || _isCasting);

        _targetUpperBodyWeight = shouldUseUpperBody ? 1f : 0f;

        float blendSpeed = config != null ? config.upperBodyWeightBlendSpeed : 10f;
        _currentUpperBodyWeight = Mathf.MoveTowards(
            _currentUpperBodyWeight,
            _targetUpperBodyWeight,
            blendSpeed * deltaTime);

        animator.SetLayerWeight(_upperBodyLayerIndex, _currentUpperBodyWeight);
    }

    // =========================================================
    // PUBLIC CONTROL API (CALLED BY AI OR NETWORK SYNC)
    // =========================================================

    /// <summary>
    /// Manually sets the monster's world movement speed (m/s) when not using NavMeshAgent directly
    /// (e.g., on remote Photon Fusion clients or custom CharacterController).
    /// </summary>
    public void SetWorldSpeed(float worldSpeed)
    {
        autoReadNavMeshSpeed = false;
        _externalWorldSpeed = Mathf.Max(0f, worldSpeed);
    }

    /// <summary>
    /// Sets whether the monster is in Combat/Aggro stance (Combat_Idle vs normal Locomotion).
    /// </summary>
    public void SetCombat(bool inCombat)
    {
        if (_isDead)
        {
            return;
        }

        _isCombat = inCombat;
        animator.SetBool(IsCombatHash, inCombat);
    }

    /// <summary>
    /// Enters or exits Evade mode (monster runs back to spawn ignoring combat).
    /// Clears pending attack/cast states so it runs cleanly.
    /// </summary>
    public void SetEvading(bool evading)
    {
        if (_isDead)
        {
            return;
        }

        _isEvading = evading;
        if (evading)
        {
            ClearActionTriggers();
            SetCasting(false);
            SetCombat(false);
            _isActionInProgress = false;
        }
    }

    /// <summary>
    /// Triggers a random basic attack weighted by MonsterAnimationConfig.
    /// </summary>
    public int TriggerRandomAttack()
    {
        int index = config != null ? config.PickRandomAttackIndex() : 0;
        TriggerAttack(index);
        return index;
    }

    /// <summary>
    /// Triggers the next alternating basic attack so left/right swings stay symmetrical and natural.
    /// </summary>
    public int TriggerNextAttack(int lastAttackIndex)
    {
        int index = config != null ? config.PickNextAttackIndex(lastAttackIndex) : 0;
        TriggerAttack(index);
        return index;
    }

    /// <summary>
    /// Triggers a specific basic attack index (0 = Attack_01, 1 = Attack_02, 2 = Attack_03).
    /// </summary>
    public void TriggerAttack(int attackIndex)
    {
        if (_isDead || _isStunned || _isEvading)
        {
            return;
        }

        int maxAttacks = config != null ? config.attackCount : 3;
        int clampedIndex = Mathf.Clamp(attackIndex, 0, Mathf.Max(0, maxAttacks - 1));

        if (!_isCombat)
        {
            SetCombat(true);
        }

        _isActionInProgress = true;
        _actionEnteredAnimatorState = false;
        _actionStartedAt = Time.time;
        animator.ResetTrigger(HitHash);
        animator.SetInteger(AttackIndexHash, clampedIndex);
        animator.SetTrigger(AttackHash);
    }

    /// <summary>
    /// Triggers a special skill.
    /// If startCastingLoop is true, enters Skill_Cast loop until ReleaseCastSkill() is called.
    /// If startCastingLoop is false, fires Skill_Release immediately.
    /// </summary>
    public void TriggerSkill(int skillIndex = 0, bool startCastingLoop = false)
    {
        if (_isDead || _isStunned || _isEvading)
        {
            return;
        }

        if (!_isCombat)
        {
            SetCombat(true);
        }

        _isActionInProgress = true;
        _actionEnteredAnimatorState = false;
        _actionStartedAt = Time.time;
        _isCasting = startCastingLoop;

        animator.ResetTrigger(HitHash);
        animator.SetInteger(SkillIndexHash, Mathf.Max(0, skillIndex));
        animator.SetBool(IsCastingHash, startCastingLoop);
        animator.SetTrigger(SkillHash);
    }

    /// <summary>
    /// Releases a currently channeling Skill_Cast into Skill_Release.
    /// </summary>
    public void ReleaseCastSkill()
    {
        if (!_isCasting || _isDead)
        {
            return;
        }

        _isCasting = false;
        _actionStartedAt = Time.time;
        _actionEnteredAnimatorState = false;
        animator.SetBool(IsCastingHash, false);
    }

    /// <summary>
    /// Directly sets the IsCasting bool (e.g., for network synchronization or cancelling a cast).
    /// </summary>
    public void SetCasting(bool isCasting)
    {
        _isCasting = isCasting;
        animator.SetBool(IsCastingHash, isCasting);
    }

    /// <summary>
    /// Requests a Hit_React flinch animation.
    /// Automatically respects boss super-armor (canFlinch = false), cooldowns, and dead/stunned states.
    /// </summary>
    public bool TriggerHit()
    {
        if (_isDead || _isStunned || _isEvading)
        {
            return false;
        }

        if (config != null)
        {
            if (!config.canFlinch)
            {
                return false;
            }

            if (!config.canFlinchDuringAttack && (_isActionInProgress || _isCasting))
            {
                return false;
            }

            if (Time.time - _lastFlinchTime < config.flinchCooldown)
            {
                return false;
            }
        }

        _lastFlinchTime = Time.time;
        _isActionInProgress = false;
        _isCasting = false;

        ClearActionTriggers();
        animator.SetBool(IsCastingHash, false);
        animator.SetTrigger(HitHash);
        return true;
    }

    /// <summary>
    /// Applies or removes the Stun state.
    /// Clears all pending Attack/Skill/Hit triggers so the monster does not fire a queued attack upon waking up.
    /// </summary>
    public void SetStunned(bool stunned)
    {
        if (_isDead)
        {
            return;
        }

        _isStunned = stunned;
        if (stunned)
        {
            _isActionInProgress = false;
            _isCasting = false;
            ClearActionTriggers();
            animator.SetBool(IsCastingHash, false);
            ForceResetUpperBodyWeight();
        }

        animator.SetBool(IsStunnedHash, stunned);
    }

    /// <summary>
    /// Transitions the monster into the terminal Death state immediately.
    /// Clears all triggers, stops casting/stun, and zeroes UpperBody layer weight.
    /// </summary>
    public void SetDead(bool dead = true)
    {
        _isDead = dead;

        if (dead)
        {
            _isActionInProgress = false;
            _isCasting = false;
            _isStunned = false;
            _isEvading = false;

            ClearActionTriggers();
            animator.SetBool(IsCastingHash, false);
            animator.SetBool(IsStunnedHash, false);
            animator.SetFloat(SpeedHash, 0f);
            ForceResetUpperBodyWeight();
        }

        animator.SetBool(IsDeadHash, dead);
    }

    /// <summary>
    /// Resets all states so a dead monster can be reused from an Object Pool.
    /// </summary>
    public void ResetForPoolSpawn()
    {
        _isDead = false;
        _isStunned = false;
        _isCasting = false;
        _isCombat = false;
        _isEvading = false;
        _isActionInProgress = false;
        _externalWorldSpeed = 0f;

        ClearActionTriggers();
        animator.SetBool(IsDeadHash, false);
        animator.SetBool(IsStunnedHash, false);
        animator.SetBool(IsCastingHash, false);
        animator.SetBool(IsCombatHash, false);
        animator.SetFloat(SpeedHash, 0f);
        ForceResetUpperBodyWeight();

        animator.Rebind();
        animator.Update(0f);
    }

    /// <summary>
    /// Clears any unconsumed Animator triggers to prevent ghost attacks after Stun/Hit/Death.
    /// </summary>
    public void ClearActionTriggers()
    {
        if (animator == null)
        {
            return;
        }

        animator.ResetTrigger(AttackHash);
        animator.ResetTrigger(SkillHash);
        animator.ResetTrigger(HitHash);
    }

    private void ForceResetUpperBodyWeight()
    {
        _currentUpperBodyWeight = 0f;
        _targetUpperBodyWeight = 0f;
        if (_upperBodyLayerIndex >= 0 && animator != null)
        {
            animator.SetLayerWeight(_upperBodyLayerIndex, 0f);
        }
    }

    // =========================================================
    // ANIMATION EVENT RECEIVERS (Embedded in FBX / AnimationClips)
    // =========================================================

    /// <summary>Animation Event: Weapon/claw active hit frame.</summary>
    public void OnAttackHit()
    {
        if (_isDead || _isStunned)
        {
            return;
        }

        OnAttackHitEvent?.Invoke();
    }

    /// <summary>Animation Event: Attack animation recovery completed.</summary>
    public void OnAttackEnd()
    {
        _isActionInProgress = false;
        OnAttackEndEvent?.Invoke();
    }

    /// <summary>Animation Event: Skill projectile/AoE release frame.</summary>
    public void OnSkillRelease()
    {
        if (_isDead || _isStunned)
        {
            return;
        }

        _isActionInProgress = false;
        OnSkillReleaseEvent?.Invoke();
    }

    /// <summary>Animation Event: Footstep contact frame during Walk/Run.</summary>
    public void OnFootstep()
    {
        if (_isDead || _isStunned)
        {
            return;
        }

        OnFootstepEvent?.Invoke();
    }

    /// <summary>Animation Event: Final frame of Death animation (ready for Object Pool).</summary>
    public void OnDeathComplete()
    {
        OnDeathCompleteEvent?.Invoke();
    }
}
