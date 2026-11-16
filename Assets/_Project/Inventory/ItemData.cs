using UnityEngine;

public enum ItemType
{
    Consumable = 0,
    Weapon = 1,
    Equipment = 2,
    QuestItem = 3,
    CraftingMaterial = 4,
    Shield = 5,
    Armor = 6,
    Pants = 7
}

public enum EquipmentSlotType
{
    None = 0,
    Sword = 1,
    Shield = 2,
    Armor = 3,
    Pants = 4
}

public enum ItemTier
{
    Normal = 0,
    Common = 0,
    Rare = 1,
    Epic = 2,
    Legendary = 3
}

/// <summary>
/// Data-driven item definition supporting Consumables, Crafting Materials, and 4 Equippable Gear Slots
/// (Sword, Shield, Armor, Pants) across 4 Rarity Tiers (Normal, Rare, Epic, Legendary) and +0..+10 Enhancement scaling.
/// </summary>
[CreateAssetMenu(fileName = "NewItem", menuName = "Hunter/Item Data")]
public class ItemData : ScriptableObject
{
    [Tooltip("Unique, stable id. This string (not the asset) is what travels over the network.")]
    public string itemId = "item_new";
    public string displayName = "New Item";
    [TextArea] public string description = "";
    public ItemType type = ItemType.CraftingMaterial;
    public ItemTier tier = ItemTier.Normal;
    public Sprite icon;
    [Tooltip("World pickup colour (no icon needed for the dev visuals).")]
    public Color worldColor = Color.white;

    [Header("Stacking")]
    public bool stackable = true;
    [Min(1)] public int maxStack = 20;

    [Header("Consumable")]
    [Min(0)] public int healAmount = 0;
    [Min(0)] public int staminaRestoreAmount = 0;
    [Min(0)] public int attackBuffBonus = 0;
    [Min(0f)] public float attackBuffDuration = 0f;

    [Header("Equipment Stat Bonuses")]
    public int damageBonus = 0;
    public int maxHealthBonus = 0;
    public int maxStaminaBonus = 0;
    public int defenseBonus = 0;
    public float moveSpeedBonus = 0f;
    [Range(0f, 1f)] public float critChanceBonus = 0f;

    [Header("Rules")]
    public bool droppable = true;

    public int MaxStackSize => stackable ? Mathf.Max(1, maxStack) : 1;
    public bool IsUsable => type == ItemType.Consumable;
    public bool IsEquippable => GetEquipmentSlot() != EquipmentSlotType.None;

    public EquipmentSlotType GetEquipmentSlot()
    {
        return type switch
        {
            ItemType.Weapon => EquipmentSlotType.Sword,
            ItemType.Shield => EquipmentSlotType.Shield,
            ItemType.Armor => EquipmentSlotType.Armor,
            ItemType.Equipment => EquipmentSlotType.Armor,
            ItemType.Pants => EquipmentSlotType.Pants,
            _ => EquipmentSlotType.None
        };
    }

    public int GetEnhancedDamage(int enhancementLevel) => EnhancementSystem.ScaleIntStat(damageBonus, enhancementLevel);
    public int GetEnhancedMaxHealth(int enhancementLevel) => EnhancementSystem.ScaleIntStat(maxHealthBonus, enhancementLevel);
    public int GetEnhancedMaxStamina(int enhancementLevel) => EnhancementSystem.ScaleIntStat(maxStaminaBonus, enhancementLevel);
    public int GetEnhancedDefense(int enhancementLevel) => EnhancementSystem.ScaleIntStat(defenseBonus, enhancementLevel);
    public float GetEnhancedMoveSpeed(int enhancementLevel) => EnhancementSystem.ScaleFloatStat(moveSpeedBonus, enhancementLevel);
    public float GetEnhancedCritChance(int enhancementLevel) => Mathf.Clamp01(EnhancementSystem.ScaleFloatStat(critChanceBonus, enhancementLevel));

    public static string GetTierDisplayName(ItemTier tier)
    {
        return tier switch
        {
            ItemTier.Normal => "Normal",
            ItemTier.Rare => "Rare",
            ItemTier.Epic => "Epic",
            ItemTier.Legendary => "Legendary",
            _ => "Normal"
        };
    }

    public static Color GetTierColor(ItemTier tier)
    {
        return tier switch
        {
            ItemTier.Normal => new Color(0.82f, 0.85f, 0.88f, 1f),
            ItemTier.Rare => new Color(0.22f, 0.68f, 1.0f, 1f),
            ItemTier.Epic => new Color(0.75f, 0.32f, 0.98f, 1f),
            ItemTier.Legendary => new Color(1.0f, 0.76f, 0.12f, 1f),
            _ => Color.white
        };
    }
}
