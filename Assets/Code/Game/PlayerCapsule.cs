using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class PlayerCapsule : NetworkBehaviour
{
    public Renderer bodyRenderer;
    public TMP_Text nameLabel;

    public float moveSpeed = 6f;
    public float runSpeed = 1.6f;
    public float acceleration = 40f;
    public float rotationSpeed = 12f;


    public bool detachNameLabel = true;
    public bool rotateTowardsMovement = true;


    /// <summary>Which of the 4 avatars this player picked - drives the capsule colour.</summary>
    [Networked] public int AvatarIndex { get; set; }
    [Networked] public NetworkString<_16> Nick { get; set; }

    /// <summary>The capsule the local player is allowed to move.</summary>
    public static PlayerCapsule Local { get; private set; }

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

        // A capsule that tips over is not what we want; rotation is done by script.
        rb.freezeRotation = true;
    }

    public override void Spawned()
    {
        bool Owner = Object.HasStateAuthority;
        isOwner = Owner;

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


    // ------------------------------------------------------------- movement

    void FixedUpdate()
    {
        if (!isOwner) return;

        var wanted = new Vector3(moveInput.x, 0f, moveInput.y);
        if (wanted.sqrMagnitude > 1f) wanted.Normalize();

        float speed = moveSpeed * (sprintHeld ? 4f : 1f);
        Vector3 wantedVelocity = wanted * speed;
        Vector3 velocity = rb.linearVelocity;
        Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
        horizontal = Vector3.MoveTowards(horizontal, wantedVelocity, acceleration * Time.fixedDeltaTime);
        rb.linearVelocity = new Vector3(horizontal.x, velocity.y, horizontal.z);

        if (rotateTowardsMovement && wanted.sqrMagnitude > 0.0001f)
        {
            var look = Quaternion.LookRotation(wanted, Vector3.up);
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, look, rotationSpeed * Time.fixedDeltaTime));
        }
    }

    public override void Render()
    {
        ApplyLook(force: false);
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
