using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Fusion;
using System;

public class RoomListItemUI : MonoBehaviour
{
    public TMP_Text roomNameText;
    public TMP_Text playerCountText;
    public Button joinButton;

    private string _sessionName;
    private Action<string> _onJoinClicked;

    public void Setup(SessionInfo session, Action<string> onJoinCallback)
    {
        _sessionName = session.Name;
        _onJoinClicked = onJoinCallback;

        if (roomNameText) roomNameText.text = session.Name;
        if (playerCountText) playerCountText.text = $"{session.PlayerCount}/{session.MaxPlayers}";

        if (joinButton)
        {
            // ปิดปุ่มถ้าห้องเต็มหรือห้องปิดรับคนแล้ว
            joinButton.interactable = session.IsOpen && session.PlayerCount < session.MaxPlayers;
            joinButton.onClick.RemoveAllListeners();
            joinButton.onClick.AddListener(() => _onJoinClicked?.Invoke(_sessionName));
        }
    }
}