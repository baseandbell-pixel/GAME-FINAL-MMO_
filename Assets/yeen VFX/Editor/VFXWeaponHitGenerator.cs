using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Editor Generator สำหรับสร้างระบบเอฟเฟกต์ "โจมตีโดนมอนสเตอร์ 4 เลเยอร์ระดับ AAA (Weapon Hit Impact VFX)"
/// กดคลิกเดียวจากเมนู:
/// VFX Mentor/18. สร้างเอฟเฟกต์โจมตีโดนมอนสเตอร์ 4 เลเยอร์ (Weapon Hit Impact VFX)
/// จะสร้างไฟล์ทั้งหมดลงใน Assets/yeen VFX/03_VFX_Prefabs_And_Assets/Generated_WeaponHitImpact อัตโนมัติ:
/// - T_WeaponHitSlash.png (512x256 px): รอยตัดพลังงานเส้นเดี่ยวปลายเรียวแหลมคมกริบทั้ง 2 ด้าน
/// - T_WeaponHitCore.png (256x256 px): แกนแสงปะทะทรงกลมฟุ้งนุ่มผสานแฉกแสงแนวนอน
/// - T_WeaponHitWave.png (256x256 px): วงแหวนคลื่นอากาศขอบคม
/// - Material แบบ URP Particles/Unlit Additive HDR (Surface = Transparent, Blend = Additive, Cull = Off)
/// - VFX_WeaponHitImpact.prefab (โครงสร้าง 4 เลเยอร์ครบสมบูรณ์)
/// พร้อมผูกเข้ากับตัวละครที่เลือกใน Hierarchy อัตโนมัติ
/// </summary>
public static class VFXWeaponHitGenerator
{
    private const string OutputFolder = "Assets/yeen VFX/03_VFX_Prefabs_And_Assets/Generated_WeaponHitImpact";
    private const string SlashTexPath = OutputFolder + "/T_WeaponHitSlash.png";
    private const string CoreTexPath = OutputFolder + "/T_WeaponHitCore.png";
    private const string WaveTexPath = OutputFolder + "/T_WeaponHitWave.png";

    private const string MatCorePath = OutputFolder + "/Mat_WeaponHitCore_Add.mat";
    private const string MatSlashPath = OutputFolder + "/Mat_WeaponHitSlash_Add.mat";
    private const string MatSparkPath = OutputFolder + "/Mat_WeaponHitSparks_Add.mat";
    private const string MatWavePath = OutputFolder + "/Mat_WeaponHitWave_Add.mat";

    private const string PrefabPath = OutputFolder + "/VFX_WeaponHitImpact.prefab";

    [MenuItem("yeen VFX/14. [เกิดตอนตีศัตรู] สร้างเอฟเฟกต์โจมตีโดนมอนสเตอร์ 4 เลเยอร์ (Weapon Hit Impact)")]
    [MenuItem("VFX Mentor/18. สร้างเอฟเฟกต์โจมตีโดนมอนสเตอร์ 4 เลเยอร์ (Weapon Hit Impact VFX)")]
    public static void GenerateWeaponHitImpactVFX()
    {
        EnsureOutputFolder();
        EnsureTagExists("Enemy");

        // 1. วาดและบันทึกไฟล์ Texture 3 ใบด้วยสมการคณิตศาสตร์
        Texture2D slashTex = SaveTexturePng(GenerateWeaponHitSlashTexture(512, 256), SlashTexPath);
        Texture2D coreTex = SaveTexturePng(GenerateWeaponHitCoreTexture(256, 256), CoreTexPath);
        Texture2D waveTex = SaveTexturePng(GenerateWeaponHitWaveTexture(256, 256), WaveTexPath);

        // 2. สร้างและบันทึก Material แบบ URP Additive HDR (ตั้งค่า HDR Intensity บน _BaseColor เพื่อ Bloom จริงใน URP)
        Material matCore = CreateOrUpdateAdditiveMaterial(MatCorePath, coreTex, 4.5f);
        Material matSlash = CreateOrUpdateAdditiveMaterial(MatSlashPath, slashTex, 3.8f);
        Material matSpark = CreateOrUpdateAdditiveMaterial(MatSparkPath, coreTex, 4.2f);
        Material matWave = CreateOrUpdateAdditiveMaterial(MatWavePath, waveTex, 2.8f);

        // 3. ประกอบโครงสร้าง Prefab 4 เลเยอร์ครบตามกฎเหล็ก
        Mesh quadMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        GameObject root = new GameObject("VFX_WeaponHitImpact");

        try
        {
            BuildLayer1CoreContactFlare(root.transform, matCore);
            BuildLayer2DirectionalImpactSlash(root.transform, quadMesh, matSlash);
            BuildLayer3KineticHitSparks(root.transform, matSpark);
            BuildLayer4AirPressureShockwave(root.transform, quadMesh, matWave);

            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // 4. ผูกเข้ากับตัวละครที่เลือกใน Hierarchy อัตโนมัติ (หรือสร้าง Tester ให้หากไม่ได้เลือกไว้)
            BindPrefabToSelectedObject(savedPrefab);

            EditorGUIUtility.PingObject(savedPrefab);
            Debug.Log($"[VFXWeaponHitGenerator] สร้าง Weapon Hit Impact VFX 4 เลเยอร์สำเร็จที่: {PrefabPath}");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // =========================================================================
    // LAYER 1 - LAYER 4 BUILDERS
    // =========================================================================

    /// <summary>
    /// Layer 1 (Anticipation / Core Contact Flare - 0.00s - 0.11s)
    /// Billboard, Start Size = 1.75, สีขาวอมเหลือง HDR 4.5x, Explosive Ease-Out
    /// </summary>
    private static void BuildLayer1CoreContactFlare(Transform parent, Material mat)
    {
        ParticleSystem ps = CreateParticleLayer(parent, "L1_CoreContactFlare");

        var main = ps.main;
        main.duration = 0.15f;
        main.loop = false;
        main.playOnAwake = true;
        main.startDelay = 0.00f;
        main.startLifetime = 0.11f;
        main.startSpeed = 0.0f;
        main.startSize = 1.75f;
        main.startColor = Color.white; // HDR Intensity 4.5x ขับผ่าน Mat_WeaponHitCore_Add._BaseColor
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 4;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0.00f, 1) });

        var shape = ps.shape;
        shape.enabled = false;

        // กราฟ Explosive Ease-Out ระเบิดวาบตรงจุดสัมผัส
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1.0f, new AnimationCurve(
            new Keyframe(0.00f, 0.18f, 8.2f, 8.2f),
            new Keyframe(0.24f, 1.05f, 0.4f, 0.4f),
            new Keyframe(1.00f, 0.00f, -1.8f, -1.8f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1.00f, 0.99f, 0.84f), 0.00f),
                new GradientColorKey(new Color(1.00f, 0.86f, 0.36f), 0.55f),
                new GradientColorKey(new Color(1.00f, 0.62f, 0.15f), 1.00f)
            },
            new[]
            {
                new GradientAlphaKey(1.00f, 0.00f),
                new GradientAlphaKey(0.95f, 0.35f),
                new GradientAlphaKey(0.00f, 1.00f)
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sortingOrder = 12;
    }

    /// <summary>
    /// Layer 2 (Main Action / Directional Impact Slash Cut - 0.01s - 0.20s)
    /// Mesh (Quad), Alignment = Local, Start Size = 2.4 เมตร, สีฟ้าครามขอบขาว HDR 3.8x
    /// Size over Lifetime (Separate Axes): ยืดขยายความยาวอย่างรวดเร็วใน 0.04s แรกและบีบความหนาให้เรียวแหลมหายไป
    /// </summary>
    private static void BuildLayer2DirectionalImpactSlash(Transform parent, Mesh quadMesh, Material mat)
    {
        ParticleSystem ps = CreateParticleLayer(parent, "L2_DirectionalImpactSlash");

        var main = ps.main;
        main.duration = 0.25f;
        main.loop = false;
        main.playOnAwake = true;
        main.startDelay = 0.01f;
        main.startLifetime = 0.19f; // 0.01s -> 0.20s
        main.startSpeed = 0.0f;
        main.startSize = 2.4f;
        main.startColor = Color.white; // HDR Intensity 3.8x ขับผ่าน Mat_WeaponHitSlash_Add._BaseColor
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 4;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0.00f, 1) });

        var shape = ps.shape;
        shape.enabled = false;

        // Size over Lifetime (Separate Axes): X ยืดอย่างเร็วใน 0.04s แรก (t = 0.21), Y บีบเรียวแหลมจนหายไป
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.separateAxes = true;
        sol.x = new ParticleSystem.MinMaxCurve(1.0f, new AnimationCurve(
            new Keyframe(0.00f, 0.22f, 9.5f, 9.5f),
            new Keyframe(0.21f, 1.24f, 0.6f, 0.6f),
            new Keyframe(1.00f, 1.48f, 0.1f, 0.1f)));
        sol.y = new ParticleSystem.MinMaxCurve(1.0f, new AnimationCurve(
            new Keyframe(0.00f, 0.72f, 2.2f, 2.2f),
            new Keyframe(0.16f, 0.92f, -0.5f, -0.5f),
            new Keyframe(0.58f, 0.24f, -1.2f, -1.2f),
            new Keyframe(1.00f, 0.00f, -0.4f, -0.4f)));
        sol.z = new ParticleSystem.MinMaxCurve(1.0f, new AnimationCurve(
            new Keyframe(0.00f, 1.0f),
            new Keyframe(1.00f, 1.0f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.92f, 0.99f, 1.00f), 0.00f),
                new GradientColorKey(new Color(0.22f, 0.88f, 1.00f), 0.35f),
                new GradientColorKey(new Color(0.06f, 0.48f, 1.00f), 1.00f)
            },
            new[]
            {
                new GradientAlphaKey(0.00f, 0.00f),
                new GradientAlphaKey(1.00f, 0.08f),
                new GradientAlphaKey(0.88f, 0.50f),
                new GradientAlphaKey(0.00f, 1.00f)
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = quadMesh;
        renderer.alignment = ParticleSystemRenderSpace.Local;
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sortingOrder = 11;
    }

    /// <summary>
    /// Layer 3 (Secondary Elements / High-Velocity Kinetic Hit Sparks - 0.02s - 0.36s)
    /// Stretched Billboard, Simulation Space = World, Velocity Scale = 0.055, Length Scale = 4.0,
    /// Gravity Modifier = 0.9, Burst 24 เส้น, สีทองส้ม HDR
    /// </summary>
    private static void BuildLayer3KineticHitSparks(Transform parent, Material mat)
    {
        ParticleSystem ps = CreateParticleLayer(parent, "L3_KineticHitSparks");

        var main = ps.main;
        main.duration = 0.42f;
        main.loop = false;
        main.playOnAwake = true;
        main.startDelay = 0.02f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.34f); // 0.02s -> 0.36s
        main.startSpeed = new ParticleSystem.MinMaxCurve(6.8f, 14.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
        main.startColor = Color.white; // HDR Intensity 4.2x ขับผ่าน Mat_WeaponHitSparks_Add._BaseColor
        main.gravityModifier = 0.9f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 36;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0.00f, 24) });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.16f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1.00f, 0.96f, 0.72f), 0.00f),
                new GradientColorKey(new Color(1.00f, 0.58f, 0.10f), 0.42f),
                new GradientColorKey(new Color(0.95f, 0.22f, 0.02f), 1.00f)
            },
            new[]
            {
                new GradientAlphaKey(1.00f, 0.00f),
                new GradientAlphaKey(0.90f, 0.45f),
                new GradientAlphaKey(0.00f, 1.00f)
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1.0f, new AnimationCurve(
            new Keyframe(0.00f, 1.00f),
            new Keyframe(1.00f, 0.18f)));

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.055f;
        renderer.lengthScale = 4.0f;
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sortingOrder = 13;
    }

    /// <summary>
    /// Layer 4 (Impact & Residual / Air Pressure Shockwave Ring - 0.03s - 0.24s)
    /// Mesh (Quad), Alignment = Local, Start Size = 2.15, สีฟ้าอ่อน HDR 2.8x
    /// </summary>
    private static void BuildLayer4AirPressureShockwave(Transform parent, Mesh quadMesh, Material mat)
    {
        ParticleSystem ps = CreateParticleLayer(parent, "L4_AirPressureShockwave");

        var main = ps.main;
        main.duration = 0.30f;
        main.loop = false;
        main.playOnAwake = true;
        main.startDelay = 0.03f;
        main.startLifetime = 0.21f; // 0.03s -> 0.24s
        main.startSpeed = 0.0f;
        main.startSize = 2.15f;
        main.startColor = Color.white; // HDR Intensity 2.8x ขับผ่าน Mat_WeaponHitWave_Add._BaseColor
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 4;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0.00f, 1) });

        var shape = ps.shape;
        shape.enabled = false;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1.0f, new AnimationCurve(
            new Keyframe(0.00f, 0.18f, 4.2f, 4.2f),
            new Keyframe(0.42f, 0.92f, 0.9f, 0.9f),
            new Keyframe(1.00f, 1.22f, 0.1f, 0.1f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.80f, 0.97f, 1.00f), 0.00f),
                new GradientColorKey(new Color(0.32f, 0.84f, 1.00f), 0.45f),
                new GradientColorKey(new Color(0.14f, 0.62f, 1.00f), 1.00f)
            },
            new[]
            {
                new GradientAlphaKey(0.95f, 0.00f),
                new GradientAlphaKey(0.75f, 0.35f),
                new GradientAlphaKey(0.00f, 1.00f)
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = quadMesh;
        renderer.alignment = ParticleSystemRenderSpace.Local;
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sortingOrder = 10;
    }

    private static ParticleSystem CreateParticleLayer(Transform parent, string layerName)
    {
        GameObject go = new GameObject(layerName);
        go.transform.SetParent(parent, false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        return ps;
    }

    // =========================================================================
    // PROCEDURAL TEXTURES (3 ใบ: Slash 512x256, Core 256x256, Wave 256x256)
    // =========================================================================

    /// <summary>
    /// T_WeaponHitSlash.png (512x256 px): รอยตัดพลังงานเส้นเดี่ยวปลายเรียวแหลมคมกริบทั้ง 2 ด้าน
    /// </summary>
    private static Texture2D GenerateWeaponHitSlashTexture(int width, int height)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[width * height];

        for (int y = 0; y < height; y++)
        {
            float ny = (y / (float)(height - 1)) * 2f - 1f;
            for (int x = 0; x < width; x++)
            {
                float nx = (x / (float)(width - 1)) * 2f - 1f;
                float ax = Mathf.Abs(nx);
                if (ax >= 0.99f)
                {
                    pixels[y * width + x] = Color.clear;
                    continue;
                }

                float lengthProfile = Mathf.Pow(Mathf.Clamp01(1f - ax), 0.72f);
                float thickness = Mathf.Max(0.012f, 0.20f * lengthProfile);
                float razorCore = Mathf.Exp(-(ny * ny) / (thickness * thickness * 0.18f));
                float cyanHalo = Mathf.Exp(-(ny * ny) / (thickness * thickness * 1.15f)) * 0.65f;
                float tipMask = 1f - SmoothStep(0.84f, 0.99f, ax);

                float val = Mathf.Clamp01((razorCore + cyanHalo) * tipMask);
                Color col = Color.Lerp(new Color(0.55f, 0.92f, 1.0f, 1f), Color.white, Mathf.Clamp01(razorCore * 1.25f));
                col.a = val;
                pixels[y * width + x] = col;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    /// <summary>
    /// T_WeaponHitCore.png (256x256 px): แกนแสงปะทะทรงกลมฟุ้งนุ่มผสานแฉกแสงแนวนอน
    /// </summary>
    private static Texture2D GenerateWeaponHitCoreTexture(int width, int height)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[width * height];

        for (int y = 0; y < height; y++)
        {
            float ny = (y / (float)(height - 1)) * 2f - 1f;
            for (int x = 0; x < width; x++)
            {
                float nx = (x / (float)(width - 1)) * 2f - 1f;
                float r = Mathf.Sqrt(nx * nx + ny * ny);
                float edgeMask = 1f - SmoothStep(0.76f, 0.98f, r);

                float sphereCore = Mathf.Exp(-r * r * 14.0f);
                float softHalo = Mathf.Exp(-r * r * 4.2f) * 0.48f;
                float horizontalStreak = Mathf.Exp(-Mathf.Abs(ny) * 16.0f) * Mathf.Exp(-Mathf.Abs(nx) * 2.1f) * 0.92f;
                float verticalGlint = Mathf.Exp(-Mathf.Abs(nx) * 20.0f) * Mathf.Exp(-Mathf.Abs(ny) * 5.2f) * 0.35f;

                float val = Mathf.Clamp01((sphereCore + softHalo + horizontalStreak + verticalGlint) * edgeMask);
                pixels[y * width + x] = new Color(val, val, val, val);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    /// <summary>
    /// T_WeaponHitWave.png (256x256 px): วงแหวนคลื่นอากาศขอบคม
    /// </summary>
    private static Texture2D GenerateWeaponHitWaveTexture(int width, int height)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[width * height];

        for (int y = 0; y < height; y++)
        {
            float ny = (y / (float)(height - 1)) * 2f - 1f;
            for (int x = 0; x < width; x++)
            {
                float nx = (x / (float)(width - 1)) * 2f - 1f;
                float r = Mathf.Sqrt(nx * nx + ny * ny);
                if (r >= 0.98f)
                {
                    pixels[y * width + x] = Color.clear;
                    continue;
                }

                float outerCut = 1f - SmoothStep(0.84f, 0.95f, r);
                float innerFade = SmoothStep(0.54f, 0.80f, r);
                float crispRim = Mathf.Exp(-Mathf.Pow((r - 0.82f) / 0.042f, 2f));
                float val = Mathf.Clamp01((innerFade * outerCut * 0.65f + crispRim * 0.98f) * outerCut);
                pixels[y * width + x] = new Color(val, val, val, val);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    // =========================================================================
    // ASSET SAVING, MATERIAL SETUP & HIERARCHY AUTO-BINDING
    // =========================================================================

    private static Texture2D SaveTexturePng(Texture2D sourceTex, string assetPath)
    {
        byte[] pngBytes = sourceTex.EncodeToPNG();
        Object.DestroyImmediate(sourceTex);

        File.WriteAllBytes(assetPath, pngBytes);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
    }

    private static Material CreateOrUpdateAdditiveMaterial(string assetPath, Texture2D mainTex, float hdrIntensity)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Particles/Standard Unlit");

        Material mat = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
        bool isNew = mat == null;
        if (isNew)
        {
            mat = new Material(shader);
        }
        else
        {
            mat.shader = shader;
        }

        Color hdrColor = new Color(hdrIntensity, hdrIntensity, hdrIntensity, 1.0f);
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", mainTex);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", mainTex);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", hdrColor);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", hdrColor);

        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 2f);
        if (mat.HasProperty("_ColorMode")) mat.SetFloat("_ColorMode", 0f);
        if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)BlendMode.One);
        if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)CullMode.Off);

        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.EnableKeyword("_BLENDMODE_ADD");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.DisableKeyword("_ALPHAMODULATE_ON");
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.renderQueue = (int)RenderQueue.Transparent;

        if (isNew)
        {
            AssetDatabase.CreateAsset(mat, assetPath);
        }
        else
        {
            EditorUtility.SetDirty(mat);
        }

        return AssetDatabase.LoadAssetAtPath<Material>(assetPath);
    }

    private static void BindPrefabToSelectedObject(GameObject prefab)
    {
        GameObject target = Selection.activeGameObject;

        if (target == null || EditorUtility.IsPersistent(target))
        {
            target = new GameObject("WeaponHitImpactVFX_Tester");
            Undo.RegisterCreatedObjectUndo(target, "Create WeaponHitImpactVFX Tester");
            Debug.Log("[VFXWeaponHitGenerator] ไม่ได้เลือกตัวละครใน Hierarchy จึงสร้าง 'WeaponHitImpactVFX_Tester' พร้อมผูก Prefab ให้เรียบร้อยแล้ว");
        }

        MMOWeaponHitImpactVFX comp = target.GetComponent<MMOWeaponHitImpactVFX>();
        if (comp == null)
        {
            comp = Undo.AddComponent<MMOWeaponHitImpactVFX>(target);
        }

        Undo.RecordObject(comp, "Assign Weapon Hit Impact Prefab");
        comp.hitImpactPrefab = prefab;
        EditorUtility.SetDirty(comp);
        Selection.activeGameObject = target;
    }

    private static void EnsureOutputFolder()
    {
        EnsureFolderHierarchy(OutputFolder);
    }

    private static void EnsureFolderHierarchy(string folderPath)
    {
        string[] parts = folderPath.Split('/');
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

    private static void EnsureTagExists(string tagName)
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (assets == null || assets.Length == 0) return;

        SerializedObject tagManager = new SerializedObject(assets[0]);
        SerializedProperty tagsProp = tagManager.FindProperty("tags");
        if (tagsProp == null || !tagsProp.isArray) return;

        for (int i = 0; i < tagsProp.arraySize; i++)
        {
            if (tagsProp.GetArrayElementAtIndex(i).stringValue == tagName)
            {
                return;
            }
        }

        int insertIndex = tagsProp.arraySize;
        tagsProp.InsertArrayElementAtIndex(insertIndex);
        tagsProp.GetArrayElementAtIndex(insertIndex).stringValue = tagName;
        tagManager.ApplyModifiedPropertiesWithoutUndo();
    }

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp01((x - edge0) / Mathf.Max(0.0001f, edge1 - edge0));
        return t * t * (3f - 2f * t);
    }
}
