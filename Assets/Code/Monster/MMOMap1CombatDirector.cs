using UnityEngine;

/// <summary>
/// Ensures that Map1 is 100% playable whether started directly in the Editor (Standalone)
/// or loaded via Photon Fusion from Lobby.unity.
/// - Attaches MMOPlayerCombat to the local player automatically.
/// - Spawns the standalone Player prefab if no player exists in the scene.
/// </summary>
[DisallowMultipleComponent]
public sealed class MMOMap1CombatDirector : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Player prefab to spawn when testing Map1 directly in the Editor without going through Lobby.")]
    public GameObject playerPrefab;
    public Transform playerSpawnPoint;

    private GameObject _standalonePlayerInstance;

    private void Start()
    {
        EnsureLocalPlayerReady();
    }

    private void Update()
    {
        // Keep checking in case Photon Fusion spawns the networked player a few frames after scene load.
        MMOPlayerController[] players = FindObjectsByType<MMOPlayerController>();
        for (int i = 0; i < players.Length; i++)
        {
            MMOPlayerController pc = players[i];
            if (pc != null && pc.GetComponent<MMOPlayerCombat>() == null)
            {
                pc.gameObject.AddComponent<MMOPlayerCombat>();
            }
        }
    }

    private void EnsureLocalPlayerReady()
    {
        // If Photon Fusion LobbyNetwork is actively running, let it spawn the networked player.
        bool fusionRunning =
            LobbyNetwork.Instance != null &&
            LobbyNetwork.Instance.Runner != null &&
            LobbyNetwork.Instance.Runner.IsRunning;

        if (fusionRunning)
        {
            return;
        }

        MMOPlayerController existingPlayer = FindAnyObjectByType<MMOPlayerController>();
        if (existingPlayer != null)
        {
            if (existingPlayer.GetComponent<MMOPlayerCombat>() == null)
            {
                existingPlayer.gameObject.AddComponent<MMOPlayerCombat>();
            }
            return;
        }

        if (playerPrefab != null)
        {
            Vector3 spawnPos = playerSpawnPoint != null ? playerSpawnPoint.position : new Vector3(0f, 0.1f, 0f);
            Quaternion spawnRot = playerSpawnPoint != null ? playerSpawnPoint.rotation : Quaternion.identity;

            _standalonePlayerInstance = Instantiate(playerPrefab, spawnPos, spawnRot);
            _standalonePlayerInstance.name = "Player_Local";

            if (_standalonePlayerInstance.GetComponent<MMOPlayerCombat>() == null)
            {
                _standalonePlayerInstance.AddComponent<MMOPlayerCombat>();
            }
        }
    }
}
