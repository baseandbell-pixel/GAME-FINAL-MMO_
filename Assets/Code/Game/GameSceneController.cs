using Fusion;
using UnityEngine;

/// <summary>
/// Put one of these in the map scene. As soon as the runner is ready it spawns
/// the local player's character (shared mode: each client spawns and owns its own)
/// and points the camera at it.
/// </summary>
public class GameSceneController : MonoBehaviour
{
    [Header("Prefab")]
    [Tooltip("ใส่ Prefab ตัวละครหลัก เช่น GreatSwordGirl")]
    public NetworkObject characterPrefab;
    [Tooltip("ของเดิมเพื่อน (เผื่อฉากเก่าใช้)")]
    public PlayerCapsule playerCapsulePrefab;

    [Header("Spawn")]
    [Tooltip("Optional. One empty transform per seat; leave empty to spawn on a small circle.")]
    public Transform[] spawnPoints;
    public float fallbackSpawnRadius = 3f;

    [Header("Camera")]
    public SoulsCamera soulsCamera;
    public PlayerFollowCamera followCamera;

    NetworkRunner _runner;
    bool _spawnRequested;
    float _nextSpawnAttempt;

    void Awake()
    {
        // ถ้ากด Play ในฉากแมปโดยตรง (เช่น TestScene หรือ Map1) โดยไม่มี LobbyNetwork หรือ SandboxBootstrap
        // ให้สร้าง SandboxBootstrap อัตโนมัติเพื่อเชื่อมต่อ Photon และเสกตัวละครทันที
        if (LobbyNetwork.Instance == null &&
            FindAnyObjectByType<NetworkRunner>() == null &&
            FindAnyObjectByType<SandboxBootstrap>() == null)
        {
            gameObject.AddComponent<SandboxBootstrap>();
        }
    }

    void Update()
    {
        if (_runner == null)
        {
            _runner = LobbyNetwork.Instance != null ? LobbyNetwork.Instance.Runner : null;
            if (_runner == null) _runner = FindAnyObjectByType<NetworkRunner>();
        }

        if (_runner == null || !_runner.IsRunning)
        {
            if (SandboxBootstrap.OfflineFallbackActive && !_spawnRequested)
            {
                SpawnOfflineFallbackPlayer();
            }
            return;
        }

        if (!_spawnRequested) TrySpawnLocalPlayer();

        // ผูก SoulsCamera เข้ากับตัวละคร Local ทันทีที่ Spawn เสร็จ
        if (soulsCamera != null && soulsCamera.target == null && NetworkCharacter.Local != null)
        {
            BindSoulsCamera(NetworkCharacter.Local.transform);
        }

        // รองรับระบบเก่าของเพื่อน
        if (followCamera != null && followCamera.target == null && PlayerCapsule.Local != null)
        {
            followCamera.target = PlayerCapsule.Local.transform;
        }
    }

    void TrySpawnLocalPlayer()
    {
        if (characterPrefab == null && playerCapsulePrefab == null)
        {
            Debug.LogError("[GameSceneController] No characterPrefab or playerCapsulePrefab is assigned.");
            _spawnRequested = true;
            return;
        }

        if (Time.time < _nextSpawnAttempt) return;

        int seat = _runner.LocalPlayer.PlayerId;
        Vector3 spawnPos = GetSpawnPosition(seat);
        Quaternion spawnRot = GetSpawnRotation(seat);

        try
        {
            if (characterPrefab != null)
            {
                var spawnedObj = _runner.Spawn(characterPrefab, spawnPos, spawnRot, _runner.LocalPlayer);
                _spawnRequested = true;

                if (soulsCamera != null && spawnedObj != null)
                {
                    BindSoulsCamera(spawnedObj.transform);
                }
            }
            else
            {
                var capsule = _runner.Spawn(playerCapsulePrefab, spawnPos, spawnRot, _runner.LocalPlayer);
                _spawnRequested = true;

                if (followCamera != null && capsule != null)
                {
                    followCamera.target = capsule.transform;
                }
            }
        }
        catch (NetworkObjectSpawnException e)
        {
            _nextSpawnAttempt = Time.time + 0.25f;
            Debug.LogWarning($"[GameSceneController] Spawn not ready yet, retrying: {e.Message}");
        }
    }

    void BindSoulsCamera(Transform targetTransform)
    {
        if (soulsCamera == null || targetTransform == null) return;

        soulsCamera.target = targetTransform;
        var lockSys = targetTransform.GetComponent<LockOnSystem>();
        if (lockSys != null)
        {
            soulsCamera.lockSys = lockSys;
        }
    }

    void SpawnOfflineFallbackPlayer()
    {
        _spawnRequested = true;
        Vector3 spawnPos = GetSpawnPosition(0);
        Quaternion spawnRot = GetSpawnRotation(0);

        if (characterPrefab != null)
        {
            var spawnedObj = Instantiate(characterPrefab.gameObject, spawnPos, spawnRot);
            if (soulsCamera == null) soulsCamera = FindAnyObjectByType<SoulsCamera>();
            if (soulsCamera != null) BindSoulsCamera(spawnedObj.transform);
            Debug.Log("[GameSceneController] Spawned characterPrefab in Offline Fallback mode.");
        }
        else if (playerCapsulePrefab != null)
        {
            var capsule = Instantiate(playerCapsulePrefab, spawnPos, spawnRot);
            if (followCamera == null) followCamera = FindAnyObjectByType<PlayerFollowCamera>();
            if (followCamera != null) followCamera.target = capsule.transform;
            Debug.Log("[GameSceneController] Spawned playerCapsulePrefab in Offline Fallback mode.");
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

    Quaternion GetSpawnRotation(int seat)
    {
        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            var t = spawnPoints[Mathf.Abs(seat) % spawnPoints.Length];
            if (t != null) return t.rotation;
        }
        return Quaternion.identity;
    }
}