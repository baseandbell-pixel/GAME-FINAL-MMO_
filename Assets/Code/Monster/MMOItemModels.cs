using System;
using System.Collections.Generic;
using UnityEngine;

public enum ItemRarity
{
    Common = 0,
    Uncommon = 1,
    Rare = 2,
    Epic = 3,
    Legendary = 4
}

public enum ItemCategory
{
    Equipment = 0,
    Consumable = 1,
    Material = 2
}

public enum EquipSlot
{
    None = 0,
    Weapon = 1,
    Helmet = 2,
    Armor = 3,
    Boots = 4,
    Relic = 5
}

public enum SubStatType
{
    AttackFlat = 0,
    MaxHpFlat = 1,
    CritChancePct = 2,
    CritDamagePct = 3,
    MoveSpeedPct = 4,
    LifeStealPct = 5,
    CooldownReductionPct = 6
}

[Serializable]
public struct SubStatEntry
{
    public SubStatType type;
    public float value;
}

[Serializable]
public sealed class MMOItemInstance
{
    public string uid;
    public string itemId;
    public ItemRarity rarity;
    public int upgradeLevel;
    public int pityPercent;
    public int stackCount = 1;
    public bool isLocked;
    public List<SubStatEntry> subStats = new List<SubStatEntry>();

    public MMOItemInstance Clone()
    {
        var copy = new MMOItemInstance
        {
            uid = uid,
            itemId = itemId,
            rarity = rarity,
            upgradeLevel = upgradeLevel,
            pityPercent = pityPercent,
            stackCount = stackCount,
            isLocked = isLocked,
            subStats = new List<SubStatEntry>(subStats)
        };
        return copy;
    }
}

[Serializable]
public sealed class EquipSlotBinding
{
    public EquipSlot slot;
    public string itemUid;
}

[Serializable]
public sealed class MMOInventorySaveData
{
    public bool autoLootAll;
    public List<MMOItemInstance> bagItems = new List<MMOItemInstance>();
    public List<EquipSlotBinding> equipped = new List<EquipSlotBinding>();
    public string[] hotbarItemIds = new string[5];
}

public sealed class MMOItemTemplate
{
    public string itemId;
    public string displayName;
    public string shortCode;
    public ItemCategory category;
    public EquipSlot equipSlot;
    public string setId;
    public string description;
    public int baseAttack;
    public int baseMaxHp;
    public float baseCritChance;
    public float baseMoveSpeedPct;
    public float healPercent;
    public float buffDuration;
    public int maxStack = 1;
    public int sellGold = 15;
}

public struct ComputedGearStats
{
    public int attack;
    public int maxHp;
    public float critChancePct;
    public float critDamagePct;
    public float moveSpeedPct;
    public float lifeStealPct;
    public float cooldownReductionPct;
}

public static class MMOItemCatalog
{
    private static readonly Dictionary<string, MMOItemTemplate> Templates = new Dictionary<string, MMOItemTemplate>();
    private static bool _initialized;

    public static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;

        // Weapons
        Add(new MMOItemTemplate
        {
            itemId = "iron_greatsword",
            displayName = "Iron Greatsword",
            shortCode = "SWD",
            category = ItemCategory.Equipment,
            equipSlot = EquipSlot.Weapon,
            description = "Reliable steel greatsword carried by vanguard knights.",
            baseAttack = 38,
            baseCritChance = 4f,
            sellGold = 35
        });
        Add(new MMOItemTemplate
        {
            itemId = "guardian_claymore",
            displayName = "Guardian Claymore",
            shortCode = "CLY",
            category = ItemCategory.Equipment,
            equipSlot = EquipSlot.Weapon,
            description = "Heavy etched blade forged for frontline breachers.",
            baseAttack = 58,
            baseMaxHp = 80,
            baseCritChance = 6f,
            sellGold = 60
        });
        Add(new MMOItemTemplate
        {
            itemId = "crimson_reaver",
            displayName = "Crimson Reaver",
            shortCode = "RVR",
            category = ItemCategory.Equipment,
            equipSlot = EquipSlot.Weapon,
            description = "Blood-tempered blade resonating with critical strikes.",
            baseAttack = 76,
            baseCritChance = 10f,
            sellGold = 95
        });
        Add(new MMOItemTemplate
        {
            itemId = "warlord_wrath",
            displayName = "Warlord's Wrath",
            shortCode = "WRL",
            category = ItemCategory.Equipment,
            equipSlot = EquipSlot.Weapon,
            setId = "warlord",
            description = "Boss relic greatsword pulsing with ancient warlord fury.",
            baseAttack = 110,
            baseMaxHp = 150,
            baseCritChance = 14f,
            sellGold = 180
        });

        // Helmets
        Add(new MMOItemTemplate
        {
            itemId = "scout_helm",
            displayName = "Scout Helm",
            shortCode = "HLM",
            category = ItemCategory.Equipment,
            equipSlot = EquipSlot.Helmet,
            description = "Light reinforced visor with keen field awareness.",
            baseMaxHp = 110,
            baseCritChance = 3f,
            sellGold = 30
        });
        Add(new MMOItemTemplate
        {
            itemId = "warlord_crown",
            displayName = "Warlord Crown",
            shortCode = "CRN",
            category = ItemCategory.Equipment,
            equipSlot = EquipSlot.Helmet,
            setId = "warlord",
            description = "Horned crown of the Ancient Warlord.",
            baseAttack = 24,
            baseMaxHp = 260,
            baseCritChance = 6f,
            sellGold = 130
        });

        // Armors
        Add(new MMOItemTemplate
        {
            itemId = "sentinel_plate",
            displayName = "Sentinel Plate",
            shortCode = "PLT",
            category = ItemCategory.Equipment,
            equipSlot = EquipSlot.Armor,
            description = "Sturdy chestplate built to absorb heavy blows.",
            baseMaxHp = 220,
            sellGold = 45
        });
        Add(new MMOItemTemplate
        {
            itemId = "warlord_mail",
            displayName = "Warlord Battlemail",
            shortCode = "MAL",
            category = ItemCategory.Equipment,
            equipSlot = EquipSlot.Armor,
            setId = "warlord",
            description = "Obsidian-trimmed heavy armor of the Warlord Set.",
            baseAttack = 28,
            baseMaxHp = 420,
            sellGold = 150
        });

        // Boots
        Add(new MMOItemTemplate
        {
            itemId = "windrunner_greaves",
            displayName = "Windrunner Greaves",
            shortCode = "BTS",
            category = ItemCategory.Equipment,
            equipSlot = EquipSlot.Boots,
            description = "Nimble greaves granting swift battlefield footwork.",
            baseMaxHp = 95,
            baseMoveSpeedPct = 10f,
            sellGold = 38
        });
        Add(new MMOItemTemplate
        {
            itemId = "warlord_treads",
            displayName = "Warlord Treads",
            shortCode = "TRD",
            category = ItemCategory.Equipment,
            equipSlot = EquipSlot.Boots,
            setId = "warlord",
            description = "Ironshod warboots that shake the arena.",
            baseAttack = 18,
            baseMaxHp = 210,
            baseMoveSpeedPct = 16f,
            sellGold = 125
        });

        // Relics
        Add(new MMOItemTemplate
        {
            itemId = "vampiric_signet",
            displayName = "Vampiric Signet",
            shortCode = "RNG",
            category = ItemCategory.Equipment,
            equipSlot = EquipSlot.Relic,
            description = "Drains life essence from every melee strike.",
            baseAttack = 22,
            baseCritChance = 6f,
            sellGold = 70
        });
        Add(new MMOItemTemplate
        {
            itemId = "dragon_heart",
            displayName = "Dragonheart Relic",
            shortCode = "HRT",
            category = ItemCategory.Equipment,
            equipSlot = EquipSlot.Relic,
            description = "Radiates intense vitality and devastating critical power.",
            baseAttack = 42,
            baseMaxHp = 240,
            baseCritChance = 9f,
            sellGold = 140
        });

        // Consumables
        Add(new MMOItemTemplate
        {
            itemId = "potion_minor",
            displayName = "Minor HP Potion",
            shortCode = "HP1",
            category = ItemCategory.Consumable,
            description = "Restores 40% Max HP instantly.",
            healPercent = 0.40f,
            maxStack = 99,
            sellGold = 15
        });
        Add(new MMOItemTemplate
        {
            itemId = "potion_greater",
            displayName = "Greater HP Potion",
            shortCode = "HP2",
            category = ItemCategory.Consumable,
            description = "Restores 85% Max HP instantly.",
            healPercent = 0.85f,
            maxStack = 99,
            sellGold = 35
        });
        Add(new MMOItemTemplate
        {
            itemId = "elixir_berserk",
            displayName = "Berserk Elixir",
            shortCode = "ATK",
            category = ItemCategory.Consumable,
            description = "+30% Attack & +15% Crit Chance for 20s.",
            buffDuration = 20f,
            maxStack = 99,
            sellGold = 40
        });
        Add(new MMOItemTemplate
        {
            itemId = "scroll_haste",
            displayName = "Wind Haste Scroll",
            shortCode = "SPD",
            category = ItemCategory.Consumable,
            description = "+35% Move Speed & faster Dodge recovery for 15s.",
            buffDuration = 15f,
            maxStack = 99,
            sellGold = 40
        });
        Add(new MMOItemTemplate
        {
            itemId = "thunder_bomb",
            displayName = "Thunder Bomb",
            shortCode = "BMB",
            category = ItemCategory.Consumable,
            description = "Detonates shockwave damaging & stunning nearby enemies.",
            maxStack = 99,
            sellGold = 45
        });

        // Materials
        Add(new MMOItemTemplate
        {
            itemId = "enhance_stone",
            displayName = "Enhancement Stone",
            shortCode = "STN",
            category = ItemCategory.Material,
            description = "Crystal shard used at the Blacksmith to upgrade gear (+0..+15).",
            maxStack = 999,
            sellGold = 12
        });
        Add(new MMOItemTemplate
        {
            itemId = "warlord_core",
            displayName = "Warlord Core",
            shortCode = "COR",
            category = ItemCategory.Material,
            description = "Boss core dropped by Ancient Warlord. Crafts Legendary Warlord gear.",
            maxStack = 99,
            sellGold = 120
        });
    }

    private static void Add(MMOItemTemplate tpl)
    {
        Templates[tpl.itemId] = tpl;
    }

    public static MMOItemTemplate Get(string itemId)
    {
        EnsureInitialized();
        if (string.IsNullOrEmpty(itemId)) return null;
        return Templates.TryGetValue(itemId, out MMOItemTemplate tpl) ? tpl : null;
    }

    public static Color GetRarityColor(ItemRarity rarity)
    {
        return rarity switch
        {
            ItemRarity.Uncommon => new Color(0.28f, 0.92f, 0.42f, 1f),
            ItemRarity.Rare => new Color(0.28f, 0.72f, 1f, 1f),
            ItemRarity.Epic => new Color(0.76f, 0.36f, 1f, 1f),
            ItemRarity.Legendary => new Color(1f, 0.64f, 0.14f, 1f),
            _ => new Color(0.86f, 0.89f, 0.94f, 1f)
        };
    }

    public static string GetRarityHex(ItemRarity rarity)
    {
        return rarity switch
        {
            ItemRarity.Uncommon => "#47EB6B",
            ItemRarity.Rare => "#47B8FF",
            ItemRarity.Epic => "#C25CFF",
            ItemRarity.Legendary => "#FFA324",
            _ => "#DCE3F0"
        };
    }

    public static float GetRarityMultiplier(ItemRarity rarity)
    {
        return rarity switch
        {
            ItemRarity.Uncommon => 1.18f,
            ItemRarity.Rare => 1.42f,
            ItemRarity.Epic => 1.75f,
            ItemRarity.Legendary => 2.25f,
            _ => 1.0f
        };
    }

    public static ComputedGearStats EvaluateStats(MMOItemInstance item)
    {
        var stats = new ComputedGearStats();
        if (item == null) return stats;

        MMOItemTemplate tpl = Get(item.itemId);
        if (tpl == null) return stats;

        float scale = GetRarityMultiplier(item.rarity) * (1f + Mathf.Max(0, item.upgradeLevel) * 0.12f);
        stats.attack = Mathf.RoundToInt(tpl.baseAttack * scale);
        stats.maxHp = Mathf.RoundToInt(tpl.baseMaxHp * scale);
        stats.critChancePct = tpl.baseCritChance * Mathf.Lerp(1f, 1.4f, (int)item.rarity / 4f);
        stats.moveSpeedPct = tpl.baseMoveSpeedPct;

        if (item.itemId == "vampiric_signet")
        {
            stats.lifeStealPct += 4f + (int)item.rarity * 1.5f;
        }
        else if (item.itemId == "dragon_heart")
        {
            stats.critDamagePct += 25f + (int)item.rarity * 8f;
        }

        if (item.subStats != null)
        {
            for (int i = 0; i < item.subStats.Count; i++)
            {
                SubStatEntry sub = item.subStats[i];
                switch (sub.type)
                {
                    case SubStatType.AttackFlat: stats.attack += Mathf.RoundToInt(sub.value); break;
                    case SubStatType.MaxHpFlat: stats.maxHp += Mathf.RoundToInt(sub.value); break;
                    case SubStatType.CritChancePct: stats.critChancePct += sub.value; break;
                    case SubStatType.CritDamagePct: stats.critDamagePct += sub.value; break;
                    case SubStatType.MoveSpeedPct: stats.moveSpeedPct += sub.value; break;
                    case SubStatType.LifeStealPct: stats.lifeStealPct += sub.value; break;
                    case SubStatType.CooldownReductionPct: stats.cooldownReductionPct += sub.value; break;
                }
            }
        }

        return stats;
    }

    public static MMOItemInstance CreateInstance(string itemId, ItemRarity rarity, int count = 1, bool rollSubStats = true)
    {
        EnsureInitialized();
        MMOItemTemplate tpl = Get(itemId);
        if (tpl == null) return null;

        var inst = new MMOItemInstance
        {
            uid = Guid.NewGuid().ToString("N").Substring(0, 12),
            itemId = itemId,
            rarity = rarity,
            upgradeLevel = 0,
            pityPercent = 0,
            stackCount = Mathf.Clamp(count, 1, tpl.maxStack),
            isLocked = rarity >= ItemRarity.Epic
        };

        if (rollSubStats && tpl.category == ItemCategory.Equipment)
        {
            int subCount = rarity switch
            {
                ItemRarity.Uncommon => 1,
                ItemRarity.Rare => 2,
                ItemRarity.Epic => 3,
                ItemRarity.Legendary => 4,
                _ => 0
            };
            RollRandomSubStats(inst, subCount);
        }

        return inst;
    }

    private static void RollRandomSubStats(MMOItemInstance inst, int count)
    {
        if (count <= 0) return;
        var pool = new List<SubStatType>
        {
            SubStatType.AttackFlat,
            SubStatType.MaxHpFlat,
            SubStatType.CritChancePct,
            SubStatType.CritDamagePct,
            SubStatType.MoveSpeedPct,
            SubStatType.LifeStealPct,
            SubStatType.CooldownReductionPct
        };

        float rarityBoost = GetRarityMultiplier(inst.rarity);
        for (int i = 0; i < count && pool.Count > 0; i++)
        {
            int pick = UnityEngine.Random.Range(0, pool.Count);
            SubStatType type = pool[pick];
            pool.RemoveAt(pick);

            float val = type switch
            {
                SubStatType.AttackFlat => Mathf.Round(UnityEngine.Random.Range(8f, 18f) * rarityBoost),
                SubStatType.MaxHpFlat => Mathf.Round(UnityEngine.Random.Range(45f, 110f) * rarityBoost),
                SubStatType.CritChancePct => Mathf.Round(UnityEngine.Random.Range(2f, 5.5f) * rarityBoost * 10f) / 10f,
                SubStatType.CritDamagePct => Mathf.Round(UnityEngine.Random.Range(8f, 18f) * rarityBoost),
                SubStatType.MoveSpeedPct => Mathf.Round(UnityEngine.Random.Range(3f, 8f) * (1f + (int)inst.rarity * 0.2f)),
                SubStatType.LifeStealPct => Mathf.Round(UnityEngine.Random.Range(1.5f, 4f) * (1f + (int)inst.rarity * 0.2f) * 10f) / 10f,
                _ => Mathf.Round(UnityEngine.Random.Range(3f, 8f) * (1f + (int)inst.rarity * 0.2f))
            };

            inst.subStats.Add(new SubStatEntry { type = type, value = val });
        }
    }

    public static string FormatSubStat(SubStatEntry sub)
    {
        return sub.type switch
        {
            SubStatType.AttackFlat => $"+{sub.value:0} Attack Power",
            SubStatType.MaxHpFlat => $"+{sub.value:0} Max HP",
            SubStatType.CritChancePct => $"+{sub.value:0.0}% Crit Chance",
            SubStatType.CritDamagePct => $"+{sub.value:0}% Crit Damage",
            SubStatType.MoveSpeedPct => $"+{sub.value:0}% Move Speed",
            SubStatType.LifeStealPct => $"+{sub.value:0.0}% Life Steal",
            _ => $"-{sub.value:0}% Skill Cooldown"
        };
    }

    public static MMOItemInstance RollMonsterGearDrop(bool isBoss)
    {
        float roll = UnityEngine.Random.value;
        ItemRarity rarity;
        if (isBoss)
        {
            rarity = roll < 0.28f ? ItemRarity.Legendary : (roll < 0.78f ? ItemRarity.Epic : ItemRarity.Rare);
        }
        else
        {
            rarity = roll < 0.05f ? ItemRarity.Epic : (roll < 0.25f ? ItemRarity.Rare : (roll < 0.65f ? ItemRarity.Uncommon : ItemRarity.Common));
        }

        string[] normalPool =
        {
            "iron_greatsword", "guardian_claymore", "crimson_reaver",
            "scout_helm", "sentinel_plate", "windrunner_greaves", "vampiric_signet"
        };
        string[] bossPool =
        {
            "warlord_wrath", "warlord_crown", "warlord_mail", "warlord_treads", "dragon_heart", "crimson_reaver"
        };

        string[] pool = isBoss ? bossPool : normalPool;
        string itemId = pool[UnityEngine.Random.Range(0, pool.Length)];
        return CreateInstance(itemId, rarity, 1, rollSubStats: true);
    }
}
