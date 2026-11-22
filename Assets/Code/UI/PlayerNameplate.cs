using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerNameplate : MonoBehaviour
{
    [Header("UI Component References")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private Image hpFillImage;

    // ฟังก์ชันสำหรับกำหนดชื่อผู้เล่นบนหัว (รองรับทั้งชื่อจริงจาก Firebase หรือชื่อในห้อง)
    public void SetPlayerName(string newName)
    {
        if (nameText != null)
        {
            nameText.text = newName;
        }
    }

    // ฟังก์ชันสำหรับหักเลือดหลอดจิ๋ว
    public void UpdateHealth(float currentHp, float maxHp)
    {
        if (hpFillImage == null || maxHp <= 0) return;
        hpFillImage.fillAmount = Mathf.Clamp01(currentHp / maxHp);
    }
}