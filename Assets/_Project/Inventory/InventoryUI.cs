using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Phase 2+ MMORPG Character, Inventory &amp; Blacksmith Enhancement Window:
///   - Top navigation tabs: [ World Map (M) ] | [ Character &amp; Upgrade ] | [ Blacksmith (+Gear) ].
///   - Exact slotIndex &amp; instanceId binding for Use / Equip / Drop (eliminating slot-0 bugs).
///   - Full Drag-and-Drop between the 24-slot bag grid and onto the 4 Equipment Gear Cards.
///   - Scaled attribute upgrade costs (Points + Gold) and +1 to +10 Blacksmith Enhancement.
/// </summary>
public class InventoryUI : MonoBehaviour
{
    public enum HubTab
    {
        Character = 0,
        Blacksmith = 1
    }

    public static InventoryUI Instance { get; private set; }

    private const int Columns = 6;
    private const float SlotSize = 78f;
    private const float SlotGap = 8f;

    public bool IsOpen => _open;
    public bool IsBlacksmithTabActive => _activeTab == HubTab.Blacksmith;
    public int SelectedSlotIndex => _selected;
    public int EnhanceTargetSlotIndex => _enhanceTargetSlot;
    public IReadOnlyList<int> SelectedFodderIndices => _fodderSlots;
    public string HeaderStatsText => _headerStatsText != null ? _headerStatsText.text : string.Empty;
    public string LastEnhanceFeedback => _enhanceFeedbackText != null ? _enhanceFeedbackText.text : string.Empty;

    private InventoryManager _inv;
    private GameObject _panel;
    private RectTransform _panelInnerRect;
    private HubTab _activeTab = HubTab.Character;

    // Top Navigation Tabs
    private Image _tabCharacterBg;
    private Text _tabCharacterText;
    private Image _tabBlacksmithBg;
    private Text _tabBlacksmithText;
    private Text _headerStatsText;

    // Character Column vs Blacksmith Column (Left side)
    private GameObject _characterColumn;
    private GameObject _blacksmithColumn;

    // Character Column Widgets
    private Text _totalStatsText;
    private Text _statPointsText;
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

    // 4 Stat Allocation Rows
    private class StatAllocWidget
    {
        public PlayerStatType statType;
        public string shortName;
        public string effectDesc;
        public Text labelText;
        public Text costText;
        public Text valueText;
        public Button plusBtn;
    }
    private readonly StatAllocWidget[] _statWidgets = new StatAllocWidget[4];

    // Blacksmith Column Widgets
    private readonly Button[] _bsEquippedBtns = new Button[4];
    private readonly Text[] _bsEquippedTexts = new Text[4];
    private Image _bsTargetBorder;
    private Image _bsTargetIcon;
    private Text _bsTargetTitleText;
    private Text _bsTargetStatDiffText;
    private Button _bsClearTargetBtn;
    private Text _bsOddsText;
    private Text _bsGoldCostText;
    private Text _bsFodderReqText;
    private readonly Button[] _bsFodderSlotBtns = new Button[3];
    private readonly Image[] _bsFodderSlotBorders = new Image[3];
    private readonly Text[] _bsFodderSlotTexts = new Text[3];
    private Button _bsAutoPickBtn;
    private Button _bsEnhanceBtn;
    private Image _bsEnhanceBtnBg;
    private Text _bsEnhanceBtnText;
    private Text _enhanceFeedbackText;

    private int _enhanceTargetSlot = -1;
    private readonly List<int> _fodderSlots = new List<int>();

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

    // ------------------------------------------------------------------ build

    private void Init(InventoryManager inv)
    {
        Instance = this;
        _inv = inv;
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (_font == null) Debug.LogWarning("[InventoryUI] Built-in font not found; text may not render.");

        EnsureEventSystem();

        var canvasGo = new GameObject("Canvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 55;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        // Main Obsidian Frame (980 x 650)
        var outerFrame = MakeImage(canvasGo.transform, "CharacterHubFrame", new Color(0.55f, 0.45f, 0.22f, 0.95f));
        _panel = outerFrame.gameObject;
        var pr = outerFrame.rectTransform;
        pr.anchorMin = pr.anchorMax = pr.pivot = new Vector2(0.5f, 0.5f);
        pr.sizeDelta = new Vector2(980f, 650f);
        pr.anchoredPosition = Vector2.zero;

        var inner = MakeImage(_panel.transform, "PanelInner", new Color(0.05f, 0.06f, 0.09f, 0.97f));
        _panelInnerRect = inner.rectTransform;
        FillWithPadding(_panelInnerRect, 3f);

        // ============================================================
        // TOP NAVIGATION TAB BAR (3 Tabs + Header Stats + Close)
        // ============================================================
        var navBg = MakeImage(inner.transform, "TopNavigationTabs", new Color(0.08f, 0.09f, 0.13f, 1f));
        Place(navBg.rectTransform, 0f, 0f, 974f, 46f);

        MakeTabButton(navBg.transform, "TabCharacter", "[ Character & Upgrade ]", 14f, 210f, true,
            () => SetActiveTab(HubTab.Character), out _tabCharacterBg, out _tabCharacterText);
        MakeTabButton(navBg.transform, "TabBlacksmith", "[ Blacksmith (+Gear) ]", 232f, 210f, false,
            () => SetActiveTab(HubTab.Blacksmith), out _tabBlacksmithBg, out _tabBlacksmithText);
        MakeTabButton(navBg.transform, "CloseBtn", "Close [I / Esc]", 832f, 130f, false, () => SetOpen(false), out _, out _);

        _headerStatsText = MakeLabel(navBg.transform, "HeaderStats", "", 12, FontStyle.Bold, TextAnchor.MiddleRight,
            new Color(0.95f, 0.88f, 0.52f), 450f, 8f, 374f, 30f);

        // ============================================================
        // LEFT COLUMN A: CHARACTER STATS, EQUIPPED GEAR & ATTRIBUTES
        // ============================================================
        var leftCol = MakeImage(inner.transform, "CharacterColumn", new Color(0.07f, 0.08f, 0.12f, 1f));
        _characterColumn = leftCol.gameObject;
        Place(leftCol.rectTransform, 14f, 58f, 418f, 548f);

        MakeLabel(leftCol.transform, "EquipSectionTitle", "EQUIPPED GEAR  (Drag Bag Gear Here or Click Equip)", 12, FontStyle.Bold, TextAnchor.MiddleLeft,
            new Color(1f, 0.84f, 0.28f), 12f, 10f, 394f, 22f);

        BuildEquipSlotWidget(leftCol.transform, 0, EquipmentSlotType.Sword, "SWORD", 12f, 36f);
        BuildEquipSlotWidget(leftCol.transform, 1, EquipmentSlotType.Shield, "SHIELD", 212f, 36f);
        BuildEquipSlotWidget(leftCol.transform, 2, EquipmentSlotType.Armor, "ARMOR", 12f, 128f);
        BuildEquipSlotWidget(leftCol.transform, 3, EquipmentSlotType.Pants, "PANTS", 212f, 128f);

        var totalBox = MakeImage(leftCol.transform, "TotalStatsBox", new Color(0.05f, 0.06f, 0.09f, 1f));
        Place(totalBox.rectTransform, 12f, 224f, 394f, 92f);
        MakeLabel(totalBox.transform, "TotalTitle", "TOTAL CHARACTER STATS  (Base + Level + Enhanced Gear)", 12, FontStyle.Bold, TextAnchor.MiddleLeft,
            new Color(0.55f, 0.82f, 1f), 10f, 6f, 374f, 20f);
        _totalStatsText = MakeLabel(totalBox.transform, "TotalBody", "", 13, FontStyle.Bold, TextAnchor.UpperLeft,
            Color.white, 10f, 30f, 374f, 58f);

        var allocBox = MakeImage(leftCol.transform, "StatAllocBox", new Color(0.05f, 0.06f, 0.09f, 1f));
        Place(allocBox.rectTransform, 12f, 326f, 394f, 210f);
        MakeLabel(allocBox.transform, "AllocTitle", "ATTRIBUTES & UPGRADE", 13, FontStyle.Bold, TextAnchor.MiddleLeft,
            new Color(1f, 0.84f, 0.28f), 10f, 8f, 210f, 22f);
        _statPointsText = MakeLabel(allocBox.transform, "UnspentPts", "Points: 0", 13, FontStyle.Bold, TextAnchor.MiddleRight,
            new Color(1f, 0.84f, 0.28f), 220f, 8f, 162f, 22f);

        BuildStatRow(allocBox.transform, 0, PlayerStatType.STR, "STR", "+4 ATK", 42f);
        BuildStatRow(allocBox.transform, 1, PlayerStatType.VIT, "VIT", "+15 HP", 82f);
        BuildStatRow(allocBox.transform, 2, PlayerStatType.END, "END", "+12 STA", 122f);
        BuildStatRow(allocBox.transform, 3, PlayerStatType.AGI, "AGI", "+3% CRIT", 162f);

        // ============================================================
        // LEFT COLUMN B: BLACKSMITH GEAR ENHANCEMENT (+1 TO +10)
        // ============================================================
        BuildBlacksmithColumn(inner.transform);

        // ============================================================
        // RIGHT COLUMN: DARK MMORPG INVENTORY GRID & RARITY INSPECTOR
        // ============================================================
        var rightCol = MakeImage(inner.transform, "InventoryColumn", new Color(0.07f, 0.08f, 0.12f, 1f));
        Place(rightCol.rectTransform, 444f, 58f, 516f, 548f);

        MakeLabel(rightCol.transform, "InvTitle", "INVENTORY  (Drag to Reorder/Equip  |  Click to Inspect)", 12, FontStyle.Bold, TextAnchor.MiddleLeft,
            new Color(1f, 0.84f, 0.28f), 14f, 10f, 488f, 22f);

        int count = _inv.Capacity;
        _slotBorders = new Image[count];
        _slotBgs = new Image[count];
        _slotIcons = new Image[count];
        _slotLabels = new Text[count];
        _slotQtyLabels = new Text[count];

        for (int i = 0; i < count; i++)
        {
            int col = i % Columns, row = i / Columns;
            float x = 14f + col * (SlotSize + SlotGap);
            float y = 38f + row * (SlotSize + SlotGap);

            var borderImg = MakeImage(rightCol.transform, "SlotBorder" + i, new Color(0.22f, 0.25f, 0.32f, 1f));
            Place(borderImg.rectTransform, x, y, SlotSize, SlotSize);

            var bgImg = MakeImage(borderImg.transform, "SlotBg", new Color(0.10f, 0.12f, 0.16f, 1f));
            FillWithPadding(bgImg.rectTransform, 2f);

            var btn = bgImg.gameObject.AddComponent<Button>();
            btn.targetGraphic = bgImg;
            int captured = i;
            btn.onClick.AddListener(() => OnGridSlotClicked(captured));

            var relay = bgImg.gameObject.AddComponent<SlotDragRelay>();
            relay.owner = this;
            relay.slotIndex = captured;

            var iconImg = MakeImage(bgImg.transform, "Icon", Color.white);
            iconImg.raycastTarget = false;
            Place(iconImg.rectTransform, 20f, 10f, 34f, 34f);
            iconImg.gameObject.SetActive(false);

            var label = MakeLabel(bgImg.transform, "Label", "", 10, FontStyle.Bold, TextAnchor.LowerCenter,
                Color.white, 3f, 44f, SlotSize - 10f, 26f);

            var qty = MakeLabel(bgImg.transform, "Qty", "", 10, FontStyle.Bold, TextAnchor.UpperRight,
                new Color(1f, 0.92f, 0.45f), 4f, 3f, SlotSize - 12f, 16f);

            _slotBorders[i] = borderImg;
            _slotBgs[i] = bgImg;
            _slotIcons[i] = iconImg;
            _slotLabels[i] = label;
            _slotQtyLabels[i] = qty;
        }

        // Selected Item Detail Box + Action Buttons (Bottom of Right Column)
        var detailFrame = MakeImage(rightCol.transform, "SelectedDetailBox", new Color(0.05f, 0.06f, 0.09f, 1f));
        Place(detailFrame.rectTransform, 14f, 384f, 488f, 114f);

        _detailRarityBar = MakeImage(detailFrame.transform, "RarityStripe", new Color(0.35f, 0.38f, 0.45f, 1f));
        Place(_detailRarityBar.rectTransform, 0f, 0f, 5f, 114f);

        _detailTitleText = MakeLabel(detailFrame.transform, "DetailTitle", "Select an item to inspect.", 14, FontStyle.Bold, TextAnchor.MiddleLeft,
            Color.white, 14f, 6f, 464f, 24f);
        _detailBodyText = MakeLabel(detailFrame.transform, "DetailBody", "", 12, FontStyle.Normal, TextAnchor.UpperLeft,
            new Color(0.84f, 0.87f, 0.92f), 14f, 32f, 464f, 76f);

        _useBtn = MakeActionButton(rightCol.transform, "UseBtn", "Use", 14f, 506f, 156f, 32f, OnUseOrAutoClick, out _);
        _equipBtn = MakeActionButton(rightCol.transform, "EquipBtn", "Equip", 180f, 506f, 156f, 32f, OnEquipOrTargetClick, out _equipBtnText);
        _dropBtn = MakeActionButton(rightCol.transform, "DropBtn", "Drop", 346f, 506f, 156f, 32f, () =>
        {
            if (_selected >= 0) _inv.DropItem(_selected);
        }, out _);

        // Bottom Footer Hint
        MakeLabel(inner.transform, "Hint",
            "Rarity Tiers: Normal (Grey)  |  Rare (Blue)  |  Epic (Purple)  |  Legendary (Gold)  |  Blacksmith (+1..+10)",
            12, FontStyle.Normal, TextAnchor.MiddleCenter, new Color(0.70f, 0.76f, 0.84f), 14f, 612f, 946f, 26f);

        // ============================================================
        // FLOATING HOVER RARITY TOOLTIP
        // ============================================================
        var tipBorder = MakeImage(_panel.transform, "HoverTooltip", new Color(0.75f, 0.78f, 0.82f, 0.98f));
        tipBorder.raycastTarget = false;
        _tooltipBorder = tipBorder;
        _tooltipRect = tipBorder.rectTransform;
        _tooltipRect.anchorMin = _tooltipRect.anchorMax = new Vector2(0f, 1f);
        _tooltipRect.pivot = new Vector2(0f, 1f);
        _tooltipRect.sizeDelta = new Vector2(280f, 134f);

        var tipBg = MakeImage(_tooltipRect, "TipBg", new Color(0.04f, 0.05f, 0.08f, 0.98f));
        tipBg.raycastTarget = false;
        FillWithPadding(tipBg.rectTransform, 2f);

        _tooltipTitle = MakeLabel(tipBg.transform, "TipTitle", "", 13, FontStyle.Bold, TextAnchor.UpperLeft,
            Color.white, 8f, 6f, 260f, 34f);
        _tooltipBody = MakeLabel(tipBg.transform, "TipBody", "", 11, FontStyle.Normal, TextAnchor.UpperLeft,
            new Color(0.86f, 0.88f, 0.92f), 8f, 40f, 260f, 86f);

        _tooltipRect.gameObject.SetActive(false);

        // ============================================================
        // FLOATING DRAG GHOST ICON
        // ============================================================
        _dragGhostBorder = MakeImage(_panel.transform, "DragGhost", Color.white);
        _dragGhostBorder.raycastTarget = false;
        _dragGhostObj = _dragGhostBorder.gameObject;
        _dragGhostRect = _dragGhostBorder.rectTransform;
        _dragGhostRect.sizeDelta = new Vector2(54f, 54f);
        var ghostInner = MakeImage(_dragGhostRect, "Inner", new Color(0.07f, 0.09f, 0.14f, 0.92f));
        ghostInner.raycastTarget = false;
        FillWithPadding(ghostInner.rectTransform, 2f);
        _dragGhostIcon = MakeImage(ghostInner.transform, "Icon", Color.white);
        _dragGhostIcon.raycastTarget = false;
        FillWithPadding(_dragGhostIcon.rectTransform, 6f);
        _dragGhostObj.SetActive(false);

        ApplyActiveTabVisuals();
        _panel.SetActive(false);
        _inv.Changed += Refresh;
        Debug.Log("[InventoryUI] Upgraded MMORPG Hub built (" + count + " slots + Drag&Drop + Blacksmith).");
    }

    private void BuildBlacksmithColumn(Transform parent)
    {
        var bsCol = MakeImage(parent, "BlacksmithColumn", new Color(0.07f, 0.08f, 0.12f, 1f));
        _blacksmithColumn = bsCol.gameObject;
        Place(bsCol.rectTransform, 14f, 58f, 418f, 548f);

        MakeLabel(bsCol.transform, "BsTitle", "BLACKSMITH ENHANCEMENT  (+1 to +10)", 13, FontStyle.Bold, TextAnchor.MiddleLeft,
            new Color(1f, 0.78f, 0.25f), 12f, 10f, 394f, 22f);

        // Quick-Select Equipped Gear Row (4 Buttons)
        EquipmentSlotType[] eqOrder = { EquipmentSlotType.Sword, EquipmentSlotType.Shield, EquipmentSlotType.Armor, EquipmentSlotType.Pants };
        string[] eqNames = { "Sword", "Shield", "Armor", "Pants" };
        for (int i = 0; i < 4; i++)
        {
            EquipmentSlotType slotType = eqOrder[i];
            float bx = 12f + i * 99f;
            _bsEquippedBtns[i] = MakeActionButton(bsCol.transform, "BsEqBtn_" + eqNames[i], eqNames[i], bx, 38f, 94f, 44f, () =>
            {
                SelectEquippedAsEnhanceTarget(slotType);
            }, out _bsEquippedTexts[i]);
            _bsEquippedTexts[i].fontSize = 11;
        }

        // Target Gear Preview Box
        _bsTargetBorder = MakeImage(bsCol.transform, "TargetPreviewBorder", new Color(0.26f, 0.30f, 0.38f, 1f));
        Place(_bsTargetBorder.rectTransform, 12f, 92f, 394f, 140f);
        var targetBg = MakeImage(_bsTargetBorder.transform, "TargetBg", new Color(0.05f, 0.06f, 0.09f, 1f));
        FillWithPadding(targetBg.rectTransform, 2f);

        var iconFrame = MakeImage(targetBg.transform, "IconFrame", new Color(0.11f, 0.13f, 0.18f, 1f));
        Place(iconFrame.rectTransform, 10f, 14f, 64f, 64f);
        _bsTargetIcon = MakeImage(iconFrame.transform, "Icon", Color.white);
        FillWithPadding(_bsTargetIcon.rectTransform, 8f);
        _bsTargetIcon.gameObject.SetActive(false);

        _bsTargetTitleText = MakeLabel(targetBg.transform, "TargetTitle", "Select Equipment from Bag or Equipped Bar", 13, FontStyle.Bold, TextAnchor.UpperLeft,
            Color.white, 84f, 10f, 238f, 36f);
        _bsTargetStatDiffText = MakeLabel(targetBg.transform, "TargetStats", "Pick any gear to inspect +Next level stats, Gold cost, and required Fodder.", 11, FontStyle.Normal, TextAnchor.UpperLeft,
            new Color(0.76f, 0.85f, 0.94f), 84f, 50f, 296f, 82f);

        _bsClearTargetBtn = MakeActionButton(targetBg.transform, "ClearTargetBtn", "Clear", 328f, 8f, 54f, 24f, () =>
        {
            _enhanceTargetSlot = -1;
            _fodderSlots.Clear();
            SetEnhanceFeedback("Select Target Gear to enhance.", new Color(0.80f, 0.84f, 0.92f));
            Refresh();
        }, out _);

        // Requirements & Success Odds Box
        var reqBox = MakeImage(bsCol.transform, "RequirementsBox", new Color(0.05f, 0.06f, 0.09f, 1f));
        Place(reqBox.rectTransform, 12f, 240f, 394f, 106f);
        MakeLabel(reqBox.transform, "ReqHeader", "ENHANCEMENT COST & ODDS", 12, FontStyle.Bold, TextAnchor.MiddleLeft,
            new Color(0.55f, 0.82f, 1f), 10f, 6f, 374f, 20f);
        _bsOddsText = MakeLabel(reqBox.transform, "OddsText", "Success Chance: --", 13, FontStyle.Bold, TextAnchor.MiddleLeft,
            Color.white, 10f, 28f, 374f, 22f);
        _bsGoldCostText = MakeLabel(reqBox.transform, "GoldCostText", "Gold Required: --", 13, FontStyle.Bold, TextAnchor.MiddleLeft,
            new Color(1f, 0.86f, 0.30f), 10f, 52f, 374f, 22f);
        _bsFodderReqText = MakeLabel(reqBox.transform, "FodderReqText", "Material Fodder: --", 12, FontStyle.Normal, TextAnchor.MiddleLeft,
            new Color(0.82f, 0.86f, 0.92f), 10f, 76f, 374f, 22f);

        // Attached Fodder Slots (up to 3)
        var fodderStrip = MakeImage(bsCol.transform, "FodderStrip", new Color(0.05f, 0.06f, 0.09f, 1f));
        Place(fodderStrip.rectTransform, 12f, 354f, 394f, 88f);
        MakeLabel(fodderStrip.transform, "FodderTitle", "SACRIFICE FODDER SLOTS  (Click bag gear to attach/remove)", 11, FontStyle.Bold, TextAnchor.MiddleLeft,
            new Color(1f, 0.72f, 0.32f), 10f, 6f, 374f, 18f);

        for (int i = 0; i < 3; i++)
        {
            int fodderIdx = i;
            float fx = 10f + i * 126f;
            var fBorder = MakeImage(fodderStrip.transform, "FodderSlotBorder" + i, new Color(0.24f, 0.28f, 0.36f, 1f));
            Place(fBorder.rectTransform, fx, 28f, 118f, 52f);
            var fBg = MakeImage(fBorder.transform, "Bg", new Color(0.10f, 0.12f, 0.16f, 1f));
            FillWithPadding(fBg.rectTransform, 2f);
            var fBtn = fBg.gameObject.AddComponent<Button>();
            fBtn.targetGraphic = fBg;
            fBtn.onClick.AddListener(() =>
            {
                if (fodderIdx < _fodderSlots.Count)
                {
                    _fodderSlots.RemoveAt(fodderIdx);
                    Refresh();
                }
            });
            var fTxt = MakeLabel(fBg.transform, "Text", "Empty", 10, FontStyle.Bold, TextAnchor.MiddleCenter,
                new Color(0.65f, 0.70f, 0.78f), 4f, 4f, 106f, 40f);

            _bsFodderSlotBorders[i] = fBorder;
            _bsFodderSlotBtns[i] = fBtn;
            _bsFodderSlotTexts[i] = fTxt;
        }

        // Action Row: Auto-Pick + ENHANCE
        _bsAutoPickBtn = MakeActionButton(bsCol.transform, "AutoPickFodderBtn", "Auto-Pick Fodder", 12f, 452f, 152f, 40f, AutoPickEligibleFodder, out _);

        var enhBorder = MakeImage(bsCol.transform, "EnhanceBtnBorder", new Color(0.85f, 0.65f, 0.18f, 1f));
        Place(enhBorder.rectTransform, 174f, 452f, 232f, 40f);
        var enhBg = MakeImage(enhBorder.transform, "EnhanceBtnBg", new Color(0.22f, 0.42f, 0.18f, 1f));
        FillWithPadding(enhBg.rectTransform, 2f);
        _bsEnhanceBtnBg = enhBg;
        _bsEnhanceBtn = enhBg.gameObject.AddComponent<Button>();
        _bsEnhanceBtn.targetGraphic = enhBg;
        _bsEnhanceBtn.onClick.AddListener(ExecuteEnhanceClick);
        _bsEnhanceBtnText = MakeLabel(enhBg.transform, "Text", "ENHANCE GEAR", 14, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Color(1f, 0.94f, 0.72f), 0f, 0f, 228f, 36f);

        _enhanceFeedbackText = MakeLabel(bsCol.transform, "EnhanceFeedback",
            "Select Target Gear (+0..+9), add required Fodder, and forge!",
            12, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.76f, 0.84f, 0.92f),
            12f, 498f, 394f, 42f);

        _blacksmithColumn.SetActive(false);
    }

    private void BuildEquipSlotWidget(Transform parent, int idx, EquipmentSlotType slotType, string slotTitle, float x, float y)
    {
        var border = MakeImage(parent, "EquipBorder_" + slotTitle, new Color(0.24f, 0.27f, 0.34f, 1f));
        Place(border.rectTransform, x, y, 194f, 84f);

        var bg = MakeImage(border.transform, "Bg", new Color(0.09f, 0.11f, 0.15f, 1f));
        FillWithPadding(bg.rectTransform, 2f);

        // Allow dropping dragged gear directly onto this Equipment card!
        var dropRelay = bg.gameObject.AddComponent<EquipDropRelay>();
        dropRelay.owner = this;
        dropRelay.slotType = slotType;

        var iconFrame = MakeImage(bg.transform, "IconFrame", new Color(0.14f, 0.16f, 0.22f, 1f));
        iconFrame.raycastTarget = false;
        Place(iconFrame.rectTransform, 8f, 12f, 54f, 54f);

        var iconImg = MakeImage(iconFrame.transform, "Icon", Color.white);
        iconImg.raycastTarget = false;
        iconImg.sprite = HudIcons.GetForEquipmentSlot(slotType);
        FillWithPadding(iconImg.rectTransform, 8f);

        MakeLabel(bg.transform, "SlotTag", slotTitle, 10, FontStyle.Bold, TextAnchor.UpperLeft,
            new Color(0.60f, 0.68f, 0.80f), 68f, 6f, 84f, 16f);

        var nameLbl = MakeLabel(bg.transform, "ItemName", "Empty", 11, FontStyle.Bold, TextAnchor.UpperLeft,
            Color.white, 68f, 22f, 118f, 30f);

        var statLbl = MakeLabel(bg.transform, "ItemStats", "-", 10, FontStyle.Normal, TextAnchor.LowerLeft,
            new Color(0.55f, 0.90f, 0.65f), 68f, 52f, 118f, 24f);

        var unequip = MakeActionButton(bg.transform, "UnequipBtn", "X", 162f, 4f, 24f, 20f,
            () => { _inv.UnequipSlot(slotType); }, out _);

        _equipWidgets[idx] = new EquipSlotWidget
        {
            slotType = slotType,
            border = border,
            icon = iconImg,
            nameText = nameLbl,
            statText = statLbl,
            unequipBtn = unequip
        };
    }

    private void BuildStatRow(Transform parent, int idx, PlayerStatType statType, string statName, string desc, float y)
    {
        var rowBg = MakeImage(parent, "StatRow_" + statName, new Color(0.09f, 0.11f, 0.15f, 1f));
        Place(rowBg.rectTransform, 10f, y, 374f, 34f);

        var lbl = MakeLabel(rowBg.transform, "StatLabel", $"{statName} <color=#95A2B8>({desc})</color>", 11, FontStyle.Bold, TextAnchor.MiddleLeft,
            Color.white, 8f, 4f, 158f, 26f);

        var costLbl = MakeLabel(rowBg.transform, "StatCost", "1pt+10g", 10, FontStyle.Bold, TextAnchor.MiddleRight,
            new Color(1f, 0.84f, 0.32f), 166f, 4f, 94f, 26f);

        var valLbl = MakeLabel(rowBg.transform, "StatValue", "0", 13, FontStyle.Bold, TextAnchor.MiddleRight,
            new Color(0.95f, 0.96f, 1f), 262f, 4f, 44f, 26f);

        var plus = MakeActionButton(rowBg.transform, "PlusBtn", "+", 314f, 4f, 50f, 26f, () =>
        {
            if (PlayerCombat.Local != null && PlayerCombat.Local.AllocateStatPoint(statType))
                Refresh();
        }, out _);

        _statWidgets[idx] = new StatAllocWidget
        {
            statType = statType,
            shortName = statName,
            effectDesc = desc,
            labelText = lbl,
            costText = costLbl,
            valueText = valLbl,
            plusBtn = plus
        };
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null) return;

        var es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        var module = es.AddComponent<InputSystemUIInputModule>();
        module.AssignDefaultActions();
        Debug.Log("[InventoryUI] No EventSystem found - created one with InputSystemUIInputModule.");
    }

    // ------------------------------------------------------------------ input / tab switching

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

    public void SwitchToWorldMapTab()
    {
        // World Map has been removed per UI cleanup requirements.
    }

    public void OpenToEnhancementTab()
    {
        if (!_open) SetOpen(true);
        SetActiveTab(HubTab.Blacksmith);
    }

    public void SetActiveTab(HubTab tab)
    {
        _activeTab = tab;
        ApplyActiveTabVisuals();
        Refresh();
    }

    public void SetEnhanceTargetSlot(int slotIndex)
    {
        _enhanceTargetSlot = slotIndex;
        _fodderSlots.Clear();
        Refresh();
    }

    private void ApplyActiveTabVisuals()
    {
        bool bs = _activeTab == HubTab.Blacksmith;
        if (_characterColumn != null) _characterColumn.SetActive(!bs);
        if (_blacksmithColumn != null) _blacksmithColumn.SetActive(bs);

        Color activeBg = new Color(0.55f, 0.44f, 0.18f, 1f);
        Color idleBg = new Color(0.14f, 0.17f, 0.24f, 1f);
        Color activeTxt = new Color(1f, 0.94f, 0.72f);

        if (_tabCharacterBg != null) _tabCharacterBg.color = bs ? idleBg : activeBg;
        if (_tabCharacterText != null) _tabCharacterText.color = bs ? Color.white : activeTxt;
        if (_tabBlacksmithBg != null) _tabBlacksmithBg.color = bs ? activeBg : idleBg;
        if (_tabBlacksmithText != null) _tabBlacksmithText.color = bs ? activeTxt : Color.white;
    }

    public void SetOpen(bool open)
    {
        if (open == _open) return;

        if (open && GameplayInput.IsBlocked)
        {
            Debug.LogWarning("[InventoryUI] Another UI is already open - ignoring open request.");
            return;
        }

        _open = open;
        _panel.SetActive(open);
        if (!open)
        {
            HideTooltip();
            EndDragGhost();
        }

        if (open)
        {
            GameplayInput.PushBlocker("InventoryUI");
            Refresh();
        }
        else
        {
            GameplayInput.PopBlocker("InventoryUI");
        }
        Debug.Log("[InventoryUI] " + (open ? "Opened." : "Closed."));
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_inv != null) _inv.Changed -= Refresh;
        if (_open) GameplayInput.PopBlocker("InventoryUI");
    }

    // ------------------------------------------------------------------ slot interactions & Blacksmith actions

    private void Select(int index)
    {
        _selected = index;
        Refresh();
    }

    private void OnGridSlotClicked(int index)
    {
        if (_inv == null) return;
        _selected = index;

        if (_activeTab == HubTab.Blacksmith)
        {
            var clicked = _inv.GetSlot(index);
            if (clicked != null && !clicked.IsEmpty && clicked.item != null)
            {
                if (_enhanceTargetSlot < 0)
                {
                    if (clicked.item.IsEquippable)
                    {
                        _enhanceTargetSlot = index;
                        _fodderSlots.Clear();
                        SetEnhanceFeedback("Target set! Now click eligible bag gear to attach Fodder.", new Color(0.76f, 0.88f, 1f));
                    }
                    else
                    {
                        SetEnhanceFeedback("Only Equippable Gear can be enhanced.", new Color(1f, 0.55f, 0.35f));
                    }
                }
                else if (index == _enhanceTargetSlot)
                {
                    // Clicking target again keeps it selected for inspection without error
                }
                else
                {
                    var targetSlot = _inv.GetSlot(_enhanceTargetSlot);
                    if (targetSlot != null && !targetSlot.IsEmpty && targetSlot.item != null)
                    {
                        if (_fodderSlots.Contains(index))
                        {
                            _fodderSlots.Remove(index);
                        }
                        else if (_inv.IsSlotEquipped(index))
                        {
                            SetEnhanceFeedback("Unequip an item before using it as Enhancement Fodder.", new Color(1f, 0.65f, 0.30f));
                        }
                        else
                        {
                            var spec = EnhancementSystem.GetNextSpec(targetSlot.item, targetSlot.enhancementLevel);
                            if (!EnhancementSystem.IsEligibleFodder(targetSlot.item, targetSlot.instanceId, clicked, spec.minFodderTier))
                            {
                                SetEnhanceFeedback(
                                    $"Ineligible Fodder: needs {ItemData.GetTierDisplayName(spec.minFodderTier)}..{ItemData.GetTierDisplayName(targetSlot.item.tier)} gear.",
                                    new Color(1f, 0.45f, 0.35f));
                            }
                            else if (_fodderSlots.Count >= spec.requiredFodderCount)
                            {
                                SetEnhanceFeedback($"Already attached {spec.requiredFodderCount} Fodder item(s). Click one to remove or click ENHANCE.", new Color(1f, 0.84f, 0.35f));
                            }
                            else
                            {
                                _fodderSlots.Add(index);
                                SetEnhanceFeedback($"Fodder attached ({_fodderSlots.Count}/{spec.requiredFodderCount}).", new Color(0.45f, 0.96f, 0.72f));
                            }
                        }
                    }
                }
            }
        }

        Refresh();
    }

    private void SelectEquippedAsEnhanceTarget(EquipmentSlotType slotType)
    {
        if (_inv == null) return;
        var eqRef = _inv.GetEquippedRef(slotType);
        if (eqRef.IsEmpty)
        {
            SetEnhanceFeedback($"No {slotType} equipped. Click any bag gear on the right!", new Color(1f, 0.72f, 0.35f));
            Refresh();
            return;
        }
        int idx = _inv.FindSlotIndexByInstanceId(eqRef.instanceId);
        if (idx >= 0)
        {
            _enhanceTargetSlot = idx;
            _selected = idx;
            _fodderSlots.Clear();
            SetEnhanceFeedback($"Selected equipped {eqRef.ResolveItem()?.displayName ?? slotType.ToString()} as Target.", new Color(0.55f, 0.90f, 1f));
        }
        Refresh();
    }

    private void AutoPickEligibleFodder()
    {
        if (_inv == null) return;
        var targetSlot = _inv.GetSlot(_enhanceTargetSlot);
        if (targetSlot == null || targetSlot.IsEmpty || targetSlot.item == null || !targetSlot.item.IsEquippable)
        {
            SetEnhanceFeedback("Select valid Target Gear first.", new Color(1f, 0.55f, 0.35f));
            return;
        }

        _fodderSlots.Clear();
        var spec = EnhancementSystem.GetNextSpec(targetSlot.item, targetSlot.enhancementLevel);

        // Prefer lower-tier unequipped gear first so valuable high-tier pieces are preserved
        var candidates = new List<int>();
        for (int i = 0; i < _inv.Slots.Count; i++)
        {
            if (i == _enhanceTargetSlot || _inv.IsSlotEquipped(i)) continue;
            var s = _inv.Slots[i];
            if (EnhancementSystem.IsEligibleFodder(targetSlot.item, targetSlot.instanceId, s, spec.minFodderTier))
                candidates.Add(i);
        }

        candidates.Sort((a, b) =>
        {
            var sa = _inv.Slots[a];
            var sb = _inv.Slots[b];
            int tc = sa.item.tier.CompareTo(sb.item.tier);
            if (tc != 0) return tc;
            return sa.enhancementLevel.CompareTo(sb.enhancementLevel);
        });

        for (int i = 0; i < candidates.Count && _fodderSlots.Count < spec.requiredFodderCount; i++)
            _fodderSlots.Add(candidates[i]);

        if (_fodderSlots.Count >= spec.requiredFodderCount)
            SetEnhanceFeedback($"Auto-picked {_fodderSlots.Count}/{spec.requiredFodderCount} Fodder item(s).", new Color(0.45f, 0.96f, 0.72f));
        else
            SetEnhanceFeedback($"Only found {_fodderSlots.Count}/{spec.requiredFodderCount} eligible Fodder item(s).", new Color(1f, 0.72f, 0.35f));

        Refresh();
    }

    private void ExecuteEnhanceClick()
    {
        if (_inv == null) return;
        EnhanceResult res = _inv.TryEnhanceItem(_enhanceTargetSlot, _fodderSlots);
        _fodderSlots.Clear();

        if (!res.attempted)
            SetEnhanceFeedback(res.message, new Color(1f, 0.45f, 0.35f));
        else if (res.succeeded)
            SetEnhanceFeedback(res.message, new Color(0.32f, 0.98f, 0.65f));
        else
            SetEnhanceFeedback(res.message, new Color(1f, 0.55f, 0.25f));

        Refresh();
    }

    private void SetEnhanceFeedback(string msg, Color col)
    {
        if (_enhanceFeedbackText == null) return;
        _enhanceFeedbackText.text = msg;
        _enhanceFeedbackText.color = col;
    }

    private void OnUseOrAutoClick()
    {
        if (_inv == null) return;
        if (_activeTab == HubTab.Blacksmith)
        {
            AutoPickEligibleFodder();
            return;
        }
        if (_selected >= 0) _inv.UseItem(_selected);
    }

    private void OnEquipOrTargetClick()
    {
        if (_inv == null || _selected < 0) return;
        if (_activeTab == HubTab.Blacksmith)
        {
            var s = _inv.GetSlot(_selected);
            if (s != null && !s.IsEmpty && s.item != null && s.item.IsEquippable)
            {
                _enhanceTargetSlot = _selected;
                _fodderSlots.Clear();
                SetEnhanceFeedback($"Target switched to {s.item.displayName} +{s.enhancementLevel}.", new Color(0.55f, 0.90f, 1f));
                Refresh();
            }
            return;
        }
        _inv.EquipItem(_selected);
    }

    // ------------------------------------------------------------------ Drag-and-Drop

    private void OnSlotBeginDrag(int slotIndex, PointerEventData eventData)
    {
        if (!_open || _inv == null) return;
        var slot = _inv.GetSlot(slotIndex);
        if (slot == null || slot.IsEmpty || slot.item == null) return;

        _draggingFromIndex = slotIndex;
        HideTooltip();

        Color tierCol = ItemData.GetTierColor(slot.item.tier);
        _dragGhostBorder.color = tierCol;
        _dragGhostIcon.sprite = HudIcons.GetForItem(slot.item);
        _dragGhostIcon.color = tierCol;
        _dragGhostObj.SetActive(true);
        UpdateDragGhostPosition(eventData);
    }

    private void OnSlotDrag(PointerEventData eventData)
    {
        if (_draggingFromIndex < 0 || _dragGhostObj == null || !_dragGhostObj.activeSelf) return;
        UpdateDragGhostPosition(eventData);
    }

    private void OnSlotDrop(int targetSlotIndex)
    {
        int from = _draggingFromIndex;
        EndDragGhost();
        if (from < 0 || from == targetSlotIndex || _inv == null) return;

        if (_inv.SwapOrMoveSlots(from, targetSlotIndex))
        {
            // Keep target/fodder selections aligned if a Blacksmith item moved
            if (_enhanceTargetSlot == from) _enhanceTargetSlot = targetSlotIndex;
            else if (_enhanceTargetSlot == targetSlotIndex) _enhanceTargetSlot = from;

            for (int i = 0; i < _fodderSlots.Count; i++)
            {
                if (_fodderSlots[i] == from) _fodderSlots[i] = targetSlotIndex;
                else if (_fodderSlots[i] == targetSlotIndex) _fodderSlots[i] = from;
            }

            _selected = targetSlotIndex;
            Refresh();
        }
    }

    private void OnEquipSlotDrop(EquipmentSlotType targetSlotType)
    {
        int from = _draggingFromIndex;
        EndDragGhost();
        if (from < 0 || _inv == null) return;

        if (_inv.EquipFromDrag(from, targetSlotType))
        {
            _selected = from;
            Refresh();
        }
    }

    private void OnSlotEndDrag()
    {
        EndDragGhost();
    }

    private void EndDragGhost()
    {
        _draggingFromIndex = -1;
        if (_dragGhostObj != null && _dragGhostObj.activeSelf)
            _dragGhostObj.SetActive(false);
    }

    private void UpdateDragGhostPosition(PointerEventData eventData)
    {
        if (_panel == null || _dragGhostRect == null) return;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)_panel.transform, eventData.position, eventData.pressEventCamera, out Vector2 localPoint))
        {
            _dragGhostRect.localPosition = localPoint;
        }
    }

    // ------------------------------------------------------------------ Hover Tooltip

    private void OnSlotHoverEnter(int index)
    {
        _hoveredIndex = index;
        if (!_open || _draggingFromIndex >= 0 || _inv == null || index < 0 || index >= _inv.Slots.Count)
        {
            HideTooltip();
            return;
        }

        var slot = _inv.Slots[index];
        if (slot == null || slot.IsEmpty) { HideTooltip(); return; }

        var item = slot.item;
        int enh = slot.enhancementLevel;
        Color tierCol = ItemData.GetTierColor(item.tier);
        _tooltipBorder.color = tierCol;
        _tooltipTitle.color = tierCol;
        string enhSuffix = enh > 0 ? $" +{enh}" : "";
        _tooltipTitle.text = $"{item.displayName}{enhSuffix}\n[{ItemData.GetTierDisplayName(item.tier)} {FormatItemCategory(item)}]";
        _tooltipBody.text = BuildItemStatSummary(item, enh, includeDesc: true);

        _tooltipRect.gameObject.SetActive(true);
        UpdateHoverTooltipPosition();
    }

    private void OnSlotHoverExit(int index)
    {
        if (_hoveredIndex == index)
        {
            _hoveredIndex = -1;
            HideTooltip();
        }
    }

    private void HideTooltip()
    {
        if (_tooltipRect != null && _tooltipRect.gameObject.activeSelf)
            _tooltipRect.gameObject.SetActive(false);
    }

    private void UpdateHoverTooltipPosition()
    {
        if (_tooltipRect == null || !_tooltipRect.gameObject.activeSelf || _hoveredIndex < 0 || _hoveredIndex >= _slotBorders.Length)
            return;

        var slotRt = _slotBorders[_hoveredIndex].rectTransform;
        Vector3 worldTopRight = slotRt.TransformPoint(new Vector3(SlotSize + 8f, 0f, 0f));
        Vector2 localPos = _panel.transform.InverseTransformPoint(worldTopRight);
        localPos.x = Mathf.Clamp(localPos.x, -460f, 200f);
        localPos.y = Mathf.Clamp(localPos.y, -280f, 240f);
        _tooltipRect.localPosition = localPos;
    }

    // ------------------------------------------------------------------ view refresh

    public void Refresh()
    {
        if (!_open || _inv == null) return;

        var combat = PlayerCombat.Local;
        var capsule = PlayerCapsule.Local;
        int gold = combat != null ? combat.NetworkGold : 0;

        if (_headerStatsText != null)
        {
            int nextExp = combat != null ? combat.ExpToNextLevel : 100;
            _headerStatsText.text = $"Lv.{_inv.Level}  |  EXP {_inv.Experience}/{nextExp}  |  {gold:N0} G";
        }

        if (_activeTab == HubTab.Character)
            RefreshCharacterColumn(combat, capsule, gold);
        else
            RefreshBlacksmithColumn(gold);

        RefreshInventoryGrid();
        RefreshSelectedInspector();
    }

    private void RefreshCharacterColumn(PlayerCombat combat, PlayerCapsule capsule, int gold)
    {
        // 4 Equipment Slots (Sword, Shield, Armor, Pants)
        for (int i = 0; i < _equipWidgets.Length; i++)
        {
            var w = _equipWidgets[i];
            if (w == null) continue;
            var eqRef = _inv.GetEquippedRef(w.slotType);
            ItemData equipped = eqRef.ResolveItem();
            if (!eqRef.IsEmpty && equipped != null)
            {
                int enh = eqRef.enhancementLevel;
                Color tc = ItemData.GetTierColor(equipped.tier);
                w.border.color = tc;
                w.icon.color = tc;
                w.nameText.text = enh > 0 ? $"{equipped.displayName} +{enh}" : equipped.displayName;
                w.nameText.color = tc;
                w.statText.text = BuildItemStatSummary(equipped, enh, includeDesc: false).Replace("\n", " ");
                w.unequipBtn.gameObject.SetActive(true);
            }
            else
            {
                w.border.color = new Color(0.22f, 0.25f, 0.32f, 1f);
                w.icon.color = new Color(0.32f, 0.36f, 0.44f, 0.65f);
                w.nameText.text = "Empty Slot";
                w.nameText.color = new Color(0.55f, 0.60f, 0.68f);
                w.statText.text = "-";
                w.unequipBtn.gameObject.SetActive(false);
            }
        }

        // Derived Total Stats & Scaled Attribute Costs
        if (combat != null)
        {
            float spd = capsule != null ? capsule.TotalMoveSpeed : 6f;
            _totalStatsText.text =
                $"ATK: {combat.TotalAttackDamage}     |     DEF: {combat.TotalDefense}     |     CRIT: {combat.TotalCritChance * 100f:0}%\n" +
                $"Max HP: {combat.MaxHealth}     |     Max STA: {combat.MaxStamina:0}     |     SPD: {spd:0.00}";

            int pts = combat.NetworkUnspentStatPoints;
            _statPointsText.text = $"Pts: {pts}  |  {gold:N0} G";
            _statPointsText.color = pts > 0 ? new Color(1f, 0.85f, 0.22f) : new Color(0.65f, 0.70f, 0.78f);

            int[] statVals = { combat.StatPointsSTR, combat.StatPointsVIT, combat.StatPointsEND, combat.StatPointsAGI };
            for (int i = 0; i < _statWidgets.Length; i++)
            {
                var sw = _statWidgets[i];
                int curVal = statVals[i];
                int ptCost = PlayerCombat.GetAttributeUpgradePointCost(curVal);
                int goldCost = PlayerCombat.GetAttributeUpgradeGoldCost(curVal);
                bool canAfford = pts >= ptCost && gold >= goldCost;

                sw.valueText.text = curVal.ToString();
                sw.costText.text = $"{ptCost}pt + {goldCost}g";
                sw.costText.color = canAfford ? new Color(1f, 0.86f, 0.32f) : new Color(0.85f, 0.45f, 0.42f);
                sw.plusBtn.interactable = canAfford;
            }
        }
    }

    private void RefreshBlacksmithColumn(int playerGold)
    {
        // Prune stale fodder slots
        for (int i = _fodderSlots.Count - 1; i >= 0; i--)
        {
            int idx = _fodderSlots[i];
            var fs = _inv.GetSlot(idx);
            if (idx == _enhanceTargetSlot || fs == null || fs.IsEmpty || _inv.IsSlotEquipped(idx))
                _fodderSlots.RemoveAt(i);
        }

        // Update 4 Equipped Quick-Select Buttons
        EquipmentSlotType[] eqOrder = { EquipmentSlotType.Sword, EquipmentSlotType.Shield, EquipmentSlotType.Armor, EquipmentSlotType.Pants };
        string[] eqShort = { "Sword", "Shield", "Armor", "Pants" };
        for (int i = 0; i < 4; i++)
        {
            var eqRef = _inv.GetEquippedRef(eqOrder[i]);
            var item = eqRef.ResolveItem();
            if (!eqRef.IsEmpty && item != null)
            {
                string enhStr = eqRef.enhancementLevel > 0 ? $" +{eqRef.enhancementLevel}" : "";
                _bsEquippedTexts[i].text = $"{eqShort[i]}\n{item.displayName}{enhStr}";
                _bsEquippedTexts[i].color = ItemData.GetTierColor(item.tier);
                _bsEquippedBtns[i].interactable = true;
            }
            else
            {
                _bsEquippedTexts[i].text = $"{eqShort[i]}\n(Empty)";
                _bsEquippedTexts[i].color = new Color(0.55f, 0.60f, 0.68f);
                _bsEquippedBtns[i].interactable = false;
            }
        }

        var targetSlot = _inv.GetSlot(_enhanceTargetSlot);
        if (targetSlot == null || targetSlot.IsEmpty || targetSlot.item == null || !targetSlot.item.IsEquippable)
        {
            _enhanceTargetSlot = -1;
            _fodderSlots.Clear();

            _bsTargetBorder.color = new Color(0.26f, 0.30f, 0.38f, 1f);
            _bsTargetIcon.gameObject.SetActive(false);
            _bsTargetTitleText.text = "Select Equipment to Enhance";
            _bsTargetTitleText.color = Color.white;
            _bsTargetStatDiffText.text = "Click an Equipped button above or any gear in your Bag grid to prepare +1 to +10 forging.";
            _bsClearTargetBtn.interactable = false;

            _bsOddsText.text = "Success Chance: --";
            _bsGoldCostText.text = $"Gold Required: --   (Balance: {playerGold:N0} G)";
            _bsFodderReqText.text = "Material Fodder: --";

            for (int i = 0; i < 3; i++)
            {
                _bsFodderSlotBorders[i].color = new Color(0.22f, 0.26f, 0.32f);
                _bsFodderSlotTexts[i].text = "Empty";
                _bsFodderSlotTexts[i].color = new Color(0.55f, 0.60f, 0.68f);
                _bsFodderSlotBtns[i].interactable = false;
            }

            _bsAutoPickBtn.interactable = false;
            _bsEnhanceBtn.interactable = false;
            _bsEnhanceBtnBg.color = new Color(0.18f, 0.20f, 0.25f, 1f);
            _bsEnhanceBtnText.text = "ENHANCE GEAR";
            return;
        }

        var tItem = targetSlot.item;
        int curLvl = targetSlot.enhancementLevel;
        Color tc = ItemData.GetTierColor(tItem.tier);
        _bsTargetBorder.color = tc;
        _bsTargetIcon.gameObject.SetActive(true);
        _bsTargetIcon.sprite = HudIcons.GetForItem(tItem);
        _bsTargetIcon.color = tc;
        _bsClearTargetBtn.interactable = true;

        bool isMax = curLvl >= EnhancementSystem.MaxEnhancementLevel;
        _bsTargetTitleText.color = tc;
        _bsTargetTitleText.text = $"{tItem.displayName}  +{curLvl} {(isMax ? "(MAX)" : $"-> +{curLvl + 1}")}";

        if (isMax)
        {
            _bsTargetStatDiffText.text = BuildItemStatSummary(tItem, curLvl, includeDesc: false) + "\nMaximum +10 Enhancement Reached!";
            _bsOddsText.text = "Success Chance: MAX LEVEL";
            _bsGoldCostText.text = $"Balance: {playerGold:N0} G";
            _bsFodderReqText.text = "No further Fodder required.";
            for (int i = 0; i < 3; i++)
            {
                _bsFodderSlotBorders[i].color = new Color(0.22f, 0.26f, 0.32f);
                _bsFodderSlotTexts[i].text = "MAX";
                _bsFodderSlotBtns[i].interactable = false;
            }
            _bsAutoPickBtn.interactable = false;
            _bsEnhanceBtn.interactable = false;
            _bsEnhanceBtnBg.color = new Color(0.25f, 0.22f, 0.14f, 1f);
            _bsEnhanceBtnText.text = "MAX +10 REACHED";
            return;
        }

        var spec = EnhancementSystem.GetNextSpec(tItem, curLvl);
        _bsTargetStatDiffText.text = BuildEnhanceStatPreview(tItem, curLvl, spec.targetLevel, spec.statBonusPercent);

        Color oddsCol = spec.successRate >= 0.70f ? new Color(0.38f, 0.96f, 0.62f)
            : (spec.successRate >= 0.40f ? new Color(1f, 0.84f, 0.28f) : new Color(1f, 0.45f, 0.30f));
        _bsOddsText.text = $"Success Chance: {Mathf.RoundToInt(spec.successRate * 100f)}%";
        _bsOddsText.color = oddsCol;

        bool hasGold = playerGold >= spec.goldCost;
        _bsGoldCostText.text = $"Gold Cost: {spec.goldCost:N0} G   (You have: {playerGold:N0} G)";
        _bsGoldCostText.color = hasGold ? new Color(1f, 0.86f, 0.32f) : new Color(1f, 0.42f, 0.35f);

        _bsFodderReqText.text =
            $"Fodder: {_fodderSlots.Count}/{spec.requiredFodderCount} Gear   (Allowed: {ItemData.GetTierDisplayName(spec.minFodderTier)} .. {ItemData.GetTierDisplayName(tItem.tier)})";
        _bsFodderReqText.color = _fodderSlots.Count >= spec.requiredFodderCount ? new Color(0.45f, 0.95f, 0.68f) : new Color(0.86f, 0.88f, 0.92f);

        for (int i = 0; i < 3; i++)
        {
            if (i >= spec.requiredFodderCount)
            {
                _bsFodderSlotBorders[i].color = new Color(0.16f, 0.18f, 0.22f);
                _bsFodderSlotTexts[i].text = "Locked";
                _bsFodderSlotTexts[i].color = new Color(0.42f, 0.45f, 0.52f);
                _bsFodderSlotBtns[i].interactable = false;
            }
            else if (i < _fodderSlots.Count)
            {
                var fs = _inv.GetSlot(_fodderSlots[i]);
                if (fs != null && !fs.IsEmpty && fs.item != null)
                {
                    Color fc = ItemData.GetTierColor(fs.item.tier);
                    _bsFodderSlotBorders[i].color = fc;
                    string fEnh = fs.enhancementLevel > 0 ? $" +{fs.enhancementLevel}" : "";
                    _bsFodderSlotTexts[i].text = $"{fs.item.displayName}{fEnh}\n[Click Remove]";
                    _bsFodderSlotTexts[i].color = fc;
                    _bsFodderSlotBtns[i].interactable = true;
                }
            }
            else
            {
                _bsFodderSlotBorders[i].color = new Color(0.38f, 0.32f, 0.18f);
                _bsFodderSlotTexts[i].text = $"Fodder #{i + 1}\n(Pick from Bag)";
                _bsFodderSlotTexts[i].color = new Color(0.75f, 0.78f, 0.85f);
                _bsFodderSlotBtns[i].interactable = false;
            }
        }

        bool ready = hasGold && _fodderSlots.Count >= spec.requiredFodderCount;
        _bsAutoPickBtn.interactable = true;
        _bsEnhanceBtn.interactable = ready;
        _bsEnhanceBtnBg.color = ready ? new Color(0.18f, 0.50f, 0.24f, 1f) : new Color(0.22f, 0.24f, 0.30f, 1f);
        _bsEnhanceBtnText.text = $"ENHANCE +{curLvl + 1}  ({Mathf.RoundToInt(spec.successRate * 100f)}%)";
    }

    private void RefreshInventoryGrid()
    {
        var slots = _inv.Slots;
        bool bsMode = _activeTab == HubTab.Blacksmith;
        InventorySlot targetSlot = bsMode ? _inv.GetSlot(_enhanceTargetSlot) : null;
        EnhancementSystem.EnhancementSpec? bsSpec = null;
        if (bsMode && targetSlot != null && !targetSlot.IsEmpty && targetSlot.item != null && targetSlot.enhancementLevel < EnhancementSystem.MaxEnhancementLevel)
            bsSpec = EnhancementSystem.GetNextSpec(targetSlot.item, targetSlot.enhancementLevel);

        for (int i = 0; i < _slotBgs.Length && i < slots.Count; i++)
        {
            var s = slots[i];
            bool selected = i == _selected;

            if (s.IsEmpty)
            {
                _slotBorders[i].color = selected ? new Color(0.75f, 0.78f, 0.85f) : new Color(0.20f, 0.23f, 0.30f);
                _slotBgs[i].color = selected ? new Color(0.18f, 0.21f, 0.28f) : new Color(0.10f, 0.12f, 0.16f);
                _slotIcons[i].gameObject.SetActive(false);
                _slotLabels[i].text = "";
                _slotQtyLabels[i].text = "";
                continue;
            }

            Color tierColor = ItemData.GetTierColor(s.item.tier);
            bool isEq = _inv.IsSlotEquipped(i);
            bool isTarget = bsMode && i == _enhanceTargetSlot;
            bool isFodder = bsMode && _fodderSlots.Contains(i);

            Color borderCol = selected ? Color.white : tierColor;
            if (isTarget) borderCol = new Color(0.28f, 0.96f, 0.85f);
            else if (isFodder) borderCol = new Color(1f, 0.56f, 0.18f);

            _slotBorders[i].color = borderCol;
            _slotBgs[i].color = selected
                ? Color.Lerp(new Color(0.14f, 0.16f, 0.22f), tierColor, 0.32f)
                : Color.Lerp(new Color(0.09f, 0.10f, 0.14f), tierColor, 0.14f);

            // Slightly dim ineligible items when choosing fodder in Blacksmith tab
            float alpha = 1f;
            if (bsMode && bsSpec.HasValue && !isTarget && !isFodder)
            {
                bool ok = !isEq && EnhancementSystem.IsEligibleFodder(targetSlot.item, targetSlot.instanceId, s, bsSpec.Value.minFodderTier);
                if (!ok) alpha = 0.45f;
            }

            _slotIcons[i].gameObject.SetActive(true);
            _slotIcons[i].sprite = HudIcons.GetForItem(s.item);
            _slotIcons[i].color = new Color(tierColor.r, tierColor.g, tierColor.b, alpha);

            string enhSuffix = s.enhancementLevel > 0 ? $" +{s.enhancementLevel}" : "";
            _slotLabels[i].text = s.item.displayName + enhSuffix;
            _slotLabels[i].color = Color.Lerp(Color.white, tierColor, 0.45f) * new Color(1f, 1f, 1f, alpha);

            if (isTarget)
            {
                _slotQtyLabels[i].text = "[TARGET]";
                _slotQtyLabels[i].color = new Color(0.28f, 0.98f, 0.85f);
            }
            else if (isFodder)
            {
                _slotQtyLabels[i].text = "[FODDER]";
                _slotQtyLabels[i].color = new Color(1f, 0.62f, 0.22f);
            }
            else if (isEq)
            {
                _slotQtyLabels[i].text = s.enhancementLevel > 0 ? $"[E]+{s.enhancementLevel}" : "[E]";
                _slotQtyLabels[i].color = new Color(0.35f, 0.98f, 0.62f);
            }
            else if (s.enhancementLevel > 0)
            {
                _slotQtyLabels[i].text = $"+{s.enhancementLevel}";
                _slotQtyLabels[i].color = new Color(0.45f, 0.92f, 1f);
            }
            else
            {
                _slotQtyLabels[i].text = s.quantity > 1 ? $"x{s.quantity}" : "";
                _slotQtyLabels[i].color = new Color(1f, 0.90f, 0.45f);
            }
        }
    }

    private void RefreshSelectedInspector()
    {
        var slots = _inv.Slots;
        InventorySlot sel = (_selected >= 0 && _selected < slots.Count) ? slots[_selected] : null;
        bool bsMode = _activeTab == HubTab.Blacksmith;

        Text useBtnLabel = _useBtn.GetComponentInChildren<Text>();
        if (useBtnLabel != null)
            useBtnLabel.text = bsMode ? "Auto-Pick" : "Use";

        if (sel == null || sel.IsEmpty)
        {
            _detailRarityBar.color = new Color(0.35f, 0.38f, 0.45f, 1f);
            _detailTitleText.text = "Select an item to inspect.";
            _detailTitleText.color = Color.white;
            _detailBodyText.text = bsMode
                ? "Blacksmith Tip: Pick Target Gear, attach matching Fodder, and forge up to +10!"
                : "Tip: Drag items to reorder slots or drop gear directly onto Equipment cards!";
            _useBtn.interactable = bsMode && _enhanceTargetSlot >= 0;
            _equipBtn.interactable = _dropBtn.interactable = false;
            _equipBtnText.text = bsMode ? "Set Target" : "Equip";
            return;
        }

        var item = sel.item;
        int enh = sel.enhancementLevel;
        Color selTierColor = ItemData.GetTierColor(item.tier);
        _detailRarityBar.color = selTierColor;
        _detailTitleText.color = selTierColor;
        string enhSuffix = enh > 0 ? $" +{enh}" : "";
        _detailTitleText.text = $"{item.displayName}{enhSuffix}   [{ItemData.GetTierDisplayName(item.tier)} {FormatItemCategory(item)}]";
        _detailBodyText.text = $"Slot #{_selected + 1}  |  Qty: {sel.quantity}/{item.MaxStackSize}   " +
                               BuildItemStatSummary(item, enh, includeDesc: true);

        if (bsMode)
        {
            _useBtn.interactable = _enhanceTargetSlot >= 0;
            _equipBtn.interactable = item.IsEquippable;
            _equipBtnText.text = "Set Target";
            _dropBtn.interactable = false;
        }
        else
        {
            _useBtn.interactable = item.IsUsable;
            _equipBtn.interactable = item.IsEquippable;
            _equipBtnText.text = _inv.IsSlotEquipped(_selected) ? "Unequip" : "Equip";
            _dropBtn.interactable = item.droppable && !_inv.IsSlotEquipped(_selected);
        }
    }

    private static string FormatItemCategory(ItemData item)
    {
        return item.GetEquipmentSlot() switch
        {
            EquipmentSlotType.Sword => "Sword",
            EquipmentSlotType.Shield => "Shield",
            EquipmentSlotType.Armor => "Armor",
            EquipmentSlotType.Pants => "Pants",
            _ => item.type.ToString()
        };
    }

    private static string BuildItemStatSummary(ItemData item, int enhancementLevel, bool includeDesc)
    {
        var sb = new System.Text.StringBuilder();
        int dmg = item.GetEnhancedDamage(enhancementLevel);
        int hp = item.GetEnhancedMaxHealth(enhancementLevel);
        int sta = item.GetEnhancedMaxStamina(enhancementLevel);
        int def = item.GetEnhancedDefense(enhancementLevel);
        float spd = item.GetEnhancedMoveSpeed(enhancementLevel);
        float crit = item.GetEnhancedCritChance(enhancementLevel);

        if (dmg != 0) sb.Append($"+{dmg} ATK  ");
        if (hp != 0) sb.Append($"+{hp} Max HP  ");
        if (sta != 0) sb.Append($"+{sta} Max STA  ");
        if (def != 0) sb.Append($"+{def} DEF  ");
        if (spd > 0f) sb.Append($"+{spd:0.0} SPD  ");
        if (crit > 0f) sb.Append($"+{crit * 100f:0}% CRIT  ");
        if (item.healAmount > 0) sb.Append($"Heals +{item.healAmount} HP  ");
        if (item.staminaRestoreAmount > 0) sb.Append($"+{item.staminaRestoreAmount} STA  ");
        if (item.attackBuffBonus > 0) sb.Append($"+{item.attackBuffBonus} ATK ({item.attackBuffDuration:0}s)  ");

        if (includeDesc && !string.IsNullOrEmpty(item.description))
        {
            if (sb.Length > 0) sb.Append("\n");
            sb.Append(item.description);
        }
        return sb.ToString().Trim();
    }

    private static string BuildEnhanceStatPreview(ItemData item, int curLvl, int nextLvl, float nextBonusPct)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"Upgrade Multiplier: +{Mathf.RoundToInt(nextBonusPct * 100f)}% Total Stats\n");

        if (item.damageBonus > 0)
            sb.Append($"ATK: {item.GetEnhancedDamage(curLvl)} -> {item.GetEnhancedDamage(nextLvl)}   ");
        if (item.maxHealthBonus > 0)
            sb.Append($"HP: {item.GetEnhancedMaxHealth(curLvl)} -> {item.GetEnhancedMaxHealth(nextLvl)}   ");
        if (item.maxStaminaBonus > 0)
            sb.Append($"STA: {item.GetEnhancedMaxStamina(curLvl)} -> {item.GetEnhancedMaxStamina(nextLvl)}   ");
        if (item.defenseBonus > 0)
            sb.Append($"DEF: {item.GetEnhancedDefense(curLvl)} -> {item.GetEnhancedDefense(nextLvl)}   ");
        if (item.critChanceBonus > 0f)
            sb.Append($"CRIT: {item.GetEnhancedCritChance(curLvl) * 100f:0.#}% -> {item.GetEnhancedCritChance(nextLvl) * 100f:0.#}%   ");
        if (item.moveSpeedBonus > 0f)
            sb.Append($"SPD: {item.GetEnhancedMoveSpeed(curLvl):0.0} -> {item.GetEnhancedMoveSpeed(nextLvl):0.0}");

        return sb.ToString().Trim();
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
