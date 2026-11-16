using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Resolves ItemData by id and provides the 4-Tier Rarity Loot tables (Normal, Rare, Epic, Legendary)
/// covering Weapons (Sword), Shields, Armor (Chestplate), Pants (Greaves), and Consumables.
/// </summary>
public static class ItemRegistry
{
    private static Dictionary<string, ItemData> _byId;

    // Normal Camp Drop Weights: 50% Normal, 30% Rare, 15% Epic, 5% Legendary
    public const float NormalChance = 0.50f;
    public const float CommonChance = 0.50f;
    public const float RareChance = 0.30f;
    public const float EpicChance = 0.15f;
    public const float LegendaryChance = 0.05f;

    // Boss Chest Drop Weights: 0% Normal, 30% Rare, 45% Epic, 25% Legendary
    public const float BossRareChance = 0.30f;
    public const float BossEpicChance = 0.45f;
    public const float BossLegendaryChance = 0.25f;

    private static readonly string[] NormalIds =
    {
        "health_potion",
        "basic_iron_scraps",
        "iron_sword",
        "wooden_buckler",
        "hunter_vest",
        "leather_greaves"
    };

    private static readonly string[] RareIds =
    {
        "stamina_elixir",
        "reinforced_greatsword_oil",
        "steel_claymore",
        "guardian_kite_shield",
        "chainmail_cuirass",
        "plated_greaves"
    };

    private static readonly string[] EpicIds =
    {
        "runed_whetstone",
        "aegis_tower_shield",
        "knights_crest",
        "valkyrie_greaves"
    };

    private static readonly string[] LegendaryIds =
    {
        "sovereign_greatsword_shard",
        "dragonscale_bulwark",
        "dragon_slayer_relic",
        "sovereign_war_greaves"
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => _byId = null;

    public static ItemData Get(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return null;
        EnsureLoaded();
        if (_byId.TryGetValue(itemId, out var item)) return item;

        Debug.LogWarning($"[ItemRegistry] Unknown item id '{itemId}'. Is the asset in Resources/Items?");
        return null;
    }

    public static IReadOnlyDictionary<string, ItemData> All
    {
        get
        {
            EnsureLoaded();
            return _byId;
        }
    }

    /// <summary>
    /// Rolls the standard camp 4-Tier Loot Table (50% Normal, 30% Rare, 15% Epic, 5% Legendary).
    /// </summary>
    public static ItemData RollTieredLoot(out ItemTier rolledTier, float? fixedRoll01 = null)
    {
        EnsureLoaded();
        float roll = fixedRoll01.HasValue ? Mathf.Clamp01(fixedRoll01.Value) : Random.value;

        string[] pool;
        if (roll < NormalChance)
        {
            rolledTier = ItemTier.Normal;
            pool = NormalIds;
        }
        else if (roll < NormalChance + RareChance)
        {
            rolledTier = ItemTier.Rare;
            pool = RareIds;
        }
        else if (roll < NormalChance + RareChance + EpicChance)
        {
            rolledTier = ItemTier.Epic;
            pool = EpicIds;
        }
        else
        {
            rolledTier = ItemTier.Legendary;
            pool = LegendaryIds;
        }

        string chosenId = pool[Random.Range(0, pool.Length)];
        return Get(chosenId);
    }

    /// <summary>
    /// Rolls the high-tier Boss Loot Table (30% Rare, 45% Epic, 25% Legendary).
    /// </summary>
    public static ItemData RollBossLoot(out ItemTier rolledTier, float? fixedRoll01 = null)
    {
        EnsureLoaded();
        float roll = fixedRoll01.HasValue ? Mathf.Clamp01(fixedRoll01.Value) : Random.value;

        string[] pool;
        if (roll < BossRareChance)
        {
            rolledTier = ItemTier.Rare;
            pool = RareIds;
        }
        else if (roll < BossRareChance + BossEpicChance)
        {
            rolledTier = ItemTier.Epic;
            pool = EpicIds;
        }
        else
        {
            rolledTier = ItemTier.Legendary;
            pool = LegendaryIds;
        }

        string chosenId = pool[Random.Range(0, pool.Length)];
        return Get(chosenId);
    }

    private static void EnsureLoaded()
    {
        if (_byId != null) return;

        _byId = new Dictionary<string, ItemData>();
        var all = Resources.LoadAll<ItemData>("Items");
        foreach (var item in all)
        {
            if (item == null || string.IsNullOrEmpty(item.itemId)) continue;
            if (_byId.ContainsKey(item.itemId))
            {
                Debug.LogWarning($"[ItemRegistry] Duplicate item id '{item.itemId}' ({item.name}) ignored.");
                continue;
            }
            _byId.Add(item.itemId, item);
        }

        EnsureTieredItemsRegistered();
        Debug.Log($"[ItemRegistry] Loaded {_byId.Count} item definitions across 4 Rarity Tiers (Normal, Rare, Epic, Legendary).");
    }

    private static void EnsureTieredItemsRegistered()
    {
        // ============================================================
        // 1. NORMAL TIER (White/Grey - 50%)
        // ============================================================
        RegisterOrUpdate("health_potion", "Healing Potion", "Restores 45 HP when consumed.",
            ItemType.Consumable, ItemTier.Normal, new Color(0.25f, 0.92f, 0.38f),
            stackable: true, maxStack: 10, healAmount: 45);

        RegisterOrUpdate("basic_iron_scraps", "Basic Iron Scraps", "Normal salvaged iron fragments from enemy camps.",
            ItemType.CraftingMaterial, ItemTier.Normal, new Color(0.75f, 0.78f, 0.82f),
            stackable: true, maxStack: 20);

        RegisterOrUpdate("iron_sword", "Iron GreatSword", "Standard-issue iron greatsword. +10 Attack Damage.",
            ItemType.Weapon, ItemTier.Normal, new Color(0.82f, 0.85f, 0.88f),
            stackable: false, maxStack: 1, damageBonus: 10);

        RegisterOrUpdate("wooden_buckler", "Ironclad Buckler", "Sturdy buckler shield. +20 Max HP, +10 Max Stamina, +2 Defense.",
            ItemType.Shield, ItemTier.Normal, new Color(0.78f, 0.82f, 0.86f),
            stackable: false, maxStack: 1, maxHpBonus: 20, maxStaBonus: 10, defBonus: 2);

        RegisterOrUpdate("hunter_vest", "Hunter Chestplate", "Light reinforced chest armor. +25 Max HP, +2 Defense.",
            ItemType.Armor, ItemTier.Normal, new Color(0.80f, 0.83f, 0.86f),
            stackable: false, maxStack: 1, maxHpBonus: 25, defBonus: 2);

        RegisterOrUpdate("leather_greaves", "Leather Greaves", "Flexible combat pants. +15 Max HP, +10 Max Stamina, +0.2 Move Speed.",
            ItemType.Pants, ItemTier.Normal, new Color(0.76f, 0.80f, 0.84f),
            stackable: false, maxStack: 1, maxHpBonus: 15, maxStaBonus: 10, speedBonus: 0.2f);

        // ============================================================
        // 2. RARE TIER (Blue - 30%)
        // ============================================================
        RegisterOrUpdate("stamina_elixir", "Stamina Elixir", "Instantly restores 80 Stamina for sustained combat.",
            ItemType.Consumable, ItemTier.Rare, new Color(0.20f, 0.72f, 1.00f),
            stackable: true, maxStack: 10, staminaRestore: 80);

        RegisterOrUpdate("reinforced_greatsword_oil", "Reinforced GreatSword Oil", "Coats your blade, granting +15 Attack Damage for 45 seconds.",
            ItemType.Consumable, ItemTier.Rare, new Color(0.18f, 0.85f, 0.95f),
            stackable: true, maxStack: 10, atkBuffBonus: 15, atkBuffDuration: 45f);

        RegisterOrUpdate("steel_claymore", "Knight's Steel Claymore", "Fine tempered claymore. +16 Attack Damage, +5% Crit.",
            ItemType.Weapon, ItemTier.Rare, new Color(0.22f, 0.68f, 1.00f),
            stackable: false, maxStack: 1, damageBonus: 16, critBonus: 0.05f);

        RegisterOrUpdate("guardian_kite_shield", "Guardian Kite Shield", "Tempered steel kite shield. +45 Max HP, +20 Max Stamina, +4 Defense.",
            ItemType.Shield, ItemTier.Rare, new Color(0.20f, 0.65f, 0.98f),
            stackable: false, maxStack: 1, maxHpBonus: 45, maxStaBonus: 20, defBonus: 4);

        RegisterOrUpdate("chainmail_cuirass", "Reinforced Chainmail Armor", "Heavy chainmail chestplate. +55 Max HP, +10 Max Stamina, +4 Defense.",
            ItemType.Armor, ItemTier.Rare, new Color(0.24f, 0.70f, 1.00f),
            stackable: false, maxStack: 1, maxHpBonus: 55, maxStaBonus: 10, defBonus: 4);

        RegisterOrUpdate("plated_greaves", "Plated Greaves", "Steel-plated greaves. +35 Max HP, +20 Max Stamina, +0.35 Move Speed.",
            ItemType.Pants, ItemTier.Rare, new Color(0.22f, 0.66f, 0.96f),
            stackable: false, maxStack: 1, maxHpBonus: 35, maxStaBonus: 20, speedBonus: 0.35f);

        // ============================================================
        // 3. EPIC TIER (Purple - 15%)
        // ============================================================
        RegisterOrUpdate("runed_whetstone", "Runed GreatSword", "Arcane-etched greatsword. +24 Attack Damage, +10% Crit.",
            ItemType.Weapon, ItemTier.Epic, new Color(0.78f, 0.28f, 0.96f),
            stackable: false, maxStack: 1, damageBonus: 24, critBonus: 0.10f);

        RegisterOrUpdate("aegis_tower_shield", "Runed Aegis Shield", "Enchanted tower shield. +80 Max HP, +35 Max Stamina, +4 Attack, +6 Defense.",
            ItemType.Shield, ItemTier.Epic, new Color(0.72f, 0.26f, 0.94f),
            stackable: false, maxStack: 1, damageBonus: 4, maxHpBonus: 80, maxStaBonus: 35, defBonus: 6);

        RegisterOrUpdate("knights_crest", "Royal Knight's Armor", "Epic plate armor of the Royal Guard. +95 Max HP, +25 Max Stamina, +10 Attack, +6 Defense.",
            ItemType.Armor, ItemTier.Epic, new Color(0.75f, 0.32f, 0.98f),
            stackable: false, maxStack: 1, damageBonus: 10, maxHpBonus: 95, maxStaBonus: 25, defBonus: 6);

        RegisterOrUpdate("valkyrie_greaves", "Valkyrie Vanguard Pants", "Winged battle greaves. +60 Max HP, +35 Max Stamina, +0.55 Move Speed.",
            ItemType.Pants, ItemTier.Epic, new Color(0.76f, 0.30f, 0.96f),
            stackable: false, maxStack: 1, maxHpBonus: 60, maxStaBonus: 35, speedBonus: 0.55f);

        // ============================================================
        // 4. LEGENDARY TIER (Gold/Orange - 5%)
        // ============================================================
        RegisterOrUpdate("sovereign_greatsword_shard", "Sovereign GreatSword", "Mythic blade of the Sovereign King. +38 Attack Damage, +20% Crit.",
            ItemType.Weapon, ItemTier.Legendary, new Color(1.00f, 0.65f, 0.10f),
            stackable: false, maxStack: 1, damageBonus: 38, critBonus: 0.20f);

        RegisterOrUpdate("dragonscale_bulwark", "Dragonscale Bulwark Shield", "Forged from ancient dragon scales. +130 Max HP, +50 Max Stamina, +10 Attack, +10 Defense.",
            ItemType.Shield, ItemTier.Legendary, new Color(1.00f, 0.74f, 0.14f),
            stackable: false, maxStack: 1, damageBonus: 10, maxHpBonus: 130, maxStaBonus: 50, defBonus: 10);

        RegisterOrUpdate("dragon_slayer_relic", "Dragon-Slayer Chestplate", "Legendary dragonfire plate armor. +160 Max HP, +40 Max Stamina, +20 Attack, +10 Defense.",
            ItemType.Armor, ItemTier.Legendary, new Color(1.00f, 0.78f, 0.12f),
            stackable: false, maxStack: 1, damageBonus: 20, maxHpBonus: 160, maxStaBonus: 40, defBonus: 10);

        RegisterOrUpdate("sovereign_war_greaves", "Sovereign War Greaves", "Legendary greaves of the Sovereign. +110 Max HP, +55 Max Stamina, +6 Attack, +0.8 Move Speed.",
            ItemType.Pants, ItemTier.Legendary, new Color(1.00f, 0.70f, 0.12f),
            stackable: false, maxStack: 1, damageBonus: 6, maxHpBonus: 110, maxStaBonus: 55, speedBonus: 0.8f);
    }

    private static void RegisterOrUpdate(
        string id,
        string displayName,
        string description,
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
