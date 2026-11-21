using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// ระบบควบคุมการเคลื่อนที่ของตัวละครผู้เล่นหลัก (GreatSword Character Controller) สำหรับ Photon Fusion Shared Mode:
///   - รองรับการทำงานกับ CharacterController โดยตรงบน GreatSwordGirl.prefab (และรองรับโหมด Rigidbody สำรอง)
///   - ปรับจูนความเร็วเดิน ย่อง วิ่ง และการออกตัว/เบรกให้มีน้ำหนักสมจริงสไตล์เกมกำลังภายใน (Where Winds Meet) ไม่ลื่นไถล
///   - รองรับการเดิน 8 ทิศทาง (8-Directional Locomotion), การก้มย่องลอบสังหาร (Crouch ปุ่ม C), การกระโดด (ปุ่ม Spacebar) และการกลิ้งหลบ (Left Ctrl)
///   - ซิงก์ AvatarIndex จากหน้า Lobby และระบบเซฟ เพื่อแสดงรูปโปรไฟล์มุมขวาบนและสีชื่อผู้เล่นตรงกันทุกเครื่อง
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PlayerCapsule : NetworkBehaviour
{
    // เรนเดอเรอร์สำรอง (ปิดอัตโนมัติเมื่อใช้โมเดล GreatSword)
    public Renderer bodyRenderer;
    // ป้ายชื่อและเลเวลที่ลอยเหนือศีรษะตัวละคร
    public TMP_Text nameLabel;

    [Header("Character visual (โมเดลตัวละครหลัก GreatSword)")]
    [Tooltip("Rigged character transform (root itself for GreatSwordGirl.prefab, or child visual).")]
    [SerializeField] private Transform characterVisual;

    [Header("Optional animation & network sync")]
    [SerializeField] private Animator animator;
    [SerializeField] private NetworkMecanimAnimator networkAnimator;
    [SerializeField] private string moveXParameter = "MoveX";
    [SerializeField] private string moveYParameter = "MoveY";
    [SerializeField] private string sprintParameter = "IsSprinting";

    [Header("Overhead nameplate (ป้ายชื่อเหนือศีรษะ)")]
    [SerializeField] private float overheadNameFontSize = 2.6f;
    [SerializeField] private float overheadNameScale = 0.70f;
    [SerializeField] private Vector3 overheadNameOffset = new Vector3(0f, 2.05f, 0f);

    [Header("Where Winds Meet Grounded Martial-Arts Movement (ความเร็วและน้ำหนักการเคลื่อนที่)")]
    // ความเร็วเดินย่องลอบเร้น / เดินช้า (m/s)
    public float walkSpeed = 2.2f;
    // ความเร็วเคลื่อนที่มาตรฐานขณะสำรวจและต่อสู้ (m/s) - สมดุลกับจังหวะก้าวเท้า
    public float moveSpeed = 3.8f;
    // ตัวคูณความเร็วขณะกดวิ่ง Sprint (Shift) -> ~5.89 m/s สมจริงมีน้ำหนัก
    public float runSpeed = 1.55f;
    // อัตราเร่งตอนออกตัว (ให้ความรู้สึกถ่ายน้ำหนักก่อนพุ่งตัว)
    public float acceleration = 18f;
    // อัตราหน่วงตอนหยุดเดิน (หยุดเท้ามั่นคง ไม่ลื่นไถล)
    public float deceleration = 22f;
    // ความเร็วในการหมุนตัวละคร
    public float rotationSpeed = 11f;

    [Header("Jump & Crouch Settings (การกระโดดปุ่ม Spacebar และก้มย่องปุ่ม C)")]
    // ผูกปุ่มกระโดดเป็น Spacebar แทนปุ่ม F (ปุ่ม F ย้ายไปใช้สำหรับตั้งการ์ด Block/Parry เท่านั้น)
    public Key jumpKey = Key.Space;
    public Key crouchKey = Key.C;
    public float jumpVelocity = 5.8f;
    public float gravity = -20f;

    public bool detachNameLabel = true;
    public bool rotateTowardsMovement = true;
    [Tooltip("When true and an action camera (MovementReference) is active, the character faces the camera yaw while moving.")]
    public bool strafeInActionCamera = true;

    // ---- ข้อมูลซิงก์ผ่าน Photon Fusion Shared Mode ----
    [Networked] public int AvatarIndex { get; set; }
    [Networked] public NetworkString<_16> Nick { get; set; }
    [Networked] public Vector2 NetworkMove { get; set; }
    [Networked] public NetworkBool NetworkSprinting { get; set; }
    [Networked] public NetworkBool NetworkGrounded { get; set; } = true;
    [Networked] public NetworkBool NetworkCrouching { get; set; }

    // ตัวละครของผู้เล่นเครื่องนี้ (Local Player)
    public static PlayerCapsule Local { get; private set; }

    // ล็อกการเคลื่อนที่ชั่วคราว (เช่น ขณะตาย)
    public bool MovementLocked { get; set; }

    // แกนอ้างอิงทิศทางจากกล้อง (PlayerFollowCamera)
    public Transform MovementReference { get; set; }

    // เป้าหมายที่กำลังล็อกเป้าอยู่ (TargetLockSystem)
    public Transform FacingTarget { get; set; }

    // ตัวคูณความเร็วเคลื่อนที่ (เช่น ลดความเร็วขณะตั้งการ์ดบล็อก)
    public float SpeedMultiplier { get; set; } = 1f;

    // ทรานส์ฟอร์มของโมเดลตัวละคร
    public Transform CharacterVisual => characterVisual;

    // สถานะพุ่งกลิ้งหลบ, แตะพื้น, ก้มย่อง, วิ่งเร็ว
    public bool IsDashing => Time.time < _dashUntil;
    public bool IsGrounded => Object != null && Object.IsValid ? (bool)NetworkGrounded : _localGrounded;
    public bool IsCrouching => Object != null && Object.IsValid ? (bool)NetworkCrouching : _crouchToggle;
    public bool IsSprinting => Object != null && Object.IsValid ? (bool)NetworkSprinting : false;

    // ---- คุณสมบัติเชื่อมต่อระบบ Stamina, Level & Stat Allocation ----
    public float CurrentStamina => _combat != null ? _combat.NetworkStamina : 100f;
    public float MaxStamina => _combat != null ? _combat.MaxStamina : 100f;
    public int Level => _combat != null && _combat.IsSpawned ? _combat.NetworkLevel : 1;
    public int Experience => _combat != null && _combat.IsSpawned ? _combat.NetworkExperience : 0;
    public int ExpToNextLevel => _combat != null ? _combat.ExpToNextLevel : 100;
    public int UnspentStatPoints => _combat != null && _combat.IsSpawned ? _combat.NetworkUnspentStatPoints : 0;
    public int StatSTR => _combat != null && _combat.IsSpawned ? _combat.StatPointsSTR : 0;
    public int StatVIT => _combat != null && _combat.IsSpawned ? _combat.StatPointsVIT : 0;
    public int StatEND => _combat != null && _combat.IsSpawned ? _combat.StatPointsEND : 0;
    public int StatAGI => _combat != null && _combat.IsSpawned ? _combat.StatPointsAGI : 0;
    public float TotalMoveSpeed => moveSpeed + (_combat != null ? _combat.TotalMoveSpeedBonus : 0f);

    public void AddExperience(int amount) => _combat?.AddExperience(amount);
    public bool AllocateStatPoint(PlayerStatType stat) => _combat != null && _combat.AllocateStatPoint(stat);

    private Rigidbody rb;
    private CharacterController _cc;
    private CapsuleCollider collider;
    private PlayerCombat _combat;
    private PlayerAnimationDriver _animationDriver;
    private NetworkTransform _networkTransform;

    private Vector3 _dashVelocity;
    private float _dashUntil;
    private Vector3 _ccHorizontalVel;
    private float _ccVerticalVel;
    private bool _localGrounded = true;
    private float _lastJumpTime = -10f;
    private bool _crouchToggle;

    private Vector2 moveInput;
    private bool sprintHeld;
    private bool _jumpQueued;
    private float _overrideMoveUntil;
    // คิววาร์ปสำหรับซิงก์ผ่าน FixedUpdateNetwork ของ Photon Fusion 2 (ป้องกันการเด้งกลับเมื่อเรียกจาก Coroutine/Respawn)
    private bool _hasPendingTeleport;
    private Vector3 _pendingTeleportPos;

    private bool isOwner;
    private MaterialPropertyBlock _mpb;
    private Transform _labelTransform;
    private Vector3 _labelOffset = new Vector3(0f, 2.05f, 0f);
    private int _appliedAvatar = -1;
    private int _appliedLevel = -1;
    private string _appliedNick;

    private void Awake()
    {
        // ป้องกันค่าค้างจาก Prefab เก่าที่ยังผูกปุ่มกระโดดเป็น F -> บังคับย้ายเป็น Spacebar เสมอ
        if (jumpKey == Key.F)
            jumpKey = Key.Space;

        rb = GetComponent<Rigidbody>();
        _cc = GetComponent<CharacterController>();
        collider = GetComponent<CapsuleCollider>();
        _combat = GetComponent<PlayerCombat>();
        _networkTransform = GetComponent<NetworkTransform>();

        if (characterVisual == null)
        {
            var cv = transform.Find("CharacterVisual");
            characterVisual = cv != null ? cv : transform;
        }

        if (animator == null)
        {
            animator = characterVisual != null
                ? characterVisual.GetComponentInChildren<Animator>()
                : GetComponentInChildren<Animator>();
        }
        if (networkAnimator == null)
            networkAnimator = GetComponentInChildren<NetworkMecanimAnimator>();

        _animationDriver = GetComponent<PlayerAnimationDriver>();

        // ซ่อน Capsule Mesh ชั่วคราวเสมอ หากติดอยู่บน Root
        var rootMeshRenderer = GetComponent<MeshRenderer>();
        if (rootMeshRenderer != null) rootMeshRenderer.enabled = false;
        if (bodyRenderer != null && bodyRenderer.gameObject == gameObject)
            bodyRenderer.enabled = false;

        // หากมี CharacterController ให้ใช้โหมด Kinematic Rigidbody เพื่อป้องกันการชนซ้อนกันใน PhysX
        if (_cc != null && rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }
        if (rb != null) rb.freezeRotation = true;
    }

    public override void Spawned()
    {
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
            else if (PlayerSaveManager.Instance != null)
            {
                var saved = PlayerSaveManager.Instance.LoadFromDisk();
                if (saved != null)
                {
                    avatar = saved.avatarId;
                    if (!string.IsNullOrEmpty(saved.playerName))
                        nick = GoogleAPI.ClampName(saved.playerName);
                }
            }

            AvatarIndex = Mathf.Clamp(avatar, 0, LobbyNetwork.AvatarCount - 1);
            Nick = nick;
            NetworkGrounded = true;
            NetworkCrouching = false;

            Runner.SetPlayerObject(Runner.LocalPlayer, Object);
        }

        if (_cc != null)
        {
            // เปิด CharacterController บนเครื่องเจ้าของ และใช้เป็น Collider สำหรับตรวจ Hit ฝั่ง Remote
            _cc.enabled = isOwner;
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
            }
        }
        else if (rb != null)
        {
            rb.isKinematic = !isOwner;
            rb.interpolation = isOwner ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
        }

        Debug.Log($"[PlayerCapsule] Spawned {name}; local={isOwner}, useCC={_cc != null}, avatar={AvatarIndex}, moveSpeed={moveSpeed}, runMult={runSpeed}.");

        SetUpNameLabel();
        ApplyLook(force: true);
    }

    /// <summary>
    /// อัปเดตรหัสโปรไฟล์ Avatar จากระบบเซฟหรือหน้า Lobby
    /// </summary>
    public void SetAvatarIndex(int index)
    {
        int clamped = Mathf.Clamp(index, 0, LobbyNetwork.AvatarCount - 1);
        if (Object != null && Object.IsValid && Object.HasStateAuthority)
            AvatarIndex = clamped;
        if (GoogleAPI.instance != null)
            GoogleAPI.instance.avatarId = clamped;
        ApplyLook(force: true);
    }

    /// <summary>
    /// วาร์ปตัวละครไปยังตำแหน่งเป้าหมายอย่างปลอดภัย (รองรับทั้ง CharacterController, Rigidbody และ Photon Fusion 2 FixedUpdateNetwork)
    /// </summary>
    public void TeleportCharacter(Vector3 targetPosition)
    {
        ApplyTeleportImmediate(targetPosition);
        // บันทึกคิวไว้ยืนยันอีกครั้งภายใน FixedUpdateNetwork เพื่อให้ NetworkTransform ซิงก์ตรงทุก Peer โดยไม่โดนดึงกลับ
        _hasPendingTeleport = true;
        _pendingTeleportPos = targetPosition;
    }

    private void ApplyTeleportImmediate(Vector3 targetPosition)
    {
        bool ccWasEnabled = _cc != null && _cc.enabled;
        if (ccWasEnabled) _cc.enabled = false;

        if (rb != null)
        {
            rb.position = targetPosition;
            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        _ccHorizontalVel = Vector3.zero;
        _ccVerticalVel = 0f;
        _dashUntil = -1f;
        transform.position = targetPosition;
        Physics.SyncTransforms();

        if (_networkTransform != null && Object != null && Object.IsValid && Object.HasStateAuthority)
            _networkTransform.Teleport(targetPosition, transform.rotation);

        if (ccWasEnabled) _cc.enabled = true;
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Local == this) Local = null;
        DestroyDetachedLabel();
    }

    private void OnDestroy()
    {
        DestroyDetachedLabel();
    }

    // ---------------------------------------------------------------- การรับค่า Input

    public void OnMove(InputValue value)
    {
        moveInput = value.Get<Vector2>();
    }

    public void OnSprint(InputValue value)
    {
        sprintHeld = value.isPressed;
    }

    /// <summary>
    /// จำลองการส่งคำสั่งเดิน/วิ่ง (ใช้สำหรับระบบทดสอบอัตโนมัติหรือ AI)
    /// </summary>
    public void SetMoveInput(Vector2 move, bool sprint = false, float holdSeconds = 0.25f)
    {
        moveInput = Vector2.ClampMagnitude(move, 1f);
        sprintHeld = sprint;
        _overrideMoveUntil = holdSeconds > 0f ? Time.time + holdSeconds : 0f;
    }

    public void SetCrouch(bool crouching)
    {
        _crouchToggle = crouching;
        if (Object != null && Object.IsValid && Object.HasStateAuthority)
            NetworkCrouching = crouching;
    }

    public bool IsLocallyControlled => Object != null && (Object.HasInputAuthority || (Runner != null && Runner.GameMode == GameMode.Shared && Object.HasStateAuthority));

    private void Update()
    {
        if (!isOwner) return;

        if (Time.time < _overrideMoveUntil) return;
        if (!Application.isFocused || GameplayInput.IsBlocked)
        {
            // รีเซ็ตอินพุตการเดินทันทีเมื่อหลุดโฟกัสหรือเปิดเมนู เพื่อไม่ให้ตัวละครเดินค้าง
            moveInput = Vector2.zero;
            sprintHeld = false;
            return;
        }

        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            Vector2 direct = new Vector2(
                (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1f : 0f),
                (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1f : 0f));
            moveInput = direct;
            sprintHeld = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;

            if (sprintHeld && _crouchToggle)
                SetCrouch(false);

            // กด C เพื่อสลับท่าย่อง (Crouch / Stealth)
            if (keyboard[crouchKey].wasPressedThisFrame && !MovementLocked && !IsDashing)
                SetCrouch(!_crouchToggle);

            // กด Spacebar เพื่อกระโดด (คิวไว้ประมวลผลใน FixedUpdateNetwork เพื่อไม่ให้เฟรมตกแล้วกดไม่ติด)
            if (keyboard[jumpKey].wasPressedThisFrame && !MovementLocked && !IsDashing)
                _jumpQueued = true;
        }

        var gamepad = Gamepad.current;
        if (gamepad != null && gamepad.leftStick.ReadValue().sqrMagnitude > 0.01f)
            moveInput = gamepad.leftStick.ReadValue();
    }

    /// <summary>
    /// ประมวลผลการเคลื่อนที่หลักภายใน FixedUpdateNetwork ของ Photon Fusion เพื่อซิงก์กับ NetworkTransform อย่างสมบูรณ์ (ไม่โดนดึงกลับ)
    /// </summary>
    public override void FixedUpdateNetwork()
    {
        if (!isOwner) return;

        // ประมวลผลการวาร์ปในรอบ FixedUpdateNetwork ของ Fusion เพื่อให้ NetworkTransform อัปเดตตำแหน่งถาวรไม่เด้งกลับ
        if (_hasPendingTeleport)
        {
            _hasPendingTeleport = false;
            ApplyTeleportImmediate(_pendingTeleportPos);
            return;
        }

        if (_cc != null && _cc.enabled)
        {
            if (_jumpQueued)
            {
                _jumpQueued = false;
                TryJump();
            }
            TickCharacterControllerStep(Runner != null ? Runner.DeltaTime : Time.fixedDeltaTime);
        }
    }

    /// <summary>
    /// สั่งให้ตัวละครกระโดดขึ้นจากพื้น พร้อมส่งทิศทางเข้าสู่ Animator และ Network
    /// </summary>
    public bool TryJump()
    {
        if (!isOwner || MovementLocked || IsDashing) return false;
        if (Time.time - _lastJumpTime < 0.55f || !_localGrounded) return false;

        _lastJumpTime = Time.time;
        _localGrounded = false;
        SetCrouch(false);
        if (Object != null && Object.IsValid && Object.HasStateAuthority)
            NetworkGrounded = false;

        if (_cc != null && _cc.enabled)
        {
            _ccVerticalVel = jumpVelocity;
        }
        else if (rb != null)
        {
            Vector3 v = rb.linearVelocity;
            rb.linearVelocity = new Vector3(v.x, jumpVelocity, v.z);
        }

        Vector2 jDir = moveInput.sqrMagnitude > 0.01f ? moveInput.normalized : Vector2.zero;
        _animationDriver?.PlayJump(jDir.x, jDir.y);
        return true;
    }

    // ------------------------------------------------------------- การคำนวณการเคลื่อนที่ (CharacterController & Rigidbody)

    private void TickCharacterControllerStep(float dt)
    {
        if (dt <= 0f) return;

        bool wasGrounded = _cc.isGrounded && (Time.time - _lastJumpTime >= 0.16f);
        if (wasGrounded && _ccVerticalVel < 0f)
            _ccVerticalVel = -2.2f;
        else
            _ccVerticalVel += gravity * dt;

        var wanted = MovementLocked ? Vector3.zero : DesiredMoveWorld();
        bool isMoving = wanted.sqrMagnitude > 0.001f;

        bool canSprint = _combat == null || (_combat.CanSprint && !_combat.IsBlocking);
        bool effectiveSprint = sprintHeld && isMoving && canSprint && !_crouchToggle;
        if (effectiveSprint && _combat != null)
            effectiveSprint = _combat.ConsumeSprintStamina(dt);

        if (IsDashing)
        {
            Vector3 dashStep = (_dashVelocity + Vector3.up * _ccVerticalVel) * dt;
            _cc.Move(dashStep);
            if (rb != null) rb.position = transform.position;
            _localGrounded = _cc.isGrounded && (Time.time - _lastJumpTime >= 0.16f);
            if (Object != null && Object.IsValid && Object.HasStateAuthority)
                NetworkGrounded = _localGrounded;
            return;
        }

        // ความเร็วสไตล์ Where Winds Meet: ย่อง (2.2) / เดินสำรวจ (3.8) / วิ่งเต็มสปีด (~5.9)
        float basePlusGearSpeed = Mathf.Min(TotalMoveSpeed, 5.2f);
        float sprintFactor = Mathf.Clamp(runSpeed, 1.35f, 1.75f);
        float stanceMult = _crouchToggle ? (walkSpeed / Mathf.Max(0.1f, moveSpeed)) : (effectiveSprint ? sprintFactor : 1f);
        float speed = basePlusGearSpeed * stanceMult * Mathf.Max(0f, SpeedMultiplier);

        Vector3 wantedHorizontal = wanted * speed;
        float accelRate = isMoving ? acceleration : deceleration;
        _ccHorizontalVel = Vector3.MoveTowards(_ccHorizontalVel, wantedHorizontal, accelRate * dt);

        _cc.Move((_ccHorizontalVel + Vector3.up * _ccVerticalVel) * dt);
        if (rb != null) rb.position = transform.position;
        _localGrounded = CheckGrounded();

        UpdateFacingAndNetworkParams(wanted, effectiveSprint, dt);
    }

    private void FixedUpdate()
    {
        // หากใช้ CharacterController การเคลื่อนที่ทำงานใน FixedUpdateNetwork แล้ว
        if (!isOwner || (_cc != null && _cc.enabled)) return;

        _localGrounded = CheckGrounded();

        if (IsDashing)
        {
            Vector3 v = rb.linearVelocity;
            rb.linearVelocity = new Vector3(_dashVelocity.x, v.y, _dashVelocity.z);
            if (Object.HasStateAuthority) NetworkGrounded = _localGrounded;
            return;
        }

        var wanted = MovementLocked ? Vector3.zero : DesiredMoveWorld();
        bool isMoving = wanted.sqrMagnitude > 0.001f;

        bool canSprint = _combat == null || (_combat.CanSprint && !_combat.IsBlocking);
        bool effectiveSprint = sprintHeld && isMoving && canSprint && !_crouchToggle;
        if (effectiveSprint && _combat != null)
            effectiveSprint = _combat.ConsumeSprintStamina(Time.fixedDeltaTime);

        float basePlusGearSpeed = Mathf.Min(TotalMoveSpeed, 5.2f);
        float sprintFactor = Mathf.Clamp(runSpeed, 1.35f, 1.75f);
        float stanceMult = _crouchToggle ? (walkSpeed / Mathf.Max(0.1f, moveSpeed)) : (effectiveSprint ? sprintFactor : 1f);
        float speed = basePlusGearSpeed * stanceMult * Mathf.Max(0f, SpeedMultiplier);

        Vector3 wantedVelocity = wanted * speed;
        Vector3 velocity = rb.linearVelocity;
        Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);

        float accelRate = isMoving ? acceleration : deceleration;
        horizontal = Vector3.MoveTowards(horizontal, wantedVelocity, accelRate * Time.fixedDeltaTime);
        rb.linearVelocity = new Vector3(horizontal.x, velocity.y, horizontal.z);

        UpdateFacingAndNetworkParams(wanted, effectiveSprint, Time.fixedDeltaTime);
    }

    private void UpdateFacingAndNetworkParams(Vector3 wanted, bool effectiveSprint, float dt)
    {
        Quaternion targetRotation = transform.rotation;
        bool rotateApplied = false;

        if (FacingTarget != null && !MovementLocked)
        {
            Vector3 toTarget = FacingTarget.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.0001f)
            {
                targetRotation = Quaternion.LookRotation(toTarget, Vector3.up);
                rotateApplied = true;
            }
        }
        else if (MovementReference != null && strafeInActionCamera && wanted.sqrMagnitude > 0.0001f)
        {
            targetRotation = Quaternion.Euler(0f, MovementReference.eulerAngles.y, 0f);
            rotateApplied = true;
        }
        else if (rotateTowardsMovement && wanted.sqrMagnitude > 0.0001f)
        {
            targetRotation = Quaternion.LookRotation(wanted, Vector3.up);
            rotateApplied = true;
        }

        if (rotateApplied)
        {
            Quaternion slerped = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * dt);
            if (_cc != null && _cc.enabled)
            {
                transform.rotation = slerped;
                if (rb != null) rb.rotation = slerped;
            }
            else if (rb != null)
            {
                rb.MoveRotation(slerped);
            }
        }

        if (Object != null && Object.IsValid && Object.HasStateAuthority)
        {
            Vector3 refSpace = (MovementReference != null && strafeInActionCamera && FacingTarget == null)
                ? Quaternion.Inverse(Quaternion.Euler(0f, MovementReference.eulerAngles.y, 0f)) * wanted
                : Quaternion.Inverse(transform.rotation) * wanted;
            NetworkMove = new Vector2(refSpace.x, refSpace.z);
            NetworkSprinting = effectiveSprint;
            NetworkGrounded = _localGrounded;
            NetworkCrouching = _crouchToggle;
        }
    }

    private bool CheckGrounded()
    {
        if (Time.time - _lastJumpTime < 0.18f) return false;
        if (_cc != null && _cc.enabled)
        {
            if (_cc.isGrounded) return true;
            Vector3 origin = transform.position + Vector3.up * 0.25f;
            return Physics.Raycast(origin, Vector3.down, 0.42f, ~0, QueryTriggerInteraction.Ignore);
        }

        Vector3 legacyOrigin = transform.position + Vector3.up * 0.2f;
        float dist = collider != null ? (collider.height * 0.5f + 0.22f) : 1.25f;
        return Physics.Raycast(legacyOrigin, Vector3.down, dist, ~0, QueryTriggerInteraction.Ignore);
    }

    public Vector3 DesiredMoveWorld()
    {
        var wanted = new Vector3(moveInput.x, 0f, moveInput.y);
        if (MovementReference != null) wanted = Quaternion.Euler(0f, MovementReference.eulerAngles.y, 0f) * wanted;
        if (wanted.sqrMagnitude > 1f) wanted.Normalize();
        return wanted;
    }

    /// <summary>
    /// สั่งพุ่งตัวเมื่อกดกลิ้งหลบ (Dodge Roll)
    /// </summary>
    public void StartDash(Vector3 worldDirection, float distance, float duration)
    {
        if (!isOwner || duration <= 0f) return;
        worldDirection.y = 0f;
        if (worldDirection.sqrMagnitude < 0.0001f) worldDirection = transform.forward;
        _dashVelocity = worldDirection.normalized * (distance / duration);
        _dashUntil = Time.time + duration;
    }

    public void SetBodyVisible(bool visible)
    {
        if (characterVisual != null && characterVisual != transform) return;
        if (bodyRenderer != null && bodyRenderer.gameObject == gameObject) bodyRenderer.enabled = visible;
    }

    public override void Render()
    {
        ApplyLook(force: false);
        if (_animationDriver == null && networkAnimator == null)
            ApplyAnimator(NetworkMove, NetworkSprinting);
    }

    private void ApplyAnimator(Vector2 move, bool sprint)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        animator.SetFloat(moveXParameter, move.x, 0.1f, Time.deltaTime);
        animator.SetFloat(moveYParameter, move.y, 0.1f, Time.deltaTime);
        animator.SetBool(sprintParameter, sprint);
    }

    private void SetUpNameLabel()
    {
        if (nameLabel == null) return;

        nameLabel.fontSize = overheadNameFontSize;
        _labelTransform = nameLabel.transform;
        _labelTransform.localScale = Vector3.one * overheadNameScale;
        _labelOffset = characterVisual != null && characterVisual == transform
            ? new Vector3(0f, 2.15f, 0f)
            : overheadNameOffset;
        if (detachNameLabel) _labelTransform.SetParent(null, worldPositionStays: true);
    }

    private void LateUpdate()
    {
        if (_labelTransform == null) return;
        _labelTransform.position = transform.position + _labelOffset;
        var cam = Camera.main;
        if (cam != null) _labelTransform.rotation = cam.transform.rotation;
    }

    private void DestroyDetachedLabel()
    {
        if (detachNameLabel && _labelTransform != null) Destroy(_labelTransform.gameObject);
        _labelTransform = null;
    }

    private void ApplyLook(bool force)
    {
        if (force || _appliedAvatar != AvatarIndex)
        {
            _appliedAvatar = AvatarIndex;
            var color = AvatarPalette.Get(AvatarIndex);

            if (bodyRenderer != null && (characterVisual == null || characterVisual == transform))
            {
                if (bodyRenderer is MeshRenderer)
                {
                    _mpb ??= new MaterialPropertyBlock();
                    bodyRenderer.GetPropertyBlock(_mpb);
                    _mpb.SetColor("_BaseColor", color);
                    _mpb.SetColor("_Color", color);
                    bodyRenderer.SetPropertyBlock(_mpb);
                }
            }
            if (nameLabel != null) nameLabel.color = color;
        }

        string nick = Nick.Value;
        int lvl = Mathf.Clamp(Level, 1, PlayerCombat.MaxLevel);
        if (nameLabel != null && (force || _appliedNick != nick || _appliedLevel != lvl))
        {
            _appliedNick = nick;
            _appliedLevel = lvl;
            nameLabel.fontSize = overheadNameFontSize;
            // แสดงป้าย [MAX] บนชื่อเหนือหัวเมื่อตัวละครถึงเพดาน Lv.30
            nameLabel.text = lvl >= PlayerCombat.MaxLevel
                ? $"Lv.{PlayerCombat.MaxLevel} [MAX] {nick}"
                : $"Lv.{lvl} {nick}";
        }
    }
}
