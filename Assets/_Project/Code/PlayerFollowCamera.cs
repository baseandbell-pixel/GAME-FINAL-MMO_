using UnityEngine;
using UnityEngine.InputSystem;

public enum CameraMode
{
    /// <summary>Original fixed-offset follow camera (default, unchanged behaviour).</summary>
    Legacy,
    /// <summary>Over-the-shoulder orbit camera driven by the mouse.</summary>
    FreeLook,
    /// <summary>Camera keeps the player and the locked target in frame.</summary>
    TargetLock,
    Tactical
}

/// <summary>
/// Third-person camera. Legacy mode is the original smoothed follow camera. Enabling "useActionCamera" switches to an
/// Action-RPG over-the-shoulder camera with mouse look, collision avoidance and a target-lock mode.
///
/// In action mode the camera also becomes the movement frame (PlayerCapsule.MovementReference) so WASD is camera-relative,
/// and it owns the cursor: locked while playing, freed while a UI is open (GameplayInput) or after pressing Escape
/// (click the game view to capture it again).
/// </summary>
public class PlayerFollowCamera : MonoBehaviour
{
    // ---------------- legacy (kept exactly as before) ----------------
    public Transform target;
    public Vector3 offset = new Vector3(0f, 8f, -8f);
    public float smoothing = 8f;

    // ---------------- action camera ----------------
    [Header("Action camera")]
    public bool useActionCamera = false;
    [Min(0.5f)] public float distance = 4.5f;
    public float minDistance = 1.2f;
    public float maxDistance = 8f;
    public float pivotHeight = 1.6f;
    [Tooltip("Positive = camera sits over the right shoulder.")]
    public float shoulderOffset = 0.65f;
    public float pivotSmoothTime = 0.06f;

    [Header("Free look")]
    public float mouseSensitivity = 0.12f;
    public bool invertY = false;
    public float minPitch = -25f;
    public float maxPitch = 65f;
    public float defaultPitch = 15f;
    [Tooltip("Optional ARPG style: rotate only while Right Mouse is held. Off keeps continuous free-look after the cursor is captured.")]
    public bool holdRightMouseToLook = false;

    [Header("Tactical 2.5D")]
    public Key tacticalToggleKey = Key.V;
    [Min(12f)] public float tacticalHeight = 20f;
    [Min(8f)] public float tacticalDistance = 16f;
    [Range(30f, 80f)] public float tacticalPitch = 58f;

    [Header("Target lock")]
    public float lockRotateSpeed = 6f;
    public float lockPitch = 18f;
    [Tooltip("Extra camera distance per metre between player and target, so both stay in frame.")]
    public float lockDistancePerMetre = 0.35f;

    [Header("Collision")]
    public float collisionRadius = 0.25f;
    public LayerMask collisionMask = ~0;
    public float zoomOutSpeed = 6f;

    public bool LookEnabled { get; set; } = true;

    public CameraMode Mode
    {
        get
        {
            if (!useActionCamera) return CameraMode.Legacy;
            return _lockTarget != null ? CameraMode.TargetLock : CameraMode.FreeLook;
        }
    }

    private Transform _lockTarget;
    private float _yaw, _pitch, _currentDistance;
    private Vector3 _smoothPivot, _pivotVelocity;
    private bool _initialised;
    private bool _cursorLocked, _manualUnlock;
    private bool _tacticalMode;
    private CameraMode _loggedMode = CameraMode.Legacy;
    private PlayerCapsule _boundCapsule;
    private Transform _boundTarget;
    private readonly RaycastHit[] _hits = new RaycastHit[8];

    // ------------------------------------------------------------------ public API

    /// <summary>Enter (non-null) or leave (null) Target-Lock mode.</summary>
    public void SetLockTarget(Transform lockTarget)
    {
        _lockTarget = lockTarget;
        if (_lockTarget != null && _tacticalMode)
        {
            _tacticalMode = false;
            Debug.Log("[ActionCamera] Tactical mode exited for target lock.");
        }
        LogModeIfChanged();
    }

    // ------------------------------------------------------------------ unity

    private void OnDisable()
    {
        ReleaseCursor();
        UnbindCapsule();
    }

    private void LateUpdate()
    {
        if (target == null) return;

        if (!useActionCamera)
        {
            ReleaseCursor();
            UnbindCapsule();
            LegacyUpdate();
            return;
        }

        BindCapsule();
        UpdateCursor();
        ActionUpdate();
        LogModeIfChanged();
    }

    // ------------------------------------------------------------------ legacy

    private void LegacyUpdate()
    {
        Vector3 wanted = target.position + offset;
        transform.position = Vector3.Lerp(transform.position, wanted, 1f - Mathf.Exp(-smoothing * Time.deltaTime));
        transform.LookAt(target.position + Vector3.up);
    }

    // ------------------------------------------------------------------ action camera

    private void ActionUpdate()
    {
        float dt = Time.deltaTime;
        Vector3 rawPivot = target.position + Vector3.up * pivotHeight;

        if (!_initialised)
        {
            _yaw = target.eulerAngles.y;
            _pitch = defaultPitch;
            _smoothPivot = rawPivot;
            _currentDistance = distance;
            _initialised = true;
            Debug.Log("[ActionCamera] Initialised behind target.");
        }

        _smoothPivot = Vector3.SmoothDamp(_smoothPivot, rawPivot, ref _pivotVelocity, pivotSmoothTime);

        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard[tacticalToggleKey].wasPressedThisFrame && _lockTarget == null)
        {
            _tacticalMode = !_tacticalMode;
            Debug.Log($"[ActionCamera] Tactical 2.5D mode {(_tacticalMode ? "enabled" : "disabled")} via {tacticalToggleKey}.");
        }

        if (_tacticalMode)
        {
            TacticalUpdate(rawPivot, dt);
            return;
        }

        float desiredDistance = distance;

        if (_lockTarget != null)
        {
            Vector3 toTarget = _lockTarget.position - rawPivot;
            Vector3 flat = new Vector3(toTarget.x, 0f, toTarget.z);
            float t = 1f - Mathf.Exp(-lockRotateSpeed * dt);

            if (flat.sqrMagnitude > 0.01f)
            {
                float wantedYaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
                _yaw = Mathf.LerpAngle(_yaw, wantedYaw, t);
            }
            _pitch = Mathf.Lerp(_pitch, lockPitch, t);
            desiredDistance = Mathf.Clamp(distance + toTarget.magnitude * lockDistancePerMetre, minDistance, maxDistance + 4f);
        }
        else if (_cursorLocked && LookEnabled && !GameplayInput.IsBlocked)
        {
            var mouse = Mouse.current;
            if (mouse != null && (!holdRightMouseToLook || mouse.rightButton.isPressed))
            {
                Vector2 delta = mouse.delta.ReadValue();
                _yaw += delta.x * mouseSensitivity;
                _pitch += (invertY ? delta.y : -delta.y) * mouseSensitivity;
                _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
            }
        }

        Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
        Vector3 right = rot * Vector3.right;
        Vector3 back = rot * Vector3.back;

        Vector3 origin = _smoothPivot + right * shoulderOffset;
        float resolved = ResolveDistance(origin, back, desiredDistance);

        // Pull in instantly when blocked (no clipping), ease back out when the way is clear.
        if (resolved < _currentDistance) _currentDistance = resolved;
        else _currentDistance = Mathf.Lerp(_currentDistance, resolved, 1f - Mathf.Exp(-zoomOutSpeed * dt));

        Vector3 position = origin + back * _currentDistance;
        transform.position = position;

        if (_lockTarget != null)
        {
            Vector3 lookAt = Vector3.Lerp(_smoothPivot, _lockTarget.position, 0.4f);
            Vector3 dir = lookAt - position;
            transform.rotation = dir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(dir, Vector3.up) : rot;
        }
        else
        {
            transform.rotation = rot;
        }
    }

    private void TacticalUpdate(Vector3 rawPivot, float dt)
    {
        var mouse = Mouse.current;
        if (_cursorLocked && LookEnabled && !GameplayInput.IsBlocked && mouse != null)
        {
            // Tactical camera keeps a high pitch but permits full map rotation.
            _yaw += mouse.delta.ReadValue().x * mouseSensitivity;
        }

        Quaternion rotation = Quaternion.Euler(tacticalPitch, _yaw, 0f);
        Vector3 desired = rawPivot + Vector3.up * tacticalHeight + rotation * Vector3.back * tacticalDistance;
        transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-12f * dt));
        Vector3 dir = rawPivot - transform.position;
        transform.rotation = dir.sqrMagnitude > .001f ? Quaternion.LookRotation(dir, Vector3.up) : rotation;
    }

    private float ResolveDistance(Vector3 origin, Vector3 direction, float desired)
    {
        int count = Physics.SphereCastNonAlloc(origin, collisionRadius, direction, _hits, desired, collisionMask, QueryTriggerInteraction.Ignore);

        float closest = desired;
        for (int i = 0; i < count; i++)
        {
            var hit = _hits[i];
            if (hit.collider == null) continue;
            if (hit.distance <= 0f) continue;                                   // origin already inside: ignore
            if (hit.collider.transform.IsChildOf(target)) continue;             // never collide with the followed player
            if (hit.collider.attachedRigidbody != null) continue;               // dynamic/kinematic bodies (players, NPCs) don't push the camera
            if (hit.distance < closest) closest = hit.distance;
        }

        if (closest < desired) closest = Mathf.Max(closest - 0.05f, 0.3f);
        return closest;
    }

    // ------------------------------------------------------------------ cursor / movement frame

    private void UpdateCursor()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;

        if (kb != null && kb.escapeKey.wasPressedThisFrame && !GameplayInput.IsBlocked) _manualUnlock = true;
        if (_manualUnlock && !GameplayInput.IsBlocked && mouse != null &&
            (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame))
            _manualUnlock = false;

        bool rightMouseHeld = mouse != null && mouse.rightButton.isPressed;
        bool want = LookEnabled && !GameplayInput.IsBlocked && !_manualUnlock && Application.isFocused && (!holdRightMouseToLook || rightMouseHeld);
        if (want == _cursorLocked) return;

        _cursorLocked = want;
        Cursor.lockState = want ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !want;
        Debug.Log($"[ActionCamera] Cursor {(want ? "locked (mouse look on)" : "released")}.");
    }

    private void ReleaseCursor()
    {
        if (!_cursorLocked) return;
        _cursorLocked = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void BindCapsule()
    {
        if (_boundTarget != target)
        {
            UnbindCapsule();
            _boundTarget = target;
            _boundCapsule = target != null ? target.GetComponent<PlayerCapsule>() : null;
            if (_boundCapsule == null)
                Debug.LogWarning("[ActionCamera] Target has no PlayerCapsule - movement stays world-relative.");
        }
        if (_boundCapsule != null && _boundCapsule.MovementReference != transform)
        {
            _boundCapsule.MovementReference = transform;
            Debug.Log("[ActionCamera] Movement is now camera-relative.");
        }
    }

    private void UnbindCapsule()
    {
        if (_boundCapsule != null && _boundCapsule.MovementReference == transform)
            _boundCapsule.MovementReference = null;
        _boundCapsule = null;
        _boundTarget = null;
    }

    private void LogModeIfChanged()
    {
        var m = _tacticalMode ? CameraMode.Tactical : Mode;
        if (m == _loggedMode) return;
        Debug.Log($"[ActionCamera] Mode {_loggedMode} -> {m}" + (m == CameraMode.TargetLock ? $" (target={_lockTarget.name})" : ""));
        _loggedMode = m;
    }
}
