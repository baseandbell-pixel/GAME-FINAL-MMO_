using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// ตัวเชื่อมระบบเอฟเฟกต์อัตโนมัติ (Zero-Edit Runtime VFX Auto-Integrator)
/// - ทำงานทั้งหมดจากในโฟลเดอร์ Assets/yeen VFX โดยไม่ต้องแก้ไขโค้ดเกมหลักของเพื่อนแม้แต่บรรทัดเดียว
/// - เชื่อมเอฟเฟกต์ครบทั้ง 13 ระบบเข้ากับ PlayerCombat, CombatHud, EnemyNPC/MonsterEnemyNPC, InventoryManager, LootChest และ LocalPersonalLoot:
///   1) ฟันดาบสายลม (SwordWindSlashVFX — ออโต้จาก VfxPlayerAutoBinder)
///   2) วิ่งลมระดับขา (RunWindSlipstream — ซิงก์ 3-Player Co-op)
///   3) กลิ้งหลบลมระดับขา (MMODodgeSmokeController — ซิงก์ 3-Player Co-op)
///   4) บล็อคโล่พลังงานวงกลม 3 มิติ (MMOBlockParryController — ซิงก์ 3-Player Co-op)
///   5) ตีบล็อคแตกโล่กระจาย 4 เลเยอร์ (MMOGuardBreakVFX — ผูก PlayerCombat.GuardBroken ทุกผู้เล่น)
///   6) ยาฮีลหิ้งห้อยเขียว (MMOHealingPotionVFX — กรอง SafeZone Regen)
///   7) ยาสเตมิน่าผีเสื้อฟ้า (MMOStaminaPotionVFX)
///   8) เลเวลอัพเสาแสงทองลอยหาย (MMOLevelUpController)
///   9) ตัวเลขคอมโบไร้กรอบจังหวะหัวใจ (MMOHeartbeatComboUI — ไม่มีแรงก์ C-SSS)
///  10) โจมตีโดนมอนสเตอร์ 4 เลเยอร์ + มอนกระพริบสีแดง-ส้ม 0.09s (MMOWeaponHitImpactVFX)
///  11) คริติคอลฟันสีแดงขนาดใหญ่ + มอนกระพริบแสง (MMOCriticalHitVFX)
///  12) เลือดมอนสเตอร์/บอสตาย (MMOMonsterDeathBloodVFX — ไม่ซ้ำกับ MonsterEnemyNPC + ตัดระยะไกล)
///  13) เปิดหีบสมบัติแสงทอง 4 เลเยอร์ + เส้นแสงไอเทมลอยโค้งตามระดับความหายากแล้วจางหายเมื่อตกพื้น (MMOTreasureChestController)
/// - ปิดปุ่มทดสอบที่อาจชนกับปุ่มเกมจริงทั้งหมด (เช่น 1, 2, B, Y, H, V, L, Space, คลิกซ้าย/ขวา) เพื่อไม่รบกวนการควบคุมของเพื่อน
/// </summary>
[AddComponentMenu("yeen VFX/00. [Auto System] ตัวเชื่อมเอฟเฟกต์อัตโนมัติทั้งหมด (Yeen VFX Auto Integrator)")]
[DisallowMultipleComponent]
public sealed class YeenVFXAutoIntegrator : MonoBehaviour
{
    private static YeenVFXAutoIntegrator _instance;

    // Reflection cache สำหรับซ่อนข้อความ COMBO แบบเก่ากลางจอของ CombatHud (ไม่แก้ไฟล์ CombatHud.cs ของเพื่อน)
    private static readonly FieldInfo CombatHudComboField =
        typeof(CombatHud).GetField("_combo", BindingFlags.Instance | BindingFlags.NonPublic);

    // State ติดตาม Local Player
    private PlayerCombat _boundLocalPlayer;
    private CombatHud _cachedLocalCombatHud;
    private float _localPlayerBoundAtTime = -10f;
    private int _lastHealth = -1;
    private int _lastMaxHealth = -1;
    private float _lastStamina = -1f;
    private float _lastMaxStamina = -1f;
    private int _lastKnownLevel = -1;
    private float _suppressPotionVfxUntil = -10f;

    // State ติดตามตัวละครทั้งหมด มอนสเตอร์ หีบสมบัติ ไอเทมจากหีบ และดาเมจคริติคอล
    private readonly HashSet<PlayerCombat> _configuredPlayers = new HashSet<PlayerCombat>();
    private readonly Dictionary<PlayerCombat, Action> _guardBreakHandlers = new Dictionary<PlayerCombat, Action>();
    private readonly List<PlayerCombat> _playerCleanupBuffer = new List<PlayerCombat>(8);

    private readonly HashSet<EnemyNPC> _trackedAliveEnemies = new HashSet<EnemyNPC>();
    private readonly HashSet<EnemyNPC> _configuredAttackVfxEnemies = new HashSet<EnemyNPC>();
    private readonly HashSet<MonsterEnemyNPC> _trackedGuardingMonsters = new HashSet<MonsterEnemyNPC>();
    private readonly List<EnemyNPC> _enemyCleanupBuffer = new List<EnemyNPC>(32);
    private readonly List<MonsterEnemyNPC> _monsterGuardCleanupBuffer = new List<MonsterEnemyNPC>(16);

    private readonly HashSet<FloatingDamagePopup> _processedPopups = new HashSet<FloatingDamagePopup>();
    private readonly List<FloatingDamagePopup> _popupCleanupBuffer = new List<FloatingDamagePopup>(32);

    private readonly Dictionary<LootChest, ChestTrackState> _trackedChests = new Dictionary<LootChest, ChestTrackState>();
    private readonly List<LootChest> _chestCleanupBuffer = new List<LootChest>(16);

    private readonly HashSet<LocalPersonalLoot> _seenPersonalLoot = new HashSet<LocalPersonalLoot>();
    private readonly List<LocalPersonalLoot> _lootCleanupBuffer = new List<LocalPersonalLoot>(32);
    private readonly List<RecentOpenedChestWindow> _recentOpenedChests = new List<RecentOpenedChestWindow>(4);

    private float _nextSlowScanTime = 0f;

    private sealed class ChestTrackState
    {
        public MMOTreasureChestController vfx;
        public bool wasOpened;
    }

    private sealed class RecentOpenedChestWindow
    {
        public Vector3 mouthPosition;
        public float expiresAt;
        public int launchedCount;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticInstance()
    {
        _instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (_instance != null) return;

        GameObject go = new GameObject("[YeenVFX_AutoIntegrator]");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<YeenVFXAutoIntegrator>();
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
    }

    private void OnDestroy()
    {
        UnbindLocalPlayer();
        UnbindAllSharedGuardBreakHandlers();
        if (_instance == this) _instance = null;
    }

    private void LateUpdate()
    {
        // 1. ตรวจสอบ Local Player และผู้เล่นทั้งหมดในห้อง Co-op (สแกนทุก 0.25 วินาทีเพื่อประหยัด CPU)
        if (Time.unscaledTime >= _nextSlowScanTime)
        {
            _nextSlowScanTime = Time.unscaledTime + 0.25f;
            SyncAllPlayersVFX();
            SyncAllEnemiesAttackVFX();
            SyncLocalPlayerBinding();
            CleanupDestroyedTrackingEntries();
        }

        // 2. ซ่อนโดมบล็อคเดิมของ PlayerCombat เพื่อให้เห็นโล่พลังงานวงกลม 3 มิติของเราชัดเจนเพียงอันเดียว
        HideLegacyBlockDomeRenderers();

        // 3. ตรวจจับเลเวลอัพ (สำรองกรณี NetworkLevel ซิงก์ผ่าน OnChangedRender)
        CheckLocalLevelUp();

        // 4. ตรวจจับคริติคอล (Giant Red Slash) และซ่อนข้อความ COMBO เก่าไม่ให้ซ้อนทับคอมโบไร้กรอบใหม่
        PollCriticalPopups();
        SuppressLegacyCombatHudComboText();

        // 5. ตรวจจับมอนสเตอร์และบอสตาย (รองรับทั้ง EnemyNPC เดิม และ MonsterEnemyNPC Orc ใหม่)
        PollEnemyDeaths();

        // 6. ตรวจจับการเปิดหีบสมบัติ (LootChest -> แสงทอง 4 เลเยอร์ + เส้นแสงไอเทมลอยโค้งตามระดับความหายาก)
        PollLootChests();
        PollRecentChestLootArcs();
    }

    // ================================================================================================
    // 1. ติดตั้งเอฟเฟกต์พื้นฐานให้ผู้เล่นทุกคน (วิ่งระดับขา / โล่วงกลม / กลิ้งระดับขา / ตีบล็อคแตก 4 เลเยอร์)
    // ================================================================================================
    private void SyncAllPlayersVFX()
    {
        var allPlayers = PlayerCombat.All;
        for (int i = 0; i < allPlayers.Count; i++)
        {
            PlayerCombat p = allPlayers[i];
            if (p == null || !p.IsSpawned) continue;

            if (_configuredPlayers.Contains(p)) continue;
            _configuredPlayers.Add(p);

            AttachSharedPlayerVFX(p);
        }
    }

    private void AttachSharedPlayerVFX(PlayerCombat player)
    {
        try
        {
            // A. เอฟเฟกต์วิ่งลมระดับขา (RunWindSlipstream — ซิงก์ Co-op 3 ผู้เล่น)
            if (player.GetComponent<RunWindSlipstream>() == null)
            {
                player.gameObject.AddComponent<RunWindSlipstream>();
            }

            // B. เอฟเฟกต์บล็อคโล่พลังงานวงกลม 3 มิติ (ซิงก์เฉพาะกับ PlayerCombat.IsBlocking เพื่อไม่ขัดกับ SafeZone / Stamina / ชักดาบ)
            MMOBlockParryController blockVfx = player.GetComponent<MMOBlockParryController>();
            if (blockVfx == null)
            {
                blockVfx = player.gameObject.AddComponent<MMOBlockParryController>();
            }
            blockVfx.syncWithPlayerCombat = true;
            blockVfx.allowRightMouseGuard = false;
            blockVfx.showDebugHUD = false;

            // C. เอฟเฟกต์กลิ้งหลบสายลมระดับขา (ซิงก์เฉพาะกับสถานะ Dash/Dodge/Roll จริง ไม่ลั่นตอนกระโดดหรือพิมพ์แชท)
            MMODodgeSmokeController dodgeVfx = player.GetComponent<MMODodgeSmokeController>();
            if (dodgeVfx == null)
            {
                dodgeVfx = player.gameObject.AddComponent<MMODodgeSmokeController>();
            }
            dodgeVfx.syncWithCharacterRollAndDash = true;
            dodgeVfx.autoDetectAnimatorRollState = true;
            dodgeVfx.enableDirectKeyTrigger = false;
            dodgeVfx.showDebugHUD = false;

            // D. เอฟเฟกต์ตีบล็อคแตกโล่กระจาย 4 เลเยอร์ (MMOGuardBreakVFX — ผูก Event PlayerCombat.GuardBroken)
            MMOGuardBreakVFX guardBreakVfx = player.GetComponent<MMOGuardBreakVFX>();
            if (guardBreakVfx == null)
            {
                guardBreakVfx = player.gameObject.AddComponent<MMOGuardBreakVFX>();
            }
            guardBreakVfx.enableTestHotkey = false;
            guardBreakVfx.showDebugHUD = false;
            guardBreakVfx.enableCameraShake = false; // PlayerCombat.RPC_BroadcastGuardBreak สั่น PlayerFollowCamera ให้อยู่แล้ว
            guardBreakVfx.isLocalPlayerOwner = player.IsLocallyControlled;
            if (player.IsLocallyControlled)
            {
                guardBreakVfx.SetAsPrimaryLocalController();
            }

            if (!_guardBreakHandlers.ContainsKey(player))
            {
                PlayerCombat capturedPlayer = player;
                Action handler = () => HandlePlayerGuardBroken(capturedPlayer);
                capturedPlayer.GuardBroken += handler;
                _guardBreakHandlers.Add(capturedPlayer, handler);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[YeenVFXAutoIntegrator] Skipped shared VFX on {player.name}: {ex.Message}");
        }
    }

    private static void HandlePlayerGuardBroken(PlayerCombat player)
    {
        if (player == null || !player.IsSpawned) return;
        if (!CombatHud.IsWithinCombatVisualRange(player.transform.position, false)) return;

        try
        {
            MMOBlockParryController blockVfx = player.GetComponent<MMOBlockParryController>();
            Vector3 shieldPos = (blockVfx != null && blockVfx.guardVfxMount != null)
                ? blockVfx.guardVfxMount.position
                : player.transform.position + Vector3.up * 1.18f + player.transform.forward * 0.90f;

            // ปิดม่านโล่วงกลมทันทีแล้วระเบิดเศษผลึก 3D 4 เลเยอร์
            if (blockVfx != null)
            {
                blockVfx.SetGuardStanceActive(false);
            }

            MMOGuardBreakVFX.SpawnGuardBreak(shieldPos, player.transform.forward, -1f, triggerCameraShake: false);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[YeenVFXAutoIntegrator] Guard break VFX skipped: {ex.Message}");
        }
    }

    private void UnbindAllSharedGuardBreakHandlers()
    {
        foreach (var kv in _guardBreakHandlers)
        {
            if (kv.Key != null && kv.Value != null)
            {
                kv.Key.GuardBroken -= kv.Value;
            }
        }
        _guardBreakHandlers.Clear();
    }

    private void HideLegacyBlockDomeRenderers()
    {
        var allPlayers = PlayerCombat.All;
        for (int i = 0; i < allPlayers.Count; i++)
        {
            PlayerCombat p = allPlayers[i];
            if (p == null || p.BlockVfxObject == null) continue;
            if (!p.BlockVfxObject.activeSelf) continue;

            // ปิดเฉพาะ Renderer ของโดมวงกลมเก่า คง activeSelf ไว้เผื่อระบบเพื่อนเช็ก IsBlockVfxActive
            Renderer[] renderers = p.BlockVfxObject.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                if (renderers[r] != null && renderers[r].enabled)
                {
                    renderers[r].enabled = false;
                }
            }
        }
    }

    // ================================================================================================
    // 2. เชื่อมต่อ Local Player (ยาฮีล / ยาสเตมิน่า / เลเวลอัพ / คอมโบไร้กรอบ / โจมตีโดนมอน + กระพริบแสง / เลือดมอนตาย)
    // ================================================================================================
    private void SyncLocalPlayerBinding()
    {
        PlayerCombat local = PlayerCombat.Local;
        if (local == _boundLocalPlayer) return;

        UnbindLocalPlayer();
        if (local == null || !local.IsSpawned) return;

        _boundLocalPlayer = local;
        _cachedLocalCombatHud = local.GetComponent<CombatHud>();
        _localPlayerBoundAtTime = Time.unscaledTime;
        _lastHealth = local.NetworkHealth;
        _lastMaxHealth = local.MaxHealth;
        _lastStamina = local.CurrentStamina;
        _lastMaxStamina = local.MaxStamina;
        _lastKnownLevel = local.NetworkLevel;

        try
        {
            // ยาฮีลหิ้งห้อยสีเขียว (ปิดปุ่มเทส 1/H เพื่อไม่ให้ชนกับปุ่มชักดาบ [1] และ Hotbar ของเพื่อน)
            MMOHealingPotionVFX healVfx = local.GetComponent<MMOHealingPotionVFX>();
            if (healVfx == null) healVfx = local.gameObject.AddComponent<MMOHealingPotionVFX>();
            healVfx.enableTestHotkeys = false;
            healVfx.showDebugHUD = false;

            // ยาสเตมิน่าผีเสื้อสีฟ้า (ปิดปุ่มเทส 2/V เพื่อไม่ให้ชนกับปุ่ม Hotbar [2] ของเพื่อน)
            MMOStaminaPotionVFX staminaVfx = local.GetComponent<MMOStaminaPotionVFX>();
            if (staminaVfx == null) staminaVfx = local.gameObject.AddComponent<MMOStaminaPotionVFX>();
            staminaVfx.enableTestHotkeys = false;
            staminaVfx.showDebugHUD = false;

            // เลเวลอัพเสาแสงทองลอยขึ้นแล้วจางหาย
            MMOLevelUpController levelUpVfx = local.GetComponent<MMOLevelUpController>();
            if (levelUpVfx == null) levelUpVfx = local.gameObject.AddComponent<MMOLevelUpController>();
            levelUpVfx.enableTestHotkey = false;
            levelUpVfx.allowLeftClick = false;

            // ตัวเลขคอมโบไร้กรอบเต้นจังหวะหัวใจ (ไม่มี C-SSS, นับเฉพาะตอนตีโดนศัตรูจริง ปิดคลิกฟันลม)
            MMOHeartbeatComboUI comboUi = local.GetComponent<MMOHeartbeatComboUI>();
            if (comboUi == null) comboUi = local.gameObject.AddComponent<MMOHeartbeatComboUI>();
            comboUi.countOnAttackInputForTest = false;

            // ตัวจัดการโจมตีโดนมอนสเตอร์ 4 เลเยอร์ + มอนกระพริบแสง 0.09s (ปิดปุ่มเทส Y และ Trigger บนลำตัว)
            MMOWeaponHitImpactVFX weaponHitVfx = local.GetComponent<MMOWeaponHitImpactVFX>();
            if (weaponHitVfx == null) weaponHitVfx = local.gameObject.AddComponent<MMOWeaponHitImpactVFX>();
            weaponHitVfx.enableTriggerDetection = false;
            weaponHitVfx.enableTestHotkey = false;
            weaponHitVfx.showDebugHUD = false;
            weaponHitVfx.autoRegisterCombo = false;

            // ตัวจัดการเลือดมอนสเตอร์ตาย
            MMOMonsterDeathBloodVFX bloodVfx = local.GetComponent<MMOMonsterDeathBloodVFX>();
            if (bloodVfx == null) bloodVfx = local.gameObject.AddComponent<MMOMonsterDeathBloodVFX>();
            bloodVfx.enableTestHotkey = false;
            bloodVfx.showTestHUD = false;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[YeenVFXAutoIntegrator] Local VFX setup skipped: {ex.Message}");
        }

        local.HealthChanged += OnLocalHealthChanged;
        local.StaminaChanged += OnLocalStaminaChanged;
        local.ProgressionChanged += OnLocalProgressionChanged;
        local.HitLanded += OnLocalHitLanded;
        local.BlockImpacted += OnLocalBlockImpacted;
        local.ParrySucceeded += OnLocalParrySucceeded;
    }

    private void UnbindLocalPlayer()
    {
        if (_boundLocalPlayer != null)
        {
            _boundLocalPlayer.HealthChanged -= OnLocalHealthChanged;
            _boundLocalPlayer.StaminaChanged -= OnLocalStaminaChanged;
            _boundLocalPlayer.ProgressionChanged -= OnLocalProgressionChanged;
            _boundLocalPlayer.HitLanded -= OnLocalHitLanded;
            _boundLocalPlayer.BlockImpacted -= OnLocalBlockImpacted;
            _boundLocalPlayer.ParrySucceeded -= OnLocalParrySucceeded;
        }
        _boundLocalPlayer = null;
        _cachedLocalCombatHud = null;
    }

    // ================================================================================================
    // 3. ตรวจจับยาฮีล ยาสเตมิน่า และเลเวลอัพผ่าน C# Events ของ PlayerCombat
    // ================================================================================================
    private void OnLocalHealthChanged(int current, int max)
    {
        // ยาฮีล (HealSelf): เลือดเพิ่มขณะที่ยังไม่ตาย (prev > 0), MaxHP เท่าเดิม, และไม่ใช่การพักฟื้นอัตโนมัติใน SafeZone
        bool isSafeRegen = _boundLocalPlayer != null && _boundLocalPlayer.IsSafeZoneRegenTick;
        if (!isSafeRegen && _lastHealth > 0 && current > _lastHealth && max == _lastMaxHealth && Time.unscaledTime > _suppressPotionVfxUntil)
        {
            try
            {
                MMOHealingPotionVFX.UseHealPotion();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YeenVFXAutoIntegrator] Heal VFX skipped: {ex.Message}");
            }
        }

        _lastHealth = current;
        _lastMaxHealth = max;
    }

    private void OnLocalStaminaChanged(float current, float max)
    {
        // ยาสเตมิน่า (RestoreStamina): สเตมิน่าพุ่งขึ้นทันที >= 8 หน่วยในเฟรมเดียว (แยกจาก Regen ปกติที่ขึ้นทีละ ~0.35/เฟรม)
        if (_lastStamina >= 0f &&
            _lastHealth > 0 &&
            (current - _lastStamina) >= 8.0f &&
            Mathf.Abs(max - _lastMaxStamina) < 0.01f &&
            Time.unscaledTime > _suppressPotionVfxUntil)
        {
            try
            {
                MMOStaminaPotionVFX.UseStaminaPotion();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YeenVFXAutoIntegrator] Stamina VFX skipped: {ex.Message}");
            }
        }

        _lastStamina = current;
        _lastMaxStamina = max;
    }

    private void OnLocalProgressionChanged()
    {
        CheckLocalLevelUp();
    }

    private void CheckLocalLevelUp()
    {
        if (_boundLocalPlayer == null || !_boundLocalPlayer.IsSpawned) return;

        int currentLevel = _boundLocalPlayer.NetworkLevel;
        if (currentLevel <= 0) return;

        // ช่วง 1.2 วินาทีแรกหลังเข้าเกม ให้บันทึกเลเวลที่โหลดจากเซฟไว้ก่อน เพื่อไม่ให้เด้งเอฟเฟกต์ตอนเพิ่งล็อกอิน
        if (Time.unscaledTime - _localPlayerBoundAtTime < 1.2f)
        {
            _lastKnownLevel = currentLevel;
            return;
        }

        if (_lastKnownLevel >= 1 && currentLevel > _lastKnownLevel)
        {
            _lastKnownLevel = currentLevel;
            _suppressPotionVfxUntil = Time.unscaledTime + 0.4f;
            try
            {
                MMOLevelUpController.PlayLevelUpVFX(_boundLocalPlayer.transform);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YeenVFXAutoIntegrator] LevelUp VFX skipped: {ex.Message}");
            }
        }
        else if (_lastKnownLevel < 1)
        {
            _lastKnownLevel = currentLevel;
        }
    }

    // ================================================================================================
    // 4. ตรวจจับโจมตีโดนมอนสเตอร์ (Weapon Hit Impact + Hit-Flash), คริติคอล และคอมโบไร้กรอบ
    // ================================================================================================
    private void PollCriticalPopups()
    {
        var activePopups = FloatingDamagePopup.ActivePopups;
        for (int i = 0; i < activePopups.Count; i++)
        {
            FloatingDamagePopup popup = activePopups[i];
            if (popup == null || _processedPopups.Contains(popup)) continue;
            _processedPopups.Add(popup);

            if (popup.DamageAmount > 0 && popup.IsCritical)
            {
                if (!CombatHud.IsWithinCombatVisualRange(popup.transform.position, false)) continue;

                try
                {
                    float critScale = popup.ComboStep >= 3 ? 1.2f : -1f;
                    MMOCriticalHitVFX.SpawnCriticalHit(popup.transform.position, critScale);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[YeenVFXAutoIntegrator] Critical VFX skipped: {ex.Message}");
                }
            }
        }
    }

    private void OnLocalHitLanded(EnemyNPC enemy, int damage, bool isCrit)
    {
        if (damage <= 0) return;

        try
        {
            MMOHeartbeatComboUI.RegisterHit(1);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[YeenVFXAutoIntegrator] Heartbeat Combo UI skipped: {ex.Message}");
        }

        if (enemy == null) return;

        try
        {
            Vector3 hitPos = enemy.LockPoint != null
                ? enemy.LockPoint.position
                : enemy.transform.position + Vector3.up * 1.2f;

            if (isCrit)
            {
                // คริติคอลเล่น Giant Red Slash จาก PollCriticalPopups + กระพริบแสงบนตัวมอนสเตอร์
                MMOWeaponHitImpactVFX.FlashEnemy(enemy.gameObject);
            }
            else
            {
                // ฟันปกติเล่นเอฟเฟกต์ปะทะ 4 เลเยอร์ + กระพริบแสงสีแดง-ส้ม 0.09s (ไม่นับคอมโบซ้ำ)
                MMOWeaponHitImpactVFX.SpawnOnMonster(hitPos, enemy.gameObject, enemy.IsBoss ? 1.3f : -1f, registerComboHit: false);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[YeenVFXAutoIntegrator] Weapon hit impact VFX skipped: {ex.Message}");
        }
    }

    private void OnLocalBlockImpacted(int reducedDamage)
    {
        if (_boundLocalPlayer == null) return;
        MMOBlockParryController blockVfx = _boundLocalPlayer.GetComponent<MMOBlockParryController>();
        if (blockVfx != null)
        {
            blockVfx.TriggerBlockAbsorb();
        }
    }

    private void OnLocalParrySucceeded(EnemyNPC attacker)
    {
        if (_boundLocalPlayer == null) return;
        MMOBlockParryController blockVfx = _boundLocalPlayer.GetComponent<MMOBlockParryController>();
        if (blockVfx != null)
        {
            blockVfx.TriggerParryDeflect();
        }
    }

    private void SuppressLegacyCombatHudComboText()
    {
        if (_boundLocalPlayer == null || CombatHudComboField == null) return;
        if (_cachedLocalCombatHud == null)
        {
            _cachedLocalCombatHud = _boundLocalPlayer.GetComponent<CombatHud>();
            if (_cachedLocalCombatHud == null) return;
        }

        if ((int)CombatHudComboField.GetValue(_cachedLocalCombatHud) > 0)
        {
            CombatHudComboField.SetValue(_cachedLocalCombatHud, 0);
        }
    }

    // ================================================================================================
    // 5. เชื่อมเอฟเฟกต์มอนสเตอร์โจมตีตามแอนิเมชัน + มอนบล็อคแตก + มอนสเตอร์และบอสตาย (EnemyNPC + MonsterEnemyNPC)
    // ================================================================================================
    private void SyncAllEnemiesAttackVFX()
    {
        var enemies = EnemyNPC.All;
        for (int i = 0; i < enemies.Count; i++)
        {
            EnemyNPC enemy = enemies[i];
            if (enemy == null || !enemy.IsSpawned) continue;

            if (_configuredAttackVfxEnemies.Contains(enemy)) continue;
            _configuredAttackVfxEnemies.Add(enemy);

            try
            {
                Animator anim = enemy.GetComponentInChildren<Animator>(true);
                if (anim == null) anim = enemy.GetComponent<Animator>();

                MMOMonsterAnimAttackVFX attackVfx = enemy.GetComponent<MMOMonsterAnimAttackVFX>();
                if (attackVfx == null)
                {
                    attackVfx = enemy.gameObject.AddComponent<MMOMonsterAnimAttackVFX>();
                }
                attackVfx.ConfigureForLiveMonster(anim, enemy.IsBoss, enemy.IsBoss ? 65f : 32f);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YeenVFXAutoIntegrator] Skipped monster attack VFX on {enemy.name}: {ex.Message}");
            }
        }
    }

    private void PollEnemyDeaths()
    {
        var enemies = EnemyNPC.All;
        for (int i = 0; i < enemies.Count; i++)
        {
            EnemyNPC enemy = enemies[i];
            if (enemy == null || !enemy.IsSpawned) continue;

            // ตรวจจับมอนสเตอร์ที่กำลังยกโล่บล็อคค้าง (MonsterEnemyNPC.IsGuarding) แล้วโดนตีจนเกราะแตก/ติดมึนงง
            if (enemy is MonsterEnemyNPC monster)
            {
                if (monster.IsAlive && monster.IsGuarding)
                {
                    _trackedGuardingMonsters.Add(monster);
                }
                else if (_trackedGuardingMonsters.Contains(monster))
                {
                    _trackedGuardingMonsters.Remove(monster);
                    if (monster.IsAlive && (monster.IsStunnedNet || monster.IsStaggered) &&
                        CombatHud.IsWithinCombatVisualRange(monster.transform.position, monster.IsBoss))
                    {
                        try
                        {
                            Vector3 shieldPos = monster.transform.position + Vector3.up * (monster.IsBoss ? 1.85f : 1.18f) + monster.transform.forward * (monster.IsBoss ? 1.25f : 0.85f);
                            MMOGuardBreakVFX.SpawnGuardBreak(shieldPos, monster.transform.forward, monster.IsBoss ? 1.45f : 1.0f, triggerCameraShake: false);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[YeenVFXAutoIntegrator] Monster guard break VFX skipped: {ex.Message}");
                        }
                    }
                }
            }

            if (enemy.IsAlive)
            {
                _trackedAliveEnemies.Add(enemy);
            }
            else if (_trackedAliveEnemies.Contains(enemy))
            {
                _trackedAliveEnemies.Remove(enemy);
                // MonsterEnemyNPC สร้างบ่อเลือดตามสเกลความสูงเองแล้วใน HandleBaseOnDeath (ไม่สร้างซ้ำซ้อน)
                if (enemy is MonsterEnemyNPC) continue;
                // ตัดระยะเอฟเฟกต์เลือดมอนตายที่อยู่ไกลข้ามแมปเพื่อลดภาระ GPU/Particle
                if (!CombatHud.IsWithinCombatVisualRange(enemy.transform.position, enemy.IsBoss)) continue;
                try
                {
                    MMOMonsterDeathBloodVFX.SpawnDeathBlood(enemy.transform.position, enemy.IsBoss ? 1.85f : -1f);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[YeenVFXAutoIntegrator] Death blood VFX skipped: {ex.Message}");
                }
            }
        }
    }

    // ================================================================================================
    // 6. ตรวจจับการเปิดหีบสมบัติ (แสงทอง 4 เลเยอร์ + เส้นแสงไอเทมลอยโค้งตามระดับความหายากแล้วจางหายเมื่อตกพื้น)
    // ================================================================================================
    private void PollLootChests()
    {
        var chests = LootChest.All;
        for (int i = 0; i < chests.Count; i++)
        {
            LootChest chest = chests[i];
            if (chest == null) continue;

            if (!_trackedChests.TryGetValue(chest, out ChestTrackState state))
            {
                MMOTreasureChestController vfx = chest.GetComponent<MMOTreasureChestController>();
                if (vfx == null)
                {
                    vfx = chest.gameObject.AddComponent<MMOTreasureChestController>();
                }

                // ผูกเฉพาะเอฟเฟกต์แสงทอง 4 เลเยอร์ (ปล่อยให้ LootChest หมุนฝาหีบและลบตัวเองตามระบบเดิมของเพื่อน)
                vfx.BindExternalChest(
                    lidPivot: null,
                    originLocalPosition: new Vector3(0f, 0.56f, 0f),
                    dedicatedPointLight: null,
                    enableStandaloneInput: false,
                    enableDebugHud: false);
                vfx.chestLidPivot = null;

                state = new ChestTrackState
                {
                    vfx = vfx,
                    wasOpened = chest.IsOpened
                };
                _trackedChests.Add(chest, state);
            }

            if (!state.wasOpened && chest.IsOpened)
            {
                state.wasOpened = true;
                _recentOpenedChests.Add(new RecentOpenedChestWindow
                {
                    mouthPosition = chest.transform.position + Vector3.up * 0.65f,
                    expiresAt = Time.unscaledTime + 0.45f,
                    launchedCount = 0
                });

                if (state.vfx != null)
                {
                    try
                    {
                        state.vfx.TriggerOpenChest();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[YeenVFXAutoIntegrator] Chest VFX skipped: {ex.Message}");
                    }
                }
            }
        }
    }

    private void PollRecentChestLootArcs()
    {
        float now = Time.unscaledTime;
        for (int i = _recentOpenedChests.Count - 1; i >= 0; i--)
        {
            if (now > _recentOpenedChests[i].expiresAt)
            {
                _recentOpenedChests.RemoveAt(i);
            }
        }

        var allLoot = LocalPersonalLoot.All;
        for (int i = 0; i < allLoot.Count; i++)
        {
            LocalPersonalLoot loot = allLoot[i];
            if (loot == null || _seenPersonalLoot.Contains(loot)) continue;
            _seenPersonalLoot.Add(loot);

            if (_recentOpenedChests.Count == 0 || loot.Data == null) continue;

            // ค้นหาหีบสมบัติที่เพิ่งเปิดในรัศมี 7.5 เมตร เพื่อปล่อยเส้นแสงโค้งตามระดับไอเทม (ขาวเงิน/ฟ้า/ม่วง/ทอง)
            RecentOpenedChestWindow matchedWindow = null;
            float bestDistSqr = 7.5f * 7.5f;
            Vector3 lootPos = loot.transform.position;

            for (int w = 0; w < _recentOpenedChests.Count; w++)
            {
                Vector3 delta = lootPos - _recentOpenedChests[w].mouthPosition;
                delta.y = 0f;
                float sqr = delta.sqrMagnitude;
                if (sqr <= bestDistSqr)
                {
                    bestDistSqr = sqr;
                    matchedWindow = _recentOpenedChests[w];
                }
            }

            if (matchedWindow != null)
            {
                int idx = matchedWindow.launchedCount++;
                float delay = idx * 0.08f;
                float arcHeight = 1.95f + (idx % 3) * 0.28f;
                var arc = loot.gameObject.AddComponent<YeenChestLootArcRunner>();
                arc.Launch(matchedWindow.mouthPosition, lootPos, loot.Data.tier, delay, 0.72f, arcHeight);
            }
        }
    }

    // ================================================================================================
    // 7. เก็บกวาด Reference ของวัตถุที่ถูกทำลายแล้ว (ป้องกัน Memory Leak)
    // ================================================================================================
    private void CleanupDestroyedTrackingEntries()
    {
        _configuredPlayers.RemoveWhere(p => p == null);

        _playerCleanupBuffer.Clear();
        foreach (var kv in _guardBreakHandlers)
        {
            if (kv.Key == null) _playerCleanupBuffer.Add(kv.Key);
        }
        for (int i = 0; i < _playerCleanupBuffer.Count; i++)
        {
            _guardBreakHandlers.Remove(_playerCleanupBuffer[i]);
        }

        _enemyCleanupBuffer.Clear();
        foreach (EnemyNPC e in _trackedAliveEnemies)
        {
            if (e == null) _enemyCleanupBuffer.Add(e);
        }
        for (int i = 0; i < _enemyCleanupBuffer.Count; i++)
        {
            _trackedAliveEnemies.Remove(_enemyCleanupBuffer[i]);
        }

        _configuredAttackVfxEnemies.RemoveWhere(e => e == null);

        _monsterGuardCleanupBuffer.Clear();
        foreach (MonsterEnemyNPC m in _trackedGuardingMonsters)
        {
            if (m == null) _monsterGuardCleanupBuffer.Add(m);
        }
        for (int i = 0; i < _monsterGuardCleanupBuffer.Count; i++)
        {
            _trackedGuardingMonsters.Remove(_monsterGuardCleanupBuffer[i]);
        }

        _popupCleanupBuffer.Clear();
        foreach (FloatingDamagePopup pop in _processedPopups)
        {
            if (pop == null) _popupCleanupBuffer.Add(pop);
        }
        for (int i = 0; i < _popupCleanupBuffer.Count; i++)
        {
            _processedPopups.Remove(_popupCleanupBuffer[i]);
        }

        _chestCleanupBuffer.Clear();
        foreach (var kv in _trackedChests)
        {
            if (kv.Key == null) _chestCleanupBuffer.Add(kv.Key);
        }
        for (int i = 0; i < _chestCleanupBuffer.Count; i++)
        {
            _trackedChests.Remove(_chestCleanupBuffer[i]);
        }

        _lootCleanupBuffer.Clear();
        foreach (LocalPersonalLoot it in _seenPersonalLoot)
        {
            if (it == null) _lootCleanupBuffer.Add(it);
        }
        for (int i = 0; i < _lootCleanupBuffer.Count; i++)
        {
            _seenPersonalLoot.Remove(_lootCleanupBuffer[i]);
        }
    }
}

/// <summary>
/// คอมโพเนนต์ช่วยยิงไอเทมที่ดรอปจากหีบสมบัติให้ลอยเป็นเส้นโค้งพร้อมเส้นแสงสีตามระดับความหายาก
/// แล้วหยุดปล่อยเส้นแสงให้จางหายไปอย่างสะอาดทันทีที่ไอเทมตกลงพื้น (ไม่แก้ไข LocalPersonalLoot.cs ของเพื่อน)
/// </summary>
internal sealed class YeenChestLootArcRunner : MonoBehaviour
{
    private Vector3 _startPos;
    private Vector3 _endPos;
    private float _delay;
    private float _duration;
    private float _arcHeight;
    private float _elapsed;
    private bool _launched;
    private bool _trailStarted;
    private ItemTier _tier;
    private TrailRenderer _trail;

    public void Launch(Vector3 startPos, Vector3 endPos, ItemTier tier, float delay, float duration, float arcHeight)
    {
        _startPos = startPos;
        _endPos = endPos;
        _tier = tier;
        _delay = Mathf.Max(0f, delay);
        _duration = Mathf.Max(0.25f, duration);
        _arcHeight = Mathf.Max(0.8f, arcHeight);
        _elapsed = 0f;
        _trailStarted = false;
        _launched = true;

        transform.position = _startPos;
    }

    private void Update()
    {
        if (!_launched) return;

        _elapsed += Time.deltaTime;
        if (_elapsed < _delay)
        {
            transform.position = _startPos;
            return;
        }

        if (!_trailStarted)
        {
            _trailStarted = true;
            Transform trailAnchor = transform.Find("Visual");
            if (trailAnchor == null) trailAnchor = transform;
            _trail = MMOTreasureChestController.AttachLootArcTrail(trailAnchor, _tier, 0.36f);
            if (_trail != null)
            {
                _trail.Clear();
                _trail.emitting = true;
            }
        }

        float t = Mathf.Clamp01((_elapsed - _delay) / _duration);
        Vector3 pos = Vector3.Lerp(_startPos, _endPos, t);
        pos.y += 4f * _arcHeight * t * (1f - t);
        transform.position = pos;

        if (t >= 1f)
        {
            transform.position = _endPos;
            _launched = false;

            if (_trail != null)
            {
                _trail.emitting = false;
                Destroy(_trail.gameObject, _trail.time + 0.12f);
                _trail = null;
            }

            Destroy(this);
        }
    }

    private void OnDestroy()
    {
        if (_launched)
        {
            transform.position = _endPos;
        }
        if (_trail != null)
        {
            _trail.emitting = false;
            Destroy(_trail.gameObject, _trail.time + 0.12f);
        }
    }
}
