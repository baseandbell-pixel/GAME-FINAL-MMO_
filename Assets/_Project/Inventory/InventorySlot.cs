using System;

/// <summary>One inventory cell: an item definition and a quantity. Empty when item is null.</summary>
[Serializable]
public class InventorySlot
{
    public ItemData item;
    public int quantity;

    public bool IsEmpty => item == null || quantity <= 0;
    public int FreeSpace => IsEmpty ? 0 : item.MaxStackSize - quantity;

    public void Clear()
    {
        item = null;
        quantity = 0;
    }
}
