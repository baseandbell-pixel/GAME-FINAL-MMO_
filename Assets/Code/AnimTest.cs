using UnityEngine;
using UnityEngine.InputSystem;

public class AnimTest : MonoBehaviour
{
    public bool lockOn = false;      // ติ๊กเพื่อทดสอบโหมดล็อกเป้า
    public float turnSpeed = 10f;
    public float walkSpeed = 2f;
    public float runSpeed = 5f;

    Animator anim;
    Transform cam;

    void Awake()
    {
        anim = GetComponent<Animator>();
        cam = Camera.main.transform;
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        Vector2 input = Vector2.zero;
        if (kb.wKey.isPressed) input.y += 1;
        if (kb.sKey.isPressed) input.y -= 1;
        if (kb.dKey.isPressed) input.x += 1;
        if (kb.aKey.isPressed) input.x -= 1;
        input = Vector2.ClampMagnitude(input, 1f);

        bool moving = input.sqrMagnitude > 0.01f;
        anim.SetBool("Moving", moving);

        // ทิศหน้า/ขวาของกล้องบนพื้นราบ
        Vector3 camF = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
        Vector3 camR = Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized;

        if (lockOn)
        {
            // หันหน้าตามกล้อง (แทนทิศศัตรู) เดินได้ 8 ทิศ
            Quaternion look = Quaternion.LookRotation(camF);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, turnSpeed * Time.deltaTime);
            float spd = kb.leftShiftKey.isPressed ? 2f : 1f;
            anim.SetFloat("MoveX", input.x * spd, 0.1f, Time.deltaTime);
            anim.SetFloat("MoveY", input.y * spd, 0.1f, Time.deltaTime);
            bool run = kb.leftShiftKey.isPressed;
            Vector3 move = camF * input.y + camR * input.x;
            transform.position += move * (run ? runSpeed : walkSpeed) * Time.deltaTime;
        }
        else
        {
            // โหมดอิสระ: หมุนไปทางที่กดแล้วเดินหน้าอย่างเดียว
            if (moving)
            {
                Vector3 dir = camF * input.y + camR * input.x;
                Quaternion look = Quaternion.LookRotation(dir);
                transform.rotation = Quaternion.Slerp(transform.rotation, look, turnSpeed * Time.deltaTime);
                bool run = kb.leftShiftKey.isPressed;
                transform.position += transform.forward * (run ? runSpeed : walkSpeed) * Time.deltaTime;
            }
            anim.SetFloat("MoveX", 0f, 0.1f, Time.deltaTime);
            float target = input.magnitude * (kb.leftShiftKey.isPressed ? 2f : 1f);
            anim.SetFloat("MoveY", target, 0.1f, Time.deltaTime);
        }
    }
}