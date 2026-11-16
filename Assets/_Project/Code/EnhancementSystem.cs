using UnityEngine;

/// <summary>
/// Defines +0 to +10 Equipment Enhancement (Blacksmith / ตีบวก) progression:
/// escalating Gold cost, required Fodder quantity, minimum Fodder Tier, decreasing Success Rate,
/// and multiplicative stat scaling.
/// </summary>
public static class EnhancementSystem
{
    public const int MaxEnhancementLevel = 10;

    public struct EnhancementSpec
    {
        public int targetLevel;
        public int goldCost;
        public int requiredFodderCount;
        public ItemTier minFodderTier;
        public float successRate; // 0..1
        public float statBonusPercent;
    }

    // Stat bonus multipliers at +0 .. +10
    private static readonly float[] StatBonusTable =
    {
        0.00f, // +0
        0.12f, // +1
        0.24f, // +2
        0.38f, // +3
        0.54f, // +4
        0.72f, // +5
        0.92f, // +6
        1.15f, // +7
        1.42f, // +8
        1.72f, // +9
        2.10f  // +10
    };

    private static readonly int[] GoldCostTable =
    {
        50,    // +0 -> +1
        95,    // +1 -> +2
        160,   // +2 -> +3
        260,   // +3 -> +4
        410,   // +4 -> +5
        620,   // +5 -> +6
        920,   // +6 -> +7
        1350,  // +7 -> +8
        1950,  // +8 -> +9
        2800   // +9 -> +10
    };

    private static readonly float[] SuccessRateTable =
    {
        1.00f, // +0 -> +1
        0.95f, // +1 -> +2
        0.85f, // +2 -> +3
        0.72f, // +3 -> +4
        0.60f, // +4 -> +5
        0.48f, // +5 -> +6
        0.36f, // +6 -> +7
        0.26f, // +7 -> +8
        0.18f, // +8 -> +9
        0.12f  // +9 -> +10
    };

    public static EnhancementSpec GetNextSpec(ItemData targetItem, int currentEnhancementLevel)
    {
        int nextLvl = Mathf.Clamp(currentEnhancementLevel + 1, 1, MaxEnhancementLevel);
        int idx = nextLvl - 1;

        int tierMultiplier = targetItem != null ? ((int)targetItem.tier + 1) : 1;
        int goldCost = Mathf.RoundToInt(GoldCostTable[idx] * (0.75f + 0.25f * tierMultiplier));

        int fodderCount = nextLvl <= 3 ? 1 : (nextLvl <= 7 ? 2 : 3);

        // Required fodder tier escalates with +Level, capped at targetItem.tier so lower-tier items
        // can still be enhanced with same/lower tier fodder.
        ItemTier desiredMinTier = nextLvl <= 3
            ? ItemTier.Normal
            : (nextLvl <= 6 ? ItemTier.Rare : ItemTier.Epic);
        if (targetItem != null && desiredMinTier > targetItem.tier)
            desiredMinTier = targetItem.tier;

        return new EnhancementSpec
        {
            targetLevel = nextLvl,
            goldCost = goldCost,
            requiredFodderCount = fodderCount,
            minFodderTier = desiredMinTier,
            successRate = SuccessRateTable[idx],
            statBonusPercent = StatBonusTable[nextLvl]
        };
    }

    public static float GetStatMultiplier(int enhancementLevel)
    {
        int clamped = Mathf.Clamp(enhancementLevel, 0, MaxEnhancementLevel);
        return 1f + StatBonusTable[clamped];
    }

    public static int ScaleIntStat(int baseValue, int enhancementLevel)
    {
        if (baseValue <= 0 || enhancementLevel <= 0) return baseValue;
        float mult = GetStatMultiplier(enhancementLevel);
        return Mathf.Max(baseValue + enhancementLevel, Mathf.RoundToInt(baseValue * mult));
    }

    public static float ScaleFloatStat(float baseValue, int enhancementLevel)
    {
        if (baseValue <= 0f || enhancementLevel <= 0) return baseValue;
        return baseValue * GetStatMultiplier(enhancementLevel);
    }

    public static bool IsEligibleFodder(ItemData targetItem, string targetInstanceId, InventorySlot candidate, ItemTier minTier)
    {
        if (targetItem == null || candidate == null || candidate.IsEmpty || candidate.item == null) return false;
        if (!candidate.item.IsEquippable) return false;
        if (!string.IsNullOrEmpty(targetInstanceId) && candidate.instanceId == targetInstanceId) return false;
        // Must be same or lower tier than the target gear, AND meet the minimum escalating tier requirement
        if (candidate.item.tier > targetItem.tier) return false;
        if (candidate.item.tier < minTier) return false;
        return true;
    }
}
