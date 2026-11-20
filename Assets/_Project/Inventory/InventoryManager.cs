using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks an exact equipped item instance and its +Enhancement Level for each gear slot.
/// </summary>
[Serializable]
public class EquippedGearRef
{
    public EquipmentSlotType slotType;
    public string instanceId;
    public string itemId;
    public int enhancementLevel;

    public bool IsEmpty => string.IsNullOrEmpty(itemId);

    public ItemData ResolveItem() => string.IsNullOrEmpty(itemId) ? null : ItemRegistry.Get(itemId);

    public void Clear()
    {
        instanceId = null;
        itemId = null;
        enhancementLevel = 0;
    }
}

/// <summary>
/// Per-player inventory &amp; 4-slot equipment manager with exact slotIndex operations,
/// Drag-and-Drop reordering, and PlayerSaveManager hooks.
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
    private readonly Dictionary<EquipmentSlotType, EquippedGearRef> _equipped = new Dictionary<EquipmentSlotType, EquippedGearRef>
    {
        { EquipmentSlotType.Sword, new EquippedGearRef { slotType = EquipmentSlotType.Sword } },
        { EquipmentSlotType.Shield, new EquippedGearRef { slotType = EquipmentSlotType.Shield } },
        { EquipmentSlotType.Armor, new EquippedGearRef { slotType = EquipmentSlotType.Armor } },
        { EquipmentSlotType.Pants, new EquippedGearRef { slotType = EquipmentSlotType.Pants } }
    };

    private PlayerCombat _combat;
    private InventoryUI _ui;
    private bool _activated;

    public bool IsActivated => _activated;
    public IReadOnlyList<InventorySlot> Slots => _slots;
    public int Capacity => capacity;
    public int Experience => _combat != null && _combat.IsSpawned ? _combat.NetworkExperience : _localExpFallback;
    public int Level => _combat != null && _combat.IsSpawned ? _combat.NetworkLevel : 1;
    public int UnspentStatPoints => _combat != null && _combat.IsSpawned ? _combat.NetworkUnspentStatPoints : 0;
    public int Gold => _combat != null && _combat.IsSpawned ? _combat.NetworkGold : 0;

    public string EquippedWeaponId => _equipped[EquipmentSlotType.Sword].itemId;
    public string EquippedShieldId => _equipped[EquipmentSlotType.Shield].itemId;
    public string EquippedArmorId => _equipped[EquipmentSlotType.Armor].itemId;
    public string EquippedPantsId => _equipped[EquipmentSlotType.Pants].itemId;
    public string EquippedEquipmentId => EquippedArmorId;

    // Aggregated gear bonuses
    public int GearDamageBonus { get; private set; }
    public int GearMaxHealthBonus { get; private set; }
    public int GearMaxStaminaBonus { get; private set; }
    public int GearDefenseBonus { get; private set; }
    public float GearMoveSpeedBonus { get; private set; }
    public float GearCritChanceBonus { get; private set; }

    public WorldItem WorldItemPrefab => worldItemPrefab;

    /// <summary>Raised whenever slots, equipment, currency, enhancement, or experience change.</summary>
    public event Action Changed;

    private int _localExpFallback;

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
        _combat.ProgressionChanged += OnProgressionChanged;
        PlayerSaveManager.EnsureExists();
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

    // ------------------------------------------------------------------ add / remove (exact & stacked)

    public bool CanAdd(ItemData item, int quantity)
    {
        if (item == null || quantity <= 0) return false;

        int room = 0;
        int max = item.MaxStackSize;
        foreach (var s in _slots)
        {
            if (s.IsEmpty) room += max;
            else if (s.item == item && s.enhancementLevel == 0) room += s.FreeSpace;
            if (room >= quantity) return true;
        }
        return room >= quantity;
    }

    /// <summary>Adds items to existing un-enhanced stacks or empty slots. Returns quantity that did NOT fit.</summary>
    public int AddItem(ItemData item, int quantity, int enhancementLevel = 0)
    {
        if (item == null || quantity <= 0)
        {
            Debug.LogWarning("[Inventory] AddItem ignored: null item or non-positive quantity.");
            return Mathf.Max(0, quantity);
        }

        int remaining = quantity;
        int max = item.MaxStackSize;

        if (item.stackable && enhancementLevel == 0)
        {
            foreach (var s in _slots)
            {
                if (remaining <= 0) break;
                if (s.IsEmpty || s.item != item || s.enhancementLevel != 0 || s.FreeSpace <= 0) continue;
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
            s.Set(item, add, enhancementLevel);
            remaining -= add;
        }

        int added = quantity - remaining;
        if (added > 0)
        {
            Debug.Log($"[Inventory] +{added}x {item.displayName}{(enhancementLevel > 0 ? $" +{enhancementLevel}" : "")} [{ItemData.GetTierDisplayName(item.tier)}]");
            Changed?.Invoke();
            PlayerSaveManager.RequestAutoSave("AddItem");
        }
        else
        {
            Debug.LogWarning($"[Inventory] Inventory full: could not add {item.displayName}.");
        }

        return remaining;
    }

    /// <summary>Adds an item directly into a specific slotIndex if empty (used by tests or exact placement).</summary>
    public bool AddItemAtSlot(int slotIndex, ItemData item, int quantity = 1, int enhancementLevel = 0)
    {
        var s = GetSlot(slotIndex);
        if (s == null || !s.IsEmpty || item == null || quantity <= 0) return false;
        s.Set(item, Mathf.Min(item.MaxStackSize, quantity), enhancementLevel);
        Changed?.Invoke();
        PlayerSaveManager.RequestAutoSave("AddItemAtSlot");
        return true;
    }

    /// <summary>Consumes quantity STRICTLY from the given slotIndex (never scans from slot 0).</summary>
    public int ConsumeSlotAtExactIndex(int slotIndex, int quantity = 1)
    {
        var s = GetSlot(slotIndex);
        if (s == null || s.IsEmpty || quantity <= 0) return 0;

        int take = Mathf.Min(s.quantity, quantity);
        string consumedInstanceId = s.instanceId;
        string itemName = s.item.displayName;

        s.quantity -= take;
        if (s.quantity <= 0)
        {
            s.Clear();
            UnequipByInstanceId(consumedInstanceId);
        }

        Debug.Log($"[Inventory] Slot {slotIndex}: consumed {take}x {itemName}.");
        RecomputeBonus();
        Changed?.Invoke();
        PlayerSaveManager.RequestAutoSave("ConsumeExactSlot");
        return take;
    }

    public int Count(ItemData item)
    {
        int n = 0;
        foreach (var s in _slots) if (!s.IsEmpty && s.item == item) n += s.quantity;
        return n;
    }

    // ------------------------------------------------------------------ Drag-and-Drop Reordering

    /// <summary>Moves, merges (if un-enhanced stackable), or swaps two exact inventory slots.</summary>
    public bool SwapOrMoveSlots(int fromIndex, int toIndex)
    {
        if (fromIndex == toIndex) return false;
        var fromSlot = GetSlot(fromIndex);
        var toSlot = GetSlot(toIndex);
        if (fromSlot == null || toSlot == null || fromSlot.IsEmpty) return false;

        // Merge un-enhanced identical stackables
        if (!toSlot.IsEmpty && fromSlot.item == toSlot.item && fromSlot.item.stackable &&
            fromSlot.enhancementLevel == 0 && toSlot.enhancementLevel == 0 && toSlot.FreeSpace > 0)
        {
            int move = Mathf.Min(fromSlot.quantity, toSlot.FreeSpace);
            toSlot.quantity += move;
            fromSlot.quantity -= move;
            if (fromSlot.quantity <= 0) fromSlot.Clear();
        }
        else
        {
            var temp = new InventorySlot();
            temp.CopyFrom(toSlot);
            toSlot.CopyFrom(fromSlot);
            fromSlot.CopyFrom(temp);
        }

        Debug.Log($"[Inventory] Moved/Swapped slot {fromIndex} <-> slot {toIndex}.");
        Changed?.Invoke();
        PlayerSaveManager.RequestAutoSave("DragSwapSlots");
        return true;
    }

    /// <summary>Equips an item dragged directly from inventory slotIndex onto an Equipment slot card.</summary>
    public bool EquipFromDrag(int fromSlotIndex, EquipmentSlotType targetSlotType)
    {
        var s = GetSlot(fromSlotIndex);
        if (s == null || s.IsEmpty || !s.item.IsEquippable) return false;
        if (s.item.GetEquipmentSlot() != targetSlotType) return false;
        return EquipSlotByIndex(fromSlotIndex, toggleOffIfAlreadyEquipped: false);
    }

    // ------------------------------------------------------------------ Exact-Slot Use / Equip / Drop

    public bool UseItem(int slotIndex)
    {
        var s = GetSlot(slotIndex);
        if (s == null || s.IsEmpty) return false;

        var item = s.item;
        if (!item.IsUsable)
        {
            Debug.LogWarning($"[Inventory] Slot {slotIndex} ({item.displayName}) is not usable.");
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
                MMOHealingPotionVFX.UseHealPotion();
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
            MMOStaminaPotionVFX.UseStaminaPotion();
            appliedAny = true;
        }

        if (item.attackBuffBonus > 0)
        {
            float duration = item.attackBuffDuration > 0f ? item.attackBuffDuration : 30f;
            _combat.ApplyAttackBuff(item.attackBuffBonus, duration);
            appliedAny = true;
        }

        if (!appliedAny) return false;

        Debug.Log($"[Inventory] Used item strictly at slot {slotIndex}: {item.displayName}.");
        ConsumeSlotAtExactIndex(slotIndex, 1);
        return true;
    }

    /// <summary>Equips or toggles equip strictly on the exact slotIndex clicked.</summary>
    public bool EquipItem(int slotIndex)
    {
        return EquipSlotByIndex(slotIndex, toggleOffIfAlreadyEquipped: true);
    }

    private bool EquipSlotByIndex(int slotIndex, bool toggleOffIfAlreadyEquipped)
    {
        var s = GetSlot(slotIndex);
        if (s == null || s.IsEmpty) return false;
        s.EnsureInstanceId();

        var item = s.item;
        if (!item.IsEquippable)
        {
            Debug.LogWarning($"[Inventory] Slot {slotIndex} ({item.displayName}) cannot be equipped.");
            return false;
        }

        EquipmentSlotType slotType = item.GetEquipmentSlot();
        var eqRef = _equipped[slotType];

        if (toggleOffIfAlreadyEquipped && eqRef.instanceId == s.instanceId)
        {
            eqRef.Clear();
            Debug.Log($"[Inventory] Unequipped slot {slotIndex} ({item.displayName} +{s.enhancementLevel}).");
        }
        else
        {
            eqRef.slotType = slotType;
            eqRef.instanceId = s.instanceId;
            eqRef.itemId = item.itemId;
            eqRef.enhancementLevel = s.enhancementLevel;
            Debug.Log($"[Inventory] Equipped EXACT slot {slotIndex}: {item.displayName} +{s.enhancementLevel} (instance={s.instanceId}).");
        }

        RecomputeBonus();
        Changed?.Invoke();
        PlayerSaveManager.RequestAutoSave("EquipItem");
        return true;
    }

    /// <summary>Convenience overload when equipping by ItemData directly (finds matching slot index).</summary>
    public bool EquipItem(ItemData item)
    {
        if (item == null || !item.IsEquippable) return false;
        for (int i = 0; i < _slots.Count; i++)
        {
            if (!_slots[i].IsEmpty && _slots[i].item == item)
                return EquipSlotByIndex(i, toggleOffIfAlreadyEquipped: false);
        }
        return false;
    }

    public bool UnequipSlot(EquipmentSlotType slotType)
    {
        if (!_equipped.TryGetValue(slotType, out EquippedGearRef eq) || eq.IsEmpty) return false;
        eq.Clear();
        Debug.Log($"[Inventory] Unequipped gear slot {slotType}.");
        RecomputeBonus();
        Changed?.Invoke();
        PlayerSaveManager.RequestAutoSave("UnequipSlot");
        return true;
    }

    private void UnequipByInstanceId(string instanceId)
    {
        if (string.IsNullOrEmpty(instanceId)) return;
        foreach (var kv in _equipped)
        {
            if (kv.Value.instanceId == instanceId)
                kv.Value.Clear();
        }
    }

    public EquippedGearRef GetEquippedRef(EquipmentSlotType slotType)
    {
        return _equipped.TryGetValue(slotType, out var eq) ? eq : null;
    }

    public ItemData GetEquippedItem(EquipmentSlotType slotType)
    {
        return _equipped.TryGetValue(slotType, out var eq) ? eq.ResolveItem() : null;
    }

    public int GetEquippedEnhancementLevel(EquipmentSlotType slotType)
    {
        return _equipped.TryGetValue(slotType, out var eq) && !eq.IsEmpty ? eq.enhancementLevel : 0;
    }

    public bool IsSlotEquipped(int slotIndex)
    {
        var s = GetSlot(slotIndex);
        if (s == null || s.IsEmpty) return false;
        if (string.IsNullOrEmpty(s.instanceId)) return false;
        EquipmentSlotType slotType = s.item.GetEquipmentSlot();
        if (slotType == EquipmentSlotType.None) return false;
        return _equipped.TryGetValue(slotType, out var eq) && eq.instanceId == s.instanceId;
    }

    public bool IsEquipped(ItemData item)
    {
        if (item == null) return false;
        foreach (var kv in _equipped)
        {
            if (!kv.Value.IsEmpty && kv.Value.itemId == item.itemId) return true;
        }
        return false;
    }

    /// <summary>Drops strictly the exact slotIndex clicked (never searches from slot 0).</summary>
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

        Debug.Log($"[Inventory] Dropped strictly slot {slotIndex}: {qty}x {item.displayName}.");
        ConsumeSlotAtExactIndex(slotIndex, qty);
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

    public int FindSlotIndexByInstanceId(string instanceId)
    {
        if (string.IsNullOrEmpty(instanceId)) return -1;
        for (int i = 0; i < _slots.Count; i++)
        {
            if (!_slots[i].IsEmpty && _slots[i].instanceId == instanceId) return i;
        }
        return -1;
    }

    // ------------------------------------------------------------------ EXP & Gold

    public void AddExperience(int amount)
    {
        if (amount <= 0) return;
        _localExpFallback += amount;
        if (_combat != null && _combat.IsSpawned)
            _combat.AddExperience(amount);
        Changed?.Invoke();
    }

    public void AddGold(int amount, string source = "Reward")
    {
        if (_combat != null && _combat.IsSpawned)
            _combat.AddGold(amount, source);
    }

    // ------------------------------------------------------------------ Persistence Export / Restore

    public void ExportSaveData(PlayerSaveData data)
    {
        if (data == null) return;
        data.inventorySlots.Clear();
        for (int i = 0; i < _slots.Count; i++)
        {
            var s = _slots[i];
            if (s.IsEmpty || s.item == null) continue;
            s.EnsureInstanceId();
            data.inventorySlots.Add(new SavedInventorySlotData
            {
                slotIndex = i,
                instanceId = s.instanceId,
                itemId = s.item.itemId,
                quantity = s.quantity,
                tier = (int)s.item.tier,
                enhancementLevel = s.enhancementLevel
            });
        }

        data.equippedSlots.Clear();
        foreach (var kv in _equipped)
        {
            if (kv.Value.IsEmpty) continue;
            var resolved = kv.Value.ResolveItem();
            data.equippedSlots.Add(new SavedEquippedSlotData
            {
                slotType = (int)kv.Key,
                slotName = kv.Key == EquipmentSlotType.Sword ? "Weapon" : kv.Key.ToString(),
                instanceId = kv.Value.instanceId,
                itemId = kv.Value.itemId,
                tier = resolved != null ? (int)resolved.tier : 0,
                enhancementLevel = kv.Value.enhancementLevel
            });
        }
    }

    public void RestoreSaveData(PlayerSaveData data)
    {
        if (data == null) return;

        for (int i = 0; i < _slots.Count; i++) _slots[i].Clear();
        foreach (var kv in _equipped) kv.Value.Clear();

        if (data.inventorySlots != null)
        {
            foreach (var saved in data.inventorySlots)
            {
                if (saved == null || saved.slotIndex < 0 || saved.slotIndex >= _slots.Count) continue;
                var item = ItemRegistry.Get(saved.itemId);
                if (item == null || saved.quantity <= 0) continue;
                _slots[saved.slotIndex].Set(item, saved.quantity, saved.enhancementLevel, saved.instanceId);
            }
        }

        if (data.equippedSlots != null)
        {
            foreach (var savedEq in data.equippedSlots)
            {
                if (savedEq == null) continue;
                var slotType = (EquipmentSlotType)savedEq.slotType;
                if (!_equipped.TryGetValue(slotType, out var eqRef)) continue;
                var item = ItemRegistry.Get(savedEq.itemId);
                if (item == null) continue;

                eqRef.slotType = slotType;
                eqRef.instanceId = savedEq.instanceId;
                eqRef.itemId = savedEq.itemId;
                eqRef.enhancementLevel = savedEq.enhancementLevel;
            }
        }

        RecomputeBonus();
        Changed?.Invoke();
    }

    // ------------------------------------------------------------------ helpers

    public InventorySlot GetSlot(int index)
    {
        if (index < 0 || index >= _slots.Count) return null;
        return _slots[index];
    }

    public void RecomputeBonus()
    {
        int dmg = 0, hp = 0, sta = 0, def = 0;
        float spd = 0f, crit = 0f;

        foreach (var kv in _equipped)
        {
            var eq = kv.Value;
            if (eq == null || eq.IsEmpty) continue;
            var item = eq.ResolveItem();
            if (item == null) continue;

            int enh = eq.enhancementLevel;
            dmg += item.GetEnhancedDamage(enh);
            hp += item.GetEnhancedMaxHealth(enh);
            sta += item.GetEnhancedMaxStamina(enh);
            def += item.GetEnhancedDefense(enh);
            spd += item.GetEnhancedMoveSpeed(enh);
            crit += item.GetEnhancedCritChance(enh);
        }

        GearDamageBonus = dmg;
        GearMaxHealthBonus = hp;
        GearMaxStaminaBonus = sta;
        GearDefenseBonus = def;
        GearMoveSpeedBonus = spd;
        GearCritChanceBonus = crit;

        if (_combat != null)
            _combat.ApplyEquipmentBonuses(dmg, hp, sta, def, spd, crit);

        Debug.Log($"[Inventory] Gear Totals -> ATK +{dmg}, HP +{hp}, STA +{sta}, DEF +{def}, SPD +{spd:0.00}, CRIT +{crit * 100f:0}%.");
    }

    public void ClearSlotsForTest()
    {
        for (int i = 0; i < _slots.Count; i++) _slots[i].Clear();
        foreach (var kv in _equipped) kv.Value.Clear();
        RecomputeBonus();
        Changed?.Invoke();
    }

    public void SetSlotDirectForTest(int index, ItemData item, int qty, int enhancement = 0)
    {
        if (index < 0 || index >= _slots.Count) return;
        _slots[index].Set(item, qty, enhancement);
        Changed?.Invoke();
    }
}
