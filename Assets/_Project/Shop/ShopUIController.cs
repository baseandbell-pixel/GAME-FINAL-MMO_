using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

public class ShopUIController : MonoBehaviour
{
    public static ShopUIController Instance { get; private set; }

    [Header("Catalog (สร้าง Asset จาก Create > MMO Shop > Shop Catalog Data หรือปล่อยว่างใช้ค่าเริ่มต้น)")]
    [SerializeField] private ShopCatalogData catalogData;

    [Header("UI References")]
    [SerializeField] private GameObject shopPanelRoot;
    [SerializeField] private TMP_Text goldText;
    [SerializeField] private TMP_Text statusFeedbackText;
    [SerializeField] private Button closeButton;
    [SerializeField] private Transform buyContainer;
    [SerializeField] private Transform sellContainer;
    [SerializeField] private ShopItemRowUI itemRowPrefab;

    private bool _isOpen;
    private List<ShopCatalogEntry> _runtimeCatalog;
    private InventoryManager _subscribedInventory;

    public bool IsOpen => _isOpen;

    private void Awake()
    {
        Instance = this;
        _runtimeCatalog = (catalogData != null && catalogData.entries.Count > 0)
            ? catalogData.entries
            : ShopCatalogData.CreateDefaultEntries();
    }

    private void Start()
    {
        if (closeButton != null)
        {
            closeButton.onClick.AddListener(() => SetShopOpen(false));
        }

        if (shopPanelRoot != null)
        {
            shopPanelRoot.SetActive(false);
        }

        TrySubscribeInventoryEvents();
    }

    private void OnEnable()
    {
        TrySubscribeInventoryEvents();
    }

    private void OnDisable()
    {
        UnsubscribeInventoryEvents();

        // ป้องกัน Blocker ค้างหาก GameObject ถูกปิดขณะเปิดร้านอยู่
        if (_isOpen) SetShopOpen(false);
    }

    private void OnDestroy()
    {
        UnsubscribeInventoryEvents();

        if (_isOpen)
        {
            _isOpen = false;
            GameplayInput.PopBlocker("ShopUI");
        }
    }

    private void Update()
    {
        // เผื่อผู้เล่นเพิ่ง Spawn ผ่าน Photon Fusion ทีหลัง ให้ตามผูก Event อัตโนมัติ
        if (_subscribedInventory == null && InventoryManager.Local != null)
        {
            TrySubscribeInventoryEvents();
        }

        // กด Esc ปิดหน้าร้านเหมือน InventoryUI และ WorldMap
        if (_isOpen && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            SetShopOpen(false);
        }
    }

    public void ToggleShop()
    {
        SetShopOpen(!_isOpen);
    }

    public void SetShopOpen(bool open)
    {
        if (open == _isOpen) return;

        // ป้องกันเปิดร้านซ้อนกับ Modal อื่น (เช่น Inventory หรือ WorldMap)
        if (open && GameplayInput.IsBlocked) return;

        _isOpen = open;
        if (shopPanelRoot != null) shopPanelRoot.SetActive(open);

        if (open)
        {
            TrySubscribeInventoryEvents();
            GameplayInput.PushBlocker("ShopUI");
            ShowFeedback("เลือกซื้อสินค้าหรือขายไอเทมจากกระเป๋า (กด Esc เพื่อปิด)", Color.white);
            RefreshShopUI();
        }
        else
        {
            GameplayInput.PopBlocker("ShopUI");
        }
    }

    public void RefreshShopUI()
    {
        if (!_isOpen) return;

        var inv = InventoryManager.Local;
        var combat = PlayerCombat.Local;
        if (inv == null || !inv.IsActivated || combat == null || !combat.IsSpawned) return;

        if (goldText != null)
        {
            goldText.text = $"Gold: {combat.NetworkGold:N0}";
        }

        if (itemRowPrefab == null) return;

        // 1. สร้างรายการฝั่งซื้อ (ดึง ItemData จริงจาก ItemRegistry และรูปจาก HudIcons)
        if (buyContainer != null)
        {
            ClearChildren(buyContainer);

            foreach (ShopCatalogEntry entry in _runtimeCatalog)
            {
                if (!entry.availableToBuy || ItemRegistry.IsRemovedJunkItem(entry.itemId)) continue;

                ItemData item = ItemRegistry.Get(entry.itemId);
                if (item == null) continue;

                ShopItemRowUI row = Instantiate(itemRowPrefab, buyContainer);
                string capturedId = item.itemId;
                int capturedPrice = entry.buyPrice;

                row.Setup(
                    item.displayName,
                    ItemData.GetTierColor(item.tier),
                    $"ซื้อ: {capturedPrice:N0} G",
                    HudIcons.GetForItem(item),
                    "ซื้อ",
                    new Color(0.15f, 0.68f, 0.38f),
                    () => HandleBuyClick(capturedId, capturedPrice)
                );
            }
        }

        // 2. สร้างรายการฝั่งขาย (วนทุกช่อง 0..Capacity-1 แสดงเฉพาะช่องที่มีของและไม่ได้สวมใส่อยู่)
        if (sellContainer != null)
        {
            ClearChildren(sellContainer);

            for (int i = 0; i < inv.Capacity; i++)
            {
                InventorySlot slot = inv.GetSlot(i);
                if (slot == null || slot.IsEmpty || slot.item == null) continue;
                if (inv.IsSlotEquipped(i)) continue;
                if (ItemRegistry.IsRemovedJunkItem(slot.item.itemId)) continue;

                int capturedSlotIndex = i;
                int baseSellPrice = GetBaseSellPrice(slot.item);
                int finalSellPrice = ShopTransactionService.CalculateUnitSellPrice(baseSellPrice, slot.enhancementLevel);

                string enhText = slot.enhancementLevel > 0 ? $" +{slot.enhancementLevel}" : "";
                string qtyText = slot.quantity > 1 ? $" (x{slot.quantity})" : "";
                string displayLabel = $"{slot.item.displayName}{enhText}{qtyText}";

                ShopItemRowUI row = Instantiate(itemRowPrefab, sellContainer);
                row.Setup(
                    displayLabel,
                    ItemData.GetTierColor(slot.item.tier),
                    $"ขายได้: {finalSellPrice:N0} G",
                    HudIcons.GetForItem(slot.item),
                    "ขาย",
                    new Color(0.83f, 0.54f, 0.1f),
                    () => HandleSellClick(capturedSlotIndex, baseSellPrice)
                );
            }
        }
    }

    private void HandleBuyClick(string itemId, int unitBuyPrice)
    {
        bool ok = ShopTransactionService.TryBuyItem(itemId, 1, unitBuyPrice, out string msg);
        ShowFeedback(msg, ok ? new Color(0.18f, 0.8f, 0.44f) : new Color(0.91f, 0.3f, 0.24f));
        RefreshShopUI();
    }

    private void HandleSellClick(int slotIndex, int baseUnitSellPrice)
    {
        bool ok = ShopTransactionService.TrySellItemAtSlot(slotIndex, 1, baseUnitSellPrice, out string msg);
        ShowFeedback(msg, ok ? new Color(0.95f, 0.77f, 0.06f) : new Color(0.91f, 0.3f, 0.24f));
        RefreshShopUI();
    }

    private int GetBaseSellPrice(ItemData item)
    {
        if (catalogData != null) return catalogData.GetBaseSellPrice(item);

        ShopCatalogEntry entry = _runtimeCatalog.Find(e => e.itemId == item.itemId);
        if (entry != null && entry.sellPrice > 0) return entry.sellPrice;

        switch (item.tier)
        {
            case ItemTier.Normal: return 40;
            case ItemTier.Rare: return 120;
            case ItemTier.Epic: return 300;
            case ItemTier.Legendary: return 750;
            default: return 20;
        }
    }

    private void ShowFeedback(string message, Color color)
    {
        if (statusFeedbackText != null)
        {
            statusFeedbackText.text = message;
            statusFeedbackText.color = color;
        }
        Debug.Log($"[Shop] {message}");
    }

    private void TrySubscribeInventoryEvents()
    {
        if (_subscribedInventory != null || InventoryManager.Local == null) return;
        _subscribedInventory = InventoryManager.Local;
        _subscribedInventory.Changed += RefreshShopUI;
    }

    private void UnsubscribeInventoryEvents()
    {
        if (_subscribedInventory == null) return;
        _subscribedInventory.Changed -= RefreshShopUI;
        _subscribedInventory = null;
    }

    private static void ClearChildren(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Destroy(parent.GetChild(i).gameObject);
        }
    }
}