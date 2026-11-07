using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Draws the minimap / world map from MapManager's render texture plus a pooled set of marker dots.
/// Markers (north-up): white = you, cyan = other players, red = hostile NPCs, yellow diamonds = loot.
/// Created automatically by MapManager.
/// </summary>
public class MinimapUI : MonoBehaviour
{
    private const float MiniSize = 220f;
    private const float WorldSize = 640f;
    private const float Border = 4f;

    private MapManager _map;
    private RectTransform _frameRect, _viewRect;
    private RawImage _view;
    private Text _legend;
    private bool _worldMode;
    private Font _font;

    private readonly List<MapMarker> _markers = new List<MapMarker>(64);
    private readonly List<Image> _dots = new List<Image>(64);

    private void Start()
    {
        _map = MapManager.Instance;
        if (_map == null) { Debug.LogWarning("[MinimapUI] No MapManager - UI disabled."); enabled = false; return; }

        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var canvasGo = new GameObject("MapCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // frame (border)
        var frame = NewImage(canvasGo.transform, "MapFrame", new Color(0f, 0f, 0f, 0.85f));
        frame.raycastTarget = false;
        _frameRect = frame.rectTransform;

        // map view
        var viewGo = new GameObject("MapView", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage), typeof(RectMask2D));
        viewGo.transform.SetParent(frame.transform, false);
        _viewRect = (RectTransform)viewGo.transform;
        _viewRect.anchorMin = Vector2.zero;
        _viewRect.anchorMax = Vector2.one;
        _viewRect.offsetMin = new Vector2(Border, Border);
        _viewRect.offsetMax = new Vector2(-Border, -Border);
        _view = viewGo.GetComponent<RawImage>();
        _view.texture = _map.MapTexture;
        _view.raycastTarget = false;

        // "N" marker and legend
        MakeText(_viewRect, "North", "N", 16, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(0f, -2f), new Vector2(30f, 22f));
        _legend = MakeText(_viewRect, "Legend", "white: you    cyan: players    red: hostile    yellow: loot   (M / Esc to close)",
                           14, TextAnchor.LowerCenter, new Vector2(0.5f, 0f), new Vector2(0f, 4f), new Vector2(620f, 22f));
        _legend.gameObject.SetActive(false);

        ApplyMode(false);
        Debug.Log("[MinimapUI] Built.");
    }

    private void Update()
    {
        if (_map == null) return;

        if (_worldMode != _map.IsWorldMapOpen) ApplyMode(_map.IsWorldMapOpen);

        if (_view != null && _view.texture != _map.MapTexture) _view.texture = _map.MapTexture;

        _map.CollectMarkers(_markers);

        float half = _viewRect.rect.width * 0.5f;
        float scale = half / Mathf.Max(1f, _map.CurrentRadius);
        Vector3 focus = _map.Focus;
        float limit = half - 6f;

        int used = 0;
        for (int i = 0; i < _markers.Count; i++)
        {
            var m = _markers[i];
            float x = (m.position.x - focus.x) * scale;
            float y = (m.position.z - focus.z) * scale;

            bool outside = Mathf.Abs(x) > limit || Mathf.Abs(y) > limit;
            if (outside)
            {
                // Players stay visible as edge hints; everything else is hidden when out of range.
                if (m.kind != MarkerKind.RemotePlayer) continue;
                x = Mathf.Clamp(x, -limit, limit);
                y = Mathf.Clamp(y, -limit, limit);
            }

            var dot = GetDot(used++);
            Style(dot, m.kind);
            dot.rectTransform.anchoredPosition = new Vector2(x, y);
        }

        for (int i = used; i < _dots.Count; i++)
            if (_dots[i].gameObject.activeSelf) _dots[i].gameObject.SetActive(false);
    }

    private void ApplyMode(bool world)
    {
        _worldMode = world;
        float size = (world ? WorldSize : MiniSize) + Border * 2f;

        if (world)
        {
            _frameRect.anchorMin = _frameRect.anchorMax = _frameRect.pivot = new Vector2(0.5f, 0.5f);
            _frameRect.anchoredPosition = Vector2.zero;
        }
        else
        {
            _frameRect.anchorMin = _frameRect.anchorMax = _frameRect.pivot = new Vector2(1f, 1f);
            _frameRect.anchoredPosition = new Vector2(-20f, -20f);
        }
        _frameRect.sizeDelta = new Vector2(size, size);
        if (_legend != null) _legend.gameObject.SetActive(world);
    }

    // ------------------------------------------------------------------ pooled dots

    private Image GetDot(int index)
    {
        while (_dots.Count <= index)
        {
            var img = NewImage(_viewRect, "Dot" + _dots.Count, Color.white);
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            _dots.Add(img);
        }

        var dot = _dots[index];
        if (!dot.gameObject.activeSelf) dot.gameObject.SetActive(true);
        return dot;
    }

    private static void Style(Image dot, MarkerKind kind)
    {
        var rt = dot.rectTransform;
        switch (kind)
        {
            case MarkerKind.LocalPlayer:
                dot.color = Color.white; rt.sizeDelta = new Vector2(12f, 12f); rt.localRotation = Quaternion.identity; break;
            case MarkerKind.RemotePlayer:
                dot.color = new Color(0.2f, 0.9f, 1f); rt.sizeDelta = new Vector2(10f, 10f); rt.localRotation = Quaternion.identity; break;
            case MarkerKind.Enemy:
                dot.color = new Color(1f, 0.15f, 0.15f); rt.sizeDelta = new Vector2(9f, 9f); rt.localRotation = Quaternion.identity; break;
            default:
                dot.color = new Color(1f, 0.85f, 0.15f); rt.sizeDelta = new Vector2(8f, 8f); rt.localRotation = Quaternion.Euler(0f, 0f, 45f); break;
        }
    }

    // ------------------------------------------------------------------ helpers

    private static Image NewImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        return img;
    }

    private Text MakeText(Transform parent, string name, string text, int size, TextAnchor align, Vector2 anchor, Vector2 pos, Vector2 dim)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = dim;
        var t = go.GetComponent<Text>();
        t.font = _font;
        t.fontSize = size;
        t.alignment = align;
        t.color = Color.white;
        t.text = text;
        t.raycastTarget = false;
        return t;
    }
}
