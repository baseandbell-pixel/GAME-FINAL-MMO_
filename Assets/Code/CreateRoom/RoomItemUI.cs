using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RoomItemUI : MonoBehaviour
{
    public TMP_Text roomNameText;
    public TMP_Text playerCountText;
    public Button joinButton;
    public TMP_Text joinButtonLabel;

    [Header("Private Room Indicator (Optional)")]
    [Tooltip("ไอคอนแม่กุญแจหรือป้าย Private (ถ้าไม่ได้ใส่ ระบบจะเติมคำว่า [Private] ท้ายชื่อห้องให้แทน)")]
    public GameObject lockIcon;

    string _sessionName;
    Action<string> _onJoinClicked;

    public void Setup(string sessionName, string displayRoomName, int playerCount, int maxPlayers, bool isOpen, bool isPrivate, Action<string> onJoinClicked)
    {
        _sessionName = sessionName;
        _onJoinClicked = onJoinClicked;

        int effectiveMax = maxPlayers > 0 ? maxPlayers : LobbyNetwork.MaxPlayers;
        bool isFull = playerCount >= effectiveMax;
        bool canJoin = isOpen && !isFull;

        if (lockIcon) lockIcon.SetActive(isPrivate);

        if (roomNameText)
        {
            // ถ้าไม่ได้ลากไอคอนแม่กุญแจมาใส่ ให้แสดงคำว่า [Private] ต่อท้ายชื่อห้องแทน
            roomNameText.text = (isPrivate && lockIcon == null)
                ? $"{displayRoomName}  <color=#FFD166>[Private]</color>"
                : displayRoomName;
        }

        if (playerCountText) playerCountText.text = $"{playerCount}/{effectiveMax}";

        if (joinButton)
        {
            joinButton.interactable = canJoin;
            joinButton.onClick.RemoveAllListeners();
            joinButton.onClick.AddListener(() => _onJoinClicked?.Invoke(_sessionName));
        }

        if (joinButtonLabel)
        {
            if (canJoin)
                joinButtonLabel.text = "JOIN";
            else if (isFull)
                joinButtonLabel.text = "FULL";
            else
                joinButtonLabel.text = "CLOSED";
        }
    }
}