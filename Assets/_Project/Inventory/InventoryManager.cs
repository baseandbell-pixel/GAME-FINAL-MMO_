using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-player, client-side inventory. Only the peer that controls the player activates it (checked through
/// PlayerCombat's authority flags), so remote proxies carry an inert component.
///
/// NOTE: inventory contents live on the owning client only (not networked, not persisted). Items cross the
/// network exclusively as WorldItem pickups/drops. Persisting to GoogleAPI/backend is the next step.
/// </summary>
[RequireComponent(typeof(PlayerCombat))]
public class InventoryManager : MonoBehaviour
{
    public static InventoryManager Local { get; private set; }

    [Min(1)] [SerializeField] private int capacity = 24;
    [Tooltip("Prefab used when dropping items back into the world.")]
    [SerializeField] private WorldItem worldItemPrefab;
    [SerializeField] private bool createUi = true;

    private readonly List<InventorySlot> _slots = new List<InventorySlot>();
    private PlayerCombat _combat;
    private InventoryUI _ui;
    private bool _activated;

    public IReadOnlyList<InventorySlot> Slots => _slots;
    public int Capacity => capacity;
    public int Experience { get; private set; }
    public string EquippedWeaponId { get; private set; }
    public string EquippedEquipmentId { get; private set; }

    /// <summary>Raised whenever slots, equipment or experience change.</summary>
    public event Action Changed;

    // ------------------------------------------------------------------ lifecycle

    private void Awake()
    {
        _combat = GetComponent<PlayerCombat>();
        for (int i = 0; i < capacity; i++) _slots.Add(new InventorySlot());
    }

    private void Update()
    {
        if (_activated || _combat == null) return;
        if (!_combat.IsSpawned || !_combat.IsLocallyControlled) return;

        _activated = true;
        Local = this;
        Debug.Log($"[Inventory] Activated for local player '{name}' ({capacity} slots).");
        if (createUi) _ui = InventoryUI.Create(this);
    }

    private void OnDestroy()
    {
        if (Local == this) Local = null;
        if (_ui != null) Destroy(_ui.gameObject);
    }

    // ------------------------------------------------------------------ add / remove

    public bool CanAdd(ItemData item, int quantity)
    {
        if (item == null || quantity <= 0) return false;

        int room = 0;
        int max = item.MaxStackSize;
        foreach (var s in _slots)
        {
            if (s.IsEmpty) room += max;
            else if (s.item == item) room += s.FreeSpace;
            if (room >= quantity) return true;
        }
        return room >= quantity;
    }

    /// <summary>Adds items, filling existing stacks first. Returns the quantity that did NOT fit.</summary>
    public int AddItem(ItemData item, int quantity)
    {
        if (item == null || quantity <= 0)
        {
            Debug.LogWarning("[Inventory] AddItem ignored: null item or non-positive quantity.");
            return Mathf.Max(0, quantity);
        }

        int remaining = quantity;
        int max = item.MaxStackSize;

        if (item.stackable)
        {
            foreach (var s in _slots)
            {
                if (remaining <= 0) break;
                if (s.IsEmpty || s.item != item || s.FreeSpace <= 0) continue;
                int add = Mathf.Min(s.FreeSpace, remaining);
                s.quantity += add;
                remaining -= add;
            }
        }

        foreach (var s in _slots)
        {
            if (remaining <= 0) break;
            if (!s.IsEmpty) continue;
            int add = Mathf.Min(max, remaining);
            s.item = item;
            s.quantity = add;
            remaining -= add;
        }

        int added = quantity - remaining;
        if (added > 0)
            Debug.Log($"[Inventory] +{added}x {item.displayName}" + (remaining > 0 ? $" ({remaining} did not fit)" : ""));
        else
            Debug.LogWarning($"[Inventory] Inventory full: could not add {item.displayName}.");

        if (added > 0) Changed?.Invoke();
        return remaining;
    }

    /// <summary>Removes up to quantity of an item across slots. Returns how many were removed.</summary>
    public int RemoveItem(ItemData item, int quantity)
    {
        if (item == null || quantity <= 0) return 0;

        int toRemove = quantity;
        foreach (var s in _slots)
        {
            if (toRemove <= 0) break;
            if (s.IsEmpty || s.item != item) continue;
            int take = Mathf.Min(s.quantity, toRemove);
            s.quantity -= take;
            toRemove -= take;
            if (s.quantity <= 0) s.Clear();
        }

        int removed = quantity - toRemove;
        if (removed > 0)
        {
            Debug.Log($"[Inventory] -{removed}x {item.displayName}");
            ValidateEquipment();
            Changed?.Invoke();
        }
        return removed;
    }

    public int Count(ItemData item)
    {
        int n = 0;
        foreach (var s in _slots) if (!s.IsEmpty && s.item == item) n += s.quantity;
        return n;
    }

    // ------------------------------------------------------------------ use / equip / drop

    public bool UseItem(int slotIndex)
    {
        var s = GetSlot(slotIndex);
        if (s == null || s.IsEmpty) return false;

        var item = s.item;
        if (!item.IsUsable)
        {
            Debug.LogWarning($"[Inventory] {item.displayName} is not usable.");
            return false;
        }
        if (_combat == null || !_combat.IsSpawned || _combat.IsDead)
        {
            Debug.LogWarning("[Inventory] Cannot use items right now (dead or not spawned).");
            return false;
        }

        if (item.healAmount > 0)
        {
            if (_combat.NetworkHealth >= _combat.MaxHealth)
            {
                Debug.LogWarning("[Inventory] Health is already full - potion not consumed.");
                return false;
            }
            _combat.HealSelf(item.healAmount);
        }

        Debug.Log($"[Inventory] Used {item.displayName}.");
        RemoveItem(item, 1);
        return true;
    }

    /// <summary>Toggles equip state for a Weapon/Equipment slot.</summary>
    public bool EquipItem(int slotIndex)
    {
        var s = GetSlot(slotIndex);
        if (s == null || s.IsEmpty) return false;

        var item = s.item;
        if (!item.IsEquippable)
        {
            Debug.LogWarning($"[Inventory] {item.displayName} cannot be equipped.");
            return false;
        }

        if (item.type == ItemType.Weapon)
            EquippedWeaponId = EquippedWeaponId == item.itemId ? null : item.itemId;
        else
            EquippedEquipmentId = EquippedEquipmentId == item.itemId ? null : item.itemId;

        Debug.Log($"[Inventory] {(IsEquipped(item) ? "Equipped" : "Unequipped")} {item.displayName}.");
        RecomputeBonus();
        Changed?.Invoke();
        return true;
    }

    public bool IsEquipped(ItemData item)
    {
        return item != null && (item.itemId == EquippedWeaponId || item.itemId == EquippedEquipmentId);
    }

    /// <summary>Drops the whole stack in front of the player as a networked WorldItem.</summary>
    public bool DropItem(int slotIndex)
    {
        var s = GetSlot(slotIndex);
        if (s == null || s.IsEmpty) return false;
        if (!s.item.droppable)
        {
            Debug.LogWarning($"[Inventory] {s.item.displayName} cannot be dropped.");
            return false;
        }

        Vector3 pos = transform.position + transform.forward * 1.5f;
        pos.y = 0.4f;

        var item = s.item;
        int qty = s.quantity;
        if (SpawnWorldItem(item, qty, pos) == null)
        {
            Debug.LogWarning("[Inventory] Drop failed - item kept.");
            return false;
        }

        Debug.Log($"[Inventory] Dropped {qty}x {item.displayName}.");
        RemoveItem(item, qty);
        return true;
    }

    public WorldItem SpawnWorldItem(ItemData item, int quantity, Vector3 position)
    {
        if (worldItemPrefab == null)
        {
            Debug.LogWarning("[Inventory] worldItemPrefab is not assigned - cannot spawn world items.");
            return null;
        }
        return WorldItem.Spawn(_combat != null ? _combat.Runner : null, worldItemPrefab, position, item, quantity);
    }

    public void AddExperience(int amount)
    {
        if (amount <= 0) return;
        Experience += amount;
        Debug.Log($"[Inventory] +{amount} EXP (total {Experience}).");
        Changed?.Invoke();
    }

    // ------------------------------------------------------------------ helpers

    private InventorySlot GetSlot(int index)
    {
        if (index < 0 || index >= _slots.Count) return null;
        return _slots[index];
    }

    /// <summary>Unequips anything that no longer exists in the inventory.</summary>
    private void ValidateEquipment()
    {
        if (EquippedWeaponId != null && !Has(EquippedWeaponId)) { Debug.Log("[Inventory] Equipped weapon left the inventory - unequipped."); EquippedWeaponId = null; }
        if (EquippedEquipmentId != null && !Has(EquippedEquipmentId)) { Debug.Log("[Inventory] Equipped gear left the inventory - unequipped."); EquippedEquipmentId = null; }
        RecomputeBonus();
    }

    private bool Has(string itemId)
    {
        foreach (var s in _slots) if (!s.IsEmpty && s.item.itemId == itemId) return true;
        return false;
    }

    private void RecomputeBonus()
    {
        int bonus = 0;
        var w = ItemRegistry.Get(EquippedWeaponId);
        var e = ItemRegistry.Get(EquippedEquipmentId);
        if (w != null) bonus += w.damageBonus;
        if (e != null) bonus += e.damageBonus;

        if (_combat != null) _combat.WeaponDamageBonus = bonus;
        Debug.Log($"[Inventory] Gear damage bonus is now +{bonus}.");
    }
}
