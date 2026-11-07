using UnityEngine;

/// <summary>
/// Third-person MMORPG camera with three presets, smooth orbit/zoom,
/// cursor unlock support, and sphere-cast obstruction handling.
/// Attach this component to the Main Camera.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class MMOCameraController : MonoBehaviour
{
    public enum CameraPreset
    {
        StandardMMO = 0,
        ActionCombat = 1,
        RaidBoss = 2
    }

    private struct PresetSettings
    {
        public Vector3 targetOffset;
        public float distance;
        public float minDistance;
        public float maxDistance;
        public float fieldOfView;
        public float pitch;
    }

    public static MMOCameraController Instance { get; private set; }

    [Header("Target")]
    [SerializeField] private Transform target;
    [SerializeField] private CameraPreset startingPreset = CameraPreset.StandardMMO;

    [Header("Mouse Orbit")]
    [SerializeField, Min(0.01f)] private float mouseSensitivity = 2.5f;
    [SerializeField] private float minPitch = -10f;
    [SerializeField] private float maxPitch = 75f;
    [SerializeField, Min(0.01f)] private float orbitSmoothTime = 0.08f;

    [Header("Zoom")]
    [SerializeField, Min(0.01f)] private float zoomSensitivity = 2.0f;
    [SerializeField, Min(0.01f)] private float distanceSmooth = 10f;

    [Header("Camera Smoothing")]
    [SerializeField, Min(0.01f)] private float offsetSmoothTime = 0.12f;
    [SerializeField, Min(0.01f)] private float fovSmooth = 8f;

    [Header("Anti Clipping")]
    [Tooltip("Include walls and world geometry. The Player layer is also removed from this mask automatically.")]
    [SerializeField] private LayerMask obstructionMask = ~0;
    [SerializeField] private string playerLayerName = "Player";
    [SerializeField, Min(0.01f)] private float collisionRadius = 0.22f;
    [SerializeField, Min(0f)] private float collisionPadding = 0.12f;
    [SerializeField, Min(0.01f)] private float collisionReturnSmooth = 8f;

    private Camera _camera;
    private CameraPreset _currentPreset;
    private PresetSettings _settings;

    private Vector3 _desiredOffset;
    private Vector3 _currentOffset;
    private Vector3 _offsetVelocity;

    private float _targetDistance;
    private float _currentDistance;
    private float _currentFov;

    private float _targetYaw;
    private float _targetPitch;
    private float _yaw;
    private float _pitch;
    private float _yawVelocity;
    private float _pitchVelocity;

    private bool _gameplayEnabled;
    private bool _altHeld;
    private bool _initialized;

    public Transform Target => target;
    public Transform CameraTransform => transform;
    public CameraPreset CurrentPreset => _currentPreset;
    public bool GameplayEnabled => _gameplayEnabled;
    public bool IsGameplayInputLocked =>
        _gameplayEnabled && !_altHeld && Cursor.lockState == CursorLockMode.Locked;

    /// <summary>Horizontal direction the camera is aiming toward.</summary>
    public Vector3 PlanarAimDirection
    {
        get
        {
            Vector3 direction = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
        }
    }

    public Vector3 PlanarAimRight
    {
        get
        {
            Vector3 direction = Quaternion.Euler(0f, _yaw, 0f) * Vector3.right;
            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.right;
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        _camera = GetComponent<Camera>();

        _currentPreset = startingPreset;
        _settings = GetPresetSettings(_currentPreset);
        _desiredOffset = _settings.targetOffset;
        _currentOffset = _desiredOffset;
        _targetDistance = _settings.distance;
        _currentDistance = _targetDistance;
        _currentFov = _settings.fieldOfView;
        _targetPitch = _settings.pitch;
        _pitch = _targetPitch;

        if (target != null)
        {
            _targetYaw = target.eulerAngles.y;
            _yaw = _targetYaw;
        }

        _camera.fieldOfView = _currentFov;
        _initialized = true;
        ApplyCursorState();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void Update()
    {
        if (!_gameplayEnabled)
        {
            return;
        }

        bool altNow = Input.GetKey(KeyCode.LeftAlt);
        if (altNow != _altHeld)
        {
            _altHeld = altNow;
            ApplyCursorState();
        }

        // While Left Alt is held, the cursor is available for UI and the camera is frozen.
        if (_altHeld)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.V))
        {
            CyclePreset();
        }

        float mouseX = Input.GetAxis("Mouse X");
        float mouseY = Input.GetAxis("Mouse Y");
        _targetYaw += mouseX * mouseSensitivity;
        _targetPitch -= mouseY * mouseSensitivity;
        _targetPitch = Mathf.Clamp(_targetPitch, minPitch, maxPitch);

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.0001f)
        {
            _targetDistance -= scroll * zoomSensitivity * 10f;
            _targetDistance = Mathf.Clamp(_targetDistance, _settings.minDistance, _settings.maxDistance);
        }
    }

    private void LateUpdate()
    {
        if (!_initialized || target == null || _camera == null)
        {
            return;
        }

        float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
        _yaw = Mathf.SmoothDampAngle(_yaw, _targetYaw, ref _yawVelocity, orbitSmoothTime, Mathf.Infinity, deltaTime);
        _pitch = Mathf.SmoothDampAngle(_pitch, _targetPitch, ref _pitchVelocity, orbitSmoothTime, Mathf.Infinity, deltaTime);

        _currentOffset = Vector3.SmoothDamp(
            _currentOffset,
            _desiredOffset,
            ref _offsetVelocity,
            offsetSmoothTime,
            Mathf.Infinity,
            deltaTime);

        float distanceLerp = 1f - Mathf.Exp(-distanceSmooth * deltaTime);
        float fovLerp = 1f - Mathf.Exp(-fovSmooth * deltaTime);
        _currentDistance = Mathf.Lerp(_currentDistance, _targetDistance, distanceLerp);
        _currentFov = Mathf.Lerp(_currentFov, _settings.fieldOfView, fovLerp);
        _camera.fieldOfView = _currentFov;

        Quaternion cameraRotation = Quaternion.Euler(_pitch, _yaw, 0f);
        Vector3 planarRight = Quaternion.Euler(0f, _yaw, 0f) * Vector3.right;
        Vector3 focusPoint = target.position
                           + Vector3.up * _currentOffset.y
                           + planarRight * _currentOffset.x
                           + PlanarAimDirection * _currentOffset.z;

        float allowedDistance = _currentDistance;
        Vector3 castDirection = -(cameraRotation * Vector3.forward);

        int effectiveObstructionMask = obstructionMask.value;
        int playerLayer = LayerMask.NameToLayer(playerLayerName);
        if (playerLayer >= 0)
        {
            effectiveObstructionMask &= ~(1 << playerLayer);
        }

        if (effectiveObstructionMask != 0 && Physics.SphereCast(
                focusPoint,
                collisionRadius,
                castDirection,
                out RaycastHit hit,
                _currentDistance,
                effectiveObstructionMask,
                QueryTriggerInteraction.Ignore))
        {
            allowedDistance = Mathf.Max(0.15f, hit.distance - collisionPadding);
        }

        // Pull inward immediately when blocked; move outward with damping when the obstruction clears.
        if (allowedDistance < _currentDistance)
        {
            _currentDistance = allowedDistance;
        }
        else
        {
            float returnLerp = 1f - Mathf.Exp(-collisionReturnSmooth * deltaTime);
            _currentDistance = Mathf.Lerp(_currentDistance, allowedDistance, returnLerp);
        }

        transform.SetPositionAndRotation(
            focusPoint + castDirection * _currentDistance,
            cameraRotation);
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        if (target != null)
        {
            _targetYaw = target.eulerAngles.y;
            if (!_initialized)
            {
                _yaw = _targetYaw;
            }
        }
    }

    public void SetGameplayEnabled(bool enabled)
    {
        _gameplayEnabled = enabled;
        _altHeld = false;
        ApplyCursorState();
    }

    public void CyclePreset()
    {
        int next = ((int)_currentPreset + 1) % 3;
        SetPreset((CameraPreset)next);
    }

    public void SetPreset(CameraPreset preset)
    {
        _currentPreset = preset;
        _settings = GetPresetSettings(_currentPreset);
        _desiredOffset = _settings.targetOffset;
        _targetDistance = _settings.distance;
        _targetPitch = Mathf.Clamp(_settings.pitch, minPitch, maxPitch);
    }

    private PresetSettings GetPresetSettings(CameraPreset preset)
    {
        switch (preset)
        {
            case CameraPreset.ActionCombat:
                return new PresetSettings
                {
                    targetOffset = new Vector3(0.62f, 1.52f, 0f),
                    distance = 3.4f,
                    minDistance = 1.5f,
                    maxDistance = 6.5f,
                    fieldOfView = 55f,
                    pitch = 10f
                };

            case CameraPreset.RaidBoss:
                return new PresetSettings
                {
                    targetOffset = new Vector3(0f, 2.65f, 0f),
                    distance = 12f,
                    minDistance = 6f,
                    maxDistance = 22f,
                    fieldOfView = 65f,
                    pitch = 34f
                };

            default:
                return new PresetSettings
                {
                    targetOffset = new Vector3(0f, 1.55f, 0f),
                    distance = 6f,
                    minDistance = 2f,
                    maxDistance = 14f,
                    fieldOfView = 60f,
                    pitch = 18f
                };
        }
    }

    private void ApplyCursorState()
    {
        if (!_gameplayEnabled)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return;
        }

        if (_altHeld)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            ApplyCursorState();
        }
    }
}
