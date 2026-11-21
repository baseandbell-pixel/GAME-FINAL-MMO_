using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// ระบบเรดาร์ Minimap มุมซ้ายบน และหน้าต่าง World Map ขนาดใหญ่แบบโต้ตอบได้:
///   - สไตล์ Dark Obsidian + Brushed Gold + Jade Accent เข้าชุดกับ UI ทั้งหมด
///   - World Map ขนาดใหญ่ (1080x840) รองรับการซูมเข้า-ออกด้วยลูกกลิ้งเมาส์ (Scroll Wheel Zoom) และคลิกลากเลื่อนแผนที่ (Drag Pan)
///   - จัดเรียงป้ายชื่อหมุดบนแผนที่อัตโนมัติไม่ให้ซ้อนทับกัน (Non-Overlapping Label Decluttering)
///   - คลิกซ้ายเพื่อปักหมุด Waypoint 3D และคลิกขวาเพื่อลบหมุด
/// </summary>
public class MinimapUI : MonoBehaviour
{
    public static MinimapUI Instance { get; private set; }

    private const float MiniSize = 196f;
    private const float WorldModalWidth = 1080f;
    private const float WorldMapAreaSize = 720f;
    private const float SideBarWidth = 324f;
    private const float HeaderHeight = 52f;
    private const float FooterHeight = 56f;
    private const float Border = 4f;

    public bool IsWorldMode => _worldMode;

    private MapManager _map;
    private RectTransform _frameRect, _innerRect, _viewRect;
    private GameObject _topNavBar;
    private GameObject _footerBar;
    private GameObject _sidePanel;
    private GameObject _miniFooter;
    private RawImage _view;
    private Text _waypointText;
    private Text _miniFooterText;
    private Text _zoomLabel;
    private Button _clearPinBtn;
    private bool _worldMode;
    private Font _font;

    // สถานะการลากเลื่อนแผนที่
    private bool _isDraggingMap;
    private bool _dragMovedEnough;
    private Vector2 _lastDragScreenPos;

    private readonly List<MapMarker> _markers = new List<MapMarker>(96);
    private readonly List<MarkerWidget> _pool = new List<MarkerWidget>(96);
    private readonly List<Rect> _placedLabelRects = new List<Rect>(32);
    // วงสีโปร่งแสงแสดงอาณาเขตโซนต่างๆ บน World Map (Comfort Zone, Starter Camps 1–2, Frontier Camps 3–4, Boss Citadel)
    private readonly List<Image> _zoneOverlayPool = new List<Image>(8);
    private Transform _zoneOverlayContainer;

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
        _font = UITheme.GetFont();

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

        // กรอบหลักสี Brushed Gold + เงา
        var frame = NewImage(canvasGo.transform, "MapFrame", UITheme.BrushedGold);
        UITheme.ApplySlicedStyle(frame, UITheme.BrushedGold);
        UITheme.AddShadow(frame.gameObject, 2.5f);
        frame.raycastTarget = false;
        _frameRect = frame.rectTransform;

        // พื้นหลังด้านในสี Dark Obsidian
        var inner = NewImage(frame.transform, "MapInner", UITheme.ObsidianDeep);
        UITheme.ApplySlicedStyle(inner, UITheme.ObsidianDeep);
        inner.raycastTarget = false;
        _innerRect = inner.rectTransform;
        _innerRect.anchorMin = Vector2.zero;
        _innerRect.anchorMax = Vector2.one;
        _innerRect.offsetMin = new Vector2(Border, Border);
        _innerRect.offsetMax = new Vector2(-Border, -Border);

        // ============================================================
        // แถบแท็บด้านบน (แสดงเฉพาะตอนเปิด World Map)
        // ============================================================
        _topNavBar = new GameObject("TopNavigationTabs", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _topNavBar.transform.SetParent(_innerRect, false);
        _topNavBar.GetComponent<Image>().color = UITheme.ObsidianHeader;
        var navRt = (RectTransform)_topNavBar.transform;
        navRt.anchorMin = new Vector2(0f, 1f);
        navRt.anchorMax = new Vector2(1f, 1f);
        navRt.pivot = new Vector2(0.5f, 1f);
        navRt.anchoredPosition = Vector2.zero;
        navRt.sizeDelta = new Vector2(0f, HeaderHeight);

        MakeTabButton(_topNavBar.transform, "TabWorldMap", "[ World Map ]", 14f, 154f, true, () => { });
        MakeTabButton(_topNavBar.transform, "TabCharacter", "[ Character & Bag ]", 176f, 184f, false, () =>
        {
            if (_map != null) _map.SwitchToCharacterTab();
        });
        MakeTabButton(_topNavBar.transform, "TabBlacksmith", "[ Blacksmith (+Gear) ]", 368f, 190f, false, () =>
        {
            if (_map != null) _map.SwitchToCharacterTab();
            InventoryUI.Instance?.OpenToEnhancementTab();
        });
        MakeTabButton(_topNavBar.transform, "TabCompendium", "[ Item Guide ]", 566f, 154f, false, () =>
        {
            if (_map != null) _map.SwitchToCharacterTab();
            InventoryUI.Instance?.OpenToCompendiumTab();
        });
        MakeTabButton(_topNavBar.transform, "CloseMapBtn", "Close [M / Esc]", 926f, 134f, false, () =>
        {
            if (_map != null) _map.SetWorldMapOpen(false);
        });

        // ============================================================
        // ช่องแสดงแผนที่ (รองรับ Scroll Zoom + Drag Pan + Waypoint Click)
        // ============================================================
        var viewGo = new GameObject("MapView", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage), typeof(RectMask2D));
        viewGo.transform.SetParent(_innerRect, false);
        _viewRect = (RectTransform)viewGo.transform;
        _view = viewGo.GetComponent<RawImage>();
        _view.texture = _map.MapTexture;
        _view.raycastTarget = false;

        var relay = viewGo.AddComponent<MapClickRelay>();
        relay.owner = this;

        // คอนเทนเนอร์วงสีขอบเขตโซน (วางใต้หมุดเพื่อไม่บังไอคอนผู้เล่นหรือศัตรู)
        var overlayRoot = new GameObject("ZoneOverlays", typeof(RectTransform));
        overlayRoot.transform.SetParent(_viewRect, false);
        var ort = (RectTransform)overlayRoot.transform;
        ort.anchorMin = Vector2.zero;
        ort.anchorMax = Vector2.one;
        ort.offsetMin = Vector2.zero;
        ort.offsetMax = Vector2.zero;
        _zoneOverlayContainer = overlayRoot.transform;

        MakeText(_viewRect, "North", "N", 16, FontStyle.Bold, TextAnchor.UpperCenter,
            UITheme.BrushedGoldBright, new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(36f, 24f));

        // ============================================================
        // แผงด้านขวาในโหมด World Map (ปุ่มซูม +/-, Reset View, คำอธิบายหมุด)
        // ============================================================
        _sidePanel = NewImage(_innerRect, "WorldSidePanel", UITheme.ObsidianPanel).gameObject;
        UITheme.ApplySlicedStyle(_sidePanel.GetComponent<Image>(), UITheme.ObsidianPanel);
        var srt = (RectTransform)_sidePanel.transform;
        srt.anchorMin = new Vector2(1f, 0f);
        srt.anchorMax = new Vector2(1f, 1f);
        srt.pivot = new Vector2(1f, 0.5f);
        srt.offsetMin = new Vector2(-(SideBarWidth + 12f), FooterHeight + 8f);
        srt.offsetMax = new Vector2(-12f, -(HeaderHeight + 8f));

        MakeText(_sidePanel.transform, "ControlsHeader", "MAP CONTROLS & ZOOM", 14, FontStyle.Bold, TextAnchor.UpperLeft,
            UITheme.BrushedGoldBright, new Vector2(0f, 1f), new Vector2(16f, -14f), new Vector2(290f, 24f));

        _zoomLabel = MakeText(_sidePanel.transform, "ZoomLabel", "Zoom: 1.0x", 14, FontStyle.Bold, TextAnchor.UpperLeft,
            UITheme.JadeAccent, new Vector2(0f, 1f), new Vector2(16f, -44f), new Vector2(290f, 24f));

        var zoomInBtn = MakeTabButton(_sidePanel.transform, "ZoomInBtn", "Zoom In (+)", 16f, 138f, false, () => _map?.AdjustWorldMapZoom(-0.18f));
        ((RectTransform)zoomInBtn.transform).anchoredPosition = new Vector2(16f, -74f);

        var zoomOutBtn = MakeTabButton(_sidePanel.transform, "ZoomOutBtn", "Zoom Out (-)", 164f, 138f, false, () => _map?.AdjustWorldMapZoom(0.18f));
        ((RectTransform)zoomOutBtn.transform).anchoredPosition = new Vector2(164f, -74f);

        var resetBtn = MakeTabButton(_sidePanel.transform, "ResetViewBtn", "Reset Center & Zoom", 16f, 286f, false, () => _map?.ResetWorldMapView());
        ((RectTransform)resetBtn.transform).anchoredPosition = new Vector2(16f, -112f);

        var clearGo = MakeTabButton(_sidePanel.transform, "ClearPinBtn", "Clear Waypoint Pin", 16f, 286f, false, () => _map?.ClearWaypoint());
        ((RectTransform)clearGo.transform).anchoredPosition = new Vector2(16f, -150f);
        _clearPinBtn = clearGo.GetComponent<Button>();

        MakeText(_sidePanel.transform, "UsageTips",
            "• Mouse Wheel: Zoom In / Out\n" +
            "• Left-Click & Drag: Pan Map\n" +
            "• Left-Click: Pin 3D Waypoint\n" +
            "• Right-Click: Clear Waypoint",
            13, FontStyle.Normal, TextAnchor.UpperLeft, UITheme.TextSecondary,
            new Vector2(0f, 1f), new Vector2(16f, -200f), new Vector2(290f, 96f));

        MakeText(_sidePanel.transform, "LegendHeader", "MAP LEGEND", 14, FontStyle.Bold, TextAnchor.UpperLeft,
            UITheme.BrushedGoldBright, new Vector2(0f, 1f), new Vector2(16f, -312f), new Vector2(290f, 24f));

        MakeText(_sidePanel.transform, "LegendBody",
            "White Arrow : You (Local Player)\n" +
            "Cyan Circle : Other Players\n" +
            "Emerald Shield : Comfort Safe Zone\n" +
            "Orange Flame : Enemy Camps\n" +
            "Crimson Crown : Boss Arena\n" +
            "Gold Chest : Camp / Boss Rewards\n" +
            "Gold Diamond : Your Personal Loot\n" +
            "Mint Beacon : Active Waypoint",
            13, FontStyle.Normal, TextAnchor.UpperLeft, UITheme.TextPrimary,
            new Vector2(0f, 1f), new Vector2(16f, -342f), new Vector2(290f, 230f));

        // ============================================================
        // แถบด้านล่างของ Minimap (โหมดย่อ) และ World Map (โหมดเต็ม)
        // ============================================================
        _miniFooter = new GameObject("MiniFooter", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _miniFooter.transform.SetParent(_innerRect, false);
        _miniFooter.GetComponent<Image>().color = UITheme.ObsidianHeader;
        var mfrt = (RectTransform)_miniFooter.transform;
        mfrt.anchorMin = new Vector2(0f, 0f);
        mfrt.anchorMax = new Vector2(1f, 0f);
        mfrt.pivot = new Vector2(0.5f, 0f);
        mfrt.anchoredPosition = Vector2.zero;
        mfrt.sizeDelta = new Vector2(0f, 24f);
        _miniFooterText = MakeText(_miniFooter.transform, "MiniLabel", "[M] World Map", 11, FontStyle.Bold,
            TextAnchor.MiddleCenter, UITheme.TextSecondary, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(MiniSize - 8f, 22f));

        _footerBar = new GameObject("WorldFooter", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _footerBar.transform.SetParent(_innerRect, false);
        _footerBar.GetComponent<Image>().color = UITheme.ObsidianHeader;
        var frt = (RectTransform)_footerBar.transform;
        frt.anchorMin = new Vector2(0f, 0f);
        frt.anchorMax = new Vector2(1f, 0f);
        frt.pivot = new Vector2(0.5f, 0f);
        frt.anchoredPosition = Vector2.zero;
        frt.sizeDelta = new Vector2(0f, FooterHeight);

        _waypointText = MakeText(_footerBar.transform, "WaypointInfo",
            "Left-Click on map to pin Waypoint  |  Drag to Pan  |  Scroll Wheel to Zoom",
            14, FontStyle.Bold, TextAnchor.MiddleLeft, UITheme.JadeAccent,
            new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(1020f, 30f));

        ApplyMode(false);
        Debug.Log("[MinimapUI] Upgraded Large Interactive Zoom & Pan World Map built.");
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null) return;
        var es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        var module = es.AddComponent<InputSystemUIInputModule>();
        module.AssignDefaultActions();
    }

    // ------------------------------------------------------------------ การโต้ตอบ Zoom / Pan / Pin บนแผนที่

    public void OnMapScroll(PointerEventData eventData)
    {
        if (!_worldMode || _map == null) return;
        float delta = -Mathf.Sign(eventData.scrollDelta.y) * 0.14f;
        _map.AdjustWorldMapZoom(delta);
    }

    public void OnMapBeginDrag(PointerEventData eventData)
    {
        if (!_worldMode || eventData.button != PointerEventData.InputButton.Left) return;
        _isDraggingMap = true;
        _dragMovedEnough = false;
        _lastDragScreenPos = eventData.position;
    }

    public void OnMapDrag(PointerEventData eventData)
    {
        if (!_worldMode || !_isDraggingMap || _map == null || _viewRect == null) return;

        Vector2 screenDelta = eventData.position - _lastDragScreenPos;
        _lastDragScreenPos = eventData.position;
        if (screenDelta.sqrMagnitude > 2f) _dragMovedEnough = true;

        float halfSize = Mathf.Max(1f, Mathf.Min(_viewRect.rect.width, _viewRect.rect.height) * 0.5f);
        float scale = halfSize / Mathf.Max(1f, _map.CurrentRadius);
        Vector2 worldDelta = new Vector2(-screenDelta.x / scale, -screenDelta.y / scale);
        _map.PanWorldMap(worldDelta);
    }

    public void OnMapEndDrag()
    {
        _isDraggingMap = false;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!_worldMode || _map == null || _viewRect == null) return;
        if (_dragMovedEnough)
        {
            _dragMovedEnough = false;
            return;
        }

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

        if (_zoomLabel != null && _worldMode)
        {
            float displayZoom = 1f / Mathf.Max(0.1f, _map.WorldMapZoom);
            _zoomLabel.text = $"Zoom: {displayZoom:0.00}x  (Radius {_map.CurrentRadius:0}m)";
        }

        if (_map.HasActiveWaypoint)
        {
            float dist = _map.GetLocalPlayerDistanceToWaypoint();
            if (_miniFooterText != null)
            {
                _miniFooterText.text = $"PIN: {dist:0}m  |  [M] Map";
                _miniFooterText.color = UITheme.JadeAccent;
            }
            if (_waypointText != null)
            {
                _waypointText.text = $"Waypoint Pinned: ({_map.ActiveWaypointPosition.x:0}, {_map.ActiveWaypointPosition.z:0}) — {dist:0.0}m away  (Right-Click to Clear)";
                _waypointText.color = UITheme.JadeAccent;
            }
            if (_clearPinBtn != null) _clearPinBtn.interactable = true;
        }
        else
        {
            if (_miniFooterText != null)
            {
                _miniFooterText.text = "[M] World Map";
                _miniFooterText.color = UITheme.TextSecondary;
            }
            if (_waypointText != null)
            {
                _waypointText.text = "Left-Click to Pin Waypoint  •  Drag to Pan  •  Scroll Wheel to Zoom";
                _waypointText.color = UITheme.TextSecondary;
            }
            if (_clearPinBtn != null) _clearPinBtn.interactable = false;
        }

        _map.CollectMarkers(_markers);

        float halfW = _viewRect.rect.width * 0.5f;
        float halfH = _viewRect.rect.height * 0.5f;
        float radius = Mathf.Max(1f, _map.CurrentRadius);
        float scale = Mathf.Min(halfW, halfH) / radius;
        Vector3 focus = _map.Focus;
        float limitX = halfW - 12f;
        float limitY = halfH - 12f;

        _placedLabelRects.Clear();
        UpdateWorldZoneOverlays(focus, scale);
        int used = 0;

        for (int i = 0; i < _markers.Count; i++)
        {
            var m = _markers[i];
            float x = (m.position.x - focus.x) * scale;
            float y = (m.position.z - focus.z) * scale;

            bool outside = Mathf.Abs(x) > limitX || Mathf.Abs(y) > limitY;
            if (outside)
            {
                if (m.kind == MarkerKind.Enemy || m.kind == MarkerKind.Loot) continue;
                x = Mathf.Clamp(x, -limitX, limitX);
                y = Mathf.Clamp(y, -limitY, limitY);
            }

            var widget = GetWidget(used++);
            Style(widget, m, _worldMode);
            widget.rect.anchoredPosition = new Vector2(x, y);

            // ป้องกันป้ายข้อความบนแผนที่ซ้อนทับกันในโหมด World Map
            if (_worldMode && widget.label.gameObject.activeSelf)
            {
                var lrt = widget.label.rectTransform;
                Vector2 center = new Vector2(x, y) + lrt.anchoredPosition;
                Rect candidate = new Rect(center.x - 58f, center.y - 10f, 116f, 20f);
                if (OverlapsAnyLabel(candidate))
                {
                    // ลองเลื่อนขึ้นด้านบนหนึ่งจังหวะก่อน หากยังทับอยู่ให้ซ่อนป้ายที่มีความสำคัญรองลงมา
                    Vector2 nudgedPos = lrt.anchoredPosition + new Vector2(0f, 22f);
                    Rect nudgedRect = new Rect((new Vector2(x, y) + nudgedPos).x - 58f, (new Vector2(x, y) + nudgedPos).y - 10f, 116f, 20f);
                    if (!OverlapsAnyLabel(nudgedRect))
                    {
                        lrt.anchoredPosition = nudgedPos;
                        _placedLabelRects.Add(nudgedRect);
                    }
                    else if (m.kind != MarkerKind.LocalPlayer && m.kind != MarkerKind.Boss && m.kind != MarkerKind.Waypoint)
                    {
                        widget.label.gameObject.SetActive(false);
                    }
                    else
                    {
                        _placedLabelRects.Add(candidate);
                    }
                }
                else
                {
                    _placedLabelRects.Add(candidate);
                }
            }
        }

        for (int i = used; i < _pool.Count; i++)
        {
            if (_pool[i].root.activeSelf) _pool[i].root.SetActive(false);
        }
    }

    /// <summary>
    /// วาดวงโปร่งแสงแสดงอาณาเขตโซนหลักทั้งหมดบน World Map (Comfort Zone, Starter Camps 1–2, Mid-Field Camps 3–4, Boss Citadel)
    /// ย่อ-ขยายและเลื่อนตำแหน่งสัมพันธ์กับ Zoom และ Pan ของแผนที่แบบเรียลไทม์
    /// </summary>
    private void UpdateWorldZoneOverlays(Vector3 focus, float scale)
    {
        if (_zoneOverlayContainer == null) return;
        if (!_worldMode)
        {
            for (int i = 0; i < _zoneOverlayPool.Count; i++)
                if (_zoneOverlayPool[i].gameObject.activeSelf) _zoneOverlayPool[i].gameObject.SetActive(false);
            return;
        }

        int idx = 0;
        if (SafeZone.Instance != null)
        {
            PlaceZoneOverlay(idx++, SafeZone.Instance.Center, SafeZone.Instance.Radius + 2f, focus, scale, new Color(0.14f, 0.90f, 0.64f, 0.16f));
        }

        if (SandboxWorldSpawner.Instance != null)
        {
            var camps = SandboxWorldSpawner.Instance.Camps;
            for (int i = 0; i < camps.Count; i++)
            {
                var c = camps[i];
                Color col = c.MaxLevel <= 10
                    ? new Color(0.98f, 0.64f, 0.18f, 0.14f)  // โซน Tier 1 Starter (Lv.1–10)
                    : new Color(0.95f, 0.30f, 0.22f, 0.15f); // โซน Tier 2 Mid-Field Frontier (Lv.11–22)
                PlaceZoneOverlay(idx++, c.Center, Mathf.Max(14f, c.CampAreaRadius), focus, scale, col);
            }

            PlaceZoneOverlay(idx++, SandboxWorldSpawner.Instance.BossArenaCenter, SandboxWorldSpawner.Instance.BossArenaProximityRadius, focus, scale, new Color(0.92f, 0.14f, 0.18f, 0.18f));
        }

        for (int i = idx; i < _zoneOverlayPool.Count; i++)
        {
            if (_zoneOverlayPool[i].gameObject.activeSelf) _zoneOverlayPool[i].gameObject.SetActive(false);
        }
    }

    private void PlaceZoneOverlay(int index, Vector3 worldPos, float worldRadius, Vector3 focus, float scale, Color color)
    {
        while (_zoneOverlayPool.Count <= index)
        {
            var img = NewImage(_zoneOverlayContainer, $"ZoneOverlay_{_zoneOverlayPool.Count}", color);
            img.sprite = HudIcons.GetSprite("circle");
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            _zoneOverlayPool.Add(img);
        }

        var overlay = _zoneOverlayPool[index];
        overlay.color = color;
        float x = (worldPos.x - focus.x) * scale;
        float y = (worldPos.z - focus.z) * scale;
        float diam = Mathf.Max(20f, worldRadius * 2f * scale);
        overlay.rectTransform.anchoredPosition = new Vector2(x, y);
        overlay.rectTransform.sizeDelta = new Vector2(diam, diam);
        if (!overlay.gameObject.activeSelf) overlay.gameObject.SetActive(true);
    }

    private bool OverlapsAnyLabel(Rect candidate)
    {
        for (int i = 0; i < _placedLabelRects.Count; i++)
        {
            if (_placedLabelRects[i].Overlaps(candidate)) return true;
        }
        return false;
    }

    /// <summary>
    /// ซิงก์โหมดการแสดงผล World Map / Minimap ทันทีเมื่อ MapManager สั่งเปิดปิด
    /// </summary>
    public void SyncWorldModeImmediate(bool world)
    {
        if (_worldMode != world) ApplyMode(world);
    }

    private void ApplyMode(bool world)
    {
        _worldMode = world;
        if (_topNavBar != null) _topNavBar.SetActive(world);
        if (_footerBar != null) _footerBar.SetActive(world);
        if (_sidePanel != null) _sidePanel.SetActive(world);
        if (_miniFooter != null) _miniFooter.SetActive(!world);
        if (_view != null) _view.raycastTarget = world;

        if (world)
        {
            _frameRect.anchorMin = _frameRect.anchorMax = _frameRect.pivot = new Vector2(0.5f, 0.5f);
            _frameRect.anchoredPosition = new Vector2(0f, 18f);
            float totalH = HeaderHeight + WorldMapAreaSize + FooterHeight + Border * 2f;
            _frameRect.sizeDelta = new Vector2(WorldModalWidth + Border * 2f, totalH);

            _viewRect.anchorMin = Vector2.zero;
            _viewRect.anchorMax = Vector2.one;
            _viewRect.offsetMin = new Vector2(12f, FooterHeight + 8f);
            _viewRect.offsetMax = new Vector2(-(SideBarWidth + 24f), -(HeaderHeight + 8f));
        }
        else
        {
            _frameRect.anchorMin = _frameRect.anchorMax = _frameRect.pivot = new Vector2(0f, 1f);
            _frameRect.anchoredPosition = new Vector2(18f, -18f);
            _frameRect.sizeDelta = new Vector2(MiniSize + Border * 2f, MiniSize + 24f + Border * 2f);

            _viewRect.anchorMin = Vector2.zero;
            _viewRect.anchorMax = Vector2.one;
            _viewRect.offsetMin = new Vector2(0f, 24f);
            _viewRect.offsetMax = Vector2.zero;
        }
    }

    // ------------------------------------------------------------------ พูลของหมุดแผนที่

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

            var lbl = MakeText(go.transform, "Label", "", 12, FontStyle.Bold, TextAnchor.UpperCenter,
                Color.white, new Vector2(0.5f, 0.5f), new Vector2(0f, -16f), new Vector2(180f, 22f));
            lbl.horizontalOverflow = HorizontalWrapMode.Overflow;
            UITheme.AddShadow(lbl.gameObject, 1f);

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
        lrt.anchoredPosition = new Vector2(0f, -16f);
        bool showLabel = worldMode;

        switch (m.kind)
        {
            case MarkerKind.LocalPlayer:
                w.icon.sprite = HudIcons.GetSprite("player_arrow");
                w.icon.color = Color.white;
                irt.sizeDelta = worldMode ? new Vector2(20f, 20f) : new Vector2(14f, 14f);
                if (PlayerCombat.Local != null)
                {
                    float yaw = PlayerCombat.Local.transform.eulerAngles.y;
                    irt.localRotation = Quaternion.Euler(0f, 0f, -yaw);
                }
                w.label.color = Color.white;
                lrt.anchoredPosition = new Vector2(24f, 6f);
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
                irt.sizeDelta = worldMode ? new Vector2(26f, 26f) : new Vector2(18f, 18f);
                w.label.color = new Color(1f, 0.72f, 0.25f);
                lrt.anchoredPosition = new Vector2(0f, -18f);
                break;

            case MarkerKind.EnemyCamp:
                w.icon.sprite = HudIcons.GetSprite("camp");
                w.icon.color = new Color(1f, 0.58f, 0.15f, 0.92f);
                irt.sizeDelta = worldMode ? new Vector2(20f, 20f) : new Vector2(14f, 14f);
                w.label.color = new Color(1f, 0.78f, 0.40f);
                lrt.anchoredPosition = new Vector2(0f, -16f);
                break;

            case MarkerKind.LootChest:
                w.icon.sprite = HudIcons.GetSprite("chest");
                w.icon.color = UITheme.BrushedGoldBright;
                irt.sizeDelta = worldMode ? new Vector2(18f, 18f) : new Vector2(13f, 13f);
                w.label.color = UITheme.BrushedGoldBright;
                lrt.anchoredPosition = new Vector2(0f, 24f);
                break;

            case MarkerKind.SafeZone:
                w.icon.sprite = HudIcons.GetSprite("shield");
                w.icon.color = UITheme.JadeAccent;
                irt.sizeDelta = worldMode ? new Vector2(24f, 24f) : new Vector2(15f, 15f);
                w.label.color = UITheme.JadeAccent;
                lrt.anchoredPosition = new Vector2(0f, 26f);
                break;

            case MarkerKind.Waypoint:
                w.icon.sprite = HudIcons.GetSprite("waypoint");
                w.icon.color = new Color(0.20f, 1.0f, 0.68f);
                irt.sizeDelta = worldMode ? new Vector2(24f, 24f) : new Vector2(18f, 18f);
                w.label.color = new Color(0.35f, 1.0f, 0.75f);
                lrt.anchoredPosition = new Vector2(0f, 26f);
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
                w.icon.color = UITheme.BrushedGoldBright;
                irt.sizeDelta = new Vector2(9f, 9f);
                showLabel = false;
                break;
        }

        w.label.gameObject.SetActive(showLabel && !string.IsNullOrEmpty(m.label));
        if (showLabel) w.label.text = m.label;
    }

    // ------------------------------------------------------------------ ฟังก์ชันสร้าง UI

    private GameObject MakeTabButton(Transform parent, string name, string label, float x, float w, bool active, UnityEngine.Events.UnityAction onClick)
    {
        Color bg = active ? UITheme.BrushedGold : new Color(0.14f, 0.17f, 0.24f, 1f);
        var img = NewImage(parent, name, bg);
        UITheme.ApplySlicedStyle(img, bg);
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -9f);
        rt.sizeDelta = new Vector2(w, 34f);

        var btn = img.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

        MakeText(img.transform, "Text", label, 13, FontStyle.Bold, TextAnchor.MiddleCenter,
            active ? new Color(0.08f, 0.06f, 0.02f) : Color.white, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(w - 8f, 28f));
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

    private class MapClickRelay : MonoBehaviour,
        IPointerClickHandler, IScrollHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public MinimapUI owner;
        public void OnPointerClick(PointerEventData eventData) => owner?.OnPointerClick(eventData);
        public void OnScroll(PointerEventData eventData) => owner?.OnMapScroll(eventData);
        public void OnBeginDrag(PointerEventData eventData) => owner?.OnMapBeginDrag(eventData);
        public void OnDrag(PointerEventData eventData) => owner?.OnMapDrag(eventData);
        public void OnEndDrag(PointerEventData eventData) => owner?.OnMapEndDrag();
    }
}
