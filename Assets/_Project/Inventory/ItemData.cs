using UnityEngine;

// ประเภทของไอเทมในเกมทั้งหมด
public enum ItemType
{
    Consumable = 0,       // ไอเทมกดใช้ฟื้นฟู/บัฟ
    Weapon = 1,           // อาวุธ (ช่อง Sword)
    Equipment = 2,        // อุปกรณ์เกราะ (Legacy mapping -> Armor)
    QuestItem = 3,        // ไอเทมเควสต์
    CraftingMaterial = 4, // วัตถุดิบ
    Shield = 5,           // โล่ป้องกัน (ช่อง Shield)
    Armor = 6,            // เกราะลำตัว (ช่อง Armor)
    Pants = 7             // กางเกง/เกราะขา (ช่อง Pants)
}

// ช่องสวมใส่อุปกรณ์หลักทั้ง 4 ช่องของตัวละคร
public enum EquipmentSlotType
{
    None = 0,   // ไม่ใช่อุปกรณ์สวมใส่
    Sword = 1,  // ช่องอาวุธดาบใหญ่
    Shield = 2, // ช่องโล่
    Armor = 3,  // ช่องเกราะลำตัว
    Pants = 4   // ช่องกางเกง/เกราะขา
}

// ระดับความหายากของไอเทม 4 ระดับ
public enum ItemTier
{
    Normal = 0,    // ระดับทั่วไป (สีเทา/ขาว)
    Common = 0,    // ชื่อรองสำหรับ Normal
    Rare = 1,      // ระดับหายาก (สีฟ้า)
    Epic = 2,      // ระดับมหากาพย์ (สีม่วง)
    Legendary = 3  // ระดับตำนาน (สีทอง)
}

/// <summary>
/// ข้อมูลไอเทมแบบ ScriptableObject รองรับทั้งยาฟื้นฟูและอุปกรณ์สวมใส่ 4 ช่อง (Sword, Shield, Armor, Pants)
/// พร้อมระดับความหายาก 4 Tier, ระบบตีบวก +0 ถึง +10 และข้อมูลสำหรับหน้าคู่มือไอเทม (Item Compendium)
/// </summary>
[CreateAssetMenu(fileName = "NewItem", menuName = "Hunter/Item Data")]
public class ItemData : ScriptableObject
{
    // รหัสไอเทมที่ไม่ซ้ำกัน ใช้สำหรับส่งผ่าน Network และบันทึกลงไฟล์ Save
    [Tooltip("Unique, stable id. This string (not the asset) is what travels over the network.")]
    public string itemId = "item_new";

    // ชื่อไอเทมที่แสดงบนหน้าจอ UI
    public string displayName = "New Item";

    // คำอธิบายคุณสมบัติของไอเทม
    [TextArea] public string description = "";

    // คำแนะนำวิธีหาและวิธีใช้งานสำหรับหน้า Item Guide / Tutorial Compendium
    [TextArea] public string obtainAndUseGuide = "";

    // ประเภทไอเทมและระดับความหายาก
    public ItemType type = ItemType.Consumable;
    public ItemTier tier = ItemTier.Normal;

    // คลาสและประเภทอาวุธที่รองรับ (เตรียมพร้อมสำหรับคลาส Magic ในอนาคต)
    public CombatClassType requiredClass = CombatClassType.GreatSword;
    public WeaponArchetype weaponArchetype = WeaponArchetype.GreatSword;

    // ไอคอนและสีประจำตัวเมื่อดรอปลงพื้นโลก
    public Sprite icon;
    [Tooltip("World pickup colour (no icon needed for the dev visuals).")]
    public Color worldColor = Color.white;

    // การซ้อนทับของไอเทมในกระเป๋า (Stacking)
    [Header("Stacking")]
    public bool stackable = true;
    [Min(1)] public int maxStack = 20;

    // ค่าฟื้นฟูและบัฟสำหรับไอเทมประเภทกดใช้ (Consumable)
    [Header("Consumable")]
    [Min(0)] public int healAmount = 0;
    [Min(0)] public int staminaRestoreAmount = 0;
    [Min(0)] public int attackBuffBonus = 0;
    [Min(0f)] public float attackBuffDuration = 0f;

    // ค่าสเตตัสโบนัสเมื่อสวมใส่อุปกรณ์ (Equipment Stat Bonuses)
    [Header("Equipment Stat Bonuses")]
    public int damageBonus = 0;
    public int maxHealthBonus = 0;
    public int maxStaminaBonus = 0;
    public int defenseBonus = 0;
    public float moveSpeedBonus = 0f;
    [Range(0f, 1f)] public float critChanceBonus = 0f;

    // กฎการทิ้งไอเทมลงพื้น
    [Header("Rules")]
    public bool droppable = true;

    // จำนวนสูงสุดต่อช่อง
    public int MaxStackSize => stackable ? Mathf.Max(1, maxStack) : 1;

    // ตรวจสอบว่าเป็นไอเทมกดใช้หรือไม่
    public bool IsUsable => type == ItemType.Consumable;

    // ตรวจสอบว่าเป็นอุปกรณ์ที่สวมใส่ได้หรือไม่
    public bool IsEquippable => GetEquipmentSlot() != EquipmentSlotType.None;

    /// <summary>
    /// คืนค่าช่องสวมใส่อุปกรณ์ (Sword, Shield, Armor, Pants) ตามประเภทของไอเทม
    /// </summary>
    public EquipmentSlotType GetEquipmentSlot()
    {
        return type switch
        {
            ItemType.Weapon => EquipmentSlotType.Sword,
            ItemType.Shield => EquipmentSlotType.Shield,
            ItemType.Armor => EquipmentSlotType.Armor,
            ItemType.Equipment => EquipmentSlotType.Armor,
            ItemType.Pants => EquipmentSlotType.Pants,
            _ => EquipmentSlotType.None
        };
    }

    /// <summary>
    /// คืนค่าหมวดหมู่ไอเทมเป็นข้อความสำหรับแสดงในหน้า Item Compendium / Tutorial Guide
    /// </summary>
    public string GetCategoryDisplayName()
    {
        return type switch
        {
            ItemType.Weapon => "Weapons (Sword)",
            ItemType.Shield => "Shields",
            ItemType.Armor => "Armor (Chestplate)",
            ItemType.Equipment => "Armor (Chestplate)",
            ItemType.Pants => "Pants (Greaves)",
            ItemType.Consumable => "Consumables",
            _ => "Other"
        };
    }

    // คำนวณค่าสเตตัสหลังตีบวกแบบจำนวนเต็มคงที่ (+1 ถึง +2 ต่อระดับ ไม่มีตัวคูณ % และบังคับ Role Cap บนอุปกรณ์ป้องกัน)
    public int GetEnhancedDamage(int enhancementLevel) =>
        EnhancementSystem.ScaleAttackStat(damageBonus, enhancementLevel, GetEquipmentSlot(), tier);
    public int GetEnhancedMaxHealth(int enhancementLevel) =>
        EnhancementSystem.ScaleHealthStat(maxHealthBonus, enhancementLevel);
    public int GetEnhancedMaxStamina(int enhancementLevel) =>
        EnhancementSystem.ScaleStaminaStat(maxStaminaBonus, enhancementLevel);
    public int GetEnhancedDefense(int enhancementLevel) =>
        EnhancementSystem.ScaleDefenseStat(defenseBonus, enhancementLevel);
    public float GetEnhancedMoveSpeed(int enhancementLevel) =>
        EnhancementSystem.ScaleMoveSpeedStat(moveSpeedBonus, enhancementLevel);
    // ยกเลิกค่า % Critical Chance บนอุปกรณ์ทั้งหมด (ระบบคริติคอลเปลี่ยนเป็น Skill-Based 100%)
    public float GetEnhancedCritChance(int enhancementLevel) => 0f;

    /// <summary>
    /// สรุปค่าสเตตัสทั้งหมดของไอเทมเป็นข้อความบรรทัดเดียวแบบจำนวนเต็มคงที่ (ไม่มี %) สำหรับแสดงใน UI และหน้าคู่มือ
    /// </summary>
    public string GetFormattedStatsSummary(int enhancementLevel = 0)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (IsUsable)
        {
            if (healAmount > 0) parts.Add($"+{healAmount} HP");
            if (staminaRestoreAmount > 0) parts.Add($"+{staminaRestoreAmount} Stamina");
            if (attackBuffBonus > 0) parts.Add($"+{attackBuffBonus} ATK ({attackBuffDuration:0}s)");
        }
        else
        {
            int dmg = GetEnhancedDamage(enhancementLevel);
            int hp = GetEnhancedMaxHealth(enhancementLevel);
            int sta = GetEnhancedMaxStamina(enhancementLevel);
            int def = GetEnhancedDefense(enhancementLevel);
            float spd = GetEnhancedMoveSpeed(enhancementLevel);

            if (dmg > 0) parts.Add($"+{dmg} ATK");
            if (hp > 0) parts.Add($"+{hp} Max HP");
            if (sta > 0) parts.Add($"+{sta} Max STA");
            if (def > 0) parts.Add($"+{def} DEF");
            if (spd > 0.001f) parts.Add($"+{spd:0.00} Move SPD");
        }
        return parts.Count > 0 ? string.Join("  |  ", parts) : "No stat bonuses";
    }

    /// <summary>
    /// แปลงระดับความหายากเป็นชื่อภาษาอังกฤษมาตรฐาน
    /// </summary>
    public static string GetTierDisplayName(ItemTier tier)
    {
        return tier switch
        {
            ItemTier.Normal => "Normal",
            ItemTier.Rare => "Rare",
            ItemTier.Epic => "Epic",
            ItemTier.Legendary => "Legendary",
            _ => "Normal"
        };
    }

    /// <summary>
    /// คืนค่าสีประจำระดับความหายาก (Normal=เทาเงิน, Rare=ฟ้า, Epic=ม่วง, Legendary=ทอง)
    /// </summary>
    public static Color GetTierColor(ItemTier tier)
    {
        return tier switch
        {
            ItemTier.Normal => new Color(0.82f, 0.85f, 0.88f, 1f),
            ItemTier.Rare => new Color(0.22f, 0.68f, 1.0f, 1f),
            ItemTier.Epic => new Color(0.75f, 0.32f, 0.98f, 1f),
            ItemTier.Legendary => new Color(1.0f, 0.76f, 0.12f, 1f),
            _ => Color.white
        };
    }
}
