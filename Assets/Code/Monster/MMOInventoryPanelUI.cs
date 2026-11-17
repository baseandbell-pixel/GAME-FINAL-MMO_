using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Full AAA MMORPG Character Paperdoll, 32-Slot Grid Bag, Hover Comparison Tooltip,
/// Quick Hotbar Binding, Salvage, Blacksmith Enhancement (+0..+15 + Pity), and Boss Crafting UI.
/// </summary>
[DisallowMultipleComponent]
public sealed class MMOInventoryPanelUI : MonoBehaviour
{
    public enum BagFilter { All = 0, Equipment = 1, Consumable = 2, Material = 3 }

    private MMOPlayerCombat _combat;
    private MMOPlayerInventory _inventory;
    private Sprite _rectSprite;

    private GameObject _overlay;
    private BagFilter _filter = BagFilter.All;
    private bool _bossCraftTab;
    private string _selectedUid;
    private int _dragSourceBagSlot = -1;

    // Left Column: Paperdoll + Live Stats
    private readonly Button[] _equipBtns = new Button[5];
    private readonly Outline[] _equipOutlines = new Outline[5];
    private readonly TMP_Text[] _equipTexts = new TMP_Text[5];
    private TMP_Text _charStatsText;

    // Middle Column: 32-Slot Grid Bag + Toolbar
    private readonly Button[] _filterBtns = new Button[4];
    private readonly BagSlotWidget[] _bagWidgets = new BagSlotWidget[MMOPlayerInventory.MaxBagSlots];
    private TMP_Text _autoLootBtnText;
    private TMP_Text _bagHeaderText;

    // Right Column: Inspector / Blacksmith / Boss Craft
    private GameObject _inspectSection;
    private GameObject _craftSection;
    private TMP_Text _inspectTitle;
    private TMP_Text _inspectBody;
    private TMP_Text _primaryActionText;
    private TMP_Text _lockBtnText;
    private TMP_Text _salvageBtnText;
    private TMP_Text _enhanceInfoText;
    private TMP_Text _enhanceBtnText;
    private Image _pityBarFill;
    private GameObject _hotbarBindRow;
    private readonly TMP_Text[] _craftBtnTexts = new TMP_Text[4];
    private TMP_Text _craftHeaderMaterialsText;

    // Hover Tooltip
    private GameObject _tooltipRoot;
    private Outline _tooltipOutline;
    private TMP_Text _tooltipTitle;
    private TMP_Text _tooltipBody;

    private static readonly EquipSlot[] SlotOrder =
    {
        EquipSlot.Weapon, EquipSlot.Helmet, EquipSlot.Armor, EquipSlot.Boots, EquipSlot.Relic
    };

    private struct BagSlotWidget
    {
        public GameObject root;
        public Image bg;
        public Outline outline;
        public TMP_Text codeText;
        public TMP_Text metaText;
        public int slotIndex;
    }

    public sealed class SlotPointerProxy : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
    {
        public MMOInventoryPanelUI owner;
        public int bagIndex = -1;
        public EquipSlot equipSlot = EquipSlot.None;

        public void OnPointerEnter(PointerEventData eventData) => owner?.OnSlotHoverEnter(bagIndex, equipSlot);
        public void OnPointerExit(PointerEventData eventData) => owner?.OnSlotHoverExit();
        public void OnPointerClick(PointerEventData eventData) => owner?.OnSlotClicked(bagIndex, equipSlot, eventData.button);
        public void OnBeginDrag(PointerEventData eventData) => owner?.OnSlotBeginDrag(bagIndex);
        public void OnDrag(PointerEventData eventData) { }
        public void OnEndDrag(PointerEventData eventData) => owner?.OnSlotEndDrag();
        public void OnDrop(PointerEventData eventData) => owner?.OnSlotDrop(bagIndex);
    }

    public void Build(Transform canvasTransform, MMOPlayerCombat combat, MMOPlayerInventory inventory, Sprite rectSprite)
    {
        _combat = combat;
        _inventory = inventory;
        _rectSprite = rectSprite;

        BuildModalHierarchy(canvasTransform);
        BuildTooltip(canvasTransform);
        SetVisible(false);
    }

    public void SetVisible(bool visible)
    {
        if (_overlay == null) return;
        if (_overlay.activeSelf != visible)
        {
            _overlay.SetActive(visible);
        }
        if (!visible && _tooltipRoot != null)
        {
            _tooltipRoot.SetActive(false);
            _dragSourceBagSlot = -1;
        }
        if (visible)
        {
            Refresh();
        }
    }

    private void Update()
    {
        if (_tooltipRoot != null && _tooltipRoot.activeSelf)
        {
            RectTransform rt = _tooltipRoot.GetComponent<RectTransform>();
            Vector2 pos = Input.mousePosition;
            float xOffset = pos.x > Screen.width * 0.68f ? -310f : 22f;
            float yOffset = pos.y < Screen.height * 0.35f ? 22f : -22f;
            rt.position = pos + new Vector2(xOffset, yOffset);
        }
    }

    // =========================================================
    // UI CONSTRUCTION
    // =========================================================

    private void BuildModalHierarchy(Transform canvasTransform)
    {
        _overlay = CreatePanel("AAAInventoryOverlay", canvasTransform, Vector2.zero, Vector2.one,
            Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.64f));
        RectTransform ort = _overlay.GetComponent<RectTransform>();
        ort.anchorMin = Vector2.zero;
        ort.anchorMax = Vector2.one;
        ort.sizeDelta = Vector2.zero;

        GameObject win = CreatePanel("InventoryWindow", _overlay.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1180f, 670f),
            new Color(0.06f, 0.08f, 0.12f, 0.98f));
        AddOutline(win, new Color(0.95f, 0.76f, 0.28f, 0.85f));

        TMP_Text title = CreateText("Title", win.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(20f, -12f), new Vector2(-180f, 36f), 22, FontStyles.Bold, TextAlignmentOptions.Left,
            new Color(1f, 0.86f, 0.30f));
        title.rectTransform.pivot = new Vector2(0f, 1f);
        title.text = "CHARACTER EQUIPMENT, BAG & BLACKSMITH FORGE";

        Button closeBtn = CreateButton("CloseBtn", win.transform, new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-16f, -12f), new Vector2(130f, 34f), new Color(0.58f, 0.20f, 0.22f, 1f), "Close [I]", 14);
        closeBtn.GetComponent<RectTransform>().pivot = new Vector2(1f, 1f);
        closeBtn.onClick.AddListener(() => _combat?.ToggleInventoryPanel());

        BuildEquipmentColumn(win.transform);
        BuildBagColumn(win.transform);
        BuildRightForgeColumn(win.transform);
    }

    private void BuildEquipmentColumn(Transform parent)
    {
        GameObject col = CreatePanel("EquipColumn", parent, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(16f, -18f), new Vector2(280f, 590f), new Color(0.09f, 0.11f, 0.16f, 0.95f));
        col.GetComponent<RectTransform>().pivot = new Vector2(0f, 0.5f);
        AddOutline(col, new Color(0.32f, 0.52f, 0.82f, 0.45f));

        TMP_Text hdr = CreateText("EquipHeader", col.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(10f, -8f), new Vector2(-20f, 24f), 15, FontStyles.Bold, TextAlignmentOptions.Center,
            new Color(0.75f, 0.90f, 1f));
        hdr.rectTransform.pivot = new Vector2(0.5f, 1f);
        hdr.text = "EQUIPPED GEAR (R-Click Unequip)";

        for (int i = 0; i < SlotOrder.Length; i++)
        {
            EquipSlot slot = SlotOrder[i];
            Button btn = CreateButton($"EquipSlot_{slot}", col.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -40f - i * 64f), new Vector2(256f, 56f),
                new Color(0.12f, 0.15f, 0.22f, 1f), "", 13);
            btn.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 1f);

            Outline border = AddOutlineComponent(btn.gameObject, new Color(0.35f, 0.42f, 0.55f, 0.8f));
            TMP_Text txt = btn.GetComponentInChildren<TMP_Text>();
            txt.textWrappingMode = TextWrappingModes.NoWrap;

            SlotPointerProxy proxy = btn.gameObject.AddComponent<SlotPointerProxy>();
            proxy.owner = this;
            proxy.equipSlot = slot;

            _equipBtns[i] = btn;
            _equipOutlines[i] = border;
            _equipTexts[i] = txt;
        }

        _charStatsText = CreateText("CharStatsText", col.transform,
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(12f, 12f), new Vector2(-24f, 214f),
            13, FontStyles.Normal, TextAlignmentOptions.TopLeft, new Color(0.90f, 0.94f, 1f));
        _charStatsText.rectTransform.pivot = new Vector2(0f, 0f);
        _charStatsText.textWrappingMode = TextWrappingModes.Normal;
    }

    private void BuildBagColumn(Transform parent)
    {
        GameObject col = CreatePanel("BagColumn", parent, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(308f, -18f), new Vector2(510f, 590f), new Color(0.09f, 0.11f, 0.16f, 0.95f));
        col.GetComponent<RectTransform>().pivot = new Vector2(0f, 0.5f);
        AddOutline(col, new Color(0.32f, 0.52f, 0.82f, 0.45f));

        _bagHeaderText = CreateText("BagHeader", col.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(12f, -8f), new Vector2(-24f, 24f), 15, FontStyles.Bold, TextAlignmentOptions.Left, Color.white);
        _bagHeaderText.rectTransform.pivot = new Vector2(0f, 1f);

        string[] filterLabels = { "All", "Gear", "Consumables", "Materials" };
        for (int i = 0; i < 4; i++)
        {
            int idx = i;
            Button fBtn = CreateButton($"Filter_{i}", col.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(12f + i * 122f, -38f), new Vector2(116f, 30f),
                new Color(0.16f, 0.22f, 0.34f, 1f), filterLabels[i], 13);
            fBtn.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
            fBtn.onClick.AddListener(() => { _filter = (BagFilter)idx; Refresh(); });
            _filterBtns[i] = fBtn;
        }

        // 8 x 4 Grid Slots (32 slots)
        const int cols = 8;
        const float cellSize = 56f;
        const float gap = 5f;
        for (int i = 0; i < MMOPlayerInventory.MaxBagSlots; i++)
        {
            int row = i / cols;
            int c = i % cols;
            float x = 14f + c * (cellSize + gap);
            float y = -78f - row * (cellSize + gap);

            GameObject cell = CreatePanel($"BagCell_{i}", col.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(x, y), new Vector2(cellSize, cellSize),
                new Color(0.12f, 0.14f, 0.20f, 1f));
            cell.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
            Outline border = AddOutlineComponent(cell, new Color(0.28f, 0.34f, 0.44f, 0.7f));
            cell.AddComponent<Button>();

            TMP_Text code = CreateText("Code", cell.transform, new Vector2(0f, 0.42f), new Vector2(1f, 1f),
                Vector2.zero, Vector2.zero, 13, FontStyles.Bold, TextAlignmentOptions.Center, Color.white);
            TMP_Text meta = CreateText("Meta", cell.transform, new Vector2(0f, 0f), new Vector2(1f, 0.46f),
                Vector2.zero, Vector2.zero, 11, FontStyles.Bold, TextAlignmentOptions.Center, new Color(1f, 0.88f, 0.45f));

            SlotPointerProxy proxy = cell.AddComponent<SlotPointerProxy>();
            proxy.owner = this;
            proxy.bagIndex = i;

            _bagWidgets[i] = new BagSlotWidget
            {
                root = cell,
                bg = cell.GetComponent<Image>(),
                outline = border,
                codeText = code,
                metaText = meta,
                slotIndex = i
            };
        }

        // Bag Toolbar + Camp Shop
        Button autoLootBtn = CreateButton("AutoLootToggle", col.transform,
            new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(14f, 170f), new Vector2(236f, 34f),
            new Color(0.18f, 0.34f, 0.52f, 1f), "Auto-Loot All: OFF", 13);
        autoLootBtn.GetComponent<RectTransform>().pivot = new Vector2(0f, 0f);
        _autoLootBtnText = autoLootBtn.GetComponentInChildren<TMP_Text>();
        autoLootBtn.onClick.AddListener(() =>
        {
            if (_inventory == null) return;
            _inventory.AutoLootAll = !_inventory.AutoLootAll;
            _inventory.SaveToCloud();
            Refresh();
        });

        Button salvageJunkBtn = CreateButton("SalvageJunkBtn", col.transform,
            new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(258f, 170f), new Vector2(238f, 34f),
            new Color(0.56f, 0.28f, 0.16f, 1f), "Salvage Junk (White/Green)", 13);
        salvageJunkBtn.GetComponent<RectTransform>().pivot = new Vector2(0f, 0f);
        salvageJunkBtn.onClick.AddListener(() => _inventory?.SalvageAllUnlockedJunk(ItemRarity.Uncommon));

        TMP_Text shopHdr = CreateText("ShopHeader", col.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(14f, 134f), new Vector2(-28f, 22f), 13, FontStyles.Bold, TextAlignmentOptions.Left,
            new Color(1f, 0.86f, 0.35f));
        shopHdr.rectTransform.pivot = new Vector2(0f, 0f);
        shopHdr.text = "CAMP SUPPLY SHOP (Click to Buy into Bag)";

        AddShopBtn(col.transform, new Vector2(14f, 84f), new Vector2(156f, 40f), "Minor HP Pot\n50G", "potion_minor", 50);
        AddShopBtn(col.transform, new Vector2(177f, 84f), new Vector2(156f, 40f), "Greater HP Pot\n110G", "potion_greater", 110);
        AddShopBtn(col.transform, new Vector2(340f, 84f), new Vector2(156f, 40f), "Berserk Elixir\n130G", "elixir_berserk", 130);
        AddShopBtn(col.transform, new Vector2(14f, 36f), new Vector2(238f, 40f), "Wind Haste Scroll (120G)", "scroll_haste", 120);
        AddShopBtn(col.transform, new Vector2(258f, 36f), new Vector2(238f, 40f), "Thunder Bomb (140G)", "thunder_bomb", 140);

        TMP_Text hint = CreateText("BagHint", col.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(14f, 8f), new Vector2(-28f, 20f), 11, FontStyles.Italic, TextAlignmentOptions.Center,
            new Color(0.75f, 0.82f, 0.92f));
        hint.rectTransform.pivot = new Vector2(0.5f, 0f);
        hint.text = "L-Click: Select | R-Click: Use/Equip | Drag-Drop: Swap Slots";
    }

    private void AddShopBtn(Transform parent, Vector2 pos, Vector2 size, string label, string itemId, int cost)
    {
        Button btn = CreateButton($"Shop_{itemId}", parent, Vector2.zero, Vector2.zero, pos, size,
            new Color(0.16f, 0.36f, 0.28f, 1f), label, 12);
        btn.GetComponent<RectTransform>().pivot = Vector2.zero;
        btn.onClick.AddListener(() => { _combat?.TryBuyShopItem(itemId, cost); Refresh(); });
    }

    private void BuildRightForgeColumn(Transform parent)
    {
        GameObject col = CreatePanel("ForgeColumn", parent, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(830f, -18f), new Vector2(334f, 590f), new Color(0.09f, 0.11f, 0.16f, 0.95f));
        col.GetComponent<RectTransform>().pivot = new Vector2(0f, 0.5f);
        AddOutline(col, new Color(0.95f, 0.72f, 0.25f, 0.55f));

        Button tabInspect = CreateButton("TabInspect", col.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(12f, -10f), new Vector2(152f, 32f), new Color(0.24f, 0.44f, 0.74f, 1f), "Inspect & Enhance", 13);
        tabInspect.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
        tabInspect.onClick.AddListener(() => { _bossCraftTab = false; Refresh(); });

        Button tabCraft = CreateButton("TabCraft", col.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(170f, -10f), new Vector2(152f, 32f), new Color(0.62f, 0.34f, 0.18f, 1f), "Boss Craft", 13);
        tabCraft.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
        tabCraft.onClick.AddListener(() => { _bossCraftTab = true; Refresh(); });

        // Section 1: Inspector + Blacksmith
        _inspectSection = new GameObject("InspectSection");
        RectTransform irt = _inspectSection.AddComponent<RectTransform>();
        irt.SetParent(col.transform, false);
        irt.anchorMin = Vector2.zero;
        irt.anchorMax = Vector2.one;
        irt.offsetMin = new Vector2(12f, 12f);
        irt.offsetMax = new Vector2(-12f, -50f);

        _inspectTitle = CreateText("InspectTitle", irt, new Vector2(0f, 1f), new Vector2(1f, 1f),
            Vector2.zero, new Vector2(0f, 28f), 16, FontStyles.Bold, TextAlignmentOptions.Left, Color.white);
        _inspectTitle.rectTransform.pivot = new Vector2(0f, 1f);

        _inspectBody = CreateText("InspectBody", irt, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0f, -32f), new Vector2(0f, 165f), 13, FontStyles.Normal, TextAlignmentOptions.TopLeft,
            new Color(0.88f, 0.92f, 0.98f));
        _inspectBody.rectTransform.pivot = new Vector2(0f, 1f);
        _inspectBody.textWrappingMode = TextWrappingModes.Normal;

        Button primaryBtn = CreateButton("PrimaryActionBtn", irt, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, -206f), new Vector2(150f, 34f), new Color(0.18f, 0.54f, 0.34f, 1f), "Equip / Use", 13);
        primaryBtn.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
        _primaryActionText = primaryBtn.GetComponentInChildren<TMP_Text>();
        primaryBtn.onClick.AddListener(OnClickPrimaryAction);

        Button lockBtn = CreateButton("LockBtn", irt, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(158f, -206f), new Vector2(72f, 34f), new Color(0.28f, 0.34f, 0.46f, 1f), "Lock", 12);
        lockBtn.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
        _lockBtnText = lockBtn.GetComponentInChildren<TMP_Text>();
        lockBtn.onClick.AddListener(() => { if (!string.IsNullOrEmpty(_selectedUid)) { _inventory?.ToggleLock(_selectedUid); Refresh(); } });

        Button salvageBtn = CreateButton("SalvageBtn", irt, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(236f, -206f), new Vector2(74f, 34f), new Color(0.56f, 0.24f, 0.20f, 1f), "Salvage", 12);
        salvageBtn.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
        _salvageBtnText = salvageBtn.GetComponentInChildren<TMP_Text>();
        salvageBtn.onClick.AddListener(() => { if (!string.IsNullOrEmpty(_selectedUid)) { _inventory?.SalvageByUid(_selectedUid); Refresh(); } });

        // Hotbar [1..5] Assign Row
        _hotbarBindRow = CreatePanel("HotbarBindRow", irt, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0f, -248f), new Vector2(0f, 54f), new Color(0.12f, 0.14f, 0.20f, 0.9f));
        _hotbarBindRow.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 1f);
        TMP_Text bindHdr = CreateText("BindLbl", _hotbarBindRow.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(6f, -2f), new Vector2(-12f, 18f), 11, FontStyles.Bold, TextAlignmentOptions.Left, new Color(0.75f, 0.90f, 1f));
        bindHdr.rectTransform.pivot = new Vector2(0f, 1f);
        bindHdr.text = "Assign Consumable to Quick Hotbar:";
        for (int h = 0; h < 5; h++)
        {
            int slotIdx = h;
            Button hbBtn = CreateButton($"BindHB_{h}", _hotbarBindRow.transform,
                new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(6f + h * 60f, 4f), new Vector2(56f, 26f),
                new Color(0.20f, 0.34f, 0.56f, 1f), $"[{h + 1}]", 12);
            hbBtn.GetComponent<RectTransform>().pivot = Vector2.zero;
            hbBtn.onClick.AddListener(() => OnClickBindHotbar(slotIdx));
        }

        // Blacksmith Enhance Panel
        GameObject smith = CreatePanel("BlacksmithBox", irt, new Vector2(0f, 0f), new Vector2(1f, 0f),
            Vector2.zero, new Vector2(0f, 212f), new Color(0.12f, 0.10f, 0.14f, 0.95f));
        smith.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 0f);
        AddOutline(smith, new Color(1f, 0.55f, 0.18f, 0.65f));

        TMP_Text smithHdr = CreateText("SmithHdr", smith.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(10f, -6f), new Vector2(-20f, 22f), 14, FontStyles.Bold, TextAlignmentOptions.Center, new Color(1f, 0.72f, 0.25f));
        smithHdr.rectTransform.pivot = new Vector2(0.5f, 1f);
        smithHdr.text = "BLACKSMITH ENHANCE (+0 -> +15)";

        _enhanceInfoText = CreateText("EnhanceInfo", smith.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(10f, -32f), new Vector2(-20f, 92f), 12, FontStyles.Normal, TextAlignmentOptions.TopLeft, Color.white);
        _enhanceInfoText.rectTransform.pivot = new Vector2(0f, 1f);
        _enhanceInfoText.textWrappingMode = TextWrappingModes.Normal;

        GameObject pityBg = CreatePanel("PityBar", smith.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(10f, 58f), new Vector2(-20f, 14f), new Color(0.20f, 0.14f, 0.16f, 1f));
        pityBg.GetComponent<RectTransform>().pivot = new Vector2(0f, 0f);
        GameObject pityFill = CreatePanel("Fill", pityBg.transform, Vector2.zero, Vector2.one,
            Vector2.zero, Vector2.zero, new Color(1f, 0.62f, 0.18f, 1f));
        _pityBarFill = pityFill.GetComponent<Image>();
        _pityBarFill.type = Image.Type.Filled;
        _pityBarFill.fillMethod = Image.FillMethod.Horizontal;

        Button enhanceBtn = CreateButton("EnhanceBtn", smith.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(10f, 10f), new Vector2(-20f, 40f), new Color(0.76f, 0.36f, 0.14f, 1f), "ENHANCE GEAR!", 15);
        enhanceBtn.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 0f);
        _enhanceBtnText = enhanceBtn.GetComponentInChildren<TMP_Text>();
        enhanceBtn.onClick.AddListener(() => { if (!string.IsNullOrEmpty(_selectedUid)) { _inventory?.TryEnhanceItem(_selectedUid, out _); Refresh(); } });

        // Section 2: Boss Craft
        _craftSection = new GameObject("CraftSection");
        RectTransform crt = _craftSection.AddComponent<RectTransform>();
        crt.SetParent(col.transform, false);
        crt.anchorMin = Vector2.zero;
        crt.anchorMax = Vector2.one;
        crt.offsetMin = new Vector2(12f, 12f);
        crt.offsetMax = new Vector2(-12f, -50f);

        _craftHeaderMaterialsText = CreateText("CraftMaterials", crt, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0f, -6f), new Vector2(0f, 44f), 13, FontStyles.Bold, TextAlignmentOptions.TopLeft,
            new Color(1f, 0.86f, 0.35f));
        _craftHeaderMaterialsText.rectTransform.pivot = new Vector2(0f, 1f);
        _craftHeaderMaterialsText.textWrappingMode = TextWrappingModes.Normal;

        for (int r = 0; r < MMOPlayerInventory.BossRecipes.Length; r++)
        {
            int idx = r;
            Button craftBtn = CreateButton($"CraftRecipe_{r}", crt,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -64f - r * 102f), new Vector2(0f, 90f),
                new Color(0.18f, 0.14f, 0.24f, 1f), "", 13);
            craftBtn.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 1f);
            AddOutline(craftBtn.gameObject, new Color(1f, 0.65f, 0.18f, 0.65f));
            _craftBtnTexts[r] = craftBtn.GetComponentInChildren<TMP_Text>();
            _craftBtnTexts[r].textWrappingMode = TextWrappingModes.Normal;
            craftBtn.onClick.AddListener(() => { _inventory?.TryCraftBossRecipe(idx, out _); Refresh(); });
        }
    }

    private void BuildTooltip(Transform canvasTransform)
    {
        _tooltipRoot = CreatePanel("ItemHoverTooltip", canvasTransform, Vector2.zero, Vector2.zero,
            Vector2.zero, new Vector2(300f, 210f), new Color(0.04f, 0.05f, 0.09f, 0.97f));
        _tooltipRoot.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
        _tooltipOutline = AddOutlineComponent(_tooltipRoot, new Color(1f, 0.8f, 0.25f, 0.9f));

        _tooltipTitle = CreateText("Title", _tooltipRoot.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(10f, -8f), new Vector2(-20f, 26f), 15, FontStyles.Bold, TextAlignmentOptions.Left, Color.white);
        _tooltipTitle.rectTransform.pivot = new Vector2(0f, 1f);

        _tooltipBody = CreateText("Body", _tooltipRoot.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(10f, -38f), new Vector2(-20f, 162f), 12, FontStyles.Normal, TextAlignmentOptions.TopLeft,
            new Color(0.90f, 0.94f, 1f));
        _tooltipBody.rectTransform.pivot = new Vector2(0f, 1f);
        _tooltipBody.textWrappingMode = TextWrappingModes.Normal;

        // Disable raycast so tooltip never steals hover
        foreach (Graphic g in _tooltipRoot.GetComponentsInChildren<Graphic>(true))
        {
            g.raycastTarget = false;
        }
        _tooltipRoot.SetActive(false);
    }

    // =========================================================
    // REFRESH & POINTER INTERACTIONS
    // =========================================================

    public void Refresh()
    {
        if (_inventory == null || _overlay == null || !_overlay.activeSelf) return;

        // 1. Equipped slots + total stats
        for (int i = 0; i < SlotOrder.Length; i++)
        {
            EquipSlot slot = SlotOrder[i];
            MMOItemInstance eq = _inventory.GetEquipped(slot);
            if (eq != null)
            {
                MMOItemTemplate tpl = MMOItemCatalog.Get(eq.itemId);
                string plus = eq.upgradeLevel > 0 ? $" +{eq.upgradeLevel}" : "";
                Color col = MMOItemCatalog.GetRarityColor(eq.rarity);
                _equipOutlines[i].effectColor = col;
                _equipTexts[i].color = col;
                _equipTexts[i].text = $"{slot}: {tpl?.displayName}{plus} [{eq.rarity}]";
            }
            else
            {
                _equipOutlines[i].effectColor = new Color(0.32f, 0.38f, 0.48f, 0.7f);
                _equipTexts[i].color = new Color(0.62f, 0.68f, 0.78f);
                _equipTexts[i].text = $"{slot}: (Empty)";
            }
        }

        if (_combat != null)
        {
            ComputedGearStats gs = _inventory.GetTotalEquippedStats(out int setCount);
            _charStatsText.text =
                $"<color=#FFD94D>COMBAT STATS (Lv.{_combat.playerLevel})</color>\n" +
                $"Attack: {_combat.attack1MinDamage}-{_combat.attack2MaxDamage}  (Gear +{gs.attack})\n" +
                $"Max HP: {_combat.MaxHealth}  (Gear +{gs.maxHp})\n" +
                $"Crit: {Mathf.RoundToInt(_combat.baseCritChance * 100f)}%  |  Crit DMG: x{_combat.critDamageMultiplier:0.00}\n" +
                $"Move Speed: +{gs.moveSpeedPct:0}%  |  Life Steal: {gs.lifeStealPct:0.0}%\n" +
                $"Skill CDR: -{gs.cooldownReductionPct:0}%\n" +
                $"<color=#FFA324>Warlord Set: {setCount}/4</color>" +
                (setCount >= 2 ? "\n<color=#47EB6B>(2pc) +35 ATK, +25% CritDMG</color>" : "") +
                (setCount >= 4 ? "\n<color=#47EB6B>(4pc) +300 HP, -15% CDR, +4% Leech</color>" : "");
        }

        // 2. Bag Grid
        int used = 0;
        for (int i = 0; i < MMOPlayerInventory.MaxBagSlots; i++)
        {
            MMOItemInstance item = _inventory.GetBagSlot(i);
            BagSlotWidget w = _bagWidgets[i];
            if (item != null) used++;

            bool matchesFilter = item != null && MatchesFilter(item);
            if (item != null && matchesFilter)
            {
                MMOItemTemplate tpl = MMOItemCatalog.Get(item.itemId);
                Color rarityCol = MMOItemCatalog.GetRarityColor(item.rarity);
                bool selected = item.uid == _selectedUid;
                w.bg.color = selected ? new Color(0.24f, 0.30f, 0.42f, 1f) : new Color(0.13f, 0.16f, 0.23f, 1f);
                w.outline.effectColor = selected ? Color.white : rarityCol;
                w.codeText.color = rarityCol;
                w.codeText.text = tpl != null ? tpl.shortCode : "ITM";

                string lockTag = item.isLocked ? "*" : "";
                w.metaText.text = item.upgradeLevel > 0
                    ? $"+{item.upgradeLevel}{lockTag}"
                    : (item.stackCount > 1 ? $"x{item.stackCount}" : lockTag);
            }
            else
            {
                w.bg.color = item == null ? new Color(0.09f, 0.11f, 0.15f, 0.9f) : new Color(0.08f, 0.09f, 0.12f, 0.4f);
                w.outline.effectColor = new Color(0.24f, 0.28f, 0.36f, 0.5f);
                w.codeText.text = "";
                w.metaText.text = "";
            }
        }

        _bagHeaderText.text = $"ADVENTURER BAG ({used}/{MMOPlayerInventory.MaxBagSlots})   |   Gold: {_combat?.Gold ?? 0}G   |   Stones: {_inventory.GetItemCount("enhance_stone")}";
        _autoLootBtnText.text = _inventory.AutoLootAll ? "Auto-Loot All: ON" : "Auto-Loot All: OFF (Press [F])";

        for (int f = 0; f < 4; f++)
        {
            _filterBtns[f].GetComponent<Image>().color = (int)_filter == f
                ? new Color(0.28f, 0.52f, 0.88f, 1f)
                : new Color(0.14f, 0.18f, 0.28f, 1f);
        }

        // 3. Right Column
        _inspectSection.SetActive(!_bossCraftTab);
        _craftSection.SetActive(_bossCraftTab);
        if (_bossCraftTab) RefreshCraftSection();
        else RefreshInspectorSection();
    }

    private void RefreshInspectorSection()
    {
        MMOItemInstance item = _inventory.FindByUid(_selectedUid, out int bagIdx, out EquipSlot eqSlot);
        if (item == null)
        {
            _inspectTitle.text = "No Item Selected";
            _inspectTitle.color = Color.white;
            _inspectBody.text = "Click any item in your Bag or Equipped Gear slots to inspect stats, equip, salvage, bind to Hotbar [1-5], or enhance at the Blacksmith.";
            _hotbarBindRow.SetActive(false);
            _enhanceInfoText.text = "Select an Equipment piece to enhance (+0 -> +15).";
            _pityBarFill.fillAmount = 0f;
            _enhanceBtnText.text = "Select Gear";
            return;
        }

        MMOItemTemplate tpl = MMOItemCatalog.Get(item.itemId);
        Color col = MMOItemCatalog.GetRarityColor(item.rarity);
        string plus = item.upgradeLevel > 0 ? $" +{item.upgradeLevel}" : "";
        _inspectTitle.text = $"[{item.rarity}] {tpl?.displayName}{plus}";
        _inspectTitle.color = col;

        var sb = new StringBuilder();
        sb.AppendLine(tpl?.description);
        if (tpl != null && tpl.category == ItemCategory.Equipment)
        {
            ComputedGearStats st = MMOItemCatalog.EvaluateStats(item);
            if (st.attack > 0) sb.AppendLine($"Attack Power: +{st.attack}");
            if (st.maxHp > 0) sb.AppendLine($"Max HP: +{st.maxHp}");
            if (st.critChancePct > 0) sb.AppendLine($"Crit Chance: +{st.critChancePct:0.0}%");
            if (st.moveSpeedPct > 0) sb.AppendLine($"Move Speed: +{st.moveSpeedPct:0}%");
            for (int i = 0; i < item.subStats.Count; i++)
            {
                sb.AppendLine($"<color=#78E8FF>  • {MMOItemCatalog.FormatSubStat(item.subStats[i])}</color>");
            }
        }
        else if (item.stackCount > 1)
        {
            sb.AppendLine($"Stack Quantity: {item.stackCount}");
        }
        _inspectBody.text = sb.ToString();

        _primaryActionText.text = eqSlot != EquipSlot.None ? "Unequip"
            : (tpl?.category == ItemCategory.Equipment ? "Equip"
            : (tpl?.category == ItemCategory.Consumable ? "Use Now" : "Material"));
        _lockBtnText.text = item.isLocked ? "Unlock" : "Lock";
        _salvageBtnText.text = (bagIdx >= 0 && !item.isLocked) ? "Salvage" : "---";

        _hotbarBindRow.SetActive(tpl != null && tpl.category == ItemCategory.Consumable);

        if (tpl != null && tpl.category == ItemCategory.Equipment)
        {
            if (item.upgradeLevel >= MMOPlayerInventory.MaxUpgradeLevel)
            {
                _enhanceInfoText.text = $"MAX LEVEL (+{MMOPlayerInventory.MaxUpgradeLevel}) REACHED!";
                _pityBarFill.fillAmount = 1f;
                _enhanceBtnText.text = "MAXED OUT";
            }
            else
            {
                MMOPlayerInventory.GetEnhanceCosts(item, out int gCost, out int sCost, out int pct);
                _enhanceInfoText.text =
                    $"Next: +{item.upgradeLevel + 1} (+12% Base Stats)\n" +
                    $"Success Chance: <color=#FFD94D>{pct}%</color>   |   Pity: {item.pityPercent}%\n" +
                    $"Cost: {gCost}G + {sCost} Stones (You: {_combat?.Gold ?? 0}G, {_inventory.GetItemCount("enhance_stone")} Stn)";
                _pityBarFill.fillAmount = Mathf.Clamp01(item.pityPercent / 100f);
                _enhanceBtnText.text = $"ENHANCE TO +{item.upgradeLevel + 1}";
            }
        }
        else
        {
            _enhanceInfoText.text = "Select an Equipment item to enhance.";
            _pityBarFill.fillAmount = 0f;
            _enhanceBtnText.text = "Equipment Only";
        }
    }

    private void RefreshCraftSection()
    {
        int cores = _inventory.GetItemCount("warlord_core");
        int stones = _inventory.GetItemCount("enhance_stone");
        _craftHeaderMaterialsText.text =
            $"BOSS FORGE MATERIALS:\nWarlord Cores: <color=#C25CFF>{cores}</color>   |   Stones: {stones}   |   Gold: {_combat?.Gold ?? 0}G";

        for (int i = 0; i < MMOPlayerInventory.BossRecipes.Length; i++)
        {
            var r = MMOPlayerInventory.BossRecipes[i];
            MMOItemTemplate tpl = MMOItemCatalog.Get(r.itemId);
            string hex = MMOItemCatalog.GetRarityHex(r.minRarity);
            _craftBtnTexts[i].text =
                $"<color={hex}>[{r.minRarity}] {tpl?.displayName}</color> (Warlord Set)\n" +
                $"Needs: {r.coreCost} Warlord Core, {r.stoneCost} Stones, {r.goldCost}G";
        }
    }

    private void OnClickPrimaryAction()
    {
        MMOItemInstance item = _inventory.FindByUid(_selectedUid, out int bagIdx, out EquipSlot eqSlot);
        if (item == null) return;
        if (eqSlot != EquipSlot.None) _inventory.UnequipSlot(eqSlot);
        else if (bagIdx >= 0) _inventory.UseOrEquipBagSlot(bagIdx);
        Refresh();
    }

    private void OnClickBindHotbar(int hotbarSlot)
    {
        MMOItemInstance item = _inventory.FindByUid(_selectedUid, out _, out _);
        if (item == null) return;
        _inventory.BindHotbar(hotbarSlot, item.itemId);
        _combat?.ShowBanner($"Bound to Quick Slot [{hotbarSlot + 1}]!", 1.5f);
        Refresh();
    }

    private void OnSlotClicked(int bagIndex, EquipSlot equipSlot, PointerEventData.InputButton button)
    {
        if (equipSlot != EquipSlot.None)
        {
            MMOItemInstance eq = _inventory.GetEquipped(equipSlot);
            if (eq == null) return;
            _selectedUid = eq.uid;
            if (button == PointerEventData.InputButton.Right)
            {
                _inventory.UnequipSlot(equipSlot);
            }
            Refresh();
            return;
        }

        MMOItemInstance item = _inventory.GetBagSlot(bagIndex);
        if (item == null) return;
        _selectedUid = item.uid;
        if (button == PointerEventData.InputButton.Right)
        {
            _inventory.UseOrEquipBagSlot(bagIndex);
        }
        Refresh();
    }

    private void OnSlotBeginDrag(int bagIndex)
    {
        if (bagIndex >= 0 && _inventory.GetBagSlot(bagIndex) != null)
        {
            _dragSourceBagSlot = bagIndex;
        }
    }

    private void OnSlotEndDrag()
    {
        _dragSourceBagSlot = -1;
    }

    private void OnSlotDrop(int targetBagIndex)
    {
        if (_dragSourceBagSlot >= 0 && targetBagIndex >= 0)
        {
            _inventory.SwapBagSlots(_dragSourceBagSlot, targetBagIndex);
            Refresh();
        }
        _dragSourceBagSlot = -1;
    }

    private void OnSlotHoverEnter(int bagIndex, EquipSlot equipSlot)
    {
        if (_inventory == null || _tooltipRoot == null) return;
        MMOItemInstance item = equipSlot != EquipSlot.None
            ? _inventory.GetEquipped(equipSlot)
            : _inventory.GetBagSlot(bagIndex);
        if (item == null) return;

        MMOItemTemplate tpl = MMOItemCatalog.Get(item.itemId);
        if (tpl == null) return;

        Color col = MMOItemCatalog.GetRarityColor(item.rarity);
        _tooltipOutline.effectColor = col;
        _tooltipTitle.color = col;
        string plus = item.upgradeLevel > 0 ? $" +{item.upgradeLevel}" : "";
        _tooltipTitle.text = $"[{item.rarity}] {tpl.displayName}{plus}";

        var sb = new StringBuilder();
        sb.AppendLine(tpl.description);
        if (tpl.category == ItemCategory.Equipment)
        {
            ComputedGearStats st = MMOItemCatalog.EvaluateStats(item);
            MMOItemInstance equippedCompare = equipSlot == EquipSlot.None ? _inventory.GetEquipped(tpl.equipSlot) : null;
            ComputedGearStats eqSt = equippedCompare != null ? MMOItemCatalog.EvaluateStats(equippedCompare) : default;

            AppendCompareStat(sb, "Attack", st.attack, eqSt.attack, equippedCompare != null);
            AppendCompareStat(sb, "Max HP", st.maxHp, eqSt.maxHp, equippedCompare != null);
            if (st.critChancePct > 0 || eqSt.critChancePct > 0)
                sb.AppendLine($"Crit: +{st.critChancePct:0.0}%");
            for (int i = 0; i < item.subStats.Count; i++)
            {
                sb.AppendLine($"<color=#78E8FF>{MMOItemCatalog.FormatSubStat(item.subStats[i])}</color>");
            }
        }

        _tooltipBody.text = sb.ToString();
        _tooltipRoot.SetActive(true);
    }

    private static void AppendCompareStat(StringBuilder sb, string label, int value, int equippedVal, bool hasEquipped)
    {
        if (value == 0 && (!hasEquipped || equippedVal == 0)) return;
        if (!hasEquipped)
        {
            sb.AppendLine($"{label}: +{value}");
            return;
        }
        int diff = value - equippedVal;
        string diffText = diff > 0 ? $" <color=#47EB6B>(+{diff})</color>"
            : (diff < 0 ? $" <color=#FF5555>({diff})</color>" : "");
        sb.AppendLine($"{label}: +{value}{diffText}");
    }

    private void OnSlotHoverExit()
    {
        if (_tooltipRoot != null) _tooltipRoot.SetActive(false);
    }

    private bool MatchesFilter(MMOItemInstance item)
    {
        if (_filter == BagFilter.All) return true;
        MMOItemTemplate tpl = MMOItemCatalog.Get(item.itemId);
        if (tpl == null) return true;
        return _filter switch
        {
            BagFilter.Equipment => tpl.category == ItemCategory.Equipment,
            BagFilter.Consumable => tpl.category == ItemCategory.Consumable,
            BagFilter.Material => tpl.category == ItemCategory.Material,
            _ => true
        };
    }

    // =========================================================
    // LOCAL UI FACTORIES
    // =========================================================

    private GameObject CreatePanel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 pos, Vector2 size, Color color)
    {
        GameObject go = new GameObject(name);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = anchorMin;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        Image img = go.AddComponent<Image>();
        img.sprite = _rectSprite;
        img.color = color;
        return go;
    }

    private Button CreateButton(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 pos, Vector2 size, Color bgColor, string text, float fontSize)
    {
        GameObject go = CreatePanel(name, parent, anchorMin, anchorMax, pos, size, bgColor);
        Button btn = go.AddComponent<Button>();
        TMP_Text label = CreateText("Label", go.transform, Vector2.zero, Vector2.one,
            Vector2.zero, Vector2.zero, fontSize, FontStyles.Bold, TextAlignmentOptions.Center, Color.white);
        label.text = text;
        return btn;
    }

    private TMP_Text CreateText(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 pos, Vector2 size, float fontSize, FontStyles style, TextAlignmentOptions align, Color color)
    {
        GameObject go = new GameObject(name);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.color = color;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void AddOutline(GameObject go, Color color)
    {
        Outline ol = go.AddComponent<Outline>();
        ol.effectColor = color;
        ol.effectDistance = new Vector2(1.5f, 1.5f);
    }

    private static Outline AddOutlineComponent(GameObject go, Color color)
    {
        Outline ol = go.AddComponent<Outline>();
        ol.effectColor = color;
        ol.effectDistance = new Vector2(1.5f, 1.5f);
        return ol;
    }
}
