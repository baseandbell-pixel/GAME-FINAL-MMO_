using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// เครื่องมือสร้างระบบเอฟเฟกต์ฟื้นสเตมิน่าฝูงโลมาแสงสีฟ้าครามล้อมตัว 4 เลเยอร์ระดับ AAA อัตโนมัติ (Zero-External-Asset)
/// - วาด Texture Procedural 3 ไฟล์ (T_CyanDolphinSpirit.png 256x128, T_AquaLightOrb.png 128x128, T_HydroGroundRipple.png 256x256)
/// - สร้าง Material URP Additive HDR รองรับ Post-Processing Bloom ให้ฝูงโลมาแสงสีฟ้าครามเรืองแสงสวยงาม
/// - สร้างโหนด VFX_StaminaDolphin_Mount พร้อม ParticleSystem ครบทั้ง 4 เลเยอร์และผูกเข้ากับตัวละครที่เลือกใน Hierarchy ทันที
/// </summary>
public static class Tool_12_Build_StaminaPotion_Butterflies_OnPlayer
{
    private const string OutputFolder = "Assets/yeen VFX/03_VFX_Prefabs_And_Assets/Generated_StaminaPotion";
    private const string TexDolphinPath = OutputFolder + "/T_CyanButterflySpirit.png";
    private const string TexAquaOrbPath = OutputFolder + "/T_AquaLightOrb.png";
    private const string TexGroundRipplePath = OutputFolder + "/T_HydroGroundRipple.png";

    private const string MatGroundRipplePath = OutputFolder + "/M_Stamina_GroundRipple_Add.mat";
    private const string MatDolphinPath = OutputFolder + "/M_Stamina_CyanButterfly_Add.mat";
    private const string MatAquaSparklesPath = OutputFolder + "/M_Stamina_AquaSparkles_Add.mat";
    private const string MatBodyMistPath = OutputFolder + "/M_Stamina_CyanBodyMist_Add.mat";

    [MenuItem("yeen VFX/12. [ใส่บน Player] สร้างเอฟเฟกต์ยาสเตมิน่าผีเสื้อสีฟ้า (Stamina Potion Butterflies)")]
    public static void GenerateStaminaDolphinVFX()
    {
        EnsureFolderHierarchy(OutputFolder);

        // 1. วาดและบันทึก Procedural Textures 3 ไฟล์ด้วยสมการคณิตศาสตร์
        GenerateAndSaveButterflySpiritTexture(TexDolphinPath, 256, 256);
        GenerateAndSaveAquaOrbTexture(TexAquaOrbPath, 128, 128);
        GenerateAndSaveHydroGroundRippleTexture(TexGroundRipplePath, 256, 256);

        AssetDatabase.Refresh();

        ConfigureTextureImporter(TexDolphinPath);
        ConfigureTextureImporter(TexAquaOrbPath);
        ConfigureTextureImporter(TexGroundRipplePath);

        Texture2D texDolphin = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDolphinPath);
        Texture2D texAquaOrb = AssetDatabase.LoadAssetAtPath<Texture2D>(TexAquaOrbPath);
        Texture2D texGroundRipple = AssetDatabase.LoadAssetAtPath<Texture2D>(TexGroundRipplePath);

        // 2. สร้าง URP Additive HDR Materials 4 ไฟล์ (Surface=Transparent, Blend=Additive, Cull=Off)
        Material matGroundRipple = CreateOrUpdateURPAdditiveMaterial(
            MatGroundRipplePath, texGroundRipple, new Color(0.28f, 1.45f, 1.85f, 1f));

        Material matDolphin = CreateOrUpdateURPAdditiveMaterial(
            MatDolphinPath, texDolphin, new Color(0.35f, 1.65f, 2.15f, 1f));

        Material matAquaSparkles = CreateOrUpdateURPAdditiveMaterial(
            MatAquaSparklesPath, texAquaOrb, new Color(0.45f, 1.75f, 2.10f, 1f));

        Material matBodyMist = CreateOrUpdateURPAdditiveMaterial(
            MatBodyMistPath, texAquaOrb, new Color(0.24f, 1.25f, 1.55f, 0.62f));

        // 3. หาตัวละครเป้าหมายที่เลือกอยู่ใน Hierarchy (หากไม่ได้เลือก จะสร้างโหนดทดสอบให้อัตโนมัติ)
        GameObject targetCharacter = Selection.activeGameObject;
        bool createdStandaloneTester = false;
        if (targetCharacter == null || !targetCharacter.scene.IsValid())
        {
            targetCharacter = GameObject.Find("AAA_StaminaDolphin_Tester");
            if (targetCharacter == null)
            {
                targetCharacter = new GameObject("AAA_StaminaDolphin_Tester");
                Undo.RegisterCreatedObjectUndo(targetCharacter, "Create AAA Stamina Dolphin Tester");
            }
            createdStandaloneTester = true;
        }

        // ลบโหนด VFX_StaminaDolphin_Mount เดิมบนตัวละครออกก่อนหากมี เพื่อสร้างใหม่สะอาด 100%
        Transform oldMount = targetCharacter.transform.Find("VFX_StaminaDolphin_Mount");
        if (oldMount != null)
        {
            Undo.DestroyObjectImmediate(oldMount.gameObject);
        }

        // 4. สร้าง VFX_StaminaDolphin_Mount พร้อม ParticleSystem ทั้ง 4 เลเยอร์ผูกเข้ากับตัวละคร
        GameObject mountObj = new GameObject("VFX_StaminaDolphin_Mount");
        Undo.RegisterCreatedObjectUndo(mountObj, "Create VFX_StaminaDolphin_Mount");
        mountObj.transform.SetParent(targetCharacter.transform, false);
        Transform visualChild = targetCharacter.transform.Find("CharacterVisual");
        mountObj.transform.localPosition = visualChild != null ? new Vector3(0f, visualChild.localPosition.y, 0f) : Vector3.zero;
        mountObj.transform.localRotation = Quaternion.identity;

        ParticleSystem psL1 = BuildLayer1HydroGroundRipple(mountObj.transform, matGroundRipple);
        ParticleSystem psL2 = BuildLayer2LeapingCyanDolphins(mountObj.transform, matDolphin);
        ParticleSystem psL3 = BuildLayer3AquaSparklesAndBubbles(mountObj.transform, matAquaSparkles);
        ParticleSystem psL4 = BuildLayer4RefreshingCyanBodyMist(mountObj.transform, matBodyMist);

        // ไฟ Point Light สีฟ้าครามเสริมบรรยากาศรอบตัว
        GameObject lightObj = new GameObject("Stamina_CyanPointLight");
        lightObj.transform.SetParent(mountObj.transform, false);
        lightObj.transform.localPosition = new Vector3(0f, 1.05f, 0f);
        Light pointLight = lightObj.AddComponent<Light>();
        pointLight.type = LightType.Point;
        pointLight.color = new Color(0.18f, 0.86f, 1.0f, 1f);
        pointLight.range = 6.2f;
        pointLight.intensity = 0f;
        pointLight.enabled = false;

        // 5. ติดตั้งและผูกค่าบน MMOStaminaPotionVFX อัตโนมัติ
        MMOStaminaPotionVFX controller = targetCharacter.GetComponent<MMOStaminaPotionVFX>();
        if (controller == null)
        {
            controller = Undo.AddComponent<MMOStaminaPotionVFX>(targetCharacter);
        }

        Undo.RecordObject(controller, "Configure MMOStaminaPotionVFX");
        controller.vfxMountRoot = mountObj.transform;
        controller.l1HydroCyanGroundRipple = psL1;
        controller.l2LeapingCyanDolphins = psL2;
        controller.l3AquaSparklesAndBubbles = psL3;
        controller.l4RefreshingCyanBodyMist = psL4;
        controller.staminaCyanPointLight = pointLight;
        controller.staminaEffectDuration = 2.8f;
        controller.enableTestHotkeys = true;
        controller.showDebugHUD = true;
        EditorUtility.SetDirty(controller);

        Selection.activeGameObject = targetCharacter;
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string targetNotice = createdStandaloneTester
            ? "เนื่องจากยังไม่ได้เลือกตัวละครใน Hierarchy ระบบจึงสร้างโหนดทดสอบ 'AAA_StaminaDolphin_Tester' ให้ (สามารถลาก VFX_StaminaDolphin_Mount ไปวางเป็นลูกของตัวละครจริงได้ทันที)"
            : "ติดตั้งโหนด 'VFX_StaminaDolphin_Mount' และสคริปต์ MMOStaminaPotionVFX ลงบน '" + targetCharacter.name + "' เรียบร้อยแล้ว!";

        EditorUtility.DisplayDialog(
            "สร้างเอฟเฟกต์ฟื้นสเตมิน่าผีเสื้อแสงฟ้าสำเร็จ 100%!",
            "สร้าง Texture 3 ไฟล์ + URP Additive HDR Materials + เอฟเฟกต์ผีเสื้อแสงสีฟ้า 4 เลเยอร์ครบสมบูรณ์แล้ว!\n" +
            "ไฟล์ทั้งหมดถูกบันทึกไว้ที่: " + OutputFolder + "\n\n" +
            targetNotice + "\n\n" +
            "วิธีทดสอบทันที:\n" +
            "1. กดปุ่ม Play ด้านบน\n" +
            "2. กดปุ่ม [V] หรือหมายเลข [2] เพื่อปล่อยฝูงผีเสื้อแสงสีฟ้าครามบินวนล้อมรอบตัวละคร!",
            "รับทราบ (OK)");
    }

    // =================================================================================================
    // PART A: วาด Procedural Textures 3 ไฟล์ (Cyan Dolphin Spirit 256x128, Aqua Orb 128x128, Hydro Ripple 256x256)
    // =================================================================================================

    /// <summary>
    /// T_CyanButterflySpirit.png (256x256): ผีเสื้อวิญญาณแสงสีฟ้าคราม ปีกบน-ล่างสมมาตร ขอบปีกเรืองแสงและหนวดโค้งอ่อนช้อย
    /// </summary>
    private static void GenerateAndSaveButterflySpiritTexture(string assetPath, int width, int height)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[width * height];

        for (int y = 0; y < height; y++)
        {
            float ny = ((float)y / (height - 1)) * 2f - 1f;
            for (int x = 0; x < width; x++)
            {
                float nx = ((float)x / (width - 1)) * 2f - 1f;

                EvalButterflyPixel(nx, ny, out float alpha, out float coreBrightness);
                float edgeFade = 1f - SmoothStep(0.90f, 0.99f, Mathf.Max(Mathf.Abs(nx), Mathf.Abs(ny)));
                alpha = Mathf.Clamp01(alpha * edgeFade);

                Color col = Color.Lerp(
                    new Color(0.10f, 0.86f, 1.0f, alpha),
                    new Color(0.95f, 0.99f, 1.0f, alpha),
                    Mathf.Clamp01(coreBrightness));
                col.a = alpha;

                pixels[y * width + x] = col;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        SaveTextureAsPNG(tex, assetPath);
        Object.DestroyImmediate(tex);
    }

    private static void EvalButterflyPixel(float nx, float ny, out float alpha, out float coreBrightness)
    {
        float ax = Mathf.Abs(nx);

        // ปีกบน (Forewing)
        float fx = ax - 0.43f;
        float fy = ny - 0.25f;
        float cosF = Mathf.Cos(-0.42f);
        float sinF = Mathf.Sin(-0.42f);
        float rfx = fx * cosF - fy * sinF;
        float rfy = fx * sinF + fy * cosF;
        float foreScaleY = 0.29f + 0.08f * Mathf.Clamp(rfx, -0.4f, 0.4f);
        float dFore = Mathf.Sqrt((rfx * rfx) / (0.44f * 0.44f) + (rfy * rfy) / (foreScaleY * foreScaleY)) - 1.0f;

        // ปีกล่าง (Hindwing)
        float hx = ax - 0.30f;
        float hy = ny + 0.27f;
        float cosH = Mathf.Cos(0.48f);
        float sinH = Mathf.Sin(0.48f);
        float rhx = hx * cosH - hy * sinH;
        float rhy = hx * sinH + hy * cosH;
        float dHind = Mathf.Sqrt((rhx * rhx) / (0.31f * 0.31f) + (rhy * rhy) / (0.23f * 0.23f)) - 1.0f;

        float dWing = SmoothMin(dFore, dHind, 0.14f);
        float wingFill = (1f - SmoothStep(-0.08f, 0.06f, dWing)) * SmoothStep(0.02f, 0.09f, ax);
        float wingRim = Mathf.Exp(-Mathf.Pow(dWing / 0.075f, 2f)) * SmoothStep(0.03f, 0.10f, ax);

        float angleFromThorax = Mathf.Atan2(ny - 0.04f, ax + 0.02f);
        float veinPattern = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(angleFromThorax * 14f), 5f) * wingFill * 0.45f;

        float spotDist = Mathf.Sqrt((ax - 0.58f) * (ax - 0.58f) + (ny - 0.36f) * (ny - 0.36f));
        float wingSpot = Mathf.Exp(-spotDist * spotDist * 90f) * 0.85f;

        // ลำตัวผีเสื้อและหนวดโค้ง
        float bodyDist = SdSegment(ax, ny, 0f, 0.26f, 0f, -0.44f);
        float bodyRadius = Mathf.Lerp(0.056f, 0.024f, Mathf.InverseLerp(0.26f, -0.44f, ny));
        float bodyMask = 1f - SmoothStep(bodyRadius * 0.65f, bodyRadius * 1.45f, bodyDist);

        float antDistA = SdSegment(ax, ny, 0.01f, 0.28f, 0.10f, 0.52f);
        float antDistB = SdSegment(ax, ny, 0.10f, 0.52f, 0.20f, 0.64f);
        float antMask = (1f - SmoothStep(0.014f, 0.036f, Mathf.Min(antDistA, antDistB))) * 0.88f;

        float outerGlow = Mathf.Exp(-Mathf.Max(0f, dWing) * 5.5f) * 0.30f;

        alpha = Mathf.Clamp01(wingFill * 0.68f + wingRim * 0.72f + veinPattern + wingSpot + bodyMask + antMask + outerGlow);
        coreBrightness = Mathf.Clamp01(bodyMask * 0.95f + wingRim * 0.65f + wingSpot * 0.85f + veinPattern * 0.5f);
    }

    private static float SmoothMin(float a, float b, float k)
    {
        float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / Mathf.Max(0.0001f, k));
        return Mathf.Lerp(b, a, h) - k * h * (1f - h);
    }

    /// <summary>
    /// T_AquaLightOrb.png (128x128): ฟองแสงสีฟ้าแกนกลางสว่างขอบฟุ้งนุ่มนวล
    /// </summary>
    private static void GenerateAndSaveAquaOrbTexture(string assetPath, int width, int height)
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
                float edgeMask = 1.0f - SmoothStep(0.75f, 0.98f, dist);

                float brightCore = Mathf.Exp(-dist * dist * 16.0f);
                float bubbleRim = Mathf.Exp(-Mathf.Pow((dist - 0.52f) / 0.14f, 2f)) * 0.35f;
                float velvetHalo = Mathf.Exp(-dist * dist * 3.8f) * 0.58f;
                float value = Mathf.Clamp01((brightCore + bubbleRim + velvetHalo) * edgeMask);

                Color col = Color.Lerp(
                    new Color(0.14f, 0.86f, 1.0f, value),
                    new Color(0.94f, 0.99f, 1.0f, value),
                    Mathf.Pow(value, 1.4f));
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
    /// T_HydroGroundRipple.png (256x256): วงคลื่นน้ำใต้ฝ่าเท้าขอบนุ่มนวล 2 ชั้น
    /// </summary>
    private static void GenerateAndSaveHydroGroundRippleTexture(string assetPath, int width, int height)
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
                float outerCutoff = 1.0f - SmoothStep(0.86f, 0.99f, dist);

                float outerWave = Mathf.Exp(-Mathf.Pow((dist - 0.74f) / 0.068f, 2f));
                float innerWave = Mathf.Exp(-Mathf.Pow((dist - 0.50f) / 0.095f, 2f)) * 0.52f;
                float hydroFlow = 0.88f + 0.12f * Mathf.Sin(angle * 8f);
                float value = Mathf.Clamp01((outerWave + innerWave) * hydroFlow * outerCutoff);

                Color col = Color.Lerp(
                    new Color(0.12f, 0.82f, 1.0f, value),
                    new Color(0.88f, 0.99f, 1.0f, value),
                    Mathf.Pow(outerWave, 1.45f));
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
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);           // Off
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
        mat.renderQueue = (int)RenderQueue.Transparent;

        EditorUtility.SetDirty(mat);
        return mat;
    }

    // =================================================================================================
    // PART C: สร้างโครงสร้าง ParticleSystem ทั้ง 4 เลเยอร์ตามกฎเหล็กข้อที่ 1
    // =================================================================================================

    // Layer 1 (Anticipation / Hydro-Cyan Ground Ripple Ring - 0.00s - 0.85s)
    private static ParticleSystem BuildLayer1HydroGroundRipple(Transform parent, Material mat)
    {
        GameObject obj = new GameObject("L1_HydroCyan_GroundRipple");
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = new Vector3(0f, 0.05f, 0f);

        ParticleSystem ps = obj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.duration = 0.90f;
        main.loop = false;
        main.playOnAwake = false;
        main.startDelay = 0.0f;
        main.startLifetime = 0.85f; // 0.00s - 0.85s
        main.startSpeed = 0f;
        main.startSize = 1.85f;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        // กฎข้อ 1: สีฟ้าคราม HDR intensity 3.4x
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.28f, 2.75f, 3.4f, 1f));
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 4;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new ParticleSystem.Burst[]
        {
            new ParticleSystem.Burst(0.0f, 2) // กฎข้อ 1: Burst 2 วง
        });

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;

        ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.separateAxes = false;
        AnimationCurve rippleExpand = new AnimationCurve(
            new Keyframe(0.00f, 0.22f, 2.9f, 2.9f),
            new Keyframe(0.55f, 0.95f, 0.45f, 0.45f),
            new Keyframe(1.00f, 1.18f, 0.0f, 0.0f));
        sol.size = new ParticleSystem.MinMaxCurve(1f, rippleExpand);

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(0.82f, 0.98f, 1.0f), 0.0f),
                new GradientColorKey(new Color(0.14f, 0.86f, 1.0f), 0.55f),
                new GradientColorKey(new Color(0.06f, 0.52f, 0.92f), 1.0f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0.00f, 0.00f),
                new GradientAlphaKey(0.95f, 0.14f),
                new GradientAlphaKey(0.65f, 0.58f),
                new GradientAlphaKey(0.00f, 1.00f)
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard; // กฎข้อ 1: Horizontal Billboard
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return ps;
    }

    // Layer 2 (Main Action / Leaping Cyan Dolphin Spirits - หัวใจหลัก 0.05s - 2.60s)
    private static ParticleSystem BuildLayer2LeapingCyanDolphins(Transform parent, Material mat)
    {
        GameObject obj = new GameObject("L2_Leaping_CyanDolphinSpirits");
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = new Vector3(0f, 0.14f, 0f);
        obj.transform.localRotation = Quaternion.identity; // คงแกน Local +Y ชี้ขึ้นฟ้าให้ Orbital Y และ Linear Y เกลียวขึ้นศีรษะ

        ParticleSystem ps = obj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.duration = 2.60f;
        main.loop = true;
        main.playOnAwake = false;
        main.startDelay = 0.05f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.85f, 2.65f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.09f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.28f); // ผีเสื้อขนาดเล็กละมุนตา
        main.startRotation = new ParticleSystem.MinMaxCurve(-22f * Mathf.Deg2Rad, 22f * Mathf.Deg2Rad);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.32f, 2.65f, 3.60f, 1f),
            new Color(0.82f, 3.30f, 4.10f, 1f));
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 90;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 12f;
        emission.SetBursts(new ParticleSystem.Burst[]
        {
            new ParticleSystem.Burst(0.0f, 8)
        });

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.68f;
        shape.radiusThickness = 0.35f;
        shape.rotation = new Vector3(-90f, 0f, 0f);

        // ทุกแกนใน Velocity over Lifetime ต้องเป็นโหมด Constant เหมือนกันทั้งหมด 100% (Unity 6 Safe)
        ParticleSystem.VelocityOverLifetimeModule vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = new ParticleSystem.MinMaxCurve(0f);
        vel.y = new ParticleSystem.MinMaxCurve(0.42f);       // ลอยขึ้นช้าๆ นุ่มนวล
        vel.z = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalX = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalY = new ParticleSystem.MinMaxCurve(1.35f); // บินวนรอบตัวช้าลงแบบผ่อนคลาย
        vel.orbitalZ = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalOffsetX = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalOffsetY = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalOffsetZ = new ParticleSystem.MinMaxCurve(0f);
        vel.radial = new ParticleSystem.MinMaxCurve(0f);
        vel.speedModifier = new ParticleSystem.MinMaxCurve(1f);

        ParticleSystem.NoiseModule noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.28f;
        noise.frequency = 1.2f;
        noise.scrollSpeed = 0.55f;
        noise.damping = true;
        noise.octaveCount = 2;

        ParticleSystem.RotationOverLifetimeModule rol = ps.rotationOverLifetime;
        rol.enabled = true;
        rol.separateAxes = true;
        rol.x = new ParticleSystem.MinMaxCurve(0f);
        rol.y = new ParticleSystem.MinMaxCurve(0f);
        rol.z = new ParticleSystem.MinMaxCurve(16f * Mathf.Deg2Rad);

        ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.separateAxes = false;
        AnimationCurve butterflySize = new AnimationCurve(
            new Keyframe(0.00f, 0.15f),
            new Keyframe(0.16f, 1.00f),
            new Keyframe(0.32f, 0.84f),
            new Keyframe(0.48f, 1.02f),
            new Keyframe(0.64f, 0.86f),
            new Keyframe(0.80f, 0.92f),
            new Keyframe(1.00f, 0.00f));
        sol.size = new ParticleSystem.MinMaxCurve(1f, butterflySize);

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(0.88f, 0.99f, 1.0f), 0.0f),
                new GradientColorKey(new Color(0.22f, 0.90f, 1.0f), 0.55f),
                new GradientColorKey(new Color(0.10f, 0.58f, 0.98f), 1.0f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0.00f, 0.00f),
                new GradientAlphaKey(1.00f, 0.16f),
                new GradientAlphaKey(0.92f, 0.72f),
                new GradientAlphaKey(0.00f, 1.00f)
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard; // Billboard ให้รูปปีกผีเสื้อสมมาตรไม่ยืดเบี้ยว
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return ps;
    }

    // Layer 3 (Secondary Elements / Aqua Sparkles & Bubbles - 0.10s - 2.50s)
    private static ParticleSystem BuildLayer3AquaSparklesAndBubbles(Transform parent, Material mat)
    {
        GameObject obj = new GameObject("L3_Aqua_SparklesAndBubbles");
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = new Vector3(0f, 0.12f, 0f);
        obj.transform.localRotation = Quaternion.identity;

        ParticleSystem ps = obj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.duration = 2.50f;
        main.loop = true;
        main.playOnAwake = false;
        main.startDelay = 0.10f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.60f, 2.35f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.10f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.13f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.42f, 2.85f, 3.60f, 1f),
            new Color(0.95f, 3.45f, 4.10f, 1f));
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 140;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 24f;
        emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0.0f, 10) });

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.68f;
        shape.radiusThickness = 0.45f;
        shape.rotation = new Vector3(-90f, 0f, 0f);

        ParticleSystem.VelocityOverLifetimeModule vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = new ParticleSystem.MinMaxCurve(0f);
        vel.y = new ParticleSystem.MinMaxCurve(0.48f);
        vel.z = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalX = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalY = new ParticleSystem.MinMaxCurve(1.15f);
        vel.orbitalZ = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalOffsetX = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalOffsetY = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalOffsetZ = new ParticleSystem.MinMaxCurve(0f);
        vel.radial = new ParticleSystem.MinMaxCurve(0f);
        vel.speedModifier = new ParticleSystem.MinMaxCurve(1f);

        ParticleSystem.NoiseModule noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.18f;
        noise.frequency = 1.2f;
        noise.damping = true;

        ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.separateAxes = false;
        AnimationCurve bubbleSize = new AnimationCurve(
            new Keyframe(0.00f, 0.00f),
            new Keyframe(0.18f, 1.00f),
            new Keyframe(0.55f, 0.78f),
            new Keyframe(1.00f, 0.00f));
        sol.size = new ParticleSystem.MinMaxCurve(1f, bubbleSize);

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(0.88f, 0.99f, 1.0f), 0.0f),
                new GradientColorKey(new Color(0.24f, 0.88f, 1.0f), 1.0f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0.00f, 0.00f),
                new GradientAlphaKey(0.95f, 0.16f),
                new GradientAlphaKey(0.82f, 0.70f),
                new GradientAlphaKey(0.00f, 1.00f)
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard; // กฎข้อ 1: Billboard
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return ps;
    }

    // Layer 4 (Impact & Residual / Refreshing Cyan Body Mist - 0.10s - 2.20s)
    private static ParticleSystem BuildLayer4RefreshingCyanBodyMist(Transform parent, Material mat)
    {
        GameObject obj = new GameObject("L4_Refreshing_CyanBodyMist");
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = new Vector3(0f, 1.0f, 0f); // กลางลำตัว

        ParticleSystem ps = obj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.duration = 2.20f;
        main.loop = false;
        main.playOnAwake = false;
        main.startDelay = 0.10f;    // เริ่มที่ 0.10s
        main.startLifetime = 2.10f; // จบที่ 2.20s (0.10 + 2.10)
        main.startSpeed = 0f;
        main.startSize = 1.15f;
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.24f, 1.65f, 2.25f, 0.40f));
        main.simulationSpace = ParticleSystemSimulationSpace.Local; // กฎข้อ 1: Local Space
        main.maxParticles = 3;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0.0f, 2) });

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;

        ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.separateAxes = false;
        AnimationCurve mistSize = new AnimationCurve(
            new Keyframe(0.00f, 0.65f),
            new Keyframe(0.36f, 1.08f),
            new Keyframe(1.00f, 1.30f));
        sol.size = new ParticleSystem.MinMaxCurve(1f, mistSize);

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(0.65f, 0.96f, 1.0f), 0.0f),
                new GradientColorKey(new Color(0.14f, 0.78f, 1.0f), 1.0f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0.00f, 0.00f),
                new GradientAlphaKey(0.42f, 0.22f),
                new GradientAlphaKey(0.30f, 0.68f),
                new GradientAlphaKey(0.00f, 1.00f)
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard; // กฎข้อ 1: Billboard
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return ps;
    }

    // =================================================================================================
    // UTILITY HELPERS
    // =================================================================================================
    private static float SdSegment(float px, float py, float ax, float ay, float bx, float by)
    {
        float pax = px - ax;
        float pay = py - ay;
        float bax = bx - ax;
        float bay = by - ay;
        float denom = Mathf.Max(0.00001f, bax * bax + bay * bay);
        float h = Mathf.Clamp01((pax * bax + pay * bay) / denom);
        float dx = pax - bax * h;
        float dy = pay - bay * h;
        return Mathf.Sqrt(dx * dx + dy * dy);
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
