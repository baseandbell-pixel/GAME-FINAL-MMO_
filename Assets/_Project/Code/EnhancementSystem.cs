using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ระบบตีบวกอุปกรณ์ (+0 ถึง +10) ของช่างตีเหล็ก (Blacksmith Enhancement System - Flat Integer Rebalance):
///   - ยกเลิกระบบคูณเปอร์เซ็นต์ (%) และเปลี่ยนเป็นโบนัสจำนวนเต็มคงที่ (+1 ถึง +2 ต่อระดับตีบวก) เพื่อไม่ให้พลังพุ่งเกินเส้นโค้ง Lv.1–30
///   - บังคับแยกบทบาทอุปกรณ์อย่างเคร่งครัด (Role Separation):
///       * อาวุธ (Sword): ได้โบนัส ATK แบบจำนวนเต็ม (+1/ขั้น สำหรับ Normal/Rare และ +2/ขั้น สำหรับ Epic/Legendary)
///       * อุปกรณ์ป้องกัน (Shield, Armor, Pants): ไม่เพิ่ม ATK จากการตีบวก (คงเพดาน Role Cap: Normal/Rare = 0, Epic <= 1, Legendary <= 2)
///         แต่เพิ่มค่าป้องกัน DEF (+1 ทุก 2 ขั้น), MaxHP (+2/ขั้น), MaxSTA (+1 ทุก 2 ขั้น) และ MoveSpeed (+0.02/ขั้น)
///   - ปลดล็อกข้อจำกัดวัตถุดิบ: ใช้อุปกรณ์สำรองระดับใดก็ได้ (Normal..Legendary) พร้อมคำนวณโอกาสสำเร็จแบบไดนามิกตาม Tier Gap
/// </summary>
public static class EnhancementSystem
{
    // ระดับการตีบวกสูงสุด (+10)
    public const int MaxEnhancementLevel = 10;

    /// <summary>
    /// ข้อมูลเงื่อนไขการตีบวกในขั้นถัดไป (ใช้ค่าเพิ่มแบบ Flat Integer ไม่มีตัวคูณ %)
    /// </summary>
    public struct EnhancementSpec
    {
        public int targetLevel;         // ระดับ + ถัดไปที่จะตีบวก (1..10)
        public int goldCost;            // จำนวน Gold ที่ต้องใช้
        public int requiredFodderCount; // จำนวนอุปกรณ์วัตถุดิบที่ต้องใช้ (1..3 ชิ้น)
        public ItemTier minFodderTier;  // ระดับวัตถุดิบขั้นต่ำ (ปลดล็อกเป็น Normal เสมอ)
        public float successRate;       // อัตราสำเร็จพื้นฐาน (0..1)
        public float statBonusPercent;  // ฟิลด์เดิมสำหรับความเข้ากันได้ (คงค่า 0 เพื่อเลิกใช้ระบบ %)
        public int flatPrimaryGain;     // โบนัสสเตตัสหลักแบบจำนวนเต็มที่เพิ่มขึ้นต่อ 1 ขั้น (+1 ถึง +2)
    }

    // โบนัส Pity (Failstack) ต่อการตีบวกพลาด 1 ครั้งบนอุปกรณ์ชิ้นนั้น (+6%/ครั้ง สูงสุด +60%) รีเซ็ตเป็น 0 เมื่อตีติด
    public const float FailstackBonusPerFail = 0.06f;
    public const float MaxFailstackBonus = 0.60f;

    // ตารางค่าใช้จ่าย Gold พื้นฐานที่ปรับสมดุลใหม่ (+1 ถึง +10) ลดความตึงช่วง +7..+10 ให้ฟาร์มได้สนุกไม่ท้อ
    private static readonly int[] GoldCostTable =
    {
        40,    // +0 -> +1
        75,    // +1 -> +2
        120,   // +2 -> +3
        190,   // +3 -> +4
        290,   // +4 -> +5
        420,   // +5 -> +6
        600,   // +6 -> +7
        820,   // +7 -> +8
        1100,  // +8 -> +9
        1450   // +9 -> +10
    };

    // ตารางโอกาสตีบวกสำเร็จพื้นฐานที่ปรับสมดุลใหม่ (22% ที่ +9->+10 พร้อมระบบ Pity Failstack +6%/ครั้ง)
    private static readonly float[] SuccessRateTable =
    {
        1.00f, // +0 -> +1 (100%)
        0.95f, // +1 -> +2 (95%)
        0.88f, // +2 -> +3 (88%)
        0.78f, // +3 -> +4 (78%)
        0.68f, // +4 -> +5 (68%)
        0.58f, // +5 -> +6 (58%)
        0.48f, // +6 -> +7 (48%)
        0.38f, // +7 -> +8 (38%)
        0.28f, // +8 -> +9 (28%)
        0.22f  // +9 -> +10 (22%)
    };

    /// <summary>
    /// คืนค่าเพดาน ATK สูงสุดที่อนุญาตสำหรับอุปกรณ์ป้องกันตามระดับความหายาก (Role Separation Cap):
    /// Normal/Rare = 0, Epic = 1, Legendary = 2
    /// </summary>
    public static int GetDefensiveGearMaxAtkCap(ItemTier tier)
    {
        return tier switch
        {
            ItemTier.Epic => 1,
            ItemTier.Legendary => 2,
            _ => 0
        };
    }

    /// <summary>
    /// คืนค่าจำนวน ATK ที่เพิ่มต่อ 1 ขั้นตีบวกสำหรับอาวุธ (Normal/Rare = +1/ขั้น, Epic/Legendary = +2/ขั้น)
    /// </summary>
    public static int GetWeaponAtkPerLevel(ItemTier tier)
    {
        return tier >= ItemTier.Epic ? 2 : 1;
    }

    /// <summary>
    /// ดึงข้อมูลเงื่อนไขการตีบวกขั้นถัดไป (ปลดล็อกให้ใช้วัตถุดิบได้ตั้งแต่ระดับ Normal ทุกขั้น)
    /// </summary>
    public static EnhancementSpec GetNextSpec(ItemData targetItem, int currentEnhancementLevel)
    {
        int nextLvl = Mathf.Clamp(currentEnhancementLevel + 1, 1, MaxEnhancementLevel);
        int idx = nextLvl - 1;

        int tierMultiplier = targetItem != null ? ((int)targetItem.tier + 1) : 1;
        // ตัวคูณราคาตาม Tier แบบสมดุล: Normal 1.00x, Rare 1.15x, Epic 1.30x, Legendary 1.45x
        int goldCost = Mathf.RoundToInt(GoldCostTable[idx] * (0.85f + 0.15f * tierMultiplier));

        // จำนวนวัตถุดิบ: +1..+3 ใช้ 1 ชิ้น, +4..+7 ใช้ 2 ชิ้น, +8..+10 ใช้ 3 ชิ้น
        int fodderCount = nextLvl <= 3 ? 1 : (nextLvl <= 7 ? 2 : 3);

        // คำนวณโบนัสจำนวนเต็มหลักที่เพิ่มขึ้นในขั้นนี้ (+1 ถึง +2)
        int flatGain = 1;
        if (targetItem != null)
        {
            flatGain = targetItem.GetEquipmentSlot() == EquipmentSlotType.Sword
                ? GetWeaponAtkPerLevel(targetItem.tier)
                : 2; // อุปกรณ์ป้องกันเพิ่ม MaxHP +2 ต่อขั้น และ DEF/STA +1 ทุก 2 ขั้น
        }

        return new EnhancementSpec
        {
            targetLevel = nextLvl,
            goldCost = goldCost,
            requiredFodderCount = fodderCount,
            minFodderTier = ItemTier.Normal, // อนุญาตให้ใช้อุปกรณ์ทุกระดับ (Normal ขึ้นไป) เป็นวัตถุดิบได้เสมอ
            successRate = SuccessRateTable[idx],
            statBonusPercent = 0f,           // ยกเลิกโบนัสแบบเปอร์เซ็นต์ทั้งหมด
            flatPrimaryGain = flatGain
        };
    }

    /// <summary>
    /// คืนค่าโบนัสโอกาสตีบวกจากระบบ Pity (Failstack) ตามจำนวนครั้งที่ตีพลาดสะสม (+6% ต่อครั้ง สูงสุด +60%)
    /// </summary>
    public static float GetFailstackBonus(int failStacks)
    {
        if (failStacks <= 0) return 0f;
        return Mathf.Clamp(failStacks * FailstackBonusPerFail, 0f, MaxFailstackBonus);
    }

    /// <summary>
    /// คำนวณอัตราความสำเร็จ (%) แบบไดนามิกตามระดับความหายากของวัตถุดิบที่เลือกเทียบกับอุปกรณ์เป้าหมาย + โบนัส Pity Failstack
    /// </summary>
    public static float CalculateDynamicSuccessRate(
        ItemData targetItem,
        int currentEnhancementLevel,
        IReadOnlyList<ItemData> selectedFodderItems,
        int failStacks = 0)
    {
        var spec = GetNextSpec(targetItem, currentEnhancementLevel);
        float baseRate = spec.successRate;
        float pityBonus = GetFailstackBonus(failStacks);
        if (targetItem == null || selectedFodderItems == null || selectedFodderItems.Count == 0)
            return Mathf.Clamp(baseRate + pityBonus, 0.05f, 1.00f);

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

        if (validCount == 0) return Mathf.Clamp(baseRate + pityBonus, 0.05f, 1.00f);
        float avgFactor = sumFactor / validCount;
        return Mathf.Clamp(baseRate * avgFactor + pityBonus, 0.05f, 1.00f);
    }

    /// <summary>
    /// คืนค่าตัวคูณ 1.0 คงที่ (ยกเลิกระบบคูณเปอร์เซ็นต์แล้ว เปลี่ยนไปใช้โบนัสจำนวนเต็ม Flat Scaling แทน)
    /// </summary>
    public static float GetStatMultiplier(int enhancementLevel)
    {
        return 1f;
    }

    /// <summary>
    /// คำนวณ ATK หลังตีบวกแบบจำนวนเต็มคงที่ (Flat Integer):
    ///   - อาวุธ (Sword): +1 ATK/ขั้น (Normal/Rare) หรือ +2 ATK/ขั้น (Epic/Legendary)
    ///   - อุปกรณ์ป้องกัน (Shield/Armor/Pants): ไม่เพิ่ม ATK และบังคับเพดานสูงสุดตาม Role Cap (Normal/Rare=0, Epic<=1, Legendary<=2)
    /// </summary>
    public static int ScaleAttackStat(int baseValue, int enhancementLevel, EquipmentSlotType slot, ItemTier tier)
    {
        int clamped = Mathf.Clamp(enhancementLevel, 0, MaxEnhancementLevel);
        if (slot != EquipmentSlotType.Sword)
        {
            // อุปกรณ์ป้องกันห้ามเพิ่ม ATK จากการตีบวก และถูกจำกัดเพดานตาม Tier เสมอ
            int cap = GetDefensiveGearMaxAtkCap(tier);
            return Mathf.Clamp(baseValue, 0, cap);
        }

        if (baseValue <= 0 || clamped <= 0) return Mathf.Max(0, baseValue);
        return baseValue + clamped * GetWeaponAtkPerLevel(tier);
    }

    /// <summary>
    /// คำนวณ MaxHP หลังตีบวกแบบจำนวนเต็มคงที่: เพิ่ม +2 MaxHP ต่อ 1 ขั้นตีบวก (เฉพาะไอเทมที่มีค่า HP ตั้งต้น > 0)
    /// </summary>
    public static int ScaleHealthStat(int baseValue, int enhancementLevel)
    {
        int clamped = Mathf.Clamp(enhancementLevel, 0, MaxEnhancementLevel);
        if (baseValue <= 0 || clamped <= 0) return Mathf.Max(0, baseValue);
        return baseValue + clamped * 2;
    }

    /// <summary>
    /// คำนวณ MaxStamina หลังตีบวกแบบจำนวนเต็มคงที่: เพิ่ม +1 MaxSTA ทุก 2 ขั้นตีบวก (+1 ที่ +2, +4, +6, +8, +10)
    /// </summary>
    public static int ScaleStaminaStat(int baseValue, int enhancementLevel)
    {
        int clamped = Mathf.Clamp(enhancementLevel, 0, MaxEnhancementLevel);
        if (baseValue <= 0 || clamped <= 0) return Mathf.Max(0, baseValue);
        return baseValue + (clamped / 2);
    }

    /// <summary>
    /// คำนวณ Defense หลังตีบวกแบบจำนวนเต็มคงที่: เพิ่ม +1 DEF ทุก 2 ขั้นตีบวก (เริ่มได้ +1 ตั้งแต่ +1 -> +5 ที่ +9/+10)
    /// </summary>
    public static int ScaleDefenseStat(int baseValue, int enhancementLevel)
    {
        int clamped = Mathf.Clamp(enhancementLevel, 0, MaxEnhancementLevel);
        if (baseValue <= 0 || clamped <= 0) return Mathf.Max(0, baseValue);
        return baseValue + ((clamped + 1) / 2);
    }

    /// <summary>
    /// คำนวณ MoveSpeed หลังตีบวกแบบคงที่ (+0.02 m/s ต่อ 1 ขั้นตีบวก เฉพาะอุปกรณ์ที่เพิ่มความเร็วอยู่แล้ว)
    /// </summary>
    public static float ScaleMoveSpeedStat(float baseValue, int enhancementLevel)
    {
        int clamped = Mathf.Clamp(enhancementLevel, 0, MaxEnhancementLevel);
        if (baseValue <= 0f || clamped <= 0) return Mathf.Max(0f, baseValue);
        return Mathf.Round((baseValue + clamped * 0.02f) * 100f) * 0.01f;
    }

    /// <summary>
    /// เมธอดจำนวนเต็มมาตรฐานสำหรับโค้ดทั่วไป (เพิ่ม +1 คงที่ต่อขั้นตีบวก ไม่มีตัวคูณ %)
    /// </summary>
    public static int ScaleIntStat(int baseValue, int enhancementLevel)
    {
        int clamped = Mathf.Clamp(enhancementLevel, 0, MaxEnhancementLevel);
        if (baseValue <= 0 || clamped <= 0) return Mathf.Max(0, baseValue);
        return baseValue + clamped;
    }

    /// <summary>
    /// เมธอดทศนิยมมาตรฐาน (เรียกใช้ระบบเพิ่มคงที่ +0.02 ต่อขั้น แทนการคูณ %)
    /// </summary>
    public static float ScaleFloatStat(float baseValue, int enhancementLevel)
    {
        return ScaleMoveSpeedStat(baseValue, enhancementLevel);
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
