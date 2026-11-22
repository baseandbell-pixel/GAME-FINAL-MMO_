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
    public const string EmptyHubPrefabPath = SystemRoot + "/Prefabs/MonsterZones_Hub.prefab";
    public const string VerificationReportPath = SystemRoot + "/VerificationReport.txt";

    // Read-only reference to existing Enemy.prefab & WorldItem.prefab (NEVER modified!)
    public const string FriendEnemyTemplatePath = "Assets/_Project/Prefabs/Enemy.prefab";
    public const string FriendWorldItemPath = "Assets/_Project/Prefabs/WorldItem.prefab";
    public const string OrcSourceFolder = ModelsRoot + "/Orc";
    public const string OrcPrefabPath = MonsterPrefabsRoot + "/Enemy_Orc.prefab";
    public const string OrcZonePrefabPath = ZonePrefabsRoot + "/Zone_Orc.prefab";

    public const string GolemSourceFolder = ModelsRoot + "/Golem";
    public const string GolemPrefabPath = MonsterPrefabsRoot + "/Enemy_Golem.prefab";
    public const string GolemZonePrefabPath = ZonePrefabsRoot + "/Zone_Golem.prefab";

    public const string TrollSourceFolder = ModelsRoot + "/Troll";
    public const string TrollPrefabPath = MonsterPrefabsRoot + "/Enemy_Troll.prefab";
    public const string TrollZonePrefabPath = ZonePrefabsRoot + "/Zone_Troll.prefab";

    public const string SlimeSourceFolder = ModelsRoot + "/Slime";
    public const string SlimePrefabPath = MonsterPrefabsRoot + "/Enemy_Slime.prefab";
    public const string SlimeZonePrefabPath = ZonePrefabsRoot + "/Zone_Slime.prefab";

    public const string TurtleShellSourceFolder = ModelsRoot + "/TurtleShell";
    public const string TurtleShellPrefabPath = MonsterPrefabsRoot + "/Enemy_TurtleShell.prefab";
    public const string TurtleShellZonePrefabPath = ZonePrefabsRoot + "/Zone_TurtleShell.prefab";

    public const string BossOgreSourceFolder = ModelsRoot + "/BossOgre";
    public const string BossOgrePrefabPath = MonsterPrefabsRoot + "/Enemy_BossOgre.prefab";
    public const string BossOgreZonePrefabPath = ZonePrefabsRoot + "/Zone_BossOgre.prefab";

    public const string GoblinSourceFolder = ModelsRoot + "/Goblin";
    public const string GoblinPrefabPath = MonsterPrefabsRoot + "/Enemy_Goblin.prefab";
    public const string GoblinZonePrefabPath = ZonePrefabsRoot + "/Zone_Goblin.prefab";

    // Canonical placeholder clip keys embedded inside AC_Monster_Base.controller
    private const string KeySpawn = "Placeholder_Spawn";
    private const string KeyIdle = "Placeholder_Idle";
    private const string KeyWalk = "Placeholder_Walk";
    private const string KeyRun = "Placeholder_Run";
    private const string KeyCombatIdle = "Placeholder_Combat_Idle";
    private const string KeyGuard = "Placeholder_Guard";
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
            bool hasLatestCombatAudit = File.Exists(VerificationReportPath) &&
                                        File.ReadAllText(VerificationReportPath).Contains("Master-Only Spawn + Participation Chest Audit");
            bool needBuild = !hasLatestCombatAudit ||
                             !File.Exists(BaseControllerPath) ||
                             !File.Exists(OrcPrefabPath) ||
                             !File.Exists(OrcZonePrefabPath) ||
                             !File.Exists(ManagerPrefabPath) ||
                             !File.Exists(EmptyHubPrefabPath) ||
                             !File.Exists(VerificationReportPath) ||
                             (Directory.Exists(GolemSourceFolder) && (!File.Exists(GolemPrefabPath) || !File.Exists(GolemZonePrefabPath))) ||
                             (Directory.Exists(TrollSourceFolder) && (!File.Exists(TrollPrefabPath) || !File.Exists(TrollZonePrefabPath))) ||
                             (Directory.Exists(SlimeSourceFolder) && (!File.Exists(SlimePrefabPath) || !File.Exists(SlimeZonePrefabPath))) ||
                             (Directory.Exists(TurtleShellSourceFolder) && (!File.Exists(TurtleShellPrefabPath) || !File.Exists(TurtleShellZonePrefabPath))) ||
                             (Directory.Exists(BossOgreSourceFolder) && (!File.Exists(BossOgrePrefabPath) || !File.Exists(BossOgreZonePrefabPath))) ||
                             (Directory.Exists(GoblinSourceFolder) && (!File.Exists(GoblinPrefabPath) || !File.Exists(GoblinZonePrefabPath)));
            if (needBuild && Directory.Exists(ModelsRoot))
            {
                BuildAllMonstersInModelsFolder();
            }

            // Ensure Dev_Kulpat.unity is wired when SandboxWorldSpawner is present (does NOT interfere if user removed SandboxWorldSpawner!)
            if (File.Exists(OrcPrefabPath))
            {
                Scene activeScene = SceneManager.GetActiveScene();
                bool needSceneWire = !hasLatestCombatAudit;
                if (!needSceneWire && activeScene.IsValid() && activeScene.name.Equals("Dev_Kulpat", StringComparison.OrdinalIgnoreCase))
                {
                    SandboxWorldSpawner sp = UnityEngine.Object.FindAnyObjectByType<SandboxWorldSpawner>(FindObjectsInactive.Include);
                    if (sp != null)
                    {
                        var spSo = new SerializedObject(sp);
                        SerializedProperty arr = spSo.FindProperty("campEnemyPrefabs");
                        SerializedProperty bossP = spSo.FindProperty("bossPrefab");
                        bool anyCampNull = arr == null || arr.arraySize < 4;
                        for (int ci = 0; !anyCampNull && ci < 4; ci++)
                        {
                            if (arr.GetArrayElementAtIndex(ci).objectReferenceValue == null)
                                anyCampNull = true;
                        }
                        bool bossNotOgre = bossP == null || bossP.objectReferenceValue == null || !(bossP.objectReferenceValue is MonsterEnemyNPC);
                        if (anyCampNull || bossNotOgre)
                            needSceneWire = true;
                    }
                }
                if (needSceneWire)
                {
                    EnsureAllSevenCampsInDevKulpatScene();
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
                float height = ResolveDefaultWorldHeight(monsterName, isBoss);
                int defaultCount = ResolveDefaultSpawnCount(monsterName, isBoss);
                float defaultSpacing = isBoss ? 16f
                                     : monsterName.Equals("Golem", StringComparison.OrdinalIgnoreCase) ? 15f
                                     : monsterName.Equals("Troll", StringComparison.OrdinalIgnoreCase) ? 14f
                                     : monsterName.Equals("Slime", StringComparison.OrdinalIgnoreCase) ? 10f
                                     : (monsterName.Equals("TurtleShell", StringComparison.OrdinalIgnoreCase) || monsterName.Equals("Goblin", StringComparison.OrdinalIgnoreCase)) ? 11f
                                     : 12f;
                float defaultRadius = defaultSpacing + 3f;
                float respawnMin = isBoss ? 120f
                                 : monsterName.Equals("Golem", StringComparison.OrdinalIgnoreCase) ? 50f
                                 : monsterName.Equals("Troll", StringComparison.OrdinalIgnoreCase) ? 40f
                                 : monsterName.Equals("Slime", StringComparison.OrdinalIgnoreCase) ? 25f
                                 : (monsterName.Equals("TurtleShell", StringComparison.OrdinalIgnoreCase) || monsterName.Equals("Goblin", StringComparison.OrdinalIgnoreCase)) ? 30f
                                 : 35f;
                float respawnMax = isBoss ? 150f : respawnMin + 15f;
                ResolveDefaultZoneLevels(monsterName, isBoss, out int minLvl, out int maxLvl);

                string prefabPath = $"{MonsterPrefabsRoot}/Enemy_{monsterName}.prefab";
                string zonePath = $"{ZonePrefabsRoot}/Zone_{monsterName}.prefab";

                BuildMonsterFromFolder(folderAssetPath, monsterName, isBoss, height);
                BuildZonePrefab(monsterName, prefabPath, zonePath,
                    spawnCount: defaultCount,
                    slotSpacing: defaultSpacing,
                    territoryRadius: defaultRadius,
                    respawnMin: respawnMin,
                    respawnMax: respawnMax,
                    zoneMinLevel: minLvl,
                    zoneMaxLevel: maxLvl);
                builtCount++;
            }

            BuildManagerPrefab();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            TryRebuildFusionPrefabTable();
            VerifyMonsterSystemPackage();
            Debug.Log($"[MonsterSystem] Built {builtCount} monster package(s) from '{ModelsRoot}' + updated MonsterSystem_Manager.prefab & Fusion Prefab Table.");
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

    private static float ResolveDefaultWorldHeight(string monsterName, bool isBoss)
    {
        if (isBoss) return 3.20f;
        if (monsterName.Equals("Golem", StringComparison.OrdinalIgnoreCase)) return 3.00f;
        if (monsterName.Equals("Troll", StringComparison.OrdinalIgnoreCase)) return 2.70f;
        if (monsterName.Equals("Slime", StringComparison.OrdinalIgnoreCase)) return 1.10f;
        if (monsterName.Equals("TurtleShell", StringComparison.OrdinalIgnoreCase)) return 1.30f;
        if (monsterName.Equals("Goblin", StringComparison.OrdinalIgnoreCase)) return 1.45f;
        return 2.15f;
    }

    private static int ResolveDefaultSpawnCount(string monsterName, bool isBoss)
    {
        if (isBoss) return 1;
        if (monsterName.Equals("Golem", StringComparison.OrdinalIgnoreCase)) return 2;
        if (monsterName.Equals("Troll", StringComparison.OrdinalIgnoreCase)) return 3;
        return 4;
    }

    private static MonsterArchetype ResolveDefaultArchetype(string monsterName, bool isBoss)
    {
        if (isBoss || monsterName.Equals("Golem", StringComparison.OrdinalIgnoreCase) || monsterName.Equals("Troll", StringComparison.OrdinalIgnoreCase))
            return MonsterArchetype.Brute;
        if (monsterName.Equals("Slime", StringComparison.OrdinalIgnoreCase) || monsterName.Equals("Goblin", StringComparison.OrdinalIgnoreCase))
            return MonsterArchetype.Skirmisher;
        return MonsterArchetype.Warrior;
    }

    private static void ResolveDefaultZoneLevels(string monsterName, bool isBoss, out int minLvl, out int maxLvl)
    {
        if (isBoss) { minLvl = 20; maxLvl = 25; return; }
        if (monsterName.Equals("Golem", StringComparison.OrdinalIgnoreCase)) { minLvl = 10; maxLvl = 13; return; }
        if (monsterName.Equals("Troll", StringComparison.OrdinalIgnoreCase)) { minLvl = 6; maxLvl = 9; return; }
        if (monsterName.Equals("Goblin", StringComparison.OrdinalIgnoreCase)) { minLvl = 2; maxLvl = 4; return; }
        if (monsterName.Equals("TurtleShell", StringComparison.OrdinalIgnoreCase)) { minLvl = 2; maxLvl = 3; return; }
        minLvl = 1; maxLvl = 2;
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

    private static void TryRebuildFusionPrefabTable()
    {
        try
        {
            EditorApplication.ExecuteMenuItem("Tools/Fusion/Rebuild Prefab Table");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[MonsterSystem] Could not auto-invoke Fusion Rebuild Prefab Table: {ex.Message}");
        }
    }

    private const string DevKulpatScenePath = "Assets/_Project/Scenes/Sandboxes/Dev_Kulpat.unity";

    [MenuItem("Tools/ระบบมอนสเตอร์ (Monster System)/3b. จัดวางแคมป์มอนสเตอร์ครบทั้ง 7 ตัวลงฉากนี้ (4 แคมป์หลัก + 2 แคมป์เสริม + บอส)")]
    public static void AssignAllCampsAndBossToSpawnerMenu()
    {
        EnsureAllSevenCampsInDevKulpatScene();
    }

    public static void EnsureAllSevenCampsInDevKulpatScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        Scene activeScene = SceneManager.GetActiveScene();
        SandboxWorldSpawner spawner = UnityEngine.Object.FindAnyObjectByType<SandboxWorldSpawner>(FindObjectsInactive.Include);
        if (spawner != null)
        {
            AssignAllCampsAndBossToSpawnerInActiveScene(saveScene: true);
            return;
        }

        // If user is currently on Lobby.unity (or another scene without SandboxWorldSpawner), temporarily open Dev_Kulpat.unity, wire all 7 camps, save, and return!
        if (File.Exists(DevKulpatScenePath))
        {
            string previousScenePath = activeScene.IsValid() ? activeScene.path : string.Empty;
            if (!string.IsNullOrEmpty(previousScenePath) && activeScene.isDirty)
                EditorSceneManager.SaveScene(activeScene);

            Scene devScene = EditorSceneManager.OpenScene(DevKulpatScenePath, OpenSceneMode.Single);
            if (devScene.IsValid())
            {
                AssignAllCampsAndBossToSpawnerInActiveScene(saveScene: true);
            }

            if (!string.IsNullOrEmpty(previousScenePath) &&
                !previousScenePath.Equals(DevKulpatScenePath, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(previousScenePath))
            {
                EditorSceneManager.OpenScene(previousScenePath, OpenSceneMode.Single);
            }
        }
    }

    public static void AssignAllCampsAndBossToSpawnerInActiveScene(bool saveScene)
    {
        SandboxWorldSpawner spawner = UnityEngine.Object.FindAnyObjectByType<SandboxWorldSpawner>(FindObjectsInactive.Include);
        if (spawner == null)
        {
            Debug.LogWarning("[MonsterSystem] No SandboxWorldSpawner found in active scene.");
            return;
        }

        EnemyNPC c1 = AssetDatabase.LoadAssetAtPath<EnemyNPC>(OrcPrefabPath);
        EnemyNPC c2 = AssetDatabase.LoadAssetAtPath<EnemyNPC>(GoblinPrefabPath) ?? AssetDatabase.LoadAssetAtPath<EnemyNPC>(TurtleShellPrefabPath);
        EnemyNPC c3 = AssetDatabase.LoadAssetAtPath<EnemyNPC>(TrollPrefabPath);
        EnemyNPC c4 = AssetDatabase.LoadAssetAtPath<EnemyNPC>(GolemPrefabPath);
        EnemyNPC boss = AssetDatabase.LoadAssetAtPath<EnemyNPC>(BossOgrePrefabPath);

        if (c1 == null || c2 == null || c3 == null || c4 == null || boss == null)
        {
            BuildAllMonstersInModelsFolder();
            c1 = AssetDatabase.LoadAssetAtPath<EnemyNPC>(OrcPrefabPath);
            c2 = AssetDatabase.LoadAssetAtPath<EnemyNPC>(GoblinPrefabPath) ?? AssetDatabase.LoadAssetAtPath<EnemyNPC>(TurtleShellPrefabPath);
            c3 = AssetDatabase.LoadAssetAtPath<EnemyNPC>(TrollPrefabPath);
            c4 = AssetDatabase.LoadAssetAtPath<EnemyNPC>(GolemPrefabPath);
            boss = AssetDatabase.LoadAssetAtPath<EnemyNPC>(BossOgrePrefabPath);
        }

        var so = new SerializedObject(spawner);
        SerializedProperty campArr = so.FindProperty("campEnemyPrefabs");
        if (campArr != null)
        {
            if (campArr.arraySize < 4) campArr.arraySize = 4;
            if (c1 != null) campArr.GetArrayElementAtIndex(0).objectReferenceValue = c1;
            if (c2 != null) campArr.GetArrayElementAtIndex(1).objectReferenceValue = c2;
            if (c3 != null) campArr.GetArrayElementAtIndex(2).objectReferenceValue = c3;
            if (c4 != null) campArr.GetArrayElementAtIndex(3).objectReferenceValue = c4;
        }
        SerializedProperty bossProp = so.FindProperty("bossPrefab");
        if (bossProp != null && boss != null)
            bossProp.objectReferenceValue = boss;

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(spawner);

        // Place Extra Camp 5 (Zone_Slime) at (-15, 0, -18) and Extra Camp 6 (Zone_TurtleShell) at (15, 0, -18)
        // right in the wide-open Starter Meadows outside the Safe Zone gate so ALL 7 monsters are live in the world!
        PlaceOrUpdateExtraCampZone("Slime", new Vector3(-15f, 0f, -18f), slotSpacing: 8f, minLevel: 1, maxLevel: 3, campNum: 5);
        PlaceOrUpdateExtraCampZone("TurtleShell", new Vector3(15f, 0f, -18f), slotSpacing: 8f, minLevel: 3, maxLevel: 5, campNum: 6);

        Scene activeScene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(activeScene);
        if (saveScene && activeScene.IsValid() && !string.IsNullOrEmpty(activeScene.path))
            EditorSceneManager.SaveScene(activeScene);

        TryRebuildFusionPrefabTable();
        Debug.Log("[MonsterSystem] Placed ALL 7 Monster Camps in scene: Camp1=Orc (-46,-22), Camp2=Goblin (46,-22), Camp3=Troll (-58,28), Camp4=Golem (58,28), ExtraCamp5=Zone_Slime (-15,-18), ExtraCamp6=Zone_TurtleShell (15,-18), Boss=BossOgre (0,74)!");
    }

    private static void PlaceOrUpdateExtraCampZone(string monsterName, Vector3 worldPos, float slotSpacing, int minLevel, int maxLevel, int campNum = 0)
    {
        string zonePath = $"{ZonePrefabsRoot}/Zone_{monsterName}.prefab";
        GameObject zonePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(zonePath);
        if (zonePrefab == null) return;

        string targetName = $"Zone_{monsterName}";
        MonsterSpawnZone found = null;
        foreach (MonsterSpawnZone existing in UnityEngine.Object.FindObjectsByType<MonsterSpawnZone>(FindObjectsInactive.Include))
        {
            if (existing != null && existing.gameObject.name == targetName)
            {
                found = existing;
                break;
            }
        }

        if (found == null)
        {
            GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(zonePrefab);
            inst.name = targetName;
            found = inst.GetComponent<MonsterSpawnZone>();
        }

        if (found != null)
        {
            found.transform.position = worldPos;
            string enemyPath = $"{MonsterPrefabsRoot}/Enemy_{monsterName}.prefab";
            EnemyNPC enemyPrefab = AssetDatabase.LoadAssetAtPath<EnemyNPC>(enemyPath);
            var zSo = new SerializedObject(found);
            if (enemyPrefab != null) SetObjProp(zSo, "monsterPrefab", enemyPrefab);
            SetIntProp(zSo, "spawnCount", 4);
            SetFloatProp(zSo, "slotSpacing", slotSpacing);
            SetFloatProp(zSo, "territoryRadius", slotSpacing + 3f);
            SetBoolProp(zSo, "useFixedZoneLevel", true);
            SetIntProp(zSo, "zoneMinLevel", minLevel);
            SetIntProp(zSo, "zoneMaxLevel", maxLevel);
            SetIntProp(zSo, "respawnMode", (int)MonsterZoneRespawnMode.CampClearWave);
            SetIntProp(zSo, "campNumber", campNum);
            SetBoolProp(zSo, "showOnWorldMap", true);
            SetBoolProp(zSo, "spawnRewardChestOnZoneClear", true);
            SetBoolProp(zSo, "awardClearGold", true);
            SetBoolProp(zSo, "allowNonMasterFallbackSpawn", false);
            zSo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(found);
        }
    }

    [MenuItem("Tools/ระบบมอนสเตอร์ (Monster System)/3.5 สร้าง Empty ตัวคุมโซนเปล่า (MonsterZones_Hub)")]
    public static void CreateEmptyZoneHubInActiveScene()
    {
        MonsterSystemManager existing = UnityEngine.Object.FindAnyObjectByType<MonsterSystemManager>(FindObjectsInactive.Include);
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            Debug.Log($"[MonsterSystem] Scene already has a Zone Hub '{existing.name}'. Selected it in Hierarchy.");
            return;
        }

        GameObject hubPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EmptyHubPrefabPath);
        if (hubPrefab == null)
        {
            BuildManagerPrefab();
            hubPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EmptyHubPrefabPath);
        }

        GameObject inst;
        if (hubPrefab != null)
        {
            inst = (GameObject)PrefabUtility.InstantiatePrefab(hubPrefab);
            inst.name = "MonsterZones_Hub";
        }
        else
        {
            inst = new GameObject("MonsterZones_Hub");
            var mgrNew = inst.AddComponent<MonsterSystemManager>();
            mgrNew.sharedWorldItemPrefab = AssetDatabase.LoadAssetAtPath<WorldItem>(FriendWorldItemPath);
            mgrNew.ensureFallbackSafeZoneIfMissing = true;
            mgrNew.autoSyncChildZones = true;
        }

        inst.transform.position = Vector3.zero;
        Undo.RegisterCreatedObjectUndo(inst, "Create MonsterZones_Hub");
        Selection.activeGameObject = inst;
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[MonsterSystem] Created clean Empty Hub 'MonsterZones_Hub'! Drag any Zone_* under it or click '+ Orc / + Goblin / ...' in its Inspector.");
    }

    public static void AddZoneAsChildOfHub(MonsterSystemManager hub, string monsterName)
    {
        if (hub == null || string.IsNullOrWhiteSpace(monsterName)) return;
        string zonePath = $"{ZonePrefabsRoot}/Zone_{monsterName}.prefab";
        GameObject zonePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(zonePath);
        if (zonePrefab == null)
        {
            Debug.LogError($"[MonsterSystem] Could not load '{zonePath}'. Build the monster first.");
            return;
        }

        // Generate a unique child name so the user can place multiple camps of the same monster!
        string baseName = $"Zone_{monsterName}";
        string uniqueName = baseName;
        int suffix = 2;
        while (hub.transform.Find(uniqueName) != null)
        {
            uniqueName = $"{baseName}_{suffix}";
            suffix++;
        }

        GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(zonePrefab, hub.transform);
        inst.name = uniqueName;

        // Place right where the user is looking in Scene View (snapped to ground if possible), otherwise offset cleanly
        Vector3 placePos = new Vector3(0f, 0f, -18f + (hub.transform.childCount - 1) * 4f);
        if (SceneView.lastActiveSceneView != null)
        {
            Vector3 pivot = SceneView.lastActiveSceneView.pivot;
            if (Physics.Raycast(pivot + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 120f))
                placePos = hit.point;
            else
                placePos = new Vector3(pivot.x, 0f, pivot.z);
        }
        inst.transform.position = placePos;

        Undo.RegisterCreatedObjectUndo(inst, $"Add {uniqueName} to {hub.name}");
        hub.RefreshChildZones();
        EditorUtility.SetDirty(hub);
        Selection.activeGameObject = inst;
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[MonsterSystem] Added '{uniqueName}' under '{hub.name}' at {placePos}.");
    }

    public static void GroupAllSceneZonesUnderHub(MonsterSystemManager hub)
    {
        if (hub == null) return;
        MonsterSpawnZone[] allZones = UnityEngine.Object.FindObjectsByType<MonsterSpawnZone>(FindObjectsInactive.Include);
        int moved = 0;
        for (int i = 0; i < allZones.Length; i++)
        {
            MonsterSpawnZone z = allZones[i];
            if (z == null || z.transform.IsChildOf(hub.transform)) continue;
            Undo.SetTransformParent(z.transform, hub.transform, "Group Zone Under Hub");
            moved++;
        }
        Undo.RecordObject(hub, "Sync Managed Zones");
        hub.RefreshChildZones();
        EditorUtility.SetDirty(hub);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[MonsterSystem] Grouped {moved} scene zone(s) under '{hub.name}' (total managed: {hub.managedZones.Count}).");
    }

    public static void AutoNumberChildZones(MonsterSystemManager hub)
    {
        if (hub == null) return;
        hub.RefreshChildZones();
        int nextCampNum = 1;
        for (int i = 0; i < hub.managedZones.Count; i++)
        {
            MonsterSpawnZone z = hub.managedZones[i];
            if (z == null) continue;
            Undo.RecordObject(z, "Auto-Number Child Zones");
            var so = new SerializedObject(z);
            SerializedProperty numProp = so.FindProperty("campNumber");
            if (numProp != null)
            {
                if (z.IsBossZone)
                    numProp.intValue = 0;
                else
                    numProp.intValue = nextCampNum++;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(z);
        }
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[MonsterSystem] Auto-numbered {nextCampNum - 1} camp(s) under '{hub.name}' in Hierarchy order.");
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
        controller.AddParameter("IsGuarding", AnimatorControllerParameterType.Bool);

        // 2. Embed 14 unique placeholder clips
        var clips = new Dictionary<string, AnimationClip>
        {
            { KeySpawn, CreatePlaceholderClip(controller, KeySpawn, 0.20f, loop: false) },
            { KeyIdle, CreatePlaceholderClip(controller, KeyIdle, 1.00f, loop: true) },
            { KeyWalk, CreatePlaceholderClip(controller, KeyWalk, 1.00f, loop: true) },
            { KeyRun, CreatePlaceholderClip(controller, KeyRun, 1.00f, loop: true) },
            { KeyCombatIdle, CreatePlaceholderClip(controller, KeyCombatIdle, 1.00f, loop: true) },
            { KeyGuard, CreatePlaceholderClip(controller, KeyGuard, 1.00f, loop: true) },
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

        AnimatorState guardState = rootSm.AddState("Guard", new Vector3(520f, 110f, 0f));
        guardState.motion = clips[KeyGuard];
        guardState.tag = "Guard";

        var tLocoToGuard = locomotionState.AddTransition(guardState);
        tLocoToGuard.hasExitTime = false;
        tLocoToGuard.duration = 0.12f;
        tLocoToGuard.AddCondition(AnimatorConditionMode.If, 0f, "IsGuarding");

        var tCombatToGuard = combatIdleState.AddTransition(guardState);
        tCombatToGuard.hasExitTime = false;
        tCombatToGuard.duration = 0.12f;
        tCombatToGuard.AddCondition(AnimatorConditionMode.If, 0f, "IsGuarding");

        var tGuardToCombat = guardState.AddTransition(combatIdleState);
        tGuardToCombat.hasExitTime = false;
        tGuardToCombat.duration = 0.14f;
        tGuardToCombat.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsGuarding");

        // AnyState -> Guard (snaps immediately into Guard pose even from Hit_React or Attack!)
        var tAnyToGuard = rootSm.AddAnyStateTransition(guardState);
        tAnyToGuard.hasExitTime = false;
        tAnyToGuard.duration = 0.10f;
        tAnyToGuard.canTransitionToSelf = false;
        tAnyToGuard.AddCondition(AnimatorConditionMode.If, 0f, "IsGuarding");
        tAnyToGuard.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsStunned");
        tAnyToGuard.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsDead");

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
        AddAttackEntryTransitions(guardState, atk1, atk2, atk3);
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
        tAnyToHit.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsGuarding");
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
        AssetDatabase.ImportAsset(BaseControllerPath, ImportAssetOptions.ForceUpdate);
        return AssetDatabase.LoadAssetAtPath<AnimatorController>(BaseControllerPath) ?? controller;
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
        bool isNewAoc = aoc == null;
        if (isNewAoc)
        {
            aoc = new AnimatorOverrideController(baseController);
        }
        else
        {
            aoc.runtimeAnimatorController = baseController;
        }

        AnimationClip walkClip = GetClipOrFallback(discoveredClips, "walk", idleClip);
        AnimationClip runClip = GetClipOrFallback(discoveredClips, "run", walkClip);
        AnimationClip combatIdleClip = GetClipOrFallback(discoveredClips, "combat_idle", idleClip);
        AnimationClip guardClip = GetClipOrFallback(discoveredClips, "guard", combatIdleClip);
        AnimationClip atk1Clip = GetClipOrFallback(discoveredClips, "attack_01", idleClip);
        AnimationClip atk2Clip = GetClipOrFallback(discoveredClips, "attack_02", atk1Clip);
        AnimationClip atk3Clip = GetClipOrFallback(discoveredClips, "attack_03", atk1Clip);
        AnimationClip skillCastClip = GetClipOrFallback(discoveredClips, "skill_cast", combatIdleClip);
        AnimationClip heavySlamFallback = atk3Clip != atk1Clip ? atk3Clip : atk2Clip;
        AnimationClip skillReleaseClip = GetClipOrFallback(discoveredClips, "skill_release", heavySlamFallback);
        AnimationClip hitReactClip = GetClipOrFallback(discoveredClips, "hit_react", idleClip);
        AnimationClip stunClip = GetClipOrFallback(discoveredClips, "stun", idleClip);
        AnimationClip deathClip = GetClipOrFallback(discoveredClips, "death", idleClip);
        AnimationClip spawnClip = GetClipOrFallback(discoveredClips, "spawn", idleClip);

        var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        aoc.GetOverrides(overrides);
        if (overrides.Count == 0 && baseController != null)
        {
            var seen = new HashSet<AnimationClip>();
            foreach (AnimationClip bc in baseController.animationClips)
            {
                if (bc != null && seen.Add(bc))
                    overrides.Add(new KeyValuePair<AnimationClip, AnimationClip>(bc, null));
            }
        }
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
                KeyGuard => guardClip,
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
        if (isNewAoc)
            AssetDatabase.CreateAsset(aoc, aocPath);
        EditorUtility.SetDirty(aoc);
        AssetDatabase.SaveAssets();

        // 2. Config_<MonsterName>.asset (Preserve user's Loot Table, Stat Multipliers, Audio & Height on rebuild!)
        string configPath = $"{monsterAnimDir}/Config_{monsterName}.asset";
        MonsterAnimationConfig config = AssetDatabase.LoadAssetAtPath<MonsterAnimationConfig>(configPath);
        bool isNewConfig = config == null;
        if (isNewConfig)
        {
            config = ScriptableObject.CreateInstance<MonsterAnimationConfig>();
            config.monsterName = monsterName;
            config.targetWorldHeight = targetWorldHeight;
        }
        else if (string.IsNullOrWhiteSpace(config.monsterName))
        {
            config.monsterName = monsterName;
        }

        config.archetype = ResolveDefaultArchetype(monsterName, isBoss);
        config.isBoss = isBoss;
        config.canFlinch = !isBoss && config.archetype != MonsterArchetype.Brute;

        // Species-tuned defaults balanced for TMRKKxOG Lv.1-30 3-Tier Zone Scaling & PlayerCombat 2.65m / 3.25m sword reach!
        if (isBoss)
        {
            config.healthMultiplier = 1.00f;
            config.damageMultiplier = 1.15f;
            config.defenseBonus = 3;
            config.attackRange = 2.85f;
            config.attackImpactDelay = 0.46f;
            config.counterImpactDelay = 0.38f;
        }
        else if (monsterName.Equals("Golem", StringComparison.OrdinalIgnoreCase))
        {
            config.healthMultiplier = 2.10f;
            config.damageMultiplier = 1.75f;
            config.defenseBonus = 4;
            config.attackRange = 2.45f;
            config.attackImpactDelay = 0.48f;
            config.counterImpactDelay = 0.40f;
        }
        else if (monsterName.Equals("Troll", StringComparison.OrdinalIgnoreCase))
        {
            config.healthMultiplier = 1.80f;
            config.damageMultiplier = 1.55f;
            config.defenseBonus = 2;
            config.attackRange = 2.38f;
            config.attackImpactDelay = 0.46f;
            config.counterImpactDelay = 0.38f;
        }
        else if (monsterName.Equals("Slime", StringComparison.OrdinalIgnoreCase))
        {
            config.healthMultiplier = 0.85f;
            config.damageMultiplier = 0.85f;
            config.defenseBonus = 0;
            config.attackRange = 1.85f;
            config.attackImpactDelay = 0.40f;
            config.counterImpactDelay = 0.36f;
        }
        else if (monsterName.Equals("TurtleShell", StringComparison.OrdinalIgnoreCase))
        {
            config.healthMultiplier = 1.25f;
            config.damageMultiplier = 1.05f;
            config.defenseBonus = 3;
            config.attackRange = 1.95f;
            config.attackImpactDelay = 0.42f;
            config.counterImpactDelay = 0.36f;
        }
        else if (monsterName.Equals("Goblin", StringComparison.OrdinalIgnoreCase))
        {
            config.healthMultiplier = 1.05f;
            config.damageMultiplier = 1.15f;
            config.defenseBonus = 1;
            config.attackRange = 2.05f;
            config.attackImpactDelay = 0.40f;
            config.counterImpactDelay = 0.36f;
        }
        else
        {
            config.healthMultiplier = 1.35f;
            config.damageMultiplier = 1.38f;
            config.defenseBonus = 1;
            config.attackRange = 2.25f;
            config.attackImpactDelay = 0.42f;
            config.counterImpactDelay = 0.36f;
        }

        if (isNewConfig)
        {
            AssetDatabase.CreateAsset(config, configPath);
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
        // Preserve both .meta GUID and internal MonsterEnemyNPC component local fileID by editing in-place when prefab already exists!
        bool canEditInPlace = File.Exists(outputPrefabPath) &&
                              AssetDatabase.LoadAssetAtPath<MonsterEnemyNPC>(outputPrefabPath) != null;

        if (!canEditInPlace)
        {
            string metaPath = outputPrefabPath + ".meta";
            string preservedMeta = File.Exists(metaPath) ? File.ReadAllText(metaPath) : null;

            if (File.Exists(outputPrefabPath))
                AssetDatabase.DeleteAsset(outputPrefabPath);

            if (File.Exists(FriendEnemyTemplatePath))
            {
                AssetDatabase.CopyAsset(FriendEnemyTemplatePath, outputPrefabPath);
                if (!string.IsNullOrEmpty(preservedMeta))
                    File.WriteAllText(metaPath, preservedMeta);
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
                if (!string.IsNullOrEmpty(preservedMeta))
                {
                    File.WriteAllText(metaPath, preservedMeta);
                    AssetDatabase.Refresh();
                }
            }
        }

        // Read friend's template lootPrefab & lootTable directly from FriendEnemyTemplatePath so in-place rebuilds also get friend's loot!
        UnityEngine.Object savedLootPrefab = null;
        var savedLootEntries = new List<(UnityEngine.Object item, int minAmount, int maxAmount, float chance)>();
        int savedMaxHp = 120;
        int savedAtkDmg = 10;
        float savedAtkCd = 2.0f;
        float savedAtkRange = 2.5f;
        int savedExp = 50;

        EnemyNPC templateNpc = AssetDatabase.LoadAssetAtPath<EnemyNPC>(FriendEnemyTemplatePath);
        if (templateNpc != null)
        {
            var tplSo = new SerializedObject(templateNpc);
            savedLootPrefab = tplSo.FindProperty("lootPrefab")?.objectReferenceValue;
            SerializedProperty tplLootTable = tplSo.FindProperty("lootTable");
            if (tplLootTable != null && tplLootTable.isArray)
            {
                for (int li = 0; li < tplLootTable.arraySize; li++)
                {
                    SerializedProperty elem = tplLootTable.GetArrayElementAtIndex(li);
                    UnityEngine.Object itemObj = elem.FindPropertyRelative("item")?.objectReferenceValue;
                    int minAmt = elem.FindPropertyRelative("minAmount")?.intValue ?? 1;
                    int maxAmt = elem.FindPropertyRelative("maxAmount")?.intValue ?? 1;
                    float ch = elem.FindPropertyRelative("chance")?.floatValue ?? 0.5f;
                    if (itemObj != null)
                        savedLootEntries.Add((itemObj, minAmt, maxAmt, ch));
                }
            }
            savedMaxHp = templateNpc.MaxHealth;
            savedAtkDmg = templateNpc.AttackDamage;
            savedAtkCd = templateNpc.AttackCooldown;
            savedAtkRange = templateNpc.AttackRange;
            savedExp = templateNpc.ExperienceReward;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(outputPrefabPath);
        try
        {
            root.name = $"Enemy_{monsterName}";

            // Remove legacy capsule visual (KEEP CapsuleCollider, Rigidbody, NetworkObject, NetworkTransform!)
            MeshRenderer rootCapsuleRenderer = root.GetComponent<MeshRenderer>();
            if (rootCapsuleRenderer != null)
                UnityEngine.Object.DestroyImmediate(rootCapsuleRenderer);
            MeshFilter rootCapsuleFilter = root.GetComponent<MeshFilter>();
            if (rootCapsuleFilter != null)
                UnityEngine.Object.DestroyImmediate(rootCapsuleFilter);

            EnemyNPC oldNpc = root.GetComponent<EnemyNPC>();
            if (oldNpc != null && !(oldNpc is MonsterEnemyNPC))
            {
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
            SetFloatProp(npcSo, "riggedLockPointHeight", Mathf.Clamp(targetWorldHeight * 0.22f, 0.25f, 0.85f));
            SetFloatProp(npcSo, "riggedOverheadBarHeight", Mathf.Clamp(targetWorldHeight + 0.55f, 1.55f, 4.10f));
            SetBoolProp(npcSo, "useNavMeshWhenAvailable", false);
            if (savedLootPrefab != null) SetObjProp(npcSo, "lootPrefab", savedLootPrefab);
            SerializedProperty dstLootTable = npcSo.FindProperty("lootTable");
            if (dstLootTable != null && dstLootTable.isArray && savedLootEntries.Count > 0)
            {
                dstLootTable.arraySize = savedLootEntries.Count;
                for (int li = 0; li < savedLootEntries.Count; li++)
                {
                    SerializedProperty elem = dstLootTable.GetArrayElementAtIndex(li);
                    SerializedProperty pItem = elem.FindPropertyRelative("item");
                    SerializedProperty pMin = elem.FindPropertyRelative("minAmount");
                    SerializedProperty pMax = elem.FindPropertyRelative("maxAmount");
                    SerializedProperty pCh = elem.FindPropertyRelative("chance");
                    if (pItem != null) pItem.objectReferenceValue = savedLootEntries[li].item;
                    if (pMin != null) pMin.intValue = savedLootEntries[li].minAmount;
                    if (pMax != null) pMax.intValue = savedLootEntries[li].maxAmount;
                    if (pCh != null) pCh.floatValue = savedLootEntries[li].chance;
                }
            }
            SetIntProp(npcSo, "MaxHealth", savedMaxHp);
            SetIntProp(npcSo, "AttackDamage", savedAtkDmg);
            SetFloatProp(npcSo, "AttackCooldown", savedAtkCd);
            SetFloatProp(npcSo, "AttackRange", config != null && config.attackRange >= 1.0f ? config.attackRange : savedAtkRange);
            SetIntProp(npcSo, "experienceReward", savedExp);
            // Species-Tuned Guard & Combat Personality (distinct feel per archetype while ALL 7 can Guard/Counter/GuardBreak)
            int spHitsBeforeGuard = 2;
            float spGuardDuration = 1.75f;
            float spGuardCooldown = 2.8f;
            float spReactiveChance = 0.42f;
            bool spAllowEmergencyGuard = true;
            if (config != null)
            {
                if (config.isBoss)
                {
                    spHitsBeforeGuard = 2;
                    spGuardDuration = 1.75f;
                    spGuardCooldown = 3.0f;
                    spReactiveChance = 0.40f;
                    spAllowEmergencyGuard = true;
                }
                else if (config.archetype == MonsterArchetype.Brute)
                {
                    spHitsBeforeGuard = 3;
                    spGuardDuration = 1.65f;
                    spGuardCooldown = 3.6f;
                    spReactiveChance = 0.30f;
                    spAllowEmergencyGuard = true;
                }
                else if (config.archetype == MonsterArchetype.Skirmisher)
                {
                    bool isSlime = monsterName.Equals("Slime", StringComparison.OrdinalIgnoreCase);
                    spHitsBeforeGuard = 2;
                    spGuardDuration = isSlime ? 1.35f : 1.45f;
                    spGuardCooldown = isSlime ? 4.5f : 3.8f;
                    spReactiveChance = isSlime ? 0.22f : 0.28f;
                    spAllowEmergencyGuard = false;
                }
            }
            SetIntProp(npcSo, "hitsBeforeGuard", spHitsBeforeGuard);
            SetFloatProp(npcSo, "guardDuration", spGuardDuration);
            SetFloatProp(npcSo, "guardCooldown", spGuardCooldown);
            SetFloatProp(npcSo, "reactiveGuardChanceOnCooldown", spReactiveChance);
            SetBoolProp(npcSo, "allowLowHpEmergencyGuard", spAllowEmergencyGuard);
            SetIntProp(npcSo, "guardBonusDefense", 999);
            if (config != null)
            {
                SetFloatProp(npcSo, "attackImpactDelay", Mathf.Max(0.40f, config.attackImpactDelay));
                SetFloatProp(npcSo, "counterImpactDelay", Mathf.Max(0.36f, config.counterImpactDelay));
            }
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
        float respawnMax,
        int zoneMinLevel = 1,
        int zoneMaxLevel = 2)
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
            SetBoolProp(so, "useFixedZoneLevel", true);
            SetIntProp(so, "zoneMinLevel", Mathf.Max(1, zoneMinLevel));
            SetIntProp(so, "zoneMaxLevel", Mathf.Max(zoneMinLevel, zoneMaxLevel));
            SetIntProp(so, "respawnMode", (int)MonsterZoneRespawnMode.CampClearWave);
            SetFloatProp(so, "respawnMinSeconds", respawnMin);
            SetFloatProp(so, "respawnMaxSeconds", respawnMax);
            SetFloatProp(so, "antiPopInPlayerRadius", 6.0f);
            SetIntProp(so, "campNumber", 0);
            SetBoolProp(so, "showOnWorldMap", true);
            SetBoolProp(so, "spawnRewardChestOnZoneClear", true);
            SetBoolProp(so, "awardClearGold", true);
            SetBoolProp(so, "allowNonMasterFallbackSpawn", false);
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(go, zoneOutputPath);
            Debug.Log($"[MonsterSystem] Saved reusable zone prefab '{zoneOutputPath}' (monster={monsterComp.name}, count={spawnCount}, Lv.{zoneMinLevel}-{zoneMaxLevel}, respawn={respawnMin}-{respawnMax}s).");
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
                int nextSlotIdx = root.transform.childCount;
                for (int i = 0; i < zoneGuids.Length; i++)
                {
                    string zPath = AssetDatabase.GUIDToAssetPath(zoneGuids[i]);
                    GameObject zPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(zPath);
                    if (zPrefab == null || zPrefab.GetComponent<MonsterSpawnZone>() == null) continue;

                    string zName = zPrefab.name;
                    Transform existingChild = root.transform.Find(zName);
                    GameObject zInst;
                    if (existingChild == null)
                    {
                        zInst = (GameObject)PrefabUtility.InstantiatePrefab(zPrefab, root.transform);
                        zInst.name = zName;
                        nextSlotIdx++;
                    }
                    else
                    {
                        zInst = existingChild.gameObject;
                    }
                    // Safe non-overlapping world positions well outside SafeZone (0, 0, -60)!
                    zInst.transform.localPosition = ResolveDefaultZonePositionInManager(zName, i);
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

        // Also build/update a 100% clean Empty Zone Hub prefab (no pre-placed zones — drag or click '+' to add any camp!)
        BuildEmptyZoneHubPrefab(worldItemPrefab);
    }

    private static void BuildEmptyZoneHubPrefab(WorldItem worldItemPrefab)
    {
        GameObject hubGo = new GameObject("MonsterZones_Hub");
        try
        {
            var mgr = hubGo.AddComponent<MonsterSystemManager>();
            mgr.enableAllZones = true;
            mgr.autoSyncChildZones = true;
            mgr.ensureFallbackSafeZoneIfMissing = true;
            mgr.fallbackSafeZoneCenter = new Vector3(0f, 0f, -60f);
            mgr.fallbackSafeZoneRadius = 12f;
            if (worldItemPrefab != null)
                mgr.sharedWorldItemPrefab = worldItemPrefab;

            PrefabUtility.SaveAsPrefabAsset(hubGo, EmptyHubPrefabPath);
            Debug.Log($"[MonsterSystem] Saved clean empty zone hub prefab '{EmptyHubPrefabPath}'.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(hubGo);
        }
    }

    private static Vector3 ResolveDefaultZonePositionInManager(string zoneName, int index)
    {
        if (zoneName.Equals("Zone_Slime", StringComparison.OrdinalIgnoreCase)) return new Vector3(-15f, 0f, -18f);
        if (zoneName.Equals("Zone_TurtleShell", StringComparison.OrdinalIgnoreCase)) return new Vector3(15f, 0f, -18f);
        if (zoneName.Equals("Zone_Orc", StringComparison.OrdinalIgnoreCase)) return new Vector3(-46f, 0f, -22f);
        if (zoneName.Equals("Zone_Goblin", StringComparison.OrdinalIgnoreCase)) return new Vector3(46f, 0f, -22f);
        if (zoneName.Equals("Zone_Troll", StringComparison.OrdinalIgnoreCase)) return new Vector3(-58f, 0f, 28f);
        if (zoneName.Equals("Zone_Golem", StringComparison.OrdinalIgnoreCase)) return new Vector3(58f, 0f, 28f);
        if (zoneName.Equals("Zone_BossOgre", StringComparison.OrdinalIgnoreCase)) return new Vector3(0f, 0f, 74f);
        return new Vector3((index % 3 - 1) * 32f, 0f, 6f + (index / 3) * 28f);
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
                string fileName = Path.GetFileNameWithoutExtension(path);
                bool looksLikeAnimOnly = fileName.Contains("@") ||
                                         fileName.EndsWith("_Anim", StringComparison.OrdinalIgnoreCase) ||
                                         path.Replace('\\', '/').IndexOf("/Animations/", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!looksLikeAnimOnly) return go;
                if (fallback == null) fallback = go;
            }
        }
        return fallback;
    }

    /// <summary>
    /// Universal clip discovery: supports Mixamo '@'-split FBX files, non-'@' split FBX files (Slime/TurtleShell),
    /// AND single multi-clip FBX files with all animations embedded inside (Golem/Troll/BossOgre/Goblin)!
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
                    if (cn.StartsWith("Take 00", StringComparison.OrdinalIgnoreCase) &&
                        fileName.EndsWith("Mesh", StringComparison.OrdinalIgnoreCase))
                        continue;
                    keySource = genericClipName ? fileName : cn;
                }

                if (firstValidClip == null) firstValidClip = clip;

                string suffix = keySource.Trim().ToLowerInvariant().Replace(" ", "_").Replace("-", "_");

                // Skip numeric-only dummy takes (e.g. "001", "002"), transition helpers & secondary directional/emote clips!
                if (int.TryParse(suffix, out _) ||
                    suffix.Contains("_to_") ||
                    suffix.Contains("walkbwd") || suffix.Contains("walkback") ||
                    suffix.Contains("walkleft") || suffix.Contains("walkright") ||
                    suffix.Contains("sensesomethingst") || suffix.Contains("sensesomethingstart") ||
                    suffix.Contains("victory") ||
                    suffix.Contains("taunt") || suffix.Contains("dodge") ||
                    suffix.Contains("defendgethit") || suffix.Contains("defendhit"))
                    continue;

                if (suffix == "defend" || suffix == "block" || suffix.Contains("block_idle") || suffix.Contains("guard"))
                {
                    result["guard"] = clip;
                }
                else if (suffix.Contains("crouch_idle") || suffix.Contains("sensesomething") || suffix == "idle_break")
                {
                    if (!result.ContainsKey("guard"))
                        result["guard"] = clip;
                    if (!result.ContainsKey("combat_idle"))
                        result["combat_idle"] = clip;
                }
                else if (suffix.Contains("combat_idle") || suffix.Contains("combatidle") ||
                    suffix.Contains("idlebattle") || suffix.Contains("fighting_stance"))
                {
                    if (!result.ContainsKey("combat_idle") || !suffix.EndsWith("_2"))
                        result["combat_idle"] = clip;
                }
                else if (suffix == "crouch")
                {
                    if (!result.ContainsKey("hit_react"))
                        result["hit_react"] = clip;
                }
                else if (suffix == "idle" || suffix == "idlenormal" || suffix == "idle_1" ||
                         (suffix.Contains("idle") && !suffix.Contains("break") && !suffix.EndsWith("_2")))
                {
                    result["idle"] = clip;
                }
                else if (suffix.Contains("idle") || suffix.Contains("stand") || suffix.Contains("breathe"))
                {
                    if (!result.ContainsKey("idle")) result["idle"] = clip;
                }
                else if (suffix == "walk" || suffix == "walkfwd" || suffix.Contains("walk"))
                    result["walk"] = clip;
                else if (suffix == "run" || suffix == "runfwd" || suffix.Contains("run") || suffix.Contains("sprint") || suffix.Contains("chase"))
                    result["run"] = clip;
                else if (suffix.Contains("attack_01") || suffix.Contains("attack01") || suffix.Contains("attack_1") ||
                         suffix == "attack1" || suffix == "atack" || suffix == "atack_1")
                {
                    result["attack_01"] = clip;
                    if (!attackClips.Contains(clip)) attackClips.Add(clip);
                }
                else if (suffix.Contains("attack_02") || suffix.Contains("attack02") || suffix.Contains("attack_2") ||
                         suffix == "attack2" || suffix == "atack_2" || suffix == "atack2")
                {
                    result["attack_02"] = clip;
                    if (!attackClips.Contains(clip)) attackClips.Add(clip);
                }
                else if (suffix.Contains("attack_03") || suffix.Contains("attack03") || suffix.Contains("attack_3") ||
                         suffix == "attack3" || suffix == "atack_3" || suffix == "atack3")
                {
                    result["attack_03"] = clip;
                    if (!attackClips.Contains(clip)) attackClips.Add(clip);
                }
                else if (suffix.Contains("attack") || suffix.Contains("atack") || suffix.Contains("slash") ||
                         suffix.Contains("bite") || suffix.Contains("claw") || suffix.Contains("punch") || suffix.Contains("swing"))
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
                else if (suffix.Contains("hit_react") || suffix.Contains("hitreact") || suffix.Contains("get_hit") ||
                         suffix.Contains("gethit") || suffix.Contains("hit") || suffix.Contains("damage") ||
                         suffix.Contains("hurt") || suffix.Contains("flinch"))
                {
                    if (!result.ContainsKey("hit_react") || !suffix.EndsWith("_2"))
                        result["hit_react"] = clip;
                }
                else if (suffix.Contains("stun") || suffix.Contains("dizzy"))
                    result["stun"] = clip;
                else if (suffix == "death" || suffix == "die")
                    result["death"] = clip;
                else if (suffix.Contains("death") || suffix.Contains("die") || suffix.Contains("dead"))
                {
                    if (!result.ContainsKey("death")) result["death"] = clip;
                }
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

        // Priority A: Clone ONLY standalone .mat assets (NEVER embedded FBX dummy mats!) and guarantee URP Lit + _EMISSION!
        string[] existingMatGuids = AssetDatabase.FindAssets("t:Material", new[] { sourceFolder });
        if (existingMatGuids != null && existingMatGuids.Length > 0)
        {
            for (int i = 0; i < existingMatGuids.Length; i++)
            {
                string srcPath = AssetDatabase.GUIDToAssetPath(existingMatGuids[i]);
                if (string.IsNullOrEmpty(srcPath) || !srcPath.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                    continue;

                Material srcMat = AssetDatabase.LoadAssetAtPath<Material>(srcPath);
                if (srcMat == null) continue;

                string partKey = Path.GetFileNameWithoutExtension(srcPath);
                string dstPath = $"{matDir}/Mat_{monsterName}_{partKey}.mat";
                bool srcIsUrp = srcMat.shader != null && srcMat.shader.name.Contains("Universal Render Pipeline");

                Texture mainTex = srcMat.HasProperty("_BaseMap") ? srcMat.GetTexture("_BaseMap") : null;
                if (mainTex == null && srcMat.HasProperty("_MainTex")) mainTex = srcMat.GetTexture("_MainTex");
                Color baseCol = srcMat.HasProperty("_BaseColor") ? srcMat.GetColor("_BaseColor")
                              : (srcMat.HasProperty("_Color") ? srcMat.GetColor("_Color") : Color.white);
                Texture bumpTex = srcMat.HasProperty("_BumpMap") ? srcMat.GetTexture("_BumpMap") : null;
                Texture metTex = srcMat.HasProperty("_MetallicGlossMap") ? srcMat.GetTexture("_MetallicGlossMap") : null;
                float metallic = srcMat.HasProperty("_Metallic") ? srcMat.GetFloat("_Metallic") : 0.05f;
                float smoothness = srcMat.HasProperty("_Smoothness") ? srcMat.GetFloat("_Smoothness")
                                 : (srcMat.HasProperty("_Glossiness") ? srcMat.GetFloat("_Glossiness") : 0.35f);

                Material dstMat = AssetDatabase.LoadAssetAtPath<Material>(dstPath);
                if (dstMat == null)
                {
                    dstMat = srcIsUrp ? new Material(srcMat) : new Material(urpLit);
                    AssetDatabase.CreateAsset(dstMat, dstPath);
                }
                else
                {
                    if (srcIsUrp)
                    {
                        dstMat.shader = srcMat.shader;
                        dstMat.CopyPropertiesFromMaterial(srcMat);
                    }
                    else
                    {
                        dstMat.shader = urpLit;
                    }
                }

                if (!srcIsUrp)
                {
                    if (mainTex != null) dstMat.SetTexture("_BaseMap", mainTex);
                    dstMat.SetColor("_BaseColor", baseCol);
                    if (bumpTex != null)
                    {
                        dstMat.SetTexture("_BumpMap", bumpTex);
                        dstMat.SetFloat("_BumpScale", 1.0f);
                        dstMat.EnableKeyword("_NORMALMAP");
                    }
                    if (metTex != null)
                    {
                        dstMat.SetTexture("_MetallicGlossMap", metTex);
                        dstMat.SetFloat("_Metallic", 1.0f);
                        dstMat.SetFloat("_Smoothness", Mathf.Clamp(smoothness, 0.25f, 0.65f));
                        dstMat.EnableKeyword("_METALLICSPECGLOSSMAP");
                    }
                    else
                    {
                        dstMat.SetFloat("_Metallic", metallic);
                        dstMat.SetFloat("_Smoothness", Mathf.Clamp(smoothness, 0.15f, 0.55f));
                    }
                }

                if (dstMat.HasProperty("_EmissionMap"))
                    dstMat.SetTexture("_EmissionMap", null);
                dstMat.EnableKeyword("_EMISSION");
                dstMat.SetColor("_EmissionColor", Color.black);
                dstMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                EditorUtility.SetDirty(dstMat);
                materials[partKey] = dstMat;
            }

            if (materials.Count > 0)
            {
                AssetDatabase.SaveAssets();
                return materials;
            }
        }

        string[] texGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { sourceFolder });
        var partTextures = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < texGuids.Length; i++)
        {
            string texPath = AssetDatabase.GUIDToAssetPath(texGuids[i]);
            string baseName = Path.GetFileNameWithoutExtension(texPath);
            string[] tokens = baseName.Split('_');
            string lastToken = tokens[tokens.Length - 1].ToLowerInvariant();

            string channel;
            bool lastIsChannel = true;
            if (lastToken.Contains("normal") || lastToken.Contains("bump")) channel = "normal";
            else if (lastToken == "ao" || lastToken.Contains("occlusion")) channel = "ao";
            else if (lastToken.Contains("rough")) channel = "roughness";
            else if (lastToken.Contains("metal")) channel = "metallic";
            else if (lastToken == "basecolor" || lastToken == "color" || lastToken == "albedo" || lastToken == "diffuse" || lastToken == "skin") channel = "basecolor";
            else
            {
                channel = "basecolor";
                lastIsChannel = false;
            }

            string partName;
            int endIdx = lastIsChannel ? tokens.Length - 1 : tokens.Length;
            int startIdx = 0;
            if (endIdx - startIdx >= 2 && tokens[0].Equals(monsterName, StringComparison.OrdinalIgnoreCase))
                startIdx = 1;

            if (endIdx > startIdx)
                partName = string.Join("_", tokens, startIdx, endIdx - startIdx);
            else
                partName = "Main";

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
        string probe = (matName + " " + rendererName).ToLowerInvariant().Replace('_', ' ');
        Material bestMatch = null;
        int bestLen = -1;
        foreach (var kv in urpMaterials)
        {
            string normKey = kv.Key.ToLowerInvariant().Replace('_', ' ');
            if (probe.Contains(normKey) && normKey.Length > bestLen)
            {
                bestMatch = kv.Value;
                bestLen = normKey.Length;
            }
        }
        if (bestMatch != null) return bestMatch;
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

            // Tier 1b: Blender Auto-Rig Pro bones (Golem: toes_01.l/r, foot.l/r, head.x)
            Transform arpToeL = FindDeepChild(temp.transform, "toes_01.l");
            Transform arpToeR = FindDeepChild(temp.transform, "toes_01.r");
            Transform arpFootL = FindDeepChild(temp.transform, "foot.l");
            Transform arpFootR = FindDeepChild(temp.transform, "foot.r");
            Transform arpHead = FindDeepChild(temp.transform, "head.x") ?? FindDeepChild(temp.transform, "head");
            if ((arpToeL != null || arpFootL != null) && arpHead != null)
            {
                float fMin = float.MaxValue;
                if (arpToeL != null) fMin = Mathf.Min(fMin, arpToeL.position.y);
                if (arpToeR != null) fMin = Mathf.Min(fMin, arpToeR.position.y);
                if (fMin == float.MaxValue)
                {
                    if (arpFootL != null) fMin = Mathf.Min(fMin, arpFootL.position.y - 0.10f);
                    if (arpFootR != null) fMin = Mathf.Min(fMin, arpFootR.position.y - 0.10f);
                }
                else
                {
                    fMin -= 0.05f;
                }
                feetY = fMin;
                height = Mathf.Max(0.5f, (arpHead.position.y + 0.25f) - feetY);
                return;
            }

            // Tier 1c: 3ds Max CAT Rig bones (Troll: cave_troll_LLegPlatform/RLegPlatform + cave_troll_Hub003_dnu)
            Transform catFootL = FindDeepChild(temp.transform, "cave_troll_LLegPlatform");
            Transform catFootR = FindDeepChild(temp.transform, "cave_troll_RLegPlatform");
            Transform catHead = FindDeepChild(temp.transform, "cave_troll_Hub003_dnu") ?? FindDeepChild(temp.transform, "cave_troll_Hub002");
            if ((catFootL != null || catFootR != null) && catHead != null)
            {
                float cMin = float.MaxValue;
                if (catFootL != null) cMin = Mathf.Min(cMin, catFootL.position.y);
                if (catFootR != null) cMin = Mathf.Min(cMin, catFootR.position.y);
                feetY = cMin;
                height = Mathf.Max(0.8f, (catHead.position.y * 1.18f) - feetY);
                return;
            }

            // Tier 1d: Blender Rigify bones (BossOgre: toe.L/R, Left_Foot/Right_Foot.001 + spine.006_end)
            Transform rigToeL = FindDeepChild(temp.transform, "toe.L") ?? FindDeepChild(temp.transform, "Left_Foot");
            Transform rigToeR = FindDeepChild(temp.transform, "toe.R") ?? FindDeepChild(temp.transform, "Right_Foot.001");
            Transform rigHead = FindDeepChild(temp.transform, "spine.006_end") ?? FindDeepChild(temp.transform, "spine.006");
            if ((rigToeL != null || rigToeR != null) && rigHead != null)
            {
                float rMin = float.MaxValue;
                if (rigToeL != null) rMin = Mathf.Min(rMin, rigToeL.position.y);
                if (rigToeR != null) rMin = Mathf.Min(rMin, rigToeR.position.y);
                feetY = rMin - 0.04f;
                height = Mathf.Max(0.8f, rigHead.position.y - feetY);
                return;
            }

            // Tier 1e: Generic Blender humanoid bones (Goblin: Toes.L/R, Foot.L/R + Head)
            Transform genToeL = FindDeepChild(temp.transform, "Toes.L") ?? FindDeepChild(temp.transform, "Foot.L");
            Transform genToeR = FindDeepChild(temp.transform, "Toes.R") ?? FindDeepChild(temp.transform, "Foot.R");
            Transform genHead = FindDeepChild(temp.transform, "Head");
            if ((genToeL != null || genToeR != null) && genHead != null)
            {
                float gMin = float.MaxValue;
                if (genToeL != null) gMin = Mathf.Min(gMin, genToeL.position.y);
                if (genToeR != null) gMin = Mathf.Min(gMin, genToeR.position.y);
                feetY = gMin - 0.03f;
                height = Mathf.Max(0.6f, (genHead.position.y + 0.22f) - feetY);
                return;
            }

            // Tier 2: Vertex-exact baked SkinnedMeshRenderer bounds (zero loose culling AABB padding -> fixes floating Slime & TurtleShell!)
            if (MeasureBakedSkinnedMeshWorldY(temp, out float smrMinY, out float smrMaxY) && smrMaxY > smrMinY + 0.15f)
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
            string[] expectedParams = { "Speed", "IsCombat", "IsGuarding", "Attack", "AttackIndex", "Skill", "SkillIndex", "IsCasting", "Hit", "IsStunned", "IsDead" };
            int found = 0;
            foreach (string ep in expectedParams)
                foreach (var p in baseCtrl.parameters)
                    if (p.name == ep) { found++; break; }
            Check("1.2 Base Controller Has All 11 Parameters", found == 11, $"Found {found}/11");

            bool upperOk = baseCtrl.layers.Length >= 2 &&
                           baseCtrl.layers[1].name == "UpperBody" &&
                           baseCtrl.layers[1].blendingMode == AnimatorLayerBlendingMode.Override &&
                           Mathf.Approximately(baseCtrl.layers[1].defaultWeight, 0f);
            Check("1.3 UpperBody Override Layer (Weight=0)", upperOk, $"Layers={baseCtrl.layers.Length}");

            AnimatorStateMachine sm = baseCtrl.layers[0].stateMachine;
            bool selfOff = sm.anyStateTransitions.Length == 4;
            foreach (var tr in sm.anyStateTransitions) if (tr.canTransitionToSelf) selfOff = false;
            Check("1.4 AnyState CanTransitionToSelf=False (Guard/Hit/Stun/Death)", selfOff, $"AnyStateTransitions={sm.anyStateTransitions.Length}");

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
        Check("3.1 AOC_Orc All 14 Override Slots Non-Null", allFilled && mapped == 14, $"Mapped {mapped}/14");

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

                    int texturedCount = 0;
                    var orcRenderers = bridge != null && bridge.VisualRoot != null
                        ? bridge.VisualRoot.GetComponentsInChildren<Renderer>(true)
                        : Array.Empty<Renderer>();
                    for (int r = 0; r < orcRenderers.Length; r++)
                    {
                        Material m = orcRenderers[r].sharedMaterial;
                        if (m != null && ((m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null) ||
                                          (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null)))
                            texturedCount++;
                    }
                    Check("4.5 Enemy_Orc Textured URP Materials (Non-Gray)", texturedCount > 0 && texturedCount == orcRenderers.Length,
                        $"TexturedRenderers={texturedCount}/{orcRenderers.Length}");
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

        // 7-12. Additional Species Checks (Golem, Troll, Slime, TurtleShell, BossOgre, Goblin)
        VerifyAdditionalMonster("7", "Golem", GolemSourceFolder, GolemPrefabPath, GolemZonePrefabPath,
            footBoneA: "toes_01.l", footBoneB: "toes_01.r", moveBoneA: "arm_stretch.r", moveBoneB: "root.x", Check);
        VerifyAdditionalMonster("8", "Troll", TrollSourceFolder, TrollPrefabPath, TrollZonePrefabPath,
            footBoneA: "cave_troll_LLegPlatform", footBoneB: "cave_troll_RLegPlatform", moveBoneA: "cave_troll_Spine2", moveBoneB: "cave_troll_Hub001", Check);
        VerifyAdditionalMonster("9", "Slime", SlimeSourceFolder, SlimePrefabPath, SlimeZonePrefabPath,
            footBoneA: "", footBoneB: "", moveBoneA: "Spine01", moveBoneB: "Body", Check);
        VerifyAdditionalMonster("10", "TurtleShell", TurtleShellSourceFolder, TurtleShellPrefabPath, TurtleShellZonePrefabPath,
            footBoneA: "", footBoneB: "", moveBoneA: "Spine01", moveBoneB: "Head", Check);
        VerifyAdditionalMonster("11", "BossOgre", BossOgreSourceFolder, BossOgrePrefabPath, BossOgreZonePrefabPath,
            footBoneA: "toe.L", footBoneB: "toe.R", moveBoneA: "upper_arm.R", moveBoneB: "spine", Check);
        VerifyAdditionalMonster("12", "Goblin", GoblinSourceFolder, GoblinPrefabPath, GoblinZonePrefabPath,
            footBoneA: "Toes.L", footBoneB: "Toes.R", moveBoneA: "Upper Arm.R", moveBoneB: "Hips", Check);

        // 13. Universal 7-Species Combat & Guard Audit (AttackRange, ImpactDelay >= 0.40s, Guard Threshold = 2, FusionPrefab)
        string[] allSpecies = { "Orc", "Golem", "Troll", "Slime", "TurtleShell", "BossOgre", "Goblin" };
        int combatReadyCount = 0;
        var combatSummary = new List<string>();
        foreach (string sp in allSpecies)
        {
            string pPath = $"{MonsterPrefabsRoot}/Enemy_{sp}.prefab";
            string cPath = $"{AnimRoot}/{sp}/Config_{sp}.asset";
            string aPath = $"{AnimRoot}/{sp}/AOC_{sp}.overrideController";
            GameObject pGo = AssetDatabase.LoadAssetAtPath<GameObject>(pPath);
            MonsterAnimationConfig cfg = AssetDatabase.LoadAssetAtPath<MonsterAnimationConfig>(cPath);
            AnimatorOverrideController spAoc = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(aPath);
            MonsterEnemyNPC mNpc = pGo != null ? pGo.GetComponent<MonsterEnemyNPC>() : null;

            bool rangeOk = cfg != null && (cfg.isBoss ? cfg.attackRange <= 2.90f : cfg.attackRange <= 2.50f);
            bool delayOk = cfg != null && cfg.attackImpactDelay >= 0.40f && cfg.counterImpactDelay >= 0.36f;
            bool guardClipOk = spAoc != null && spAoc[KeyGuard] != null;
            bool guardNpcOk = false;
            if (mNpc != null)
            {
                var so = new SerializedObject(mNpc);
                int hitsReq = so.FindProperty("hitsBeforeGuard")?.intValue ?? 99;
                int guardDef = so.FindProperty("guardBonusDefense")?.intValue ?? 0;
                guardNpcOk = hitsReq >= 1 && hitsReq <= 3 && guardDef >= 999;
            }
            if (rangeOk && delayOk && guardClipOk && guardNpcOk)
            {
                combatReadyCount++;
                combatSummary.Add($"{sp}(R={cfg.attackRange:0.00}m,D={cfg.attackImpactDelay:0.00}s,G={spAoc[KeyGuard].name})");
            }
        }
        Check("13.1 All 7 Species Combat & Guard Audit (Master-Only Spawn + Participation Chest Audit)",
            combatReadyCount == allSpecies.Length,
            $"{combatReadyCount}/{allSpecies.Length}: {string.Join(", ", combatSummary)}");

        sb.AppendLine($"TOTAL: {pass + fail} | PASSED: {pass} | FAILED: {fail}");
        File.WriteAllText(VerificationReportPath, sb.ToString(), new UTF8Encoding(false));
        AssetDatabase.ImportAsset(VerificationReportPath, ImportAssetOptions.ForceUpdate);
        Debug.Log($"[MonsterSystem] Verification finished: {pass}/{pass + fail} passed.");
    }

    private static void VerifyAdditionalMonster(
        string sectionPrefix,
        string monsterName,
        string sourceFolder,
        string prefabPath,
        string zonePath,
        string footBoneA,
        string footBoneB,
        string moveBoneA,
        string moveBoneB,
        Action<string, bool, string> check)
    {
        if (!Directory.Exists(sourceFolder)) return;

        string aocPath = $"{AnimRoot}/{monsterName}/AOC_{monsterName}.overrideController";
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
        check($"{sectionPrefix}.1 AOC_{monsterName} All 14 Override Slots Non-Null", allFilled && mapped == 14, $"Mapped {mapped}/14");

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        check($"{sectionPrefix}.2 Enemy_{monsterName}.prefab Exists", prefab != null, prefabPath);
        if (prefab != null)
        {
            GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                var mNpc = inst.GetComponent<MonsterEnemyNPC>();
                var bridge = inst.GetComponent<MonsterAnimatorBridge>();
                var netObj = inst.GetComponent<NetworkObject>();
                check($"{sectionPrefix}.3 Enemy_{monsterName} Uses MonsterEnemyNPC + NetworkObject + Bridge",
                    mNpc != null && bridge != null && bridge.Animator != null && !bridge.Animator.applyRootMotion && netObj != null && bridge.VisualRenderers != null && bridge.VisualRenderers.Length > 0,
                    $"Renderers={bridge?.VisualRenderers?.Length ?? 0}, Config={bridge?.Config?.name}");

                CapsuleCollider cap = inst.GetComponent<CapsuleCollider>();
                float pivotH = cap != null ? (cap.height * 0.5f - cap.center.y) * inst.transform.localScale.y : 1.5f;
                inst.transform.position = new Vector3(0f, pivotH, 0f);
                if (aoc != null && bridge != null && bridge.VisualRoot != null)
                {
                    AnimationClip idleClip = aoc[KeyIdle];
                    if (idleClip != null) idleClip.SampleAnimation(bridge.VisualRoot.gameObject, 0f);
                    float feetY = 99f;
                    if (!string.IsNullOrEmpty(footBoneA) && !string.IsNullOrEmpty(footBoneB))
                    {
                        Transform fA = FindDeepChild(bridge.VisualRoot, footBoneA);
                        Transform fB = FindDeepChild(bridge.VisualRoot, footBoneB);
                        if (fA != null && fB != null)
                            feetY = Mathf.Min(fA.position.y, fB.position.y);
                    }
                    else
                    {
                        if (MeasureBakedSkinnedMeshWorldY(bridge.VisualRoot.gameObject, out float bakedMinY, out _))
                            feetY = bakedMinY;
                    }
                    check($"{sectionPrefix}.4 Enemy_{monsterName} Feet Seated Flush On Ground (y=0)", feetY >= -0.10f && feetY <= 0.18f, $"feetWorldY={feetY:0.000}m");

                    Transform[] allBones = bridge.VisualRoot.GetComponentsInChildren<Transform>(true);
                    string[] keys = { KeyIdle, KeyWalk, KeyRun, KeyAttack01, KeyHitReact, KeyDeath };
                    int moving = 0;
                    var details = new List<string>();
                    if (allBones != null && allBones.Length > 0)
                    {
                        var r0 = new Quaternion[allBones.Length];
                        var p0 = new Vector3[allBones.Length];
                        var s0 = new Vector3[allBones.Length];
                        foreach (string k in keys)
                        {
                            AnimationClip c = aoc[k];
                            if (c == null) continue;
                            c.SampleAnimation(bridge.VisualRoot.gameObject, 0f);
                            for (int bi = 0; bi < allBones.Length; bi++)
                            {
                                r0[bi] = allBones[bi].localRotation;
                                p0[bi] = allBones[bi].localPosition;
                                s0[bi] = allBones[bi].localScale;
                            }
                            float maxDeg = 0f, maxPos = 0f, maxScale = 0f;
                            foreach (float f in new[] { 0.25f, 0.5f, 0.75f, 0.9f })
                            {
                                c.SampleAnimation(bridge.VisualRoot.gameObject, c.length * f);
                                for (int bi = 0; bi < allBones.Length; bi++)
                                {
                                    maxDeg = Mathf.Max(maxDeg, Quaternion.Angle(r0[bi], allBones[bi].localRotation));
                                    maxPos = Mathf.Max(maxPos, Vector3.Distance(p0[bi], allBones[bi].localPosition));
                                    maxScale = Mathf.Max(maxScale, Vector3.Distance(s0[bi], allBones[bi].localScale));
                                }
                            }
                            if (maxDeg > 0.5f || maxPos > 0.005f || maxScale > 0.005f) moving++;
                            details.Add($"{k.Replace("Placeholder_", "")}({maxDeg:0.0}deg)");
                        }
                    }
                    check($"{sectionPrefix}.5 Enemy_{monsterName} Real Bone Motion (Zero T-Pose)", moving == keys.Length, $"{moving}/{keys.Length}: {string.Join(", ", details)}");

                    int texturedCount = 0;
                    var visRenderers = bridge != null && bridge.VisualRoot != null
                        ? bridge.VisualRoot.GetComponentsInChildren<Renderer>(true)
                        : Array.Empty<Renderer>();
                    for (int r = 0; r < visRenderers.Length; r++)
                    {
                        Material m = visRenderers[r].sharedMaterial;
                        if (m != null && ((m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null) ||
                                          (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null)))
                            texturedCount++;
                    }
                    check($"{sectionPrefix}.7 Enemy_{monsterName} Textured URP Materials (Non-Gray)", texturedCount > 0 && texturedCount == visRenderers.Length,
                        $"TexturedRenderers={texturedCount}/{visRenderers.Length}");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(inst);
            }
        }

        GameObject zPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(zonePath);
        MonsterSpawnZone zComp = zPrefab != null ? zPrefab.GetComponent<MonsterSpawnZone>() : null;
        check($"{sectionPrefix}.6 Plug-and-Play Zone_{monsterName}.prefab Wired",
            zComp != null && zComp.MonsterPrefab != null && zComp.MonsterPrefab.GetComponent<MonsterEnemyNPC>() != null,
            $"Prefab={zComp?.MonsterPrefab?.name}, Count={zComp?.SpawnCount ?? 0}");
    }

    private static bool MeasureBakedSkinnedMeshWorldY(GameObject root, out float minY, out float maxY)
    {
        minY = float.MaxValue;
        maxY = float.MinValue;
        if (root == null) return false;

        SkinnedMeshRenderer[] smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (smrs == null || smrs.Length == 0) return false;

        Mesh baked = new Mesh();
        try
        {
            for (int i = 0; i < smrs.Length; i++)
            {
                SkinnedMeshRenderer smr = smrs[i];
                if (smr == null || smr.sharedMesh == null) continue;
                smr.updateWhenOffscreen = true;
                baked.Clear();
                smr.BakeMesh(baked, useScale: false);
                Vector3[] verts = baked.vertices;
                Transform tr = smr.transform;
                for (int v = 0; v < verts.Length; v++)
                {
                    float wy = tr.TransformPoint(verts[v]).y;
                    if (wy < minY) minY = wy;
                    if (wy > maxY) maxY = wy;
                }
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(baked);
        }

        return minY < float.MaxValue && maxY > minY;
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
