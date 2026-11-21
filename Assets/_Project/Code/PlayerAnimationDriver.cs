using System.Collections;
using Fusion;
using UnityEngine;

/// <summary>
/// ตัวขับเคลื่อนอนิเมชันของผู้เล่น (Player Animation Driver) สำหรับ Photon Fusion Shared Mode
/// เชื่อมต่อพารามิเตอร์และสถานะทั้งหมดใน PlayerAnimator (ครบ 100% ทุกคลิปอนิเมชัน) ทั้งฝั่ง Host และ Client ผ่าน NetworkMecanimAnimator:
///   - การเคลื่อนที่ 8 ทิศทางและ Sprint (Locomotion / Great Sword Locomotion): MoveX, MoveY, Speed, Direction, Moving, IsMoving, IsSprinting
///   - การก้มย่องลอบสังหาร (Crouch Idle / Crouch Move): IsCrouching
///   - ท่ายืนสุ่ม (Idle Variations): Unarmed Look 1..5 และ Great Sword Look 1..5
///   - การชักดาบ/เก็บดาบ (Weapon Action Layer): Draw A Great Sword, Sheath A Great Sword พร้อมสลับโมเดลดาบในมือและกลางหลัง
///   - การกระโดดและกลางอากาศ: Jump Start (JumpX, JumpY), Fall, Land (Grounded)
///   - การโจมตีครบทุกชุด: Light Attack 1..3, Heavy Attack, Heavy Slash 1..3, Run Attack, Run Heavy Attack, Jump Attack, Jump Heavy Attack, Run Jump Heavy Attack
///   - ท่าลอบสังหารจากด้านหลัง: Stealth Assassination (Assassinate)
///   - การป้องกันและเสียหลัก: Block (IsBlocking), Block Hit (รับดาเมจขณะตั้งการ์ด), Guard Break (การ์ดแตก Stunned)
///   - การกลิ้งหลบ 4 ทิศทาง (Roll / Great Sword Roll), การถูกโจมตี (Hit Reaction), การตาย (Death), การใช้ยา/บัฟ (Buff) และท่าร่ายเวท (Spell Cast)
/// </summary>
[DisallowMultipleComponent]
public class PlayerAnimationDriver : MonoBehaviour
{
    [Header("References (การอ้างอิงคอมโพเนนต์)")]
    // ตัวควบคุม Animator ของโมเดลตัวละคร GreatSword
    [SerializeField] private Animator animator;
    // ตัวซิงก์อนิเมชันผ่านเครือข่าย Photon Fusion (ซิงก์ทั้งพารามิเตอร์และ Trigger ข้ามเครื่องอัตโนมัติ)
    [SerializeField] private NetworkMecanimAnimator networkAnimator;
    // โมเดลดาบในมือขวา (Sword_Hand)
    [SerializeField] private GameObject swordHand;
    // โมเดลดาบที่สะพายอยู่กลางหลัง (Sword_Back)
    [SerializeField] private GameObject swordBack;

    [Header("Locomotion & Combat Stance Settings (ตั้งค่าความนุ่มนวลและสถานะถือดาบ)")]
    // เวลาหน่วงการเกลี่ยค่า Blend Tree ให้การออกตัวและหยุดเดินมีน้ำหนักสมจริงสไตล์ Where Winds Meet
    [SerializeField] private float locomotionDampTime = 0.11f;
    // ค่าสำรองกรณีทดสอบแบบ Offline ที่ไม่มี PlayerCombat (หากมี PlayerCombat จะอิงค่า Networked IsWeaponDrawn เสมอ)
    [SerializeField] private bool alwaysArmedInSandbox = false;
    // จังหวะหน่วงสลับโมเดลดาบจากกลางหลัง -> มือขวา ให้ตรงกับท่าจับด้ามดาบในคลิป Draw A Great Sword
    [SerializeField] private float drawSocketSwapDelay = 0.18f;
    // จังหวะหน่วงสลับโมเดลดาบจากมือขวา -> กลางหลัง ให้ตรงกับท่าวางดาบลงฝักในคลิป Sheath A Great Sword
    [SerializeField] private float sheatheSocketSwapDelay = 0.30f;
    // เวลารอขั้นต่ำและสูงสุดในการสุ่มเล่นท่า Idle Look Variation
    [SerializeField] private float minIdleVariationWait = 7f;
    [SerializeField] private float maxIdleVariationWait = 13f;

    // คืนค่า Animator ที่ใช้งานอยู่
    public Animator Animator => animator;
    // คืนค่า NetworkMecanimAnimator ที่ใช้งานอยู่
    public NetworkMecanimAnimator NetworkAnimator => networkAnimator;
    // ชื่อสถานะอนิเมชันที่เพิ่งสั่งเล่นล่าสุด (ใช้ตรวจสอบและดีบัก)
    public string LastPlayedActionState { get; private set; } = "Unarmed Idle";
    // สถานะว่ากำลังอยู่ในช่วง Micro Hit-Stop (ชะลอภาพอนิเมชันชั่วคราวตอนฟันโดน) หรือไม่
    public bool IsInHitStop { get; private set; }
    // ตรวจสอบตำแหน่งปัจจุบันของโมเดลดาบ (อยู่ในมือขวา หรือสะพายอยู่กลางหลัง)
    public bool IsSwordInHand => swordHand != null && swordHand.activeSelf;
    public bool IsSwordOnBack => swordBack != null && swordBack.activeSelf;

    private PlayerCapsule _capsule;
    private PlayerCombat _combat;
    private float _idleVariationTimer;
    private int _lastIdleVariant;
    private float _hitStopEndRealtime = -1f;
    private const float NormalAnimatorSpeed = 1f;
    // สถานะติดตามการสลับโมเดลดาบในมือ vs กลางหลังให้สัมพันธ์กับคลิป Draw/Sheath
    private bool _lastArmedStance;
    private bool _swordCurrentlyInHand;
    private float _swordSwapAtTime = -1f;
    private bool _pendingSwordInHand;

    // แคชค่า Hash ของพารามิเตอร์ทั้งหมดใน playerAnimator เพื่อประสิทธิภาพสูงสุด
    private static readonly int HashMoveX = Animator.StringToHash("MoveX");
    private static readonly int HashMoveY = Animator.StringToHash("MoveY");
    private static readonly int HashSpeed = Animator.StringToHash("Speed");
    private static readonly int HashDirection = Animator.StringToHash("Direction");
    private static readonly int HashMoving = Animator.StringToHash("Moving");
    private static readonly int HashIsMoving = Animator.StringToHash("IsMoving");
    private static readonly int HashIsSprinting = Animator.StringToHash("IsSprinting");
    private static readonly int HashIsCrouching = Animator.StringToHash("IsCrouching");
    private static readonly int HashGrounded = Animator.StringToHash("Grounded");
    private static readonly int HashInCombat = Animator.StringToHash("InCombat");
    private static readonly int HashSkipDraw = Animator.StringToHash("SkipDraw");
    private static readonly int HashIsBlocking = Animator.StringToHash("IsBlocking");
    private static readonly int HashBlock = Animator.StringToHash("Block");
    private static readonly int HashBlockHit = Animator.StringToHash("BlockHit");
    private static readonly int HashGuardBreak = Animator.StringToHash("GuardBreak");
    private static readonly int HashAttack = Animator.StringToHash("Attack");
    private static readonly int HashHeavyAttack = Animator.StringToHash("HeavyAttack");
    private static readonly int HashRunAttack = Animator.StringToHash("RunAttack");
    private static readonly int HashRunHeavyAttack = Animator.StringToHash("RunHeavyAttack");
    private static readonly int HashJumpAttack = Animator.StringToHash("JumpAttack");
    private static readonly int HashJumpHeavyAttack = Animator.StringToHash("JumpHeavyAttack");
    private static readonly int HashRunJumpHeavyAttack = Animator.StringToHash("RunJumpHeavyAttack");
    private static readonly int HashAssassinate = Animator.StringToHash("Assassinate");
    private static readonly int HashRoll = Animator.StringToHash("Roll");
    private static readonly int HashDodge = Animator.StringToHash("Dodge");
    private static readonly int HashRollX = Animator.StringToHash("RollX");
    private static readonly int HashRollY = Animator.StringToHash("RollY");
    private static readonly int HashJump = Animator.StringToHash("Jump");
    private static readonly int HashJumpX = Animator.StringToHash("JumpX");
    private static readonly int HashJumpY = Animator.StringToHash("JumpY");
    private static readonly int HashHit = Animator.StringToHash("Hit");
    private static readonly int HashDead = Animator.StringToHash("Dead");
    private static readonly int HashBuff = Animator.StringToHash("Buff");
    private static readonly int HashSpellCast = Animator.StringToHash("SpellCast");
    private static readonly int HashIdleVariant = Animator.StringToHash("IdleVariant");
    private static readonly int HashIdleVariation = Animator.StringToHash("IdleVariation");

    // แคชค่า Hash ของ State ใน Base Layer
    private static readonly int StateLightAttack1 = Animator.StringToHash("Base Layer.Light Attack 1");
    private static readonly int StateLightAttack2 = Animator.StringToHash("Base Layer.Light Attack 2");
    private static readonly int StateLightAttack3 = Animator.StringToHash("Base Layer.Light Attack 3");
    private static readonly int StateHeavyAttack = Animator.StringToHash("Base Layer.Heavy Attack");
    private static readonly int StateHeavySlash1 = Animator.StringToHash("Base Layer.Heavy Slash 1");
    private static readonly int StateHeavySlash2 = Animator.StringToHash("Base Layer.Heavy Slash 2");
    private static readonly int StateHeavySlash3 = Animator.StringToHash("Base Layer.Heavy Slash 3");
    private static readonly int StateRunAttack = Animator.StringToHash("Base Layer.Run Attack");
    private static readonly int StateRunHeavyAttack = Animator.StringToHash("Base Layer.Run Heavy Attack");
    private static readonly int StateJumpAttack = Animator.StringToHash("Base Layer.Jump Attack");
    private static readonly int StateJumpHeavyAttack = Animator.StringToHash("Base Layer.Jump Heavy Attack");
    private static readonly int StateRunJumpHeavyAttack = Animator.StringToHash("Base Layer.Run Jump Heavy Attack");
    private static readonly int StateStealthAssassination = Animator.StringToHash("Base Layer.Stealth Assassination");
    private static readonly int StateBlock = Animator.StringToHash("Base Layer.Block");
    private static readonly int StateBlockHit = Animator.StringToHash("Base Layer.Block Hit");
    private static readonly int StateGuardBreak = Animator.StringToHash("Base Layer.Guard Break");
    private static readonly int StateBuff = Animator.StringToHash("Base Layer.Buff");
    private static readonly int StateSpellCast = Animator.StringToHash("Base Layer.Spell Cast");

    // ตัวแปรตรวจสอบว่ามีพารามิเตอร์นั้นๆ อยู่ใน Animator Controller หรือไม่
    private bool _hasSpeedParam;
    private bool _hasIsMovingParam;
    private bool _hasDirectionParam;
    private bool _hasIsSprintingParam;
    private bool _hasIsCrouchingParam;
    private bool _hasIsBlockingParam;
    private bool _hasBlockParam;
    private bool _hasBlockHitParam;
    private bool _hasGuardBreakParam;
    private bool _hasDodgeParam;
    private bool _hasAssassinateParam;
    private bool _hasBuffParam;
    private bool _hasSpellCastParam;
    private bool _hasIdleVariationParam;
    private bool _parametersChecked;

    private void Awake()
    {
        _capsule = GetComponent<PlayerCapsule>();
        _combat = GetComponent<PlayerCombat>();

        ResolveReferences();
        CacheParameterAvailability();
        InitializeAnimatorDefaults();
        _idleVariationTimer = Random.Range(minIdleVariationWait, maxIdleVariationWait);
    }

    /// <summary>
    /// ค้นหาและผูกการอ้างอิง Animator, NetworkMecanimAnimator, Sword_Hand และ Sword_Back อัตโนมัติ
    /// </summary>
    public void ResolveReferences()
    {
        if (animator == null)
        {
            if (_capsule != null && _capsule.CharacterVisual != null)
                animator = _capsule.CharacterVisual.GetComponentInChildren<Animator>();
            if (animator == null)
                animator = GetComponentInChildren<Animator>();
        }

        if (networkAnimator == null)
            networkAnimator = GetComponentInChildren<NetworkMecanimAnimator>();

        if (networkAnimator != null && networkAnimator.Animator == null && animator != null)
            networkAnimator.Animator = animator;

        if (animator != null && (swordHand == null || swordBack == null))
        {
            foreach (Transform t in animator.GetComponentsInChildren<Transform>(true))
            {
                if (swordHand == null && t.name == "Sword_Hand") swordHand = t.gameObject;
                else if (swordBack == null && t.name == "Sword_Back") swordBack = t.gameObject;
            }
        }
    }

    /// <summary>
    /// แคชว่าพารามิเตอร์ตัวใดมีอยู่จริงใน Animator Controller เพื่อความปลอดภัยไม่ให้เกิด Warning
    /// </summary>
    private void CacheParameterAvailability()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;

        _hasSpeedParam = HasParam(HashSpeed);
        _hasIsMovingParam = HasParam(HashIsMoving);
        _hasDirectionParam = HasParam(HashDirection);
        _hasIsSprintingParam = HasParam(HashIsSprinting);
        _hasIsCrouchingParam = HasParam(HashIsCrouching);
        _hasIsBlockingParam = HasParam(HashIsBlocking);
        _hasBlockParam = HasParam(HashBlock);
        _hasBlockHitParam = HasParam(HashBlockHit);
        _hasGuardBreakParam = HasParam(HashGuardBreak);
        _hasDodgeParam = HasParam(HashDodge);
        _hasAssassinateParam = HasParam(HashAssassinate);
        _hasBuffParam = HasParam(HashBuff);
        _hasSpellCastParam = HasParam(HashSpellCast);
        _hasIdleVariationParam = HasParam(HashIdleVariation);
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

    /// <summary>
    /// กำหนดค่าเริ่มต้นของ Animator เมื่อผู้เล่นเกิดในฉาก (เริ่มต้นเก็บดาบไว้กลางหลังจนกว่าจะกด [1])
    /// </summary>
    private void InitializeAnimatorDefaults()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;

        bool initialArmed = (_combat != null && _combat.IsSpawned)
            ? (_combat.IsWeaponDrawn && !_combat.IsInSafeZone)
            : false;

        animator.applyRootMotion = false;
        animator.SetBool(HashGrounded, true);
        animator.SetBool(HashSkipDraw, true);
        animator.SetBool(HashInCombat, initialArmed);

        SyncWeaponDrawnImmediate(initialArmed);
    }

    /// <summary>
    /// สลับตำแหน่งโมเดลดาบและสถานะตั้งต้นทันทีโดยไม่ต้องรอคลิป Draw/Sheath (ใช้ตอน Spawn หรือ Reset)
    /// </summary>
    public void SyncWeaponDrawnImmediate(bool drawn)
    {
        _lastArmedStance = drawn;
        _swordCurrentlyInHand = drawn;
        _pendingSwordInHand = drawn;
        _swordSwapAtTime = -1f;
        UpdateSwordVisuals(drawn);
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            animator.SetBool(HashSkipDraw, true);
            animator.SetBool(HashInCombat, drawn);
        }
        LastPlayedActionState = drawn ? "Great Sword Idle" : "Unarmed Idle";
    }

    /// <summary>
    /// รับสัญญาณเมื่อค่า Networked IsWeaponDrawn เปลี่ยนแปลง เพื่อเริ่มเล่นคลิปชักดาบ/เก็บดาบและตั้งเวลาสลับตำแหน่งโมเดลดาบ
    /// </summary>
    public void NotifyWeaponDrawnChanged(bool drawn)
    {
        _lastArmedStance = drawn;
        _pendingSwordInHand = drawn;
        float delay = drawn ? Mathf.Max(0f, drawSocketSwapDelay) : Mathf.Max(0f, sheatheSocketSwapDelay);
        _swordSwapAtTime = Time.time + delay;

        if (animator != null && animator.runtimeAnimatorController != null)
        {
            // เปิด SkipDraw = false เพื่อให้เลเยอร์ Weapon Action เล่นท่า Draw A Great Sword / Sheath A Great Sword อย่างสมบูรณ์
            animator.SetBool(HashSkipDraw, false);
            animator.SetBool(HashInCombat, drawn);
        }
        LastPlayedActionState = drawn ? "Draw A Great Sword" : "Sheath A Great Sword";
    }

    /// <summary>
    /// สั่งยิง Trigger ให้ทั้ง Animator ในเครื่องและซิงก์ผ่าน NetworkMecanimAnimator ข้ามเครือข่าย (ถ้าเราเป็นเจ้าของ State Authority)
    /// </summary>
    private void FireSyncedTrigger(int triggerHash)
    {
        if (animator == null) return;
        animator.ResetTrigger(triggerHash);

        bool canUseNma = networkAnimator != null && _combat != null && _combat.Object != null &&
                         _combat.Object.IsValid && _combat.Object.HasStateAuthority;
        if (canUseNma)
        {
            networkAnimator.SetTrigger(triggerHash, passThroughOnInputAuthority: true);
        }
        else
        {
            animator.SetTrigger(triggerHash);
        }
    }

    private void LateUpdate()
    {
        // คืนความเร็ว Animator กลับเป็นปกติ (1.0) ทันทีเมื่อหมดช่วงเวลา Micro Hit-Stop (ปลอดภัยแม้ฟันโดนหลายตัวพร้อมกัน)
        if (IsInHitStop && Time.realtimeSinceStartup >= _hitStopEndRealtime)
        {
            if (animator != null) animator.speed = NormalAnimatorSpeed;
            IsInHitStop = false;
        }

        if (animator == null || animator.runtimeAnimatorController == null) return;
        if (!_parametersChecked) CacheParameterAvailability();

        bool isDead = _combat != null && _combat.IsDead;
        bool isBlocking = _combat != null && _combat.IsBlocking && !isDead;
        bool inSafeZone = _combat != null && _combat.IsInSafeZone;
        bool armedStance = (_combat != null && _combat.IsSpawned)
            ? (_combat.IsWeaponDrawn && !inSafeZone && !isDead)
            : (alwaysArmedInSandbox && !inSafeZone && !isDead);

        bool wantArmedVisual = armedStance || isBlocking;

        // ตรวจจับการเปลี่ยนสถานะถือดาบ/เก็บดาบทั้งบน Local และ Remote Proxies เพื่อสลับโมเดลดาบตรงจังหวะแอนิเมชัน
        if (wantArmedVisual != _lastArmedStance)
        {
            _lastArmedStance = wantArmedVisual;
            _pendingSwordInHand = wantArmedVisual;
            if (isDead)
            {
                _swordSwapAtTime = -1f;
                _swordCurrentlyInHand = false;
                UpdateSwordVisuals(false);
            }
            else
            {
                float delay = wantArmedVisual ? Mathf.Max(0f, drawSocketSwapDelay) : Mathf.Max(0f, sheatheSocketSwapDelay);
                _swordSwapAtTime = Time.time + delay;
                LastPlayedActionState = wantArmedVisual ? "Draw A Great Sword" : "Sheath A Great Sword";
            }
        }

        // สลับโมเดลดาบระหว่างมือขวา (Sword_Hand) และกลางหลัง (Sword_Back) เมื่อถึงจังหวะมือแตะด้ามดาบ
        if (_swordSwapAtTime >= 0f && Time.time >= _swordSwapAtTime)
        {
            _swordSwapAtTime = -1f;
            _swordCurrentlyInHand = _pendingSwordInHand;
            UpdateSwordVisuals(_swordCurrentlyInHand);
        }
        else if (_swordSwapAtTime < 0f && _swordCurrentlyInHand != wantArmedVisual)
        {
            _swordCurrentlyInHand = wantArmedVisual;
            UpdateSwordVisuals(_swordCurrentlyInHand);
        }

        // หากเป็นตัวละครของผู้เล่นคนอื่น (Remote Proxy) และมี NetworkMecanimAnimator คอยซิงก์ให้อยู่แล้ว ไม่ต้องเขียนทับค่าพารามิเตอร์ซ้ำ
        bool isRemoteProxy = _combat != null && _combat.Object != null && _combat.Object.IsValid && !_combat.Object.HasStateAuthority;
        if (isRemoteProxy && networkAnimator != null)
        {
            return;
        }

        Vector2 netMove = _capsule != null ? _capsule.NetworkMove : Vector2.zero;
        bool netSprint = _capsule != null && _capsule.NetworkSprinting;
        bool netGrounded = _capsule == null || _capsule.IsGrounded;
        bool netCrouch = _capsule != null && _capsule.IsCrouching;

        animator.SetBool(HashGrounded, netGrounded);
        animator.SetBool(HashSkipDraw, false);
        animator.SetBool(HashInCombat, armedStance);
        animator.SetBool(HashDead, isDead);

        if (_hasIsBlockingParam)
            animator.SetBool(HashIsBlocking, isBlocking);
        if (_hasIsCrouchingParam)
            animator.SetBool(HashIsCrouching, netCrouch && !isDead);

        if (isDead)
        {
            SetLocomotionImmediate(0f, 0f, false, false);
            return;
        }

        // ขณะอยู่ในท่าโจมตี (Tag = "Attack") ให้หยุดการส่งค่า Moving ชั่วคราวเพื่อให้แอนิเมชันฟันจบครบวงสวิง
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
        // Blend Tree 2D Freeform Directional: 1.0 = เดิน/ก้าวด้านข้าง 8 ทิศทาง, 2.0..2.5 = วิ่ง/สปรินท์เร็ว
        float speedScale = (netSprint && !isBlocking && !netCrouch) ? 2.25f : 1f;
        float targetX = moving ? Mathf.Clamp(netMove.x, -1f, 1f) * speedScale : 0f;
        float targetY = moving ? Mathf.Clamp(netMove.y, -1f, 1f) * speedScale : 0f;
        float speed = moving ? Mathf.Clamp01(netMove.magnitude) * speedScale : 0f;
        float direction = moving ? Mathf.Atan2(netMove.x, netMove.y) * Mathf.Rad2Deg : 0f;

        animator.SetBool(HashMoving, moving);
        if (_hasIsMovingParam) animator.SetBool(HashIsMoving, moving);
        if (_hasIsSprintingParam) animator.SetBool(HashIsSprinting, netSprint && moving && !isBlocking && !netCrouch);

        animator.SetFloat(HashMoveX, targetX, locomotionDampTime, Time.deltaTime);
        animator.SetFloat(HashMoveY, targetY, locomotionDampTime, Time.deltaTime);
        if (_hasSpeedParam) animator.SetFloat(HashSpeed, speed, locomotionDampTime, Time.deltaTime);
        if (_hasDirectionParam) animator.SetFloat(HashDirection, direction, locomotionDampTime, Time.deltaTime);

        if (!isRemoteProxy)
        {
            TickIdleVariations(moving, netGrounded, isBlocking, netCrouch, armedStance);
        }
    }

    /// <summary>
    /// สุ่มเล่นท่ายืนมองรอบตัว (Idle Look Variations 1..5 ทั้งมือเปล่าและถือดาบ) เมื่อยืนนิ่ง
    /// </summary>
    private void TickIdleVariations(bool moving, bool grounded, bool blocking, bool crouching, bool armed)
    {
        if (!_hasIdleVariationParam) return;
        if (moving || !grounded || blocking || crouching)
        {
            _idleVariationTimer = Random.Range(minIdleVariationWait, maxIdleVariationWait);
            return;
        }

        _idleVariationTimer -= Time.deltaTime;
        if (_idleVariationTimer <= 0f)
        {
            _idleVariationTimer = Random.Range(minIdleVariationWait, maxIdleVariationWait);
            int maxVar = 5;
            int nextVar = Random.Range(1, maxVar + 1);
            if (nextVar == _lastIdleVariant) nextVar = (nextVar % maxVar) + 1;
            _lastIdleVariant = nextVar;

            animator.SetInteger(HashIdleVariant, nextVar);
            FireSyncedTrigger(HashIdleVariation);
        }
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

    /// <summary>
    /// สลับการแสดงผลโมเดลดาบในมือ (Sword_Hand) และดาบสะพายหลัง (Sword_Back)
    /// </summary>
    private void UpdateSwordVisuals(bool armed)
    {
        if (swordHand != null && swordHand.activeSelf != armed)
            swordHand.SetActive(armed);
        if (swordBack != null && swordBack.activeSelf == armed)
            swordBack.SetActive(!armed);
    }

    // ------------------------------------------------------------------ ฟังก์ชันสั่งเล่นอนิเมชันต่อสู้และแอ็กชัน (ซิงก์ทุกเครื่อง)

    /// <summary>
    /// สั่งเล่นท่าโจมตีเบาตามจังหวะคอมโบ (1..3)
    /// </summary>
    public void PlayAttack(int comboStep)
    {
        PlayAttackKind(PlayerAttackKind.LightCombo, comboStep);
    }

    /// <summary>
    /// สั่งเล่นอนิเมชันโจมตีครบทุกรูปแบบใน PlayerAnimator (Light 1..3, Heavy 1..3, Run, Jump, Stealth Assassination, Spell Cast)
    /// </summary>
    public void PlayAttackKind(PlayerAttackKind kind, int comboStep = 1)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;

        animator.SetBool(HashSkipDraw, true);
        animator.SetBool(HashInCombat, true);
        _swordSwapAtTime = -1f;
        _swordCurrentlyInHand = true;
        _lastArmedStance = true;
        UpdateSwordVisuals(true);

        animator.ResetTrigger(HashRoll);
        if (_hasDodgeParam) animator.ResetTrigger(HashDodge);

        int targetState;
        switch (kind)
        {
            case PlayerAttackKind.StealthAssassination:
                if (_hasAssassinateParam) FireSyncedTrigger(HashAssassinate);
                targetState = StateStealthAssassination;
                LastPlayedActionState = "Stealth Assassination";
                break;

            case PlayerAttackKind.HeavyCombo:
                FireSyncedTrigger(HashHeavyAttack);
                targetState = comboStep switch
                {
                    2 => StateHeavySlash2,
                    3 => StateHeavySlash3,
                    _ => StateHeavySlash1
                };
                LastPlayedActionState = $"Heavy Slash {Mathf.Clamp(comboStep, 1, 3)}";
                break;

            case PlayerAttackKind.RunAttack:
                FireSyncedTrigger(HashRunAttack);
                targetState = StateRunAttack;
                LastPlayedActionState = "Run Attack";
                break;

            case PlayerAttackKind.RunHeavyAttack:
                FireSyncedTrigger(HashRunHeavyAttack);
                targetState = StateRunHeavyAttack;
                LastPlayedActionState = "Run Heavy Attack";
                break;

            case PlayerAttackKind.JumpAttack:
                FireSyncedTrigger(HashJumpAttack);
                targetState = StateJumpAttack;
                LastPlayedActionState = "Jump Attack";
                break;

            case PlayerAttackKind.JumpHeavyAttack:
                FireSyncedTrigger(HashJumpHeavyAttack);
                targetState = StateJumpHeavyAttack;
                LastPlayedActionState = "Jump Heavy Attack";
                break;

            case PlayerAttackKind.RunJumpHeavyAttack:
                FireSyncedTrigger(HashRunJumpHeavyAttack);
                targetState = StateRunJumpHeavyAttack;
                LastPlayedActionState = "Run Jump Heavy Attack";
                break;

            case PlayerAttackKind.SpellCast:
                if (_hasSpellCastParam) FireSyncedTrigger(HashSpellCast);
                targetState = StateSpellCast;
                LastPlayedActionState = "Spell Cast";
                break;

            default:
                FireSyncedTrigger(HashAttack);
                targetState = comboStep switch
                {
                    2 => StateLightAttack2,
                    3 => StateLightAttack3,
                    _ => StateLightAttack1
                };
                LastPlayedActionState = $"Light Attack {Mathf.Clamp(comboStep, 1, 3)}";
                break;
        }

        animator.CrossFadeInFixedTime(targetState, 0.09f, 0, 0f);
    }

    /// <summary>
    /// สั่งเล่นท่ากระโดด (Jump Start BlendTree) พร้อมทิศทาง JumpX, JumpY
    /// </summary>
    public void PlayJump(float jumpX, float jumpY)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        animator.SetFloat(HashJumpX, Mathf.Clamp(jumpX, -1f, 1f));
        animator.SetFloat(HashJumpY, Mathf.Clamp(jumpY, -1f, 1f));
        FireSyncedTrigger(HashJump);
        LastPlayedActionState = "Jump Start";
    }

    /// <summary>
    /// สั่งเล่นท่ากลิ้งหลบ 4 ทิศทาง (Great Sword Roll)
    /// </summary>
    public void PlayDodge(float rollX, float rollY)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;

        animator.ResetTrigger(HashAttack);
        animator.ResetTrigger(HashHeavyAttack);
        animator.ResetTrigger(HashRunAttack);

        animator.SetFloat(HashRollX, Mathf.Clamp(rollX, -1f, 1f));
        animator.SetFloat(HashRollY, Mathf.Clamp(rollY, -1f, 1f));

        FireSyncedTrigger(HashRoll);
        if (_hasDodgeParam) FireSyncedTrigger(HashDodge);
        LastPlayedActionState = "Great Sword Roll";
    }

    /// <summary>
    /// อัปเดตท่าตั้งการ์ดป้องกัน (Block Stance)
    /// </summary>
    public void SetBlocking(bool blocking)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;

        if (blocking)
        {
            animator.ResetTrigger(HashAttack);
            animator.SetBool(HashSkipDraw, true);
            animator.SetBool(HashInCombat, true);
            _swordSwapAtTime = -1f;
            _swordCurrentlyInHand = true;
            _lastArmedStance = true;
            UpdateSwordVisuals(true);
            if (_hasBlockParam) FireSyncedTrigger(HashBlock);
            if (!animator.GetCurrentAnimatorStateInfo(0).IsName("Block"))
                animator.CrossFadeInFixedTime(StateBlock, 0.08f, 0);
            LastPlayedActionState = "Block";
        }

        if (_hasIsBlockingParam)
            animator.SetBool(HashIsBlocking, blocking);
    }

    /// <summary>
    /// เล่นอนิเมชันรับแรงปะทะขณะตั้งการ์ด (Block Hit / Parry Impact)
    /// </summary>
    public void PlayBlockImpact()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        if (_hasBlockHitParam) FireSyncedTrigger(HashBlockHit);
        animator.CrossFadeInFixedTime(StateBlockHit, 0.06f, 0, 0f);
        LastPlayedActionState = "Block Hit";
    }

    /// <summary>
    /// เล่นอนิเมชันปัดป้องสมบูรณ์ (Perfect Parry Impact) โดยรีเซ็ต Trigger โจมตีค้างและสั่งเล่นท่า Block Hit อย่างคมชัด
    /// </summary>
    public void PlayParryImpact()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        animator.ResetTrigger(HashAttack);
        animator.ResetTrigger(HashHeavyAttack);
        animator.ResetTrigger(HashRunAttack);
        if (_hasBlockHitParam) FireSyncedTrigger(HashBlockHit);
        animator.CrossFadeInFixedTime(StateBlockHit, 0.04f, 0, 0f);
        LastPlayedActionState = "Perfect Parry";
    }

    /// <summary>
    /// สั่งทำ Micro Hit-Stop เฉพาะภาพอนิเมชันในเครื่อง (ไม่แตะ Time.timeScale เพื่อไม่ให้ Photon Fusion Tick เสียจังหวะ)
    /// ใช้เวลาจริงแบบ Deadline เพื่อไม่ให้เกิด Race Condition เมื่อฟันโดนศัตรูหลายตัวในวงสวิงเดียวกัน
    /// </summary>
    public void TriggerHitStop(float durationSeconds, float slowSpeed = 0.06f)
    {
        if (animator == null || !gameObject.activeInHierarchy || durationSeconds <= 0f) return;

        float clampedDuration = Mathf.Clamp(durationSeconds, 0.015f, 0.14f);
        float clampedSlow = Mathf.Clamp(slowSpeed, 0.02f, 0.4f);

        _hitStopEndRealtime = Mathf.Max(_hitStopEndRealtime, Time.realtimeSinceStartup + clampedDuration);
        animator.speed = clampedSlow;
        IsInHitStop = true;
    }

    private void OnDisable()
    {
        // คืนความเร็ว Animator กลับเป็นปกติเสมอหากคอมโพเนนต์ถูกปิดหรือ Despawn ระหว่างติด Hit-Stop
        if (IsInHitStop && animator != null)
            animator.speed = NormalAnimatorSpeed;
        IsInHitStop = false;
        _hitStopEndRealtime = -1f;
    }

    /// <summary>
    /// เล่นอนิเมชันการ์ดแตกเมื่อ Stamina หมดขณะบล็อก (Guard Break / Stunned)
    /// </summary>
    public void PlayGuardBreak()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        if (_hasGuardBreakParam) FireSyncedTrigger(HashGuardBreak);
        animator.CrossFadeInFixedTime(StateGuardBreak, 0.08f, 0, 0f);
        LastPlayedActionState = "Guard Break";
    }

    /// <summary>
    /// เล่นอนิเมชันถูกโจมตี (Hit Reaction)
    /// </summary>
    public void PlayHitReaction()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        FireSyncedTrigger(HashHit);
        LastPlayedActionState = "Hit Reaction";
    }

    /// <summary>
    /// เล่นอนิเมชันใช้ไอเทมฟื้นฟูหรือเคลือบน้ำมันดาบ (Buff)
    /// </summary>
    public void PlayBuff()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        if (_hasBuffParam) FireSyncedTrigger(HashBuff);
        animator.CrossFadeInFixedTime(StateBuff, 0.1f, 0, 0f);
        LastPlayedActionState = "Buff";
    }

    /// <summary>
    /// ตั้งค่าสถานะตาย (Death)
    /// </summary>
    public void SetDead(bool dead)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        animator.SetBool(HashDead, dead);
        if (dead) LastPlayedActionState = "Death";
    }
}
