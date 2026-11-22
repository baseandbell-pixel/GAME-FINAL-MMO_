#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// AAA Wind Slash Wave Generator (4-Layer Architecture):
///   - Layer 1 (Anticipation): Layer1_WindEdgeFlash (Crisp White-Teal star compression flash on the blade)
///   - Layer 2 (Main Action):  Layer2_MainWindSlashWave (Multi-streak Aero Crescent Wave that sweeps AND shoots forward out of the sword)
///   - Layer 3 (Secondary):    Layer3_WindStreamRibbons (Turbulent swirling wind ribbons + air particles trailing the wave)
///   - Layer 4 (Residual):     Layer4_AirWakeMist (Soft dissipating air pressure mist left in the wake of the slash)
/// Automatically generates textures, HDR materials, prefabs, and binds SwordWindSlashVFX to the character prefabs.
/// </summary>
public static class Tool_01_Build_SwordSlash_WindBlade_OnPlayer
{
    private const string RootFolder = "Assets/yeen VFX/03_VFX_Prefabs_And_Assets";
    private const string TexFolder = "Assets/yeen VFX/03_VFX_Prefabs_And_Assets/Textures";
    private const string MatFolder = "Assets/yeen VFX/03_VFX_Prefabs_And_Assets/Materials";
    private const string PrefabFolder = "Assets/yeen VFX/03_VFX_Prefabs_And_Assets/Prefabs";
    private const string ResVfxFolder = "Assets/yeen VFX/Resources/VFX";
    private const string ResourcePrefabPath = "Assets/yeen VFX/Resources/VFX/VFX_WindBladeSlash.prefab";

    private static void AutoEnsureWindSlashBuilt()
    {
        // Superseded by Tool_03_Build_SwordSlash_WhiteWind_OnPlayer (Pure White 3-Point Synced System)
    }

    [MenuItem("yeen VFX/01. [ใส่บน Player] สร้างเอฟเฟกต์ฟันดาบสายลม (Wind Blade Slash)")]
    public static void GenerateWorkshop1Menu()
    {
        BuildWindSlashAssetsAndBind(spawnScenePreview: true);
    }

    public static void BuildWindSlashAssetsAndBind(bool spawnScenePreview)
    {
        EnsureFolders();
        SetupPostProcessingInScene();

        // 1. Generate Procedural Wind Textures
        Texture2D flareTex = GenerateStarFlareTexture(TexFolder + "/T_VFX_WindSpark.png", 256);
        Texture2D windCrescentTex = GenerateWindCrescentTexture(TexFolder + "/T_VFX_WindSlashCrescent.png", 512);
        Texture2D ribbonTex = GenerateWindRibbonTexture(TexFolder + "/T_VFX_WindStreakTrail.png", 256);
        Texture2D mistTex = GenerateSoftMistTexture(TexFolder + "/T_VFX_WindMist.png", 256);

        // 2. Create HDR Additive & Alpha Materials
        Material flashMat = CreateParticleMaterial(
            MatFolder + "/M_VFX_WindFlash_Add.mat",
            flareTex,
            new Color(0.55f, 1.0f, 0.90f, 1f),
            hdrIntensity: 4.2f,
            additive: true);

        Material windWaveMat = CreateParticleMaterial(
            MatFolder + "/M_VFX_WindSlashWave_Add.mat",
            windCrescentTex,
            new Color(0.38f, 0.98f, 0.85f, 1f),
            hdrIntensity: 3.8f,
            additive: true);

        Material ribbonMat = CreateParticleMaterial(
            MatFolder + "/M_VFX_WindRibbon_Add.mat",
            ribbonTex,
            new Color(0.45f, 1.0f, 0.92f, 1f),
            hdrIntensity: 3.2f,
            additive: true);

        Material mistMat = CreateParticleMaterial(
            MatFolder + "/M_VFX_WindWakeMist_Alpha.mat",
            mistTex,
            new Color(0.65f, 0.95f, 0.90f, 0.42f),
            hdrIntensity: 1.2f,
            additive: false);

        // 3. Build 4-Layer Wind Slash Hierarchy
        GameObject tempRoot = Build4LayerWindSlashHierarchy(flashMat, windWaveMat, flareTex, ribbonMat, mistMat, loopForPreview: false);

        string prefabPath = PrefabFolder + "/VFX_WindBladeSlash.prefab";
        PrefabUtility.SaveAsPrefabAsset(tempRoot, prefabPath);
        GameObject resPrefab = PrefabUtility.SaveAsPrefabAsset(tempRoot, ResourcePrefabPath);
        Object.DestroyImmediate(tempRoot);

        // 4. Bind SwordWindSlashVFX to both GreatSwordGirl.prefab and Player.prefab
        BindVfxToPrefab("Assets/Organ/Models/Prefab/GreatSwordPrefab/GreatSwordGirl.prefab", resPrefab);
        BindVfxToPrefab("Assets/_Project/Prefabs/Player.prefab", resPrefab);

        // 5. Optional: Spawn looping scene preview so the student can inspect all 4 layers in the Inspector
        if (spawnScenePreview)
        {
            GameObject existingPreview = GameObject.Find("VFX_WindBladeSlash_Preview");
            if (existingPreview != null) Object.DestroyImmediate(existingPreview);

            GameObject previewRoot = Build4LayerWindSlashHierarchy(flashMat, windWaveMat, flareTex, ribbonMat, mistMat, loopForPreview: true);
            previewRoot.name = "VFX_WindBladeSlash_Preview";
            previewRoot.transform.position = new Vector3(0f, 1.3f, 0f);
            Selection.activeGameObject = previewRoot;
            EditorGUIUtility.PingObject(resPrefab);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("==========================================================================");
        Debug.Log("[MMO VFX] 4-Layer Wind Slash Wave (VFX_WindBladeSlash) generated & bound to character sword!");
        Debug.Log("Press Play and Left-Click to see the Wind Wave shoot out of your GreatSword!");
        Debug.Log("==========================================================================");
    }

    private static void BindVfxToPrefab(string assetPath, GameObject windSlashPrefab)
    {
        GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (prefabRoot == null) return;

        var vfx = prefabRoot.GetComponent<SwordWindSlashVFX>();
        if (vfx == null) vfx = prefabRoot.AddComponent<SwordWindSlashVFX>();
        vfx.windSlashPrefab = windSlashPrefab;
        EditorUtility.SetDirty(prefabRoot);
    }

    private static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/yeen VFX"))
            AssetDatabase.CreateFolder("Assets", "yeen VFX");
        if (!AssetDatabase.IsValidFolder(RootFolder))
            AssetDatabase.CreateFolder("Assets/yeen VFX", "03_VFX_Prefabs_And_Assets");
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

    private static void SetupPostProcessingInScene()
    {
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            var camData = mainCam.GetComponent<UniversalAdditionalCameraData>();
            if (camData == null) camData = mainCam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            EditorUtility.SetDirty(mainCam.gameObject);
        }

        Volume globalVol = Object.FindAnyObjectByType<Volume>();
        if (globalVol != null && globalVol.sharedProfile != null)
        {
            VolumeProfile vp = globalVol.sharedProfile;
            if (!vp.TryGet(out Bloom bloom)) bloom = vp.Add<Bloom>(true);
            bloom.active = true;
            bloom.threshold.overrideState = true;
            bloom.threshold.value = 0.9f;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 1.25f;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.65f;
            EditorUtility.SetDirty(vp);
        }
    }

    // ------------------------------------------------------------------ Procedural Wind Textures

    /// <summary>
    /// Generates a multi-layered Wind Blade Crescent texture with sharp leading edge + feathered wind streaks.
    /// </summary>
    private static Texture2D GenerateWindCrescentTexture(string assetPath, int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[size * size];
        float half = (size - 1) * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = (x - half) / half; // -1..1
                float ny = (y - half) / half; // -1..1
                float r = Mathf.Sqrt(nx * nx + ny * ny);
                float angle = Mathf.Atan2(ny, nx) * Mathf.Rad2Deg; // -180..180, 0 is +X (forward direction of wave)

                // Wind arc spans -105..+105 degrees
                float normAngle = Mathf.Abs(angle) / 105f;
                if (normAngle >= 1f || r < 0.22f || r > 0.98f)
                {
                    pixels[y * size + x] = Color.clear;
                    continue;
                }

                // Smooth tapering at both tips of the wind blade
                float tipFalloff = Mathf.Sin((1f - normAngle) * Mathf.PI * 0.5f);
                tipFalloff = Mathf.Pow(tipFalloff, 1.25f);

                float outerArcRadius = 0.82f - normAngle * 0.06f;
                float thickness = Mathf.Lerp(0.03f, 0.34f, tipFalloff);
                float distFromOuter = r - outerArcRadius;

                float alpha = 0f;
                if (distFromOuter >= 0f)
                {
                    // Razor-sharp outer wind pressure edge
                    float edge = Mathf.Clamp01(1f - (distFromOuter / (thickness * 0.22f)));
                    alpha = Mathf.Pow(edge, 2.2f);
                }
                else
                {
                    // Inner multi-layered wind streams (3 distinct air current bands)
                    float innerNorm = Mathf.Clamp01(1f - (-distFromOuter / thickness));
                    float mainBody = Mathf.Pow(innerNorm, 1.5f);

                    // High-frequency wind streaks along the arc
                    float streak1 = Mathf.Sin(r * 65f + angle * 0.18f) * 0.5f + 0.5f;
                    float streak2 = Mathf.Sin(r * 130f - angle * 0.25f) * 0.5f + 0.5f;
                    float windStreaks = Mathf.Lerp(0.45f, 1.15f, streak1 * 0.65f + streak2 * 0.35f);

                    // Secondary thin inner wind ring
                    float innerRing = Mathf.Exp(-Mathf.Pow((innerNorm - 0.38f) * 7.5f, 2f)) * 0.65f;

                    alpha = (mainBody * windStreaks + innerRing);
                }

                alpha = Mathf.Clamp01(alpha * tipFalloff);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return SaveAndImportTexture(tex, assetPath);
    }

    private static Texture2D GenerateStarFlareTexture(string assetPath, int size)
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
                float dist = Mathf.Sqrt(nx * nx + ny * ny);

                float core = Mathf.Pow(Mathf.Clamp01(1f - dist), 3.0f);
                float spikeX = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(ny) * 7.0f), 2.5f) * Mathf.Clamp01(1f - Mathf.Abs(nx));
                float spikeY = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(nx) * 7.0f), 2.5f) * Mathf.Clamp01(1f - Mathf.Abs(ny));
                float alpha = Mathf.Clamp01(core + (spikeX + spikeY) * 0.85f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return SaveAndImportTexture(tex, assetPath);
    }

    private static Texture2D GenerateWindRibbonTexture(string assetPath, int size)
    {
        var tex = new Texture2D(size, 64, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[size * 64];

        for (int y = 0; y < 64; y++)
        {
            float ny = Mathf.Abs((y - 31.5f) / 31.5f); // 0 at center, 1 at top/bottom edge
            float vMask = Mathf.Pow(Mathf.Clamp01(1f - ny), 2.2f);

            for (int x = 0; x < size; x++)
            {
                float u = x / (float)(size - 1); // 0..1 along trail length
                float uMask = Mathf.Sin(u * Mathf.PI);
                pixels[y * size + x] = new Color(1f, 1f, 1f, vMask * uMask);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return SaveAndImportTexture(tex, assetPath);
    }

    private static Texture2D GenerateSoftMistTexture(string assetPath, int size)
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
                float radial = Mathf.Clamp01(1f - r);
                float noise = Mathf.PerlinNoise(nx * 2.8f + 4.1f, ny * 2.8f + 1.7f);
                float alpha = Mathf.Pow(radial, 1.8f) * Mathf.Lerp(0.55f, 1.0f, noise);
                pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return SaveAndImportTexture(tex, assetPath);
    }

    private static Texture2D SaveAndImportTexture(Texture2D tex, string assetPath)
    {
        byte[] png = tex.EncodeToPNG();
        File.WriteAllBytes(assetPath, png);
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
    }

    // ------------------------------------------------------------------ Material Builder

    private static Material CreateParticleMaterial(string matPath, Texture2D mainTex, Color baseColor, float hdrIntensity, bool additive)
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

        float factor = Mathf.Pow(2f, hdrIntensity * 0.5f);
        Color hdrColor = new Color(baseColor.r * factor, baseColor.g * factor, baseColor.b * factor, baseColor.a);

        mat.SetTexture("_BaseMap", mainTex);
        mat.SetColor("_BaseColor", hdrColor);
        mat.SetFloat("_Surface", 1f); // Transparent
        mat.SetFloat("_Blend", additive ? 2f : 0f); // 2 = Additive, 0 = Alpha
        mat.SetFloat("_Cull", 0f);    // Render Both Faces
        mat.SetFloat("_ZWrite", 0f);
        mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent;

        EditorUtility.SetDirty(mat);
        return mat;
    }

    // ------------------------------------------------------------------ 4-Layer Hierarchy Builder

    private static GameObject Build4LayerWindSlashHierarchy(
        Material flashMat,
        Material windWaveMat,
        Texture2D sparkTex,
        Material ribbonMat,
        Material mistMat,
        bool loopForPreview)
    {
        var root = new GameObject("VFX_WindBladeSlash");

        // =========================================================================
        // LAYER 1: ANTICIPATION / WIND EDGE FLASH (0.00s - 0.14s)
        // =========================================================================
        var l1 = new GameObject("Layer1_WindEdgeFlash");
        l1.transform.SetParent(root.transform, false);
        l1.transform.localPosition = new Vector3(0f, 0f, -0.2f);
        ConfigureLayer1WindFlash(l1.AddComponent<ParticleSystem>(), l1.GetComponent<ParticleSystemRenderer>(), flashMat, loopForPreview);

        // =========================================================================
        // LAYER 2: MAIN ACTION / FLYING WIND SLASH WAVE (0.01s - 0.34s)
        // =========================================================================
        var l2 = new GameObject("Layer2_MainWindSlashWave");
        l2.transform.SetParent(root.transform, false);
        l2.transform.localPosition = Vector3.zero;
        ConfigureLayer2MainWindWave(l2.AddComponent<ParticleSystem>(), l2.GetComponent<ParticleSystemRenderer>(), windWaveMat, loopForPreview);

        // =========================================================================
        // LAYER 3: SECONDARY / SWIRLING WIND STREAM RIBBONS (0.02s - 0.45s)
        // =========================================================================
        var l3 = new GameObject("Layer3_WindStreamRibbons");
        l3.transform.SetParent(root.transform, false);
        l3.transform.localPosition = Vector3.zero;
        ConfigureLayer3WindRibbons(l3.AddComponent<ParticleSystem>(), l3.GetComponent<ParticleSystemRenderer>(), flashMat, ribbonMat, loopForPreview);

        // =========================================================================
        // LAYER 4: RESIDUAL / AIR WAKE DISTORTION MIST (0.05s - 0.55s)
        // =========================================================================
        var l4 = new GameObject("Layer4_AirWakeMist");
        l4.transform.SetParent(root.transform, false);
        l4.transform.localPosition = new Vector3(0f, 0f, 0.6f);
        ConfigureLayer4AirWakeMist(l4.AddComponent<ParticleSystem>(), l4.GetComponent<ParticleSystemRenderer>(), mistMat, loopForPreview);

        return root;
    }

    private static void ConfigureLayer1WindFlash(ParticleSystem ps, ParticleSystemRenderer psr, Material mat, bool loop)
    {
        var main = ps.main;
        main.duration = 0.85f;
        main.loop = loop;
        main.startDelay = 0f;
        main.startLifetime = 0.14f;
        main.startSpeed = 0f;
        main.startSize = 1.6f;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.playOnAwake = true;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0.0f, 2) });

        var shape = ps.shape;
        shape.enabled = false;

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0.0f, 0.25f),
            new Keyframe(0.20f, 1.0f),
            new Keyframe(1.0f, 0.0f)
        ));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0.0f),
                new GradientColorKey(new Color(0.55f, 1.0f, 0.88f), 0.5f),
                new GradientColorKey(new Color(0.15f, 0.85f, 0.95f), 1.0f)
            },
            new[]
            {
                new GradientAlphaKey(1.0f, 0.0f),
                new GradientAlphaKey(0.85f, 0.35f),
                new GradientAlphaKey(0.0f, 1.0f)
            }
        );
        col.color = new ParticleSystem.MinMaxGradient(grad);

        psr.renderMode = ParticleSystemRenderMode.Billboard;
        psr.sharedMaterial = mat;
    }

    private static void ConfigureLayer2MainWindWave(ParticleSystem ps, ParticleSystemRenderer psr, Material mat, bool loop)
    {
        var main = ps.main;
        main.duration = 0.85f;
        main.loop = loop;
        main.startDelay = 0.01f;
        main.startLifetime = 0.32f;
        main.startSpeed = 0f; // Forward motion is controlled via Velocity over Lifetime curve for Easing!
        main.startSize = 3.6f;
        main.startRotation3D = true;
        // Lay the quad flat on the local XZ swing plane, facing its crescent forward along +Z
        main.startRotationX = 90f * Mathf.Deg2Rad;
        main.startRotationY = -90f * Mathf.Deg2Rad;
        main.startRotationZ = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.playOnAwake = true;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        // 3 overlapping wind arcs: creates a razor-sharp White-Hot center + wide emerald-cyan wind aura!
        emission.SetBursts(new[]
        {
            new ParticleSystem.Burst(0.0f, 2),
            new ParticleSystem.Burst(0.03f, 1) // Secondary trailing wind echo wave!
        });

        var shape = ps.shape;
        shape.enabled = false;

        // KEY TO FLYING WIND WAVE: Shoot forward along +Z fast (16 m/s) then decelerate smoothly (3 m/s)
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = new ParticleSystem.MinMaxCurve(0f);
        vel.y = new ParticleSystem.MinMaxCurve(0f);
        AnimationCurve forwardSpeedCurve = new AnimationCurve(
            new Keyframe(0.0f, 16.5f),
            new Keyframe(0.35f, 8.5f),
            new Keyframe(1.0f, 2.0f)
        );
        vel.z = new ParticleSystem.MinMaxCurve(1f, forwardSpeedCurve);

        // Rotational whip as the wind wave leaves the blade
        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.separateAxes = true;
        rot.x = 0f;
        rot.y = 0f;
        rot.z = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0.0f, 360f * Mathf.Deg2Rad),
            new Keyframe(0.4f, 110f * Mathf.Deg2Rad),
            new Keyframe(1.0f, 15f * Mathf.Deg2Rad)
        ));

        // Expand width as the wind wave travels forward
        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0.0f, 0.65f),
            new Keyframe(0.25f, 1.05f),
            new Keyframe(1.0f, 1.32f)
        ));

        // Color Over Lifetime: White-Hot Blade -> Emerald Mint Wind -> Sky Cyan fade
        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0.0f),
                new GradientColorKey(new Color(0.62f, 1.0f, 0.88f), 0.28f),
                new GradientColorKey(new Color(0.18f, 0.85f, 0.98f), 0.75f),
                new GradientColorKey(new Color(0.08f, 0.45f, 0.85f), 1.0f)
            },
            new[]
            {
                new GradientAlphaKey(0.0f, 0.0f),
                new GradientAlphaKey(1.0f, 0.10f),
                new GradientAlphaKey(0.85f, 0.55f),
                new GradientAlphaKey(0.0f, 1.0f)
            }
        );
        col.color = new ParticleSystem.MinMaxGradient(grad);

        psr.renderMode = ParticleSystemRenderMode.Mesh;
        GameObject tempQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Mesh quadMesh = tempQuad.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(tempQuad);
        psr.mesh = quadMesh;
        psr.alignment = ParticleSystemRenderSpace.Local;
        psr.sharedMaterial = mat;
    }

    private static void ConfigureLayer3WindRibbons(ParticleSystem ps, ParticleSystemRenderer psr, Material particleMat, Material trailMat, bool loop)
    {
        var main = ps.main;
        main.duration = 0.85f;
        main.loop = loop;
        main.startDelay = 0.01f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.28f, 0.44f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(7.5f, 14.0f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.22f);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.playOnAwake = true;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0.0f, 14) });

        // Cone/Box arc emitting forward along the wind wave
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 32f;
        shape.radius = 0.9f;
        shape.rotation = new Vector3(0f, 0f, 0f);

        // Air turbulence (Noise Module) so the wind streams swirl organically!
        var noise = ps.noise;
        noise.enabled = true;
        noise.separateAxes = true;
        noise.strengthX = 2.4f;
        noise.strengthY = 1.6f;
        noise.strengthZ = 1.2f;
        noise.frequency = 1.8f;
        noise.scrollSpeed = 2.5f;

        // Trails Module: creates sleek wind ribbons behind each air particle
        var trails = ps.trails;
        trails.enabled = true;
        trails.ratio = 0.85f;
        trails.lifetime = new ParticleSystem.MinMaxCurve(0.45f);
        trails.minVertexDistance = 0.06f;
        trails.dieWithParticles = false;
        trails.sizeAffectsWidth = true;
        trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.0f),
            new Keyframe(0.2f, 1.0f),
            new Keyframe(1f, 0.0f)
        ));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0.0f),
                new GradientColorKey(new Color(0.50f, 1.0f, 0.86f), 0.45f),
                new GradientColorKey(new Color(0.20f, 0.80f, 1.0f), 1.0f)
            },
            new[]
            {
                new GradientAlphaKey(0.0f, 0.0f),
                new GradientAlphaKey(0.95f, 0.15f),
                new GradientAlphaKey(0.0f, 1.0f)
            }
        );
        col.color = new ParticleSystem.MinMaxGradient(grad);

        psr.renderMode = ParticleSystemRenderMode.Billboard;
        psr.sharedMaterial = particleMat;
        psr.trailMaterial = trailMat;
    }

    private static void ConfigureLayer4AirWakeMist(ParticleSystem ps, ParticleSystemRenderer psr, Material mat, bool loop)
    {
        var main = ps.main;
        main.duration = 0.85f;
        main.loop = loop;
        main.startDelay = 0.03f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.52f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(3.5f, 6.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(1.4f, 2.4f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.playOnAwake = true;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0.0f, 6) });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 25f;
        shape.radius = 0.7f;

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.6f),
            new Keyframe(1f, 1.45f)
        ));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.75f, 1.0f, 0.95f), 0.0f),
                new GradientColorKey(new Color(0.35f, 0.85f, 0.90f), 1.0f)
            },
            new[]
            {
                new GradientAlphaKey(0.0f, 0.0f),
                new GradientAlphaKey(0.32f, 0.20f),
                new GradientAlphaKey(0.0f, 1.0f)
            }
        );
        col.color = new ParticleSystem.MinMaxGradient(grad);

        psr.renderMode = ParticleSystemRenderMode.Billboard;
        psr.sharedMaterial = mat;
    }
}
#endif
