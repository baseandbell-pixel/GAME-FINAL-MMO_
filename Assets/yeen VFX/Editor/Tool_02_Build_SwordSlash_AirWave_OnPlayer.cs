#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Editor Generator for the AAA Wuxia Sheer Wind &amp; Air Distortion Sword Slash:
/// Menu: "VFX Master/Generate AAA Wuxia Wind Slash"
///   1. Procedurally builds 3D Crescent Mesh ('Mesh_WuxiaCrescentSlash.asset') with Sine Taper,
///      normalized UVs (U=0..1 along blade arc, V=0..1 inner-to-outer), and Vertex Color Alpha tapering.
///   2. Procedurally generates 'T_WindStreaks_Mask.png' and 'T_WindDistortion_Normal.png'.
///   3. Creates URP Materials powered by 'Custom/VFX/WuxiaAirDistortion'.
///   4. Assembles the 4-Layer Particle System hierarchy + Sword rig with WuxiaSlashController
///      and binds it to both the standalone showcase rig and the game's player prefabs.
/// </summary>
public static class Tool_02_Build_SwordSlash_AirWave_OnPlayer
{
    private const string RootFolder = "Assets/yeen VFX/03_VFX_Prefabs_And_Assets";
    private const string ShaderPath = "Assets/yeen VFX/03_VFX_Prefabs_And_Assets/Shaders/SHADER_SwordSlash_AirDistortion.shader";
    private const string MeshFolder = "Assets/yeen VFX/03_VFX_Prefabs_And_Assets/Meshes";
    private const string TexFolder = "Assets/yeen VFX/03_VFX_Prefabs_And_Assets/Textures";
    private const string MatFolder = "Assets/yeen VFX/03_VFX_Prefabs_And_Assets/Materials";
    private const string PrefabFolder = "Assets/yeen VFX/03_VFX_Prefabs_And_Assets/Prefabs";
    private const string ResVfxFolder = "Assets/yeen VFX/Resources/VFX";
    private const string ResourceSlashPrefabPath = "Assets/yeen VFX/Resources/VFX/VFX_WindBladeSlash.prefab";

    private static void AutoGenerateIfMissing()
    {
        // Superseded by Tool_03_Build_SwordSlash_WhiteWind_OnPlayer (Pure White 3-Point Synced System)
    }

    [MenuItem("yeen VFX/02. [ใส่บน Player] สร้างเอฟเฟกต์ฟันดาบคลื่นอากาศ (Air Wave Slash)")]
    public static void GenerateWuxiaWindSlashMenu()
    {
        GenerateWuxiaWindSlashInternal(spawnDemoRigInScene: true);
    }

    private static void GenerateWuxiaWindSlashInternal(bool spawnDemoRigInScene)
    {
        EnsureFolders();
        ConfigureUrpOpaqueAndPostProcessing();

        // 1. Generate Procedural 3D Crescent Slash Mesh (Sine Taper + Auto UV + Vertex Color Alpha)
        Mesh crescentMesh = GenerateWuxiaCrescentMesh(MeshFolder + "/Mesh_WuxiaCrescentSlash.asset");

        // 2. Generate Procedural Textures (Silky Wuxia Wind Streaks Mask + Tangent-Space Air Distortion Normal Map)
        Texture2D windMaskTex = GenerateWindStreaksMaskTexture(TexFolder + "/T_WindStreaks_Mask.png", 512, 256);
        Texture2D normalMapTex = GenerateWindDistortionNormalTexture(TexFolder + "/T_WindDistortion_Normal.png", 512, 256);
        Texture2D flashFlareTex = GenerateSoftFlareTexture(TexFolder + "/T_WuxiaQiFlare.png", 256);

        // 3. Create Materials connected to WuxiaAirDistortion.shader & URP Particle Shaders
        Shader wuxiaShader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (wuxiaShader == null) wuxiaShader = Shader.Find("Custom/VFX/WuxiaAirDistortion");

        // Layer 2A Material: Pure Air Refraction + Subtle Sheer White-Cyan Wind Streaks
        Material mat2ADistortion = CreateWuxiaDistortionMaterial(
            MatFolder + "/M_WuxiaAirDistortion_2A.mat",
            wuxiaShader,
            windMaskTex,
            normalMapTex,
            tintColor: new Color(0.82f, 0.96f, 1.0f, 0.48f),
            rimColorHdr: new Color(1.4f, 2.1f, 2.5f, 1.0f),
            outerRimPower: 6.5f,
            outerRimIntensity: 1.15f,
            distortionStrength: 0.055f,
            scrollSpeedU: -2.8f);

        // Layer 2B Material: Razor-Sharp Outer Qi Blade Edge (High HDR Rim Intensity along outer arc)
        Material mat2BRazorRim = CreateWuxiaDistortionMaterial(
            MatFolder + "/M_WuxiaRazorQiRim_2B.mat",
            wuxiaShader,
            windMaskTex,
            normalMapTex,
            tintColor: new Color(0.90f, 0.98f, 1.0f, 0.72f),
            rimColorHdr: new Color(3.2f, 4.6f, 5.2f, 1.0f),
            outerRimPower: 14.0f,
            outerRimIntensity: 2.8f,
            distortionStrength: 0.025f,
            scrollSpeedU: -3.6f);

        // Layer 1 & 3 Material: Sheer Qi Flash & Torn Wind Streaks
        Material matSheerQiAdd = CreateUrpParticleMaterial(
            MatFolder + "/M_WuxiaSheerQi_Add.mat",
            flashFlareTex,
            new Color(0.85f, 0.97f, 1.0f, 0.85f),
            hdrMultiplier: 2.6f,
            additive: true);

        // Layer 4 Material: Residual Air Wake Distortion
        Material matResidualWake = CreateWuxiaDistortionMaterial(
            MatFolder + "/M_WuxiaResidualWake_4.mat",
            wuxiaShader,
            windMaskTex,
            normalMapTex,
            tintColor: new Color(0.78f, 0.94f, 1.0f, 0.26f),
            rimColorHdr: new Color(0.9f, 1.2f, 1.4f, 0.5f),
            outerRimPower: 4.0f,
            outerRimIntensity: 0.45f,
            distortionStrength: 0.028f,
            scrollSpeedU: -1.2f);

        // 4. Build the 4-Layer Wuxia Slash Effect Prefab (used by both the standalone rig and character combat)
        GameObject slashOnlyRoot = Build4LayerWuxiaSlashVfx(
            crescentMesh,
            matSheerQiAdd,
            mat2ADistortion,
            mat2BRazorRim,
            matResidualWake);

        string vfxPrefabPath = PrefabFolder + "/VFX_WuxiaSheerWindSlash.prefab";
        PrefabUtility.SaveAsPrefabAsset(slashOnlyRoot, vfxPrefabPath);
        GameObject resSlashPrefab = PrefabUtility.SaveAsPrefabAsset(slashOnlyRoot, ResourceSlashPrefabPath);
        Object.DestroyImmediate(slashOnlyRoot);

        // Bind to existing character prefabs so GreatSwordGirl & Player automatically fire the new Wuxia Slash!
        BindToCharacterPrefab("Assets/Organ/Models/Prefab/GreatSwordPrefab/GreatSwordGirl.prefab", resSlashPrefab);
        BindToCharacterPrefab("Assets/_Project/Prefabs/Player.prefab", resSlashPrefab);

        // 5. Build Standalone Wuxia Sword Rig with WuxiaSlashController in Scene if requested
        if (spawnDemoRigInScene)
        {
            GameObject existingRig = GameObject.Find("WuxiaSwordSlash_Rig");
            if (existingRig != null) Object.DestroyImmediate(existingRig);

            GameObject rig = BuildCompleteWuxiaSwordRig(
                crescentMesh,
                matSheerQiAdd,
                mat2ADistortion,
                mat2BRazorRim,
                matResidualWake);

            var player = Object.FindAnyObjectByType<PlayerCombat>();
            if (player != null)
            {
                rig.transform.position = player.transform.position + player.transform.forward * 1.6f + Vector3.up * 1.15f;
                rig.transform.rotation = player.transform.rotation;
            }
            else
            {
                rig.transform.position = new Vector3(0f, 1.15f, 0f);
            }

            string fullRigPrefabPath = PrefabFolder + "/WuxiaSwordSlash_Rig.prefab";
            PrefabUtility.SaveAsPrefabAssetAndConnect(rig, fullRigPrefabPath, InteractionMode.AutomatedAction);
            Selection.activeGameObject = rig;
            EditorGUIUtility.PingObject(rig);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("==========================================================================");
        Debug.Log("[VFX Master] AAA Wuxia Sheer Wind & Air Distortion Sword Slash generated!");
        Debug.Log("1. Mesh: Assets/_Project/VFX/Meshes/Mesh_WuxiaCrescentSlash.asset");
        Debug.Log("2. Textures: T_WindStreaks_Mask.png & T_WindDistortion_Normal.png");
        Debug.Log("3. Shader & Materials: Custom/VFX/WuxiaAirDistortion");
        Debug.Log("4. Press PLAY and Left-Click (or Spacebar) to slash with Wuxia Air Distortion!");
        Debug.Log("==========================================================================");
    }

    private static void BindToCharacterPrefab(string prefabPath, GameObject slashPrefab)
    {
        GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (root == null) return;
        var hook = root.GetComponent<SwordWindSlashVFX>();
        if (hook == null) hook = root.AddComponent<SwordWindSlashVFX>();
        hook.windSlashPrefab = slashPrefab;
        EditorUtility.SetDirty(root);
    }

    private static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/yeen VFX"))
            AssetDatabase.CreateFolder("Assets", "yeen VFX");
        if (!AssetDatabase.IsValidFolder(RootFolder))
            AssetDatabase.CreateFolder("Assets/yeen VFX", "03_VFX_Prefabs_And_Assets");
        if (!AssetDatabase.IsValidFolder(RootFolder + "/Shaders"))
            AssetDatabase.CreateFolder(RootFolder, "Shaders");
        if (!AssetDatabase.IsValidFolder(MeshFolder))
            AssetDatabase.CreateFolder(RootFolder, "Meshes");
        if (!AssetDatabase.IsValidFolder(TexFolder))
            AssetDatabase.CreateFolder(RootFolder, "Textures");
        if (!AssetDatabase.IsValidFolder(MatFolder))
            AssetDatabase.CreateFolder(RootFolder, "Materials");
        if (!AssetDatabase.IsValidFolder(PrefabFolder))
            AssetDatabase.CreateFolder(RootFolder, "Prefabs");
        if (!AssetDatabase.IsValidFolder("Assets/yeen VFX/Resources"))
            AssetDatabase.CreateFolder("Assets/yeen VFX", "Resources");
        if (!AssetDatabase.IsValidFolder(ResVfxFolder))
            AssetDatabase.CreateFolder("Assets/yeen VFX/Resources", "VFX");
    }

    private static void ConfigureUrpOpaqueAndPostProcessing()
    {
        var urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urpAsset != null)
        {
            urpAsset.supportsCameraOpaqueTexture = true;
            urpAsset.supportsCameraDepthTexture = true;
            urpAsset.supportsHDR = true;
            EditorUtility.SetDirty(urpAsset);
        }

        Camera cam = Camera.main;
        if (cam != null)
        {
            var camData = cam.GetComponent<UniversalAdditionalCameraData>();
            if (camData == null) camData = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.requiresColorOption = CameraOverrideOption.On;
            camData.requiresDepthOption = CameraOverrideOption.On;
            EditorUtility.SetDirty(cam.gameObject);
        }
    }

    // ====================================================================
    // 1. PROCEDURAL 3D CRESCENT SLASH MESH (Sine Taper + UV + Vertex Alpha)
    // ====================================================================

    private static Mesh GenerateWuxiaCrescentMesh(string assetPath)
    {
        const int segU = 64; // Segments along the swing arc (U: 0 -> 1)
        const int segV = 16; // Segments from inner radius to outer razor edge (V: 0 -> 1)

        const float arcAngleDegrees = 200f;
        const float outerRadius = 1.65f;
        const float maxRadialWidth = 0.92f;

        int vertCount = (segU + 1) * (segV + 1);
        Vector3[] vertices = new Vector3[vertCount];
        Vector3[] normals = new Vector3[vertCount];
        Vector2[] uvs = new Vector2[vertCount];
        Color[] colors = new Color[vertCount];
        int[] triangles = new int[segU * segV * 6];

        float halfArcRad = (arcAngleDegrees * 0.5f) * Mathf.Deg2Rad;

        int vIdx = 0;
        for (int iu = 0; iu <= segU; iu++)
        {
            float u = iu / (float)segU; // 0 (start of slash) -> 1 (tip of slash)

            // Sine Taper: pinches both ends of the crescent to razor-sharp points
            float sineTaper = Mathf.Sin(u * Mathf.PI);
            float widthTaper = Mathf.Pow(Mathf.Clamp01(sineTaper), 0.72f);

            // Sweep angle centered around +Z forward axis
            float angle = Mathf.Lerp(-halfArcRad, halfArcRad, u);
            float sinA = Mathf.Sin(angle);
            float cosA = Mathf.Cos(angle);

            // Slight spiral expansion towards the leading tip of the slash
            float currentOuterR = outerRadius * Mathf.Lerp(0.86f, 1.06f, u);
            float currentWidth = maxRadialWidth * widthTaper;
            float currentInnerR = Mathf.Max(0.15f, currentOuterR - currentWidth);

            for (int iv = 0; iv <= segV; iv++)
            {
                float v = iv / (float)segV; // 0 (inner edge) -> 1 (outer razor edge)
                float r = Mathf.Lerp(currentInnerR, currentOuterR, v);

                // Subtle 3D dish/camber across V so the air wave has real 3D curvature
                float dishY = Mathf.Sin(v * Mathf.PI) * 0.06f * widthTaper;

                vertices[vIdx] = new Vector3(sinA * r, dishY, cosA * r);
                normals[vIdx] = Vector3.up;
                uvs[vIdx] = new Vector2(u, v);

                // Vertex Color Alpha: smooth fade on inner ring (v->0) and both crescent tips (u->0, u->1)
                float innerAlpha = Mathf.SmoothStep(0f, 0.42f, v);
                float outerEdgeFeather = 1f - Mathf.SmoothStep(0.95f, 1.0f, v);
                float tipAlpha = Mathf.Pow(Mathf.Clamp01(sineTaper), 0.65f);
                float finalVertexAlpha = Mathf.Clamp01(innerAlpha * outerEdgeFeather * tipAlpha);

                colors[vIdx] = new Color(1f, 1f, 1f, finalVertexAlpha);
                vIdx++;
            }
        }

        int tIdx = 0;
        int stride = segV + 1;
        for (int iu = 0; iu < segU; iu++)
        {
            for (int iv = 0; iv < segV; iv++)
            {
                int a = iu * stride + iv;
                int b = (iu + 1) * stride + iv;
                int c = (iu + 1) * stride + (iv + 1);
                int d = iu * stride + (iv + 1);

                triangles[tIdx++] = a;
                triangles[tIdx++] = b;
                triangles[tIdx++] = c;

                triangles[tIdx++] = a;
                triangles[tIdx++] = c;
                triangles[tIdx++] = d;
            }
        }

        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
        Mesh mesh = existing != null ? existing : new Mesh();
        mesh.Clear();
        mesh.name = "Mesh_WuxiaCrescentSlash";
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.colors = colors;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();

        if (existing == null)
            AssetDatabase.CreateAsset(mesh, assetPath);
        else
            EditorUtility.SetDirty(mesh);

        return mesh;
    }

    // ====================================================================
    // 2. PROCEDURAL TEXTURES (Wind Streaks Mask & Air Distortion Normal Map)
    // ====================================================================

    private static Texture2D GenerateWindStreaksMaskTexture(string assetPath, int width, int height)
    {
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[width * height];

        for (int y = 0; y < height; y++)
        {
            float v = y / (float)(height - 1); // 0 = inner, 1 = outer razor edge

            // Outer blade edge (top of texture, v near 0.90..0.97) is razor-sharp
            float razorRim = Mathf.Exp(-Mathf.Pow((v - 0.92f) * 28f, 2f));
            float innerGrad = Mathf.SmoothStep(0.05f, 0.45f, v) * (1f - Mathf.SmoothStep(0.95f, 1.0f, v));

            for (int x = 0; x < width; x++)
            {
                float u = x / (float)(width - 1);
                float uFade = Mathf.Sin(u * Mathf.PI);

                // Layered Wuxia horizontal silk wind ribbons
                float n1 = Mathf.PerlinNoise(u * 4.5f + 1.3f, v * 18.0f + 2.7f);
                float n2 = Mathf.PerlinNoise(u * 9.0f + 7.1f, v * 36.0f + 5.2f);
                float fineThreads = Mathf.Sin(v * 95f + n1 * 4.5f) * 0.5f + 0.5f;
                fineThreads = Mathf.Pow(fineThreads, 3.2f);

                float bodyStreaks = Mathf.Pow(Mathf.Clamp01(n1 * 0.65f + n2 * 0.35f), 1.8f) * (0.45f + 0.55f * fineThreads);
                float combined = Mathf.Clamp01((bodyStreaks * innerGrad * 0.95f + razorRim * 1.25f) * uFade);

                pixels[y * width + x] = new Color(combined, combined, combined, combined);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return SaveAndImportTexture(tex, assetPath, isNormalMap: false, wrapRepeatU: true);
    }

    private static Texture2D GenerateWindDistortionNormalTexture(string assetPath, int width, int height)
    {
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        float[,] hMap = new float[width, height];

        for (int y = 0; y < height; y++)
        {
            float v = y / (float)(height - 1);
            for (int x = 0; x < width; x++)
            {
                float u = x / (float)(width - 1);
                // Seamless tileable U wave + multi-octave Perlin turbulence
                float angleU = u * Mathf.PI * 2f;
                float p1 = Mathf.PerlinNoise(Mathf.Cos(angleU) * 1.8f + 3.1f, v * 10.0f + Mathf.Sin(angleU) * 1.8f);
                float p2 = Mathf.PerlinNoise(Mathf.Cos(angleU * 2f) * 3.2f + 8.4f, v * 22.0f + 1.9f);
                float ridges = Mathf.Sin(v * 42f + p1 * 5.0f) * 0.25f;
                hMap[x, y] = p1 * 0.6f + p2 * 0.4f + ridges;
            }
        }

        Color[] pixels = new Color[width * height];
        const float bumpScale = 5.5f;

        for (int y = 0; y < height; y++)
        {
            int yUp = Mathf.Min(height - 1, y + 1);
            int yDown = Mathf.Max(0, y - 1);

            for (int x = 0; x < width; x++)
            {
                int xRight = (x + 1) % width;
                int xLeft = (x - 1 + width) % width;

                float dX = (hMap[xRight, y] - hMap[xLeft, y]) * bumpScale;
                float dY = (hMap[x, yUp] - hMap[x, yDown]) * bumpScale;

                Vector3 n = new Vector3(-dX, -dY, 1.0f).normalized;
                pixels[y * width + x] = new Color(
                    n.x * 0.5f + 0.5f,
                    n.y * 0.5f + 0.5f,
                    n.z * 0.5f + 0.5f,
                    1.0f);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return SaveAndImportTexture(tex, assetPath, isNormalMap: true, wrapRepeatU: true);
    }

    private static Texture2D GenerateSoftFlareTexture(string assetPath, int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[size * size];
        float half = (size - 1) * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = (x - half) / half;
                float ny = (y - half) / half;
                float r = Mathf.Sqrt(nx * nx + ny * ny);
                float core = Mathf.Pow(Mathf.Clamp01(1f - r), 2.8f);
                float streakX = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(ny) * 8f), 2.4f) * Mathf.Clamp01(1f - Mathf.Abs(nx));
                float alpha = Mathf.Clamp01(core + streakX * 0.9f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return SaveAndImportTexture(tex, assetPath, isNormalMap: false, wrapRepeatU: false);
    }

    private static Texture2D SaveAndImportTexture(Texture2D tex, string assetPath, bool isNormalMap, bool wrapRepeatU)
    {
        File.WriteAllBytes(assetPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = isNormalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (!isNormalMap)
            {
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
            }
            importer.wrapModeU = wrapRepeatU ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            importer.wrapModeV = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
    }

    // ====================================================================
    // 3. MATERIAL BUILDERS
    // ====================================================================

    private static Material CreateWuxiaDistortionMaterial(
        string matPath,
        Shader shader,
        Texture2D mainTex,
        Texture2D normalTex,
        Color tintColor,
        Color rimColorHdr,
        float outerRimPower,
        float outerRimIntensity,
        float distortionStrength,
        float scrollSpeedU)
    {
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");

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

        mat.SetTexture("_MainTex", mainTex);
        mat.SetTexture("_BumpMap", normalTex);
        mat.SetColor("_TintColor", tintColor);
        mat.SetColor("_RimColor", rimColorHdr);
        mat.SetFloat("_OuterRimPower", outerRimPower);
        mat.SetFloat("_OuterRimIntensity", outerRimIntensity);
        mat.SetFloat("_DistortionStrength", distortionStrength);
        mat.SetFloat("_ScrollSpeedU", scrollSpeedU);
        mat.SetFloat("_DistortionScrollU", scrollSpeedU * 1.2f);
        mat.SetFloat("_Opacity", 1.0f);

        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static Material CreateUrpParticleMaterial(
        string matPath,
        Texture2D mainTex,
        Color baseColor,
        float hdrMultiplier,
        bool additive)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
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

        Color hdr = new Color(
            baseColor.r * hdrMultiplier,
            baseColor.g * hdrMultiplier,
            baseColor.b * hdrMultiplier,
            baseColor.a);

        mat.SetTexture("_BaseMap", mainTex);
        mat.SetColor("_BaseColor", hdr);
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", additive ? 2f : 0f);
        mat.SetFloat("_Cull", 0f);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent + 60;

        EditorUtility.SetDirty(mat);
        return mat;
    }

    // ====================================================================
    // 4. ASSEMBLE 4-LAYER PARTICLE ARCHITECTURE & SWORD RIG
    // ====================================================================

    private static GameObject BuildCompleteWuxiaSwordRig(
        Mesh crescentMesh,
        Material matLayer1And3,
        Material matLayer2ADistortion,
        Material matLayer2BRazorRim,
        Material matLayer4Residual)
    {
        var rigRoot = new GameObject("WuxiaSwordSlash_Rig");

        // 4.1 Simulated Wuxia Sword Pivot & Blade Mesh
        var swordPivot = new GameObject("SwordPivot").transform;
        swordPivot.SetParent(rigRoot.transform, false);
        swordPivot.localPosition = Vector3.zero;

        CreateSwordVisualPart(swordPivot, "SwordHilt", PrimitiveType.Cylinder,
            new Vector3(0f, 0f, 0.18f), new Vector3(90f, 0f, 0f), new Vector3(0.045f, 0.18f, 0.045f),
            new Color(0.18f, 0.15f, 0.12f));
        CreateSwordVisualPart(swordPivot, "SwordGuard", PrimitiveType.Cube,
            new Vector3(0f, 0f, 0.36f), Vector3.zero, new Vector3(0.26f, 0.04f, 0.06f),
            new Color(0.78f, 0.64f, 0.25f));
        CreateSwordVisualPart(swordPivot, "SwordBlade", PrimitiveType.Cube,
            new Vector3(0f, 0f, 1.05f), Vector3.zero, new Vector3(0.065f, 0.014f, 1.35f),
            new Color(0.85f, 0.92f, 0.98f));

        // 4.2 4-Layer Wuxia Slash VFX Root
        GameObject slashVfx = Build4LayerWuxiaSlashVfx(
            crescentMesh,
            matLayer1And3,
            matLayer2ADistortion,
            matLayer2BRazorRim,
            matLayer4Residual);
        slashVfx.name = "SlashVFX_Root";
        slashVfx.transform.SetParent(rigRoot.transform, false);
        slashVfx.transform.localPosition = Vector3.zero;

        // 4.3 Attach WuxiaSlashController
        var controller = rigRoot.AddComponent<WuxiaSlashController>();
        controller.slashVfxRoot = slashVfx.transform;
        controller.swordPivot = swordPivot;
        controller.CacheParticles();
        controller.StopAndClearAllEffects();

        return rigRoot;
    }

    private static void CreateSwordVisualPart(
        Transform parent,
        string name,
        PrimitiveType type,
        Vector3 localPos,
        Vector3 localEuler,
        Vector3 localScale,
        Color color)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        var col = go.GetComponent<Collider>();
        if (col != null) Object.DestroyImmediate(col);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(localEuler);
        go.transform.localScale = localScale;

        var r = go.GetComponent<Renderer>();
        if (r != null)
        {
            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit != null)
            {
                var mat = new Material(urpLit);
                mat.SetColor("_BaseColor", color);
                r.sharedMaterial = mat;
            }
        }
    }

    private static GameObject Build4LayerWuxiaSlashVfx(
        Mesh crescentMesh,
        Material matLayer1And3,
        Material matLayer2ADistortion,
        Material matLayer2BRazorRim,
        Material matLayer4Residual)
    {
        var root = new GameObject("VFX_WuxiaSheerWindSlash");

        // -------------------------------------------------------------------------
        // LAYER 1: ANTICIPATION (0.00s - 0.08s) - Sheer Qi Gathering Flash at Hilt
        // -------------------------------------------------------------------------
        var l1 = new GameObject("Layer1_AnticipationFlash");
        l1.transform.SetParent(root.transform, false);
        l1.transform.localPosition = new Vector3(-0.35f, 0f, 0.45f);
        ConfigureLayer1Anticipation(
            l1.AddComponent<ParticleSystem>(),
            l1.GetComponent<ParticleSystemRenderer>(),
            matLayer1And3);

        // -------------------------------------------------------------------------
        // LAYER 2A: MAIN ACTION - Screen-Space Air Distortion Crescent
        // -------------------------------------------------------------------------
        var l2A = new GameObject("Layer2A_AirDistortionSlash");
        l2A.transform.SetParent(root.transform, false);
        l2A.transform.localPosition = Vector3.zero;
        ConfigureLayer2CrescentSlash(
            l2A.AddComponent<ParticleSystem>(),
            l2A.GetComponent<ParticleSystemRenderer>(),
            crescentMesh,
            matLayer2ADistortion,
            startSize: 1.35f,
            lifetime: 0.24f,
            forwardSpeedStart: 8.5f);

        // -------------------------------------------------------------------------
        // LAYER 2B: MAIN ACTION - Outer Razor Qi Blade Rim (HDR Edge Highlight)
        // -------------------------------------------------------------------------
        var l2B = new GameObject("Layer2B_RazorQiRim");
        l2B.transform.SetParent(root.transform, false);
        l2B.transform.localPosition = Vector3.zero;
        ConfigureLayer2CrescentSlash(
            l2B.AddComponent<ParticleSystem>(),
            l2B.GetComponent<ParticleSystemRenderer>(),
            crescentMesh,
            matLayer2BRazorRim,
            startSize: 1.38f,
            lifetime: 0.20f,
            forwardSpeedStart: 9.2f);

        // -------------------------------------------------------------------------
        // LAYER 3: SECONDARY ELEMENTS - Stretched Billboard Torn Wind Streaks
        // -------------------------------------------------------------------------
        var l3 = new GameObject("Layer3_TornWindStreaks");
        l3.transform.SetParent(root.transform, false);
        l3.transform.localPosition = Vector3.zero;
        ConfigureLayer3TornWindStreaks(
            l3.AddComponent<ParticleSystem>(),
            l3.GetComponent<ParticleSystemRenderer>(),
            matLayer1And3);

        // -------------------------------------------------------------------------
        // LAYER 4: IMPACT & RESIDUAL - Lingering Air Wake (0.45s fade)
        // -------------------------------------------------------------------------
        var l4 = new GameObject("Layer4_ResidualAirWake");
        l4.transform.SetParent(root.transform, false);
        l4.transform.localPosition = new Vector3(0f, 0f, 0.35f);
        ConfigureLayer4ResidualWake(
            l4.AddComponent<ParticleSystem>(),
            l4.GetComponent<ParticleSystemRenderer>(),
            crescentMesh,
            matLayer4Residual);

        return root;
    }

    private static void ConfigureLayer1Anticipation(ParticleSystem ps, ParticleSystemRenderer psr, Material mat)
    {
        var main = ps.main;
        main.duration = 0.50f;
        main.loop = false;
        main.playOnAwake = false;
        main.startDelay = 0.0f;
        main.startLifetime = 0.08f; // Crisp 80ms Qi compression flash before slash release
        main.startSpeed = 0f;
        main.startSize = 1.45f;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0.0f, 2) });

        var shape = ps.shape;
        shape.enabled = false;

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0.0f, 0.2f),
            new Keyframe(0.25f, 1.0f),
            new Keyframe(1.0f, 0.0f)
        ));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0.0f),
                new GradientColorKey(new Color(0.75f, 0.95f, 1.0f), 1.0f)
            },
            new[]
            {
                new GradientAlphaKey(0.95f, 0.0f),
                new GradientAlphaKey(0.0f, 1.0f)
            }
        );
        col.color = new ParticleSystem.MinMaxGradient(grad);

        psr.renderMode = ParticleSystemRenderMode.Billboard;
        psr.sharedMaterial = mat;
    }

    private static void ConfigureLayer2CrescentSlash(
        ParticleSystem ps,
        ParticleSystemRenderer psr,
        Mesh crescentMesh,
        Material mat,
        float startSize,
        float lifetime,
        float forwardSpeedStart)
    {
        var main = ps.main;
        main.duration = 0.50f;
        main.loop = false;
        main.playOnAwake = false;
        main.startDelay = 0.015f;
        main.startLifetime = lifetime;
        main.startSpeed = 0f;
        main.startSize = startSize;
        main.startRotation3D = true;
        main.startRotationX = 0f;
        main.startRotationY = -32f * Mathf.Deg2Rad;
        main.startRotationZ = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0.0f, 1) });

        var shape = ps.shape;
        shape.enabled = false;

        // Explosive Ease-Out Forward Velocity (air wave detaches from the sword and slices forward)
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = new ParticleSystem.MinMaxCurve(0f);
        vel.y = new ParticleSystem.MinMaxCurve(0f);
        vel.z = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0.0f, forwardSpeedStart),
            new Keyframe(0.30f, forwardSpeedStart * 0.35f),
            new Keyframe(1.0f, 0.4f)
        ));

        // Explosive Ease-Out Rotational Sweep around Y (local normal of the crescent mesh)
        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.separateAxes = true;
        rot.x = 0f;
        rot.z = 0f;
        rot.y = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0.0f, 540f * Mathf.Deg2Rad),
            new Keyframe(0.28f, 160f * Mathf.Deg2Rad),
            new Keyframe(1.0f, 15f * Mathf.Deg2Rad)
        ));

        // Explosive Ease-Out Size expansion
        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0.0f, 0.72f),
            new Keyframe(0.22f, 1.04f),
            new Keyframe(1.0f, 1.18f)
        ));

        // Smooth Alpha Over Lifetime passed into Vertex Color Alpha of WuxiaAirDistortion.shader
        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0.0f),
                new GradientColorKey(new Color(0.82f, 0.96f, 1.0f), 0.55f),
                new GradientColorKey(new Color(0.60f, 0.88f, 1.0f), 1.0f)
            },
            new[]
            {
                new GradientAlphaKey(0.0f, 0.0f),
                new GradientAlphaKey(1.0f, 0.12f),
                new GradientAlphaKey(0.85f, 0.52f),
                new GradientAlphaKey(0.0f, 1.0f)
            }
        );
        col.color = new ParticleSystem.MinMaxGradient(grad);

        psr.renderMode = ParticleSystemRenderMode.Mesh;
        psr.mesh = crescentMesh;
        psr.alignment = ParticleSystemRenderSpace.Local;
        psr.sharedMaterial = mat;
    }

    private static void ConfigureLayer3TornWindStreaks(ParticleSystem ps, ParticleSystemRenderer psr, Material mat)
    {
        var main = ps.main;
        main.duration = 0.50f;
        main.loop = false;
        main.playOnAwake = false;
        main.startDelay = 0.02f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.16f, 0.28f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(9.0f, 18.0f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0.0f, 18) });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 38f;
        shape.radius = 1.15f;

        var limitVel = ps.limitVelocityOverLifetime;
        limitVel.enabled = true;
        limitVel.limit = 2.0f;
        limitVel.dampen = 0.22f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0.0f),
                new GradientColorKey(new Color(0.75f, 0.95f, 1.0f), 1.0f)
            },
            new[]
            {
                new GradientAlphaKey(0.0f, 0.0f),
                new GradientAlphaKey(0.85f, 0.15f),
                new GradientAlphaKey(0.0f, 1.0f)
            }
        );
        col.color = new ParticleSystem.MinMaxGradient(grad);

        // Stretched Billboard so fast particles elongate into razor-thin wind needles
        psr.renderMode = ParticleSystemRenderMode.Stretch;
        psr.cameraVelocityScale = 0f;
        psr.velocityScale = 0.08f;
        psr.lengthScale = 3.8f;
        psr.sharedMaterial = mat;
    }

    private static void ConfigureLayer4ResidualWake(
        ParticleSystem ps,
        ParticleSystemRenderer psr,
        Mesh crescentMesh,
        Material mat)
    {
        var main = ps.main;
        main.duration = 0.60f;
        main.loop = false;
        main.playOnAwake = false;
        main.startDelay = 0.04f;
        main.startLifetime = 0.45f; // Lingers 0.45s in the air along the blade trajectory
        main.startSpeed = 0f;
        main.startSize = 1.42f;
        main.startRotation3D = true;
        main.startRotationX = 0f;
        main.startRotationY = -10f * Mathf.Deg2Rad;
        main.startRotationZ = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0.0f, 1) });

        var shape = ps.shape;
        shape.enabled = false;

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = new ParticleSystem.MinMaxCurve(0f);
        vel.y = new ParticleSystem.MinMaxCurve(0f);
        vel.z = new ParticleSystem.MinMaxCurve(1.8f);

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0.0f, 0.95f),
            new Keyframe(1.0f, 1.28f)
        ));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.85f, 0.96f, 1.0f), 0.0f),
                new GradientColorKey(new Color(0.65f, 0.88f, 0.98f), 1.0f)
            },
            new[]
            {
                new GradientAlphaKey(0.0f, 0.0f),
                new GradientAlphaKey(0.55f, 0.18f),
                new GradientAlphaKey(0.0f, 1.0f)
            }
        );
        col.color = new ParticleSystem.MinMaxGradient(grad);

        psr.renderMode = ParticleSystemRenderMode.Mesh;
        psr.mesh = crescentMesh;
        psr.alignment = ParticleSystemRenderSpace.Local;
        psr.sharedMaterial = mat;
    }
}
#endif
