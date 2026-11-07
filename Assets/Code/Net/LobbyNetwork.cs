using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(NetworkRunner))]
public class LobbyNetwork : MonoBehaviour, INetworkRunnerCallbacks
{
    public enum State { Idle, Connecting, ConnectedToPhoton, Joining, InLobby, Starting, InGame, Failed }

    public const int AvatarCount = 4;
    public const int MaxPlayers = 4;

    public static LobbyNetwork Instance { get; private set; }

    public LobbyPlayer lobbyPlayerPrefab;

    [Header("Gameplay")]
    [Tooltip("Name of the map scene to load when the first player presses PLAY. It must be in Build Settings.")]
    public string gameSceneName = "SimpleScene";

    /// <summary>Raised whenever the connection state changes (state, human readable message).</summary>
    public event Action<State, string> OnStateChanged;

    /// <summary>
    /// Raised when the room gets a new "first player". In Shared mode Photon hands the
    /// master client role to somebody else as soon as the current one leaves, so this is
    /// how the remaining clients learn who owns the PLAY button now.
    /// (new master, true when that is the local player)
    /// </summary>
    public event Action<PlayerRef, bool> OnMasterClientChanged;

    public State CurrentState { get; private set; } = State.Idle;
    public NetworkRunner Runner { get; private set; }

    /// <summary>The player that currently owns the room, or PlayerRef.None when not in a room.</summary>
    public PlayerRef MasterClient { get; private set; } = PlayerRef.None;

    PlayerRef _lastMaster = PlayerRef.None;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        Runner = GetComponent<NetworkRunner>();
        Runner.ProvideInput = false;
        Runner.AddCallbacks(this);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        // Shared mode has no "master client changed" callback, so we watch it ourselves.
        PollMasterClient();
    }

    void PollMasterClient()
    {
        PlayerRef master = PlayerRef.None;
        if (Runner != null && Runner.IsRunning && Runner.GameMode == GameMode.Shared)
            master = Runner.GetMasterClient();

        if (master == _lastMaster) return;

        bool hadPreviousMaster = _lastMaster.IsRealPlayer;
        _lastMaster = master;
        MasterClient = master;

        // The very first assignment (we just created/joined the room) is not a hand-over,
        // so only a real change is announced.
        if (!master.IsRealPlayer || !hadPreviousMaster) return;

        bool localIsMaster = Runner != null && master == Runner.LocalPlayer;
        OnMasterClientChanged?.Invoke(master, localIsMaster);
    }

    void SetState(State state, string message)
    {
        CurrentState = state;
        OnStateChanged?.Invoke(state, message);
    }

    public async Task<bool> ConnectToPhotonAsync()
    {
        if (CurrentState == State.Connecting || CurrentState == State.Joining) return false;
        if (CurrentState == State.ConnectedToPhoton || CurrentState == State.InLobby) return true;

        SetState(State.Connecting, "Connecting to Photon...");

        var result = await Runner.JoinSessionLobby(SessionLobby.Shared);
        if (!result.Ok)
        {
            SetState(State.Failed, $"Photon connect failed: {result.ShutdownReason}");
            return false;
        }

        SetState(State.ConnectedToPhoton, "Connected. Ready to Quick Join.");
        return true;
    }

    public async Task<bool> QuickJoinAsync()
    {
        if (CurrentState == State.Idle || CurrentState == State.Failed)
        {
            if (!await ConnectToPhotonAsync()) return false;
        }
        if (CurrentState == State.Joining || CurrentState == State.InLobby) return false;

        SetState(State.Joining, "Looking for a room...");

        var result = await Runner.StartGame(new StartGameArgs
        {
            GameMode = GameMode.Shared,
            SessionName = null,                       // null => random matchmaking
            PlayerCount = MaxPlayers,
            MatchmakingMode = Photon.Realtime.MatchmakingMode.FillRoom,
            EnableClientSessionCreation = true,       // create a room if none is free
            SceneManager = GetComponent<INetworkSceneManager>() ?? gameObject.AddComponent<NetworkSceneManagerDefault>(),
            ObjectProvider = GetComponent<INetworkObjectProvider>() ?? gameObject.AddComponent<NetworkObjectProviderDefault>(),
        });

        if (!result.Ok)
        {
            SetState(State.Failed, $"Quick Join failed: {result.ShutdownReason}");
            return false;
        }

        SpawnLocalPlayer();
        SetState(State.InLobby, $"Joined room {Runner.SessionInfo.Name}");
        return true;
    }

    /// <summary>True for the player that owns the room - in Shared mode that is the first player in.</summary>
    public bool IsFirstPlayer => Runner != null && Runner.IsRunning && Runner.IsSharedModeMasterClient;

    /// <summary>Loads the map for everybody in the room. Only the first player may call it.</summary>
    public void StartGame()
    {
        if (!IsFirstPlayer)
        {
            Debug.LogWarning("[LobbyNetwork] Only the first player (master client) can start the game.");
            return;
        }
        if (string.IsNullOrEmpty(gameSceneName))
        {
            Debug.LogError("[LobbyNetwork] gameSceneName is not set.");
            return;
        }

        SetState(State.Starting, "Loading the map...");
        Runner.LoadScene(gameSceneName, LoadSceneMode.Single);
    }

    void SpawnLocalPlayer()
    {
        if (lobbyPlayerPrefab == null)
        {
            Debug.LogError("[LobbyNetwork] lobbyPlayerPrefab is not assigned.");
            return;
        }
        if (LobbyPlayer.Local != null) return;

        Runner.Spawn(lobbyPlayerPrefab, Vector3.zero, Quaternion.identity, Runner.LocalPlayer);
    }

    public async void LeaveLobby()
    {
        if (Runner == null || !Runner.IsRunning) return;
        await Runner.Shutdown();
        SetState(State.Idle, "Left the room.");
    }


    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player) { }
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        // The role may have just moved to somebody else - tell the UI right away
        // instead of waiting for the next Update.
        PollMasterClient();
    }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        LobbyPlayer.All.Clear();
        MasterClient = PlayerRef.None;
        _lastMaster = PlayerRef.None;
        SetState(State.Idle, $"Disconnected ({shutdownReason}).");
    }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        SetState(State.Failed, $"Disconnected: {reason}");
    }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
        SetState(State.Failed, $"Connect failed: {reason}");
    }

    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnUserSimulationMessage(NetworkRunner runner) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnSceneLoadDone(NetworkRunner runner)
    {
        if (CurrentState == State.Starting) SetState(State.InGame, "In game.");
    }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
}
