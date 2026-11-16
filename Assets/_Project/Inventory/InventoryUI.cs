using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Runtime-built inventory overlay (uGUI, no prefab needed). Toggle with I or Tab, close with Escape.
/// While open it raises GameplayInput's blocker so attacking, camera look and lock-on pause and the cursor is freed.
/// Created by InventoryManager for the locally controlled player only.
/// </summary>
public class InventoryUI : MonoBehaviour
{
    private const int Columns = 6;
    private const float SlotSize = 76f;
    private const float SlotGap = 6f;

    private InventoryManager _inv;
    private GameObject _panel;
    private Text _detail;
    private Text _expText;
    private Button _useBtn, _equipBtn, _dropBtn;
    private Button[] _slotButtons;
    private Text[] _slotLabels;
    private Image[] _slotImages;
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
        _inv = inv;
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (_font == null) Debug.LogWarning("[InventoryUI] Built-in font not found; text may not render.");

        EnsureEventSystem();

        var canvasGo = new GameObject("Canvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        // main panel (centred)
        var panelImg = MakeImage(canvasGo.transform, "Panel", new Color(0.05f, 0.06f, 0.09f, 0.92f));
        _panel = panelImg.gameObject;
        var pr = panelImg.rectTransform;
        pr.anchorMin = pr.anchorMax = pr.pivot = new Vector2(0.5f, 0.5f);
        pr.sizeDelta = new Vector2(790f, 540f);
        pr.anchoredPosition = Vector2.zero;

        MakeLabel(_panel.transform, "Title", "INVENTORY   (I / Tab to close)", 22, TextAnchor.UpperLeft, 20f, 14f, 500f, 34f);
        _expText = MakeLabel(_panel.transform, "Exp", "", 18, TextAnchor.UpperRight, 520f, 16f, 250f, 30f);

        int count = _inv.Capacity;
        _slotButtons = new Button[count];
        _slotLabels = new Text[count];
        _slotImages = new Image[count];
        for (int i = 0; i < count; i++)
        {
            int col = i % Columns, row = i / Columns;
            float x = 20f + col * (SlotSize + SlotGap);
            float y = 60f + row * (SlotSize + SlotGap);

            var img = MakeImage(_panel.transform, "Slot" + i, new Color(0.16f, 0.18f, 0.24f, 1f));
            Place(img.rectTransform, x, y, SlotSize, SlotSize);
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            int captured = i;
            btn.onClick.AddListener(() => Select(captured));

            var label = MakeLabel(img.transform, "Label", "", 14, TextAnchor.MiddleCenter, 2f, 2f, SlotSize - 4f, SlotSize - 4f);
            _slotButtons[i] = btn;
            _slotImages[i] = img;
            _slotLabels[i] = label;
        }

        // detail column
        var detailBg = MakeImage(_panel.transform, "DetailBg", new Color(0.1f, 0.11f, 0.15f, 1f));
        Place(detailBg.rectTransform, 530f, 60f, 240f, 380f);
        _detail = MakeLabel(detailBg.transform, "Detail", "Select an item.", 16, TextAnchor.UpperLeft, 10f, 10f, 220f, 360f);

        _useBtn = MakeButton(_panel.transform, "Use", 530f, 452f, 76f, 36f, () => { _inv.UseItem(_selected); });
        _equipBtn = MakeButton(_panel.transform, "Equip", 612f, 452f, 76f, 36f, () => { _inv.EquipItem(_selected); });
        _dropBtn = MakeButton(_panel.transform, "Drop", 694f, 452f, 76f, 36f, () => { _inv.DropItem(_selected); });

        MakeLabel(_panel.transform, "Hint", "Click a slot to inspect it.   E = pick up   Middle Mouse / Q = lock on   M = map", 14, TextAnchor.LowerLeft, 20f, 500f, 700f, 28f);

        _panel.SetActive(false);
        _inv.Changed += Refresh;
        Debug.Log("[InventoryUI] Built overlay (" + count + " slots).");
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null || FindFirstObjectByType<EventSystem>() != null) return;

        var es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        var module = es.AddComponent<InputSystemUIInputModule>();
        module.AssignDefaultActions();
        Debug.Log("[InventoryUI] No EventSystem found - created one with InputSystemUIInputModule.");
    }

    // ------------------------------------------------------------------ input / state

    private void Update()
    {
        if (_inv == null) return;
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.iKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame) SetOpen(!_open);
        else if (_open && kb.escapeKey.wasPressedThisFrame) SetOpen(false);
    }

    private void SetOpen(bool open)
    {
        if (open == _open) return;

        if (open && GameplayInput.IsBlocked)
        {
            Debug.LogWarning("[InventoryUI] Another UI is already open - ignoring open request.");
            return;
        }

        _open = open;
        _panel.SetActive(open);
        if (open) { GameplayInput.PushBlocker("InventoryUI"); Refresh(); }
        else GameplayInput.PopBlocker("InventoryUI");
        Debug.Log("[InventoryUI] " + (open ? "Opened." : "Closed."));
    }

    private void OnDestroy()
    {
        if (_inv != null) _inv.Changed -= Refresh;
        if (_open) GameplayInput.PopBlocker("InventoryUI");
    }

    private void Select(int index)
    {
        _selected = index;
        Refresh();
    }

    // ------------------------------------------------------------------ view

    private void Refresh()
    {
        if (!_open || _inv == null) return;

        var slots = _inv.Slots;
        for (int i = 0; i < _slotButtons.Length && i < slots.Count; i++)
        {
            var s = slots[i];
            bool selected = i == _selected;
            if (s.IsEmpty)
            {
                _slotLabels[i].text = "";
                _slotImages[i].color = selected ? new Color(0.35f, 0.38f, 0.5f) : new Color(0.16f, 0.18f, 0.24f);
                continue;
            }

            string equipped = _inv.IsEquipped(s.item) ? "[E] " : "";
            string qty = s.quantity > 1 ? "\nx" + s.quantity : "";
            _slotLabels[i].text = equipped + s.item.displayName + qty;

            Color c = TypeColor(s.item.type);
            _slotImages[i].color = selected ? Color.Lerp(c, Color.white, 0.35f) : c;
        }

        _expText.text = "EXP " + _inv.Experience;

        InventorySlot sel = (_selected >= 0 && _selected < slots.Count) ? slots[_selected] : null;
        if (sel == null || sel.IsEmpty)
        {
            _detail.text = "Select an item.";
            _useBtn.interactable = _equipBtn.interactable = _dropBtn.interactable = false;
            return;
        }

        var item = sel.item;
        _detail.text = item.displayName + "\n<" + item.type + ">\n\n" + item.description + "\n\n" +
                       "Qty: " + sel.quantity + "/" + item.MaxStackSize +
                       (item.healAmount > 0 ? "\nHeals " + item.healAmount : "") +
                       (item.damageBonus != 0 ? "\nDamage +" + item.damageBonus : "");
        _useBtn.interactable = item.IsUsable;
        _equipBtn.interactable = item.IsEquippable;
        _dropBtn.interactable = item.droppable;
    }

    private static Color TypeColor(ItemType t)
    {
        switch (t)
        {
            case ItemType.Consumable: return new Color(0.2f, 0.5f, 0.3f);
            case ItemType.Weapon: return new Color(0.65f, 0.38f, 0.15f);
            case ItemType.Equipment: return new Color(0.2f, 0.35f, 0.65f);
            case ItemType.QuestItem: return new Color(0.65f, 0.55f, 0.15f);
            default: return new Color(0.4f, 0.4f, 0.45f);
        }
    }

    // ------------------------------------------------------------------ tiny uGUI helpers

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

    private Text MakeLabel(Transform parent, string name, string text, int size, TextAnchor anchor, float x, float y, float w, float h)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.transform.SetParent(parent, false);
        Place((RectTransform)go.transform, x, y, w, h);
        var t = go.GetComponent<Text>();
        t.font = _font;
        t.fontSize = size;
        t.alignment = anchor;
        t.color = Color.white;
        t.text = text;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        return t;
    }

    private Button MakeButton(Transform parent, string label, float x, float y, float w, float h, UnityEngine.Events.UnityAction onClick)
    {
        var img = MakeImage(parent, label + "Button", new Color(0.25f, 0.3f, 0.45f, 1f));
        Place(img.rectTransform, x, y, w, h);
        var btn = img.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);
        MakeLabel(img.transform, "Text", label, 16, TextAnchor.MiddleCenter, 0f, 0f, w, h);
        return btn;
    }
}
