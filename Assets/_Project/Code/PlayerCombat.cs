using System;
using System.Collections;
using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

public enum PlayerStatType
{
    STR = 0, // +1 Attack Damage ต่อแต้ม (Flat Integer)
    VIT = 1, // +6 Max HP ต่อแต้ม (Flat Integer)
    END = 2, // +2 Max Stamina ต่อแต้ม (จำกัดเพดานรวมไม่เกิน 150 STA)
    AGI = 3  // +0.08 Move Speed ต่อแต้ม (ยกเลิก % CRIT สุ่ม — ใช้ Skill-Based Crit แทน)
}

/// <summary>
/// ระบบต่อสู้ระยะประชิดและจัดการค่าพลังผู้เล่นผ่าน Photon Fusion Shared Mode (MMORPG Combat Core - Lv.1–30 Rebalance):
///   - เพดานเลเวลสูงสุด MaxLevel = 30 พร้อมการเติบโตแบบ Flat Integer คงที่และเพดาน MaxStamina = 150
///   - ระบบ Critical Hit แบบเน้นฝีมือ (Skill-Based): ไม่สุ่ม % และไม่คริอัตโนมัติในฮิต 1–2
///     ติดคริติคอล (1.55x–1.75x) เฉพาะ Combo Finisher (ฮิตสุดท้าย), Parry Counter-Slash และ Stealth Backstab (2.5x)
///   - รองรับ IClassCombatHandler เพื่อขยายคลาส Magic ในอนาคตโดยไม่ต้องรื้อโค้ดหลัก
///   - ขับเคลื่อนท่าโจมตีครบทุกชุดของ GreatSword (Light Combo 1..3, Heavy Slash 1..3, Run Attack, Jump Attack, Stealth Assassination)
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class PlayerCombat : NetworkBehaviour, IClassCombatHandler, IWorldInteractable
{
    // ---- IWorldInteractable (รองรับการกด [G] ค้าง 2.5 วินาทีเพื่อชุบชีวิตเพื่อนที่ล้มกลางไฟต์ในโหมด Co-op) ----
    public InteractionChannel Channel => InteractionChannel.NpcTalk;
    public int PriorityBias => -1; // สำคัญกว่าคุย NPC เพื่อไม่ให้หน้าต่างเควสต์มาแย่งจังหวะชุบเพื่อน
    public float InteractionRange => 3.5f;
    public Vector3 InteractionWorldPosition => transform.position;
    public bool RequiresHold => true;
    public float HoldDurationSeconds => 2.5f;

    public bool CanInteractNow(PlayerCombat rescuer)
    {
        return _isSpawned && IsDead && rescuer != null && rescuer != this && rescuer.IsSpawned && !rescuer.IsDead;
    }

    public string GetPromptLabel(PlayerCombat rescuer)
    {
        return $"Hold [G] Revive {PartyManager.GetDisplayName(this)} (2.5s)";
    }

    public Color GetPromptAccentColor(PlayerCombat rescuer)
    {
        return new Color(0.32f, 0.98f, 0.72f, 1f);
    }

    public void ExecuteInteraction(PlayerCombat rescuer)
    {
        if (!CanInteractNow(rescuer)) return;
        string rescuerName = PartyManager.GetDisplayName(rescuer);
        RPC_ReviveByAlly(rescuerName);
        PlayerStatusHud.Instance?.ShowToast($"Revived {PartyManager.GetDisplayName(this)}!", new Color(0.32f, 0.98f, 0.72f), 2.5f);
    }
    private const int DefaultMaxHealth = 100;
    private const float DefaultMaxStamina = 100f;
    // เพดานเลเวลสูงสุดของแพตช์ปัจจุบัน (Lv.30 Cap)
    public const int MaxLevel = 30;
    // เพดานค่า Stamina สูงสุดที่เข้มงวด (ป้องกันการสแปมคอมโบและกลิ้งหลบไม่จำกัดในเลเวลสูง)
    public const float HardMaxStaminaCap = 150f;
    // เพดานโบนัสความเร็วเคลื่อนที่จากค่า AGI
    private const float MaxAgiMoveSpeedBonus = 1.60f;
    public const int StatPointsPerLevel = 3;
    // เพดานค่าเงินและค่าสเตตัสต่อช่องสูงสุดที่ระบบเซฟอนุญาต (ป้องกันการแก้ไฟล์เซฟโกงค่า)
    public const int MaxSafeGold = 9999999;
    public const int MaxSingleStatValue = 30;

    // ------------------------------------------------------------------ inspector

    [Header("References")]
    [Tooltip("Trigger collider on the weapon blade (Sword_Hand/WeaponHitbox), enabled only during the active attack window.")]
    [SerializeField] private Collider weaponHitbox;
    [Tooltip("Optional. Receives the attack trigger on every peer.")]
    [SerializeField] private Animator animator;
    [SerializeField] private string attackTriggerName = "Attack";
    [Tooltip("Optional. Played on every peer when an attack starts.")]
    [SerializeField] private ParticleSystem attackVfx;
    [Tooltip("Deprecated debug renderer. Always kept hidden during Play Mode so the swing hitbox is invisible.")]
    [SerializeField] private Renderer hitboxDebugVisual;

    [Header("Attack & Skill-Based Criticals (ระบบคริติคอลเน้นฝีมือ)")]
    [Min(0.05f)] [SerializeField] private float attackCooldown = 0.6f;
    [Tooltip("A click made this early during the cooldown is remembered and fired when it ends.")]
    [Min(0f)] [SerializeField] private float inputBufferSeconds = 0.2f;
    [Tooltip("The authority accepts requests this much earlier than the cooldown (jitter tolerance).")]
    [Min(0f)] [SerializeField] private float cooldownTolerance = 0.1f;
    // ค่าพลังโจมตีพื้นฐานที่ Lv.1 (ปรับเป็น 14 เพื่อรวมกับ Iron GreatSword +5 = 19 ATK โจมตีมอนโซนเริ่มต้นตายใน 4–5 ฮิต)
    [Min(1)] [SerializeField] private int attackDamage = 14;
    [Tooltip("Time window after a swing to chain into the next combo step (1 -> 2 -> 3).")]
    [Min(0.2f)] [SerializeField] private float comboResetSeconds = 1.45f;
    // ตัวคูณดาเมจปกติสำหรับ Combo ฮิต 2 (ไม่ติด Critical Hit เพื่อตัดอาการสแปมคริ)
    [Min(1f)] [SerializeField] private float comboStep2NormalMultiplier = 1.05f;
    // ตัวคูณ Critical Hit สำหรับ Combo Finisher ฮิตสุดท้าย (Light Combo ฮิต 3 = 1.55x)
    [Range(1.50f, 1.75f)] [SerializeField] private float lightFinisherCritMultiplier = 1.55f;
    // ตัวคูณ Critical Hit สำหรับ Heavy Slash Combo Finisher ฮิตสุดท้าย (1.75x)
    [Range(1.50f, 1.75f)] [SerializeField] private float heavyFinisherCritMultiplier = 1.75f;
    // ตัวคูณ Critical Hit สำหรับการฟันสวนกลับทันทีหลัง Perfect Parry (1.75x)
    [Range(1.50f, 1.75f)] [SerializeField] private float parryCounterCritMultiplier = 1.75f;
    // ระยะเวลาที่บัฟ Parry Counter-Slash CRIT พร้อมใช้งานหลังจากทำ Perfect Parry สำเร็จ (วินาที)
    [Min(0.5f)] [SerializeField] private float parryCounterWindowSeconds = 3.5f;
    // ตัวคูณ Critical Hit สำหรับท่าลอบสังหารจากด้านหลัง (Stealth Assassination Backstab)
    [Min(1.5f)] [SerializeField] private float stealthBackstabCritMultiplier = 2.50f;

    [Header("Block & Dodge (ตั้งการ์ดป้องกันและกลิ้งหลบ)")]
    [Range(0.1f, 1f)] [SerializeField] private float blockMoveSpeedMultiplier = 0.45f;
    // ลดดาเมจที่ได้รับลง 75% ขณะตั้งการ์ดป้องกันด้วยดาบใหญ่
    [Range(0f, 0.95f)] [SerializeField] private float blockDamageReduction = 0.75f;
    [Min(0.1f)] [SerializeField] private float dodgeCooldown = 0.75f;
    [Min(0.1f)] [SerializeField] private float dodgeDuration = 0.45f;
    [Min(0.5f)] [SerializeField] private float dodgeDistance = 4.2f;

    [Header("Timed Parry (ปัดป้องสมบูรณ์ / Perfect Block)")]
    // หน้าต่างเวลาหลังจากกดเริ่มตั้งการ์ดที่จะนับเป็น Perfect Parry (รับดาเมจ 0 + ศัตรูติด Stagger)
    [Range(0.05f, 0.50f)] [SerializeField] private float parryWindowSeconds = 0.25f;
    // คูลดาวน์ขั้นต่ำระหว่างการเปิดหน้าต่าง Parry แต่ละครั้ง เพื่อป้องกันการกดย้ำปุ่มบล็อกรัวๆ
    [Range(0.10f, 1.50f)] [SerializeField] private float parryCooldownSeconds = 0.45f;
    // ระยะเวลาที่มอนสเตอร์ทั่วไปจะติดสถานะ Stagger เมื่อถูก Perfect Parry
    [Range(0.3f, 3.0f)] [SerializeField] private float parryEnemyStaggerDuration = 1.45f;
    // ระยะเวลาที่บอสจะติดสถานะ Stagger เมื่อถูก Perfect Parry ในระยะประชิด
    [Range(0.2f, 2.0f)] [SerializeField] private float parryBossStaggerDuration = 0.70f;
    // โบนัส Stamina ที่คืนให้เมื่อปัดป้องสมบูรณ์สำเร็จ
    [Min(0f)] [SerializeField] private float parryStaminaReward = 12f;

    [Header("Combat Feel (Hit-Stop & Camera Shake)")]
    // เปิด/ปิดการชะลอภาพอนิเมชันชั่วคราว (Micro Hit-Stop) เฉพาะฝั่งผู้เล่นเมื่อฟันโดนหรือ Parry สำเร็จ
    [SerializeField] private bool enableCombatHitStop = true;
    // เปิด/ปิดแรงสั่นกล้องตามน้ำหนักการปะทะ
    [SerializeField] private bool enableCameraShake = true;

    [Header("Stamina")]
    [Min(10f)] [SerializeField] private float maxStamina = DefaultMaxStamina;
    [Min(1f)] [SerializeField] private float sprintStaminaPerSecond = 15f;
    [Min(1f)] [SerializeField] private float attackStaminaCost = 15f;
    [Min(1f)] [SerializeField] private float dodgeStaminaCost = 25f;
    [Min(1f)] [SerializeField] private float blockHitStaminaCost = 18f;
    [Min(1f)] [SerializeField] private float staminaRegenRate = 22f;
    [Min(0f)] [SerializeField] private float staminaRegenDelay = 1.1f;

    [Header("Hit window")]
    [Tooltip("ON: Animation Events call AnimEvent_OpenHitWindow / AnimEvent_CloseHitWindow. OFF: timed coroutine.")]
    [SerializeField] private bool useAnimationEvents;
    [Min(0f)] [SerializeField] private float hitWindowDelay = 0.08f;
    [Min(0.01f)] [SerializeField] private float hitWindowDuration = 0.30f;
    [Tooltip("Hard cap: the window is force-closed after this long, even if an animation event never arrives.")]
    [Min(0.05f)] [SerializeField] private float hitWindowSafetyTimeout = 1f;

    [Header("Validation (applied by the victim's State Authority)")]
    [Tooltip("Upper bound for any single damage application.")]
    [Min(1)] [SerializeField] private int maxDamagePerHit = 400;
    [Tooltip("Attacker and victim must be at most this far apart (generous: remote proxies lag).")]
    [Min(0.5f)] [SerializeField] private float maxHitDistance = 5f;

    [Header("PvP & Co-op Safety (ความปลอดภัยโหมด Co-op)")]
    [Tooltip("เปิดเพื่อให้ผู้เล่นฟันทำดาเมจกันเองได้ (ค่าเริ่มต้นปิด เพื่อป้องกันการฟันเพื่อนร่วมทีมโดยไม่ตั้งใจขณะช่วยกันตีบอส/แคมป์)")]
    [SerializeField] private bool allowFriendlyFirePvp = false;

    public bool AllowFriendlyFirePvp => allowFriendlyFirePvp;

    [Header("Health")]
    [Min(1)] [SerializeField] private int maxHealth = DefaultMaxHealth;
    [Tooltip("Seconds until a dead player is restored at its spawn point. 0 or less = stay dead.")]
    [SerializeField] private float respawnDelaySeconds = 3f;

    [Header("Debug")]
#pragma warning disable CS0414
    [SerializeField] private bool showDebugHud = false;
#pragma warning restore CS0414

    [Header("Events (all peers)")]
    public UnityEvent onAttackStarted;

    // ------------------------------------------------------------------ networked state

    /// <summary>Replicated current health.</summary>
    [Networked, OnChangedRender(nameof(OnHealthChanged))]
    public int NetworkHealth { get; set; } = DefaultMaxHealth;

    /// <summary>Replicated player stamina (0..MaxStamina).</summary>
    [Networked, OnChangedRender(nameof(OnStaminaChanged))]
    public float NetworkStamina { get; set; } = DefaultMaxStamina;

    /// <summary>Replicated blocking stance.</summary>
    [Networked, OnChangedRender(nameof(OnBlockingChanged))]
    public NetworkBool IsBlocking { get; set; }

    /// <summary>
    /// สถานะชักดาบ/เก็บดาบที่ซิงก์ผ่าน Photon Fusion ไปยังทุกเครื่องในห้อง (true = ถือดาบพร้อมสู้, false = เก็บดาบไว้กลางหลังในโหมดสำรวจ)
    /// </summary>
    [Networked, OnChangedRender(nameof(OnWeaponDrawnChanged))]
    public NetworkBool IsWeaponDrawn { get; set; }

    /// <summary>Replicated current combo step (1..3).</summary>
    [Networked]
    public int ComboStep { get; set; } = 1;

    // ---- Networked Progression & Stat Allocation ----
    [Networked, OnChangedRender(nameof(OnProgressionNetChanged))]
    public int NetworkLevel { get; set; } = 1;

    [Networked, OnChangedRender(nameof(OnProgressionNetChanged))]
    public int NetworkExperience { get; set; } = 0;

    [Networked, OnChangedRender(nameof(OnProgressionNetChanged))]
    public int NetworkUnspentStatPoints { get; set; } = 0;

    [Networked, OnChangedRender(nameof(OnProgressionNetChanged))]
    public int StatPointsSTR { get; set; } = 0;

    [Networked, OnChangedRender(nameof(OnProgressionNetChanged))]
    public int StatPointsVIT { get; set; } = 0;

    [Networked, OnChangedRender(nameof(OnProgressionNetChanged))]
    public int StatPointsEND { get; set; } = 0;

    [Networked, OnChangedRender(nameof(OnProgressionNetChanged))]
    public int StatPointsAGI { get; set; } = 0;

    // ---- Networked Economy & Safe Zone ----
    [Networked, OnChangedRender(nameof(OnProgressionNetChanged))]
    public int NetworkGold { get; set; } = 0;

    [Networked]
    public NetworkBool IsInSafeZone { get; set; }

    // ---- Networked Equipped Gear Aggregated Bonuses ----
    [Networked, OnChangedRender(nameof(OnProgressionNetChanged))]
    public int NetworkGearDamageBonus { get; set; } = 0;

    [Networked, OnChangedRender(nameof(OnProgressionNetChanged))]
    public int NetworkGearMaxHealthBonus { get; set; } = 0;

    [Networked, OnChangedRender(nameof(OnProgressionNetChanged))]
    public int NetworkGearMaxStaminaBonus { get; set; } = 0;

    [Networked, OnChangedRender(nameof(OnProgressionNetChanged))]
    public int NetworkGearDefenseBonus { get; set; } = 0;

    [Networked, OnChangedRender(nameof(OnProgressionNetChanged))]
    public float NetworkGearMoveSpeedBonus { get; set; } = 0f;

    [Networked, OnChangedRender(nameof(OnProgressionNetChanged))]
    public float NetworkGearCritBonus { get; set; } = 0f;

    // ------------------------------------------------------------------ public API (Aggregated Stats)

    public int BaseMaxHealth => maxHealth;
    public float BaseMaxStamina => maxStamina;
    public int BaseAttackDamage => attackDamage;
    // เลเวลที่ถูกจำกัดให้อยู่ในช่วง 1..MaxLevel (30) เสมอ
    public int ClampedLevel => _isSpawned ? Mathf.Clamp(NetworkLevel, 1, MaxLevel) : 1;
    // ตรวจสอบว่าผู้เล่นถึงเพดานเลเวลสูงสุด (Lv.30) แล้วหรือไม่
    public bool IsMaxLevel => _isSpawned && NetworkLevel >= MaxLevel;
    // ตรวจสอบว่าผู้เล่นเพิ่งทำ Perfect Parry สำเร็จและพร้อมฟันสวนกลับแบบการันตี Critical Hit หรือไม่
    public bool IsParryCounterReady => Time.time <= _parryCounterReadyUntil;

    /// <summary>
    /// ค่า Max HP รวม = Base (100) + โบนัสเลเวล (+10 HP/เลเวล รวม +290 ที่ Lv.30) + VIT (+6 HP/แต้ม) + อุปกรณ์สวมใส่
    /// </summary>
    public int MaxHealth => _isSpawned
        ? maxHealth + (ClampedLevel - 1) * 10 + StatPointsVIT * 6 + NetworkGearMaxHealthBonus
        : maxHealth;

    /// <summary>
    /// ค่า Max Stamina รวม = Base (100) + โบนัสเลเวล (สูงสุด +16 STA) + END (+2 STA/แต้ม) + อุปกรณ์สวมใส่
    /// จำกัดเพดานสูงสุดไม่เกิน HardMaxStaminaCap (150 STA) โดยแต้ม END ยังเพิ่ม Stamina Regen (+4%/แต้ม) และลดค่าใช้กลิ้ง/บล็อก (-1.5%/แต้ม สูงสุด -30%) เสมอไม่สูญเปล่า
    /// </summary>
    public float MaxStamina => _isSpawned
        ? Mathf.Min(HardMaxStaminaCap, maxStamina + Mathf.Min(16f, (ClampedLevel - 1) * 1f) + StatPointsEND * 2f + NetworkGearMaxStaminaBonus)
        : Mathf.Min(HardMaxStaminaCap, maxStamina);

    /// <summary>
    /// อัตราฟื้นฟู Stamina ต่อวินาทีรวมโบนัสจากค่า END (+4% ต่อแต้ม END ทำให้ค่า END คุ้มค่าเสมอแม้ MaxStamina ชนเพดาน 150)
    /// </summary>
    public float TotalStaminaRegenRate => staminaRegenRate * (1f + (_isSpawned ? Mathf.Min(StatPointsEND, MaxSingleStatValue) * 0.04f : 0f));

    /// <summary>
    /// ตัวคูณลดการใช้ Stamina ตอนกลิ้งหลบและรับแรงปะทะขณะตั้งการ์ดจากค่า END (-1.5% ต่อแต้ม END สูงสุดลด 30% ที่ END 20)
    /// </summary>
    public float EndStaminaCostMultiplier => 1f - (_isSpawned ? Mathf.Min(StatPointsEND, 20) * 0.015f : 0f);

    /// <summary>
    /// ค่า ATK รวม = Base (14) + โบนัสเลเวล (+1 ATK ทุก 2 เลเวล รวม +14 ที่ Lv.30) + STR (+1 ATK/แต้ม) + อาวุธ/อุปกรณ์ + บัฟ
    /// </summary>
    public int TotalAttackDamage => _isSpawned
        ? attackDamage + ((ClampedLevel - 1) / 2) + StatPointsSTR * 1 + NetworkGearDamageBonus + ActiveBuffDamageBonus
        : attackDamage;

    /// <summary>
    /// ค่า DEF รวม = โบนัสเลเวล (+1 DEF ทุก 3 เลเวล รวม +9 ที่ Lv.30) + โล่/ชุดเกราะ (Flat Integer)
    /// </summary>
    public int TotalDefense => _isSpawned
        ? ((ClampedLevel - 1) / 3) + NetworkGearDefenseBonus
        : 0;

    /// <summary>
    /// โบนัสความเร็วเคลื่อนที่จากค่า AGI (+0.08/แต้ม สูงสุด +1.60) รวมกับกางเกง/รองเท้าที่สวมใส่
    /// </summary>
    public float TotalMoveSpeedBonus => _isSpawned
        ? (Mathf.Min(MaxAgiMoveSpeedBonus, StatPointsAGI * 0.08f) + NetworkGearMoveSpeedBonus)
        : 0f;

    /// <summary>
    /// ยกเลิกโอกาสสุ่ม Critical % ทั้งหมด (คืนค่า 0%) — คริติคอลเกิดจากฝีมือเท่านั้น (Combo Finisher / Parry Counter / Stealth Backstab)
    /// </summary>
    public float TotalCritChance => 0f;

    public int ExpToNextLevel => IsMaxLevel ? 0 : GetExpRequiredForNextLevel(ClampedLevel);
    public bool IsInCombat => _isSpawned && (Time.time - _lastCombatActivityTime < 6f);

    /// <summary>
    /// จริงเมื่อสามารถวิ่ง Sprint ได้ฟรีโดยไม่เสีย Stamina (อยู่ในเขตปลอดภัย SafeZone หรือไม่ได้อยู่ในสถานะต่อสู้และไม่มีมอนกำลังไล่ล่าใกล้ตัว)
    /// </summary>
    public bool IsFreeExplorationSprintActive
    {
        get
        {
            if (!_isSpawned || IsDead) return false;
            if (IsInSafeZone || SafeZone.IsPointInAnySafeZone(transform.position)) return true;
            if (IsInCombat) return false;
            for (int i = 0; i < EnemyNPC.All.Count; i++)
            {
                var e = EnemyNPC.All[i];
                if (e == null || !e.IsAlive) continue;
                if (e.State == NpcState.Chasing || e.State == NpcState.Attacking)
                {
                    Vector3 diff = e.transform.position - transform.position;
                    diff.y = 0f;
                    if (diff.sqrMagnitude <= 16f * 16f) return false;
                }
            }
            return true;
        }
    }

    public float CurrentStamina => _isSpawned ? NetworkStamina : maxStamina;
    public bool CanSprint => !IsDead && (IsFreeExplorationSprintActive || (CurrentStamina > 0.5f && !_sprintExhaustedCooldown));
    // ต้องชักดาบออกมาก่อน (IsWeaponDrawn) และอยู่นอกเขตปลอดภัยจึงจะโจมตีได้
    public bool CanAttack => !IsDead && IsWeaponDrawn && !IsInSafeZone && !SafeZone.IsPointInAnySafeZone(transform.position) && CurrentStamina > 0f;
    public bool CanDodge => !IsDead && CurrentStamina > 0f;
    // ข้อความแจ้งเตือนเมื่อผู้เล่นพยายามโจมตีหรือป้องกันขณะยังเก็บดาบอยู่กลางหลัง
    public const string DrawWeaponPromptMessage = "Press [1] to Draw GreatSword before attacking! (กด [1] ชักดาบก่อนโจมตี)";
    public bool IsDead => _isSpawned && NetworkHealth <= 0;
    public bool IsHitWindowOpen { get; private set; }
    public bool IsDodging => Time.time < _dodgeInvulnerableUntil;
    public Collider WeaponHitbox => weaponHitbox;
    public GameObject BlockVfxObject => _blockVfxRoot;
    public bool IsBlockVfxActive => _blockVfxRoot != null && _blockVfxRoot.activeSelf;
    // อัตราการลดดาเมจขณะตั้งการ์ดปกติ (เช่น 0.75 = 75%)
    public float BlockDamageReduction => blockDamageReduction;
    // ช่วงเวลาหน้าต่างปัดป้องสมบูรณ์ (วินาที)
    public float ParryWindowSeconds => parryWindowSeconds;
    // ตรวจสอบว่าผู้เล่นกำลังอยู่ในหน้าต่างเวลา Perfect Parry หรือไม่
    public bool IsInParryWindow => IsBlocking && (Time.time - _blockStartedTime <= parryWindowSeconds);

    // อ้างอิงรูปแบบโจมตีปัจจุบัน (สำหรับสั่งเล่นอนิเมชันและคำนวณตัวคูณดาเมจ)
    public PlayerAttackKind CurrentAttackKind { get; private set; } = PlayerAttackKind.LightCombo;

    // ------------------------------------------------------------------ IClassCombatHandler (รองรับการเพิ่มคลาส Magic ในอนาคต)

    // คลาสต่อสู้ปัจจุบันของผู้เล่น (GreatSword)
    public CombatClassType ActiveClass => CombatClassType.GreatSword;

    // ประเภทอาวุธที่สวมใส่อยู่ปัจจุบัน
    public WeaponArchetype ActiveWeaponArchetype
    {
        get
        {
            var inv = InventoryManager.Local;
            if (inv != null)
            {
                var eqWeapon = inv.GetEquippedItem(EquipmentSlotType.Sword);
                if (eqWeapon != null) return eqWeapon.weaponArchetype;
            }
            return WeaponArchetype.GreatSword;
        }
    }

    public bool CanPerformPrimaryAttack() => CanAttack;
    public bool TryPerformPrimaryAttack() => TryRequestAttack();
    public bool TryPerformSecondaryAction() => TryRequestAttackKind(ResolveContextualHeavyAttackKind());

    /// <summary>Sequence number of the swing this peer last saw start (identifies a swing for de-duplication).</summary>
    public int CurrentSequence { get; private set; }

    /// <summary>(current, max) - raised on every peer when health changes.</summary>
    public event Action<int, int> HealthChanged;

    /// <summary>(current, max) - raised on every peer when stamina changes.</summary>
    public event Action<float, float> StaminaChanged;

    /// <summary>Raised when Level, EXP, Stat Points, or Equipped Gear stats change.</summary>
    public event Action ProgressionChanged;

    /// <summary>true = died, false = revived. Raised on every peer.</summary>
    public event Action<bool> DeathStateChanged;

    // ---- อีเวนต์การต่อสู้สำหรับทีม Audio / VFX / UI (Team Handoff Contract) ----
    // ยิงบนทุก Peer เมื่อตัวละครเริ่มออกท่าโจมตี (รูปแบบท่า, จังหวะคอมโบ 1..3)
    public event Action<PlayerAttackKind, int> AttackPerformed;
    // ยิงเมื่อดาบฟันโดนมอนสเตอร์/บอส (เป้าหมาย, ดาเมจสุทธิ, ติดคริหรือไม่)
    public event Action<EnemyNPC, int, bool> HitLanded;
    // ยิงบนทุก Peer เมื่อทำ Perfect Parry สำเร็จ (ส่งศัตรูผู้โจมตีมาด้วยถ้ามี)
    public event Action<EnemyNPC> ParrySucceeded;
    // ยิงบนทุก Peer เมื่อตั้งการ์ดรับดาเมจปกติสำเร็จ (ดาเมจหลังหักลดแล้ว)
    public event Action<int> BlockImpacted;
    // ยิงบนทุก Peer เมื่อ Stamina หมดขณะตั้งการ์ดจนการ์ดแตก
    public event Action GuardBroken;
    // ยิงบนทุก Peer เมื่อผู้เล่นชักดาบ (true) หรือเก็บดาบ (false)
    public event Action<bool> WeaponDrawnChanged;

    /// <summary>Every spawned player on this peer (local + remote proxies). Used by NPC AI and the map.</summary>
    public static readonly List<PlayerCombat> All = new List<PlayerCombat>();

    /// <summary>The player this peer controls.</summary>
    public static PlayerCombat Local { get; private set; }

    public bool IsSpawned => _isSpawned;

    /// <summary>Legacy setter compatibility for WeaponDamageBonus.</summary>
    public int WeaponDamageBonus
    {
        get => _isSpawned ? NetworkGearDamageBonus : 0;
        set
        {
            if (_isSpawned && Object.HasStateAuthority)
                NetworkGearDamageBonus = value;
        }
    }

    /// <summary>Temporary attack buff from consumables (e.g. Reinforced GreatSword Oil).</summary>
    public int ActiveBuffDamageBonus => Time.time < _attackBuffUntil ? _attackBuffBonus : 0;
    public float ActiveBuffRemainingSeconds => Mathf.Max(0f, _attackBuffUntil - Time.time);

    public static int GetExpRequiredForNextLevel(int currentLevel)
    {
        return 100 + Mathf.Max(0, currentLevel - 1) * 75;
    }

    // ------------------------------------------------------------------ private state

    private PlayerCapsule _capsule;
    private PlayerAnimationDriver _animationDriver;
    private Rigidbody _rb;
    private Collider _bodyCollider;
    private NetworkTransform _networkTransform;
    private int _attackTriggerHash;

    private bool _isSpawned;
    private bool _lastDeadState;
    private int _lastHealthValue = DefaultMaxHealth;
    private Vector3 _spawnPosition;

    // Local (input side)
    private float _nextLocalAttackTime;
    private float _lastLocalAttackTime = -100f;
    private int _localPredictedComboStep;
    private float _bufferedUntil = -1f;
    private bool _bufferedHeavy;
    private float _nextLocalDodgeTime;
    private float _overrideBlockUntil;
    private bool _localBlockingSent;
    // ตัวหน่วงคูลดาวน์การสลับชัก/เก็บดาบ (ปุ่ม 1) และเวลาล่าสุดที่แสดงข้อความเตือนแต่ละประเภทแยกกัน
    private float _nextWeaponToggleTime;
    private float _lastSheathedPromptTime = -10f;
    private float _lastSafeZonePromptTime = -10f;
    // เวลาสิ้นสุดของหน้าต่างฟันสวนกลับการันตี Critical Hit หลังจากทำ Perfect Parry สำเร็จ
    private float _parryCounterReadyUntil = -100f;

    // Authority side
    private float _nextAuthorityAttackTime;
    private float _lastAuthorityAttackTime = -100f;
    private float _lastCombatActivityTime = -100f;
    private int _authorityComboStep;
    private int _sequenceCounter;
    private float _dodgeInvulnerableUntil;
    private float _blockStartedTime = -100f;
    private float _nextParryReadyTime = -100f;
    private float _parryFlashUntil = -100f;
    private float _nextStaminaRegenTime;
    private bool _sprintExhaustedCooldown;
    private int _attackBuffBonus;
    private float _attackBuffUntil;
    private Coroutine _respawnRoutine;

    // Block VFX
    private GameObject _blockVfxRoot;
    private Transform _blockDome;
    private Transform _blockRing;
    private Renderer _blockDomeRenderer;
    private MaterialPropertyBlock _blockDomeMpb;

    // Shared: running hit window
    private Coroutine _hitWindowRoutine;

    // Victim side: last swing that already damaged us, per attacker.
    private readonly Dictionary<NetworkId, int> _lastHitSequenceByAttacker = new Dictionary<NetworkId, int>();

    // ------------------------------------------------------------------ lifecycle

    private void Awake()
    {
        _capsule = GetComponent<PlayerCapsule>();
        _animationDriver = GetComponent<PlayerAnimationDriver>();
        _rb = GetComponent<Rigidbody>();
        _bodyCollider = GetComponent<Collider>();
        _networkTransform = GetComponent<NetworkTransform>();
        _attackTriggerHash = string.IsNullOrEmpty(attackTriggerName) ? 0 : Animator.StringToHash(attackTriggerName);

        if (animator == null)
        {
            if (_animationDriver != null && _animationDriver.Animator != null)
                animator = _animationDriver.Animator;
            else
                animator = GetComponentInChildren<Animator>();
        }

        EnsureBladeHitboxAlignedAndClean();
        BuildBlockShieldVfx();
    }

    private void EnsureBladeHitboxAlignedAndClean()
    {
        if (hitboxDebugVisual != null)
        {
            hitboxDebugVisual.enabled = false;
            hitboxDebugVisual.gameObject.SetActive(false);
            hitboxDebugVisual = null;
        }

        Transform swordHand = null;
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "Sword_Hand")
            {
                swordHand = t;
                break;
            }
            if (t.name == "WeaponHitboxVisual")
            {
                var r = t.GetComponent<Renderer>();
                if (r != null) r.enabled = false;
                t.gameObject.SetActive(false);
            }
        }

        if (weaponHitbox == null)
        {
            Transform existingWh = null;
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "WeaponHitbox") { existingWh = t; break; }
            }
            if (existingWh != null) weaponHitbox = existingWh.GetComponent<Collider>();
        }

        if (swordHand != null && weaponHitbox != null)
        {
            Transform whTransform = weaponHitbox.transform;
            foreach (Renderer r in whTransform.GetComponentsInChildren<Renderer>(true))
            {
                r.enabled = false;
                if (r.gameObject != whTransform.gameObject)
                    r.gameObject.SetActive(false);
            }

            if (whTransform.parent != swordHand)
            {
                whTransform.SetParent(swordHand, false);
                whTransform.localPosition = Vector3.zero;
                whTransform.localRotation = Quaternion.identity;
                whTransform.localScale = Vector3.one;
            }

            if (weaponHitbox is SphereCollider oldSphere)
            {
                Destroy(oldSphere);
                var box = whTransform.GetComponent<BoxCollider>();
                if (box == null) box = whTransform.gameObject.AddComponent<BoxCollider>();
                box.center = Vector3.zero;
                box.size = new Vector3(1.6f, 0.5f, 8.6f);
                box.isTrigger = true;
                weaponHitbox = box;
            }
            else if (weaponHitbox is BoxCollider box)
            {
                box.center = Vector3.zero;
                box.size = new Vector3(1.6f, 0.5f, 8.6f);
                box.isTrigger = true;
            }
        }

        if (weaponHitbox == null && swordHand != null)
        {
            // สร้าง BoxCollider สำหรับใบดาบอัตโนมัติบน Sword_Hand หากยังไม่ได้ผูกไว้ใน Prefab
            var hitboxObj = new GameObject("WeaponHitbox");
            hitboxObj.transform.SetParent(swordHand, false);
            hitboxObj.transform.localPosition = Vector3.zero;
            hitboxObj.transform.localRotation = Quaternion.identity;
            hitboxObj.transform.localScale = Vector3.one;
            var autoBox = hitboxObj.AddComponent<BoxCollider>();
            autoBox.center = Vector3.zero;
            autoBox.size = new Vector3(1.6f, 0.5f, 8.6f);
            autoBox.isTrigger = true;
            autoBox.enabled = false;
            weaponHitbox = autoBox;
        }

        if (weaponHitbox == null)
        {
            Debug.LogError($"[PlayerCombat] '{name}': weaponHitbox is not assigned - attacks will never hit.", this);
            return;
        }

        weaponHitbox.isTrigger = true;
        SetHitbox(false);
    }

    private void BuildBlockShieldVfx()
    {
        Transform existing = transform.Find("BlockShieldVFX");
        if (existing != null)
        {
            _blockVfxRoot = existing.gameObject;
            _blockDome = existing.Find("ShieldDome");
            _blockRing = existing.Find("ShieldRing");
            _blockVfxRoot.SetActive(false);
            return;
        }

        _blockVfxRoot = new GameObject("BlockShieldVFX");
        _blockVfxRoot.transform.SetParent(transform, false);
        _blockVfxRoot.transform.localPosition = Vector3.zero;

        // ปรับระดับความสูงของโล่พลังงาน Block VFX ให้ตรงกับตัวละครที่มีจุดหมุนอยู่ที่เท้า (GreatSwordGirl) vs กึ่งกลางตัว
        float yOffset = (_capsule != null && _capsule.CharacterVisual == transform) ? 1.0f : 0f;

        GameObject dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        dome.name = "ShieldDome";
        var domeCol = dome.GetComponent<Collider>();
        if (domeCol != null) Destroy(domeCol);
        dome.transform.SetParent(_blockVfxRoot.transform, false);
        dome.transform.localPosition = new Vector3(0f, 0.15f + yOffset, 0.25f);
        dome.transform.localScale = new Vector3(1.35f, 1.65f, 1.15f);
        // ใช้ URP Unlit Transparent ถาวรแทน Shader.Find("Sprites/Default") เพื่อป้องกันการถูก Strip ใน Standalone Build
        UrpMaterialHelper.ApplyTransparentColor(dome.GetComponent<Renderer>(), new Color(0.18f, 0.78f, 1.0f, 0.26f), disableShadows: true);
        _blockDome = dome.transform;

        GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "ShieldRing";
        var ringCol = ring.GetComponent<Collider>();
        if (ringCol != null) Destroy(ringCol);
        ring.transform.SetParent(_blockVfxRoot.transform, false);
        ring.transform.localPosition = new Vector3(0f, -0.92f + yOffset, 0f);
        ring.transform.localScale = new Vector3(1.65f, 0.02f, 1.65f);
        UrpMaterialHelper.ApplyTransparentColor(ring.GetComponent<Renderer>(), new Color(0.25f, 0.90f, 1.0f, 0.58f), disableShadows: true);
        _blockRing = ring.transform;

        _blockVfxRoot.SetActive(false);
    }

    public override void Spawned()
    {
        _isSpawned = true;
        _spawnPosition = transform.position;

        if (!All.Contains(this)) All.Add(this);
        InteractionManager.Register(this);
        if (IsLocallyControlled)
        {
            Local = this;
            Debug.Log($"[PlayerCombat] Local player registered ({name}, id={Object.Id}).");
            CombatHud.EnsureFor(this);
            BossHudUI.EnsureFor(this);
            PartyManager.EnsureExists();
        }

        if (Object.HasStateAuthority)
        {
            // บังคับเลเวลเริ่มต้นให้อยู่ในช่วง 1..MaxLevel (30) และจำกัด HP/Stamina ไม่เกินเพดานใหม่
            NetworkLevel = Mathf.Clamp(NetworkLevel, 1, MaxLevel);
            if (NetworkLevel >= MaxLevel) NetworkExperience = 0;
            NetworkHealth = MaxHealth;
            NetworkStamina = MaxStamina;
            bool startInSafeZone = SafeZone.IsPointInAnySafeZone(transform.position);
            IsInSafeZone = startInSafeZone;
            // เริ่มต้นแบบเก็บดาบไว้กลางหลังเสมอ (โหมดสำรวจ/เขตปลอดภัย) ผู้เล่นกด [1] นอกเขตปลอดภัยเพื่อชักดาบ
            IsWeaponDrawn = false;
        }

        ApplyHealthState(forceNotify: true);
        UpdateBlockVfxVisual(IsBlocking && !IsDead);
        _animationDriver?.SyncWeaponDrawnImmediate(IsWeaponDrawn && !IsInSafeZone);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (IsLocallyControlled && PlayerSaveManager.Instance != null)
            PlayerSaveManager.Instance.SaveNow("OnNetworkDespawn");

        _isSpawned = false;
        All.Remove(this);
        InteractionManager.Unregister(this);
        if (Local == this) Local = null;
        StopAllCoroutines();
        _hitWindowRoutine = null;
        _respawnRoutine = null;
        CloseHitWindow();
        UpdateBlockVfxVisual(false);
        _lastHitSequenceByAttacker.Clear();
    }

    // ------------------------------------------------------------------ EXP, Gold, Leveling, Scaled Stat Allocation & Persistence

    public int GetStatPointValue(PlayerStatType statType)
    {
        if (!_isSpawned) return 0;
        return statType switch
        {
            PlayerStatType.STR => StatPointsSTR,
            PlayerStatType.VIT => StatPointsVIT,
            PlayerStatType.END => StatPointsEND,
            PlayerStatType.AGI => StatPointsAGI,
            _ => 0
        };
    }

    public static int GetAttributeUpgradePointCost(int currentStatValue)
    {
        return currentStatValue >= 10 ? 2 : 1;
    }

    /// <summary>
    /// คำนวณแต้มสเตตัสสะสมทั้งหมดที่ใช้เพื่ออัปเกรดค่าสเตตัส 1 ช่องจาก 0 จนถึง statValue
    /// (ช่วง 0->10 ใช้ 1 แต้ม/ขั้น, ตั้งแต่ 10 ขึ้นไปใช้ 2 แต้ม/ขั้น ตรงกับ GetAttributeUpgradePointCost)
    /// </summary>
    public static int GetCumulativePointsForStatValue(int statValue)
    {
        int v = Mathf.Max(0, statValue);
        int tier1 = Mathf.Min(v, 10);
        int tier2 = Mathf.Max(0, v - 10);
        return tier1 + tier2 * 2;
    }

    public static int GetAttributeUpgradeGoldCost(int currentStatValue)
    {
        // ปรับสมดุลค่าทองอัป Stat ให้สมเหตุสมผลและไม่แย่งเงินตีบวก Blacksmith ช่วงกลาง-ปลายเกม
        if (currentStatValue < 5) return 0;
        if (currentStatValue < 10) return 45;
        if (currentStatValue < 15) return 120;
        return 220 + (currentStatValue - 15) * 40;
    }

    public void AddGold(int amount, string source = "Reward")
    {
        if (!_isSpawned || amount <= 0) return;
        if (Object.HasStateAuthority)
        {
            long nextGold = (long)NetworkGold + amount;
            NetworkGold = (int)Math.Min(MaxSafeGold, Math.Max(0L, nextGold));
            ProgressionChanged?.Invoke();
            if (IsLocallyControlled) CombatHud.NotifyGoldGained(amount, source);
            PlayerSaveManager.RequestAutoSave($"AddGold_{source}");
            Debug.Log($"[PlayerCombat] +{amount} Gold from {source} (total={NetworkGold}).");
        }
        else if (IsLocallyControlled)
        {
            RPC_AddGold(amount, source);
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_AddGold(int amount, string source)
    {
        if (!Object.HasStateAuthority || amount <= 0) return;
        AddGold(amount, source);
    }

    public bool TrySpendGold(int amount)
    {
        if (!_isSpawned || amount < 0) return false;
        if (amount == 0) return true;
        if (NetworkGold < amount) return false;

        if (Object.HasStateAuthority)
        {
            NetworkGold = Mathf.Max(0, NetworkGold - amount);
            ProgressionChanged?.Invoke();
            PlayerSaveManager.RequestAutoSave("SpendGold");
            return true;
        }
        if (IsLocallyControlled)
        {
            RPC_SpendGold(amount);
            return true;
        }
        return false;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SpendGold(int amount)
    {
        if (!Object.HasStateAuthority || amount <= 0 || NetworkGold < amount) return;
        NetworkGold = Mathf.Max(0, NetworkGold - amount);
        ProgressionChanged?.Invoke();
        PlayerSaveManager.RequestAutoSave("SpendGold");
    }

    /// <summary>Awards EXP and automatically processes Level-Ups (+3 Stat Points per level).</summary>
    public void AddExperience(int amount)
    {
        if (!_isSpawned || amount <= 0) return;
        if (Object.HasStateAuthority)
        {
            ApplyExperienceAuthority(amount);
        }
        else if (IsLocallyControlled)
        {
            RPC_AddExperience(amount);
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_AddExperience(int amount)
    {
        if (!Object.HasStateAuthority || amount <= 0) return;
        ApplyExperienceAuthority(amount);
    }

    /// <summary>
    /// คำนวณ EXP และการอัปเลเวลฝั่ง State Authority โดยล็อกเพดานสูงสุดที่ MaxLevel = 30
    /// เมื่อถึง Lv.30 จะหยุดสะสม EXP ทันที (NetworkExperience = 0) เพื่อแสดงสถานะ MAX
    /// </summary>
    private void ApplyExperienceAuthority(int amount)
    {
        int currentLvl = Mathf.Clamp(NetworkLevel, 1, MaxLevel);
        if (currentLvl >= MaxLevel)
        {
            // ถึงเพดาน Lv.30 แล้ว: คงเลเวลไว้ที่ 30 และล้าง EXP ส่วนเกินให้เป็น 0
            NetworkLevel = MaxLevel;
            NetworkExperience = 0;
            ProgressionChanged?.Invoke();
            return;
        }

        int exp = Mathf.Max(0, NetworkExperience) + amount;
        int lvl = currentLvl;
        int pointsGained = 0;

        while (lvl < MaxLevel && exp >= GetExpRequiredForNextLevel(lvl))
        {
            exp -= GetExpRequiredForNextLevel(lvl);
            lvl++;
            pointsGained += StatPointsPerLevel;
        }

        if (lvl >= MaxLevel)
        {
            lvl = MaxLevel;
            exp = 0;
        }

        bool leveledUp = lvl > NetworkLevel;
        NetworkExperience = exp;
        if (leveledUp)
        {
            NetworkLevel = lvl;
            NetworkUnspentStatPoints += pointsGained;
            NetworkHealth = MaxHealth;
            NetworkStamina = MaxStamina;
            Debug.Log($"[PlayerCombat] LEVEL UP! {name} reached Level {NetworkLevel}{(NetworkLevel >= MaxLevel ? " (MAX)" : "")} (+{pointsGained} Stat Points, total unspent={NetworkUnspentStatPoints}).");
        }
        ProgressionChanged?.Invoke();
        PlayerSaveManager.RequestAutoSave(leveledUp ? "LevelUp" : "AddExperience");
    }

    /// <summary>Allocates Stat Points + scaled Gold cost into STR, VIT, END, or AGI.</summary>
    public bool AllocateStatPoint(PlayerStatType statType)
    {
        if (!_isSpawned) return false;
        int curVal = GetStatPointValue(statType);
        int ptCost = GetAttributeUpgradePointCost(curVal);
        int goldCost = GetAttributeUpgradeGoldCost(curVal);
        if (NetworkUnspentStatPoints < ptCost || NetworkGold < goldCost) return false;

        if (Object.HasStateAuthority)
        {
            return ApplyAllocateStatAuthority(statType);
        }
        if (IsLocallyControlled)
        {
            RPC_AllocateStatPoint((int)statType);
            return true;
        }
        return false;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_AllocateStatPoint(int statTypeInt)
    {
        if (!Object.HasStateAuthority) return;
        ApplyAllocateStatAuthority((PlayerStatType)statTypeInt);
    }

    private bool ApplyAllocateStatAuthority(PlayerStatType statType)
    {
        int curVal = GetStatPointValue(statType);
        int ptCost = GetAttributeUpgradePointCost(curVal);
        int goldCost = GetAttributeUpgradeGoldCost(curVal);

        if (NetworkUnspentStatPoints < ptCost || NetworkGold < goldCost) return false;

        int prevMaxHp = MaxHealth;
        float prevMaxSta = MaxStamina;

        NetworkUnspentStatPoints -= ptCost;
        NetworkGold -= goldCost;

        switch (statType)
        {
            case PlayerStatType.STR: StatPointsSTR++; break;
            case PlayerStatType.VIT: StatPointsVIT++; break;
            case PlayerStatType.END: StatPointsEND++; break;
            case PlayerStatType.AGI: StatPointsAGI++; break;
            default:
                NetworkUnspentStatPoints += ptCost;
                NetworkGold += goldCost;
                return false;
        }

        int hpDelta = MaxHealth - prevMaxHp;
        if (hpDelta > 0 && !IsDead) NetworkHealth = Mathf.Min(MaxHealth, NetworkHealth + hpDelta);

        float staDelta = MaxStamina - prevMaxSta;
        if (staDelta > 0f && !IsDead) NetworkStamina = Mathf.Min(MaxStamina, NetworkStamina + staDelta);

        Debug.Log($"[PlayerCombat] Allocated +1 {statType} (cost {ptCost}pt + {goldCost}g). STR={StatPointsSTR}, VIT={StatPointsVIT}, END={StatPointsEND}, AGI={StatPointsAGI}.");
        ProgressionChanged?.Invoke();
        PlayerSaveManager.RequestAutoSave($"Allocate_{statType}");
        return true;
    }

    /// <summary>
    /// ส่งออกข้อมูลตัวละครลงไฟล์เซฟ โดยควบคุมเลเวลไม่เกิน MaxLevel (30) และ Stamina ไม่เกินเพดาน 150
    /// </summary>
    public void ExportSaveData(PlayerSaveData data)
    {
        if (data == null || !_isSpawned) return;
        int clampedLvl = Mathf.Clamp(NetworkLevel, 1, MaxLevel);
        data.level = clampedLvl;
        data.experience = clampedLvl >= MaxLevel ? 0 : Mathf.Max(0, NetworkExperience);
        data.unspentStatPoints = Mathf.Max(0, NetworkUnspentStatPoints);
        data.statSTR = StatPointsSTR;
        data.statVIT = StatPointsVIT;
        data.statEND = StatPointsEND;
        data.statAGI = StatPointsAGI;
        data.gold = Mathf.Max(0, NetworkGold);
        data.currentHealth = Mathf.Clamp(NetworkHealth, 0, MaxHealth);
        data.currentStamina = Mathf.Clamp(NetworkStamina, 0f, MaxStamina);
    }

    /// <summary>
    /// นำข้อมูลจากไฟล์เซฟกลับมาใช้ โดยบังคับ Clamp เลเวลไม่เกิน MaxLevel = 30, ตรวจสอบงบแต้มสเตตัสไม่เกินแต้มจริงของเลเวลนั้น,
    /// และจำกัดเพดาน Gold / HP / Stamina เพื่อป้องกันการแก้ไขไฟล์เซฟโกงค่า
    /// </summary>
    public void ApplySaveData(PlayerSaveData data)
    {
        if (data == null || !_isSpawned) return;
        if (!Object.HasStateAuthority) return;

        int clampedLvl = Mathf.Clamp(data.level, 1, MaxLevel);
        int maxEarnedPoints = Mathf.Max(0, clampedLvl - 1) * StatPointsPerLevel;

        int str = Mathf.Clamp(data.statSTR, 0, MaxSingleStatValue);
        int vit = Mathf.Clamp(data.statVIT, 0, MaxSingleStatValue);
        int end = Mathf.Clamp(data.statEND, 0, MaxSingleStatValue);
        int agi = Mathf.Clamp(data.statAGI, 0, MaxSingleStatValue);

        int spent = GetCumulativePointsForStatValue(str)
                  + GetCumulativePointsForStatValue(vit)
                  + GetCumulativePointsForStatValue(end)
                  + GetCumulativePointsForStatValue(agi);

        if (spent > maxEarnedPoints)
        {
            int rawSpent = spent;
            while (spent > maxEarnedPoints && (str > 0 || vit > 0 || end > 0 || agi > 0))
            {
                if (str >= vit && str >= end && str >= agi) str--;
                else if (vit >= str && vit >= end && vit >= agi) vit--;
                else if (end >= str && end >= vit && end >= agi) end--;
                else agi--;

                spent = GetCumulativePointsForStatValue(str)
                      + GetCumulativePointsForStatValue(vit)
                      + GetCumulativePointsForStatValue(end)
                      + GetCumulativePointsForStatValue(agi);
            }
            Debug.LogWarning($"[PlayerCombat] Save stat budget sanitized for Lv.{clampedLvl} (spent {rawSpent} > max {maxEarnedPoints}) -> STR={str}, VIT={vit}, END={end}, AGI={agi} (spent={spent}).");
        }

        int remainingBudget = Mathf.Max(0, maxEarnedPoints - spent);
        int clampedUnspent = Mathf.Clamp(data.unspentStatPoints, 0, remainingBudget);
        if (data.unspentStatPoints > remainingBudget)
        {
            Debug.LogWarning($"[PlayerCombat] Save unspentStatPoints sanitized for Lv.{clampedLvl}: {data.unspentStatPoints} -> {clampedUnspent}.");
        }

        NetworkLevel = clampedLvl;
        NetworkExperience = clampedLvl >= MaxLevel
            ? 0
            : Mathf.Clamp(data.experience, 0, Mathf.Max(0, GetExpRequiredForNextLevel(clampedLvl) - 1));
        NetworkUnspentStatPoints = clampedUnspent;
        StatPointsSTR = str;
        StatPointsVIT = vit;
        StatPointsEND = end;
        StatPointsAGI = agi;
        NetworkGold = Mathf.Clamp(data.gold, 0, MaxSafeGold);
        NetworkHealth = Mathf.Clamp(NetworkHealth, 1, MaxHealth);
        NetworkStamina = Mathf.Clamp(NetworkStamina, 1f, MaxStamina);
        ProgressionChanged?.Invoke();
    }

    /// <summary>
    /// กำหนดค่า HP และ Stamina จากไฟล์เซฟหลังสวมใส่อุปกรณ์ครบแล้ว โดย Clamp ตาม MaxHealth และ MaxStamina (<= 150) ใหม่เสมอ
    /// </summary>
    public void ApplySaveHealthAndStamina(int savedHp, float savedStamina)
    {
        if (!_isSpawned || !Object.HasStateAuthority) return;
        NetworkHealth = savedHp > 0 ? Mathf.Clamp(savedHp, 1, MaxHealth) : MaxHealth;
        NetworkStamina = savedStamina > 0f ? Mathf.Clamp(savedStamina, 1f, MaxStamina) : MaxStamina;
        HealthChanged?.Invoke(NetworkHealth, MaxHealth);
        StaminaChanged?.Invoke(NetworkStamina, MaxStamina);
    }

    /// <summary>Syncs total equipped gear bonuses (Sword + Shield + Armor + Pants) over Photon Fusion.</summary>
    public void ApplyEquipmentBonuses(int dmgBonus, int maxHpBonus, int maxStaBonus, int defBonus, float moveSpeedBonus, float critBonus)
    {
        if (!_isSpawned) return;
        if (Object.HasStateAuthority)
        {
            SetEquipmentBonusesAuthority(dmgBonus, maxHpBonus, maxStaBonus, defBonus, moveSpeedBonus, critBonus);
        }
        else if (IsLocallyControlled)
        {
            RPC_SyncEquipmentBonuses(dmgBonus, maxHpBonus, maxStaBonus, defBonus, moveSpeedBonus, critBonus);
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SyncEquipmentBonuses(int dmgBonus, int maxHpBonus, int maxStaBonus, int defBonus, float moveSpeedBonus, float critBonus)
    {
        if (!Object.HasStateAuthority) return;
        SetEquipmentBonusesAuthority(dmgBonus, maxHpBonus, maxStaBonus, defBonus, moveSpeedBonus, critBonus);
    }

    private void SetEquipmentBonusesAuthority(int dmgBonus, int maxHpBonus, int maxStaBonus, int defBonus, float moveSpeedBonus, float critBonus)
    {
        int prevMaxHp = MaxHealth;
        float prevMaxSta = MaxStamina;

        // บีบเพดานโบนัสอุปกรณ์ฝั่ง State Authority ป้องกันค่าผิดปกติจากการยิง RPC โกงอุปกรณ์
        NetworkGearDamageBonus = Mathf.Clamp(dmgBonus, 0, 250);
        NetworkGearMaxHealthBonus = Mathf.Clamp(maxHpBonus, 0, 1500);
        NetworkGearMaxStaminaBonus = Mathf.Clamp(maxStaBonus, 0, 100);
        NetworkGearDefenseBonus = Mathf.Clamp(defBonus, 0, 250);
        NetworkGearMoveSpeedBonus = Mathf.Clamp(moveSpeedBonus, 0f, 4f);
        NetworkGearCritBonus = Mathf.Clamp(critBonus, 0f, 1f);

        int hpDiff = MaxHealth - prevMaxHp;
        if (hpDiff > 0 && !IsDead)
            NetworkHealth = Mathf.Min(MaxHealth, NetworkHealth + hpDiff);
        else if (NetworkHealth > MaxHealth)
            NetworkHealth = MaxHealth;

        float staDiff = MaxStamina - prevMaxSta;
        if (staDiff > 0f && !IsDead)
            NetworkStamina = Mathf.Min(MaxStamina, NetworkStamina + staDiff);
        else if (NetworkStamina > MaxStamina)
            NetworkStamina = MaxStamina;

        ProgressionChanged?.Invoke();
    }

    private void OnProgressionNetChanged()
    {
        if (!_isSpawned) return;
        ProgressionChanged?.Invoke();
        HealthChanged?.Invoke(NetworkHealth, MaxHealth);
        StaminaChanged?.Invoke(NetworkStamina, MaxStamina);
    }

    // ------------------------------------------------------------------ 1. local input & stamina tick

    // แฟล็กแจ้งผู้ฟัง (เช่น YeenVFXAutoIntegrator) ว่าการเพิ่ม HP รอบนี้มาจากการพักฟื้นอัตโนมัติใน SafeZone ไม่ใช่การดื่มยาเลือด
    public bool IsSafeZoneRegenTick { get; private set; }
    private float _safeZoneHpRegenCarry;

    public bool IsLocallyControlled =>
        Object.HasInputAuthority || (Runner != null && Runner.GameMode == GameMode.Shared && Object.HasStateAuthority);

    public override void FixedUpdateNetwork()
    {
        if (!_isSpawned || !Object.HasStateAuthority) return;
        bool inSafe = SafeZone.IsPointInAnySafeZone(transform.position);
        IsInSafeZone = inSafe;

        // เมื่อเดินเข้าสู่เขตปลอดภัย (Comfort Zone) ให้เก็บดาบและปิดสถานะต่อสู้อัตโนมัติทันที
        if (inSafe && (IsWeaponDrawn || IsBlocking))
        {
            SetAuthorityWeaponDrawn(false);
        }

        // ฟื้นฟู HP อัตโนมัติอย่างนุ่มนวลขณะพักอยู่ในเขตปลอดภัย (SafeZone Sanctuary Regen: +15% MaxHP/วิ)
        if (inSafe && !IsDead && NetworkHealth < MaxHealth)
        {
            float healPerSec = Mathf.Max(10f, MaxHealth * 0.15f);
            _safeZoneHpRegenCarry += healPerSec * Runner.DeltaTime;
            int wholeHeal = Mathf.FloorToInt(_safeZoneHpRegenCarry);
            if (wholeHeal > 0)
            {
                _safeZoneHpRegenCarry -= wholeHeal;
                IsSafeZoneRegenTick = true;
                try
                {
                    NetworkHealth = Mathf.Min(MaxHealth, NetworkHealth + wholeHeal);
                    HealthChanged?.Invoke(NetworkHealth, MaxHealth);
                }
                finally
                {
                    IsSafeZoneRegenTick = false;
                }
            }
        }
        else
        {
            _safeZoneHpRegenCarry = 0f;
        }

        TickStaminaRegen(Runner.DeltaTime);
    }

    private void Update()
    {
        if (_blockVfxRoot != null && _blockVfxRoot.activeSelf)
        {
            bool parryFlash = Time.time < _parryFlashUntil;
            if (_blockDome != null)
            {
                float s = parryFlash ? 1.22f : (1f + Mathf.Sin(Time.time * 6f) * 0.035f);
                _blockDome.localScale = new Vector3(1.35f * s, 1.65f * s, 1.15f * s);
                if (_blockDomeRenderer == null) _blockDomeRenderer = _blockDome.GetComponent<Renderer>();
                if (_blockDomeRenderer != null)
                {
                    _blockDomeMpb ??= new MaterialPropertyBlock();
                    Color shieldColor = parryFlash
                        ? new Color(1.0f, 0.92f, 0.35f, 0.55f)
                        : (IsInParryWindow ? new Color(0.32f, 0.98f, 1.0f, 0.34f) : new Color(0.18f, 0.72f, 1.0f, 0.24f));
                    _blockDomeMpb.SetColor("_BaseColor", shieldColor);
                    _blockDomeMpb.SetColor("_Color", shieldColor);
                    _blockDomeRenderer.SetPropertyBlock(_blockDomeMpb);
                }
            }
            if (_blockRing != null)
            {
                float r = parryFlash ? 2.25f : (1.65f + Mathf.Sin(Time.time * 4.5f) * 0.08f);
                _blockRing.localScale = new Vector3(r, 0.02f, r);
            }
        }

        if (!_isSpawned) return;

        if (Object.HasStateAuthority && IsHitWindowOpen && !IsDead)
        {
            PollActiveWeaponHits();
        }

        if (!IsLocallyControlled) return;

        if (IsDead)
        {
            _bufferedUntil = -1f;
            SetBlockingInput(false);
            return;
        }

        // ตรวจจับปุ่ม [1] สำหรับชักดาบ / เก็บดาบด้วยตนเองนอกเขตปลอดภัย
        if (PollWeaponTogglePressed())
        {
            TryToggleWeaponDraw();
        }

        if (PollDodgePressed())
        {
            TryRequestDodge();
        }

        bool inSafeNow = IsInSafeZone || SafeZone.IsPointInAnySafeZone(transform.position);

        if (Application.isFocused && Time.time >= _overrideBlockUntil)
        {
            bool blockPressedNow = PollBlockJustPressed();
            bool wantBlock = !IsDodging && !inSafeNow && IsWeaponDrawn && CurrentStamina > 0f && PollBlockHeld();
            if (blockPressedNow && !inSafeNow && !IsWeaponDrawn)
            {
                NotifyWeaponMustBeDrawn();
            }
            SetBlockingInput(wantBlock);
        }

        // รองรับทั้งโจมตีปกติ (LMB -> Light / Run / Jump / Stealth Assassination) และโจมตีหนัก (R -> Heavy / RunHeavy / JumpHeavy)
        if (PollAttackPressed())
        {
            if (inSafeNow)
            {
                NotifySafeZoneWeaponsDisabled();
            }
            else if (!IsWeaponDrawn)
            {
                NotifyWeaponMustBeDrawn();
            }
            else
            {
                _bufferedUntil = Time.time + inputBufferSeconds;
                _bufferedHeavy = false;
            }
        }
        else if (PollHeavyAttackPressed())
        {
            if (inSafeNow)
            {
                NotifySafeZoneWeaponsDisabled();
            }
            else if (!IsWeaponDrawn)
            {
                NotifyWeaponMustBeDrawn();
            }
            else
            {
                _bufferedUntil = Time.time + inputBufferSeconds;
                _bufferedHeavy = true;
            }
        }

        if (_bufferedUntil >= Time.time && Time.time >= _nextLocalAttackTime && !IsDodging)
        {
            _bufferedUntil = -1f;
            if (CanAttack)
            {
                if (_localBlockingSent) SetBlockingInput(false);
                if (_bufferedHeavy)
                    TryRequestAttackKind(ResolveContextualHeavyAttackKind());
                else
                    TryRequestAttack();
            }
        }
    }

    private void TickStaminaRegen(float dt)
    {
        if (IsDead) return;

        bool sprinting = _capsule != null && _capsule.NetworkSprinting;
        bool freeSprint = IsFreeExplorationSprintActive;
        // ในโหมดสำรวจนอกต่อสู้หรือ SafeZone การวิ่ง Sprint ไม่เสีย Stamina และยังฟื้น Stamina กลับเต็มได้ปกติ
        if ((sprinting && !freeSprint) || IsDodging || IsHitWindowOpen) return;
        if (Time.time < _nextStaminaRegenTime && !freeSprint) return;

        float cap = MaxStamina;
        if (NetworkStamina < cap)
        {
            // ใช้ TotalStaminaRegenRate ที่รวมโบนัสฟื้นฟู +4%/แต้ม จากค่า END
            NetworkStamina = Mathf.Min(cap, NetworkStamina + TotalStaminaRegenRate * dt);
            if (_sprintExhaustedCooldown && NetworkStamina >= 12f)
                _sprintExhaustedCooldown = false;
        }
    }

    public bool ConsumeSprintStamina(float dt)
    {
        if (!_isSpawned || IsDead) return false;

        // วิ่ง Sprint ฟรีเมื่ออยู่ใน SafeZone หรืออยู่นอกการต่อสู้ (Out-of-Combat) เพื่อสำรวจแผนที่ FinalTestv1 ได้ไม่เหนื่อย
        if (IsFreeExplorationSprintActive)
        {
            _sprintExhaustedCooldown = false;
            return true;
        }

        if (_sprintExhaustedCooldown || NetworkStamina <= 0f)
        {
            _sprintExhaustedCooldown = true;
            return false;
        }

        if (Object.HasStateAuthority)
        {
            float cost = sprintStaminaPerSecond * dt;
            NetworkStamina = Mathf.Max(0f, NetworkStamina - cost);
            _nextStaminaRegenTime = Time.time + staminaRegenDelay;
            if (NetworkStamina <= 0.01f)
            {
                NetworkStamina = 0f;
                _sprintExhaustedCooldown = true;
                return false;
            }
        }
        return NetworkStamina > 0f;
    }

    private bool TryConsumeAuthorityStamina(float cost)
    {
        if (!Object.HasStateAuthority) return false;
        if (NetworkStamina <= 0f) return false;

        NetworkStamina = Mathf.Max(0f, NetworkStamina - cost);
        _nextStaminaRegenTime = Time.time + staminaRegenDelay;
        if (NetworkStamina <= 0.01f)
        {
            NetworkStamina = 0f;
            _sprintExhaustedCooldown = true;
        }
        return true;
    }

    public void SetStamina(float value)
    {
        if (!_isSpawned || !Object.HasStateAuthority) return;
        NetworkStamina = Mathf.Clamp(value, 0f, MaxStamina);
        if (NetworkStamina <= 0f)
        {
            _sprintExhaustedCooldown = true;
            _nextStaminaRegenTime = Time.time + staminaRegenDelay;
        }
        else if (NetworkStamina >= 12f)
        {
            _sprintExhaustedCooldown = false;
        }
    }

    public void RestoreStamina(float amount)
    {
        if (!_isSpawned || !Object.HasStateAuthority || IsDead || amount <= 0f) return;
        float before = NetworkStamina;
        NetworkStamina = Mathf.Min(MaxStamina, NetworkStamina + amount);
        _sprintExhaustedCooldown = false;
        Debug.Log($"[PlayerCombat] Restored {NetworkStamina - before:0} Stamina ({before:0} -> {NetworkStamina:0}/{MaxStamina:0}).");
    }

    public void ApplyAttackBuff(int bonusDamage, float durationSeconds)
    {
        if (bonusDamage <= 0 || durationSeconds <= 0f) return;
        _attackBuffBonus = bonusDamage;
        _attackBuffUntil = Time.time + durationSeconds;
        ProgressionChanged?.Invoke();
        Debug.Log($"[PlayerCombat] Applied +{bonusDamage} Attack Buff for {durationSeconds:0}s.");
    }

    /// <summary>
    /// ตรวจจับการกดปุ่มโจมตีปกติ (คลิกซ้าย Mouse Left Button)
    /// </summary>
    private static bool PollAttackPressed()
    {
        if (!Application.isFocused || GameplayInput.IsBlocked) return false;

        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return false;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return false;

        return true;
    }

    /// <summary>
    /// ตรวจจับการกดปุ่มโจมตีหนัก (ปุ่ม R) เพื่อสั่งท่า Heavy Slash / Run Heavy / Jump Heavy
    /// </summary>
    private static bool PollHeavyAttackPressed()
    {
        if (!Application.isFocused || GameplayInput.IsBlocked) return false;

        var kb = Keyboard.current;
        if (kb == null || !kb.rKey.wasPressedThisFrame) return false;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return false;

        return true;
    }

    // ปุ่มควบคุมมาตรฐานของระบบต่อสู้ (Block/Parry = F เท่านั้น ตัด Alt ออก, Dodge = Left Ctrl เพื่อไม่ชนกับ Spacebar กระโดด)
    public const Key BlockParryKey = Key.F;
    public const Key DodgeKey = Key.LeftCtrl;

    /// <summary>
    /// ตรวจจับการกดค้างปุ่มตั้งการ์ด/ปัดป้อง (ปุ่ม F เท่านั้น — ตัด LeftAlt / RightAlt และปุ่มเมาส์ออกทั้งหมด)
    /// </summary>
    private static bool PollBlockHeld()
    {
        if (!Application.isFocused || GameplayInput.IsBlocked || IsAnyTextFieldFocused()) return false;

        var kb = Keyboard.current;
        return kb != null && kb.fKey.isPressed;
    }

    /// <summary>
    /// ตรวจจับปุ่มกลิ้งหลบ (Left Ctrl เท่านั้น — ยกเลิก Spacebar เพราะย้าย Spacebar ไปเป็นปุ่มกระโดด Jump แล้ว)
    /// </summary>
    private static bool PollDodgePressed()
    {
        if (!Application.isFocused || GameplayInput.IsBlocked || IsAnyTextFieldFocused()) return false;

        var kb = Keyboard.current;
        return kb != null && kb.leftCtrlKey.wasPressedThisFrame;
    }

    /// <summary>
    /// ตรวจจับปุ่มเลข [1] สำหรับสลับชักดาบ / เก็บดาบ (บล็อกอัตโนมัติเมื่อเปิดหน้าต่าง UI หรือกำลังพิมพ์ข้อความ)
    /// </summary>
    private static bool PollWeaponTogglePressed()
    {
        if (!Application.isFocused || GameplayInput.IsBlocked || IsAnyTextFieldFocused()) return false;
        if (InventoryUI.Instance != null && InventoryUI.Instance.IsOpen) return false;
        if (MapManager.Instance != null && MapManager.Instance.IsWorldMapOpen) return false;

        var kb = Keyboard.current;
        return kb != null && (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame);
    }

    /// <summary>
    /// ตรวจจับจังหวะกดปุ่มบล็อก [F] ครั้งแรกในเฟรมนี้ (ตัด Alt ออกทั้งหมด) เพื่อแจ้งเตือนให้ชักดาบหากยังเก็บดาบอยู่
    /// </summary>
    private static bool PollBlockJustPressed()
    {
        if (!Application.isFocused || GameplayInput.IsBlocked || IsAnyTextFieldFocused()) return false;
        var kb = Keyboard.current;
        return kb != null && kb.fKey.wasPressedThisFrame;
    }

    /// <summary>
    /// ตรวจสอบว่าผู้เล่นกำลังพิมพ์ในช่อง InputField หรือไม่ เพื่อไม่ให้ปุ่มลัดทำงานชนกับการพิมพ์
    /// </summary>
    private static bool IsAnyTextFieldFocused()
    {
        if (EventSystem.current == null) return false;
        var selected = EventSystem.current.currentSelectedGameObject;
        if (selected == null) return false;
        return selected.GetComponent<TMP_InputField>() != null || selected.GetComponent<UnityEngine.UI.InputField>() != null;
    }

    /// <summary>
    /// แสดงข้อความแจ้งเตือนแบบหน่วงเวลาให้กด [1] ชักดาบก่อนโจมตีหรือตั้งการ์ด
    /// </summary>
    public void NotifyWeaponMustBeDrawn()
    {
        if (Time.unscaledTime - _lastSheathedPromptTime < 1.4f) return;
        _lastSheathedPromptTime = Time.unscaledTime;
        PlayerStatusHud.Instance?.ShowToast(DrawWeaponPromptMessage, new Color(1f, 0.84f, 0.25f), 2.2f);
        Debug.Log($"[PlayerCombat] Combat action blocked while sheathed: {DrawWeaponPromptMessage}");
    }

    private void NotifySafeZoneWeaponsDisabled()
    {
        if (Time.unscaledTime - _lastSafeZonePromptTime < 1.4f) return;
        _lastSafeZonePromptTime = Time.unscaledTime;
        PlayerStatusHud.Instance?.ShowToast("Comfort Zone: Weapons Disabled", new Color(0.32f, 0.96f, 0.72f), 2.0f);
    }

    /// <summary>
    /// สลับสถานะชักดาบ / เก็บดาบด้วยตนเอง (เรียกจากปุ่ม [1] หรือสคริปต์ทดสอบ)
    /// </summary>
    public bool TryToggleWeaponDraw()
    {
        return TrySetWeaponDrawn(!IsWeaponDrawn);
    }

    /// <summary>
    /// สั่งเปลี่ยนสถานะชักดาบ (true) หรือเก็บดาบ (false) พร้อมซิงก์ผ่าน Photon Fusion ไปยังทุกเครื่อง
    /// </summary>
    public bool TrySetWeaponDrawn(bool drawn)
    {
        if (!_isSpawned || !IsLocallyControlled || IsDead) return false;
        if (Time.time < _nextWeaponToggleTime) return false;

        bool inSafe = IsInSafeZone || SafeZone.IsPointInAnySafeZone(transform.position);
        if (drawn && inSafe)
        {
            NotifySafeZoneWeaponsDisabled();
            return false;
        }

        if (IsWeaponDrawn == drawn) return false;

        _nextWeaponToggleTime = Time.time + 0.32f;
        _bufferedUntil = -1f;
        if (!drawn && _localBlockingSent) SetBlockingInput(false);

        if (Object.HasStateAuthority)
            SetAuthorityWeaponDrawn(drawn);
        else
            RPC_SetWeaponDrawn(drawn);

        return true;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SetWeaponDrawn(NetworkBool drawn)
    {
        if (!Object.HasStateAuthority) return;
        SetAuthorityWeaponDrawn(drawn);
    }

    /// <summary>
    /// อัปเดตสถานะการชักดาบฝั่ง State Authority (บังคับเก็บดาบเสมอหากอยู่ในเขตปลอดภัยหรือตาย)
    /// </summary>
    private void SetAuthorityWeaponDrawn(bool wantDrawn)
    {
        if (!Object.HasStateAuthority) return;

        bool inSafe = IsInSafeZone || SafeZone.IsPointInAnySafeZone(transform.position);
        bool effectiveDrawn = !IsDead && !inSafe && wantDrawn;

        if (!effectiveDrawn)
        {
            if (IsBlocking) SetAuthorityBlockingState(false);
            if (IsHitWindowOpen) CloseHitWindow();
        }

        if (IsWeaponDrawn == effectiveDrawn) return;

        IsWeaponDrawn = effectiveDrawn;
        Debug.Log($"[PlayerCombat] Weapon stance changed -> {(effectiveDrawn ? "DRAWN (Combat Ready)" : "SHEATHED (Exploration Mode)")}.");
    }

    /// <summary>
    /// Callback บนทุกเครื่องเมื่อค่า Networked IsWeaponDrawn เปลี่ยนแปลง (อัปเดตแอนิเมชันและแจ้ง Event ให้ทีม UI/Audio)
    /// </summary>
    private void OnWeaponDrawnChanged()
    {
        if (!_isSpawned) return;
        bool drawn = IsWeaponDrawn && !IsInSafeZone && !IsDead;
        _animationDriver?.NotifyWeaponDrawnChanged(drawn);
        WeaponDrawnChanged?.Invoke(drawn);
    }

    /// <summary>
    /// สั่งโจมตีปกติแบบฉลาดตามบริบท (Stealth Assassination / Jump Attack / Run Attack / Light Combo)
    /// </summary>
    public bool TryRequestAttack()
    {
        return TryRequestAttackKind(ResolveContextualLightAttackKind());
    }

    /// <summary>
    /// คำนวณระยะเวลา Cooldown และช่วงเปิด Hitbox ของแต่ละรูปแบบท่าโจมตีให้สอดคล้องกับความเร็วอนิเมชันจริง
    /// </summary>
    public void GetAttackTiming(PlayerAttackKind kind, int comboStep, out float cooldown, out float hitDelay, out float hitDuration)
    {
        float baseCd = Mathf.Max(0.15f, attackCooldown);
        float baseDelay = Mathf.Max(0.02f, hitWindowDelay);
        float baseDur = Mathf.Max(0.08f, hitWindowDuration);

        switch (kind)
        {
            case PlayerAttackKind.HeavyCombo:
                cooldown = baseCd * 1.28f;
                hitDelay = Mathf.Max(0.14f, baseDelay * 1.9f);
                hitDuration = baseDur * 1.15f;
                break;

            case PlayerAttackKind.RunAttack:
                cooldown = baseCd * 0.95f;
                hitDelay = baseDelay;
                hitDuration = baseDur;
                break;

            case PlayerAttackKind.RunHeavyAttack:
                cooldown = baseCd * 1.22f;
                hitDelay = Mathf.Max(0.13f, baseDelay * 1.7f);
                hitDuration = baseDur * 1.15f;
                break;

            case PlayerAttackKind.JumpAttack:
                cooldown = baseCd * 0.92f;
                hitDelay = Mathf.Max(0.05f, baseDelay * 0.8f);
                hitDuration = baseDur;
                break;

            case PlayerAttackKind.JumpHeavyAttack:
            case PlayerAttackKind.RunJumpHeavyAttack:
                cooldown = baseCd * 1.35f;
                hitDelay = Mathf.Max(0.16f, baseDelay * 2.1f);
                hitDuration = baseDur * 1.25f;
                break;

            case PlayerAttackKind.StealthAssassination:
                cooldown = baseCd * 1.35f;
                hitDelay = baseDelay * 1.2f;
                hitDuration = baseDur * 1.2f;
                break;

            case PlayerAttackKind.SpellCast:
                cooldown = baseCd * 1.05f;
                hitDelay = Mathf.Max(0.14f, baseDelay * 1.8f);
                hitDuration = baseDur * 0.75f;
                break;

            default:
                // LightCombo: จังหวะที่ 1-2 ออกไว, จังหวะปิดคอมโบที่ 3 หนักแน่นขึ้นเล็กน้อย
                if (comboStep >= 3)
                {
                    cooldown = baseCd * 1.05f;
                    hitDelay = baseDelay * 1.15f;
                    hitDuration = baseDur;
                }
                else
                {
                    cooldown = baseCd * 0.85f;
                    hitDelay = baseDelay;
                    hitDuration = baseDur * 0.85f;
                }
                break;
        }
    }

    /// <summary>
    /// สั่งโจมตีตามรูปแบบท่าที่ระบุ (ส่งผ่าน Photon Fusion RPC เพื่อซิงก์ทุกเครื่อง)
    /// </summary>
    public bool TryRequestAttackKind(PlayerAttackKind kind)
    {
        if (!_isSpawned || !IsLocallyControlled || IsDead) return false;
        if (IsInSafeZone || SafeZone.IsPointInAnySafeZone(transform.position))
        {
            Debug.Log("[PlayerCombat] Attack prevented: Inside Comfort Zone (Safe Haven).");
            return false;
        }
        if (!IsWeaponDrawn)
        {
            NotifyWeaponMustBeDrawn();
            return false;
        }
        if (CurrentStamina <= 0f)
        {
            Debug.Log("[PlayerCombat] Attack prevented: Stamina depleted.");
            return false;
        }
        if (Time.time < _nextLocalAttackTime) return false;

        int predictedStep = (Time.time - _lastLocalAttackTime <= comboResetSeconds)
            ? (_localPredictedComboStep % 3) + 1
            : 1;
        _localPredictedComboStep = predictedStep;
        _lastLocalAttackTime = Time.time;

        GetAttackTiming(kind, predictedStep, out float attackCd, out _, out _);
        _nextLocalAttackTime = Time.time + attackCd;
        _lastCombatActivityTime = Time.time;

        if (kind == PlayerAttackKind.LightCombo)
            RPC_RequestAttack();
        else
            RPC_RequestAttackKind((byte)kind);
        return true;
    }

    /// <summary>
    /// วิเคราะห์บริบทการโจมตีเบา (LMB): ลอบสังหารด้านหลัง -> กลางอากาศ -> วิ่งฟัน -> คอมโบปกติ
    /// </summary>
    public PlayerAttackKind ResolveContextualLightAttackKind()
    {
        if (ActiveWeaponArchetype != WeaponArchetype.GreatSword)
            return PlayerAttackKind.SpellCast;

        for (int i = 0; i < EnemyNPC.All.Count; i++)
        {
            var npc = EnemyNPC.All[i];
            if (npc != null && npc.IsSpawned && npc.IsAlive && npc.CanBeAssassinatedBy(transform))
                return PlayerAttackKind.StealthAssassination;
        }

        if (_capsule != null && !_capsule.IsGrounded)
            return PlayerAttackKind.JumpAttack;

        if (_capsule != null && _capsule.NetworkSprinting)
            return PlayerAttackKind.RunAttack;

        return PlayerAttackKind.LightCombo;
    }

    /// <summary>
    /// วิเคราะห์บริบทการโจมตีหนัก (ปุ่ม R): วิ่งกระโดดฟัน -> กระโดดฟันหนัก -> วิ่งฟันหนัก -> คอมโบฟันหนัก
    /// </summary>
    public PlayerAttackKind ResolveContextualHeavyAttackKind()
    {
        if (ActiveWeaponArchetype != WeaponArchetype.GreatSword)
            return PlayerAttackKind.SpellCast;

        bool sprinting = _capsule != null && _capsule.NetworkSprinting;
        bool airborne = _capsule != null && !_capsule.IsGrounded;

        if (airborne && sprinting) return PlayerAttackKind.RunJumpHeavyAttack;
        if (airborne) return PlayerAttackKind.JumpHeavyAttack;
        if (sprinting) return PlayerAttackKind.RunHeavyAttack;
        return PlayerAttackKind.HeavyCombo;
    }

    /// <summary>
    /// สั่งเล่นอนิเมชัน Buff เมื่อผู้เล่นใช้ไอเทมยาฟื้นฟูหรือยาเพิ่มพลังโจมตี
    /// </summary>
    public void PlayConsumableBuffEffect()
    {
        if (!_isSpawned || IsDead) return;
        if (Object.HasStateAuthority)
            RPC_BroadcastConsumableBuff();
        else if (IsLocallyControlled)
            RPC_RequestConsumableBuff();
    }

    public bool TryRequestDodge()
    {
        if (!_isSpawned || !IsLocallyControlled || IsDead) return false;
        if (CurrentStamina <= 0f)
        {
            Debug.Log("[PlayerCombat] Dodge prevented: Stamina depleted.");
            return false;
        }
        if (Time.time < _nextLocalDodgeTime) return false;

        _nextLocalDodgeTime = Time.time + dodgeCooldown;
        SetBlockingInput(false);

        Vector3 worldDir = _capsule != null ? _capsule.DesiredMoveWorld() : Vector3.zero;
        if (worldDir.sqrMagnitude < 0.001f)
            worldDir = transform.forward;
        worldDir.Normalize();

        if (_capsule != null)
            _capsule.StartDash(worldDir, dodgeDistance, dodgeDuration);

        Vector3 localDir = Quaternion.Inverse(transform.rotation) * worldDir;
        Vector2 roll2D = new Vector2(localDir.x, localDir.z);
        if (roll2D.sqrMagnitude < 0.001f) roll2D = Vector2.up;
        else roll2D.Normalize();

        RPC_RequestDodge(roll2D.x, roll2D.y);
        return true;
    }

    public void SetBlockingInput(bool blocking, float holdSeconds = 0f)
    {
        if (!_isSpawned || !IsLocallyControlled) return;
        bool inSafe = IsInSafeZone || SafeZone.IsPointInAnySafeZone(transform.position);
        if (IsDead || inSafe || !IsWeaponDrawn || CurrentStamina <= 0f) blocking = false;
        if (holdSeconds > 0f) _overrideBlockUntil = Time.time + holdSeconds;

        if (_capsule != null)
            _capsule.SpeedMultiplier = blocking ? blockMoveSpeedMultiplier : 1f;

        if (_localBlockingSent == blocking) return;
        _localBlockingSent = blocking;
        if (Object.HasStateAuthority)
        {
            SetAuthorityBlockingState(!IsDead && blocking);
        }
        else
        {
            RPC_SetBlocking(blocking);
        }
    }

    /// <summary>
    /// อัปเดตสถานะการตั้งการ์ดฝั่ง State Authority พร้อมบันทึกเวลาเริ่มตั้งการ์ดสำหรับหน้าต่าง Perfect Parry
    /// </summary>
    private void SetAuthorityBlockingState(bool wantBlock)
    {
        if (!Object.HasStateAuthority) return;

        bool inSafe = IsInSafeZone || SafeZone.IsPointInAnySafeZone(transform.position);
        bool effectiveBlock = !IsDead && !inSafe && IsWeaponDrawn && NetworkStamina > 0f && wantBlock;
        bool wasBlocking = IsBlocking;
        IsBlocking = effectiveBlock;

        if (effectiveBlock && !wasBlocking)
        {
            // เปิดหน้าต่าง Perfect Parry เฉพาะเมื่อพ้นคูลดาวน์ Parry แล้ว (ป้องกันการกดบล็อกรัวๆ เพื่อโกง Parry)
            if (Time.time >= _nextParryReadyTime)
            {
                _blockStartedTime = Time.time;
                _nextParryReadyTime = Time.time + parryCooldownSeconds;
            }
            else
            {
                _blockStartedTime = -100f;
            }
        }
        else if (!effectiveBlock)
        {
            _blockStartedTime = -100f;
        }

        UpdateBlockVfxVisual(IsBlocking);
        RPC_BroadcastBlocking(IsBlocking);
    }

    // ------------------------------------------------------------------ 2. RPC pipeline

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RequestAttack()
    {
        ExecuteAuthorityAttack(PlayerAttackKind.LightCombo);
    }

    /// <summary>
    /// รับคำขอโจมตีระบุประเภทท่าทางจาก Input Authority ไปยัง State Authority เพื่อซิงก์ทุกเครื่อง
    /// </summary>
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RequestAttackKind(byte attackKindByte)
    {
        ExecuteAuthorityAttack((PlayerAttackKind)attackKindByte);
    }

    /// <summary>
    /// ประมวลผลการโจมตีฝั่ง State Authority: หัก Stamina, คำนวณ Combo และกระจาย RPC ไปยังผู้เล่นทุกคน
    /// </summary>
    private void ExecuteAuthorityAttack(PlayerAttackKind kind)
    {
        if (!Object.HasStateAuthority) return;
        if (IsDead) return;
        if (IsInSafeZone || SafeZone.IsPointInAnySafeZone(transform.position)) return;
        if (!IsWeaponDrawn) return;
        if (NetworkStamina <= 0f) return;

        float now = Time.time;
        if (now < _nextAuthorityAttackTime - cooldownTolerance) return;

        float staminaCost = (kind == PlayerAttackKind.HeavyCombo || kind == PlayerAttackKind.RunHeavyAttack ||
                             kind == PlayerAttackKind.JumpHeavyAttack || kind == PlayerAttackKind.RunJumpHeavyAttack)
            ? attackStaminaCost * 1.25f
            : attackStaminaCost;
        if (!TryConsumeAuthorityStamina(staminaCost)) return;

        if (now - _lastAuthorityAttackTime <= comboResetSeconds)
            _authorityComboStep = (_authorityComboStep % 3) + 1;
        else
            _authorityComboStep = 1;

        GetAttackTiming(kind, _authorityComboStep, out float attackCd, out _, out _);
        _nextAuthorityAttackTime = now + attackCd;
        _lastCombatActivityTime = now;

        if (IsBlocking)
        {
            IsBlocking = false;
            _blockStartedTime = -100f;
            UpdateBlockVfxVisual(false);
        }

        _lastAuthorityAttackTime = now;
        ComboStep = _authorityComboStep;

        _sequenceCounter++;
        if (kind == PlayerAttackKind.LightCombo)
            RPC_BroadcastAttack(_sequenceCounter, _authorityComboStep);
        else
            RPC_BroadcastAttackKind(_sequenceCounter, _authorityComboStep, (byte)kind);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_BroadcastAttack(int sequence, int comboStep = 1)
    {
        ApplyBroadcastAttack(sequence, comboStep, PlayerAttackKind.LightCombo);
    }

    /// <summary>
    /// กระจายคำสั่งเล่นท่าโจมตีพิเศษ (Heavy, Run, Jump, Stealth Assassination, SpellCast) ไปยังทุกเครื่อง
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_BroadcastAttackKind(int sequence, int comboStep, byte attackKindByte)
    {
        ApplyBroadcastAttack(sequence, comboStep, (PlayerAttackKind)attackKindByte);
    }

    private void ApplyBroadcastAttack(int sequence, int comboStep, PlayerAttackKind kind)
    {
        CurrentSequence = sequence;
        ComboStep = comboStep;
        CurrentAttackKind = kind;
        _lastCombatActivityTime = Time.time;

        if (_animationDriver != null)
            _animationDriver.PlayAttackKind(kind, comboStep);
        else if (animator != null && _attackTriggerHash != 0)
            animator.SetTrigger(_attackTriggerHash);

        if (attackVfx != null) attackVfx.Play();
        onAttackStarted?.Invoke();
        AttackPerformed?.Invoke(kind, comboStep);

        if (_hitWindowRoutine != null) StopCoroutine(_hitWindowRoutine);
        _hitWindowRoutine = StartCoroutine(HitWindowRoutine(kind, comboStep));
    }

    /// <summary>
    /// ซิงก์อนิเมชันรับแรงกระแทกขณะตั้งการ์ดปกติ (Block Hit) ข้ามเครือข่ายพร้อมแจ้ง Event
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_BroadcastBlockHit(int reducedDamage = 0)
    {
        if (!IsDead) _animationDriver?.PlayBlockImpact();
        if (enableCameraShake && IsLocallyControlled)
            PlayerFollowCamera.ShakeActive(0.10f, 0.10f);
        BlockImpacted?.Invoke(reducedDamage);
    }

    /// <summary>
    /// ซิงก์เอฟเฟกต์ปัดป้องสมบูรณ์ (Perfect Parry) ไปยังทุกเครื่อง พร้อม Hit-Stop, Camera Shake และ Event
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_BroadcastParrySuccess(NetworkId attackerNpcId)
    {
        if (IsDead) return;
        _parryFlashUntil = Time.time + 0.25f;
        _animationDriver?.PlayParryImpact();

        EnemyNPC attackerNpc = null;
        if (Runner != null && attackerNpcId.IsValid && Runner.TryFindObject(attackerNpcId, out NetworkObject npcObj) && npcObj != null)
            attackerNpc = npcObj.GetComponent<EnemyNPC>();

        FloatingDamagePopup.SpawnWarning(transform.position + Vector3.up * 2.25f, "PERFECT PARRY!", new Color(0.28f, 0.96f, 1.0f, 1f));

        if (IsLocallyControlled)
        {
            if (enableCombatHitStop) _animationDriver?.TriggerHitStop(0.065f, 0.05f);
            if (enableCameraShake) PlayerFollowCamera.ShakeActive(0.22f, 0.14f);
        }

        ParrySucceeded?.Invoke(attackerNpc);
    }

    /// <summary>
    /// ซิงก์อนิเมชันการ์ดแตก (Guard Break / Stunned) เมื่อ Stamina หมดขณะตั้งการ์ด
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_BroadcastGuardBreak()
    {
        if (!IsDead) _animationDriver?.PlayGuardBreak();
        if (enableCameraShake && IsLocallyControlled)
            PlayerFollowCamera.ShakeActive(0.22f, 0.18f);
        GuardBroken?.Invoke();
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RequestConsumableBuff()
    {
        if (!Object.HasStateAuthority || IsDead) return;
        RPC_BroadcastConsumableBuff();
    }

    /// <summary>
    /// ซิงก์อนิเมชันใช้ยา/บัฟ (Buff) ข้ามเครือข่ายไปยังผู้เล่นทุกคน
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_BroadcastConsumableBuff()
    {
        if (!IsDead) _animationDriver?.PlayBuff();
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RequestDodge(float rollX, float rollY)
    {
        if (!Object.HasStateAuthority || IsDead) return;
        // ใช้ตัวคูณลดค่า Stamina จากแต้ม END สำหรับการกลิ้งหลบ
        if (!TryConsumeAuthorityStamina(dodgeStaminaCost * EndStaminaCostMultiplier)) return;

        if (IsBlocking)
        {
            IsBlocking = false;
            UpdateBlockVfxVisual(false);
        }
        _dodgeInvulnerableUntil = Time.time + dodgeDuration;
        RPC_BroadcastDodge(rollX, rollY);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_BroadcastDodge(float rollX, float rollY)
    {
        _dodgeInvulnerableUntil = Time.time + dodgeDuration;
        if (_hitWindowRoutine != null) { StopCoroutine(_hitWindowRoutine); _hitWindowRoutine = null; }
        CloseHitWindow();
        UpdateBlockVfxVisual(false);

        if (_animationDriver != null)
            _animationDriver.PlayDodge(rollX, rollY);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SetBlocking(NetworkBool blocking)
    {
        SetAuthorityBlockingState(blocking);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_BroadcastBlocking(NetworkBool blocking)
    {
        UpdateBlockVfxVisual(blocking && !IsDead);
        if (_animationDriver != null)
            _animationDriver.SetBlocking(blocking);
    }

    private void OnBlockingChanged()
    {
        UpdateBlockVfxVisual(IsBlocking && !IsDead);
        if (_animationDriver != null)
            _animationDriver.SetBlocking(IsBlocking);
    }

    private void UpdateBlockVfxVisual(bool active)
    {
        if (_blockVfxRoot == null) BuildBlockShieldVfx();
        if (_blockVfxRoot != null && _blockVfxRoot.activeSelf != active)
            _blockVfxRoot.SetActive(active);
    }

    private void OnStaminaChanged()
    {
        if (!_isSpawned) return;
        StaminaChanged?.Invoke(NetworkStamina, MaxStamina);
    }

    // ------------------------------------------------------------------ 3. hit window

    private IEnumerator HitWindowRoutine(PlayerAttackKind kind, int comboStep)
    {
        CloseHitWindow();

        if (useAnimationEvents)
        {
            yield return new WaitForSeconds(hitWindowSafetyTimeout);
        }
        else
        {
            GetAttackTiming(kind, comboStep, out _, out float delay, out float duration);
            if (delay > 0f) yield return new WaitForSeconds(delay);
            OpenHitWindow();
            yield return new WaitForSeconds(Mathf.Min(duration, hitWindowSafetyTimeout));
        }

        CloseHitWindow();
        _hitWindowRoutine = null;
    }

    public void AnimEvent_OpenHitWindow() { if (useAnimationEvents) OpenHitWindow(); }
    public void AnimEvent_CloseHitWindow() { if (useAnimationEvents) CloseHitWindow(); }

    private void OpenHitWindow()
    {
        if (!_isSpawned || IsDead) return;
        IsHitWindowOpen = true;
        SetHitbox(true);
        if (Object.HasStateAuthority) PollActiveWeaponHits();
    }

    private void CloseHitWindow()
    {
        IsHitWindowOpen = false;
        SetHitbox(false);
    }

    private void SetHitbox(bool active)
    {
        if (weaponHitbox != null) weaponHitbox.enabled = active;
        if (hitboxDebugVisual != null) hitboxDebugVisual.enabled = false;
    }

    // ------------------------------------------------------------------ 4. hit resolution

    private void OnTriggerEnter(Collider other)
    {
        HandleTriggerOverlap(other);
    }

    private void OnTriggerStay(Collider other)
    {
        HandleTriggerOverlap(other);
    }

    private void HandleTriggerOverlap(Collider other)
    {
        if (!_isSpawned || !Object.HasStateAuthority) return;
        if (IsDead || other == null) return;

        var npc = other.GetComponentInParent<EnemyNPC>();
        if (npc != null)
        {
            TryHitNpc(npc, other);
            return;
        }

        var attacker = other.GetComponentInParent<PlayerCombat>();
        if (attacker == null || attacker == this || !attacker._isSpawned) return;

        // ป้องกันการฟันทำดาเมจเพื่อนร่วมทีมในโหมด Co-op (หากไม่ได้เปิด allowFriendlyFirePvp ทั้งสองฝั่ง)
        if (!allowFriendlyFirePvp || !attacker.allowFriendlyFirePvp) return;

        if (other != attacker.weaponHitbox || !attacker.IsHitWindowOpen || attacker.IsDead) return;
        if (!WeaponOverlapsBody(attacker)) return;
        if ((attacker.transform.position - transform.position).sqrMagnitude > maxHitDistance * maxHitDistance) return;

        NetworkId attackerId = attacker.Object.Id;
        if (_lastHitSequenceByAttacker.TryGetValue(attackerId, out int lastSeq) && lastSeq == attacker.CurrentSequence) return;
        _lastHitSequenceByAttacker[attackerId] = attacker.CurrentSequence;

        RPC_TakeDamage(attacker.TotalAttackDamage);
    }

    private void PollActiveWeaponHits()
    {
        if (!IsHitWindowOpen || weaponHitbox == null) return;
        Physics.SyncTransforms();

        for (int i = 0; i < EnemyNPC.All.Count; i++)
        {
            EnemyNPC npc = EnemyNPC.All[i];
            if (npc == null || !npc.IsSpawned || !npc.IsAlive || npc.BodyCollider == null) continue;
            TryHitNpc(npc, npc.BodyCollider);
        }
    }

    private bool WeaponOverlapsBody(PlayerCombat attacker)
    {
        if (_bodyCollider == null || attacker.weaponHitbox == null) return false;

        Transform w = attacker.weaponHitbox.transform;
        Transform b = _bodyCollider.transform;
        return Physics.ComputePenetration(
            attacker.weaponHitbox, w.position, w.rotation,
            _bodyCollider, b.position, b.rotation,
            out _, out _);
    }

    // ------------------------------------------------------------------ NPC interaction

    private readonly Dictionary<NetworkId, int> _lastNpcHitSequence = new Dictionary<NetworkId, int>();
    private readonly Dictionary<NetworkId, float> _lastNpcAttackTime = new Dictionary<NetworkId, float>();
    private const float NpcDamageRangeSlack = 3.5f;

    /// <summary>
    /// คำนวณดาเมจและเงื่อนไข Critical Hit แบบเน้นฝีมือ (Skill-Based Critical Rework):
    ///   - ฮิต 1 และ ฮิต 2 (Light / Heavy): ไม่ติดคริติคอลอัตโนมัติ (ตัด ComboStep == 2 Auto-Crit และตัดสุ่ม % ออกทั้งหมด)
    ///   - ติด Critical Hit เฉพาะ 3 เงื่อนไขฝีมือเท่านั้น:
    ///       1) Combo Finisher (ฮิตสุดท้าย ComboStep >= 3): Light 1.55x / Heavy 1.75x
    ///       2) Timed Parry Counter-Slash (ตีศัตรูที่ติด Stagger หรือฟันสวนภายในหน้าต่างหลัง Perfect Parry): 1.75x
    ///       3) Stealth Assassination Backstab (ลอบสังหารมอนสเตอร์ทั่วไปจากด้านหลัง): 2.50x
    /// </summary>
    public int EvaluateSkillBasedStrike(
        int baseAtk,
        PlayerAttackKind attackKind,
        int comboStep,
        bool targetIsBoss,
        bool targetIsStaggered,
        bool parryCounterWindowActive,
        out bool isCrit,
        out bool isParryCounter,
        out float appliedMultiplier)
    {
        bool isStealthStrike = attackKind == PlayerAttackKind.StealthAssassination && !targetIsBoss;
        bool isHeavyStrike = attackKind == PlayerAttackKind.HeavyCombo ||
                             attackKind == PlayerAttackKind.RunHeavyAttack ||
                             attackKind == PlayerAttackKind.JumpHeavyAttack ||
                             attackKind == PlayerAttackKind.RunJumpHeavyAttack;

        // 1) เฉพาะฮิตสุดท้ายของคอมโบ (ComboStep >= 3) เท่านั้นที่เป็น Combo Finisher CRIT
        bool isComboFinisher = comboStep >= 3 &&
                               (attackKind == PlayerAttackKind.LightCombo || attackKind == PlayerAttackKind.HeavyCombo);

        // 2) ฟันสวนกลับใส่ศัตรูที่ติด Stagger หรือภายในหน้าต่างหลังปัดป้องสมบูรณ์ (Timed Parry Counter-Slash)
        isParryCounter = !isStealthStrike && (targetIsStaggered || parryCounterWindowActive);

        // 3) รวมเงื่อนไข Critical Hit (ไม่มีการสุ่ม % และไม่มีการคริใน Combo ฮิต 1–2)
        isCrit = isStealthStrike || isComboFinisher || isParryCounter;

        if (isStealthStrike)
        {
            appliedMultiplier = stealthBackstabCritMultiplier; // 2.50x Backstab CRIT
        }
        else if (isParryCounter)
        {
            // สวนกลับหลัง Perfect Parry การันตี CRIT 1.75x (ไม่ทะลุเพดานแม้กดทับกับ Finisher)
            appliedMultiplier = parryCounterCritMultiplier;
        }
        else if (isComboFinisher)
        {
            // ฮิตปิดคอมโบ (ฮิต 3): Light Finisher = 1.55x CRIT, Heavy Finisher = 1.75x CRIT
            appliedMultiplier = isHeavyStrike ? heavyFinisherCritMultiplier : lightFinisherCritMultiplier;
        }
        else if (isHeavyStrike)
        {
            // ท่าฟันหนักฮิต 1–2 / วิ่งฟันหนัก / กระโดดฟันหนัก: ดาเมจหนักกว่าปกติแต่ไม่ติด CRIT
            appliedMultiplier = comboStep == 2 ? 1.25f : 1.20f;
        }
        else
        {
            // ท่าฟันเบาฮิต 1–2 / วิ่งฟัน / กระโดดฟัน: ดาเมจปกติ ไม่ติด CRIT
            appliedMultiplier = comboStep == 2 ? comboStep2NormalMultiplier : 1.00f;
        }

        return Mathf.Max(1, Mathf.RoundToInt(baseAtk * appliedMultiplier));
    }

    private void TryHitNpc(EnemyNPC npc, Collider other)
    {
        if (!IsHitWindowOpen || weaponHitbox == null) return;
        if (IsInSafeZone || SafeZone.IsPointInAnySafeZone(transform.position)) return;
        if (!npc.IsSpawned || !npc.IsAlive || npc.BodyCollider == null || other != npc.BodyCollider) return;

        Transform w = weaponHitbox.transform;
        Transform b = npc.BodyCollider.transform;
        bool bladeOverlap = Physics.ComputePenetration(weaponHitbox, w.position, w.rotation, npc.BodyCollider, b.position, b.rotation, out _, out _);

        if (!bladeOverlap)
        {
            Vector3 toNpc = npc.transform.position - transform.position;
            toNpc.y = 0f;
            float d = toNpc.magnitude;
            float reach = npc.IsBoss ? 3.25f : 2.65f;
            if (d > reach) return;
            if (d > 0.65f && Vector3.Dot(transform.forward, toNpc / d) < 0.15f) return;
        }

        NetworkId id = npc.Object.Id;
        if (_lastNpcHitSequence.TryGetValue(id, out int seq) && seq == CurrentSequence) return;
        _lastNpcHitSequence[id] = CurrentSequence;

        int totalAtk = TotalAttackDamage;
        bool isStealthStrike = CurrentAttackKind == PlayerAttackKind.StealthAssassination && !npc.IsBoss;
        bool isHeavyStrike = CurrentAttackKind == PlayerAttackKind.HeavyCombo ||
                             CurrentAttackKind == PlayerAttackKind.RunHeavyAttack ||
                             CurrentAttackKind == PlayerAttackKind.JumpHeavyAttack ||
                             CurrentAttackKind == PlayerAttackKind.RunJumpHeavyAttack;

        bool parryWindowReady = Time.time <= _parryCounterReadyUntil;
        int finalDamage = EvaluateSkillBasedStrike(
            totalAtk,
            CurrentAttackKind,
            ComboStep,
            npc.IsBoss,
            npc.IsStaggered,
            parryWindowReady,
            out bool isCrit,
            out bool isParryCounter,
            out float appliedMult);

        // เมื่อใช้โบนัสฟันสวนกลับหลัง Perfect Parry แล้ว ให้ล้างหน้าต่างทันที (1 Perfect Parry = 1 Counter CRIT)
        if (isParryCounter && parryWindowReady)
        {
            _parryCounterReadyUntil = -100f;
        }

        _lastCombatActivityTime = Time.time;

        Debug.Log($"[PlayerCombat] Swing #{CurrentSequence} ({CurrentAttackKind}, Combo {ComboStep}, x{appliedMult:0.00}{(isCrit ? " CRIT!" : "")}{(isParryCounter ? " PARRY-COUNTER!" : "")}) hit NPC {npc.name} (id={id}) for {finalDamage} (TotalATK={totalAtk}).");
        npc.RPC_TakeDamageWithCrit(finalDamage, isCrit, ComboStep);
        HitLanded?.Invoke(npc, finalDamage, isCrit);

        // ตอบสนองความหนักแน่นของการฟันฝั่งเครื่องผู้เล่น (Network-Safe Hit-Stop & Camera Shake)
        if (IsLocallyControlled)
        {
            if (enableCombatHitStop)
            {
                float hitStopDur = isStealthStrike ? 0.08f
                    : (isCrit || isHeavyStrike ? 0.055f : 0.028f);
                _animationDriver?.TriggerHitStop(hitStopDur, 0.06f);
            }
            if (enableCameraShake)
            {
                float shakeAmp = isStealthStrike ? 0.28f
                    : (isCrit ? 0.17f : (isHeavyStrike ? 0.11f : 0.06f));
                PlayerFollowCamera.ShakeActive(shakeAmp, isStealthStrike ? 0.18f : 0.12f);
            }
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_ReceiveNpcDamage(NetworkId npcId, int patternDamageOverride = 0)
    {
        if (!Object.HasStateAuthority || IsDead) return;
        if (IsInSafeZone || SafeZone.IsPointInAnySafeZone(transform.position))
        {
            Debug.Log("[PlayerCombat] NPC damage blocked: player is inside Comfort Zone.");
            return;
        }

        if (Runner == null || !Runner.TryFindObject(npcId, out NetworkObject npcObject) || npcObject == null)
        {
            Debug.LogWarning($"[PlayerCombat] NPC damage rejected: object {npcId} not found.");
            return;
        }

        var npc = npcObject.GetComponent<EnemyNPC>();
        if (npc == null || !npc.IsAlive)
        {
            Debug.LogWarning($"[PlayerCombat] NPC damage rejected: {npcId} is not a living EnemyNPC.");
            return;
        }

        float limit = npc.IsBoss ? 22f : npc.AttackRange + NpcDamageRangeSlack;
        if ((npc.transform.position - transform.position).sqrMagnitude > limit * limit)
        {
            Debug.LogWarning($"[PlayerCombat] NPC damage rejected: {npc.name} out of range.");
            return;
        }

        float now = Time.time;
        if (_lastNpcAttackTime.TryGetValue(npcId, out float last) && now - last < npc.AttackCooldown * 0.4f)
        {
            return;
        }
        _lastNpcAttackTime[npcId] = now;
        _lastCombatActivityTime = now;

        int maxAllowed = npc.IsBoss ? npc.ScaledAttackDamage * 2 : npc.ScaledAttackDamage + 4;
        int incoming = patternDamageOverride > 0
            ? Mathf.Clamp(patternDamageOverride, 1, maxAllowed)
            : npc.CurrentAttackDamage;
        Debug.Log($"[PlayerCombat] {npc.name} hits {name} for {incoming}.");
        ApplyIncomingDamageAuthority(incoming, npc);
    }

    public void HealSelf(int amount)
    {
        if (!_isSpawned || !Object.HasStateAuthority || IsDead || amount <= 0) return;
        int before = NetworkHealth;
        NetworkHealth = Mathf.Min(MaxHealth, NetworkHealth + amount);
        Debug.Log($"[PlayerCombat] Healed {NetworkHealth - before} ({before} -> {NetworkHealth}/{MaxHealth}).");
    }

    // ------------------------------------------------------------------ 5. health, defense, timed parry & block stamina drain

    [Rpc(RpcSources.StateAuthority, RpcTargets.StateAuthority)]
    public void RPC_TakeDamage(int damage)
    {
        ApplyIncomingDamageAuthority(damage, attackerNpc: null);
    }

    /// <summary>
    /// ประมวลผลดาเมจขาเข้าฝั่ง State Authority: ตรวจสอบ i-frame, Perfect Parry, Sustained Block, Defense และสถานะตาย
    /// </summary>
    private void ApplyIncomingDamageAuthority(int damage, EnemyNPC attackerNpc)
    {
        if (!Object.HasStateAuthority) return;
        if (IsDead || IsDodging || Time.time < _reviveInvulnerableUntil) return;
        if (IsInSafeZone || SafeZone.IsPointInAnySafeZone(transform.position)) return;

        _lastCombatActivityTime = Time.time;
        damage = Mathf.Clamp(damage, 0, maxDamagePerHit);

        // 1. ตรวจสอบ Perfect Parry ก่อน (กดตั้งการ์ดทันเวลาภายใน parryWindowSeconds -> รับดาเมจ 0 + คืน Stamina + ทำให้ศัตรู Stagger)
        if (IsBlocking && NetworkStamina > 0f && (Time.time - _blockStartedTime <= parryWindowSeconds))
        {
            if (parryStaminaReward > 0f)
            {
                NetworkStamina = Mathf.Min(MaxStamina, NetworkStamina + parryStaminaReward);
                _sprintExhaustedCooldown = false;
            }
            // รีเซ็ตคูลดาวน์ Parry สั้นลงเมื่อปัดป้องสำเร็จ และเปิดหน้าต่าง Counter-Slash CRIT (3.5 วินาที)
            _nextParryReadyTime = Time.time + 0.15f;
            _parryCounterReadyUntil = Time.time + parryCounterWindowSeconds;

            NetworkId attackerNetId = (attackerNpc != null && attackerNpc.Object != null && attackerNpc.Object.IsValid)
                ? attackerNpc.Object.Id
                : default;
            RPC_BroadcastParrySuccess(attackerNetId);

            if (attackerNpc != null && attackerNpc.IsAlive)
            {
                float distToAttacker = Vector3.Distance(transform.position, attackerNpc.transform.position);
                bool canStagger = !attackerNpc.IsBoss || distToAttacker <= 7.5f;
                if (canStagger)
                {
                    float staggerDur = attackerNpc.IsBoss ? parryBossStaggerDuration : parryEnemyStaggerDuration;
                    attackerNpc.RPC_ApplyParryStagger(staggerDur);
                }
            }

            Debug.Log($"[PlayerCombat] PERFECT PARRY! Blocked {damage} dmg cleanly from {(attackerNpc != null ? attackerNpc.name : "attacker")}.");
            return;
        }

        // 2. หักค่าป้องกันจากอุปกรณ์สวมใส่ (Armor / Shield / Pants)
        if (TotalDefense > 0)
            damage = Mathf.Max(1, damage - TotalDefense);

        // 3. ตั้งการ์ดป้องกันปกติ (Sustained Block: ลดดาเมจ 75% และใช้ Stamina ลดลงตามแต้ม END)
        if (IsBlocking && NetworkStamina > 0f)
        {
            damage = Mathf.Max(1, Mathf.RoundToInt(damage * (1f - blockDamageReduction)));
            TryConsumeAuthorityStamina(blockHitStaminaCost * EndStaminaCostMultiplier);
            if (NetworkStamina <= 0f)
            {
                IsBlocking = false;
                _blockStartedTime = -100f;
                _localBlockingSent = false;
                if (_capsule != null) _capsule.SpeedMultiplier = 1f;
                UpdateBlockVfxVisual(false);
                RPC_BroadcastBlocking(false);
                RPC_BroadcastGuardBreak();
                Debug.Log("[PlayerCombat] Guard broken! Stamina depleted from blocking hit.");
            }
            else
            {
                // เล่นอนิเมชันรับแรงปะทะขณะตั้งการ์ด (Block Hit) บนทุกเครื่อง
                RPC_BroadcastBlockHit(damage);
            }
        }
        if (damage <= 0) return;

        NetworkHealth = Mathf.Max(0, NetworkHealth - damage);

        if (NetworkHealth <= 0 && respawnDelaySeconds > 0f && _respawnRoutine == null)
            _respawnRoutine = StartCoroutine(RespawnRoutine());
    }

    private float _reviveInvulnerableUntil = -100f;
    private const float CoopDownedReviveWindowSeconds = 15f;

    private bool HasLivingAllyInRoom()
    {
        for (int i = 0; i < All.Count; i++)
        {
            var other = All[i];
            if (other != null && other != this && other.IsSpawned && !other.IsDead)
                return true;
        }
        return false;
    }

    private IEnumerator RespawnRoutine()
    {
        bool coopMode = HasLivingAllyInRoom();
        float waitDur = coopMode ? CoopDownedReviveWindowSeconds : respawnDelaySeconds;

        if (IsLocallyControlled && coopMode)
        {
            PlayerStatusHud.Instance?.ShowToast(
                $"DOWNED! Allies can [Hold G] to Revive you ({waitDur:0}s)  |  Press [R] to Respawn now",
                new Color(1f, 0.42f, 0.30f),
                5.0f);
        }

        float elapsed = 0f;
        while (elapsed < waitDur)
        {
            // หากเพื่อนชุบชีวิตสำเร็จระหว่างรอ ให้หยุดการเกิดใหม่ทันที
            if (!_isSpawned || !IsDead)
            {
                _respawnRoutine = null;
                yield break;
            }
            if (IsLocallyControlled && coopMode && elapsed >= 1.5f)
            {
                var kb = Keyboard.current;
                if (kb != null && kb.rKey.wasPressedThisFrame && !GameplayInput.IsBlocked)
                    break;
            }
            elapsed += Time.deltaTime;
            yield return null;
        }

        _respawnRoutine = null;
        if (!_isSpawned || !Object.HasStateAuthority || !IsDead) yield break;

        TeleportToSpawn();
        _lastHitSequenceByAttacker.Clear();
        NetworkStamina = MaxStamina;
        _sprintExhaustedCooldown = false;
        _reviveInvulnerableUntil = Time.time + 2.0f;
        NetworkHealth = MaxHealth;
    }

    private void TeleportToSpawn()
    {
        Vector3 fallbackSafe = SafeZone.Instance != null ? SafeZone.Instance.ReconnectSpawnPosition : _spawnPosition;
        // หากผู้เล่นปลดล็อกหินเทเลพอร์ตใกล้จุดตายไว้แล้ว ให้เกิดที่หินใกล้สุดแทนการเดินไกลจากเมือง
        Vector3 dest = TeleportWaystone.ResolveBestRespawnPosition(transform.position, fallbackSafe);
        TeleportTo(dest);
    }

    /// <summary>
    /// รับคำสั่งชุบชีวิตกลางไฟต์จากเพื่อนร่วมห้อง (กด [G] ค้าง 2.5 วินาที) -> ฟื้นตรงจุดเดิม 50% HP + 100% Stamina + อมตะสั้น 2.5 วิ
    /// ตรวจสอบระยะประชิดและสถานะมีชีวิตของผู้ชุบฝั่ง Authority เพื่อป้องกันการยิง RPC ชุบข้ามแมป
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_ReviveByAlly(NetworkString<_32> rescuerName, RpcInfo info = default)
    {
        if (!_isSpawned || !Object.HasStateAuthority || !IsDead) return;

        // ตรวจสอบว่าผู้ส่งคำสั่งชุบยังมีชีวิตและอยู่ใกล้ตัวจริง (เผื่อ Lag 6.5m)
        if (info.Source != PlayerRef.None)
        {
            PlayerCombat rescuer = PartyManager.FindCombatByRef(info.Source);
            if (rescuer != null)
            {
                if (rescuer.IsDead) return;
                Vector3 diff = rescuer.transform.position - transform.position;
                diff.y = 0f;
                if (diff.magnitude > 6.5f) return;
            }
        }

        if (_respawnRoutine != null)
        {
            StopCoroutine(_respawnRoutine);
            _respawnRoutine = null;
        }

        _lastHitSequenceByAttacker.Clear();
        NetworkStamina = MaxStamina;
        _sprintExhaustedCooldown = false;
        _reviveInvulnerableUntil = Time.time + 2.5f;
        NetworkHealth = Mathf.Max(1, Mathf.RoundToInt(MaxHealth * 0.50f));

        string helper = rescuerName.ToString();
        if (IsLocallyControlled)
        {
            PlayerStatusHud.Instance?.ShowToast(
                $"Revived by {(string.IsNullOrEmpty(helper) ? "Ally" : helper)}! (50% HP restored)",
                new Color(0.32f, 0.98f, 0.72f),
                3.0f);
        }
        Debug.Log($"[PlayerCombat] {name} revived on-spot by '{helper}' at {NetworkHealth}/{MaxHealth} HP.");
    }

    /// <summary>
    /// ส่งสัญญาณระบบปาร์ตี้ (Invite / Accept / Decline / Leave / SyncMember) ข้ามเครื่องผ่าน Photon Fusion Shared Mode
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_PartySignal(int actionType, int partyId, PlayerRef senderRef, NetworkString<_32> senderName)
    {
        if (!_isSpawned || !Object.HasStateAuthority) return;
        PartyManager.EnsureExists().HandleNetworkSignal((PartySignalAction)actionType, partyId, senderRef, senderName.ToString());
    }

    public void TeleportTo(Vector3 targetPosition)
    {
        if (_capsule != null)
        {
            _capsule.TeleportCharacter(targetPosition);
            return;
        }
        if (_rb != null && !_rb.isKinematic)
        {
            _rb.position = targetPosition;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }
        transform.position = targetPosition;
        if (_networkTransform != null) _networkTransform.Teleport(targetPosition, transform.rotation);
    }

    private void OnHealthChanged()
    {
        ApplyHealthState(forceNotify: false);
    }

    private void ApplyHealthState(bool forceNotify)
    {
        if (!_isSpawned) return;

        int health = NetworkHealth;
        bool dead = health <= 0;

        if (!forceNotify && health < _lastHealthValue && !dead)
        {
            _animationDriver?.PlayHitReaction();
            if (enableCameraShake && IsLocallyControlled)
                PlayerFollowCamera.ShakeActive(0.13f, 0.11f);
        }
        _lastHealthValue = health;

        HealthChanged?.Invoke(health, MaxHealth);

        if (!forceNotify && dead == _lastDeadState) return;
        _lastDeadState = dead;

        if (_capsule != null)
        {
            _capsule.MovementLocked = dead;
            if (dead) _capsule.SpeedMultiplier = 1f;
        }

        if (dead)
        {
            if (Object.HasStateAuthority)
            {
                if (IsBlocking) IsBlocking = false;
                if (IsWeaponDrawn) IsWeaponDrawn = false;
            }
            _parryCounterReadyUntil = -100f;
            UpdateBlockVfxVisual(false);
            if (_hitWindowRoutine != null) { StopCoroutine(_hitWindowRoutine); _hitWindowRoutine = null; }
            CloseHitWindow();
            _bufferedUntil = -1f;
        }

        // ในโหมด Co-op คงการมองเห็นตัวละครที่ล้มอยู่กับพื้นไว้เพื่อให้เพื่อนมองเห็นและเดินมากด [G] ค้างชุบชีวิตได้
        if (_capsule != null) _capsule.SetBodyVisible(!dead || HasLivingAllyInRoom());
        _animationDriver?.SetDead(dead);

        DeathStateChanged?.Invoke(dead);
    }

    // ------------------------------------------------------------------ debug

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private void OnGUI()
    {
        if (!showDebugHud || !_isSpawned || !IsLocallyControlled) return;

        float cd = Mathf.Max(0f, _nextLocalAttackTime - Time.time);
        GUI.Box(new Rect(10f, 10f, 310f, 96f), GUIContent.none);
        string lvDisplay = IsMaxLevel
            ? $"Lv.{MaxLevel} (MAX)"
            : $"Lv.{NetworkLevel} (EXP {NetworkExperience}/{ExpToNextLevel})";
        GUI.Label(new Rect(18f, 14f, 296f, 88f),
            $"{lvDisplay}  StatPts: {NetworkUnspentStatPoints}\n" +
            $"HP {NetworkHealth}/{MaxHealth}   STA {NetworkStamina:0}/{MaxStamina:0}   ATK {TotalAttackDamage}   DEF {TotalDefense}\n" +
            $"STR {StatPointsSTR}  VIT {StatPointsVIT}  END {StatPointsEND}  AGI {StatPointsAGI}  |  {(IsInCombat ? "IN COMBAT" : "COMBAT READY")}\n" +
            $"Cooldown {cd:0.00}s   Combo {ComboStep}   Block {(IsBlocking ? "ON" : "off")}");
    }

    private void OnDrawGizmosSelected()
    {
        if (weaponHitbox == null) return;
        Gizmos.color = IsHitWindowOpen ? Color.red : new Color(1f, 0.6f, 0f, 0.6f);
        Gizmos.matrix = weaponHitbox.transform.localToWorldMatrix;
        if (weaponHitbox is SphereCollider s) Gizmos.DrawWireSphere(s.center, s.radius);
        else if (weaponHitbox is BoxCollider bx) Gizmos.DrawWireCube(bx.center, bx.size);
    }
#endif
}

