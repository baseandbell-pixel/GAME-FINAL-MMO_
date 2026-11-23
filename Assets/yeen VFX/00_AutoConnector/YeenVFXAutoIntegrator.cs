using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// ตัวเชื่อมระบบเอฟเฟกต์อัตโนมัติ (Zero-Edit Runtime VFX Auto-Integrator)
/// - ทำงานทั้งหมดจากในโฟลเดอร์ Assets/yeen VFX โดยไม่ต้องแก้ไขโค้ดเกมหลักของเพื่อนแม้แต่บรรทัดเดียว
/// - เชื่อมเอฟเฟกต์ครบทั้ง 11 ระบบเข้ากับ PlayerCombat, CombatHud, EnemyNPC/MonsterEnemyNPC, InventoryManager และ LootChest
/// - ปิดปุ่มทดสอบที่อาจชนกับปุ่มเกมจริง (เช่น 1, 2, Space, คลิกซ้าย, คลิกขวา) เพื่อไม่ให้รบกวนการควบคุมของเพื่อน
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

    // State ติดตามตัวละครทั้งหมด มอนสเตอร์ หีบสมบัติ และดาเมจคริติคอล
    private readonly HashSet<PlayerCombat> _configuredPlayers = new HashSet<PlayerCombat>();
    private readonly HashSet<EnemyNPC> _trackedAliveEnemies = new HashSet<EnemyNPC>();
    private readonly List<EnemyNPC> _enemyCleanupBuffer = new List<EnemyNPC>(32);
    private readonly HashSet<FloatingDamagePopup> _processedPopups = new HashSet<FloatingDamagePopup>();
    private readonly List<FloatingDamagePopup> _popupCleanupBuffer = new List<FloatingDamagePopup>(32);
    private readonly Dictionary<LootChest, ChestTrackState> _trackedChests = new Dictionary<LootChest, ChestTrackState>();
    private readonly List<LootChest> _chestCleanupBuffer = new List<LootChest>(16);

    private float _nextSlowScanTime = 0f;

    private sealed class ChestTrackState
    {
        public MMOTreasureChestController vfx;
        public bool wasOpened;
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
        if (_instance == this) _instance = null;
    }

    private void LateUpdate()
    {
        // 1. ตรวจสอบ Local Player และผู้เล่นทั้งหมดในห้อง Co-op (สแกนทุก 0.25 วินาทีเพื่อประหยัด CPU)
        if (Time.unscaledTime >= _nextSlowScanTime)
        {
            _nextSlowScanTime = Time.unscaledTime + 0.25f;
            SyncAllPlayersVFX();
            SyncLocalPlayerBinding();
            CleanupDestroyedTrackingEntries();
        }

        // 2. ซ่อนโดมบล็อคเดิมของ PlayerCombat เพื่อให้เห็นโล่แสง 3 มิติของเราชัดเจนเพียงอันเดียว
        HideLegacyBlockDomeRenderers();

        // 3. ตรวจจับเลเวลอัพ (สำรองกรณี NetworkLevel ซิงก์ผ่าน OnChangedRender)
        CheckLocalLevelUp();

        // 4. ตรวจจับคริติคอล (Giant Red Slash) และซ่อนข้อความ COMBO เก่าไม่ให้ซ้อนทับกรอบคอมโบใหม่
        PollCriticalPopups();
        SuppressLegacyCombatHudComboText();

        // 5. ตรวจจับมอนสเตอร์และบอสตาย (รองรับทั้ง EnemyNPC เดิม และ MonsterEnemyNPC Orc ใหม่)
        PollEnemyDeaths();

        // 6. ตรวจจับการเปิดหีบสมบัติ (LootChest -> แสงทอง 4 เลเยอร์)
        PollLootChests();
    }

    // ================================================================================================
    // 1. ติดตั้งเอฟเฟกต์พื้นฐานให้ผู้เล่นทุกคน (วิ่ง / บล็อคโล่ 3 มิติ / กลิ้งควันขาว)
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

    private static void AttachSharedPlayerVFX(PlayerCombat player)
    {
        try
        {
            // A. เอฟเฟกต์วิ่งลมผ่านตัว (RunWindSlipstream)
            if (player.GetComponent<RunWindSlipstream>() == null)
            {
                player.gameObject.AddComponent<RunWindSlipstream>();
            }

            // B. เอฟเฟกต์บล็อคโล่แสง 3 มิติ (ซิงก์เฉพาะกับ PlayerCombat.IsBlocking เพื่อไม่ขัดกับ SafeZone / Stamina / ชักดาบ)
            MMOBlockParryController blockVfx = player.GetComponent<MMOBlockParryController>();
            if (blockVfx == null)
            {
                blockVfx = player.gameObject.AddComponent<MMOBlockParryController>();
            }
            blockVfx.syncWithPlayerCombat = true;
            blockVfx.allowRightMouseGuard = false;
            blockVfx.showDebugHUD = false;

            // C. เอฟเฟกต์กลิ้งหลบควันขาว (ซิงก์เฉพาะกับสถานะ Dash/Dodge/Roll จริง ไม่ลั่นตอนกระโดดหรือพิมพ์แชท)
            MMODodgeSmokeController dodgeVfx = player.GetComponent<MMODodgeSmokeController>();
            if (dodgeVfx == null)
            {
                dodgeVfx = player.gameObject.AddComponent<MMODodgeSmokeController>();
            }
            dodgeVfx.syncWithCharacterRollAndDash = true;
            dodgeVfx.autoDetectAnimatorRollState = true;
            dodgeVfx.enableDirectKeyTrigger = false;
            dodgeVfx.showDebugHUD = false;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[YeenVFXAutoIntegrator] Skipped shared VFX on {player.name}: {ex.Message}");
        }
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
    // 2. เชื่อมต่อ Local Player (ยาฮีล / ยาสเตมิน่า / เลเวลอัพ / กรอบคอมโบหัวใจเต้น / เลือดมอนตาย)
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

            // เลเวลอัพเสาแสงทอง
            MMOLevelUpController levelUpVfx = local.GetComponent<MMOLevelUpController>();
            if (levelUpVfx == null) levelUpVfx = local.gameObject.AddComponent<MMOLevelUpController>();
            levelUpVfx.enableTestHotkey = false;
            levelUpVfx.allowLeftClick = false;

            // กรอบคอมโบหัวใจเต้น "ตึก-ตัก" (นับเฉพาะตอนตีโดนศัตรูจริง ปิดคลิกฟันลม)
            MMOHeartbeatComboUI comboUi = local.GetComponent<MMOHeartbeatComboUI>();
            if (comboUi == null) comboUi = local.gameObject.AddComponent<MMOHeartbeatComboUI>();
            comboUi.countOnAttackInputForTest = false;

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
    // 4. ตรวจจับคริติคอล (FloatingDamagePopup) และคอมโบตีโดนศัตรู (CombatHud)
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
    // 5. ตรวจจับมอนสเตอร์และบอสตาย (EnemyNPC + MonsterEnemyNPC Orc)
    // ================================================================================================
    private void PollEnemyDeaths()
    {
        var enemies = EnemyNPC.All;
        for (int i = 0; i < enemies.Count; i++)
        {
            EnemyNPC enemy = enemies[i];
            if (enemy == null || !enemy.IsSpawned) continue;

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
    // 6. ตรวจจับการเปิดหีบสมบัติ (LootChest -> ปล่อยแสงทอง 4 เลเยอร์โดยไม่กวนอนิเมชันฝาหีบเดิม)
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

    // ================================================================================================
    // 7. เก็บกวาด Reference ของวัตถุที่ถูกทำลายแล้ว (ป้องกัน Memory Leak)
    // ================================================================================================
    private void CleanupDestroyedTrackingEntries()
    {
        _configuredPlayers.RemoveWhere(p => p == null);

        _enemyCleanupBuffer.Clear();
        foreach (EnemyNPC e in _trackedAliveEnemies)
        {
            if (e == null) _enemyCleanupBuffer.Add(e);
        }
        for (int i = 0; i < _enemyCleanupBuffer.Count; i++)
        {
            _trackedAliveEnemies.Remove(_enemyCleanupBuffer[i]);
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
    }
}
