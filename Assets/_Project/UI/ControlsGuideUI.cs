using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// แผงแนะนำปุ่มควบคุมสำหรับผู้เล่นใหม่ (Controls Hint มุมขวาล่าง + หน้าต่างคู่มือปุ่มทั้งหมด [H])
/// สร้างเป็น uGUI Canvas Prefab ที่แก้ไขข้อความ ตำแหน่ง ฟอนต์ และสีได้โดยตรงใน Unity Editor
/// </summary>
[DisallowMultipleComponent]
public sealed class ControlsGuideUI : MonoBehaviour
{
    public static ControlsGuideUI Instance { get; private set; }

    [Header("Editable Unity Canvas References")]
    [SerializeField] private Key toggleKey = Key.H;
    [SerializeField] private GameObject cornerHintRoot;
    [SerializeField] private TMP_Text cornerHintText;
    [SerializeField] private GameObject fullGuideModalRoot;
    [SerializeField] private Button closeButton;

    public bool IsModalOpen { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (closeButton != null)
            closeButton.onClick.AddListener(() => SetModalOpen(false));
        if (fullGuideModalRoot != null)
            fullGuideModalRoot.SetActive(false);
    }

    private void OnDestroy()
    {
        if (IsModalOpen) GameplayInput.PopBlocker("ControlsGuideUI");
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb[toggleKey].wasPressedThisFrame)
            {
                if (IsModalOpen || !GameplayInput.IsBlocked)
                    SetModalOpen(!IsModalOpen);
            }
            else if (IsModalOpen && kb.escapeKey.wasPressedThisFrame)
            {
                SetModalOpen(false);
            }
        }

        // อัปเดตป้ายมุมจอแบบเรียลไทม์: เตือนให้กด [1] ชักดาบเมื่ออยู่นอกเขตปลอดภัยแต่ยังเก็บดาบอยู่
        if (cornerHintRoot != null && cornerHintText != null)
        {
            var player = PlayerCombat.Local;
            bool showHint = player != null && player.IsSpawned && !IsModalOpen;
            if (cornerHintRoot.activeSelf != showHint)
                cornerHintRoot.SetActive(showHint);

            if (showHint)
            {
                bool inSafe = player.IsInSafeZone || SafeZone.IsPointInAnySafeZone(player.transform.position);
                string weaponHint = inSafe
                    ? "<color=#42F5C8>[Safe Zone]</color> Free Sprint"
                    : (player.IsWeaponDrawn
                        ? "<color=#FFD166>[1]</color> Sheathe  |  <color=#FFD166>[LMB/R]</color> Attack  |  <color=#FFD166>[F]</color> Block"
                        : "<color=#FF9F43>[1] Draw GreatSword to Fight!</color>");

                cornerHintText.text =
                    $"{weaponHint}\n" +
                    "<color=#D0DCE8>[E]</color> Loot / Waystone   <color=#D0DCE8>[G]</color> NPC / Hold Revive\n" +
                    "<color=#D0DCE8>[I]</color> Bag   <color=#D0DCE8>[M]</color> Map   <color=#D0DCE8>[P]</color> Party   <color=#FFD166>[H]</color> All Controls";
            }
        }
    }

    public void SetModalOpen(bool open)
    {
        if (open == IsModalOpen) return;
        if (open && GameplayInput.IsBlocked) return;

        IsModalOpen = open;
        if (fullGuideModalRoot != null)
            fullGuideModalRoot.SetActive(open);

        if (open) GameplayInput.PushBlocker("ControlsGuideUI");
        else GameplayInput.PopBlocker("ControlsGuideUI");
    }
}
