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
    /// ลงทะเบียนและกำหนดค่าสเตตัสแบบจำนวนเต็มคงที่ (Flat Integer Rebalance - ลดลง 50% ทุก Tier + แยกบทบาทอุปกรณ์ + ตัด % CRIT ออกทั้งหมด)
    /// พร้อมคำแนะนำการใช้งานสำหรับหน้าคู่มือไอเทม (Item Compendium Guide) ให้ตรงกับค่าจริง 100%
    /// </summary>
    private static void EnsureTieredItemsRegistered()
    {
        // ============================================================
        // 1. NORMAL TIER (สีเทาเงิน - ลดค่าพลังอุปกรณ์ลง 50%, อาวุธเน้น ATK, ชุดป้องกัน ATK = +0)
        // ============================================================
        RegisterOrUpdate("health_potion", "Healing Potion",
            "Restores 28% of Max HP (minimum +50 HP) immediately when consumed.",
            "Drop from Camp Enemies & Chests. Select in Inventory or Hotbar [2-7] to restore 28% Max HP (min +50 HP) during combat.",
            ItemType.Consumable, ItemTier.Normal, new Color(0.25f, 0.92f, 0.38f),
            stackable: true, maxStack: 10, healAmount: 50);

        RegisterOrUpdate("iron_sword", "Iron GreatSword",
            "Standard-issue iron greatsword for vanguard warriors. +5 Attack Damage.",
            "Obtained from Starter Camps. Equip into the [WEAPON] slot or enhance (+1 ATK/level) at the Blacksmith.",
            ItemType.Weapon, ItemTier.Normal, new Color(0.82f, 0.85f, 0.88f),
            stackable: false, maxStack: 1, damageBonus: 5);

        RegisterOrUpdate("wooden_buckler", "Ironclad Buckler",
            "Sturdy iron-rimmed buckler shield. +5 Max HP, +3 Max Stamina, +1 Defense.",
            "Obtained from Starter Camps. Equip into the [SHIELD] slot for pure defensive survivability (+0 ATK).",
            ItemType.Shield, ItemTier.Normal, new Color(0.78f, 0.82f, 0.86f),
            stackable: false, maxStack: 1, damageBonus: 0, maxHpBonus: 5, maxStaBonus: 3, defBonus: 1);

        RegisterOrUpdate("hunter_vest", "Hunter Chestplate",
            "Light reinforced chest armor for martial mobility. +6 Max HP, +1 Defense.",
            "Obtained from Starter Camps. Equip into the [ARMOR] slot to reduce incoming physical damage (+0 ATK).",
            ItemType.Armor, ItemTier.Normal, new Color(0.80f, 0.83f, 0.86f),
            stackable: false, maxStack: 1, damageBonus: 0, maxHpBonus: 6, defBonus: 1);

        RegisterOrUpdate("leather_greaves", "Leather Greaves",
            "Flexible combat leg guards. +4 Max HP, +3 Max Stamina, +0.10 Move Speed.",
            "Obtained from Starter Camps. Equip into the [PANTS] slot for mobility and stamina (+0 ATK).",
            ItemType.Pants, ItemTier.Normal, new Color(0.76f, 0.80f, 0.84f),
            stackable: false, maxStack: 1, damageBonus: 0, maxHpBonus: 4, maxStaBonus: 3, speedBonus: 0.10f);

        // ============================================================
        // 2. RARE TIER (สีฟ้า - ลดค่าพลังลง 50%, ตัด % CRIT ออก, ชุดป้องกัน ATK = +0)
        // ============================================================
        RegisterOrUpdate("stamina_elixir", "Stamina Elixir",
            "Instantly restores 60 Stamina for sustained combos, sprinting, and dodging.",
            "Drops from Enemies & Treasure Chests. Use from Inventory or Hotbar [2-7] to recover 60 Stamina immediately.",
            ItemType.Consumable, ItemTier.Rare, new Color(0.20f, 0.72f, 1.00f),
            stackable: true, maxStack: 10, staminaRestore: 60);

        RegisterOrUpdate("reinforced_greatsword_oil", "Reinforced GreatSword Oil",
            "Alchemical blade oil granting +10 Attack Damage for 60 seconds.",
            "Drops from Rare Chests & Enemies. Use from Inventory or Hotbar [2-7] before Frontier or Boss fights for a 60s +10 ATK buff.",
            ItemType.Consumable, ItemTier.Rare, new Color(0.18f, 0.85f, 0.95f),
            stackable: true, maxStack: 10, atkBuffBonus: 10, atkBuffDuration: 60f);

        RegisterOrUpdate("steel_claymore", "Knight's Steel Claymore",
            "Fine tempered knight's claymore. +8 Attack Damage, +2 Max Stamina.",
            "Drops from Camps & Boss. Equip in [WEAPON] slot or enhance (+1 ATK/level) up to +10 at the Blacksmith.",
            ItemType.Weapon, ItemTier.Rare, new Color(0.22f, 0.68f, 1.00f),
            stackable: false, maxStack: 1, damageBonus: 8, maxStaBonus: 2, critBonus: 0f);

        RegisterOrUpdate("guardian_kite_shield", "Guardian Kite Shield",
            "Tempered steel kite shield. +10 Max HP, +5 Max Stamina, +2 Defense.",
            "Drops from Camps & Boss. Equip in [SHIELD] slot for reliable guard endurance and mitigation (+0 ATK).",
            ItemType.Shield, ItemTier.Rare, new Color(0.20f, 0.65f, 0.98f),
            stackable: false, maxStack: 1, damageBonus: 0, maxHpBonus: 10, maxStaBonus: 5, defBonus: 2);

        RegisterOrUpdate("chainmail_cuirass", "Reinforced Chainmail Armor",
            "Heavy chainmail chestplate. +13 Max HP, +3 Max Stamina, +2 Defense.",
            "Drops from Camps & Boss. Equip in [ARMOR] slot for sturdy Max HP and physical mitigation (+0 ATK).",
            ItemType.Armor, ItemTier.Rare, new Color(0.24f, 0.70f, 1.00f),
            stackable: false, maxStack: 1, damageBonus: 0, maxHpBonus: 13, maxStaBonus: 3, defBonus: 2);

        RegisterOrUpdate("plated_greaves", "Plated Greaves",
            "Steel-plated greaves. +8 Max HP, +5 Max Stamina, +0.18 Move Speed.",
            "Drops from Camps & Boss. Equip in [PANTS] slot for faster footwork and dodge endurance (+0 ATK).",
            ItemType.Pants, ItemTier.Rare, new Color(0.22f, 0.66f, 0.96f),
            stackable: false, maxStack: 1, damageBonus: 0, maxHpBonus: 8, maxStaBonus: 5, speedBonus: 0.18f);

        // ============================================================
        // 3. EPIC TIER (สีม่วง - ลดค่าพลังลง 50%, ตัด % CRIT ออก, ชุดป้องกันเพดาน ATK <= +1)
        // ============================================================
        RegisterOrUpdate("runed_whetstone", "Runed GreatSword",
            "Arcane-etched greatsword humming with flat force. +12 Attack Damage, +4 Max Stamina.",
            "Epic drop from Frontier Camps & Boss. Equip in [WEAPON] slot (+2 ATK/level when enhanced).",
            ItemType.Weapon, ItemTier.Epic, new Color(0.78f, 0.28f, 0.96f),
            stackable: false, maxStack: 1, damageBonus: 12, maxStaBonus: 4, critBonus: 0f);

        RegisterOrUpdate("aegis_tower_shield", "Runed Aegis Shield",
            "Enchanted tower shield. +18 Max HP, +8 Max Stamina, +3 Defense, +1 Attack.",
            "Epic drop from Frontier Camps & Boss. Equip in [SHIELD] slot for elite defense (Role Cap: +1 ATK max).",
            ItemType.Shield, ItemTier.Epic, new Color(0.72f, 0.26f, 0.94f),
            stackable: false, maxStack: 1, damageBonus: 1, maxHpBonus: 18, maxStaBonus: 8, defBonus: 3);

        RegisterOrUpdate("knights_crest", "Royal Knight's Armor",
            "Epic plate armor of the Royal Guard. +22 Max HP, +6 Max Stamina, +3 Defense, +1 Attack.",
            "Epic drop from Frontier Camps & Boss. Equip in [ARMOR] slot for heavy defense (Role Cap: +1 ATK max).",
            ItemType.Armor, ItemTier.Epic, new Color(0.75f, 0.32f, 0.98f),
            stackable: false, maxStack: 1, damageBonus: 1, maxHpBonus: 22, maxStaBonus: 6, defBonus: 3);

        RegisterOrUpdate("valkyrie_greaves", "Valkyrie Vanguard Pants",
            "Winged battle greaves. +14 Max HP, +8 Max Stamina, +0.28 Move Speed.",
            "Epic drop from Frontier Camps & Boss. Equip in [PANTS] slot for superior martial agility (+0 ATK).",
            ItemType.Pants, ItemTier.Epic, new Color(0.76f, 0.30f, 0.96f),
            stackable: false, maxStack: 1, damageBonus: 0, maxHpBonus: 14, maxStaBonus: 8, speedBonus: 0.28f);

        // ============================================================
        // 4. LEGENDARY TIER (สีทอง - ลดค่าพลังลง 50%, ตัด % CRIT ออก, ชุดป้องกันเพดาน ATK <= +2)
        // ============================================================
        RegisterOrUpdate("sovereign_greatsword_shard", "Sovereign GreatSword",
            "Mythic blade of the Sovereign King. +19 Attack Damage, +6 Max Stamina.",
            "Legendary drop from Boss Sovereign Golem. Equip in [WEAPON] slot (+2 ATK/level when enhanced).",
            ItemType.Weapon, ItemTier.Legendary, new Color(1.00f, 0.65f, 0.10f),
            stackable: false, maxStack: 1, damageBonus: 19, maxStaBonus: 6, critBonus: 0f);

        RegisterOrUpdate("dragonscale_bulwark", "Dragonscale Bulwark Shield",
            "Forged from ancient dragon scales. +29 Max HP, +11 Max Stamina, +5 Defense, +2 Attack.",
            "Legendary drop from Boss Sovereign Golem. Equip in [SHIELD] slot for top fortification (Role Cap: +2 ATK max).",
            ItemType.Shield, ItemTier.Legendary, new Color(1.00f, 0.74f, 0.14f),
            stackable: false, maxStack: 1, damageBonus: 2, maxHpBonus: 29, maxStaBonus: 11, defBonus: 5);

        RegisterOrUpdate("dragon_slayer_relic", "Dragon-Slayer Chestplate",
            "Legendary dragonfire plate armor. +36 Max HP, +9 Max Stamina, +5 Defense, +2 Attack.",
            "Legendary drop from Boss Sovereign Golem. Equip in [ARMOR] slot for peak survivability (Role Cap: +2 ATK max).",
            ItemType.Armor, ItemTier.Legendary, new Color(1.00f, 0.78f, 0.12f),
            stackable: false, maxStack: 1, damageBonus: 2, maxHpBonus: 36, maxStaBonus: 9, defBonus: 5);

        RegisterOrUpdate("sovereign_war_greaves", "Sovereign War Greaves",
            "Legendary greaves of the Sovereign. +24 Max HP, +12 Max Stamina, +0.40 Move Speed.",
            "Legendary drop from Boss Sovereign Golem. Equip in [PANTS] slot for top speed and endurance (+0 ATK).",
            ItemType.Pants, ItemTier.Legendary, new Color(1.00f, 0.70f, 0.12f),
            stackable: false, maxStack: 1, damageBonus: 0, maxHpBonus: 24, maxStaBonus: 12, speedBonus: 0.40f);
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
