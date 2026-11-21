using System;
using System.Collections.Generic;
using UnityEngine;

public enum MonsterLootRollMode
{
    /// <summary>Each entry rolls independently against its Drop Rate (%). If all miss, nothing drops.</summary>
    IndependentPerItem = 0,
    /// <summary>Rolls 0..100 once across the table. Any remaining percentage up to 100% drops nothing.</summary>
    SinglePickTable = 1
}

[Serializable]
public class MonsterLootDropEntry
{
    [Tooltip("Drag any ItemData asset from the project here.")]
    public ItemData item;
    [Tooltip("Chance to drop in percent (0% = never, 100% = guaranteed).")]
    [Range(0f, 100f)] public float dropRatePercent = 25f;
    [Min(1)] public int minAmount = 1;
    [Min(1)] public int maxAmount = 1;
}

/// <summary>
/// Per-monster animation, species stat scaling, combat timing, loose-coupled loot drop table,
/// hit-flash, and optional 3D audio configuration asset.
/// 100% self-contained inside Assets/MonsterSystem — rebuilding prefabs NEVER wipes your tuned values or loot!
/// </summary>
[CreateAssetMenu(fileName = "Config_Monster", menuName = "Monster System/Monster Animation Config")]
public class MonsterAnimationConfig : ScriptableObject
{
    [Header("1. ชื่อและระดับมอนสเตอร์")]
    public string monsterName = "Monster";
    [Tooltip("ติ๊กถ้ามอนตัวนี้เป็นบอส (เปิด Super Armor ตีไม่สะดุ้งอัตโนมัติ)")]
    public bool isBoss = false;

    [Header("2. ตัวคูณสเตตัสเฉพาะสายพันธุ์ (คูณเพิ่มจากเลเวลผู้เล่น)")]
    [Tooltip("ตัวคูณเลือด (เช่น 1.67 จะได้เลือด ~200 ที่ Lv.1 และยังโตตามเลเวลผู้เล่น)")]
    [Min(0.2f)] public float healthMultiplier = 1.67f;
    [Tooltip("ตัวคูณพลังโจมตี (เช่น 1.50 จะได้โจมตี 15 ที่ Lv.1)")]
    [Min(0.2f)] public float damageMultiplier = 1.50f;
    [Tooltip("พลังป้องกันเสริมประจำสายพันธุ์")]
    [Min(0)] public int defenseBonus = 1;

    [Header("3. ระยะตี & จังหวะดาเมจลง")]
    [Tooltip("ระยะโจมตีประชิด (เมตร)")]
    [Min(1.0f)] public float attackRange = 2.5f;
    [Tooltip("วินาทีตั้งแต่เริ่มง้างจนแขนฟันโดน (ช่วงนี้ผู้เล่นกลิ้งหลบได้)")]
    [Min(0.05f)] public float attackImpactDelay = 0.40f;
    [Min(0.05f)] public float counterImpactDelay = 0.36f;

    [Header("4. ตารางของดรอป (ลาก ItemData ของเพื่อนมาใส่ + ปรับเรท %)")]
    [Tooltip("IndependentPerItem = ทอยแยกทีละชิ้น (ไม่ติดเลย = ไม่ออก) | SinglePickTable = ทอยเลือก 1 ชิ้นจาก 100%")]
    public MonsterLootRollMode lootRollMode = MonsterLootRollMode.IndependentPerItem;
    [Tooltip("จำกัดจำนวนชิ้นสูงสุดที่ดรอปได้ต่อการฆ่า 1 ตัวในโหมด Independent (0 = ไม่จำกัด)")]
    [Min(0)] public int maxDropsPerKill = 0;
    public List<MonsterLootDropEntry> lootDrops = new List<MonsterLootDropEntry>();

    [Header("5. ดรอปเงิน Gold เสริม (ตั้ง 0% = ปิดไว้)")]
    [Range(0f, 100f)] public float goldDropChancePercent = 0f;
    [Min(0)] public int minGold = 5;
    [Min(0)] public int maxGold = 15;

    [Header("6. การสะดุ้ง / Super Armor")]
    [Tooltip("มอนทั่วไปจะสะดุ้งตอนโดนตี ถ้าเป็นบอสจะไม่สะดุ้ง")]
    public bool canFlinch = true;
    [Min(0f)] public float hitReactCooldown = 0.25f;

    [Header("7. จำนวนท่าโจมตี / สกิล")]
    [Range(1, 3)] public int attackVariantCount = 2;
    [Range(1, 3)] public int skillVariantCount = 1;

    [Header("8. การเดิน & ล็อกสะโพกกันสไลด์")]
    [Min(0.01f)] public float speedDampTime = 0.14f;
    [Min(0.1f)] public float walkSpeedThreshold = 1.6f;
    [Min(0.5f)] public float runSpeedThreshold = 4.2f;
    [Min(0.01f)] public float moveStopEpsilon = 0.05f;
    [Tooltip("ล็อกแกน XZ ของสะโพกตอนยังมีชีวิต กันอนิเมชันสไลด์หลุดตัว")]
    public bool lockHipsXZInPlace = true;

    [Header("9. แสงกระพริบตอนโดนตี / บล็อก")]
    public bool enableHitFlash = true;
    [Min(0.02f)] public float hitFlashDuration = 0.11f;
    public Color hitFlashEmissionColor = new Color(0.85f, 0.25f, 0.20f, 1f);
    public Color blockFlashEmissionColor = new Color(1.0f, 0.72f, 0.18f, 1f);

    [Header("10. ความสูงโมเดลในโลกจริง (เมตร)")]
    [Min(0.5f)] public float targetWorldHeight = 2.15f;

    [Header("11. เสียง 3D (ไม่บังคับ — เว้นว่างได้ไม่ Error)")]
    [Range(0f, 1f)] public float audioVolume = 0.85f;
    public AudioClip[] attackSwingClips;
    public AudioClip[] hitReactClips;
    public AudioClip guardRaiseClip;
    public AudioClip guardBlockClip;
    public AudioClip guardBreakClip;
    public AudioClip deathClip;

    /// <summary>Effective flinch flag: Bosses never flinch even if canFlinch was left true.</summary>
    public bool EffectiveCanFlinch => !isBoss && canFlinch;

    /// <summary>Computes exact probability (0..100%) that killing this monster drops NO items.</summary>
    public float CalculateNoDropProbabilityPercent(float dropRateMultiplier = 1f)
    {
        if (lootDrops == null || lootDrops.Count == 0 || dropRateMultiplier <= 0f)
            return 100f;

        if (lootRollMode == MonsterLootRollMode.SinglePickTable)
        {
            float sum = 0f;
            for (int i = 0; i < lootDrops.Count; i++)
            {
                var e = lootDrops[i];
                if (e == null || e.item == null || e.dropRatePercent <= 0f) continue;
                sum += Mathf.Max(0f, e.dropRatePercent * dropRateMultiplier);
            }
            return Mathf.Clamp(100f - sum, 0f, 100f);
        }

        double missProb = 1.0;
        bool hasAnyValid = false;
        for (int i = 0; i < lootDrops.Count; i++)
        {
            var e = lootDrops[i];
            if (e == null || e.item == null || e.dropRatePercent <= 0f) continue;
            hasAnyValid = true;
            double p = Mathf.Clamp01((e.dropRatePercent * dropRateMultiplier) / 100f);
            missProb *= (1.0 - p);
        }
        return hasAnyValid ? (float)(missProb * 100.0) : 100f;
    }

    /// <summary>Returns the sum of valid item drop rates (useful to warn if SinglePickTable exceeds 100%).</summary>
    public float CalculateTotalConfiguredPercent()
    {
        if (lootDrops == null) return 0f;
        float sum = 0f;
        for (int i = 0; i < lootDrops.Count; i++)
        {
            var e = lootDrops[i];
            if (e == null || e.item == null || e.dropRatePercent <= 0f) continue;
            sum += e.dropRatePercent;
        }
        return sum;
    }
}
