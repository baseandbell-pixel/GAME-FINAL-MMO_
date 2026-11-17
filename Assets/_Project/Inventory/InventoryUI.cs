using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// หน้าต่างจัดการตัวละคร กระเป๋า ช่างตีเหล็ก และสารานุกรมไอเทมสไตล์ Action-MMORPG (Dark Obsidian + Brushed Gold + Jade Accent):
///   - ขยายขนาดหน้าต่างและช่องกระเป๋าให้ใหญ่ อ่านง่าย และลากวางสะดวก (1280x780, SlotSize 94px)
///   - ซ่อนอุปกรณ์ที่สวมใส่อยู่ออกจากตารางกระเป๋า (แสดงเฉพาะในช่อง Equipment 4 ช่อง และคืนกลับช่องกระเป๋าเมื่อถอด)
///   - แถบแสดงโอกาสตีบวกสำเร็จแบบไดนามิก (% Progress Bar) ในหน้า Blacksmith ตามระดับวัตถุดิบที่เลือก
///   - แท็บ Item Guide / Compendium แยกหมวดหมู่ Weapons, Shields, Armor, Pants, Consumables พร้อมวิธีใช้งานและสเตตัสครบถ้วน
///   - รองรับการลากไอเทมจากกระเป๋าไปยังแถบ Hotbar (1-6) ด้านล่างหน้าจอ
/// </summary>
public class InventoryUI : MonoBehaviour
{
    public enum HubTab
    {
        Character = 0,
        Blacksmith = 1,
        Compendium = 2
    }

    public enum CompendiumCategory
    {
        All = 0,
        Weapons = 1,
        Shields = 2,
        Armor = 3,
        Pants = 4,
        Consumables = 5
    }

    public static InventoryUI Instance { get; private set; }

    private const int Columns = 6;
    private const float SlotSize = 94f;
    private const float SlotGap = 12f;
    private const float PanelWidth = 1280f;
    private const float PanelHeight = 780f;

    public bool IsOpen => _open;
    public bool IsBlacksmithTabActive => _activeTab == HubTab.Blacksmith;
    public bool IsCompendiumTabActive => _activeTab == HubTab.Compendium;
    public int SelectedSlotIndex => _selected;
    public int EnhanceTargetSlotIndex => _enhanceTargetSlot;
    public IReadOnlyList<int> SelectedFodderIndices => _fodderSlots;
    public string HeaderStatsText => _headerStatsText != null ? _headerStatsText.text : string.Empty;
    public string LastEnhanceFeedback => _enhanceFeedbackText != null ? _enhanceFeedbackText.text : string.Empty;

    // คืนค่าช่องกระเป๋าจริงที่กำลังถูกลากอยู่ (ใช้เชื่อมกับ HotbarUI)
    public int ActiveDragInventorySlotIndex => _draggingFromIndex;

    // จำนวนไอเทมที่แสดงอยู่ในตารางกระเป๋า (ไม่นับรวมชิ้นที่สวมใส่อยู่)
    public int VisibleBagItemCount
    {
        get
        {
            if (_inv == null) return 0;
            int n = 0;
            for (int i = 0; i < _inv.Slots.Count; i++)
            {
                if (!_inv.Slots[i].IsEmpty && !_inv.IsSlotEquipped(i)) n++;
            }
            return n;
        }
    }

    private InventoryManager _inv;
    private GameObject _panel;
    private RectTransform _panelInnerRect;
    private HubTab _activeTab = HubTab.Character;

    // ปุ่มแท็บด้านบน
    private Image _tabCharacterBg;
    private Text _tabCharacterText;
    private Image _tabBlacksmithBg;
    private Text _tabBlacksmithText;
    private Image _tabCompendiumBg;
    private Text _tabCompendiumText;
    private Text _headerStatsText;

    // คอลัมน์หลักตามแต่ละแท็บ
    private GameObject _characterColumn;
    private GameObject _blacksmithColumn;
    private GameObject _inventoryRightColumn;
    private GameObject _compendiumColumn;

    // ข้อมูลและวิดเจ็ตในแท็บ Character
    private Text _totalStatsText;
    private Text _statPointsText;
    private Text _detailTitleText;
    private Text _detailBodyText;
    private Image _detailRarityBar;
    private Button _useBtn, _equipBtn, _dropBtn;
    private Text _equipBtnText;

    // ตารางช่องกระเป๋า 24 ช่อง พร้อมแมปตำแหน่งที่แสดงผล -> ตำแหน่งจริงใน InventoryManager
    private Image[] _slotBorders;
    private Image[] _slotBgs;
    private Image[] _slotIcons;
    private Text[] _slotLabels;
    private Text[] _slotQtyLabels;
    private int[] _visibleToRealSlot;

    // ช่องสวมใส่อุปกรณ์ 4 ช่อง (Sword, Shield, Armor, Pants)
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

    // แถวอัปเกรดสเตตัส 4 ค่า
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

    // วิดเจ็ตแท็บ Blacksmith (+Gear) พร้อมแถบ % สำเร็จแบบไดนามิก
    private readonly Button[] _bsEquippedBtns = new Button[4];
    private readonly Text[] _bsEquippedTexts = new Text[4];
    private Image _bsTargetBorder;
    private Image _bsTargetIcon;
    private Text _bsTargetTitleText;
    private Text _bsTargetStatDiffText;
    private Button _bsClearTargetBtn;
    private Text _bsOddsText;
    private Image _bsOddsBarBg;
    private Image _bsOddsBarFill;
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

    // วิดเจ็ตแท็บ Item Compendium / Guide
    private CompendiumCategory _compCategory = CompendiumCategory.All;
    private readonly Image[] _compCatBgs = new Image[6];
    private readonly Text[] _compCatTexts = new Text[6];
    private Transform _compListContent;
    private readonly List<GameObject> _compEntryObjects = new List<GameObject>();
    private string _selectedCompItemId;
    private Image _compDetailBorder;
    private Image _compDetailIcon;
    private Text _compDetailTitle;
    private Text _compDetailSubtitle;
    private Text _compDetailUsage;
    private Text _compDetailStats;

    private int _enhanceTargetSlot = -1;
    private readonly List<int> _fodderSlots = new List<int>();

    // Tooltip ลอยเมื่อเอาเมาส์ชี้
    private RectTransform _tooltipRect;
    private Image _tooltipBorder;
    private Text _tooltipTitle;
    private Text _tooltipBody;
    private int _hoveredCellIndex = -1;

    // สถานะ Drag & Drop และ Ghost Icon
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

    // ------------------------------------------------------------------ การสร้างโครงสร้าง UI ทั้งหมด

    private void Init(InventoryManager inv)
    {
        Instance = this;
        _inv = inv;
        _font = UITheme.GetFont();
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

        // กรอบหลักสี Dark Obsidian ขอบทอง Brushed Gold (1280 x 780)
        var outerFrame = MakeImage(canvasGo.transform, "CharacterHubFrame", UITheme.BrushedGold);
        UITheme.ApplySlicedStyle(outerFrame, UITheme.BrushedGold);
        UITheme.AddShadow(outerFrame.gameObject, 3f);
        _panel = outerFrame.gameObject;
        var pr = outerFrame.rectTransform;
        pr.anchorMin = pr.anchorMax = pr.pivot = new Vector2(0.5f, 0.5f);
        pr.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        pr.anchoredPosition = new Vector2(0f, 24f);

        var inner = MakeImage(_panel.transform, "PanelInner", UITheme.ObsidianDeep);
        UITheme.ApplySlicedStyle(inner, UITheme.ObsidianDeep);
        _panelInnerRect = inner.rectTransform;
        FillWithPadding(_panelInnerRect, 3f);

        // ============================================================
        // แถบแท็บด้านบน (World Map | Character | Blacksmith | Item Guide)
        // ============================================================
        var navBg = MakeImage(inner.transform, "TopNavigationTabs", UITheme.ObsidianHeader);
        Place(navBg.rectTransform, 0f, 0f, PanelWidth - 6f, 54f);

        MakeTabButton(navBg.transform, "TabWorldMap", "[ World Map (M) ]", 14f, 166f, false, SwitchToWorldMapTab, out _, out _);
        MakeTabButton(navBg.transform, "TabCharacter", "[ Character & Bag ]", 188f, 188f, true,
            () => SetActiveTab(HubTab.Character), out _tabCharacterBg, out _tabCharacterText);
        MakeTabButton(navBg.transform, "TabBlacksmith", "[ Blacksmith (+Gear) ]", 384f, 196f, false,
            () => SetActiveTab(HubTab.Blacksmith), out _tabBlacksmithBg, out _tabBlacksmithText);
        MakeTabButton(navBg.transform, "TabCompendium", "[ Item Guide ]", 588f, 164f, false,
            () => SetActiveTab(HubTab.Compendium), out _tabCompendiumBg, out _tabCompendiumText);
        MakeTabButton(navBg.transform, "CloseBtn", "Close [I / Esc]", 1122f, 138f, false, () => SetOpen(false), out _, out _);

        _headerStatsText = MakeLabel(navBg.transform, "HeaderStats", "", 14, FontStyle.Bold, TextAnchor.MiddleRight,
            UITheme.BrushedGoldBright, 760f, 10f, 350f, 34f);
        UITheme.AddShadow(_headerStatsText.gameObject, 1f);

        // ============================================================
        // คอลัมน์ซ้าย A: CHARACTER STATS, EQUIPPED GEAR & ATTRIBUTES
        // ============================================================
        var leftCol = MakeImage(inner.transform, "CharacterColumn", UITheme.ObsidianPanel);
        UITheme.ApplySlicedStyle(leftCol, UITheme.ObsidianPanel);
        _characterColumn = leftCol.gameObject;
        Place(leftCol.rectTransform, 16f, 64f, 564f, 668f);

        var eqHeader = MakeLabel(leftCol.transform, "EquipSectionTitle",
            "EQUIPPED GEAR  (Equipped items move out of Bag into these 4 slots)", 13, FontStyle.Bold, TextAnchor.MiddleLeft,
            UITheme.BrushedGoldBright, 16f, 12f, 532f, 24f);
        UITheme.AddShadow(eqHeader.gameObject, 1f);

        BuildEquipSlotWidget(leftCol.transform, 0, EquipmentSlotType.Sword, "WEAPON", 16f, 42f);
        BuildEquipSlotWidget(leftCol.transform, 1, EquipmentSlotType.Shield, "SHIELD", 290f, 42f);
        BuildEquipSlotWidget(leftCol.transform, 2, EquipmentSlotType.Armor, "ARMOR", 16f, 148f);
        BuildEquipSlotWidget(leftCol.transform, 3, EquipmentSlotType.Pants, "PANTS", 290f, 148f);

        var totalBox = MakeImage(leftCol.transform, "TotalStatsBox", UITheme.ObsidianDeep);
        UITheme.ApplySlicedStyle(totalBox, UITheme.ObsidianDeep);
        Place(totalBox.rectTransform, 16f, 260f, 532f, 108f);
        MakeLabel(totalBox.transform, "TotalTitle", "TOTAL CHARACTER STATS  (Base + Level + Enhanced Gear)", 13, FontStyle.Bold, TextAnchor.MiddleLeft,
            UITheme.JadeAccent, 12f, 8f, 508f, 22f);
        _totalStatsText = MakeLabel(totalBox.transform, "TotalBody", "", 15, FontStyle.Bold, TextAnchor.UpperLeft,
            UITheme.TextPrimary, 12f, 36f, 508f, 66f);

        var allocBox = MakeImage(leftCol.transform, "StatAllocBox", UITheme.ObsidianDeep);
        UITheme.ApplySlicedStyle(allocBox, UITheme.ObsidianDeep);
        Place(allocBox.rectTransform, 16f, 380f, 532f, 272f);
        MakeLabel(allocBox.transform, "AllocTitle", "ATTRIBUTES & UPGRADE", 14, FontStyle.Bold, TextAnchor.MiddleLeft,
            UITheme.BrushedGoldBright, 12f, 10f, 260f, 24f);
        _statPointsText = MakeLabel(allocBox.transform, "UnspentPts", "Pts: 0", 14, FontStyle.Bold, TextAnchor.MiddleRight,
            UITheme.BrushedGoldBright, 276f, 10f, 242f, 24f);

        BuildStatRow(allocBox.transform, 0, PlayerStatType.STR, "STR", "+4 ATK", 48f);
        BuildStatRow(allocBox.transform, 1, PlayerStatType.VIT, "VIT", "+15 HP", 102f);
        BuildStatRow(allocBox.transform, 2, PlayerStatType.END, "END", "+12 STA", 156f);
        BuildStatRow(allocBox.transform, 3, PlayerStatType.AGI, "AGI", "+3% CRIT", 210f);

        // ============================================================
        // คอลัมน์ซ้าย B: BLACKSMITH GEAR ENHANCEMENT (+1 TO +10)
        // ============================================================
        BuildBlacksmithColumn(inner.transform);

        // ============================================================
        // คอลัมน์ขวา: BAG GRID (ซ่อนไอเทมที่สวมใส่อยู่ + Drag ไป Hotbar 1-6 ได้)
        // ============================================================
        var rightCol = MakeImage(inner.transform, "InventoryColumn", UITheme.ObsidianPanel);
        UITheme.ApplySlicedStyle(rightCol, UITheme.ObsidianPanel);
        _inventoryRightColumn = rightCol.gameObject;
        Place(rightCol.rectTransform, 594f, 64f, 664f, 668f);

        var bagTitle = MakeLabel(rightCol.transform, "InvTitle",
            "INVENTORY BAG  (Drag to Reorder / Equip / Drop on Hotbar 1-6)", 13, FontStyle.Bold, TextAnchor.MiddleLeft,
            UITheme.BrushedGoldBright, 18f, 12f, 628f, 24f);
        UITheme.AddShadow(bagTitle.gameObject, 1f);

        int count = _inv.Capacity;
        _slotBorders = new Image[count];
        _slotBgs = new Image[count];
        _slotIcons = new Image[count];
        _slotLabels = new Text[count];
        _slotQtyLabels = new Text[count];
        _visibleToRealSlot = new int[count];

        for (int i = 0; i < count; i++)
        {
            _visibleToRealSlot[i] = i;
            int col = i % Columns, row = i / Columns;
            float x = 20f + col * (SlotSize + SlotGap);
            float y = 42f + row * (SlotSize + SlotGap);

            var borderImg = MakeImage(rightCol.transform, "SlotBorder" + i, UITheme.BrushedGoldDark);
            UITheme.ApplySlicedStyle(borderImg, UITheme.BrushedGoldDark);
            Place(borderImg.rectTransform, x, y, SlotSize, SlotSize);

            var bgImg = MakeImage(borderImg.transform, "SlotBg", UITheme.ObsidianSlotEmpty);
            UITheme.ApplySlicedStyle(bgImg, UITheme.ObsidianSlotEmpty);
            FillWithPadding(bgImg.rectTransform, 2.5f);

            var btn = bgImg.gameObject.AddComponent<Button>();
            btn.targetGraphic = bgImg;
            int capturedCell = i;
            btn.onClick.AddListener(() => OnGridCellClicked(capturedCell));

            var relay = bgImg.gameObject.AddComponent<SlotDragRelay>();
            relay.owner = this;
            relay.cellIndex = capturedCell;

            var iconImg = MakeImage(bgImg.transform, "Icon", Color.white);
            iconImg.raycastTarget = false;
            iconImg.preserveAspect = true;
            Place(iconImg.rectTransform, 22f, 12f, 44f, 44f);
            iconImg.gameObject.SetActive(false);

            var label = MakeLabel(bgImg.transform, "Label", "", 11, FontStyle.Bold, TextAnchor.LowerCenter,
                Color.white, 3f, 56f, SlotSize - 12f, 30f);

            var qty = MakeLabel(bgImg.transform, "Qty", "", 12, FontStyle.Bold, TextAnchor.UpperRight,
                UITheme.BrushedGoldBright, 4f, 4f, SlotSize - 14f, 18f);
            UITheme.AddShadow(qty.gameObject, 1f);

            _slotBorders[i] = borderImg;
            _slotBgs[i] = bgImg;
            _slotIcons[i] = iconImg;
            _slotLabels[i] = label;
            _slotQtyLabels[i] = qty;
        }

        // กล่องแสดงรายละเอียดไอเทมที่เลือก + ปุ่มจัดการ (ด้านล่างคอลัมน์ขวา)
        var detailFrame = MakeImage(rightCol.transform, "SelectedDetailBox", UITheme.ObsidianDeep);
        UITheme.ApplySlicedStyle(detailFrame, UITheme.ObsidianDeep);
        Place(detailFrame.rectTransform, 20f, 470f, 624f, 130f);

        _detailRarityBar = MakeImage(detailFrame.transform, "RarityStripe", new Color(0.35f, 0.38f, 0.45f, 1f));
        Place(_detailRarityBar.rectTransform, 0f, 0f, 6f, 130f);

        _detailTitleText = MakeLabel(detailFrame.transform, "DetailTitle", "Select an item to inspect.", 16, FontStyle.Bold, TextAnchor.MiddleLeft,
            UITheme.TextPrimary, 16f, 8f, 594f, 26f);
        _detailBodyText = MakeLabel(detailFrame.transform, "DetailBody", "", 13, FontStyle.Normal, TextAnchor.UpperLeft,
            UITheme.TextSecondary, 16f, 38f, 594f, 86f);

        _useBtn = MakeActionButton(rightCol.transform, "UseBtn", "Use", 20f, 610f, 198f, 42f, OnUseOrAutoClick, out _);
        _equipBtn = MakeActionButton(rightCol.transform, "EquipBtn", "Equip", 233f, 610f, 198f, 42f, OnEquipOrTargetClick, out _equipBtnText);
        _dropBtn = MakeActionButton(rightCol.transform, "DropBtn", "Drop", 446f, 610f, 198f, 42f, () =>
        {
            if (_selected >= 0) _inv.DropItem(_selected);
        }, out _);

        // ============================================================
        // แท็บสารานุกรมไอเทม (ITEM COMPENDIUM / GUIDE)
        // ============================================================
        BuildCompendiumColumn(inner.transform);

        // แถบคำแนะนำด้านล่างสุดของหน้าต่าง
        MakeLabel(inner.transform, "Hint",
            "Tiers: Normal (Grey) | Rare (Blue) | Epic (Purple) | Legendary (Gold)   •   Drag items onto Hotbar [1-6] below for quick access   •   [M] World Map",
            12, FontStyle.Normal, TextAnchor.MiddleCenter, UITheme.TextSecondary, 16f, 740f, PanelWidth - 38f, 28f);

        // ============================================================
        // TOOLTIP ลอยเมื่อเอาเมาส์ชี้
        // ============================================================
        var tipBorder = MakeImage(_panel.transform, "HoverTooltip", UITheme.BrushedGold);
        UITheme.ApplySlicedStyle(tipBorder, UITheme.BrushedGold);
        tipBorder.raycastTarget = false;
        _tooltipBorder = tipBorder;
        _tooltipRect = tipBorder.rectTransform;
        _tooltipRect.anchorMin = _tooltipRect.anchorMax = new Vector2(0f, 1f);
        _tooltipRect.pivot = new Vector2(0f, 1f);
        _tooltipRect.sizeDelta = new Vector2(310f, 150f);

        var tipBg = MakeImage(_tooltipRect, "TipBg", UITheme.ObsidianDeep);
        UITheme.ApplySlicedStyle(tipBg, UITheme.ObsidianDeep);
        tipBg.raycastTarget = false;
        FillWithPadding(tipBg.rectTransform, 2f);

        _tooltipTitle = MakeLabel(tipBg.transform, "TipTitle", "", 14, FontStyle.Bold, TextAnchor.UpperLeft,
            Color.white, 10f, 8f, 288f, 38f);
        _tooltipBody = MakeLabel(tipBg.transform, "TipBody", "", 12, FontStyle.Normal, TextAnchor.UpperLeft,
            UITheme.TextSecondary, 10f, 48f, 288f, 94f);
        _tooltipRect.gameObject.SetActive(false);

        // ============================================================
        // DRAG GHOST ICON
        // ============================================================
        _dragGhostBorder = MakeImage(_panel.transform, "DragGhost", Color.white);
        _dragGhostBorder.raycastTarget = false;
        _dragGhostObj = _dragGhostBorder.gameObject;
        _dragGhostRect = _dragGhostBorder.rectTransform;
        _dragGhostRect.sizeDelta = new Vector2(62f, 62f);
        var ghostInner = MakeImage(_dragGhostRect, "Inner", UITheme.ObsidianPanel);
        ghostInner.raycastTarget = false;
        FillWithPadding(ghostInner.rectTransform, 2f);
        _dragGhostIcon = MakeImage(ghostInner.transform, "Icon", Color.white);
        _dragGhostIcon.raycastTarget = false;
        _dragGhostIcon.preserveAspect = true;
        FillWithPadding(_dragGhostIcon.rectTransform, 6f);
        _dragGhostObj.SetActive(false);

        ApplyActiveTabVisuals();
        _panel.SetActive(false);
        _inv.Changed += Refresh;
        Debug.Log("[InventoryUI] Overhauled Dark Obsidian/Gold/Jade Hub built (1280x780, 94px slots, Equipped Filtering, Dynamic Blacksmith %, Compendium).");
    }

    private void BuildBlacksmithColumn(Transform parent)
    {
        var bsCol = MakeImage(parent, "BlacksmithColumn", UITheme.ObsidianPanel);
        UITheme.ApplySlicedStyle(bsCol, UITheme.ObsidianPanel);
        _blacksmithColumn = bsCol.gameObject;
        Place(bsCol.rectTransform, 16f, 64f, 564f, 668f);

        var bsTitle = MakeLabel(bsCol.transform, "BsTitle", "BLACKSMITH ENHANCEMENT  (+1 to +10)", 14, FontStyle.Bold, TextAnchor.MiddleLeft,
            UITheme.BrushedGoldBright, 16f, 12f, 532f, 24f);
        UITheme.AddShadow(bsTitle.gameObject, 1f);

        // ปุ่มเลือกอุปกรณ์ที่สวมใส่อยู่ 4 ช่องอย่างรวดเร็ว
        EquipmentSlotType[] eqOrder = { EquipmentSlotType.Sword, EquipmentSlotType.Shield, EquipmentSlotType.Armor, EquipmentSlotType.Pants };
        string[] eqNames = { "Sword", "Shield", "Armor", "Pants" };
        for (int i = 0; i < 4; i++)
        {
            EquipmentSlotType slotType = eqOrder[i];
            float bx = 16f + i * 134f;
            _bsEquippedBtns[i] = MakeActionButton(bsCol.transform, "BsEqBtn_" + eqNames[i], eqNames[i], bx, 42f, 128f, 52f, () =>
            {
                SelectEquippedAsEnhanceTarget(slotType);
            }, out _bsEquippedTexts[i]);
            _bsEquippedTexts[i].fontSize = 12;
        }

        // กล่องพรีวิวอุปกรณ์เป้าหมาย
        _bsTargetBorder = MakeImage(bsCol.transform, "TargetPreviewBorder", UITheme.BrushedGoldDark);
        UITheme.ApplySlicedStyle(_bsTargetBorder, UITheme.BrushedGoldDark);
        Place(_bsTargetBorder.rectTransform, 16f, 104f, 532f, 150f);
        var targetBg = MakeImage(_bsTargetBorder.transform, "TargetBg", UITheme.ObsidianDeep);
        UITheme.ApplySlicedStyle(targetBg, UITheme.ObsidianDeep);
        FillWithPadding(targetBg.rectTransform, 2f);

        var iconFrame = MakeImage(targetBg.transform, "IconFrame", UITheme.ObsidianSlot);
        UITheme.ApplySlicedStyle(iconFrame, UITheme.ObsidianSlot);
        Place(iconFrame.rectTransform, 14f, 16f, 76f, 76f);
        _bsTargetIcon = MakeImage(iconFrame.transform, "Icon", Color.white);
        _bsTargetIcon.preserveAspect = true;
        FillWithPadding(_bsTargetIcon.rectTransform, 10f);
        _bsTargetIcon.gameObject.SetActive(false);

        _bsTargetTitleText = MakeLabel(targetBg.transform, "TargetTitle", "Select Equipment from Bag or Equipped Bar", 15, FontStyle.Bold, TextAnchor.UpperLeft,
            Color.white, 102f, 12f, 340f, 38f);
        _bsTargetStatDiffText = MakeLabel(targetBg.transform, "TargetStats", "Pick any gear to inspect +Next level stats, Gold cost, and dynamic Fodder %.", 12, FontStyle.Normal, TextAnchor.UpperLeft,
            UITheme.TextSecondary, 102f, 54f, 416f, 88f);

        _bsClearTargetBtn = MakeActionButton(targetBg.transform, "ClearTargetBtn", "Clear", 452f, 10f, 66f, 28f, () =>
        {
            _enhanceTargetSlot = -1;
            _fodderSlots.Clear();
            SetEnhanceFeedback("Select Target Gear to enhance.", UITheme.TextSecondary);
            Refresh();
        }, out _);

        // กล่องเงื่อนไขและแถบ % โอกาสตีบวกสำเร็จแบบไดนามิก
        var reqBox = MakeImage(bsCol.transform, "RequirementsBox", UITheme.ObsidianDeep);
        UITheme.ApplySlicedStyle(reqBox, UITheme.ObsidianDeep);
        Place(reqBox.rectTransform, 16f, 264f, 532f, 142f);

        MakeLabel(reqBox.transform, "ReqHeader", "ENHANCEMENT COST & DYNAMIC SUCCESS RATE", 13, FontStyle.Bold, TextAnchor.MiddleLeft,
            UITheme.JadeAccent, 12f, 8f, 508f, 22f);

        _bsOddsText = MakeLabel(reqBox.transform, "OddsText", "Success Chance: --", 14, FontStyle.Bold, TextAnchor.MiddleLeft,
            Color.white, 12f, 32f, 508f, 24f);

        // แถบ Progress Bar แสดง % โอกาสตีบวกสำเร็จตาม Tier วัตถุดิบ
        _bsOddsBarBg = MakeImage(reqBox.transform, "OddsBarBg", new Color(0.12f, 0.14f, 0.19f, 1f));
        UITheme.ApplySlicedStyle(_bsOddsBarBg, new Color(0.12f, 0.14f, 0.19f, 1f));
        Place(_bsOddsBarBg.rectTransform, 12f, 60f, 508f, 20f);

        _bsOddsBarFill = MakeImage(_bsOddsBarBg.transform, "OddsBarFill", UITheme.JadeAccent);
        _bsOddsBarFill.sprite = UITheme.GetFillSprite();
        _bsOddsBarFill.type = Image.Type.Filled;
        _bsOddsBarFill.fillMethod = Image.FillMethod.Horizontal;
        _bsOddsBarFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        _bsOddsBarFill.fillAmount = 0f;
        FillWithPadding(_bsOddsBarFill.rectTransform, 2f);

        _bsGoldCostText = MakeLabel(reqBox.transform, "GoldCostText", "Gold Required: --", 13, FontStyle.Bold, TextAnchor.MiddleLeft,
            UITheme.BrushedGoldBright, 12f, 86f, 508f, 24f);
        _bsFodderReqText = MakeLabel(reqBox.transform, "FodderReqText", "Material Fodder: --", 13, FontStyle.Normal, TextAnchor.MiddleLeft,
            UITheme.TextSecondary, 12f, 112f, 508f, 24f);

        // ช่องใส่วัตถุดิบ (Fodder Slots สูงสุด 3 ช่อง)
        var fodderStrip = MakeImage(bsCol.transform, "FodderStrip", UITheme.ObsidianDeep);
        UITheme.ApplySlicedStyle(fodderStrip, UITheme.ObsidianDeep);
        Place(fodderStrip.rectTransform, 16f, 416f, 532f, 100f);
        MakeLabel(fodderStrip.transform, "FodderTitle", "SACRIFICE FODDER SLOTS  (Higher tier fodder boosts Success %)", 12, FontStyle.Bold, TextAnchor.MiddleLeft,
            new Color(1f, 0.76f, 0.34f), 12f, 8f, 508f, 20f);

        for (int i = 0; i < 3; i++)
        {
            int fodderIdx = i;
            float fx = 12f + i * 171f;
            var fBorder = MakeImage(fodderStrip.transform, "FodderSlotBorder" + i, UITheme.BrushedGoldDark);
            UITheme.ApplySlicedStyle(fBorder, UITheme.BrushedGoldDark);
            Place(fBorder.rectTransform, fx, 32f, 164f, 58f);
            var fBg = MakeImage(fBorder.transform, "Bg", UITheme.ObsidianSlot);
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

            var fTxt = MakeLabel(fBg.transform, "Text", "Empty", 12, FontStyle.Bold, TextAnchor.MiddleCenter,
                UITheme.TextMuted, 4f, 4f, 152f, 46f);

            _bsFodderSlotBorders[i] = fBorder;
            _bsFodderSlotTexts[i] = fTxt;
            _bsFodderSlotBtns[i] = fBtn;
        }

        _bsAutoPickBtn = MakeActionButton(bsCol.transform, "AutoPickBtn", "Auto-Pick Fodder", 16f, 526f, 186f, 42f, AutoPickEligibleFodder, out _);
        _bsEnhanceBtn = MakeActionButton(bsCol.transform, "EnhanceBtn", "ENHANCE GEAR", 214f, 526f, 334f, 42f, ExecuteEnhanceClick, out _bsEnhanceBtnText);
        _bsEnhanceBtnBg = _bsEnhanceBtn.GetComponent<Image>();
        _bsEnhanceBtnBg.color = UITheme.JadeButton;

        var fbBox = MakeImage(bsCol.transform, "EnhanceFeedbackBox", UITheme.ObsidianDeep);
        Place(fbBox.rectTransform, 16f, 578f, 532f, 76f);
        _enhanceFeedbackText = MakeLabel(fbBox.transform, "FeedbackText",
            "Select Equipment and Fodder (Normal..Legendary) to see dynamic Success % and forge +1..+10.",
            13, FontStyle.Bold, TextAnchor.MiddleLeft, UITheme.TextSecondary, 12f, 6f, 508f, 64f);
    }

    private void BuildCompendiumColumn(Transform parent)
    {
        var compRoot = MakeImage(parent, "CompendiumColumn", UITheme.ObsidianPanel);
        UITheme.ApplySlicedStyle(compRoot, UITheme.ObsidianPanel);
        _compendiumColumn = compRoot.gameObject;
        Place(compRoot.rectTransform, 16f, 64f, PanelWidth - 38f, 668f);

        var title = MakeLabel(compRoot.transform, "CompTitle",
            "ITEM COMPENDIUM & EQUIPMENT GUIDE  (Browse all gear, rarity stats & usage tips)", 15, FontStyle.Bold, TextAnchor.MiddleLeft,
            UITheme.BrushedGoldBright, 18f, 12f, 1180f, 26f);
        UITheme.AddShadow(title.gameObject, 1f);

        // ปุ่มกรองหมวดหมู่ทั้ง 6 หมวด
        string[] catNames = { "All Items", "Weapons", "Shields", "Armor", "Pants", "Consumables" };
        for (int i = 0; i < 6; i++)
        {
            CompendiumCategory cat = (CompendiumCategory)i;
            MakeTabButton(compRoot.transform, "CompCat_" + i, catNames[i], 18f + i * 198f, 188f, i == 0, () =>
            {
                _compCategory = cat;
                RefreshCompendium();
            }, out _compCatBgs[i], out _compCatTexts[i]);
            Place(_compCatBgs[i].rectTransform, 18f + i * 198f, 44f, 188f, 34f);
        }

        // รายการไอเทมแบบเลื่อนได้ด้านซ้าย
        var listFrame = MakeImage(compRoot.transform, "CompListFrame", UITheme.ObsidianDeep);
        UITheme.ApplySlicedStyle(listFrame, UITheme.ObsidianDeep);
        Place(listFrame.rectTransform, 18f, 88f, 554f, 562f);

        var scrollGo = new GameObject("ScrollView", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect));
        scrollGo.transform.SetParent(listFrame.transform, false);
        FillWithPadding((RectTransform)scrollGo.transform, 4f);
        var maskImg = scrollGo.GetComponent<Image>();
        maskImg.color = new Color(1f, 1f, 1f, 0.02f);
        scrollGo.GetComponent<Mask>().showMaskGraphic = false;

        var contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentGo.transform.SetParent(scrollGo.transform, false);
        var contentRt = (RectTransform)contentGo.transform;
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = new Vector2(0f, 0f);

        var vlg = contentGo.GetComponent<VerticalLayoutGroup>();
        vlg.spacing = 6f;
        vlg.padding = new RectOffset(6, 6, 6, 6);
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        var csf = contentGo.GetComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = scrollGo.GetComponent<ScrollRect>();
        scroll.viewport = (RectTransform)scrollGo.transform;
        scroll.content = contentRt;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.scrollSensitivity = 28f;
        _compListContent = contentRt;

        // กล่องรายละเอียดคู่มือไอเทมด้านขวา
        _compDetailBorder = MakeImage(compRoot.transform, "CompDetailBorder", UITheme.BrushedGold);
        UITheme.ApplySlicedStyle(_compDetailBorder, UITheme.BrushedGold);
        Place(_compDetailBorder.rectTransform, 588f, 88f, 636f, 562f);

        var detailBg = MakeImage(_compDetailBorder.transform, "CompDetailBg", UITheme.ObsidianDeep);
        UITheme.ApplySlicedStyle(detailBg, UITheme.ObsidianDeep);
        FillWithPadding(detailBg.rectTransform, 2.5f);

        var dIconFrame = MakeImage(detailBg.transform, "DetailIconFrame", UITheme.ObsidianSlot);
        UITheme.ApplySlicedStyle(dIconFrame, UITheme.ObsidianSlot);
        Place(dIconFrame.rectTransform, 20f, 20f, 96f, 96f);
        _compDetailIcon = MakeImage(dIconFrame.transform, "Icon", Color.white);
        _compDetailIcon.preserveAspect = true;
        FillWithPadding(_compDetailIcon.rectTransform, 12f);

        _compDetailTitle = MakeLabel(detailBg.transform, "CompDetailTitle", "Select an item", 22, FontStyle.Bold, TextAnchor.UpperLeft,
            UITheme.BrushedGoldBright, 132f, 22f, 480f, 36f);
        _compDetailSubtitle = MakeLabel(detailBg.transform, "CompDetailSub", "", 14, FontStyle.Bold, TextAnchor.UpperLeft,
            UITheme.JadeAccent, 132f, 62f, 480f, 48f);

        MakeLabel(detailBg.transform, "UsageHeader", "HOW IT WORKS & USAGE GUIDE", 14, FontStyle.Bold, TextAnchor.MiddleLeft,
            UITheme.BrushedGoldBright, 20f, 134f, 592f, 24f);
        _compDetailUsage = MakeLabel(detailBg.transform, "UsageBody", "", 14, FontStyle.Normal, TextAnchor.UpperLeft,
            UITheme.TextPrimary, 20f, 164f, 592f, 164f);

        MakeLabel(detailBg.transform, "StatsHeader", "FULL STAT BREAKDOWN", 14, FontStyle.Bold, TextAnchor.MiddleLeft,
            UITheme.JadeAccent, 20f, 340f, 592f, 24f);
        _compDetailStats = MakeLabel(detailBg.transform, "StatsBody", "", 14, FontStyle.Bold, TextAnchor.UpperLeft,
            UITheme.TextSecondary, 20f, 370f, 592f, 174f);
    }

    private void BuildEquipSlotWidget(Transform parent, int idx, EquipmentSlotType slotType, string header, float x, float y)
    {
        var border = MakeImage(parent, "EquipSlot_" + slotType, UITheme.BrushedGoldDark);
        UITheme.ApplySlicedStyle(border, UITheme.BrushedGoldDark);
        Place(border.rectTransform, x, y, 258f, 96f);

        var bg = MakeImage(border.transform, "Bg", UITheme.ObsidianSlot);
        UITheme.ApplySlicedStyle(bg, UITheme.ObsidianSlot);
        FillWithPadding(bg.rectTransform, 2f);

        var dropRelay = bg.gameObject.AddComponent<EquipDropRelay>();
        dropRelay.owner = this;
        dropRelay.slotType = slotType;

        var iconFrame = MakeImage(bg.transform, "IconFrame", UITheme.ObsidianDeep);
        Place(iconFrame.rectTransform, 10f, 22f, 50f, 50f);
        var icon = MakeImage(iconFrame.transform, "Icon", new Color(0.32f, 0.36f, 0.44f, 0.7f));
        icon.sprite = HudIcons.GetForEquipmentSlot(slotType);
        icon.preserveAspect = true;
        FillWithPadding(icon.rectTransform, 7f);

        MakeLabel(bg.transform, "SlotHeader", header, 11, FontStyle.Bold, TextAnchor.MiddleLeft,
            UITheme.BrushedGoldBright, 68f, 6f, 112f, 18f);

        var nameLbl = MakeLabel(bg.transform, "ItemName", "Empty Slot", 13, FontStyle.Bold, TextAnchor.MiddleLeft,
            UITheme.TextMuted, 68f, 26f, 180f, 24f);

        var statLbl = MakeLabel(bg.transform, "ItemStats", "-", 11, FontStyle.Normal, TextAnchor.UpperLeft,
            UITheme.TextSecondary, 68f, 52f, 180f, 36f);

        var unequipBtn = MakeActionButton(bg.transform, "UnequipBtn", "Unequip", 180f, 5f, 70f, 22f, () =>
        {
            _inv.UnequipSlot(slotType);
        }, out Text uTxt);
        uTxt.fontSize = 10;

        _equipWidgets[idx] = new EquipSlotWidget
        {
            slotType = slotType,
            border = border,
            icon = icon,
            nameText = nameLbl,
            statText = statLbl,
            unequipBtn = unequipBtn
        };
    }

    private void BuildStatRow(Transform parent, int idx, PlayerStatType statType, string statName, string desc, float y)
    {
        var rowBg = MakeImage(parent, "StatRow_" + statName, UITheme.ObsidianSlot);
        UITheme.ApplySlicedStyle(rowBg, UITheme.ObsidianSlot);
        Place(rowBg.rectTransform, 12f, y, 508f, 44f);

        var lbl = MakeLabel(rowBg.transform, "StatName", $"{statName}  ({desc})", 14, FontStyle.Bold, TextAnchor.MiddleLeft,
            UITheme.TextPrimary, 12f, 6f, 210f, 32f);

        var costLbl = MakeLabel(rowBg.transform, "StatCost", "1pt+10g", 12, FontStyle.Bold, TextAnchor.MiddleRight,
            UITheme.BrushedGoldBright, 226f, 6f, 120f, 32f);

        var valLbl = MakeLabel(rowBg.transform, "StatValue", "0", 15, FontStyle.Bold, TextAnchor.MiddleRight,
            UITheme.JadeAccent, 352f, 6f, 62f, 32f);

        var plus = MakeActionButton(rowBg.transform, "PlusBtn", "+", 426f, 6f, 68f, 32f, () =>
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
    }

    // ------------------------------------------------------------------ การควบคุมปุ่มเปิดปิดและแท็บ

    private void Update()
    {
        if (_inv == null) return;
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.iKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame)
        {
            if (!_open && MapManager.Instance != null && MapManager.Instance.IsWorldMapOpen)
            {
                MapManager.Instance.SwitchToCharacterTab();
            }
            else
            {
                SetOpen(!_open);
            }
        }
        else if (_open && kb.escapeKey.wasPressedThisFrame)
        {
            SetOpen(false);
        }

        if (_open && _hoveredCellIndex >= 0 && _draggingFromIndex < 0)
            UpdateHoverTooltipPosition();
    }

    public void SwitchToWorldMapTab()
    {
        SetOpen(false);
        if (MapManager.Instance != null)
            MapManager.Instance.SetWorldMapOpen(true);
    }

    public void OpenToEnhancementTab()
    {
        if (!_open && MapManager.Instance != null && MapManager.Instance.IsWorldMapOpen)
            MapManager.Instance.SetWorldMapOpen(false);

        if (!_open) SetOpen(true);
        SetActiveTab(HubTab.Blacksmith);
    }

    public void OpenToCompendiumTab()
    {
        if (!_open && MapManager.Instance != null && MapManager.Instance.IsWorldMapOpen)
            MapManager.Instance.SetWorldMapOpen(false);

        if (!_open) SetOpen(true);
        SetActiveTab(HubTab.Compendium);
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
        bool charTab = _activeTab == HubTab.Character;
        bool bsTab = _activeTab == HubTab.Blacksmith;
        bool compTab = _activeTab == HubTab.Compendium;

        if (_characterColumn != null) _characterColumn.SetActive(charTab);
        if (_blacksmithColumn != null) _blacksmithColumn.SetActive(bsTab);
        if (_inventoryRightColumn != null) _inventoryRightColumn.SetActive(!compTab);
        if (_compendiumColumn != null) _compendiumColumn.SetActive(compTab);

        Color activeBg = UITheme.BrushedGold;
        Color idleBg = new Color(0.14f, 0.17f, 0.24f, 1f);
        Color activeTxt = new Color(0.08f, 0.06f, 0.02f, 1f);

        if (_tabCharacterBg != null) _tabCharacterBg.color = charTab ? activeBg : idleBg;
        if (_tabCharacterText != null) _tabCharacterText.color = charTab ? activeTxt : Color.white;
        if (_tabBlacksmithBg != null) _tabBlacksmithBg.color = bsTab ? activeBg : idleBg;
        if (_tabBlacksmithText != null) _tabBlacksmithText.color = bsTab ? activeTxt : Color.white;
        if (_tabCompendiumBg != null) _tabCompendiumBg.color = compTab ? activeBg : idleBg;
        if (_tabCompendiumText != null) _tabCompendiumText.color = compTab ? activeTxt : Color.white;
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

    // ------------------------------------------------------------------ การโต้ตอบกับช่องกระเป๋าและ Blacksmith

    /// <summary>
    /// แปลงลำดับช่องที่แสดงบนตาราง UI (ซึ่งซ่อนของที่สวมใส่อยู่) เป็น slotIndex จริงใน InventoryManager
    /// </summary>
    public int GetRealSlotIndexForCell(int cellIndex)
    {
        if (_visibleToRealSlot == null || cellIndex < 0 || cellIndex >= _visibleToRealSlot.Length)
            return -1;
        return _visibleToRealSlot[cellIndex];
    }

    private int FindFirstEmptyBagSlotIndex()
    {
        if (_inv == null) return -1;
        for (int i = 0; i < _inv.Slots.Count; i++)
        {
            if (_inv.Slots[i].IsEmpty && !_inv.IsSlotEquipped(i)) return i;
        }
        return -1;
    }

    private void OnGridCellClicked(int cellIndex)
    {
        if (_inv == null) return;
        int realIndex = GetRealSlotIndexForCell(cellIndex);
        if (realIndex < 0)
        {
            _selected = -1;
            Refresh();
            return;
        }

        _selected = realIndex;

        if (_activeTab == HubTab.Blacksmith)
        {
            var clicked = _inv.GetSlot(realIndex);
            if (clicked != null && !clicked.IsEmpty && clicked.item != null)
            {
                if (_enhanceTargetSlot < 0)
                {
                    if (clicked.item.IsEquippable)
                    {
                        _enhanceTargetSlot = realIndex;
                        _fodderSlots.Clear();
                        SetEnhanceFeedback("Target set! Click bag gear to attach Fodder.", UITheme.JadeAccent);
                    }
                    else
                    {
                        SetEnhanceFeedback("Only Equippable Gear can be enhanced.", new Color(1f, 0.55f, 0.35f));
                    }
                }
                else if (realIndex != _enhanceTargetSlot)
                {
                    var targetSlot = _inv.GetSlot(_enhanceTargetSlot);
                    if (targetSlot != null && !targetSlot.IsEmpty && targetSlot.item != null)
                    {
                        if (_fodderSlots.Contains(realIndex))
                        {
                            _fodderSlots.Remove(realIndex);
                        }
                        else if (_inv.IsSlotEquipped(realIndex))
                        {
                            SetEnhanceFeedback("Unequip an item before using it as Fodder.", new Color(1f, 0.65f, 0.30f));
                        }
                        else
                        {
                            var spec = EnhancementSystem.GetNextSpec(targetSlot.item, targetSlot.enhancementLevel);
                            if (!EnhancementSystem.IsEligibleFodder(targetSlot.item, targetSlot.instanceId, clicked, spec.minFodderTier))
                            {
                                SetEnhanceFeedback("Ineligible Fodder for this enhancement.", new Color(1f, 0.45f, 0.35f));
                            }
                            else if (_fodderSlots.Count >= spec.requiredFodderCount)
                            {
                                SetEnhanceFeedback($"Already attached {spec.requiredFodderCount} Fodder item(s).", UITheme.BrushedGoldBright);
                            }
                            else
                            {
                                _fodderSlots.Add(realIndex);
                                SetEnhanceFeedback($"Fodder attached ({_fodderSlots.Count}/{spec.requiredFodderCount}).", UITheme.JadeAccent);
                            }
                        }
                    }
                }
            }
        }

        Refresh();
    }

    public void SelectEquippedAsEnhanceTarget(EquipmentSlotType slotType)
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
            SetEnhanceFeedback($"Selected equipped {eqRef.ResolveItem()?.displayName ?? slotType.ToString()} as Target.", UITheme.JadeAccent);
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
            SetEnhanceFeedback($"Auto-picked {_fodderSlots.Count}/{spec.requiredFodderCount} Fodder item(s).", UITheme.JadeAccent);
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
            SetEnhanceFeedback(res.message, UITheme.JadeAccent);
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
                SetEnhanceFeedback($"Target switched to {s.item.displayName} +{s.enhancementLevel}.", UITheme.JadeAccent);
                Refresh();
            }
            return;
        }
        _inv.EquipItem(_selected);
    }

    // ------------------------------------------------------------------ ระบบ Drag-and-Drop

    private void OnCellBeginDrag(int cellIndex, PointerEventData eventData)
    {
        if (!_open || _inv == null) return;
        int realIndex = GetRealSlotIndexForCell(cellIndex);
        var slot = _inv.GetSlot(realIndex);
        if (slot == null || slot.IsEmpty || slot.item == null) return;

        _draggingFromIndex = realIndex;
        HideTooltip();

        Color tierCol = ItemData.GetTierColor(slot.item.tier);
        _dragGhostBorder.color = tierCol;
        _dragGhostIcon.sprite = HudIcons.GetForItem(slot.item);
        _dragGhostIcon.color = tierCol;
        _dragGhostObj.SetActive(true);
        UpdateDragGhostPosition(eventData);
    }

    private void OnCellDrag(PointerEventData eventData)
    {
        if (_draggingFromIndex < 0 || _dragGhostObj == null || !_dragGhostObj.activeSelf) return;
        UpdateDragGhostPosition(eventData);
    }

    private void OnCellDrop(int targetCellIndex)
    {
        int from = _draggingFromIndex;
        EndDragGhost();
        if (from < 0 || _inv == null) return;

        int targetRealIndex = GetRealSlotIndexForCell(targetCellIndex);
        if (targetRealIndex < 0) targetRealIndex = FindFirstEmptyBagSlotIndex();
        if (targetRealIndex < 0 || from == targetRealIndex) return;

        if (_inv.SwapOrMoveSlots(from, targetRealIndex))
        {
            if (_enhanceTargetSlot == from) _enhanceTargetSlot = targetRealIndex;
            else if (_enhanceTargetSlot == targetRealIndex) _enhanceTargetSlot = from;

            for (int i = 0; i < _fodderSlots.Count; i++)
            {
                if (_fodderSlots[i] == from) _fodderSlots[i] = targetRealIndex;
                else if (_fodderSlots[i] == targetRealIndex) _fodderSlots[i] = from;
            }

            _selected = targetRealIndex;
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
            _selected = -1;
            Refresh();
        }
    }

    private void OnCellEndDrag()
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

    private void OnCellHoverEnter(int cellIndex)
    {
        _hoveredCellIndex = cellIndex;
        int realIndex = GetRealSlotIndexForCell(cellIndex);
        if (!_open || _draggingFromIndex >= 0 || _inv == null || realIndex < 0 || realIndex >= _inv.Slots.Count)
        {
            HideTooltip();
            return;
        }

        var slot = _inv.Slots[realIndex];
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

    private void OnCellHoverExit(int cellIndex)
    {
        if (_hoveredCellIndex == cellIndex)
        {
            _hoveredCellIndex = -1;
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
        if (_tooltipRect == null || !_tooltipRect.gameObject.activeSelf || _hoveredCellIndex < 0 || _hoveredCellIndex >= _slotBorders.Length)
            return;

        var slotRt = _slotBorders[_hoveredCellIndex].rectTransform;
        Vector3 worldTopRight = slotRt.TransformPoint(new Vector3(SlotSize + 8f, 0f, 0f));
        Vector2 localPos = _panel.transform.InverseTransformPoint(worldTopRight);
        localPos.x = Mathf.Clamp(localPos.x, -580f, 300f);
        localPos.y = Mathf.Clamp(localPos.y, -320f, 290f);
        _tooltipRect.localPosition = localPos;
    }

    // ------------------------------------------------------------------ การรีเฟรชหน้าจอทั้งหมด

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
        {
            RefreshCharacterColumn(combat, capsule, gold);
            RefreshInventoryGrid();
            RefreshSelectedInspector();
        }
        else if (_activeTab == HubTab.Blacksmith)
        {
            RefreshBlacksmithColumn(gold);
            RefreshInventoryGrid();
            RefreshSelectedInspector();
        }
        else
        {
            RefreshCompendium();
        }
    }

    private void RefreshCharacterColumn(PlayerCombat combat, PlayerCapsule capsule, int gold)
    {
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
                w.icon.sprite = HudIcons.GetForItem(equipped);
                w.icon.color = tc;
                w.nameText.text = enh > 0 ? $"{equipped.displayName} +{enh}" : equipped.displayName;
                w.nameText.color = tc;
                w.statText.text = BuildItemStatSummary(equipped, enh, includeDesc: false).Replace("\n", " ");
                w.unequipBtn.gameObject.SetActive(true);
            }
            else
            {
                w.border.color = UITheme.BrushedGoldDark;
                w.icon.sprite = HudIcons.GetForEquipmentSlot(w.slotType);
                w.icon.color = new Color(0.32f, 0.36f, 0.44f, 0.65f);
                w.nameText.text = "Empty Slot";
                w.nameText.color = UITheme.TextMuted;
                w.statText.text = "-";
                w.unequipBtn.gameObject.SetActive(false);
            }
        }

        if (combat != null)
        {
            float spd = capsule != null ? capsule.TotalMoveSpeed : 6f;
            _totalStatsText.text =
                $"ATK: {combat.TotalAttackDamage}     |     DEF: {combat.TotalDefense}     |     CRIT: {combat.TotalCritChance * 100f:0}%\n" +
                $"Max HP: {combat.MaxHealth}     |     Max STA: {combat.MaxStamina:0}     |     SPD: {spd:0.00}";

            int pts = combat.NetworkUnspentStatPoints;
            _statPointsText.text = $"Pts: {pts}  |  {gold:N0} G";
            _statPointsText.color = pts > 0 ? UITheme.BrushedGoldBright : UITheme.TextSecondary;

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
                sw.costText.color = canAfford ? UITheme.BrushedGoldBright : new Color(0.85f, 0.45f, 0.42f);
                sw.plusBtn.interactable = canAfford;
            }
        }
    }

    private void RefreshBlacksmithColumn(int playerGold)
    {
        for (int i = _fodderSlots.Count - 1; i >= 0; i--)
        {
            int idx = _fodderSlots[i];
            var fs = _inv.GetSlot(idx);
            if (idx == _enhanceTargetSlot || fs == null || fs.IsEmpty || _inv.IsSlotEquipped(idx))
                _fodderSlots.RemoveAt(i);
        }

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
                _bsEquippedTexts[i].color = UITheme.TextMuted;
                _bsEquippedBtns[i].interactable = false;
            }
        }

        var targetSlot = _inv.GetSlot(_enhanceTargetSlot);
        if (targetSlot == null || targetSlot.IsEmpty || targetSlot.item == null || !targetSlot.item.IsEquippable)
        {
            _enhanceTargetSlot = -1;
            _fodderSlots.Clear();

            _bsTargetBorder.color = UITheme.BrushedGoldDark;
            _bsTargetIcon.gameObject.SetActive(false);
            _bsTargetTitleText.text = "Select Equipment to Enhance";
            _bsTargetTitleText.color = Color.white;
            _bsTargetStatDiffText.text = "Click an Equipped button above or any gear in your Bag grid to prepare +1 to +10 forging.";
            _bsClearTargetBtn.interactable = false;

            _bsOddsText.text = "Success Chance: --";
            _bsOddsText.color = UITheme.TextSecondary;
            _bsOddsBarFill.fillAmount = 0f;
            _bsGoldCostText.text = $"Gold Required: --   (Balance: {playerGold:N0} G)";
            _bsFodderReqText.text = "Material Fodder: --";

            for (int i = 0; i < 3; i++)
            {
                _bsFodderSlotBorders[i].color = UITheme.BrushedGoldDark;
                _bsFodderSlotTexts[i].text = "Empty";
                _bsFodderSlotTexts[i].color = UITheme.TextMuted;
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
            _bsOddsBarFill.fillAmount = 1f;
            _bsOddsBarFill.color = UITheme.BrushedGoldBright;
            _bsGoldCostText.text = $"Balance: {playerGold:N0} G";
            _bsFodderReqText.text = "No further Fodder required.";
            for (int i = 0; i < 3; i++)
            {
                _bsFodderSlotBorders[i].color = UITheme.BrushedGoldDark;
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

        // คำนวณอัตราสำเร็จแบบไดนามิกตาม Tier ของวัตถุดิบที่ใส่เข้ามาจริง
        var selectedFodderItems = new List<ItemData>(_fodderSlots.Count);
        for (int i = 0; i < _fodderSlots.Count; i++)
        {
            var fs = _inv.GetSlot(_fodderSlots[i]);
            if (fs != null && !fs.IsEmpty && fs.item != null)
                selectedFodderItems.Add(fs.item);
        }
        float dynamicRate = EnhancementSystem.CalculateDynamicSuccessRate(tItem, curLvl, selectedFodderItems);
        int pct = Mathf.RoundToInt(dynamicRate * 100f);

        Color oddsCol = dynamicRate >= 0.70f ? UITheme.JadeAccent
            : (dynamicRate >= 0.40f ? UITheme.BrushedGoldBright : new Color(1f, 0.42f, 0.28f));
        string rateTag = selectedFodderItems.Count > 0 ? "Dynamic Rate" : "Base Rate";
        _bsOddsText.text = $"Success Chance: {pct}%  ({rateTag})";
        _bsOddsText.color = oddsCol;
        _bsOddsBarFill.fillAmount = dynamicRate;
        _bsOddsBarFill.color = oddsCol;

        bool hasGold = playerGold >= spec.goldCost;
        _bsGoldCostText.text = $"Gold Cost: {spec.goldCost:N0} G   (You have: {playerGold:N0} G)";
        _bsGoldCostText.color = hasGold ? UITheme.BrushedGoldBright : new Color(1f, 0.42f, 0.35f);

        _bsFodderReqText.text =
            $"Fodder: {_fodderSlots.Count}/{spec.requiredFodderCount} Gear   (Any Tier {ItemData.GetTierDisplayName(spec.minFodderTier)}+)";
        _bsFodderReqText.color = _fodderSlots.Count >= spec.requiredFodderCount ? UITheme.JadeAccent : UITheme.TextSecondary;

        for (int i = 0; i < 3; i++)
        {
            if (i >= spec.requiredFodderCount)
            {
                _bsFodderSlotBorders[i].color = new Color(0.16f, 0.18f, 0.22f);
                _bsFodderSlotTexts[i].text = "Locked";
                _bsFodderSlotTexts[i].color = UITheme.TextMuted;
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
                _bsFodderSlotBorders[i].color = UITheme.BrushedGoldDark;
                _bsFodderSlotTexts[i].text = $"Fodder #{i + 1}\n(Pick from Bag)";
                _bsFodderSlotTexts[i].color = UITheme.TextSecondary;
                _bsFodderSlotBtns[i].interactable = false;
            }
        }

        bool ready = hasGold && _fodderSlots.Count >= spec.requiredFodderCount;
        _bsAutoPickBtn.interactable = true;
        _bsEnhanceBtn.interactable = ready;
        _bsEnhanceBtnBg.color = ready ? UITheme.JadeButton : new Color(0.22f, 0.24f, 0.30f, 1f);
        _bsEnhanceBtnText.text = $"ENHANCE +{curLvl + 1}  ({pct}%)";
    }

    /// <summary>
    /// รีเฟรชตารางกระเป๋า 24 ช่อง โดยซ่อนอุปกรณ์ที่สวมใส่อยู่ใน 4 ช่อง Equipment ออกไปจากตารางกระเป๋า
    /// </summary>
    private void RefreshInventoryGrid()
    {
        var slots = _inv.Slots;
        bool bsMode = _activeTab == HubTab.Blacksmith;
        InventorySlot targetSlot = bsMode ? _inv.GetSlot(_enhanceTargetSlot) : null;
        EnhancementSystem.EnhancementSpec? bsSpec = null;
        if (bsMode && targetSlot != null && !targetSlot.IsEmpty && targetSlot.item != null && targetSlot.enhancementLevel < EnhancementSystem.MaxEnhancementLevel)
            bsSpec = EnhancementSystem.GetNextSpec(targetSlot.item, targetSlot.enhancementLevel);

        // รวบรวมเฉพาะช่องไอเทมที่ไม่ได้สวมใส่อยู่มาแสดงผลในกระเป๋า
        var visibleList = new List<int>(_slotBgs.Length);
        var emptyList = new List<int>(_slotBgs.Length);
        for (int r = 0; r < slots.Count; r++)
        {
            if (_inv.IsSlotEquipped(r)) continue;
            if (!slots[r].IsEmpty) visibleList.Add(r);
            else emptyList.Add(r);
        }

        for (int cell = 0; cell < _slotBgs.Length; cell++)
        {
            int realIdx = cell < visibleList.Count
                ? visibleList[cell]
                : (cell - visibleList.Count < emptyList.Count ? emptyList[cell - visibleList.Count] : -1);
            _visibleToRealSlot[cell] = realIdx;

            var s = realIdx >= 0 ? slots[realIdx] : null;
            bool selected = realIdx >= 0 && realIdx == _selected;

            if (s == null || s.IsEmpty)
            {
                _slotBorders[cell].color = selected ? UITheme.BrushedGoldBright : UITheme.BrushedGoldDark;
                _slotBgs[cell].color = selected ? new Color(0.18f, 0.21f, 0.28f) : UITheme.ObsidianSlotEmpty;
                _slotIcons[cell].gameObject.SetActive(false);
                _slotLabels[cell].text = "";
                _slotQtyLabels[cell].text = "";
                continue;
            }

            Color tierColor = ItemData.GetTierColor(s.item.tier);
            bool isTarget = bsMode && realIdx == _enhanceTargetSlot;
            bool isFodder = bsMode && _fodderSlots.Contains(realIdx);

            Color borderCol = selected ? Color.white : tierColor;
            if (isTarget) borderCol = UITheme.JadeAccent;
            else if (isFodder) borderCol = new Color(1f, 0.56f, 0.18f);

            _slotBorders[cell].color = borderCol;
            _slotBgs[cell].color = selected
                ? Color.Lerp(UITheme.ObsidianSlot, tierColor, 0.34f)
                : Color.Lerp(UITheme.ObsidianSlot, tierColor, 0.14f);

            float alpha = 1f;
            if (bsMode && bsSpec.HasValue && !isTarget && !isFodder)
            {
                bool ok = EnhancementSystem.IsEligibleFodder(targetSlot.item, targetSlot.instanceId, s, bsSpec.Value.minFodderTier);
                if (!ok) alpha = 0.45f;
            }

            _slotIcons[cell].gameObject.SetActive(true);
            _slotIcons[cell].sprite = HudIcons.GetForItem(s.item);
            _slotIcons[cell].color = new Color(tierColor.r, tierColor.g, tierColor.b, alpha);

            string enhSuffix = s.enhancementLevel > 0 ? $" +{s.enhancementLevel}" : "";
            _slotLabels[cell].text = s.item.displayName + enhSuffix;
            _slotLabels[cell].color = Color.Lerp(Color.white, tierColor, 0.45f) * new Color(1f, 1f, 1f, alpha);

            if (isTarget)
            {
                _slotQtyLabels[cell].text = "[TARGET]";
                _slotQtyLabels[cell].color = UITheme.JadeAccent;
            }
            else if (isFodder)
            {
                _slotQtyLabels[cell].text = "[FODDER]";
                _slotQtyLabels[cell].color = new Color(1f, 0.62f, 0.22f);
            }
            else if (s.enhancementLevel > 0)
            {
                _slotQtyLabels[cell].text = $"+{s.enhancementLevel}";
                _slotQtyLabels[cell].color = new Color(0.45f, 0.92f, 1f);
            }
            else
            {
                _slotQtyLabels[cell].text = s.quantity > 1 ? $"x{s.quantity}" : "";
                _slotQtyLabels[cell].color = UITheme.BrushedGoldBright;
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
                ? "Blacksmith Tip: Pick Target Gear, attach Fodder (Normal..Legendary) to see dynamic Success %, and forge!"
                : "Tip: Drag items to Hotbar [1-6] below or drag gear onto Equipment slots on the left.";
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
        _detailBodyText.text = $"Bag Slot #{_selected + 1}  |  Qty: {sel.quantity}/{item.MaxStackSize}   " +
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

    // ------------------------------------------------------------------ รีเฟรชแท็บ Item Compendium

    /// <summary>
    /// อัปเดตรายการไอเทมทั้งหมดในสมุดสารานุกรม (Item Compendium) ตามหมวดหมู่ที่เลือก
    /// </summary>
    public void RefreshCompendium()
    {
        for (int i = 0; i < 6; i++)
        {
            bool active = (int)_compCategory == i;
            if (_compCatBgs[i] != null) _compCatBgs[i].color = active ? UITheme.BrushedGold : new Color(0.14f, 0.17f, 0.24f, 1f);
            if (_compCatTexts[i] != null) _compCatTexts[i].color = active ? new Color(0.08f, 0.06f, 0.02f) : Color.white;
        }

        if (_compListContent == null) return;
        for (int i = 0; i < _compEntryObjects.Count; i++)
        {
            if (_compEntryObjects[i] != null) Destroy(_compEntryObjects[i]);
        }
        _compEntryObjects.Clear();

        var filtered = new List<ItemData>();
        foreach (var kv in ItemRegistry.All)
        {
            var item = kv.Value;
            if (item == null || ItemRegistry.IsRemovedJunkItem(item.itemId)) continue;
            if (!MatchesCompendiumCategory(item, _compCategory)) continue;
            filtered.Add(item);
        }

        filtered.Sort((a, b) =>
        {
            int c = a.tier.CompareTo(b.tier);
            if (c != 0) return c;
            return string.Compare(a.displayName, b.displayName, StringComparison.Ordinal);
        });

        ItemData selectedItem = null;
        for (int i = 0; i < filtered.Count; i++)
        {
            var item = filtered[i];
            if (item.itemId == _selectedCompItemId) selectedItem = item;

            bool isSelected = item.itemId == _selectedCompItemId;
            Color tc = ItemData.GetTierColor(item.tier);

            var rowBorder = MakeImage(_compListContent, "CompEntry_" + item.itemId, isSelected ? UITheme.JadeAccent : tc);
            UITheme.ApplySlicedStyle(rowBorder, isSelected ? UITheme.JadeAccent : tc);
            var le = rowBorder.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 64f;
            le.minHeight = 64f;

            var rowBg = MakeImage(rowBorder.transform, "Bg", isSelected ? Color.Lerp(UITheme.ObsidianSlot, tc, 0.26f) : UITheme.ObsidianSlot);
            FillWithPadding(rowBg.rectTransform, 2f);
            var btn = rowBg.gameObject.AddComponent<Button>();
            btn.targetGraphic = rowBg;
            ItemData captured = item;
            btn.onClick.AddListener(() =>
            {
                _selectedCompItemId = captured.itemId;
                RefreshCompendium();
            });

            var icon = MakeImage(rowBg.transform, "Icon", tc);
            icon.sprite = HudIcons.GetForItem(item);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            Place(icon.rectTransform, 10f, 10f, 40f, 40f);

            MakeLabel(rowBg.transform, "Title", item.displayName, 14, FontStyle.Bold, TextAnchor.UpperLeft,
                tc, 62f, 8f, 340f, 24f);
            MakeLabel(rowBg.transform, "Meta", $"{ItemData.GetTierDisplayName(item.tier)}  •  {FormatItemCategory(item)}", 12, FontStyle.Normal, TextAnchor.LowerLeft,
                UITheme.TextSecondary, 62f, 32f, 340f, 20f);

            _compEntryObjects.Add(rowBorder.gameObject);
        }

        if (selectedItem == null && filtered.Count > 0)
        {
            selectedItem = filtered[0];
            _selectedCompItemId = selectedItem.itemId;
        }

        RenderCompendiumDetail(selectedItem);
    }

    private static bool MatchesCompendiumCategory(ItemData item, CompendiumCategory cat)
    {
        if (cat == CompendiumCategory.All) return true;
        return cat switch
        {
            CompendiumCategory.Weapons => item.GetEquipmentSlot() == EquipmentSlotType.Sword,
            CompendiumCategory.Shields => item.GetEquipmentSlot() == EquipmentSlotType.Shield,
            CompendiumCategory.Armor => item.GetEquipmentSlot() == EquipmentSlotType.Armor,
            CompendiumCategory.Pants => item.GetEquipmentSlot() == EquipmentSlotType.Pants,
            CompendiumCategory.Consumables => item.type == ItemType.Consumable,
            _ => true
        };
    }

    private void RenderCompendiumDetail(ItemData item)
    {
        if (item == null)
        {
            _compDetailBorder.color = UITheme.BrushedGoldDark;
            _compDetailIcon.gameObject.SetActive(false);
            _compDetailTitle.text = "No items in this category.";
            _compDetailSubtitle.text = "";
            _compDetailUsage.text = "";
            _compDetailStats.text = "";
            return;
        }

        Color tc = ItemData.GetTierColor(item.tier);
        _compDetailBorder.color = tc;
        _compDetailIcon.gameObject.SetActive(true);
        _compDetailIcon.sprite = HudIcons.GetForItem(item);
        _compDetailIcon.color = tc;

        _compDetailTitle.color = tc;
        _compDetailTitle.text = item.displayName;
        _compDetailSubtitle.color = UITheme.JadeAccent;
        _compDetailSubtitle.text = $"Rarity: {ItemData.GetTierDisplayName(item.tier)}   |   Category: {FormatItemCategory(item)}   |   Max Stack: {item.MaxStackSize}";

        _compDetailUsage.text = BuildCompendiumUsageGuide(item);
        _compDetailStats.text = BuildCompendiumFullStats(item);
    }

    private static string BuildCompendiumUsageGuide(ItemData item)
    {
        string desc = string.IsNullOrEmpty(item.description) ? "" : item.description + "\n\n";
        if (item.type == ItemType.Consumable)
        {
            return desc +
                "• Drag this item from your Inventory Bag onto Hotbar slots [1-6] at the bottom of the screen for instant activation during combat.\n" +
                "• Using a potion triggers the Buff animation across Photon Fusion Shared Mode.";
        }

        return item.GetEquipmentSlot() switch
        {
            EquipmentSlotType.Sword => desc +
                "• Equip in the WEAPON slot (removed from bag grid while equipped).\n" +
                "• Combat: [LMB] Light 3-Hit Combo / Run Attack / Jump Attack / Stealth Assassinate (3.0x from behind), [R] Heavy Slash Combo.\n" +
                "• Can be enhanced at the Blacksmith up to +10.",
            EquipmentSlotType.Shield => desc +
                "• Equip in the SHIELD slot to boost Defense & Health.\n" +
                "• Hold [Right-Click] or [Left Alt] to block incoming damage by 75% (drains Stamina on impact).\n" +
                "• Can be enhanced at the Blacksmith up to +10.",
            EquipmentSlotType.Armor => desc +
                "• Equip in the ARMOR slot for flat Defense and Max HP scaling.\n" +
                "• Enhance at the Blacksmith using any gear tier (Normal..Legendary) as Fodder.",
            EquipmentSlotType.Pants => desc +
                "• Equip in the PANTS slot for Defense, Stamina, and Movement Speed bonuses.\n" +
                "• Enhance at the Blacksmith up to +10.",
            _ => desc + "• Standard item."
        };
    }

    private static string BuildCompendiumFullStats(ItemData item)
    {
        var sb = new System.Text.StringBuilder();
        if (item.damageBonus != 0) sb.AppendLine($"• Attack Damage: +{item.damageBonus}  (At +10: +{item.GetEnhancedDamage(10)})");
        if (item.defenseBonus != 0) sb.AppendLine($"• Defense: +{item.defenseBonus}  (At +10: +{item.GetEnhancedDefense(10)})");
        if (item.maxHealthBonus != 0) sb.AppendLine($"• Max Health: +{item.maxHealthBonus}  (At +10: +{item.GetEnhancedMaxHealth(10)})");
        if (item.maxStaminaBonus != 0) sb.AppendLine($"• Max Stamina: +{item.maxStaminaBonus}  (At +10: +{item.GetEnhancedMaxStamina(10)})");
        if (item.moveSpeedBonus > 0f) sb.AppendLine($"• Move Speed: +{item.moveSpeedBonus:0.0} m/s  (At +10: +{item.GetEnhancedMoveSpeed(10):0.0} m/s)");
        if (item.critChanceBonus > 0f) sb.AppendLine($"• Critical Chance: +{item.critChanceBonus * 100f:0.#}%  (At +10: +{item.GetEnhancedCritChance(10) * 100f:0.#}%)");
        if (item.healAmount > 0) sb.AppendLine($"• Instant Heal: +{item.healAmount} HP");
        if (item.staminaRestoreAmount > 0) sb.AppendLine($"• Stamina Restore: +{item.staminaRestoreAmount} STA");
        if (item.attackBuffBonus > 0) sb.AppendLine($"• Attack Buff: +{item.attackBuffBonus} ATK for {item.attackBuffDuration:0}s");
        if (sb.Length == 0) sb.AppendLine("• Utility / Special Item");
        return sb.ToString().TrimEnd();
    }

    private static string FormatItemCategory(ItemData item)
    {
        return item.GetEquipmentSlot() switch
        {
            EquipmentSlotType.Sword => "Weapon",
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

    // ------------------------------------------------------------------ ฟังก์ชันช่วยสร้าง uGUI

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
        Color bg = active ? UITheme.BrushedGold : new Color(0.14f, 0.17f, 0.24f, 1f);
        var img = MakeImage(parent, name, bg);
        UITheme.ApplySlicedStyle(img, bg);
        Place(img.rectTransform, x, 10f, w, 34f);
        var btn = img.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);
        textOut = MakeLabel(img.transform, "Text", label, 13, FontStyle.Bold, TextAnchor.MiddleCenter,
            active ? new Color(0.08f, 0.06f, 0.02f) : Color.white, 4f, 2f, w - 8f, 30f);
        bgOut = img;
    }

    private Button MakeActionButton(Transform parent, string name, string label, float x, float y, float w, float h, UnityEngine.Events.UnityAction onClick, out Text labelOut)
    {
        var img = MakeImage(parent, name, new Color(0.16f, 0.22f, 0.32f, 1f));
        UITheme.ApplySlicedStyle(img, new Color(0.16f, 0.22f, 0.32f, 1f));
        Place(img.rectTransform, x, y, w, h);
        var btn = img.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);
        labelOut = MakeLabel(img.transform, "Text", label, 13, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white, 2f, 2f, w - 4f, h - 4f);
        return btn;
    }

    private class SlotDragRelay : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
    {
        public InventoryUI owner;
        public int cellIndex;
        public void OnPointerEnter(PointerEventData eventData) => owner?.OnCellHoverEnter(cellIndex);
        public void OnPointerExit(PointerEventData eventData) => owner?.OnCellHoverExit(cellIndex);
        public void OnBeginDrag(PointerEventData eventData) => owner?.OnCellBeginDrag(cellIndex, eventData);
        public void OnDrag(PointerEventData eventData) => owner?.OnCellDrag(eventData);
        public void OnEndDrag(PointerEventData eventData) => owner?.OnCellEndDrag();
        public void OnDrop(PointerEventData eventData) => owner?.OnCellDrop(cellIndex);
    }

    private class EquipDropRelay : MonoBehaviour, IDropHandler
    {
        public InventoryUI owner;
        public EquipmentSlotType slotType;
        public void OnDrop(PointerEventData eventData) => owner?.OnEquipSlotDrop(slotType);
    }
}
