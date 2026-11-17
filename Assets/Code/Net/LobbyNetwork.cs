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
    public const string NetworkBuildVersion = "mmo_v6";
    public const string CustomLobbyName = "MMO_" + NetworkBuildVersion;

    public static LobbyNetwork Instance { get; private set; }

    public LobbyPlayer lobbyPlayerPrefab;

    [Header("Gameplay")]
    [Tooltip("Name of the map scene to load when the first player presses PLAY. It must be in Build Settings.")]
    public string gameSceneName = "SimpleScene";
    [Tooltip("Fusion Network Prefab Ref for the actual playable character prefab (NetworkObject + NetworkTransform + MMOPlayerFusion).")]
    public NetworkPrefabRef gameplayPlayerPrefab;
    [Tooltip("World position where gameplay characters spawn after the game scene loads.")]
    public Vector3 gameplaySpawnPosition = Vector3.zero;
    [Tooltip("Extra world-space spacing multiplied by the local PlayerId modulo MaxPlayers to avoid spawning everyone at exactly the same point.")]
    public Vector3 gameplaySpawnOffsetPerPlayer = new Vector3(2f, 0f, 0f);

    private bool _localGameplayPlayerSpawned;
    private NetworkObject _localGameplayPlayer;
    private GameObject _runnerHost;
    private bool _isReturningToLobby;
    private readonly List<SessionInfo> _availableSessions = new List<SessionInfo>();

    /// <summary>Raised whenever the connection state changes (state, human readable message).</summary>
    public event Action<State, string> OnStateChanged;
    public event Action<IReadOnlyList<SessionInfo>> OnSessionListChanged;

    public string CurrentRoomCode =>
        (Runner != null && Runner.IsRunning && Runner.SessionInfo.IsValid)
            ? Runner.SessionInfo.Name
            : string.Empty;

    public IReadOnlyList<SessionInfo> AvailableSessions => _availableSessions;

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

        // Keep this manager (and GoogleAPI if co-located) alive across Fusion scene loads.
        DontDestroyOnLoad(transform.root.gameObject);

        // If a NetworkRunner was serialized directly onto this GameObject in the scene,
        // destroy it so Fusion Shutdown() only destroys a dedicated child GameObject.
        NetworkRunner bakedRunner = GetComponent<NetworkRunner>();
        if (bakedRunner != null)
        {
            bakedRunner.enabled = false;
            Destroy(bakedRunner);
        }

        EnsureFreshRunner();
    }

    private void EnsureFreshRunner()
    {
        if (Runner != null && !Runner.IsShutdown)
        {
            return;
        }

        if (_runnerHost != null)
        {
            Destroy(_runnerHost);
            _runnerHost = null;
        }
        Runner = null;

        _runnerHost = new GameObject("FusionRunner");
        _runnerHost.transform.SetParent(transform, false);
        Runner = _runnerHost.AddComponent<NetworkRunner>();
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
        if ((CurrentState == State.ConnectedToPhoton || CurrentState == State.InLobby) && Runner != null && !Runner.IsShutdown)
        {
            return true;
        }

        EnsureFreshRunner();
        SetState(State.Connecting, "Connecting to Photon...");

        var result = await Runner.JoinSessionLobby(SessionLobby.Custom, CustomLobbyName);
        if (!result.Ok)
        {
            SetState(State.Failed, $"Photon connect failed: {result.ShutdownReason}");
            return false;
        }

        SetState(State.ConnectedToPhoton, $"Connected ({NetworkBuildVersion}). Ready to Join or Create Room.");
        return true;
    }

    public Task<bool> QuickJoinAsync()
    {
        return StartGameSessionAsync(null, allowCreate: true, isPublicMatchmaking: true, "Looking for a room...");
    }

    public Task<bool> CreateRoomAsync(string optionalCode = null)
    {
        string code = SanitizeRoomCode(optionalCode);
        if (string.IsNullOrEmpty(code))
        {
            code = GenerateRoomCode(5);
        }

        return StartGameSessionAsync(code, allowCreate: true, isPublicMatchmaking: false, $"Creating room {code}...");
    }

    public async Task<bool> JoinRoomByCodeAsync(string rawCode)
    {
        string code = SanitizeRoomCode(rawCode);
        if (string.IsNullOrEmpty(code))
        {
            SetState(State.Failed, "Please enter a valid Room Code.");
            return false;
        }

        return await StartGameSessionAsync(code, allowCreate: false, isPublicMatchmaking: false, $"Joining room {code}...");
    }

    private async Task<bool> StartGameSessionAsync(
        string sessionName,
        bool allowCreate,
        bool isPublicMatchmaking,
        string statusMessage)
    {
        EnsureFreshRunner();

        if (CurrentState == State.Idle || CurrentState == State.Failed || CurrentState == State.InGame || CurrentState == State.Starting)
        {
            if (!await ConnectToPhotonAsync()) return false;
        }
        if (CurrentState == State.Joining || CurrentState == State.InLobby) return false;

        EnsureFreshRunner();
        SetState(State.Joining, statusMessage);

        GameObject runnerHost = Runner.gameObject;
        var sessionProps = new Dictionary<string, SessionProperty>
        {
            { "ver", NetworkBuildVersion }
        };

        var result = await Runner.StartGame(new StartGameArgs
        {
            GameMode = GameMode.Shared,
            CustomLobbyName = CustomLobbyName,
            SessionName = sessionName,
            PlayerCount = MaxPlayers,
            IsVisible = isPublicMatchmaking,
            SessionProperties = sessionProps,
            MatchmakingMode = Photon.Realtime.MatchmakingMode.FillRoom,
            EnableClientSessionCreation = allowCreate,
            SceneManager = runnerHost.GetComponent<INetworkSceneManager>() ?? runnerHost.AddComponent<NetworkSceneManagerDefault>(),
            ObjectProvider = runnerHost.GetComponent<INetworkObjectProvider>() ?? runnerHost.AddComponent<NetworkObjectProviderDefault>(),
        });

        if (!result.Ok)
        {
            SetState(State.Failed, $"Room join failed: {result.ShutdownReason}");
            return false;
        }

        SpawnLocalPlayer();
        string roomLabel = Runner.SessionInfo.IsValid ? Runner.SessionInfo.Name : (sessionName ?? "Room");
        SetState(State.InLobby, $"Joined room {roomLabel}");
        return true;
    }

    public static string SanitizeRoomCode(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var sb = new System.Text.StringBuilder(12);
        string upper = raw.Trim().ToUpperInvariant();
        for (int i = 0; i < upper.Length && sb.Length < 12; i++)
        {
            char c = upper[i];
            if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_')
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static string GenerateRoomCode(int length)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        char[] buf = new char[length];
        for (int i = 0; i < length; i++)
        {
            buf[i] = alphabet[UnityEngine.Random.Range(0, alphabet.Length)];
        }
        return new string(buf);
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

    private async Task ShutdownRunnerSafely()
    {
        NetworkRunner activeRunner = Runner;
        Runner = null;

        if (activeRunner != null && !activeRunner.IsShutdown)
        {
            await activeRunner.Shutdown(destroyGameObject: activeRunner.gameObject != gameObject);
        }

        if (_runnerHost != null)
        {
            Destroy(_runnerHost);
            _runnerHost = null;
        }

        LobbyPlayer.All.Clear();
        MasterClient = PlayerRef.None;
        _lastMaster = PlayerRef.None;
        _localGameplayPlayer = null;
        _localGameplayPlayerSpawned = false;
    }

    public async void LeaveLobby()
    {
        await ShutdownRunnerSafely();
        SetState(State.Idle, "Left the room.");
        EnsureFreshRunner();
    }

    /// <summary>
    /// Leaves the active Fusion session (if any) and returns cleanly to the Lobby scene.
    /// </summary>
    public async void ReturnToLobbyScene()
    {
        if (_isReturningToLobby) return;
        _isReturningToLobby = true;
        try
        {
            await ShutdownRunnerSafely();
            SetState(State.Idle, "Returned to Lobby.");
            EnsureFreshRunner();
            SceneManager.LoadScene("Lobby", LoadSceneMode.Single);
        }
        finally
        {
            _isReturningToLobby = false;
        }
    }


    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player) { }
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        if (Runner != null && runner != Runner) return;
        // The role may have just moved to somebody else - tell the UI right away
        // instead of waiting for the next Update.
        PollMasterClient();
    }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        if (Runner != null && runner != Runner) return;

        LobbyPlayer.All.Clear();
        MasterClient = PlayerRef.None;
        _lastMaster = PlayerRef.None;
        _localGameplayPlayer = null;
        _localGameplayPlayerSpawned = false;
        if (CurrentState != State.Idle)
        {
            SetState(State.Idle, $"Disconnected ({shutdownReason}).");
        }
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
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
    {
        _availableSessions.Clear();
        if (sessionList != null)
        {
            for (int i = 0; i < sessionList.Count; i++)
            {
                if (sessionList[i].IsValid && sessionList[i].IsVisible)
                {
                    _availableSessions.Add(sessionList[i]);
                }
            }
        }
        OnSessionListChanged?.Invoke(_availableSessions);
    }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnSceneLoadDone(NetworkRunner runner)
    {
        if (Runner != null && runner != Runner) return;

        string activeSceneName = SceneManager.GetActiveScene().name;
        bool inGameScene = !string.IsNullOrEmpty(gameSceneName) &&
                           string.Equals(activeSceneName, gameSceneName, StringComparison.OrdinalIgnoreCase);

        if (!inGameScene && CurrentState != State.Starting)
        {
            return;
        }

        SetState(State.InGame, "In game.");
        SpawnLocalGameplayPlayer();
    }

    /// <summary>
    /// Each Fusion Shared Mode client spawns the character it owns after the synchronized
    /// gameplay scene has finished loading. The lobby LobbyPlayer remains responsible for lobby UI.
    /// </summary>
    private void SpawnLocalGameplayPlayer()
    {
        if (_localGameplayPlayerSpawned)
        {
            return;
        }

        if (Runner == null || !Runner.IsRunning)
        {
            Debug.LogError("[LobbyNetwork] Cannot spawn gameplay player because the NetworkRunner is not running.");
            return;
        }

        if (!gameplayPlayerPrefab.IsValid)
        {
            Debug.LogError("[LobbyNetwork] gameplayPlayerPrefab is not assigned. Assign the Fusion gameplay character prefab in the Inspector.");
            return;
        }

        try
        {
            int spawnSlot = Runner.LocalPlayer.PlayerId % MaxPlayers;
            Vector3 spawnPosition = gameplaySpawnPosition + gameplaySpawnOffsetPerPlayer * spawnSlot;
            spawnPosition.y = Mathf.Max(0.1f, spawnPosition.y);

            _localGameplayPlayer = Runner.Spawn(
                gameplayPlayerPrefab,
                spawnPosition,
                Quaternion.identity);

            _localGameplayPlayerSpawned = _localGameplayPlayer != null;
            if (!_localGameplayPlayerSpawned)
            {
                Debug.LogError("[LobbyNetwork] Fusion returned no NetworkObject when spawning the gameplay player.");
            }
        }
        catch (Exception exception)
        {
            _localGameplayPlayerSpawned = false;
            Debug.LogError("[LobbyNetwork] Gameplay player spawn failed: " + exception);
        }
    }
    public void OnSceneLoadStart(NetworkRunner runner)
    {
        // The MasterClient initiates LoadScene, but every peer receives this callback.
        // Mark every peer as Starting so each one spawns its own Shared Mode character in OnSceneLoadDone.
        if (CurrentState != State.Starting)
        {
            SetState(State.Starting, "Loading the map...");
        }
    }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
}
