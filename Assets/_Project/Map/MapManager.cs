using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

public enum MarkerKind
{
    LocalPlayer,
    RemotePlayer,
    Enemy,
    Loot,
    Boss,
    EnemyCamp,
    LootChest,
    Waypoint,
    SafeZone,
    WaystoneLocked,
    WaystoneUnlocked
}

public struct MapMarker
{
    public Vector3 position;
    public MarkerKind kind;
    public string label;
}

/// <summary>
/// Owns the top-down map camera, gathers world marker data (Players, Enemies, Boss, Camps, Loot Chests, Waypoint),
/// and manages the Waypoint coordinate &amp; distance backend.
/// </summary>
public class MapManager : MonoBehaviour
{
    public static MapManager Instance { get; private set; }

    [Header("Camera")]
    [SerializeField] private int textureSize = 1024;
    [SerializeField] private float cameraHeight = 165f;
    [SerializeField] private float refreshInterval = 0.1f;
    [SerializeField] private Color backgroundColor = new Color(0.07f, 0.09f, 0.11f, 1f);

    [Header("Ranges (ขอบเขตแผนที่โลกขยาย 220x220 เมตร)")]
    [Min(5f)] [SerializeField] private float minimapRadius = 38f;
    [Min(10f)] [SerializeField] private float worldMapRadius = 112f;
    [SerializeField] private Vector3 worldMapCenter = new Vector3(0f, 0f, 6f);

    [Header("Waypoint")]
    [SerializeField] private float waypointAutoClearRadius = 2.5f;

    // รองรับการซูมละเอียดบนแผนที่ขนาดใหญ่ (FinalTestv1 3600x3600m) ตั้งแต่ 0.08x จนถึง 2.40x
    public const float MinWorldZoom = 0.08f;
    public const float MaxWorldZoom = 2.40f;

    public bool IsWorldMapOpen { get; private set; }
    public RenderTexture MapTexture => _texture;
    // รัศมีแผนที่โลกจริงคำนวณร่วมกับระดับการซูม (WorldMapZoom)
    public float CurrentRadius => IsWorldMapOpen ? worldMapRadius * WorldMapZoom : minimapRadius;
    public float MinimapRadius => minimapRadius;
    public float WorldMapRadius => worldMapRadius;
    public float WorldMapZoom { get; private set; } = 1.0f;
    public Vector2 WorldMapPanOffset { get; private set; }

    // ---- Waypoint Backend ----
    public bool HasActiveWaypoint { get; private set; }
    public Vector3 ActiveWaypointPosition { get; private set; }
    public event Action<bool, Vector3> WaypointChanged;

    private Camera _cam;
    private RenderTexture _texture;
    private float _nextRender;
    private Vector3 _focus;
    private GameObject _worldWaypointBeacon;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[MapManager] A second MapManager was found - destroying this one.");
            Destroy(gameObject);
            return;
        }
        Instance = this;

        _texture = new RenderTexture(textureSize, textureSize, 16, RenderTextureFormat.ARGB32) { name = "MinimapRT" };
        _texture.Create();

        var camGo = new GameObject("MinimapCamera");
        camGo.transform.SetParent(transform, false);
        _cam = camGo.AddComponent<Camera>();
        _cam.orthographic = true;
        _cam.orthographicSize = minimapRadius;
        _cam.clearFlags = CameraClearFlags.SolidColor;
        _cam.backgroundColor = backgroundColor;
        _cam.nearClipPlane = 1f;
        // ขยายระยะ farClipPlane ให้มองเห็นภูมิประเทศที่มีความสูงต่ำทั่วแผนที่ FinalTestv1 โดยไม่ถูกตัดขาด
        _cam.farClipPlane = cameraHeight + 600f;
        _cam.depth = -50f;
        _cam.targetTexture = _texture;
        _cam.allowHDR = false;
        _cam.allowMSAA = false;
        _cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        var urp = _cam.GetUniversalAdditionalCameraData();
        if (urp != null)
        {
            urp.renderShadows = false;
            urp.renderPostProcessing = false;
            urp.requiresColorOption = CameraOverrideOption.Off;
            urp.requiresDepthOption = CameraOverrideOption.Off;
        }

        _cam.enabled = false;

        gameObject.AddComponent<MinimapUI>();
        Debug.Log($"[MapManager] Ready (RT {textureSize}px, minimap {minimapRadius}m, world map {worldMapRadius}m, {1f / refreshInterval:0} Hz).");
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (IsWorldMapOpen) GameplayInput.PopBlocker("MapManager");
        if (_texture != null) { _texture.Release(); Destroy(_texture); }
    }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.mKey.wasPressedThisFrame)
            {
                Debug.Log("[MapManager] InputSystem Key.M received; toggling world map.");
                if (!IsWorldMapOpen && InventoryUI.Instance != null && InventoryUI.Instance.IsOpen)
                {
                    InventoryUI.Instance.SwitchToWorldMapTab();
                }
                else
                {
                    ToggleWorldMap();
                }
            }
            else if (IsWorldMapOpen && kb.escapeKey.wasPressedThisFrame)
            {
                SetWorldMapOpen(false);
            }
        }

        if (HasActiveWaypoint && PlayerCombat.Local != null && PlayerCombat.Local.IsSpawned)
        {
            if (GetLocalPlayerDistanceToWaypoint() <= waypointAutoClearRadius)
            {
                Debug.Log("[MapManager] Reached active waypoint - clearing pin.");
                ClearWaypoint();
            }
        }
    }

    // ------------------------------------------------------------------ Waypoint API

    /// <summary>Pins a world-space waypoint coordinate on the map and displays a 3D beacon.</summary>
    public void SetWaypoint(Vector3 worldPos)
    {
        // ยิง Raycast แนบเสา Waypoint 3D ลงพื้นผิวจริงของแผนที่ (ไม่จมใต้เนินหรือลอยกลางอากาศ)
        float groundY = SandboxWorldSpawner.SampleGroundY(worldPos, worldPos.y);
        ActiveWaypointPosition = new Vector3(worldPos.x, groundY, worldPos.z);
        HasActiveWaypoint = true;
        UpdateWorldWaypointBeacon(true);
        Debug.Log($"[MapManager] Waypoint pinned at {ActiveWaypointPosition} (distance={GetLocalPlayerDistanceToWaypoint():0.0}m).");
        WaypointChanged?.Invoke(true, ActiveWaypointPosition);
    }

    /// <summary>
    /// Converts a normalized click offset inside the map view (-1..+1 from map center) into a world coordinate and pins the waypoint.
    /// </summary>
    public void SetWaypointFromNormalizedMapClick(Vector2 normalizedOffsetFromCenter)
    {
        float r = CurrentRadius;
        Vector3 worldPos = new Vector3(
            _focus.x + Mathf.Clamp(normalizedOffsetFromCenter.x, -1f, 1f) * r,
            0f,
            _focus.z + Mathf.Clamp(normalizedOffsetFromCenter.y, -1f, 1f) * r);
        SetWaypoint(worldPos);
    }

    /// <summary>Removes the active waypoint marker.</summary>
    public void ClearWaypoint()
    {
        if (!HasActiveWaypoint) return;
        HasActiveWaypoint = false;
        ActiveWaypointPosition = Vector3.zero;
        UpdateWorldWaypointBeacon(false);
        Debug.Log("[MapManager] Waypoint cleared.");
        WaypointChanged?.Invoke(false, Vector3.zero);
    }

    private void UpdateWorldWaypointBeacon(bool active)
    {
        if (!active)
        {
            if (_worldWaypointBeacon != null) _worldWaypointBeacon.SetActive(false);
            return;
        }

        if (_worldWaypointBeacon == null)
        {
            _worldWaypointBeacon = new GameObject("WaypointBeacon3D");
            var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beam.name = "Beam";
            var col = beam.GetComponent<Collider>();
            if (col != null) Destroy(col);
            beam.transform.SetParent(_worldWaypointBeacon.transform, false);
            beam.transform.localPosition = new Vector3(0f, 12f, 0f);
            beam.transform.localScale = new Vector3(0.45f, 12f, 0.45f);
            // ใช้ URP Unlit Transparent ถาวรสำหรับลำแสง Waypoint (ป้องกันการถูก Strip Shader ใน Standalone Build)
            UrpMaterialHelper.ApplyTransparentColor(beam.GetComponent<Renderer>(), new Color(0.18f, 0.96f, 0.65f, 0.35f), disableShadows: true);
        }

        _worldWaypointBeacon.transform.position = ActiveWaypointPosition;
        _worldWaypointBeacon.SetActive(true);
    }

    /// <summary>Calculates planar XZ distance in meters from any world position to the active waypoint.</summary>
    public float GetDistanceToWaypoint(Vector3 fromWorldPos)
    {
        if (!HasActiveWaypoint) return 0f;
        Vector3 a = new Vector3(fromWorldPos.x, 0f, fromWorldPos.z);
        Vector3 b = new Vector3(ActiveWaypointPosition.x, 0f, ActiveWaypointPosition.z);
        return Vector3.Distance(a, b);
    }

    /// <summary>Calculates planar XZ distance in meters from the local player to the active waypoint.</summary>
    public float GetLocalPlayerDistanceToWaypoint()
    {
        if (!HasActiveWaypoint) return 0f;
        var local = PlayerCombat.Local;
        if (local == null || !local.IsSpawned) return Vector3.Distance(new Vector3(_focus.x, 0f, _focus.z), ActiveWaypointPosition);
        return GetDistanceToWaypoint(local.transform.position);
    }

    // ------------------------------------------------------------------ Map Camera & Markers

    public void ToggleWorldMap()
    {
        SetWorldMapOpen(!IsWorldMapOpen);
    }

    public void SetWorldMapOpen(bool open)
    {
        if (open == IsWorldMapOpen) return;

        if (open && GameplayInput.IsBlocked)
        {
            Debug.LogWarning("[MapManager] Another UI is open - world map not opened.");
            return;
        }

        IsWorldMapOpen = open;
        if (IsWorldMapOpen) GameplayInput.PushBlocker("MapManager");
        else GameplayInput.PopBlocker("MapManager");

        UpdateFocus();
        if (_cam != null)
        {
            _cam.orthographicSize = CurrentRadius;
            _cam.transform.position = new Vector3(_focus.x, cameraHeight, _focus.z);
        }
        _nextRender = 0f;
        MinimapUI.Instance?.SyncWorldModeImmediate(IsWorldMapOpen);
        Debug.Log($"[MapManager] World map {(IsWorldMapOpen ? "opened" : "closed")} (radius {CurrentRadius}m).");
    }

    /// <summary>Closes the World Map and opens the Character &amp; Upgrade window cleanly without freeing GameplayInput lock.</summary>
    public void SwitchToCharacterTab()
    {
        SetWorldMapOpen(false);
        if (InventoryUI.Instance != null)
        {
            InventoryUI.Instance.SetOpen(true);
        }
    }

    /// <summary>
    /// ปรับระดับการซูมของ World Map (ค่าบวก = ซูมออกเห็นกว้างขึ้น, ค่าลบ = ซูมเข้าเห็นละเอียดขึ้น)
    /// </summary>
    public void AdjustWorldMapZoom(float delta)
    {
        SetWorldMapZoom(WorldMapZoom + delta);
    }

    /// <summary>
    /// กำหนดระดับการซูมของ World Map ให้อยู่ในช่วงที่กำหนด
    /// </summary>
    public void SetWorldMapZoom(float zoom)
    {
        WorldMapZoom = Mathf.Clamp(zoom, MinWorldZoom, MaxWorldZoom);
        _nextRender = 0f;
    }

    /// <summary>
    /// เลื่อนมุมมอง World Map ตามระยะทางในโลก (เมตร) เมื่อผู้เล่นคลิกลาก
    /// </summary>
    public void PanWorldMap(Vector2 deltaWorldXZ)
    {
        // ปรับขอบเขตการลากแผนที่ตามขนาด worldMapRadius จริง (รองรับแผนที่ขนาดใหญ่ FinalTestv1)
        float panLimit = Mathf.Max(110f, worldMapRadius);
        Vector2 next = WorldMapPanOffset + deltaWorldXZ;
        next.x = Mathf.Clamp(next.x, -panLimit, panLimit);
        next.y = Mathf.Clamp(next.y, -panLimit, panLimit);
        WorldMapPanOffset = next;
        UpdateFocus();
        _nextRender = 0f;
    }

    /// <summary>
    /// รีเซ็ตมุมมองและการซูมของ World Map กลับสู่ค่าเริ่มต้น
    /// </summary>
    public void ResetWorldMapView()
    {
        WorldMapZoom = 1.0f;
        WorldMapPanOffset = Vector2.zero;
        UpdateFocus();
        _nextRender = 0f;
    }

    private void UpdateFocus()
    {
        if (IsWorldMapOpen)
        {
            _focus = new Vector3(worldMapCenter.x + WorldMapPanOffset.x, 0f, worldMapCenter.z + WorldMapPanOffset.y);
            return;
        }
        var local = PlayerCombat.Local;
        if (local != null && local.IsSpawned) _focus = local.transform.position;
    }

    private void LateUpdate()
    {
        if (_cam == null) return;

        if (_cam.enabled) { _cam.enabled = false; return; }

        UpdateFocus();

        if (Time.unscaledTime < _nextRender) return;
        _nextRender = Time.unscaledTime + refreshInterval;

        _cam.orthographicSize = CurrentRadius;
        _cam.transform.position = new Vector3(_focus.x, cameraHeight, _focus.z);
        _cam.enabled = true;
    }

    public Vector3 Focus => _focus;

    /// <summary>Fills 'into' (cleared first) with every marker that should be visible right now.</summary>
    public void CollectMarkers(List<MapMarker> into)
    {
        into.Clear();

        for (int i = 0; i < PlayerCombat.All.Count; i++)
        {
            var p = PlayerCombat.All[i];
            if (p == null || !p.IsSpawned || p.IsDead) continue;
            into.Add(new MapMarker
            {
                position = p.transform.position,
                kind = p == PlayerCombat.Local ? MarkerKind.LocalPlayer : MarkerKind.RemotePlayer,
                label = p == PlayerCombat.Local ? "You" : "Player"
            });
        }

        if (SafeZone.Instance != null)
        {
            into.Add(new MapMarker
            {
                position = SafeZone.Instance.Center,
                kind = MarkerKind.SafeZone,
                label = "Comfort Zone (Safe)"
            });
        }

        if (SandboxWorldSpawner.Instance != null)
        {
            var camps = SandboxWorldSpawner.Instance.Camps;
            for (int i = 0; i < camps.Count; i++)
            {
                var c = camps[i];
                string levelTag = c.MinLevel == c.MaxLevel ? $"Lv.{c.MinLevel}" : $"Lv.{c.MinLevel}-{c.MaxLevel}";
                // แยกป้ายชื่อโซนตามระดับความยาก 3-Tier (Starter <= Lv.10, Frontier >= Lv.11)
                string tierShort = c.MaxLevel <= 10 ? "Starter" : "Frontier";
                string shortName = $"Camp {c.CampId} {tierShort} ({levelTag})";
                into.Add(new MapMarker
                {
                    position = c.Center,
                    kind = MarkerKind.EnemyCamp,
                    label = c.IsCleared ? $"Camp {c.CampId} ({c.GetFormattedRespawnTime()})" : shortName
                });
            }

            // แสดงหมุด Boss Arena เพียง 1 หมุด (ติดตามตำแหน่งบอสจริงเมื่อยังมีชีวิต เพื่อไม่ให้ชื่อบอสซ้อนกัน 2 บรรทัด)
            var activeBoss = SandboxWorldSpawner.Instance.ActiveBoss;
            Vector3 bossMarkerPos = (activeBoss != null && activeBoss.IsAlive)
                ? activeBoss.transform.position
                : SandboxWorldSpawner.Instance.BossArenaCenter;

            into.Add(new MapMarker
            {
                position = bossMarkerPos,
                kind = MarkerKind.Boss,
                label = SandboxWorldSpawner.Instance.IsBossRespawning
                    ? $"Boss Citadel ({SandboxWorldSpawner.Instance.GetBossRespawnCountdownText().Replace("Boss respawns in ", "")})"
                    : $"Boss Citadel (Lv.{SandboxWorldSpawner.Instance.BossArenaZoneLevel})"
            });
        }

        // แสดงแคมป์ที่ผู้เล่นวางโซน MonsterSpawnZone เองในแมป (รันเลข Camp 5, 6, 7... อัตโนมัติหรือตามเลขที่ตั้งเอง + ติดตามตำแหน่งบอสจริง)
        for (int i = 0; i < MonsterSpawnZone.All.Count; i++)
        {
            var z = MonsterSpawnZone.All[i];
            if (z == null || !z.isActiveAndEnabled || !z.ShowOnWorldMap) continue;
            into.Add(new MapMarker
            {
                position = z.GetMapMarkerPosition(),
                kind = z.IsBossZone ? MarkerKind.Boss : MarkerKind.EnemyCamp,
                label = z.GetMapMarkerLabel()
            });
        }

        for (int i = 0; i < EnemyNPC.All.Count; i++)
        {
            var e = EnemyNPC.All[i];
            if (e == null || !e.IsAlive) continue;
            // บอสถูกเพิ่มเป็นหมุดหลักของ Boss Citadel แล้วด้านบน ข้ามเพื่อไม่ให้ซ้อนทับ
            if (e.IsBoss) continue;
            into.Add(new MapMarker
            {
                position = e.transform.position,
                kind = MarkerKind.Enemy,
                label = ""
            });
        }

        for (int i = 0; i < LootChest.All.Count; i++)
        {
            var ch = LootChest.All[i];
            if (ch == null || ch.IsOpened) continue;
            bool eligible = ch.IsLocalPlayerEligible();
            into.Add(new MapMarker
            {
                position = ch.transform.position,
                kind = MarkerKind.LootChest,
                label = eligible ? (ch.IsBossChest ? "Boss Chest" : "Chest") : "Locked Chest"
            });
        }

        // แสดงจุดดรอปของไอเทมส่วนตัวแบบ Client-Local เฉพาะบนแผนที่ของเจ้าของสิทธิ์
        for (int i = 0; i < LocalPersonalLoot.All.Count; i++)
        {
            var localDrop = LocalPersonalLoot.All[i];
            if (localDrop == null || localDrop.IsCollected) continue;
            into.Add(new MapMarker
            {
                position = localDrop.transform.position,
                kind = MarkerKind.Loot,
                label = localDrop.Data != null ? localDrop.Data.displayName : "Personal Loot"
            });
        }

        for (int i = 0; i < WorldItem.All.Count; i++)
        {
            var w = WorldItem.All[i];
            // แสดงจุดดรอปบนแผนที่เฉพาะไอเทมที่ผู้เล่นเครื่องนี้มีสิทธิ์เห็นและเก็บ (Personal Instanced Loot)
            if (w == null || !w.IsVisibleAndLootableByLocalPlayer()) continue;
            into.Add(new MapMarker
            {
                position = w.transform.position,
                kind = MarkerKind.Loot,
                label = w.Data != null ? w.Data.displayName : "Loot"
            });
        }

        // แสดงหมุดหินเทเลพอร์ต (Teleport Waystones) ทั้งที่ปลดล็อกแล้วและยังไม่ปลดล็อกบนแผนที่
        for (int i = 0; i < TeleportWaystone.All.Count; i++)
        {
            var ws = TeleportWaystone.All[i];
            if (ws == null || !ws.isActiveAndEnabled) continue;
            bool unlocked = ws.IsUnlocked;
            into.Add(new MapMarker
            {
                position = ws.transform.position,
                kind = unlocked ? MarkerKind.WaystoneUnlocked : MarkerKind.WaystoneLocked,
                label = unlocked ? $"{ws.DisplayName} [Warp]" : $"{ws.DisplayName} (Locked)"
            });
        }

        if (HasActiveWaypoint)
        {
            into.Add(new MapMarker
            {
                position = ActiveWaypointPosition,
                kind = MarkerKind.Waypoint,
                label = $"Waypoint ({GetLocalPlayerDistanceToWaypoint():0}m)"
            });
        }
    }
}
