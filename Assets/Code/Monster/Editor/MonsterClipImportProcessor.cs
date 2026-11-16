using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Automatically enforces MMO Monster Rig & Animation Clip Import Standards:
/// 1. Animation Type = Generic (never Humanoid, saving CPU & supporting non-humanoid bones).
/// 2. Avatar Setup = NoAvatar so Generic transform path bindings (e.g. mixamorig:Hips/...) match 100% across separate FBXs.
/// 3. In-Place (Root Motion / Bake Simulation disabled).
/// 4. Loop Time = ON for Idle, Walk, Run, Combat_Idle, Skill_Cast, Stun, Evade_Run.
/// 5. Loop Time = OFF for Spawn, Attack_01..03, Skill_Release, Hit_React, Knockdown, Death.
/// </summary>
public sealed class MonsterClipImportProcessor : AssetPostprocessor
{
    private static readonly string[] TargetFolderKeywords =
    {
        "Model Enemy",
        "Monster",
        "Monsters",
        "Enemies",
        "Enemy"
    };

    private static readonly string[] LoopKeywords =
    {
        "combat_idle",
        "combatidle",
        "skill_cast",
        "skillcast",
        "evade_run",
        "evaderun",
        "idle",
        "walk",
        "run",
        "cast",
        "stun",
        "evade"
    };

    private static readonly string[] NonLoopKeywords =
    {
        "attack_01",
        "attack_02",
        "attack_03",
        "attack1",
        "attack2",
        "attack3",
        "attack",
        "skill_release",
        "skillrelease",
        "skill1",
        "skill2",
        "skill",
        "hit_react",
        "hitreact",
        "hit",
        "knockdown",
        "death",
        "die",
        "dead",
        "spawn"
    };

    public static bool AutoProcessEnabled = true;

    private void OnPreprocessModel()
    {
        if (!AutoProcessEnabled || !IsMonsterAssetPath(assetPath))
        {
            return;
        }

        ModelImporter importer = assetImporter as ModelImporter;
        if (importer == null)
        {
            return;
        }

        // 1. Always enforce Generic rig with NoAvatar so separate FBXs bind directly by bone path.
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
        importer.optimizeGameObjects = false;
    }

    private void OnPreprocessAnimation()
    {
        if (!AutoProcessEnabled || !IsMonsterAssetPath(assetPath))
        {
            return;
        }

        ModelImporter importer = assetImporter as ModelImporter;
        if (importer == null)
        {
            return;
        }

        string fileName = Path.GetFileNameWithoutExtension(assetPath);
        if (string.IsNullOrEmpty(fileName))
        {
            return;
        }

        bool? shouldLoop = DetermineLoopSetting(fileName);
        if (!shouldLoop.HasValue)
        {
            return;
        }

        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0)
        {
            clips = importer.defaultClipAnimations;
        }

        if (clips == null || clips.Length == 0)
        {
            return;
        }

        bool loop = shouldLoop.Value;
        for (int i = 0; i < clips.Length; i++)
        {
            ModelImporterClipAnimation clip = clips[i];

            if (clips.Length == 1)
            {
                clip.name = fileName;
            }

            clip.loopTime = loop;
            clip.loopPose = loop;
            clip.lockRootRotation = true;
            clip.lockRootHeightY = true;
            clip.lockRootPositionXZ = true;
            clip.keepOriginalOrientation = true;
            clip.keepOriginalPositionY = true;
            clip.keepOriginalPositionXZ = true;

            clips[i] = clip;
        }

        importer.clipAnimations = clips;
        importer.resampleCurves = true;
        importer.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
    }

    public static bool IsMonsterAssetPath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        if (!path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (path.IndexOf("MyCharacter", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return false;
        }

        for (int i = 0; i < TargetFolderKeywords.Length; i++)
        {
            if (path.IndexOf(TargetFolderKeywords[i], StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    public static bool? DetermineLoopSetting(string fileOrClipName)
    {
        if (string.IsNullOrEmpty(fileOrClipName))
        {
            return null;
        }

        string lower = fileOrClipName.ToLowerInvariant();

        for (int i = 0; i < NonLoopKeywords.Length; i++)
        {
            if (MatchesKeyword(lower, NonLoopKeywords[i]))
            {
                return false;
            }
        }

        for (int i = 0; i < LoopKeywords.Length; i++)
        {
            if (MatchesKeyword(lower, LoopKeywords[i]))
            {
                return true;
            }
        }

        return null;
    }

    private static bool MatchesKeyword(string lowerName, string keyword)
    {
        return lowerName == keyword ||
               lowerName.EndsWith("_" + keyword, StringComparison.Ordinal) ||
               lowerName.EndsWith("-" + keyword, StringComparison.Ordinal) ||
               lowerName.StartsWith(keyword + "_", StringComparison.Ordinal) ||
               lowerName.Contains(keyword);
    }

    public static int ReimportMonsterFolder(string folderAssetPath)
    {
        if (string.IsNullOrEmpty(folderAssetPath) || !AssetDatabase.IsValidFolder(folderAssetPath))
        {
            Debug.LogWarning($"[MonsterClipImportProcessor] Folder not found: {folderAssetPath}");
            return 0;
        }

        string[] guids = AssetDatabase.FindAssets("t:Model", new[] { folderAssetPath });
        int count = 0;

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (!path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                continue;
            }

            bool dirty = false;
            if (importer.animationType != ModelImporterAnimationType.Generic)
            {
                importer.animationType = ModelImporterAnimationType.Generic;
                dirty = true;
            }

            if (importer.avatarSetup != ModelImporterAvatarSetup.NoAvatar)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
                dirty = true;
            }

            string fileName = Path.GetFileNameWithoutExtension(path);
            bool? loopSetting = DetermineLoopSetting(fileName);

            if (loopSetting.HasValue)
            {
                ModelImporterClipAnimation[] clips = importer.clipAnimations;
                if (clips == null || clips.Length == 0)
                {
                    clips = importer.defaultClipAnimations;
                    dirty = true;
                }

                if (clips != null && clips.Length > 0)
                {
                    bool loop = loopSetting.Value;
                    for (int c = 0; c < clips.Length; c++)
                    {
                        ModelImporterClipAnimation clip = clips[c];
                        if (clips.Length == 1 && clip.name != fileName)
                        {
                            clip.name = fileName;
                            dirty = true;
                        }

                        if (clip.loopTime != loop || clip.loopPose != loop)
                        {
                            clip.loopTime = loop;
                            clip.loopPose = loop;
                            dirty = true;
                        }

                        clip.lockRootRotation = true;
                        clip.lockRootHeightY = true;
                        clip.lockRootPositionXZ = true;
                        clip.keepOriginalOrientation = true;
                        clip.keepOriginalPositionY = true;
                        clip.keepOriginalPositionXZ = true;
                        clips[c] = clip;
                    }

                    importer.clipAnimations = clips;
                }
            }

            if (dirty)
            {
                importer.SaveAndReimport();
            }
            count++;
        }

        AssetDatabase.SaveAssets();
        return count;
    }

    [MenuItem("Tools/MMO Animation/Reimport & Fix Clips in 'Model Enemy'", priority = 20)]
    public static void ReimportModelEnemyFolderMenu()
    {
        const string modelEnemyPath = "Assets/Characters/Model Enemy";
        if (!AssetDatabase.IsValidFolder(modelEnemyPath))
        {
            return;
        }

        int processed = ReimportMonsterFolder(modelEnemyPath);
        Debug.Log($"[MonsterClipImportProcessor] Configured Generic Rig + Loop Time on {processed} FBX file(s) in '{modelEnemyPath}'.");
    }
}
