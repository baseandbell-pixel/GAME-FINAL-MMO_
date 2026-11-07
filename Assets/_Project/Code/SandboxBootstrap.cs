using System;
using System.Threading.Tasks;
using Fusion;
using Fusion.Photon.Realtime;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Dev-only entry point for sandbox scenes (e.g. Dev_Kulpat).
///
/// Pressing Play directly in a sandbox scene skips the Lobby, so no NetworkRunner exists and
/// GameSceneController would wait forever. This starts a Shared Mode session in the currently
/// loaded scene. If a runner is already running (you came through the Lobby), it does nothing.
///
/// Every editor/build instance that opens the sandbox joins the same session name, so two
/// instances are enough to test PvP combat.
///
/// RACE GUARD: GameSceneController spawns as soon as runner.IsRunning is true. In the Lobby flow
/// the runner is long-running, but here the runner is created INSIDE the scene, and IsRunning flips
/// before the simulation / scene registration is ready. Spawning in that window throws inside Fusion
/// and leaves an orphaned, half-initialised capsule in the scene. So in standalone mode the
/// controller is held disabled until the session has started and the scene manager is idle.
/// </summary>
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public class SandboxBootstrap : MonoBehaviour
{
    private const int StableFramesRequired = 5;
    private const float ReadyTimeoutSeconds = 20f;

    [Tooltip("All instances using the same name end up in the same room.")]
    [SerializeField] private string sessionName = "Dev_Kulpat";

    [Min(1)]
    [SerializeField] private int maxPlayers = 4;

    private NetworkRunner _runner;
    private GameSceneController _controller;
    private bool _starting;
    private bool _destroyed;

    private void Awake()
    {
        // Standalone path only: hold the spawner back until the session is fully ready.
        if (!HasRunningRunner())
        {
            _controller = FindFirstObjectByType<GameSceneController>();
            if (_controller != null) _controller.enabled = false;
        }
    }

    private async void Start()
    {
        if (_starting) return;

        if (HasRunningRunner())
        {
            Debug.Log("[SandboxBootstrap] A runner is already running - using it.");
            ReleaseController();
            return;
        }

        if (string.IsNullOrEmpty(PhotonAppSettings.Global.AppSettings.AppIdFusion))
        {
            Debug.LogError("[SandboxBootstrap] Photon Fusion App ID is missing (Tools > Fusion > Fusion Hub).");
            return;
        }

        int buildIndex = SceneManager.GetActiveScene().buildIndex;
        if (buildIndex < 0)
        {
            Debug.LogError($"[SandboxBootstrap] Scene '{SceneManager.GetActiveScene().name}' is not in Build Settings.");
            return;
        }

        _starting = true;
        try
        {
            var runnerObject = new GameObject("SandboxNetworkRunner");
            _runner = runnerObject.AddComponent<NetworkRunner>();
            _runner.ProvideInput = false;           // combat/movement are owner-driven, not tick-input driven

            var sceneInfo = new NetworkSceneInfo();
            sceneInfo.AddSceneRef(SceneRef.FromIndex(buildIndex));

            var result = await _runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Shared,
                SessionName = sessionName,
                PlayerCount = maxPlayers,
                Scene = sceneInfo,
            });

            if (_destroyed) return;

            if (!result.Ok)
            {
                Debug.LogError($"[SandboxBootstrap] Start failed: {result.ShutdownReason}");
                return;
            }

            await WaitUntilSceneSettledAsync();
            if (_destroyed) return;

            Debug.Log($"[SandboxBootstrap] Joined Shared session '{sessionName}'. Releasing spawner.");
            ReleaseController();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
        finally
        {
            _starting = false;
        }
    }

    /// <summary>Waits until Fusion's scene manager has been idle for several consecutive frames.</summary>
    private async Task WaitUntilSceneSettledAsync()
    {
        float deadline = Time.realtimeSinceStartup + ReadyTimeoutSeconds;
        int stable = 0;

        while (!_destroyed && stable < StableFramesRequired)
        {
            if (Time.realtimeSinceStartup > deadline)
            {
                Debug.LogWarning("[SandboxBootstrap] Timed out waiting for the scene manager to settle; continuing anyway.");
                return;
            }

            bool busy = _runner == null || !_runner.IsRunning || _runner.SceneManager == null || _runner.SceneManager.IsBusy;
            stable = busy ? 0 : stable + 1;
            await Task.Yield();
        }
    }

    private void ReleaseController()
    {
        if (_controller == null) _controller = FindFirstObjectByType<GameSceneController>();
        if (_controller != null) _controller.enabled = true;
    }

    private static bool HasRunningRunner()
    {
        var existing = LobbyNetwork.Instance != null ? LobbyNetwork.Instance.Runner : FindAnyObjectByType<NetworkRunner>();
        return existing != null && existing.IsRunning;
    }

    private void OnDestroy()
    {
        _destroyed = true;
        if (_runner != null && _runner.IsRunning) _ = _runner.Shutdown();
    }
}
