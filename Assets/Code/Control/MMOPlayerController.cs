using UnityEngine;

/// <summary>
/// Local character movement, grounded/jump physics, locomotion blend values,
/// and a buffered two-hit attack combo.
/// Add this to the network player root beside CharacterController.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public sealed class MMOPlayerController : MonoBehaviour
{
    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int IsGroundedHash = Animator.StringToHash("IsGrounded");
    private static readonly int JumpHash = Animator.StringToHash("Jump");
    private static readonly int Attack1Hash = Animator.StringToHash("Attack1");
    private static readonly int Attack2Hash = Animator.StringToHash("Attack2");

    private static readonly int AttackTagHash = Animator.StringToHash("Attack");
    private static readonly int Attack1StateHash = Animator.StringToHash("Attack1");

    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private Transform groundCheck;
    [SerializeField] private LayerMask groundLayers = ~0;
    [SerializeField] private bool allowStandaloneInput = true;

    [Header("Movement Speeds")]
    [SerializeField, Min(0f)] private float walkSpeed = 2.2f;
    [SerializeField, Min(0f)] private float runSpeed = 5.6f;
    [Tooltip("Normal movement runs. CapsLock toggles walk mode; Left Shift forces run.")]
    [SerializeField] private bool startInWalkMode = false;

    [Header("Movement Smoothing")]
    [SerializeField, Min(0.01f)] private float accelerationSmoothTime = 0.12f;
    [SerializeField, Min(0.01f)] private float turnSmoothTime = 0.10f;
    [SerializeField, Min(0.01f)] private float attackTurnSmoothTime = 0.045f;
    [SerializeField, Min(0.01f)] private float animatorDampTime = 0.12f;

    [Header("Jump and Grounding")]
    [SerializeField, Min(0.01f)] private float groundCheckRadius = 0.22f;
    [SerializeField] private float jumpHeight = 1.65f;
    [SerializeField] private float gravity = -25f;
    [SerializeField, Min(0f)] private float groundStickForce = 2.5f;

    [Header("Attack Combo")]
    [Tooltip("The second click is buffered until this normalized time if it arrives early.")]
    [Range(0f, 0.95f)][SerializeField] private float comboWindowStart = 0.38f;
    [Range(0.05f, 1f)][SerializeField] private float comboWindowEnd = 0.88f;
    [SerializeField, Min(0f)] private float attackStartGraceTime = 0.25f;

    private CharacterController _characterController;
    private MMOCameraController _cameraController;

    private bool _inputEnabled;
    private bool _walkMode;
    private bool _isGrounded;
    private bool _attackActive;
    private bool _comboQueued;

    private float _verticalVelocity;
    private float _currentMoveSpeed;
    private float _speedSmoothVelocity;
    private float _rotationSmoothVelocity;
    private float _attackCommandTime;
    private float _lastAttackStateSeenTime;

    private int _attackStep;
    private int _networkActionCode;
    private int _networkActionSequence;
    private int _lastRemoteActionSequence = -1;

    private float _networkAnimationSpeed;

    private bool _hasSpeedParameter;
    private bool _hasGroundedParameter;
    private bool _hasJumpParameter;
    private bool _hasAttack1Parameter;
    private bool _hasAttack2Parameter;

    /// <summary>0 = locomotion, 1 = jump, 2 = attack one, 3 = attack two.</summary>
    public int NetworkActionCode => _networkActionCode;
    public int NetworkActionSequence => _networkActionSequence;
    public float NetworkAnimationSpeed => _networkAnimationSpeed;
    public bool NetworkIsGrounded => _isGrounded;
    public bool IsLocalControl => _inputEnabled;

    private void Awake()
    {
        _characterController = GetComponent<CharacterController>();
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        _walkMode = startInWalkMode;
        bool hasFusionDriver = GetComponent<MMOPlayerFusion>() != null;
        _inputEnabled = !hasFusionDriver && allowStandaloneInput;
        _characterController.enabled = !hasFusionDriver;

        if (animator == null)
        {
            Debug.LogError("[MMOPlayerController] Animator was not found on this player or its children.", this);
            enabled = false;
            return;
        }

        // CharacterController owns translation; root motion would move the root a second time.
        animator.applyRootMotion = false;

        _hasSpeedParameter = HasParameter(SpeedHash, AnimatorControllerParameterType.Float);
        _hasGroundedParameter = HasParameter(IsGroundedHash, AnimatorControllerParameterType.Bool);
        _hasJumpParameter = HasParameter(JumpHash, AnimatorControllerParameterType.Trigger);
        _hasAttack1Parameter = HasParameter(Attack1Hash, AnimatorControllerParameterType.Trigger);
        _hasAttack2Parameter = HasParameter(Attack2Hash, AnimatorControllerParameterType.Trigger);

        if (!_hasSpeedParameter || !_hasGroundedParameter || !_hasJumpParameter || !_hasAttack1Parameter || !_hasAttack2Parameter)
        {
            Debug.LogError(
                "[MMOPlayerController] Animator parameters must be Speed (Float), IsGrounded (Bool), Jump (Trigger), Attack1 (Trigger), and Attack2 (Trigger).",
                this);
        }
    }

    private void Start()
    {
        if (_cameraController == null)
        {
            _cameraController = MMOCameraController.Instance;
            if (_cameraController == null)
            {
                _cameraController = FindObjectOfType<MMOCameraController>();
            }
        }

        if (_inputEnabled && _cameraController != null)
        {
            _cameraController.SetTarget(transform);
        }
    }

    private void Update()
    {
        if (!_inputEnabled || !_characterController.enabled || animator == null)
        {
            return;
        }

        float deltaTime = Time.deltaTime;
        bool gameplayInputAllowed = _cameraController == null
            ? allowStandaloneInput
            : _cameraController.GameplayEnabled;

        if (gameplayInputAllowed && Input.GetKeyDown(KeyCode.CapsLock))
        {
            _walkMode = !_walkMode;
        }

        Vector2 moveInput = Vector2.zero;
        if (gameplayInputAllowed)
        {
            moveInput.x = Input.GetAxisRaw("Horizontal");
            moveInput.y = Input.GetAxisRaw("Vertical");
            moveInput = Vector2.ClampMagnitude(moveInput, 1f);
        }

        Vector3 cameraForward = _cameraController != null
            ? _cameraController.PlanarAimDirection
            : transform.forward;
        Vector3 cameraRight = _cameraController != null
            ? _cameraController.PlanarAimRight
            : transform.right;

        Vector3 moveDirection = cameraForward * moveInput.y + cameraRight * moveInput.x;
        moveDirection.y = 0f;
        if (moveDirection.sqrMagnitude > 1f)
        {
            moveDirection.Normalize();
        }

        UpdateGroundedState();

        if (_isGrounded && _verticalVelocity <= 0f && _networkActionCode == 1)
        {
            _networkActionCode = 0;
        }

        if (gameplayInputAllowed && Input.GetKeyDown(KeyCode.Space) && _isGrounded && !_attackActive)
        {
            BeginJump();
        }

        if (gameplayInputAllowed && Input.GetMouseButtonDown(0) && CanStartAttackFromMouse())
        {
            HandleAttackClick();
        }

        UpdateAttackComboState();

        bool hasMoveInput = moveInput.sqrMagnitude > 0.0001f;
        bool forceRun = gameplayInputAllowed && Input.GetKey(KeyCode.LeftShift);
        bool shouldRun = forceRun || !_walkMode;
        float desiredMoveSpeed = hasMoveInput
            ? (shouldRun ? runSpeed : walkSpeed)
            : 0f;

        if (_attackActive)
        {
            // Hard-stop horizontal motion during weapon swings to avoid foot sliding.
            _currentMoveSpeed = 0f;
            _speedSmoothVelocity = 0f;
        }
        else
        {
            _currentMoveSpeed = Mathf.SmoothDamp(
                _currentMoveSpeed,
                desiredMoveSpeed,
                ref _speedSmoothVelocity,
                accelerationSmoothTime,
                Mathf.Infinity,
                deltaTime);
        }

        UpdateFacing(moveDirection, hasMoveInput, deltaTime);

        if (_isGrounded)
        {
            if (_verticalVelocity < 0f)
            {
                _verticalVelocity = -groundStickForce;
            }
        }
        else
        {
            _verticalVelocity += gravity * deltaTime;
        }

        Vector3 horizontalVelocity = _attackActive ? Vector3.zero : moveDirection * _currentMoveSpeed;
        Vector3 totalVelocity = horizontalVelocity + Vector3.up * _verticalVelocity;
        _characterController.Move(totalVelocity * deltaTime);

        _networkAnimationSpeed = _attackActive ? 0f : (hasMoveInput ? (shouldRun ? 2f : 1f) : 0f);
        if (_hasSpeedParameter)
        {
            animator.SetFloat(SpeedHash, _networkAnimationSpeed, animatorDampTime, deltaTime);
        }
        if (_hasGroundedParameter)
        {
            animator.SetBool(IsGroundedHash, _isGrounded);
        }
    }

    private void UpdateGroundedState()
    {
        Vector3 checkPosition = groundCheck != null
            ? groundCheck.position
            : transform.position + Vector3.up * 0.12f;

        bool sphereGrounded = Physics.CheckSphere(
            checkPosition,
            groundCheckRadius,
            groundLayers,
            QueryTriggerInteraction.Ignore);

        _isGrounded = sphereGrounded || _characterController.isGrounded;
    }

    private void BeginJump()
    {
        _isGrounded = false;
        if (_hasGroundedParameter)
        {
            animator.SetBool(IsGroundedHash, false);
        }

        _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        _networkActionCode = 1;
        _networkActionSequence++;

        if (_hasJumpParameter)
        {
            animator.ResetTrigger(JumpHash);
            animator.SetTrigger(JumpHash);
        }
    }

    private bool CanStartAttackFromMouse()
    {
        // Check the key directly as well as the camera lock state so execution order between Update methods cannot cause a UI click to become an attack.
        if (Input.GetKey(KeyCode.LeftAlt) || !gameplayInputAllowedForAttack())
        {
            return false;
        }

        if (_cameraController != null)
        {
            return _cameraController.IsGameplayInputLocked;
        }

        return Cursor.lockState == CursorLockMode.Locked;
    }

    private bool gameplayInputAllowedForAttack()
    {
        return _cameraController == null || _cameraController.GameplayEnabled;
    }

    private void HandleAttackClick()
    {
        if (!_attackActive)
        {
            if (_isGrounded)
            {
                BeginAttackOne();
            }
            return;
        }

        if (_attackStep != 1)
        {
            return;
        }

        if (TryGetAttackOneNormalizedTime(out float normalizedTime))
        {
            if (normalizedTime > comboWindowEnd)
            {
                return;
            }

            if (normalizedTime >= comboWindowStart)
            {
                BeginAttackTwo();
            }
            else
            {
                _comboQueued = true;
            }
        }
        else if (Time.time - _attackCommandTime <= attackStartGraceTime)
        {
            // A quick second click can be buffered while the first state is blending in.
            _comboQueued = true;
        }
    }

    private void BeginAttackOne()
    {
        _attackActive = true;
        _comboQueued = false;
        _attackStep = 1;
        _attackCommandTime = Time.time;
        _lastAttackStateSeenTime = Time.time;
        _networkActionCode = 2;
        _networkActionSequence++;

        if (_hasAttack1Parameter)
        {
            animator.ResetTrigger(Attack1Hash);
            animator.ResetTrigger(Attack2Hash);
            animator.SetTrigger(Attack1Hash);
        }
    }

    private void BeginAttackTwo()
    {
        if (!_attackActive || _attackStep != 1)
        {
            return;
        }

        _comboQueued = false;
        _attackStep = 2;
        _attackCommandTime = Time.time;
        _networkActionCode = 3;
        _networkActionSequence++;

        if (_hasAttack2Parameter)
        {
            animator.ResetTrigger(Attack2Hash);
            animator.SetTrigger(Attack2Hash);
        }
    }

    private void UpdateAttackComboState()
    {
        if (!_attackActive)
        {
            return;
        }

        bool attackStateIsPlaying = TryGetAttackStateInfo(out AnimatorStateInfo attackStateInfo);
        if (attackStateIsPlaying)
        {
            _lastAttackStateSeenTime = Time.time;
        }

        if (_attackStep == 1 && TryGetAttackOneNormalizedTime(out float normalizedTime))
        {
            if (_comboQueued && normalizedTime >= comboWindowStart && normalizedTime <= comboWindowEnd)
            {
                BeginAttackTwo();
            }
            else if (normalizedTime > comboWindowEnd)
            {
                _comboQueued = false;
            }
        }

        // Wait for the trigger-to-state transition and then for the attack state to exit.
        bool attackStateHasBeenSeen = Time.time - _lastAttackStateSeenTime < 0.12f;
        bool commandIsStillStarting = Time.time - _attackCommandTime <= attackStartGraceTime;
        if (!attackStateIsPlaying && !attackStateHasBeenSeen && !commandIsStillStarting)
        {
            FinishAttack();
        }
    }

    private bool TryGetAttackOneNormalizedTime(out float normalizedTime)
    {
        AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0);
        if (current.shortNameHash == Attack1StateHash)
        {
            normalizedTime = current.normalizedTime;
            return true;
        }

        if (animator.IsInTransition(0))
        {
            AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(0);
            if (next.shortNameHash == Attack1StateHash)
            {
                normalizedTime = next.normalizedTime;
                return true;
            }
        }

        normalizedTime = 0f;
        return false;
    }

    private bool TryGetAttackStateInfo(out AnimatorStateInfo stateInfo)
    {
        AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0);
        if (current.tagHash == AttackTagHash)
        {
            stateInfo = current;
            return true;
        }

        if (animator.IsInTransition(0))
        {
            AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(0);
            if (next.tagHash == AttackTagHash)
            {
                stateInfo = next;
                return true;
            }
        }

        stateInfo = default;
        return false;
    }

    private void FinishAttack()
    {
        _attackActive = false;
        _comboQueued = false;
        _attackStep = 0;
        _networkActionCode = 0;
        _currentMoveSpeed = 0f;
        _speedSmoothVelocity = 0f;
    }

    private void UpdateFacing(Vector3 moveDirection, bool hasMoveInput, float deltaTime)
    {
        Vector3 facingDirection = Vector3.zero;

        if (_attackActive)
        {
            facingDirection = _cameraController != null
                ? _cameraController.PlanarAimDirection
                : transform.forward;
        }
        else if (hasMoveInput)
        {
            bool actionCamera = _cameraController != null &&
                                _cameraController.CurrentPreset == MMOCameraController.CameraPreset.ActionCombat;
            facingDirection = actionCamera && _cameraController.GameplayEnabled
                ? _cameraController.PlanarAimDirection
                : moveDirection;
        }

        facingDirection.y = 0f;
        if (facingDirection.sqrMagnitude < 0.0001f)
        {
            return;
        }

        float targetAngle = Mathf.Atan2(facingDirection.x, facingDirection.z) * Mathf.Rad2Deg;
        float smoothTime = _attackActive ? attackTurnSmoothTime : turnSmoothTime;
        float currentAngle = transform.eulerAngles.y;
        float smoothedAngle = Mathf.SmoothDampAngle(
            currentAngle,
            targetAngle,
            ref _rotationSmoothVelocity,
            smoothTime,
            Mathf.Infinity,
            deltaTime);

        transform.rotation = Quaternion.Euler(0f, smoothedAngle, 0f);
    }

    /// <summary>Called by MMOPlayerFusion after Fusion assigns this object's authority.</summary>
    public void SetLocalAuthority(bool isLocal, MMOCameraController cameraController)
    {
        _inputEnabled = isLocal;
        _cameraController = cameraController;

        if (_characterController != null)
        {
            _characterController.enabled = isLocal;
        }

        if (_cameraController != null)
        {
            if (isLocal)
            {
                _cameraController.SetTarget(transform);
            }
            else if (_cameraController.Target == transform)
            {
                _cameraController.SetTarget(null);
            }
        }
    }

    /// <summary>
    /// Drives the remote Animator from replicated Fusion values. This method is never used for the owner.
    /// </summary>
    public void ApplyNetworkAnimation(float speed, bool grounded, int actionCode, int actionSequence)
    {
        if (_inputEnabled || animator == null)
        {
            return;
        }

        if (_hasSpeedParameter)
        {
            animator.SetFloat(SpeedHash, Mathf.Clamp(speed, 0f, 2f), animatorDampTime, Time.deltaTime);
        }
        if (_hasGroundedParameter)
        {
            animator.SetBool(IsGroundedHash, grounded);
        }

        if (actionSequence == _lastRemoteActionSequence)
        {
            return;
        }

        _lastRemoteActionSequence = actionSequence;
        switch (actionCode)
        {
            case 1:
                if (_hasJumpParameter)
                {
                    animator.ResetTrigger(JumpHash);
                    animator.SetTrigger(JumpHash);
                }
                break;

            case 2:
                if (_hasAttack1Parameter)
                {
                    animator.ResetTrigger(Attack1Hash);
                    animator.SetTrigger(Attack1Hash);
                }
                break;

            case 3:
                if (_hasAttack2Parameter)
                {
                    animator.ResetTrigger(Attack2Hash);
                    animator.SetTrigger(Attack2Hash);
                }
                break;
        }
    }

    private bool HasParameter(int parameterHash, AnimatorControllerParameterType parameterType)
    {
        if (animator == null)
        {
            return false;
        }

        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].nameHash == parameterHash && parameters[i].type == parameterType)
            {
                return true;
            }
        }

        return false;
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 checkPosition = groundCheck != null
            ? groundCheck.position
            : transform.position + Vector3.up * 0.12f;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(checkPosition, groundCheckRadius);
    }
}
