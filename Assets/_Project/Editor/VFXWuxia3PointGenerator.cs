#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// AAA 3-Point Pure White Wuxia Wind Slash Generator:
/// Menu: "VFX Mentor/Generate 3-Point Pure White Wuxia Wind Slash"
///
/// Enforces all 3 Strict Rules:
///   1) 3-Point Sword &amp; Scabbard Attachment:
///        - Point1_SwordTip      (Outer Pure White Razor Wind Trail)
///        - Point2_SwordMid      (Mid-Blade Sheer White Wind &amp; Air Distortion Curtain)
///        - Point3_Scabbard_Hilt (Inner Wind Trail + Phase-1 Scabbard Draw Wind Burst)
///   2) Pure White Sheer Wind (RGB = 1, 1, 1; Alpha 0.15..0.65; Soft HDR 1.4x..2.0x; ZERO green/blue/blobs)
///   3) 100% Frame-Locked 3-Phase Animation Sync (Phase 1 Wind-up -> Phase 2 Active Swing -> Phase 3 Recovery)
/// </summary>
public static class VFXWuxia3PointGenerator
{
    private const string RootFolder = "Assets/_Project/VFX";
    private const string ShaderPath = "Assets/_Project/VFX/Shaders/WuxiaWhiteWindDistortion.shader";
    private const string MeshFolder = "Assets/_Project/VFX/Meshes";
    private const string TexFolder = "Assets/_Project/VFX/Textures";
    private const string MatFolder = "Assets/_Project/VFX/Materials";
    private const string PrefabFolder = "Assets/_Project/VFX/Prefabs";
    private const string ResVfxFolder = "Assets/_Project/Resources/VFX";
    private const string ResourceSlashPrefabPath = "Assets/_Project/Resources/VFX/VFX_WindBladeSlash.prefab";

    [InitializeOnLoadMethod]
    private static void AutoEnsureGenerated()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (AssetDatabase.LoadAssetAtPath<Mesh>(MeshFolder + "/Mesh_SyncedWhiteSlash.asset") == null)
            {
                Generate3PointPureWhiteWuxiaSlash(spawnRigInScene: false);
            }
        };
    }

    [MenuItem("VFX Mentor/Generate 3-Point Pure White Wuxia Wind Slash")]
    public static void GenerateMenuCommand()
    {
        Generate3PointPureWhiteWuxiaSlash(spawnRigInScene: true);
    }

    public static void Generate3PointPureWhiteWuxiaSlash(bool spawnRigInScene)
    {
        EnsureFolders();
        ConfigureUrpOpaqueAndBloom();

        // 1. Build Procedural 3D Crescent Mesh ('Mesh_SyncedWhiteSlash.asset') with Sine Taper & Vertex Alpha
        Mesh syncedSlashMesh = BuildSyncedWhiteCrescentMesh(MeshFolder + "/Mesh_SyncedWhiteSlash.asset");

        // 2. Build Procedural Pure White Wind Textures (Zero green/blue, zero circular blobs)
        Texture2D whiteStreaksTex = GeneratePureWhiteWindStreaksTexture(TexFolder + "/T_PureWhiteWindStreaks.png", 512, 256);
        Texture2D normalMapTex = GenerateWindDistortionNormalTexture(TexFolder + "/T_WindDistortion_Normal.png", 512, 256);

        // 3. Build Pure White Materials using WuxiaWhiteWindDistortion.shader
        Shader whiteWindShader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (whiteWindShader == null) whiteWindShader = Shader.Find("Custom/VFX/WuxiaWhiteWindDistortion");

        // Material 1: Point 1 (Sword Tip) & Layer 2B Outer Razor White Rim (Soft HDR 1.85x Pure White)
        Material matTipAndRazorRim = CreateWhiteWindMaterial(
            MatFolder + "/M_PureWhite_TipAndRim.mat",
            whiteWindShader,
            whiteStreaksTex,
            normalMapTex,
            tintColor: new Color(1.0f, 1.0f, 1.0f, 0.62f),
            rimColorHdr: new Color(1.85f, 1.85f, 1.85f, 1.0f),
            outerRimPower: 12.0f,
            outerRimIntensity: 1.80f,
            distortionStrength: 0.025f,
            scrollSpeedU: -3.5f);

        // Material 2: Point 2 (Sword Mid) & Layer 2A Main Sheer Wind + Air Distortion (Alpha 0.42, HDR 1.45x)
        Material matMidAndMainWave = CreateWhiteWindMaterial(
            MatFolder + "/M_PureWhite_MidAndWave.mat",
            whiteWindShader,
            whiteStreaksTex,
            normalMapTex,
            tintColor: new Color(1.0f, 1.0f, 1.0f, 0.42f),
            rimColorHdr: new Color(1.45f, 1.45f, 1.45f, 1.0f),
            outerRimPower: 7.0f,
            outerRimIntensity: 1.10f,
            distortionStrength: 0.052f,
            scrollSpeedU: -2.8f);

        // Material 3: Point 3 (Hilt/Scabbard) & Layer 4 Residual Air Wake (Subtle Alpha 0.22, HDR 1.25x)
        Material matHiltAndResidual = CreateWhiteWindMaterial(
            MatFolder + "/M_PureWhite_HiltAndWake.mat",
            whiteWindShader,
            whiteStreaksTex,
            normalMapTex,
            tintColor: new Color(1.0f, 1.0f, 1.0f, 0.24f),
            rimColorHdr: new Color(1.25f, 1.25f, 1.25f, 0.65f),
            outerRimPower: 5.0f,
            outerRimIntensity: 0.55f,
            distortionStrength: 0.030f,
            scrollSpeedU: -1.6f);

        // Material 4: Stretched Wind Needles (Pure White Streak Texture, no blobs)
        Material matWindNeedles = CreatePureWhiteStreakParticleMaterial(
            MatFolder + "/M_PureWhite_WindNeedles.mat",
            whiteStreaksTex,
            alpha: 0.58f,
            hdrMultiplier: 1.65f);

        // 4. Save the standaloneSynced Crescent Wave Prefab & also update Resources/VFX for character combat
        GameObject wavePrefabRoot = BuildSyncedCrescentWaveHierarchy(
            syncedSlashMesh,
            matMidAndMainWave,
            matTipAndRazorRim,
            matWindNeedles,
            matHiltAndResidual);

        PrefabUtility.SaveAsPrefabAsset(wavePrefabRoot, PrefabFolder + "/VFX_PureWhiteSyncedWave.prefab");
        GameObject resWavePrefab = PrefabUtility.SaveAsPrefabAsset(wavePrefabRoot, ResourceSlashPrefabPath);
        Object.DestroyImmediate(wavePrefabRoot);

        // Bind to character prefabs (GreatSwordGirl & Player)
        BindToCharacterPrefab("Assets/Organ/Models/Prefab/GreatSwordPrefab/GreatSwordGirl.prefab", resWavePrefab);
        BindToCharacterPrefab("Assets/_Project/Prefabs/Player.prefab", resWavePrefab);

        // 5. Build Complete 3-Point Sword + Scabbard Rig in Scene
        if (spawnRigInScene)
        {
            GameObject oldRig1 = GameObject.Find("Wuxia3PointWhiteSlash_Rig");
            if (oldRig1 != null) Object.DestroyImmediate(oldRig1);
            GameObject oldRig2 = GameObject.Find("WuxiaSwordSlash_Rig");
            if (oldRig2 != null) Object.DestroyImmediate(oldRig2);
            GameObject oldPreview = GameObject.Find("VFX_WindBladeSlash_Preview");
            if (oldPreview != null) Object.DestroyImmediate(oldPreview);

            GameObject rig = Build3PointSwordAndScabbardRig(
                syncedSlashMesh,
                matTipAndRazorRim,
                matMidAndMainWave,
                matHiltAndResidual,
                matWindNeedles);

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

            string rigPrefabPath = PrefabFolder + "/Wuxia3PointWhiteSlash_Rig.prefab";
            PrefabUtility.SaveAsPrefabAssetAndConnect(rig, rigPrefabPath, InteractionMode.AutomatedAction);
            Selection.activeGameObject = rig;
            EditorGUIUtility.PingObject(rig);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("==========================================================================");
        Debug.Log("[VFX Mentor] 3-Point Pure White Wuxia Wind Slash generated successfully!");
        Debug.Log("• Point1_SwordTip, Point2_SwordMid, Point3_Scabbard_Hilt configured.");
        Debug.Log("• Pure White Sheer Wind (R=1, G=1, B=1) with zero green/blue or blob artifacts.");
        Debug.Log("• 3-Phase Frame-Locked Animation Sync ready! Press PLAY and Left-Click / Spacebar!");
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
        if (!AssetDatabase.IsValidFolder("Assets/_Project"))
            AssetDatabase.CreateFolder("Assets", "_Project");
        if (!AssetDatabase.IsValidFolder(RootFolder))
            AssetDatabase.CreateFolder("Assets/_Project", "VFX");
        if (!AssetDatabase.IsValidFolder("Assets/_Project/VFX/Shaders"))
            AssetDatabase.CreateFolder(RootFolder, "Shaders");
        if (!AssetDatabase.IsValidFolder(MeshFolder))
            AssetDatabase.CreateFolder(RootFolder, "Meshes");
        if (!AssetDatabase.IsValidFolder(TexFolder))
            AssetDatabase.CreateFolder(RootFolder, "Textures");
        if (!AssetDatabase.IsValidFolder(MatFolder))
            AssetDatabase.CreateFolder(RootFolder, "Materials");
        if (!AssetDatabase.IsValidFolder(PrefabFolder))
            AssetDatabase.CreateFolder(RootFolder, "Prefabs");
        if (!AssetDatabase.IsValidFolder("Assets/_Project/Resources"))
            AssetDatabase.CreateFolder("Assets/_Project", "Resources");
        if (!AssetDatabase.IsValidFolder(ResVfxFolder))
            AssetDatabase.CreateFolder("Assets/_Project/Resources", "VFX");
    }

    private static void ConfigureUrpOpaqueAndBloom()
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
    // 1. PROCEDURAL 3D CRESCENT MESH ('Mesh_SyncedWhiteSlash.asset')
    // ====================================================================

    private static Mesh BuildSyncedWhiteCrescentMesh(string assetPath)
    {
        const int segU = 64;
        const int segV = 16;
        const float arcDegrees = 195f;
        const float outerRadius = 1.62f;
        const float maxRadialWidth = 0.86f;

        int vertCount = (segU + 1) * (segV + 1);
        Vector3[] vertices = new Vector3[vertCount];
        Vector3[] normals = new Vector3[vertCount];
        Vector2[] uvs = new Vector2[vertCount];
        Color[] colors = new Color[vertCount];
        int[] triangles = new int[segU * segV * 6];

        float halfArcRad = (arcDegrees * 0.5f) * Mathf.Deg2Rad;
        int idx = 0;

        for (int iu = 0; iu <= segU; iu++)
        {
            float u = iu / (float)segU; // 0..1 along swing arc
            float sineTaper = Mathf.Sin(u * Mathf.PI); // Pinches both tips to 0 width
            float widthFactor = Mathf.Pow(Mathf.Clamp01(sineTaper), 0.70f);

            float angle = Mathf.Lerp(-halfArcRad, halfArcRad, u);
            float sinA = Mathf.Sin(angle);
            float cosA = Mathf.Cos(angle);

            float curOuterR = outerRadius * Mathf.Lerp(0.88f, 1.05f, u);
            float curWidth = maxRadialWidth * widthFactor;
            float curInnerR = Mathf.Max(0.18f, curOuterR - curWidth);

            for (int iv = 0; iv <= segV; iv++)
            {
                float v = iv / (float)segV; // 0 = inner, 1 = outer blade edge
                float r = Mathf.Lerp(curInnerR, curOuterR, v);
                float dishY = Mathf.Sin(v * Mathf.PI) * 0.035f * widthFactor;

                vertices[idx] = new Vector3(sinA * r, dishY, cosA * r);
                normals[idx] = Vector3.up;
                uvs[idx] = new Vector2(u, v);

                // Smooth Vertex Alpha on inner radius & both tips
                float innerAlpha = Mathf.SmoothStep(0.0f, 0.45f, v);
                float outerFeather = 1.0f - Mathf.SmoothStep(0.95f, 1.0f, v);
                float tipAlpha = Mathf.Pow(Mathf.Clamp01(sineTaper), 0.65f);
                float alpha = Mathf.Clamp01(innerAlpha * outerFeather * tipAlpha);

                // Strictly Pure White Vertex Color (1, 1, 1, alpha)
                colors[idx] = new Color(1f, 1f, 1f, alpha);
                idx++;
            }
        }

        int t = 0;
        int stride = segV + 1;
        for (int iu = 0; iu < segU; iu++)
        {
            for (int iv = 0; iv < segV; iv++)
            {
                int a = iu * stride + iv;
                int b = (iu + 1) * stride + iv;
                int c = (iu + 1) * stride + (iv + 1);
                int d = iu * stride + (iv + 1);

                triangles[t++] = a;
                triangles[t++] = b;
                triangles[t++] = c;

                triangles[t++] = a;
                triangles[t++] = c;
                triangles[t++] = d;
            }
        }

        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
        Mesh mesh = existing != null ? existing : new Mesh();
        mesh.Clear();
        mesh.name = "Mesh_SyncedWhiteSlash";
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
    // 2. PROCEDURAL PURE WHITE WIND TEXTURES (Zero Green/Blob Artifacts)
    // ====================================================================

    private static Texture2D GeneratePureWhiteWindStreaksTexture(string assetPath, int width, int height)
    {
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[width * height];

        for (int y = 0; y < height; y++)
        {
            float v = y / (float)(height - 1); // 0 = inner, 1 = top razor edge

            // Razor-sharp white wind line near v = 0.92
            float topRazorEdge = Mathf.Exp(-Mathf.Pow((v - 0.92f) * 30f, 2f));
            float radialMask = Mathf.SmoothStep(0.06f, 0.42f, v) * (1f - Mathf.SmoothStep(0.96f, 1.0f, v));

            for (int x = 0; x < width; x++)
            {
                float u = x / (float)(width - 1);
                float uTaper = Mathf.Sin(u * Mathf.PI);

                // Horizontal Wuxia silk wind filaments (high-aspect ratio horizontal flow, zero circular blobs)
                float n1 = Mathf.PerlinNoise(u * 3.8f + 2.1f, v * 20.0f + 1.4f);
                float n2 = Mathf.PerlinNoise(u * 8.2f + 5.7f, v * 38.0f + 6.3f);
                float filaments = Mathf.Sin(v * 110f + n1 * 4.2f) * 0.5f + 0.5f;
                filaments = Mathf.Pow(filaments, 3.5f);

                float streakBody = Mathf.Pow(Mathf.Clamp01(n1 * 0.65f + n2 * 0.35f), 1.9f) * (0.40f + 0.60f * filaments);
                float alpha = Mathf.Clamp01((streakBody * radialMask * 0.90f + topRazorEdge * 1.25f) * uTaper);

                // Strictly Pure White RGB (1.0, 1.0, 1.0)
                pixels[y * width + x] = new Color(1.0f, 1.0f, 1.0f, alpha);
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
                float radU = u * Mathf.PI * 2f;
                float p1 = Mathf.PerlinNoise(Mathf.Cos(radU) * 1.7f + 4.1f, v * 11.0f + Mathf.Sin(radU) * 1.7f);
                float p2 = Mathf.PerlinNoise(Mathf.Cos(radU * 2f) * 3.4f + 7.2f, v * 24.0f + 2.3f);
                float wave = Mathf.Sin(v * 46f + p1 * 4.8f) * 0.24f;
                hMap[x, y] = p1 * 0.6f + p2 * 0.4f + wave;
            }
        }

        Color[] pixels = new Color[width * height];
        const float bumpStrength = 5.2f;

        for (int y = 0; y < height; y++)
        {
            int yUp = Mathf.Min(height - 1, y + 1);
            int yDown = Mathf.Max(0, y - 1);

            for (int x = 0; x < width; x++)
            {
                int xRight = (x + 1) % width;
                int xLeft = (x - 1 + width) % width;

                float dX = (hMap[xRight, y] - hMap[xLeft, y]) * bumpStrength;
                float dY = (hMap[x, yUp] - hMap[x, yDown]) * bumpStrength;

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
    // 3. MATERIAL BUILDERS (Pure White Sheer Palette)
    // ====================================================================

    private static Material CreateWhiteWindMaterial(
        string matPath,
        Shader shader,
        Texture2D streakTex,
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

        mat.SetTexture("_WindStreakTex", streakTex);
        mat.SetTexture("_BumpMap", normalTex);
        mat.SetColor("_TintColor", tintColor);
        mat.SetColor("_RimColor", rimColorHdr);
        mat.SetFloat("_OuterRimPower", outerRimPower);
        mat.SetFloat("_OuterRimIntensity", outerRimIntensity);
        mat.SetFloat("_DistortionStrength", distortionStrength);
        mat.SetFloat("_ScrollSpeedU", scrollSpeedU);
        mat.SetFloat("_DistortionScrollU", scrollSpeedU * 1.18f);
        mat.SetFloat("_Opacity", 1.0f);

        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static Material CreatePureWhiteStreakParticleMaterial(
        string matPath,
        Texture2D streakTex,
        float alpha,
        float hdrMultiplier)
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

        // Pure White (R=1, G=1, B=1) with soft HDR intensity (1.4x - 2.0x) and Alpha blending
        Color pureWhiteHdr = new Color(1.0f * hdrMultiplier, 1.0f * hdrMultiplier, 1.0f * hdrMultiplier, alpha);
        mat.SetTexture("_BaseMap", streakTex);
        mat.SetColor("_BaseColor", pureWhiteHdr);
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f); // Alpha Blend (SrcAlpha OneMinusSrcAlpha) for soft sheer wind
        mat.SetFloat("_Cull", 0f);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent + 65;

        EditorUtility.SetDirty(mat);
        return mat;
    }

    // ====================================================================
    // 4. ASSEMBLE 3-POINT SWORD + SCABBARD RIG & 4-LAYER PARTICLES
    // ====================================================================

    private static GameObject Build3PointSwordAndScabbardRig(
        Mesh syncedSlashMesh,
        Material matTipAndRazorRim,
        Material matMidAndMainWave,
        Material matHiltAndResidual,
        Material matWindNeedles)
    {
        var rigRoot = new GameObject("Wuxia3PointWhiteSlash_Rig");

        // 4.1 Stationary Scabbard at Hip/Center + Point 3 Scabbard Draw Burst
        var scabbardRoot = new GameObject("Scabbard_Sheath").transform;
        scabbardRoot.SetParent(rigRoot.transform, false);
        scabbardRoot.localPosition = new Vector3(-0.22f, -0.05f, 0.15f);
        scabbardRoot.localRotation = Quaternion.Euler(0f, -55f, 0f);

        CreatePart(scabbardRoot, "ScabbardBody", PrimitiveType.Cube,
            new Vector3(0f, 0f, -0.55f), Vector3.zero, new Vector3(0.075f, 0.032f, 1.15f),
            new Color(0.14f, 0.14f, 0.16f));
        CreatePart(scabbardRoot, "ScabbardMouthThroat", PrimitiveType.Cube,
            new Vector3(0f, 0f, 0.04f), Vector3.zero, new Vector3(0.095f, 0.045f, 0.08f),
            new Color(0.75f, 0.75f, 0.78f));

        // Layer 1 (Anticipation / Draw Burst at Point 3 Scabbard Mouth: 0.00s - 0.07s)
        var scabbardBurstGo = new GameObject("Layer1_ScabbardDrawBurst");
        scabbardBurstGo.transform.SetParent(rigRoot.transform, false);
        scabbardBurstGo.transform.localPosition = new Vector3(-0.18f, 0f, 0.22f);
        var scabbardBurstPs = scabbardBurstGo.AddComponent<ParticleSystem>();
        ConfigureLayer1ScabbardDrawBurst(
            scabbardBurstPs,
            scabbardBurstGo.GetComponent<ParticleSystemRenderer>(),
            matWindNeedles);

        // 4.2 Weapon Pivot (Driven Frame-by-Frame by WuxiaSyncedSlashController)
        var weaponPivot = new GameObject("WeaponPivot").transform;
        weaponPivot.SetParent(rigRoot.transform, false);
        weaponPivot.localPosition = Vector3.zero;

        CreatePart(weaponPivot, "SwordGrip", PrimitiveType.Cylinder,
            new Vector3(0f, 0f, 0.16f), new Vector3(90f, 0f, 0f), new Vector3(0.04f, 0.16f, 0.04f),
            new Color(0.16f, 0.16f, 0.18f));
        CreatePart(weaponPivot, "SwordGuard", PrimitiveType.Cube,
            new Vector3(0f, 0f, 0.34f), Vector3.zero, new Vector3(0.24f, 0.035f, 0.055f),
            new Color(0.82f, 0.82f, 0.86f));
        CreatePart(weaponPivot, "SwordBlade", PrimitiveType.Cube,
            new Vector3(0f, 0f, 1.05f), Vector3.zero, new Vector3(0.058f, 0.012f, 1.38f),
            new Color(0.94f, 0.96f, 0.98f));

        // -------------------------------------------------------------------------
        // STRICT RULE 1: 3-POINT SWORD ATTACHMENT (Point 1 Tip, Point 2 Mid, Point 3 Hilt)
        // -------------------------------------------------------------------------
        // Point 1: Sword Tip (z = 1.72m)
        var point1Tip = new GameObject("Point1_SwordTip").transform;
        point1Tip.SetParent(weaponPivot, false);
        point1Tip.localPosition = new Vector3(0f, 0f, 1.72f);
        TrailRenderer tipTrail = BuildPureWhiteBladeTrail(
            point1Tip.gameObject,
            matTipAndRazorRim,
            width: 0.22f,
            time: 0.16f,
            maxAlpha: 0.65f);

        // Point 2: Sword Mid-Blade (z = 1.08m)
        var point2Mid = new GameObject("Point2_SwordMid").transform;
        point2Mid.SetParent(weaponPivot, false);
        point2Mid.localPosition = new Vector3(0f, 0f, 1.08f);
        TrailRenderer midTrail = BuildPureWhiteBladeTrail(
            point2Mid.gameObject,
            matMidAndMainWave,
            width: 0.68f,
            time: 0.18f,
            maxAlpha: 0.45f);

        // Point 3: Scabbard / Hilt (z = 0.44m)
        var point3Hilt = new GameObject("Point3_Scabbard_Hilt").transform;
        point3Hilt.SetParent(weaponPivot, false);
        point3Hilt.localPosition = new Vector3(0f, 0f, 0.44f);
        TrailRenderer hiltTrail = BuildPureWhiteBladeTrail(
            point3Hilt.gameObject,
            matHiltAndResidual,
            width: 0.35f,
            time: 0.14f,
            maxAlpha: 0.28f);

        // 4.3 Frame-Locked Crescent Wave Root (Layers 2A, 2B, 3, and 4)
        GameObject syncedWaveGo = BuildSyncedCrescentWaveHierarchy(
            syncedSlashMesh,
            matMidAndMainWave,
            matTipAndRazorRim,
            matWindNeedles,
            matHiltAndResidual);
        syncedWaveGo.transform.SetParent(rigRoot.transform, false);
        syncedWaveGo.transform.localPosition = Vector3.zero;

        // 4.4 Attach & Configure WuxiaSyncedSlashController
        var ctrl = rigRoot.AddComponent<WuxiaSyncedSlashController>();
        ctrl.weaponPivot = weaponPivot;
        ctrl.tipTrail = tipTrail;
        ctrl.midTrail = midTrail;
        ctrl.hiltTrail = hiltTrail;
        ctrl.scabbardBurst = scabbardBurstPs;
        ctrl.syncedCrescentWave = syncedWaveGo.GetComponent<ParticleSystem>();
        ctrl.CacheParticleSystems();
        ctrl.ResetAndClearAllVFX();

        return rigRoot;
    }

    private static TrailRenderer BuildPureWhiteBladeTrail(
        GameObject host,
        Material mat,
        float width,
        float time,
        float maxAlpha)
    {
        var tr = host.GetComponent<TrailRenderer>();
        if (tr == null) tr = host.AddComponent<TrailRenderer>();

        tr.time = time;
        tr.minVertexDistance = 0.025f;
        tr.autodestruct = false;
        tr.emitting = false; // Strictly OFF until Phase 2 Active Slash!
        tr.shadowCastingMode = ShadowCastingMode.Off;
        tr.receiveShadows = false;
        tr.numCornerVertices = 4;
        tr.numCapVertices = 4;
        tr.textureMode = LineTextureMode.Stretch;

        tr.widthMultiplier = width;
        tr.widthCurve = new AnimationCurve(
            new Keyframe(0.0f, 0.15f),
            new Keyframe(0.12f, 1.0f),
            new Keyframe(1.0f, 0.0f)
        );

        // Strictly Pure White (1, 1, 1) with soft Alpha (0.15 to 0.65)
        Gradient grad = new Gradient();
        grad.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0.0f),
                new GradientColorKey(Color.white, 1.0f)
            },
            new[]
            {
                new GradientAlphaKey(0.0f, 0.0f),
                new GradientAlphaKey(maxAlpha, 0.12f),
                new GradientAlphaKey(maxAlpha * 0.5f, 0.55f),
                new GradientAlphaKey(0.0f, 1.0f)
            }
        );
        tr.colorGradient = grad;
        tr.sharedMaterial = mat;
        return tr;
    }

    private static GameObject BuildSyncedCrescentWaveHierarchy(
        Mesh syncedSlashMesh,
        Material matLayer2A,
        Material matLayer2B,
        Material matLayer3Needles,
        Material matLayer4Wake)
    {
        var root = new GameObject("SyncedCrescentWave_Root");
        var rootPs = root.AddComponent<ParticleSystem>();
        var rootMain = rootPs.main;
        rootMain.playOnAwake = false;
        rootMain.loop = false;
        rootMain.duration = 0.45f;
        var rootEmission = rootPs.emission;
        rootEmission.enabled = false;

        // Layer 2A: Main Sheer White Wind & Screen-Space Air Distortion Crescent
        var l2A = new GameObject("Layer2A_SheerWhiteAirDistortion");
        l2A.transform.SetParent(root.transform, false);
        ConfigureCrescentWaveLayer(
            l2A.AddComponent<ParticleSystem>(),
            l2A.GetComponent<ParticleSystemRenderer>(),
            syncedSlashMesh,
            matLayer2A,
            startSize: 1.32f,
            lifetime: 0.22f,
            forwardSpeed: 7.5f,
            peakAlpha: 0.55f);

        // Layer 2B: Outer Pure White Razor Qi Blade Edge
        var l2B = new GameObject("Layer2B_PureWhiteRazorEdge");
        l2B.transform.SetParent(root.transform, false);
        ConfigureCrescentWaveLayer(
            l2B.AddComponent<ParticleSystem>(),
            l2B.GetComponent<ParticleSystemRenderer>(),
            syncedSlashMesh,
            matLayer2B,
            startSize: 1.35f,
            lifetime: 0.18f,
            forwardSpeed: 8.2f,
            peakAlpha: 0.65f);

        // Layer 3: Secondary Elements - Stretched Billboard Pure White Wind Filaments (Zero blobs)
        var l3 = new GameObject("Layer3_StretchedWhiteWindFilaments");
        l3.transform.SetParent(root.transform, false);
        ConfigureLayer3WhiteWindFilaments(
            l3.AddComponent<ParticleSystem>(),
            l3.GetComponent<ParticleSystemRenderer>(),
            matLayer3Needles);

        // Layer 4: Impact & Residual - 0.45s Lingering Air Wake
        var l4 = new GameObject("Layer4_ResidualWhiteAirWake");
        l4.transform.SetParent(root.transform, false);
        ConfigureCrescentWaveLayer(
            l4.AddComponent<ParticleSystem>(),
            l4.GetComponent<ParticleSystemRenderer>(),
            syncedSlashMesh,
            matLayer4Wake,
            startSize: 1.40f,
            lifetime: 0.45f,
            forwardSpeed: 2.2f,
            peakAlpha: 0.32f);

        return root;
    }

    private static void ConfigureLayer1ScabbardDrawBurst(
        ParticleSystem ps,
        ParticleSystemRenderer psr,
        Material mat)
    {
        var main = ps.main;
        main.duration = 0.20f;
        main.loop = false;
        main.playOnAwake = false;
        main.startDelay = 0f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.06f, 0.09f); // 0.00s - 0.07s Phase 1 window
        main.startSpeed = new ParticleSystem.MinMaxCurve(6.0f, 12.0f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.08f);
        main.startColor = new Color(1f, 1f, 1f, 0.55f);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0.0f, 10) });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 22f;
        shape.radius = 0.08f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0.0f),
                new GradientColorKey(Color.white, 1.0f)
            },
            new[]
            {
                new GradientAlphaKey(0.0f, 0.0f),
                new GradientAlphaKey(0.55f, 0.20f),
                new GradientAlphaKey(0.0f, 1.0f)
            }
        );
        col.color = new ParticleSystem.MinMaxGradient(grad);

        // Stretched Billboard so scabbard burst looks like sharp air lines, never round blobs
        psr.renderMode = ParticleSystemRenderMode.Stretch;
        psr.cameraVelocityScale = 0f;
        psr.velocityScale = 0.06f;
        psr.lengthScale = 4.2f;
        psr.sharedMaterial = mat;
    }

    private static void ConfigureCrescentWaveLayer(
        ParticleSystem ps,
        ParticleSystemRenderer psr,
        Mesh crescentMesh,
        Material mat,
        float startSize,
        float lifetime,
        float forwardSpeed,
        float peakAlpha)
    {
        var main = ps.main;
        main.duration = 0.50f;
        main.loop = false;
        main.playOnAwake = false;
        main.startDelay = 0.0f; // Triggered directly at the start of Phase 2 (0.07s)
        main.startLifetime = lifetime;
        main.startSpeed = 0f;
        main.startSize = startSize;
        main.startRotation3D = true;
        main.startRotationX = 0f;
        main.startRotationY = -24f * Mathf.Deg2Rad;
        main.startRotationZ = 0f;
        main.startColor = Color.white;
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
        vel.z = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0.0f, forwardSpeed),
            new Keyframe(0.35f, forwardSpeed * 0.35f),
            new Keyframe(1.0f, 0.25f)
        ));

        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.separateAxes = true;
        rot.x = 0f;
        rot.z = 0f;
        rot.y = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0.0f, 420f * Mathf.Deg2Rad),
            new Keyframe(0.30f, 120f * Mathf.Deg2Rad),
            new Keyframe(1.0f, 10f * Mathf.Deg2Rad)
        ));

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0.0f, 0.78f),
            new Keyframe(0.25f, 1.02f),
            new Keyframe(1.0f, 1.15f)
        ));

        // Pure White (1, 1, 1) with soft Alpha (0.15..0.65)
        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0.0f),
                new GradientColorKey(Color.white, 1.0f)
            },
            new[]
            {
                new GradientAlphaKey(0.0f, 0.0f),
                new GradientAlphaKey(peakAlpha, 0.14f),
                new GradientAlphaKey(peakAlpha * 0.65f, 0.55f),
                new GradientAlphaKey(0.0f, 1.0f)
            }
        );
        col.color = new ParticleSystem.MinMaxGradient(grad);

        psr.renderMode = ParticleSystemRenderMode.Mesh;
        psr.mesh = crescentMesh;
        psr.alignment = ParticleSystemRenderSpace.Local;
        psr.sharedMaterial = mat;
    }

    private static void ConfigureLayer3WhiteWindFilaments(
        ParticleSystem ps,
        ParticleSystemRenderer psr,
        Material mat)
    {
        var main = ps.main;
        main.duration = 0.45f;
        main.loop = false;
        main.playOnAwake = false;
        main.startDelay = 0.0f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.14f, 0.24f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(8.0f, 15.0f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
        main.startColor = Color.white;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0.0f, 14) });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 34f;
        shape.radius = 1.10f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0.0f),
                new GradientColorKey(Color.white, 1.0f)
            },
            new[]
            {
                new GradientAlphaKey(0.0f, 0.0f),
                new GradientAlphaKey(0.50f, 0.15f),
                new GradientAlphaKey(0.0f, 1.0f)
            }
        );
        col.color = new ParticleSystem.MinMaxGradient(grad);

        psr.renderMode = ParticleSystemRenderMode.Stretch;
        psr.cameraVelocityScale = 0f;
        psr.velocityScale = 0.07f;
        psr.lengthScale = 4.5f;
        psr.sharedMaterial = mat;
    }

    private static void CreatePart(
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
}
#endif
