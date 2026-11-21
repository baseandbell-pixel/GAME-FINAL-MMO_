using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GUIControl : MonoBehaviour
{
    public static GUIControl Instance { get; private set; }

    GoogleAPI api;
    public string googleDesktopClientId;
    public string googleDesktopClientSecret;

    public Button signInbutton;
    public TMP_Text debug;

    [Header("Panels")]
    public GameObject menuPanel;          // หน้ากด Start Game
    public Button startGameButton;        // ปุ่ม Start Game บนหน้า MenuPanel
    public GameObject createRoomPanel;    // หน้า List ห้อง + ปุ่ม Create
    public GameObject lobbyPanel;         // หน้า Lobby รอผู้เล่น 4 คน

    public LobbyNetwork lobbyNetwork;

    private bool _busy;
    private bool _pass;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        api = GoogleAPI.instance;

        if (menuPanel) menuPanel.SetActive(false);
        if (createRoomPanel) createRoomPanel.SetActive(false);
        if (lobbyPanel) lobbyPanel.SetActive(false);

        if (startGameButton)
        {
            startGameButton.interactable = false;
            startGameButton.onClick.AddListener(OnClickStartGame);
        }

        if (lobbyNetwork == null) lobbyNetwork = LobbyNetwork.Instance;
        if (lobbyNetwork != null) lobbyNetwork.OnStateChanged += OnNetworkStateChanged;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (startGameButton) startGameButton.onClick.RemoveListener(OnClickStartGame);
        if (lobbyNetwork != null) lobbyNetwork.OnStateChanged -= OnNetworkStateChanged;
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
        if (startGameButton) startGameButton.interactable = false;

        if (lobbyNetwork == null) lobbyNetwork = LobbyNetwork.Instance;
        if (lobbyNetwork == null)
        {
            SetStatus("LobbyNetwork is missing in the scene.");
            return;
        }

        SetStatus("Connecting to Photon...");
        bool ok = await lobbyNetwork.ConnectToPhotonAsync();
        if (startGameButton) startGameButton.interactable = ok;
    }

    public async void OnClickStartGame()
    {
        if (lobbyNetwork == null) return;

        if (lobbyNetwork.CurrentState == LobbyNetwork.State.Idle ||
            lobbyNetwork.CurrentState == LobbyNetwork.State.Failed)
        {
            if (startGameButton) startGameButton.interactable = false;
            bool ok = await lobbyNetwork.ConnectToPhotonAsync();
            if (startGameButton) startGameButton.interactable = true;
            if (!ok) return;
        }

        if (menuPanel) menuPanel.SetActive(false);
        if (lobbyPanel) lobbyPanel.SetActive(false);
        if (createRoomPanel) createRoomPanel.SetActive(true);
    }

    public void OpenLobbyPanel()
    {
        if (menuPanel) menuPanel.SetActive(false);
        if (createRoomPanel) createRoomPanel.SetActive(false);

        if (lobbyPanel != null)
        {
            lobbyPanel.SetActive(true);
        }
        else
        {
            Debug.LogError("[GUIControl] ยังไม่ได้ลาก LobbyPanel มาใส่ในช่อง Lobby Panel ของ GUIControl!");
        }
    }

    public void BackToMenuPanel()
    {
        if (createRoomPanel) createRoomPanel.SetActive(false);
        if (lobbyPanel) lobbyPanel.SetActive(false);
        if (menuPanel) menuPanel.SetActive(true);
    }

    async void OnNetworkStateChanged(LobbyNetwork.State state, string message)
    {
        SetStatus(message);

        // เมื่อสร้างห้องหรือเข้าห้องสำเร็จ ให้สลับเปิดหน้า LobbyPanel อัตโนมัติทันที
        if (state == LobbyNetwork.State.InLobby)
        {
            OpenLobbyPanel();
        }
        else if (state == LobbyNetwork.State.Idle)
        {
            if (lobbyPanel && lobbyPanel.activeSelf)
            {
                lobbyPanel.SetActive(false);
                if (createRoomPanel) createRoomPanel.SetActive(true);
                if (lobbyNetwork != null) await lobbyNetwork.ConnectToPhotonAsync();
            }
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