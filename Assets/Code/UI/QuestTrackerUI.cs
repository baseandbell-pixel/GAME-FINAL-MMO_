using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

[System.Serializable]
public class QuestData
{
    public string questTitle;
    public string objectiveText;
    public int targetCount = 5;
    [HideInInspector] public int currentCount = 0;
    [HideInInspector] public bool isCompleted = false;
}

public class QuestTrackerUI : MonoBehaviour
{
    public static QuestTrackerUI Instance { get; private set; }

    [Header("UI References")]
    [SerializeField] private GameObject questPanel;
    [SerializeField] private TMP_Text questTitleText;
    [SerializeField] private TMP_Text questObjectiveText;

    [Header("Toggle Button References")]
    [SerializeField] private Button toggleButton;
    [SerializeField] private TMP_Text toggleButtonText;

    [Header("Quest List (กำหนดรายการเควสต์)")]
    [SerializeField] private List<QuestData> quests = new List<QuestData>();

    private int currentQuestIndex = 0;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        if (quests.Count == 0)
        {
            quests.Add(new QuestData
            {
                questTitle = "บททดสอบนักรบฝึกหัด",
                objectiveText = "กำจัดมอนสเตอร์รอบทุ่งหญ้า",
                targetCount = 5
            });
            quests.Add(new QuestData
            {
                questTitle = "สำรวจพื้นที่ใหม่",
                objectiveText = "เก็บผลึกพลังงาน",
                targetCount = 3
            });
        }

        // ผูกปุ่มคลิกย่อ/ขยายกรอบเควสต์
        if (toggleButton != null)
        {
            toggleButton.onClick.AddListener(ToggleQuestPanel);
        }

        UpdateToggleButtonVisual();
        RefreshUI();
    }

    private void Update()
    {
        if (Keyboard.current == null || ChatBoxUI.IsTyping) return;

        // กด L: ซ่อนหรือแสดงกรอบเควสต์ (ใช้ฟังก์ชันเดียวกับปุ่มคลิก)
        if (Keyboard.current.lKey.wasPressedThisFrame)
        {
            ToggleQuestPanel();
        }

        // กด P: จำลองความคืบหน้าเควสต์ (+1)
        if (Keyboard.current.pKey.wasPressedThisFrame)
        {
            AddProgress(1);
        }
    }

    public void ToggleQuestPanel()
    {
        if (questPanel == null) return;

        questPanel.SetActive(!questPanel.activeSelf);
        UpdateToggleButtonVisual();

        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlayClickSFX();
        }
    }

    private void UpdateToggleButtonVisual()
    {
        if (toggleButtonText != null && questPanel != null)
        {
            toggleButtonText.text = questPanel.activeSelf ? "-" : "+";
        }
    }

    public void AddProgress(int amount = 1)
    {
        if (currentQuestIndex >= quests.Count) return;

        QuestData activeQuest = quests[currentQuestIndex];

        if (activeQuest.isCompleted)
        {
            NextQuest();
            return;
        }

        activeQuest.currentCount = Mathf.Min(activeQuest.targetCount, activeQuest.currentCount + amount);

        if (activeQuest.currentCount >= activeQuest.targetCount)
        {
            activeQuest.isCompleted = true;

            if (SoundManager.Instance != null) SoundManager.Instance.PlayExpSFX();

            if (ChatBoxUI.Instance != null)
            {
                ChatBoxUI.Instance.AddMessage($"<color=#2ECC71>[Quest]: สำเร็จภารกิจ '{activeQuest.questTitle}' แล้ว!</color>");
            }
        }
        else
        {
            if (SoundManager.Instance != null) SoundManager.Instance.PlayClickSFX();
        }

        RefreshUI();
    }

    public void NextQuest()
    {
        if (currentQuestIndex < quests.Count - 1)
        {
            currentQuestIndex++;
            if (ChatBoxUI.Instance != null)
            {
                ChatBoxUI.Instance.AddMessage($"<color=#F1C40F>[Quest]: เริ่มภารกิจใหม่ '{quests[currentQuestIndex].questTitle}'</color>");
            }
            RefreshUI();
        }
    }

    private void RefreshUI()
    {
        if (quests.Count == 0 || currentQuestIndex >= quests.Count) return;

        QuestData activeQuest = quests[currentQuestIndex];

        if (questTitleText != null)
        {
            questTitleText.text = activeQuest.questTitle;
        }

        if (questObjectiveText != null)
        {
            if (activeQuest.isCompleted)
            {
                string nextHint = currentQuestIndex < quests.Count - 1 ? "\n<color=#AAAAAA>(กด P รับเควสต์ถัดไป)</color>" : "";
                questObjectiveText.text = $"<color=#2ECC71>✓ {activeQuest.objectiveText} ({activeQuest.currentCount}/{activeQuest.targetCount})</color>{nextHint}";
            }
            else
            {
                questObjectiveText.text = $"- {activeQuest.objectiveText} ({activeQuest.currentCount}/{activeQuest.targetCount})";
            }
        }
    }
}