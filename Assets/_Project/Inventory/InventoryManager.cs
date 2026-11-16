using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-player inventory and 4-slot equipment manager (Sword, Shield, Armor, Pants).
/// Aggregates equipped gear stats and pushes them to PlayerCombat / PlayerCapsule for networked stat calculation.
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
    private PlayerCapsule _capsule;
    private InventoryUI _ui;
    private bool _activated;

    public IReadOnlyList<InventorySlot> Slots => _slots;
    public int Capacity => capacity;
    public int Experience => _combat != null && _combat.IsSpawned ? _combat.NetworkExperience : _localExpFallback;
    public int Level => _combat != null && _combat.IsSpawned ? _combat.NetworkLevel : 1;
    public int UnspentStatPoints => _combat != null && _combat.IsSpawned ? _combat.NetworkUnspentStatPoints : 0;

    // 4 Equippable Gear Slots: Sword (Weapon), Shield, Armor (Chestplate), Pants (Greaves)
    public string EquippedWeaponId { get; private set; }
    public string EquippedShieldId { get; private set; }
    public string EquippedArmorId { get; private set; }
    public string EquippedPantsId { get; private set; }
    public string EquippedEquipmentId => EquippedArmorId;

    // Aggregated gear bonuses
    public int GearDamageBonus { get; private set; }
    public int GearMaxHealthBonus { get; private set; }
    public int GearMaxStaminaBonus { get; private set; }
    public int GearDefenseBonus { get; private set; }
    public float GearMoveSpeedBonus { get; private set; }
    public float GearCritChanceBonus { get; private set; }

    public WorldItem WorldItemPrefab => worldItemPrefab;

    /// <summary>Raised whenever slots, equipment, level, or experience change.</summary>
    public event Action Changed;

    private int _localExpFallback;

    // ------------------------------------------------------------------ lifecycle

    private void Awake()
    {
        _combat = GetComponent<PlayerCombat>();
        _capsule = GetComponent<PlayerCapsule>();
        for (int i = 0; i < capacity; i++) _slots.Add(new InventorySlot());
    }

    private void Update()
    {
        if (_activated || _combat == null) return;
        if (!_combat.IsSpawned || !_combat.IsLocallyControlled) return;

        _activated = true;
        Local = this;
        _combat.ProgressionChanged += OnProgressionChanged;
        Debug.Log($"[Inventory] Activated for local player '{name}' ({capacity} slots).");
        if (createUi) _ui = InventoryUI.Create(this);
    }

    private void OnDestroy()
    {
        if (_combat != null) _combat.ProgressionChanged -= OnProgressionChanged;
        if (Local == this) Local = null;
        if (_ui != null) Destroy(_ui.gameObject);
    }

    private void OnProgressionChanged()
    {
        Changed?.Invoke();
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
            Debug.Log($"[Inventory] +{added}x {item.displayName} [{ItemData.GetTierDisplayName(item.tier)}]" + (remaining > 0 ? $" ({remaining} did not fit)" : ""));
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

    // ------------------------------------------------------------------ use / equip / unequip / drop

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

        bool appliedAny = false;

        if (item.healAmount > 0)
        {
            if (_combat.NetworkHealth < _combat.MaxHealth)
            {
                _combat.HealSelf(item.healAmount);
                appliedAny = true;
            }
            else if (item.staminaRestoreAmount <= 0 && item.attackBuffBonus <= 0)
            {
                Debug.LogWarning("[Inventory] Health is already full - potion not consumed.");
                return false;
            }
        }

        if (item.staminaRestoreAmount > 0)
        {
            _combat.RestoreStamina(item.staminaRestoreAmount);
            appliedAny = true;
        }

        if (item.attackBuffBonus > 0)
        {
            float duration = item.attackBuffDuration > 0f ? item.attackBuffDuration : 30f;
            _combat.ApplyAttackBuff(item.attackBuffBonus, duration);
            appliedAny = true;
        }

        if (!appliedAny) return false;

        Debug.Log($"[Inventory] Used {item.displayName}.");
        RemoveItem(item, 1);
        return true;
    }

    /// <summary>Toggles or swaps equip state for a Sword, Shield, Armor, or Pants slot by inventory slot index.</summary>
    public bool EquipItem(int slotIndex)
    {
        var s = GetSlot(slotIndex);
        if (s == null || s.IsEmpty) return false;
        return EquipItem(s.item);
    }

    /// <summary>Equips or unequips the given ItemData into its matching EquipmentSlotType (Sword, Shield, Armor, Pants).</summary>
    public bool EquipItem(ItemData item)
    {
        if (item == null || !item.IsEquippable)
        {
            if (item != null) Debug.LogWarning($"[Inventory] {item.displayName} cannot be equipped.");
            return false;
        }

        EquipmentSlotType slotType = item.GetEquipmentSlot();
        switch (slotType)
        {
            case EquipmentSlotType.Sword:
                EquippedWeaponId = EquippedWeaponId == item.itemId ? null : item.itemId;
                break;
            case EquipmentSlotType.Shield:
                EquippedShieldId = EquippedShieldId == item.itemId ? null : item.itemId;
                break;
            case EquipmentSlotType.Armor:
                EquippedArmorId = EquippedArmorId == item.itemId ? null : item.itemId;
                break;
            case EquipmentSlotType.Pants:
                EquippedPantsId = EquippedPantsId == item.itemId ? null : item.itemId;
                break;
            default:
                return false;
        }

        Debug.Log($"[Inventory] {(IsEquipped(item) ? "Equipped" : "Unequipped")} {item.displayName} in slot {slotType}.");
        RecomputeBonus();
        Changed?.Invoke();
        return true;
    }

    /// <summary>Cleanly unequips whatever item is currently in the specified gear slot.</summary>
    public bool UnequipSlot(EquipmentSlotType slotType)
    {
        bool changed = false;
        switch (slotType)
        {
            case EquipmentSlotType.Sword:
                if (EquippedWeaponId != null) { EquippedWeaponId = null; changed = true; }
                break;
            case EquipmentSlotType.Shield:
                if (EquippedShieldId != null) { EquippedShieldId = null; changed = true; }
                break;
            case EquipmentSlotType.Armor:
                if (EquippedArmorId != null) { EquippedArmorId = null; changed = true; }
                break;
            case EquipmentSlotType.Pants:
                if (EquippedPantsId != null) { EquippedPantsId = null; changed = true; }
                break;
        }

        if (changed)
        {
            Debug.Log($"[Inventory] Unequipped slot {slotType}.");
            RecomputeBonus();
            Changed?.Invoke();
        }
        return changed;
    }

    public ItemData GetEquippedItem(EquipmentSlotType slotType)
    {
        return slotType switch
        {
            EquipmentSlotType.Sword => ItemRegistry.Get(EquippedWeaponId),
            EquipmentSlotType.Shield => ItemRegistry.Get(EquippedShieldId),
            EquipmentSlotType.Armor => ItemRegistry.Get(EquippedArmorId),
            EquipmentSlotType.Pants => ItemRegistry.Get(EquippedPantsId),
            _ => null
        };
    }

    public bool IsEquipped(ItemData item)
    {
        if (item == null || string.IsNullOrEmpty(item.itemId)) return false;
        return item.itemId == EquippedWeaponId ||
               item.itemId == EquippedShieldId ||
               item.itemId == EquippedArmorId ||
               item.itemId == EquippedPantsId;
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
        _localExpFallback += amount;
        if (_combat != null && _combat.IsSpawned)
        {
            _combat.AddExperience(amount);
        }
        Debug.Log($"[Inventory] +{amount} EXP (Level {Level}, EXP {Experience}).");
        Changed?.Invoke();
    }

    // ------------------------------------------------------------------ helpers

    private InventorySlot GetSlot(int index)
    {
        if (index < 0 || index >= _slots.Count) return null;
        return _slots[index];
    }

    private void ValidateEquipment()
    {
        if (EquippedWeaponId != null && !Has(EquippedWeaponId)) { EquippedWeaponId = null; }
        if (EquippedShieldId != null && !Has(EquippedShieldId)) { EquippedShieldId = null; }
        if (EquippedArmorId != null && !Has(EquippedArmorId)) { EquippedArmorId = null; }
        if (EquippedPantsId != null && !Has(EquippedPantsId)) { EquippedPantsId = null; }
        RecomputeBonus();
    }

    private bool Has(string itemId)
    {
        foreach (var s in _slots) if (!s.IsEmpty && s.item.itemId == itemId) return true;
        return false;
    }

    public void RecomputeBonus()
    {
        int dmg = 0, hp = 0, sta = 0, def = 0;
        float spd = 0f, crit = 0f;

        AccumulateItemStats(ItemRegistry.Get(EquippedWeaponId), ref dmg, ref hp, ref sta, ref def, ref spd, ref crit);
        AccumulateItemStats(ItemRegistry.Get(EquippedShieldId), ref dmg, ref hp, ref sta, ref def, ref spd, ref crit);
        AccumulateItemStats(ItemRegistry.Get(EquippedArmorId), ref dmg, ref hp, ref sta, ref def, ref spd, ref crit);
        AccumulateItemStats(ItemRegistry.Get(EquippedPantsId), ref dmg, ref hp, ref sta, ref def, ref spd, ref crit);

        GearDamageBonus = dmg;
        GearMaxHealthBonus = hp;
        GearMaxStaminaBonus = sta;
        GearDefenseBonus = def;
        GearMoveSpeedBonus = spd;
        GearCritChanceBonus = crit;

        if (_combat != null)
        {
            _combat.ApplyEquipmentBonuses(dmg, hp, sta, def, spd, crit);
        }

        Debug.Log($"[Inventory] Recomputed Gear Bonuses -> ATK +{dmg}, HP +{hp}, STA +{sta}, DEF +{def}, SPD +{spd:0.00}, CRIT +{crit * 100f:0}%.");
    }

    private static void AccumulateItemStats(
        ItemData item,
        ref int dmg,
        ref int hp,
        ref int sta,
        ref int def,
        ref float spd,
        ref float crit)
    {
        if (item == null) return;
        dmg += item.damageBonus;
        hp += item.maxHealthBonus;
        sta += item.maxStaminaBonus;
        def += item.defenseBonus;
        spd += item.moveSpeedBonus;
        crit += item.critChanceBonus;
    }
}
