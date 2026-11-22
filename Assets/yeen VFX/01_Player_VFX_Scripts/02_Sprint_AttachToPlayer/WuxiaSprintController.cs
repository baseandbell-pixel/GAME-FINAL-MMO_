using UnityEngine;

[AddComponentMenu("yeen VFX/02C. [Player] วิ่งพุ่งตัววิชาตัวเบา (Wuxia Sprint)")]
public class WuxiaSprintController : MonoBehaviour
{
    [Header("1. ปุ่มกดวิ่งความเร็วสูง (Sprint Input)")]
    public KeyCode sprintKey = KeyCode.LeftShift;
    [Tooltip("ติ๊กถูกหากต้องการให้จำลองการก้าวเท้าซ้าย-ขวาอัตโนมัติเพื่อดูผลลัพธ์")]
    public bool simulateFootRunning = true;
    public float strideFrequency = 14f; // ความถี่การก้าวเท้าตอนวิ่งไว

    [Header("2. จุดยึดที่เท้าทั้ง 2 ข้าง (ลากกระดูกเท้าซ้าย-ขวามาใส่ช่องนี้)")]
    public Transform leftFootMount;
    public Transform rightFootMount;

    [Header("3. อ้างอิงเอฟเฟกต์ทั้ง 4 เลเยอร์")]
    [Tooltip("Layer 1: วงแหวนลมระเบิดจังหวะเริ่มกด Shift (Dash Burst)")]
    public ParticleSystem l1DashBurstRing;

    [Tooltip("Layer 2: เส้นสายลมติดข้อเท้าซ้ายและขวา (Foot Trails)")]
    public TrailRenderer l2LeftFootTrail;
    public TrailRenderer l2RightFootTrail;

    [Tooltip("Layer 3: เส้นลมความเร็วสูงพุ่งสวนรอบตัว (Speed Slipstream)")]
    public ParticleSystem l3SpeedLines;

    [Tooltip("Layer 4: ละอองลมและฝุ่นใต้เท้าซ้าย-ขวา (Ground Wind Puffs)")]
    public ParticleSystem l4LeftFootDust;
    public ParticleSystem l4RightFootDust;

    private bool isSprinting = false;
    private float runCycleTimer = 0f;
    private Vector3 leftFootBasePos;
    private Vector3 rightFootBasePos;

    void Start()
    {
        if (leftFootMount != null) leftFootBasePos = leftFootMount.localPosition;
        if (rightFootMount != null) rightFootBasePos = rightFootMount.localPosition;

        // ปิดการปล่อยเอฟเฟกต์ทั้งหมดไว้ตอนเริ่มเกม (จะทำงานเฉพาะตอนกด Shift)
        SetSprintVFXState(false, true);
    }

    void Update()
    {
        // 1. จังหวะเริ่มกดปุ่ม Left Shift (Trigger Layer 1: Sonic Dash Burst + เปิดลมที่เท้า)
        if (Input.GetKeyDown(sprintKey))
        {
            StartSprintVFX();
        }
        // 2. จังหวะปล่อยปุ่ม Left Shift (ปิดการปล่อยลมใหม่ แต่ปล่อยให้หางลมเก่าค่อยๆ จางหาย)
        else if (Input.GetKeyUp(sprintKey))
        {
            StopSprintVFX();
        }

        // จำลองการวิ่งก้าวเท้าซ้าย-ขวาด้วยความเร็วสูง (เพื่อให้เห็นเส้นลมที่เท้าพริ้วไหว)
        if (isSprinting && simulateFootRunning)
        {
            runCycleTimer += Time.deltaTime * strideFrequency;

            if (leftFootMount != null)
            {
                float zOffsetL = Mathf.Sin(runCycleTimer) * 0.45f;
                float yOffsetL = Mathf.Max(0f, Mathf.Cos(runCycleTimer)) * 0.25f;
                leftFootMount.localPosition = leftFootBasePos + new Vector3(0f, yOffsetL, zOffsetL);
            }

            if (rightFootMount != null)
            {
                float zOffsetR = Mathf.Sin(runCycleTimer + Mathf.PI) * 0.45f;
                float yOffsetR = Mathf.Max(0f, Mathf.Cos(runCycleTimer + Mathf.PI)) * 0.25f;
                rightFootMount.localPosition = rightFootBasePos + new Vector3(0f, yOffsetR, zOffsetR);
            }
        }
        else if (!isSprinting && simulateFootRunning)
        {
            // คืนตำแหน่งเท้ากลับมาท่ายืนปกติอย่างนุ่มนวล
            if (leftFootMount != null)
                leftFootMount.localPosition = Vector3.Lerp(leftFootMount.localPosition, leftFootBasePos, Time.deltaTime * 10f);
            if (rightFootMount != null)
                rightFootMount.localPosition = Vector3.Lerp(rightFootMount.localPosition, rightFootBasePos, Time.deltaTime * 10f);
        }
    }

    public void StartSprintVFX()
    {
        isSprinting = true;

        // เล่น Layer 1: วงแหวนลมกระแทกออกตัว (เล่นครั้งเดียวตอนกด Shift)
        if (l1DashBurstRing != null)
        {
            l1DashBurstRing.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            l1DashBurstRing.Play(true);
        }

        // เปิดการทำงานของ Layer 2, 3, 4 ต่อเนื่องตราบใดที่ยังกด Shift ค้างไว้
        SetSprintVFXState(true, false);
    }

    public void StopSprintVFX()
    {
        isSprinting = false;
        SetSprintVFXState(false, false);
    }

    private void SetSprintVFXState(bool active, bool clearImmediate)
    {
        // ควบคุมเส้นลมที่เท้าซ้าย-ขวา (Layer 2)
        if (l2LeftFootTrail != null)
        {
            if (clearImmediate) l2LeftFootTrail.Clear();
            l2LeftFootTrail.emitting = active;
        }
        if (l2RightFootTrail != null)
        {
            if (clearImmediate) l2RightFootTrail.Clear();
            l2RightFootTrail.emitting = active;
        }

        // ควบคุมเส้นลมแหวกอากาศรอบตัว (Layer 3)
        ToggleParticleLoop(l3SpeedLines, active, clearImmediate);

        // ควบคุมฝุ่นลมใต้เท้าซ้าย-ขวา (Layer 4)
        ToggleParticleLoop(l4LeftFootDust, active, clearImmediate);
        ToggleParticleLoop(l4RightFootDust, active, clearImmediate);
    }

    private void ToggleParticleLoop(ParticleSystem ps, bool play, bool clear)
    {
        if (ps == null) return;
        if (play)
        {
            ps.Play(true);
        }
        else
        {
            // ใช้ StopEmitting เพื่อให้เม็ดลมที่ปล่อยออกมาแล้วค่อยๆ จางหายไปเอง ไม่ตัดหายกลางอากาศ
            ps.Stop(true, clear ? ParticleSystemStopBehavior.StopEmittingAndClear : ParticleSystemStopBehavior.StopEmitting);
        }
    }
}