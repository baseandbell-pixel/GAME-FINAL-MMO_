using UnityEngine;

public enum ItemType
{
    Consumable,
    Weapon,
    Equipment,
    QuestItem,
    CraftingMaterial
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
    public Sprite icon;
    [Tooltip("World pickup colour (no icon needed for the dev visuals).")]
    public Color worldColor = Color.white;

    [Header("Stacking")]
    public bool stackable = true;
    [Min(1)] public int maxStack = 20;

    [Header("Consumable")]
    [Min(0)] public int healAmount = 0;

    [Header("Weapon / Equipment")]
    public int damageBonus = 0;

    [Header("Rules")]
    public bool droppable = true;

    public int MaxStackSize => stackable ? Mathf.Max(1, maxStack) : 1;
    public bool IsUsable => type == ItemType.Consumable;
    public bool IsEquippable => type == ItemType.Weapon || type == ItemType.Equipment;
}
