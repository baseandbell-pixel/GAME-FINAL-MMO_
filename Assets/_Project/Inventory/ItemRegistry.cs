using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Resolves ItemData by id. Network objects carry only the id string; every peer resolves it locally
/// from Resources/Items, so all clients must ship the same item assets.
/// </summary>
public static class ItemRegistry
{
    private static Dictionary<string, ItemData> _byId;

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
        Debug.Log($"[ItemRegistry] Loaded {_byId.Count} item definitions from Resources/Items.");
    }
}
