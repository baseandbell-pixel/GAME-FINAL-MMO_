using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// เครื่องมือสร้างระบบเอฟเฟกต์มอนสเตอร์ตายพร้อมกองเลือดไหลนองบนพื้น 4 เลเยอร์ระดับ AAA อัตโนมัติ
/// - วาด Texture Procedural 2 ไฟล์ (T_OrganicBloodPool.png 512x512 ด้วย Perlin Noise 2 ความถี่ และ T_BloodSplatterDrops.png 128x128)
/// - สร้าง Material Alpha Blend (Sprites/Default สำรองด้วย URP Particles/Unlit) แสดงผลเป็นของเหลวสมจริงไม่เรืองแสง
/// - บันทึกเป็น Prefab ชื่อ VFX_MonsterDeathBloodPool.prefab ลงใน Assets/VFX_MonsterDeathBlood
/// - ลบต้นแบบออกจากฉากไม่ให้รก Hierarchy และผูกเข้ากับตัวละครที่เลือกอัตโนมัติ
/// </summary>
public static class Tool_10_Build_MonsterDeath_BloodPool_OnEnemyDeath
{
    private const string OutputFolder = "Assets/yeen VFX/03_VFX_Prefabs_And_Assets/Generated_DeathBlood";
    private const string TexBloodPoolPath = OutputFolder + "/T_OrganicBloodPool.png";
    private const string TexBloodDropsPath = OutputFolder + "/T_BloodSplatterDrops.png";

    private const string MatBloodPoolPath = OutputFolder + "/M_DeathBlood_Pool_Alpha.mat";
    private const string MatBloodSplatterPath = OutputFolder + "/M_DeathBlood_Splatter_Alpha.mat";
    private const string MatBloodMistPath = OutputFolder + "/M_DeathBlood_SoulMist_Alpha.mat";

    private const string PrefabPath = OutputFolder + "/VFX_MonsterDeathBloodPool.prefab";

    [MenuItem("yeen VFX/10. [เกิดตอนมอนตาย] สร้างเอฟเฟกต์เลือดนองพื้นตอนมอนตาย (Death Blood Pool)")]
    public static void GenerateMonsterDeathBloodVFX()
    {
        EnsureFolderHierarchy(OutputFolder);

        // 1. วาดและบันทึก Procedural Liquid Blood Textures 2 ไฟล์ด้วยสมการ Perlin Noise
        GenerateAndSaveOrganicBloodPoolTexture(TexBloodPoolPath, 512, 512);
        GenerateAndSaveBloodDropTexture(TexBloodDropsPath, 128, 128);

        AssetDatabase.Refresh();

        ConfigureTextureImporter(TexBloodPoolPath);
        ConfigureTextureImporter(TexBloodDropsPath);

        Texture2D texBloodPool = AssetDatabase.LoadAssetAtPath<Texture2D>(TexBloodPoolPath);
        Texture2D texBloodDrops = AssetDatabase.LoadAssetAtPath<Texture2D>(TexBloodDropsPath);

        // 2. สร้าง Material Alpha Blend สำหรับของเหลวเลือดสมจริงใน Unity 6 URP (ไม่เรืองแสงเป็นเลเซอร์)
        Material matBloodPool = CreateOrUpdateAlphaBlendMaterial(MatBloodPoolPath, texBloodPool, Color.white);
        Material matBloodSplatter = CreateOrUpdateAlphaBlendMaterial(MatBloodSplatterPath, texBloodDrops, Color.white);
        Material matBloodMist = CreateOrUpdateAlphaBlendMaterial(MatBloodMistPath, texBloodDrops, Color.white);

        // 3. ประกอบโครงสร้าง ParticleSystem 4 เลเยอร์แล้วบันทึกเป็น Prefab
        Mesh quadMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        if (quadMesh == null)
        {
            GameObject tempQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quadMesh = tempQuad.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(tempQuad);
        }

        GameObject tempRoot = new GameObject("VFX_MonsterDeathBloodPool");
        BuildLayer1DeathSplatterBurst(tempRoot.transform, matBloodSplatter);
        BuildLayer2ExpandingGroundPool(tempRoot.transform, quadMesh, matBloodPool);
        BuildLayer3PeripheralDroplets(tempRoot.transform, quadMesh, matBloodPool);
        BuildLayer4RisingSoulMist(tempRoot.transform, matBloodMist);

        // บันทึกเป็น Prefab ลง Assets/VFX_MonsterDeathBlood/VFX_MonsterDeathBloodPool.prefab
        GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(tempRoot, PrefabPath);

        // ลบตัวต้นแบบออกจากฉากไม่ให้รก Hierarchy
        Object.DestroyImmediate(tempRoot);

        // 4. ผูกเข้ากับตัวละครที่เลือกอยู่ใน Hierarchy อัตโนมัติ
        GameObject targetCharacter = Selection.activeGameObject;
        string bindMessage;
        if (targetCharacter != null && targetCharacter.scene.IsValid())
        {
            MMOMonsterDeathBloodVFX bloodComp = targetCharacter.GetComponent<MMOMonsterDeathBloodVFX>();
            if (bloodComp == null)
            {
                bloodComp = Undo.AddComponent<MMOMonsterDeathBloodVFX>(targetCharacter);
            }
            Undo.RecordObject(bloodComp, "Bind Monster Death Blood Pool VFX Prefab");
            bloodComp.deathBloodVfxPrefab = savedPrefab;
            bloodComp.bloodPoolScale = 1.0f;
            bloodComp.surfaceNormalOffset = 0.06f;
            bloodComp.enableTestHotkey = true;
            bloodComp.showTestHUD = true;
            EditorUtility.SetDirty(bloodComp);

            bindMessage = "ผูกสคริปต์ MMOMonsterDeathBloodVFX และ Prefab เข้ากับ '" + targetCharacter.name + "' เรียบร้อยแล้ว!";
        }
        else
        {
            bindMessage = "ยังไม่ได้เลือกตัวละครใน Hierarchy (สามารถลาก " + PrefabPath + " ไปใส่ช่อง Death Blood Vfx Prefab บนสคริปต์ MMOMonsterDeathBloodVFX ได้ทันที)";
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorGUIUtility.PingObject(savedPrefab);

        EditorUtility.DisplayDialog(
            "สร้างเอฟเฟกต์กองเลือดมอนสเตอร์ตายสำเร็จ 100%!",
            "สร้าง Procedural Textures 2 ไฟล์ + Alpha Blend Materials + Prefab 4 เลเยอร์เรียบร้อยแล้ว:\n" +
            PrefabPath + "\n\n" +
            bindMessage + "\n\n" +
            "วิธีทดสอบทันที:\n" +
            "1. กดปุ่ม Play ด้านบน\n" +
            "2. กดปุ่ม [K] เพื่อเสกกองเลือดไหลนองแนบพื้นด้านหน้าตัวละคร หรือฆ่ามอนสเตอร์ในเกมจริง!",
            "รับทราบ (OK)");
    }

    // =================================================================================================
    // PART A: วาด Procedural Liquid Blood Textures 2 ไฟล์ด้วยสมการ Perlin Noise 2 ความถี่
    // =================================================================================================

    /// <summary>
    /// T_OrganicBloodPool.png (512x512): แอ่งเลือดรูปทรงอิสระขอบหยักธรรมชาติด้วย Perlin Noise 2 ความถี่
    /// พร้อมเงาความเข้มข้นหนืดของเลือดบริเวณใจกลางแอ่งและขอบเปียกคมธรรมชาติ
    /// </summary>
    private static void GenerateAndSaveOrganicBloodPoolTexture(string assetPath, int width, int height)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[width * height];

        for (int y = 0; y < height; y++)
        {
            float ny = ((float)y / (height - 1)) * 2f - 1f;
            for (int x = 0; x < width; x++)
            {
                float nx = ((float)x / (width - 1)) * 2f - 1f;
                float dist = Mathf.Sqrt(nx * nx + ny * ny);
                float angle = Mathf.Atan2(ny, nx);

                // Perlin Noise ความถี่ต่ำ (กำหนดส่วนเว้าโค้งใหญ่ของแอ่งของเหลว)
                float lowX = Mathf.Cos(angle) * 1.35f + 4.2f;
                float lowY = Mathf.Sin(angle) * 1.35f + 2.7f;
                float noiseLow = Mathf.PerlinNoise(lowX, lowY);

                // Perlin Noise ความถี่สูง (กำหนดขอบหยักละเอียดและติ่งเลือดที่ไหลแผ่)
                float highX = Mathf.Cos(angle) * 3.85f + 11.5f;
                float highY = Mathf.Sin(angle) * 3.85f + 8.3f;
                float noiseHigh = Mathf.PerlinNoise(highX, highY);

                // รัศมีธรรมชาติไม่เป็นวงกลมแข็งทื่อ
                float organicRadius = 0.56f + (noiseLow - 0.5f) * 0.34f + (noiseHigh - 0.5f) * 0.14f;

                // ขอบของเหลวเปียกชัดแต่ไม่เป็นรอยหยักพิกเซล
                float alpha = 1.0f - SmoothStep(organicRadius - 0.042f, organicRadius + 0.022f, dist);

                // ความเข้มข้นและเงาความลึกตรงกลางแอ่งเลือด + ลายความหนืดภายใน
                float internalFlow = Mathf.PerlinNoise(nx * 3.4f + 7.1f, ny * 3.4f + 5.4f);
                float normalizedDepth = Mathf.Clamp01(1.0f - (dist / Mathf.Max(0.1f, organicRadius)));

                // ขอบนอกจะบางและใสกว่าเล็กน้อย ส่วนใจกลางแอ่งจะเข้มข้นหนืดกว่า
                float viscosityShade = Mathf.Lerp(0.96f, 0.48f, Mathf.Pow(normalizedDepth, 0.85f) * 0.72f + internalFlow * 0.28f);
                float alphaDepth = alpha * Mathf.Lerp(0.84f, 1.0f, normalizedDepth);

                Color col = new Color(viscosityShade, viscosityShade * 0.86f, viscosityShade * 0.86f, alphaDepth);
                pixels[y * width + x] = col;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        SaveTextureAsPNG(tex, assetPath);
        Object.DestroyImmediate(tex);
    }

    /// <summary>
    /// T_BloodSplatterDrops.png (128x128): หยดเลือดทรงกลมนุ่มนวลสำหรับสะเก็ดเลือดและไอหมอกวิญญาณสีแดง
    /// </summary>
    private static void GenerateAndSaveBloodDropTexture(string assetPath, int width, int height)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[width * height];

        for (int y = 0; y < height; y++)
        {
            float ny = ((float)y / (height - 1)) * 2f - 1f;
            for (int x = 0; x < width; x++)
            {
                float nx = ((float)x / (width - 1)) * 2f - 1f;
                float dist = Mathf.Sqrt(nx * nx + ny * ny);

                float alpha = Mathf.Pow(1.0f - SmoothStep(0.14f, 0.96f, dist), 1.35f);
                float shade = Mathf.Lerp(0.72f, 1.0f, SmoothStep(0.0f, 0.72f, dist));

                pixels[y * width + x] = new Color(shade, shade * 0.90f, shade * 0.90f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        SaveTextureAsPNG(tex, assetPath);
        Object.DestroyImmediate(tex);
    }

    // =================================================================================================
    // PART B: สร้าง Material แบบ Alpha Blend (Sprites/Default สำรองด้วย URP Particles/Unlit)
    // =================================================================================================
    private static Material CreateOrUpdateAlphaBlendMaterial(string assetPath, Texture2D mainTex, Color tintColor)
    {
        // กฎเหล็กข้อที่ 4: ใช้ Sprites/Default เป็นหลัก (สำรองด้วย URP Particles/Unlit) โหมด Alpha Blend
        // เพื่อแสดงผลเป็นของเหลวสีแดงเข้มสมจริงบนพื้นใน Unity 6 URP ไม่เรืองแสงเหมือนเลเซอร์
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        }
        if (shader == null)
        {
            shader = Shader.Find("Particles/Standard Unlit");
        }

        Material mat = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, assetPath);
        }
        else
        {
            mat.shader = shader;
        }

        mat.mainTexture = mainTex;
        mat.color = tintColor;

        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", mainTex);
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", mainTex);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", tintColor);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tintColor);

        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f); // Transparent
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);     // 0 = Alpha Blend
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);       // Cull Off (2 ด้าน)
        if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
        if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);

        mat.SetOverrideTag("RenderType", "Transparent");
        mat.DisableKeyword("_BLENDMODE_ADD");
        mat.renderQueue = (int)RenderQueue.Transparent;

        EditorUtility.SetDirty(mat);
        return mat;
    }

    // =================================================================================================
    // PART C: สร้างโครงสร้าง ParticleSystem ทั้ง 4 เลเยอร์ตามกฎเหล็กข้อที่ 1
    // =================================================================================================

    // Layer 1 (Anticipation / Death Crimson Splatter Burst - 0.00s - 0.65s)
    private static void BuildLayer1DeathSplatterBurst(Transform parent, Material mat)
    {
        GameObject obj = new GameObject("L1_Death_CrimsonSplatterBurst");
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = new Vector3(0f, 0.95f, 0f); // พุ่งออกจากกลางลำตัวมอนสเตอร์

        ParticleSystem ps = obj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.duration = 0.70f;
        main.loop = false;
        main.playOnAwake = true;
        main.startDelay = 0.0f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.65f); // 0.00s - 0.65s
        main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 6.0f);      // กฎข้อ 1: ความเร็ว 2.5 - 6.0
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.19f);
        main.gravityModifier = 1.8f;                                       // กฎข้อ 1: Gravity Modifier = 1.8
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.78f, 0.03f, 0.05f, 0.96f),
            new Color(0.95f, 0.07f, 0.09f, 0.98f)); // สีแดงสด Fresh Crimson
        main.simulationSpace = ParticleSystemSimulationSpace.World;        // กฎข้อ 1: World Space
        main.maxParticles = 48;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new ParticleSystem.Burst[]
        {
            new ParticleSystem.Burst(0.0f, 28) // กฎข้อ 1: Burst 28 เม็ด
        });

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.32f;

        ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.separateAxes = false;
        AnimationCurve dropSize = new AnimationCurve(
            new Keyframe(0.0f, 1.0f),
            new Keyframe(0.75f, 0.85f),
            new Keyframe(1.0f, 0.25f));
        sol.size = new ParticleSystem.MinMaxCurve(1f, dropSize);

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(0.92f, 0.06f, 0.08f), 0.0f),
                new GradientColorKey(new Color(0.42f, 0.01f, 0.02f), 1.0f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0.98f, 0.0f),
                new GradientAlphaKey(0.88f, 0.65f),
                new GradientAlphaKey(0.0f, 1.0f)
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch; // กฎข้อ 1: Stretched Billboard
        renderer.velocityScale = 0.08f;
        renderer.lengthScale = 2.4f;
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    // Layer 2 (Main Action / Expanding Ground Blood Pool - 0.08s - 4.80s)
    private static void BuildLayer2ExpandingGroundPool(Transform parent, Mesh quadMesh, Material mat)
    {
        GameObject obj = new GameObject("L2_Expanding_GroundBloodPool");
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = Vector3.zero;
        obj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // กฎข้อ 1: นอนราบขนานพื้น (Rotation X = 90)

        ParticleSystem ps = obj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.duration = 4.90f;
        main.loop = false;
        main.playOnAwake = true;
        main.startDelay = 0.08f;    // เริ่มที่ 0.08s
        main.startLifetime = 4.72f; // จบที่ 4.80s (0.08 + 4.72)
        main.startSpeed = 0f;
        main.startSize = 2.2f;      // กฎข้อ 1: Start Size = 2.2 เมตร
        main.startColor = new ParticleSystem.MinMaxGradient(Color.white);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 2;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0.0f, 1) });

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;

        // กฎข้อ 1: ขยายวงกว้างออกอย่างนุ่มนวลใน 1.2 วินาทีแรก (1.2 / 4.72 = 0.254) จาก 0.15 -> 1.15 เท่า
        ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.separateAxes = false;
        AnimationCurve poolSpreadCurve = new AnimationCurve(
            new Keyframe(0.00f, 0.15f, 5.2f, 5.2f),
            new Keyframe(0.254f, 1.15f, 0.25f, 0.25f),
            new Keyframe(1.00f, 1.18f, 0.0f, 0.0f));
        sol.size = new ParticleSystem.MinMaxCurve(1f, poolSpreadCurve);

        // กฎข้อ 1: เปลี่ยนจากสีเลือดสด (Fresh Crimson) -> สีเลือดเข้ม (Dark Dried Blood)
        // แช่ค้าง Alpha = 0.92 แล้วค่อยๆ จางหาย 0.92 -> 0.0 ในช่วง 60% - 100% ของอายุ
        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(0.76f, 0.04f, 0.06f), 0.00f), // Fresh Crimson
                new GradientColorKey(new Color(0.46f, 0.02f, 0.03f), 0.30f), // Deepening Blood
                new GradientColorKey(new Color(0.28f, 0.01f, 0.02f), 1.00f)  // Dark Dried Blood
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0.00f, 0.00f),
                new GradientAlphaKey(0.92f, 0.06f),
                new GradientAlphaKey(0.92f, 0.60f),
                new GradientAlphaKey(0.00f, 1.00f)
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;  // กฎข้อ 1: Mesh (Quad)
        renderer.mesh = quadMesh;
        renderer.alignment = ParticleSystemRenderSpace.Local; // กฎข้อ 1: Alignment = Local
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    // Layer 3 (Secondary Elements / Peripheral Ground Droplets - 0.12s - 4.60s)
    private static void BuildLayer3PeripheralDroplets(Transform parent, Mesh quadMesh, Material mat)
    {
        GameObject obj = new GameObject("L3_Peripheral_GroundDroplets");
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = new Vector3(0f, 0.004f, 0f); // ยกสูงกว่า Layer 2 เล็กน้อยกันกระพริบ
        obj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        ParticleSystem ps = obj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.duration = 4.70f;
        main.loop = false;
        main.playOnAwake = true;
        main.startDelay = 0.12f;    // เริ่มที่ 0.12s
        main.startLifetime = 4.48f; // จบที่ 4.60s (0.12 + 4.48)
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.26f, 0.56f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f); // กฎข้อ 1: สุ่มมุมหมุน 360 องศา
        main.startColor = new ParticleSystem.MinMaxGradient(Color.white);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 16;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new ParticleSystem.Burst[]
        {
            new ParticleSystem.Burst(0.0f, 8) // กฎข้อ 1: Burst 8 เม็ด
        });

        // กฎข้อ 1: Circle Shape Radius = 0.85 กระจายหยดเลือดรอบแอ่งหลักบนพื้น
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.85f;
        shape.radiusThickness = 0.65f;

        ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.separateAxes = false;
        AnimationCurve dropSpread = new AnimationCurve(
            new Keyframe(0.00f, 0.25f, 4.0f, 4.0f),
            new Keyframe(0.18f, 1.00f, 0.1f, 0.1f),
            new Keyframe(1.00f, 1.02f, 0.0f, 0.0f));
        sol.size = new ParticleSystem.MinMaxCurve(1f, dropSpread);

        // จางหายพร้อมกับแอ่งเลือดหลักในช่วง 60% - 100%
        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(0.72f, 0.03f, 0.05f), 0.00f),
                new GradientColorKey(new Color(0.30f, 0.01f, 0.02f), 0.45f),
                new GradientColorKey(new Color(0.24f, 0.01f, 0.015f), 1.00f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0.00f, 0.00f),
                new GradientAlphaKey(0.88f, 0.08f),
                new GradientAlphaKey(0.88f, 0.60f),
                new GradientAlphaKey(0.00f, 1.00f)
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh; // กฎข้อ 1: Mesh (Quad)
        renderer.mesh = quadMesh;
        renderer.alignment = ParticleSystemRenderSpace.Local;
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    // Layer 4 (Impact & Residual / Rising Crimson Soul Mist - 0.10s - 1.50s)
    private static void BuildLayer4RisingSoulMist(Transform parent, Material mat)
    {
        GameObject obj = new GameObject("L4_Rising_CrimsonSoulMist");
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = new Vector3(0f, 0.12f, 0f);
        obj.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

        ParticleSystem ps = obj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.duration = 1.60f;
        main.loop = false;
        main.playOnAwake = true;
        main.startDelay = 0.10f; // เริ่มที่ 0.10s
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.00f, 1.40f); // จบราว 1.50s
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.70f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.55f, 1.15f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.70f, 0.06f, 0.09f, 0.34f),
            new Color(0.88f, 0.14f, 0.16f, 0.42f));
        main.simulationSpace = ParticleSystemSimulationSpace.World; // กฎข้อ 1: World Space
        main.maxParticles = 24;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new ParticleSystem.Burst[]
        {
            new ParticleSystem.Burst(0.0f, 12) // กฎข้อ 1: Burst 12 เม็ด
        });

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.62f;

        // ปลอดภัย 100% ใน Unity 6: กำหนด Constant Mode เสมอกันทุกแกนใน Velocity over Lifetime
        ParticleSystem.VelocityOverLifetimeModule vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(0f);
        vel.y = new ParticleSystem.MinMaxCurve(0.55f);
        vel.z = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalX = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalY = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalZ = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalOffsetX = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalOffsetY = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalOffsetZ = new ParticleSystem.MinMaxCurve(0f);
        vel.radial = new ParticleSystem.MinMaxCurve(0.12f);
        vel.speedModifier = new ParticleSystem.MinMaxCurve(1f);

        ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.separateAxes = false;
        AnimationCurve mistCurve = new AnimationCurve(
            new Keyframe(0.0f, 0.55f),
            new Keyframe(1.0f, 1.55f));
        sol.size = new ParticleSystem.MinMaxCurve(1f, mistCurve);

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(0.85f, 0.10f, 0.12f), 0.0f),
                new GradientColorKey(new Color(0.45f, 0.03f, 0.05f), 1.0f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0.00f, 0.00f),
                new GradientAlphaKey(0.42f, 0.22f),
                new GradientAlphaKey(0.00f, 1.00f)
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard; // กฎข้อ 1: Billboard
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    // =================================================================================================
    // UTILITY HELPERS
    // =================================================================================================
    private static void SaveTextureAsPNG(Texture2D tex, string assetPath)
    {
        byte[] pngData = tex.EncodeToPNG();
        string fullPath = Path.GetFullPath(assetPath);
        string dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllBytes(fullPath, pngData);
    }

    private static void ConfigureTextureImporter(string assetPath)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null) return;

        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
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

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp01((x - edge0) / Mathf.Max(0.00001f, edge1 - edge0));
        return t * t * (3f - 2f * t);
    }
}
