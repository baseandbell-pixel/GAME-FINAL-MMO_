using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ระบบตีบวกอุปกรณ์ (+0 ถึง +10) ของช่างตีเหล็ก (Blacksmith Enhancement System):
///   - ปลดล็อกข้อจำกัดวัตถุดิบ: สามารถใช้อุปกรณ์สำรองระดับใดก็ได้ (Normal, Rare, Epic, Legendary) เป็นวัตถุดิบตีบวกได้ทุกระดับ
///   - คำนวณอัตราสำเร็จ (%) แบบไดนามิกตามส่วนต่างระดับความหายาก (Tier Gap) ระหว่างอุปกรณ์เป้าหมายกับวัตถุดิบที่ใช้:
///       * ใช้วัตถุดิบระดับต่ำกว่าเป้าหมาย -> โอกาสสำเร็จลดลงตามสัดส่วน
///       * ใช้วัตถุดิบระดับเท่ากับเป้าหมาย -> ได้โอกาสสำเร็จตามค่ามาตรฐาน
///       * ใช้วัตถุดิบระดับสูงกว่าเป้าหมาย -> ได้รับโบนัสเพิ่มโอกาสสำเร็จสูงขึ้น
/// </summary>
public static class EnhancementSystem
{
    // ระดับการตีบวกสูงสุด (+10)
    public const int MaxEnhancementLevel = 10;

    /// <summary>
    /// ข้อมูลเงื่อนไขการตีบวกในขั้นถัดไป
    /// </summary>
    public struct EnhancementSpec
    {
        public int targetLevel;         // ระดับ + ถัดไปที่จะตีบวก (1..10)
        public int goldCost;            // จำนวน Gold ที่ต้องใช้
        public int requiredFodderCount; // จำนวนอุปกรณ์วัตถุดิบที่ต้องใช้ (1..3 ชิ้น)
        public ItemTier minFodderTier;  // ระดับวัตถุดิบขั้นต่ำ (ปลดล็อกเป็น Normal เสมอตามข้อกำหนดใหม่)
        public float successRate;       // อัตราสำเร็จพื้นฐาน (0..1)
        public float statBonusPercent;  // เปอร์เซ็นต์โบนัสสเตตัสที่เพิ่มขึ้นเมื่อสำเร็จ
    }

    // ตารางโบนัสสเตตัสที่เพิ่มขึ้นตามระดับ +0 ถึง +10
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

    // ตารางค่าใช้จ่าย Gold พื้นฐานในแต่ละระดับ
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

    // ตารางโอกาสตีบวกสำเร็จพื้นฐานเมื่อใช้วัตถุดิบระดับเดียวกัน
    private static readonly float[] SuccessRateTable =
    {
        1.00f, // +0 -> +1 (100%)
        0.95f, // +1 -> +2 (95%)
        0.85f, // +2 -> +3 (85%)
        0.72f, // +3 -> +4 (72%)
        0.60f, // +4 -> +5 (60%)
        0.48f, // +5 -> +6 (48%)
        0.36f, // +6 -> +7 (36%)
        0.26f, // +7 -> +8 (26%)
        0.18f, // +8 -> +9 (18%)
        0.12f  // +9 -> +10 (12%)
    };

    /// <summary>
    /// ดึงข้อมูลเงื่อนไขการตีบวกขั้นถัดไป (ปลดล็อกให้ใช้วัตถุดิบได้ตั้งแต่ระดับ Normal ทุกขั้น)
    /// </summary>
    public static EnhancementSpec GetNextSpec(ItemData targetItem, int currentEnhancementLevel)
    {
        int nextLvl = Mathf.Clamp(currentEnhancementLevel + 1, 1, MaxEnhancementLevel);
        int idx = nextLvl - 1;

        int tierMultiplier = targetItem != null ? ((int)targetItem.tier + 1) : 1;
        int goldCost = Mathf.RoundToInt(GoldCostTable[idx] * (0.75f + 0.25f * tierMultiplier));

        // จำนวนวัตถุดิบ: +1..+3 ใช้ 1 ชิ้น, +4..+7 ใช้ 2 ชิ้น, +8..+10 ใช้ 3 ชิ้น
        int fodderCount = nextLvl <= 3 ? 1 : (nextLvl <= 7 ? 2 : 3);

        return new EnhancementSpec
        {
            targetLevel = nextLvl,
            goldCost = goldCost,
            requiredFodderCount = fodderCount,
            minFodderTier = ItemTier.Normal, // อนุญาตให้ใช้อุปกรณ์ทุกระดับ (Normal ขึ้นไป) เป็นวัตถุดิบได้เสมอ
            successRate = SuccessRateTable[idx],
            statBonusPercent = StatBonusTable[nextLvl]
        };
    }

    /// <summary>
    /// คำนวณอัตราความสำเร็จ (%) แบบไดนามิกตามระดับความหายากของวัตถุดิบที่เลือกเทียบกับอุปกรณ์เป้าหมาย
    /// </summary>
    public static float CalculateDynamicSuccessRate(ItemData targetItem, int currentEnhancementLevel, IReadOnlyList<ItemData> selectedFodderItems)
    {
        var spec = GetNextSpec(targetItem, currentEnhancementLevel);
        float baseRate = spec.successRate;
        if (targetItem == null || selectedFodderItems == null || selectedFodderItems.Count == 0)
            return baseRate;

        // คำนวณตัวคูณเฉลี่ยจากส่วนต่างระดับ (Tier Gap) ของวัตถุดิบทุกชิ้นที่ใส่เข้ามา
        float sumFactor = 0f;
        int validCount = 0;

        for (int i = 0; i < selectedFodderItems.Count; i++)
        {
            var fodder = selectedFodderItems[i];
            if (fodder == null) continue;
            validCount++;

            int tierGap = (int)fodder.tier - (int)targetItem.tier;
            float factor;
            if (tierGap == 0)
            {
                // วัตถุดิบระดับเดียวกับเป้าหมาย = ได้โอกาสสำเร็จ 100% ของค่ามาตรฐาน
                factor = 1.00f;
            }
            else if (tierGap < 0)
            {
                // วัตถุดิบระดับต่ำกว่าเป้าหมาย (เช่น เอา Normal มาตีบวก Epic/Legendary) -> โอกาสสำเร็จลดลงตามส่วนต่าง
                factor = tierGap switch
                {
                    -1 => 0.80f, // ต่ำกว่า 1 ระดับ เหลือ 80% ของอัตราปกติ
                    -2 => 0.62f, // ต่ำกว่า 2 ระดับ เหลือ 62% ของอัตราปกติ
                    _  => 0.45f  // ต่ำกว่า 3 ระดับ เหลือ 45% ของอัตราปกติ
                };
            }
            else
            {
                // วัตถุดิบระดับสูงกว่าเป้าหมาย (เช่น เอา Rare/Epic มาตีบวก Normal) -> ได้โบนัสโอกาสสำเร็จเพิ่มขึ้น
                factor = 1.00f + tierGap * 0.18f;
            }

            sumFactor += factor;
        }

        if (validCount == 0) return baseRate;
        float avgFactor = sumFactor / validCount;
        return Mathf.Clamp(baseRate * avgFactor, 0.05f, 1.00f);
    }

    /// <summary>
    /// คืนค่าตัวคูณสเตตัสตามระดับการตีบวก (+0 ถึง +10)
    /// </summary>
    public static float GetStatMultiplier(int enhancementLevel)
    {
        int clamped = Mathf.Clamp(enhancementLevel, 0, MaxEnhancementLevel);
        return 1f + StatBonusTable[clamped];
    }

    /// <summary>
    /// คำนวณค่าสเตตัสจำนวนเต็มที่เพิ่มขึ้นจากการตีบวก
    /// </summary>
    public static int ScaleIntStat(int baseValue, int enhancementLevel)
    {
        if (baseValue <= 0 || enhancementLevel <= 0) return baseValue;
        float mult = GetStatMultiplier(enhancementLevel);
        return Mathf.Max(baseValue + enhancementLevel, Mathf.RoundToInt(baseValue * mult));
    }

    /// <summary>
    /// คำนวณค่าสเตตัสทศนิยมที่เพิ่มขึ้นจากการตีบวก
    /// </summary>
    public static float ScaleFloatStat(float baseValue, int enhancementLevel)
    {
        if (baseValue <= 0f || enhancementLevel <= 0) return baseValue;
        return baseValue * GetStatMultiplier(enhancementLevel);
    }

    /// <summary>
    /// ตรวจสอบว่าช่องกระเป๋านี้สามารถใช้เป็นวัตถุดิบตีบวกได้หรือไม่ (อนุญาตอุปกรณ์สวมใส่ทุกระดับ Normal, Rare, Epic, Legendary)
    /// </summary>
    public static bool IsEligibleFodder(ItemData targetItem, string targetInstanceId, InventorySlot candidate, ItemTier minTier = ItemTier.Normal)
    {
        if (targetItem == null || candidate == null || candidate.IsEmpty || candidate.item == null) return false;
        if (!candidate.item.IsEquippable) return false;
        // ห้ามใช้ไอเทมชิ้นเดียวกับตัวเป้าหมายมาเป็นวัตถุดิบให้ตัวเอง
        if (!string.IsNullOrEmpty(targetInstanceId) && candidate.instanceId == targetInstanceId) return false;
        return true;
    }
}
