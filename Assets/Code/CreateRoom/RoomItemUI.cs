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

    string _sessionName;
    Action<string> _onJoinClicked;

    public void Setup(string sessionName, string displayRoomName, int playerCount, int maxPlayers, bool isOpen, Action<string> onJoinClicked)
    {
        _sessionName = sessionName;
        _onJoinClicked = onJoinClicked;

        if (roomNameText) roomNameText.text = displayRoomName;
        if (playerCountText) playerCountText.text = $"{playerCount}/{maxPlayers}";

        bool canJoin = isOpen && playerCount < maxPlayers;
        if (joinButton)
        {
            joinButton.interactable = canJoin;
            joinButton.onClick.RemoveAllListeners();
            joinButton.onClick.AddListener(() => _onJoinClicked?.Invoke(_sessionName));
        }

        if (joinButtonLabel)
            joinButtonLabel.text = canJoin ? "JOIN" : "FULL";
    }
}