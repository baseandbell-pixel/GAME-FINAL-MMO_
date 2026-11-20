using System;
using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GUIControl : MonoBehaviour
{
    GoogleAPI api;
    public string googleDesktopClientId;
    public string googleDesktopClientSecret;

    public Button signInbutton;
    public TMP_Text debug;

    [Header("Menu Panel (หน้า Start Game)")]
    public GameObject menuPanel;
    public Button openRoomBrowserButton; // ปุ่ม Start Game ที่กดแล้วไปหน้า Room List
    public Button quickJoinButton;       // ปุ่ม Quick Join เดิม (ปรับให้เปิดหน้า Room List เช่นกัน)

    [Header("Room Browser Panel (หน้าสร้างและเลือกห้อง)")]
    public GameObject roomBrowserPanel;  // Panel แสดงรายชื่อห้อง + สร้างห้อง
    public TMP_InputField roomNameInput; // ช่องกรอกชื่อห้อง
    public Button createRoomButton;      // ปุ่ม Create Room
    public Transform roomListContent;    // Content ของ ScrollView ที่จะวางปุ่มห้อง
    public RoomListItemUI roomItemPrefab;// Prefab ปุ่มห้อง

    [Header("Room Lobby Panel (หน้าในห้องรอเริ่มเกม)")]
    public GameObject lobbyPanel;        // Panel เดิมที่มีตัวละคร / Ready / Play

    public LobbyNetwork lobbyNetwork;

    private bool _busy;
    private bool _pass;

    void Start()
    {
        api = GoogleAPI.instance;

        if (menuPanel) menuPanel.SetActive(false);
        if (roomBrowserPanel) roomBrowserPanel.SetActive(false);
        if (lobbyPanel) lobbyPanel.SetActive(false);

        if (quickJoinButton)
        {
            quickJoinButton.interactable = false;
            quickJoinButton.onClick.AddListener(OnClickQuickJoin);
        }

        if (openRoomBrowserButton)
        {
            openRoomBrowserButton.interactable = false;
            openRoomBrowserButton.onClick.AddListener(OnClickOpenRoomBrowser);
        }

        if (createRoomButton)
        {
            createRoomButton.onClick.AddListener(OnClickCreateRoom);
        }

        if (lobbyNetwork == null) lobbyNetwork = LobbyNetwork.Instance;
        if (lobbyNetwork != null)
        {
            lobbyNetwork.OnStateChanged += OnNetworkStateChanged;
            lobbyNetwork.OnRoomListUpdated += UpdateRoomListUI;
        }
    }

    void OnDestroy()
    {
        if (quickJoinButton) quickJoinButton.onClick.RemoveListener(OnClickQuickJoin);
        if (openRoomBrowserButton) openRoomBrowserButton.onClick.RemoveListener(OnClickOpenRoomBrowser);
        if (createRoomButton) createRoomButton.onClick.RemoveListener(OnClickCreateRoom);

        if (lobbyNetwork != null)
        {
            lobbyNetwork.OnStateChanged -= OnNetworkStateChanged;
            lobbyNetwork.OnRoomListUpdated -= UpdateRoomListUI;
        }
    }

    public async void OnClickSignIn()
    {
        debug.text = "Process Google...";
        if (_busy) return;
        _busy = true;
        api.stage = 0;
        SetStatus("Opening browser...");
        if (signInbutton) signInbutton.interactable = false;
        try
        {
            var oauth = new GoogleOAuthLoopback(
                googleDesktopClientId,
                googleDesktopClientSecret,
                "openid", "email", "profile"
            );
            var tokens = await oauth.SignInAsync();
            SetStatus("Exchanging token with Firebase...");
            var user = await FirebaseGoogleSignIn.SignInWithIdTokenAsync(tokens.id_token);
            SetStatus($"Signed in: {user.DisplayName} ({user.Email})");
            Debug.Log($"Firebase UID: {user.UserId}");
            api.uuid = user.UserId;

            SetStatus("Loading profile...");
            await api.LoadProfileAsync(user.DisplayName);
            SetStatus($"Welcome, {api.username}!");

            api.stage = 10;
            _pass = true;
        }
        catch (Exception ex)
        {
            Debug.LogError("Google sign-in failed: " + ex);
            SetStatus("Sign-in failed. Check Console.");
            api.stage = 1;
            _pass = false;
        }
        finally
        {
            if (signInbutton && _pass) signInbutton.gameObject.SetActive(false);
            else if (signInbutton) signInbutton.interactable = true;
            _busy = false;
        }

        if (_pass) ShowMenuAfterLogin();
    }

    async void ShowMenuAfterLogin()
    {
        if (menuPanel) menuPanel.SetActive(true);
        if (quickJoinButton) quickJoinButton.interactable = false;
        if (openRoomBrowserButton) openRoomBrowserButton.interactable = false;

        if (lobbyNetwork == null) lobbyNetwork = LobbyNetwork.Instance;
        if (lobbyNetwork == null)
        {
            SetStatus("LobbyNetwork is missing in the scene.");
            return;
        }

        SetStatus("Connecting to Photon...");
        bool ok = await lobbyNetwork.ConnectToPhotonAsync();
        if (quickJoinButton) quickJoinButton.interactable = ok;
        if (openRoomBrowserButton) openRoomBrowserButton.interactable = ok;
    }

    // กดปุ่ม Start Game จากหน้า Menu เพื่อเปิดหน้า Room List
    public void OnClickOpenRoomBrowser()
    {
        if (menuPanel) menuPanel.SetActive(false);
        if (roomBrowserPanel) roomBrowserPanel.SetActive(true);

        // อัปเดตรายการห้องที่มีอยู่ทันทีที่เปิดหน้านี้
        if (lobbyNetwork != null)
        {
            UpdateRoomListUI(lobbyNetwork.CachedSessionList);
        }
    }

    // ปุ่ม Back จากหน้า Room Browser กลับไป Menu
    public void OnClickBackToMenu()
    {
        if (roomBrowserPanel) roomBrowserPanel.SetActive(false);
        if (menuPanel) menuPanel.SetActive(true);
    }

    // กดปุ่ม Create Room
    public async void OnClickCreateRoom()
    {
        if (lobbyNetwork == null) return;
        SetBrowserButtonsInteractable(false);

        string roomName = roomNameInput != null ? roomNameInput.text : "";
        bool ok = await lobbyNetwork.CreateRoomAsync(roomName);

        SetBrowserButtonsInteractable(true);

        if (ok)
        {
            EnterRoomLobbyUI();
        }
    }

    // กดปุ่ม Join ที่รายการห้องใน Room List
    private async void OnClickJoinRoomFromList(string roomName)
    {
        if (lobbyNetwork == null) return;
        SetBrowserButtonsInteractable(false);

        bool ok = await lobbyNetwork.JoinRoomAsync(roomName);

        SetBrowserButtonsInteractable(true);

        if (ok)
        {
            EnterRoomLobbyUI();
        }
    }

    // วาดรายการห้องใน ScrollView
    private void UpdateRoomListUI(List<SessionInfo> sessions)
    {
        if (roomListContent == null || roomItemPrefab == null) return;

        // ล้างรายการเก่าทิ้งก่อน
        foreach (Transform child in roomListContent)
        {
            Destroy(child.gameObject);
        }

        if (sessions == null) return;

        foreach (var session in sessions)
        {
            // แสดงเฉพาะห้องที่เปิดอยู่และมองเห็นได้
            if (!session.IsVisible) continue;

            RoomListItemUI item = Instantiate(roomItemPrefab, roomListContent);
            item.Setup(session, OnClickJoinRoomFromList);
        }
    }

    // [อัปเดต] ปรับให้ปุ่มเก่าที่ผูก OnClickQuickJoin ไว้ เปิดหน้าเลือกห้องแทนการสุ่มเข้าห้องทันที
    public void OnClickQuickJoin()
    {
        OnClickOpenRoomBrowser();
    }

    private void EnterRoomLobbyUI()
    {
        if (menuPanel) menuPanel.SetActive(false);
        if (roomBrowserPanel) roomBrowserPanel.SetActive(false);
        if (lobbyPanel) lobbyPanel.SetActive(true);
    }

    private void SetBrowserButtonsInteractable(bool interactable)
    {
        if (createRoomButton) createRoomButton.interactable = interactable;
        if (quickJoinButton) quickJoinButton.interactable = interactable;
    }

    void OnNetworkStateChanged(LobbyNetwork.State state, string message)
    {
        SetStatus(message);

        // กรณีหลุดออกจากห้อง หรือกด LeaveLobby()
        if (state == LobbyNetwork.State.Idle || state == LobbyNetwork.State.ConnectedToPhoton)
        {
            if (lobbyPanel && lobbyPanel.activeSelf)
            {
                lobbyPanel.SetActive(false);
                // ส่งกลับมาหน้า Room Browser เพื่อให้เลือกห้องใหม่ได้ทันที
                if (roomBrowserPanel) roomBrowserPanel.SetActive(true);
                else if (menuPanel) menuPanel.SetActive(true);
            }
        }
        else if (state == LobbyNetwork.State.Failed)
        {
            if (lobbyPanel) lobbyPanel.SetActive(false);
            if (roomBrowserPanel) roomBrowserPanel.SetActive(false);
            if (menuPanel) menuPanel.SetActive(true);
            if (quickJoinButton) quickJoinButton.interactable = true;
            if (openRoomBrowserButton) openRoomBrowserButton.interactable = true;
        }
    }

    private void SetStatus(string msg)
    {
        if (debug) debug.text = msg;
    }

    public void QuitApplicatio()
    {
        Application.Quit();
    }
}