using System;

/// <summary>
/// One inventory cell: carries an ItemData reference, quantity, unique per-stack instanceId,
/// and gear enhancement level (+0 to +10) so exact slot indices and enhanced items are preserved.
/// </summary>
[Serializable]
public class InventorySlot
{
    public string instanceId;
    public ItemData item;
    public int quantity;
    public int enhancementLevel;

    public bool IsEmpty => item == null || quantity <= 0;
    public int FreeSpace => IsEmpty || enhancementLevel > 0 ? 0 : item.MaxStackSize - quantity;

    public void Set(ItemData newItem, int newQty, int enhanceLevel = 0, string existingInstanceId = null)
    {
        item = newItem;
        quantity = newQty;
        enhancementLevel = enhanceLevel < 0 ? 0 : enhanceLevel;
        instanceId = string.IsNullOrEmpty(existingInstanceId) ? Guid.NewGuid().ToString("N") : existingInstanceId;
    }

    public void EnsureInstanceId()
    {
        if (!IsEmpty && string.IsNullOrEmpty(instanceId))
            instanceId = Guid.NewGuid().ToString("N");
    }

    public void CopyFrom(InventorySlot other)
    {
        if (other == null || other.IsEmpty)
        {
            Clear();
            return;
        }
        instanceId = string.IsNullOrEmpty(other.instanceId) ? Guid.NewGuid().ToString("N") : other.instanceId;
        item = other.item;
        quantity = other.quantity;
        enhancementLevel = other.enhancementLevel;
    }

    public void Clear()
    {
        instanceId = null;
        item = null;
        quantity = 0;
        enhancementLevel = 0;
    }
}
