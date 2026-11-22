using UnityEngine;

public static class ShopTransactionService
{
    public const int EnhancementPriceBonusPerLevel = 25;

    public static int CalculateUnitSellPrice(int baseUnitSellPrice, int enhancementLevel)
    {
        return Mathf.Max(1, baseUnitSellPrice + enhancementLevel * EnhancementPriceBonusPerLevel);
    }

    public static bool TryBuyItem(string itemId, int quantity, int unitBuyPrice, out string feedbackMessage)
    {
        var combat = PlayerCombat.Local;
        var inv = InventoryManager.Local;

        if (combat == null || !combat.IsSpawned || inv == null || !inv.IsActivated)
        {
            feedbackMessage = "ระบบผู้เล่นยังไม่พร้อมใช้งาน";
            return false;
        }

        if (string.IsNullOrEmpty(itemId) || ItemRegistry.IsRemovedJunkItem(itemId))
        {
            feedbackMessage = "ไอเทมนี้ไม่สามารถซื้อขายได้";
            return false;
        }

        if (quantity <= 0 || unitBuyPrice < 0)
        {
            feedbackMessage = "จำนวนหรือราคาสินค้าไม่ถูกต้อง";
            return false;
        }

        ItemData item = ItemRegistry.Get(itemId);
        if (item == null)
        {
            feedbackMessage = $"ไม่พบไอเทมรหัส '{itemId}' ใน ItemRegistry";
            return false;
        }

        int totalCost = unitBuyPrice * quantity;
        if (combat.NetworkGold < totalCost)
        {
            feedbackMessage = $"เงินไม่พอ (ต้องการ {totalCost:N0} G แต่มี {combat.NetworkGold:N0} G)";
            return false;
        }

        // เช็กช่องว่างกระเป๋าก่อนหักเงินเสมอ
        if (!inv.CanAdd(item, quantity))
        {
            feedbackMessage = "ช่องเก็บของในกระเป๋าเต็ม! กรุณาเคลียร์ช่องว่างก่อนซื้อ";
            return false;
        }

        if (!combat.TrySpendGold(totalCost))
        {
            feedbackMessage = "หักเงินไม่สำเร็จ กรุณาลองใหม่อีกครั้ง";
            return false;
        }

        int leftover = inv.AddItem(item, quantity, enhancementLevel: 0);
        if (leftover > 0)
        {
            int refund = leftover * unitBuyPrice;
            if (refund > 0) combat.AddGold(refund, "ShopRefund");
        }

        PlayerSaveManager.Instance?.SaveNow($"ShopBuy_{item.itemId}");

        int bought = quantity - leftover;
        feedbackMessage = $"ซื้อ {item.displayName} x{bought} สำเร็จ (-{bought * unitBuyPrice:N0} G)";
        return bought > 0;
    }

    public static bool TrySellItemAtSlot(int slotIndex, int sellQuantity, int baseUnitSellPrice, out string feedbackMessage)
    {
        var combat = PlayerCombat.Local;
        var inv = InventoryManager.Local;

        if (combat == null || !combat.IsSpawned || inv == null || !inv.IsActivated)
        {
            feedbackMessage = "ระบบผู้เล่นยังไม่พร้อมใช้งาน";
            return false;
        }

        // ดึงช่องกระเป๋าตาม slotIndex จริงที่ผู้เล่นคลิก ห้ามค้นหาตามชื่อหรือ itemId
        InventorySlot slot = inv.GetSlot(slotIndex);
        if (slot == null || slot.IsEmpty || slot.item == null)
        {
            feedbackMessage = "ช่องกระเป๋าที่เลือกว่างเปล่า";
            return false;
        }

        if (ItemRegistry.IsRemovedJunkItem(slot.item.itemId))
        {
            feedbackMessage = "ไม่สามารถขายไอเทมที่ถูกตัดออกจากระบบได้";
            return false;
        }

        // ห้ามขายอุปกรณ์ที่สวมใส่อยู่ โดยเช็กจาก slotIndex ตรงๆ
        if (inv.IsSlotEquipped(slotIndex))
        {
            feedbackMessage = "ไม่สามารถขายอุปกรณ์ที่กำลังสวมใส่อยู่ได้ กรุณาถอดออกก่อน";
            return false;
        }

        if (sellQuantity <= 0 || sellQuantity > slot.quantity)
        {
            feedbackMessage = $"จำนวนที่ต้องการขายไม่ถูกต้อง (ในช่องมี {slot.quantity} ชิ้น)";
            return false;
        }

        ItemData soldItem = slot.item;
        int finalUnitPrice = CalculateUnitSellPrice(baseUnitSellPrice, slot.enhancementLevel);

        int removed = inv.ConsumeSlotAtExactIndex(slotIndex, sellQuantity);
        if (removed <= 0)
        {
            feedbackMessage = "ลบไอเทมจากช่องกระเป๋าไม่สำเร็จ";
            return false;
        }

        int totalEarned = finalUnitPrice * removed;
        combat.AddGold(totalEarned, $"Sold {soldItem.displayName}");

        PlayerSaveManager.Instance?.SaveNow($"ShopSell_{soldItem.itemId}");

        feedbackMessage = $"ขาย {soldItem.displayName} x{removed} ได้รับ +{totalEarned:N0} G";
        return true;
    }
}