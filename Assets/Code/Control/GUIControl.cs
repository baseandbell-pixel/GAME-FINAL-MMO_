using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Google/Firebase login and lobby UI controller with gameplay cursor/input gating.
/// This version preserves the existing GoogleAPI, FirebaseGoogleSignIn, and LobbyNetwork flow.
/// </summary>
public sealed class GUIControl : MonoBehaviour
{
    public static GUIControl Instance { get; private set; }

    private GoogleAPI api;

    [Header("Google OAuth")]
    public string googleDesktopClientId;
    public string googleDesktopClientSecret;

    [Header("Login UI")]
    public Button signInbutton;
    public TMP_Text debug;

    [Header("Menu and Lobby UI")]
    public GameObject menuPanel;
    public Button quickJoinButton;
    public GameObject lobbyPanel;
    public LobbyNetwork lobbyNetwork;

    [Header("Gameplay Input")]
    [Tooltip("The Main Camera component that owns MMOCameraController. It can be found automatically after the game scene loads.")]
    public MMOCameraController mmoCameraController;
    [Tooltip("Keep this UI controller alive while Fusion changes from the lobby scene to the gameplay scene.")]
    public bool persistAcrossScenes = true;

    private bool _busy;
    private bool _pass;
    private LobbyNetwork _subscribedLobbyNetwork;

    // Runtime Room Management UI attached cleanly inside menuPanel
    private bool _roomUiBuilt;
    private Button _createRoomButton;
    private Button _joinCodeButton;
    private TMP_InputField _roomCodeInput;
    private TMP_Text _onlineRoomsLabel;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(transform.root.gameObject);
            return;
        }
        Instance = this;

        if (persistAcrossScenes)
        {
            // Keep this component and its UI references alive when Fusion loads the game scene.
            DontDestroyOnLoad(transform.root.gameObject);
        }
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        BindLobbyNetwork();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Start()
    {
        api = GoogleAPI.instance;
        SetGameplayMode(false);

        // Only the login step is visible until Google sign-in succeeds.
        if (menuPanel) menuPanel.SetActive(false);
        if (lobbyPanel) lobbyPanel.SetActive(false);

        if (quickJoinButton)
        {
            quickJoinButton.interactable = false;
            quickJoinButton.onClick.AddListener(OnClickQuickJoin);
        }

        BindLobbyNetwork();
    }

    private void BindLobbyNetwork()
    {
        LobbyNetwork latest = LobbyNetwork.Instance != null ? LobbyNetwork.Instance : lobbyNetwork;
        if (latest == null)
        {
            return;
        }

        if (_subscribedLobbyNetwork != latest)
        {
            if (_subscribedLobbyNetwork != null)
            {
                _subscribedLobbyNetwork.OnStateChanged -= OnNetworkStateChanged;
                _subscribedLobbyNetwork.OnSessionListChanged -= OnSessionListUpdated;
            }
            _subscribedLobbyNetwork = latest;
            _subscribedLobbyNetwork.OnStateChanged += OnNetworkStateChanged;
            _subscribedLobbyNetwork.OnSessionListChanged += OnSessionListUpdated;
        }

        lobbyNetwork = latest;
    }

    private void OnSessionListUpdated(System.Collections.Generic.IReadOnlyList<Fusion.SessionInfo> sessions)
    {
        if (_onlineRoomsLabel != null)
        {
            int count = sessions != null ? sessions.Count : 0;
            _onlineRoomsLabel.text = $"Open Rooms ({LobbyNetwork.NetworkBuildVersion}): {count}";
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        BindLobbyNetwork();
        mmoCameraController = null;

        if (string.Equals(scene.name, "Lobby", StringComparison.OrdinalIgnoreCase))
        {
            if (lobbyPanel) lobbyPanel.SetActive(false);
            if (debug) debug.gameObject.SetActive(true);
            SetGameplayMode(false);

            api = GoogleAPI.instance;
            if (IsSignedIn)
            {
                if (signInbutton) signInbutton.gameObject.SetActive(false);
                ShowMenuAfterLogin();
            }
            else
            {
                if (signInbutton)
                {
                    signInbutton.gameObject.SetActive(true);
                    signInbutton.interactable = true;
                }
                if (menuPanel) menuPanel.SetActive(false);
            }
            return;
        }

        // Gameplay scene (e.g. Map1): never leave Lobby or Menu panels visible over the world.
        if (menuPanel) menuPanel.SetActive(false);
        if (lobbyPanel) lobbyPanel.SetActive(false);
        if (debug) debug.gameObject.SetActive(false);
        SetGameplayMode(true);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        if (quickJoinButton)
        {
            quickJoinButton.onClick.RemoveListener(OnClickQuickJoin);
        }

        if (_subscribedLobbyNetwork != null)
        {
            _subscribedLobbyNetwork.OnStateChanged -= OnNetworkStateChanged;
            _subscribedLobbyNetwork.OnSessionListChanged -= OnSessionListUpdated;
            _subscribedLobbyNetwork = null;
        }
    }

    public async void OnClickSignIn()
    {
        if (_busy)
        {
            return;
        }

        if (api == null)
        {
            api = GoogleAPI.instance;
        }

        if (api == null)
        {
            SetStatus("GoogleAPI instance is missing in the scene.");
            return;
        }

        _busy = true;
        _pass = false;
        api.stage = 0;
        SetGameplayMode(false);
        SetStatus("Opening browser...");

        if (signInbutton)
        {
            signInbutton.interactable = false;
        }

        try
        {
            var oauth = new GoogleOAuthLoopback(
                googleDesktopClientId,
                googleDesktopClientSecret,
                "openid", "email", "profile");

            var tokens = await oauth.SignInAsync();
            SetStatus("Exchanging token with Firebase...");

            var user = await FirebaseGoogleSignIn.SignInWithIdTokenAsync(tokens.id_token);
            SetStatus($"Signed in: {user.DisplayName} ({user.Email})");
            Debug.Log($"Firebase UID: {user.UserId}");

            api.uuid = user.UserId;

            // Firestore remains the source of truth for the player name and selected avatar.
            SetStatus("Loading profile...");
            await api.LoadProfileAsync(user.DisplayName);
            SetStatus($"Welcome, {api.username}!");

            api.stage = 10;
            _pass = true;
        }
        catch (Exception exception)
        {
            Debug.LogError("Google sign-in failed: " + exception);
            SetStatus("Sign-in failed. Check Console.");
            api.stage = 1;
            _pass = false;
        }
        finally
        {
            if (signInbutton && _pass)
            {
                signInbutton.gameObject.SetActive(false);
            }
            else if (signInbutton)
            {
                signInbutton.interactable = true;
            }

            _busy = false;
        }

        if (_pass)
        {
            ShowMenuAfterLogin();
        }
    }

    private async void ShowMenuAfterLogin()
    {
        SetGameplayMode(false);
        if (menuPanel) menuPanel.SetActive(true);
        if (lobbyPanel) lobbyPanel.SetActive(false);
        EnsureRoomManagementUI();
        SetRoomButtonsInteractable(false);

        BindLobbyNetwork();

        if (lobbyNetwork == null)
        {
            SetStatus("LobbyNetwork is missing in the scene.");
            return;
        }

        SetStatus("Connecting to Photon...");
        bool connected = await lobbyNetwork.ConnectToPhotonAsync();
        SetRoomButtonsInteractable(connected);
        if (_subscribedLobbyNetwork != null)
        {
            OnSessionListUpdated(_subscribedLobbyNetwork.AvailableSessions);
        }
    }

    public void OnClickQuickJoin()
    {
        _ = RunRoomActionAsync(() => lobbyNetwork.QuickJoinAsync());
    }

    private void OnClickCreateRoom()
    {
        string code = _roomCodeInput != null ? _roomCodeInput.text : string.Empty;
        _ = RunRoomActionAsync(() => lobbyNetwork.CreateRoomAsync(code));
    }

    private void OnClickJoinByCode()
    {
        string code = _roomCodeInput != null ? _roomCodeInput.text : string.Empty;
        _ = RunRoomActionAsync(() => lobbyNetwork.JoinRoomByCodeAsync(code));
    }

    private async System.Threading.Tasks.Task RunRoomActionAsync(Func<System.Threading.Tasks.Task<bool>> action)
    {
        BindLobbyNetwork();

        if (lobbyNetwork == null)
        {
            SetStatus("LobbyNetwork is missing in the scene.");
            return;
        }

        SetGameplayMode(false);
        SetRoomButtonsInteractable(false);

        bool joined = await action();
        if (!joined)
        {
            SetRoomButtonsInteractable(true);
            return;
        }

        if (menuPanel) menuPanel.SetActive(false);
        if (lobbyPanel) lobbyPanel.SetActive(true);
        SetGameplayMode(false);
    }

    private void SetRoomButtonsInteractable(bool interactable)
    {
        if (quickJoinButton) quickJoinButton.interactable = interactable;
        if (_createRoomButton) _createRoomButton.interactable = interactable;
        if (_joinCodeButton) _joinCodeButton.interactable = interactable;
    }

    private void EnsureRoomManagementUI()
    {
        if (_roomUiBuilt || menuPanel == null)
        {
            return;
        }
        _roomUiBuilt = true;

        Vector2 baseCenter = Vector2.zero;
        if (quickJoinButton != null)
        {
            RectTransform qRt = quickJoinButton.GetComponent<RectTransform>();
            if (qRt != null)
            {
                baseCenter = qRt.anchoredPosition;
            }
        }

        GameObject container = new GameObject("RoomManagementRow");
        RectTransform cRt = container.AddComponent<RectTransform>();
        cRt.SetParent(menuPanel.transform, false);
        cRt.anchorMin = new Vector2(0.5f, 0.5f);
        cRt.anchorMax = new Vector2(0.5f, 0.5f);
        cRt.pivot = new Vector2(0.5f, 0.5f);
        cRt.anchoredPosition = baseCenter + new Vector2(0f, 118f);
        cRt.sizeDelta = new Vector2(360f, 130f);

        // Create Room Button
        _createRoomButton = BuildMenuButton(container.transform, "CreateRoomBtn",
            new Vector2(0f, 42f), new Vector2(360f, 40f),
            new Color(0.18f, 0.56f, 0.34f, 1f), "Create Room (Private / Friends)");
        _createRoomButton.onClick.AddListener(OnClickCreateRoom);

        // Room Code Input
        GameObject inputGo = new GameObject("RoomCodeInput");
        RectTransform inRt = inputGo.AddComponent<RectTransform>();
        inRt.SetParent(container.transform, false);
        inRt.anchorMin = new Vector2(0.5f, 0.5f);
        inRt.anchorMax = new Vector2(0.5f, 0.5f);
        inRt.pivot = new Vector2(0.5f, 0.5f);
        inRt.anchoredPosition = new Vector2(-62f, -6f);
        inRt.sizeDelta = new Vector2(236f, 38f);
        Image inBg = inputGo.AddComponent<Image>();
        inBg.color = new Color(0.10f, 0.13f, 0.18f, 0.96f);

        GameObject textArea = new GameObject("TextArea");
        RectTransform taRt = textArea.AddComponent<RectTransform>();
        taRt.SetParent(inRt, false);
        taRt.anchorMin = Vector2.zero;
        taRt.anchorMax = Vector2.one;
        taRt.offsetMin = new Vector2(10f, 4f);
        taRt.offsetMax = new Vector2(-10f, -4f);
        textArea.AddComponent<RectMask2D>();

        TMP_Text inputTxt = BuildMenuLabel("Text", taRt, 16, Color.white, TextAlignmentOptions.Left);
        TMP_Text placeholder = BuildMenuLabel("Placeholder", taRt, 15, new Color(0.6f, 0.68f, 0.78f, 0.7f), TextAlignmentOptions.Left);
        placeholder.text = "Room Code (optional)";

        _roomCodeInput = inputGo.AddComponent<TMP_InputField>();
        _roomCodeInput.textViewport = taRt;
        _roomCodeInput.textComponent = inputTxt;
        _roomCodeInput.placeholder = placeholder;
        _roomCodeInput.characterLimit = 12;

        // Join Code Button
        _joinCodeButton = BuildMenuButton(container.transform, "JoinCodeBtn",
            new Vector2(122f, -6f), new Vector2(116f, 38f),
            new Color(0.20f, 0.46f, 0.85f, 1f), "Join Code");
        _joinCodeButton.onClick.AddListener(OnClickJoinByCode);

        // Open Rooms label
        GameObject roomsGo = new GameObject("OpenRoomsText");
        RectTransform rmRt = roomsGo.AddComponent<RectTransform>();
        rmRt.SetParent(container.transform, false);
        rmRt.anchorMin = new Vector2(0.5f, 0.5f);
        rmRt.anchorMax = new Vector2(0.5f, 0.5f);
        rmRt.pivot = new Vector2(0.5f, 0.5f);
        rmRt.anchoredPosition = new Vector2(0f, -44f);
        rmRt.sizeDelta = new Vector2(360f, 24f);
        _onlineRoomsLabel = roomsGo.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null) _onlineRoomsLabel.font = TMP_Settings.defaultFontAsset;
        _onlineRoomsLabel.fontSize = 14;
        _onlineRoomsLabel.alignment = TextAlignmentOptions.Center;
        _onlineRoomsLabel.color = new Color(0.75f, 0.88f, 1f);
        _onlineRoomsLabel.text = $"Open Rooms ({LobbyNetwork.NetworkBuildVersion}): 0";
    }

    private static Button BuildMenuButton(Transform parent, string name, Vector2 pos, Vector2 size, Color color, string text)
    {
        GameObject go = new GameObject(name);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        Image img = go.AddComponent<Image>();
        img.color = color;
        Button btn = go.AddComponent<Button>();

        TMP_Text label = BuildMenuLabel("Label", rt, 15, Color.white, TextAlignmentOptions.Center);
        label.fontStyle = FontStyles.Bold;
        label.text = text;
        return btn;
    }

    private static TMP_Text BuildMenuLabel(string name, Transform parent, float fontSize, Color color, TextAlignmentOptions align)
    {
        GameObject go = new GameObject(name);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = align;
        return tmp;
    }

    private bool IsSignedIn
    {
        get
        {
            if (api == null) api = GoogleAPI.instance;
            return _pass || (api != null && (api.stage >= 10 || api.profileLoaded || !string.IsNullOrEmpty(api.uuid)));
        }
    }

    private void OnNetworkStateChanged(LobbyNetwork.State state, string message)
    {
        SetStatus(message);

        if (state == LobbyNetwork.State.Starting)
        {
            if (menuPanel) menuPanel.SetActive(false);
            if (lobbyPanel) lobbyPanel.SetActive(false);
            if (debug) debug.gameObject.SetActive(true);
            SetGameplayMode(false);
            return;
        }

        if (state == LobbyNetwork.State.InGame)
        {
            if (menuPanel) menuPanel.SetActive(false);
            if (lobbyPanel) lobbyPanel.SetActive(false);
            if (debug) debug.gameObject.SetActive(false);
            SetGameplayMode(true);
            return;
        }

        if (debug) debug.gameObject.SetActive(true);

        // Login, connecting, menu, joining, lobby, loading, and failure states never accept gameplay input.
        SetGameplayMode(false);

        if (state == LobbyNetwork.State.Idle || state == LobbyNetwork.State.Failed)
        {
            if (lobbyPanel) lobbyPanel.SetActive(false);
            bool inLobbyScene = string.Equals(SceneManager.GetActiveScene().name, "Lobby", StringComparison.OrdinalIgnoreCase);
            if (menuPanel) menuPanel.SetActive(IsSignedIn && inLobbyScene);

            if (menuPanel && menuPanel.activeSelf)
            {
                SetRoomButtonsInteractable(true);
            }
        }
    }

    /// <summary>
    /// Call this from a pause/options panel too: false unlocks the cursor and blocks camera/mouse attacks;
    /// true restores gameplay lock after the UI is closed.
    /// </summary>
    public void SetGameplayMode(bool enabled)
    {
        if (mmoCameraController == null)
        {
            mmoCameraController = MMOCameraController.Instance;
        }

        if (mmoCameraController == null)
        {
            mmoCameraController = FindAnyObjectByType<MMOCameraController>();
        }

        if (mmoCameraController != null)
        {
            mmoCameraController.SetGameplayEnabled(enabled);
        }
        else if (!enabled)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void SetStatus(string message)
    {
        if (debug)
        {
            debug.text = message;
        }
    }

    public void QuitApplication()
    {
        Application.Quit();
    }

    public void QuitApplicatio()
    {
        QuitApplication();
    }
}
