using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Phase 2 Upgraded Minimap &amp; Interactive World Map Hub:
///   - Compact Top-Left Radar Minimap (avoids collision with Top-Right Player Status HUD).
///   - Full World Map window featuring Top Navigation Tabs ([ World Map ] | [ Character &amp; Upgrade ]),
///     clear icons &amp; labels for Player, Enemy Camps, Boss Arena, Loot Chests, and Waypoint,
///     plus Click-to-Pin Waypoint (Left-Click = pin, Right-Click = clear).
/// </summary>
public class MinimapUI : MonoBehaviour, IPointerClickHandler
{
    public static MinimapUI Instance { get; private set; }

    private const float MiniSize = 196f;
    private const float WorldWidth = 640f;
    private const float WorldMapAreaSize = 620f;
    private const float HeaderHeight = 46f;
    private const float FooterHeight = 68f;
    private const float Border = 4f;

    public bool IsWorldMode => _worldMode;

    private MapManager _map;
    private RectTransform _frameRect, _innerRect, _viewRect;
    private GameObject _topNavBar;
    private GameObject _footerBar;
    private GameObject _miniFooter;
    private RawImage _view;
    private Text _legendText;
    private Text _waypointText;
    private Text _miniFooterText;
    private Button _clearPinBtn;
    private bool _worldMode;
    private Font _font;

    private readonly List<MapMarker> _markers = new List<MapMarker>(96);
    private readonly List<MarkerWidget> _pool = new List<MarkerWidget>(96);

    private class MarkerWidget
    {
        public GameObject root;
        public RectTransform rect;
        public Image icon;
        public Text label;
    }

    private void Start()
    {
        Instance = this;
        _map = MapManager.Instance;
        if (_map == null) { Debug.LogWarning("[MinimapUI] No MapManager - UI disabled."); enabled = false; return; }

        EnsureEventSystem();
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var canvasGo = new GameObject("MapCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 45;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        // Outer border frame
        var frame = NewImage(canvasGo.transform, "MapFrame", new Color(0.55f, 0.45f, 0.22f, 0.94f));
        frame.raycastTarget = false;
        _frameRect = frame.rectTransform;

        // Inner obsidian backing
        var inner = NewImage(frame.transform, "MapInner", new Color(0.05f, 0.06f, 0.09f, 0.96f));
        inner.raycastTarget = false;
        _innerRect = inner.rectTransform;
        _innerRect.anchorMin = Vector2.zero;
        _innerRect.anchorMax = Vector2.one;
        _innerRect.offsetMin = new Vector2(Border, Border);
        _innerRect.offsetMax = new Vector2(-Border, -Border);

        // ============================================================
        // TOP NAVIGATION TAB BAR (Visible in World Map mode)
        // ============================================================
        _topNavBar = new GameObject("TopNavigationTabs", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _topNavBar.transform.SetParent(_innerRect, false);
        _topNavBar.GetComponent<Image>().color = new Color(0.08f, 0.09f, 0.13f, 1f);
        var navRt = (RectTransform)_topNavBar.transform;
        navRt.anchorMin = new Vector2(0f, 1f);
        navRt.anchorMax = new Vector2(1f, 1f);
        navRt.pivot = new Vector2(0.5f, 1f);
        navRt.anchoredPosition = Vector2.zero;
        navRt.sizeDelta = new Vector2(0f, HeaderHeight);

        MakeTabButton(_topNavBar.transform, "TabWorldMap", "[ World Map ]", 12f, 180f, true, () => { });
        MakeTabButton(_topNavBar.transform, "TabCharacter", "[ Character & Upgrade ]", 200f, 220f, false, () =>
        {
            if (_map != null) _map.SwitchToCharacterTab();
        });
        MakeTabButton(_topNavBar.transform, "CloseMapBtn", "Close [M / Esc]", 496f, 132f, false, () =>
        {
            if (_map != null) _map.SetWorldMapOpen(false);
        });

        // ============================================================
        // INTERACTIVE MAP VIEWPORT
        // ============================================================
        var viewGo = new GameObject("MapView", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage), typeof(RectMask2D));
        viewGo.transform.SetParent(_innerRect, false);
        _viewRect = (RectTransform)viewGo.transform;
        _view = viewGo.GetComponent<RawImage>();
        _view.texture = _map.MapTexture;
        _view.raycastTarget = false;

        // Forward pointer clicks on MapView to this MinimapUI
        var relay = viewGo.AddComponent<MapClickRelay>();
        relay.owner = this;

        // Compass N
        MakeText(_viewRect, "North", "N", 15, FontStyle.Bold, TextAnchor.UpperCenter,
            new Color(1f, 0.84f, 0.25f, 0.95f), new Vector2(0.5f, 1f), new Vector2(0f, -4f), new Vector2(30f, 22f));

        // ============================================================
        // COMPACT MINIMAP FOOTER (Waypoint distance / hotkey hint)
        // ============================================================
        _miniFooter = new GameObject("MiniFooter", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _miniFooter.transform.SetParent(_innerRect, false);
        _miniFooter.GetComponent<Image>().color = new Color(0.06f, 0.08f, 0.11f, 0.92f);
        var mfrt = (RectTransform)_miniFooter.transform;
        mfrt.anchorMin = new Vector2(0f, 0f);
        mfrt.anchorMax = new Vector2(1f, 0f);
        mfrt.pivot = new Vector2(0.5f, 0f);
        mfrt.anchoredPosition = Vector2.zero;
        mfrt.sizeDelta = new Vector2(0f, 24f);
        _miniFooterText = MakeText(_miniFooter.transform, "MiniLabel", "[M] World Map", 11, FontStyle.Bold,
            TextAnchor.MiddleCenter, new Color(0.75f, 0.82f, 0.90f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(MiniSize - 8f, 22f));

        // ============================================================
        // WORLD MAP BOTTOM BAR (Waypoint Controls & Icon Legend)
        // ============================================================
        _footerBar = new GameObject("WorldFooter", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _footerBar.transform.SetParent(_innerRect, false);
        _footerBar.GetComponent<Image>().color = new Color(0.07f, 0.08f, 0.12f, 1f);
        var frt = (RectTransform)_footerBar.transform;
        frt.anchorMin = new Vector2(0f, 0f);
        frt.anchorMax = new Vector2(1f, 0f);
        frt.pivot = new Vector2(0.5f, 0f);
        frt.anchoredPosition = Vector2.zero;
        frt.sizeDelta = new Vector2(0f, FooterHeight);

        _waypointText = MakeText(_footerBar.transform, "WaypointInfo",
            "Left-Click anywhere on the map to pin a Waypoint  |  Right-Click to clear",
            13, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.30f, 0.95f, 0.72f, 1f),
            new Vector2(0f, 1f), new Vector2(14f, -18f), new Vector2(520f, 24f));

        var clearGo = MakeTabButton(_footerBar.transform, "ClearPinBtn", "Clear Pin", 516f, 110f, false, () =>
        {
            if (_map != null) _map.ClearWaypoint();
        });
        ((RectTransform)clearGo.transform).anchoredPosition = new Vector2(516f, -6f);
        ((RectTransform)clearGo.transform).sizeDelta = new Vector2(110f, 26f);
        _clearPinBtn = clearGo.GetComponent<Button>();

        _legendText = MakeText(_footerBar.transform, "Legend",
            "White: You  |  Orange: Camps  |  Crown: Boss  |  Gold: Chests  |  Mint: Pin",
            12, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.82f, 0.85f, 0.90f, 1f),
            new Vector2(0f, 0f), new Vector2(14f, 16f), new Vector2(612f, 22f));

        ApplyMode(false);
        Debug.Log("[MinimapUI] Phase 2 Upgraded Map Hub built.");
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null) return;
        var es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        var module = es.AddComponent<InputSystemUIInputModule>();
        module.AssignDefaultActions();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!_worldMode || _map == null || _viewRect == null) return;

        if (eventData.button == PointerEventData.InputButton.Right)
        {
            _map.ClearWaypoint();
            return;
        }

        if (eventData.button == PointerEventData.InputButton.Left)
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_viewRect, eventData.position, eventData.pressEventCamera, out Vector2 localPt))
            {
                float halfW = Mathf.Max(1f, _viewRect.rect.width * 0.5f);
                float halfH = Mathf.Max(1f, _viewRect.rect.height * 0.5f);
                Vector2 norm = new Vector2(localPt.x / halfW, localPt.y / halfH);
                _map.SetWaypointFromNormalizedMapClick(norm);
            }
        }
    }

    private void Update()
    {
        if (_map == null) return;

        if (_worldMode != _map.IsWorldMapOpen) ApplyMode(_map.IsWorldMapOpen);
        if (_view != null && _view.texture != _map.MapTexture) _view.texture = _map.MapTexture;

        // Update Waypoint labels
        if (_map.HasActiveWaypoint)
        {
            float dist = _map.GetLocalPlayerDistanceToWaypoint();
            if (_miniFooterText != null)
            {
                _miniFooterText.text = $"PIN: {dist:0}m  |  [M] Map";
                _miniFooterText.color = new Color(0.25f, 0.96f, 0.68f, 1f);
            }
            if (_waypointText != null)
            {
                _waypointText.text = $"Waypoint Pinned: ({_map.ActiveWaypointPosition.x:0}, {_map.ActiveWaypointPosition.z:0}) — {dist:0.0}m away  (Right-Click to Clear)";
                _waypointText.color = new Color(0.25f, 0.96f, 0.68f, 1f);
            }
            if (_clearPinBtn != null) _clearPinBtn.interactable = true;
        }
        else
        {
            if (_miniFooterText != null)
            {
                _miniFooterText.text = "[M] World Map";
                _miniFooterText.color = new Color(0.75f, 0.82f, 0.90f, 1f);
            }
            if (_waypointText != null)
            {
                _waypointText.text = "Left-Click anywhere on the map to pin a Waypoint  |  Right-Click to clear";
                _waypointText.color = new Color(0.76f, 0.82f, 0.90f, 1f);
            }
            if (_clearPinBtn != null) _clearPinBtn.interactable = false;
        }

        _map.CollectMarkers(_markers);

        float halfW = _viewRect.rect.width * 0.5f;
        float halfH = _viewRect.rect.height * 0.5f;
        float radius = Mathf.Max(1f, _map.CurrentRadius);
        float scale = Mathf.Min(halfW, halfH) / radius;
        Vector3 focus = _map.Focus;
        float limitX = halfW - 10f;
        float limitY = halfH - 10f;

        int used = 0;
        for (int i = 0; i < _markers.Count; i++)
        {
            var m = _markers[i];
            float x = (m.position.x - focus.x) * scale;
            float y = (m.position.z - focus.z) * scale;

            bool outside = Mathf.Abs(x) > limitX || Mathf.Abs(y) > limitY;
            if (outside)
            {
                // Clamp Waypoint, Boss, Camps, and Remote Players to edge so players can navigate towards them
                if (m.kind == MarkerKind.Enemy || m.kind == MarkerKind.Loot) continue;
                x = Mathf.Clamp(x, -limitX, limitX);
                y = Mathf.Clamp(y, -limitY, limitY);
            }

            var widget = GetWidget(used++);
            Style(widget, m, _worldMode);
            widget.rect.anchoredPosition = new Vector2(x, y);
        }

        for (int i = used; i < _pool.Count; i++)
        {
            if (_pool[i].root.activeSelf) _pool[i].root.SetActive(false);
        }
    }

    private void ApplyMode(bool world)
    {
        _worldMode = world;
        if (_topNavBar != null) _topNavBar.SetActive(world);
        if (_footerBar != null) _footerBar.SetActive(world);
        if (_miniFooter != null) _miniFooter.SetActive(!world);
        if (_view != null) _view.raycastTarget = world;

        if (world)
        {
            _frameRect.anchorMin = _frameRect.anchorMax = _frameRect.pivot = new Vector2(0.5f, 0.5f);
            _frameRect.anchoredPosition = Vector2.zero;
            float totalH = HeaderHeight + WorldMapAreaSize + FooterHeight + Border * 2f;
            _frameRect.sizeDelta = new Vector2(WorldWidth + Border * 2f, totalH);

            _viewRect.anchorMin = Vector2.zero;
            _viewRect.anchorMax = Vector2.one;
            _viewRect.offsetMin = new Vector2(10f, FooterHeight);
            _viewRect.offsetMax = new Vector2(-10f, -HeaderHeight);
        }
        else
        {
            // Compact Radar at Top-Left (avoids collision with Top-Right Player Status HUD)
            _frameRect.anchorMin = _frameRect.anchorMax = _frameRect.pivot = new Vector2(0f, 1f);
            _frameRect.anchoredPosition = new Vector2(18f, -18f);
            _frameRect.sizeDelta = new Vector2(MiniSize + Border * 2f, MiniSize + 24f + Border * 2f);

            _viewRect.anchorMin = Vector2.zero;
            _viewRect.anchorMax = Vector2.one;
            _viewRect.offsetMin = new Vector2(0f, 24f);
            _viewRect.offsetMax = Vector2.zero;
        }
    }

    // ------------------------------------------------------------------ pooled markers

    private MarkerWidget GetWidget(int index)
    {
        while (_pool.Count <= index)
        {
            var go = new GameObject("Marker" + _pool.Count, typeof(RectTransform));
            go.transform.SetParent(_viewRect, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);

            var imgGo = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            imgGo.transform.SetParent(go.transform, false);
            var img = imgGo.GetComponent<Image>();
            img.raycastTarget = false;
            var irt = img.rectTransform;
            irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(0.5f, 0.5f);
            irt.anchoredPosition = Vector2.zero;

            var lbl = MakeText(go.transform, "Label", "", 11, FontStyle.Bold, TextAnchor.UpperCenter,
                Color.white, new Vector2(0.5f, 0.5f), new Vector2(0f, -16f), new Vector2(120f, 20f));

            _pool.Add(new MarkerWidget { root = go, rect = rt, icon = img, label = lbl });
        }

        var w = _pool[index];
        if (!w.root.activeSelf) w.root.SetActive(true);
        return w;
    }

    private static void Style(MarkerWidget w, MapMarker m, bool worldMode)
    {
        var irt = w.icon.rectTransform;
        var lrt = w.label.rectTransform;
        irt.localRotation = Quaternion.identity;
        lrt.anchoredPosition = new Vector2(0f, -15f);
        bool showLabel = worldMode;

        switch (m.kind)
        {
            case MarkerKind.LocalPlayer:
                w.icon.sprite = HudIcons.GetSprite("player_arrow");
                w.icon.color = Color.white;
                irt.sizeDelta = worldMode ? new Vector2(18f, 18f) : new Vector2(14f, 14f);
                if (PlayerCombat.Local != null)
                {
                    float yaw = PlayerCombat.Local.transform.eulerAngles.y;
                    irt.localRotation = Quaternion.Euler(0f, 0f, -yaw);
                }
                w.label.color = Color.white;
                lrt.anchoredPosition = new Vector2(22f, 6f);
                break;

            case MarkerKind.RemotePlayer:
                w.icon.sprite = HudIcons.GetSprite("circle");
                w.icon.color = new Color(0.25f, 0.92f, 1f);
                irt.sizeDelta = new Vector2(12f, 12f);
                w.label.color = new Color(0.55f, 0.95f, 1f);
                break;

            case MarkerKind.Boss:
                w.icon.sprite = HudIcons.GetSprite("boss");
                w.icon.color = new Color(1f, 0.35f, 0.18f);
                irt.sizeDelta = worldMode ? new Vector2(24f, 24f) : new Vector2(18f, 18f);
                w.label.color = new Color(1f, 0.72f, 0.25f);
                lrt.anchoredPosition = new Vector2(0f, -16f);
                break;

            case MarkerKind.EnemyCamp:
                w.icon.sprite = HudIcons.GetSprite("camp");
                w.icon.color = new Color(1f, 0.58f, 0.15f, 0.92f);
                irt.sizeDelta = worldMode ? new Vector2(20f, 20f) : new Vector2(14f, 14f);
                w.label.color = new Color(1f, 0.78f, 0.40f);
                lrt.anchoredPosition = new Vector2(0f, -15f);
                break;

            case MarkerKind.LootChest:
                w.icon.sprite = HudIcons.GetSprite("chest");
                w.icon.color = new Color(1f, 0.86f, 0.22f);
                irt.sizeDelta = worldMode ? new Vector2(18f, 18f) : new Vector2(13f, 13f);
                w.label.color = new Color(1f, 0.90f, 0.35f);
                lrt.anchoredPosition = new Vector2(0f, 28f);
                break;

            case MarkerKind.Waypoint:
                w.icon.sprite = HudIcons.GetSprite("waypoint");
                w.icon.color = new Color(0.20f, 1.0f, 0.68f);
                irt.sizeDelta = worldMode ? new Vector2(24f, 24f) : new Vector2(18f, 18f);
                w.label.color = new Color(0.35f, 1.0f, 0.75f);
                lrt.anchoredPosition = new Vector2(0f, 28f);
                showLabel = true;
                break;

            case MarkerKind.Enemy:
                w.icon.sprite = HudIcons.GetSprite("circle");
                w.icon.color = new Color(1f, 0.18f, 0.18f);
                irt.sizeDelta = new Vector2(8f, 8f);
                showLabel = false;
                break;

            default:
                w.icon.sprite = HudIcons.GetSprite("diamond");
                w.icon.color = new Color(1f, 0.85f, 0.20f);
                irt.sizeDelta = new Vector2(8f, 8f);
                showLabel = false;
                break;
        }

        w.label.gameObject.SetActive(showLabel && !string.IsNullOrEmpty(m.label));
        if (showLabel) w.label.text = m.label;
    }

    // ------------------------------------------------------------------ UI builders

    private GameObject MakeTabButton(Transform parent, string name, string label, float x, float w, bool active, UnityEngine.Events.UnityAction onClick)
    {
        Color bg = active ? new Color(0.55f, 0.44f, 0.18f, 1f) : new Color(0.14f, 0.17f, 0.24f, 1f);
        var img = NewImage(parent, name, bg);
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -8f);
        rt.sizeDelta = new Vector2(w, 30f);

        var btn = img.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

        MakeText(img.transform, "Text", label, 13, FontStyle.Bold, TextAnchor.MiddleCenter,
            active ? new Color(1f, 0.94f, 0.72f) : Color.white, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(w - 8f, 26f));
        return img.gameObject;
    }

    private static Image NewImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        return img;
    }

    private Text MakeText(Transform parent, string name, string text, int size, FontStyle style, TextAnchor align, Color color, Vector2 anchor, Vector2 pos, Vector2 dim)
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
        t.fontStyle = style;
        t.alignment = align;
        t.color = color;
        t.text = text;
        t.raycastTarget = false;
        return t;
    }

    private class MapClickRelay : MonoBehaviour, IPointerClickHandler
    {
        public MinimapUI owner;
        public void OnPointerClick(PointerEventData eventData)
        {
            if (owner != null) owner.OnPointerClick(eventData);
        }
    }
}
