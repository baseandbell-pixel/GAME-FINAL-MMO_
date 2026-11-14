using UnityEngine;
using UnityEngine.InputSystem;

public class SoulsCamera : MonoBehaviour
{
    public Transform target;
    public LockOnSystem lockSys;   // ลากตัวละครที่มี LockOnSystem ใส่

    public Vector3 pivotOffset = new Vector3(0, 1.6f, 0);
    public float distance = 4f;
    public float minDistance = 1.5f;
    public float sensitivity = 0.12f;
    public float minPitch = -10f, maxPitch = 60f;
    public float verticalSmooth = 0.1f;
    public LayerMask collisionMask = ~0;   // เอา Layer Player ออกใน Inspector

    [Header("Lock-on")]
    public float lockTurnSpeed = 8f;  // ยิ่งมากกล้องยิ่งตามเป้าเร็ว
    public float lockPitch = 15f;     // มุมก้มของกล้องตอนล็อกเป้า

    float yaw, pitch = 15f;
    float currentDist, velY;
    Vector3 pivot;
    readonly RaycastHit[] hits = new RaycastHit[8];

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        yaw = target.eulerAngles.y;
        pivot = target.position + pivotOffset;
        currentDist = distance;
    }

    void LateUpdate()
    {
        if (target == null || Mouse.current == null) return;

        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // X/Z ตามเป๊ะ, Y หน่วงนิดหน่อย
        Vector3 desired = target.position + pivotOffset;
        pivot.x = desired.x;
        pivot.z = desired.z;
        pivot.y = Mathf.SmoothDamp(pivot.y, desired.y, ref velY, verticalSmooth);

        bool locked = lockSys != null && lockSys.HasTarget;
        if (locked)
        {
            // กล้องหมุนไปมองเป้าเอง (ไม่รับเมาส์)
            Vector3 toTarget = lockSys.Target.Point - pivot;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.01f)
            {
                float wantYaw = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
                yaw = Mathf.LerpAngle(yaw, wantYaw, lockTurnSpeed * Time.deltaTime);
            }
            pitch = Mathf.Lerp(pitch, Mathf.Clamp(lockPitch, minPitch, maxPitch), lockTurnSpeed * Time.deltaTime);
        }
        else
        {
            Vector2 d = Mouse.current.delta.ReadValue();
            yaw += d.x * sensitivity;
            pitch = Mathf.Clamp(pitch - d.y * sensitivity, minPitch, maxPitch);
        }

        Quaternion rot = Quaternion.Euler(pitch, yaw, 0);
        Vector3 dir = rot * Vector3.back;

        float targetDist = distance;
        int n = Physics.SphereCastNonAlloc(pivot, 0.2f, dir, hits, distance, collisionMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            if (hits[i].transform.IsChildOf(target)) continue; // ข้ามตัวละครและลูกทั้งหมด
            if (hits[i].distance <= 0f) continue;              // เริ่มต้นอยู่ในตัว Collider
            targetDist = Mathf.Min(targetDist, Mathf.Max(minDistance, hits[i].distance));
        }

        // ชนแล้วเข้าทันที ออกนุ่มๆ
        currentDist = targetDist < currentDist
            ? targetDist
            : Mathf.Lerp(currentDist, targetDist, 5f * Time.deltaTime);

        transform.SetPositionAndRotation(pivot + dir * currentDist, rot);
    }
}