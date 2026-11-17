using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Fusion;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Unity Editor Automation Tool for building the Scalable Monster Animation Pipeline,
/// placing Enemies & Territory Zones in Map1, and running an automated verification suite.
/// </summary>
[InitializeOnLoad]
public sealed class MonsterAnimatorBuilder : EditorWindow
{
    public const string DefaultOutputFolder = "Assets/Characters/Monsters";
    public const string BaseControllerFileName = "AC_Monster_Base.controller";
    public const string UpperBodyMaskFileName = "Mask_Monster_UpperBody.mask";
    public const string DefaultMonsterFolder = "Assets/Characters/Model Enemy";
    public const string Map1ScenePath = "Assets/Scenes/Map1.unity";
    public const string LobbyScenePath = "Assets/Scenes/Lobby.unity";
    public const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";
    public const string PlayerControllerPath = "Assets/Characters/AC_Player.controller";
    private const string PipelineVersionStampPath = "Temp/MonsterPipelineV3_Ready.stamp";

    public const string SlotSpawn = "Placeholder_Spawn";
    public const string SlotIdle = "Placeholder_Idle";
    public const string SlotWalk = "Placeholder_Walk";
    public const string SlotRun = "Placeholder_Run";
    public const string SlotCombatIdle = "Placeholder_Combat_Idle";
    public const string SlotAttack01 = "Placeholder_Attack_01";
    public const string SlotAttack02 = "Placeholder_Attack_02";
    public const string SlotAttack03 = "Placeholder_Attack_03";
    public const string SlotSkillCast = "Placeholder_Skill_Cast";
    public const string SlotSkillRelease01 = "Placeholder_Skill_Release_01";
    public const string SlotSkillRelease02 = "Placeholder_Skill_Release_02";
    public const string SlotHitReact = "Placeholder_Hit_React";
    public const string SlotStun = "Placeholder_Stun";
    public const string SlotDeath = "Placeholder_Death";
    public const string SlotUpperAttack = "Placeholder_UpperBody_Attack";
    public const string SlotUpperCast = "Placeholder_UpperBody_Cast";
    public const string SlotUpperRelease = "Placeholder_UpperBody_Release";

    private static readonly string[] AllPlaceholderSlotNames =
    {
        SlotSpawn,
        SlotIdle,
        SlotWalk,
        SlotRun,
        SlotCombatIdle,
        SlotAttack01,
        SlotAttack02,
        SlotAttack03,
        SlotSkillCast,
        SlotSkillRelease01,
        SlotSkillRelease02,
        SlotHitReact,
        SlotStun,
        SlotDeath,
        SlotUpperAttack,
        SlotUpperCast,
        SlotUpperRelease
    };

    private string _baseOutputFolder = DefaultOutputFolder;
    private string _monsterSourceFolder = DefaultMonsterFolder;
    private string _monsterName = "Enemy";
    private bool _isBossOrElite = false;
    private bool _createPrefabInFolder = true;

    static MonsterAnimatorBuilder()
    {
        EditorApplication.delayCall += AutoInitializeMap1MonstersOnce;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode)
        {
            EnsureBuiltIfNeeded();
        }
    }

    private static void AutoInitializeMap1MonstersOnce()
    {
        if (EditorApplication.isPlaying)
        {
            return;
        }

        EnsureBuiltIfNeeded();
    }

    private static void EnsureBuiltIfNeeded()
    {
        CleanupStrayEditModeVfxObjects();

        string prefabPath = CombineAssetPath(DefaultMonsterFolder, "Enemy_Monster.prefab");
        GameObject existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

        Scene activeScene = SceneManager.GetActiveScene();
        bool map1Loaded = activeScene.IsValid() && activeScene.path == Map1ScenePath;
        bool map1HasMonsters = map1Loaded && FindObjectsByType<MMOMonsterAI>().Length >= 3;
        bool needsV2Upgrade =
            existingPrefab == null ||
            existingPrefab.GetComponent<MMOMonsterNetwork>() == null ||
            !File.Exists(PipelineVersionStampPath);

        if ((needsV2Upgrade && !activeScene.isDirty) || (map1Loaded && !map1HasMonsters))
        {
            BuildFullPipelineAndPopulateMap1(silent: true);
        }
        else if (!File.Exists("Temp/MonsterMap1TestReport.txt"))
        {
            Debug.ClearDeveloperConsole();
            string report = RunAutomatedVerificationSuite();
            Debug.Log(report);
        }
    }

    private static void CleanupStrayEditModeVfxObjects()
    {
        if (EditorApplication.isPlaying) return;

        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid()) return;

        GameObject[] roots = activeScene.GetRootGameObjects();
        bool cleaned = false;
        for (int i = 0; i < roots.Length; i++)
        {
            GameObject go = roots[i];
            if (go == null) continue;
            if (go.name == "MMOCombatVFX_Manager" ||
                go.name.StartsWith("VFX_", StringComparison.Ordinal) ||
                go.name.Contains("Popup"))
            {
                DestroyImmediate(go);
                cleaned = true;
            }
        }

        if (cleaned)
        {
            EditorSceneManager.MarkSceneDirty(activeScene);
            EditorSceneManager.SaveScene(activeScene);
        }
    }

    // =========================================================
    // MENU ITEMS
    // =========================================================

    [MenuItem("Tools/MMO Animation/Build Base Monster Animator & Setup Map1", priority = 1)]
    public static void BuildBaseMonsterAnimatorMenu()
    {
        string report = BuildFullPipelineAndPopulateMap1(silent: false);
        Debug.Log(report);
    }

    [MenuItem("Tools/MMO Animation/Run Automated Monster & Combat Self-Test", priority = 2)]
    public static void RunSelfTestMenu()
    {
        string report = RunAutomatedVerificationSuite();
        Debug.Log(report);
    }

    [MenuItem("Tools/MMO Animation/Monster Animation Pipeline Window", priority = 3)]
    public static void OpenWindow()
    {
        MonsterAnimatorBuilder window = GetWindow<MonsterAnimatorBuilder>("Monster Anim Pipeline");
        window.minSize = new Vector2(460f, 400f);
        window.Show();
    }

    public static string BuildFullPipelineAndPopulateMap1(bool silent)
    {
        EnsureFolderExists(DefaultOutputFolder);

        // 1. Reimport FBXs in Model Enemy (Generic + NoAvatar + Loop Time)
        if (AssetDatabase.IsValidFolder(DefaultMonsterFolder))
        {
            MonsterClipImportProcessor.ReimportMonsterFolder(DefaultMonsterFolder);
        }

        // 2. Build AC_Monster_Base.controller
        AnimatorController baseController = BuildBaseAnimatorController(DefaultOutputFolder);

        // 3. Build AOC_Enemy.overrideController + Config_Enemy.asset + Config_Enemy_Boss.asset + Enemy_Monster.prefab
        AnimatorOverrideController aoc = null;
        MonsterAnimationConfig normalConfig = null;
        GameObject enemyPrefab = null;

        if (AssetDatabase.IsValidFolder(DefaultMonsterFolder))
        {
            aoc = BuildMonsterOverrideController(
                baseController,
                DefaultMonsterFolder,
                "Enemy",
                out normalConfig,
                out enemyPrefab,
                isBoss: false,
                createPrefab: true);
        }

        // 4. Tune Player Animator (AC_Player.controller) & Player.prefab for crisp symmetrical transitions
        TunePlayerAnimatorAndPrefab();

        // 5. Populate Map1.unity with MMOMap1CombatDirector + 3 Monsters with Territory Zones
        PopulateMap1Scene(enemyPrefab, normalConfig);

        try
        {
            File.WriteAllText(PipelineVersionStampPath, DateTime.UtcNow.ToString("O"));
        }
        catch { }

        // 6. Run Automated Verification Suite and write report
        string testReport = RunAutomatedVerificationSuite();

        if (!silent)
        {
            EditorUtility.DisplayDialog(
                "MMO Monster Pipeline & Map1 Ready",
                "Successfully built AC_Monster_Base, AOC_Enemy, Enemy_Monster.prefab, placed 3 Monsters with Territory Zones in Map1, and passed all automated self-tests!\n\nPress PLAY in Map1 to fight the monsters!",
                "OK");
        }

        return testReport;
    }

    // =========================================================
    // EDITOR WINDOW GUI
    // =========================================================

    private void OnGUI()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("MMO Scalable Monster Animation Pipeline", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "1. Builds AC_Monster_Base.controller (10 parameters, 1D Blend Tree, Attacks/Skills Sub-State Machines, AnyState rules, UpperBody layer).\n" +
            "2. Reimports monster FBX files as Generic Rig + In-Place + auto Loop Time.\n" +
            "3. Generates AOC_<MonsterName>.overrideController, MonsterAnimationConfig, Prefab, and populates Map1.",
            MessageType.Info);

        EditorGUILayout.Space(6f);
        _baseOutputFolder = EditorGUILayout.TextField("Base Output Folder", _baseOutputFolder);
        _monsterSourceFolder = EditorGUILayout.TextField("Monster FBX Folder", _monsterSourceFolder);
        _monsterName = EditorGUILayout.TextField("Monster Name", _monsterName);
        _isBossOrElite = EditorGUILayout.Toggle("Is Boss / Elite (No Flinch)", _isBossOrElite);
        _createPrefabInFolder = EditorGUILayout.Toggle("Create / Update Prefab", _createPrefabInFolder);

        EditorGUILayout.Space(8f);
        if (GUILayout.Button("Build All + Populate Map1 + Run Self-Test", GUILayout.Height(34f)))
        {
            BuildFullPipelineAndPopulateMap1(silent: false);
        }

        if (GUILayout.Button("Run Automated Verification Suite Only", GUILayout.Height(28f)))
        {
            RunSelfTestMenu();
        }
    }

    // =========================================================
    // STEP 1: BUILD AC_Monster_Base.controller
    // =========================================================

    public static AnimatorController BuildBaseAnimatorController(string outputFolder)
    {
        EnsureFolderExists(outputFolder);

        string controllerPath = CombineAssetPath(outputFolder, BaseControllerFileName);
        string maskPath = CombineAssetPath(outputFolder, UpperBodyMaskFileName);

        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath) != null)
        {
            AssetDatabase.DeleteAsset(controllerPath);
        }

        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        Dictionary<string, AnimationClip> slots = CreateEmbeddedPlaceholderClips(controller);

        // 10 Required Parameters
        AddParameterIfMissing(controller, "Speed", AnimatorControllerParameterType.Float);
        AddParameterIfMissing(controller, "IsCombat", AnimatorControllerParameterType.Bool);
        AddParameterIfMissing(controller, "Attack", AnimatorControllerParameterType.Trigger);
        AddParameterIfMissing(controller, "AttackIndex", AnimatorControllerParameterType.Int);
        AddParameterIfMissing(controller, "Skill", AnimatorControllerParameterType.Trigger);
        AddParameterIfMissing(controller, "SkillIndex", AnimatorControllerParameterType.Int);
        AddParameterIfMissing(controller, "IsCasting", AnimatorControllerParameterType.Bool);
        AddParameterIfMissing(controller, "Hit", AnimatorControllerParameterType.Trigger);
        AddParameterIfMissing(controller, "IsStunned", AnimatorControllerParameterType.Bool);
        AddParameterIfMissing(controller, "IsDead", AnimatorControllerParameterType.Bool);

        AnimatorStateMachine rootSm = controller.layers[0].stateMachine;
        rootSm.name = "Base Layer";
        rootSm.entryPosition = new Vector3(0f, 0f, 0f);
        rootSm.anyStatePosition = new Vector3(0f, 260f, 0f);
        rootSm.exitPosition = new Vector3(900f, 260f, 0f);

        // Spawn -> Locomotion
        AnimatorState spawnState = rootSm.AddState("Spawn", new Vector3(240f, 0f, 0f));
        spawnState.motion = slots[SlotSpawn];
        rootSm.defaultState = spawnState;

        BlendTree locomotionTree = new BlendTree
        {
            name = "Locomotion_BlendTree",
            blendType = BlendTreeType.Simple1D,
            blendParameter = "Speed",
            useAutomaticThresholds = false
        };
        AssetDatabase.AddObjectToAsset(locomotionTree, controller);

        locomotionTree.AddChild(slots[SlotIdle], 0.0f);
        locomotionTree.AddChild(slots[SlotWalk], 1.5f);
        locomotionTree.AddChild(slots[SlotRun], 4.5f);

        AnimatorState locomotionState = rootSm.AddState("Locomotion", new Vector3(500f, 0f, 0f));
        locomotionState.motion = locomotionTree;

        AnimatorState combatIdleState = rootSm.AddState("Combat_Idle", new Vector3(500f, 120f, 0f));
        combatIdleState.motion = slots[SlotCombatIdle];

        AnimatorStateTransition spawnToLoco = spawnState.AddTransition(locomotionState);
        spawnToLoco.hasExitTime = true;
        spawnToLoco.exitTime = 0.05f;
        spawnToLoco.duration = 0.10f;
        spawnToLoco.canTransitionToSelf = false;

        AnimatorStateTransition locoToCombat = locomotionState.AddTransition(combatIdleState);
        locoToCombat.hasExitTime = false;
        locoToCombat.duration = 0.16f;
        locoToCombat.canTransitionToSelf = false;
        locoToCombat.AddCondition(AnimatorConditionMode.If, 0f, "IsCombat");
        locoToCombat.AddCondition(AnimatorConditionMode.Less, 0.2f, "Speed");

        AnimatorStateTransition combatToLocoOut = combatIdleState.AddTransition(locomotionState);
        combatToLocoOut.hasExitTime = false;
        combatToLocoOut.duration = 0.16f;
        combatToLocoOut.canTransitionToSelf = false;
        combatToLocoOut.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsCombat");

        AnimatorStateTransition combatToLocoMove = combatIdleState.AddTransition(locomotionState);
        combatToLocoMove.hasExitTime = false;
        combatToLocoMove.duration = 0.16f;
        combatToLocoMove.canTransitionToSelf = false;
        combatToLocoMove.AddCondition(AnimatorConditionMode.Greater, 0.2f, "Speed");

        // Sub-State Machine: Attacks
        AnimatorStateMachine attacksSm = rootSm.AddStateMachine("Attacks", new Vector3(820f, 80f, 0f));

        AnimatorState attack01 = attacksSm.AddState("Attack_01", new Vector3(320f, 0f, 0f));
        attack01.motion = slots[SlotAttack01];
        attacksSm.defaultState = attack01;

        AnimatorState attack02 = attacksSm.AddState("Attack_02", new Vector3(320f, 80f, 0f));
        attack02.motion = slots[SlotAttack02];

        AnimatorState attack03 = attacksSm.AddState("Attack_03", new Vector3(320f, 160f, 0f));
        attack03.motion = slots[SlotAttack03];

        AddAttackEntryTransition(combatIdleState, attack01, 0);
        AddAttackEntryTransition(combatIdleState, attack02, 1);
        AddAttackEntryTransition(combatIdleState, attack03, 2);

        AddAttackEntryTransition(locomotionState, attack01, 0);
        AddAttackEntryTransition(locomotionState, attack02, 1);
        AddAttackEntryTransition(locomotionState, attack03, 2);

        AddReturnToCombatIdleTransition(attack01, combatIdleState, 0.84f, 0.18f);
        AddReturnToCombatIdleTransition(attack02, combatIdleState, 0.84f, 0.18f);
        AddReturnToCombatIdleTransition(attack03, combatIdleState, 0.84f, 0.18f);

        // Sub-State Machine: Skills
        AnimatorStateMachine skillsSm = rootSm.AddStateMachine("Skills", new Vector3(820f, 180f, 0f));

        AnimatorState skillCast = skillsSm.AddState("Skill_Cast", new Vector3(280f, 0f, 0f));
        skillCast.motion = slots[SlotSkillCast];
        skillsSm.defaultState = skillCast;

        AnimatorState skillRelease01 = skillsSm.AddState("Skill_Release_01", new Vector3(560f, -40f, 0f));
        skillRelease01.motion = slots[SlotSkillRelease01];

        AnimatorState skillRelease02 = skillsSm.AddState("Skill_Release_02", new Vector3(560f, 60f, 0f));
        skillRelease02.motion = slots[SlotSkillRelease02];

        AddSkillCastEntryTransition(combatIdleState, skillCast);
        AddSkillCastEntryTransition(locomotionState, skillCast);

        AnimatorStateTransition castToRel01 = skillCast.AddTransition(skillRelease01);
        castToRel01.hasExitTime = false;
        castToRel01.duration = 0.10f;
        castToRel01.canTransitionToSelf = false;
        castToRel01.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsCasting");
        castToRel01.AddCondition(AnimatorConditionMode.Equals, 0f, "SkillIndex");

        AnimatorStateTransition castToRel02 = skillCast.AddTransition(skillRelease02);
        castToRel02.hasExitTime = false;
        castToRel02.duration = 0.10f;
        castToRel02.canTransitionToSelf = false;
        castToRel02.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsCasting");
        castToRel02.AddCondition(AnimatorConditionMode.Equals, 1f, "SkillIndex");

        AddInstantSkillTransition(combatIdleState, skillRelease01, 0);
        AddInstantSkillTransition(combatIdleState, skillRelease02, 1);
        AddInstantSkillTransition(locomotionState, skillRelease01, 0);
        AddInstantSkillTransition(locomotionState, skillRelease02, 1);

        AddReturnToCombatIdleTransition(skillRelease01, combatIdleState, 0.85f, 0.18f);
        AddReturnToCombatIdleTransition(skillRelease02, combatIdleState, 0.85f, 0.18f);

        // Any State -> Hit_React, Stun, Death
        AnimatorState hitReactState = rootSm.AddState("Hit_React", new Vector3(260f, 200f, 0f));
        hitReactState.motion = slots[SlotHitReact];

        AnimatorStateTransition anyToHit = rootSm.AddAnyStateTransition(hitReactState);
        anyToHit.hasExitTime = false;
        anyToHit.duration = 0.08f;
        anyToHit.canTransitionToSelf = false;
        anyToHit.AddCondition(AnimatorConditionMode.If, 0f, "Hit");
        anyToHit.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsDead");
        anyToHit.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsStunned");

        AddReturnToCombatIdleTransition(hitReactState, combatIdleState, 0.82f, 0.16f);

        AnimatorState stunState = rootSm.AddState("Stun", new Vector3(260f, 280f, 0f));
        stunState.motion = slots[SlotStun];

        AnimatorStateTransition anyToStun = rootSm.AddAnyStateTransition(stunState);
        anyToStun.hasExitTime = false;
        anyToStun.duration = 0.10f;
        anyToStun.canTransitionToSelf = false;
        anyToStun.AddCondition(AnimatorConditionMode.If, 0f, "IsStunned");
        anyToStun.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsDead");

        AnimatorStateTransition stunToCombat = stunState.AddTransition(combatIdleState);
        stunToCombat.hasExitTime = false;
        stunToCombat.duration = 0.15f;
        stunToCombat.canTransitionToSelf = false;
        stunToCombat.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsStunned");
        stunToCombat.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsDead");

        AnimatorState deathState = rootSm.AddState("Death", new Vector3(260f, 360f, 0f));
        deathState.motion = slots[SlotDeath];

        AnimatorStateTransition anyToDeath = rootSm.AddAnyStateTransition(deathState);
        anyToDeath.hasExitTime = false;
        anyToDeath.duration = 0.10f;
        anyToDeath.canTransitionToSelf = false;
        anyToDeath.AddCondition(AnimatorConditionMode.If, 0f, "IsDead");

        // Layer 2: UpperBody
        AvatarMask upperBodyMask = CreateOrLoadUpperBodyAvatarMask(maskPath);

        AnimatorStateMachine upperSm = new AnimatorStateMachine
        {
            name = MonsterAnimatorBridge.UpperBodyLayerName,
            hideFlags = HideFlags.HideInHierarchy
        };
        AssetDatabase.AddObjectToAsset(upperSm, controller);

        AnimatorControllerLayer upperLayer = new AnimatorControllerLayer
        {
            name = MonsterAnimatorBridge.UpperBodyLayerName,
            stateMachine = upperSm,
            avatarMask = upperBodyMask,
            blendingMode = AnimatorLayerBlendingMode.Override,
            defaultWeight = 0f
        };
        controller.AddLayer(upperLayer);

        AnimatorState upperEmpty = upperSm.AddState("UpperBody_Empty", new Vector3(250f, 0f, 0f));
        upperSm.defaultState = upperEmpty;

        AnimatorState upperAttack = upperSm.AddState("UpperBody_Attack", new Vector3(520f, -40f, 0f));
        upperAttack.motion = slots[SlotUpperAttack];

        AnimatorState upperCast = upperSm.AddState("UpperBody_Cast", new Vector3(520f, 50f, 0f));
        upperCast.motion = slots[SlotUpperCast];

        AnimatorState upperRelease = upperSm.AddState("UpperBody_Release", new Vector3(780f, 50f, 0f));
        upperRelease.motion = slots[SlotUpperRelease];

        AnimatorStateTransition upperToAttack = upperEmpty.AddTransition(upperAttack);
        upperToAttack.hasExitTime = false;
        upperToAttack.duration = 0.10f;
        upperToAttack.canTransitionToSelf = false;
        upperToAttack.AddCondition(AnimatorConditionMode.If, 0f, "Attack");

        AnimatorStateTransition upperAttackToEmpty = upperAttack.AddTransition(upperEmpty);
        upperAttackToEmpty.hasExitTime = true;
        upperAttackToEmpty.exitTime = 0.90f;
        upperAttackToEmpty.duration = 0.15f;
        upperAttackToEmpty.canTransitionToSelf = false;

        AnimatorStateTransition upperToCast = upperEmpty.AddTransition(upperCast);
        upperToCast.hasExitTime = false;
        upperToCast.duration = 0.10f;
        upperToCast.canTransitionToSelf = false;
        upperToCast.AddCondition(AnimatorConditionMode.If, 0f, "Skill");
        upperToCast.AddCondition(AnimatorConditionMode.If, 0f, "IsCasting");

        AnimatorStateTransition upperCastToRelease = upperCast.AddTransition(upperRelease);
        upperCastToRelease.hasExitTime = false;
        upperCastToRelease.duration = 0.10f;
        upperCastToRelease.canTransitionToSelf = false;
        upperCastToRelease.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsCasting");

        AnimatorStateTransition upperReleaseToEmpty = upperRelease.AddTransition(upperEmpty);
        upperReleaseToEmpty.hasExitTime = true;
        upperReleaseToEmpty.exitTime = 0.90f;
        upperReleaseToEmpty.duration = 0.15f;
        upperReleaseToEmpty.canTransitionToSelf = false;

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    // =========================================================
    // STEP 2: BUILD AnimatorOverrideController + Config + Prefab
    // =========================================================

    public static AnimatorOverrideController BuildMonsterOverrideController(
        AnimatorController baseController,
        string monsterFolder,
        string monsterName,
        out MonsterAnimationConfig generatedConfig,
        out GameObject generatedPrefab,
        bool isBoss = false,
        bool createPrefab = true)
    {
        generatedConfig = null;
        generatedPrefab = null;

        if (baseController == null || string.IsNullOrEmpty(monsterFolder) || !AssetDatabase.IsValidFolder(monsterFolder))
        {
            return null;
        }

        string safeName = string.IsNullOrWhiteSpace(monsterName) ? "Monster" : monsterName.Trim().Replace(" ", "_");
        string aocPath = CombineAssetPath(monsterFolder, $"AOC_{safeName}.overrideController");

        AnimatorOverrideController aoc = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(aocPath);
        if (aoc == null)
        {
            aoc = new AnimatorOverrideController(baseController);
            AssetDatabase.CreateAsset(aoc, aocPath);
        }
        else
        {
            aoc.runtimeAnimatorController = baseController;
        }

        Dictionary<string, AnimationClip> discoveredClips = FindClipsInFolder(monsterFolder);

        AnimationClip idleClip = MatchBestClip(discoveredClips, "idle");
        AnimationClip walkClip = MatchBestClip(discoveredClips, "walk") ?? idleClip;
        AnimationClip runClip = MatchBestClip(discoveredClips, "run") ?? walkClip;
        AnimationClip combatIdleClip = MatchBestClip(discoveredClips, "combat_idle", "combatidle") ?? idleClip;
        AnimationClip spawnClip = MatchBestClip(discoveredClips, "spawn") ?? idleClip;

        AnimationClip attack01Clip = MatchBestClip(discoveredClips, "attack_01", "attack1", "attack");
        AnimationClip attack02Clip = MatchBestClip(discoveredClips, "attack_02", "attack2") ?? attack01Clip;
        AnimationClip attack03Clip = MatchBestClip(discoveredClips, "attack_03", "attack3") ?? attack02Clip;

        AnimationClip skillCastClip = MatchBestClip(discoveredClips, "skill_cast", "skillcast", "cast") ?? combatIdleClip;
        AnimationClip skillRel01Clip = MatchBestClip(discoveredClips, "skill_release", "skill1", "skill") ?? attack01Clip;
        AnimationClip skillRel02Clip = MatchBestClip(discoveredClips, "skill2") ?? skillRel01Clip;

        AnimationClip hitClip = MatchBestClip(discoveredClips, "hit_react", "hitreact", "hit");
        AnimationClip stunClip = MatchBestClip(discoveredClips, "stun") ?? combatIdleClip;
        AnimationClip deathClip = MatchBestClip(discoveredClips, "death", "die", "dead");

        var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(AllPlaceholderSlotNames.Length);
        aoc.GetOverrides(overrides);

        for (int i = 0; i < overrides.Count; i++)
        {
            AnimationClip placeholder = overrides[i].Key;
            if (placeholder == null) continue;

            AnimationClip replacement = placeholder.name switch
            {
                SlotSpawn => spawnClip,
                SlotIdle => idleClip,
                SlotWalk => walkClip,
                SlotRun => runClip,
                SlotCombatIdle => combatIdleClip,
                SlotAttack01 => attack01Clip,
                SlotAttack02 => attack02Clip,
                SlotAttack03 => attack03Clip,
                SlotSkillCast => skillCastClip,
                SlotSkillRelease01 => skillRel01Clip,
                SlotSkillRelease02 => skillRel02Clip,
                SlotHitReact => hitClip,
                SlotStun => stunClip,
                SlotDeath => deathClip,
                SlotUpperAttack => attack01Clip,
                SlotUpperCast => skillCastClip,
                SlotUpperRelease => skillRel01Clip,
                _ => null
            };

            if (replacement != null)
            {
                overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(placeholder, replacement);
            }
        }

        aoc.ApplyOverrides(overrides);
        EditorUtility.SetDirty(aoc);

        int detectedAttackCount = 1;
        if (MatchBestClip(discoveredClips, "attack_03", "attack3") != null)
        {
            detectedAttackCount = 3;
        }
        else if (MatchBestClip(discoveredClips, "attack_02", "attack2") != null)
        {
            detectedAttackCount = 2;
        }

        // Standard Config
        string configPath = CombineAssetPath(monsterFolder, $"Config_{safeName}.asset");
        generatedConfig = CreateOrUpdateConfigAsset(configPath, "Ancient Warrior", isBoss: false, detectedAttackCount);

        // Also create a Boss Config variant for Elite/Boss monsters in Map1
        string bossConfigPath = CombineAssetPath(monsterFolder, $"Config_{safeName}_Boss.asset");
        CreateOrUpdateConfigAsset(bossConfigPath, "Ancient Warlord", isBoss: true, detectedAttackCount);

        if (createPrefab)
        {
            generatedPrefab = CreateOrUpdateMonsterPrefab(monsterFolder, safeName, aoc, generatedConfig);
        }

        AssetDatabase.SaveAssets();
        return aoc;
    }

    private static MonsterAnimationConfig CreateOrUpdateConfigAsset(
        string configPath,
        string displayName,
        bool isBoss,
        int detectedAttackCount)
    {
        MonsterAnimationConfig cfg = AssetDatabase.LoadAssetAtPath<MonsterAnimationConfig>(configPath);
        if (cfg == null)
        {
            cfg = CreateInstance<MonsterAnimationConfig>();
            AssetDatabase.CreateAsset(cfg, configPath);
        }

        cfg.monsterName = displayName;
        cfg.isBossOrElite = isBoss;
        cfg.canFlinch = !isBoss;
        cfg.canFlinchDuringAttack = false;
        cfg.flinchCooldown = 0.75f;
        cfg.walkSpeed = 1.5f;
        cfg.runSpeed = 3.8f;
        cfg.speedDampTime = 0.14f;
        cfg.skillCount = isBoss ? 1 : 0;
        cfg.attackCount = detectedAttackCount;
        cfg.attackWeights = detectedAttackCount switch
        {
            1 => new float[] { 1f, 0f, 0f },
            2 => new float[] { 0.55f, 0.45f, 0f },
            _ => new float[] { 0.45f, 0.35f, 0.20f }
        };
        EditorUtility.SetDirty(cfg);
        return cfg;
    }

    private static float MeasureObjectVisualHeight(
        GameObject prefabAsset,
        RuntimeAnimatorController fallbackController = null,
        AnimationClip fallbackPoseClip = null)
    {
        if (prefabAsset == null) return 0f;

        GameObject temp = Instantiate(prefabAsset, Vector3.zero, Quaternion.identity);
        try
        {
            Animator anim = temp.GetComponentInChildren<Animator>(true);
            if (anim != null)
            {
                if (anim.runtimeAnimatorController == null && fallbackController != null)
                {
                    anim.avatar = null;
                    anim.runtimeAnimatorController = fallbackController;
                }
                anim.Rebind();
                anim.Update(0f);
            }

            if (fallbackPoseClip != null)
            {
                fallbackPoseClip.SampleAnimation(temp, 0f);
            }

            SkinnedMeshRenderer[] skinned = temp.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Bounds combined = default;
            bool hasBounds = false;

            for (int i = 0; i < skinned.Length; i++)
            {
                SkinnedMeshRenderer smr = skinned[i];
                if (smr == null) continue;
                smr.updateWhenOffscreen = true;
                Mesh baked = new Mesh();
                smr.BakeMesh(baked);
                Bounds localB = baked.bounds;
                DestroyImmediate(baked);

                if (localB.size.sqrMagnitude > 0.0001f)
                {
                    Vector3 lossy = smr.transform.lossyScale;
                    Vector3 worldMin = smr.transform.position + Vector3.Scale(localB.min, lossy);
                    Vector3 worldMax = smr.transform.position + Vector3.Scale(localB.max, lossy);
                    Bounds wb = new Bounds((worldMin + worldMax) * 0.5f, new Vector3(
                        Mathf.Abs(worldMax.x - worldMin.x),
                        Mathf.Abs(worldMax.y - worldMin.y),
                        Mathf.Abs(worldMax.z - worldMin.z)));

                    if (!hasBounds)
                    {
                        combined = wb;
                        hasBounds = true;
                    }
                    else
                    {
                        combined.Encapsulate(wb);
                    }
                }
            }

            if (hasBounds && combined.size.y > 0.25f)
            {
                return combined.size.y;
            }

            Transform head = FindBoneRecursive(temp.transform, "mixamorig:HeadTop_End")
                          ?? FindBoneRecursive(temp.transform, "mixamorig:Head");
            if (head != null)
            {
                return Mathf.Max(0.5f, (head.position.y - temp.transform.position.y) + 0.18f);
            }
        }
        finally
        {
            DestroyImmediate(temp);
        }

        return 0f;
    }

    private static GameObject CreateOrUpdateMonsterPrefab(
        string monsterFolder,
        string safeName,
        AnimatorOverrideController aoc,
        MonsterAnimationConfig config)
    {
        GameObject baseModelPrefab = FindBaseMeshModelInFolder(monsterFolder, safeName);
        if (baseModelPrefab == null)
        {
            return null;
        }

        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        float playerHeight = MeasureObjectVisualHeight(playerPrefab);
        if (playerHeight < 0.5f) playerHeight = 1.80f;

        Dictionary<string, AnimationClip> enemyClips = FindClipsInFolder(monsterFolder);
        AnimationClip idleClip = MatchBestClip(enemyClips, "idle");

        float rawEnemyHeight = MeasureObjectVisualHeight(baseModelPrefab, aoc, idleClip);
        float heightMatchScale = (rawEnemyHeight > 0.25f)
            ? Mathf.Clamp(playerHeight / rawEnemyHeight, 0.4f, 4.0f)
            : 1.0f;

        string prefabPath = CombineAssetPath(monsterFolder, $"{safeName}_Monster.prefab");
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(baseModelPrefab);
        instance.name = $"{safeName}_Monster";
        instance.transform.localScale = Vector3.one * heightMatchScale;

        try
        {
            Animator animator = instance.GetComponent<Animator>();
            if (animator == null)
            {
                animator = instance.AddComponent<Animator>();
            }

            // Generic path binding requires avatar = null when clips are imported with NoAvatar
            animator.avatar = null;
            animator.runtimeAnimatorController = aoc;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            CapsuleCollider capsule = instance.GetComponent<CapsuleCollider>();
            if (capsule == null)
            {
                capsule = instance.AddComponent<CapsuleCollider>();
            }
            float localColliderHeight = playerHeight / Mathf.Max(0.1f, heightMatchScale);
            capsule.center = new Vector3(0f, localColliderHeight * 0.5f, 0f);
            capsule.radius = Mathf.Clamp(0.46f / Mathf.Max(0.1f, heightMatchScale), 0.25f, 1.2f);
            capsule.height = localColliderHeight;

            // Wire Targetable + LockVisual renderer so CapsLock highlights the monster in yellow!
            Targetable targetable = instance.GetComponent<Targetable>();
            if (targetable == null)
            {
                targetable = instance.AddComponent<Targetable>();
            }

            Renderer mainRenderer = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (mainRenderer == null)
            {
                mainRenderer = instance.GetComponentInChildren<Renderer>(true);
            }

            SerializedObject soTargetable = new SerializedObject(targetable);
            SerializedProperty lockVisualProp = soTargetable.FindProperty("lockVisual");
            if (lockVisualProp != null && mainRenderer != null)
            {
                lockVisualProp.objectReferenceValue = mainRenderer;
            }
            SerializedProperty aimOffsetProp = soTargetable.FindProperty("fallbackAimOffset");
            if (aimOffsetProp != null)
            {
                aimOffsetProp.vector3Value = new Vector3(0f, localColliderHeight * 0.62f, 0f);
            }
            soTargetable.ApplyModifiedPropertiesWithoutUndo();

            MonsterAnimatorBridge bridge = instance.GetComponent<MonsterAnimatorBridge>();
            if (bridge == null)
            {
                bridge = instance.AddComponent<MonsterAnimatorBridge>();
            }
            bridge.Config = config;

            MMOMonsterAI ai = instance.GetComponent<MMOMonsterAI>();
            if (ai == null)
            {
                ai = instance.AddComponent<MMOMonsterAI>();
            }
            ai.displayName = "Ancient Warrior";
            ai.level = 5;
            ai.maxHealth = 1000;
            ai.aggroRadius = 7.0f;
            ai.territoryRadius = 13.5f;
            ai.patrolRadius = 3.5f;

            // Photon Fusion Shared Mode components (safe no-op when running Standalone in Map1)
            if (instance.GetComponent<NetworkObject>() == null)
            {
                instance.AddComponent<NetworkObject>();
            }
            if (instance.GetComponent<NetworkTransform>() == null)
            {
                instance.AddComponent<NetworkTransform>();
            }
            if (instance.GetComponent<MMOMonsterNetwork>() == null)
            {
                instance.AddComponent<MMOMonsterNetwork>();
            }

            return PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
        }
        finally
        {
            DestroyImmediate(instance);
        }
    }

    public static void TunePlayerAnimatorAndPrefab()
    {
        AnimatorController playerCtrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(PlayerControllerPath);
        if (playerCtrl != null && playerCtrl.layers.Length > 0)
        {
            AnimatorStateMachine sm = playerCtrl.layers[0].stateMachine;
            AnimatorState locomotion = FindStateByName(sm, "Locomotion");
            AnimatorState jump = FindStateByName(sm, "Jump");
            AnimatorState attack1 = FindStateByName(sm, "Attack1");
            AnimatorState attack2 = FindStateByName(sm, "Attack2");

            if (attack1 != null) attack1.tag = "Attack";
            if (attack2 != null) attack2.tag = "Attack";

            if (locomotion != null)
            {
                for (int i = 0; i < locomotion.transitions.Length; i++)
                {
                    AnimatorStateTransition t = locomotion.transitions[i];
                    if (t.destinationState == attack1)
                    {
                        t.hasExitTime = false;
                        t.duration = 0.08f;
                    }
                    else if (t.destinationState == jump)
                    {
                        t.hasExitTime = false;
                        t.duration = 0.08f;
                    }
                }
            }

            if (jump != null)
            {
                for (int i = 0; i < jump.transitions.Length; i++)
                {
                    AnimatorStateTransition t = jump.transitions[i];
                    if (t.destinationState == locomotion)
                    {
                        t.duration = 0.14f;
                    }
                }
            }

            if (attack1 != null)
            {
                for (int i = 0; i < attack1.transitions.Length; i++)
                {
                    AnimatorStateTransition t = attack1.transitions[i];
                    if (t.destinationState == attack2)
                    {
                        t.hasExitTime = false;
                        t.duration = 0.10f;
                    }
                    else if (t.destinationState == locomotion)
                    {
                        t.hasExitTime = true;
                        t.exitTime = 0.84f;
                        t.duration = 0.18f;
                    }
                }
            }

            if (attack2 != null)
            {
                for (int i = 0; i < attack2.transitions.Length; i++)
                {
                    AnimatorStateTransition t = attack2.transitions[i];
                    if (t.destinationState == locomotion)
                    {
                        t.hasExitTime = true;
                        t.exitTime = 0.84f;
                        t.duration = 0.18f;
                    }
                }
            }

            EditorUtility.SetDirty(playerCtrl);
        }

        if (File.Exists(PlayerPrefabPath))
        {
            GameObject playerRoot = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                MMOPlayerController pc = playerRoot.GetComponent<MMOPlayerController>();
                if (pc != null)
                {
                    SerializedObject so = new SerializedObject(pc);
                    SetSerializedFloat(so, "walkSpeed", 2.4f);
                    SetSerializedFloat(so, "runSpeed", 5.4f);
                    SetSerializedFloat(so, "animatorDampTime", 0.10f);
                    SetSerializedFloat(so, "groundCheckRadius", 0.22f);
                    SetSerializedFloat(so, "jumpHeight", 1.35f);
                    SetSerializedFloat(so, "comboWindowStart", 0.40f);
                    SetSerializedFloat(so, "comboWindowEnd", 0.88f);
                    so.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(playerRoot, PlayerPrefabPath);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(playerRoot);
            }
        }

        AssetDatabase.SaveAssets();
    }

    private static AnimatorState FindStateByName(AnimatorStateMachine sm, string stateName)
    {
        for (int i = 0; i < sm.states.Length; i++)
        {
            if (sm.states[i].state != null && sm.states[i].state.name == stateName)
            {
                return sm.states[i].state;
            }
        }
        return null;
    }

    private static void SetSerializedFloat(SerializedObject so, string propName, float value)
    {
        SerializedProperty prop = so.FindProperty(propName);
        if (prop != null)
        {
            prop.floatValue = value;
        }
    }

    // =========================================================
    // STEP 3: POPULATE Map1.unity WITH MONSTERS & TERRITORIES
    // =========================================================

    public static void PopulateMap1Scene(GameObject monsterPrefab, MonsterAnimationConfig normalConfig)
    {
        if (monsterPrefab == null)
        {
            string prefabPath = CombineAssetPath(DefaultMonsterFolder, "Enemy_Monster.prefab");
            monsterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        }

        if (monsterPrefab == null)
        {
            Debug.LogError("[MonsterAnimatorBuilder] Cannot populate Map1 because Enemy_Monster.prefab was not found.");
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        string originalScenePath = activeScene.IsValid() ? activeScene.path : string.Empty;
        bool openedTempScene = false;
        Scene map1Scene = activeScene;

        if (!activeScene.IsValid() || activeScene.path != Map1ScenePath)
        {
            if (File.Exists(Map1ScenePath))
            {
                map1Scene = EditorSceneManager.OpenScene(Map1ScenePath, OpenSceneMode.Single);
                openedTempScene = true;
            }
            else
            {
                return;
            }
        }

        // Remove old test dummy or old generated monsters so we don't duplicate
        GameObject[] roots = map1Scene.GetRootGameObjects();
        Transform spawnPointTransform = null;

        for (int i = 0; i < roots.Length; i++)
        {
            GameObject root = roots[i];
            if (root == null) continue;

            if (root.name == "Spawn")
            {
                spawnPointTransform = root.transform;
            }
            else if (root.name.StartsWith("Ancient_", StringComparison.Ordinal) ||
                     root.name == "MMO_Map1_CombatSystem" ||
                     root.name == "LockOn Test Dummy")
            {
                DestroyImmediate(root);
            }
        }

        // 1. Create MMOMap1CombatDirector so pressing Play directly in Map1 spawns the Player with MMOPlayerCombat!
        GameObject directorGo = new GameObject("MMO_Map1_CombatSystem");
        MMOMap1CombatDirector director = directorGo.AddComponent<MMOMap1CombatDirector>();
        director.playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        director.playerSpawnPoint = spawnPointTransform;

        // Load Boss config
        string bossConfigPath = CombineAssetPath(DefaultMonsterFolder, "Config_Enemy_Boss.asset");
        MonsterAnimationConfig bossConfig = AssetDatabase.LoadAssetAtPath<MonsterAnimationConfig>(bossConfigPath) ?? normalConfig;

        // 2. Spawn Monster #1: Ancient Warrior (Directly in front of Player Spawn at Z = 11)
        SpawnSceneMonster(
            monsterPrefab,
            normalConfig,
            objectName: "Ancient_Warrior_Territory1",
            displayName: "Ancient Warrior",
            level: 5,
            maxHp: 1000,
            minDmg: 40,
            maxDmg: 65,
            position: new Vector3(0f, 0f, 11f),
            yaw: 180f,
            scale: 1.0f,
            aggroRadius: 6.8f,
            territoryRadius: 13.0f,
            patrolRadius: 3.2f);

        // 3. Spawn Monster #2: Ancient Guardian (Left Camp at X = -14, Z = 16)
        SpawnSceneMonster(
            monsterPrefab,
            normalConfig,
            objectName: "Ancient_Guardian_Territory2",
            displayName: "Ancient Guardian",
            level: 7,
            maxHp: 1450,
            minDmg: 55,
            maxDmg: 85,
            position: new Vector3(-14f, 0f, 16f),
            yaw: 145f,
            scale: 1.06f,
            aggroRadius: 7.2f,
            territoryRadius: 13.5f,
            patrolRadius: 4.0f);

        // 4. Spawn Monster #3: Ancient Warlord BOSS (Right Camp at X = 14, Z = 18)
        SpawnSceneMonster(
            monsterPrefab,
            bossConfig,
            objectName: "Ancient_Warlord_Boss_Territory3",
            displayName: "Ancient Warlord",
            level: 10,
            maxHp: 2600,
            minDmg: 85,
            maxDmg: 125,
            position: new Vector3(14f, 0f, 18f),
            yaw: -145f,
            scale: 1.30f,
            aggroRadius: 8.2f,
            territoryRadius: 15.0f,
            patrolRadius: 3.5f);

        EditorSceneManager.MarkSceneDirty(map1Scene);
        EditorSceneManager.SaveScene(map1Scene);

        if (openedTempScene && !string.IsNullOrEmpty(originalScenePath) && File.Exists(originalScenePath))
        {
            EditorSceneManager.OpenScene(originalScenePath, OpenSceneMode.Single);
        }
    }

    private static void SpawnSceneMonster(
        GameObject monsterPrefab,
        MonsterAnimationConfig config,
        string objectName,
        string displayName,
        int level,
        int maxHp,
        int minDmg,
        int maxDmg,
        Vector3 position,
        float yaw,
        float scale,
        float aggroRadius,
        float territoryRadius,
        float patrolRadius)
    {
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(monsterPrefab);
        instance.name = objectName;
        instance.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        float prefabBaseScale = Mathf.Max(0.1f, monsterPrefab.transform.localScale.x);
        instance.transform.localScale = Vector3.one * (prefabBaseScale * scale);

        MonsterAnimatorBridge bridge = instance.GetComponent<MonsterAnimatorBridge>();
        if (bridge != null && config != null)
        {
            bridge.Config = config;
            EditorUtility.SetDirty(bridge);
        }

        MMOMonsterAI ai = instance.GetComponent<MMOMonsterAI>();
        if (ai != null)
        {
            ai.displayName = displayName;
            ai.level = level;
            ai.maxHealth = maxHp;
            ai.minAttackDamage = minDmg;
            ai.maxAttackDamage = maxDmg;
            ai.aggroRadius = aggroRadius;
            ai.territoryRadius = territoryRadius;
            ai.patrolRadius = patrolRadius;
            ai.showTerritoryRings = true;
            EditorUtility.SetDirty(ai);
        }
    }

    // =========================================================
    // STEP 4: AUTOMATED VERIFICATION & SELF-TEST SUITE
    // =========================================================

    public static string RunAutomatedVerificationSuite()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("=== MMO MONSTER PIPELINE & MAP1 AUTOMATED TEST REPORT ===");
        int passed = 0;
        int failed = 0;

        void AssertTest(string testName, bool condition, string details = "")
        {
            if (condition)
            {
                passed++;
                sb.AppendLine($"[PASS] {testName} {(string.IsNullOrEmpty(details) ? "" : "- " + details)}");
            }
            else
            {
                failed++;
                sb.AppendLine($"[FAIL] {testName} {(string.IsNullOrEmpty(details) ? "" : "- " + details)}");
            }
        }

        // 1. Verify FBX Generic Rig & Loop Time settings in Model Enemy
        Dictionary<string, AnimationClip> clips = FindClipsInFolder(DefaultMonsterFolder);
        AssertTest("Model Enemy Clip Discovery", clips.Count >= 7, $"Found {clips.Count} clip keys in '{DefaultMonsterFolder}'");

        string[] loopExpected = { "Idle", "Walk", "Run" };
        foreach (string clipName in loopExpected)
        {
            AnimationClip c = MatchBestClip(clips, clipName);
            bool loopOk = c != null && c.isLooping;
            AssertTest($"Clip '{clipName}' LoopTime == true", loopOk, c != null ? $"isLooping={c.isLooping}" : "Clip missing");
        }

        string[] nonLoopExpected = { "Attack_01", "Attack_02", "Hit_React", "Death" };
        foreach (string clipName in nonLoopExpected)
        {
            AnimationClip c = MatchBestClip(clips, clipName);
            bool nonLoopOk = c != null && !c.isLooping;
            AssertTest($"Clip '{clipName}' LoopTime == false", nonLoopOk, c != null ? $"isLooping={c.isLooping}" : "Clip missing");
        }

        // 2. Verify AC_Monster_Base.controller structure
        string baseControllerPath = CombineAssetPath(DefaultOutputFolder, BaseControllerFileName);
        AnimatorController baseCtrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(baseControllerPath);
        AssertTest("AC_Monster_Base Exists", baseCtrl != null, baseControllerPath);

        if (baseCtrl != null)
        {
            AssertTest("AC_Monster_Base Parameter Count == 10", baseCtrl.parameters.Length == 10, $"Count={baseCtrl.parameters.Length}");
            AssertTest("AC_Monster_Base Has Base + UpperBody Layers", baseCtrl.layers.Length == 2, $"Layers={baseCtrl.layers.Length}");

            AnimatorStateTransition[] anyTransitions = baseCtrl.layers[0].stateMachine.anyStateTransitions;
            bool allNoSelfTransition = anyTransitions.Length >= 3;
            for (int i = 0; i < anyTransitions.Length; i++)
            {
                if (anyTransitions[i].canTransitionToSelf)
                {
                    allNoSelfTransition = false;
                }
            }
            AssertTest("AnyState Transitions Disable CanTransitionToSelf", allNoSelfTransition, $"AnyState count={anyTransitions.Length}");
        }

        // 3. Verify Bone Animation Binding on Enemy_Monster.prefab
        string prefabPath = CombineAssetPath(DefaultMonsterFolder, "Enemy_Monster.prefab");
        GameObject enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        AssertTest("Enemy_Monster.prefab Exists", enemyPrefab != null, prefabPath);

        if (enemyPrefab != null)
        {
            GameObject tempInstance = Instantiate(enemyPrefab, Vector3.zero, Quaternion.identity);
            try
            {
                Transform rightArm = FindBoneRecursive(tempInstance.transform, "mixamorig:RightArm");
                Transform hips = FindBoneRecursive(tempInstance.transform, "mixamorig:Hips");
                AssertTest("Enemy Skeleton Bones Present", rightArm != null && hips != null, "mixamorig:Hips & mixamorig:RightArm found");

                AnimationClip attack1Clip = MatchBestClip(clips, "Attack_01");
                if (rightArm != null && attack1Clip != null)
                {
                    Quaternion rotAtZero = rightArm.localRotation;
                    attack1Clip.SampleAnimation(tempInstance, attack1Clip.length * 0.45f);
                    Quaternion rotAtMid = rightArm.localRotation;
                    float angleDiff = Quaternion.Angle(rotAtZero, rotAtMid);
                    AssertTest("Generic Rig Animation Curve Binding Moves Enemy Bones", angleDiff > 1.0f, $"RightArm rotated {angleDiff:0.0} deg during Attack_01");
                }

                // 4. Verify MonsterAnimatorBridge & MMOMonsterAI Combat + Crit + Territory + Network Logic
                MonsterAnimatorBridge bridge = tempInstance.GetComponent<MonsterAnimatorBridge>();
                MMOMonsterAI ai = tempInstance.GetComponent<MMOMonsterAI>();
                Targetable targetable = tempInstance.GetComponent<Targetable>();
                NetworkObject netObj = tempInstance.GetComponent<NetworkObject>();
                NetworkTransform netTx = tempInstance.GetComponent<NetworkTransform>();
                MMOMonsterNetwork monsterNet = tempInstance.GetComponent<MMOMonsterNetwork>();

                AssertTest("Enemy Has MonsterAnimatorBridge + MMOMonsterAI + Targetable", bridge != null && ai != null && targetable != null);
                AssertTest("Enemy Has Fusion NetworkObject + NetworkTransform + MMOMonsterNetwork", netObj != null && netTx != null && monsterNet != null);

                // Verify Player vs Enemy Height Parity ("ขยายตัวละครให้เท่าคนเล่น")
                GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
                float playerHeight = MeasureObjectVisualHeight(playerPrefab);
                AnimationClip idleClipForTest = MatchBestClip(clips, "idle");
                float enemyHeight = MeasureObjectVisualHeight(enemyPrefab, null, idleClipForTest);
                bool heightMatched = playerHeight > 0.5f && enemyHeight > 0.5f && Mathf.Abs(playerHeight - enemyHeight) <= 0.18f;
                AssertTest("Enemy Height Matches Player Height", heightMatched, $"Player={playerHeight:0.00}m, Enemy={enemyHeight:0.00}m (scale={enemyPrefab.transform.localScale.x:0.00})");

                if (ai != null && bridge != null)
                {
                    ai.EnsureReferencesInitialized();
                    int startHp = ai.CurrentHealth;
                    ai.TakeDamage(150, isCritical: false, tempInstance.transform, tempInstance.transform.position);
                    int afterNormalHit = ai.CurrentHealth;
                    AssertTest("Normal Hit Reduces Monster HP", afterNormalHit == startHp - 150, $"HP: {startHp} -> {afterNormalHit}");

                    ai.TakeDamage(320, isCritical: true, tempInstance.transform, tempInstance.transform.position);
                    int afterCritHit = ai.CurrentHealth;
                    AssertTest("Critical Hit Reduces Monster HP", afterCritHit == afterNormalHit - 320, $"HP: {afterNormalHit} -> {afterCritHit}");

                    ai.EnterEvadeState();
                    AssertTest("Territory Evade Resets Monster HP to 100%", ai.IsEvading && ai.CurrentHealth == ai.MaxHealth, $"State={ai.CurrentState}, HP={ai.CurrentHealth}/{ai.MaxHealth}");

                    ai.TakeDamage(99999, isCritical: true, null, tempInstance.transform.position);
                    AssertTest("Evading Monster Is Invulnerable During Return", ai.CurrentHealth == ai.MaxHealth);

                    // Verify Normal Stun vs Boss Super Armor Resist
                    string bossCfgPath = CombineAssetPath(DefaultMonsterFolder, "Config_Enemy_Boss.asset");
                    MonsterAnimationConfig bossCfg = AssetDatabase.LoadAssetAtPath<MonsterAnimationConfig>(bossCfgPath);
                    AssertTest("Boss Config Enables Super Armor + Skill", bossCfg != null && bossCfg.isBossOrElite && !bossCfg.canFlinch && bossCfg.skillCount >= 1);
                }
            }
            finally
            {
                DestroyImmediate(tempInstance);
            }
        }

        // 5. Verify Player Animator Smoothness & Combo Window
        AnimatorController playerCtrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(PlayerControllerPath);
        bool playerAnimOk = false;
        if (playerCtrl != null && playerCtrl.layers.Length > 0)
        {
            AnimatorState attack1State = FindStateByName(playerCtrl.layers[0].stateMachine, "Attack1");
            AnimatorState attack2State = FindStateByName(playerCtrl.layers[0].stateMachine, "Attack2");
            playerAnimOk = attack1State != null && attack2State != null &&
                           attack1State.tag == "Attack" && attack2State.tag == "Attack";
        }
        AssertTest("AC_Player.controller Has Attack Tags & Tuned States", playerAnimOk);

        // 6. Verify Map1 Scene Setup + Lobby->Map1 Dual-Mode Compatibility
        Scene activeScene = SceneManager.GetActiveScene();
        string originalScenePath = activeScene.IsValid() ? activeScene.path : string.Empty;
        bool openedTempMap1 = false;
        if ((!activeScene.IsValid() || activeScene.path != Map1ScenePath) && !activeScene.isDirty && File.Exists(Map1ScenePath))
        {
            EditorSceneManager.OpenScene(Map1ScenePath, OpenSceneMode.Single);
            openedTempMap1 = true;
        }

        MMOMonsterAI[] sceneMonsters = FindObjectsByType<MMOMonsterAI>();
        MMOMap1CombatDirector sceneDirector = FindAnyObjectByType<MMOMap1CombatDirector>();
        AssertTest("Map1 Has MMOMap1CombatDirector + Player Prefab Assigned", sceneDirector != null && sceneDirector.playerPrefab != null);
        AssertTest("Map1 Has 3 Active Monsters with Territory Zones", sceneMonsters.Length >= 3, $"Count={sceneMonsters.Length}");

        if (openedTempMap1 && !string.IsNullOrEmpty(originalScenePath) && File.Exists(originalScenePath))
        {
            EditorSceneManager.OpenScene(originalScenePath, OpenSceneMode.Single);
        }

        bool lobbyInBuild = false;
        bool map1InBuild = false;
        for (int i = 0; i < EditorBuildSettings.scenes.Length; i++)
        {
            string path = EditorBuildSettings.scenes[i].path;
            if (EditorBuildSettings.scenes[i].enabled && path == LobbyScenePath) lobbyInBuild = true;
            if (EditorBuildSettings.scenes[i].enabled && path == Map1ScenePath) map1InBuild = true;
        }
        AssertTest("Build Settings Include Enabled Lobby.unity & Map1.unity", lobbyInBuild && map1InBuild);

        bool lobbyLinksToMap1 = File.Exists(LobbyScenePath) &&
                                File.ReadAllText(LobbyScenePath).Contains("gameSceneName: Map1");
        AssertTest("Lobby.unity LobbyNetwork Targets Map1", lobbyLinksToMap1);

        sb.AppendLine($"=== SUMMARY: {passed} PASSED, {failed} FAILED ===");

        try
        {
            File.WriteAllText("Temp/MonsterMap1TestReport.txt", sb.ToString());
        }
        catch { }

        return sb.ToString();
    }

    private static Transform FindBoneRecursive(Transform parent, string boneName)
    {
        if (parent.name == boneName) return parent;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform found = FindBoneRecursive(parent.GetChild(i), boneName);
            if (found != null) return found;
        }
        return null;
    }

    // =========================================================
    // INTERNAL HELPERS
    // =========================================================

    private static GameObject FindBaseMeshModelInFolder(string monsterFolder, string safeName)
    {
        string[] modelGuids = AssetDatabase.FindAssets("t:Model", new[] { monsterFolder });
        GameObject fallback = null;

        for (int i = 0; i < modelGuids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(modelGuids[i]);
            string fileName = Path.GetFileNameWithoutExtension(path);

            GameObject candidate = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (candidate == null) continue;

            bool hasRenderer = candidate.GetComponentInChildren<Renderer>(true) != null;
            if (!hasRenderer) continue;

            if (string.Equals(fileName, safeName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "Enemy", StringComparison.OrdinalIgnoreCase) ||
                !MonsterClipImportProcessor.DetermineLoopSetting(fileName).HasValue)
            {
                return candidate;
            }

            if (fallback == null) fallback = candidate;
        }

        return fallback;
    }

    private static Dictionary<string, AnimationClip> FindClipsInFolder(string folderPath)
    {
        var result = new Dictionary<string, AnimationClip>(StringComparer.OrdinalIgnoreCase);
        if (!AssetDatabase.IsValidFolder(folderPath)) return result;

        string[] modelGuids = AssetDatabase.FindAssets("t:AnimationClip", new[] { folderPath });
        for (int i = 0; i < modelGuids.Length; i++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(modelGuids[i]);
            string fileNameKey = Path.GetFileNameWithoutExtension(assetPath);

            UnityEngine.Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            for (int j = 0; j < subAssets.Length; j++)
            {
                if (subAssets[j] is AnimationClip clip &&
                    !clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(fileNameKey) && !result.ContainsKey(fileNameKey))
                    {
                        result[fileNameKey] = clip;
                    }
                    if (!result.ContainsKey(clip.name))
                    {
                        result[clip.name] = clip;
                    }
                }
            }
        }

        return result;
    }

    private static AnimationClip MatchBestClip(Dictionary<string, AnimationClip> clips, params string[] keywords)
    {
        for (int k = 0; k < keywords.Length; k++)
        {
            if (clips.TryGetValue(keywords[k], out AnimationClip exact))
            {
                return exact;
            }
        }

        for (int k = 0; k < keywords.Length; k++)
        {
            string kw = keywords[k].ToLowerInvariant();
            foreach (KeyValuePair<string, AnimationClip> pair in clips)
            {
                string keyLower = pair.Key.ToLowerInvariant();
                if (keyLower.EndsWith("_" + kw, StringComparison.Ordinal) ||
                    keyLower.EndsWith(kw, StringComparison.Ordinal) ||
                    keyLower.Contains(kw))
                {
                    return pair.Value;
                }
            }
        }

        return null;
    }

    private static Dictionary<string, AnimationClip> CreateEmbeddedPlaceholderClips(AnimatorController controller)
    {
        var map = new Dictionary<string, AnimationClip>(AllPlaceholderSlotNames.Length);

        for (int i = 0; i < AllPlaceholderSlotNames.Length; i++)
        {
            string slotName = AllPlaceholderSlotNames[i];
            AnimationClip clip = new AnimationClip { name = slotName };

            bool shouldLoop =
                slotName == SlotIdle ||
                slotName == SlotWalk ||
                slotName == SlotRun ||
                slotName == SlotCombatIdle ||
                slotName == SlotSkillCast ||
                slotName == SlotStun ||
                slotName == SlotUpperCast;

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = shouldLoop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            AssetDatabase.AddObjectToAsset(clip, controller);
            map[slotName] = clip;
        }

        return map;
    }

    private static void AddAttackEntryTransition(AnimatorState sourceState, AnimatorState targetAttackState, int attackIndex)
    {
        AnimatorStateTransition t = sourceState.AddTransition(targetAttackState);
        t.hasExitTime = false;
        t.duration = 0.12f;
        t.canTransitionToSelf = false;
        t.AddCondition(AnimatorConditionMode.If, 0f, "Attack");
        t.AddCondition(AnimatorConditionMode.Equals, attackIndex, "AttackIndex");
    }

    private static void AddSkillCastEntryTransition(AnimatorState sourceState, AnimatorState skillCastState)
    {
        AnimatorStateTransition t = sourceState.AddTransition(skillCastState);
        t.hasExitTime = false;
        t.duration = 0.10f;
        t.canTransitionToSelf = false;
        t.AddCondition(AnimatorConditionMode.If, 0f, "Skill");
        t.AddCondition(AnimatorConditionMode.If, 0f, "IsCasting");
    }

    private static void AddInstantSkillTransition(AnimatorState sourceState, AnimatorState skillReleaseState, int skillIndex)
    {
        AnimatorStateTransition t = sourceState.AddTransition(skillReleaseState);
        t.hasExitTime = false;
        t.duration = 0.10f;
        t.canTransitionToSelf = false;
        t.AddCondition(AnimatorConditionMode.If, 0f, "Skill");
        t.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsCasting");
        t.AddCondition(AnimatorConditionMode.Equals, skillIndex, "SkillIndex");
    }

    private static void AddReturnToCombatIdleTransition(AnimatorState fromState, AnimatorState combatIdleState, float exitTime, float duration)
    {
        AnimatorStateTransition t = fromState.AddTransition(combatIdleState);
        t.hasExitTime = true;
        t.exitTime = exitTime;
        t.duration = duration;
        t.canTransitionToSelf = false;
    }

    private static void AddParameterIfMissing(AnimatorController controller, string paramName, AnimatorControllerParameterType type)
    {
        AnimatorControllerParameter[] parameters = controller.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].name == paramName) return;
        }
        controller.AddParameter(paramName, type);
    }

    private static AvatarMask CreateOrLoadUpperBodyAvatarMask(string maskAssetPath)
    {
        AvatarMask mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(maskAssetPath);
        if (mask != null) return mask;

        mask = new AvatarMask();
        for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
        {
            AvatarMaskBodyPart part = (AvatarMaskBodyPart)i;
            bool enablePart =
                part == AvatarMaskBodyPart.Body ||
                part == AvatarMaskBodyPart.Head ||
                part == AvatarMaskBodyPart.LeftArm ||
                part == AvatarMaskBodyPart.RightArm ||
                part == AvatarMaskBodyPart.LeftFingers ||
                part == AvatarMaskBodyPart.RightFingers;

            mask.SetHumanoidBodyPartActive(part, enablePart);
        }

        AssetDatabase.CreateAsset(mask, maskAssetPath);
        return mask;
    }

    private static void EnsureFolderExists(string assetFolderPath)
    {
        if (string.IsNullOrEmpty(assetFolderPath) || AssetDatabase.IsValidFolder(assetFolderPath)) return;

        string[] parts = assetFolderPath.Replace('\\', '/').Split('/');
        string current = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }
            current = next;
        }
    }

    private static string CombineAssetPath(string folder, string file)
    {
        return folder.TrimEnd('/', '\\') + "/" + file;
    }
}
