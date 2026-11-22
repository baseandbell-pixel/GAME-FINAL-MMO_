using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using TMPro;

public class ChatBoxUI : MonoBehaviour
{
    public static ChatBoxUI Instance { get; private set; }

    // สคริปต์อื่นสามารถเรียกเช็ก ChatBoxUI.IsTyping ได้จากทุกที่
    public static bool IsTyping => Instance != null && Instance.chatInputField != null &&
                                   (Instance.chatInputField.isFocused || Instance.wasFocusedLastFrame);

    [Header("UI References")]
    [SerializeField] private TMP_Text chatLogText;
    [SerializeField] private TMP_InputField chatInputField;
    [SerializeField] private Button sendButton;
    [SerializeField] private ScrollRect chatScrollRect;

    [Header("Chat Settings")]
    [SerializeField] private string senderName = "Hero_01";
    [SerializeField] private int maxMessages = 100;

    private readonly List<string> messageHistory = new List<string>();
    private bool wasFocusedLastFrame = false;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        if (sendButton != null)
        {
            sendButton.onClick.AddListener(SendChatMessage);
        }

        AddMessage("<color=#F1C40F>[System]: เชื่อมต่อระบบแชทสำเร็จ กด Enter เพื่อพูดคุย</color>");
    }

    private void Update()
    {
        if (Keyboard.current == null || chatInputField == null) return;

        // กด Esc เพื่อยกเลิกการพิมพ์และออกจากช่องแชท
        if (wasFocusedLastFrame && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            UnfocusChat();
            return;
        }

        // จัดการปุ่ม Enter (เปิดช่องพิมพ์ / ส่งข้อความ / ปิดช่องพิมพ์ถ้าว่าง)
        if (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame)
        {
            if (wasFocusedLastFrame)
            {
                if (!string.IsNullOrWhiteSpace(chatInputField.text))
                {
                    SendChatMessage();
                }
                else
                {
                    UnfocusChat();
                }
            }
            else
            {
                chatInputField.ActivateInputField();
            }
        }
    }

    private void LateUpdate()
    {
        // จดจำสถานะโฟกัสไว้เทียบในเฟรมถัดไป ป้องกันปุ่ม Enter เปิด-ปิดซ้อนกันในเฟรมเดียว
        wasFocusedLastFrame = chatInputField != null && chatInputField.isFocused;
    }

    public void SendChatMessage()
    {
        if (chatInputField == null || string.IsNullOrWhiteSpace(chatInputField.text)) return;

        string cleanText = chatInputField.text.Trim();
        string formattedMessage = $"<color=#00FFCC>[{senderName}]:</color> {cleanText}";
        AddMessage(formattedMessage);

        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlayClickSFX();
        }

        chatInputField.text = "";
        UnfocusChat(); // ส่งเสร็จแล้วออกจากช่องพิมพ์ทันที เพื่อให้กดสกิลหรือเดินต่อได้เลย
    }

    private void UnfocusChat()
    {
        if (chatInputField != null)
        {
            chatInputField.DeactivateInputField();
        }

        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
    }

    public void AddMessage(string message)
    {
        messageHistory.Add(message);

        if (messageHistory.Count > maxMessages)
        {
            messageHistory.RemoveAt(0);
        }

        if (chatLogText != null)
        {
            chatLogText.text = string.Join("\n", messageHistory);
        }

        if (gameObject.activeInHierarchy)
        {
            StartCoroutine(ScrollToBottom());
        }
    }

    private IEnumerator ScrollToBottom()
    {
        yield return null;
        Canvas.ForceUpdateCanvases();

        if (chatScrollRect != null)
        {
            chatScrollRect.verticalNormalizedPosition = 0f;
        }
    }
}