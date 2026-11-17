using UnityEngine;

public class MMOLevelUpController : MonoBehaviour
{
    [Header("ปุ่มกดทดสอบ Level Up")]
    public KeyCode triggerKey = KeyCode.L;
    public bool allowLeftClick = true;

    [Header("อ้างอิงระบบ Particle หลัก")]
    public ParticleSystem levelUpRootVFX;
    public Transform characterMesh;

    private float popTimer = 0f;
    private bool isPopping = false;
    private Vector3 baseScale = Vector3.one;

    void Start()
    {
        if (characterMesh != null) baseScale = characterMesh.localScale;

        // ซ่อนเอฟเฟกต์ไว้ก่อนตอนเริ่มเกม จะแสดงเฉพาะตอนเลเวลอัปเท่านั้น
        if (levelUpRootVFX != null)
        {
            levelUpRootVFX.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    void Update()
    {
        // กดปุ่ม L หรือคลิกซ้ายเพื่อสั่งแสดงเอฟเฟกต์ Level Up
        if (Input.GetKeyDown(triggerKey) || (allowLeftClick && Input.GetMouseButtonDown(0)))
        {
            TriggerLevelUp();
        }

        // จำลองอนิเมชั่นตัวละครเบ่งพลังเล็กน้อยตอนเลเวลอัป
        if (isPopping && characterMesh != null)
        {
            popTimer += Time.deltaTime * 6f;
            float scaleBoost = 1f + Mathf.Sin(Mathf.Clamp01(popTimer) * Mathf.PI) * 0.12f;
            characterMesh.localScale = new Vector3(baseScale.x * scaleBoost, baseScale.y * scaleBoost, baseScale.z * scaleBoost);
            if (popTimer >= 1f)
            {
                characterMesh.localScale = baseScale;
                isPopping = false;
            }
        }
    }

    // ฟังก์ชันนี้สามารถนำไปเรียกจากสคริปต์ระบบ EXP ของเกมคุณได้ทันที!
    public void TriggerLevelUp()
    {
        if (levelUpRootVFX == null) return;

        levelUpRootVFX.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        levelUpRootVFX.Play(true);

        isPopping = true;
        popTimer = 0f;
    }
}