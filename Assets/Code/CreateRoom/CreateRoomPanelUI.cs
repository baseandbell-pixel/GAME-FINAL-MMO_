using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CreateRoomPanelUI : MonoBehaviour
{
    [Header("Controls")]
    public Button createRoomButton;
    public Button backButton;
    public TMP_InputField roomNameInput;

    [Header("Room List")]
    public Transform roomListContainer;
    public RoomItemUI roomItemPrefab;

    [Header("Empty State (Optional)")]
    public GameObject emptyListPlaceholder;
    public GameObject[] blankSlotRows;

    [Header("Navigation")]
    public GUIControl guiControl;

    readonly List<RoomItemUI> _spawnedItems = new List<RoomItemUI>();
    bool _isRequesting;

    GUIControl Gui => guiControl != null ? guiControl : GUIControl.Instance;

    void OnEnable()
    {
        if (createRoomButton) createRoomButton.onClick.AddListener(OnClickCreateRoom);
        if (backButton) backButton.onClick.AddListener(OnClickBack);

        if (LobbyNetwork.Instance != null)
        {
            LobbyNetwork.Instance.OnSessionListChanged += RefreshRoomList;
            RefreshRoomList(LobbyNetwork.Instance.CachedSessions);
        }
        else
        {
            RefreshRoomList(null);
        }

        SetInteractable(true);
    }

    void OnDisable()
    {
        if (createRoomButton) createRoomButton.onClick.RemoveListener(OnClickCreateRoom);
        if (backButton) backButton.onClick.RemoveListener(OnClickBack);

        if (LobbyNetwork.Instance != null)
            LobbyNetwork.Instance.OnSessionListChanged -= RefreshRoomList;
    }

    public async void OnClickCreateRoom()
    {
        if (_isRequesting || LobbyNetwork.Instance == null) return;

        _isRequesting = true;
        SetInteractable(false);

        string customName = roomNameInput ? roomNameInput.text : null;
        bool ok = await LobbyNetwork.Instance.CreateRoomAsync(customName);

        _isRequesting = false;
        if (!ok)
        {
            SetInteractable(true);
            return;
        }

        if (Gui != null) Gui.OpenLobbyPanel();
    }

    async void OnClickJoinRoom(string sessionName)
    {
        if (_isRequesting || LobbyNetwork.Instance == null) return;

        _isRequesting = true;
        SetInteractable(false);

        bool ok = await LobbyNetwork.Instance.JoinRoomAsync(sessionName);

        _isRequesting = false;
        if (!ok)
        {
            SetInteractable(true);
            return;
        }

        if (Gui != null) Gui.OpenLobbyPanel();
    }

    void OnClickBack()
    {
        if (_isRequesting) return;
        if (Gui != null) Gui.BackToMenuPanel();
    }

    void RefreshRoomList(IReadOnlyList<SessionInfo> sessions)
    {
        foreach (var item in _spawnedItems)
        {
            if (item != null) Destroy(item.gameObject);
        }
        _spawnedItems.Clear();

        int activeRoomCount = 0;

        if (sessions != null && roomListContainer != null && roomItemPrefab != null)
        {
            foreach (var session in sessions)
            {
                if (!session.IsValid || !session.IsVisible) continue;

                string displayName = session.Name;
                if (session.Properties != null &&
                    session.Properties.TryGetValue("roomName", out var roomProp) &&
                    roomProp.IsString)
                {
                    displayName = (string)roomProp;
                }

                var row = Instantiate(roomItemPrefab, roomListContainer);
                row.Setup(
                    session.Name,
                    displayName,
                    session.PlayerCount,
                    session.MaxPlayers,
                    session.IsOpen,
                    OnClickJoinRoom
                );
                _spawnedItems.Add(row);
                activeRoomCount++;
            }
        }

        bool hasRooms = activeRoomCount > 0;
        if (emptyListPlaceholder) emptyListPlaceholder.SetActive(!hasRooms);

        if (blankSlotRows != null)
        {
            for (int i = 0; i < blankSlotRows.Length; i++)
            {
                if (blankSlotRows[i] != null)
                    blankSlotRows[i].SetActive(i >= activeRoomCount);
            }
        }
    }

    void SetInteractable(bool value)
    {
        if (createRoomButton) createRoomButton.interactable = value;
        if (backButton) backButton.interactable = value;
        if (roomNameInput) roomNameInput.interactable = value;
    }
}