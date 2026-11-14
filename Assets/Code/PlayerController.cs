using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Keys (เปลี่ยนได้ใน Inspector)")]
    public Key runKey = Key.LeftShift;
    public Key rollKey = Key.Space;
    public Key jumpKey = Key.F;

    [Header("Mode (ถ้ามี LockOnSystem จะถูกตั้งอัตโนมัติ)")]
    public bool lockOn = false;

    [Header("Move")]
    public float walkSpeed = 3f;
    public float runSpeed = 5f;
    public float turnSpeed = 10f;
    [Range(0f, 1f)] public float airControl = 0.5f;

    [Header("Roll")]
    public float rollDistance = 3.5f;
    public float rollDuration = 0.55f;
    public float rollLockTime = 0.8f;
    public float rollCooldown = 0.1f;
    public float rollBufferWindow = 0.25f;
    public AnimationCurve rollCurve = new AnimationCurve(
        new Keyframe(0f, 1.8f),
        new Keyframe(0.35f, 1.3f),
        new Keyframe(1f, 0f));

    [Header("Attack (เมาส์ซ้าย = เบา, เมาส์ขวา = หนัก)")]
    [Range(0f, 1f)] public float rollCancelAt = 0.7f; // กลิ้งยกเลิกท่าโจมตีได้เมื่อท่าเล่นไปกี่ %
    public float attackBufferWindow = 0.3f;           // จำปุ่มโจมตีที่กดล่วงหน้าไว้กี่วินาที (0 = ไม่จำ)
    public float triggerLife = 0.4f;                  // Trigger โจมตีที่ไม่มีใครรับ จะหมดอายุกี่วินาที
    public float attackTurnSpeed = 5f;                // ตอน Lock-on หันหาเป้าระหว่างฟัน
    public float forceCombatAttackLock = 0.3f;        // กดตีตอนยังไม่ถือดาบ: ล็อกการเดินไว้กี่วินาที ให้ Animator สลับเป็นชุดถือดาบก่อน

    [Header("Attack Movement (ก้าวพุ่งตอนฟัน)")]
    public bool useAttackRootMotion = true;           // ใช้การขยับจากคลิป (Position XZ ต้องไม่ติ๊ก Bake Into Pose)
    public float attackRootMotionScale = 1f;          // ปรับระยะก้าวของทุกท่า (1 = ตามคลิป)
    [Range(0f, 1f)] public float momentumKeep = 0.8f; // เคลื่อนที่/วิ่งแล้วฟัน เก็บความเร็วไว้กี่ % (0 = ไม่เก็บ)
    public float momentumDecay = 6f;                  // ความเร็วที่เก็บไว้ลดลงเร็วแค่ไหน (มาก = หยุดเร็ว)

    [Header("Invincibility Frames")]
    public float iFrameStart = 0.05f;
    public float iFrameEnd = 0.4f;
    public bool IsInvincible => rolling && rollTimer >= iFrameStart && rollTimer <= iFrameEnd;

    [Header("Jump")]
    public float jumpHeight = 1.2f;
    public float gravity = -20f;
    public float coyoteTime = 0.1f;

    CharacterController cc;
    Animator anim;
    Transform cam;
    LockOnSystem lockSys;
    CombatState combat;

    Vector3 airMove;
    Vector3 carryVel;
    float verticalVel;
    float lastGroundedTime;

    bool rolling;
    float rollTimer;
    float nextRollTime;
    float rollBufferStamp = -999f;
    float curveArea = 1f;
    Vector3 rollDir;

    string pendingTrigger;
    float pendingTime;
    float attackLockUntil;

    // ที่จำปุ่มโจมตี
    bool bufferedLight;
    bool bufferedHeavy;
    float attackBufferStamp = -999f;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        anim = GetComponent<Animator>();
        lockSys = GetComponent<LockOnSystem>();
        combat = GetComponent<CombatState>();
        cam = Camera.main.transform;
    }

    // ใช้การขยับตัวจากคลิป "เฉพาะตอนอยู่ในท่าโจมตี" (ท่าอื่นโค้ดคุมเอง)
    void OnAnimatorMove()
    {
        if (!useAttackRootMotion) return;
        if (!anim.GetCurrentAnimatorStateInfo(0).IsTag("Attack")) return;

        Vector3 d = anim.deltaPosition;
        d.y = 0f;
        cc.Move(d * attackRootMotionScale);
    }

    // ทิศที่ตัวละครหัน: ถ้าล็อกเป้าอยู่ = หันหาเป้า ไม่งั้นใช้ค่าสำรอง
    Vector3 FaceDir(Vector3 fallback)
    {
        if (lockSys != null && lockSys.HasTarget) return lockSys.FlatDirFrom(transform.position);
        return fallback;
    }

    // กำลังอยู่ในท่าโจมตีหรือไม่ (progress = ท่าเล่นไปกี่ %)
    bool IsAttacking(out float progress)
    {
        progress = 0f;
        var cur = anim.GetCurrentAnimatorStateInfo(0);
        bool inAttack = cur.IsTag("Attack");
        if (inAttack) progress = cur.normalizedTime;
        if (anim.IsInTransition(0) && anim.GetNextAnimatorStateInfo(0).IsTag("Attack")) inAttack = true;
        return inAttack || Time.time < attackLockUntil;
    }

    // กำลังอยู่ Light Attack 1 หรือ 2 (ต่อคอมโบต่อได้)
    bool InLightCombo()
    {
        var cur = anim.GetCurrentAnimatorStateInfo(0);
        return cur.IsName("Light Attack 1") || cur.IsName("Light Attack 2");
    }

    // ยิงท่าโจมตี (ใช้ปุ่มที่จำไว้แล้ว จึงล้างที่จำปุ่มทุกครั้ง)
    void FireAttack(string trigger)
    {
        pendingTrigger = trigger;
        pendingTime = Time.time;
        attackLockUntil = Time.time + 0.1f; // กันขยับ 1-2 เฟรมก่อน Animator เข้าท่า
        anim.ResetTrigger(trigger);
        anim.SetTrigger(trigger);
        bufferedLight = false;
        bufferedHeavy = false;
        if (combat != null) combat.NotifyAttack(); // ต่อเวลาถือดาบ
    }

    // เคลียร์ Trigger ที่ไม่มี Transition ไหนรับ กันไปยิงทีหลังแบบไม่ตั้งใจ
    void ExpireStaleTrigger(bool attacking)
    {
        if (pendingTrigger == null || attacking) return;
        if (Time.time - pendingTime > triggerLife)
        {
            anim.ResetTrigger(pendingTrigger);
            pendingTrigger = null;
        }
    }

    void ResetAttackTriggers()
    {
        anim.ResetTrigger("Attack");
        anim.ResetTrigger("HeavyAttack");
        anim.ResetTrigger("RunAttack");
        anim.ResetTrigger("RunHeavyAttack");
        anim.ResetTrigger("JumpAttack");
        anim.ResetTrigger("JumpHeavyAttack");
        anim.ResetTrigger("RunJumpHeavyAttack");
        pendingTrigger = null;
    }

    // จำปุ่มโจมตีที่เพิ่งกด (เรียกทุกเฟรม แม้กำลังกลิ้งอยู่)
    void BufferAttackInput(Mouse mouse)
    {
        if (mouse.leftButton.wasPressedThisFrame)
        {
            bufferedLight = true;
            bufferedHeavy = false;
            attackBufferStamp = Time.time;
        }
        if (mouse.rightButton.wasPressedThisFrame)
        {
            bufferedHeavy = true;
            bufferedLight = false;
            attackBufferStamp = Time.time;
        }
    }

    // มีปุ่มโจมตีที่จำไว้และยังไม่หมดอายุหรือไม่
    bool HasBufferedAttack(out bool light, out bool heavy)
    {
        light = bufferedLight;
        heavy = bufferedHeavy;
        if (!light && !heavy) return false;

        if (Time.time - attackBufferStamp > attackBufferWindow)
        {
            bufferedLight = false;
            bufferedHeavy = false;
            light = false;
            heavy = false;
            return false;
        }
        return true;
    }

    void HandleAttackInput(bool grounded, bool moving, bool run, bool attacking)
    {
        if (!HasBufferedAttack(out bool light, out bool heavy)) return;

        // ระหว่างฟัน: กดเบาเพื่อต่อคอมโบได้เฉพาะตอนอยู่ Light 1/2
        // ปุ่มอื่น (เช่น ตอน Light 3 หรือกดตีหนักระหว่างฟัน) จะถูกจำไว้ แล้วยิงทันทีเมื่อท่าจบ ถ้ายังไม่หมดเวลา
        if (attacking)
        {
            if (light && InLightCombo()) FireAttack("Attack");
            return;
        }

        if (!grounded)
        {
            // กลางอากาศ: ตีหนัก = ท่าเฉพาะ (กระโดดตอนวิ่ง ใช้ท่า Run+Jump+Heavy), ตีเบา = Jump Attack
            bool runningJump = airMove.magnitude >= (walkSpeed + runSpeed) * 0.5f;
            if (heavy) FireAttack(runningJump ? "RunJumpHeavyAttack" : "JumpHeavyAttack");
            else if (light) FireAttack("JumpAttack");
            return;
        }

        // เริ่มโจมตีบนพื้น: เก็บความเร็วที่กำลังเคลื่อนที่ไว้ (วิ่งแล้วฟัน/ตีหนัก จะพุ่งต่อไปข้างหน้า)
        carryVel = moving ? airMove * momentumKeep : Vector3.zero;

        if (heavy) FireAttack(run && moving ? "RunHeavyAttack" : "HeavyAttack");
        else if (run && moving) FireAttack("RunAttack");
        else FireAttack("Attack");
    }

    void Update()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb == null) return;

        if (lockSys != null) lockOn = lockSys.HasTarget;
        if (kb[rollKey].wasPressedThisFrame) rollBufferStamp = Time.time;
        if (mouse != null && attackBufferWindow > 0f) BufferAttackInput(mouse);
        else if (mouse != null) { bufferedLight = mouse.leftButton.wasPressedThisFrame; bufferedHeavy = mouse.rightButton.wasPressedThisFrame; attackBufferStamp = Time.time; }

        // ---- Input ----
        Vector2 input = Vector2.zero;
        if (kb.wKey.isPressed) input.y += 1;
        if (kb.sKey.isPressed) input.y -= 1;
        if (kb.dKey.isPressed) input.x += 1;
        if (kb.aKey.isPressed) input.x -= 1;
        input = Vector2.ClampMagnitude(input, 1f);

        bool moving = input.sqrMagnitude > 0.01f;
        bool run = kb[runKey].isPressed;

        Vector3 camF = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
        Vector3 camR = Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized;
        Vector3 wishDir = camF * input.y + camR * input.x;

        // ---- พื้น + แรงโน้มถ่วง ----
        bool grounded = cc.isGrounded;
        if (grounded) lastGroundedTime = Time.time;
        if (grounded && verticalVel < 0f) verticalVel = -2f;
        verticalVel += gravity * Time.deltaTime;
        // จะอัปเดต Grounded หลัง CharacterController.Move() เพื่อใช้ผลชนพื้นของเฟรมล่าสุด

        bool armed = combat != null && combat.InCombat;
        bool attacking = IsAttacking(out float attackProgress);
        ExpireStaleTrigger(attacking);

        // ---- กำลังกลิ้ง: ล็อกการควบคุม ----
        if (rolling)
        {
            rollTimer += Time.deltaTime;
            float t = rollTimer / rollDuration;
            float speed = t < 1f
                ? rollDistance / rollDuration * rollCurve.Evaluate(t) / curveArea
                : 0f;
            cc.Move((rollDir * speed + Vector3.up * verticalVel) * Time.deltaTime);
            anim.SetBool("Grounded", cc.isGrounded);
            if (rollTimer >= Mathf.Max(rollLockTime, rollDuration)) rolling = false;
            return;
        }

        // ---- เริ่มกลิ้ง (กลิ้งยกเลิกท่าโจมตีได้ช่วงท้ายท่า) ----
        bool wantRoll = Time.time - rollBufferStamp <= rollBufferWindow;
        bool rollAllowed = !attacking || attackProgress >= rollCancelAt;
        if (wantRoll && grounded && Time.time >= nextRollTime && rollAllowed)
        {
            StartRoll(input, moving, wishDir, camF, camR);
            return;
        }

        // ---- กดตีตอนยังไม่ได้ถือดาบ → เข้าสถานะต่อสู้ทันที (ข้ามท่าชักดาบ) ----
        bool forcedCombat = false;
        if (!armed && combat != null && HasBufferedAttack(out _, out _))
        {
            combat.ForceCombat();
            armed = true;
            forcedCombat = true;
        }

        // ---- โจมตี (เฉพาะตอนถือดาบ) ใช้ปุ่มที่จำไว้ ----
        float pendingBefore = pendingTime;
        if (armed) HandleAttackInput(grounded, moving, run, attacking);

        // ถ้าเพิ่งเข้าสถานะต่อสู้จากการกดตี: ล็อกการเดินสั้นๆ ให้ Animator สลับเป็นชุดถือดาบก่อน
        if (forcedCombat && pendingTime != pendingBefore)
            attackLockUntil = Time.time + forceCombatAttackLock;

        attacking = IsAttacking(out attackProgress);

        // ---- ระหว่างโจมตี: ล็อกการเดิน (ก้าวพุ่งมาจากคลิป + แรงที่เก็บไว้) ----
        if (attacking)
        {
            carryVel = Vector3.Lerp(carryVel, Vector3.zero, momentumDecay * Time.deltaTime);
            cc.Move((carryVel + Vector3.up * verticalVel) * Time.deltaTime);
            anim.SetBool("Grounded", cc.isGrounded);
            airMove = Vector3.zero;

            // รอให้ Animator เข้าท่าโจมตีจริงก่อน ค่อยหยุด Moving (กัน Transition ไป Idle แย่งท่าโจมตี)
            var curS = anim.GetCurrentAnimatorStateInfo(0);
            bool inAttackState = curS.IsTag("Attack") ||
                (anim.IsInTransition(0) && anim.GetNextAnimatorStateInfo(0).IsTag("Attack"));
            if (inAttackState)
            {
                anim.SetBool("Moving", false);
                anim.SetFloat("MoveX", 0f, 0.1f, Time.deltaTime);
                anim.SetFloat("MoveY", 0f, 0.1f, Time.deltaTime);
            }

            if (lockOn)
            {
                Quaternion look = Quaternion.LookRotation(FaceDir(camF));
                transform.rotation = Quaternion.Slerp(transform.rotation, look, attackTurnSpeed * Time.deltaTime);
            }
            return;
        }

        carryVel = Vector3.zero;

        // ---- กระโดด (มี coyote time) ----
        bool canJump = Time.time - lastGroundedTime <= coyoteTime;
        if (kb[jumpKey].wasPressedThisFrame && canJump)
        {
            verticalVel = Mathf.Sqrt(-2f * gravity * jumpHeight);
            lastGroundedTime = -999f;

            // ทิศสำหรับเลือกท่ากระโดด: Lock-on = ตามปุ่มที่กด, โหมดอิสระ = หน้าถ้ากำลังเคลื่อนที่
            Vector2 jin = moving ? input.normalized : Vector2.zero;
            if (!lockOn && moving) jin = new Vector2(0f, 1f);
            anim.SetFloat("JumpX", jin.x);
            anim.SetFloat("JumpY", jin.y);

            anim.ResetTrigger("Jump");
            anim.SetTrigger("Jump");
        }

        // ---- เคลื่อนที่แนวราบ ----
        float moveSpeed = run ? runSpeed : walkSpeed;
        Vector3 targetMove = wishDir * moveSpeed;
        if (grounded) airMove = targetMove;
        else airMove = Vector3.MoveTowards(airMove, targetMove, airControl * 10f * Time.deltaTime);

        cc.Move((airMove + Vector3.up * verticalVel) * Time.deltaTime);
        anim.SetBool("Grounded", cc.isGrounded);

        // ---- หมุนตัว + ส่งค่าเข้า Animator ----
        anim.SetBool("Moving", moving);

        if (lockOn)
        {
            Quaternion look = Quaternion.LookRotation(FaceDir(camF));
            transform.rotation = Quaternion.Slerp(transform.rotation, look, turnSpeed * Time.deltaTime);
            float spd = run ? 2f : 1f;
            anim.SetFloat("MoveX", input.x * spd, 0.1f, Time.deltaTime);
            anim.SetFloat("MoveY", input.y * spd, 0.1f, Time.deltaTime);
        }
        else
        {
            if (moving)
            {
                Quaternion look = Quaternion.LookRotation(wishDir);
                transform.rotation = Quaternion.Slerp(transform.rotation, look, turnSpeed * Time.deltaTime);
            }
            anim.SetFloat("MoveX", 0f, 0.1f, Time.deltaTime);
            anim.SetFloat("MoveY", input.magnitude * (run ? 2f : 1f), 0.1f, Time.deltaTime);
        }
    }

    void StartRoll(Vector2 input, bool moving, Vector3 wishDir, Vector3 camF, Vector3 camR)
    {
        ComputeCurveArea();
        ResetAttackTriggers();
        attackLockUntil = 0f;
        carryVel = Vector3.zero;
        bufferedLight = false;
        bufferedHeavy = false;

        if (lockOn)
        {
            // หันหาเป้า แล้วกลิ้งตามทิศที่กด (ไม่กดทิศ = ถอยหลัง)
            Vector2 rin = moving ? input.normalized : new Vector2(0f, -1f);
            transform.rotation = Quaternion.LookRotation(FaceDir(camF));
            rollDir = (camF * rin.y + camR * rin.x).normalized;
            anim.SetFloat("RollX", rin.x);
            anim.SetFloat("RollY", rin.y);
        }
        else
        {
            // หันไปทางที่กด แล้วกลิ้งหน้า
            rollDir = moving ? wishDir.normalized : transform.forward;
            transform.rotation = Quaternion.LookRotation(rollDir);
            anim.SetFloat("RollX", 0f);
            anim.SetFloat("RollY", 1f);
        }

        rolling = true;
        rollTimer = 0f;
        rollBufferStamp = -999f;
        nextRollTime = Time.time + Mathf.Max(rollLockTime, rollDuration) + rollCooldown;
        anim.ResetTrigger("Roll");
        anim.SetTrigger("Roll");
    }

    void ComputeCurveArea()
    {
        const int N = 32;
        float sum = 0f;
        for (int i = 0; i < N; i++) sum += rollCurve.Evaluate((i + 0.5f) / N);
        curveArea = Mathf.Max(0.01f, sum / N);
    }
}