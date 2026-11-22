using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

public class PlayerHUD : MonoBehaviour
{
    public static PlayerHUD Instance { get; private set; }

    [Header("UI Fill References (ลากหลอด Fill มาใส่)")]
    [SerializeField] private Image hpFillImage;
    [SerializeField] private Image mpFillImage;
    [SerializeField] private Image expFillImage;

    [Header("UI Text References (ลาก Text ตัวเลขมาใส่)")]
    [SerializeField] private TMP_Text hpValueText;
    [SerializeField] private TMP_Text mpValueText;
    [SerializeField] private TMP_Text expValueText;
    [SerializeField] private TMP_Text levelText;

    [Header("World Space Reference (ป้ายชื่อบนหัว)")]
    [SerializeField] private PlayerNameplate playerNameplate;

    [Header("Testing Parameters (ค่าสถานะจำลอง)")]
    [SerializeField] private string testPlayerName = "Hero_01";
    [SerializeField] private int currentLevel = 1;
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float currentHealth = 100f;
    [SerializeField] private float maxMana = 50f;
    [SerializeField] private float currentMana = 50f;
    [SerializeField] private float maxExp = 50f;
    [SerializeField] private float currentExp = 0f;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        if (playerNameplate != null)
        {
            playerNameplate.SetPlayerName(testPlayerName);
        }

        UpdateLevelText();
        UpdateHealth(currentHealth, maxHealth);
        UpdateMana(currentMana, maxMana);
        UpdateExp(currentExp, maxExp);
    }

    private void Update()
    {
        if (Keyboard.current == null || ChatBoxUI.IsTyping) return;

        // กด H / J: ลดเลือด / เพิ่มเลือด
        if (Keyboard.current.hKey.wasPressedThisFrame)
        {
            currentHealth = Mathf.Max(0, currentHealth - 10f);
            UpdateHealth(currentHealth, maxHealth);
            if (SoundManager.Instance != null) SoundManager.Instance.PlayDamageSFX();
        }
        if (Keyboard.current.jKey.wasPressedThisFrame)
        {
            currentHealth = Mathf.Min(maxHealth, currentHealth + 10f);
            UpdateHealth(currentHealth, maxHealth);
            if (SoundManager.Instance != null) SoundManager.Instance.PlayHealSFX();
        }

        // กด U / I: ลดมานา / ฟื้นฟูมานา
        if (Keyboard.current.uKey.wasPressedThisFrame)
        {
            TryConsumeMana(10f);
        }
        if (Keyboard.current.iKey.wasPressedThisFrame)
        {
            currentMana = Mathf.Min(maxMana, currentMana + 10f);
            UpdateMana(currentMana, maxMana);
            if (SoundManager.Instance != null) SoundManager.Instance.PlayHealSFX();
        }

        // กด K: เพิ่ม EXP (ถ้าเต็มหลอดจะเลเวลอัปและรีเซ็ตเลือด/มานาให้เต็ม)
        if (Keyboard.current.kKey.wasPressedThisFrame)
        {
            currentExp += 10f;
            if (currentExp >= maxExp)
            {
                currentExp = 0f;
                currentLevel++;
                currentHealth = maxHealth;
                currentMana = maxMana;
                UpdateLevelText();
                UpdateHealth(currentHealth, maxHealth);
                UpdateMana(currentMana, maxMana);
            }
            UpdateExp(currentExp, maxExp);
            if (SoundManager.Instance != null) SoundManager.Instance.PlayExpSFX();
        }
    }

    // ฟังก์ชันสำหรับให้ปุ่มสกิลเรียกใช้เพื่อตัดค่า MP
    public bool TryConsumeMana(float manaCost)
    {
        if (currentMana < manaCost) return false; // มานาไม่พอ

        currentMana = Mathf.Max(0, currentMana - manaCost);
        UpdateMana(currentMana, maxMana);
        return true;
    }

    public void UpdateHealth(float currentHp, float maxHp)
    {
        if (hpFillImage != null && maxHp > 0)
            hpFillImage.fillAmount = Mathf.Clamp01(currentHp / maxHp);

        if (hpValueText != null)
            hpValueText.text = $"{Mathf.CeilToInt(currentHp)} / {Mathf.CeilToInt(maxHp)}";

        if (playerNameplate != null)
            playerNameplate.UpdateHealth(currentHp, maxHp);
    }

    public void UpdateMana(float currentMp, float maxMp)
    {
        if (mpFillImage != null && maxMp > 0)
            mpFillImage.fillAmount = Mathf.Clamp01(currentMp / maxMp);

        if (mpValueText != null)
            mpValueText.text = $"{Mathf.CeilToInt(currentMp)} / {Mathf.CeilToInt(maxMp)}";
    }

    public void UpdateExp(float currentXp, float maxXp)
    {
        if (expFillImage == null || maxXp <= 0) return;
        float ratio = Mathf.Clamp01(currentXp / maxXp);
        expFillImage.fillAmount = ratio;

        if (expValueText != null)
            expValueText.text = $"EXP {Mathf.RoundToInt(ratio * 100f)}%";
    }

    private void UpdateLevelText()
    {
        if (levelText != null)
            levelText.text = $"Lv.{currentLevel}";
    }
}