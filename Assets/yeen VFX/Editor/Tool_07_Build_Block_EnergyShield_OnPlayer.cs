#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// เครื่องมือสร้างระบบเอฟเฟกต์ "รูปโล่ป้องกัน 3 มิติ (Sculpted Energy Guard Shield VFX)" สำหรับ Unity 6 (URP)
/// สร้าง 3D Mesh รูปโล่โค้ง, วาด Texture รูปโล่อัศวินพร้อมลายรังผึ้งและตราสัญลักษณ์ตรงกลาง (512x512 px)
/// และติดตั้งลงบนตัวละคร (ไม่มีเอฟเฟกต์สะท้อนหรือสะเก็ดไฟกระเด็น)
/// </summary>
public class Tool_07_Build_Block_EnergyShield_OnPlayer : EditorWindow
{
    private const string OutputFolder = "Assets/yeen VFX/03_VFX_Prefabs_And_Assets/Generated_BlockShield";
    private const string ShieldMeshAssetPath = "Assets/yeen VFX/03_VFX_Prefabs_And_Assets/Generated_BlockShield/MESH_Block_GuardShield.asset";
    private const string ShieldEmblemTexPath = "Assets/yeen VFX/03_VFX_Prefabs_And_Assets/Generated_BlockShield/TEX_Block_EnergyShield_512.png";
    private const string ShieldGlowTexPath = "Assets/yeen VFX/03_VFX_Prefabs_And_Assets/Generated_BlockShield/TEX_Block_ShieldBackGlow.png";

    private const string ShieldMatPath = "Assets/yeen VFX/03_VFX_Prefabs_And_Assets/Generated_BlockShield/MAT_Block_EnergyShield.mat";
    private const string ShieldGlowMatPath = "Assets/yeen VFX/03_VFX_Prefabs_And_Assets/Generated_BlockShield/MAT_Block_ShieldBackGlow.mat";

    [MenuItem("yeen VFX/07. [ใส่บน Player] สร้างเอฟเฟกต์บล็อครูปโล่แสง 3 มิติ (Block Guard Shield)")]
    public static void GenerateGuardShieldVFX()
    {
        EnsureOutputDirectory();

        // 1. สร้างและบันทึก 3D Mesh รูปโล่โค้ง
        Mesh shieldMesh = SaveProceduralShieldMesh(ShieldMeshAssetPath);

        // 2. วาดและบันทึก Texture รูปโล่พลังงาน 512x512 px และแสงเรืองขอบหลังโล่
        Texture2D shieldEmblemTex = SaveTextureAsset(MMOBlockParryController.CreateHeraldicShieldEmblemTexture(512), ShieldEmblemTexPath);
        Texture2D shieldGlowTex = SaveTextureAsset(MMOBlockParryController.CreateShieldBackGlowTexture(256), ShieldGlowTexPath);

        // 3. สร้าง Materials ที่แสดงผลชัดเจน 100% ใน Unity 6 URP
        Material shieldMat = CreateGuaranteedURPMaterial(ShieldMatPath, shieldEmblemTex, new Color(0.32f, 0.84f, 1.0f, 0.86f));
        Material shieldGlowMat = CreateGuaranteedURPMaterial(ShieldGlowMatPath, shieldGlowTex, new Color(0.25f, 0.78f, 1.0f, 0.42f));

        List<string> installedTargets = new List<string>();

        GameObject targetCharacter = ResolveSelectedOrSceneCharacter();
        if (targetCharacter != null)
        {
            BuildSculptedGuardShieldOnCharacter(targetCharacter, shieldMesh, shieldMat, shieldGlowMat);
            installedTargets.Add("Scene Character: " + targetCharacter.name);
        }

        string[] mainPrefabs = new string[]
        {
            "Assets/Organ/Models/Prefab/GreatSwordPrefab/GreatSwordGirl.prefab",
            "Assets/_Project/Prefabs/Player.prefab"
        };

        for (int i = 0; i < mainPrefabs.Length; i++)
        {
            string prefabPath = mainPrefabs[i];
            if (File.Exists(prefabPath))
            {
                GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
                if (prefabRoot != null)
                {
                    BuildSculptedGuardShieldOnCharacter(prefabRoot, shieldMesh, shieldMat, shieldGlowMat);
                    PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                    PrefabUtility.UnloadPrefabContents(prefabRoot);
                    installedTargets.Add("Prefab: " + Path.GetFileName(prefabPath));
                }
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (!Application.isPlaying && UnityEngine.SceneManagement.SceneManager.GetActiveScene().IsValid())
        {
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }

        EditorUtility.DisplayDialog(
            "สร้างเอฟเฟกต์รูปโล่ป้องกัน 3 มิติ สำเร็จ!",
            "ดำเนินการสร้างรูปโล่ป้องกัน 3 มิติ (ไม่มีเอฟเฟกต์สะท้อนกระเด็น) เรียบร้อยแล้ว:\n\n" +
            "• " + string.Join("\n• ", installedTargets.ToArray()) + "\n\n" +
            "กด Play แล้วกด [คลิกขวาเมาส์] หรือปุ่ม [F] ค้างไว้ เพื่อดูรูปโล่ป้องกันด้านหน้าตัวละครได้เลยครับ",
            "รับทราบ"
        );
    }

    private static GameObject ResolveSelectedOrSceneCharacter()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected != null)
        {
            Animator anim = selected.GetComponentInParent<Animator>();
            if (anim != null) return anim.gameObject;

            PlayerController pc = selected.GetComponentInParent<PlayerController>();
            if (pc != null) return pc.gameObject;

            PlayerCapsule cap = selected.GetComponentInParent<PlayerCapsule>();
            if (cap != null) return cap.gameObject;

            return selected;
        }

        PlayerController scenePc = Object.FindAnyObjectByType<PlayerController>();
        if (scenePc != null) return scenePc.gameObject;

        PlayerCapsule sceneCap = Object.FindAnyObjectByType<PlayerCapsule>();
        if (sceneCap != null) return sceneCap.gameObject;

        return null;
    }

    private static void BuildSculptedGuardShieldOnCharacter(
        GameObject characterRoot,
        Mesh shieldMesh,
        Material shieldMat,
        Material shieldGlowMat)
    {
        if (characterRoot == null) return;

        Undo.RegisterFullObjectHierarchyUndo(characterRoot, "Build Sculpted Guard Shield VFX");

        Transform existingRoot = characterRoot.transform.Find("VFX_AAA_GuardAndParry_Root");
        if (existingRoot != null)
        {
            Object.DestroyImmediate(existingRoot.gameObject);
        }

        GameObject vfxRoot = new GameObject("VFX_AAA_GuardAndParry_Root");
        vfxRoot.transform.SetParent(characterRoot.transform, false);
        vfxRoot.transform.localPosition = new Vector3(0f, 1.05f, 0.65f);
        vfxRoot.transform.localRotation = Quaternion.identity;
        vfxRoot.transform.localScale = Vector3.one;

        // รูปโล่พลังงานวงกลมหลัก (อัตราส่วน 1:1 ตรงกับ MMOBlockParryController)
        GameObject shieldObj = new GameObject("L1_GuardShieldShape_Mesh");
        shieldObj.transform.SetParent(vfxRoot.transform, false);
        shieldObj.transform.localPosition = Vector3.zero;
        shieldObj.transform.localRotation = Quaternion.identity;
        shieldObj.transform.localScale = new Vector3(1.18f, 1.18f, 1.0f);

        MeshFilter shieldMf = shieldObj.AddComponent<MeshFilter>();
        shieldMf.sharedMesh = shieldMesh;

        MeshRenderer shieldMr = shieldObj.AddComponent<MeshRenderer>();
        shieldMr.sharedMaterial = shieldMat;
        shieldMr.shadowCastingMode = ShadowCastingMode.Off;
        shieldMr.receiveShadows = false;
        shieldMr.sortingOrder = 12;

        // แสงเรืองขอบหลังโล่วงกลม
        GameObject glowObj = new GameObject("L1_GuardShieldGlow_Back");
        glowObj.transform.SetParent(vfxRoot.transform, false);
        glowObj.transform.localPosition = new Vector3(0f, 0f, -0.02f);
        glowObj.transform.localRotation = Quaternion.identity;
        glowObj.transform.localScale = new Vector3(1.30f, 1.30f, 1.0f);

        MeshFilter glowMf = glowObj.AddComponent<MeshFilter>();
        glowMf.sharedMesh = shieldMesh;

        MeshRenderer glowMr = glowObj.AddComponent<MeshRenderer>();
        glowMr.sharedMaterial = shieldGlowMat;
        glowMr.shadowCastingMode = ShadowCastingMode.Off;
        glowMr.receiveShadows = false;
        glowMr.sortingOrder = 11;

        MMOBlockParryController controller = characterRoot.GetComponent<MMOBlockParryController>();
        if (controller == null)
        {
            controller = characterRoot.AddComponent<MMOBlockParryController>();
        }

        controller.guardVfxMount = vfxRoot.transform;
        controller.guardShieldDomeTransform = shieldObj.transform;
        controller.guardShieldDomeRenderer = shieldMr;
        controller.guardShieldGlowTransform = glowObj.transform;
        controller.guardShieldGlowRenderer = glowMr;
        controller.showDebugHUD = false;

        EditorUtility.SetDirty(controller);
        EditorUtility.SetDirty(characterRoot);
    }

    private static Mesh SaveProceduralShieldMesh(string assetPath)
    {
        Mesh generated = MMOBlockParryController.CreateCurvedShieldMesh();
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
        if (existing != null)
        {
            existing.Clear();
            existing.vertices = generated.vertices;
            existing.uv = generated.uv;
            existing.colors = generated.colors;
            existing.triangles = generated.triangles;
            existing.RecalculateNormals();
            existing.RecalculateBounds();
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(generated);
            return existing;
        }

        AssetDatabase.CreateAsset(generated, assetPath);
        return generated;
    }

    private static Texture2D SaveTextureAsset(Texture2D runtimeTex, string assetPath)
    {
        File.WriteAllBytes(assetPath, runtimeTex.EncodeToPNG());
        Object.DestroyImmediate(runtimeTex);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
    }

    private static Material CreateGuaranteedURPMaterial(string matPath, Texture2D tex, Color tintColor)
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Particles/Standard Unlit");

        Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, matPath);
        }
        else
        {
            mat.shader = shader;
        }

        mat.mainTexture = tex;
        mat.color = tintColor;
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", tintColor);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tintColor);
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)CullMode.Off);
        if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);

        mat.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static void EnsureOutputDirectory()
    {
        if (!Directory.Exists(OutputFolder))
        {
            Directory.CreateDirectory(OutputFolder);
        }
    }
}
#endif
