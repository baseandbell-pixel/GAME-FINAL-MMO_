using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// แถบปุ่มลัด 7 ช่องด้านล่างกลางหน้าจอ (Bottom-Center 7-Slot Hotbar: [1] GreatSword + [2-7] Quick-Slots):
///   - ช่อง [1] (Weapon Slot): ล็อกถาวรสำหรับชัก/เก็บดาบ GreatSword พร้อมไอคอนดาบถาวรและไฟเรืองแสงบอกสถานะ Drawn/Sheathed
///   - ช่อง [2-7] (Item Quick-Slots): ผูกตรงกับ Index 0..5 ของ InventoryManager (เข้ากันได้กับไฟล์เซฟเดิม 100%)
///   - สไตล์ Dark Obsidian + Brushed Gold + Jade Accent รองรับ Drag &amp; Drop และคลิกขวาเพื่อล้างช่องไอเทม
/// </summary>
public class HotbarUI : MonoBehaviour
{
    public static HotbarUI Instance { get; private set; }

    private const float SlotSize = 68f;
    private const float SlotGap = 10f;
    private const float WeaponDividerGap = 10f;
    private const float BarPadding = 12f;
    // จำนวนช่องที่แสดงผลทั้งหมดบนแถบ Hotbar (ช่อง [1] ดาบใหญ่ + 6 ช่องไอเทม [2..7] = 7 ช่อง)
    public const int TotalDisplaySlots = InventoryManager.HotbarCapacity + 1;

    // คุณสมบัติตรวจสอบสถานะช่อง [1] (Weapon Slot) สำหรับ HUD และการทดสอบอัตโนมัติ
    public int TotalDisplayedSlotCount => TotalDisplaySlots;
    public bool IsWeaponSlotHighlighted { get; private set; }
    public bool HasWeaponSlotSwordIcon => _weaponSlotIcon != null && _weaponSlotIcon.gameObject.activeSelf && _weaponSlotIcon.sprite != null;
    public string WeaponSlotKeyLabel => _weaponSlotKeyLabel != null ? _weaponSlotKeyLabel.text : string.Empty;
    public string WeaponSlotStateLabel => _weaponSlotStateLabel != null ? _weaponSlotStateLabel.text : string.Empty;

    private InventoryManager _inv;
    private Font _font;
    private RectTransform _barRect;

    // องค์ประกอบ UI ของช่อง [1] เฉพาะสำหรับดาบ GreatSword (ล็อกถาวร ห้ามไอเทมอื่นทับ)
    private Image _weaponSlotBorder;
    private Image _weaponSlotBg;
    private Image _weaponSlotIcon;
    private Image _weaponSlotFlash;
    private Text _weaponSlotKeyLabel;
    private Text _weaponSlotStateLabel;
    private float _weaponFlashTimer;
    private bool _lastObservedWeaponDrawn;

    private readonly Image[] _slotBorders = new Image[InventoryManager.HotbarCapacity];
    private readonly Image[] _slotBgs = new Image[InventoryManager.HotbarCapacity];
    private readonly Image[] _slotIcons = new Image[InventoryManager.HotbarCapacity];
    private readonly Image[] _slotFlashes = new Image[InventoryManager.HotbarCapacity];
    private readonly Text[] _slotKeyLabels = new Text[InventoryManager.HotbarCapacity];
    private readonly Text[] _slotQtyLabels = new Text[InventoryManager.HotbarCapacity];
    private readonly float[] _flashTimers = new float[InventoryManager.HotbarCapacity];

    // สถานะการลากสลับช่องภายใน Hotbar
    private int _draggingHotbarIndex = -1;
    private bool _droppedOnValidHotbar;
    private GameObject _dragGhostObj;
    private RectTransform _dragGhostRect;
    private Image _dragGhostBorder;
    private Image _dragGhostIcon;

    /// <summary>
    /// สร้างและผูก HotbarUI เข้ากับ InventoryManager ของผู้เล่น Local
    /// </summary>
    public static HotbarUI Create(InventoryManager inv)
    {
        var go = new GameObject("HotbarUI");
        var ui = go.AddComponent<HotbarUI>();
        ui.Init(inv);
        return ui;
    }

    private void Init(InventoryManager inv)
    {
        Instance = this;
        _inv = inv;
        _font = UITheme.GetFont();

        var canvasGo = new GameObject("HotbarCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 56;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        float totalW = BarPadding * 2f + TotalDisplaySlots * SlotSize + (TotalDisplaySlots - 1) * SlotGap + WeaponDividerGap;
        float totalH = BarPadding * 2f + SlotSize;

        // กรอบหลักแบบ 9-Sliced Dark Obsidian + Brushed Gold ปักหมุดด้านล่างกลางหน้าจอ
        var frameGo = new GameObject("HotbarFrame", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        frameGo.transform.SetParent(canvasGo.transform, false);
        var frameImg = frameGo.GetComponent<Image>();
        UITheme.ApplySlicedStyle(frameImg, UITheme.BrushedGoldDark);
        UITheme.AddShadow(frameGo, 2f);

        _barRect = (RectTransform)frameGo.transform;
        _barRect.anchorMin = new Vector2(0.5f, 0f);
        _barRect.anchorMax = new Vector2(0.5f, 0f);
        _barRect.pivot = new Vector2(0.5f, 0f);
        _barRect.anchoredPosition = new Vector2(0f, 20f);
        _barRect.sizeDelta = new Vector2(totalW, totalH);

        var innerGo = new GameObject("HotbarInner", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        innerGo.transform.SetParent(_barRect, false);
        var innerImg = innerGo.GetComponent<Image>();
        UITheme.ApplySlicedStyle(innerImg, UITheme.ObsidianDeep);
        var innerRt = (RectTransform)innerGo.transform;
        innerRt.anchorMin = Vector2.zero;
        innerRt.anchorMax = Vector2.one;
        innerRt.offsetMin = new Vector2(2f, 2f);
        innerRt.offsetMax = new Vector2(-2f, -2f);

        // ============================================================
        // ช่อง [1] เฉพาะสำหรับชัก/เก็บดาบ GreatSword (ล็อกถาวร ไม่ให้ลากไอเทมมาทับ)
        // ============================================================
        float weaponSlotX = BarPadding - 2f;
        BuildLockedWeaponSlot(innerRt, weaponSlotX);

        // เส้นคั่นแนวตั้งสีทองบางๆ ระหว่างช่องอาวุธ [1] กับช่องไอเทมลัด [2-7]
        var divGo = new GameObject("WeaponDivider", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        divGo.transform.SetParent(innerRt, false);
        var divImg = divGo.GetComponent<Image>();
        divImg.color = new Color(UITheme.BrushedGold.r, UITheme.BrushedGold.g, UITheme.BrushedGold.b, 0.42f);
        divImg.raycastTarget = false;
        var drt = (RectTransform)divGo.transform;
        drt.anchorMin = drt.anchorMax = new Vector2(0f, 0.5f);
        drt.pivot = new Vector2(0.5f, 0.5f);
        drt.anchoredPosition = new Vector2(weaponSlotX + SlotSize + (SlotGap + WeaponDividerGap) * 0.5f, 0f);
        drt.sizeDelta = new Vector2(2f, SlotSize - 14f);

        // ============================================================
        // ช่อง [2-7] สำหรับไอเทมลัด (ลากจากกระเป๋า/สลับช่องได้ ผูกกับ Index 0..5 ของ InventoryManager)
        // ============================================================
        float itemsStartX = weaponSlotX + SlotSize + SlotGap + WeaponDividerGap;
        for (int i = 0; i < InventoryManager.HotbarCapacity; i++)
        {
            float x = itemsStartX + i * (SlotSize + SlotGap);

            var borderGo = new GameObject($"HotbarSlot_{i + 2}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            borderGo.transform.SetParent(innerRt, false);
            var borderImg = borderGo.GetComponent<Image>();
            UITheme.ApplySlicedStyle(borderImg, UITheme.BrushedGoldDark);
            var brt = (RectTransform)borderGo.transform;
            brt.anchorMin = brt.anchorMax = new Vector2(0f, 0.5f);
            brt.pivot = new Vector2(0f, 0.5f);
            brt.anchoredPosition = new Vector2(x, 0f);
            brt.sizeDelta = new Vector2(SlotSize, SlotSize);

            var bgGo = new GameObject("SlotBg", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            bgGo.transform.SetParent(brt, false);
            var bgImg = bgGo.GetComponent<Image>();
            UITheme.ApplySlicedStyle(bgImg, UITheme.ObsidianSlotEmpty);
            var bgrt = (RectTransform)bgGo.transform;
            bgrt.anchorMin = Vector2.zero;
            bgrt.anchorMax = Vector2.one;
            bgrt.offsetMin = new Vector2(2.5f, 2.5f);
            bgrt.offsetMax = new Vector2(-2.5f, -2.5f);

            // ตัวรับ Drag & Drop และคลิกขวาบนช่อง Hotbar
            var relay = bgGo.AddComponent<HotbarSlotRelay>();
            relay.owner = this;
            relay.hotbarIndex = i;

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            iconGo.transform.SetParent(bgrt, false);
            var iconImg = iconGo.GetComponent<Image>();
            iconImg.raycastTarget = false;
            iconImg.preserveAspect = true;
            var irt = (RectTransform)iconGo.transform;
            irt.anchorMin = Vector2.zero;
            irt.anchorMax = Vector2.one;
            irt.offsetMin = new Vector2(10f, 10f);
            irt.offsetMax = new Vector2(-10f, -10f);
            iconGo.SetActive(false);

            // แสงแฟลชไฮไลต์เมื่อกดใช้ปุ่มลัด
            var flashGo = new GameObject("Flash", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            flashGo.transform.SetParent(bgrt, false);
            var flashImg = flashGo.GetComponent<Image>();
            flashImg.raycastTarget = false;
            flashImg.color = new Color(UITheme.JadeAccent.r, UITheme.JadeAccent.g, UITheme.JadeAccent.b, 0f);
            var frt = (RectTransform)flashGo.transform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.offsetMin = Vector2.zero;
            frt.offsetMax = Vector2.zero;

            // ตัวเลขปุ่มลัด 2..7 มุมซ้ายบน (เลื่อนจาก 1..6 เพื่อกันปุ่ม [1] ไว้สำหรับชัก/เก็บดาบโดยเฉพาะ)
            var keyLbl = MakeLabel(bgrt, "Key", (i + 2).ToString(), 12, FontStyle.Bold, TextAnchor.UpperLeft,
                UITheme.BrushedGoldBright, new Vector2(4f, -3f), new Vector2(22f, 20f), new Vector2(0f, 1f));
            UITheme.AddShadow(keyLbl.gameObject, 1f);

            // จำนวนคงเหลือมุมขวาล่าง
            var qtyLbl = MakeLabel(bgrt, "Qty", "", 12, FontStyle.Bold, TextAnchor.LowerRight,
                new Color(1f, 0.94f, 0.56f), new Vector2(-4f, 3f), new Vector2(SlotSize - 12f, 20f), new Vector2(1f, 0f));
            UITheme.AddShadow(qtyLbl.gameObject, 1f);

            _slotBorders[i] = borderImg;
            _slotBgs[i] = bgImg;
            _slotIcons[i] = iconImg;
            _slotFlashes[i] = flashImg;
            _slotKeyLabels[i] = keyLbl;
            _slotQtyLabels[i] = qtyLbl;
        }

        // สร้างไอคอน Ghost สำหรับแสดงระหว่างลากย้ายช่อง Hotbar
        var ghostGo = new GameObject("HotbarDragGhost", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        ghostGo.transform.SetParent(canvasGo.transform, false);
        _dragGhostBorder = ghostGo.GetComponent<Image>();
        _dragGhostBorder.raycastTarget = false;
        _dragGhostObj = ghostGo;
        _dragGhostRect = (RectTransform)ghostGo.transform;
        _dragGhostRect.sizeDelta = new Vector2(56f, 56f);

        var ghostInner = new GameObject("Inner", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        ghostInner.transform.SetParent(_dragGhostRect, false);
        var giImg = ghostInner.GetComponent<Image>();
        giImg.color = UITheme.ObsidianPanel;
        giImg.raycastTarget = false;
        var giRt = (RectTransform)ghostInner.transform;
        giRt.anchorMin = Vector2.zero;
        giRt.anchorMax = Vector2.one;
        giRt.offsetMin = new Vector2(2f, 2f);
        giRt.offsetMax = new Vector2(-2f, -2f);

        var ghostIconGo = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        ghostIconGo.transform.SetParent(giRt, false);
        _dragGhostIcon = ghostIconGo.GetComponent<Image>();
        _dragGhostIcon.raycastTarget = false;
        _dragGhostIcon.preserveAspect = true;
        var gIconRt = (RectTransform)ghostIconGo.transform;
        gIconRt.anchorMin = Vector2.zero;
        gIconRt.anchorMax = Vector2.one;
        gIconRt.offsetMin = new Vector2(6f, 6f);
        gIconRt.offsetMax = new Vector2(-6f, -6f);
        _dragGhostObj.SetActive(false);

        if (_inv != null) _inv.Changed += Refresh;
        Refresh();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_inv != null) _inv.Changed -= Refresh;
    }

    /// <summary>
    /// สร้างช่อง [1] เฉพาะสำหรับดาบ GreatSword (ล็อกไม่รับ Drag &amp; Drop และแสดงไอคอนดาบถาวร)
    /// </summary>
    private void BuildLockedWeaponSlot(RectTransform parent, float x)
    {
        var borderGo = new GameObject("HotbarSlot_1_GreatSword", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        borderGo.transform.SetParent(parent, false);
        _weaponSlotBorder = borderGo.GetComponent<Image>();
        UITheme.ApplySlicedStyle(_weaponSlotBorder, UITheme.BrushedGoldDark);
        var brt = (RectTransform)borderGo.transform;
        brt.anchorMin = brt.anchorMax = new Vector2(0f, 0.5f);
        brt.pivot = new Vector2(0f, 0.5f);
        brt.anchoredPosition = new Vector2(x, 0f);
        brt.sizeDelta = new Vector2(SlotSize, SlotSize);

        var bgGo = new GameObject("SlotBg", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        bgGo.transform.SetParent(brt, false);
        _weaponSlotBg = bgGo.GetComponent<Image>();
        UITheme.ApplySlicedStyle(_weaponSlotBg, UITheme.ObsidianSlotEmpty);
        var bgrt = (RectTransform)bgGo.transform;
        bgrt.anchorMin = Vector2.zero;
        bgrt.anchorMax = Vector2.one;
        bgrt.offsetMin = new Vector2(2.5f, 2.5f);
        bgrt.offsetMax = new Vector2(-2.5f, -2.5f);

        // คลิกซ้ายที่ช่อง [1] เพื่อสลับชัก/เก็บดาบได้เช่นเดียวกับการกดปุ่ม 1 (แต่ไม่รับ Drop ทับ)
        var btn = bgGo.AddComponent<Button>();
        btn.targetGraphic = _weaponSlotBg;
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(() => PlayerCombat.Local?.TryToggleWeaponDraw());

        var iconGo = new GameObject("SwordIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        iconGo.transform.SetParent(bgrt, false);
        _weaponSlotIcon = iconGo.GetComponent<Image>();
        _weaponSlotIcon.sprite = HudIcons.GetSprite("sword");
        _weaponSlotIcon.raycastTarget = false;
        _weaponSlotIcon.preserveAspect = true;
        var irt = (RectTransform)iconGo.transform;
        irt.anchorMin = Vector2.zero;
        irt.anchorMax = Vector2.one;
        irt.offsetMin = new Vector2(10f, 10f);
        irt.offsetMax = new Vector2(-10f, -10f);

        var flashGo = new GameObject("Flash", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        flashGo.transform.SetParent(bgrt, false);
        _weaponSlotFlash = flashGo.GetComponent<Image>();
        _weaponSlotFlash.raycastTarget = false;
        _weaponSlotFlash.color = new Color(UITheme.BrushedGoldBright.r, UITheme.BrushedGoldBright.g, UITheme.BrushedGoldBright.b, 0f);
        var frt = (RectTransform)flashGo.transform;
        frt.anchorMin = Vector2.zero;
        frt.anchorMax = Vector2.one;
        frt.offsetMin = Vector2.zero;
        frt.offsetMax = Vector2.zero;

        _weaponSlotKeyLabel = MakeLabel(bgrt, "Key", "1", 12, FontStyle.Bold, TextAnchor.UpperLeft,
            UITheme.BrushedGoldBright, new Vector2(4f, -3f), new Vector2(22f, 20f), new Vector2(0f, 1f));
        UITheme.AddShadow(_weaponSlotKeyLabel.gameObject, 1f);

        _weaponSlotStateLabel = MakeLabel(bgrt, "StateTag", "OFF", 10, FontStyle.Bold, TextAnchor.LowerRight,
            UITheme.TextMuted, new Vector2(-4f, 3f), new Vector2(SlotSize - 10f, 16f), new Vector2(1f, 0f));
        UITheme.AddShadow(_weaponSlotStateLabel.gameObject, 1f);
    }

    /// <summary>
    /// อัปเดตแสงสีและสถานะของช่อง [1] GreatSword ให้สะท้อนสถานะชักดาบ (Drawn) vs เก็บดาบ (Sheathed) แบบเรียลไทม์
    /// </summary>
    private void UpdateWeaponSlotVisual()
    {
        if (_weaponSlotBorder == null || _weaponSlotBg == null || _weaponSlotIcon == null) return;

        var local = PlayerCombat.Local;
        bool inSafeZone = local != null && local.IsSpawned && (local.IsInSafeZone || SafeZone.IsPointInAnySafeZone(local.transform.position));
        bool drawn = local != null && local.IsSpawned && !local.IsDead && !inSafeZone && local.IsWeaponDrawn;

        if (drawn != _lastObservedWeaponDrawn)
        {
            _lastObservedWeaponDrawn = drawn;
            _weaponFlashTimer = 1f;
        }

        if (_weaponFlashTimer > 0f)
        {
            _weaponFlashTimer = Mathf.Max(0f, _weaponFlashTimer - Time.unscaledDeltaTime * 3.2f);
            if (_weaponSlotFlash != null)
            {
                Color fc = drawn ? UITheme.BrushedGoldBright : UITheme.TextMuted;
                _weaponSlotFlash.color = new Color(fc.r, fc.g, fc.b, _weaponFlashTimer * 0.45f);
            }
        }

        IsWeaponSlotHighlighted = drawn;

        // ใช้สี Tier ของดาบที่สวมใส่อยู่ถ้ามี (หรือสีทองสว่างเมื่อชักดาบ)
        ItemData equippedSword = _inv != null ? _inv.GetEquippedItem(EquipmentSlotType.Sword) : null;
        Sprite swordSprite = equippedSword != null ? HudIcons.GetForItem(equippedSword) : HudIcons.GetSprite("sword");
        if (_weaponSlotIcon.sprite != swordSprite) _weaponSlotIcon.sprite = swordSprite;

        if (drawn)
        {
            float pulse = 0.5f + Mathf.Sin(Time.unscaledTime * 5.0f) * 0.5f;
            _weaponSlotBorder.color = Color.Lerp(UITheme.BrushedGoldBright, UITheme.JadeAccent, pulse * 0.45f);
            _weaponSlotBg.color = Color.Lerp(new Color(0.18f, 0.14f, 0.06f, 1f), new Color(0.10f, 0.22f, 0.18f, 1f), pulse * 0.4f);
            _weaponSlotIcon.color = equippedSword != null
                ? Color.Lerp(ItemData.GetTierColor(equippedSword.tier), Color.white, 0.35f)
                : UITheme.BrushedGoldBright;
            if (_weaponSlotKeyLabel != null) _weaponSlotKeyLabel.color = UITheme.BrushedGoldBright;
            if (_weaponSlotStateLabel != null)
            {
                _weaponSlotStateLabel.text = "DRAWN";
                _weaponSlotStateLabel.color = UITheme.JadeAccent;
            }
        }
        else
        {
            _weaponSlotBorder.color = new Color(0.32f, 0.28f, 0.20f, 0.85f);
            _weaponSlotBg.color = UITheme.ObsidianSlotEmpty;
            _weaponSlotIcon.color = new Color(0.55f, 0.58f, 0.64f, 0.58f);
            if (_weaponSlotKeyLabel != null) _weaponSlotKeyLabel.color = UITheme.TextMuted;
            if (_weaponSlotStateLabel != null)
            {
                _weaponSlotStateLabel.text = inSafeZone ? "SAFE" : "SHEATH";
                _weaponSlotStateLabel.color = inSafeZone ? new Color(0.32f, 0.88f, 0.64f, 0.9f) : UITheme.TextMuted;
            }
        }
    }

    private void Update()
    {
        UpdateWeaponSlotVisual();

        // อัปเดตเอฟเฟกต์แสงแฟลชเมื่อกดใช้งานช่อง Hotbar [2-7]
        for (int i = 0; i < InventoryManager.HotbarCapacity; i++)
        {
            if (_flashTimers[i] > 0f)
            {
                _flashTimers[i] = Mathf.Max(0f, _flashTimers[i] - Time.unscaledDeltaTime * 3.5f);
                if (_slotFlashes[i] != null)
                {
                    _slotFlashes[i].color = new Color(UITheme.JadeAccent.r, UITheme.JadeAccent.g, UITheme.JadeAccent.b, _flashTimers[i] * 0.45f);
                }
            }
        }

        HandleHotbarKeybinds();
    }

    /// <summary>
    /// ตรวจจับปุ่มตัวเลข 2-7 เพื่อใช้งานไอเทมใน Hotbar ทันที (ปุ่ม 1 สงวนไว้สำหรับชัก/เก็บอาวุธของ PlayerCombat)
    /// บล็อกอัตโนมัติเมื่อเปิดหน้าต่าง Modal หรือกำลังพิมพ์ข้อความ
    /// </summary>
    private void HandleHotbarKeybinds()
    {
        if (_inv == null || !Application.isFocused) return;
        if (GameplayInput.IsBlocked || IsAnyTextFieldFocused()) return;
        if (MapManager.Instance != null && MapManager.Instance.IsWorldMapOpen) return;
        if (InventoryUI.Instance != null && InventoryUI.Instance.IsOpen) return;

        var kb = Keyboard.current;
        if (kb == null) return;

        for (int i = 0; i < InventoryManager.HotbarCapacity; i++)
        {
            bool pressed = i switch
            {
                0 => kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame,
                1 => kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame,
                2 => kb.digit4Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame,
                3 => kb.digit5Key.wasPressedThisFrame || kb.numpad5Key.wasPressedThisFrame,
                4 => kb.digit6Key.wasPressedThisFrame || kb.numpad6Key.wasPressedThisFrame,
                5 => kb.digit7Key.wasPressedThisFrame || kb.numpad7Key.wasPressedThisFrame,
                _ => false
            };

            if (pressed)
            {
                TriggerHotbarIndex(i);
            }
        }
    }

    /// <summary>
    /// คืนข้อความตัวเลขปุ่มลัดที่แสดงบนช่อง Hotbar ที่ระบุ (เช่น index 0 -> "2", index 5 -> "7")
    /// </summary>
    public string GetSlotKeyLabel(int hotbarIndex)
    {
        if (_slotKeyLabels == null || hotbarIndex < 0 || hotbarIndex >= _slotKeyLabels.Length || _slotKeyLabels[hotbarIndex] == null)
            return string.Empty;
        return _slotKeyLabels[hotbarIndex].text;
    }

    /// <summary>
    /// สั่งใช้งานช่อง Hotbar ตามลำดับ (0..5) พร้อมเล่นแอนิเมชันแสงไฮไลต์
    /// </summary>
    public bool TriggerHotbarIndex(int hotbarIndex)
    {
        if (_inv == null || hotbarIndex < 0 || hotbarIndex >= InventoryManager.HotbarCapacity) return false;
        bool used = _inv.UseHotbarSlot(hotbarIndex);
        if (used)
        {
            _flashTimers[hotbarIndex] = 1f;
            Refresh();
        }
        return used;
    }

    private static bool IsAnyTextFieldFocused()
    {
        if (EventSystem.current == null) return false;
        var selected = EventSystem.current.currentSelectedGameObject;
        if (selected == null) return false;

        var legacyInput = selected.GetComponent<InputField>();
        if (legacyInput != null && legacyInput.isFocused) return true;

        var tmpInput = selected.GetComponent<TMP_InputField>();
        if (tmpInput != null && tmpInput.isFocused) return true;

        return false;
    }

    /// <summary>
    /// อัปเดตการแสดงผลไอคอน จำนวน และสีระดับความหายากของ Hotbar ทั้ง 6 ช่อง
    /// </summary>
    public void Refresh()
    {
        if (_inv == null) return;
        _inv.ValidateHotbarBindings();

        for (int i = 0; i < InventoryManager.HotbarCapacity; i++)
        {
            var slot = _inv.GetHotbarBoundSlot(i);
            if (slot == null || slot.IsEmpty || slot.item == null)
            {
                _slotBorders[i].color = UITheme.BrushedGoldDark;
                _slotBgs[i].color = UITheme.ObsidianSlotEmpty;
                _slotIcons[i].gameObject.SetActive(false);
                _slotQtyLabels[i].text = "";
            }
            else
            {
                Color tierColor = ItemData.GetTierColor(slot.item.tier);
                _slotBorders[i].color = tierColor;
                _slotBgs[i].color = Color.Lerp(UITheme.ObsidianSlot, tierColor, 0.16f);
                _slotIcons[i].gameObject.SetActive(true);
                _slotIcons[i].sprite = HudIcons.GetForItem(slot.item);
                _slotIcons[i].color = tierColor;

                if (slot.enhancementLevel > 0)
                    _slotQtyLabels[i].text = $"+{slot.enhancementLevel}";
                else if (slot.quantity > 1)
                    _slotQtyLabels[i].text = $"x{slot.quantity}";
                else
                    _slotQtyLabels[i].text = "";
            }
        }
    }

    // ------------------------------------------------------------------ การจัดการ Drag & Drop และคลิกบนช่อง Hotbar

    internal void BeginHotbarDrag(int hotbarIndex, PointerEventData eventData)
    {
        if (_inv == null) return;
        var slot = _inv.GetHotbarBoundSlot(hotbarIndex);
        if (slot == null || slot.IsEmpty || slot.item == null) return;

        _draggingHotbarIndex = hotbarIndex;
        _droppedOnValidHotbar = false;

        Color tc = ItemData.GetTierColor(slot.item.tier);
        _dragGhostBorder.color = tc;
        _dragGhostIcon.sprite = HudIcons.GetForItem(slot.item);
        _dragGhostIcon.color = tc;
        _dragGhostObj.SetActive(true);
        _dragGhostRect.position = eventData.position;
    }

    internal void UpdateHotbarDrag(PointerEventData eventData)
    {
        if (_draggingHotbarIndex >= 0 && _dragGhostObj != null && _dragGhostObj.activeSelf)
            _dragGhostRect.position = eventData.position;
    }

    internal void EndHotbarDrag()
    {
        if (_draggingHotbarIndex >= 0 && !_droppedOnValidHotbar && _inv != null)
        {
            // ลากออกนอกแถบ Hotbar = ล้างปุ่มลัดออกโดยไม่ลบไอเทมในกระเป๋า
            _inv.ClearHotbarSlot(_draggingHotbarIndex);
        }

        _draggingHotbarIndex = -1;
        _droppedOnValidHotbar = false;
        if (_dragGhostObj != null) _dragGhostObj.SetActive(false);
    }

    internal void HandleDropOnHotbarSlot(int targetHotbarIndex)
    {
        if (_inv == null || targetHotbarIndex < 0 || targetHotbarIndex >= InventoryManager.HotbarCapacity) return;

        // 1. กรณีลากมาจากช่องภายใน InventoryUI
        if (InventoryUI.Instance != null && InventoryUI.Instance.ActiveDragInventorySlotIndex >= 0)
        {
            int invIdx = InventoryUI.Instance.ActiveDragInventorySlotIndex;
            _inv.AssignHotbarSlot(targetHotbarIndex, invIdx);
            _flashTimers[targetHotbarIndex] = 0.85f;
            Refresh();
            return;
        }

        // 2. กรณีลากสลับช่องภายใน Hotbar เอง
        if (_draggingHotbarIndex >= 0)
        {
            _droppedOnValidHotbar = true;
            if (_draggingHotbarIndex != targetHotbarIndex)
            {
                _inv.SwapHotbarSlots(_draggingHotbarIndex, targetHotbarIndex);
                _flashTimers[targetHotbarIndex] = 0.7f;
            }
        }
    }

    internal void HandleHotbarSlotClick(int hotbarIndex, PointerEventData eventData)
    {
        if (_inv == null) return;
        if (eventData.button == PointerEventData.InputButton.Right)
        {
            // คลิกขวาเพื่อล้างปุ่มลัดออกโดยไม่ลบไอเทม
            _inv.ClearHotbarSlot(hotbarIndex);
        }
    }

    private Text MakeLabel(Transform parent, string name, string text, int size, FontStyle style, TextAnchor align,
        Color color, Vector2 pos, Vector2 sizeDelta, Vector2 anchor)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = sizeDelta;
        var t = go.GetComponent<Text>();
        t.font = _font;
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = align;
        t.color = color;
        t.raycastTarget = false;
        return t;
    }
}

/// <summary>
/// ตัวส่งต่อเหตุการณ์ Drag &amp; Drop และคลิกสำหรับแต่ละช่องของ HotbarUI
/// </summary>
public class HotbarSlotRelay : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler, IPointerClickHandler
{
    public HotbarUI owner;
    public int hotbarIndex;

    public void OnBeginDrag(PointerEventData eventData) => owner?.BeginHotbarDrag(hotbarIndex, eventData);
    public void OnDrag(PointerEventData eventData) => owner?.UpdateHotbarDrag(eventData);
    public void OnEndDrag(PointerEventData eventData) => owner?.EndHotbarDrag();
    public void OnDrop(PointerEventData eventData) => owner?.HandleDropOnHotbarSlot(hotbarIndex);
    public void OnPointerClick(PointerEventData eventData) => owner?.HandleHotbarSlotClick(hotbarIndex, eventData);
}
