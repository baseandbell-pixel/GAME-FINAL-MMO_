using System.Collections.Generic;
using UnityEngine;

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
/// MapManager - Disabled/Removed per UI &amp; Map cleanup requirements.
/// Methods and properties are kept as safe no-op stubs to prevent compile or null-ref errors.
/// </summary>
public class MapManager : MonoBehaviour
{
    public static MapManager Instance { get; private set; }

    public bool IsWorldMapOpen => false;
    public RenderTexture MapTexture => null;
    public float CurrentRadius => 0f;
    public Vector3 Focus => Vector3.zero;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        enabled = false;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void ToggleWorldMap() { }
    public void SetWorldMapOpen(bool open) { }
    public void SwitchToCharacterTab() { }
    public void CollectMarkers(List<MapMarker> into) { into?.Clear(); }
}
