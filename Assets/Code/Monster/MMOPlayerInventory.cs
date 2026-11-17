using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Full MMORPG Bag (32 slots), Equipment Paperdoll (5 slots), Quick Hotbar (1-5),
/// Salvage, Blacksmith Enhancement (+0..+15 + Pity), Boss Crafting, and Firestore Cloud Save.
/// </summary>
[DisallowMultipleComponent]
public sealed class MMOPlayerInventory : MonoBehaviour
{
    public const int MaxBagSlots = 32;
    public const int HotbarSlotCount = 5;
    public const int MaxUpgradeLevel = 15;

    public struct CraftRecipe
    {
        public string itemId;
        public ItemRarity minRarity;
        public int coreCost;
        public int stoneCost;
        public int goldCost;
    }

    public static readonly CraftRecipe[] BossRecipes =
    {
        new CraftRecipe { itemId = "warlord_wrath", minRarity = ItemRarity.Legendary, coreCost = 2, stoneCost = 6, goldCost = 250 },
        new CraftRecipe { itemId = "warlord_crown", minRarity = ItemRarity.Epic, coreCost = 1, stoneCost = 4, goldCost = 180 },
        new CraftRecipe { itemId = "warlord_mail", minRarity = ItemRarity.Epic, coreCost = 2, stoneCost = 5, goldCost = 220 },
        new CraftRecipe { itemId = "warlord_treads", minRarity = ItemRarity.Epic, coreCost = 1, stoneCost = 4, goldCost = 160 }
    };

    private MMOPlayerCombat _combat;
    private MMOPlayerController _controller;
    private Transform _equippedSwordVisual;
    private Renderer[] _swordRenderers;

    private readonly MMOItemInstance[] _bag = new MMOItemInstance[MaxBagSlots];
    private readonly Dictionary<EquipSlot, MMOItemInstance> _equipped = new Dictionary<EquipSlot, MMOItemInstance>();
    private readonly string[] _hotbarItemIds = new string[HotbarSlotCount];
    private readonly float[] _hotbarCooldownUntil = new float[HotbarSlotCount];

    public bool AutoLootAll { get; set; }

    public event Action OnInventoryChanged;

    public void Initialize(MMOPlayerCombat combat, MMOPlayerController controller)
    {
        MMOItemCatalog.EnsureInitialized();
        _combat = combat;
        _controller = controller;
        LocateSwordVisual();

        bool loaded = false;
        if (GoogleAPI.instance != null && GoogleAPI.instance.profileLoaded && !string.IsNullOrEmpty(GoogleAPI.instance.inventoryJson))
        {
            loaded = TryLoadFromJson(GoogleAPI.instance.inventoryJson);
        }

        if (!loaded)
        {
            GrantStarterLoadout();
        }

        RefreshSwordVisual();
        OnInventoryChanged?.Invoke();
    }

    private void LocateSwordVisual()
    {
        Transform[] all = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].name == "EquippedWeaponVisual" || all[i].name == "Player_Sword_Equipped")
            {
                _equippedSwordVisual = all[i];
                _swordRenderers = _equippedSwordVisual.GetComponentsInChildren<Renderer>(true);
                break;
            }
        }
    }

    private void GrantStarterLoadout()
    {
        Array.Clear(_bag, 0, _bag.Length);
        _equipped.Clear();

        AddItem(MMOItemCatalog.CreateInstance("iron_greatsword", ItemRarity.Common, 1, false));
        AddItem(MMOItemCatalog.CreateInstance("potion_minor", ItemRarity.Common, 3, false));
        AddItem(MMOItemCatalog.CreateInstance("elixir_berserk", ItemRarity.Uncommon, 1, false));
        AddItem(MMOItemCatalog.CreateInstance("enhance_stone", ItemRarity.Common, 2, false));

        _hotbarItemIds[0] = "potion_minor";
        _hotbarItemIds[1] = "elixir_berserk";
        _hotbarItemIds[2] = "potion_greater";
        _hotbarItemIds[3] = "scroll_haste";
        _hotbarItemIds[4] = "thunder_bomb";
    }

    // =========================================================
    // READ ACCESSORS
    // =========================================================

    public MMOItemInstance GetBagSlot(int index) =>
        (index >= 0 && index < MaxBagSlots) ? _bag[index] : null;

    public MMOItemInstance GetEquipped(EquipSlot slot) =>
        _equipped.TryGetValue(slot, out MMOItemInstance item) ? item : null;

    public string GetHotbarItemId(int slotIdx) =>
        (slotIdx >= 0 && slotIdx < HotbarSlotCount) ? _hotbarItemIds[slotIdx] : string.Empty;

    public float GetHotbarCooldownRemaining(int slotIdx) =>
        (slotIdx >= 0 && slotIdx < HotbarSlotCount) ? Mathf.Max(0f, _hotbarCooldownUntil[slotIdx] - Time.time) : 0f;

    public int GetItemCount(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return 0;
        int total = 0;
        for (int i = 0; i < MaxBagSlots; i++)
        {
            if (_bag[i] != null && _bag[i].itemId == itemId)
            {
                total += Mathf.Max(1, _bag[i].stackCount);
            }
        }
        return total;
    }

    public int GetTotalPotionCount() =>
        GetItemCount("potion_minor") + GetItemCount("potion_greater");

    public MMOItemInstance FindByUid(string uid, out int bagSlotIndex, out EquipSlot equippedSlot)
    {
        bagSlotIndex = -1;
        equippedSlot = EquipSlot.None;
        if (string.IsNullOrEmpty(uid)) return null;

        for (int i = 0; i < MaxBagSlots; i++)
        {
            if (_bag[i] != null && _bag[i].uid == uid)
            {
                bagSlotIndex = i;
                return _bag[i];
            }
        }

        foreach (var kvp in _equipped)
        {
            if (kvp.Value != null && kvp.Value.uid == uid)
            {
                equippedSlot = kvp.Key;
                return kvp.Value;
            }
        }

        return null;
    }

    // =========================================================
    // BAG OPERATIONS
    // =========================================================

    public bool AddItem(MMOItemInstance incoming)
    {
        if (incoming == null) return false;
        MMOItemTemplate tpl = MMOItemCatalog.Get(incoming.itemId);
        if (tpl == null) return false;

        int remaining = Mathf.Max(1, incoming.stackCount);

        // Stack onto existing stacks if stackable
        if (tpl.maxStack > 1)
        {
            for (int i = 0; i < MaxBagSlots && remaining > 0; i++)
            {
                MMOItemInstance existing = _bag[i];
                if (existing != null && existing.itemId == incoming.itemId && existing.stackCount < tpl.maxStack)
                {
                    int room = tpl.maxStack - existing.stackCount;
                    int add = Mathf.Min(room, remaining);
                    existing.stackCount += add;
                    remaining -= add;
                }
            }
        }

        while (remaining > 0)
        {
            int freeSlot = FindFirstEmptyBagSlot();
            if (freeSlot < 0)
            {
                SaveToCloud();
                OnInventoryChanged?.Invoke();
                return false;
            }

            MMOItemInstance slotItem = incoming.Clone();
            if (string.IsNullOrEmpty(slotItem.uid))
            {
                slotItem.uid = Guid.NewGuid().ToString("N").Substring(0, 12);
            }
            int put = Mathf.Min(tpl.maxStack, remaining);
            slotItem.stackCount = put;
            remaining -= put;
            _bag[freeSlot] = slotItem;
        }

        SaveToCloud();
        OnInventoryChanged?.Invoke();
        return true;
    }

    public bool ConsumeOne(string itemId)
    {
        for (int i = 0; i < MaxBagSlots; i++)
        {
            MMOItemInstance item = _bag[i];
            if (item != null && item.itemId == itemId && item.stackCount > 0)
            {
                item.stackCount--;
                if (item.stackCount <= 0)
                {
                    _bag[i] = null;
                }
                SaveToCloud();
                OnInventoryChanged?.Invoke();
                return true;
            }
        }
        return false;
    }

    public void SwapBagSlots(int slotA, int slotB)
    {
        if (slotA < 0 || slotA >= MaxBagSlots || slotB < 0 || slotB >= MaxBagSlots || slotA == slotB)
        {
            return;
        }

        MMOItemInstance temp = _bag[slotA];
        _bag[slotA] = _bag[slotB];
        _bag[slotB] = temp;
        SaveToCloud();
        OnInventoryChanged?.Invoke();
    }

    public bool ToggleLock(string uid)
    {
        MMOItemInstance item = FindByUid(uid, out _, out _);
        if (item == null) return false;
        item.isLocked = !item.isLocked;
        SaveToCloud();
        OnInventoryChanged?.Invoke();
        return true;
    }

    public bool UseOrEquipBagSlot(int bagSlot)
    {
        MMOItemInstance item = GetBagSlot(bagSlot);
        if (item == null) return false;
        MMOItemTemplate tpl = MMOItemCatalog.Get(item.itemId);
        if (tpl == null) return false;

        if (tpl.category == ItemCategory.Equipment)
        {
            return EquipFromBagSlot(bagSlot);
        }

        if (tpl.category == ItemCategory.Consumable)
        {
            return ActivateConsumableItem(item.itemId, hotbarSlot: -1);
        }

        return false;
    }

    public bool EquipFromBagSlot(int bagSlot)
    {
        MMOItemInstance incoming = GetBagSlot(bagSlot);
        if (incoming == null) return false;
        MMOItemTemplate tpl = MMOItemCatalog.Get(incoming.itemId);
        if (tpl == null || tpl.category != ItemCategory.Equipment || tpl.equipSlot == EquipSlot.None)
        {
            return false;
        }

        _equipped.TryGetValue(tpl.equipSlot, out MMOItemInstance previous);
        _equipped[tpl.equipSlot] = incoming;
        _bag[bagSlot] = previous;

        RefreshSwordVisual();
        MMOAudioManager.PlayLootPickup(transform.position);
        SaveToCloud();
        OnInventoryChanged?.Invoke();
        return true;
    }

    public bool UnequipSlot(EquipSlot slot)
    {
        if (!_equipped.TryGetValue(slot, out MMOItemInstance item) || item == null)
        {
            return false;
        }

        int freeSlot = FindFirstEmptyBagSlot();
        if (freeSlot < 0)
        {
            _combat?.ShowBanner("BAG IS FULL!", 1.8f);
            return false;
        }

        _equipped.Remove(slot);
        _bag[freeSlot] = item;
        RefreshSwordVisual();
        SaveToCloud();
        OnInventoryChanged?.Invoke();
        return true;
    }

    public void BindHotbar(int hotbarIndex, string itemId)
    {
        if (hotbarIndex < 0 || hotbarIndex >= HotbarSlotCount) return;
        MMOItemTemplate tpl = MMOItemCatalog.Get(itemId);
        if (tpl == null || tpl.category != ItemCategory.Consumable) return;

        _hotbarItemIds[hotbarIndex] = itemId;
        SaveToCloud();
        OnInventoryChanged?.Invoke();
    }

    public bool UseHotbarSlot(int hotbarIndex)
    {
        if (hotbarIndex < 0 || hotbarIndex >= HotbarSlotCount) return false;
        if (Time.time < _hotbarCooldownUntil[hotbarIndex]) return false;

        string itemId = _hotbarItemIds[hotbarIndex];
        if (string.IsNullOrEmpty(itemId)) return false;

        return ActivateConsumableItem(itemId, hotbarIndex);
    }

    private bool ActivateConsumableItem(string itemId, int hotbarSlot)
    {
        if (_combat == null || _combat.IsDead) return false;
        MMOItemTemplate tpl = MMOItemCatalog.Get(itemId);
        if (tpl == null || tpl.category != ItemCategory.Consumable) return false;

        if (GetItemCount(itemId) <= 0)
        {
            _combat.ShowBanner($"OUT OF {tpl.displayName.ToUpperInvariant()}!", 1.8f);
            return false;
        }

        if ((itemId == "potion_minor" || itemId == "potion_greater") && _combat.CurrentHealth >= _combat.MaxHealth)
        {
            _combat.ShowBanner("HP IS ALREADY FULL!", 1.4f);
            return false;
        }

        if (!ConsumeOne(itemId)) return false;

        float cdrFactor = 1f - Mathf.Clamp(GetCooldownReductionPct() * 0.01f, 0f, 0.45f);
        if (hotbarSlot >= 0 && hotbarSlot < HotbarSlotCount)
        {
            _hotbarCooldownUntil[hotbarSlot] = Time.time + (4.5f * cdrFactor);
        }

        _combat.ApplyConsumableEffect(tpl);
        return true;
    }

    // =========================================================
    // SALVAGE, BLACKSMITH ENHANCE (+PITY), & BOSS CRAFT
    // =========================================================

    public bool SalvageByUid(string uid)
    {
        MMOItemInstance item = FindByUid(uid, out int bagSlot, out EquipSlot eqSlot);
        if (item == null || bagSlot < 0 || eqSlot != EquipSlot.None || item.isLocked)
        {
            return false;
        }

        MMOItemTemplate tpl = MMOItemCatalog.Get(item.itemId);
        if (tpl == null) return false;

        int stones = item.rarity switch
        {
            ItemRarity.Uncommon => 2,
            ItemRarity.Rare => 4,
            ItemRarity.Epic => 7,
            ItemRarity.Legendary => 12,
            _ => 1
        } + item.upgradeLevel;

        int goldGain = tpl.sellGold + item.upgradeLevel * 20;
        _bag[bagSlot] = null;

        if (tpl.category == ItemCategory.Equipment)
        {
            AddItem(MMOItemCatalog.CreateInstance("enhance_stone", ItemRarity.Common, stones, false));
            _combat?.GrantGoldFromSalvage(goldGain);
            _combat?.ShowBanner($"SALVAGED: +{stones} Stones, +{goldGain}G", 2.0f);
        }
        else
        {
            int stackGold = goldGain * Mathf.Max(1, item.stackCount);
            _bag[bagSlot] = null;
            _combat?.GrantGoldFromSalvage(stackGold);
            _combat?.ShowBanner($"SOLD: +{stackGold}G", 1.8f);
        }

        MMOAudioManager.PlayLootPickup(transform.position);
        SaveToCloud();
        OnInventoryChanged?.Invoke();
        return true;
    }

    public int SalvageAllUnlockedJunk(ItemRarity maxRarity = ItemRarity.Uncommon)
    {
        int count = 0;
        int totalStones = 0;
        int totalGold = 0;

        for (int i = 0; i < MaxBagSlots; i++)
        {
            MMOItemInstance item = _bag[i];
            if (item == null || item.isLocked || item.rarity > maxRarity || item.upgradeLevel > 0) continue;

            MMOItemTemplate tpl = MMOItemCatalog.Get(item.itemId);
            if (tpl == null || tpl.category != ItemCategory.Equipment) continue;

            totalStones += item.rarity == ItemRarity.Uncommon ? 2 : 1;
            totalGold += tpl.sellGold;
            _bag[i] = null;
            count++;
        }

        if (count > 0)
        {
            AddItem(MMOItemCatalog.CreateInstance("enhance_stone", ItemRarity.Common, totalStones, false));
            _combat?.GrantGoldFromSalvage(totalGold);
            _combat?.ShowBanner($"SALVAGED {count} GEAR: +{totalStones} Stones, +{totalGold}G", 2.2f);
            MMOAudioManager.PlayLootPickup(transform.position);
            SaveToCloud();
            OnInventoryChanged?.Invoke();
        }
        return count;
    }

    public static void GetEnhanceCosts(MMOItemInstance item, out int goldCost, out int stoneCost, out int successChancePct)
    {
        int lv = item != null ? item.upgradeLevel : 0;
        goldCost = 60 + lv * 55;
        stoneCost = 1 + lv / 2;
        if (item != null && item.pityPercent >= 100)
        {
            successChancePct = 100;
            return;
        }

        successChancePct = lv < 3 ? 100 : (lv < 6 ? 80 : (lv < 10 ? 60 : 40));
    }

    public bool TryEnhanceItem(string uid, out string resultBanner)
    {
        resultBanner = "";
        MMOItemInstance item = FindByUid(uid, out _, out EquipSlot eqSlot);
        if (item == null) return false;

        MMOItemTemplate tpl = MMOItemCatalog.Get(item.itemId);
        if (tpl == null || tpl.category != ItemCategory.Equipment)
        {
            resultBanner = "Only Equipment can be enhanced!";
            return false;
        }

        if (item.upgradeLevel >= MaxUpgradeLevel)
        {
            resultBanner = $"Already at MAX (+{MaxUpgradeLevel})!";
            return false;
        }

        GetEnhanceCosts(item, out int goldCost, out int stoneCost, out int chancePct);
        if (_combat == null || _combat.Gold < goldCost)
        {
            resultBanner = $"Need {goldCost}G!";
            return false;
        }
        if (GetItemCount("enhance_stone") < stoneCost)
        {
            resultBanner = $"Need {stoneCost} Enhancement Stones!";
            return false;
        }

        _combat.SpendGold(goldCost);
        for (int s = 0; s < stoneCost; s++) ConsumeOne("enhance_stone");

        bool success = chancePct >= 100 || UnityEngine.Random.Range(0, 100) < chancePct;
        if (success)
        {
            item.upgradeLevel++;
            item.pityPercent = 0;
            resultBanner = $"ENHANCE SUCCESS! {tpl.displayName} +{item.upgradeLevel}";
            MMOAudioManager.PlayLevelUp(transform.position);
        }
        else
        {
            item.pityPercent = Mathf.Min(100, item.pityPercent + 25);
            resultBanner = $"Enhance Failed... Pity {item.pityPercent}% (+25%)";
            MMOAudioManager.PlayPlayerHurt(transform.position);
        }

        if (eqSlot == EquipSlot.Weapon)
        {
            RefreshSwordVisual();
        }
        _combat.ShowBanner(resultBanner, 2.4f);
        SaveToCloud();
        OnInventoryChanged?.Invoke();
        return success;
    }

    public bool TryCraftBossRecipe(int recipeIndex, out string resultBanner)
    {
        resultBanner = "";
        if (recipeIndex < 0 || recipeIndex >= BossRecipes.Length || _combat == null) return false;
        CraftRecipe recipe = BossRecipes[recipeIndex];

        if (_combat.Gold < recipe.goldCost)
        {
            resultBanner = $"Need {recipe.goldCost}G!";
            return false;
        }
        if (GetItemCount("warlord_core") < recipe.coreCost)
        {
            resultBanner = $"Need {recipe.coreCost} Warlord Core(s)!";
            return false;
        }
        if (GetItemCount("enhance_stone") < recipe.stoneCost)
        {
            resultBanner = $"Need {recipe.stoneCost} Stones!";
            return false;
        }
        if (FindFirstEmptyBagSlot() < 0)
        {
            resultBanner = "Bag is full!";
            return false;
        }

        _combat.SpendGold(recipe.goldCost);
        for (int i = 0; i < recipe.coreCost; i++) ConsumeOne("warlord_core");
        for (int i = 0; i < recipe.stoneCost; i++) ConsumeOne("enhance_stone");

        MMOItemInstance crafted = MMOItemCatalog.CreateInstance(recipe.itemId, recipe.minRarity, 1, rollSubStats: true);
        AddItem(crafted);
        MMOItemTemplate tpl = MMOItemCatalog.Get(recipe.itemId);
        resultBanner = $"CRAFTED [{recipe.minRarity}] {tpl?.displayName}!";
        _combat.ShowBanner(resultBanner, 2.8f);
        MMOAudioManager.PlayLevelUp(transform.position);
        return true;
    }

    // =========================================================
    // STATS, SET BONUSES, & 3D WEAPON VISUAL
    // =========================================================

    public ComputedGearStats GetTotalEquippedStats(out int warlordSetPieces)
    {
        var total = new ComputedGearStats();
        warlordSetPieces = 0;

        foreach (var kvp in _equipped)
        {
            MMOItemInstance item = kvp.Value;
            if (item == null) continue;

            MMOItemTemplate tpl = MMOItemCatalog.Get(item.itemId);
            if (tpl != null && tpl.setId == "warlord")
            {
                warlordSetPieces++;
            }

            ComputedGearStats s = MMOItemCatalog.EvaluateStats(item);
            total.attack += s.attack;
            total.maxHp += s.maxHp;
            total.critChancePct += s.critChancePct;
            total.critDamagePct += s.critDamagePct;
            total.moveSpeedPct += s.moveSpeedPct;
            total.lifeStealPct += s.lifeStealPct;
            total.cooldownReductionPct += s.cooldownReductionPct;
        }

        if (warlordSetPieces >= 2)
        {
            total.attack += 35;
            total.critDamagePct += 25f;
        }
        if (warlordSetPieces >= 4)
        {
            total.maxHp += 300;
            total.cooldownReductionPct += 15f;
            total.lifeStealPct += 4f;
        }

        return total;
    }

    public float GetCooldownReductionPct()
    {
        return GetTotalEquippedStats(out _).cooldownReductionPct;
    }

    public void RefreshSwordVisual()
    {
        MMOItemInstance weapon = GetEquipped(EquipSlot.Weapon);
        ApplyWeaponVisualState(weapon != null ? (int)weapon.rarity : -1, weapon != null ? weapon.upgradeLevel : 0);
    }

    public void ApplyWeaponVisualState(int weaponRarity, int upgradeLevel)
    {
        if (_equippedSwordVisual == null) LocateSwordVisual();
        if (_equippedSwordVisual == null) return;

        bool visible = weaponRarity >= 0;
        if (_equippedSwordVisual.gameObject.activeSelf != visible)
        {
            _equippedSwordVisual.gameObject.SetActive(visible);
        }

        if (!visible || _swordRenderers == null) return;

        Color glow = MMOItemCatalog.GetRarityColor((ItemRarity)Mathf.Clamp(weaponRarity, 0, 4));
        float intensity = weaponRarity >= 2 || upgradeLevel >= 3 ? (0.45f + upgradeLevel * 0.08f) : 0f;
        MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        for (int i = 0; i < _swordRenderers.Length; i++)
        {
            Renderer r = _swordRenderers[i];
            if (r == null) continue;
            r.GetPropertyBlock(mpb);
            mpb.SetColor("_EmissionColor", glow * intensity);
            r.SetPropertyBlock(mpb);
        }
    }

    // =========================================================
    // SERIALIZATION & HELPERS
    // =========================================================

    private int FindFirstEmptyBagSlot()
    {
        for (int i = 0; i < MaxBagSlots; i++)
        {
            if (_bag[i] == null) return i;
        }
        return -1;
    }

    public void SaveToCloud()
    {
        if (GoogleAPI.instance == null || string.IsNullOrEmpty(GoogleAPI.instance.uuid))
        {
            return;
        }

        var data = new MMOInventorySaveData
        {
            autoLootAll = AutoLootAll,
            hotbarItemIds = (string[])_hotbarItemIds.Clone()
        };

        for (int i = 0; i < MaxBagSlots; i++)
        {
            if (_bag[i] != null)
            {
                data.bagItems.Add(_bag[i]);
            }
        }

        foreach (var kvp in _equipped)
        {
            if (kvp.Value != null)
            {
                data.bagItems.Add(kvp.Value);
                data.equipped.Add(new EquipSlotBinding { slot = kvp.Key, itemUid = kvp.Value.uid });
            }
        }

        GoogleAPI.instance.SaveInventorySnapshot(JsonUtility.ToJson(data));
    }

    private bool TryLoadFromJson(string json)
    {
        try
        {
            MMOInventorySaveData data = JsonUtility.FromJson<MMOInventorySaveData>(json);
            if (data == null || data.bagItems == null) return false;

            Array.Clear(_bag, 0, _bag.Length);
            _equipped.Clear();
            AutoLootAll = data.autoLootAll;

            var equippedLookup = new Dictionary<string, EquipSlot>();
            if (data.equipped != null)
            {
                for (int i = 0; i < data.equipped.Count; i++)
                {
                    if (!string.IsNullOrEmpty(data.equipped[i].itemUid))
                    {
                        equippedLookup[data.equipped[i].itemUid] = data.equipped[i].slot;
                    }
                }
            }

            int bagIdx = 0;
            for (int i = 0; i < data.bagItems.Count; i++)
            {
                MMOItemInstance item = data.bagItems[i];
                if (item == null || MMOItemCatalog.Get(item.itemId) == null) continue;

                if (!string.IsNullOrEmpty(item.uid) && equippedLookup.TryGetValue(item.uid, out EquipSlot eqSlot))
                {
                    _equipped[eqSlot] = item;
                }
                else if (bagIdx < MaxBagSlots)
                {
                    _bag[bagIdx++] = item;
                }
            }

            if (data.hotbarItemIds != null)
            {
                for (int i = 0; i < HotbarSlotCount && i < data.hotbarItemIds.Length; i++)
                {
                    _hotbarItemIds[i] = data.hotbarItemIds[i];
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[MMOPlayerInventory] Failed to parse saved inventory JSON: " + ex.Message);
            return false;
        }
    }
}
