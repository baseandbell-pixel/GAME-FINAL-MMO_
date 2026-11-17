using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Player MMORPG Combat Controller:
/// - Hooks into MMOPlayerController's 2-hit Attack Combo (Left Click: Attack1 -> Attack2).
/// - Calculates melee hit detection in a front arc + CapsLock locked Targetable priority.
/// - Critical Hit system (Crit Chance, Crit Damage Multiplier, Camera Shake, Gold Crit Popup & Shockwave VFX).
/// - Special Skill [E]: Heavy Stun Strike (high Crit + applies Stun to non-boss monsters).
/// - Heal [R]: Instant HP recovery potion.
/// - Clean MMO HUD overlay (Target Frame + Player Health/EXP/Crit bar).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(MMOPlayerController))]
public sealed class MMOPlayerCombat : MonoBehaviour
{
    [Header("Player Stats")]
    [Min(1)] public int playerLevel = 5;
    [Min(100)] public int maxHealth = 1200;
    [Min(1)] public int attack1MinDamage = 115;
    [Min(1)] public int attack1MaxDamage = 150;
    [Min(1)] public int attack2MinDamage = 180;
    [Min(1)] public int attack2MaxDamage = 235;

    [Header("Critical Hit Settings (ระบบติดคริ)")]
    [Range(0f, 1f)] public float baseCritChance = 0.38f;
    [Range(0f, 1f)] public float attack2BonusCritChance = 0.20f;
    [Min(1.2f)] public float critDamageMultiplier = 2.25f;

    [Header("Melee Hitbox")]
    [Min(1.5f)] public float attackRange = 3.4f;
    [Range(45f, 180f)] public float attackArcDegrees = 135f;
    [Range(0.05f, 0.6f)] public float attack1ImpactDelay = 0.24f;
    [Range(0.05f, 0.6f)] public float attack2ImpactDelay = 0.28f;

    [Header("Special Stun Skill [E] & Heal [R]")]
    [Min(1f)] public float stunSkillCooldown = 5.0f;
    [Min(0.5f)] public float stunDuration = 2.5f;
    [Min(1f)] public float healPotionCooldown = 6.0f;

    private MMOPlayerController _playerController;
    private MMOCameraController _cameraController;
    private MMOPlayerInventory _inventory;

    private int _baseMaxHealth;
    private int _baseAtk1Min;
    private int _baseAtk1Max;
    private int _baseAtk2Min;
    private int _baseAtk2Max;
    private float _baseCritChance;
    private float _baseCritMult;

    private int _currentHealth;
    private int _currentExp;
    private int _expToNextLevel = 500;
    private float _nextStunSkillTime;
    private float _nextHealTime;
    private bool _isDead;
    private bool _pauseMenuOpen;
    private bool _inventoryOpen;
    private bool _chatOpen;
    private int _chatClosedFrame = -1;
    private float _respawnCountdown;
    private Vector3 _spawnOrigin;
    private MMOMonsterAI _lastHitMonster;
    private MMOPlayerFusion _playerFusion;
    private MMOGameplayHUD _hud;
    private string _lastCombatBanner = "";
    private float _lastCombatBannerUntil;

    // Economy, Equipment, & Quest Loop
    private int _gold;
    private int _questStage;
    private int _questKills;
    private int _bossKills;
    private float _powerBuffUntil;
    private float _hasteBuffUntil;
    private float _lifeStealPct;
    private float _cooldownReductionPct;
    private int _warlordSetPieces;

    private struct ChatEntry
    {
        public string line;
        public float receivedAt;
    }

    private readonly List<ChatEntry> _chatLog = new List<ChatEntry>();
    private const int MaxChatEntries = 7;

    public int CurrentHealth => _currentHealth;
    public int MaxHealth => maxHealth;
    public bool IsDead => _isDead;
    public int Gold => _gold;
    public int Potions => _inventory != null ? _inventory.GetTotalPotionCount() : 0;
    public int BossKills => _bossKills;
    public int WarlordSetPieces => _warlordSetPieces;
    public float LifeStealPct => _lifeStealPct;
    public float CooldownReductionPct => _cooldownReductionPct;
    public float PowerBuffRemaining => Mathf.Max(0f, _powerBuffUntil - Time.time);
    public float HasteBuffRemaining => Mathf.Max(0f, _hasteBuffUntil - Time.time);
    public MMOPlayerInventory Inventory => _inventory;

    private void Awake()
    {
        _playerController = GetComponent<MMOPlayerController>();
        _playerFusion = GetComponent<MMOPlayerFusion>();
        _inventory = GetComponent<MMOPlayerInventory>() ?? gameObject.AddComponent<MMOPlayerInventory>();

        _baseMaxHealth = maxHealth;
        _baseAtk1Min = attack1MinDamage;
        _baseAtk1Max = attack1MaxDamage;
        _baseAtk2Min = attack2MinDamage;
        _baseAtk2Max = attack2MaxDamage;
        _baseCritChance = baseCritChance;
        _baseCritMult = critDamageMultiplier;
        _currentHealth = maxHealth;
    }

    private void OnEnable()
    {
        if (_playerController == null)
        {
            _playerController = GetComponent<MMOPlayerController>();
        }

        if (_playerController != null)
        {
            _playerController.OnAttackStepStarted += OnPlayerAttackStepStarted;
        }

        MMOPlayerFusion.OnChatMessageReceived += HandleChatMessageReceived;
    }

    private void OnDisable()
    {
        if (_playerController != null)
        {
            _playerController.OnAttackStepStarted -= OnPlayerAttackStepStarted;
        }

        MMOPlayerFusion.OnChatMessageReceived -= HandleChatMessageReceived;
    }

    private void HandleChatMessageReceived(string sender, string message)
    {
        _chatLog.Add(new ChatEntry { line = $"[{sender}]: {message}", receivedAt = Time.unscaledTime });
        if (_chatLog.Count > MaxChatEntries)
        {
            _chatLog.RemoveAt(0);
        }
    }

    private void Start()
    {
        _spawnOrigin = transform.position;
        GameObject spawnMarker = GameObject.Find("Spawn");
        if (spawnMarker != null)
        {
            _spawnOrigin = spawnMarker.transform.position;
        }

        if (GoogleAPI.instance != null && GoogleAPI.instance.profileLoaded)
        {
            playerLevel = Mathf.Max(5, GoogleAPI.instance.level);
            _currentExp = Mathf.Clamp(GoogleAPI.instance.exp, 0, _expToNextLevel - 1);
            _gold = Mathf.Max(0, GoogleAPI.instance.gold);
            _questStage = Mathf.Max(0, GoogleAPI.instance.questStage);
            _questKills = Mathf.Max(0, GoogleAPI.instance.questKills);
            _bossKills = Mathf.Max(0, GoogleAPI.instance.bossKills);
        }

        if (_inventory != null)
        {
            _inventory.OnInventoryChanged += RecalculateTotalCombatStats;
            _inventory.Initialize(this, _playerController);
        }
        RecalculateTotalCombatStats();
        _currentHealth = maxHealth;

        _cameraController = MMOCameraController.Instance;
        if (_cameraController == null)
        {
            _cameraController = FindAnyObjectByType<MMOCameraController>();
        }
    }

    private void OnDestroy()
    {
        if (_inventory != null)
        {
            _inventory.OnInventoryChanged -= RecalculateTotalCombatStats;
        }
        if (_hud != null)
        {
            Destroy(_hud.gameObject);
            _hud = null;
        }
    }

    private void Update()
    {
        if (_playerController == null || !_playerController.IsLocalControl)
        {
            return;
        }

        if (_hud == null)
        {
            _hud = MMOGameplayHUD.CreateForPlayer(this, _playerController);
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (_chatOpen)
            {
                CloseChatInput();
                return;
            }
            if (_inventoryOpen)
            {
                ToggleInventoryPanel();
                return;
            }
            TogglePauseMenu();
        }

        if (_pauseMenuOpen)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            if (!_chatOpen && Time.frameCount > _chatClosedFrame)
            {
                OpenChatInput();
                return;
            }
        }

        if (_chatOpen)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.I) || Input.GetKeyDown(KeyCode.B))
        {
            ToggleInventoryPanel();
            return;
        }

        if (_inventoryOpen)
        {
            return;
        }

        if (_isDead)
        {
            _respawnCountdown = Mathf.Max(0f, _respawnCountdown - Time.deltaTime);
            return;
        }

        // Passive out-of-combat health regen
        if (_currentHealth < maxHealth && Time.frameCount % 60 == 0)
        {
            _currentHealth = Mathf.Min(maxHealth, _currentHealth + 8);
        }

        // Refresh dynamic buff speeds each frame
        if (_playerController != null)
        {
            ComputedGearStats gs = _inventory != null ? _inventory.GetTotalEquippedStats(out _) : default;
            float speedBonus = 1f + (gs.moveSpeedPct * 0.01f) + (Time.time < _hasteBuffUntil ? 0.35f : 0f);
            float cdFactor = 1f - Mathf.Clamp((_cooldownReductionPct * 0.01f) + (Time.time < _hasteBuffUntil ? 0.25f : 0f), 0f, 0.55f);
            _playerController.ExternalSpeedMultiplier = speedBonus;
            _playerController.ExternalCooldownMultiplier = cdFactor;
        }

        // Skill [E]: Heavy Critical Stun Strike
        if (Input.GetKeyDown(KeyCode.E) && Time.time >= _nextStunSkillTime && !_playerController.IsAttacking && !_playerController.IsDodging)
        {
            ExecuteStunStrikeSkill();
        }

        // Ground Loot [F]
        if (Input.GetKeyDown(KeyCode.F))
        {
            MMOLootPickup.TryPickupNearest(transform.position);
        }

        // Quick Hotbar [1]..[5] (and [R] triggers Slot 1)
        if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.R))
        {
            _inventory?.UseHotbarSlot(0);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            _inventory?.UseHotbarSlot(1);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            _inventory?.UseHotbarSlot(2);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha4))
        {
            _inventory?.UseHotbarSlot(3);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha5))
        {
            _inventory?.UseHotbarSlot(4);
        }
    }

    private void LateUpdate()
    {
        if (_hud == null || _playerController == null || !_playerController.IsLocalControl)
        {
            return;
        }

        float now = Time.unscaledTime;
        var chatLines = new System.Text.StringBuilder();
        for (int i = 0; i < _chatLog.Count; i++)
        {
            if (_chatOpen || now - _chatLog[i].receivedAt <= 12f)
            {
                if (chatLines.Length > 0) chatLines.Append('\n');
                chatLines.Append(_chatLog[i].line);
            }
        }

        GetQuestSummary(out string questTitle, out string questDetail);
        MMOLootPickup nearestDrop = MMOLootPickup.FindNearestInteractable(transform.position);
        string lootPrompt = nearestDrop != null ? $"[F] Pick Up: {nearestDrop.DisplayPromptText}" : string.Empty;

        _hud.RefreshHUD(
            GetActiveTargetMonster(),
            _isDead,
            _respawnCountdown,
            _lastCombatBanner,
            Time.time < _lastCombatBannerUntil,
            playerLevel,
            _currentHealth,
            maxHealth,
            _currentExp,
            _expToNextLevel,
            GetEffectiveCritChance(false),
            critDamageMultiplier,
            _playerController.DodgeCooldownRemaining,
            Mathf.Max(0f, _nextStunSkillTime - Time.time),
            _gold,
            _bossKills,
            PowerBuffRemaining,
            HasteBuffRemaining,
            questTitle,
            questDetail,
            lootPrompt,
            _inventory,
            _inventoryOpen,
            _chatOpen,
            chatLines.ToString(),
            _pauseMenuOpen);
    }

    private void TogglePauseMenu()
    {
        _pauseMenuOpen = !_pauseMenuOpen;
        if (_pauseMenuOpen) _inventoryOpen = false;
        _playerController?.SetInputSuppressed(_pauseMenuOpen || _chatOpen || _inventoryOpen);
        UpdateCursorStateForOverlays();
    }

    public void ToggleInventoryPanel()
    {
        _inventoryOpen = !_inventoryOpen;
        _playerController?.SetInputSuppressed(_pauseMenuOpen || _chatOpen || _inventoryOpen);
        UpdateCursorStateForOverlays();
    }

    public void ClosePauseMenuFromUI()
    {
        if (!_pauseMenuOpen) return;
        PlayerPrefs.Save();
        _pauseMenuOpen = false;
        _playerController?.SetInputSuppressed(_chatOpen || _inventoryOpen);
        UpdateCursorStateForOverlays();
    }

    public void ReturnToLobbyFromUI()
    {
        PlayerPrefs.Save();
        SaveProgressionToCloud();
        _pauseMenuOpen = false;
        _inventoryOpen = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (LobbyNetwork.Instance != null)
        {
            LobbyNetwork.Instance.ReturnToLobbyScene();
        }
        else
        {
            SceneManager.LoadScene("Lobby", LoadSceneMode.Single);
        }
    }

    private void OpenChatInput()
    {
        _chatOpen = true;
        _inventoryOpen = false;
        _playerController?.SetInputSuppressed(true);
        UpdateCursorStateForOverlays();
        _hud?.OnChatOpened();
    }

    private void CloseChatInput()
    {
        _chatOpen = false;
        _chatClosedFrame = Time.frameCount;
        _playerController?.SetInputSuppressed(_pauseMenuOpen || _inventoryOpen);
        UpdateCursorStateForOverlays();
        _hud?.OnChatClosed();
    }

    public void SubmitChatMessageFromUI(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            if (_playerFusion == null) _playerFusion = GetComponent<MMOPlayerFusion>();
            if (_playerFusion != null)
            {
                _playerFusion.SendChatMessage(text);
            }
            else
            {
                string localName = GoogleAPI.instance != null && !string.IsNullOrEmpty(GoogleAPI.instance.username)
                    ? GoogleAPI.instance.username
                    : "Player";
                HandleChatMessageReceived(localName, text.Trim());
            }
        }

        CloseChatInput();
    }

    private void UpdateCursorStateForOverlays()
    {
        bool anyOverlayOpen = _pauseMenuOpen || _chatOpen || _inventoryOpen;
        if (_cameraController == null) _cameraController = MMOCameraController.Instance;
        if (_cameraController != null)
        {
            _cameraController.SetGameplayEnabled(!anyOverlayOpen);
        }
        else
        {
            Cursor.lockState = anyOverlayOpen ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = anyOverlayOpen;
        }
    }

    private void OnPlayerAttackStepStarted(int attackStep)
    {
        bool isFinisher = attackStep >= 2;
        MMOCombatVFX.SpawnSlashArc(transform.position, transform.forward, attackRange * 0.72f, isFinisher);
        MMOAudioManager.PlaySwordSwing(transform.position, isFinisher);

        float delay = isFinisher ? attack2ImpactDelay : attack1ImpactDelay;
        StartCoroutine(PerformMeleeSwingHitRoutine(attackStep, delay));
    }

    private IEnumerator PerformMeleeSwingHitRoutine(int attackStep, float delay)
    {
        yield return new WaitForSeconds(delay);

        // Spawn a sword swing ring effect in front of the player
        Vector3 swingCenter = transform.position + Vector3.up * 1.1f + transform.forward * 1.15f;
        List<MMOMonsterAI> targets = FindMonstersInMeleeArc(attackRange, attackArcDegrees);

        if (targets.Count == 0)
        {
            yield break;
        }

        bool isHeavyComboFinisher = attackStep >= 2;
        float effectiveCritChance = GetEffectiveCritChance(isHeavyComboFinisher);
        float buffMultiplier = Time.time < _powerBuffUntil ? 1.30f : 1.0f;
        int totalDealtForLifeSteal = 0;

        for (int i = 0; i < targets.Count; i++)
        {
            MMOMonsterAI monster = targets[i];
            if (monster == null || monster.IsDead)
            {
                continue;
            }

            bool isCrit = Random.value <= effectiveCritChance;
            int minDmg = isHeavyComboFinisher ? attack2MinDamage : attack1MinDamage;
            int maxDmg = isHeavyComboFinisher ? attack2MaxDamage : attack1MaxDamage;

            int rawDamage = Mathf.RoundToInt(Random.Range(minDmg, maxDmg + 1) * buffMultiplier);
            int finalDamage = isCrit
                ? Mathf.RoundToInt(rawDamage * critDamageMultiplier)
                : rawDamage;

            Vector3 hitPoint = monster.transform.position + Vector3.up * 1.25f;
            if (!monster.IsEvading)
            {
                monster.RequestDamage(finalDamage, isCrit, transform, hitPoint);
                _lastHitMonster = monster;
                totalDealtForLifeSteal += finalDamage;

                if (isCrit)
                {
                    _lastCombatBanner = $"[CRIT] CRITICAL HIT! ({finalDamage} DMG)";
                    _lastCombatBannerUntil = Time.time + 1.8f;

                    if (_cameraController == null) _cameraController = MMOCameraController.Instance;
                    if (_cameraController != null)
                    {
                        _cameraController.TriggerImpactShake(
                            isHeavyComboFinisher ? 0.20f : 0.14f,
                            isHeavyComboFinisher ? 0.18f : 0.13f);
                    }
                }
            }
        }

        if (totalDealtForLifeSteal > 0 && _lifeStealPct > 0.1f && _currentHealth < maxHealth)
        {
            int leech = Mathf.Max(1, Mathf.RoundToInt(totalDealtForLifeSteal * (_lifeStealPct * 0.01f)));
            _currentHealth = Mathf.Min(maxHealth, _currentHealth + leech);
        }
    }

    private void ExecuteStunStrikeSkill()
    {
        float cdrFactor = 1f - Mathf.Clamp(_cooldownReductionPct * 0.01f, 0f, 0.45f);
        _nextStunSkillTime = Time.time + stunSkillCooldown * cdrFactor;
        _playerController.TriggerSkillAction(4);

        Vector3 burstCenter = transform.position + Vector3.up * 1.0f + transform.forward * 1.5f;
        MMOCombatVFX.SpawnSlashArc(transform.position, transform.forward, attackRange * 0.9f, true, new Color(1f, 0.88f, 0.15f, 1f));
        MMOCombatVFX.SpawnHitEffect(burstCenter, isCritical: true, new Color(1f, 0.88f, 0.15f, 1f));
        MMOAudioManager.PlayStunStrike(burstCenter);

        List<MMOMonsterAI> targets = FindMonstersInMeleeArc(attackRange + 1.2f, 180f);
        for (int i = 0; i < targets.Count; i++)
        {
            MMOMonsterAI monster = targets[i];
            if (monster == null || monster.IsDead)
            {
                continue;
            }

            float buffMult = Time.time < _powerBuffUntil ? 1.30f : 1.0f;
            int critDmg = Mathf.RoundToInt(Random.Range(attack2MinDamage, attack2MaxDamage + 1) * critDamageMultiplier * buffMult);
            Vector3 hitPoint = monster.transform.position + Vector3.up * 1.25f;

            monster.RequestDamage(critDmg, isCritical: true, transform, hitPoint);
            monster.RequestStun(stunDuration);
            _lastHitMonster = monster;
        }

        if (_cameraController == null) _cameraController = MMOCameraController.Instance;
        if (_cameraController != null)
        {
            _cameraController.TriggerImpactShake(0.22f, 0.20f);
        }

        _lastCombatBanner = "[STUN] THUNDER STUN STRIKE! (CRITICAL + STUN)";
        _lastCombatBannerUntil = Time.time + 2.2f;
    }

    public void TakeDamageFromMonster(int damage, bool isCrit, MMOMonsterAI attacker)
    {
        if (_isDead)
        {
            return;
        }

        if (_playerController != null && _playerController.IsInvulnerable)
        {
            MMOCombatVFX.SpawnStatusPopup(
                transform.position + Vector3.up * 2.15f,
                "DODGED!",
                new Color(0.35f, 0.95f, 1f, 1f),
                1.0f);
            MMOAudioManager.PlayDodgeDash(transform.position);
            return;
        }

        int actual = Mathf.Max(1, damage);
        _currentHealth = Mathf.Max(0, _currentHealth - actual);

        Vector3 hitPos = transform.position + Vector3.up * 1.3f;
        MMOCombatVFX.SpawnHitEffect(hitPos, isCrit, new Color(1f, 0.25f, 0.2f, 1f));
        MMOCombatVFX.SpawnDamagePopup(hitPos + Vector3.up * 0.4f, actual, isCrit, isPlayerDamage: true);
        MMOAudioManager.PlayPlayerHurt(hitPos);

        Vector3 attackerPos = attacker != null
            ? attacker.transform.position
            : transform.position - transform.forward;
        _playerController?.ApplyHitReaction(attackerPos, isCrit);

        if (_cameraController == null) _cameraController = MMOCameraController.Instance;
        if (_cameraController != null)
        {
            _cameraController.TriggerImpactShake(isCrit ? 0.24f : 0.14f, isCrit ? 0.18f : 0.10f);
        }

        if (_currentHealth <= 0)
        {
            StartCoroutine(PlayerRespawnRoutine());
        }
    }

    private IEnumerator PlayerRespawnRoutine()
    {
        _isDead = true;
        _respawnCountdown = 2.5f;
        _playerController.SetDead(true);

        MMOAudioManager.PlayDeath(transform.position);
        MMOCombatVFX.SpawnStatusPopup(
            transform.position + Vector3.up * 2.3f,
            "DEFEATED!",
            new Color(1f, 0.25f, 0.25f, 1f),
            1.25f);

        yield return new WaitForSeconds(2.5f);

        CharacterController cc = GetComponent<CharacterController>();
        bool ccWasEnabled = cc != null && cc.enabled;
        if (cc != null) cc.enabled = false;
        transform.position = _spawnOrigin + Vector3.up * 0.1f;
        if (cc != null) cc.enabled = ccWasEnabled;

        _currentHealth = maxHealth;
        _isDead = false;
        _playerController.SetDead(false);

        MMOCombatVFX.SpawnRespawnEffect(transform.position);
        MMOAudioManager.PlayHeal(transform.position);
        MMOCombatVFX.SpawnStatusPopup(
            transform.position + Vector3.up * 2.3f,
            "RESPAWNED (Full HP!)",
            new Color(0.3f, 0.95f, 1f, 1f),
            1.1f);
    }

    public void AwardKillExp(int exp, MMOMonsterAI defeatedMonster)
    {
        GrantExpAmount(exp);

        if (defeatedMonster != null)
        {
            bool isBoss = defeatedMonster.Bridge != null &&
                          defeatedMonster.Bridge.Config != null &&
                          defeatedMonster.Bridge.Config.isBossOrElite;
            Vector3 dropCenter = defeatedMonster.transform.position;

            int goldDrop = isBoss ? Random.Range(220, 320) : Random.Range(55, 95);
            MMOLootPickup.SpawnGoldDrop(dropCenter, goldDrop, this, _inventory);

            int stoneDrop = isBoss ? Random.Range(3, 6) : (Random.value < 0.65f ? 1 : 0);
            if (stoneDrop > 0)
            {
                MMOLootPickup.SpawnItemDrop(
                    dropCenter,
                    MMOItemCatalog.CreateInstance("enhance_stone", ItemRarity.Common, stoneDrop, false),
                    this,
                    _inventory);
            }

            if (isBoss || Random.value < 0.60f)
            {
                string potId = isBoss ? "potion_greater" : (Random.value < 0.25f ? "elixir_berserk" : "potion_minor");
                ItemRarity potRarity = potId == "potion_minor" ? ItemRarity.Common : ItemRarity.Uncommon;
                MMOLootPickup.SpawnItemDrop(
                    dropCenter,
                    MMOItemCatalog.CreateInstance(potId, potRarity, isBoss ? 2 : 1, false),
                    this,
                    _inventory);
            }

            if (isBoss)
            {
                MMOLootPickup.SpawnItemDrop(
                    dropCenter,
                    MMOItemCatalog.CreateInstance("warlord_core", ItemRarity.Epic, Random.Range(1, 3), false),
                    this,
                    _inventory);
            }

            if (isBoss || Random.value < 0.70f)
            {
                MMOItemInstance gearDrop = MMOItemCatalog.RollMonsterGearDrop(isBoss);
                MMOLootPickup.SpawnItemDrop(dropCenter, gearDrop, this, _inventory);
            }

            AdvanceQuestProgress(isBoss);
        }

        SaveProgressionToCloud();
    }

    public void CollectGoldDrop(int amount)
    {
        _gold += Mathf.Max(1, amount);
        MMOCombatVFX.SpawnStatusPopup(
            transform.position + Vector3.up * 2.15f - transform.right * 0.5f,
            $"+{amount} GOLD",
            new Color(1f, 0.85f, 0.2f, 1f),
            1.05f);
        if (Time.time >= _lastCombatBannerUntil)
        {
            ShowBanner($"LOOT: +{amount} GOLD", 1.8f);
        }
        SaveProgressionToCloud();
    }

    public void CollectItemDrop(MMOItemInstance item)
    {
        if (item == null || _inventory == null) return;
        MMOItemTemplate tpl = MMOItemCatalog.Get(item.itemId);
        if (tpl == null) return;

        bool added = _inventory.AddItem(item);
        if (!added)
        {
            ShowBanner("BAG IS FULL! Salvage or sell junk in [I]", 2.4f);
            return;
        }

        Color col = MMOItemCatalog.GetRarityColor(item.rarity);
        string qty = item.stackCount > 1 ? $" x{item.stackCount}" : "";
        MMOCombatVFX.SpawnStatusPopup(
            transform.position + Vector3.up * 2.45f + transform.right * 0.5f,
            $"+[{item.rarity}] {tpl.displayName}{qty}",
            col,
            1.15f);
        ShowBanner($"LOOTED: [{item.rarity}] {tpl.displayName}{qty}", 2.2f);

        if (item.rarity >= ItemRarity.Epic)
        {
            if (_playerFusion == null) _playerFusion = GetComponent<MMOPlayerFusion>();
            string msg = $"obtained [{item.rarity}] {tpl.displayName}!";
            if (_playerFusion != null)
            {
                _playerFusion.SendChatMessage(msg);
            }
        }

        SaveProgressionToCloud();
    }

    public void ApplyConsumableEffect(MMOItemTemplate tpl)
    {
        if (tpl == null) return;

        if (tpl.healPercent > 0f)
        {
            _playerController.TriggerSkillAction(5);
            int healAmount = Mathf.RoundToInt(maxHealth * tpl.healPercent);
            _currentHealth = Mathf.Min(maxHealth, _currentHealth + healAmount);
            MMOCombatVFX.SpawnRespawnEffect(transform.position);
            MMOAudioManager.PlayHeal(transform.position);
            MMOCombatVFX.SpawnStatusPopup(
                transform.position + Vector3.up * 2.2f,
                $"+{healAmount} HP",
                new Color(0.25f, 1f, 0.45f, 1f),
                1.1f);
            return;
        }

        if (tpl.itemId == "elixir_berserk")
        {
            _powerBuffUntil = Time.time + tpl.buffDuration;
            MMOCombatVFX.SpawnRespawnEffect(transform.position);
            MMOAudioManager.PlayLevelUp(transform.position);
            ShowBanner("BERSERK ELIXIR! (+30% ATK / +15% CRIT for 20s)", 2.4f);
        }
        else if (tpl.itemId == "scroll_haste")
        {
            _hasteBuffUntil = Time.time + tpl.buffDuration;
            MMOCombatVFX.SpawnDodgeBurst(transform.position, transform.forward);
            MMOAudioManager.PlayDodgeDash(transform.position);
            ShowBanner("WIND HASTE SCROLL! (+35% SPEED / FAST DODGE for 15s)", 2.4f);
        }
        else if (tpl.itemId == "thunder_bomb")
        {
            Vector3 center = transform.position + transform.forward * 1.4f;
            MMOCombatVFX.SpawnHitEffect(center + Vector3.up * 0.5f, true, new Color(0.4f, 0.85f, 1f, 1f));
            MMOAudioManager.PlayStunStrike(center);
            List<MMOMonsterAI> targets = FindMonstersInMeleeArc(attackRange + 3.5f, 360f);
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] == null || targets[i].IsDead) continue;
                int dmg = Mathf.RoundToInt(attack2MaxDamage * 1.8f);
                targets[i].RequestDamage(dmg, true, transform, targets[i].transform.position + Vector3.up * 1.1f);
                targets[i].RequestStun(3.2f);
            }
            ShowBanner("THUNDER BOMB! (AOE CRIT + 3.2s STUN)", 2.2f);
        }
    }

    public void GrantGoldFromSalvage(int amount)
    {
        _gold += Mathf.Max(0, amount);
        SaveProgressionToCloud();
    }

    public void SpendGold(int amount)
    {
        _gold = Mathf.Max(0, _gold - Mathf.Max(0, amount));
        SaveProgressionToCloud();
    }

    public void ShowBanner(string text, float duration)
    {
        _lastCombatBanner = text;
        _lastCombatBannerUntil = Time.time + duration;
    }

    public bool TryBuyShopItem(string itemId, int cost)
    {
        if (_gold < cost || _inventory == null) return false;
        MMOItemTemplate tpl = MMOItemCatalog.Get(itemId);
        if (tpl == null) return false;

        MMOItemInstance item = MMOItemCatalog.CreateInstance(itemId, itemId == "potion_minor" ? ItemRarity.Common : ItemRarity.Uncommon, 1, false);
        if (!_inventory.AddItem(item))
        {
            ShowBanner("BAG IS FULL!", 1.8f);
            return false;
        }

        _gold -= cost;
        MMOAudioManager.PlayLootPickup(transform.position);
        ShowBanner($"BOUGHT: {tpl.displayName}", 1.8f);
        SaveProgressionToCloud();
        return true;
    }

    public void RecalculateTotalCombatStats()
    {
        int extraLv = Mathf.Max(0, playerLevel - 5);
        int lvHp = extraLv * 150;
        int lvAtk1Min = extraLv * 15;
        int lvAtk1Max = extraLv * 20;
        int lvAtk2Min = extraLv * 25;
        int lvAtk2Max = extraLv * 30;

        ComputedGearStats gear = _inventory != null
            ? _inventory.GetTotalEquippedStats(out _warlordSetPieces)
            : default;

        int prevMax = maxHealth;
        maxHealth = Mathf.Max(100, _baseMaxHealth + lvHp + gear.maxHp);
        if (maxHealth > prevMax)
        {
            _currentHealth += (maxHealth - prevMax);
        }
        _currentHealth = Mathf.Clamp(_currentHealth, 0, maxHealth);

        attack1MinDamage = _baseAtk1Min + lvAtk1Min + gear.attack;
        attack1MaxDamage = _baseAtk1Max + lvAtk1Max + gear.attack;
        attack2MinDamage = _baseAtk2Min + lvAtk2Min + Mathf.RoundToInt(gear.attack * 1.25f);
        attack2MaxDamage = _baseAtk2Max + lvAtk2Max + Mathf.RoundToInt(gear.attack * 1.25f);
        baseCritChance = Mathf.Clamp01(_baseCritChance + gear.critChancePct * 0.01f);
        critDamageMultiplier = _baseCritMult + gear.critDamagePct * 0.01f;
        _lifeStealPct = gear.lifeStealPct;
        _cooldownReductionPct = gear.cooldownReductionPct;
    }

    private float GetEffectiveCritChance(bool isFinisher)
    {
        float buff = Time.time < _powerBuffUntil ? 0.15f : 0f;
        return Mathf.Clamp01(baseCritChance + (isFinisher ? attack2BonusCritChance : 0f) + buff);
    }

    private void GrantExpAmount(int exp)
    {
        _currentExp += Mathf.Max(0, exp);
        bool leveledUp = false;
        while (_currentExp >= _expToNextLevel)
        {
            _currentExp -= _expToNextLevel;
            playerLevel++;
            leveledUp = true;

            MMOCombatVFX.SpawnRespawnEffect(transform.position);
            MMOCombatVFX.SpawnStatusPopup(
                transform.position + Vector3.up * 2.6f,
                $"LEVEL UP! Lv.{playerLevel}",
                new Color(1f, 0.92f, 0.2f, 1f),
                1.35f);
        }

        if (leveledUp)
        {
            RecalculateTotalCombatStats();
            _currentHealth = maxHealth;
            MMOAudioManager.PlayLevelUp(transform.position);
        }
    }

    private void AdvanceQuestProgress(bool isBoss)
    {
        if (isBoss)
        {
            _bossKills++;
        }

        switch (_questStage)
        {
            case 0:
                _questKills++;
                if (_questKills >= 2)
                {
                    CompleteQuest(180, 150, 1, "QUEST COMPLETE: First Blood!");
                    _questStage = 1;
                    _questKills = 0;
                }
                break;

            case 1:
                _questKills++;
                if (_questKills >= 3)
                {
                    CompleteQuest(320, 280, 2, "QUEST COMPLETE: Guardian Threat!");
                    _questStage = 2;
                    _questKills = 0;
                }
                break;

            case 2:
                if (isBoss)
                {
                    CompleteQuest(600, 500, 2, "QUEST COMPLETE: Warlord Slain!");
                    _questStage = 3;
                    _questKills = 0;
                }
                break;

            default:
                if (isBoss)
                {
                    CompleteQuest(450, 380, 1, "BOUNTY COMPLETE: Warlord Hunter!");
                    _questKills = 0;
                }
                break;
        }
    }

    private void CompleteQuest(int rewardExp, int rewardGold, int rewardPotions, string banner)
    {
        GrantExpAmount(rewardExp);
        _gold += rewardGold;
        if (rewardPotions > 0 && _inventory != null)
        {
            _inventory.AddItem(MMOItemCatalog.CreateInstance("potion_minor", ItemRarity.Common, rewardPotions, false));
        }
        ShowBanner($"{banner} (+{rewardGold}G, +{rewardExp} EXP)", 3.2f);
        MMOAudioManager.PlayLevelUp(transform.position);
        MMOCombatVFX.SpawnStatusPopup(
            transform.position + Vector3.up * 2.85f,
            "QUEST COMPLETE!",
            new Color(1f, 0.88f, 0.2f, 1f),
            1.35f);
    }

    private void GetQuestSummary(out string title, out string detail)
    {
        switch (_questStage)
        {
            case 0:
                title = "QUEST 1: First Blood";
                detail = $"Slay Arena Monsters ({_questKills}/2)\nReward: 150G  |  180 EXP  |  +1 Pot";
                break;
            case 1:
                title = "QUEST 2: Guardian Threat";
                detail = $"Defeat 3 Monsters ({_questKills}/3)\nReward: 280G  |  320 EXP  |  +2 Pots";
                break;
            case 2:
                title = "QUEST 3: Slay the Warlord";
                detail = $"Defeat Boss Ancient Warlord ({_questKills}/1)\nReward: 500G  |  600 EXP  |  +2 Pots";
                break;
            default:
                title = "BOUNTY: Warlord Hunter";
                detail = $"Slay Boss Warlord Again (Total: {_bossKills})\nReward: 380G  |  450 EXP  |  +1 Pot";
                break;
        }
    }

    private void SaveProgressionToCloud()
    {
        GoogleAPI.instance?.SaveInventoryAndQuest(
            playerLevel,
            _currentExp,
            _gold,
            Potions,
            0,
            0,
            _questStage,
            _questKills,
            _bossKills);
    }

    private List<MMOMonsterAI> FindMonstersInMeleeArc(float maxDist, float maxArcDeg)
    {
        var result = new List<MMOMonsterAI>();
        IReadOnlyList<MMOMonsterAI> allMonsters = MMOMonsterAI.All;

        // Priority 1: If camera has a locked Targetable monster within range, always include it
        if (_cameraController == null) _cameraController = MMOCameraController.Instance;
        if (_cameraController != null && _cameraController.LockedTarget != null)
        {
            MMOMonsterAI lockedMonster = _cameraController.LockedTarget.GetComponentInParent<MMOMonsterAI>();
            if (lockedMonster != null && !lockedMonster.IsDead)
            {
                float d = Vector3.Distance(transform.position, lockedMonster.transform.position);
                if (d <= maxDist + 0.75f && HasMeleeLineOfSight(lockedMonster))
                {
                    result.Add(lockedMonster);
                }
            }
        }

        // Priority 2: All monsters within melee distance & forward cone
        float halfAngle = maxArcDeg * 0.5f;
        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude > 0.001f) forward.Normalize();

        for (int i = 0; i < allMonsters.Count; i++)
        {
            MMOMonsterAI m = allMonsters[i];
            if (m == null || m.IsDead || result.Contains(m))
            {
                continue;
            }

            Vector3 toMonster = m.transform.position - transform.position;
            toMonster.y = 0f;
            float dist = toMonster.magnitude;

            if (dist > maxDist || !HasMeleeLineOfSight(m))
            {
                continue;
            }

            if (dist <= 1.35f)
            {
                result.Add(m);
                continue;
            }

            float angle = Vector3.Angle(forward, toMonster.normalized);
            if (angle <= halfAngle)
            {
                result.Add(m);
            }
        }

        return result;
    }

    private bool HasMeleeLineOfSight(MMOMonsterAI monster)
    {
        if (monster == null)
        {
            return false;
        }

        Vector3 origin = transform.position + Vector3.up * 1.1f;
        Vector3 destination = monster.transform.position + Vector3.up * 1.1f;
        Vector3 diff = destination - origin;
        float dist = diff.magnitude;
        if (dist <= 0.25f)
        {
            return true;
        }

        RaycastHit[] hits = Physics.RaycastAll(origin, diff / dist, dist, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
        {
            Transform hitTransform = hits[i].transform;
            if (hitTransform == null) continue;
            if (hitTransform == transform || hitTransform.IsChildOf(transform)) continue;
            if (hitTransform == monster.transform || hitTransform.IsChildOf(monster.transform)) continue;
            if (hitTransform.GetComponentInParent<MMOMonsterAI>() != null) continue;
            if (hitTransform.GetComponentInParent<MMOPlayerController>() != null) continue;
            return false;
        }

        return true;
    }

    private MMOMonsterAI GetActiveTargetMonster()
    {
        if (_cameraController == null) _cameraController = MMOCameraController.Instance;
        if (_cameraController != null && _cameraController.LockedTarget != null)
        {
            MMOMonsterAI locked = _cameraController.LockedTarget.GetComponentInParent<MMOMonsterAI>();
            if (locked != null)
            {
                return locked;
            }
        }

        if (_lastHitMonster != null)
        {
            return _lastHitMonster;
        }

        // Otherwise show nearest aggroed monster
        IReadOnlyList<MMOMonsterAI> all = MMOMonsterAI.All;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i] != null && (all[i].CurrentState == MMOMonsterAI.AIState.AggroChase ||
                                   all[i].CurrentState == MMOMonsterAI.AIState.Attacking))
            {
                return all[i];
            }
        }

        return null;
    }
}
