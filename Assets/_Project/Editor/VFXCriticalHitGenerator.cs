using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// เครื่องมือสร้างระบบเอฟเฟกต์คริติคอลฮิตสีแดงขนาดใหญ่ 4 เลเยอร์ระดับ AAA อัตโนมัติ (Zero-External-Asset)
/// - วาด Texture Procedural 3 ไฟล์ (T_CrimsonCrossSlash.png 512x512, T_CrimsonCoreFlash.png 256x256, T_CrimsonShockwave.png 256x256)
/// - สร้าง Material URP Additive HDR รองรับ Post-Processing Bloom 100%
/// - บันทึกเป็น Prefab ชื่อ VFX_CrimsonCriticalHit.prefab ในโฟลเดอร์ Assets/VFX_CrimsonCritical
/// - ลบตัวต้นแบบออกจากฉากไม่ให้รก Hierarchy และผูกเข้ากับตัวละครที่เลือกอัตโนมัติ
/// </summary>
public static class VFXCriticalHitGenerator
{
    private const string OutputFolder = "Assets/VFX_CrimsonCritical";
    private const string TexCrossSlashPath = OutputFolder + "/T_CrimsonCrossSlash.png";
    private const string TexCoreFlashPath = OutputFolder + "/T_CrimsonCoreFlash.png";
    private const string TexShockwavePath = OutputFolder + "/T_CrimsonShockwave.png";

    private const string MatCoreFlashPath = OutputFolder + "/M_Crit_CoreFlash_Add.mat";
    private const string MatCrossSlashPath = OutputFolder + "/M_Crit_CrossSlash_Add.mat";
    private const string MatSparksPath = OutputFolder + "/M_Crit_Sparks_Add.mat";
    private const string MatShockwavePath = OutputFolder + "/M_Crit_Shockwave_Add.mat";

    private const string PrefabPath = OutputFolder + "/VFX_CrimsonCriticalHit.prefab";

    [MenuItem("VFX Mentor/10. สร้างเอฟเฟกต์คริติคอลสีแดงขนาดใหญ่ 4 เลเยอร์ (Crimson Critical VFX)")]
    public static void GenerateCrimsonCriticalVFX()
    {
        EnsureFolderHierarchy(OutputFolder);

        // 1. วาดและบันทึก Procedural Textures 3 ไฟล์ด้วยสมการคณิตศาสตร์
        GenerateAndSaveCrossSlashTexture(TexCrossSlashPath, 512, 512);
        GenerateAndSaveCoreFlashTexture(TexCoreFlashPath, 256, 256);
        GenerateAndSaveShockwaveTexture(TexShockwavePath, 256, 256);

        AssetDatabase.Refresh();

        ConfigureTextureImporter(TexCrossSlashPath);
        ConfigureTextureImporter(TexCoreFlashPath);
        ConfigureTextureImporter(TexShockwavePath);

        Texture2D texCrossSlash = AssetDatabase.LoadAssetAtPath<Texture2D>(TexCrossSlashPath);
        Texture2D texCoreFlash = AssetDatabase.LoadAssetAtPath<Texture2D>(TexCoreFlashPath);
        Texture2D texShockwave = AssetDatabase.LoadAssetAtPath<Texture2D>(TexShockwavePath);

        // 2. สร้าง URP Additive HDR Materials 4 ไฟล์ (Surface=Transparent, Blend=Additive, Cull=Off)
        Material matCoreFlash = CreateOrUpdateURPAdditiveMaterial(
            MatCoreFlashPath, texCoreFlash, new Color(1.5f, 0.35f, 0.28f, 1f));

        Material matCrossSlash = CreateOrUpdateURPAdditiveMaterial(
            MatCrossSlashPath, texCrossSlash, new Color(1.6f, 0.25f, 0.20f, 1f));

        Material matSparks = CreateOrUpdateURPAdditiveMaterial(
            MatSparksPath, texCoreFlash, new Color(1.8f, 0.45f, 0.16f, 1f));

        Material matShockwave = CreateOrUpdateURPAdditiveMaterial(
            MatShockwavePath, texShockwave, new Color(1.4f, 0.12f, 0.15f, 1f));

        // 3. ประกอบโครงสร้าง ParticleSystem 4 เลเยอร์ในฉากชั่วคราว แล้วบันทึกเป็น Prefab
        Mesh quadMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        if (quadMesh == null)
        {
            GameObject tempQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quadMesh = tempQuad.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(tempQuad);
        }

        GameObject tempRoot = new GameObject("VFX_CrimsonCriticalHit");
        BuildLayer1CoreFlash(tempRoot.transform, matCoreFlash);
        BuildLayer2GiantCrossSlash(tempRoot.transform, quadMesh, matCrossSlash);
        BuildLayer3HighVelocitySparks(tempRoot.transform, matSparks);
        BuildLayer4CrimsonShockwave(tempRoot.transform, quadMesh, matShockwave);

        // บันทึกเป็น Prefab ลง Assets/VFX_CrimsonCritical/VFX_CrimsonCriticalHit.prefab
        GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(tempRoot, PrefabPath);

        // กฎเหล็กข้อที่ 4: ลบตัวต้นแบบออกจากฉากไม่ให้รก Hierarchy
        Object.DestroyImmediate(tempRoot);

        // 4. ผูกเข้ากับตัวละครที่เลือกอยู่ใน Hierarchy อัตโนมัติ
        GameObject targetCharacter = Selection.activeGameObject;
        string bindMessage;
        if (targetCharacter != null && targetCharacter.scene.IsValid())
        {
            MMOCriticalHitVFX critComp = targetCharacter.GetComponent<MMOCriticalHitVFX>();
            if (critComp == null)
            {
                critComp = Undo.AddComponent<MMOCriticalHitVFX>(targetCharacter);
            }
            Undo.RecordObject(critComp, "Bind Crimson Critical VFX Prefab");
            critComp.criticalVfxPrefab = savedPrefab;
            critComp.criticalScale = 1.0f;
            critComp.enableTestHotkeys = true;
            critComp.showTestHUD = true;
            EditorUtility.SetDirty(critComp);

            bindMessage = "ผูกสคริปต์ MMOCriticalHitVFX และ Prefab เข้ากับ '" + targetCharacter.name + "' เรียบร้อยแล้ว!";
        }
        else
        {
            bindMessage = "ยังไม่ได้เลือกตัวละครใน Hierarchy (สามารถลาก " + PrefabPath + " ไปใส่ช่อง Critical Vfx Prefab บนสคริปต์ MMOCriticalHitVFX ของตัวละครได้ทันที)";
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorGUIUtility.PingObject(savedPrefab);

        EditorUtility.DisplayDialog(
            "สร้างเอฟเฟกต์คริติคอลสีแดงขนาดใหญ่สำเร็จ 100%!",
            "สร้าง Texture 3 ไฟล์ + URP Additive HDR Materials + Prefab 4 เลเยอร์เรียบร้อยแล้ว:\n" +
            PrefabPath + "\n\n" +
            bindMessage + "\n\n" +
            "วิธีทดสอบทันที:\n" +
            "1. กดปุ่ม Play ด้านบน\n" +
            "2. กดปุ่ม [T] หรือ คลิกกลางเมาส์ เพื่อเสกคริติคอลสีแดงขนาดใหญ่ด้านหน้าตัวละคร!",
            "รับทราบ (OK)");
    }

    // =================================================================================================
    // PART A: วาด Procedural Textures 3 ไฟล์ด้วยสมการคณิตศาสตร์
    // =================================================================================================

    /// <summary>
    /// T_CrimsonCrossSlash.png (512x512): รอยฟันกากบาท X สองเส้นทแยงมุมปลายเรียวแหลมคมกริบทั้ง 4 ด้าน
    /// ผสานเส้นตัดแนวนอนบางๆ ตรงกลาง และจุดระเบิดกลางกากบาท
    /// </summary>
    private static void GenerateAndSaveCrossSlashTexture(string assetPath, int width, int height)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[width * height];

        for (int y = 0; y < height; y++)
        {
            float ny = ((float)y / (height - 1)) * 2f - 1f;
            for (int x = 0; x < width; x++)
            {
                float nx = ((float)x / (width - 1)) * 2f - 1f;

                // เส้นทแยงมุมที่ 1 (เฉียงขึ้นขวา 45 องศา) ปลายเรียวแหลมคมทั้ง 2 ด้าน
                float u1 = (nx + ny) * 0.70710678f;
                float v1 = (nx - ny) * 0.70710678f;
                float slashA = EvalTaperedBlade(u1, v1, 1.18f, 0.074f);

                // เส้นทแยงมุมที่ 2 (เฉียงลงขวา -45 องศา) ปลายเรียวแหลมคมทั้ง 2 ด้าน
                float u2 = (nx - ny) * 0.70710678f;
                float v2 = (nx + ny) * 0.70710678f;
                float slashB = EvalTaperedBlade(u2, v2, 1.18f, 0.068f);

                // เส้นตัดแนวนอนบางๆ คมกริบพาดผ่านตรงกลางเสริมความดุดันแบบ Action MMORPG
                float horizCut = EvalTaperedBlade(nx, ny, 0.96f, 0.023f) * 0.88f;

                // แสงวาบจุดตัดกลางกากบาท
                float distSq = nx * nx + ny * ny;
                float centerStar = Mathf.Exp(-distSq * 24f) * 0.75f;

                float edgeMask = 1f - SmoothStep(0.86f, 0.99f, Mathf.Max(Mathf.Abs(nx), Mathf.Abs(ny)));
                float value = Mathf.Clamp01((Mathf.Max(Mathf.Max(slashA, slashB), horizCut) + centerStar * 0.55f) * edgeMask);

                // แกนคมดาบขาวอมชมพูร้อนแรง ขอบนอกเป็นสีแดงชาดสด
                Color col = Color.Lerp(
                    new Color(1.0f, 0.06f, 0.09f, value),
                    new Color(1.0f, 0.96f, 0.94f, value),
                    Mathf.Pow(value, 2.2f));
                col.a = value;

                pixels[y * width + x] = col;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        SaveTextureAsPNG(tex, assetPath);
        Object.DestroyImmediate(tex);
    }

    /// <summary>
    /// T_CrimsonCoreFlash.png (256x256): แกนแสงแฟลชทรงกลมฟุ้งนุ่มนวลแกนขาวอมแดงสว่างจ้า
    /// </summary>
    private static void GenerateAndSaveCoreFlashTexture(string assetPath, int width, int height)
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
                float edgeMask = 1f - SmoothStep(0.76f, 0.98f, dist);

                float hotCore = Mathf.Exp(-dist * dist * 15f);
                float softBloom = Mathf.Exp(-dist * dist * 3.6f) * 0.65f;
                float value = Mathf.Clamp01((hotCore + softBloom) * edgeMask);

                Color col = Color.Lerp(
                    new Color(1.0f, 0.10f, 0.12f, value),
                    new Color(1.0f, 0.96f, 0.94f, value),
                    Mathf.Pow(value, 1.45f));
                col.a = value;

                pixels[y * width + x] = col;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        SaveTextureAsPNG(tex, assetPath);
        Object.DestroyImmediate(tex);
    }

    /// <summary>
    /// T_CrimsonShockwave.png (256x256): วงแหวนคลื่นกระแทกขอบคมสีแดงเลือดหมู
    /// </summary>
    private static void GenerateAndSaveShockwaveTexture(string assetPath, int width, int height)
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
                float outerCutoff = 1f - SmoothStep(0.88f, 0.99f, dist);

                // ขอบนอกคมชัดพร้อมหางคลื่นกระแทกด้านใน
                float razorRing = Mathf.Exp(-Mathf.Pow((dist - 0.76f) / 0.046f, 2f));
                float innerTrail = Mathf.Exp(-Mathf.Pow((dist - 0.64f) / 0.11f, 2f)) * 0.44f * SmoothStep(0.36f, 0.68f, dist);
                float angularStress = 0.88f + 0.12f * Mathf.Sin(angle * 12f) * Mathf.Cos(angle * 5f);

                float value = Mathf.Clamp01((razorRing + innerTrail) * angularStress * outerCutoff);

                Color col = Color.Lerp(
                    new Color(0.85f, 0.03f, 0.07f, value),
                    new Color(1.0f, 0.72f, 0.66f, value),
                    Mathf.Pow(razorRing, 1.8f));
                col.a = value;

                pixels[y * width + x] = col;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        SaveTextureAsPNG(tex, assetPath);
        Object.DestroyImmediate(tex);
    }

    // =================================================================================================
    // PART B: สร้าง URP Additive HDR Material
    // =================================================================================================
    private static Material CreateOrUpdateURPAdditiveMaterial(string assetPath, Texture2D mainTex, Color baseColor)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
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

        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", mainTex);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", mainTex);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseColor);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", baseColor);

        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);     // Transparent
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 2f);         // Additive
        if (mat.HasProperty("_ColorMode")) mat.SetFloat("_ColorMode", 0f); // Multiply
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);           // Off (2 ด้าน)
        if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
        if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)BlendMode.One);
        if (mat.HasProperty("_SrcBlendAlpha")) mat.SetInt("_SrcBlendAlpha", (int)BlendMode.One);
        if (mat.HasProperty("_DstBlendAlpha")) mat.SetInt("_DstBlendAlpha", (int)BlendMode.One);
        if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 0f);

        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.EnableKeyword("_BLENDMODE_ADD");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.DisableKeyword("_ALPHAMODULATE_ON");
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.renderQueue = (int)RenderQueue.Transparent + 50;

        EditorUtility.SetDirty(mat);
        return mat;
    }

    // =================================================================================================
    // PART C: สร้างโครงสร้าง ParticleSystem ทั้ง 4 เลเยอร์ตามกฎเหล็กข้อที่ 1
    // =================================================================================================

    // Layer 1 (Anticipation / Crimson Impact Core Flash - 0.00s - 0.16s)
    private static void BuildLayer1CoreFlash(Transform parent, Material mat)
    {
        GameObject obj = new GameObject("L1_Crimson_CoreFlash");
        obj.transform.SetParent(parent, false);

        ParticleSystem ps = obj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.duration = 0.20f;
        main.loop = false;
        main.playOnAwake = true;
        main.startDelay = 0.0f;
        main.startLifetime = 0.16f; // 0.00s - 0.16s
        main.startSpeed = 0f;
        main.startSize = 2.6f;      // กฎข้อ 1: Start Size = 2.6
        // กฎข้อ 1: แสงแฟลชสีแดงชาดแกนกลางขาวอมแดง HDR intensity 5.2x
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(5.2f, 0.85f, 0.65f, 1f));
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 2;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0.0f, 1) });

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;

        // กราฟ Explosive Ease-Out ระเบิดวาบอย่างฉับพลัน
        ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.separateAxes = false;
        AnimationCurve explosiveCurve = new AnimationCurve(
            new Keyframe(0.0f, 0.12f, 9.5f, 9.5f),
            new Keyframe(0.22f, 1.12f, 0.4f, 0.4f),
            new Keyframe(0.55f, 0.82f, -1.2f, -1.2f),
            new Keyframe(1.0f, 0.0f, -2.2f, -2.2f));
        sol.size = new ParticleSystem.MinMaxCurve(1f, explosiveCurve);

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(1.0f, 0.94f, 0.90f), 0.0f),
                new GradientColorKey(new Color(1.0f, 0.22f, 0.18f), 0.45f),
                new GradientColorKey(new Color(0.65f, 0.02f, 0.05f), 1.0f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0.0f, 0.0f),
                new GradientAlphaKey(1.0f, 0.08f),
                new GradientAlphaKey(0.85f, 0.50f),
                new GradientAlphaKey(0.0f, 1.0f)
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard; // กฎข้อ 1: Billboard
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    // Layer 2 (Main Action / Giant Crimson X-Cross Slash - 0.02s - 0.28s)
    private static void BuildLayer2GiantCrossSlash(Transform parent, Mesh quadMesh, Material mat)
    {
        GameObject obj = new GameObject("L2_Giant_CrimsonXCrossSlash");
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = new Vector3(0f, 0f, 0.02f);

        ParticleSystem ps = obj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.duration = 0.30f;
        main.loop = false;
        main.playOnAwake = true;
        main.startDelay = 0.02f;    // เริ่มที่ 0.02s
        main.startLifetime = 0.26f; // จบที่ 0.28s (0.02 + 0.26)
        main.startSpeed = 0f;
        main.startSize = 3.8f;      // กฎข้อ 1: Start Size = 3.8 เมตร
        // กฎข้อ 1: สีแดงสดเรืองแสง HDR intensity 4.5x
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(4.5f, 0.36f, 0.24f, 1f));
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 2;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0.0f, 1) });

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;

        // ขยายตัวฉีกผ่านใน 0.06 วินาทีแรก (0.06 / 0.26 = 0.23 ของ lifetime) แล้วค้างคมก่อนสลายหายไป
        ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.separateAxes = false;
        AnimationCurve slashCurve = new AnimationCurve(
            new Keyframe(0.0f, 0.16f, 12.0f, 12.0f),
            new Keyframe(0.23f, 1.04f, 0.45f, 0.45f),
            new Keyframe(0.74f, 1.08f, 0.0f, 0.0f),
            new Keyframe(1.0f, 0.90f, -0.9f, -0.9f));
        sol.size = new ParticleSystem.MinMaxCurve(1f, slashCurve);

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(1.0f, 0.94f, 0.90f), 0.0f),
                new GradientColorKey(new Color(1.0f, 0.18f, 0.14f), 0.32f),
                new GradientColorKey(new Color(0.68f, 0.02f, 0.04f), 1.0f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0.0f, 0.0f),
                new GradientAlphaKey(1.0f, 0.08f),
                new GradientAlphaKey(0.95f, 0.54f),
                new GradientAlphaKey(0.0f, 1.0f)
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;       // กฎข้อ 1: Mesh (Quad)
        renderer.mesh = quadMesh;
        renderer.alignment = ParticleSystemRenderSpace.Local;      // กฎข้อ 1: Alignment = Local
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    // Layer 3 (Secondary Elements / High-Velocity Crimson Sparks - 0.05s - 0.42s)
    private static void BuildLayer3HighVelocitySparks(Transform parent, Material mat)
    {
        GameObject obj = new GameObject("L3_HighVelocity_CrimsonSparks");
        obj.transform.SetParent(parent, false);

        ParticleSystem ps = obj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.duration = 0.45f;
        main.loop = false;
        main.playOnAwake = true;
        main.startDelay = 0.05f; // เริ่มที่ 0.05s
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.37f); // จบราว 0.42s
        main.startSpeed = new ParticleSystem.MinMaxCurve(9.0f, 18.0f);     // กฎข้อ 1: ความเร็ว 9 - 18
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(4.8f, 0.55f, 0.18f, 1f),
            new Color(5.2f, 1.30f, 0.45f, 1f)); // สะเก็ดพลังสีแดงเพลิง HDR
        main.simulationSpace = ParticleSystemSimulationSpace.World; // กฎข้อ 1: World Space
        main.maxParticles = 64;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new ParticleSystem.Burst[]
        {
            new ParticleSystem.Burst(0.0f, 32) // กฎข้อ 1: Burst 32 เส้น
        });

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.18f;

        // หน่วงความเร็วปลายทางให้สะเก็ดพุ่งแรงตอนระเบิดแล้วค่อยๆ ดับลงอย่างมีน้ำหนัก
        ParticleSystem.LimitVelocityOverLifetimeModule lv = ps.limitVelocityOverLifetime;
        lv.enabled = true;
        lv.limit = 4.5f;
        lv.dampen = 0.22f;

        ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.separateAxes = false;
        AnimationCurve sparkCurve = new AnimationCurve(
            new Keyframe(0.0f, 1.0f),
            new Keyframe(0.55f, 0.85f),
            new Keyframe(1.0f, 0.0f));
        sol.size = new ParticleSystem.MinMaxCurve(1f, sparkCurve);

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(1.0f, 0.90f, 0.72f), 0.0f),
                new GradientColorKey(new Color(1.0f, 0.26f, 0.08f), 0.45f),
                new GradientColorKey(new Color(0.65f, 0.02f, 0.02f), 1.0f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(1.0f, 0.0f),
                new GradientAlphaKey(0.90f, 0.60f),
                new GradientAlphaKey(0.0f, 1.0f)
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch; // กฎข้อ 1: Stretched Billboard
        renderer.velocityScale = 0.06f;                         // กฎข้อ 1: Velocity Scale = 0.06
        renderer.lengthScale = 4.5f;                            // กฎข้อ 1: Length Scale = 4.5
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    // Layer 4 (Impact & Residual / Deep Crimson Shockwave Ring - 0.10s - 0.34s)
    private static void BuildLayer4CrimsonShockwave(Transform parent, Mesh quadMesh, Material mat)
    {
        GameObject obj = new GameObject("L4_Crimson_ShockwaveRing");
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = new Vector3(0f, 0f, -0.02f);

        ParticleSystem ps = obj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.duration = 0.36f;
        main.loop = false;
        main.playOnAwake = true;
        main.startDelay = 0.10f;    // เริ่มที่ 0.10s
        main.startLifetime = 0.24f; // จบที่ 0.34s (0.10 + 0.24)
        main.startSpeed = 0f;
        main.startSize = 3.4f;      // กฎข้อ 1: Start Size = 3.4
        // กฎข้อ 1: สีแดงเลือดหมู HDR intensity 3.0x
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(3.0f, 0.14f, 0.16f, 1f));
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 2;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0.0f, 1) });

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;

        // ขยายวงแหวนผลักอากาศออกรอบจุดปะทะ
        ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.separateAxes = false;
        AnimationCurve ringCurve = new AnimationCurve(
            new Keyframe(0.0f, 0.18f, 6.2f, 6.2f),
            new Keyframe(0.45f, 0.88f, 0.9f, 0.9f),
            new Keyframe(1.0f, 1.12f, 0.0f, 0.0f));
        sol.size = new ParticleSystem.MinMaxCurve(1f, ringCurve);

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(1.0f, 0.45f, 0.36f), 0.0f),
                new GradientColorKey(new Color(0.85f, 0.05f, 0.09f), 0.55f),
                new GradientColorKey(new Color(0.42f, 0.01f, 0.03f), 1.0f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0.0f, 0.0f),
                new GradientAlphaKey(1.0f, 0.14f),
                new GradientAlphaKey(0.65f, 0.62f),
                new GradientAlphaKey(0.0f, 1.0f)
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

    // =================================================================================================
    // UTILITY HELPERS
    // =================================================================================================
    private static float EvalTaperedBlade(float alongAxis, float perpAxis, float halfLen, float maxHalfWidth)
    {
        float normAlong = Mathf.Abs(alongAxis) / Mathf.Max(0.001f, halfLen);
        if (normAlong >= 1f) return 0f;

        float taper = Mathf.Pow(1f - normAlong * normAlong, 1.65f);
        float localWidth = Mathf.Max(0.002f, maxHalfWidth * taper);
        float d = Mathf.Abs(perpAxis) / localWidth;

        float coreBlade = Mathf.Exp(-d * d * 2.4f);
        float rimGlow = Mathf.Exp(-d * d * 0.65f) * 0.45f;
        return Mathf.Clamp01((coreBlade + rimGlow) * (1f - SmoothStep(0.80f, 1.0f, normAlong)));
    }

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
