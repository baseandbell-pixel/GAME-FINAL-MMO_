using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Google/Firebase login and lobby UI controller with gameplay cursor/input gating.
/// This version preserves the existing GoogleAPI, FirebaseGoogleSignIn, and LobbyNetwork flow.
/// </summary>
public sealed class GUIControl : MonoBehaviour
{
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

    private void Awake()
    {
        if (persistAcrossScenes)
        {
            // Keep this component and its UI references alive when Fusion loads the game scene.
            DontDestroyOnLoad(transform.root.gameObject);
        }
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

        if (lobbyNetwork == null)
        {
            lobbyNetwork = LobbyNetwork.Instance;
        }

        if (lobbyNetwork != null)
        {
            lobbyNetwork.OnStateChanged += OnNetworkStateChanged;
        }
    }

    private void OnDestroy()
    {
        if (quickJoinButton)
        {
            quickJoinButton.onClick.RemoveListener(OnClickQuickJoin);
        }

        if (lobbyNetwork != null)
        {
            lobbyNetwork.OnStateChanged -= OnNetworkStateChanged;
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
        if (quickJoinButton) quickJoinButton.interactable = false;

        if (lobbyNetwork == null)
        {
            lobbyNetwork = LobbyNetwork.Instance;
        }

        if (lobbyNetwork == null)
        {
            SetStatus("LobbyNetwork is missing in the scene.");
            return;
        }

        SetStatus("Connecting to Photon...");
        bool connected = await lobbyNetwork.ConnectToPhotonAsync();
        if (quickJoinButton)
        {
            quickJoinButton.interactable = connected;
        }
    }

    public async void OnClickQuickJoin()
    {
        if (lobbyNetwork == null)
        {
            lobbyNetwork = LobbyNetwork.Instance;
        }

        if (lobbyNetwork == null)
        {
            SetStatus("LobbyNetwork is missing in the scene.");
            return;
        }

        SetGameplayMode(false);
        if (quickJoinButton)
        {
            quickJoinButton.interactable = false;
        }

        bool joined = await lobbyNetwork.QuickJoinAsync();
        if (!joined)
        {
            if (quickJoinButton)
            {
                quickJoinButton.interactable = true;
            }
            return;
        }

        if (menuPanel) menuPanel.SetActive(false);
        if (lobbyPanel) lobbyPanel.SetActive(true);
        SetGameplayMode(false);
    }

    private void OnNetworkStateChanged(LobbyNetwork.State state, string message)
    {
        SetStatus(message);

        if (state == LobbyNetwork.State.InGame)
        {
            if (menuPanel) menuPanel.SetActive(false);
            if (lobbyPanel) lobbyPanel.SetActive(false);
            SetGameplayMode(true);
            return;
        }

        // Login, connecting, menu, joining, lobby, loading, and failure states never accept gameplay input.
        SetGameplayMode(false);

        if (state == LobbyNetwork.State.Idle || state == LobbyNetwork.State.Failed)
        {
            if (lobbyPanel) lobbyPanel.SetActive(false);
            if (menuPanel) menuPanel.SetActive(true);

            if (quickJoinButton && menuPanel && menuPanel.activeSelf)
            {
                quickJoinButton.interactable = true;
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
            mmoCameraController = FindObjectOfType<MMOCameraController>();
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

    public void QuitApplicatio()
    {
        Application.Quit();
    }
}
