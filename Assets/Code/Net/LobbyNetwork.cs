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

    /// <summary>Raised when the room gets a new "first player".</summary>
    public event Action<PlayerRef, bool> OnMasterClientChanged;

    // [เพิ่มใหม่] Event แจ้งเตือน UI เมื่อรายการห้องใน Lobby มีการอัปเดต
    public event Action<List<SessionInfo>> OnRoomListUpdated;

    // [เพิ่มใหม่] เก็บรายการห้องล่าสุดเผื่อ UI เปิดทีหลัง
    public List<SessionInfo> CachedSessionList { get; private set; } = new List<SessionInfo>();

    public State CurrentState { get; private set; } = State.Idle;
    public NetworkRunner Runner { get; private set; }
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

        SetState(State.ConnectedToPhoton, "Connected to Lobby.");
        return true;
    }

    // [เพิ่มใหม่] สร้างห้องแบบกำหนดชื่อเอง
    public async Task<bool> CreateRoomAsync(string roomName)
    {
        if (string.IsNullOrWhiteSpace(roomName))
        {
            roomName = $"{GoogleAPI.instance.username}'s Room";
        }

        return await StartOrJoinSessionAsync(roomName.Trim(), createIfNotExists: true);
    }

    // [เพิ่มใหม่] กดเข้าร่วมห้องจาก Room List
    public async Task<bool> JoinRoomAsync(string roomName)
    {
        if (string.IsNullOrWhiteSpace(roomName)) return false;
        return await StartOrJoinSessionAsync(roomName.Trim(), createIfNotExists: false);
    }

    // [อัปเดต] รวม Logic การเข้าห้องไว้ที่เดียว (ยังเก็บ QuickJoinAsync เดิมไว้ให้เรียกใช้ได้)
    public async Task<bool> QuickJoinAsync()
    {
        return await StartOrJoinSessionAsync(null, createIfNotExists: true);
    }

    private async Task<bool> StartOrJoinSessionAsync(string sessionName, bool createIfNotExists)
    {
        if (CurrentState == State.Idle || CurrentState == State.Failed)
        {
            if (!await ConnectToPhotonAsync()) return false;
        }
        if (CurrentState == State.Joining || CurrentState == State.InLobby) return false;

        SetState(State.Joining, string.IsNullOrEmpty(sessionName) ? "Looking for a room..." : $"Joining {sessionName}...");

        var args = new StartGameArgs
        {
            GameMode = GameMode.Shared,
            SessionName = sessionName, // ถ้าเป็น null จะสุ่ม, ถ้ามีชื่อจะเข้า/สร้างตามชื่อนั้น
            PlayerCount = MaxPlayers,
            EnableClientSessionCreation = createIfNotExists,
            SceneManager = GetComponent<INetworkSceneManager>() ?? gameObject.AddComponent<NetworkSceneManagerDefault>(),
            ObjectProvider = GetComponent<INetworkObjectProvider>() ?? gameObject.AddComponent<NetworkObjectProviderDefault>(),
        };

        // ถ้าเป็นการ Quick Join (sessionName == null) ให้ใช้ FillRoom
        if (string.IsNullOrEmpty(sessionName))
        {
            args.MatchmakingMode = Photon.Realtime.MatchmakingMode.FillRoom;
        }

        var result = await Runner.StartGame(args);

        if (!result.Ok)
        {
            // หากเข้าห้องไม่สำเร็จ ให้กลับไปสถานะ ConnectedToPhoton เพื่อเลือกห้องใหม่ได้
            SetState(State.ConnectedToPhoton, $"Failed: {result.ShutdownReason}");
            return false;
        }

        SpawnLocalPlayer();
        SetState(State.InLobby, $"Joined room: {Runner.SessionInfo.Name}");
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

        // [อัปเดต] หลังจากออกจากห้องใน Fusion Runner จะ Shutdown ตัวเอง
        // เราสั่งเชื่อมต่อเข้า SessionLobby ใหม่อัตโนมัติ เพื่อให้กลับมาเห็น Room List ได้ทันที
        SetState(State.Idle, "Left the room.");
        await ConnectToPhotonAsync();
    }

    // [อัปเดต] รับรายการห้องจาก Photon แล้วส่งให้ UI
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
    {
        CachedSessionList = sessionList;
        OnRoomListUpdated?.Invoke(sessionList);
    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player) { }
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        PollMasterClient();
    }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        LobbyPlayer.All.Clear();
        MasterClient = PlayerRef.None;
        _lastMaster = PlayerRef.None;
        CachedSessionList.Clear();
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