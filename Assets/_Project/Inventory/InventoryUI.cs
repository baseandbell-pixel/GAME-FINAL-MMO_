using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Clean MMORPG Inventory &amp; Equipment Window ([I] / [Tab]):
///   - Focused strictly on Inventory Management &amp; 4-Slot Gear Equipping (Sword, Shield, Armor, Pants).
///   - Exact slotIndex &amp; instanceId binding for Use / Equip / Drop (eliminating slot-0 bugs).
///   - Full Drag-and-Drop between the 24-slot bag grid and onto the 4 Equipment Gear Slots.
///   - No World Map tab, no Enhancement/Blacksmith forging, and no manual Stat-Point allocation clutter.
/// </summary>
public class InventoryUI : MonoBehaviour
{
    public static InventoryUI Instance { get; private set; }

    private const int Columns = 6;
    private const float SlotSize = 78f;
    private const float SlotGap = 8f;

    public bool IsOpen => _open;
    public int SelectedSlotIndex => _selected;
    public string HeaderStatsText => _headerStatsText != null ? _headerStatsText.text : string.Empty;

    private InventoryManager _inv;
    private GameObject _panel;
    private RectTransform _panelInnerRect;

    // Header
    private Text _headerStatsText;

    // Left Column: Equipment Slots + Character Gear Totals + Selected Item Inspector
    private Text _totalStatsText;
    private Text _detailTitleText;
    private Text _detailBodyText;
    private Image _detailRarityBar;
    private Button _useBtn, _equipBtn, _dropBtn;
    private Text _equipBtnText;

    // 24 Inventory Slots
    private Image[] _slotBorders;
    private Image[] _slotBgs;
    private Image[] _slotIcons;
    private Text[] _slotLabels;
    private Text[] _slotQtyLabels;

    // 4 Equipment Gear Slots
    private class EquipSlotWidget
    {
        public EquipmentSlotType slotType;
        public Image border;
        public Image icon;
        public Text nameText;
        public Text statText;
        public Button unequipBtn;
    }
    private readonly EquipSlotWidget[] _equipWidgets = new EquipSlotWidget[4];

    // Floating Hover Rarity Tooltip
    private RectTransform _tooltipRect;
    private Image _tooltipBorder;
    private Text _tooltipTitle;
    private Text _tooltipBody;
    private int _hoveredIndex = -1;

    // Drag-and-Drop State & Visual Ghost
    private int _draggingFromIndex = -1;
    private GameObject _dragGhostObj;
    private RectTransform _dragGhostRect;
    private Image _dragGhostBorder;
    private Image _dragGhostIcon;

    private int _selected = -1;
    private bool _open;
    private Font _font;

    public static InventoryUI Create(InventoryManager inv)
    {
        var go = new GameObject("InventoryUI");
        var ui = go.AddComponent<InventoryUI>();
        ui.Init(inv);
        return ui;
    }

    private void Init(InventoryManager inv)
    {
        Instance = this;
        _inv = inv;
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        EnsureEventSystem();
        BuildCanvas();
        _inv.Changed += Refresh;
        SetOpen(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_inv != null) _inv.Changed -= Refresh;
        if (_open) GameplayInput.PopBlocker("InventoryUI");
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<InputSystemUIInputModule>();
    }

    // ------------------------------------------------------------------ UI Construction

    private void BuildCanvas()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        gameObject.AddComponent<GraphicRaycaster>();

        // Outer Gold Frame (980 x 646)
        var outer = MakeImage(transform, "InventoryWindowFrame", new Color(0.58f, 0.48f, 0.24f, 0.96f));
        _panel = outer.gameObject;
        var prt = outer.rectTransform;
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
        prt.sizeDelta = new Vector2(980f, 646f);

        var inner = MakeImage(outer.transform, "InnerDarkBg", new Color(0.05f, 0.06f, 0.09f, 0.97f));
        _panelInnerRect = inner.rectTransform;
        FillWithPadding(_panelInnerRect, 3f);

        // ============================================================
        // TOP HEADER BAR (Title + Level/Gold Summary + Close)
        // ============================================================
        var navBg = MakeImage(inner.transform, "TopNavigationTabs", new Color(0.08f, 0.09f, 0.13f, 1f));
        Place(navBg.rectTransform, 0f, 0f, 974f, 46f);

        MakeLabel(navBg.transform, "WindowTitle", "INVENTORY & EQUIPMENT", 15, FontStyle.Bold, TextAnchor.MiddleLeft,
            new Color(1f, 0.88f, 0.36f), 18f, 8f, 320f, 30f);

        _headerStatsText = MakeLabel(navBg.transform, "HeaderStats", "", 13, FontStyle.Bold, TextAnchor.MiddleRight,
            new Color(0.95f, 0.88f, 0.52f), 350f, 8f, 468f, 30f);

        MakeTabButton(navBg.transform, "CloseBtn", "Close [I / Esc]", 832f, 130f, false, () => SetOpen(false), out _, out _);

        // ============================================================
        // LEFT COLUMN: EQUIPPED GEAR + TOTAL STATS + ITEM INSPECTOR
        // ============================================================
        var leftCol = MakeImage(inner.transform, "EquipmentColumn", new Color(0.07f, 0.08f, 0.12f, 1f));
        Place(leftCol.rectTransform, 14f, 58f, 418f, 548f);

        MakeLabel(leftCol.transform, "EquipSectionTitle", "EQUIPPED GEAR  (Drag Bag Gear Here or Click Equip)", 12, FontStyle.Bold, TextAnchor.MiddleLeft,
            new Color(1f, 0.84f, 0.28f), 12f, 10f, 394f, 22f);

        BuildEquipSlotWidget(leftCol.transform, 0, EquipmentSlotType.Sword, "SWORD", 12f, 36f);
        BuildEquipSlotWidget(leftCol.transform, 1, EquipmentSlotType.Shield, "SHIELD", 212f, 36f);
        BuildEquipSlotWidget(leftCol.transform, 2, EquipmentSlotType.Armor, "ARMOR", 12f, 132f);
        BuildEquipSlotWidget(leftCol.transform, 3, EquipmentSlotType.Pants, "PANTS", 212f, 132f);

        // Total Combat Stats Box (Base + Level + Equipped Gear)
        var statsBox = MakeImage(leftCol.transform, "TotalStatsBox", new Color(0.04f, 0.05f, 0.08f, 0.95f));
        Place(statsBox.rectTransform, 12f, 232f, 394f, 94f);

        MakeLabel(statsBox.transform, "StatsHeader", "CHARACTER COMBAT TOTALS (Base + Gear)", 11, FontStyle.Bold, TextAnchor.MiddleLeft,
            new Color(0.55f, 0.85f, 1f), 10f, 6f, 374f, 18f);

        _totalStatsText = MakeLabel(statsBox.transform, "TotalStatsBody", "", 12, FontStyle.Bold, TextAnchor.UpperLeft,
            new Color(0.88f, 0.94f, 1f), 10f, 28f, 374f, 60f);

        // Selected Item Inspector Box
        var detailBox = MakeImage(leftCol.transform, "SelectedItemInspector", new Color(0.04f, 0.05f, 0.08f, 0.96f));
        Place(detailBox.rectTransform, 12f, 336f, 394f, 200f);

        _detailRarityBar = MakeImage(detailBox.transform, "RarityHeaderBar", new Color(0.3f, 0.32f, 0.38f, 1f));
        Place(_detailRarityBar.rectTransform, 0f, 0f, 394f, 4f);

        _detailTitleText = MakeLabel(detailBox.transform, "DetailTitle", "Select an inventory slot", 14, FontStyle.Bold,
            TextAnchor.MiddleLeft, Color.white, 12f, 10f, 370f, 24f);
        _detailBodyText = MakeLabel(detailBox.transform, "DetailBody",
            "Click any bag slot to inspect its stats, or drag items between slots and onto the 4 Equipment slots above.",
            12, FontStyle.Normal, TextAnchor.UpperLeft, new Color(0.78f, 0.82f, 0.88f), 12f, 38f, 370f, 108f);

        _useBtn = MakeActionButton(detailBox.transform, "UseBtn", "Use", 12f, 154f, 116f, 34f, OnUseClicked, out _);
        _equipBtn = MakeActionButton(detailBox.transform, "EquipBtn", "Equip", 138f, 154f, 116f, 34f, OnEquipClicked, out _equipBtnText);
        _dropBtn = MakeActionButton(detailBox.transform, "DropBtn", "Drop", 264f, 154f, 118f, 34f, OnDropClicked, out _);

        // ============================================================
        // RIGHT COLUMN: 24-SLOT INVENTORY GRID (6x4) WITH DRAG & DROP
        // ============================================================
        var rightCol = MakeImage(inner.transform, "InventoryGridColumn", new Color(0.07f, 0.08f, 0.12f, 1f));
        Place(rightCol.rectTransform, 444f, 58f, 516f, 548f);

        MakeLabel(rightCol.transform, "BagTitle",
            "INVENTORY BAG (24 SLOTS)  —  Drag & Drop to Move, Swap, or Equip",
            12, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(1f, 0.84f, 0.28f), 14f, 10f, 488f, 22f);

        int n = _inv.Capacity;
        _slotBorders = new Image[n];
        _slotBgs = new Image[n];
        _slotIcons = new Image[n];
        _slotLabels = new Text[n];
        _slotQtyLabels = new Text[n];

        for (int i = 0; i < n; i++)
        {
            int col = i % Columns;
            int row = i / Columns;
            float x = 14f + col * (SlotSize + SlotGap);
            float y = 42f + row * (SlotSize + SlotGap);

            var border = MakeImage(rightCol.transform, $"Slot_{i:00}", new Color(0.24f, 0.27f, 0.34f, 1f));
            Place(border.rectTransform, x, y, SlotSize, SlotSize);
            _slotBorders[i] = border;

            var bg = MakeImage(border.transform, "InnerBg", new Color(0.10f, 0.11f, 0.16f, 1f));
            bg.raycastTarget = false;
            FillWithPadding(bg.rectTransform, 2f);
            _slotBgs[i] = bg;

            var icon = MakeImage(bg.transform, "ItemIcon", Color.clear);
            icon.raycastTarget = false;
            Place(icon.rectTransform, 17f, 6f, 40f, 40f);
            _slotIcons[i] = icon;

            int captured = i;
            var btn = border.gameObject.AddComponent<Button>();
            btn.targetGraphic = border;
            btn.onClick.AddListener(() => SelectSlot(captured));

            var dragRelay = border.gameObject.AddComponent<SlotDragRelay>();
            dragRelay.owner = this;
            dragRelay.slotIndex = captured;

            _slotLabels[i] = MakeLabel(bg.transform, "Name", "", 10, FontStyle.Bold,
                TextAnchor.LowerCenter, Color.white, 3f, 44f, SlotSize - 10f, 28f);
            _slotQtyLabels[i] = MakeLabel(bg.transform, "Qty", "", 11, FontStyle.Bold,
                TextAnchor.UpperRight, new Color(1f, 0.92f, 0.35f), 3f, 3f, SlotSize - 10f, 18f);
        }

        MakeLabel(rightCol.transform, "BagFooterHint",
            "• Click Use / Equip / Drop to act strictly on the selected slot.\n" +
            "• Drag any slot onto another slot to move/swap, or drag gear onto SWORD / SHIELD / ARMOR / PANTS.",
            11, FontStyle.Normal, TextAnchor.UpperLeft, new Color(0.68f, 0.75f, 0.84f), 14f, 396f, 488f, 48f);

        // Footer Bar
        MakeLabel(inner.transform, "FooterHint",
            "Rarity Tiers: Normal (Grey)  |  Rare (Blue)  |  Epic (Purple)  |  Legendary (Gold)  |  Auto-Saved to Disk",
            12, FontStyle.Normal, TextAnchor.MiddleCenter, new Color(0.70f, 0.76f, 0.84f), 14f, 612f, 946f, 26f);

        // ============================================================
        // FLOATING HOVER TOOLTIP & DRAG GHOST ICON
        // ============================================================
        BuildHoverTooltip(inner.transform);
        BuildDragGhost(inner.transform);
    }

    private void BuildEquipSlotWidget(Transform parent, int index, EquipmentSlotType slotType, string slotTitle, float x, float y)
    {
        var border = MakeImage(parent, $"EquipSlot_{slotType}", new Color(0.32f, 0.35f, 0.44f, 1f));
        Place(border.rectTransform, x, y, 194f, 86f);

        var dropRelay = border.gameObject.AddComponent<EquipDropRelay>();
        dropRelay.owner = this;
        dropRelay.slotType = slotType;

        var inner = MakeImage(border.transform, "Inner", new Color(0.09f, 0.10f, 0.15f, 1f));
        inner.raycastTarget = false;
        FillWithPadding(inner.rectTransform, 2f);

        MakeLabel(inner.transform, "SlotHeader", slotTitle, 10, FontStyle.Bold, TextAnchor.UpperLeft,
            new Color(0.62f, 0.70f, 0.82f), 8f, 5f, 110f, 14f);

        var iconFrame = MakeImage(inner.transform, "IconFrame", new Color(0.14f, 0.16f, 0.22f, 1f));
        iconFrame.raycastTarget = false;
        Place(iconFrame.rectTransform, 8f, 24f, 46f, 46f);

        var iconImg = MakeImage(iconFrame.transform, "Icon", new Color(1f, 1f, 1f, 0.25f));
        iconImg.raycastTarget = false;
        FillWithPadding(iconImg.rectTransform, 4f);
        iconImg.sprite = HudIcons.GetForEquipmentSlot(slotType);

        var nameTxt = MakeLabel(inner.transform, "EquippedName", "Empty", 11, FontStyle.Bold, TextAnchor.UpperLeft,
            new Color(0.55f, 0.58f, 0.65f), 60f, 22f, 124f, 28f);
        var statTxt = MakeLabel(inner.transform, "EquippedStats", "Drag gear here", 10, FontStyle.Normal, TextAnchor.UpperLeft,
            new Color(0.52f, 0.82f, 0.62f), 60f, 48f, 124f, 30f);

        EquipmentSlotType capturedType = slotType;
        var unequipBtn = MakeActionButton(inner.transform, "UnequipBtn", "Unequip", 124f, 4f, 62f, 18f,
            () => _inv.UnequipSlot(capturedType), out _);

        _equipWidgets[index] = new EquipSlotWidget
        {
            slotType = slotType,
            border = border,
            icon = iconImg,
            nameText = nameTxt,
            statText = statTxt,
            unequipBtn = unequipBtn
        };
    }

    private void BuildHoverTooltip(Transform parent)
    {
        var border = MakeImage(parent, "RarityHoverTooltip", new Color(0.95f, 0.78f, 0.22f, 0.98f));
        border.raycastTarget = false;
        _tooltipBorder = border;
        _tooltipRect = border.rectTransform;
        _tooltipRect.anchorMin = _tooltipRect.anchorMax = _tooltipRect.pivot = new Vector2(0f, 1f);
        _tooltipRect.sizeDelta = new Vector2(280f, 118f);

        var bg = MakeImage(border.transform, "Bg", new Color(0.04f, 0.05f, 0.08f, 0.96f));
        bg.raycastTarget = false;
        FillWithPadding(bg.rectTransform, 2f);

        _tooltipTitle = MakeLabel(bg.transform, "Title", "", 13, FontStyle.Bold, TextAnchor.UpperLeft,
            Color.white, 10f, 8f, 256f, 22f);
        _tooltipBody = MakeLabel(bg.transform, "Body", "", 11, FontStyle.Normal, TextAnchor.UpperLeft,
            new Color(0.84f, 0.88f, 0.94f), 10f, 32f, 256f, 78f);

        border.gameObject.SetActive(false);
    }

    private void BuildDragGhost(Transform parent)
    {
        var ghost = MakeImage(parent, "DragGhostIcon", new Color(1f, 0.85f, 0.25f, 0.92f));
        ghost.raycastTarget = false;
        _dragGhostObj = ghost.gameObject;
        _dragGhostBorder = ghost;
        _dragGhostRect = ghost.rectTransform;
        _dragGhostRect.anchorMin = _dragGhostRect.anchorMax = _dragGhostRect.pivot = new Vector2(0.5f, 0.5f);
        _dragGhostRect.sizeDelta = new Vector2(68f, 68f);

        var inner = MakeImage(ghost.transform, "Inner", new Color(0.06f, 0.08f, 0.12f, 0.92f));
        inner.raycastTarget = false;
        FillWithPadding(inner.rectTransform, 2f);

        _dragGhostIcon = MakeImage(inner.transform, "Icon", Color.white);
        _dragGhostIcon.raycastTarget = false;
        FillWithPadding(_dragGhostIcon.rectTransform, 6f);

        _dragGhostObj.SetActive(false);
    }

    // ------------------------------------------------------------------ Input & Open/Close

    private void Update()
    {
        if (_inv == null) return;
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.iKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame)
        {
            SetOpen(!_open);
        }
        else if (_open && kb.escapeKey.wasPressedThisFrame)
        {
            SetOpen(false);
        }

        if (_open && _hoveredIndex >= 0 && _draggingFromIndex < 0)
            UpdateHoverTooltipPosition();
    }

    public void SetOpen(bool open)
    {
        if (_open != open)
        {
            if (open) GameplayInput.PushBlocker("InventoryUI");
            else GameplayInput.PopBlocker("InventoryUI");
        }
        _open = open;
        if (_panel != null) _panel.SetActive(open);

        if (!open)
        {
            _hoveredIndex = -1;
            if (_tooltipBorder != null) _tooltipBorder.gameObject.SetActive(false);
            CancelDragGhost();
        }
        else
        {
            Refresh();
        }
    }

    public void SelectSlot(int index)
    {
        _selected = index;
        Refresh();
    }

    // ------------------------------------------------------------------ Exact Slot Button Callbacks

    private void OnUseClicked()
    {
        if (_selected >= 0) _inv.UseItem(_selected);
    }

    private void OnEquipClicked()
    {
        if (_selected >= 0) _inv.EquipItem(_selected);
    }

    private void OnDropClicked()
    {
        if (_selected >= 0) _inv.DropItem(_selected);
    }

    // ------------------------------------------------------------------ Drag-and-Drop Handlers

    private void OnSlotBeginDrag(int index, PointerEventData eventData)
    {
        var slot = _inv.GetSlot(index);
        if (slot == null || slot.IsEmpty || slot.item == null) return;

        _draggingFromIndex = index;
        _selected = index;
        if (_tooltipBorder != null) _tooltipBorder.gameObject.SetActive(false);

        if (_dragGhostObj != null)
        {
            _dragGhostObj.SetActive(true);
            _dragGhostBorder.color = ItemData.GetTierColor(slot.item.tier);
            _dragGhostIcon.sprite = HudIcons.GetForItem(slot.item);
            _dragGhostIcon.color = slot.item.worldColor;
            UpdateDragGhostPosition(eventData);
        }
    }

    private void OnSlotDrag(PointerEventData eventData)
    {
        if (_draggingFromIndex < 0) return;
        UpdateDragGhostPosition(eventData);
    }

    private void OnSlotEndDrag()
    {
        CancelDragGhost();
    }

    private void OnSlotDrop(int targetIndex)
    {
        if (_draggingFromIndex < 0) return;
        int from = _draggingFromIndex;
        CancelDragGhost();

        if (from != targetIndex)
        {
            _inv.SwapOrMoveSlots(from, targetIndex);
            _selected = targetIndex;
            Refresh();
        }
    }

    private void OnEquipSlotDrop(EquipmentSlotType targetSlotType)
    {
        if (_draggingFromIndex < 0) return;
        int from = _draggingFromIndex;
        CancelDragGhost();

        if (_inv.EquipFromDrag(from, targetSlotType))
        {
            _selected = from;
            Refresh();
        }
    }

    private void UpdateDragGhostPosition(PointerEventData eventData)
    {
        if (_dragGhostRect == null || _panelInnerRect == null) return;
        Vector2 screenPos = eventData != null ? eventData.position : Vector2.zero;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_panelInnerRect, screenPos, null, out Vector2 localPt))
        {
            _dragGhostRect.anchoredPosition = localPt;
        }
    }

    private void CancelDragGhost()
    {
        _draggingFromIndex = -1;
        if (_dragGhostObj != null) _dragGhostObj.SetActive(false);
    }

    // ------------------------------------------------------------------ Hover Tooltip

    private void OnSlotHoverEnter(int index)
    {
        if (_draggingFromIndex >= 0) return;
        _hoveredIndex = index;
        var slot = _inv.GetSlot(index);
        if (slot == null || slot.IsEmpty || slot.item == null || _tooltipBorder == null)
        {
            if (_tooltipBorder != null) _tooltipBorder.gameObject.SetActive(false);
            return;
        }

        Color tierCol = ItemData.GetTierColor(slot.item.tier);
        _tooltipBorder.color = tierCol;
        _tooltipTitle.color = tierCol;
        _tooltipTitle.text = $"[{ItemData.GetTierDisplayName(slot.item.tier)}] {slot.item.displayName}";
        _tooltipBody.text = BuildItemStatDescription(slot.item, slot.quantity, index);
        _tooltipBorder.gameObject.SetActive(true);
        UpdateHoverTooltipPosition();
    }

    private void OnSlotHoverExit(int index)
    {
        if (_hoveredIndex == index)
        {
            _hoveredIndex = -1;
            if (_tooltipBorder != null) _tooltipBorder.gameObject.SetActive(false);
        }
    }

    private void UpdateHoverTooltipPosition()
    {
        if (_tooltipRect == null || _panelInnerRect == null || Mouse.current == null) return;
        Vector2 mousePos = Mouse.current.position.ReadValue();
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_panelInnerRect, mousePos, null, out Vector2 localPoint))
        {
            float halfW = _panelInnerRect.rect.width * 0.5f;
            float halfH = _panelInnerRect.rect.height * 0.5f;
            float x = Mathf.Clamp(localPoint.x + halfW + 16f, 10f, _panelInnerRect.rect.width - 290f);
            float y = Mathf.Clamp(halfH - localPoint.y + 12f, 10f, _panelInnerRect.rect.height - 126f);
            _tooltipRect.anchoredPosition = new Vector2(x, -y);
        }
    }

    // ------------------------------------------------------------------ Refresh Visual State

    public void Refresh()
    {
        if (_inv == null || _slotBorders == null) return;

        var combat = PlayerCombat.Local;
        var capsule = combat != null ? combat.GetComponent<PlayerCapsule>() : null;

        int lvl = combat != null && combat.IsSpawned ? combat.NetworkLevel : 1;
        int exp = combat != null && combat.IsSpawned ? combat.NetworkExperience : 0;
        int nextExp = combat != null && combat.IsSpawned ? combat.ExpToNextLevel : 100;
        int gold = combat != null && combat.IsSpawned ? combat.NetworkGold : 0;

        if (_headerStatsText != null)
            _headerStatsText.text = $"Lv.{lvl} (EXP {exp}/{nextExp})   |   Gold: {gold} G";

        RefreshEquipmentAndTotals(combat, capsule);
        RefreshBagGrid();
    }

    private void RefreshEquipmentAndTotals(PlayerCombat combat, PlayerCapsule capsule)
    {
        // 1. Update 4 Equipment Slot Widgets
        for (int i = 0; i < _equipWidgets.Length; i++)
        {
            var w = _equipWidgets[i];
            if (w == null) continue;
            var item = _inv.GetEquippedItem(w.slotType);
            if (item == null)
            {
                w.border.color = new Color(0.28f, 0.31f, 0.40f, 1f);
                w.icon.sprite = HudIcons.GetForEquipmentSlot(w.slotType);
                w.icon.color = new Color(1f, 1f, 1f, 0.22f);
                w.nameText.text = "Empty Slot";
                w.nameText.color = new Color(0.52f, 0.56f, 0.64f);
                w.statText.text = "Drag gear here";
                w.unequipBtn.gameObject.SetActive(false);
            }
            else
            {
                Color tc = ItemData.GetTierColor(item.tier);
                w.border.color = tc;
                w.icon.sprite = HudIcons.GetForEquipmentSlot(w.slotType);
                w.icon.color = item.worldColor;
                w.nameText.text = $"[{ItemData.GetTierDisplayName(item.tier)}] {item.displayName}";
                w.nameText.color = tc;
                w.statText.text = BuildCompactGearStats(item);
                w.unequipBtn.gameObject.SetActive(true);
            }
        }

        // 2. Update Total Aggregated Stats Summary
        if (_totalStatsText != null && combat != null)
        {
            float spd = capsule != null ? capsule.TotalMoveSpeed : 6f;
            _totalStatsText.text =
                $"HP: {combat.NetworkHealth}/{combat.MaxHealth} (+{_inv.GearMaxHealthBonus} Gear)     " +
                $"Stamina: {combat.CurrentStamina:0}/{combat.MaxStamina:0} (+{_inv.GearMaxStaminaBonus} Gear)\n" +
                $"Attack: {combat.TotalAttackDamage} (+{_inv.GearDamageBonus} Gear)     " +
                $"Defense: {combat.TotalDefense} (+{_inv.GearDefenseBonus} Gear)\n" +
                $"Speed: {spd:0.1} m/s (+{_inv.GearMoveSpeedBonus:0.1} Gear)     " +
                $"Crit Chance: {combat.TotalCritChance * 100f:0}%";
        }

        // 3. Selected Slot Detail Inspector
        var sel = _inv.GetSlot(_selected);
        if (sel == null || sel.IsEmpty || sel.item == null)
        {
            _detailRarityBar.color = new Color(0.30f, 0.32f, 0.38f, 1f);
            _detailTitleText.color = Color.white;
            _detailTitleText.text = _selected >= 0 ? $"Slot #{_selected} (Empty)" : "Select an inventory slot";
            _detailBodyText.text = "Click any bag slot to inspect its stats, or drag items between slots and onto the 4 Equipment slots above.";
            _useBtn.interactable = false;
            _equipBtn.interactable = false;
            _dropBtn.interactable = false;
            _equipBtnText.text = "Equip";
        }
        else
        {
            var item = sel.item;
            Color tc = ItemData.GetTierColor(item.tier);
            bool eq = _inv.IsSlotEquipped(_selected);

            _detailRarityBar.color = tc;
            _detailTitleText.color = tc;
            _detailTitleText.text = $"[Slot #{_selected}] [{ItemData.GetTierDisplayName(item.tier)}] {item.displayName}";
            _detailBodyText.text = BuildItemStatDescription(item, sel.quantity, _selected);

            _useBtn.interactable = item.IsUsable;
            _equipBtn.interactable = item.IsEquippable;
            _equipBtnText.text = eq ? "Unequip" : "Equip";
            _dropBtn.interactable = item.droppable;
        }
    }

    private void RefreshBagGrid()
    {
        for (int i = 0; i < _inv.Capacity; i++)
        {
            var s = _inv.GetSlot(i);
            bool selected = i == _selected;

            if (s == null || s.IsEmpty || s.item == null)
            {
                _slotBorders[i].color = selected
                    ? new Color(0.95f, 0.78f, 0.22f, 1f)
                    : new Color(0.22f, 0.25f, 0.32f, 1f);
                _slotBgs[i].color = new Color(0.09f, 0.10f, 0.15f, 1f);
                _slotIcons[i].color = Color.clear;
                _slotLabels[i].text = "";
                _slotQtyLabels[i].text = "";
                continue;
            }

            bool equipped = _inv.IsSlotEquipped(i);
            Color tierColor = ItemData.GetTierColor(s.item.tier);

            if (selected)
                _slotBorders[i].color = new Color(1f, 0.92f, 0.32f, 1f);
            else if (equipped)
                _slotBorders[i].color = new Color(0.28f, 0.96f, 0.52f, 1f);
            else
                _slotBorders[i].color = tierColor;

            _slotBgs[i].color = new Color(
                Mathf.Lerp(0.09f, tierColor.r, 0.22f),
                Mathf.Lerp(0.10f, tierColor.g, 0.22f),
                Mathf.Lerp(0.14f, tierColor.b, 0.22f),
                1f);

            _slotIcons[i].sprite = HudIcons.GetForItem(s.item);
            _slotIcons[i].color = s.item.worldColor;

            _slotLabels[i].text = s.item.displayName;
            _slotLabels[i].color = tierColor;

            if (equipped)
                _slotQtyLabels[i].text = "EQP";
            else if (s.quantity > 1)
                _slotQtyLabels[i].text = $"x{s.quantity}";
            else
                _slotQtyLabels[i].text = "";
        }
    }

    private static string BuildCompactGearStats(ItemData item)
    {
        if (item == null) return "";
        var parts = new List<string>(4);
        if (item.damageBonus > 0) parts.Add($"+{item.damageBonus} ATK");
        if (item.maxHealthBonus > 0) parts.Add($"+{item.maxHealthBonus} HP");
        if (item.maxStaminaBonus > 0) parts.Add($"+{item.maxStaminaBonus} STA");
        if (item.defenseBonus > 0) parts.Add($"+{item.defenseBonus} DEF");
        if (item.moveSpeedBonus > 0f) parts.Add($"+{item.moveSpeedBonus:0.0} SPD");
        if (item.critChanceBonus > 0f) parts.Add($"+{item.critChanceBonus * 100f:0}% CRIT");
        return parts.Count > 0 ? string.Join("  ", parts) : "Standard Gear";
    }

    private static string BuildItemStatDescription(ItemData item, int qty, int slotIdx)
    {
        if (item == null) return "";
        var sb = new System.Text.StringBuilder();
        var eqSlot = item.GetEquipmentSlot();
        string slotTag = eqSlot != EquipmentSlotType.None ? $"Slot: {eqSlot}" : $"Type: {item.type}";
        sb.AppendLine($"{slotTag}   •   Bag Slot #{slotIdx}   •   Qty: {qty}");

        var stats = BuildCompactGearStats(item);
        if (!string.IsNullOrEmpty(stats) && stats != "Standard Gear")
            sb.AppendLine($"Gear Bonuses: {stats}");
        if (item.healAmount > 0)
            sb.AppendLine($"Use Effect: Restores +{item.healAmount} HP");
        if (item.staminaRestoreAmount > 0)
            sb.AppendLine($"Use Effect: Restores +{item.staminaRestoreAmount} Stamina");
        if (item.attackBuffBonus > 0)
            sb.AppendLine($"Use Effect: +{item.attackBuffBonus} Attack for {item.attackBuffDuration:0}s");
        if (!string.IsNullOrEmpty(item.description))
            sb.Append(item.description);
        return sb.ToString().TrimEnd();
    }

    // ------------------------------------------------------------------ uGUI helpers

    private static void FillWithPadding(RectTransform rt, float pad)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(pad, pad);
        rt.offsetMax = new Vector2(-pad, -pad);
    }

    private static void Place(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
    }

    private static Image MakeImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        return img;
    }

    private Text MakeLabel(Transform parent, string name, string text, int size, FontStyle style, TextAnchor anchor, Color color, float x, float y, float w, float h)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.transform.SetParent(parent, false);
        Place((RectTransform)go.transform, x, y, w, h);
        var t = go.GetComponent<Text>();
        t.font = _font;
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = anchor;
        t.color = color;
        t.text = text;
        t.supportRichText = true;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        return t;
    }

    private void MakeTabButton(Transform parent, string name, string label, float x, float w, bool active,
        UnityEngine.Events.UnityAction onClick, out Image bgOut, out Text textOut)
    {
        Color bg = active ? new Color(0.55f, 0.44f, 0.18f, 1f) : new Color(0.14f, 0.17f, 0.24f, 1f);
        var img = MakeImage(parent, name, bg);
        Place(img.rectTransform, x, 8f, w, 30f);
        var btn = img.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);
        textOut = MakeLabel(img.transform, "Text", label, 12, FontStyle.Bold, TextAnchor.MiddleCenter,
            active ? new Color(1f, 0.94f, 0.72f) : Color.white, 4f, 2f, w - 8f, 26f);
        bgOut = img;
    }

    private Button MakeActionButton(Transform parent, string name, string label, float x, float y, float w, float h, UnityEngine.Events.UnityAction onClick, out Text labelOut)
    {
        var img = MakeImage(parent, name, new Color(0.18f, 0.24f, 0.36f, 1f));
        Place(img.rectTransform, x, y, w, h);
        var btn = img.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);
        labelOut = MakeLabel(img.transform, "Text", label, 12, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white, 2f, 2f, w - 4f, h - 4f);
        return btn;
    }

    private class SlotDragRelay : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
    {
        public InventoryUI owner;
        public int slotIndex;
        public void OnPointerEnter(PointerEventData eventData) => owner?.OnSlotHoverEnter(slotIndex);
        public void OnPointerExit(PointerEventData eventData) => owner?.OnSlotHoverExit(slotIndex);
        public void OnBeginDrag(PointerEventData eventData) => owner?.OnSlotBeginDrag(slotIndex, eventData);
        public void OnDrag(PointerEventData eventData) => owner?.OnSlotDrag(eventData);
        public void OnEndDrag(PointerEventData eventData) => owner?.OnSlotEndDrag();
        public void OnDrop(PointerEventData eventData) => owner?.OnSlotDrop(slotIndex);
    }

    private class EquipDropRelay : MonoBehaviour, IDropHandler
    {
        public InventoryUI owner;
        public EquipmentSlotType slotType;
        public void OnDrop(PointerEventData eventData) => owner?.OnEquipSlotDrop(slotType);
    }
}
