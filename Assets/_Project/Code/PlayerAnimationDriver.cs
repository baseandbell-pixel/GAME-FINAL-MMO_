using UnityEngine;

/// <summary>
/// Bridges <see cref="PlayerCapsule"/> and <see cref="PlayerCombat"/> (Photon Fusion Shared Mode)
/// to the GreatSword humanoid <see cref="Animator"/> on every peer (Host &amp; Clients).
///
/// Maps:
///   - 8-directional locomotion: MoveX, MoveY (scaled 1x walk / 2x sprint for the 2D Freeform
///     Directional blend trees), Speed, Direction, Moving, IsMoving, IsSprinting, Grounded.
///   - Combat stance &amp; weapon meshes: InCombat, SkipDraw, Sword_Hand / Sword_Back visibility.
///   - Combat actions: 3-hit Attack combo chain (Light Attack 1 -> 2 -> 3), Block (IsBlocking),
///     4-directional Dodge/Roll (RollX, RollY, Roll), Hit reaction, and Death.
/// </summary>
[DisallowMultipleComponent]
public class PlayerAnimationDriver : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private GameObject swordHand;
    [SerializeField] private GameObject swordBack;

    [Header("Settings")]
    [SerializeField] private float locomotionDampTime = 0.08f;
    [SerializeField] private bool alwaysArmedInSandbox = true;

    public Animator Animator => animator;

    private PlayerCapsule _capsule;
    private PlayerCombat _combat;

    // Cached Animator parameter hashes
    private static readonly int HashMoveX = Animator.StringToHash("MoveX");
    private static readonly int HashMoveY = Animator.StringToHash("MoveY");
    private static readonly int HashSpeed = Animator.StringToHash("Speed");
    private static readonly int HashDirection = Animator.StringToHash("Direction");
    private static readonly int HashMoving = Animator.StringToHash("Moving");
    private static readonly int HashIsMoving = Animator.StringToHash("IsMoving");
    private static readonly int HashIsSprinting = Animator.StringToHash("IsSprinting");
    private static readonly int HashGrounded = Animator.StringToHash("Grounded");
    private static readonly int HashInCombat = Animator.StringToHash("InCombat");
    private static readonly int HashSkipDraw = Animator.StringToHash("SkipDraw");
    private static readonly int HashIsBlocking = Animator.StringToHash("IsBlocking");
    private static readonly int HashBlock = Animator.StringToHash("Block");
    private static readonly int HashAttack = Animator.StringToHash("Attack");
    private static readonly int HashHeavyAttack = Animator.StringToHash("HeavyAttack");
    private static readonly int HashRunAttack = Animator.StringToHash("RunAttack");
    private static readonly int HashRoll = Animator.StringToHash("Roll");
    private static readonly int HashDodge = Animator.StringToHash("Dodge");
    private static readonly int HashRollX = Animator.StringToHash("RollX");
    private static readonly int HashRollY = Animator.StringToHash("RollY");
    private static readonly int HashHit = Animator.StringToHash("Hit");
    private static readonly int HashDead = Animator.StringToHash("Dead");

    private static readonly int StateLightAttack1 = Animator.StringToHash("Base Layer.Light Attack 1");
    private static readonly int StateLightAttack2 = Animator.StringToHash("Base Layer.Light Attack 2");
    private static readonly int StateLightAttack3 = Animator.StringToHash("Base Layer.Light Attack 3");
    private static readonly int StateBlock = Animator.StringToHash("Base Layer.Block");

    private bool _hasSpeedParam;
    private bool _hasIsMovingParam;
    private bool _hasDirectionParam;
    private bool _hasIsSprintingParam;
    private bool _hasIsBlockingParam;
    private bool _hasBlockParam;
    private bool _hasDodgeParam;
    private bool _parametersChecked;

    private void Awake()
    {
        _capsule = GetComponent<PlayerCapsule>();
        _combat = GetComponent<PlayerCombat>();

        ResolveReferences();
        CacheParameterAvailability();
        InitializeAnimatorDefaults();
    }

    public void ResolveReferences()
    {
        if (animator == null)
        {
            if (_capsule != null && _capsule.CharacterVisual != null)
                animator = _capsule.CharacterVisual.GetComponentInChildren<Animator>();
            if (animator == null)
                animator = GetComponentInChildren<Animator>();
        }

        if (animator != null && (swordHand == null || swordBack == null))
        {
            foreach (Transform t in animator.GetComponentsInChildren<Transform>(true))
            {
                if (swordHand == null && t.name == "Sword_Hand") swordHand = t.gameObject;
                else if (swordBack == null && t.name == "Sword_Back") swordBack = t.gameObject;
            }
        }
    }

    private void CacheParameterAvailability()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;

        _hasSpeedParam = HasParam(HashSpeed);
        _hasIsMovingParam = HasParam(HashIsMoving);
        _hasDirectionParam = HasParam(HashDirection);
        _hasIsSprintingParam = HasParam(HashIsSprinting);
        _hasIsBlockingParam = HasParam(HashIsBlocking);
        _hasBlockParam = HasParam(HashBlock);
        _hasDodgeParam = HasParam(HashDodge);
        _parametersChecked = true;
    }

    private bool HasParam(int nameHash)
    {
        foreach (var p in animator.parameters)
        {
            if (p.nameHash == nameHash) return true;
        }
        return false;
    }

    private void InitializeAnimatorDefaults()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;

        animator.applyRootMotion = false;
        animator.SetBool(HashGrounded, true);
        animator.SetBool(HashSkipDraw, true);
        animator.SetBool(HashInCombat, alwaysArmedInSandbox);

        UpdateSwordVisuals(alwaysArmedInSandbox);
    }

    private void LateUpdate()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        if (!_parametersChecked) CacheParameterAvailability();

        Vector2 netMove = _capsule != null ? _capsule.NetworkMove : Vector2.zero;
        bool netSprint = _capsule != null && _capsule.NetworkSprinting;
        bool isDead = _combat != null && _combat.IsDead;
        bool isBlocking = _combat != null && _combat.IsBlocking && !isDead;

        animator.SetBool(HashGrounded, true);
        animator.SetBool(HashSkipDraw, true);
        animator.SetBool(HashInCombat, alwaysArmedInSandbox);
        animator.SetBool(HashDead, isDead);

        if (_hasIsBlockingParam)
            animator.SetBool(HashIsBlocking, isBlocking);

        UpdateSwordVisuals(alwaysArmedInSandbox);

        if (isDead)
        {
            SetLocomotionImmediate(0f, 0f, false, false);
            return;
        }

        // While in an active Attack state on Base Layer, suppress Moving so locomotion transitions
        // do not interrupt the swing early.
        var currentState = animator.GetCurrentAnimatorStateInfo(0);
        bool inAttackState = currentState.IsTag("Attack") ||
                             (animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).IsTag("Attack"));

        if (inAttackState)
        {
            animator.SetBool(HashMoving, false);
            if (_hasIsMovingParam) animator.SetBool(HashIsMoving, false);
            animator.SetFloat(HashMoveX, 0f, locomotionDampTime, Time.deltaTime);
            animator.SetFloat(HashMoveY, 0f, locomotionDampTime, Time.deltaTime);
            if (_hasSpeedParam) animator.SetFloat(HashSpeed, 0f, locomotionDampTime, Time.deltaTime);
            return;
        }

        bool moving = netMove.sqrMagnitude > 0.001f;
        // The 2D Freeform Directional Blend Trees (Locomotion / Great Sword Locomotion) have
        // Walk clips at radius 1.0 and Run / StrafeRun clips at radius 2.0.
        float speedScale = (netSprint && !isBlocking) ? 2f : 1f;
        float targetX = moving ? Mathf.Clamp(netMove.x, -1f, 1f) * speedScale : 0f;
        float targetY = moving ? Mathf.Clamp(netMove.y, -1f, 1f) * speedScale : 0f;
        float speed = moving ? Mathf.Clamp01(netMove.magnitude) * speedScale : 0f;
        float direction = moving ? Mathf.Atan2(netMove.x, netMove.y) * Mathf.Rad2Deg : 0f;

        animator.SetBool(HashMoving, moving);
        if (_hasIsMovingParam) animator.SetBool(HashIsMoving, moving);
        if (_hasIsSprintingParam) animator.SetBool(HashIsSprinting, netSprint && moving && !isBlocking);

        animator.SetFloat(HashMoveX, targetX, locomotionDampTime, Time.deltaTime);
        animator.SetFloat(HashMoveY, targetY, locomotionDampTime, Time.deltaTime);
        if (_hasSpeedParam) animator.SetFloat(HashSpeed, speed, locomotionDampTime, Time.deltaTime);
        if (_hasDirectionParam) animator.SetFloat(HashDirection, direction, locomotionDampTime, Time.deltaTime);
    }

    private void SetLocomotionImmediate(float x, float y, bool moving, bool sprinting)
    {
        animator.SetBool(HashMoving, moving);
        if (_hasIsMovingParam) animator.SetBool(HashIsMoving, moving);
        if (_hasIsSprintingParam) animator.SetBool(HashIsSprinting, sprinting);
        animator.SetFloat(HashMoveX, x);
        animator.SetFloat(HashMoveY, y);
        if (_hasSpeedParam) animator.SetFloat(HashSpeed, 0f);
    }

    private void UpdateSwordVisuals(bool armed)
    {
        if (swordHand != null && swordHand.activeSelf != armed)
            swordHand.SetActive(armed);
        if (swordBack != null && swordBack.activeSelf == armed)
            swordBack.SetActive(!armed);
    }

    // ------------------------------------------------------------------ combat hooks (called on all peers)

    /// <summary>Triggers the GreatSword attack animation for the given combo step (1..3).</summary>
    public void PlayAttack(int comboStep)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;

        animator.SetBool(HashSkipDraw, true);
        animator.SetBool(HashInCombat, true);
        UpdateSwordVisuals(true);

        animator.ResetTrigger(HashRoll);
        if (_hasDodgeParam) animator.ResetTrigger(HashDodge);
        animator.ResetTrigger(HashAttack);
        animator.SetTrigger(HashAttack);

        int targetState = comboStep switch
        {
            2 => StateLightAttack2,
            3 => StateLightAttack3,
            _ => StateLightAttack1
        };

        // CrossFade ensures responsive combo chaining (1 -> 2 -> 3) on all peers regardless of network jitter.
        animator.CrossFadeInFixedTime(targetState, 0.1f, 0, 0f);
        animator.ResetTrigger(HashAttack);
    }

    /// <summary>Triggers the 4-directional GreatSword dodge/roll animation on all peers.</summary>
    public void PlayDodge(float rollX, float rollY)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;

        animator.ResetTrigger(HashAttack);
        animator.ResetTrigger(HashHeavyAttack);
        animator.ResetTrigger(HashRunAttack);

        animator.SetFloat(HashRollX, Mathf.Clamp(rollX, -1f, 1f));
        animator.SetFloat(HashRollY, Mathf.Clamp(rollY, -1f, 1f));

        animator.ResetTrigger(HashRoll);
        animator.SetTrigger(HashRoll);
        if (_hasDodgeParam)
        {
            animator.ResetTrigger(HashDodge);
            animator.SetTrigger(HashDodge);
        }
    }

    /// <summary>Updates the blocking state on all peers.</summary>
    public void SetBlocking(bool blocking)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;

        if (blocking)
        {
            animator.ResetTrigger(HashAttack);
            animator.SetBool(HashSkipDraw, true);
            animator.SetBool(HashInCombat, true);
            UpdateSwordVisuals(true);
            if (_hasBlockParam)
            {
                animator.ResetTrigger(HashBlock);
                animator.SetTrigger(HashBlock);
            }
            if (!animator.GetCurrentAnimatorStateInfo(0).IsName("Block"))
                animator.CrossFadeInFixedTime(StateBlock, 0.1f, 0);
        }

        if (_hasIsBlockingParam)
            animator.SetBool(HashIsBlocking, blocking);
    }

    /// <summary>Plays hit flinch animation when damaged while alive.</summary>
    public void PlayHitReaction()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        animator.ResetTrigger(HashHit);
        animator.SetTrigger(HashHit);
    }

    /// <summary>Sets the Dead boolean parameter on the Animator.</summary>
    public void SetDead(bool dead)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        animator.SetBool(HashDead, dead);
    }
}
