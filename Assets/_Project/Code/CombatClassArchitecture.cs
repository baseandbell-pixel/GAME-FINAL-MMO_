using UnityEngine;

/// <summary>
/// โครงสร้างสถาปัตยกรรมสำหรับระบบคลาสตัวละครและประเภทอาวุธ (รองรับการขยายคลาส Magic ในอนาคตโดยไม่ต้องรื้อระบบเดิม)
/// </summary>
public enum CombatClassType
{
    // คลาสนักดาบใหญ่ (คลาสหลักปัจจุบัน)
    GreatSword = 0,
    // คลาสจอมเวท (เตรียมสถาปัตยกรรมรองรับในอนาคต)
    Magic = 1
}

/// <summary>
/// ประเภทของอาวุธประจำคลาส เพื่อใช้แยกอนิเมชันและรูปแบบการโจมตี
/// </summary>
public enum WeaponArchetype
{
    // ดาบใหญ่สองมือ (GreatSword)
    GreatSword = 0,
    // คทาเวทมนตร์ (Magic Staff)
    MagicStaff = 1,
    // ตำราเวทโบราณ (Grimoire / Spellbook)
    Grimoire = 2,
    // ลูกแก้ววิญญาণ (Spirit Orb)
    SpiritOrb = 3,
    // ยันต์หยกอาคม (Jade Talisman)
    JadeTalisman = 4
}

/// <summary>
/// รูปแบบท่าโจมตีของผู้เล่น เพื่อสั่งเล่นอนิเมชันใน playerAnimator ให้ครบทุกท่า
/// </summary>
public enum PlayerAttackKind : byte
{
    // โจมตีปกติคอมโบ 3 จังหวะ (Light Attack 1 -> 2 -> 3)
    LightCombo = 0,
    // โจมตีหนักคอมโบ (Heavy Attack / Heavy Slash 1 -> 2 -> 3)
    HeavyCombo = 1,
    // วิ่งโจมตีเบา (Run Attack)
    RunAttack = 2,
    // วิ่งกระโดดฟันหนัก (Run Heavy Attack)
    RunHeavyAttack = 3,
    // กระโดดโจมตีกลางอากาศ (Jump Attack)
    JumpAttack = 4,
    // กระโดดฟันหนักกลางอากาศ (Jump Heavy Attack)
    JumpHeavyAttack = 5,
    // กระโดดฟันหนักขณะวิ่ง (Run Jump Heavy Attack)
    RunJumpHeavyAttack = 6,
    // ท่าลอบสังหารจากด้านหลัง (Stealth Assassination Strike)
    StealthAssassination = 7,
    // ท่าร่ายเวทมนตร์สำหรับคลาส Magic ในอนาคต (Spell Cast)
    SpellCast = 8
}

/// <summary>
/// อินเทอร์เฟซมาตรฐานสำหรับตัวจัดการระบบต่อสู้ของแต่ละคลาส (GreatSword / Magic)
/// ช่วยให้ระบบ Inventory, Save และ Networking เรียกใช้งานได้โดยไม่ยึดติดกับคลาสใดคลาสหนึ่ง
/// </summary>
public interface IClassCombatHandler
{
    // ประเภทคลาสปัจจุบันของผู้เล่น
    CombatClassType ActiveClass { get; }

    // ประเภทอาวุธที่ถืออยู่ในปัจจุบัน
    WeaponArchetype ActiveWeaponArchetype { get; }

    // ตรวจสอบว่าสามารถกดโจมตีได้หรือไม่
    bool CanPerformPrimaryAttack();

    // สั่งโจมตีหลัก (เช่น ฟันดาบ หรือ ยิงเวท)
    bool TryPerformPrimaryAttack();

    // สั่งใช้ท่าพิเศษ/ท่ารอง (เช่น บล็อก/โจมตีหนัก หรือ ร่ายเวทบทใหญ่)
    bool TryPerformSecondaryAction();
}
