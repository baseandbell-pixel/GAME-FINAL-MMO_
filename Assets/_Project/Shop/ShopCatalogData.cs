using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class ShopCatalogEntry
{
    public string itemId;
    [Min(0)] public int buyPrice = 50;
    [Min(0)] public int sellPrice = 25;
    public bool availableToBuy = true;
}

[CreateAssetMenu(fileName = "DefaultShopCatalog", menuName = "MMO Shop/Shop Catalog Data")]
public class ShopCatalogData : ScriptableObject
{
    public List<ShopCatalogEntry> entries = new List<ShopCatalogEntry>();

    public ShopCatalogEntry FindEntry(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return null;
        return entries.Find(e => e.itemId == itemId);
    }

    // ราคาขายอ้างอิงจาก Catalog หรือคิดตามระดับ Tier หากเป็นของดรอปที่ไม่ได้ลงขายในร้าน
    public int GetBaseSellPrice(ItemData item)
    {
        if (item == null) return 0;

        ShopCatalogEntry entry = FindEntry(item.itemId);
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

    // รายการเริ่มต้นตรงตาม itemId จริง 19 รายการใน ItemRegistry (ใช้กรณีไม่ได้ผูก Asset)
    public static List<ShopCatalogEntry> CreateDefaultEntries()
    {
        return new List<ShopCatalogEntry>
        {
            // Consumables
            new ShopCatalogEntry { itemId = "health_potion", buyPrice = 50, sellPrice = 25, availableToBuy = true },
            new ShopCatalogEntry { itemId = "stamina_elixir", buyPrice = 60, sellPrice = 30, availableToBuy = true },
            new ShopCatalogEntry { itemId = "reinforced_greatsword_oil", buyPrice = 120, sellPrice = 60, availableToBuy = true },

            // Normal
            new ShopCatalogEntry { itemId = "iron_sword", buyPrice = 150, sellPrice = 75, availableToBuy = true },
            new ShopCatalogEntry { itemId = "wooden_buckler", buyPrice = 120, sellPrice = 60, availableToBuy = true },
            new ShopCatalogEntry { itemId = "hunter_vest", buyPrice = 140, sellPrice = 70, availableToBuy = true },
            new ShopCatalogEntry { itemId = "leather_greaves", buyPrice = 110, sellPrice = 55, availableToBuy = true },

            // Rare
            new ShopCatalogEntry { itemId = "steel_claymore", buyPrice = 450, sellPrice = 225, availableToBuy = true },
            new ShopCatalogEntry { itemId = "guardian_kite_shield", buyPrice = 400, sellPrice = 200, availableToBuy = true },
            new ShopCatalogEntry { itemId = "chainmail_cuirass", buyPrice = 420, sellPrice = 210, availableToBuy = true },
            new ShopCatalogEntry { itemId = "plated_greaves", buyPrice = 380, sellPrice = 190, availableToBuy = true },

            // Epic
            new ShopCatalogEntry { itemId = "runed_whetstone", buyPrice = 1000, sellPrice = 500, availableToBuy = true },
            new ShopCatalogEntry { itemId = "aegis_tower_shield", buyPrice = 950, sellPrice = 475, availableToBuy = true },
            new ShopCatalogEntry { itemId = "knights_crest", buyPrice = 980, sellPrice = 490, availableToBuy = true },
            new ShopCatalogEntry { itemId = "valkyrie_greaves", buyPrice = 900, sellPrice = 450, availableToBuy = true },

            // Legendary (ตั้งขายได้ หรือปรับ availableToBuy = false หากให้ดรอปจากบอสอย่างเดียว)
            new ShopCatalogEntry { itemId = "sovereign_greatsword_shard", buyPrice = 2500, sellPrice = 1250, availableToBuy = false },
            new ShopCatalogEntry { itemId = "dragonscale_bulwark", buyPrice = 2400, sellPrice = 1200, availableToBuy = false },
            new ShopCatalogEntry { itemId = "dragon_slayer_relic", buyPrice = 2450, sellPrice = 1225, availableToBuy = false },
            new ShopCatalogEntry { itemId = "sovereign_war_greaves", buyPrice = 2300, sellPrice = 1150, availableToBuy = false }
        };
    }
}