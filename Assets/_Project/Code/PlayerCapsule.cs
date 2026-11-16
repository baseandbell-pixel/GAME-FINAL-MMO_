using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class PlayerCapsule : NetworkBehaviour
{
    public Renderer bodyRenderer;
    public TMP_Text nameLabel;
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

    Rigidbody rb;
    CapsuleCollider collider;


    Vector2 moveInput;
    bool sprintHeld;

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

        if (bodyRenderer == null) bodyRenderer = GetComponentInChildren<Renderer>();
        if (animator == null) animator = GetComponentInChildren<Animator>();

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

            // The profile travelled from the lobby inside GoogleAPI (DontDestroyOnLoad),
            // so the values we publish here are the same ones the lobby card showed.
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
    // These are the messages PlayerInput sends when its Behavior is "Send Messages".
    // The action names come from InputSystem_Actions (Player map): Move, Jump, Sprint.

    public void OnMove(InputValue value)
    {
        moveInput = value.Get<Vector2>();
    }

    public void OnSprint(InputValue value)
    {
        sprintHeld = value.isPressed;
    }

    /// <summary>True only for the copy this peer may drive. In Shared Mode this is the locally spawned object.</summary>
    public bool IsLocallyControlled => Object != null && (Object.HasInputAuthority || (Runner != null && Runner.GameMode == GameMode.Shared && Object.HasStateAuthority));

    private void Update()
    {
        if (!isOwner || !Application.isFocused || GameplayInput.IsBlocked) return;

        // PlayerInput messages remain supported, but direct Input System polling makes movement
        // deterministic even when a spawned prefab's PlayerInput device pairing is not assigned.
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

        var wanted = MovementLocked ? Vector3.zero : new Vector3(moveInput.x, 0f, moveInput.y);
        if (MovementReference != null) wanted = Quaternion.Euler(0f, MovementReference.eulerAngles.y, 0f) * wanted;
        if (wanted.sqrMagnitude > 1f) wanted.Normalize();

        float speed = moveSpeed * (sprintHeld ? 4f : 1f);
        Vector3 wantedVelocity = wanted * speed;
        Vector3 velocity = rb.linearVelocity;
        Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
        horizontal = Vector3.MoveTowards(horizontal, wantedVelocity, acceleration * Time.fixedDeltaTime);
        rb.linearVelocity = new Vector3(horizontal.x, velocity.y, horizontal.z);

        if (FacingTarget != null && !MovementLocked)
        {
            Vector3 toTarget = FacingTarget.position - rb.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.0001f)
                rb.MoveRotation(Quaternion.Slerp(rb.rotation, Quaternion.LookRotation(toTarget, Vector3.up), rotationSpeed * Time.fixedDeltaTime));
        }
        else if (rotateTowardsMovement && wanted.sqrMagnitude > 0.0001f)
        {
            var look = Quaternion.LookRotation(wanted, Vector3.up);
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, look, rotationSpeed * Time.fixedDeltaTime));
        }

        // State Authority writes the replicated values; in Shared Mode this is always the peer
        // that owns this avatar. Proxies consume them in Render for synchronized locomotion.
        if (Object.HasStateAuthority)
        {
            Vector3 local = Quaternion.Inverse(transform.rotation) * wanted;
            NetworkMove = new Vector2(local.x, local.z);
            NetworkSprinting = sprintHeld;
        }
    }

    public override void Render()
    {
        ApplyLook(force: false);
        ApplyAnimator(NetworkMove, NetworkSprinting);
    }

    private void ApplyAnimator(Vector2 move, bool sprint)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        // Supports a 2D Freeform Directional blend tree: MoveX/MoveY cover all 8 directions.
        animator.SetFloat(moveXParameter, move.x, 0.08f, Time.deltaTime);
        animator.SetFloat(moveYParameter, move.y, 0.08f, Time.deltaTime);
        animator.SetBool(sprintParameter, sprint);
    }
    void SetUpNameLabel()
    {
        if (nameLabel == null) return;

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
