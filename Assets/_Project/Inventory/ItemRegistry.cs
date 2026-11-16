using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Resolves ItemData by id and provides the Camp Clear Tiered Loot table (50% Common, 30% Rare, 15% Epic, 5% Legendary).
/// Network objects carry only the id string; every peer resolves it locally from Resources/Items.
/// </summary>
public static class ItemRegistry
{
    private static Dictionary<string, ItemData> _byId;

    // Probability weights for Camp Reward Chest
    public const float CommonChance = 0.50f;
    public const float RareChance = 0.30f;
    public const float EpicChance = 0.15f;
    public const float LegendaryChance = 0.05f;

    private static readonly string[] CommonIds = { "health_potion", "basic_iron_scraps" };
    private static readonly string[] RareIds = { "stamina_elixir", "reinforced_greatsword_oil" };
    private static readonly string[] EpicIds = { "knights_crest", "runed_whetstone" };
    private static readonly string[] LegendaryIds = { "dragon_slayer_relic", "sovereign_greatsword_shard" };

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
    /// Rolls the tiered loot table:
    /// - Common (50%): Healing Potion / Basic Iron Scraps
    /// - Rare (30%): Stamina Elixir / Reinforced GreatSword Oil (+Atk Buff)
    /// - Epic (15%): Knight's Crest / Runed Whetstone
    /// - Legendary (5%): Dragon-Slayer Relic / Sovereign GreatSword Shard
    /// </summary>
    public static ItemData RollTieredLoot(out ItemTier rolledTier, float? fixedRoll01 = null)
    {
        EnsureLoaded();
        float roll = fixedRoll01.HasValue ? Mathf.Clamp01(fixedRoll01.Value) : Random.value;

        string[] pool;
        if (roll < CommonChance)
        {
            rolledTier = ItemTier.Common;
            pool = CommonIds;
        }
        else if (roll < CommonChance + RareChance)
        {
            rolledTier = ItemTier.Rare;
            pool = RareIds;
        }
        else if (roll < CommonChance + RareChance + EpicChance)
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
        Debug.Log($"[ItemRegistry] Loaded {_byId.Count} item definitions (including tiered loot table).");
    }

    private static void EnsureTieredItemsRegistered()
    {
        // Common (50%)
        RegisterOrUpdate("health_potion", "Healing Potion", "Restores 45 HP when consumed.",
            ItemType.Consumable, ItemTier.Common, new Color(0.25f, 0.92f, 0.38f),
            stackable: true, maxStack: 10, healAmount: 45);

        RegisterOrUpdate("basic_iron_scraps", "Basic Iron Scraps", "Common salvaged iron fragments from enemy camps.",
            ItemType.CraftingMaterial, ItemTier.Common, new Color(0.72f, 0.76f, 0.80f),
            stackable: true, maxStack: 20);

        // Rare (30%)
        RegisterOrUpdate("stamina_elixir", "Stamina Elixir", "Instantly restores 80 Stamina for sustained combat.",
            ItemType.Consumable, ItemTier.Rare, new Color(0.20f, 0.72f, 1.00f),
            stackable: true, maxStack: 10, staminaRestore: 80);

        RegisterOrUpdate("reinforced_greatsword_oil", "Reinforced GreatSword Oil", "Coats your blade, granting +15 Attack Damage for 45 seconds.",
            ItemType.Consumable, ItemTier.Rare, new Color(0.18f, 0.85f, 0.95f),
            stackable: true, maxStack: 10, atkBuffBonus: 15, atkBuffDuration: 45f);

        // Epic (15%)
        RegisterOrUpdate("knights_crest", "Knight's Crest", "An ancient emblem of valor. Grants +16 Attack Damage when equipped.",
            ItemType.Equipment, ItemTier.Epic, new Color(0.74f, 0.30f, 0.98f),
            stackable: false, maxStack: 1, damageBonus: 16);

        RegisterOrUpdate("runed_whetstone", "Runed Whetstone", "Arcane-etched whetstone. Grants +20 Weapon Damage when equipped.",
            ItemType.Weapon, ItemTier.Epic, new Color(0.82f, 0.25f, 0.95f),
            stackable: false, maxStack: 1, damageBonus: 20);

        // Legendary (5%)
        RegisterOrUpdate("dragon_slayer_relic", "Dragon-Slayer Relic", "Legendary talisman forged in dragonfire. Grants +30 Attack Damage when equipped.",
            ItemType.Equipment, ItemTier.Legendary, new Color(1.00f, 0.78f, 0.12f),
            stackable: false, maxStack: 1, damageBonus: 30);

        RegisterOrUpdate("sovereign_greatsword_shard", "Sovereign GreatSword Shard", "Mythic shard of the Sovereign Blade. Grants +35 Weapon Damage when equipped.",
            ItemType.Weapon, ItemTier.Legendary, new Color(1.00f, 0.62f, 0.08f),
            stackable: false, maxStack: 1, damageBonus: 35);
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
        int damageBonus = 0)
    {
        if (!_byId.TryGetValue(id, out ItemData item) || item == null)
        {
            item = ScriptableObject.CreateInstance<ItemData>();
            item.name = displayName.Replace(" ", "");
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
        if (healAmount > 0) item.healAmount = healAmount;
        if (staminaRestore > 0) item.staminaRestoreAmount = staminaRestore;
        if (atkBuffBonus > 0) item.attackBuffBonus = atkBuffBonus;
        if (atkBuffDuration > 0f) item.attackBuffDuration = atkBuffDuration;
        if (damageBonus > 0) item.damageBonus = damageBonus;
        item.droppable = true;
    }
}
