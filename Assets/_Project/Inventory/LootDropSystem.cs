using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// ระบบคำนวณอัตราดรอปไอเทมตามสัดส่วนดาเมจที่ทำได้ (Damage-Contribution Weighted Personal Loot System)
/// แก้ไขปัญหาแย่งของระหว่าง Host กับ Client โดย:
///   1. ผู้เล่นทุกคนที่มีส่วนร่วมทำดาเมจใส่มอนสเตอร์หรือบอส จะได้รับไอเทมดรอปส่วนตัวแยกกัน (Personal Instanced Loot)
///   2. ผู้ที่ทำดาเมจสูงสุด (Top DPS) จะได้โบนัสโอกาสออกไอเทมระดับ Epic และ Legendary สูงที่สุด
///   3. ผู้ที่ทำดาเมจรองลงมา ยังคงมีโอกาสได้ลุ้นไอเทมระดับ Epic และ Legendary จริง (ไม่เป็น 0%) แต่มีเปอร์เซ็นต์ลดหลั่นตามสัดส่วนดาเมจ
/// </summary>
public static class LootDropSystem
{
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
    /// คำนวณโอกาสออกไอเทมแต่ละระดับ (Normal, Rare, Epic, Legendary) จากสัดส่วนดาเมจของผู้เล่น
    /// </summary>
    /// <param name="isBoss">เป็นบอสหรือไม่</param>
    /// <param name="damageShare">สัดส่วนดาเมจที่ผู้เล่นทำได้ (0.0 ถึง 1.0)</param>
    /// <param name="isTopDps">เป็นผู้เล่นที่ทำดาเมจสูงสุดในการต่อสู้นี้หรือไม่</param>
    public static WeightedTierOdds CalculatePlayerTierOdds(bool isBoss, float damageShare, bool isTopDps)
    {
        float clampedShare = Mathf.Clamp01(damageShare);

        // ตัวคูณโอกาสได้ของระดับสูง (Epic / Legendary):
        // - หากเป็น Top DPS: ได้รับตัวคูณโบนัสสูงสุด (1.35x ถึง 1.65x สำหรับ Legendary และ 1.25x ถึง 1.45x สำหรับ Epic)
        // - หากเป็นผู้ทำดาเมจรอง: ยังคงมีโอกาสออก Epic / Legendary จริงเสมอ (0.55x ถึง 1.10x ตามสัดส่วนดาเมจ)
        float legendaryMult = isTopDps
            ? Mathf.Lerp(1.35f, 1.65f, clampedShare)
            : Mathf.Lerp(0.55f, 1.10f, clampedShare);

        float epicMult = isTopDps
            ? Mathf.Lerp(1.25f, 1.45f, clampedShare)
            : Mathf.Lerp(0.60f, 1.10f, clampedShare);

        if (isBoss)
        {
            // บอส: พื้นฐาน Rare 30%, Epic 45%, Legendary 25%
            float leg = Mathf.Clamp(ItemRegistry.BossLegendaryChance * legendaryMult, 0.08f, 0.50f);
            float epic = Mathf.Clamp(ItemRegistry.BossEpicChance * epicMult, 0.20f, 0.65f);
            if (leg + epic > 0.90f)
            {
                float scale = 0.90f / (leg + epic);
                leg *= scale;
                epic *= scale;
            }
            float rare = Mathf.Max(0.05f, 1f - (leg + epic));

            return new WeightedTierOdds
            {
                normalChance = 0f,
                rareChance = rare,
                epicChance = epic,
                legendaryChance = leg,
                damageShare = clampedShare,
                isTopDps = isTopDps
            };
        }
        else
        {
            // มอนสเตอร์ทั่วไป / แคมป์: พื้นฐาน Normal 50%, Rare 30%, Epic 15%, Legendary 5%
            float leg = Mathf.Clamp(ItemRegistry.LegendaryChance * legendaryMult, 0.02f, 0.20f);
            float epic = Mathf.Clamp(ItemRegistry.EpicChance * epicMult, 0.06f, 0.32f);
            float rare = Mathf.Clamp(ItemRegistry.RareChance * Mathf.Lerp(0.85f, 1.15f, clampedShare), 0.20f, 0.40f);
            float normal = Mathf.Max(0.10f, 1f - (leg + epic + rare));

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
        float? fixedRoll01 = null)
    {
        appliedOdds = CalculatePlayerTierOdds(isBoss, damageShare, isTopDps);
        float roll = fixedRoll01.HasValue ? Mathf.Clamp01(fixedRoll01.Value) : Random.value;

        // เรียงตรวจสอบจากระดับ Legendary -> Epic -> Rare -> Normal เพื่อให้ค่า fixedRoll01 ที่สูงออกระดับสูงตามมาตรฐานเดิม
        float normalAndRare = appliedOdds.normalChance + appliedOdds.rareChance;
        float normalRareEpic = normalAndRare + appliedOdds.epicChance;

        if (roll < appliedOdds.normalChance && !isBoss)
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
