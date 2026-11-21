using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ฐานข้อมูลไอเทมกลาง (Item Registry) สำหรับค้นหา ItemData ตามรหัส itemId
/// และจัดการตารางดรอปไอเทม 4 ระดับความหายาก (Normal, Rare, Epic, Legendary)
/// โดยตัดไอเทมขยะที่ไม่ใช้งาน (Old Key, Wolf Pelt, Basic Iron Scraps) ออกจากระบบดรอปและฐานข้อมูลหลัก
/// </summary>
public static class ItemRegistry
{
    // พจนานุกรมเก็บข้อมูลไอเทมทั้งหมดที่เปิดใช้งานในเกม
    private static Dictionary<string, ItemData> _byId;

    // อัตราดรอปพื้นฐานของหีบสมบัติประจำแคมป์ (Camp Treasure Chest): 62% Normal, 25% Rare, 10% Epic, 3% Legendary
    public const float NormalChance = 0.62f;
    public const float CommonChance = 0.62f;
    public const float RareChance = 0.25f;
    public const float EpicChance = 0.10f;
    public const float LegendaryChance = 0.03f;

    // อัตราดรอปพื้นฐานของมอนสเตอร์ทั่วไป (เมื่อสุ่มติดโอกาสดรอป ~30%): 74% Normal, 20% Rare, 5% Epic, 1% Legendary
    public const float EnemyNormalChance = 0.74f;
    public const float EnemyRareChance = 0.20f;
    public const float EnemyEpicChance = 0.05f;
    public const float EnemyLegendaryChance = 0.01f;

    // อัตราดรอปพื้นฐานของบอสและหีบบอส (Boss / Boss Chest): 35% Normal, 40% Rare, 18% Epic, 7% Legendary
    public const float BossNormalChance = 0.35f;
    public const float BossRareChance = 0.40f;
    public const float BossEpicChance = 0.18f;
    public const float BossLegendaryChance = 0.07f;

    // รายชื่อไอเทมระดับ Normal (ตัด basic_iron_scraps, old_key, wolf_pelt ออกตามข้อกำหนด)
    private static readonly string[] NormalIds =
    {
        "health_potion",
        "iron_sword",
        "wooden_buckler",
        "hunter_vest",
        "leather_greaves"
    };

    // รายชื่อไอเทมระดับ Rare
    private static readonly string[] RareIds =
    {
        "stamina_elixir",
        "reinforced_greatsword_oil",
        "steel_claymore",
        "guardian_kite_shield",
        "chainmail_cuirass",
        "plated_greaves"
    };

    // รายชื่อไอเทมระดับ Epic
    private static readonly string[] EpicIds =
    {
        "runed_whetstone",
        "aegis_tower_shield",
        "knights_crest",
        "valkyrie_greaves"
    };

    // รายชื่อไอเทมระดับ Legendary
    private static readonly string[] LegendaryIds =
    {
        "sovereign_greatsword_shard",
        "dragonscale_bulwark",
        "dragon_slayer_relic",
        "sovereign_war_greaves"
    };

    // รีเซ็ตแคชเมื่อเริ่ม Subsystem ใหม่ใน Unity Editor
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => _byId = null;

    /// <summary>
    /// ตรวจสอบว่าเป็นไอเทมขยะที่ถูกตัดออกจากระบบหรือไม่ (Old Key, Wolf Pelt, Basic Iron Scraps)
    /// </summary>
    public static bool IsRemovedJunkItem(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return true;
        return itemId == "old_key" || itemId == "wolf_pelt" || itemId == "basic_iron_scraps";
    }

    /// <summary>
    /// ดึงข้อมูล ItemData จากรหัส itemId (หากเป็นไอเทมขยะที่ถูกตัดออก จะคืนค่า null)
    /// </summary>
    public static ItemData Get(string itemId)
    {
        if (string.IsNullOrEmpty(itemId) || IsRemovedJunkItem(itemId)) return null;
        EnsureLoaded();
        if (_byId.TryGetValue(itemId, out var item)) return item;

        Debug.LogWarning($"[ItemRegistry] Unknown item id '{itemId}'. Is the asset in Resources/Items?");
        return null;
    }

    /// <summary>
    /// คืนค่ารายการไอเทมทั้งหมดที่เปิดใช้งานในระบบ
    /// </summary>
    public static IReadOnlyDictionary<string, ItemData> All
    {
        get
        {
            EnsureLoaded();
            return _byId;
        }
    }

    /// <summary>
    /// ดึงรายการไอเทมทั้งหมดเรียงตามหมวดหมู่และระดับความหายาก สำหรับแสดงในหน้า Item Compendium / Tutorial Guide
    /// </summary>
    public static List<ItemData> GetCompendiumItems(ItemType? filterCategory = null)
    {
        EnsureLoaded();
        var list = new List<ItemData>();
        foreach (var kv in _byId)
        {
            if (kv.Value == null || IsRemovedJunkItem(kv.Key)) continue;
            if (filterCategory.HasValue && kv.Value.type != filterCategory.Value) continue;
            list.Add(kv.Value);
        }

        // เรียงตามประเภทไอเทม และเรียงจากระดับ Normal -> Legendary
        list.Sort((a, b) =>
        {
            int catCmp = a.GetEquipmentSlot().CompareTo(b.GetEquipmentSlot());
            if (catCmp != 0) return catCmp;
            int tierCmp = a.tier.CompareTo(b.tier);
            if (tierCmp != 0) return tierCmp;
            return string.CompareOrdinal(a.displayName, b.displayName);
        });
        return list;
    }

    /// <summary>
    /// สุ่มเลือกไอเทม 1 ชิ้นจากระดับความหายาก (ItemTier) ที่ระบุ
    /// </summary>
    public static ItemData GetRandomItemOfTier(ItemTier tier)
    {
        EnsureLoaded();
        string[] pool = tier switch
        {
            ItemTier.Normal => NormalIds,
            ItemTier.Rare => RareIds,
            ItemTier.Epic => EpicIds,
            ItemTier.Legendary => LegendaryIds,
            _ => NormalIds
        };
        string chosenId = pool[Random.Range(0, pool.Length)];
        return Get(chosenId);
    }

    /// <summary>
    /// สุ่มดรอปไอเทมตามตารางมาตรฐานของแคมป์/หีบสมบัติ (62% Normal, 25% Rare, 10% Epic, 3% Legendary)
    /// </summary>
    public static ItemData RollTieredLoot(out ItemTier rolledTier, float? fixedRoll01 = null)
    {
        EnsureLoaded();
        float roll = fixedRoll01.HasValue ? Mathf.Clamp01(fixedRoll01.Value) : Random.value;

        if (roll < NormalChance)
            rolledTier = ItemTier.Normal;
        else if (roll < NormalChance + RareChance)
            rolledTier = ItemTier.Rare;
        else if (roll < NormalChance + RareChance + EpicChance)
            rolledTier = ItemTier.Epic;
        else
            rolledTier = ItemTier.Legendary;

        return GetRandomItemOfTier(rolledTier);
    }

    /// <summary>
    /// สุ่มดรอปไอเทมตามตารางบอสที่ปรับสมดุลใหม่ (35% Normal, 40% Rare, 18% Epic, 7% Legendary)
    /// </summary>
    public static ItemData RollBossLoot(out ItemTier rolledTier, float? fixedRoll01 = null)
    {
        EnsureLoaded();
        float roll = fixedRoll01.HasValue ? Mathf.Clamp01(fixedRoll01.Value) : Random.value;

        if (roll < BossNormalChance)
            rolledTier = ItemTier.Normal;
        else if (roll < BossNormalChance + BossRareChance)
            rolledTier = ItemTier.Rare;
        else if (roll < BossNormalChance + BossRareChance + BossEpicChance)
            rolledTier = ItemTier.Epic;
        else
            rolledTier = ItemTier.Legendary;

        return GetRandomItemOfTier(rolledTier);
    }

    /// <summary>
    /// โหลดไอเทมจาก Resources/Items และสร้าง/อัปเดตสเตตัสมาตรฐานของไอเทมทุกชิ้น
    /// </summary>
    private static void EnsureLoaded()
    {
        if (_byId != null) return;

        _byId = new Dictionary<string, ItemData>();
        var all = Resources.LoadAll<ItemData>("Items");
        foreach (var item in all)
        {
            if (item == null || string.IsNullOrEmpty(item.itemId)) continue;
            // ข้ามไอเทมขยะที่ตัดออก (Old Key, Wolf Pelt, Basic Iron Scraps)
            if (IsRemovedJunkItem(item.itemId)) continue;
            if (_byId.ContainsKey(item.itemId)) continue;
            _byId.Add(item.itemId, item);
        }

        EnsureTieredItemsRegistered();
        Debug.Log($"[ItemRegistry] Loaded {_byId.Count} active item definitions across 4 Rarity Tiers (Junk items excluded).");
    }

    /// <summary>
    /// ลงทะเบียนและกำหนดค่าสเตตัส + คำแนะนำการใช้งาน (Item Compendium Guide) ให้ครบทุกชิ้น
    /// </summary>
    private static void EnsureTieredItemsRegistered()
    {
        // ============================================================
        // 1. NORMAL TIER (สีเทาเงิน - ปรับสมดุลโบนัส HP/Stamina และยาฟื้นฟูให้เหมาะกับจังหวะการต่อสู้)
        // ============================================================
        RegisterOrUpdate("health_potion", "Healing Potion",
            "Restores 25 HP immediately when consumed.",
            "Drop from Camp Enemies & Chests. Select in Inventory or Hotbar [2-7] to restore 25 HP during combat.",
            ItemType.Consumable, ItemTier.Normal, new Color(0.25f, 0.92f, 0.38f),
            stackable: true, maxStack: 10, healAmount: 25);

        RegisterOrUpdate("iron_sword", "Iron GreatSword",
            "Standard-issue iron greatsword for vanguard warriors. +10 Attack Damage.",
            "Obtained from Normal Camps. Equip into the [SWORD] slot or use as Blacksmith upgrade fodder.",
            ItemType.Weapon, ItemTier.Normal, new Color(0.82f, 0.85f, 0.88f),
            stackable: false, maxStack: 1, damageBonus: 10);

        RegisterOrUpdate("wooden_buckler", "Ironclad Buckler",
            "Sturdy iron-rimmed buckler shield. +10 Max HP, +5 Max Stamina, +2 Defense.",
            "Obtained from Normal Camps. Equip into the [SHIELD] slot to boost survivability and guard defense.",
            ItemType.Shield, ItemTier.Normal, new Color(0.78f, 0.82f, 0.86f),
            stackable: false, maxStack: 1, maxHpBonus: 10, maxStaBonus: 5, defBonus: 2);

        RegisterOrUpdate("hunter_vest", "Hunter Chestplate",
            "Light reinforced chest armor for martial mobility. +12 Max HP, +2 Defense.",
            "Obtained from Normal Camps. Equip into the [ARMOR] slot to reduce incoming physical damage.",
            ItemType.Armor, ItemTier.Normal, new Color(0.80f, 0.83f, 0.86f),
            stackable: false, maxStack: 1, maxHpBonus: 12, defBonus: 2);

        RegisterOrUpdate("leather_greaves", "Leather Greaves",
            "Flexible combat leg guards. +8 Max HP, +5 Max Stamina, +0.20 Move Speed.",
            "Obtained from Normal Camps. Equip into the [PANTS] slot to increase movement speed and stamina.",
            ItemType.Pants, ItemTier.Normal, new Color(0.76f, 0.80f, 0.84f),
            stackable: false, maxStack: 1, maxHpBonus: 8, maxStaBonus: 5, speedBonus: 0.2f);

        // ============================================================
        // 2. RARE TIER (สีฟ้า - ปรับสมดุลโบนัส HP/Stamina และ Stamina Elixir)
        // ============================================================
        RegisterOrUpdate("stamina_elixir", "Stamina Elixir",
            "Instantly restores 40 Stamina for sustained combos, sprinting, and dodging.",
            "Drops from Enemies & Treasure Chests. Use from Inventory or Hotbar [2-7] to recover 40 Stamina immediately.",
            ItemType.Consumable, ItemTier.Rare, new Color(0.20f, 0.72f, 1.00f),
            stackable: true, maxStack: 10, staminaRestore: 40);

        RegisterOrUpdate("reinforced_greatsword_oil", "Reinforced GreatSword Oil",
            "Alchemical blade oil granting +15 Attack Damage for 45 seconds.",
            "Drops from Rare Chests & Enemies. Click [Use] before Boss or Camp fights for a 45s +15 ATK buff.",
            ItemType.Consumable, ItemTier.Rare, new Color(0.18f, 0.85f, 0.95f),
            stackable: true, maxStack: 10, atkBuffBonus: 15, atkBuffDuration: 45f);

        RegisterOrUpdate("steel_claymore", "Knight's Steel Claymore",
            "Fine tempered knight's claymore. +16 Attack Damage, +5% Critical Chance.",
            "Drops from Camps & Boss. Equip in [SWORD] slot or enhance up to +10 at the Blacksmith.",
            ItemType.Weapon, ItemTier.Rare, new Color(0.22f, 0.68f, 1.00f),
            stackable: false, maxStack: 1, damageBonus: 16, critBonus: 0.05f);

        RegisterOrUpdate("guardian_kite_shield", "Guardian Kite Shield",
            "Tempered steel kite shield. +20 Max HP, +10 Max Stamina, +4 Defense.",
            "Drops from Camps & Boss. Equip in [SHIELD] slot for solid guard endurance and defense.",
            ItemType.Shield, ItemTier.Rare, new Color(0.20f, 0.65f, 0.98f),
            stackable: false, maxStack: 1, maxHpBonus: 20, maxStaBonus: 10, defBonus: 4);

        RegisterOrUpdate("chainmail_cuirass", "Reinforced Chainmail Armor",
            "Heavy chainmail chestplate. +26 Max HP, +5 Max Stamina, +4 Defense.",
            "Drops from Camps & Boss. Equip in [ARMOR] slot to bolster Max HP and physical mitigation.",
            ItemType.Armor, ItemTier.Rare, new Color(0.24f, 0.70f, 1.00f),
            stackable: false, maxStack: 1, maxHpBonus: 26, maxStaBonus: 5, defBonus: 4);

        RegisterOrUpdate("plated_greaves", "Plated Greaves",
            "Steel-plated greaves. +16 Max HP, +10 Max Stamina, +0.35 Move Speed.",
            "Drops from Camps & Boss. Equip in [PANTS] slot for faster footwork and dodge recovery.",
            ItemType.Pants, ItemTier.Rare, new Color(0.22f, 0.66f, 0.96f),
            stackable: false, maxStack: 1, maxHpBonus: 16, maxStaBonus: 10, speedBonus: 0.35f);

        // ============================================================
        // 3. EPIC TIER (สีม่วง - ปรับสมดุลโบนัส HP/Stamina ให้ลดลง ~54%)
        // ============================================================
        RegisterOrUpdate("runed_whetstone", "Runed GreatSword",
            "Arcane-etched greatsword humming with power. +24 Attack Damage, +10% Crit.",
            "High-tier drop weighted toward Top DPS contributors. Equip in [SWORD] slot for massive cleave damage.",
            ItemType.Weapon, ItemTier.Epic, new Color(0.78f, 0.28f, 0.96f),
            stackable: false, maxStack: 1, damageBonus: 24, critBonus: 0.10f);

        RegisterOrUpdate("aegis_tower_shield", "Runed Aegis Shield",
            "Enchanted tower shield. +36 Max HP, +16 Max Stamina, +4 Attack, +6 Defense.",
            "Epic drop from Boss & Camps. Equip in [SHIELD] slot for elite defensive and offensive stats.",
            ItemType.Shield, ItemTier.Epic, new Color(0.72f, 0.26f, 0.94f),
            stackable: false, maxStack: 1, damageBonus: 4, maxHpBonus: 36, maxStaBonus: 16, defBonus: 6);

        RegisterOrUpdate("knights_crest", "Royal Knight's Armor",
            "Epic plate armor of the Royal Guard. +44 Max HP, +12 Max Stamina, +10 Attack, +6 Defense.",
            "Epic drop from Boss & Camps. Equip in [ARMOR] slot to gain both heavy protection and +10 ATK.",
            ItemType.Armor, ItemTier.Epic, new Color(0.75f, 0.32f, 0.98f),
            stackable: false, maxStack: 1, damageBonus: 10, maxHpBonus: 44, maxStaBonus: 12, defBonus: 6);

        RegisterOrUpdate("valkyrie_greaves", "Valkyrie Vanguard Pants",
            "Winged battle greaves. +28 Max HP, +16 Max Stamina, +0.55 Move Speed.",
            "Epic drop from Boss & Camps. Equip in [PANTS] slot for superior martial agility.",
            ItemType.Pants, ItemTier.Epic, new Color(0.76f, 0.30f, 0.96f),
            stackable: false, maxStack: 1, maxHpBonus: 28, maxStaBonus: 16, speedBonus: 0.55f);

        // ============================================================
        // 4. LEGENDARY TIER (สีทอง - ปรับสมดุลโบนัส HP/Stamina ให้ลดลง ~55%)
        // ============================================================
        RegisterOrUpdate("sovereign_greatsword_shard", "Sovereign GreatSword",
            "Mythic blade of the Sovereign King. +38 Attack Damage, +20% Critical Chance.",
            "Legendary drop from Boss Sovereign Golem (boosted by high DPS contribution). Equip in [SWORD] slot.",
            ItemType.Weapon, ItemTier.Legendary, new Color(1.00f, 0.65f, 0.10f),
            stackable: false, maxStack: 1, damageBonus: 38, critBonus: 0.20f);

        RegisterOrUpdate("dragonscale_bulwark", "Dragonscale Bulwark Shield",
            "Forged from ancient dragon scales. +58 Max HP, +22 Max Stamina, +10 Attack, +10 Defense.",
            "Legendary drop from Boss Sovereign Golem. Equip in [SHIELD] slot for unmatched fortification.",
            ItemType.Shield, ItemTier.Legendary, new Color(1.00f, 0.74f, 0.14f),
            stackable: false, maxStack: 1, damageBonus: 10, maxHpBonus: 58, maxStaBonus: 22, defBonus: 10);

        RegisterOrUpdate("dragon_slayer_relic", "Dragon-Slayer Chestplate",
            "Legendary dragonfire plate armor. +72 Max HP, +18 Max Stamina, +20 Attack, +10 Defense.",
            "Legendary drop from Boss Sovereign Golem. Equip in [ARMOR] slot for ultimate combat power.",
            ItemType.Armor, ItemTier.Legendary, new Color(1.00f, 0.78f, 0.12f),
            stackable: false, maxStack: 1, damageBonus: 20, maxHpBonus: 72, maxStaBonus: 18, defBonus: 10);

        RegisterOrUpdate("sovereign_war_greaves", "Sovereign War Greaves",
            "Legendary greaves of the Sovereign. +48 Max HP, +24 Max Stamina, +6 Attack, +0.80 Move Speed.",
            "Legendary drop from Boss Sovereign Golem. Equip in [PANTS] slot for maximum speed and endurance.",
            ItemType.Pants, ItemTier.Legendary, new Color(1.00f, 0.70f, 0.12f),
            stackable: false, maxStack: 1, damageBonus: 6, maxHpBonus: 48, maxStaBonus: 24, speedBonus: 0.8f);
    }

    private static void RegisterOrUpdate(
        string id,
        string displayName,
        string description,
        string obtainGuide,
        ItemType type,
        ItemTier tier,
        Color worldColor,
        bool stackable,
        int maxStack,
        int healAmount = 0,
        int staminaRestore = 0,
        int atkBuffBonus = 0,
        float atkBuffDuration = 0f,
        int damageBonus = 0,
        int maxHpBonus = 0,
        int maxStaBonus = 0,
        int defBonus = 0,
        float speedBonus = 0f,
        float critBonus = 0f)
    {
        if (!_byId.TryGetValue(id, out ItemData item) || item == null)
        {
            item = ScriptableObject.CreateInstance<ItemData>();
            item.name = displayName.Replace(" ", "").Replace("'", "");
            item.itemId = id;
            _byId[id] = item;
        }

        item.displayName = displayName;
        item.description = description;
        item.obtainAndUseGuide = obtainGuide;
        item.type = type;
        item.tier = tier;
        item.worldColor = worldColor;
        item.stackable = stackable;
        item.maxStack = maxStack;
        item.healAmount = healAmount;
        item.staminaRestoreAmount = staminaRestore;
        item.attackBuffBonus = atkBuffBonus;
        item.attackBuffDuration = atkBuffDuration;
        item.damageBonus = damageBonus;
        item.maxHealthBonus = maxHpBonus;
        item.maxStaminaBonus = maxStaBonus;
        item.defenseBonus = defBonus;
        item.moveSpeedBonus = speedBonus;
        item.critChanceBonus = critBonus;
        item.droppable = true;
    }
}
