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

    private int _currentHealth;
    private int _currentExp;
    private int _expToNextLevel = 500;
    private float _nextStunSkillTime;
    private float _nextHealTime;
    private bool _isDead;
    private bool _pauseMenuOpen;
    private float _respawnCountdown;
    private Vector3 _spawnOrigin;
    private MMOMonsterAI _lastHitMonster;
    private string _lastCombatBanner = "";
    private float _lastCombatBannerUntil;

    private Texture2D _whiteTex;

    public int CurrentHealth => _currentHealth;
    public int MaxHealth => maxHealth;
    public bool IsDead => _isDead;

    private void Awake()
    {
        _playerController = GetComponent<MMOPlayerController>();
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
    }

    private void OnDisable()
    {
        if (_playerController != null)
        {
            _playerController.OnAttackStepStarted -= OnPlayerAttackStepStarted;
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
            int savedLv = Mathf.Max(5, GoogleAPI.instance.level);
            int extraLevels = savedLv - playerLevel;
            if (extraLevels > 0)
            {
                playerLevel = savedLv;
                maxHealth += extraLevels * 150;
                attack1MinDamage += extraLevels * 15;
                attack1MaxDamage += extraLevels * 20;
                attack2MinDamage += extraLevels * 25;
                attack2MaxDamage += extraLevels * 30;
                _currentHealth = maxHealth;
            }
            _currentExp = Mathf.Clamp(GoogleAPI.instance.exp, 0, _expToNextLevel - 1);
        }

        _cameraController = MMOCameraController.Instance;
        if (_cameraController == null)
        {
            _cameraController = FindAnyObjectByType<MMOCameraController>();
        }
    }

    private void Update()
    {
        if (_playerController == null || !_playerController.IsLocalControl)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            TogglePauseMenu();
        }

        if (_pauseMenuOpen)
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

        // Skill [E]: Heavy Critical Stun Strike
        if (Input.GetKeyDown(KeyCode.E) && Time.time >= _nextStunSkillTime && !_playerController.IsAttacking)
        {
            ExecuteStunStrikeSkill();
        }

        // Potion [R]: Self Heal
        if (Input.GetKeyDown(KeyCode.R) && Time.time >= _nextHealTime && _currentHealth < maxHealth)
        {
            ExecuteHealPotion();
        }
    }

    private void TogglePauseMenu()
    {
        _pauseMenuOpen = !_pauseMenuOpen;
        if (_cameraController == null) _cameraController = MMOCameraController.Instance;
        if (_cameraController != null)
        {
            _cameraController.SetGameplayEnabled(!_pauseMenuOpen);
        }
        else
        {
            Cursor.lockState = _pauseMenuOpen ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = _pauseMenuOpen;
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
        float effectiveCritChance = Mathf.Clamp01(
            baseCritChance + (isHeavyComboFinisher ? attack2BonusCritChance : 0f));

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

            int rawDamage = Random.Range(minDmg, maxDmg + 1);
            int finalDamage = isCrit
                ? Mathf.RoundToInt(rawDamage * critDamageMultiplier)
                : rawDamage;

            Vector3 hitPoint = monster.transform.position + Vector3.up * 1.25f;
            if (!monster.IsEvading)
            {
                monster.RequestDamage(finalDamage, isCrit, transform, hitPoint);
                _lastHitMonster = monster;

                if (isCrit)
                {
                    _lastCombatBanner = $"⚡ CRITICAL HIT! ({finalDamage} DMG)";
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
    }

    private void ExecuteStunStrikeSkill()
    {
        _nextStunSkillTime = Time.time + stunSkillCooldown;
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

            int critDmg = Mathf.RoundToInt(Random.Range(attack2MinDamage, attack2MaxDamage + 1) * critDamageMultiplier);
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

        _lastCombatBanner = "⚡ THUNDER STUN STRIKE! (CRITICAL + STUN)";
        _lastCombatBannerUntil = Time.time + 2.2f;
    }

    private void ExecuteHealPotion()
    {
        _nextHealTime = Time.time + healPotionCooldown;
        _playerController.TriggerSkillAction(5);
        int healAmount = Mathf.RoundToInt(maxHealth * 0.45f);
        _currentHealth = Mathf.Min(maxHealth, _currentHealth + healAmount);

        MMOCombatVFX.SpawnRespawnEffect(transform.position);
        MMOAudioManager.PlayHeal(transform.position);
        MMOCombatVFX.SpawnStatusPopup(
            transform.position + Vector3.up * 2.2f,
            $"+{healAmount} HP",
            new Color(0.25f, 1f, 0.45f, 1f),
            1.1f);
    }

    public void TakeDamageFromMonster(int damage, bool isCrit, MMOMonsterAI attacker)
    {
        if (_isDead)
        {
            return;
        }

        int actual = Mathf.Max(1, damage);
        _currentHealth = Mathf.Max(0, _currentHealth - actual);

        Vector3 hitPos = transform.position + Vector3.up * 1.3f;
        MMOCombatVFX.SpawnHitEffect(hitPos, isCrit, new Color(1f, 0.25f, 0.2f, 1f));
        MMOCombatVFX.SpawnDamagePopup(hitPos + Vector3.up * 0.4f, actual, isCrit, isPlayerDamage: true);
        MMOAudioManager.PlayHitImpact(hitPos, isCrit);

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
        _currentExp += exp;
        bool leveledUp = false;
        while (_currentExp >= _expToNextLevel)
        {
            _currentExp -= _expToNextLevel;
            playerLevel++;
            maxHealth += 150;
            _currentHealth = maxHealth;
            attack1MinDamage += 15;
            attack1MaxDamage += 20;
            attack2MinDamage += 25;
            attack2MaxDamage += 30;
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
            MMOAudioManager.PlayLevelUp(transform.position);
        }

        GoogleAPI.instance?.SaveProgression(playerLevel, _currentExp);
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
                if (d <= maxDist + 0.75f)
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

            if (dist > maxDist)
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

    // =========================================================
    // MMO HUD OVERLAY (Target Frame & Player Bar)
    // =========================================================

    private void OnGUI()
    {
        if (_playerController == null || !_playerController.IsLocalControl)
        {
            return;
        }

        if (_whiteTex == null)
        {
            _whiteTex = new Texture2D(1, 1);
            _whiteTex.SetPixel(0, 0, Color.white);
            _whiteTex.Apply();
        }

        DrawTargetMonsterFrame();
        DrawPlayerCombatHUD();
        if (_pauseMenuOpen)
        {
            DrawPauseMenu();
        }
    }

    private void DrawTargetMonsterFrame()
    {
        MMOMonsterAI target = GetActiveTargetMonster();
        if (target == null)
        {
            return;
        }

        float width = 420f;
        float height = 68f;
        float x = (Screen.width - width) * 0.5f;
        float y = 18f;

        // Frame background
        DrawRect(new Rect(x - 3f, y - 3f, width + 6f, height + 6f), new Color(0.05f, 0.05f, 0.08f, 0.88f));

        bool isBoss = target.Bridge != null && target.Bridge.Config != null && target.Bridge.Config.isBossOrElite;
        string bossTag = isBoss ? "[BOSS - SUPER ARMOR] " : "";
        string stateText = target.CurrentState switch
        {
            MMOMonsterAI.AIState.AggroChase => "⚔ AGGRO (Chasing)",
            MMOMonsterAI.AIState.Attacking => "⚔ COMBAT (Attacking)",
            MMOMonsterAI.AIState.EvadeReturn => "↩ EVADING (Out of Territory)",
            MMOMonsterAI.AIState.Dead => "☠ DEFEATED (Respawning...)",
            _ => "🛡 TERRITORY PATROL"
        };

        GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            normal = { textColor = isBoss ? new Color(1f, 0.85f, 0.25f) : Color.white }
        };

        GUIStyle stateStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleRight,
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.65f, 0.3f) }
        };

        GUI.Label(new Rect(x + 10f, y + 6f, width - 20f, 22f), $"{bossTag}Lv.{target.level} {target.displayName}", titleStyle);
        GUI.Label(new Rect(x + 10f, y + 6f, width - 20f, 22f), stateText, stateStyle);

        // HP Bar
        Rect barBg = new Rect(x + 10f, y + 34f, width - 20f, 22f);
        DrawRect(barBg, new Color(0.18f, 0.06f, 0.06f, 1f));

        float fillWidth = (width - 20f) * target.HealthRatio;
        DrawRect(new Rect(barBg.x, barBg.y, fillWidth, barBg.height), new Color(0.88f, 0.18f, 0.18f, 1f));

        GUIStyle hpTextStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };
        GUI.Label(barBg, $"{target.CurrentHealth} / {target.MaxHealth} HP ({Mathf.RoundToInt(target.HealthRatio * 100f)}%)", hpTextStyle);
    }

    private void DrawPlayerCombatHUD()
    {
        float width = 520f;
        float height = 100f;
        float x = (Screen.width - width) * 0.5f;
        float y = Screen.height - height - 16f;

        if (_isDead)
        {
            GUIStyle deadStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.3f, 0.3f) }
            };
            GUI.Label(new Rect(x, Screen.height * 0.36f, width, 40f), $"DEFEATED — Respawning in {_respawnCountdown:0.0}s...", deadStyle);
        }

        // Critical Hit Banner above player HUD
        if (Time.time < _lastCombatBannerUntil && !string.IsNullOrEmpty(_lastCombatBanner))
        {
            GUIStyle critStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.85f, 0.15f) }
            };
            GUI.Label(new Rect(x, y - 34f, width, 28f), _lastCombatBanner, critStyle);
        }

        DrawRect(new Rect(x - 3f, y - 3f, width + 6f, height + 6f), new Color(0.05f, 0.06f, 0.09f, 0.88f));

        // Player HP Bar
        float hpRatio = maxHealth > 0 ? Mathf.Clamp01((float)_currentHealth / maxHealth) : 1f;
        Rect hpBg = new Rect(x + 12f, y + 10f, width - 24f, 22f);
        DrawRect(hpBg, new Color(0.08f, 0.2f, 0.1f, 1f));
        DrawRect(new Rect(hpBg.x, hpBg.y, hpBg.width * hpRatio, hpBg.height), new Color(0.18f, 0.78f, 0.32f, 1f));

        GUIStyle barLabel = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };
        GUI.Label(hpBg, $"PLAYER Lv.{playerLevel}   |   HP: {_currentHealth}/{maxHealth}   |   CRIT: {Mathf.RoundToInt(baseCritChance * 100f)}% (x{critDamageMultiplier:0.0})", barLabel);

        // Player EXP Bar
        float expRatio = _expToNextLevel > 0 ? Mathf.Clamp01((float)_currentExp / _expToNextLevel) : 0f;
        Rect expBg = new Rect(x + 12f, y + 36f, width - 24f, 10f);
        DrawRect(expBg, new Color(0.12f, 0.12f, 0.22f, 1f));
        DrawRect(new Rect(expBg.x, expBg.y, expBg.width * expRatio, expBg.height), new Color(0.42f, 0.58f, 1f, 1f));

        // Cooldowns & Controls line
        float stunCd = Mathf.Max(0f, _nextStunSkillTime - Time.time);
        float healCd = Mathf.Max(0f, _nextHealTime - Time.time);
        string stunStr = stunCd > 0.05f ? $"[E] Stun Strike ({stunCd:0.0}s)" : "[E] Stun Strike (READY)";
        string healStr = healCd > 0.05f ? $"[R] Heal ({healCd:0.0}s)" : "[R] Heal (READY)";

        GUIStyle hintStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 12,
            normal = { textColor = new Color(0.82f, 0.88f, 0.96f) }
        };

        GUI.Label(new Rect(x + 10f, y + 51f, width - 20f, 20f), $"[Left Click] 2-Hit Combo   |   {stunStr}   |   {healStr}", hintStyle);
        GUI.Label(new Rect(x + 10f, y + 71f, width - 20f, 20f), "[WASD+Shift] Move/Run | [CapsLock] Lock | [V] Cam | [Alt] Cursor | [ESC] Menu", hintStyle);
    }

    private void DrawPauseMenu()
    {
        DrawRect(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.55f));

        float w = 340f;
        float h = 270f;
        float x = (Screen.width - w) * 0.5f;
        float y = (Screen.height - h) * 0.5f;

        DrawRect(new Rect(x - 3f, y - 3f, w + 6f, h + 6f), new Color(0.25f, 0.55f, 0.95f, 0.85f));
        DrawRect(new Rect(x, y, w, h), new Color(0.08f, 0.10f, 0.14f, 0.96f));

        GUIStyle headerStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };
        GUI.Label(new Rect(x, y + 12f, w, 28f), "GAME MENU", headerStyle);

        if (_cameraController == null) _cameraController = MMOCameraController.Instance;
        if (_cameraController != null)
        {
            GUI.Label(new Rect(x + 20f, y + 48f, w - 40f, 20f), $"Mouse Sensitivity: {_cameraController.MouseSensitivity:0.0}");
            _cameraController.MouseSensitivity = GUI.HorizontalSlider(
                new Rect(x + 20f, y + 70f, w - 40f, 18f),
                _cameraController.MouseSensitivity,
                0.5f,
                6.0f);
        }

        GUI.Label(new Rect(x + 20f, y + 96f, w - 40f, 20f), $"SFX Volume: {Mathf.RoundToInt(MMOAudioManager.MasterSfxVolume * 100f)}%");
        MMOAudioManager.MasterSfxVolume = GUI.HorizontalSlider(
            new Rect(x + 20f, y + 118f, w - 40f, 18f),
            MMOAudioManager.MasterSfxVolume,
            0f,
            1f);

        string camPresetName = _cameraController != null ? _cameraController.CurrentPreset.ToString() : "StandardMMO";
        if (GUI.Button(new Rect(x + 20f, y + 146f, w - 40f, 30f), $"Camera Mode: {camPresetName} (Cycle)"))
        {
            _cameraController?.CyclePreset();
        }

        if (GUI.Button(new Rect(x + 20f, y + 184f, w - 40f, 32f), "Resume Game (ESC)"))
        {
            TogglePauseMenu();
        }

        if (GUI.Button(new Rect(x + 20f, y + 222f, w - 40f, 32f), "Return to Lobby"))
        {
            _pauseMenuOpen = false;
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

    private void DrawRect(Rect rect, Color color)
    {
        Color prev = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, _whiteTex);
        GUI.color = prev;
    }
}
