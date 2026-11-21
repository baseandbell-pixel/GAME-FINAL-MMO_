using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// ระบบคำนวณอัตราดรอปไอเทมตามสัดส่วนดาเมจที่ทำได้ (Damage-Contribution Weighted Personal Loot System)
/// ปรับสมดุล Progression ใหม่ให้ไอเทมหายากมีคุณค่าและกระเป๋าไม่ล้น:
///   1. มอนสเตอร์ทั่วไปมีโอกาสดรอปไอเทม ~26%–34% (เฉลี่ย 30%) แทนการดรอป 100% ทุกตัว (แต่ยังคงได้รับ EXP ทุกครั้ง)
///   2. เมื่อดรอปจากมอนสเตอร์ทั่วไปจะเน้นระดับ Normal (74/20/5/1%) ส่วนหีบสมบัติแคมป์ใช้ 62/25/10/3% และบอสใช้ 35/40/18/7%
///   3. ผู้ที่ทำดาเมจสูงสุด (Top DPS) ได้โบนัสตัวคูณโอกาสออกระดับ Epic และ Legendary สูงขึ้นตามสัดส่วนดาเมจ
/// </summary>
public static class LootDropSystem
{
    // โอกาสพื้นฐานที่มอนสเตอร์ทั่วไปจะดรอปไอเทม 1 ชิ้นเมื่อตาย (30% — อยู่ในช่วง 25%–35%)
    public const float NormalEnemyDropChance = 0.30f;

    /// <summary>
    /// โครงสร้างเก็บค่าโอกาสออกไอเทมแต่ละระดับ (0..1 รวมกันได้ 1.0) สำหรับผู้เล่นแต่ละคน
    /// </summary>
    public struct WeightedTierOdds
    {
        public float normalChance;
        public float rareChance;
        public float epicChance;
        public float legendaryChance;
        public float damageShare;
        public bool isTopDps;
    }

    /// <summary>
    /// คำนวณโอกาสที่มอนสเตอร์ทั่วไปจะดรอปไอเทมให้ผู้เล่นคนนี้ (26% ถึง 34% ตามสัดส่วนดาเมจและ Top DPS)
    /// </summary>
    public static float GetNormalEnemyDropChance(float damageShare, bool isTopDps)
    {
        float clampedShare = Mathf.Clamp01(damageShare);
        float chance = Mathf.Lerp(0.26f, 0.32f, clampedShare);
        if (isTopDps) chance += 0.02f;
        return Mathf.Clamp(chance, 0.25f, 0.35f);
    }

    /// <summary>
    /// สุ่มตรวจสอบว่ามอนสเตอร์ทั่วไปตัวนี้จะดรอปไอเทมให้ผู้เล่นหรือไม่ (EXP จะยังได้รับเสมอแม้ไม่ดรอปไอเทม)
    /// </summary>
    public static bool RollNormalEnemyShouldDrop(float damageShare, bool isTopDps, float? fixedRoll01 = null)
    {
        float chance = GetNormalEnemyDropChance(damageShare, isTopDps);
        float roll = fixedRoll01.HasValue ? Mathf.Clamp01(fixedRoll01.Value) : Random.value;
        return roll < chance;
    }

    /// <summary>
    /// คำนวณโอกาสออกไอเทมแต่ละระดับ (Normal, Rare, Epic, Legendary) จากสัดส่วนดาเมจของผู้เล่น
    /// </summary>
    /// <param name="isBoss">เป็นบอสหรือหีบบอสหรือไม่</param>
    /// <param name="damageShare">สัดส่วนดาเมจที่ผู้เล่นทำได้ (0.0 ถึง 1.0)</param>
    /// <param name="isTopDps">เป็นผู้เล่นที่ทำดาเมจสูงสุดในการต่อสู้นี้หรือไม่</param>
    /// <param name="isChest">เป็นหีบสมบัติหรือไม่ (true = ตารางหีบแคมป์ 62/25/10/3%, false = ตารางมอนสเตอร์ 74/20/5/1%)</param>
    public static WeightedTierOdds CalculatePlayerTierOdds(bool isBoss, float damageShare, bool isTopDps, bool isChest = false)
    {
        float clampedShare = Mathf.Clamp01(damageShare);

        // ตัวคูณโอกาสได้ของระดับสูง (Epic / Legendary) แบบปรับสมดุลใหม่:
        // - Top DPS: ได้โบนัสตัวคูณ 1.25x ถึง 1.50x สำหรับ Legendary และ 1.18x ถึง 1.35x สำหรับ Epic
        // - ผู้ทำดาเมจรอง: ได้ 0.55x ถึง 1.00x ตามสัดส่วนดาเมจ (ยังมีโอกาสได้ของแรร์จริงแต่ไม่เฟ้อ)
        float legendaryMult = isTopDps
            ? Mathf.Lerp(1.25f, 1.50f, clampedShare)
            : Mathf.Lerp(0.55f, 1.00f, clampedShare);

        float epicMult = isTopDps
            ? Mathf.Lerp(1.18f, 1.35f, clampedShare)
            : Mathf.Lerp(0.62f, 1.00f, clampedShare);

        if (isBoss)
        {
            // บอสและหีบบอส: พื้นฐาน Normal 35%, Rare 40%, Epic 18%, Legendary 7%
            float leg = Mathf.Clamp(ItemRegistry.BossLegendaryChance * legendaryMult, 0.03f, 0.14f);
            float epic = Mathf.Clamp(ItemRegistry.BossEpicChance * epicMult, 0.10f, 0.28f);
            float rare = Mathf.Clamp(ItemRegistry.BossRareChance * Mathf.Lerp(0.90f, 1.06f, clampedShare), 0.30f, 0.46f);
            float normal = Mathf.Max(0.15f, 1f - (leg + epic + rare));

            float sum = normal + rare + epic + leg;
            return new WeightedTierOdds
            {
                normalChance = normal / sum,
                rareChance = rare / sum,
                epicChance = epic / sum,
                legendaryChance = leg / sum,
                damageShare = clampedShare,
                isTopDps = isTopDps
            };
        }
        else if (isChest)
        {
            // หีบสมบัติแคมป์: พื้นฐาน Normal 62%, Rare 25%, Epic 10%, Legendary 3%
            float leg = Mathf.Clamp(ItemRegistry.LegendaryChance * legendaryMult, 0.015f, 0.06f);
            float epic = Mathf.Clamp(ItemRegistry.EpicChance * epicMult, 0.055f, 0.15f);
            float rare = Mathf.Clamp(ItemRegistry.RareChance * Mathf.Lerp(0.88f, 1.08f, clampedShare), 0.18f, 0.30f);
            float normal = Mathf.Max(0.42f, 1f - (leg + epic + rare));

            float sum = normal + rare + epic + leg;
            return new WeightedTierOdds
            {
                normalChance = normal / sum,
                rareChance = rare / sum,
                epicChance = epic / sum,
                legendaryChance = leg / sum,
                damageShare = clampedShare,
                isTopDps = isTopDps
            };
        }
        else
        {
            // มอนสเตอร์ทั่วไป (เมื่อผ่านการสุ่มโอกาสดรอป ~30% แล้ว): พื้นฐาน Normal 74%, Rare 20%, Epic 5%, Legendary 1%
            float leg = Mathf.Clamp(ItemRegistry.EnemyLegendaryChance * legendaryMult, 0.004f, 0.02f);
            float epic = Mathf.Clamp(ItemRegistry.EnemyEpicChance * epicMult, 0.025f, 0.08f);
            float rare = Mathf.Clamp(ItemRegistry.EnemyRareChance * Mathf.Lerp(0.88f, 1.10f, clampedShare), 0.14f, 0.25f);
            float normal = Mathf.Max(0.58f, 1f - (leg + epic + rare));

            // ปรับผลรวมให้เท่ากับ 1.0 พอดี
            float sum = normal + rare + epic + leg;
            return new WeightedTierOdds
            {
                normalChance = normal / sum,
                rareChance = rare / sum,
                epicChance = epic / sum,
                legendaryChance = leg / sum,
                damageShare = clampedShare,
                isTopDps = isTopDps
            };
        }
    }

    /// <summary>
    /// สุ่มไอเทม 1 ชิ้นโดยถ่วงน้ำหนักตามสัดส่วนดาเมจของผู้เล่น (Damage-Contribution Weighted Roll)
    /// </summary>
    public static ItemData RollDamageWeightedLoot(
        bool isBoss,
        float damageShare,
        bool isTopDps,
        out ItemTier rolledTier,
        out WeightedTierOdds appliedOdds,
        float? fixedRoll01 = null,
        bool isChest = false)
    {
        appliedOdds = CalculatePlayerTierOdds(isBoss, damageShare, isTopDps, isChest);
        float roll = fixedRoll01.HasValue ? Mathf.Clamp01(fixedRoll01.Value) : Random.value;

        // เรียงตรวจสอบจาก Normal -> Rare -> Epic -> Legendary เพื่อให้ค่า roll ที่สูงออกระดับสูงตามมาตรฐาน
        float normalAndRare = appliedOdds.normalChance + appliedOdds.rareChance;
        float normalRareEpic = normalAndRare + appliedOdds.epicChance;

        if (roll < appliedOdds.normalChance)
            rolledTier = ItemTier.Normal;
        else if (roll < normalAndRare)
            rolledTier = ItemTier.Rare;
        else if (roll < normalRareEpic)
            rolledTier = ItemTier.Epic;
        else
            rolledTier = ItemTier.Legendary;

        return ItemRegistry.GetRandomItemOfTier(rolledTier);
    }

    /// <summary>
    /// คำนวณผู้เล่นที่ทำดาเมจสูงสุด (Top DPS) และดาเมจรวมทั้งหมดจากตารางบันทึกดาเมจ
    /// </summary>
    public static void AnalyzeDamageContribution(
        IReadOnlyDictionary<PlayerRef, float> damageMap,
        PlayerRef fallbackKiller,
        out float totalDamage,
        out PlayerRef topDpsPlayer,
        out List<PlayerRef> participants)
    {
        totalDamage = 0f;
        topDpsPlayer = fallbackKiller;
        float highest = -1f;
        participants = new List<PlayerRef>();

        if (damageMap != null)
        {
            foreach (var kv in damageMap)
            {
                if (kv.Key == PlayerRef.None || kv.Value <= 0f) continue;
                totalDamage += kv.Value;
                participants.Add(kv.Key);
                if (kv.Value > highest)
                {
                    highest = kv.Value;
                    topDpsPlayer = kv.Key;
                }
            }
        }

        // กรณีไม่มีข้อมูลในตาราง (เช่น สั่งฆ่าผ่านการทดสอบ) ให้ผู้สังหารได้รับสิทธิ์ 100%
        if (participants.Count == 0 && fallbackKiller != PlayerRef.None)
        {
            participants.Add(fallbackKiller);
            totalDamage = 100f;
            topDpsPlayer = fallbackKiller;
        }
    }
}
