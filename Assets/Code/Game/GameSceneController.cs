
using Fusion;
using UnityEngine;

/// <summary>
/// Spawns the local network player in the gameplay scene.
/// Supports any registered Fusion NetworkObject prefab,
/// including a Mixamo character with Animator and CharacterController.
/// </summary>
public class GameSceneController : MonoBehaviour
{
    [Header("Player Prefab")]
    [Tooltip("Assign the new character prefab containing a NetworkObject.")]
    public NetworkObject playerPrefab;

    [Header("Spawn Settings")]
    [Tooltip("Optional spawn positions for players.")]
    public Transform[] spawnPoints;

    [Tooltip("Fallback spawn radius when no spawn points are assigned.")]
    public float fallbackSpawnRadius = 3f;

    [Header("Camera")]
    [Tooltip("Assign the local player's follow camera.")]
    public PlayerFollowCamera followCamera;

    private NetworkRunner runner;
    private NetworkObject localPlayerObject;

    private bool spawnRequested;
    private float nextSpawnAttempt;

    private void Update()
    {
        FindRunner();

        if (runner == null || !runner.IsRunning)
        {
            return;
        }

        if (!spawnRequested)
        {
            TrySpawnLocalPlayer();
        }

        UpdateCameraTarget();
    }

    private void FindRunner()
    {
        if (runner != null)
        {
            return;
        }

        if (LobbyNetwork.Instance != null)
        {
            runner = LobbyNetwork.Instance.Runner;
        }

        if (runner == null)
        {
            runner = FindAnyObjectByType<NetworkRunner>();
        }
    }

    private void TrySpawnLocalPlayer()
    {
        if (playerPrefab == null)
        {
            Debug.LogError(
                "[GameSceneController] Player Prefab is not assigned. " +
                "Please assign a NetworkObject prefab in the Inspector."
            );

            spawnRequested = true;
            return;
        }

        if (Time.time < nextSpawnAttempt)
        {
            return;
        }

        if (runner.LocalPlayer == PlayerRef.None)
        {
            return;
        }

        int seat = runner.LocalPlayer.PlayerId;

        try
        {
            NetworkObject spawnedPlayer = runner.Spawn(
                playerPrefab,
                GetSpawnPosition(seat),
                Quaternion.identity,
                runner.LocalPlayer
            );

            if (spawnedPlayer == null)
            {
                nextSpawnAttempt = Time.time + 0.5f;

                Debug.LogWarning(
                    "[GameSceneController] Spawn returned null. Retrying."
                );

                return;
            }

            localPlayerObject = spawnedPlayer;
            spawnRequested = true;

            Debug.Log(
                "[GameSceneController] Player spawned successfully: " +
                spawnedPlayer.name
            );

            UpdateCameraTarget();
        }
        catch (NetworkObjectSpawnException exception)
        {
            nextSpawnAttempt = Time.time + 0.5f;

            Debug.LogWarning(
                "[GameSceneController] Player prefab is not ready. " +
                "Retrying spawn: " + exception.Message
            );
        }
    }

    private void UpdateCameraTarget()
    {
        if (followCamera == null)
        {
            return;
        }

        if (followCamera.target != null)
        {
            return;
        }

        if (localPlayerObject == null)
        {
            return;
        }

        followCamera.target = localPlayerObject.transform;
    }

    private Vector3 GetSpawnPosition(int seat)
    {
        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            int index = Mathf.Abs(seat) % spawnPoints.Length;

            Transform spawnPoint = spawnPoints[index];

            if (spawnPoint != null)
            {
                return spawnPoint.position;
            }
        }

        int maxPlayers = Mathf.Max(1, LobbyNetwork.MaxPlayers);

        float angle =
            (Mathf.Abs(seat) % maxPlayers) *
            (360f / maxPlayers) *
            Mathf.Deg2Rad;

        return new Vector3(
            Mathf.Cos(angle) * fallbackSpawnRadius,
            1f,
            Mathf.Sin(angle) * fallbackSpawnRadius
        );
    }
}
