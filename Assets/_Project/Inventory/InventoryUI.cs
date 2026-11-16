using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Phase 2 MMORPG Inventory &amp; Character Upgrade Window:
///   - Top navigation tab switching seamlessly between [ World Map ] and [ Character &amp; Upgrade ].
///   - Left panel: Active Equipped Gear (Sword, Shield, Armor, Pants with icons &amp; tier border colors),
///     Total Stats summary, and Stat Point '+' allocation buttons (STR, VIT, END, AGI).
///   - Right panel: Dark 6x4 MMORPG Inventory Grid with Rarity Borders, Floating Hover Rarity Tooltip,
///     and Selected Item Use/Equip/Drop controls.
/// </summary>
public class InventoryUI : MonoBehaviour
{
    public static InventoryUI Instance { get; private set; }

    private const int Columns = 6;
    private const float SlotSize = 78f;
    private const float SlotGap = 8f;

    public bool IsOpen => _open;

    private InventoryManager _inv;
    private GameObject _panel;
    private Text _headerStatsText;
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
        public Text valueText;
        public Button plusBtn;
    }
    private readonly StatAllocWidget[] _statWidgets = new StatAllocWidget[4];

    // Floating Hover Rarity Tooltip
    private RectTransform _tooltipRect;
    private Image _tooltipBorder;
    private Text _tooltipTitle;
    private Text _tooltipBody;
    private int _hoveredIndex = -1;

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
        FillWithPadding(inner.rectTransform, 3f);

        // ============================================================
        // TOP NAVIGATION TAB BAR
        // ============================================================
        var navBg = MakeImage(inner.transform, "TopNavigationTabs", new Color(0.08f, 0.09f, 0.13f, 1f));
        Place(navBg.rectTransform, 0f, 0f, 974f, 46f);

        MakeTabButton(navBg.transform, "TabWorldMap", "[ World Map (M) ]", 14f, 200f, false, SwitchToWorldMapTab);
        MakeTabButton(navBg.transform, "TabCharacter", "[ Character & Upgrade ]", 222f, 230f, true, () => { });
        MakeTabButton(navBg.transform, "CloseBtn", "Close [I / Esc]", 826f, 134f, false, () => SetOpen(false));

        _headerStatsText = MakeLabel(navBg.transform, "HeaderStats", "", 13, FontStyle.Bold, TextAnchor.MiddleRight,
            new Color(0.85f, 0.88f, 0.94f), 460f, 8f, 350f, 30f);

        // ============================================================
        // LEFT COLUMN: CHARACTER STATS, EQUIPPED GEAR (4 SLOTS) & UPGRADES
        // ============================================================
        var leftCol = MakeImage(inner.transform, "CharacterColumn", new Color(0.07f, 0.08f, 0.12f, 1f));
        Place(leftCol.rectTransform, 14f, 58f, 418f, 548f);

        MakeLabel(leftCol.transform, "EquipSectionTitle", "EQUIPPED GEAR  (4 SLOTS)", 13, FontStyle.Bold, TextAnchor.MiddleLeft,
            new Color(1f, 0.84f, 0.28f), 12f, 10f, 394f, 22f);

        // 4 Equipment Slots (2x2 Grid: Sword, Shield, Armor, Pants)
        BuildEquipSlotWidget(leftCol.transform, 0, EquipmentSlotType.Sword, "SWORD", 12f, 36f);
        BuildEquipSlotWidget(leftCol.transform, 1, EquipmentSlotType.Shield, "SHIELD", 212f, 36f);
        BuildEquipSlotWidget(leftCol.transform, 2, EquipmentSlotType.Armor, "ARMOR", 12f, 128f);
        BuildEquipSlotWidget(leftCol.transform, 3, EquipmentSlotType.Pants, "PANTS", 212f, 128f);

        // Derived Total Stats Box
        var totalBox = MakeImage(leftCol.transform, "TotalStatsBox", new Color(0.05f, 0.06f, 0.09f, 1f));
        Place(totalBox.rectTransform, 12f, 224f, 394f, 92f);
        MakeLabel(totalBox.transform, "TotalTitle", "TOTAL CHARACTER STATS  (Base + Level + Gear)", 12, FontStyle.Bold, TextAnchor.MiddleLeft,
            new Color(0.55f, 0.82f, 1f), 10f, 6f, 374f, 20f);
        _totalStatsText = MakeLabel(totalBox.transform, "TotalBody", "", 13, FontStyle.Bold, TextAnchor.UpperLeft,
            Color.white, 10f, 30f, 374f, 58f);

        // Stat Points Allocation Box
        var allocBox = MakeImage(leftCol.transform, "StatAllocBox", new Color(0.05f, 0.06f, 0.09f, 1f));
        Place(allocBox.rectTransform, 12f, 326f, 394f, 210f);
        MakeLabel(allocBox.transform, "AllocTitle", "ATTRIBUTES & UPGRADE", 13, FontStyle.Bold, TextAnchor.MiddleLeft,
            new Color(1f, 0.84f, 0.28f), 10f, 8f, 210f, 22f);
        _statPointsText = MakeLabel(allocBox.transform, "UnspentPts", "Points: 0", 13, FontStyle.Bold, TextAnchor.MiddleRight,
            new Color(1f, 0.84f, 0.28f), 220f, 8f, 162f, 22f);

        BuildStatRow(allocBox.transform, 0, PlayerStatType.STR, "STR", "+4 ATK / pt", 42f);
        BuildStatRow(allocBox.transform, 1, PlayerStatType.VIT, "VIT", "+15 Max HP / pt", 82f);
        BuildStatRow(allocBox.transform, 2, PlayerStatType.END, "END", "+12 Max STA / pt", 122f);
        BuildStatRow(allocBox.transform, 3, PlayerStatType.AGI, "AGI", "+3% CRIT, +0.18 SPD", 162f);

        // ============================================================
        // RIGHT COLUMN: DARK MMORPG INVENTORY GRID & RARITY INSPECTOR
        // ============================================================
        var rightCol = MakeImage(inner.transform, "InventoryColumn", new Color(0.07f, 0.08f, 0.12f, 1f));
        Place(rightCol.rectTransform, 444f, 58f, 516f, 548f);

        MakeLabel(rightCol.transform, "InvTitle", "INVENTORY  (Click to Inspect / Hover for Tooltip)", 13, FontStyle.Bold, TextAnchor.MiddleLeft,
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
            btn.onClick.AddListener(() => Select(captured));

            var hover = bgImg.gameObject.AddComponent<SlotHoverRelay>();
            hover.owner = this;
            hover.slotIndex = captured;

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

        _useBtn = MakeActionButton(rightCol.transform, "UseBtn", "Use", 14f, 506f, 156f, 32f, () => { _inv.UseItem(_selected); }, out _);
        _equipBtn = MakeActionButton(rightCol.transform, "EquipBtn", "Equip", 180f, 506f, 156f, 32f, () => { _inv.EquipItem(_selected); }, out _equipBtnText);
        _dropBtn = MakeActionButton(rightCol.transform, "DropBtn", "Drop", 346f, 506f, 156f, 32f, () => { _inv.DropItem(_selected); }, out _);

        // Bottom Footer Hint
        MakeLabel(inner.transform, "Hint",
            "Rarity Tiers: Normal (Grey)   |   Rare (Blue)   |   Epic (Purple)   |   Legendary (Gold)   |   Press [M] World Map",
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
        _tooltipRect.sizeDelta = new Vector2(270f, 130f);

        var tipBg = MakeImage(_tooltipRect, "TipBg", new Color(0.04f, 0.05f, 0.08f, 0.98f));
        tipBg.raycastTarget = false;
        FillWithPadding(tipBg.rectTransform, 2f);

        _tooltipTitle = MakeLabel(tipBg.transform, "TipTitle", "", 13, FontStyle.Bold, TextAnchor.UpperLeft,
            Color.white, 8f, 6f, 250f, 34f);
        _tooltipBody = MakeLabel(tipBg.transform, "TipBody", "", 11, FontStyle.Normal, TextAnchor.UpperLeft,
            new Color(0.86f, 0.88f, 0.92f), 8f, 40f, 250f, 82f);

        _tooltipRect.gameObject.SetActive(false);

        _panel.SetActive(false);
        _inv.Changed += Refresh;
        Debug.Log("[InventoryUI] Phase 2 MMORPG Character & Inventory Hub built (" + count + " slots + 4 Gear Slots).");
    }

    private void BuildEquipSlotWidget(Transform parent, int idx, EquipmentSlotType slotType, string slotTitle, float x, float y)
    {
        var border = MakeImage(parent, "EquipBorder_" + slotTitle, new Color(0.24f, 0.27f, 0.34f, 1f));
        Place(border.rectTransform, x, y, 194f, 84f);

        var bg = MakeImage(border.transform, "Bg", new Color(0.09f, 0.11f, 0.15f, 1f));
        FillWithPadding(bg.rectTransform, 2f);

        var iconFrame = MakeImage(bg.transform, "IconFrame", new Color(0.14f, 0.16f, 0.22f, 1f));
        Place(iconFrame.rectTransform, 8f, 12f, 54f, 54f);

        var iconImg = MakeImage(iconFrame.transform, "Icon", Color.white);
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

        MakeLabel(rowBg.transform, "StatLabel", $"{statName}  <color=#95A2B8>({desc})</color>", 12, FontStyle.Bold, TextAnchor.MiddleLeft,
            Color.white, 10f, 4f, 224f, 26f);

        var valLbl = MakeLabel(rowBg.transform, "StatValue", "0", 14, FontStyle.Bold, TextAnchor.MiddleRight,
            new Color(1f, 0.88f, 0.35f), 238f, 4f, 64f, 26f);

        var plus = MakeActionButton(rowBg.transform, "PlusBtn", "+", 312f, 4f, 52f, 26f, () =>
        {
            if (PlayerCombat.Local != null && PlayerCombat.Local.AllocateStatPoint(statType))
                Refresh();
        }, out _);

        _statWidgets[idx] = new StatAllocWidget
        {
            statType = statType,
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

        if (_open && _hoveredIndex >= 0)
            UpdateHoverTooltipPosition();
    }

    public void SwitchToWorldMapTab()
    {
        SetOpen(false);
        if (MapManager.Instance != null)
            MapManager.Instance.SetWorldMapOpen(true);
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
        if (!open) HideTooltip();

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

    private void Select(int index)
    {
        _selected = index;
        Refresh();
    }

    private void OnSlotHoverEnter(int index)
    {
        _hoveredIndex = index;
        if (!_open || _inv == null || index < 0 || index >= _inv.Slots.Count) { HideTooltip(); return; }

        var slot = _inv.Slots[index];
        if (slot == null || slot.IsEmpty) { HideTooltip(); return; }

        var item = slot.item;
        Color tierCol = ItemData.GetTierColor(item.tier);
        _tooltipBorder.color = tierCol;
        _tooltipTitle.color = tierCol;
        _tooltipTitle.text = $"{item.displayName}\n[{ItemData.GetTierDisplayName(item.tier)} {FormatItemCategory(item)}]";
        _tooltipBody.text = BuildItemStatSummary(item, includeDesc: true);

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
        // Clamp horizontally inside frame
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

        if (_headerStatsText != null)
        {
            int nextExp = combat != null ? combat.ExpToNextLevel : 100;
            _headerStatsText.text = $"Lv.{_inv.Level}   |   EXP {_inv.Experience}/{nextExp}";
        }

        // Refresh 4 Equipment Slots (Sword, Shield, Armor, Pants)
        for (int i = 0; i < _equipWidgets.Length; i++)
        {
            var w = _equipWidgets[i];
            if (w == null) continue;
            ItemData equipped = _inv.GetEquippedItem(w.slotType);
            if (equipped != null)
            {
                Color tc = ItemData.GetTierColor(equipped.tier);
                w.border.color = tc;
                w.icon.color = tc;
                w.nameText.text = equipped.displayName;
                w.nameText.color = tc;
                w.statText.text = BuildItemStatSummary(equipped, includeDesc: false).Replace("\n", " ");
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

        // Refresh Total Derived Stats & Stat Allocation Buttons
        if (combat != null)
        {
            float spd = capsule != null ? capsule.TotalMoveSpeed : 6f;
            _totalStatsText.text =
                $"ATK: {combat.TotalAttackDamage}     |     DEF: {combat.TotalDefense}     |     CRIT: {combat.TotalCritChance * 100f:0}%\n" +
                $"Max HP: {combat.MaxHealth}     |     Max STA: {combat.MaxStamina:0}     |     SPD: {spd:0.00}";

            int pts = combat.NetworkUnspentStatPoints;
            _statPointsText.text = $"Available Points: {pts}";
            _statPointsText.color = pts > 0 ? new Color(1f, 0.85f, 0.22f) : new Color(0.65f, 0.70f, 0.78f);

            int[] statVals = { combat.StatPointsSTR, combat.StatPointsVIT, combat.StatPointsEND, combat.StatPointsAGI };
            for (int i = 0; i < _statWidgets.Length; i++)
            {
                _statWidgets[i].valueText.text = statVals[i].ToString();
                _statWidgets[i].plusBtn.interactable = pts > 0;
            }
        }

        // Refresh 24-Slot Inventory Grid
        var slots = _inv.Slots;
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
            bool isEq = _inv.IsEquipped(s.item);

            _slotBorders[i].color = selected ? Color.white : tierColor;
            _slotBgs[i].color = selected
                ? Color.Lerp(new Color(0.14f, 0.16f, 0.22f), tierColor, 0.32f)
                : Color.Lerp(new Color(0.09f, 0.10f, 0.14f), tierColor, 0.14f);

            _slotIcons[i].gameObject.SetActive(true);
            _slotIcons[i].sprite = HudIcons.GetForItem(s.item);
            _slotIcons[i].color = tierColor;

            _slotLabels[i].text = s.item.displayName;
            _slotLabels[i].color = Color.Lerp(Color.white, tierColor, 0.45f);
            _slotQtyLabels[i].text = isEq ? "[E]" : (s.quantity > 1 ? $"x{s.quantity}" : "");
            _slotQtyLabels[i].color = isEq ? new Color(0.35f, 0.98f, 0.62f) : new Color(1f, 0.90f, 0.45f);
        }

        // Refresh Selected Inspector Box
        InventorySlot sel = (_selected >= 0 && _selected < slots.Count) ? slots[_selected] : null;
        if (sel == null || sel.IsEmpty)
        {
            _detailRarityBar.color = new Color(0.35f, 0.38f, 0.45f, 1f);
            _detailTitleText.text = "Select an item to inspect.";
            _detailTitleText.color = Color.white;
            _detailBodyText.text = "Tip: Defeat camps for Treasure Chests or allocate Stat Points on the left!";
            _useBtn.interactable = _equipBtn.interactable = _dropBtn.interactable = false;
            _equipBtnText.text = "Equip";
            return;
        }

        var item = sel.item;
        Color selTierColor = ItemData.GetTierColor(item.tier);
        _detailRarityBar.color = selTierColor;
        _detailTitleText.color = selTierColor;
        _detailTitleText.text = $"{item.displayName}   [{ItemData.GetTierDisplayName(item.tier)} {FormatItemCategory(item)}]";
        _detailBodyText.text = $"Qty: {sel.quantity}/{item.MaxStackSize}   " + BuildItemStatSummary(item, includeDesc: true);

        _useBtn.interactable = item.IsUsable;
        _equipBtn.interactable = item.IsEquippable;
        _equipBtnText.text = _inv.IsEquipped(item) ? "Unequip" : "Equip";
        _dropBtn.interactable = item.droppable;
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

    private static string BuildItemStatSummary(ItemData item, bool includeDesc)
    {
        var sb = new System.Text.StringBuilder();
        if (item.damageBonus != 0) sb.Append($"+{item.damageBonus} ATK  ");
        if (item.maxHealthBonus != 0) sb.Append($"+{item.maxHealthBonus} Max HP  ");
        if (item.maxStaminaBonus != 0) sb.Append($"+{item.maxStaminaBonus} Max STA  ");
        if (item.defenseBonus != 0) sb.Append($"+{item.defenseBonus} DEF  ");
        if (item.moveSpeedBonus > 0f) sb.Append($"+{item.moveSpeedBonus:0.0} SPD  ");
        if (item.critChanceBonus > 0f) sb.Append($"+{item.critChanceBonus * 100f:0}% CRIT  ");
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

    private void MakeTabButton(Transform parent, string name, string label, float x, float w, bool active, UnityEngine.Events.UnityAction onClick)
    {
        Color bg = active ? new Color(0.55f, 0.44f, 0.18f, 1f) : new Color(0.14f, 0.17f, 0.24f, 1f);
        var img = MakeImage(parent, name, bg);
        Place(img.rectTransform, x, 8f, w, 30f);
        var btn = img.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);
        MakeLabel(img.transform, "Text", label, 13, FontStyle.Bold, TextAnchor.MiddleCenter,
            active ? new Color(1f, 0.94f, 0.72f) : Color.white, 4f, 2f, w - 8f, 26f);
    }

    private Button MakeActionButton(Transform parent, string name, string label, float x, float y, float w, float h, UnityEngine.Events.UnityAction onClick, out Text labelOut)
    {
        var img = MakeImage(parent, name, new Color(0.18f, 0.24f, 0.36f, 1f));
        Place(img.rectTransform, x, y, w, h);
        var btn = img.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);
        labelOut = MakeLabel(img.transform, "Text", label, 13, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white, 0f, 0f, w, h);
        return btn;
    }

    private class SlotHoverRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public InventoryUI owner;
        public int slotIndex;
        public void OnPointerEnter(PointerEventData eventData) => owner?.OnSlotHoverEnter(slotIndex);
        public void OnPointerExit(PointerEventData eventData) => owner?.OnSlotHoverExit(slotIndex);
    }
}
