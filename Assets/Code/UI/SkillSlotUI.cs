using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

public class SkillSlotUI : MonoBehaviour
{
    [Header("Skill Settings")]
    [SerializeField] private Key hotkey = Key.Digit1;
    [SerializeField] private float cooldownDuration = 5f;
    [SerializeField] private float manaCost = 10f; // ค่ามานาที่ใช้ต่อครั้ง

    [Header("UI References")]
    [SerializeField] private Image cooldownOverlay;
    [SerializeField] private TMP_Text cooldownText;



    private float currentCooldown = 0f;
    private bool isCooldown = false;

    private void Start()
    {
        // เริ่มต้นเกมให้ซ่อนเงาคูลดาวน์และตัวเลขไว้
        if (cooldownOverlay != null) cooldownOverlay.fillAmount = 0f;
        if (cooldownText != null) cooldownText.text = "";
    }

    private void Update()
    {
        // ป้องกันปุ่มสกิล 1-4 ลั่นตอนกำลังพิมพ์แชท
        if (!ChatBoxUI.IsTyping && Keyboard.current != null && Keyboard.current[hotkey].wasPressedThisFrame)
        {
            UseSkill();
        }

        // คำนวณเวลานับถอยหลัง
        if (isCooldown)
        {
            currentCooldown -= Time.deltaTime;

            if (currentCooldown <= 0f)
            {
                // คูลดาวน์เสร็จสิ้น
                currentCooldown = 0f;
                isCooldown = false;
                if (cooldownOverlay != null) cooldownOverlay.fillAmount = 0f;
                if (cooldownText != null) cooldownText.text = "";
            }
            else
            {
                // อัปเดตวงกลมเงาดำและตัวเลขวินาที
                if (cooldownOverlay != null)
                {
                    cooldownOverlay.fillAmount = currentCooldown / cooldownDuration;
                }

                if (cooldownText != null)
                {
                    // ถ้าน้อยกว่า 1 วินาที ให้โชว์ทศนิยม 1 ตำแหน่ง เช่น 0.5
                    cooldownText.text = currentCooldown < 1f
                        ? currentCooldown.ToString("F1")
                        : Mathf.CeilToInt(currentCooldown).ToString();
                }
            }
        }
    }

    // ฟังก์ชันสั่งใช้สกิลและเริ่มนับคูลดาวน์
    public void UseSkill()
    {
        if (isCooldown) return;

        // เช็กว่ามานาพอหรือไม่ ถ้าพอให้หักมานาทันที
        if (PlayerHUD.Instance != null && !PlayerHUD.Instance.TryConsumeMana(manaCost))
        {
            Debug.Log("MP ไม่พอสำหรับใช้สกิล!");
            return;
        }

        isCooldown = true;
        currentCooldown = cooldownDuration;

        if (cooldownOverlay != null) cooldownOverlay.fillAmount = 1f;

        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlaySkillSFX();
        }
    }
}