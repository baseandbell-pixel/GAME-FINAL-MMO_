#if UNITY_EDITOR
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
/// 100% Self-Contained Monster Package Builder inside <c>Assets/MonsterSystem/</c>.
/// Touches ZERO files in the friend's <c>Assets/_Project/</c> or <c>Assets/Code/</c> folders so merging
/// or moving this package into another branch produces ZERO merge conflicts!
///
/// Generates:
///   - <c>Assets/MonsterSystem/Animations/AC_Monster_Base.controller</c>
///   - <c>Assets/MonsterSystem/Animations/Orc/AOC_Orc.overrideController</c> &amp; <c>Config_Orc.asset</c>
///   - <c>Assets/MonsterSystem/Materials/Orc/Mat_Orc_Body.mat</c> &amp; <c>Mat_Orc_Accesories.mat</c>
///   - <c>Assets/MonsterSystem/Prefabs/Monsters/Enemy_Orc.prefab</c> (uses <see cref="MonsterEnemyNPC"/>)
///   - <c>Assets/MonsterSystem/Prefabs/Zones/Zone_Orc.prefab</c> (plug-and-play <see cref="MonsterSpawnZone"/>)
/// </summary>
public static class MonsterAnimatorBuilder
{
    public const string SystemRoot = "Assets/MonsterSystem";
    public const string ModelsRoot = SystemRoot + "/Models";
    public const string AnimRoot = SystemRoot + "/Animations";
    public const string BaseControllerPath = AnimRoot + "/AC_Monster_Base.controller";
    public const string MaterialsRoot = SystemRoot + "/Materials";
    public const string MonsterPrefabsRoot = SystemRoot + "/Prefabs/Monsters";
    public const string ZonePrefabsRoot = SystemRoot + "/Prefabs/Zones";
    public const string ManagerPrefabPath = SystemRoot + "/Prefabs/MonsterSystem_Manager.prefab";
    public const string VerificationReportPath = SystemRoot + "/VerificationReport.txt";

    // Read-only reference to existing Enemy.prefab & WorldItem.prefab (NEVER modified!)
    public const string FriendEnemyTemplatePath = "Assets/_Project/Prefabs/Enemy.prefab";
    public const string FriendWorldItemPath = "Assets/_Project/Prefabs/WorldItem.prefab";
    public const string OrcSourceFolder = ModelsRoot + "/Orc";
    public const string OrcPrefabPath = MonsterPrefabsRoot + "/Enemy_Orc.prefab";
    public const string OrcZonePrefabPath = ZonePrefabsRoot + "/Zone_Orc.prefab";

    // Canonical placeholder clip keys embedded inside AC_Monster_Base.controller
    private const string KeySpawn = "Placeholder_Spawn";
    private const string KeyIdle = "Placeholder_Idle";
    private const string KeyWalk = "Placeholder_Walk";
    private const string KeyRun = "Placeholder_Run";
    private const string KeyCombatIdle = "Placeholder_Combat_Idle";
    private const string KeyAttack01 = "Placeholder_Attack_01";
    private const string KeyAttack02 = "Placeholder_Attack_02";
    private const string KeyAttack03 = "Placeholder_Attack_03";
    private const string KeySkillCast = "Placeholder_Skill_Cast";
    private const string KeySkillRelease = "Placeholder_Skill_Release";
    private const string KeyHitReact = "Placeholder_Hit_React";
    private const string KeyStun = "Placeholder_Stun";
    private const string KeyDeath = "Placeholder_Death";

    private static bool _autoBuildScheduled;

    [InitializeOnLoadMethod]
    private static void AutoBuildPackageIfMissing()
    {
        if (_autoBuildScheduled) return;
        _autoBuildScheduled = true;

        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            bool needBuild = !File.Exists(BaseControllerPath) ||
                             !File.Exists(OrcPrefabPath) ||
                             !File.Exists(OrcZonePrefabPath) ||
                             !File.Exists(ManagerPrefabPath) ||
                             !File.Exists(VerificationReportPath);
            if (needBuild && Directory.Exists(OrcSourceFolder))
            {
                BuildOrcPackageAndVerify();
            }

            // One-time initial wire into Camp 1 of SandboxWorldSpawner in Dev_Kulpat.unity ONLY if campEnemyPrefabs hasn't been configured yet!
            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid() &&
                activeScene.name.Equals("Dev_Kulpat", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(OrcPrefabPath))
            {
                SandboxWorldSpawner sp = UnityEngine.Object.FindAnyObjectByType<SandboxWorldSpawner>(FindObjectsInactive.Include);
                if (sp != null)
                {
                    var spSo = new SerializedObject(sp);
                    SerializedProperty arr = spSo.FindProperty("campEnemyPrefabs");
                    if (arr != null && arr.arraySize == 0)
                        AssignOrcToCamp1InActiveScene(saveScene: true, removeExistingManager: false);
                }
            }
        };
    }

    [MenuItem("Tools/ระบบมอนสเตอร์ (Monster System)/1. บิลด์และตรวจสอบแพ็ก Orc")]
    public static void BuildOrcPackageAndVerify()
    {
        try
        {
            Debug.Log("[MonsterSystem] Building self-contained MonsterSystem package (Orc)...");
            AnimatorController baseCtrl = BuildBaseAnimatorController();
            if (baseCtrl == null)
            {
                Debug.LogError("[MonsterSystem] Failed to build AC_Monster_Base.controller.");
                return;
            }

            BuildMonsterFromFolder(
                sourceFolder: OrcSourceFolder,
                monsterName: "Orc",
                isBoss: false,
                targetWorldHeight: 2.15f);

            BuildZonePrefab(
                monsterName: "Orc",
                monsterPrefabPath: OrcPrefabPath,
                zoneOutputPath: OrcZonePrefabPath,
                spawnCount: 4,
                slotSpacing: 12f,
                territoryRadius: 15f,
                respawnMin: 35f,
                respawnMax: 50f);

            BuildManagerPrefab();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            VerifyMonsterSystemPackage();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[MonsterSystem] Build failed: {ex}");
        }
    }

    [MenuItem("Tools/ระบบมอนสเตอร์ (Monster System)/2. บิลด์มอนสเตอร์ทุกตัวในโฟลเดอร์ Models")]
    public static void BuildAllMonstersInModelsFolder()
    {
        try
        {
            if (!Directory.Exists(ModelsRoot))
            {
                Debug.LogWarning($"[MonsterSystem] Models root '{ModelsRoot}' not found.");
                return;
            }

            AnimatorController baseCtrl = BuildBaseAnimatorController();
            if (baseCtrl == null)
            {
                Debug.LogError("[MonsterSystem] Failed to build AC_Monster_Base.controller.");
                return;
            }

            string[] subDirs = Directory.GetDirectories(ModelsRoot);
            int builtCount = 0;
            for (int i = 0; i < subDirs.Length; i++)
            {
                string folderAssetPath = subDirs[i].Replace('\\', '/');
                string monsterName = Path.GetFileName(folderAssetPath);
                if (string.IsNullOrWhiteSpace(monsterName) || monsterName.StartsWith(".")) continue;

                if (FindSkinnedModelFbx(folderAssetPath) == null)
                {
                    Debug.LogWarning($"[MonsterSystem] Skipping '{folderAssetPath}' (no rigged SkinnedMeshRenderer FBX found).");
                    continue;
                }

                bool isBoss = monsterName.IndexOf("boss", StringComparison.OrdinalIgnoreCase) >= 0;
                float height = isBoss ? 3.2f : 2.15f;
                string prefabPath = $"{MonsterPrefabsRoot}/Enemy_{monsterName}.prefab";
                string zonePath = $"{ZonePrefabsRoot}/Zone_{monsterName}.prefab";

                BuildMonsterFromFolder(folderAssetPath, monsterName, isBoss, height);
                BuildZonePrefab(monsterName, prefabPath, zonePath,
                    spawnCount: isBoss ? 1 : 4,
                    slotSpacing: 12f,
                    territoryRadius: 15f,
                    respawnMin: isBoss ? 180f : 35f,
                    respawnMax: isBoss ? 240f : 50f);
                builtCount++;
            }

            BuildManagerPrefab();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            VerifyMonsterSystemPackage();
            Debug.Log($"[MonsterSystem] Built {builtCount} monster package(s) from '{ModelsRoot}' + updated MonsterSystem_Manager.prefab.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[MonsterSystem] BuildAllMonstersInModelsFolder failed: {ex}");
        }
    }

    /// <summary>Rebuilds ONLY the specified monster (from Models/&lt;monsterName&gt;) without touching other monsters.</summary>
    public static void BuildSingleMonsterByName(string monsterName)
    {
        if (string.IsNullOrWhiteSpace(monsterName)) return;
        string folderAssetPath = $"{ModelsRoot}/{monsterName}";
        if (!Directory.Exists(folderAssetPath))
        {
            Debug.LogError($"[MonsterSystem] Folder '{folderAssetPath}' not found. Place FBX files there first.");
            return;
        }

        string configPath = $"{AnimRoot}/{monsterName}/Config_{monsterName}.asset";
        MonsterAnimationConfig existingCfg = AssetDatabase.LoadAssetAtPath<MonsterAnimationConfig>(configPath);
        bool isBoss = existingCfg != null
            ? existingCfg.isBoss
            : monsterName.IndexOf("boss", StringComparison.OrdinalIgnoreCase) >= 0;
        float height = existingCfg != null ? Mathf.Max(0.5f, existingCfg.targetWorldHeight) : (isBoss ? 3.2f : 2.15f);

        BuildMonsterFromFolder(folderAssetPath, monsterName, isBoss, height);

        string prefabPath = $"{MonsterPrefabsRoot}/Enemy_{monsterName}.prefab";
        string zonePath = $"{ZonePrefabsRoot}/Zone_{monsterName}.prefab";
        BuildZonePrefab(monsterName, prefabPath, zonePath,
            spawnCount: isBoss ? 1 : 4,
            slotSpacing: 12f,
            territoryRadius: 15f,
            respawnMin: isBoss ? 180f : 35f,
            respawnMax: isBoss ? 240f : 50f);

        BuildManagerPrefab();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[MonsterSystem] Rebuilt '{monsterName}' cleanly (Config & Loot Table preserved!).");
    }

    [MenuItem("Tools/ระบบมอนสเตอร์ (Monster System)/3. ใส่ Orc ลง Camp 1 ในฉากนี้ (คง Camp 2-4 เดิม)")]
    public static void AssignOrcToCamp1Menu()
    {
        AssignOrcToCamp1InActiveScene(saveScene: true, removeExistingManager: false);
    }

    public static void AssignOrcToCamp1InActiveScene(bool saveScene, bool removeExistingManager = false)
    {
        SandboxWorldSpawner spawner = UnityEngine.Object.FindAnyObjectByType<SandboxWorldSpawner>(FindObjectsInactive.Include);
        if (spawner == null)
        {
            Debug.LogWarning("[MonsterSystem] No SandboxWorldSpawner found in active scene.");
            return;
        }

        EnemyNPC orcPrefab = AssetDatabase.LoadAssetAtPath<EnemyNPC>(OrcPrefabPath);
        if (orcPrefab == null)
        {
            BuildOrcPackageAndVerify();
            orcPrefab = AssetDatabase.LoadAssetAtPath<EnemyNPC>(OrcPrefabPath);
        }
        if (orcPrefab == null)
        {
            Debug.LogError($"[MonsterSystem] Could not load EnemyNPC on '{OrcPrefabPath}'.");
            return;
        }

        bool dirty = false;

        // Wire Camp 1 (index 0) -> Enemy_Orc.prefab; leave Camps 2, 3, 4 (indices 1..3) untouched!
        var so = new SerializedObject(spawner);
        SerializedProperty campPrefabsProp = so.FindProperty("campEnemyPrefabs");
        if (campPrefabsProp != null)
        {
            if (campPrefabsProp.arraySize < 4)
            {
                campPrefabsProp.arraySize = 4;
                dirty = true;
            }
            SerializedProperty slot0 = campPrefabsProp.GetArrayElementAtIndex(0);
            if (slot0.objectReferenceValue != orcPrefab)
            {
                slot0.objectReferenceValue = orcPrefab;
                dirty = true;
            }
            if (dirty)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(spawner);
            }
        }

        if (removeExistingManager)
        {
            MonsterSystemManager extraMgr = UnityEngine.Object.FindAnyObjectByType<MonsterSystemManager>(FindObjectsInactive.Include);
            if (extraMgr != null)
            {
                UnityEngine.Object.DestroyImmediate(extraMgr.gameObject);
                dirty = true;
            }
        }

        if (dirty)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(activeScene);
            if (saveScene && activeScene.IsValid() && !string.IsNullOrEmpty(activeScene.path))
                EditorSceneManager.SaveScene(activeScene);
            Debug.Log("[MonsterSystem] Assigned 'Enemy_Orc.prefab' to Camp 1 (West Starter) in SandboxWorldSpawner! Camps 2-4 & Boss remain untouched.");
        }
    }

    [MenuItem("Tools/ระบบมอนสเตอร์ (Monster System)/4. วางตัวคุมกลาง MonsterSystem_Manager ลงฉากนี้")]
    public static void PlaceManagerInActiveScene()
    {
        GameObject managerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ManagerPrefabPath);
        if (managerPrefab == null)
        {
            BuildOrcPackageAndVerify();
            managerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ManagerPrefabPath);
        }
        if (managerPrefab == null)
        {
            Debug.LogError($"[MonsterSystem] Could not load '{ManagerPrefabPath}'.");
            return;
        }

        MonsterSystemManager existing = UnityEngine.Object.FindAnyObjectByType<MonsterSystemManager>(FindObjectsInactive.Include);
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            Debug.Log("[MonsterSystem] 'MonsterSystem_Manager' is already present in the active scene.");
            return;
        }

        GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(managerPrefab);
        inst.name = "MonsterSystem_Manager";
        inst.transform.position = Vector3.zero;
        Undo.RegisterCreatedObjectUndo(inst, "Place MonsterSystem_Manager");
        Selection.activeGameObject = inst;
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[MonsterSystem] Placed 'MonsterSystem_Manager' in active scene. Expand it to move individual zones (e.g. Zone_Orc) anywhere on the map!");
    }

    [MenuItem("Tools/ระบบมอนสเตอร์ (Monster System)/5. วางโซนเดี่ยว Zone_Orc ลงฉากนี้")]
    public static void PlaceOrcZoneInActiveScene()
    {
        PlaceZoneInActiveScene("Orc");
    }

    public static void PlaceZoneInActiveScene(string monsterName)
    {
        if (string.IsNullOrWhiteSpace(monsterName)) return;
        string zonePath = $"{ZonePrefabsRoot}/Zone_{monsterName}.prefab";
        GameObject zonePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(zonePath);
        if (zonePrefab == null && monsterName.Equals("Orc", StringComparison.OrdinalIgnoreCase))
        {
            BuildOrcPackageAndVerify();
            zonePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(zonePath);
        }
        if (zonePrefab == null)
        {
            Debug.LogError($"[MonsterSystem] Could not load '{zonePath}'. Build the monster first.");
            return;
        }

        string targetName = $"Zone_{monsterName}";
        foreach (MonsterSpawnZone existing in UnityEngine.Object.FindObjectsByType<MonsterSpawnZone>(FindObjectsInactive.Include))
        {
            if (existing.gameObject.name == targetName)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.Log($"[MonsterSystem] '{targetName}' is already present in the active scene.");
                return;
            }
        }

        GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(zonePrefab);
        inst.name = targetName;

        // If a MonsterSystem_Manager exists in the scene, neatly parent the new zone under it!
        MonsterSystemManager mgr = UnityEngine.Object.FindAnyObjectByType<MonsterSystemManager>(FindObjectsInactive.Include);
        if (mgr != null)
        {
            inst.transform.SetParent(mgr.transform, worldPositionStays: true);
            mgr.RefreshChildZones();
            EditorUtility.SetDirty(mgr);
        }

        inst.transform.position = new Vector3(0f, 0f, -26f);
        Undo.RegisterCreatedObjectUndo(inst, $"Place {targetName}");
        Selection.activeGameObject = inst;
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[MonsterSystem] Placed '{targetName}' at (0, 0, -26) in active scene.");
    }

    [MenuItem("Tools/ระบบมอนสเตอร์ (Monster System)/6. ตรวจสอบความสมบูรณ์ของแพ็กเกจ")]
    public static void VerifyMonsterSystemPackageMenu()
    {
        VerifyMonsterSystemPackage();
    }

    // =========================================================================
    // BASE ANIMATOR CONTROLLER (AC_Monster_Base.controller)
    // =========================================================================

    public static AnimatorController BuildBaseAnimatorController()
    {
        EnsureFolder(AnimRoot);

        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(BaseControllerPath) != null)
            AssetDatabase.DeleteAsset(BaseControllerPath);

        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(BaseControllerPath);

        // 1. All 10 standard parameters
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("IsCombat", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("AttackIndex", AnimatorControllerParameterType.Int);
        controller.AddParameter("Skill", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("SkillIndex", AnimatorControllerParameterType.Int);
        controller.AddParameter("IsCasting", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("IsStunned", AnimatorControllerParameterType.Bool);
        controller.AddParameter("IsDead", AnimatorControllerParameterType.Bool);

        // 2. Embed 13 unique placeholder clips
        var clips = new Dictionary<string, AnimationClip>
        {
            { KeySpawn, CreatePlaceholderClip(controller, KeySpawn, 0.20f, loop: false) },
            { KeyIdle, CreatePlaceholderClip(controller, KeyIdle, 1.00f, loop: true) },
            { KeyWalk, CreatePlaceholderClip(controller, KeyWalk, 1.00f, loop: true) },
            { KeyRun, CreatePlaceholderClip(controller, KeyRun, 1.00f, loop: true) },
            { KeyCombatIdle, CreatePlaceholderClip(controller, KeyCombatIdle, 1.00f, loop: true) },
            { KeyAttack01, CreatePlaceholderClip(controller, KeyAttack01, 1.00f, loop: false) },
            { KeyAttack02, CreatePlaceholderClip(controller, KeyAttack02, 1.00f, loop: false) },
            { KeyAttack03, CreatePlaceholderClip(controller, KeyAttack03, 1.00f, loop: false) },
            { KeySkillCast, CreatePlaceholderClip(controller, KeySkillCast, 1.00f, loop: true) },
            { KeySkillRelease, CreatePlaceholderClip(controller, KeySkillRelease, 1.00f, loop: false) },
            { KeyHitReact, CreatePlaceholderClip(controller, KeyHitReact, 0.50f, loop: false) },
            { KeyStun, CreatePlaceholderClip(controller, KeyStun, 1.00f, loop: true) },
            { KeyDeath, CreatePlaceholderClip(controller, KeyDeath, 1.00f, loop: false) }
        };

        // 3. Base Layer State Machine
        AnimatorStateMachine rootSm = controller.layers[0].stateMachine;
        rootSm.entryPosition = new Vector3(-260f, 0f, 0f);
        rootSm.anyStatePosition = new Vector3(-260f, 220f, 0f);
        rootSm.exitPosition = new Vector3(720f, 220f, 0f);

        AnimatorState spawnState = rootSm.AddState("Spawn", new Vector3(-20f, 0f, 0f));
        spawnState.motion = clips[KeySpawn];
        rootSm.defaultState = spawnState;

        var locomotionTree = new BlendTree
        {
            name = "LocomotionBlendTree",
            blendType = BlendTreeType.Simple1D,
            blendParameter = "Speed",
            useAutomaticThresholds = false
        };
        AssetDatabase.AddObjectToAsset(locomotionTree, controller);
        locomotionTree.AddChild(clips[KeyIdle], 0.0f);
        locomotionTree.AddChild(clips[KeyWalk], 1.6f);
        locomotionTree.AddChild(clips[KeyRun], 4.2f);

        AnimatorState locomotionState = rootSm.AddState("Locomotion", new Vector3(240f, 0f, 0f));
        locomotionState.motion = locomotionTree;

        AnimatorState combatIdleState = rootSm.AddState("Combat_Idle", new Vector3(520f, 0f, 0f));
        combatIdleState.motion = clips[KeyCombatIdle];

        var tSpawnDone = spawnState.AddTransition(locomotionState);
        tSpawnDone.hasExitTime = true;
        tSpawnDone.exitTime = 0.85f;
        tSpawnDone.duration = 0.20f;

        var tSpawnMove = spawnState.AddTransition(locomotionState);
        tSpawnMove.hasExitTime = false;
        tSpawnMove.duration = 0.15f;
        tSpawnMove.AddCondition(AnimatorConditionMode.Greater, 0.10f, "Speed");

        var tSpawnCombat = spawnState.AddTransition(locomotionState);
        tSpawnCombat.hasExitTime = false;
        tSpawnCombat.duration = 0.15f;
        tSpawnCombat.AddCondition(AnimatorConditionMode.If, 0f, "IsCombat");

        // Locomotion <-> Combat_Idle with smooth crossfade & hysteresis
        var tLocoToCombat = locomotionState.AddTransition(combatIdleState);
        tLocoToCombat.hasExitTime = false;
        tLocoToCombat.duration = 0.22f;
        tLocoToCombat.AddCondition(AnimatorConditionMode.If, 0f, "IsCombat");
        tLocoToCombat.AddCondition(AnimatorConditionMode.Less, 0.12f, "Speed");

        var tCombatToLocoMove = combatIdleState.AddTransition(locomotionState);
        tCombatToLocoMove.hasExitTime = false;
        tCombatToLocoMove.duration = 0.20f;
        tCombatToLocoMove.AddCondition(AnimatorConditionMode.Greater, 0.35f, "Speed");

        var tCombatToLocoPeace = combatIdleState.AddTransition(locomotionState);
        tCombatToLocoPeace.hasExitTime = false;
        tCombatToLocoPeace.duration = 0.24f;
        tCombatToLocoPeace.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsCombat");

        // Sub-State Machine: Attacks (1.45x speed for snappy combat timing)
        AnimatorStateMachine attacksSm = rootSm.AddStateMachine("Attacks", new Vector3(380f, -180f, 0f));
        AnimatorState atk1 = attacksSm.AddState("Attack_01", new Vector3(260f, -80f, 0f));
        atk1.motion = clips[KeyAttack01];
        atk1.tag = "Attack";
        atk1.speed = 1.45f;

        AnimatorState atk2 = attacksSm.AddState("Attack_02", new Vector3(260f, 0f, 0f));
        atk2.motion = clips[KeyAttack02];
        atk2.tag = "Attack";
        atk2.speed = 1.45f;

        AnimatorState atk3 = attacksSm.AddState("Attack_03", new Vector3(260f, 80f, 0f));
        atk3.motion = clips[KeyAttack03];
        atk3.tag = "Attack";
        atk3.speed = 1.45f;

        attacksSm.defaultState = atk1;

        AddAttackEntryTransitions(locomotionState, atk1, atk2, atk3);
        AddAttackEntryTransitions(combatIdleState, atk1, atk2, atk3);
        AddReturnToIdleOrLocoTransitions(atk1, combatIdleState, locomotionState, 0.85f);
        AddReturnToIdleOrLocoTransitions(atk2, combatIdleState, locomotionState, 0.85f);
        AddReturnToIdleOrLocoTransitions(atk3, combatIdleState, locomotionState, 0.85f);

        // Sub-State Machine: Skills
        AnimatorStateMachine skillsSm = rootSm.AddStateMachine("Skills", new Vector3(640f, -180f, 0f));
        AnimatorState skillCast = skillsSm.AddState("Skill_Cast", new Vector3(240f, -40f, 0f));
        skillCast.motion = clips[KeySkillCast];
        skillCast.tag = "Skill";

        AnimatorState skillRelease = skillsSm.AddState("Skill_Release", new Vector3(500f, -40f, 0f));
        skillRelease.motion = clips[KeySkillRelease];
        skillRelease.tag = "Skill";
        skillRelease.speed = 1.35f;

        skillsSm.defaultState = skillCast;

        AddSkillEntryTransitions(locomotionState, skillCast, skillRelease);
        AddSkillEntryTransitions(combatIdleState, skillCast, skillRelease);

        var tCastToRelease = skillCast.AddTransition(skillRelease);
        tCastToRelease.hasExitTime = false;
        tCastToRelease.duration = 0.14f;
        tCastToRelease.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsCasting");

        AddReturnToIdleOrLocoTransitions(skillRelease, combatIdleState, locomotionState, 0.85f);

        // Any State -> Hit_React, Stun, Death (canTransitionToSelf = false on ALL; Death has ZERO exits!)
        AnimatorState hitReactState = rootSm.AddState("Hit_React", new Vector3(120f, 180f, 0f));
        hitReactState.motion = clips[KeyHitReact];
        hitReactState.tag = "Hit";
        hitReactState.speed = 1.35f;

        var tAnyToHit = rootSm.AddAnyStateTransition(hitReactState);
        tAnyToHit.hasExitTime = false;
        tAnyToHit.duration = 0.08f;
        tAnyToHit.canTransitionToSelf = false;
        tAnyToHit.AddCondition(AnimatorConditionMode.If, 0f, "Hit");
        tAnyToHit.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsDead");

        AddReturnToIdleOrLocoTransitions(hitReactState, combatIdleState, locomotionState, 0.82f);

        AnimatorState stunState = rootSm.AddState("Stun", new Vector3(120f, 270f, 0f));
        stunState.motion = clips[KeyStun];
        stunState.tag = "Stun";

        var tAnyToStun = rootSm.AddAnyStateTransition(stunState);
        tAnyToStun.hasExitTime = false;
        tAnyToStun.duration = 0.08f;
        tAnyToStun.canTransitionToSelf = false;
        tAnyToStun.AddCondition(AnimatorConditionMode.If, 0f, "IsStunned");
        tAnyToStun.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsDead");

        var tStunExit = stunState.AddTransition(locomotionState);
        tStunExit.hasExitTime = false;
        tStunExit.duration = 0.18f;
        tStunExit.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsStunned");

        AnimatorState deathState = rootSm.AddState("Death", new Vector3(120f, 360f, 0f));
        deathState.motion = clips[KeyDeath];
        deathState.tag = "Dead";

        var tAnyToDeath = rootSm.AddAnyStateTransition(deathState);
        tAnyToDeath.hasExitTime = false;
        tAnyToDeath.duration = 0.08f;
        tAnyToDeath.canTransitionToSelf = false;
        tAnyToDeath.AddCondition(AnimatorConditionMode.If, 0f, "IsDead");

        // 4. UpperBody Layer (Override, default weight 0)
        var upperSm = new AnimatorStateMachine
        {
            name = "UpperBody",
            hideFlags = HideFlags.HideInHierarchy
        };
        AssetDatabase.AddObjectToAsset(upperSm, controller);
        AnimatorState upperEmpty = upperSm.AddState("UpperBody_Empty", new Vector3(240f, 0f, 0f));
        upperSm.defaultState = upperEmpty;

        controller.AddLayer(new AnimatorControllerLayer
        {
            name = "UpperBody",
            stateMachine = upperSm,
            blendingMode = AnimatorLayerBlendingMode.Override,
            defaultWeight = 0f,
            syncedLayerIndex = -1
        });

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    private static AnimationClip CreatePlaceholderClip(AnimatorController controller, string clipName, float lengthSeconds, bool loop)
    {
        var clip = new AnimationClip { name = clipName, frameRate = 30f };
        var curve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(lengthSeconds, 1f));
        clip.SetCurve(string.Empty, typeof(Transform), "m_LocalScale.x", curve);

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        AssetDatabase.AddObjectToAsset(clip, controller);
        return clip;
    }

    private static void AddAttackEntryTransitions(AnimatorState source, AnimatorState atk1, AnimatorState atk2, AnimatorState atk3)
    {
        var t1 = source.AddTransition(atk1);
        t1.hasExitTime = false;
        t1.duration = 0.14f;
        t1.AddCondition(AnimatorConditionMode.If, 0f, "Attack");
        t1.AddCondition(AnimatorConditionMode.Equals, 0, "AttackIndex");

        var t2 = source.AddTransition(atk2);
        t2.hasExitTime = false;
        t2.duration = 0.14f;
        t2.AddCondition(AnimatorConditionMode.If, 0f, "Attack");
        t2.AddCondition(AnimatorConditionMode.Equals, 1, "AttackIndex");

        var t3 = source.AddTransition(atk3);
        t3.hasExitTime = false;
        t3.duration = 0.14f;
        t3.AddCondition(AnimatorConditionMode.If, 0f, "Attack");
        t3.AddCondition(AnimatorConditionMode.Equals, 2, "AttackIndex");
    }

    private static void AddSkillEntryTransitions(AnimatorState source, AnimatorState skillCast, AnimatorState skillRelease)
    {
        var tCast = source.AddTransition(skillCast);
        tCast.hasExitTime = false;
        tCast.duration = 0.15f;
        tCast.AddCondition(AnimatorConditionMode.If, 0f, "Skill");
        tCast.AddCondition(AnimatorConditionMode.If, 0f, "IsCasting");

        var tRelease = source.AddTransition(skillRelease);
        tRelease.hasExitTime = false;
        tRelease.duration = 0.15f;
        tRelease.AddCondition(AnimatorConditionMode.If, 0f, "Skill");
        tRelease.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsCasting");
    }

    private static void AddReturnToIdleOrLocoTransitions(AnimatorState source, AnimatorState combatIdle, AnimatorState locomotion, float exitTime)
    {
        var tToCombat = source.AddTransition(combatIdle);
        tToCombat.hasExitTime = true;
        tToCombat.exitTime = exitTime;
        tToCombat.duration = 0.20f;
        tToCombat.AddCondition(AnimatorConditionMode.If, 0f, "IsCombat");
        tToCombat.AddCondition(AnimatorConditionMode.Less, 0.20f, "Speed");

        var tToLoco = source.AddTransition(locomotion);
        tToLoco.hasExitTime = true;
        tToLoco.exitTime = exitTime;
        tToLoco.duration = 0.20f;
    }

    // =========================================================================
    // PER-MONSTER BUILDER (AOC, CONFIG, URP MATERIALS, STANDALONE PREFAB)
    // =========================================================================

    public static void BuildMonsterFromFolder(
        string sourceFolder,
        string monsterName,
        bool isBoss,
        float targetWorldHeight = 2.15f)
    {
        if (!Directory.Exists(sourceFolder))
        {
            Debug.LogError($"[MonsterSystem] Source folder not found: '{sourceFolder}'.");
            return;
        }

        ForceReimportMonsterFolder(sourceFolder);

        AnimatorController baseController = AssetDatabase.LoadAssetAtPath<AnimatorController>(BaseControllerPath) ?? BuildBaseAnimatorController();

        GameObject skinnedModelFbx = FindSkinnedModelFbx(sourceFolder);
        if (skinnedModelFbx == null)
        {
            Debug.LogError($"[MonsterSystem] No rigged/skinned FBX found in '{sourceFolder}'.");
            return;
        }

        Dictionary<string, AnimationClip> discoveredClips = DiscoverMonsterClips(sourceFolder, out int uniqueAttackCount);
        if (!discoveredClips.TryGetValue("idle", out AnimationClip idleClip) || idleClip == null)
        {
            Debug.LogError($"[MonsterSystem] Idle clip missing in '{sourceFolder}'.");
            return;
        }

        // 1. AOC_<MonsterName>.overrideController
        string monsterAnimDir = $"{AnimRoot}/{monsterName}";
        EnsureFolder(monsterAnimDir);
        string aocPath = $"{monsterAnimDir}/AOC_{monsterName}.overrideController";

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

        AnimationClip walkClip = GetClipOrFallback(discoveredClips, "walk", idleClip);
        AnimationClip runClip = GetClipOrFallback(discoveredClips, "run", walkClip);
        AnimationClip combatIdleClip = GetClipOrFallback(discoveredClips, "combat_idle", idleClip);
        AnimationClip atk1Clip = GetClipOrFallback(discoveredClips, "attack_01", idleClip);
        AnimationClip atk2Clip = GetClipOrFallback(discoveredClips, "attack_02", atk1Clip);
        AnimationClip atk3Clip = GetClipOrFallback(discoveredClips, "attack_03", atk1Clip);
        AnimationClip skillCastClip = GetClipOrFallback(discoveredClips, "skill_cast", combatIdleClip);
        AnimationClip skillReleaseClip = GetClipOrFallback(discoveredClips, "skill_release", atk2Clip);
        AnimationClip hitReactClip = GetClipOrFallback(discoveredClips, "hit_react", idleClip);
        AnimationClip stunClip = GetClipOrFallback(discoveredClips, "stun", idleClip);
        AnimationClip deathClip = GetClipOrFallback(discoveredClips, "death", idleClip);
        AnimationClip spawnClip = GetClipOrFallback(discoveredClips, "spawn", idleClip);

        var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        aoc.GetOverrides(overrides);
        for (int i = 0; i < overrides.Count; i++)
        {
            AnimationClip key = overrides[i].Key;
            if (key == null) continue;
            AnimationClip mapped = key.name switch
            {
                KeySpawn => spawnClip,
                KeyIdle => idleClip,
                KeyWalk => walkClip,
                KeyRun => runClip,
                KeyCombatIdle => combatIdleClip,
                KeyAttack01 => atk1Clip,
                KeyAttack02 => atk2Clip,
                KeyAttack03 => atk3Clip,
                KeySkillCast => skillCastClip,
                KeySkillRelease => skillReleaseClip,
                KeyHitReact => hitReactClip,
                KeyStun => stunClip,
                KeyDeath => deathClip,
                _ => idleClip
            };
            overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(key, mapped);
        }
        aoc.ApplyOverrides(overrides);
        EditorUtility.SetDirty(aoc);

        // 2. Config_<MonsterName>.asset (Preserve user's Loot Table, Stat Multipliers, Audio & Height on rebuild!)
        string configPath = $"{monsterAnimDir}/Config_{monsterName}.asset";
        MonsterAnimationConfig config = AssetDatabase.LoadAssetAtPath<MonsterAnimationConfig>(configPath);
        bool isNewConfig = config == null;
        if (isNewConfig)
        {
            config = ScriptableObject.CreateInstance<MonsterAnimationConfig>();
            config.monsterName = monsterName;
            config.isBoss = isBoss;
            config.canFlinch = !isBoss;
            config.targetWorldHeight = targetWorldHeight;
            config.attackRange = Mathf.Clamp(targetWorldHeight * 1.15f, 1.8f, 3.8f);
            if (atk1Clip != null && atk1Clip.length > 0.1f)
            {
                // Estimate melee impact timing (~38% of attack_01 clip length adjusted for 1.45x Animator speed)
                float estImpact = Mathf.Clamp((atk1Clip.length / 1.45f) * 0.38f, 0.22f, 0.65f);
                config.attackImpactDelay = estImpact;
                config.counterImpactDelay = Mathf.Max(0.20f, estImpact - 0.04f);
            }
            AssetDatabase.CreateAsset(config, configPath);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(config.monsterName))
                config.monsterName = monsterName;
        }
        config.attackVariantCount = Mathf.Clamp(uniqueAttackCount, 1, 3);
        EditorUtility.SetDirty(config);

        // Use config's tuned targetWorldHeight so Inspector height edits take effect on rebuild!
        float effectiveWorldHeight = Mathf.Max(0.5f, config.targetWorldHeight);

        // 3. URP PBR Materials
        Dictionary<string, Material> urpMaterials = BuildUrpMaterialsForMonster(sourceFolder, monsterName);

        // 4. Standalone Monster Prefab (Enemy_<MonsterName>.prefab)
        EnsureFolder(MonsterPrefabsRoot);
        string prefabPath = $"{MonsterPrefabsRoot}/Enemy_{monsterName}.prefab";
        BuildStandaloneMonsterPrefab(
            outputPrefabPath: prefabPath,
            monsterName: monsterName,
            skinnedModelFbx: skinnedModelFbx,
            overrideController: aoc,
            config: config,
            urpMaterials: urpMaterials,
            targetWorldHeight: effectiveWorldHeight);
    }

    private static void BuildStandaloneMonsterPrefab(
        string outputPrefabPath,
        string monsterName,
        GameObject skinnedModelFbx,
        AnimatorOverrideController overrideController,
        MonsterAnimationConfig config,
        Dictionary<string, Material> urpMaterials,
        float targetWorldHeight)
    {
        // Copy read-only from friend's Enemy.prefab if available (to inherit LootTable / NetworkObject / CapsuleCollider / Rigidbody)
        if (File.Exists(outputPrefabPath))
            AssetDatabase.DeleteAsset(outputPrefabPath);

        if (File.Exists(FriendEnemyTemplatePath))
        {
            AssetDatabase.CopyAsset(FriendEnemyTemplatePath, outputPrefabPath);
            AssetDatabase.Refresh();
        }
        else
        {
            GameObject fresh = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            fresh.name = $"Enemy_{monsterName}";
            fresh.transform.localScale = new Vector3(1.2f, 1.2f, 1.2f);
            var rb = fresh.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.isKinematic = true;
            fresh.AddComponent<NetworkObject>();
            fresh.AddComponent<NetworkTransform>();
            PrefabUtility.SaveAsPrefabAsset(fresh, outputPrefabPath);
            UnityEngine.Object.DestroyImmediate(fresh);
        }

        GameObject root = PrefabUtility.LoadPrefabContents(outputPrefabPath);
        try
        {
            root.name = $"Enemy_{monsterName}";

            // Remove legacy capsule visual (KEEP CapsuleCollider, Rigidbody, NetworkObject, NetworkTransform!)
            // MonsterEnemyNPC cleanly disconnects base EnemyNPC's animator/bodyRenderer after Awake() -> zero EnemyMat.mat dependency!
            MeshRenderer rootCapsuleRenderer = root.GetComponent<MeshRenderer>();
            if (rootCapsuleRenderer != null)
                UnityEngine.Object.DestroyImmediate(rootCapsuleRenderer);
            MeshFilter rootCapsuleFilter = root.GetComponent<MeshFilter>();
            if (rootCapsuleFilter != null)
                UnityEngine.Object.DestroyImmediate(rootCapsuleFilter);

            // Preserve lootPrefab & lootTable from base EnemyNPC before swapping component to MonsterEnemyNPC
            UnityEngine.Object savedLootPrefab = null;
            int savedMaxHp = 120;
            int savedAtkDmg = 10;
            float savedAtkCd = 2.0f;
            float savedAtkRange = 2.5f;
            int savedExp = 50;

            EnemyNPC oldNpc = root.GetComponent<EnemyNPC>();
            if (oldNpc != null && !(oldNpc is MonsterEnemyNPC))
            {
                var oldSo = new SerializedObject(oldNpc);
                savedLootPrefab = oldSo.FindProperty("lootPrefab")?.objectReferenceValue;
                savedMaxHp = oldNpc.MaxHealth;
                savedAtkDmg = oldNpc.AttackDamage;
                savedAtkCd = oldNpc.AttackCooldown;
                savedAtkRange = oldNpc.AttackRange;
                savedExp = oldNpc.ExperienceReward;
                UnityEngine.Object.DestroyImmediate(oldNpc);
            }

            // Instantiate rigged 3D visual child
            string visualChildName = $"{monsterName}Visual";
            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = root.transform.GetChild(i);
                if (child.name == visualChildName || child.name.EndsWith("Visual", StringComparison.Ordinal))
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
            }

            GameObject visualGo = (GameObject)PrefabUtility.InstantiatePrefab(skinnedModelFbx, root.transform);
            visualGo.name = visualChildName;
            visualGo.transform.localPosition = Vector3.zero;
            visualGo.transform.localRotation = Quaternion.identity;
            visualGo.transform.localScale = Vector3.one;

            foreach (Collider col in visualGo.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(col);

            // Apply URP PBR materials (and auto-convert any Built-in/Standard/null materials to URP Lit so NO model ever turns pink!)
            Shader urpLitShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material fallbackUrpMat = null;
            Renderer[] visualRenderers = visualGo.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < visualRenderers.Length; i++)
            {
                Renderer r = visualRenderers[i];
                Material[] shared = r.sharedMaterials;
                bool changed = false;
                for (int m = 0; m < shared.Length; m++)
                {
                    Material cur = shared[m];
                    string matName = cur != null ? cur.name : string.Empty;
                    Material matched = MatchMaterial(urpMaterials, matName, r.name);
                    if (matched != null)
                    {
                        shared[m] = matched;
                        changed = true;
                    }
                    else if (cur == null || cur.shader == null || !cur.shader.name.Contains("Universal Render Pipeline"))
                    {
                        Material converted = ConvertEmbeddedOrCreateFallbackUrpMaterial(cur, monsterName, urpLitShader, ref fallbackUrpMat);
                        if (converted != null)
                        {
                            shared[m] = converted;
                            changed = true;
                        }
                    }
                }
                if (changed) r.sharedMaterials = shared;
            }

            // Dynamically scale CapsuleCollider to match targetWorldHeight cleanly (preserves Orc's exact 2.0/0.5 @ 1.2 scale)
            CapsuleCollider rootCapsule = root.GetComponent<CapsuleCollider>();
            float rootScaleY = Mathf.Max(0.01f, root.transform.localScale.y);
            if (rootCapsule != null)
            {
                float desiredColliderWorldHeight = Mathf.Max(1.0f, targetWorldHeight * 1.11f);
                rootCapsule.height = Mathf.Max(1.0f, desiredColliderWorldHeight / rootScaleY);
                rootCapsule.radius = Mathf.Clamp(rootCapsule.height * 0.25f, 0.35f, 0.95f);
                rootCapsule.center = Vector3.zero;
            }
            float capsuleBottomLocalY = rootCapsule != null ? (rootCapsule.center.y - rootCapsule.height * 0.5f) : -1.0f;

            AnimationClip idleClip = overrideController != null ? overrideController[KeyIdle] : null;
            MeasureSkeletonFeetAndHeight(skinnedModelFbx, idleClip, out float feetLocalY, out float rawHeight);

            float desiredLocalHeight = targetWorldHeight / rootScaleY;
            float uniformLocalScale = desiredLocalHeight / Mathf.Max(0.25f, rawHeight);

            visualGo.transform.localScale = Vector3.one * uniformLocalScale;
            float feetOffsetLocalY = capsuleBottomLocalY - (feetLocalY * uniformLocalScale);
            visualGo.transform.localPosition = new Vector3(0f, feetOffsetLocalY, 0f);

            // Configure Animator
            Animator anim = visualGo.GetComponent<Animator>();
            if (anim == null) anim = visualGo.AddComponent<Animator>();
            anim.runtimeAnimatorController = overrideController;
            anim.avatar = null;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // Attach & wire MonsterAnimatorBridge
            MonsterAnimatorBridge bridge = root.GetComponent<MonsterAnimatorBridge>();
            if (bridge == null) bridge = root.AddComponent<MonsterAnimatorBridge>();
            bridge.BindReferences(anim, config, visualGo.transform, visualRenderers);

            // Attach & wire MonsterEnemyNPC (inherits EnemyNPC -> zero edits to friend's EnemyNPC.cs!)
            MonsterEnemyNPC monsterNpc = root.GetComponent<MonsterEnemyNPC>();
            if (monsterNpc == null) monsterNpc = root.AddComponent<MonsterEnemyNPC>();

            var npcSo = new SerializedObject(monsterNpc);
            SetObjProp(npcSo, "animatorBridge", bridge);
            // Leave base EnemyNPC.animator & bodyRenderer NULL (MonsterEnemyNPC also clears them after EnemyNPC.Awake())
            SetObjProp(npcSo, "animator", null);
            SetObjProp(npcSo, "bodyRenderer", null);
            // TMRKKxOG Hybrid flags: rigged model UI alignment & disable base NavMeshAgent warp on center-pivoted prefab
            SetBoolProp(npcSo, "hasRiggedModel", true);
            SetFloatProp(npcSo, "riggedLockPointHeight", 0.45f);
            SetBoolProp(npcSo, "useNavMeshWhenAvailable", false);
            if (savedLootPrefab != null) SetObjProp(npcSo, "lootPrefab", savedLootPrefab);
            SetIntProp(npcSo, "MaxHealth", savedMaxHp);
            SetIntProp(npcSo, "AttackDamage", savedAtkDmg);
            SetFloatProp(npcSo, "AttackCooldown", savedAtkCd);
            SetFloatProp(npcSo, "AttackRange", savedAtkRange);
            SetIntProp(npcSo, "experienceReward", savedExp);
            npcSo.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, outputPrefabPath);
            Debug.Log($"[MonsterSystem] Saved '{outputPrefabPath}' (boneFeetY={feetLocalY:0.000}, rawH={rawHeight:0.00}m, localScale={uniformLocalScale:0.000}, localY={feetOffsetLocalY:0.000}).");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        // Force reimport so Photon Fusion's NetworkObjectPostprocessor updates NetworkedBehaviours & Prefab Table
        AssetDatabase.ImportAsset(outputPrefabPath, ImportAssetOptions.ForceUpdate);
    }

    private static void BuildZonePrefab(
        string monsterName,
        string monsterPrefabPath,
        string zoneOutputPath,
        int spawnCount,
        float slotSpacing,
        float territoryRadius,
        float respawnMin,
        float respawnMax)
    {
        EnsureFolder(ZonePrefabsRoot);
        GameObject monsterGo = AssetDatabase.LoadAssetAtPath<GameObject>(monsterPrefabPath);
        EnemyNPC monsterComp = monsterGo != null ? monsterGo.GetComponent<EnemyNPC>() : null;
        if (monsterComp == null)
        {
            Debug.LogError($"[MonsterSystem] Cannot build Zone prefab: EnemyNPC/MonsterEnemyNPC missing on '{monsterPrefabPath}'.");
            return;
        }

        var go = new GameObject($"Zone_{monsterName}");
        try
        {
            var zone = go.AddComponent<MonsterSpawnZone>();
            var so = new SerializedObject(zone);
            SetObjProp(so, "monsterPrefab", monsterComp);
            SetIntProp(so, "spawnCount", spawnCount);
            SetFloatProp(so, "slotSpacing", slotSpacing);
            SetFloatProp(so, "territoryRadius", territoryRadius);
            SetIntProp(so, "respawnMode", (int)MonsterZoneRespawnMode.IndividualStaggered);
            SetFloatProp(so, "respawnMinSeconds", respawnMin);
            SetFloatProp(so, "respawnMaxSeconds", respawnMax);
            SetFloatProp(so, "antiPopInPlayerRadius", 6.0f);
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(go, zoneOutputPath);
            Debug.Log($"[MonsterSystem] Saved reusable zone prefab '{zoneOutputPath}' (monster={monsterComp.name}, count={spawnCount}, respawn={respawnMin}-{respawnMax}s).");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    /// <summary>
    /// Builds/updates the single-object plug-and-play <c>MonsterSystem_Manager.prefab</c> containing
    /// <see cref="MonsterSystemManager"/> and nested child references to all built monster zones.
    /// </summary>
    public static void BuildManagerPrefab()
    {
        EnsureFolder(SystemRoot + "/Prefabs");
        WorldItem worldItemPrefab = AssetDatabase.LoadAssetAtPath<WorldItem>(FriendWorldItemPath);

        GameObject root;
        bool existed = File.Exists(ManagerPrefabPath);
        if (existed)
        {
            root = PrefabUtility.LoadPrefabContents(ManagerPrefabPath);
        }
        else
        {
            root = new GameObject("MonsterSystem_Manager");
        }

        try
        {
            root.name = "MonsterSystem_Manager";
            MonsterSystemManager mgr = root.GetComponent<MonsterSystemManager>();
            if (mgr == null) mgr = root.AddComponent<MonsterSystemManager>();
            if (mgr.sharedWorldItemPrefab == null && worldItemPrefab != null)
                mgr.sharedWorldItemPrefab = worldItemPrefab;

            // Ensure every built Zone_*.prefab has a child instance inside the Manager (preserving existing positions!)
            if (Directory.Exists(ZonePrefabsRoot))
            {
                string[] zoneGuids = AssetDatabase.FindAssets("t:Prefab", new[] { ZonePrefabsRoot });
                int addedIdx = 0;
                for (int i = 0; i < zoneGuids.Length; i++)
                {
                    string zPath = AssetDatabase.GUIDToAssetPath(zoneGuids[i]);
                    GameObject zPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(zPath);
                    if (zPrefab == null || zPrefab.GetComponent<MonsterSpawnZone>() == null) continue;

                    string zName = zPrefab.name;
                    Transform existingChild = root.transform.Find(zName);
                    if (existingChild == null)
                    {
                        GameObject zInst = (GameObject)PrefabUtility.InstantiatePrefab(zPrefab, root.transform);
                        zInst.name = zName;
                        // Align Zone_Orc onto TMRKKxOG Starter Line (0, 0, -22) straight ahead from SafeZone (0, 0, -60) between Camp 1 (-46) & Camp 2 (+46)
                        zInst.transform.localPosition = new Vector3(addedIdx * 34f, 0f, -22f);
                        addedIdx++;
                    }
                    else if (zName == "Zone_Orc" && Mathf.Approximately(existingChild.localPosition.x, 0f) && Mathf.Approximately(existingChild.localPosition.z, -26f))
                    {
                        existingChild.localPosition = new Vector3(0f, 0f, -22f);
                    }
                }
            }

            mgr.RefreshChildZones();
            EditorUtility.SetDirty(mgr);
            PrefabUtility.SaveAsPrefabAsset(root, ManagerPrefabPath);
            Debug.Log($"[MonsterSystem] Saved scene hub prefab '{ManagerPrefabPath}' ({mgr.managedZones.Count} zone(s) managed).");
        }
        finally
        {
            if (existed)
                PrefabUtility.UnloadPrefabContents(root);
            else
                UnityEngine.Object.DestroyImmediate(root);
        }
    }

    // =========================================================================
    // HELPERS: FBX DISCOVERY, PBR MATERIALS, SKELETON BOUNDS
    // =========================================================================

    private static void ForceReimportMonsterFolder(string folder)
    {
        string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { folder });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (string.IsNullOrEmpty(path)) continue;
            if (path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
        }
    }

    private static GameObject FindSkinnedModelFbx(string folder)
    {
        string[] guids = AssetDatabase.FindAssets("t:Model", new[] { folder });
        GameObject fallback = null;
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (!path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) continue;
            GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) continue;
            var skinned = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (skinned != null && skinned.Length > 0)
            {
                if (!Path.GetFileNameWithoutExtension(path).Contains("@")) return go;
                if (fallback == null) fallback = go;
            }
        }
        return fallback;
    }

    /// <summary>
    /// Universal clip discovery: supports Mixamo '@'-split FBX files, non-'@' split FBX files (e.g. Goblin_Idle.fbx),
    /// AND single multi-clip FBX files with all animations embedded inside!
    /// </summary>
    private static Dictionary<string, AnimationClip> DiscoverMonsterClips(string folder, out int uniqueAttackCount)
    {
        var result = new Dictionary<string, AnimationClip>(StringComparer.OrdinalIgnoreCase);
        var attackClips = new List<AnimationClip>();
        AnimationClip firstValidClip = null;

        string[] guids = AssetDatabase.FindAssets("t:Model", new[] { folder });
        Array.Sort(guids, StringComparer.Ordinal);

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (!path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) continue;
            string fileName = Path.GetFileNameWithoutExtension(path);

            UnityEngine.Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(path);
            for (int j = 0; j < subAssets.Length; j++)
            {
                if (!(subAssets[j] is AnimationClip clip)) continue;
                if (clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase)) continue;
                if (clip.length <= 0.01f) continue;

                if (firstValidClip == null) firstValidClip = clip;

                // Build clean matching key from either '@suffix', clip.name (stripping 'Armature|'), or filename
                string keySource;
                if (fileName.Contains("@"))
                {
                    keySource = fileName.Substring(fileName.IndexOf('@') + 1);
                }
                else
                {
                    string cn = clip.name ?? string.Empty;
                    int pipeIdx = cn.LastIndexOf('|');
                    if (pipeIdx >= 0 && pipeIdx + 1 < cn.Length) cn = cn.Substring(pipeIdx + 1);
                    bool genericClipName = string.IsNullOrWhiteSpace(cn) ||
                                           cn.Equals("mixamo.com", StringComparison.OrdinalIgnoreCase) ||
                                           cn.StartsWith("Take 00", StringComparison.OrdinalIgnoreCase);
                    keySource = genericClipName ? fileName : cn;
                }

                string suffix = keySource.Trim().ToLowerInvariant().Replace(" ", "_").Replace("-", "_");

                if (suffix.Contains("combat_idle") || suffix.Contains("combatidle") || suffix.Contains("block_idle") || suffix.Contains("guard"))
                    result["combat_idle"] = clip;
                else if (suffix.Contains("idle") || suffix.Contains("stand") || suffix.Contains("breathe"))
                {
                    if (!result.ContainsKey("idle")) result["idle"] = clip;
                }
                else if (suffix.Contains("walk"))
                    result["walk"] = clip;
                else if (suffix.Contains("run") || suffix.Contains("sprint") || suffix.Contains("chase"))
                    result["run"] = clip;
                else if (suffix.Contains("attack_01") || suffix.Contains("attack01") || suffix.Contains("attack_1") || suffix == "attack1")
                {
                    result["attack_01"] = clip;
                    if (!attackClips.Contains(clip)) attackClips.Add(clip);
                }
                else if (suffix.Contains("attack_02") || suffix.Contains("attack02") || suffix.Contains("attack_2") || suffix == "attack2")
                {
                    result["attack_02"] = clip;
                    if (!attackClips.Contains(clip)) attackClips.Add(clip);
                }
                else if (suffix.Contains("attack_03") || suffix.Contains("attack03") || suffix.Contains("attack_3") || suffix == "attack3")
                {
                    result["attack_03"] = clip;
                    if (!attackClips.Contains(clip)) attackClips.Add(clip);
                }
                else if (suffix.Contains("attack") || suffix.Contains("slash") || suffix.Contains("bite") || suffix.Contains("claw") || suffix.Contains("punch") || suffix.Contains("swing"))
                {
                    if (!result.ContainsKey("attack_01")) result["attack_01"] = clip;
                    else if (!result.ContainsKey("attack_02")) result["attack_02"] = clip;
                    else if (!result.ContainsKey("attack_03")) result["attack_03"] = clip;
                    if (!attackClips.Contains(clip)) attackClips.Add(clip);
                }
                else if (suffix.Contains("skill_cast") || suffix.Contains("skillcast") || suffix.Contains("cast"))
                    result["skill_cast"] = clip;
                else if (suffix.Contains("skill_release") || suffix.Contains("skillrelease") || suffix.Contains("roar") || suffix.Contains("skill"))
                    result["skill_release"] = clip;
                else if (suffix.Contains("hit_react") || suffix.Contains("hitreact") || suffix.Contains("hit") || suffix.Contains("damage") || suffix.Contains("hurt") || suffix.Contains("flinch"))
                    result["hit_react"] = clip;
                else if (suffix.Contains("stun") || suffix.Contains("dizzy"))
                    result["stun"] = clip;
                else if (suffix.Contains("death") || suffix.Contains("die") || suffix.Contains("dead"))
                    result["death"] = clip;
                else if (suffix.Contains("spawn") || suffix.Contains("appear"))
                    result["spawn"] = clip;
            }
        }

        // Guarantee an Idle fallback if the model only had non-standard clip names
        if (!result.ContainsKey("idle") && firstValidClip != null)
            result["idle"] = firstValidClip;

        uniqueAttackCount = Mathf.Max(1, attackClips.Count);
        return result;
    }

    private static AnimationClip GetClipOrFallback(Dictionary<string, AnimationClip> map, string key, AnimationClip fallback)
    {
        return map.TryGetValue(key, out AnimationClip clip) && clip != null ? clip : fallback;
    }

    private static Dictionary<string, Material> BuildUrpMaterialsForMonster(string sourceFolder, string monsterName)
    {
        var materials = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        string matDir = $"{MaterialsRoot}/{monsterName}";
        EnsureFolder(matDir);

        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        string[] texGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { sourceFolder });
        var partTextures = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < texGuids.Length; i++)
        {
            string texPath = AssetDatabase.GUIDToAssetPath(texGuids[i]);
            string baseName = Path.GetFileNameWithoutExtension(texPath);
            string[] tokens = baseName.Split('_');
            string partName;
            string channel;

            if (tokens.Length >= 3)
            {
                partName = tokens[1];
                channel = tokens[tokens.Length - 1].ToLowerInvariant();
            }
            else if (tokens.Length == 2)
            {
                partName = "Main";
                channel = tokens[1].ToLowerInvariant();
            }
            else
            {
                partName = "Main";
                string lower = baseName.ToLowerInvariant();
                if (lower.Contains("normal") || lower.Contains("bump")) channel = "normal";
                else if (lower.Contains("rough")) channel = "roughness";
                else if (lower.Contains("metal")) channel = "metallic";
                else if (lower.Contains("ao") || lower.Contains("occlusion")) channel = "ao";
                else channel = "basecolor";
            }

            if (!partTextures.TryGetValue(partName, out var channels))
            {
                channels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                partTextures[partName] = channels;
            }
            channels[channel] = texPath;
        }

        foreach (var kv in partTextures)
        {
            string partName = kv.Key;
            var ch = kv.Value;

            string matPath = $"{matDir}/Mat_{monsterName}_{partName}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(urpLit);
                AssetDatabase.CreateAsset(mat, matPath);
            }
            else
            {
                mat.shader = urpLit;
            }

            mat.SetColor("_BaseColor", Color.white);

            if (TryGetChannelTexture(ch, out Texture2D baseTex, "basecolor", "color", "albedo", "diffuse"))
                mat.SetTexture("_BaseMap", baseTex);

            if (TryGetChannelTexture(ch, out Texture2D normalTex, "normal", "bump"))
            {
                mat.SetTexture("_BumpMap", normalTex);
                mat.SetFloat("_BumpScale", 1.0f);
                mat.EnableKeyword("_NORMALMAP");
            }

            if (TryGetChannelTexture(ch, out Texture2D aoTex, "ao", "occlusion"))
            {
                mat.SetTexture("_OcclusionMap", aoTex);
                mat.SetFloat("_OcclusionStrength", 1.0f);
            }

            ch.TryGetValue("metalness", out string metalPath);
            if (string.IsNullOrEmpty(metalPath)) ch.TryGetValue("metallic", out metalPath);
            ch.TryGetValue("roughness", out string roughPath);

            if (!string.IsNullOrEmpty(metalPath) || !string.IsNullOrEmpty(roughPath))
            {
                string packedPath = $"{matDir}/T_{monsterName}_{partName}_MetSmooth.png";
                Texture2D packedTex = BuildMetallicSmoothnessTexture(metalPath, roughPath, packedPath);
                if (packedTex != null)
                {
                    mat.SetTexture("_MetallicGlossMap", packedTex);
                    mat.SetFloat("_Metallic", 1.0f);
                    mat.SetFloat("_Smoothness", 1.0f);
                    mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                }
            }
            else
            {
                mat.SetFloat("_Metallic", 0.05f);
                mat.SetFloat("_Smoothness", 0.35f);
            }

            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", Color.black);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            EditorUtility.SetDirty(mat);
            materials[partName] = mat;
        }

        AssetDatabase.SaveAssets();
        return materials;
    }

    private static bool TryGetChannelTexture(Dictionary<string, string> channels, out Texture2D tex, params string[] keys)
    {
        tex = null;
        for (int i = 0; i < keys.Length; i++)
        {
            if (channels.TryGetValue(keys[i], out string path) && !string.IsNullOrEmpty(path))
            {
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex != null) return true;
            }
        }
        return false;
    }

    private static Texture2D BuildMetallicSmoothnessTexture(string metalPath, string roughPath, string outputAssetPath)
    {
        Texture2D metalSrc = LoadReadableTexture(metalPath);
        Texture2D roughSrc = LoadReadableTexture(roughPath);
        if (metalSrc == null && roughSrc == null) return null;

        int w = Mathf.Min(roughSrc != null ? roughSrc.width : metalSrc.width, 1024);
        int h = Mathf.Min(roughSrc != null ? roughSrc.height : metalSrc.height, 1024);

        var outTex = new Texture2D(w, h, TextureFormat.RGBA32, false, linear: true);
        Color32[] pixels = new Color32[w * h];

        for (int y = 0; y < h; y++)
        {
            float v = (y + 0.5f) / h;
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w;
                float m = metalSrc != null ? metalSrc.GetPixelBilinear(u, v).r : 0f;
                float r = roughSrc != null ? roughSrc.GetPixelBilinear(u, v).r : 0.65f;
                byte mb = (byte)Mathf.RoundToInt(Mathf.Clamp01(m) * 255f);
                byte sb = (byte)Mathf.RoundToInt(Mathf.Clamp01(1.0f - r) * 255f);
                pixels[y * w + x] = new Color32(mb, mb, mb, sb);
            }
        }

        outTex.SetPixels32(pixels);
        outTex.Apply(false, false);
        byte[] png = outTex.EncodeToPNG();

        UnityEngine.Object.DestroyImmediate(outTex);
        if (metalSrc != null) UnityEngine.Object.DestroyImmediate(metalSrc);
        if (roughSrc != null) UnityEngine.Object.DestroyImmediate(roughSrc);

        File.WriteAllBytes(outputAssetPath, png);
        AssetDatabase.ImportAsset(outputAssetPath, ImportAssetOptions.ForceUpdate);

        var importer = AssetImporter.GetAtPath(outputAssetPath) as TextureImporter;
        if (importer != null)
        {
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(outputAssetPath);
    }

    private static Texture2D LoadReadableTexture(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath) || !File.Exists(assetPath)) return null;
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, linear: true);
        if (!tex.LoadImage(File.ReadAllBytes(assetPath)))
        {
            UnityEngine.Object.DestroyImmediate(tex);
            return null;
        }
        return tex;
    }

    private static Material MatchMaterial(Dictionary<string, Material> urpMaterials, string matName, string rendererName)
    {
        if (urpMaterials == null || urpMaterials.Count == 0) return null;
        string probe = (matName + " " + rendererName).ToLowerInvariant();
        foreach (var kv in urpMaterials)
        {
            if (probe.Contains(kv.Key.ToLowerInvariant())) return kv.Value;
        }
        if (probe.Contains("body") || probe.Contains("teeth") || probe.Contains("nail") || probe.Contains("eye"))
        {
            if (urpMaterials.TryGetValue("Body", out Material bodyMat)) return bodyMat;
        }
        if (urpMaterials.TryGetValue("Accesories", out Material accMat)) return accMat;
        foreach (var kv in urpMaterials) return kv.Value;
        return null;
    }

    private static Material ConvertEmbeddedOrCreateFallbackUrpMaterial(
        Material sourceMat,
        string monsterName,
        Shader urpLitShader,
        ref Material sharedFallback)
    {
        if (urpLitShader == null) return sourceMat;
        string matDir = $"{MaterialsRoot}/{monsterName}";
        EnsureFolder(matDir);

        string safeName = (sourceMat != null && !string.IsNullOrWhiteSpace(sourceMat.name))
            ? sourceMat.name.Replace(" ", "_").Replace("/", "_")
            : "Default";
        string matPath = $"{matDir}/Mat_{monsterName}_{safeName}_URP.mat";

        if (sourceMat == null && sharedFallback != null)
            return sharedFallback;

        Material urpMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (urpMat == null)
        {
            urpMat = new Material(urpLitShader);
            AssetDatabase.CreateAsset(urpMat, matPath);
        }
        else
        {
            urpMat.shader = urpLitShader;
        }

        Color baseCol = Color.white;
        Texture mainTex = null;
        if (sourceMat != null)
        {
            if (sourceMat.HasProperty("_BaseColor")) baseCol = sourceMat.GetColor("_BaseColor");
            else if (sourceMat.HasProperty("_Color")) baseCol = sourceMat.GetColor("_Color");
            if (sourceMat.HasProperty("_BaseMap")) mainTex = sourceMat.GetTexture("_BaseMap");
            else if (sourceMat.HasProperty("_MainTex")) mainTex = sourceMat.GetTexture("_MainTex");
        }
        else
        {
            baseCol = new Color(0.68f, 0.70f, 0.74f, 1f);
        }

        urpMat.SetColor("_BaseColor", baseCol);
        if (mainTex != null) urpMat.SetTexture("_BaseMap", mainTex);
        urpMat.SetFloat("_Metallic", 0.05f);
        urpMat.SetFloat("_Smoothness", 0.35f);
        urpMat.EnableKeyword("_EMISSION");
        urpMat.SetColor("_EmissionColor", Color.black);
        urpMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        EditorUtility.SetDirty(urpMat);

        if (sourceMat == null) sharedFallback = urpMat;
        return urpMat;
    }

    private static void MeasureSkeletonFeetAndHeight(GameObject skinnedModelFbx, AnimationClip idleClip, out float feetY, out float height)
    {
        GameObject temp = (GameObject)PrefabUtility.InstantiatePrefab(skinnedModelFbx);
        try
        {
            temp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            temp.transform.localScale = Vector3.one;
            if (idleClip != null) idleClip.SampleAnimation(temp, 0f);

            // Tier 1: Exact Mixamo toe/foot/head bones
            Transform leftToeEnd = FindDeepChild(temp.transform, "mixamorig:LeftToe_End");
            Transform rightToeEnd = FindDeepChild(temp.transform, "mixamorig:RightToe_End");
            Transform leftToeBase = FindDeepChild(temp.transform, "mixamorig:LeftToeBase");
            Transform rightToeBase = FindDeepChild(temp.transform, "mixamorig:RightToeBase");
            Transform leftFoot = FindDeepChild(temp.transform, "mixamorig:LeftFoot");
            Transform rightFoot = FindDeepChild(temp.transform, "mixamorig:RightFoot");
            Transform headTop = FindDeepChild(temp.transform, "mixamorig:HeadTop_End");
            Transform head = FindDeepChild(temp.transform, "mixamorig:Head");

            float minFoot = float.MaxValue;
            if (leftToeEnd != null) minFoot = Mathf.Min(minFoot, leftToeEnd.position.y);
            if (rightToeEnd != null) minFoot = Mathf.Min(minFoot, rightToeEnd.position.y);
            if (leftToeBase != null) minFoot = Mathf.Min(minFoot, leftToeBase.position.y);
            if (rightToeBase != null) minFoot = Mathf.Min(minFoot, rightToeBase.position.y);
            if (minFoot == float.MaxValue)
            {
                if (leftFoot != null) minFoot = Mathf.Min(minFoot, leftFoot.position.y - 0.10f);
                if (rightFoot != null) minFoot = Mathf.Min(minFoot, rightFoot.position.y - 0.10f);
            }

            float maxHead = float.MinValue;
            if (headTop != null) maxHead = Mathf.Max(maxHead, headTop.position.y);
            else if (head != null) maxHead = Mathf.Max(maxHead, head.position.y + 0.25f);

            if (minFoot < float.MaxValue && maxHead > minFoot + 0.25f)
            {
                feetY = minFoot - 0.02f;
                height = maxHead - feetY;
                return;
            }

            // Tier 2: Generic SkinnedMeshRenderer world bounds (works for Blender, Rigify, Quadrupeds, Dragons, Slimes!)
            SkinnedMeshRenderer[] smrs = temp.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            float smrMinY = float.MaxValue;
            float smrMaxY = float.MinValue;
            for (int i = 0; i < smrs.Length; i++)
            {
                smrs[i].updateWhenOffscreen = true;
                Bounds b = smrs[i].bounds;
                if (b.size.sqrMagnitude > 0.0001f)
                {
                    smrMinY = Mathf.Min(smrMinY, b.min.y);
                    smrMaxY = Mathf.Max(smrMaxY, b.max.y);
                }
            }
            if (smrMinY < float.MaxValue && smrMaxY > smrMinY + 0.20f)
            {
                feetY = smrMinY;
                height = smrMaxY - smrMinY;
                return;
            }

            // Tier 3: Generic bone vertical span fallback
            float boneMinY = float.MaxValue;
            float boneMaxY = float.MinValue;
            foreach (Transform t in temp.GetComponentsInChildren<Transform>(true))
            {
                if (t == temp.transform) continue;
                boneMinY = Mathf.Min(boneMinY, t.position.y);
                boneMaxY = Mathf.Max(boneMaxY, t.position.y);
            }
            if (boneMinY < float.MaxValue && boneMaxY > boneMinY + 0.20f)
            {
                feetY = boneMinY - 0.05f;
                height = (boneMaxY - feetY) + 0.15f;
                return;
            }

            feetY = 0f;
            height = 2.0f;
        }
        finally
        {
            if (temp != null) UnityEngine.Object.DestroyImmediate(temp);
        }
    }

    // =========================================================================
    // AUTOMATED VERIFICATION SUITE
    // =========================================================================

    public static void VerifyMonsterSystemPackage()
    {
        var sb = new StringBuilder();
        int pass = 0, fail = 0;

        void Check(string name, bool condition, string detail)
        {
            if (condition)
            {
                pass++;
                string line = $"[VERIFY PASS] {name} -> {detail}";
                sb.AppendLine(line);
                Debug.Log(line);
            }
            else
            {
                fail++;
                string line = $"[VERIFY FAIL] {name} -> {detail}";
                sb.AppendLine(line);
                Debug.LogError(line);
            }
        }

        sb.AppendLine("=== MONSTER SYSTEM ZERO-CONFLICT PACKAGE VERIFICATION ===");
        sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        // 1. Base Controller
        AnimatorController baseCtrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(BaseControllerPath);
        Check("1.1 Base Controller Exists", baseCtrl != null, BaseControllerPath);
        if (baseCtrl != null)
        {
            string[] expectedParams = { "Speed", "IsCombat", "Attack", "AttackIndex", "Skill", "SkillIndex", "IsCasting", "Hit", "IsStunned", "IsDead" };
            int found = 0;
            foreach (string ep in expectedParams)
                foreach (var p in baseCtrl.parameters)
                    if (p.name == ep) { found++; break; }
            Check("1.2 Base Controller Has All 10 Parameters", found == 10, $"Found {found}/10");

            bool upperOk = baseCtrl.layers.Length >= 2 &&
                           baseCtrl.layers[1].name == "UpperBody" &&
                           baseCtrl.layers[1].blendingMode == AnimatorLayerBlendingMode.Override &&
                           Mathf.Approximately(baseCtrl.layers[1].defaultWeight, 0f);
            Check("1.3 UpperBody Override Layer (Weight=0)", upperOk, $"Layers={baseCtrl.layers.Length}");

            AnimatorStateMachine sm = baseCtrl.layers[0].stateMachine;
            bool selfOff = sm.anyStateTransitions.Length == 3;
            foreach (var tr in sm.anyStateTransitions) if (tr.canTransitionToSelf) selfOff = false;
            Check("1.4 AnyState CanTransitionToSelf=False", selfOff, $"AnyStateTransitions={sm.anyStateTransitions.Length}");

            AnimatorState death = null;
            foreach (var cs in sm.states) if (cs.state.name == "Death") { death = cs.state; break; }
            Check("1.5 Death State Has Zero Outgoing Transitions", death != null && death.transitions.Length == 0,
                $"DeathTransitions={(death != null ? death.transitions.Length : -1)}");
        }

        // 2. FBX Import Settings
        string[] fbxGuids = AssetDatabase.FindAssets("t:Model", new[] { OrcSourceFolder });
        bool allGenNoAv = true, allLoopOk = true;
        foreach (string g in fbxGuids)
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            if (!p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) continue;
            var mi = AssetImporter.GetAtPath(p) as ModelImporter;
            if (mi == null) continue;
            if (mi.animationType != ModelImporterAnimationType.Generic || mi.avatarSetup != ModelImporterAvatarSetup.NoAvatar)
                allGenNoAv = false;

            string fn = Path.GetFileNameWithoutExtension(p);
            if (fn.Contains("@"))
            {
                bool expLoop = MonsterClipImportProcessor.ShouldClipLoop(fn);
                var clips = mi.clipAnimations.Length > 0 ? mi.clipAnimations : mi.defaultClipAnimations;
                if (clips.Length == 0 || clips[0].loopTime != expLoop || !clips[0].lockRootPositionXZ || !clips[0].lockRootHeightY)
                    allLoopOk = false;
            }
        }
        Check("2.1 All Orc FBX Generic + NoAvatar", allGenNoAv, $"{fbxGuids.Length} models checked");
        Check("2.2 All Orc Clips LoopTime & In-Place", allLoopOk, "All clip loop/in-place flags verified");

        // 3. AOC_Orc
        string aocPath = $"{AnimRoot}/Orc/AOC_Orc.overrideController";
        AnimatorOverrideController aoc = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(aocPath);
        int mapped = 0;
        bool allFilled = aoc != null;
        if (aoc != null)
        {
            var ov = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            aoc.GetOverrides(ov);
            foreach (var kv in ov)
            {
                if (kv.Value == null) allFilled = false;
                else mapped++;
            }
        }
        Check("3.1 AOC_Orc All 13 Override Slots Non-Null", allFilled && mapped == 13, $"Mapped {mapped}/13");

        // 4. Enemy_Orc.prefab (MonsterEnemyNPC + Grounded Feet + URP Mats + Real Bone Motion)
        GameObject orcPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(OrcPrefabPath);
        Check("4.1 Enemy_Orc.prefab Exists", orcPrefab != null, OrcPrefabPath);
        if (orcPrefab != null)
        {
            GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(orcPrefab);
            try
            {
                var mNpc = inst.GetComponent<MonsterEnemyNPC>();
                var bridge = inst.GetComponent<MonsterAnimatorBridge>();
                var col = inst.GetComponent<CapsuleCollider>();
                var rb = inst.GetComponent<Rigidbody>();
                var mr = inst.GetComponent<MeshRenderer>();
                var netObj = inst.GetComponent<NetworkObject>();

                bool compOk = mNpc != null && bridge != null && bridge.Animator != null &&
                              !bridge.Animator.applyRootMotion && bridge.Config != null &&
                              col != null && rb != null && netObj != null &&
                              (mr == null || !mr.enabled) &&
                              bridge.VisualRenderers != null && bridge.VisualRenderers.Length > 0;
                Check("4.2 Enemy_Orc Uses MonsterEnemyNPC + NetworkObject + Bridge", compOk,
                    $"MonsterEnemyNPC={mNpc != null}, NetObj={netObj != null}, CapsuleDisabled={mr == null || !mr.enabled}, Renderers={bridge?.VisualRenderers?.Length ?? 0}");

                // Grounding check
                inst.transform.position = new Vector3(0f, 1.2f, 0f);
                if (aoc != null && bridge != null && bridge.VisualRoot != null)
                {
                    AnimationClip idleClip = aoc[KeyIdle];
                    if (idleClip != null) idleClip.SampleAnimation(bridge.VisualRoot.gameObject, 0f);

                    Transform lToe = FindDeepChild(bridge.VisualRoot, "mixamorig:LeftToe_End") ?? FindDeepChild(bridge.VisualRoot, "mixamorig:LeftFoot");
                    Transform rToe = FindDeepChild(bridge.VisualRoot, "mixamorig:RightToe_End") ?? FindDeepChild(bridge.VisualRoot, "mixamorig:RightFoot");
                    float feetY = (lToe != null && rToe != null) ? Mathf.Min(lToe.position.y, rToe.position.y) : 99f;
                    Check("4.3 Enemy_Orc Feet Seated Flush On Ground (y=0)", feetY >= -0.08f && feetY <= 0.14f, $"feetWorldY={feetY:0.000}m");

                    // Bone motion across all 7 clips
                    Transform hips = FindDeepChild(bridge.VisualRoot, "mixamorig:Hips");
                    Transform rightArm = FindDeepChild(bridge.VisualRoot, "mixamorig:RightArm");
                    Transform spine = FindDeepChild(bridge.VisualRoot, "mixamorig:Spine");
                    string[] keys = { KeyIdle, KeyWalk, KeyRun, KeyAttack01, KeyAttack02, KeyHitReact, KeyDeath };
                    int moving = 0;
                    var details = new List<string>();
                    if (hips != null && rightArm != null && spine != null)
                    {
                        foreach (string k in keys)
                        {
                            AnimationClip c = aoc[k];
                            if (c == null) continue;
                            c.SampleAnimation(bridge.VisualRoot.gameObject, 0f);
                            Quaternion r0 = rightArm.localRotation;
                            Quaternion s0 = spine.localRotation;
                            Vector3 h0 = hips.localPosition;
                            c.SampleAnimation(bridge.VisualRoot.gameObject, c.length * 0.5f);
                            float da = Quaternion.Angle(r0, rightArm.localRotation);
                            float ds = Quaternion.Angle(s0, spine.localRotation);
                            float dh = Vector3.Distance(h0, hips.localPosition);
                            c.SampleAnimation(bridge.VisualRoot.gameObject, c.length * 0.85f);
                            da = Mathf.Max(da, Quaternion.Angle(r0, rightArm.localRotation));
                            ds = Mathf.Max(ds, Quaternion.Angle(s0, spine.localRotation));
                            dh = Mathf.Max(dh, Vector3.Distance(h0, hips.localPosition));
                            if (da > 0.5f || ds > 0.5f || dh > 0.005f) moving++;
                            details.Add($"{k.Replace("Placeholder_", "")}({da:0.0}deg)");
                        }
                    }
                    Check("4.4 Enemy_Orc Real Bone Motion (Zero T-Pose)", moving == keys.Length, $"{moving}/{keys.Length}: {string.Join(", ", details)}");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(inst);
            }
        }

        // 5. Zone_Orc.prefab & MonsterSystem_Manager.prefab
        GameObject zonePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(OrcZonePrefabPath);
        MonsterSpawnZone zoneComp = zonePrefab != null ? zonePrefab.GetComponent<MonsterSpawnZone>() : null;
        Check("5.1 Plug-and-Play Zone_Orc.prefab Wired",
            zoneComp != null && zoneComp.MonsterPrefab != null && zoneComp.MonsterPrefab.GetComponent<MonsterEnemyNPC>() != null,
            $"Prefab={zoneComp?.MonsterPrefab?.name}, Count={zoneComp?.SpawnCount ?? 0}");

        GameObject mgrPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ManagerPrefabPath);
        MonsterSystemManager mgrComp = mgrPrefab != null ? mgrPrefab.GetComponent<MonsterSystemManager>() : null;
        Check("5.2 Scene Hub MonsterSystem_Manager.prefab Wired",
            mgrComp != null && mgrComp.GetComponentsInChildren<MonsterSpawnZone>(true).Length > 0,
            $"Zones={mgrComp?.GetComponentsInChildren<MonsterSpawnZone>(true).Length ?? 0}, WorldItem={(mgrComp?.sharedWorldItemPrefab != null ? mgrComp.sharedWorldItemPrefab.name : "None")}");

        // 6. Friend's Enemy.prefab completely untouched
        GameObject friendEnemy = AssetDatabase.LoadAssetAtPath<GameObject>(FriendEnemyTemplatePath);
        bool friendClean = friendEnemy != null &&
                           friendEnemy.GetComponent<MeshRenderer>() != null &&
                           friendEnemy.GetComponent<MeshRenderer>().enabled &&
                           friendEnemy.GetComponent<MonsterAnimatorBridge>() == null &&
                           friendEnemy.GetComponent<MonsterEnemyNPC>() == null;
        Check("6.1 Friend Enemy.prefab 100% Untouched Capsule", friendClean, "Zero modifications to friend's prefab");

        sb.AppendLine($"TOTAL: {pass + fail} | PASSED: {pass} | FAILED: {fail}");
        File.WriteAllText(VerificationReportPath, sb.ToString(), new UTF8Encoding(false));
        AssetDatabase.ImportAsset(VerificationReportPath, ImportAssetOptions.ForceUpdate);
        Debug.Log($"[MonsterSystem] Verification finished: {pass}/{pass + fail} passed.");
    }

    private static Transform FindDeepChild(Transform parent, string name)
    {
        if (parent == null) return null;
        foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    private static void SetObjProp(SerializedObject so, string name, UnityEngine.Object val)
    {
        SerializedProperty p = so.FindProperty(name);
        if (p != null) p.objectReferenceValue = val;
    }

    private static void SetIntProp(SerializedObject so, string name, int val)
    {
        SerializedProperty p = so.FindProperty(name);
        if (p != null) p.intValue = val;
    }

    private static void SetFloatProp(SerializedObject so, string name, float val)
    {
        SerializedProperty p = so.FindProperty(name);
        if (p != null) p.floatValue = val;
    }

    private static void SetBoolProp(SerializedObject so, string name, bool val)
    {
        SerializedProperty p = so.FindProperty(name);
        if (p != null) p.boolValue = val;
    }

    private static void EnsureFolder(string assetFolderPath)
    {
        string normalized = assetFolderPath.Replace('\\', '/').TrimEnd('/');
        if (AssetDatabase.IsValidFolder(normalized)) return;
        string[] parts = normalized.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
#endif
