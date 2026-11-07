using Fusion;
using UnityEngine;

/// <summary>
/// Put one of these in the map scene. As soon as the runner is ready it spawns
/// the local player's capsule (shared mode: each client spawns and owns its own)
/// and points the follow camera at it.
/// </summary>
public class GameSceneController : MonoBehaviour
{
    [Header("Prefab")]
    public PlayerCapsule playerCapsulePrefab;

    [Header("Spawn")]
    [Tooltip("Optional. One empty transform per seat; leave empty to spawn on a small circle.")]
    public Transform[] spawnPoints;
    public float fallbackSpawnRadius = 3f;

    [Header("Camera")]
    public PlayerFollowCamera followCamera;

    NetworkRunner _runner;
    bool _spawnRequested;
    float _nextSpawnAttempt;

    void Update()
    {
        if (_runner == null)
        {
            _runner = LobbyNetwork.Instance != null ? LobbyNetwork.Instance.Runner : null;
            if (_runner == null) _runner = FindAnyObjectByType<NetworkRunner>();
        }

        // This controller only lives in the map scene, so by the time Update runs the
        // scene load is already done - we just need the runner to be alive.
        if (_runner == null || !_runner.IsRunning) return;

        if (!_spawnRequested) TrySpawnLocalCapsule();

        // With EnqueueIncompleteSynchronousSpawns the capsule can appear a few frames
        // after Spawn() was called, so the camera is hooked up as soon as it exists.
        if (followCamera != null && followCamera.target == null && PlayerCapsule.Local != null)
            followCamera.target = PlayerCapsule.Local.transform;
    }

    void TrySpawnLocalCapsule()
    {
        if (playerCapsulePrefab == null)
        {
            Debug.LogError("[GameSceneController] playerCapsulePrefab is not assigned.");
            _spawnRequested = true;   // stop retrying every frame
            return;
        }

        if (Time.time < _nextSpawnAttempt) return;

        int seat = _runner.LocalPlayer.PlayerId;

        try
        {
            var capsule = _runner.Spawn(playerCapsulePrefab, GetSpawnPosition(seat), Quaternion.identity, _runner.LocalPlayer);
            _spawnRequested = true;

            if (followCamera != null && capsule != null) followCamera.target = capsule.transform;
        }
        catch (NetworkObjectSpawnException e)
        {
            // The prefab source was not in memory yet - Fusion loads it in the background,
            // so simply try again shortly instead of giving up.
            _nextSpawnAttempt = Time.time + 0.25f;
            Debug.LogWarning($"[GameSceneController] Capsule spawn not ready yet, retrying: {e.Message}");
        }
    }

    Vector3 GetSpawnPosition(int seat)
    {
        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            var t = spawnPoints[Mathf.Abs(seat) % spawnPoints.Length];
            if (t != null) return t.position;
        }

        float angle = (Mathf.Abs(seat) % LobbyNetwork.MaxPlayers) * (360f / LobbyNetwork.MaxPlayers) * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(angle), 1f, Mathf.Sin(angle)) * fallbackSpawnRadius;
    }
}
