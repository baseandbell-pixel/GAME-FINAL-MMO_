using UnityEngine;
using UnityEngine.InputSystem;

public class LockOnSystem : MonoBehaviour
{
    [Header("Keys")]
    public Key lockKey = Key.Q;

    [Header("การค้นหาเป้า")]
    public float lockRange = 15f;      // ล็อกได้ไกลสุด
    public float maxAngle = 60f;       // ต้องอยู่ในมุมนี้จากหน้ากล้อง
    public float breakDistance = 20f;  // ไกลกว่านี้ปลดล็อกอัตโนมัติ

    [Header("สะบัดเมาส์เพื่อสลับเป้า")]
    public float flickThreshold = 25f;
    public float switchCooldown = 0.4f;

    public LockOnTarget Target { get; private set; }
    public bool HasTarget => Target != null;

    float nextSwitchTime;

    // ทิศแนวราบจากตำแหน่ง from ไปยังเป้า
    public Vector3 FlatDirFrom(Vector3 from)
    {
        Vector3 d = Target.Point - from;
        d.y = 0f;
        return d.sqrMagnitude > 0.0001f ? d.normalized : transform.forward;
    }

    void Update()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb == null) return;

        if (kb[lockKey].wasPressedThisFrame)
        {
            if (HasTarget) Target = null;
            else Target = FindBest(0f, null);
        }

        if (!HasTarget) return;

        // ปลดอัตโนมัติ: เป้าหาย/ปิด หรืออยู่ไกลเกิน
        if (!Target.isActiveAndEnabled ||
            Vector3.Distance(transform.position, Target.transform.position) > breakDistance)
        {
            Target = null;
            return;
        }

        // สะบัดเมาส์ซ้าย/ขวา = สลับไปเป้าข้างๆ
        if (mouse != null && Time.time >= nextSwitchTime)
        {
            float dx = mouse.delta.ReadValue().x;
            if (Mathf.Abs(dx) > flickThreshold)
            {
                var next = FindBest(Mathf.Sign(dx), Target);
                if (next != null)
                {
                    Target = next;
                    nextSwitchTime = Time.time + switchCooldown;
                }
            }
        }
    }

    // side = 0: เลือกเป้าที่ดีสุดใกล้กลางจอ, side = -1/+1: เลือกเป้าที่อยู่ซ้าย/ขวาของเป้าปัจจุบันบนจอ
    LockOnTarget FindBest(float side, LockOnTarget current)
    {
        Camera cam = Camera.main;
        if (cam == null) return null;

        LockOnTarget best = null;
        float bestScore = float.MaxValue;
        float currentX = current != null ? cam.WorldToViewportPoint(current.Point).x : 0.5f;

        foreach (var t in LockOnTarget.All)
        {
            if (t == current) continue;

            float dist = Vector3.Distance(transform.position, t.transform.position);
            if (dist > lockRange) continue;

            Vector3 vp = cam.WorldToViewportPoint(t.Point);
            if (vp.z <= 0f) continue; // อยู่หลังกล้อง

            float angle = Vector3.Angle(cam.transform.forward, t.Point - cam.transform.position);
            if (angle > maxAngle) continue;

            float score;
            if (side == 0f)
            {
                score = angle + dist * 0.5f;
            }
            else
            {
                float dx = vp.x - currentX;
                if (dx * side <= 0f) continue; // ต้องอยู่ฝั่งที่สะบัดไป
                score = Mathf.Abs(dx);
            }

            if (score < bestScore)
            {
                bestScore = score;
                best = t;
            }
        }
        return best;
    }
}