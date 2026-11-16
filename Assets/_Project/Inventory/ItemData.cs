using UnityEngine;

public enum ItemType
{
    Consumable,
    Weapon,
    Equipment,
    QuestItem,
    CraftingMaterial
}

public enum ItemTier
{
    Common = 0,
    Rare = 1,
    Epic = 2,
    Legendary = 3
}

/// <summary>
/// Data-driven item definition. Create via Assets > Create > Hunter > Item Data and place it under
/// a Resources/Items folder so ItemRegistry can resolve it from the id sent over the network.
/// </summary>
[CreateAssetMenu(fileName = "NewItem", menuName = "Hunter/Item Data")]
public class ItemData : ScriptableObject
{
    [Tooltip("Unique, stable id. This string (not the asset) is what travels over the network.")]
    public string itemId = "item_new";
    public string displayName = "New Item";
    [TextArea] public string description = "";
    public ItemType type = ItemType.CraftingMaterial;
    public ItemTier tier = ItemTier.Common;
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

    [Header("Weapon / Equipment")]
    public int damageBonus = 0;

    [Header("Rules")]
    public bool droppable = true;

    public int MaxStackSize => stackable ? Mathf.Max(1, maxStack) : 1;
    public bool IsUsable => type == ItemType.Consumable;
    public bool IsEquippable => type == ItemType.Weapon || type == ItemType.Equipment;

    public static Color GetTierColor(ItemTier tier)
    {
        return tier switch
        {
            ItemTier.Common => new Color(0.78f, 0.82f, 0.86f, 1f),
            ItemTier.Rare => new Color(0.22f, 0.68f, 1.0f, 1f),
            ItemTier.Epic => new Color(0.75f, 0.32f, 0.98f, 1f),
            ItemTier.Legendary => new Color(1.0f, 0.76f, 0.12f, 1f),
            _ => Color.white
        };
    }
}
