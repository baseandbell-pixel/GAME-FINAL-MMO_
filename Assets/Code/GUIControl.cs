using System;
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


    public GameObject menuPanel;
    public Button quickJoinButton;

    public GameObject lobbyPanel;

    public LobbyNetwork lobbyNetwork;

    private bool _busy;
    private bool _pass;

    void Start()
    {
        api = GoogleAPI.instance;

        // Only the login step is visible until Google sign-in succeeds.
        if (menuPanel) menuPanel.SetActive(false);
        if (lobbyPanel) lobbyPanel.SetActive(false);
        if (quickJoinButton)
        {
            quickJoinButton.interactable = false;
            quickJoinButton.onClick.AddListener(OnClickQuickJoin);
        }

        if (lobbyNetwork == null) lobbyNetwork = LobbyNetwork.Instance;
        if (lobbyNetwork != null) lobbyNetwork.OnStateChanged += OnNetworkStateChanged;
    }

    void OnDestroy()
    {
        if (quickJoinButton) quickJoinButton.onClick.RemoveListener(OnClickQuickJoin);
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

            // Firestore is the source of truth for name + hero; the Google display
            // name is only a fallback for a player we have never seen before.
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

        if (lobbyNetwork == null) lobbyNetwork = LobbyNetwork.Instance;
        if (lobbyNetwork == null)
        {
            SetStatus("LobbyNetwork is missing in the scene.");
            return;
        }

        SetStatus("Connecting to Photon...");
        bool ok = await lobbyNetwork.ConnectToPhotonAsync();
        if (quickJoinButton) quickJoinButton.interactable = ok;
    }

    public async void OnClickQuickJoin()
    {
        if (lobbyNetwork == null) return;
        if (quickJoinButton) quickJoinButton.interactable = false;

        bool ok = await lobbyNetwork.QuickJoinAsync();
        if (!ok)
        {
            if (quickJoinButton) quickJoinButton.interactable = true;
            return;
        }

        if (menuPanel) menuPanel.SetActive(false);
        if (lobbyPanel) lobbyPanel.SetActive(true);
    }

    void OnNetworkStateChanged(LobbyNetwork.State state, string message)
    {
        SetStatus(message);


        if (state == LobbyNetwork.State.Idle || state == LobbyNetwork.State.Failed)
        {
            if (lobbyPanel && lobbyPanel.activeSelf)
            {
                lobbyPanel.SetActive(false);
                if (menuPanel) menuPanel.SetActive(true);
            }
            if (quickJoinButton && menuPanel && menuPanel.activeSelf)
                quickJoinButton.interactable = true;
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
