using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class PlayerCapsule : NetworkBehaviour
{
    public Renderer bodyRenderer;
    public TMP_Text nameLabel;
    [Header("Character visual")]
    [Tooltip("Optional rigged model child (e.g. CharacterVisual). When set, the legacy capsule renderer stays hidden and the model is shown instead.")]
    [SerializeField] private Transform characterVisual;
    [Header("Optional animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private string moveXParameter = "MoveX";
    [SerializeField] private string moveYParameter = "MoveY";
    [SerializeField] private string sprintParameter = "IsSprinting";

    public float moveSpeed = 6f;
    public float runSpeed = 1.6f;
    public float acceleration = 40f;
    public float rotationSpeed = 12f;

    public bool detachNameLabel = true;
    public bool rotateTowardsMovement = true;
    [Tooltip("When true and an action camera (MovementReference) is active, the character faces the camera yaw while moving so WASD drives full 8-directional strafing/locomotion.")]
    public bool strafeInActionCamera = true;

    /// <summary>Which of the 4 avatars this player picked - drives the capsule colour.</summary>
    [Networked] public int AvatarIndex { get; set; }
    [Networked] public NetworkString<_16> Nick { get; set; }
    /// <summary>Replicated locomotion values for an 8-direction animator on every peer.</summary>
    [Networked] public Vector2 NetworkMove { get; set; }
    [Networked] public NetworkBool NetworkSprinting { get; set; }

    /// <summary>The capsule the local player is allowed to move.</summary>
    public static PlayerCapsule Local { get; private set; }

    /// <summary>When true the capsule ignores movement input (set by PlayerCombat while dead).</summary>
    public bool MovementLocked { get; set; }

    /// <summary>Optional. When set (action camera), WASD is interpreted relative to this transform's yaw. Null = world axes (legacy).</summary>
    public Transform MovementReference { get; set; }

    /// <summary>Optional. When set (target lock), the capsule faces this point instead of its movement direction, so movement becomes strafing.</summary>
    public Transform FacingTarget { get; set; }

    /// <summary>Scales walk/run speed (e.g. PlayerCombat slows the player while blocking). 1 = normal.</summary>
    public float SpeedMultiplier { get; set; } = 1f;

    /// <summary>The rigged model child, or null when the player is still a plain capsule.</summary>
    public Transform CharacterVisual => characterVisual;

    /// <summary>True while a dodge dash overrides normal movement.</summary>
    public bool IsDashing => Time.time < _dashUntil;

    /// <summary>Current networked stamina (mirrored from PlayerCombat).</summary>
    public float CurrentStamina => _combat != null ? _combat.NetworkStamina : 100f;
    public float MaxStamina => _combat != null ? _combat.MaxStamina : 100f;

    /// <summary>Base movement speed plus gear/stat agility bonus.</summary>
    public float TotalMoveSpeed => moveSpeed + (_combat != null ? _combat.TotalMoveSpeedBonus : 0f);

    Rigidbody rb;
    PlayerCombat _combat;
    PlayerAnimationDriver _animationDriver;
    Vector3 _dashVelocity;
    float _dashUntil;
    CapsuleCollider collider;

    Vector2 moveInput;
    bool sprintHeld;
    float _overrideMoveUntil;

    bool isOwner;
    MaterialPropertyBlock _mpb;
    Transform _labelTransform;
    Vector3 _labelOffset = new Vector3(0f, 2f, 0f);
    int _appliedAvatar = -1;
    string _appliedNick;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        collider = GetComponent<CapsuleCollider>();
        _combat = GetComponent<PlayerCombat>();

        // Legacy fallback only: with a character model the first child renderer could be the model or a sword.
        if (bodyRenderer == null && characterVisual == null) bodyRenderer = GetComponentInChildren<Renderer>();
        if (animator == null) animator = characterVisual != null ? characterVisual.GetComponentInChildren<Animator>() : GetComponentInChildren<Animator>();
        _animationDriver = GetComponent<PlayerAnimationDriver>();

        // With a model, the primitive capsule mesh is only a collider placeholder.
        if (characterVisual != null && bodyRenderer != null) bodyRenderer.enabled = false;

        // A capsule that tips over is not what we want; rotation is done by script.
        rb.freezeRotation = true;
    }

    public override void Spawned()
    {
        // Shared Mode grants the spawning peer State Authority. Some connection paths also expose
        // Input Authority, so accept either; never make movement depend on master-client status.
        isOwner = IsLocallyControlled;

        if (isOwner)
        {
            Local = this;

            int avatar = 0;
            string nick = "Player";
            if (GoogleAPI.instance != null)
            {
                avatar = GoogleAPI.instance.avatarId;
                nick = GoogleAPI.ClampName(GoogleAPI.instance.username);
            }

            AvatarIndex = Mathf.Clamp(avatar, 0, LobbyNetwork.AvatarCount - 1);
            Nick = nick;

            Runner.SetPlayerObject(Runner.LocalPlayer, Object);
        }
        rb.isKinematic = !isOwner;
        rb.interpolation = isOwner ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
        Debug.Log($"[PlayerCapsule] Spawned {name}; local={isOwner}, stateAuthority={Object.HasStateAuthority}, inputAuthority={Object.HasInputAuthority}, mode={Runner.GameMode}.");

        SetUpNameLabel();
        ApplyLook(force: true);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Local == this) Local = null;
        DestroyDetachedLabel();
    }

    void OnDestroy()
    {
        DestroyDetachedLabel();
    }

    // ---------------------------------------------------------------- input

    public void OnMove(InputValue value)
    {
        moveInput = value.Get<Vector2>();
    }

    public void OnSprint(InputValue value)
    {
        sprintHeld = value.isPressed;
    }

    /// <summary>Sets movement and sprint input directly (used by Input messages or automated tests).</summary>
    public void SetMoveInput(Vector2 move, bool sprint = false, float holdSeconds = 0.25f)
    {
        moveInput = Vector2.ClampMagnitude(move, 1f);
        sprintHeld = sprint;
        _overrideMoveUntil = holdSeconds > 0f ? Time.time + holdSeconds : 0f;
    }

    Vector2 _localMoveNormalized;
    bool _localEffectiveSprint;

    public Vector2 EffectiveMove => (Object != null && Object.IsValid) ? NetworkMove : _localMoveNormalized;
    public bool EffectiveSprinting => (Object != null && Object.IsValid) ? (bool)NetworkSprinting : _localEffectiveSprint;

    /// <summary>True only for the copy this peer may drive. In Shared Mode this is the locally spawned object; in offline fallback Object is null/invalid.</summary>
    public bool IsLocallyControlled => Object == null || !Object.IsValid || Object.HasInputAuthority || (Runner != null && Runner.GameMode == GameMode.Shared && Object.HasStateAuthority);

    void Start()
    {
        if (Object == null || !Object.IsValid)
        {
            isOwner = true;
            if (Local == null) Local = this;
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
            }
        }
    }

    private void Update()
    {
        if (!isOwner || !Application.isFocused || GameplayInput.IsBlocked || Time.time < _overrideMoveUntil) return;

        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            Vector2 direct = new Vector2(
                (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1f : 0f),
                (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1f : 0f));
            moveInput = direct;
            sprintHeld = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
        }

        var gamepad = Gamepad.current;
        if (gamepad != null && gamepad.leftStick.ReadValue().sqrMagnitude > 0.01f)
            moveInput = gamepad.leftStick.ReadValue();
    }

    // ------------------------------------------------------------- movement

    void FixedUpdate()
    {
        if (!isOwner) return;

        if (IsDashing)
        {
            Vector3 v = rb.linearVelocity;
            rb.linearVelocity = new Vector3(_dashVelocity.x, v.y, _dashVelocity.z);
            return;
        }

        var wanted = MovementLocked ? Vector3.zero : DesiredMoveWorld();
        bool isMoving = wanted.sqrMagnitude > 0.001f;

        // Stamina gate for sprinting: sprinting requires available stamina and drains it over time.
        bool canSprint = _combat == null || (_combat.CanSprint && !_combat.IsBlocking);
        bool effectiveSprint = sprintHeld && isMoving && canSprint;
        if (effectiveSprint && _combat != null)
        {
            effectiveSprint = _combat.ConsumeSprintStamina(Time.fixedDeltaTime);
        }

        float speed = TotalMoveSpeed * (effectiveSprint ? 2.65f : 1f) * Mathf.Max(0f, SpeedMultiplier);
        Vector3 wantedVelocity = wanted * speed;
        Vector3 velocity = rb.linearVelocity;
        Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
        horizontal = Vector3.MoveTowards(horizontal, wantedVelocity, acceleration * Time.fixedDeltaTime);
        rb.linearVelocity = new Vector3(horizontal.x, velocity.y, horizontal.z);

        Quaternion targetRotation = rb.rotation;
        if (FacingTarget != null && !MovementLocked)
        {
            Vector3 toTarget = FacingTarget.position - rb.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.0001f)
            {
                targetRotation = Quaternion.LookRotation(toTarget, Vector3.up);
                rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime));
            }
        }
        else if (MovementReference != null && strafeInActionCamera && wanted.sqrMagnitude > 0.0001f)
        {
            targetRotation = Quaternion.Euler(0f, MovementReference.eulerAngles.y, 0f);
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime));
        }
        else if (rotateTowardsMovement && wanted.sqrMagnitude > 0.0001f)
        {
            targetRotation = Quaternion.LookRotation(wanted, Vector3.up);
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime));
        }

        Vector3 refSpace = (MovementReference != null && strafeInActionCamera && FacingTarget == null)
            ? Quaternion.Inverse(Quaternion.Euler(0f, MovementReference.eulerAngles.y, 0f)) * wanted
            : Quaternion.Inverse(transform.rotation) * wanted;
        _localMoveNormalized = new Vector2(refSpace.x, refSpace.z);
        _localEffectiveSprint = effectiveSprint;

        // State Authority writes the replicated values; in Shared Mode this is always the peer
        // that owns this avatar. Proxies consume them in Render for synchronized locomotion.
        if (Object != null && Object.IsValid && Object.HasStateAuthority)
        {
            NetworkMove = _localMoveNormalized;
            NetworkSprinting = effectiveSprint;
        }
    }

    /// <summary>Owner only: the current WASD/stick direction in world space (camera-relative when a MovementReference is set), magnitude 0..1.</summary>
    public Vector3 DesiredMoveWorld()
    {
        var wanted = new Vector3(moveInput.x, 0f, moveInput.y);
        if (MovementReference != null) wanted = Quaternion.Euler(0f, MovementReference.eulerAngles.y, 0f) * wanted;
        if (wanted.sqrMagnitude > 1f) wanted.Normalize();
        return wanted;
    }

    /// <summary>Owner only: overrides horizontal velocity for <paramref name="duration"/> seconds (dodge roll).</summary>
    public void StartDash(Vector3 worldDirection, float distance, float duration)
    {
        if (!isOwner || duration <= 0f) return;
        worldDirection.y = 0f;
        if (worldDirection.sqrMagnitude < 0.0001f) worldDirection = transform.forward;
        _dashVelocity = worldDirection.normalized * (distance / duration);
        _dashUntil = Time.time + duration;
    }

    /// <summary>Death hide/show. A rigged character stays visible (it plays its death animation); the legacy capsule is hidden.</summary>
    public void SetBodyVisible(bool visible)
    {
        if (characterVisual != null) return;
        if (bodyRenderer != null) bodyRenderer.enabled = visible;
    }

    public override void Render()
    {
        ApplyLook(force: false);
        if (_animationDriver == null) ApplyAnimator(NetworkMove, NetworkSprinting);
    }

    private void ApplyAnimator(Vector2 move, bool sprint)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        animator.SetFloat(moveXParameter, move.x, 0.08f, Time.deltaTime);
        animator.SetFloat(moveYParameter, move.y, 0.08f, Time.deltaTime);
        animator.SetBool(sprintParameter, sprint);
    }

    void SetUpNameLabel()
    {
        if (nameLabel == null) return;

        if (nameLabel.fontSize > 3.2f)
            nameLabel.fontSize = 3.2f;

        _labelTransform = nameLabel.transform;
        _labelOffset = _labelTransform.localPosition;
        if (detachNameLabel) _labelTransform.SetParent(null, worldPositionStays: true);
    }

    void LateUpdate()
    {
        if (_labelTransform == null) return;
        _labelTransform.position = transform.position + _labelOffset;
        var cam = Camera.main;
        if (cam != null) _labelTransform.rotation = cam.transform.rotation;
    }

    void DestroyDetachedLabel()
    {
        if (detachNameLabel && _labelTransform != null) Destroy(_labelTransform.gameObject);
        _labelTransform = null;
    }

    void ApplyLook(bool force)
    {
        if (force || _appliedAvatar != AvatarIndex)
        {
            _appliedAvatar = AvatarIndex;
            var color = AvatarPalette.Get(AvatarIndex);

            if (bodyRenderer != null)
            {
                _mpb ??= new MaterialPropertyBlock();
                bodyRenderer.GetPropertyBlock(_mpb);
                _mpb.SetColor("_BaseColor", color);
                _mpb.SetColor("_Color", color);
                bodyRenderer.SetPropertyBlock(_mpb);
            }
            if (nameLabel != null) nameLabel.color = color;
        }

        string nick = Nick.Value;
        if (nameLabel != null && (force || _appliedNick != nick))
        {
            _appliedNick = nick;
            nameLabel.text = Object.HasStateAuthority ? $"{nick} (You)" : nick;
        }
    }
}
