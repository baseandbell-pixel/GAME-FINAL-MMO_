using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// เครื่องมือสร้างระบบเอฟเฟกต์ยารักษาหิ้งห้อยสีเขียวมรกตล้อมตัว 4 เลเยอร์ระดับ AAA อัตโนมัติ (Zero-External-Asset)
/// - วาด Texture Procedural 3 ไฟล์ (T_EmeraldFireflyOrb.png 128x128, T_HealingPlusIcon.png 128x128, T_HealGroundRing.png 256x256)
/// - สร้าง Material URP Additive HDR รองรับ Post-Processing Bloom ให้หิ้งห้อยเรืองแสงสีเขียวมรกตสวยงาม
/// - สร้างโหนด VFX_HealingFirefly_Mount พร้อม ParticleSystem ครบทั้ง 4 เลเยอร์และผูกเข้ากับตัวละครที่เลือกใน Hierarchy ทันที
/// </summary>
public static class VFXHealingPotionGenerator
{
    private const string OutputFolder = "Assets/VFX_HealingPotion";
    private const string TexFireflyOrbPath = OutputFolder + "/T_EmeraldFireflyOrb.png";
    private const string TexPlusIconPath = OutputFolder + "/T_HealingPlusIcon.png";
    private const string TexGroundRingPath = OutputFolder + "/T_HealGroundRing.png";

    private const string MatGroundRingPath = OutputFolder + "/M_Heal_GroundRing_Add.mat";
    private const string MatFireflyOrbPath = OutputFolder + "/M_Heal_EmeraldFirefly_Add.mat";
    private const string MatPlusIconPath = OutputFolder + "/M_Heal_PlusIcon_Add.mat";
    private const string MatBodyAuraPath = OutputFolder + "/M_Heal_SoftBodyAura_Add.mat";

    [MenuItem("VFX Mentor/12. สร้างเอฟเฟกต์ยารักษาหิ้งห้อยสีเขียวล้อมตัว 4 เลเยอร์ (Healing Firefly VFX)")]
    public static void GenerateHealingFireflyVFX()
    {
        EnsureFolderHierarchy(OutputFolder);

        // 1. วาดและบันทึก Procedural Textures 3 ไฟล์ด้วยสมการคณิตศาสตร์
        GenerateAndSaveFireflyOrbTexture(TexFireflyOrbPath, 128, 128);
        GenerateAndSaveHealingPlusTexture(TexPlusIconPath, 128, 128);
        GenerateAndSaveHealGroundRingTexture(TexGroundRingPath, 256, 256);

        AssetDatabase.Refresh();

        ConfigureTextureImporter(TexFireflyOrbPath);
        ConfigureTextureImporter(TexPlusIconPath);
        ConfigureTextureImporter(TexGroundRingPath);

        Texture2D texFireflyOrb = AssetDatabase.LoadAssetAtPath<Texture2D>(TexFireflyOrbPath);
        Texture2D texPlusIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(TexPlusIconPath);
        Texture2D texGroundRing = AssetDatabase.LoadAssetAtPath<Texture2D>(TexGroundRingPath);

        // 2. สร้าง URP Additive HDR Materials 4 ไฟล์ (Surface=Transparent, Blend=Additive, Cull=Off)
        Material matGroundRing = CreateOrUpdateURPAdditiveMaterial(
            MatGroundRingPath, texGroundRing, new Color(0.45f, 1.55f, 0.78f, 1f));

        Material matFireflyOrb = CreateOrUpdateURPAdditiveMaterial(
            MatFireflyOrbPath, texFireflyOrb, new Color(0.55f, 1.85f, 0.92f, 1f));

        Material matPlusIcon = CreateOrUpdateURPAdditiveMaterial(
            MatPlusIconPath, texPlusIcon, new Color(0.60f, 1.75f, 1.05f, 1f));

        Material matBodyAura = CreateOrUpdateURPAdditiveMaterial(
            MatBodyAuraPath, texFireflyOrb, new Color(0.45f, 1.35f, 0.75f, 0.65f));

        // 3. หาตัวละครเป้าหมายที่เลือกอยู่ใน Hierarchy (หากไม่ได้เลือก จะสร้างโหนดทดสอบให้อัตโนมัติ)
        GameObject targetCharacter = Selection.activeGameObject;
        bool createdStandaloneTester = false;
        if (targetCharacter == null || !targetCharacter.scene.IsValid())
        {
            targetCharacter = GameObject.Find("AAA_HealingFirefly_Tester");
            if (targetCharacter == null)
            {
                targetCharacter = new GameObject("AAA_HealingFirefly_Tester");
                Undo.RegisterCreatedObjectUndo(targetCharacter, "Create AAA Healing Firefly Tester");
            }
            createdStandaloneTester = true;
        }

        // ลบโหนด VFX_HealingFirefly_Mount เดิมบนตัวละครออกก่อนหากมี เพื่อสร้างใหม่สะอาด 100%
        Transform oldMount = targetCharacter.transform.Find("VFX_HealingFirefly_Mount");
        if (oldMount != null)
        {
            Undo.DestroyObjectImmediate(oldMount.gameObject);
        }

        // 4. สร้าง VFX_HealingFirefly_Mount พร้อม ParticleSystem ทั้ง 4 เลเยอร์ผูกเข้ากับตัวละคร
        GameObject mountObj = new GameObject("VFX_HealingFirefly_Mount");
        Undo.RegisterCreatedObjectUndo(mountObj, "Create VFX_HealingFirefly_Mount");
        mountObj.transform.SetParent(targetCharacter.transform, false);
        Transform visualChild = targetCharacter.transform.Find("CharacterVisual");
        mountObj.transform.localPosition = visualChild != null ? new Vector3(0f, visualChild.localPosition.y, 0f) : Vector3.zero;
        mountObj.transform.localRotation = Quaternion.identity;

        ParticleSystem psL1 = BuildLayer1GroundPulseRing(mountObj.transform, matGroundRing);
        ParticleSystem psL2 = BuildLayer2OrbitingFireflies(mountObj.transform, matFireflyOrb);
        ParticleSystem psL3 = BuildLayer3RisingPlusIcons(mountObj.transform, matPlusIcon);
        ParticleSystem psL4 = BuildLayer4SoftGreenBodyAura(mountObj.transform, matBodyAura);

        // ไฟ Point Light สีเขียวมรกตอ่อนโยนเสริมรอบลำตัว
        GameObject lightObj = new GameObject("Heal_EmeraldPointLight");
        lightObj.transform.SetParent(mountObj.transform, false);
        lightObj.transform.localPosition = new Vector3(0f, 1.05f, 0f);
        Light pointLight = lightObj.AddComponent<Light>();
        pointLight.type = LightType.Point;
        pointLight.color = new Color(0.28f, 1.0f, 0.56f, 1f);
        pointLight.range = 6.0f;
        pointLight.intensity = 0f;
        pointLight.enabled = false;

        // 5. ติดตั้งและผูกค่าบน MMOHealingPotionVFX อัตโนมัติ
        MMOHealingPotionVFX controller = targetCharacter.GetComponent<MMOHealingPotionVFX>();
        if (controller == null)
        {
            controller = Undo.AddComponent<MMOHealingPotionVFX>(targetCharacter);
        }

        Undo.RecordObject(controller, "Configure MMOHealingPotionVFX");
        controller.vfxMountRoot = mountObj.transform;
        controller.l1GroundEmeraldPulseRing = psL1;
        controller.l2OrbitingEmeraldFireflies = psL2;
        controller.l3RisingHealingPlusIcons = psL3;
        controller.l4SoftGreenBodyAura = psL4;
        controller.healEmeraldPointLight = pointLight;
        controller.healEffectDuration = 2.8f;
        controller.enableTestHotkeys = true;
        controller.showDebugHUD = true;
        EditorUtility.SetDirty(controller);

        Selection.activeGameObject = targetCharacter;
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string targetNotice = createdStandaloneTester
            ? "เนื่องจากยังไม่ได้เลือกตัวละครใน Hierarchy ระบบจึงสร้างโหนดทดสอบ 'AAA_HealingFirefly_Tester' ให้ (สามารถลาก VFX_HealingFirefly_Mount ไปวางเป็นลูกของตัวละครจริงได้ทันที)"
            : "ติดตั้งโหนด 'VFX_HealingFirefly_Mount' และสคริปต์ MMOHealingPotionVFX ลงบน '" + targetCharacter.name + "' เรียบร้อยแล้ว!";

        EditorUtility.DisplayDialog(
            "สร้างเอฟเฟกต์ยารักษาหิ้งห้อยสีเขียวสำเร็จ 100%!",
            "สร้าง Texture 3 ไฟล์ + URP Additive HDR Materials + เอฟเฟกต์หิ้งห้อย 4 เลเยอร์ครบสมบูรณ์แล้ว!\n" +
            "ไฟล์ทั้งหมดถูกบันทึกไว้ที่: " + OutputFolder + "\n\n" +
            targetNotice + "\n\n" +
            "วิธีทดสอบทันที:\n" +
            "1. กดปุ่ม Play ด้านบน\n" +
            "2. กดปุ่ม [H] หรือหมายเลข [1] เพื่อปล่อยฝูงหิ้งห้อยสีเขียวมรกตลอยวนล้อมรอบตัวละคร!",
            "รับทราบ (OK)");
    }

    // =================================================================================================
    // PART A: วาด Procedural Textures 3 ไฟล์ (Firefly Orb, Healing Plus Icon, Heal Ground Ring)
    // =================================================================================================

    /// <summary>
    /// T_EmeraldFireflyOrb.png (128x128): ดวงไฟหิ้งห้อยทรงกลมแกนกลางสว่างขอบฟุ้งละมุน
    /// </summary>
    private static void GenerateAndSaveFireflyOrbTexture(string assetPath, int width, int height)
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

                float brightCore = Mathf.Exp(-dist * dist * 16.5f);
                float velvetHalo = Mathf.Exp(-dist * dist * 3.8f) * 0.62f;
                float value = Mathf.Clamp01((brightCore + velvetHalo) * edgeMask);

                Color col = Color.Lerp(
                    new Color(0.20f, 1.0f, 0.52f, value),
                    new Color(0.95f, 1.0f, 0.96f, value),
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
    /// T_HealingPlusIcon.png (128x128): เครื่องหมายบวกรักษา (+) ขอบมนพร้อมรัศมีเรืองแสงรอบๆ
    /// </summary>
    private static void GenerateAndSaveHealingPlusTexture(string assetPath, int width, int height)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[width * height];

        for (int y = 0; y < height; y++)
        {
            float ny = ((float)y / (height - 1)) * 2f - 1f;
            for (int x = 0; x < width; x++)
            {
                float nx = ((float)x / (width - 1)) * 2f - 1f;
                float distFromCenter = Mathf.Sqrt(nx * nx + ny * ny);
                float edgeMask = 1.0f - SmoothStep(0.78f, 0.98f, distFromCenter);

                // วาดเครื่องหมายบวกขอบมนคมชัดด้วยสมการ Signed Distance Field (Rounded Box Union)
                float dHorizontal = SdRoundedBox(nx, ny, 0.52f, 0.14f, 0.08f);
                float dVertical = SdRoundedBox(nx, ny, 0.14f, 0.52f, 0.08f);
                float dPlus = Mathf.Min(dHorizontal, dVertical);

                float plusBody = 1.0f - SmoothStep(-0.02f, 0.045f, dPlus);
                float softAura = Mathf.Exp(-Mathf.Max(0f, dPlus) * 6.5f) * 0.58f;
                float value = Mathf.Clamp01((plusBody + softAura) * edgeMask);

                Color col = Color.Lerp(
                    new Color(0.28f, 1.0f, 0.68f, value),
                    new Color(0.94f, 1.0f, 0.97f, value),
                    plusBody * 0.82f);
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
    /// T_HealGroundRing.png (256x256): วงแหวนแสงใต้ฝ่าเท้าขอบนุ่มนวลละมุนตา
    /// </summary>
    private static void GenerateAndSaveHealGroundRingTexture(string assetPath, int width, int height)
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

                float mainRing = Mathf.Exp(-Mathf.Pow((dist - 0.72f) / 0.072f, 2f));
                float softInnerHalo = Mathf.Exp(-Mathf.Pow((dist - 0.56f) / 0.16f, 2f)) * 0.44f * SmoothStep(0.24f, 0.60f, dist);
                float gentleWave = 0.90f + 0.10f * Mathf.Sin(angle * 6f);
                float value = Mathf.Clamp01((mainRing + softInnerHalo) * gentleWave * outerCutoff);

                Color col = Color.Lerp(
                    new Color(0.16f, 0.96f, 0.50f, value),
                    new Color(0.88f, 1.0f, 0.93f, value),
                    Mathf.Pow(mainRing, 1.45f));
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

    // Layer 1 (Anticipation / Ground Emerald Pulse Ring - 0.00s - 0.85s)
    private static ParticleSystem BuildLayer1GroundPulseRing(Transform parent, Material mat)
    {
        GameObject obj = new GameObject("L1_Ground_EmeraldPulseRing");
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
        main.startSize = 2.4f;      // กฎข้อ 1: Start Size = 2.4
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        // กฎข้อ 1: สีเขียวมรกต HDR intensity 3.2x
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.45f, 3.2f, 1.35f, 1f));
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
        AnimationCurve ringExpand = new AnimationCurve(
            new Keyframe(0.00f, 0.22f, 2.8f, 2.8f),
            new Keyframe(0.55f, 0.92f, 0.5f, 0.5f),
            new Keyframe(1.00f, 1.15f, 0.0f, 0.0f));
        sol.size = new ParticleSystem.MinMaxCurve(1f, ringExpand);

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(0.75f, 1.0f, 0.88f), 0.0f),
                new GradientColorKey(new Color(0.18f, 0.95f, 0.48f), 0.55f),
                new GradientColorKey(new Color(0.08f, 0.72f, 0.35f), 1.0f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0.00f, 0.00f),
                new GradientAlphaKey(0.95f, 0.14f),
                new GradientAlphaKey(0.65f, 0.55f),
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

    // Layer 2 (Main Action / Orbiting Emerald Fireflies - หัวใจหลัก 0.05s - 2.60s)
    private static ParticleSystem BuildLayer2OrbitingFireflies(Transform parent, Material mat)
    {
        GameObject obj = new GameObject("L2_Orbiting_EmeraldFireflies");
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = new Vector3(0f, 0.12f, 0f);
        obj.transform.localRotation = Quaternion.identity; // คงแกน Local +Y ชี้ขึ้นฟ้าให้ Orbital Y และ Linear Y หมุนล้อมตัวถูกต้อง 100%

        ParticleSystem ps = obj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.duration = 2.60f;
        main.loop = true;
        main.playOnAwake = false;
        main.startDelay = 0.05f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.45f, 2.25f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.20f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.09f, 0.24f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.45f, 3.4f, 1.45f, 1f),
            new Color(1.25f, 3.8f, 2.10f, 1f)); // ฝูงหิ้งห้อยสีเขียวมรกต-มินต์ HDR
        main.simulationSpace = ParticleSystemSimulationSpace.Local; // กฎข้อ 1: Local Space
        main.maxParticles = 180;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 32f; // กฎข้อ 1: Rate over Time = 32
        emission.SetBursts(new ParticleSystem.Burst[]
        {
            new ParticleSystem.Burst(0.0f, 18) // กฎข้อ 1: Burst 18 ดวง
        });

        // กฎข้อ 1: Circle Shape Radius = 0.68, Radius Thickness = 0.35 (หมุน Shape -90 แกน X ให้นอนราบรอบเท้า)
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.68f;
        shape.radiusThickness = 0.35f;
        shape.rotation = new Vector3(-90f, 0f, 0f);

        // กฎข้อ 1 (ความปลอดภัย Unity 6): ทุกแกนใน Velocity over Lifetime ต้องเป็นโหมด Constant เหมือนกันทั้งหมด 100%
        ParticleSystem.VelocityOverLifetimeModule vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = new ParticleSystem.MinMaxCurve(0f);
        vel.y = new ParticleSystem.MinMaxCurve(0.75f);       // กฎข้อ 1: Linear Y = 0.75
        vel.z = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalX = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalY = new ParticleSystem.MinMaxCurve(2.6f); // กฎข้อ 1: Orbital Y = 2.6
        vel.orbitalZ = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalOffsetX = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalOffsetY = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalOffsetZ = new ParticleSystem.MinMaxCurve(0f);
        vel.radial = new ParticleSystem.MinMaxCurve(0f);
        vel.speedModifier = new ParticleSystem.MinMaxCurve(1f);

        // กฎข้อ 1: โมดูล Noise (Strength = 0.35, Frequency = 1.4) ให้ฝูงหิ้งห้อยบินส่ายอ่อนช้อย
        ParticleSystem.NoiseModule noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.35f;
        noise.frequency = 1.4f;
        noise.scrollSpeed = 0.65f;
        noise.damping = true;
        noise.octaveCount = 2;

        // กราฟขนาดกระพริบระยิบระยับเหมือนหิ้งห้อยแล้วค่อยๆ เล็กหายไป
        ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.separateAxes = false;
        AnimationCurve fireflySize = new AnimationCurve(
            new Keyframe(0.00f, 0.00f),
            new Keyframe(0.18f, 1.00f),
            new Keyframe(0.45f, 0.72f),
            new Keyframe(0.68f, 1.05f),
            new Keyframe(1.00f, 0.00f));
        sol.size = new ParticleSystem.MinMaxCurve(1f, fireflySize);

        // ค่อยๆ สว่างขึ้นแล้วจางหายไปในอากาศอย่างอ่อนโยน
        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(0.85f, 1.0f, 0.88f), 0.0f),
                new GradientColorKey(new Color(0.32f, 1.0f, 0.56f), 0.55f),
                new GradientColorKey(new Color(0.18f, 0.85f, 0.45f), 1.0f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0.00f, 0.00f),
                new GradientAlphaKey(1.00f, 0.20f),
                new GradientAlphaKey(0.92f, 0.70f),
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

    // Layer 3 (Secondary Elements / Rising Healing Plus Icons - 0.15s - 2.40s)
    private static ParticleSystem BuildLayer3RisingPlusIcons(Transform parent, Material mat)
    {
        GameObject obj = new GameObject("L3_Rising_HealingPlusIcons");
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = new Vector3(0f, 0.85f, 0f); // ลอยขึ้นรอบลำตัวและไหล่

        ParticleSystem ps = obj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.duration = 2.40f;
        main.loop = true;
        main.playOnAwake = false;
        main.startDelay = 0.15f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.15f, 1.75f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.45f, 0.85f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.32f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 3.4f, 2.0f, 1f)); // สีเขียวมินต์เรืองแสง
        main.simulationSpace = ParticleSystemSimulationSpace.World; // กฎข้อ 1: World Space
        main.maxParticles = 40;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 8f; // กฎข้อ 1: Rate over Time = 8
        emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0.0f, 3) });

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.65f;
        shape.rotation = new Vector3(-90f, 0f, 0f);

        ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.separateAxes = false;
        AnimationCurve plusSize = new AnimationCurve(
            new Keyframe(0.00f, 0.15f),
            new Keyframe(0.18f, 1.05f),
            new Keyframe(0.78f, 0.90f),
            new Keyframe(1.00f, 0.00f));
        sol.size = new ParticleSystem.MinMaxCurve(1f, plusSize);

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(0.82f, 1.0f, 0.92f), 0.0f),
                new GradientColorKey(new Color(0.35f, 0.98f, 0.68f), 1.0f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0.00f, 0.00f),
                new GradientAlphaKey(0.95f, 0.18f),
                new GradientAlphaKey(0.80f, 0.72f),
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

    // Layer 4 (Impact & Residual / Soft Green Body Aura - 0.10s - 2.20s)
    private static ParticleSystem BuildLayer4SoftGreenBodyAura(Transform parent, Material mat)
    {
        GameObject obj = new GameObject("L4_SoftGreen_BodyAura");
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
        main.startSize = 1.45f;     // กฎข้อ 1: Start Size = 1.45
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.45f, 2.2f, 1.15f, 0.48f));
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
        AnimationCurve auraSize = new AnimationCurve(
            new Keyframe(0.00f, 0.65f),
            new Keyframe(0.35f, 1.08f),
            new Keyframe(1.00f, 1.28f));
        sol.size = new ParticleSystem.MinMaxCurve(1f, auraSize);

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(0.65f, 1.0f, 0.80f), 0.0f),
                new GradientColorKey(new Color(0.25f, 0.90f, 0.55f), 1.0f)
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
    private static float SdRoundedBox(float px, float py, float bx, float by, float r)
    {
        float qx = Mathf.Abs(px) - bx + r;
        float qy = Mathf.Abs(py) - by + r;
        float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
        float inside = Mathf.Min(Mathf.Max(qx, qy), 0f);
        return outside + inside - r;
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
