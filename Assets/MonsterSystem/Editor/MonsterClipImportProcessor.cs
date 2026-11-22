#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Self-contained Monster FBX & PBR Texture Import Processor (lives in Assets/MonsterSystem/Editor).
/// Targets <c>Assets/MonsterSystem/Models/</c> (primary portable root) and <c>Assets/Characters/Monsters/</c>.
///
/// Enforces:
///   1. Generic Rig (<see cref="ModelImporterAnimationType.Generic"/>) + <see cref="ModelImporterAvatarSetup.NoAvatar"/>.
///   2. In-Place root locks (Rotation, Height Y, Horizontal XZ).
///   3. Automatic Loop Time based on clip/file names.
///   4. PBR Texture types (NormalMap for *_Normal.*, non-sRGB for AO/Roughness/Metalness).
/// </summary>
public class MonsterClipImportProcessor : AssetPostprocessor
{
    private static readonly string[] MonsterRoots =
    {
        "Assets/MonsterSystem/Models/",
        "Assets/Characters/Monsters/"
    };

    private static readonly string[] LoopKeywords =
    {
        "combat_idle",
        "combatidle",
        "fighting_stance",
        "fightingstance",
        "idlebattle",
        "idlenormal",
        "skill_cast",
        "skillcast",
        "evade_run",
        "evaderun",
        "defend",
        "block",
        "dizzy",
        "walking",
        "walk",
        "idle",
        "run",
        "stun"
    };

    private static readonly string[] NonLoopKeywords =
    {
        "skill_release",
        "skillrelease",
        "hit_react",
        "hitreact",
        "get_hit",
        "gethit",
        "defendgethit",
        "defendhit",
        "take_damage",
        "damage",
        "hurt",
        "flinch",
        "knockdown",
        "idle_to_walk",
        "attack_01",
        "attack_02",
        "attack_03",
        "attack01",
        "attack02",
        "attack03",
        "attack1",
        "attack2",
        "attack3",
        "attack",
        "atack_3",
        "atack_2",
        "atack",
        "dodge",
        "spawn",
        "death",
        "die",
        "hit"
    };

    public static bool IsMonsterAssetPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        string normalized = path.Replace('\\', '/');
        for (int i = 0; i < MonsterRoots.Length; i++)
        {
            if (normalized.StartsWith(MonsterRoots[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static bool ShouldClipLoop(string clipOrFileName)
    {
        if (string.IsNullOrEmpty(clipOrFileName)) return false;

        string lower = clipOrFileName.ToLowerInvariant().Trim();
        int pipeIdx = lower.LastIndexOf('|');
        if (pipeIdx >= 0 && pipeIdx + 1 < lower.Length)
            lower = lower.Substring(pipeIdx + 1).Trim();
        int atIdx = lower.IndexOf('@');
        if (atIdx >= 0 && atIdx + 1 < lower.Length)
            lower = lower.Substring(atIdx + 1).Trim();

        string compact = lower.Replace(" ", "_").Replace("-", "_");

        for (int i = 0; i < NonLoopKeywords.Length; i++)
        {
            if (compact.Contains(NonLoopKeywords[i]))
                return false;
        }

        for (int i = 0; i < LoopKeywords.Length; i++)
        {
            if (compact.Contains(LoopKeywords[i]))
                return true;
        }

        return false;
    }

    private void OnPreprocessModel()
    {
        if (!IsMonsterAssetPath(assetPath)) return;
        if (!assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) return;

        var importer = assetImporter as ModelImporter;
        if (importer == null) return;

        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
        importer.importAnimation = true;
    }

    private void OnPreprocessAnimation()
    {
        if (!IsMonsterAssetPath(assetPath)) return;
        if (!assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) return;

        var importer = assetImporter as ModelImporter;
        if (importer == null) return;

        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0)
            clips = importer.defaultClipAnimations;

        if (clips == null || clips.Length == 0) return;

        string fileBaseName = Path.GetFileNameWithoutExtension(assetPath);
        bool fileHasAt = fileBaseName.Contains("@");

        for (int i = 0; i < clips.Length; i++)
        {
            ModelImporterClipAnimation clip = clips[i];
            string keyName = fileHasAt ? fileBaseName : (!string.IsNullOrEmpty(clip.name) ? clip.name : fileBaseName);

            if (fileHasAt && (string.IsNullOrEmpty(clip.name) || clip.name.Equals("mixamo.com", StringComparison.OrdinalIgnoreCase)))
                clip.name = fileBaseName;

            bool shouldLoop = ShouldClipLoop(keyName);

            clip.loopTime = shouldLoop;
            clip.loopPose = shouldLoop;
            clip.lockRootRotation = true;
            clip.lockRootHeightY = true;
            clip.lockRootPositionXZ = true;
            clip.keepOriginalOrientation = true;
            clip.keepOriginalPositionY = true;
            clip.keepOriginalPositionXZ = true;

            clips[i] = clip;
        }

        importer.clipAnimations = clips;
    }

    private void OnPreprocessTexture()
    {
        if (!IsMonsterAssetPath(assetPath)) return;

        var importer = assetImporter as TextureImporter;
        if (importer == null) return;

        string lower = Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();
        if (lower.Contains("normal"))
        {
            importer.textureType = TextureImporterType.NormalMap;
        }
        else if (lower.Contains("roughness") || lower.Contains("metalness") || lower.Contains("metallic") || lower.EndsWith("_ao") || lower.Contains("_ao_"))
        {
            importer.sRGBTexture = false;
        }
    }
}
#endif
