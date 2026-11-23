using UnityEngine;
using UnityEngine.InputSystem;

public enum CameraMode
{
    /// <summary>Original fixed-offset follow camera.</summary>
    Legacy,
    /// <summary>Smooth Wuxia third-person orbit camera (Where Winds Meet style).</summary>
    FreeLook,
    /// <summary>Camera keeps both the player and the locked target smoothly framed.</summary>
    TargetLock
}

/// <summary>
/// กล้องมุมมองบุคคลที่ 3 สไตล์ Action-RPG กำลังภายใน (Where Winds Meet Style):
///   - นำโหมดมุมมองสูง (Top-Down / Tactical [V]) ออกถาวร เพื่อคงมุมกล้องแอ็กชันที่สมจริงและลื่นไหลตลอดการเล่น
///   - หมุนกล้องด้วยเมาส์แบบ Smooth Exponential Damping (ตอบสนองไวแต่เนียนตา ไม่มีอาการสั่นกระตุก)
///   - ซูมระยะกล้องเข้า-ออกได้อย่างอิสระด้วยลูกกลิ้งเมาส์ (Scroll Wheel: 3.2m – 7.6m)
///   - เอฟเฟกต์กล้องถอยและเปิดมุมมอง FOV กว้างขึ้นอย่างนุ่มนวลขณะวิ่ง Sprint เพื่อให้ความรู้สึกพุ่งทะยานเหมือน Where Winds Meet
///   - แยกความนุ่มนวลแกนราบ/แกนตั้ง (Steadicam) พร้อมระบบกันทะลุกำแพงและพื้นเนิน (SphereCast Anti-Clip)
/// </summary>
public class PlayerFollowCamera : MonoBehaviour
{
    // ---------------- legacy (kept for non-action fallback only) ----------------
    public Transform target;
    public Vector3 offset = new Vector3(0f, 8f, -8f);
    public float smoothing = 8f;

    // ---------------- Where Winds Meet action camera ----------------
    [Header("Where Winds Meet Action Camera")]
    public bool useActionCamera = true;
    [Min(1.5f)] public float distance = 5.2f;
    [Min(1.5f)] public float minDistance = 3.2f;
    [Min(3.5f)] public float maxDistance = 7.6f;
    [Tooltip("ความเร็วในการซูมระยะกล้องด้วยลูกกลิ้งเมาส์ (Scroll Wheel)")]
    public float scrollZoomSpeed = 0.55f;
    public float zoomSmoothSpeed = 10f;
    [Tooltip("ความสูงจุดศูนย์กลางกล้องเหนือเท้าตัวละคร (1.48m จัดเฟรมสไตล์กำลังภายใน เห็นเต็มตัวและขอบฟ้าชัดเจน)")]
    public float pivotHeight = 1.48f;
    [Tooltip("เยื้องไหล่ขวาเล็กน้อยเพื่อให้ดาบใหญ่ไม่บังเป้ากลางจอ แต่ยังดูสมดุลไม่เอียง")]
    public float shoulderOffset = 0.24f;
    public float pivotSmoothTime = 0.065f;
    public float verticalSmoothTime = 0.11f;

    [Header("Free Look Orbit")]
    public float mouseSensitivity = 0.11f;
    public bool invertY = false;
    public float minPitch = -18f;
    public float maxPitch = 56f;
    public float defaultPitch = 13f;
    [Tooltip("ความเนียนในการหมุนกล้องตามเมาส์ (ค่าสูง = ไวและคม แต่นุ่มนวลไม่มีไมโครกระตุก)")]
    public float rotationSmoothSpeed = 24f;
    [Tooltip("Optional ARPG style: rotate only while Right Mouse is held. Off keeps continuous free-look after cursor capture.")]
    public bool holdRightMouseToLook = false;

    [Header("Dynamic Sprint Framing (ถอยกล้อง + เปิด FOV ตอนวิ่ง)")]
    [Tooltip("ระยะถอยกล้องเพิ่มขณะวิ่ง Sprint (เมตร)")]
    public float sprintDistanceBonus = 0.85f;
    [Tooltip("มุมมอง FOV พื้นฐานขณะเดินปกติ")]
    public float baseFov = 60f;
    [Tooltip("มุมมอง FOV ที่เปิดกว้างขึ้นขณะวิ่ง Sprint (องศา)")]
    public float sprintFovBonus = 5.5f;
    public float sprintFramingSpeed = 5.5f;

    [Header("Target Lock")]
    public float lockRotateSpeed = 7.5f;
    public float lockPitch = 15f;
    [Tooltip("Extra camera distance per metre between player and target so both stay cleanly framed.")]
    public float lockDistancePerMetre = 0.28f;

    [Header("Collision & Ground Clearance")]
    public float collisionRadius = 0.28f;
    public LayerMask collisionMask = ~0;
    public float zoomOutSpeed = 6.5f;

    [Header("Combat Camera Shake (แรงสั่นกล้องขณะต่อสู้)")]
    [SerializeField] private float shakeFrequency = 28f;
    [SerializeField] private float maxShakeOffset = 0.45f;
    [SerializeField] private float maxShakeRollDegrees = 2.5f;

    public static PlayerFollowCamera Active { get; private set; }

    public bool LookEnabled { get; set; } = true;

    public CameraMode Mode
    {
        get
        {
            if (!useActionCamera) return CameraMode.Legacy;
            return _lockTarget != null ? CameraMode.TargetLock : CameraMode.FreeLook;
        }
    }

    private Camera _camera;
    private Transform _lockTarget;
    private float _yaw, _pitch;
    private float _targetYaw, _targetPitch;
    private float _desiredZoomDistance;
    private float _smoothedZoomDistance;
    private float _currentDistance;
    private float _sprintBlend;
    private Vector3 _smoothPivot;
    private float _pivotVelX, _pivotVelY, _pivotVelZ;
    private bool _initialised;
    private bool _cursorLocked, _manualUnlock;
    private CameraMode _loggedMode = CameraMode.Legacy;
    private PlayerCapsule _boundCapsule;
    private Transform _boundTarget;
    private readonly RaycastHit[] _hits = new RaycastHit[12];

    // ตัวแปรภายในสำหรับระบบสั่นกล้อง (ลบค่าเยื้องเดิมออกก่อนคำนวณเฟรมถัดไปเพื่อป้องกันกล้องไหล)
    private float _shakeIntensity;
    private float _shakeDuration;
    private float _shakeRemaining;
    private float _shakeSeed;
    private Vector3 _lastAppliedShakeOffset;

    // ------------------------------------------------------------------ public API

    /// <summary>
    /// สั่งสั่นกล้องบนกล้องที่ใช้งานอยู่ปัจจุบัน (ใช้เรียกจากระบบต่อสู้, Parry หรือบอส)
    /// </summary>
    public static void ShakeActive(float intensity, float duration)
    {
        if (Active != null)
            Active.TriggerShake(intensity, duration);
    }

    /// <summary>
    /// สั่งสั่นกล้องด้วยความแรงและระยะเวลาที่กำหนด พร้อมลดทอนความแรงลงอย่างนุ่มนวล
    /// </summary>
    public void TriggerShake(float intensity, float duration)
    {
        if (intensity <= 0f || duration <= 0f) return;

        float currentEffective = _shakeRemaining > 0f && _shakeDuration > 0.001f
            ? _shakeIntensity * (_shakeRemaining / _shakeDuration)
            : 0f;

        if (intensity >= currentEffective)
        {
            _shakeIntensity = Mathf.Clamp01(intensity);
            _shakeDuration = Mathf.Max(0.03f, duration);
            _shakeRemaining = _shakeDuration;
            _shakeSeed = Random.Range(0f, 999f);
        }
        else
        {
            _shakeRemaining = Mathf.Max(_shakeRemaining, duration * 0.6f);
        }
    }

    /// <summary>Enter (non-null) or leave (null) Target-Lock mode.</summary>
    public void SetLockTarget(Transform lockTarget)
    {
        _lockTarget = lockTarget;
        if (_lockTarget == null)
        {
            _targetYaw = _yaw;
            _targetPitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
        }
        LogModeIfChanged();
    }

    // ------------------------------------------------------------------ unity

    private void Awake()
    {
        _camera = GetComponent<Camera>();
    }

    private void OnEnable()
    {
        Active = this;
        if (_camera == null) _camera = GetComponent<Camera>();
    }

    private void OnDisable()
    {
        if (Active == this) Active = null;
        if (_lastAppliedShakeOffset != Vector3.zero)
        {
            transform.position -= _lastAppliedShakeOffset;
            _lastAppliedShakeOffset = Vector3.zero;
        }
        _shakeRemaining = 0f;
        ReleaseCursor();
        UnbindCapsule();
    }

    private void LateUpdate()
    {
        if (target == null) return;

        // ล้างค่าเยื้องจากแรงสั่นเฟรมก่อนออกก่อนคำนวณตำแหน่งกล้องใหม่ (ป้องกันกล้องไหล)
        if (_lastAppliedShakeOffset != Vector3.zero)
        {
            transform.position -= _lastAppliedShakeOffset;
            _lastAppliedShakeOffset = Vector3.zero;
        }

        if (!useActionCamera)
        {
            ReleaseCursor();
            UnbindCapsule();
            LegacyUpdate();
            ApplyCameraShake(Time.unscaledDeltaTime);
            return;
        }

        BindCapsule();
        UpdateCursor();
        ActionUpdate();
        ApplyCameraShake(Time.unscaledDeltaTime);
        LogModeIfChanged();
    }

    /// <summary>
    /// ประมวลผลแรงสั่นของกล้องแบบ Perlin Noise และลดความแรงลงตามเวลาจริง (unscaledDeltaTime)
    /// </summary>
    private void ApplyCameraShake(float dt)
    {
        if (_shakeRemaining <= 0f || _shakeDuration <= 0.001f) return;

        _shakeRemaining = Mathf.Max(0f, _shakeRemaining - dt);
        float progress = _shakeRemaining / _shakeDuration;
        float damper = progress * progress;
        float strength = _shakeIntensity * damper;

        float t = Time.unscaledTime * shakeFrequency + _shakeSeed;
        float nx = (Mathf.PerlinNoise(t, 0.37f) - 0.5f) * 2f;
        float ny = (Mathf.PerlinNoise(0.73f, t) - 0.5f) * 2f;
        float nz = (Mathf.PerlinNoise(t * 0.7f, t * 0.7f) - 0.5f) * 2f;

        Vector3 offsetLocal = new Vector3(nx, ny, 0f) * (strength * maxShakeOffset);
        Vector3 worldOffset = transform.rotation * offsetLocal;
        transform.position += worldOffset;
        _lastAppliedShakeOffset = worldOffset;

        float roll = nz * strength * maxShakeRollDegrees;
        transform.rotation = transform.rotation * Quaternion.Euler(0f, 0f, roll);
    }

    // ------------------------------------------------------------------ legacy

    private void LegacyUpdate()
    {
        Vector3 wanted = target.position + offset;
        transform.position = Vector3.Lerp(transform.position, wanted, 1f - Mathf.Exp(-smoothing * Time.deltaTime));
        transform.LookAt(target.position + Vector3.up);
    }

    // ------------------------------------------------------------------ Where Winds Meet action camera

    private void ActionUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0.00001f) return;

        Vector3 rawPivot = target.position + Vector3.up * pivotHeight;

        if (!_initialised)
        {
            _yaw = _targetYaw = target.eulerAngles.y;
            _pitch = _targetPitch = Mathf.Clamp(defaultPitch, minPitch, maxPitch);
            _smoothPivot = rawPivot;
            _desiredZoomDistance = Mathf.Clamp(distance, minDistance, maxDistance);
            _smoothedZoomDistance = _desiredZoomDistance;
            _currentDistance = _desiredZoomDistance;
            _sprintBlend = 0f;
            _initialised = true;
            Debug.Log("[ActionCamera] Initialised Where Winds Meet third-person camera behind target.");
        }

        // หากตัวละครวาร์ปข้ามระยะไกล (> 18 เมตร เช่น ใช้หินเทเลพอร์ตหรือเกิดใหม่) ให้ย้ายจุดหมุนกล้องตามทันทีไม่บินทะลุภูเขา
        if ((rawPivot - _smoothPivot).sqrMagnitude > 18f * 18f)
        {
            _smoothPivot = rawPivot;
            _pivotVelX = _pivotVelY = _pivotVelZ = 0f;
            _currentDistance = _smoothedZoomDistance;
        }

        // แยกความนุ่มนวลแกนราบ (ตอบสนองไว) กับแกนตั้ง (นุ่มนวลลดแรงกระแทกเวลาขึ้นเนิน/กระโดด)
        float sx = Mathf.SmoothDamp(_smoothPivot.x, rawPivot.x, ref _pivotVelX, pivotSmoothTime);
        float sy = Mathf.SmoothDamp(_smoothPivot.y, rawPivot.y, ref _pivotVelY, verticalSmoothTime);
        float sz = Mathf.SmoothDamp(_smoothPivot.z, rawPivot.z, ref _pivotVelZ, pivotSmoothTime);
        _smoothPivot = new Vector3(sx, sy, sz);

        // ซูมเข้า-ออกด้วยลูกกลิ้งเมาส์ (Scroll Wheel) เมื่อไม่ได้เปิดหน้าต่าง UI
        var mouse = Mouse.current;
        if (mouse != null && LookEnabled && !GameplayInput.IsBlocked)
        {
            float scrollY = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scrollY) > 0.01f)
            {
                float step = Mathf.Sign(scrollY) * scrollZoomSpeed;
                _desiredZoomDistance = Mathf.Clamp(_desiredZoomDistance - step, minDistance, maxDistance);
            }
        }
        _smoothedZoomDistance = Mathf.Lerp(_smoothedZoomDistance, _desiredZoomDistance, 1f - Mathf.Exp(-zoomSmoothSpeed * dt));

        // ตรวจจับการวิ่ง Sprint เพื่อถอยกล้องและเปิด FOV กว้างขึ้นอย่างนุ่มนวลสไตล์ Where Winds Meet
        bool isSprintingNow = _boundCapsule != null
            && _boundCapsule.IsSprinting
            && _boundCapsule.NetworkMove.sqrMagnitude > 0.01f
            && _lockTarget == null;
        float sprintTarget = isSprintingNow ? 1f : 0f;
        _sprintBlend = Mathf.Lerp(_sprintBlend, sprintTarget, 1f - Mathf.Exp(-sprintFramingSpeed * dt));

        if (_camera != null)
        {
            float wantedFov = baseFov + sprintFovBonus * _sprintBlend;
            _camera.fieldOfView = Mathf.Lerp(_camera.fieldOfView, wantedFov, 1f - Mathf.Exp(-sprintFramingSpeed * dt));
        }

        float desiredDistance = _smoothedZoomDistance + sprintDistanceBonus * _sprintBlend;

        if (_lockTarget != null)
        {
            Vector3 toTarget = _lockTarget.position - rawPivot;
            Vector3 flat = new Vector3(toTarget.x, 0f, toTarget.z);
            float t = 1f - Mathf.Exp(-lockRotateSpeed * dt);

            if (flat.sqrMagnitude > 0.01f)
            {
                float wantedYaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
                _yaw = Mathf.LerpAngle(_yaw, wantedYaw, t);
                _targetYaw = _yaw;
            }
            _pitch = Mathf.Lerp(_pitch, lockPitch, t);
            _targetPitch = _pitch;
            desiredDistance = Mathf.Clamp(_smoothedZoomDistance + toTarget.magnitude * lockDistancePerMetre, minDistance, maxDistance + 3.0f);
        }
        else
        {
            if (_cursorLocked && LookEnabled && !GameplayInput.IsBlocked && mouse != null && (!holdRightMouseToLook || mouse.rightButton.isPressed))
            {
                Vector2 delta = mouse.delta.ReadValue();
                _targetYaw += delta.x * mouseSensitivity;
                _targetPitch += (invertY ? delta.y : -delta.y) * mouseSensitivity;
                _targetPitch = Mathf.Clamp(_targetPitch, minPitch, maxPitch);
            }

            float rotT = 1f - Mathf.Exp(-rotationSmoothSpeed * dt);
            _yaw = Mathf.LerpAngle(_yaw, _targetYaw, rotT);
            _pitch = Mathf.Lerp(_pitch, _targetPitch, rotT);
        }

        Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
        Vector3 right = rot * Vector3.right;
        Vector3 back = rot * Vector3.back;

        // เมื่อเงยกล้องต่ำกว่าแนวระนาบ ให้ยกจุดหมุนขึ้นเล็กน้อยเพื่อให้เงยชมภูเขา/บอสได้ลื่นโดยกล้องไม่จมพื้นหญ้า
        float lowPitchLift = _pitch < 2f ? Mathf.InverseLerp(2f, minPitch, _pitch) * 0.18f : 0f;
        Vector3 origin = _smoothPivot + Vector3.up * lowPitchLift + right * shoulderOffset;

        float resolved = ResolveDistance(origin, back, desiredDistance);

        // ดึงเข้าทันทีเมื่อติดกำแพง/เนิน (กันทะลุ 100%) และค่อยๆ คืนระยะอย่างนุ่มนวลเมื่อโล่ง
        if (resolved < _currentDistance) _currentDistance = resolved;
        else _currentDistance = Mathf.Lerp(_currentDistance, resolved, 1f - Mathf.Exp(-zoomOutSpeed * dt));

        Vector3 position = origin + back * _currentDistance;
        transform.position = position;

        if (_lockTarget != null)
        {
            Vector3 lookAt = Vector3.Lerp(_smoothPivot, _lockTarget.position, 0.38f);
            Vector3 dir = lookAt - position;
            transform.rotation = dir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(dir, Vector3.up) : rot;
        }
        else
        {
            transform.rotation = rot;
        }
    }

    private float ResolveDistance(Vector3 origin, Vector3 direction, float desired)
    {
        int count = Physics.SphereCastNonAlloc(origin, collisionRadius, direction, _hits, desired, collisionMask, QueryTriggerInteraction.Ignore);

        float closest = desired;
        for (int i = 0; i < count; i++)
        {
            var hit = _hits[i];
            if (hit.collider == null) continue;
            if (hit.distance <= 0f) continue;
            if (hit.collider.transform.IsChildOf(target)) continue;
            if (hit.collider.attachedRigidbody != null) continue;
            if (hit.collider.GetComponentInParent<EnemyNPC>() != null) continue;
            if (hit.collider.GetComponentInParent<PlayerCombat>() != null) continue;
            if (hit.distance < closest) closest = hit.distance;
        }

        if (closest < desired) closest = Mathf.Max(closest - 0.06f, 0.45f);
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
        var m = Mode;
        if (m == _loggedMode) return;
        Debug.Log($"[ActionCamera] Mode {_loggedMode} -> {m}" + (m == CameraMode.TargetLock && _lockTarget != null ? $" (target={_lockTarget.name})" : ""));
        _loggedMode = m;
    }
}
