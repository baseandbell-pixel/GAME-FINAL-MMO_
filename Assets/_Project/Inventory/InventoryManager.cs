using System;
using System.Collections.Generic;
using Fusion;
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

public struct EnhanceResult
{
    public bool attempted;
    public bool succeeded;
    public int previousLevel;
    public int newLevel;
    public int goldSpent;
    public string message;
}

/// <summary>
/// Per-player inventory &amp; 4-slot equipment manager with exact slotIndex operations,
/// Drag-and-Drop reordering, Equipment Enhancement (+0 to +10), and PlayerSaveManager hooks.
/// </summary>
[RequireComponent(typeof(PlayerCombat))]
public class InventoryManager : MonoBehaviour
{
    public static InventoryManager Local { get; private set; }

    // ความจุกระเป๋ามาตรฐานขยายเป็น 48 ช่อง (รองรับอุปกรณ์และยาจำนวนมากโดยไม่กระทบไฟล์เซฟเก่า)
    public const int DefaultBagCapacity = 48;
    // จำนวนช่องอุปกรณ์สวมใส่หลัก (Sword, Shield, Armor, Pants) — สำรองช่องภายในแยกจาก 48 ช่องกระเป๋าเพื่อไม่ให้กินโควตากระเป๋า
    public const int EquipmentSlotCount = 4;

    [Min(1)] [SerializeField] private int capacity = DefaultBagCapacity;
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
    private HotbarUI _hotbarUi;
    private bool _activated;

    // จำนวนช่องปุ่มลัด Hotbar (1-6) และการผูกกับช่องกระเป๋าจริง
    public const int HotbarCapacity = 6;
    private readonly int[] _hotbarSlotIndices = { -1, -1, -1, -1, -1, -1 };
    private readonly string[] _hotbarInstanceIds = new string[HotbarCapacity];

    public bool IsActivated => _activated;
    public IReadOnlyList<InventorySlot> Slots => _slots;
    public int Capacity => capacity;

    /// <summary>จำนวนช่องไอเทมที่อยู่ในกระเป๋าจริง (ไม่นับรวมอุปกรณ์ที่สวมใส่อยู่ใน 4 ช่อง Equipment)</summary>
    public int OccupiedBagSlotCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < _slots.Count; i++)
            {
                if (!_slots[i].IsEmpty && !IsSlotEquipped(i)) n++;
            }
            return n;
        }
    }

    /// <summary>จำนวนช่องว่างที่เหลือจริงในกระเป๋าหลัก (จากความจุเต็ม 48 ช่อง)</summary>
    public int FreeBagSlotCount => Mathf.Max(0, capacity - OccupiedBagSlotCount);
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
        // บังคับความจุกระเป๋าขั้นต่ำเป็น 48 ช่อง พร้อมสำรองช่องภายในอีก 4 ช่องสำหรับอุปกรณ์ที่สวมใส่อยู่ (รวม 52 ช่องภายใน)
        // เพื่อให้อุปกรณ์ที่สวมใส่ 4 ชิ้นไม่กินโควตา 48 ช่องของกระเป๋าหลัก (แก้บั๊กแสดง 44/48 แต่ขึ้น Inventory Full)
        if (capacity < DefaultBagCapacity) capacity = DefaultBagCapacity;
        int totalInternalSlots = capacity + EquipmentSlotCount;
        for (int i = 0; i < totalInternalSlots; i++) _slots.Add(new InventorySlot());
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
        if (createUi)
        {
            _ui = InventoryUI.Create(this);
            _hotbarUi = HotbarUI.Create(this);
        }
    }

    private void OnDestroy()
    {
        if (_combat != null) _combat.ProgressionChanged -= OnProgressionChanged;
        if (Local == this) Local = null;
        if (_ui != null) Destroy(_ui.gameObject);
        if (_hotbarUi != null) Destroy(_hotbarUi.gameObject);
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

        if (item.stackable)
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                var s = _slots[i];
                if (s.IsEmpty || IsSlotEquipped(i)) continue;
                if (s.item == item && s.enhancementLevel == 0) room += s.FreeSpace;
                if (room >= quantity) return true;
            }
        }

        int emptyInternal = 0;
        for (int i = 0; i < _slots.Count; i++)
        {
            if (_slots[i].IsEmpty) emptyInternal++;
        }
        int usableEmptySlots = Mathf.Min(FreeBagSlotCount, emptyInternal);
        room += usableEmptySlots * max;
        return room >= quantity;
    }

    /// <summary>Adds items to existing un-enhanced stacks or empty slots (up to Capacity visible bag slots). Returns quantity that did NOT fit.</summary>
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
            for (int i = 0; i < _slots.Count && remaining > 0; i++)
            {
                var s = _slots[i];
                if (s.IsEmpty || IsSlotEquipped(i) || s.item != item || s.enhancementLevel != 0 || s.FreeSpace <= 0) continue;
                int add = Mathf.Min(s.FreeSpace, remaining);
                s.quantity += add;
                remaining -= add;
            }
        }

        int allowedNewSlots = FreeBagSlotCount;
        for (int i = 0; i < _slots.Count && remaining > 0 && allowedNewSlots > 0; i++)
        {
            var s = _slots[i];
            if (!s.IsEmpty) continue;
            int add = Mathf.Min(max, remaining);
            s.Set(item, add, enhancementLevel);
            remaining -= add;
            allowedNewSlots--;
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

        ValidateHotbarBindings();
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
        bool mergedCompletely = false;
        if (!toSlot.IsEmpty && fromSlot.item == toSlot.item && fromSlot.item.stackable &&
            fromSlot.enhancementLevel == 0 && toSlot.enhancementLevel == 0 && toSlot.FreeSpace > 0)
        {
            int move = Mathf.Min(fromSlot.quantity, toSlot.FreeSpace);
            toSlot.quantity += move;
            fromSlot.quantity -= move;
            if (fromSlot.quantity <= 0)
            {
                fromSlot.Clear();
                mergedCompletely = true;
            }
        }
        else
        {
            var temp = new InventorySlot();
            temp.CopyFrom(toSlot);
            toSlot.CopyFrom(fromSlot);
            fromSlot.CopyFrom(temp);
        }

        // อัปเดตตำแหน่ง Hotbar ที่ผูกไว้ให้ตามช่องที่ย้ายหรือรวมกองอัตโนมัติ
        for (int h = 0; h < HotbarCapacity; h++)
        {
            if (_hotbarSlotIndices[h] == fromIndex)
            {
                _hotbarSlotIndices[h] = toIndex;
                _hotbarInstanceIds[h] = toSlot.instanceId;
            }
            else if (!mergedCompletely && _hotbarSlotIndices[h] == toIndex)
            {
                _hotbarSlotIndices[h] = fromIndex;
                _hotbarInstanceIds[h] = fromSlot.instanceId;
            }
        }
        ValidateHotbarBindings();

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
                // ปรับสมดุลยาฟื้นเลือดให้สเกลตาม MaxHP (ฮีลอย่างน้อย 28% ของ MaxHP หรือไม่ต่ำกว่า item.healAmount) เพื่อใช้ได้จริงจนถึง Lv.30
                int effectiveHeal = Mathf.Max(item.healAmount, Mathf.RoundToInt(_combat.MaxHealth * 0.28f));
                _combat.HealSelf(effectiveHeal);
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

        // สั่งเล่นอนิเมชัน Buff บนโมเดล GreatSword และซิงก์ให้ทุกเครื่องเห็น
        (_combat as PlayerCombat)?.PlayConsumableBuffEffect();

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
            if (OccupiedBagSlotCount >= capacity)
            {
                Debug.LogWarning($"[Inventory] Cannot unequip {item.displayName}: bag is full ({OccupiedBagSlotCount}/{capacity}).");
                PlayerStatusHud.Instance?.ShowToast($"Inventory Bag is full ({capacity}/{capacity})! Free a slot before unequipping.", new Color(1f, 0.35f, 0.25f));
                return false;
            }
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
        if (OccupiedBagSlotCount >= capacity)
        {
            Debug.LogWarning($"[Inventory] Cannot unequip gear slot {slotType}: bag is full ({OccupiedBagSlotCount}/{capacity}).");
            PlayerStatusHud.Instance?.ShowToast($"Inventory Bag is full ({capacity}/{capacity})! Free a slot before unequipping.", new Color(1f, 0.35f, 0.25f));
            return false;
        }
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

    /// <summary>
    /// ทิ้งไอเทมจากช่องที่เลือกอย่างปลอดภัย (ห้ามทิ้งไอเทมที่กำลังสวมใส่ ตัดของ + เซฟไฟล์ก่อนเสก WorldItem และวางบนผิวพื้นจริง)
    /// </summary>
    public bool DropItem(int slotIndex)
    {
        var s = GetSlot(slotIndex);
        if (s == null || s.IsEmpty) return false;
        if (IsSlotEquipped(slotIndex))
        {
            Debug.LogWarning($"[Inventory] Cannot drop equipped item at slot {slotIndex} ({s.item.displayName}). Unequip first.");
            return false;
        }
        if (!s.item.droppable)
        {
            Debug.LogWarning($"[Inventory] {s.item.displayName} cannot be dropped.");
            return false;
        }

        Vector3 pos = transform.position + transform.forward * 1.5f;
        pos.y = SandboxWorldSpawner.SampleGroundY(pos, transform.position.y) + 0.4f;

        var item = s.item;
        int qty = s.quantity;
        int enh = s.enhancementLevel;
        int failStacks = s.enhanceFailStacks;
        string instId = s.instanceId;

        // ป้องกันการปั๊มของใน Shared Mode (Anti-Dupe Policy): ผูกสิทธิ์ไอเทมที่โยนลงพื้นให้เก็บได้เฉพาะเจ้าของเดิม (Personal-Only)
        PlayerRef ownerRef = default;
        if (_combat != null && _combat.IsSpawned)
        {
            if (_combat.Object != null && _combat.Object.IsValid && _combat.Object.StateAuthority != PlayerRef.None)
                ownerRef = _combat.Object.StateAuthority;
            else if (_combat.Runner != null && _combat.Runner.IsRunning)
                ownerRef = _combat.Runner.LocalPlayer;
        }

        ConsumeSlotAtExactIndex(slotIndex, qty);

        if (SpawnWorldItem(item, qty, pos, enh, allowedLooter: ownerRef, isPlayerDropped: true) == null)
        {
            var rollbackSlot = GetSlot(slotIndex);
            if (rollbackSlot != null && rollbackSlot.IsEmpty)
            {
                rollbackSlot.Set(item, qty, enh, instId, failStacks);
                RecomputeBonus();
                Changed?.Invoke();
                PlayerSaveManager.RequestAutoSave("DropItemRollback");
            }
            Debug.LogWarning("[Inventory] Drop failed - item restored safely to original slot.");
            return false;
        }

        PlayerStatusHud.Instance?.ShowToast($"Dropped {qty}x {item.displayName} (Personal-Only — disappears in 30s)", new Color(0.95f, 0.78f, 0.36f), 2.5f);
        Debug.Log($"[Inventory] Dropped strictly slot {slotIndex}: {qty}x {item.displayName} (Personal-Only owner={ownerRef}).");
        return true;
    }

    /// <summary>
    /// สร้างไอเทมตกบนพื้นในโลก (รองรับระดับตีบวก, เจ้าของไอเทมส่วนตัว, และตัวนับถอยหลัง 30 วินาทีเมื่อผู้เล่นโยนทิ้ง)
    /// </summary>
    public WorldItem SpawnWorldItem(ItemData item, int quantity, Vector3 position, int enhancementLevel = 0, PlayerRef allowedLooter = default, bool isPlayerDropped = false)
    {
        if (worldItemPrefab == null)
        {
            Debug.LogWarning("[Inventory] worldItemPrefab is not assigned - cannot spawn world items.");
            return null;
        }
        return WorldItem.Spawn(_combat != null ? _combat.Runner : null, worldItemPrefab, position, item, quantity, allowedLooter, isPlayerDropped, enhancementLevel);
    }

    // ------------------------------------------------------------------ Equipment Enhancement (+0 to +10)

    /// <summary>
    /// Enhances a target gear slot (+0 -> +10) by consuming Gold and explicitly chosen Fodder inventory slot indices.
    /// </summary>
    public EnhanceResult TryEnhanceItem(int targetInventorySlotIndex, List<int> fodderSlotIndices, float? forcedRoll01 = null)
    {
        var targetSlot = GetSlot(targetInventorySlotIndex);
        if (targetSlot == null || targetSlot.IsEmpty || targetSlot.item == null || !targetSlot.item.IsEquippable)
        {
            return new EnhanceResult { attempted = false, message = "Select valid equipment to enhance." };
        }
        if (targetSlot.enhancementLevel >= EnhancementSystem.MaxEnhancementLevel)
        {
            return new EnhanceResult { attempted = false, message = $"Already at maximum +{EnhancementSystem.MaxEnhancementLevel}." };
        }
        if (_combat == null || !_combat.IsSpawned)
        {
            return new EnhanceResult { attempted = false, message = "Player not ready." };
        }

        targetSlot.EnsureInstanceId();
        var spec = EnhancementSystem.GetNextSpec(targetSlot.item, targetSlot.enhancementLevel);

        if (_combat.NetworkGold < spec.goldCost)
        {
            return new EnhanceResult { attempted = false, message = $"Need {spec.goldCost} Gold (have {_combat.NetworkGold})." };
        }

        // Validate distinct fodder slots (พร้อมป้องกันการใช้อุปกรณ์ที่กำลังสวมใส่อยู่มาเป็นวัตถุดิบย่อยตีบวก)
        var uniqueFodder = new List<int>();
        if (fodderSlotIndices != null)
        {
            for (int i = 0; i < fodderSlotIndices.Count; i++)
            {
                int idx = fodderSlotIndices[i];
                if (idx == targetInventorySlotIndex || uniqueFodder.Contains(idx) || IsSlotEquipped(idx)) continue;
                var fs = GetSlot(idx);
                if (EnhancementSystem.IsEligibleFodder(targetSlot.item, targetSlot.instanceId, fs, spec.minFodderTier))
                    uniqueFodder.Add(idx);
            }
        }

        if (uniqueFodder.Count < spec.requiredFodderCount)
        {
            return new EnhanceResult
            {
                attempted = false,
                message = $"Select {spec.requiredFodderCount} eligible [{ItemData.GetTierDisplayName(spec.minFodderTier)}+] fodder gear item(s)."
            };
        }

        // รวบรวมข้อมูลวัตถุดิบก่อนหักออกจากช่อง เพื่อใช้คำนวณโอกาสตีบวกสำเร็จแบบไดนามิก (Normal..Legendary)
        var fodderItems = new List<ItemData>(spec.requiredFodderCount);
        for (int i = 0; i < spec.requiredFodderCount; i++)
        {
            var fs = GetSlot(uniqueFodder[i]);
            if (fs != null && fs.item != null)
                fodderItems.Add(fs.item);
        }
        float dynamicSuccessRate = EnhancementSystem.CalculateDynamicSuccessRate(
            targetSlot.item,
            targetSlot.enhancementLevel,
            fodderItems,
            targetSlot.enhanceFailStacks);

        // Spend Gold & Consume Exact Fodder Slots
        if (!_combat.TrySpendGold(spec.goldCost))
        {
            return new EnhanceResult { attempted = false, message = "Failed to spend Gold." };
        }

        for (int i = 0; i < spec.requiredFodderCount; i++)
        {
            ConsumeSlotAtExactIndex(uniqueFodder[i], 1);
        }

        // Re-locate target slot in case anything changed (index stays identical because slots are fixed array)
        int prevLvl = targetSlot.enhancementLevel;
        float roll = forcedRoll01.HasValue ? Mathf.Clamp01(forcedRoll01.Value) : UnityEngine.Random.value;
        bool success = roll <= dynamicSuccessRate;

        if (success)
        {
            targetSlot.enhancementLevel++;
            targetSlot.enhanceFailStacks = 0; // รีเซ็ตแต้ม Pity เมื่อตีบวกสำเร็จ
            EquipmentSlotType slotType = targetSlot.item.GetEquipmentSlot();
            if (_equipped.TryGetValue(slotType, out var eq) && eq.instanceId == targetSlot.instanceId)
            {
                eq.enhancementLevel = targetSlot.enhancementLevel;
            }
            RecomputeBonus();
        }
        else
        {
            // สะสมแต้ม Pity Failstack (+6% ต่อครั้ง) เพื่อไม่ให้ผู้เล่นเกลือถาวร
            targetSlot.enhanceFailStacks = Mathf.Min(15, targetSlot.enhanceFailStacks + 1);
        }

        Changed?.Invoke();
        PlayerSaveManager.RequestAutoSave(success ? "EnhanceSuccess" : "EnhanceFail");

        int nextPityPct = Mathf.RoundToInt(EnhancementSystem.GetFailstackBonus(targetSlot.enhanceFailStacks) * 100f);
        string msg = success
            ? $"SUCCESS! {targetSlot.item.displayName} enhanced +{prevLvl} -> +{targetSlot.enhancementLevel}!"
            : $"FAILED! Fodder & {spec.goldCost} Gold consumed ({targetSlot.item.displayName} stays +{prevLvl}, Pity +{nextPityPct}% next try!).";
        Debug.Log($"[Enhancement] {msg} (rate={dynamicSuccessRate * 100f:0}%, roll={roll:0.00}, failStacks={targetSlot.enhanceFailStacks})");

        return new EnhanceResult
        {
            attempted = true,
            succeeded = success,
            previousLevel = prevLvl,
            newLevel = targetSlot.enhancementLevel,
            goldSpent = spec.goldCost,
            message = msg
        };
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
                enhancementLevel = s.enhancementLevel,
                enhanceFailStacks = s.enhanceFailStacks
            });
        }

        data.equippedSlots.Clear();
        foreach (var kv in _equipped)
        {
            if (kv.Value.IsEmpty) continue;
            data.equippedSlots.Add(new SavedEquippedSlotData
            {
                slotType = (int)kv.Key,
                instanceId = kv.Value.instanceId,
                itemId = kv.Value.itemId,
                enhancementLevel = kv.Value.enhancementLevel
            });
        }

        // บันทึกการผูกปุ่มลัด Hotbar 1-6 ลงไฟล์เซฟ JSON
        ValidateHotbarBindings();
        data.hotbarBindings.Clear();
        for (int h = 0; h < HotbarCapacity; h++)
        {
            int invIdx = _hotbarSlotIndices[h];
            var bound = GetSlot(invIdx);
            if (bound == null || bound.IsEmpty || bound.item == null) continue;
            bound.EnsureInstanceId();
            data.hotbarBindings.Add(new SavedHotbarBindingData
            {
                hotbarIndex = h,
                inventorySlotIndex = invIdx,
                instanceId = bound.instanceId,
                itemId = bound.item.itemId
            });
        }
    }

    public void RestoreSaveData(PlayerSaveData data)
    {
        if (data == null) return;

        for (int i = 0; i < _slots.Count; i++) _slots[i].Clear();
        foreach (var kv in _equipped) kv.Value.Clear();
        for (int h = 0; h < HotbarCapacity; h++)
        {
            _hotbarSlotIndices[h] = -1;
            _hotbarInstanceIds[h] = null;
        }

        if (data.inventorySlots != null)
        {
            foreach (var saved in data.inventorySlots)
            {
                if (saved == null || saved.slotIndex < 0 || saved.slotIndex >= _slots.Count) continue;
                var item = ItemRegistry.Get(saved.itemId);
                if (item == null || saved.quantity <= 0) continue;
                // บีบจำนวนไอเทมและระดับตีบวก (+0..+10) ให้อยู่ในขอบเขตที่ถูกต้องเสมอ
                int clampedQty = Mathf.Clamp(saved.quantity, 1, item.MaxStackSize);
                int clampedEnh = item.IsEquippable
                    ? Mathf.Clamp(saved.enhancementLevel, 0, EnhancementSystem.MaxEnhancementLevel)
                    : 0;
                int clampedFailStacks = item.IsEquippable ? Mathf.Clamp(saved.enhanceFailStacks, 0, 20) : 0;
                _slots[saved.slotIndex].Set(item, clampedQty, clampedEnh, saved.instanceId, clampedFailStacks);
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
                if (item == null || !item.IsEquippable || item.GetEquipmentSlot() != slotType) continue;

                int clampedEnh = Mathf.Clamp(savedEq.enhancementLevel, 0, EnhancementSystem.MaxEnhancementLevel);
                int matchingIdx = !string.IsNullOrEmpty(savedEq.instanceId) ? FindSlotIndexByInstanceId(savedEq.instanceId) : -1;
                if (matchingIdx >= 0 && !_slots[matchingIdx].IsEmpty && _slots[matchingIdx].item == item)
                {
                    clampedEnh = _slots[matchingIdx].enhancementLevel;
                }

                eqRef.slotType = slotType;
                eqRef.instanceId = savedEq.instanceId;
                eqRef.itemId = savedEq.itemId;
                eqRef.enhancementLevel = clampedEnh;
            }
        }

        // กู้คืนปุ่มลัด Hotbar 1-6 จากไฟล์เซฟ
        if (data.hotbarBindings != null)
        {
            foreach (var hb in data.hotbarBindings)
            {
                if (hb == null || hb.hotbarIndex < 0 || hb.hotbarIndex >= HotbarCapacity) continue;
                int targetIdx = hb.inventorySlotIndex;
                if (!string.IsNullOrEmpty(hb.instanceId))
                {
                    int byId = FindSlotIndexByInstanceId(hb.instanceId);
                    if (byId >= 0) targetIdx = byId;
                }
                var slot = GetSlot(targetIdx);
                if (slot != null && !slot.IsEmpty && slot.item != null &&
                    (string.IsNullOrEmpty(hb.itemId) || slot.item.itemId == hb.itemId))
                {
                    slot.EnsureInstanceId();
                    _hotbarSlotIndices[hb.hotbarIndex] = targetIdx;
                    _hotbarInstanceIds[hb.hotbarIndex] = slot.instanceId;
                }
            }
        }

        ValidateHotbarBindings();
        RecomputeBonus();
        Changed?.Invoke();
    }

    // ------------------------------------------------------------------ ระบบปุ่มลัด Hotbar (1-6) เชื่อมตรงกับ slotIndex / instanceId

    /// <summary>
    /// ผูกช่อง Hotbar (0..5) เข้ากับช่องกระเป๋าจริง (slotIndex) อย่างแม่นยำ
    /// </summary>
    public bool AssignHotbarSlot(int hotbarIndex, int inventorySlotIndex)
    {
        if (hotbarIndex < 0 || hotbarIndex >= HotbarCapacity) return false;
        var slot = GetSlot(inventorySlotIndex);
        if (slot == null || slot.IsEmpty || slot.item == null) return false;

        slot.EnsureInstanceId();
        _hotbarSlotIndices[hotbarIndex] = inventorySlotIndex;
        _hotbarInstanceIds[hotbarIndex] = slot.instanceId;
        Changed?.Invoke();
        PlayerSaveManager.RequestAutoSave("AssignHotbar");
        return true;
    }

    /// <summary>
    /// สลับตำแหน่งไอเทมระหว่างช่อง Hotbar สองช่อง
    /// </summary>
    public bool SwapHotbarSlots(int hotbarA, int hotbarB)
    {
        if (hotbarA < 0 || hotbarA >= HotbarCapacity || hotbarB < 0 || hotbarB >= HotbarCapacity || hotbarA == hotbarB)
            return false;

        int tmpIdx = _hotbarSlotIndices[hotbarA];
        string tmpId = _hotbarInstanceIds[hotbarA];
        _hotbarSlotIndices[hotbarA] = _hotbarSlotIndices[hotbarB];
        _hotbarInstanceIds[hotbarA] = _hotbarInstanceIds[hotbarB];
        _hotbarSlotIndices[hotbarB] = tmpIdx;
        _hotbarInstanceIds[hotbarB] = tmpId;

        ValidateHotbarBindings();
        Changed?.Invoke();
        PlayerSaveManager.RequestAutoSave("SwapHotbar");
        return true;
    }

    /// <summary>
    /// ล้างการผูกปุ่มลัดออกจากช่อง Hotbar โดยไม่ลบไอเทมในกระเป๋า
    /// </summary>
    public void ClearHotbarSlot(int hotbarIndex)
    {
        if (hotbarIndex < 0 || hotbarIndex >= HotbarCapacity) return;
        if (_hotbarSlotIndices[hotbarIndex] < 0 && string.IsNullOrEmpty(_hotbarInstanceIds[hotbarIndex])) return;

        _hotbarSlotIndices[hotbarIndex] = -1;
        _hotbarInstanceIds[hotbarIndex] = null;
        Changed?.Invoke();
        PlayerSaveManager.RequestAutoSave("ClearHotbar");
    }

    /// <summary>
    /// คืนค่าตำแหน่งช่องกระเป๋าจริงที่ผูกกับปุ่มลัด Hotbar (หรือ -1 หากว่าง)
    /// </summary>
    public int GetHotbarBoundInventoryIndex(int hotbarIndex)
    {
        if (hotbarIndex < 0 || hotbarIndex >= HotbarCapacity) return -1;
        ValidateHotbarBindings();
        return _hotbarSlotIndices[hotbarIndex];
    }

    /// <summary>
    /// คืนค่าข้อมูลช่องกระเป๋าที่ผูกอยู่กับปุ่มลัด Hotbar
    /// </summary>
    public InventorySlot GetHotbarBoundSlot(int hotbarIndex)
    {
        int idx = GetHotbarBoundInventoryIndex(hotbarIndex);
        return idx >= 0 ? GetSlot(idx) : null;
    }

    /// <summary>
    /// กดใช้งานไอเทมจากช่อง Hotbar (1-6): ใช้ยา/บัฟ หรือสวมใส่อุปกรณ์จากช่องที่ผูกไว้ทันที
    /// </summary>
    public bool UseHotbarSlot(int hotbarIndex)
    {
        int idx = GetHotbarBoundInventoryIndex(hotbarIndex);
        var slot = GetSlot(idx);
        if (slot == null || slot.IsEmpty || slot.item == null)
        {
            ClearHotbarSlot(hotbarIndex);
            return false;
        }

        if (slot.item.IsUsable)
            return UseItem(idx);

        if (slot.item.IsEquippable)
            return EquipItem(idx);

        return false;
    }

    /// <summary>
    /// ตรวจสอบความถูกต้องของปุ่มลัดทั้ง 6 ช่อง หากไอเทมถูกใช้จนหมดหรือย้ายช่องจะอัปเดต/เคลียร์ให้อัตโนมัติ
    /// </summary>
    public void ValidateHotbarBindings()
    {
        for (int h = 0; h < HotbarCapacity; h++)
        {
            int idx = _hotbarSlotIndices[h];
            string instId = _hotbarInstanceIds[h];
            if (idx < 0 && string.IsNullOrEmpty(instId)) continue;

            var slot = GetSlot(idx);
            if (slot != null && !slot.IsEmpty && (string.IsNullOrEmpty(instId) || slot.instanceId == instId))
            {
                slot.EnsureInstanceId();
                _hotbarInstanceIds[h] = slot.instanceId;
                continue;
            }

            if (!string.IsNullOrEmpty(instId))
            {
                int found = FindSlotIndexByInstanceId(instId);
                if (found >= 0)
                {
                    _hotbarSlotIndices[h] = found;
                    continue;
                }
            }

            _hotbarSlotIndices[h] = -1;
            _hotbarInstanceIds[h] = null;
        }
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
}
