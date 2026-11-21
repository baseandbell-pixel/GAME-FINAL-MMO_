using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;

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

    public event Action<State, string> OnStateChanged;
    public event Action<PlayerRef, bool> OnMasterClientChanged;
    public event Action<IReadOnlyList<SessionInfo>> OnSessionListChanged;

    public State CurrentState { get; private set; } = State.Idle;
    public NetworkRunner Runner { get; private set; }
    public PlayerRef MasterClient { get; private set; } = PlayerRef.None;

    public IReadOnlyList<SessionInfo> CachedSessions => _cachedSessions;

    PlayerRef _lastMaster = PlayerRef.None;
    List<SessionInfo> _cachedSessions = new List<SessionInfo>();

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
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

        if (!master.IsRealPlayer || !hadPreviousMaster) return;

        bool localIsMaster = Runner != null && master == Runner.LocalPlayer;
        OnMasterClientChanged?.Invoke(master, localIsMaster);
    }

    void SetState(State state, string message)
    {
        CurrentState = state;
        OnStateChanged?.Invoke(state, message);
    }

    /// <summary>สร้าง NetworkRunner เป็น Child GameObject ใหม่เมื่อยังไม่มี</summary>
    void EnsureRunnerExists()
    {
        if (Runner != null) return;

        var runnerGo = new GameObject("FusionNetworkRunner");
        runnerGo.transform.SetParent(transform);

        Runner = runnerGo.AddComponent<NetworkRunner>();
        Runner.ProvideInput = false;
        Runner.AddCallbacks(this);
    }

    void CleanupRunner(NetworkRunner targetRunner = null)
    {
        var r = targetRunner != null ? targetRunner : Runner;
        if (r != null)
        {
            r.RemoveCallbacks(this);
            if (r.gameObject != null)
                Destroy(r.gameObject);
        }
        if (Runner == r)
            Runner = null;
    }

    public async Task<bool> ConnectToPhotonAsync()
    {
        if (CurrentState == State.Connecting || CurrentState == State.Joining) return false;
        if (CurrentState == State.ConnectedToPhoton || CurrentState == State.InLobby) return true;

        SetState(State.Connecting, "Connecting to Photon...");
        EnsureRunnerExists();

        var result = await Runner.JoinSessionLobby(SessionLobby.Shared);
        if (!result.Ok)
        {
            CleanupRunner();
            SetState(State.Failed, $"Photon connect failed: {result.ShutdownReason}");
            return false;
        }

        SetState(State.ConnectedToPhoton, "Connected to Lobby.");
        return true;
    }

    /// <summary>สร้างห้องใหม่ (Player 1 กด Create)</summary>
    public async Task<bool> CreateRoomAsync(string customRoomName = null)
    {
        if (CurrentState == State.Idle || CurrentState == State.Failed || Runner == null)
        {
            if (!await ConnectToPhotonAsync()) return false;
        }
        if (CurrentState == State.Joining || CurrentState == State.InLobby) return false;

        SetState(State.Joining, "Creating room...");

        string hostName = GoogleAPI.instance != null && !string.IsNullOrEmpty(GoogleAPI.instance.username)
            ? GoogleAPI.instance.username
            : "Player";

        string displayRoomName = string.IsNullOrWhiteSpace(customRoomName)
            ? $"{hostName}'s Room"
            : customRoomName.Trim();

        string uniqueSessionId = $"Room_{Guid.NewGuid().ToString("N").Substring(0, 6)}";

        var sessionProps = new Dictionary<string, SessionProperty>
        {
            { "roomName", displayRoomName },
            { "hostName", hostName }
        };

        var result = await Runner.StartGame(new StartGameArgs
        {
            GameMode = GameMode.Shared,
            SessionName = uniqueSessionId,
            SessionProperties = sessionProps,
            PlayerCount = MaxPlayers,
            IsVisible = true,
            IsOpen = true,
            EnableClientSessionCreation = true,
            SceneManager = Runner.GetComponent<INetworkSceneManager>() ?? Runner.gameObject.AddComponent<NetworkSceneManagerDefault>(),
            ObjectProvider = Runner.GetComponent<INetworkObjectProvider>() ?? Runner.gameObject.AddComponent<NetworkObjectProviderDefault>(),
        });

        if (!result.Ok)
        {
            CleanupRunner();
            SetState(State.Failed, $"Create room failed: {result.ShutdownReason}");
            return false;
        }

        SpawnLocalPlayer();
        SetState(State.InLobby, $"Created room: {displayRoomName}");
        return true;
    }

    /// <summary>เข้าร่วมห้องที่มีอยู่แล้วจากรายการ (Player 2+ กด Join)</summary>
    public async Task<bool> JoinRoomAsync(string sessionName)
    {
        if (string.IsNullOrEmpty(sessionName)) return false;

        if (CurrentState == State.Idle || CurrentState == State.Failed || Runner == null)
        {
            if (!await ConnectToPhotonAsync()) return false;
        }
        if (CurrentState == State.Joining || CurrentState == State.InLobby) return false;

        SetState(State.Joining, "Joining room...");

        var result = await Runner.StartGame(new StartGameArgs
        {
            GameMode = GameMode.Shared,
            SessionName = sessionName,
            EnableClientSessionCreation = false,
            SceneManager = Runner.GetComponent<INetworkSceneManager>() ?? Runner.gameObject.AddComponent<NetworkSceneManagerDefault>(),
            ObjectProvider = Runner.GetComponent<INetworkObjectProvider>() ?? Runner.gameObject.AddComponent<NetworkObjectProviderDefault>(),
        });

        if (!result.Ok)
        {
            CleanupRunner();
            SetState(State.Failed, $"Join room failed: {result.ShutdownReason}");
            return false;
        }

        SpawnLocalPlayer();
        SetState(State.InLobby, $"Joined room {Runner.SessionInfo.Name}");
        return true;
    }

    public bool IsFirstPlayer => Runner != null && Runner.IsRunning && Runner.IsSharedModeMasterClient;

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

        string targetScene = gameSceneName.Trim();
        if (targetScene.Equals("Dev_Kulphat", StringComparison.OrdinalIgnoreCase))
        {
            targetScene = "Dev_Kulpat";
        }

        if (Runner != null && Runner.SessionInfo.IsValid)
            Runner.SessionInfo.IsOpen = false;

        SetState(State.Starting, "Loading the map...");
        Runner.LoadScene(targetScene, LoadSceneMode.Single);
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
        if (Runner == null) return;
        await Runner.Shutdown();
    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player) { }
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) => PollMasterClient();

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        if (Runner != null && Runner != runner) return;

        CleanupRunner(runner);
        LobbyPlayer.All.Clear();
        _cachedSessions.Clear();
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

    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
    {
        _cachedSessions = sessionList != null ? new List<SessionInfo>(sessionList) : new List<SessionInfo>();
        OnSessionListChanged?.Invoke(_cachedSessions);
    }

    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnUserSimulationMessage(NetworkRunner runner) { }
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