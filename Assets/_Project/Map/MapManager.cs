using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

public enum MarkerKind
{
    LocalPlayer,
    RemotePlayer,
    Enemy,
    Loot
}

public struct MapMarker
{
    public Vector3 position;
    public MarkerKind kind;
}

/// <summary>
/// Owns the top-down map camera and gathers marker data. Cost control:
///  - the camera is DISABLED and enabled for exactly one frame every refreshInterval seconds (default 10 Hz),
///    rendering into a small RenderTexture with shadows and post-processing off;
///  - markers are plain UI dots computed from the existing registries (PlayerCombat.All, EnemyNPC.All, WorldItem.All),
///    no extra physics or per-marker cameras.
/// M toggles the large world map. Add one MapManager to a scene; it builds its own camera and UI.
/// </summary>
public class MapManager : MonoBehaviour
{
    public static MapManager Instance { get; private set; }

    [Header("Camera")]
    [SerializeField] private int textureSize = 512;
    [SerializeField] private float cameraHeight = 120f;
    [SerializeField] private float refreshInterval = 0.1f;
    [SerializeField] private Color backgroundColor = new Color(0.07f, 0.09f, 0.11f, 1f);

    [Header("Ranges (world metres from the player to the map edge)")]
    [Min(5f)] [SerializeField] private float minimapRadius = 30f;
    [Min(10f)] [SerializeField] private float worldMapRadius = 80f;

    public bool IsWorldMapOpen { get; private set; }
    public RenderTexture MapTexture => _texture;
    public float CurrentRadius => IsWorldMapOpen ? worldMapRadius : minimapRadius;

    private Camera _cam;
    private RenderTexture _texture;
    private float _nextRender;
    private Vector3 _focus;

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
        _cam.farClipPlane = cameraHeight + 100f;
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

        _cam.enabled = false;      // enabled one frame at a time in LateUpdate

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
        if (kb == null) return;

        if (kb.mKey.wasPressedThisFrame)
        {
            Debug.Log("[MapManager] InputSystem Key.M received; toggling world map.");
            ToggleWorldMap();
        }
        else if (IsWorldMapOpen && kb.escapeKey.wasPressedThisFrame) ToggleWorldMap();
    }

    public void ToggleWorldMap()
    {
        if (!IsWorldMapOpen && GameplayInput.IsBlocked)
        {
            Debug.LogWarning("[MapManager] Another UI is open - world map not opened.");
            return;
        }

        IsWorldMapOpen = !IsWorldMapOpen;
        if (IsWorldMapOpen) GameplayInput.PushBlocker("MapManager");
        else GameplayInput.PopBlocker("MapManager");

        if (_cam != null) _cam.orthographicSize = CurrentRadius;
        _nextRender = 0f;                       // refresh immediately
        Debug.Log($"[MapManager] World map {(IsWorldMapOpen ? "opened" : "closed")} (radius {CurrentRadius}m).");
    }

    private void LateUpdate()
    {
        if (_cam == null) return;

        // A camera that was enabled last frame has rendered by now: switch it off again.
        if (_cam.enabled) { _cam.enabled = false; return; }

        var local = PlayerCombat.Local;
        if (local != null && local.IsSpawned) _focus = local.transform.position;

        if (Time.unscaledTime < _nextRender) return;
        _nextRender = Time.unscaledTime + refreshInterval;

        _cam.orthographicSize = CurrentRadius;
        _cam.transform.position = new Vector3(_focus.x, cameraHeight, _focus.z);
        _cam.enabled = true;
    }

    /// <summary>World position the map is centred on.</summary>
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
                kind = p == PlayerCombat.Local ? MarkerKind.LocalPlayer : MarkerKind.RemotePlayer
            });
        }

        for (int i = 0; i < EnemyNPC.All.Count; i++)
        {
            var e = EnemyNPC.All[i];
            if (e == null || !e.IsAlive) continue;
            into.Add(new MapMarker { position = e.transform.position, kind = MarkerKind.Enemy });
        }

        for (int i = 0; i < WorldItem.All.Count; i++)
        {
            var w = WorldItem.All[i];
            if (w == null || !w.IsSpawned || w.Claimed) continue;
            into.Add(new MapMarker { position = w.transform.position, kind = MarkerKind.Loot });
        }
    }
}
