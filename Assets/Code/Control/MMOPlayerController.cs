
using UnityEngine;

/// <summary>
/// Local character movement, grounding, jump animation locking,
/// locomotion blending and a buffered two-hit attack combo.
///
/// Designed for CharacterController + Animator.
/// Compatible with MMOPlayerFusion network animation properties.
///
/// Controls:
/// W A S D = Movement
/// W + Left Shift = Run
/// S = Backward Walk
/// Space = Jump
/// Left Click = Attack 1 / Attack 2 Combo
///
/// Requirements:
/// - No Rigidbody on the Player root.
/// - Animator Apply Root Motion must be disabled.
/// - Ground Layers must include floor layers only.
/// - Animator states: Locomotion, Jump, Attack1, Attack2.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public sealed class MMOPlayerController : MonoBehaviour
{
    // =========================================================
    // ANIMATOR HASHES
    // =========================================================

    private static readonly int SpeedHash =
        Animator.StringToHash("Speed");

    private static readonly int IsGroundedHash =
        Animator.StringToHash("IsGrounded");

    private static readonly int JumpHash =
        Animator.StringToHash("Jump");

    private static readonly int Attack1Hash =
        Animator.StringToHash("Attack1");

    private static readonly int Attack2Hash =
        Animator.StringToHash("Attack2");

    // Animator State Hashes

    private static readonly int JumpStateHash =
        Animator.StringToHash("Jump");

    private static readonly int Attack1StateHash =
        Animator.StringToHash("Attack1");

    private static readonly int Attack2StateHash =
        Animator.StringToHash("Attack2");

    private static readonly int AttackTagHash =
        Animator.StringToHash("Attack");

    // =========================================================
    // REFERENCES
    // =========================================================

    [Header("References")]

    [SerializeField]
    private Animator animator;

    [SerializeField]
    private Transform groundCheck;

    [SerializeField]
    private LayerMask groundLayers;

    [SerializeField]
    private bool allowStandaloneInput = true;

    // =========================================================
    // MOVEMENT SPEEDS
    // =========================================================

    [Header("Movement Speeds")]

    [SerializeField, Min(0f)]
    private float walkSpeed = 2.2f;

    [SerializeField, Min(0f)]
    private float runSpeed = 5.6f;

    // =========================================================
    // MOVEMENT SMOOTHING
    // =========================================================

    [Header("Movement Smoothing")]

    [SerializeField, Min(0.01f)]
    private float accelerationSmoothTime = 0.12f;

    [SerializeField, Min(0.01f)]
    private float turnSmoothTime = 0.10f;

    [SerializeField, Min(0.01f)]
    private float attackTurnSmoothTime = 0.045f;

    [SerializeField, Min(0.01f)]
    private float animatorDampTime = 0.12f;

    // =========================================================
    // JUMP AND GROUNDING
    // =========================================================

    [Header("Jump and Grounding")]

    [SerializeField, Min(0.01f)]
    private float groundCheckRadius = 0.22f;

    [SerializeField, Min(0.1f)]
    private float jumpHeight = 1.65f;

    [Tooltip("Gravity must be negative.")]
    [SerializeField]
    private float gravity = -25f;

    [SerializeField, Min(0f)]
    private float groundStickForce = 2.5f;

    [SerializeField, Min(0.5f)]
    private float jumpLockSafetyTimeout = 5f;

    // =========================================================
    // ATTACK COMBO
    // =========================================================

    [Header("Attack Combo")]

    [Range(0f, 0.95f)]
    [SerializeField]
    private float comboWindowStart = 0.38f;

    [Range(0.05f, 1f)]
    [SerializeField]
    private float comboWindowEnd = 0.88f;

    [SerializeField, Min(0f)]
    private float attackStartGraceTime = 0.25f;

    [SerializeField, Min(0.1f)]
    private float attackEntryTimeout = 1f;

    [SerializeField, Min(1f)]
    private float attackSafetyTimeout = 5f;

    // =========================================================
    // PRIVATE REFERENCES
    // =========================================================

    private CharacterController _characterController;

    private MMOCameraController _cameraController;

    // =========================================================
    // PLAYER CONTROL STATE
    // =========================================================

    private bool _inputEnabled;

    private bool _isGrounded;

    // =========================================================
    // JUMP LOCK STATE
    // =========================================================

    private bool _jumpLocked;

    private bool _jumpAnimationEntered;

    private float _jumpLockStartedAt;

    // =========================================================
    // ATTACK STATE
    // =========================================================

    private bool _attackActive;

    private bool _attackStateEntered;

    private bool _comboQueued;

    private int _attackStep;

    private float _attackCommandTime;

    private float _attackSequenceStartedAt;

    private float _lastAttackStateSeenTime;

    // =========================================================
    // MOVEMENT VARIABLES
    // =========================================================

    private float _verticalVelocity;

    private float _currentMoveSpeed;

    private float _speedSmoothVelocity;

    private float _rotationSmoothVelocity;

    // =========================================================
    // NETWORK VARIABLES
    // =========================================================

    private int _networkActionCode;

    private int _networkActionSequence;

    private int _lastRemoteActionSequence = -1;

    private float _networkAnimationSpeed;

    // Input is sampled once per rendered frame, then consumed by Fusion's simulation tick.
    // This keeps the local owner and every remote client on the same movement timeline.
    private Vector2 _cachedMoveInput;
    private bool _cachedSprintHeld;
    private bool _cachedGameplayInputAllowed;
    private bool _jumpPressed;
    private bool _attackPressed;

    // =========================================================
    // ANIMATOR PARAMETER VALIDATION
    // =========================================================

    private bool _hasSpeedParameter;

    private bool _hasGroundedParameter;

    private bool _hasJumpParameter;

    private bool _hasAttack1Parameter;

    private bool _hasAttack2Parameter;

    // =========================================================
    // PUBLIC NETWORK PROPERTIES
    // =========================================================

    /// <summary>
    /// 0 = Locomotion
    /// 1 = Jump
    /// 2 = Attack1
    /// 3 = Attack2
    /// </summary>

    public int NetworkActionCode =>
        _networkActionCode;

    public int NetworkActionSequence =>
        _networkActionSequence;

    public float NetworkAnimationSpeed =>
        _networkAnimationSpeed;

    public bool NetworkIsGrounded =>
        _isGrounded;

    public bool IsLocalControl =>
        _inputEnabled;

    public bool IsJumpLocked =>
        _jumpLocked;

    public bool IsAttacking =>
        _attackActive;

    // =========================================================
    // AWAKE
    // =========================================================

    private void Awake()
    {
        _characterController =
            GetComponent<CharacterController>();

        if (animator == null)
        {
            animator =
                GetComponentInChildren<Animator>(true);
        }

        bool hasFusionDriver =
            GetComponent<MMOPlayerFusion>() != null;

        _inputEnabled =
            !hasFusionDriver && allowStandaloneInput;

        _characterController.enabled =
            !hasFusionDriver;

        if (animator == null)
        {
            Debug.LogError(
                "[MMOPlayerController] Animator was not found.",
                this
            );

            enabled = false;
            return;
        }

        // CharacterController controls root movement.
        animator.applyRootMotion = false;

        CacheAnimatorParameters();
    }

    // =========================================================
    // START
    // =========================================================

    private void Start()
    {
        if (_cameraController == null)
        {
            _cameraController =
                MMOCameraController.Instance;
        }

        if (_cameraController == null)
        {
            _cameraController =
                FindObjectOfType<MMOCameraController>();
        }

        if (_inputEnabled &&
            _cameraController != null)
        {
            _cameraController.SetTarget(transform);
        }
    }

    // =========================================================
    // UPDATE
    // =========================================================

    private void Update()
    {
        if (!_inputEnabled ||
            !_characterController.enabled ||
            animator == null)
        {
            return;
        }

        bool gameplayInputAllowed =
            _cameraController == null
                ? allowStandaloneInput
                : _cameraController.GameplayEnabled;

        // =====================================================
        // INPUT
        // =====================================================

        _cachedMoveInput = Vector2.zero;
        _cachedGameplayInputAllowed = gameplayInputAllowed;

        if (gameplayInputAllowed)
        {
            _cachedMoveInput.x =
                Input.GetAxisRaw("Horizontal");

            _cachedMoveInput.y =
                Input.GetAxisRaw("Vertical");

            _cachedMoveInput =
                Vector2.ClampMagnitude(_cachedMoveInput, 1f);

            _cachedSprintHeld = Input.GetKey(KeyCode.LeftShift);

            // Keep one-frame button presses until the next Fusion simulation tick.
            _jumpPressed |= Input.GetKeyDown(KeyCode.Space); // <--- จุดที่แก้ไข/เพิ่มเติม
            _attackPressed |= Input.GetMouseButtonDown(0) && CanStartAttackFromMouse(); // <--- จุดที่แก้ไข/เพิ่มเติม
        }
        else
        {
            _cachedSprintHeld = false;
        }
    }

    /// <summary>
    /// Runs only on the Fusion State Authority from FixedUpdateNetwork.
    /// Never move a NetworkTransform-owned character from Unity Update.
    /// </summary>
    public void SimulateNetworked(float deltaTime) // <--- จุดที่แก้ไข/เพิ่มเติม
    {
        if (!_inputEnabled || !_characterController.enabled || animator == null)
        {
            _jumpPressed = false;
            _attackPressed = false;
            return;
        }

        bool gameplayInputAllowed = _cachedGameplayInputAllowed;
        Vector2 moveInput = _cachedMoveInput;

        // =====================================================
        // CAMERA RELATIVE MOVEMENT
        // =====================================================

        Vector3 cameraForward =
            _cameraController != null
                ? _cameraController.PlanarAimDirection
                : transform.forward;

        Vector3 cameraRight =
            _cameraController != null
                ? _cameraController.PlanarAimRight
                : transform.right;

        Vector3 moveDirection =
            cameraForward * moveInput.y +
            cameraRight * moveInput.x;

        moveDirection.y = 0f;

        if (moveDirection.sqrMagnitude > 1f)
        {
            moveDirection.Normalize();
        }

        // =====================================================
        // GROUND CHECK
        // =====================================================

        UpdateGroundedState();

        UpdateJumpLock();

        // =====================================================
        // JUMP INPUT
        // =====================================================

        if (gameplayInputAllowed &&
            _jumpPressed &&
            _isGrounded &&
            !_attackActive &&
            !_jumpLocked)
        {
            BeginJump();
        }
        _jumpPressed = false; // <--- จุดที่แก้ไข/เพิ่มเติม

        // =====================================================
        // ATTACK INPUT
        // =====================================================

        if (gameplayInputAllowed && _attackPressed)
        {
            HandleAttackClick();
        }
        _attackPressed = false; // <--- จุดที่แก้ไข/เพิ่มเติม

        UpdateAttackComboState();

        // =====================================================
        // MOVEMENT MODE
        // =====================================================

        bool hasMoveInput =
            moveInput.sqrMagnitude > 0.0001f;

        bool isMovingBackward =
            moveInput.y < -0.1f;

        // Left Shift runs only when moving forward.
        // S always uses backward walking.

        bool shouldRun =
            gameplayInputAllowed &&
            _cachedSprintHeld &&
            moveInput.y > 0.1f;

        float desiredMoveSpeed =
            hasMoveInput
                ? (shouldRun ? runSpeed : walkSpeed)
                : 0f;

        // =====================================================
        // ATTACK MOVEMENT LOCK
        // =====================================================

        if (_attackActive)
        {
            _currentMoveSpeed = 0f;

            _speedSmoothVelocity = 0f;
        }
        else
        {
            _currentMoveSpeed =
                Mathf.SmoothDamp(
                    _currentMoveSpeed,
                    desiredMoveSpeed,
                    ref _speedSmoothVelocity,
                    accelerationSmoothTime,
                    Mathf.Infinity,
                    deltaTime
                );
        }

        // =====================================================
        // ROTATION
        // =====================================================

        UpdateFacing(
            moveDirection,
            hasMoveInput,
            isMovingBackward,
            deltaTime
        );

        // =====================================================
        // GRAVITY
        // =====================================================

        UpdateVerticalVelocity(deltaTime);

        // =====================================================
        // CHARACTER MOVEMENT
        // =====================================================

        Vector3 horizontalVelocity =
            _attackActive
                ? Vector3.zero
                : moveDirection * _currentMoveSpeed;

        Vector3 totalVelocity =
            horizontalVelocity +
            Vector3.up * _verticalVelocity;

        _characterController.Move(
            totalVelocity * deltaTime
        );

        // =====================================================
        // ANIMATION SPEED
        // =====================================================

        if (_attackActive || !hasMoveInput)
        {
            _networkAnimationSpeed = 0f;
        }
        else if (isMovingBackward)
        {
            // Negative value selects Backward Walk.
            _networkAnimationSpeed = -1f;
        }
        else
        {
            // 1 = Walk
            // 2 = Run

            _networkAnimationSpeed =
                shouldRun ? 2f : 1f;
        }

        if (_hasSpeedParameter)
        {
            animator.SetFloat(
                SpeedHash,
                _networkAnimationSpeed,
                animatorDampTime,
                deltaTime
            );
        }

        if (_hasGroundedParameter)
        {
            animator.SetBool(
                IsGroundedHash,
                _isGrounded
            );
        }
    }

    // =========================================================
    // GROUNDING
    // =========================================================

    private void UpdateGroundedState()
    {
        Vector3 checkPosition =
            groundCheck != null
                ? groundCheck.position
                : transform.position + Vector3.up * 0.1f;

        // Do not detect ground while rising.

        bool movingUpward =
            _verticalVelocity > 0.05f;

        if (movingUpward)
        {
            _isGrounded = false;
            return;
        }

        int effectiveGroundMask =
            groundLayers.value;

        // Exclude Player layer.

        int playerLayer =
            gameObject.layer;

        if (playerLayer >= 0 && playerLayer < 32)
        {
            effectiveGroundMask &=
                ~(1 << playerLayer);
        }

        bool sphereGrounded = false;

        if (effectiveGroundMask != 0)
        {
            sphereGrounded =
                Physics.CheckSphere(
                    checkPosition,
                    groundCheckRadius,
                    effectiveGroundMask,
                    QueryTriggerInteraction.Ignore
                );
        }

        _isGrounded =
            sphereGrounded ||
            _characterController.isGrounded;
    }

    // =========================================================
    // GRAVITY
    // =========================================================

    private void UpdateVerticalVelocity(float deltaTime)
    {
        float downwardGravity =
            -Mathf.Abs(gravity);

        if (_isGrounded)
        {
            if (_verticalVelocity < 0f)
            {
                _verticalVelocity =
                    -groundStickForce;
            }
        }
        else
        {
            _verticalVelocity +=
                downwardGravity * deltaTime;
        }
    }

    // =========================================================
    // JUMP
    // =========================================================

    private void BeginJump()
    {
        if (_jumpLocked ||
            !_isGrounded ||
            _attackActive)
        {
            return;
        }

        _jumpLocked = true;

        _jumpAnimationEntered = false;

        _jumpLockStartedAt = Time.time;

        _isGrounded = false;

        _verticalVelocity =
            Mathf.Sqrt(
                jumpHeight *
                2f *
                Mathf.Abs(gravity)
            );

        if (_hasGroundedParameter)
        {
            animator.SetBool(
                IsGroundedHash,
                false
            );
        }

        _networkActionCode = 1;

        _networkActionSequence++;

        if (_hasJumpParameter)
        {
            animator.ResetTrigger(JumpHash);

            animator.SetTrigger(JumpHash);
        }
    }

    private bool IsJumpAnimationPlaying()
    {
        AnimatorStateInfo current =
            animator.GetCurrentAnimatorStateInfo(0);

        if (current.shortNameHash == JumpStateHash)
        {
            return true;
        }

        if (animator.IsInTransition(0))
        {
            AnimatorStateInfo next =
                animator.GetNextAnimatorStateInfo(0);

            if (next.shortNameHash == JumpStateHash)
            {
                return true;
            }
        }

        return false;
    }

    private void UpdateJumpLock()
    {
        if (!_jumpLocked)
        {
            return;
        }

        bool jumpAnimationPlaying =
            IsJumpAnimationPlaying();

        if (jumpAnimationPlaying)
        {
            _jumpAnimationEntered = true;
        }

        bool grounded =
            _isGrounded ||
            _characterController.isGrounded;

        float lockElapsed =
            Time.time - _jumpLockStartedAt;

        // Normal unlock.
        // Character must land and leave Jump state.

        if (grounded &&
            _jumpAnimationEntered &&
            !jumpAnimationPlaying &&
            !animator.IsInTransition(0))
        {
            UnlockJump();
            return;
        }

        // Safety fallback only while grounded.

        if (grounded &&
            lockElapsed >= jumpLockSafetyTimeout)
        {
            Debug.LogWarning(
                "[MMOPlayerController] Jump lock safety timeout.",
                this
            );

            if (jumpAnimationPlaying ||
                animator.IsInTransition(0))
            {
                animator.CrossFade(
                    "Locomotion",
                    0.12f,
                    0
                );
            }

            UnlockJump();
        }
    }

    private void UnlockJump()
    {
        _jumpLocked = false;

        _jumpAnimationEntered = false;

        if (_hasJumpParameter)
        {
            animator.ResetTrigger(JumpHash);
        }

        if (_networkActionCode == 1)
        {
            _networkActionCode = 0;
        }
    }

    // =========================================================
    // ATTACK INPUT
    // =========================================================

    private bool CanStartAttackFromMouse()
    {
        if (Input.GetKey(KeyCode.LeftAlt) ||
            !GameplayInputAllowedForAttack())
        {
            return false;
        }

        if (_cameraController != null)
        {
            return _cameraController.IsGameplayInputLocked;
        }

        return Cursor.lockState ==
               CursorLockMode.Locked;
    }

    private bool GameplayInputAllowedForAttack()
    {
        return _cameraController == null ||
               _cameraController.GameplayEnabled;
    }

    private void HandleAttackClick()
    {
        if (!_attackActive)
        {
            if (_isGrounded && !_jumpLocked)
            {
                BeginAttackOne();
            }

            return;
        }

        if (_attackStep != 1)
        {
            return;
        }

        if (TryGetAttackOneNormalizedTime(
            out float normalizedTime))
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
        else if (Time.time - _attackCommandTime <=
                 attackStartGraceTime)
        {
            _comboQueued = true;
        }
    }

    // =========================================================
    // ATTACK ONE
    // =========================================================

    private void BeginAttackOne()
    {
        if (_attackActive ||
            _jumpLocked ||
            !_isGrounded)
        {
            return;
        }

        _attackActive = true;

        _attackStateEntered = false;

        _comboQueued = false;

        _attackStep = 1;

        _attackCommandTime = Time.time;

        _attackSequenceStartedAt = Time.time;

        _lastAttackStateSeenTime = Time.time;

        _networkActionCode = 2;

        _networkActionSequence++;

        if (_hasAttack1Parameter)
        {
            animator.ResetTrigger(Attack1Hash);

            animator.ResetTrigger(Attack2Hash);

            animator.SetTrigger(Attack1Hash);
        }

        _currentMoveSpeed = 0f;

        _speedSmoothVelocity = 0f;

        if (_hasSpeedParameter)
        {
            animator.SetFloat(
                SpeedHash,
                0f
            );
        }
    }

    // =========================================================
    // ATTACK TWO
    // =========================================================

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

        _currentMoveSpeed = 0f;

        _speedSmoothVelocity = 0f;
    }

    // =========================================================
    // ATTACK COMBO STATE
    // =========================================================

    private void UpdateAttackComboState()
    {
        if (!_attackActive)
        {
            return;
        }

        bool attackStateIsPlaying =
            TryGetAttackStateInfo(out _);

        if (attackStateIsPlaying)
        {
            _attackStateEntered = true;

            _lastAttackStateSeenTime = Time.time;
        }

        // Buffered second click.

        if (_attackStep == 1 &&
            TryGetAttackOneNormalizedTime(
                out float normalizedTime))
        {
            if (_comboQueued &&
                normalizedTime >= comboWindowStart &&
                normalizedTime <= comboWindowEnd)
            {
                BeginAttackTwo();

                attackStateIsPlaying =
                    TryGetAttackStateInfo(out _);
            }
            else if (normalizedTime > comboWindowEnd)
            {
                _comboQueued = false;
            }
        }

        float elapsed =
            Time.time - _attackSequenceStartedAt;

        if (!_attackStateEntered)
        {
            if (elapsed >= attackEntryTimeout)
            {
                Debug.LogWarning(
                    "[MMOPlayerController] Attack Animator state was not entered.",
                    this
                );

                FinishAttack();
            }

            return;
        }

        if (elapsed >= attackSafetyTimeout)
        {
            Debug.LogWarning(
                "[MMOPlayerController] Attack safety timeout.",
                this
            );

            animator.CrossFade(
                "Locomotion",
                0.12f,
                0
            );

            FinishAttack();

            return;
        }

        // Only unlock after leaving both attack states.

        if (!attackStateIsPlaying &&
            !animator.IsInTransition(0))
        {
            FinishAttack();
        }
    }

    // =========================================================
    // CHECK ATTACK STATE
    // =========================================================

    private bool IsAttackState(
        AnimatorStateInfo stateInfo)
    {
        return stateInfo.tagHash == AttackTagHash ||
               stateInfo.shortNameHash == Attack1StateHash ||
               stateInfo.shortNameHash == Attack2StateHash;
    }

    private bool TryGetAttackStateInfo(
        out AnimatorStateInfo stateInfo)
    {
        AnimatorStateInfo current =
            animator.GetCurrentAnimatorStateInfo(0);

        if (IsAttackState(current))
        {
            stateInfo = current;
            return true;
        }

        if (animator.IsInTransition(0))
        {
            AnimatorStateInfo next =
                animator.GetNextAnimatorStateInfo(0);

            if (IsAttackState(next))
            {
                stateInfo = next;
                return true;
            }
        }

        stateInfo = default;

        return false;
    }

    private bool TryGetAttackOneNormalizedTime(
        out float normalizedTime)
    {
        AnimatorStateInfo current =
            animator.GetCurrentAnimatorStateInfo(0);

        if (current.shortNameHash == Attack1StateHash)
        {
            normalizedTime = current.normalizedTime;

            return true;
        }

        if (animator.IsInTransition(0))
        {
            AnimatorStateInfo next =
                animator.GetNextAnimatorStateInfo(0);

            if (next.shortNameHash == Attack1StateHash)
            {
                normalizedTime = next.normalizedTime;

                return true;
            }
        }

        normalizedTime = 0f;

        return false;
    }

    // =========================================================
    // FINISH ATTACK
    // =========================================================

    private void FinishAttack()
    {
        _attackActive = false;

        _attackStateEntered = false;

        _comboQueued = false;

        _attackStep = 0;

        _networkActionCode = 0;

        _currentMoveSpeed = 0f;

        _speedSmoothVelocity = 0f;

        if (_hasAttack1Parameter)
        {
            animator.ResetTrigger(Attack1Hash);
        }

        if (_hasAttack2Parameter)
        {
            animator.ResetTrigger(Attack2Hash);
        }

        if (_hasSpeedParameter)
        {
            animator.SetFloat(
                SpeedHash,
                0f,
                animatorDampTime,
                Time.deltaTime
            );
        }
    }

    // =========================================================
    // FACING AND ROTATION
    // =========================================================

    private void UpdateFacing(
        Vector3 moveDirection,
        bool hasMoveInput,
        bool isMovingBackward,
        float deltaTime)
    {
        Vector3 facingDirection = Vector3.zero;

        if (_attackActive)
        {
            facingDirection =
                _cameraController != null
                    ? _cameraController.PlanarAimDirection
                    : transform.forward;
        }
        else if (hasMoveInput)
        {
            if (isMovingBackward)
            {
                // Backward walking keeps the character facing forward.

                facingDirection =
                    _cameraController != null
                        ? _cameraController.PlanarAimDirection
                        : transform.forward;
            }
            else
            {
                bool actionCamera =
                    _cameraController != null &&
                    _cameraController.CurrentPreset ==
                    MMOCameraController.CameraPreset.ActionCombat;

                facingDirection =
                    actionCamera &&
                    _cameraController.GameplayEnabled
                        ? _cameraController.PlanarAimDirection
                        : moveDirection;
            }
        }

        facingDirection.y = 0f;

        if (facingDirection.sqrMagnitude < 0.0001f)
        {
            return;
        }

        float targetAngle =
            Mathf.Atan2(
                facingDirection.x,
                facingDirection.z
            ) * Mathf.Rad2Deg;

        float smoothTime =
            _attackActive
                ? attackTurnSmoothTime
                : turnSmoothTime;

        float currentAngle =
            transform.eulerAngles.y;

        float smoothedAngle =
            Mathf.SmoothDampAngle(
                currentAngle,
                targetAngle,
                ref _rotationSmoothVelocity,
                smoothTime,
                Mathf.Infinity,
                deltaTime
            );

        transform.rotation =
            Quaternion.Euler(
                0f,
                smoothedAngle,
                0f
            );
    }

    // =========================================================
    // LOCAL / REMOTE AUTHORITY
    // =========================================================

    public void SetLocalAuthority(
        bool isLocal,
        MMOCameraController cameraController)
    {
        _inputEnabled = isLocal;

        if (!isLocal)
        {
            _cachedMoveInput = Vector2.zero; // <--- จุดที่แก้ไข/เพิ่มเติม
            _cachedSprintHeld = false;
            _jumpPressed = false;
            _attackPressed = false;
        }

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

    // =========================================================
    // REMOTE PLAYER ANIMATION
    // =========================================================

    public void ApplyNetworkAnimation(
        float speed,
        bool grounded,
        int actionCode,
        int actionSequence)
    {
        if (_inputEnabled || animator == null)
        {
            return;
        }

        if (_hasSpeedParameter)
        {
            animator.SetFloat(
                SpeedHash,
                Mathf.Clamp(speed, -1f, 2f),
                animatorDampTime,
                Time.deltaTime
            );
        }

        if (_hasGroundedParameter)
        {
            animator.SetBool(
                IsGroundedHash,
                grounded
            );
        }

        // Prevent repeating one-shot animation events.

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

    // =========================================================
    // ANIMATOR PARAMETER VALIDATION
    // =========================================================

    private void CacheAnimatorParameters()
    {
        _hasSpeedParameter =
            HasParameter(
                SpeedHash,
                AnimatorControllerParameterType.Float
            );

        _hasGroundedParameter =
            HasParameter(
                IsGroundedHash,
                AnimatorControllerParameterType.Bool
            );

        _hasJumpParameter =
            HasParameter(
                JumpHash,
                AnimatorControllerParameterType.Trigger
            );

        _hasAttack1Parameter =
            HasParameter(
                Attack1Hash,
                AnimatorControllerParameterType.Trigger
            );

        _hasAttack2Parameter =
            HasParameter(
                Attack2Hash,
                AnimatorControllerParameterType.Trigger
            );

        if (!_hasSpeedParameter ||
            !_hasGroundedParameter ||
            !_hasJumpParameter ||
            !_hasAttack1Parameter ||
            !_hasAttack2Parameter)
        {
            Debug.LogError(
                "[MMOPlayerController] Animator parameters must be " +
                "Speed (Float), IsGrounded (Bool), Jump (Trigger), " +
                "Attack1 (Trigger), and Attack2 (Trigger).",
                this
            );
        }
    }

    private bool HasParameter(
        int parameterHash,
        AnimatorControllerParameterType parameterType)
    {
        if (animator == null)
        {
            return false;
        }

        AnimatorControllerParameter[] parameters =
            animator.parameters;

        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].nameHash == parameterHash &&
                parameters[i].type == parameterType)
            {
                return true;
            }
        }

        return false;
    }

    // =========================================================
    // INSPECTOR VALIDATION
    // =========================================================

    private void OnValidate()
    {
        walkSpeed = Mathf.Max(0f, walkSpeed);

        runSpeed = Mathf.Max(walkSpeed, runSpeed);

        jumpHeight = Mathf.Max(0.1f, jumpHeight);

        if (gravity >= 0f)
        {
            gravity = -Mathf.Max(
                9.81f,
                Mathf.Abs(gravity)
            );
        }

        groundCheckRadius =
            Mathf.Max(0.01f, groundCheckRadius);

        jumpLockSafetyTimeout =
            Mathf.Max(0.5f, jumpLockSafetyTimeout);

        attackEntryTimeout =
            Mathf.Max(0.1f, attackEntryTimeout);

        attackSafetyTimeout =
            Mathf.Max(1f, attackSafetyTimeout);

        comboWindowStart =
            Mathf.Clamp(comboWindowStart, 0f, 0.95f);

        comboWindowEnd =
            Mathf.Clamp(
                comboWindowEnd,
                comboWindowStart + 0.05f,
                1f
            );
    }

    // =========================================================
    // GROUND CHECK GIZMOS
    // =========================================================

    private void OnDrawGizmosSelected()
    {
        Vector3 checkPosition =
            groundCheck != null
                ? groundCheck.position
                : transform.position + Vector3.up * 0.1f;

        Gizmos.color = Color.yellow;

        Gizmos.DrawWireSphere(
            checkPosition,
            groundCheckRadius
        );
    }
}
