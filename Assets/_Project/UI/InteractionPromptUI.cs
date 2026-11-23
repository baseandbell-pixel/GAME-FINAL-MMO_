using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// ตัวควบคุมป้ายแจ้งเตือนปุ่มโต้ตอบกลาง ([E] เก็บของ/เปิดกล่อง/หินเทเลพอร์ต, [G] คุย NPC, [Hold G] ชุบเพื่อน)
/// ใช้ uGUI Canvas Prefab ที่แก้ไขหน้าตา ตำแหน่ง สี ฟอนต์ได้โดยตรงใน Unity Inspector/Scene (แทน OnGUI เก่าทั้งหมด)
/// </summary>
[DisallowMultipleComponent]
public sealed class InteractionPromptUI : MonoBehaviour
{
    public static InteractionPromptUI Instance { get; private set; }

    [Header("Editable Unity Canvas References (แก้ไขหน้าตาใน Prefab/Scene ได้โดยตรง)")]
    [SerializeField] private GameObject promptRoot;
    [SerializeField] private Image borderImage;
    [SerializeField] private Image keyBadgeBackground;
    [SerializeField] private TMP_Text keyBadgeText;
    [SerializeField] private TMP_Text actionLabelText;
    [SerializeField] private GameObject holdProgressRoot;
    [SerializeField] private Image holdProgressFill;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (promptRoot != null) promptRoot.SetActive(false);
        if (holdProgressRoot != null) holdProgressRoot.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void LateUpdate()
    {
        if (promptRoot == null) return;

        var player = PlayerCombat.Local;
        if (player == null || !player.IsSpawned || player.IsDead || GameplayInput.IsBlocked)
        {
            if (promptRoot.activeSelf) promptRoot.SetActive(false);
            return;
        }

        IWorldInteractable target = InteractionManager.GetPrimaryDisplayTarget(out Key key);
        if (target == null)
        {
            if (promptRoot.activeSelf) promptRoot.SetActive(false);
            return;
        }

        if (!promptRoot.activeSelf) promptRoot.SetActive(true);

        string keyText = target.RequiresHold ? $"Hold {key}" : key.ToString();
        Color accent = target.GetPromptAccentColor(player);

        if (keyBadgeText != null) keyBadgeText.text = keyText;
        if (actionLabelText != null)
        {
            actionLabelText.text = target.GetPromptLabel(player);
            actionLabelText.color = accent;
        }
        if (borderImage != null) borderImage.color = accent;
        if (keyBadgeBackground != null) keyBadgeBackground.color = new Color(accent.r * 0.25f, accent.g * 0.25f, accent.b * 0.25f, 0.95f);

        bool showHold = target.RequiresHold;
        if (holdProgressRoot != null && holdProgressRoot.activeSelf != showHold)
            holdProgressRoot.SetActive(showHold);

        if (showHold && holdProgressFill != null)
        {
            float progress = ReferenceEquals(InteractionManager.HoldingTarget, target)
                ? InteractionManager.CurrentHoldProgress01
                : 0f;
            holdProgressFill.fillAmount = progress;
            holdProgressFill.color = accent;
        }
    }
}
