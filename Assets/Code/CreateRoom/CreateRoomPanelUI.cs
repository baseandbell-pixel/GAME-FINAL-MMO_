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

    [Header("Private Room Settings")]
    [Tooltip("Toggle สำหรับเลือกว่าจะตั้งเป็นห้อง Private หรือไม่")]
    public Toggle privateToggle;
    [Tooltip("ช่องกรอกรหัสผ่าน (จะแสดง/เปิดให้พิมพ์เมื่อติ๊ก Private)")]
    public TMP_InputField passwordInput;
    [Tooltip("ข้อความแจ้งเตือน เช่น กรณีกดสร้างห้อง Private แต่ลืมใส่รหัสผ่าน (Optional)")]
    public TMP_Text warningText;
    [Tooltip("หน้าต่าง Popup สำหรับให้ผู้เล่นคนอื่นกรอกรหัสผ่านตอนกด Join")]
    public PasswordPopupUI passwordPopup;

    [Header("Room List")]
    public Transform roomListContainer;
    public RoomItemUI roomItemPrefab;

    [Header("Empty State (Optional)")]
    public GameObject emptyListPlaceholder;
    public GameObject[] blankSlotRows;

    [Header("Navigation")]
    public GUIControl guiControl;

    readonly List<RoomItemUI> _spawnedItems = new List<RoomItemUI>();
    readonly Dictionary<string, SessionInfo> _sessionLookup = new Dictionary<string, SessionInfo>();
    bool _isRequesting;

    GUIControl Gui => guiControl != null ? guiControl : GUIControl.Instance;

    void Awake()
    {
        if (passwordPopup == null)
            passwordPopup = FindObjectOfType<PasswordPopupUI>(true);
    }

    void OnEnable()
    {
        if (createRoomButton) createRoomButton.onClick.AddListener(OnClickCreateRoom);
        if (backButton) backButton.onClick.AddListener(OnClickBack);
        if (privateToggle) privateToggle.onValueChanged.AddListener(OnPrivateToggleChanged);

        // รีเซ็ตสถานะตอนเปิดหน้า CreateRoom
        if (warningText) warningText.text = "";
        OnPrivateToggleChanged(privateToggle != null && privateToggle.isOn);

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
        if (privateToggle) privateToggle.onValueChanged.RemoveListener(OnPrivateToggleChanged);

        if (LobbyNetwork.Instance != null)
            LobbyNetwork.Instance.OnSessionListChanged -= RefreshRoomList;
    }

    /// <summary>เปิด/ปิด ช่องกรอกรหัสผ่านตามสถานะของ Toggle Private</summary>
    void OnPrivateToggleChanged(bool isPrivate)
    {
        if (warningText) warningText.text = "";

        if (passwordInput != null)
        {
            passwordInput.gameObject.SetActive(isPrivate);
            if (!isPrivate) passwordInput.text = "";
        }
    }

    public async void OnClickCreateRoom()
    {
        if (_isRequesting || LobbyNetwork.Instance == null) return;

        bool isPrivate = privateToggle != null && privateToggle.isOn;
        string password = passwordInput != null ? passwordInput.text.Trim() : "";

        // บังคับให้ตั้งรหัสผ่านหากเลือก Private
        if (isPrivate && string.IsNullOrEmpty(password))
        {
            if (warningText) warningText.text = "Please enter a password for private room.";
            if (passwordInput) passwordInput.ActivateInputField();
            return;
        }

        if (warningText) warningText.text = "";

        _isRequesting = true;
        SetInteractable(false);

        string customName = roomNameInput ? roomNameInput.text : null;
        bool ok = await LobbyNetwork.Instance.CreateRoomAsync(customName, isPrivate, password);

        _isRequesting = false;
        if (!ok)
        {
            SetInteractable(true);
            return;
        }

        if (Gui != null) Gui.OpenLobbyPanel();
    }

    void OnClickJoinRoom(string sessionName)
    {
        if (_isRequesting || LobbyNetwork.Instance == null) return;

        // เช็กว่าห้องที่กด Join เป็นห้อง Private หรือไม่
        if (_sessionLookup.TryGetValue(sessionName, out var session))
        {
            bool isPrivate = GetIsPrivate(session);
            if (isPrivate)
            {
                string displayName = GetDisplayName(session);
                string expectedPassword = GetPassword(session);

                if (passwordPopup != null)
                {
                    passwordPopup.Open(displayName, expectedPassword, () =>
                    {
                        _ = ExecuteJoinRoomAsync(sessionName);
                    });
                    return;
                }
                else
                {
                    Debug.LogError("[CreateRoomPanelUI] ห้องนี้เป็น Private แต่ยังไม่ได้สร้าง PasswordPopupUI ใน Scene!");
                    return;
                }
            }
        }

        // ถ้าเป็นห้อง Public ให้เข้าทันที
        _ = ExecuteJoinRoomAsync(sessionName);
    }

    async System.Threading.Tasks.Task ExecuteJoinRoomAsync(string sessionName)
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
        _sessionLookup.Clear();

        int activeRoomCount = 0;

        if (sessions != null && roomListContainer != null && roomItemPrefab != null)
        {
            foreach (var session in sessions)
            {
                if (!session.IsValid || !session.IsVisible) continue;

                _sessionLookup[session.Name] = session;

                string displayName = GetDisplayName(session);
                bool isPrivate = GetIsPrivate(session);

                var row = Instantiate(roomItemPrefab, roomListContainer);
                row.Setup(
                    session.Name,
                    displayName,
                    session.PlayerCount,
                    session.MaxPlayers,
                    session.IsOpen,
                    isPrivate,
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

    string GetDisplayName(SessionInfo session)
    {
        if (session.Properties != null &&
            session.Properties.TryGetValue("roomName", out var roomProp) &&
            roomProp.IsString)
        {
            return (string)roomProp;
        }
        return session.Name;
    }

    bool GetIsPrivate(SessionInfo session)
    {
        if (session.Properties != null &&
            session.Properties.TryGetValue("isPrivate", out var privProp) &&
            privProp.IsInt)
        {
            return (int)privProp == 1;
        }
        return false;
    }

    string GetPassword(SessionInfo session)
    {
        if (session.Properties != null &&
            session.Properties.TryGetValue("password", out var passProp) &&
            passProp.IsString)
        {
            return (string)passProp;
        }
        return "";
    }

    void SetInteractable(bool value)
    {
        if (createRoomButton) createRoomButton.interactable = value;
        if (backButton) backButton.interactable = value;
        if (roomNameInput) roomNameInput.interactable = value;
        if (privateToggle) privateToggle.interactable = value;
        if (passwordInput) passwordInput.interactable = value;
    }
}