#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// เครื่องมือสร้างระบบเอฟเฟกต์ "กลิ้งหลบแบบควันขาว / พุ่งตัวเป็นหมอกควันระดับ AAA (White Smoke Dodge Roll & Mist Dash VFX)"
/// วาด Texture ก้อนเมฆควัน 2x2 Flipbook Sprite Sheet (T_SoftWhiteSmoke_2x2.png ขนาด 512x512 px) ด้วย Multi-Octave Perlin Noise,
/// วาดเส้นลมแหวกควัน (T_RollWindStreak.png), สร้าง Material ที่แสดงผลคมชัด 100% ใน Unity 6 URP
/// และผูกเอฟเฟกต์ทั้ง 4 เลเยอร์เข้ากับตัวละครหลักในคลิกเดียว
/// </summary>
public class Tool_06_Build_DodgeRoll_Smoke_OnPlayer : EditorWindow
{
    private const string OutputFolder = "Assets/yeen VFX/03_VFX_Prefabs_And_Assets/Generated_DodgeSmoke";
    private const string SmokeAtlasTexPath = "Assets/yeen VFX/03_VFX_Prefabs_And_Assets/Generated_DodgeSmoke/TEX_Dodge_SoftWhiteSmoke_2x2.png";
    private const string WindStreakTexPath = "Assets/yeen VFX/03_VFX_Prefabs_And_Assets/Generated_DodgeSmoke/TEX_Dodge_RollWindStreak.png";

    private const string SmokeMatPath = "Assets/yeen VFX/03_VFX_Prefabs_And_Assets/Generated_DodgeSmoke/MAT_Dodge_SoftWhiteSmoke_2x2.mat";
    private const string WindStreakMatPath = "Assets/yeen VFX/03_VFX_Prefabs_And_Assets/Generated_DodgeSmoke/MAT_Dodge_RollWindStreak.mat";

    [MenuItem("yeen VFX/06. [ใส่บน Player] สร้างเอฟเฟกต์กลิ้งหลบควันขาว (Dodge Roll Smoke)")]
    public static void GenerateWhiteSmokeRollVFX()
    {
        EnsureOutputDirectory();

        // 1. กฎเหล็กข้อที่ 2: วาดและบันทึกไฟล์ Texture T_SoftWhiteSmoke_2x2.png (512x512 px, ตาราง 2x2 Flipbook 4 รูปทรงก้อนเมฆควัน)
        Texture2D smokeAtlasTex = GenerateSoftWhiteSmoke2x2Atlas(SmokeAtlasTexPath);

        // 2. กฎเหล็กข้อที่ 2: วาดไฟล์ T_RollWindStreak.png สำหรับเส้นสายลมแหวกมวลควัน
        Texture2D windStreakTex = GenerateRollWindStreakTexture(WindStreakTexPath);

        // 3. กฎเหล็กข้อที่ 3: สร้าง Materials โดยใช้ Shader.Find("Sprites/Default") เป็นหลัก (สำรองด้วย URP Particles/Unlit)
        Material smokeMat = CreateGuaranteedURPMaterial(SmokeMatPath, smokeAtlasTex, new Color(1.0f, 1.0f, 1.0f, 0.90f));
        Material windStreakMat = CreateGuaranteedURPMaterial(WindStreakMatPath, windStreakTex, new Color(1.0f, 1.0f, 1.0f, 0.85f));

        List<string> installedTargets = new List<string>();

        // 4. ติดตั้งลงบนตัวละครที่เลือกใน Hierarchy (หรือค้นหาตัวละครในฉากอัตโนมัติ)
        GameObject targetCharacter = ResolveSelectedOrSceneCharacter();
        if (targetCharacter != null)
        {
            Build4LayerWhiteSmokeRollOnCharacter(targetCharacter, smokeMat, windStreakMat);
            installedTargets.Add("Scene Character: " + targetCharacter.name);
        }

        // 5. ติดตั้งลงใน Player Prefabs หลักของโปรเจกต์อัตโนมัติ (GreatSwordGirl.prefab และ Player.prefab)
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
                    Build4LayerWhiteSmokeRollOnCharacter(prefabRoot, smokeMat, windStreakMat);
                    PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                    PrefabUtility.UnloadPrefabContents(prefabRoot);
                    installedTargets.Add("Prefab: " + Path.GetFileName(prefabPath));
                }
            }
        }

        if (targetCharacter == null)
        {
            GameObject demoTarget = new GameObject("AAA_WhiteSmokeRoll_Character_Tester");
            demoTarget.transform.position = Vector3.zero;
            Build4LayerWhiteSmokeRollOnCharacter(demoTarget, smokeMat, windStreakMat);
            Selection.activeGameObject = demoTarget;
            installedTargets.Add("Demo Object: " + demoTarget.name);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (!Application.isPlaying && UnityEngine.SceneManagement.SceneManager.GetActiveScene().IsValid())
        {
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }

        EditorUtility.DisplayDialog(
            "สร้างเอฟเฟกต์กลิ้งเป็นควันขาว 4 เลเยอร์ สำเร็จ!",
            "ดำเนินการวาด Texture ควัน 2x2 (512x512 px) และติดตั้งเอฟเฟกต์ควันกลิ้ง 4 เลเยอร์เรียบร้อยแล้ว:\n\n" +
            "• " + string.Join("\n• ", installedTargets.ToArray()) + "\n\n" +
            "วิธีทดสอบในโหมด Play:\n" +
            "1. กดปุ่มกลิ้งของตัวละคร [Space] หรือกดปุ่มทดสอบ [C] / [Left Ctrl]\n" +
            "2. เอฟเฟกต์ควันขาวทั้ง 4 เลเยอร์จะทำงานอัตโนมัติทันทีที่ตัวละครเข้าสู่ท่า Roll / Dodge!",
            "รับทราบ (พร้อมกด Play ทดสอบ)"
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

        Animator sceneAnim = Object.FindAnyObjectByType<Animator>();
        if (sceneAnim != null) return sceneAnim.gameObject;

        return null;
    }

    // ================================================================================================
    // กฎเหล็กข้อที่ 1: โครงสร้างเอฟเฟกต์กลิ้งเป็นควันขาว 4 เลเยอร์ (4-Layer White Smoke Roll Architecture)
    // ================================================================================================
    private static void Build4LayerWhiteSmokeRollOnCharacter(
        GameObject characterRoot,
        Material smokeMat,
        Material windStreakMat)
    {
        if (characterRoot == null) return;

        Undo.RegisterFullObjectHierarchyUndo(characterRoot, "Build AAA White Smoke Roll VFX");

        Transform existingRoot = characterRoot.transform.Find("VFX_AAA_WhiteSmokeRoll_Root");
        if (existingRoot != null)
        {
            Object.DestroyImmediate(existingRoot.gameObject);
        }

        GameObject vfxRoot = new GameObject("VFX_AAA_WhiteSmokeRoll_Root");
        vfxRoot.transform.SetParent(characterRoot.transform, false);
        vfxRoot.transform.localPosition = Vector3.zero;
        vfxRoot.transform.localRotation = Quaternion.identity;
        vfxRoot.transform.localScale = Vector3.one;

        // --------------------------------------------------------------------------------------------
        // LAYER 1 (0.00s - 0.12s): Anticipation / Launch Smoke Burst
        // กลุ่มควันขาวหนานุ่มระเบิดออกเป็นวงกลมติดพื้น (Circle Shape, World Space, Burst 16 ก้อน, Limit Velocity over Lifetime)
        // --------------------------------------------------------------------------------------------
        GameObject l1Obj = new GameObject("L1_Launch_SmokeBurst");
        l1Obj.transform.SetParent(vfxRoot.transform, false);
        l1Obj.transform.localPosition = new Vector3(0f, 0.15f, 0f);
        l1Obj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // วางวงกลมขนานกับพื้น
        l1Obj.transform.localScale = Vector3.one;

        ParticleSystem l1PS = l1Obj.AddComponent<ParticleSystem>();
        ConfigureLayer1LaunchSmokeBurst(l1PS, smokeMat);

        // --------------------------------------------------------------------------------------------
        // LAYER 2 (0.02s - 0.42s): Main Action / Rolling Smoke Trail
        // กลุ่มเมฆควันขาวต่อเนื่อง (World Space, Rate over Time = 35, Rate over Distance = 12,
        // Rotation over Lifetime ให้ก้อนควันม้วนตัว, Size over Lifetime ขยายตัวออกขณะจางหาย)
        // --------------------------------------------------------------------------------------------
        GameObject l2Obj = new GameObject("L2_Rolling_SmokeTrail");
        l2Obj.transform.SetParent(vfxRoot.transform, false);
        l2Obj.transform.localPosition = new Vector3(0f, 0.55f, 0f); // ระดับเอว/ลำตัวขณะม้วนตัวกลิ้ง
        l2Obj.transform.localRotation = Quaternion.identity;
        l2Obj.transform.localScale = Vector3.one;

        ParticleSystem l2PS = l2Obj.AddComponent<ParticleSystem>();
        ConfigureLayer2RollingSmokeTrail(l2PS, smokeMat);

        // --------------------------------------------------------------------------------------------
        // LAYER 3 (0.05s - 0.35s): Secondary Elements / Wind Speed Streaks
        // โหมด Stretched Billboard (Velocity Scale = 0.06, Length Scale = 4.5, World Space)
        // ปล่อยเส้นสายลมสีขาวโปร่งใสแหวกมวลควันไปด้านหลัง
        // --------------------------------------------------------------------------------------------
        GameObject l3Obj = new GameObject("L3_Wind_SpeedStreaks");
        l3Obj.transform.SetParent(vfxRoot.transform, false);
        l3Obj.transform.localPosition = new Vector3(0f, 0.65f, 0.35f);
        l3Obj.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // เป่าเส้นลมแหวกควันไปด้านหลังตัวละคร
        l3Obj.transform.localScale = Vector3.one;

        ParticleSystem l3PS = l3Obj.AddComponent<ParticleSystem>();
        ConfigureLayer3WindSpeedStreaks(l3PS, windStreakMat);

        // --------------------------------------------------------------------------------------------
        // LAYER 4 (0.42s - 0.85s): Impact & Residual / Recovery Smoke Ring
        // กลุ่มควันจางๆ (Burst 10 ก้อน, World Space) กระจายตัวออกรอบตัวหลังกลิ้งจบและลุกขึ้นยืน
        // --------------------------------------------------------------------------------------------
        GameObject l4Obj = new GameObject("L4_Recovery_SmokeRing");
        l4Obj.transform.SetParent(vfxRoot.transform, false);
        l4Obj.transform.localPosition = new Vector3(0f, 0.25f, 0f);
        l4Obj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        l4Obj.transform.localScale = Vector3.one;

        ParticleSystem l4PS = l4Obj.AddComponent<ParticleSystem>();
        ConfigureLayer4RecoverySmokeRing(l4PS, smokeMat);

        // --------------------------------------------------------------------------------------------
        // ติดตั้งและผูกสคริปต์ MMODodgeSmokeController บนตัวละครหลัก
        // --------------------------------------------------------------------------------------------
        MMODodgeSmokeController controller = characterRoot.GetComponent<MMODodgeSmokeController>();
        if (controller == null)
        {
            controller = characterRoot.AddComponent<MMODodgeSmokeController>();
        }

        controller.smokeVfxRoot = vfxRoot.transform;
        controller.l1LaunchSmokeBurst = l1PS;
        controller.l2RollingSmokeTrail = l2PS;
        controller.l3WindSpeedStreaks = l3PS;
        controller.l4RecoverySmokeRing = l4PS;
        controller.rollDuration = 0.42f;

        EditorUtility.SetDirty(controller);
        EditorUtility.SetDirty(characterRoot);
    }

    private static void ConfigureLayer1LaunchSmokeBurst(ParticleSystem ps, Material smokeMat)
    {
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.duration = 0.5f;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.72f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(3.5f, 6.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.75f, 1.25f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new Color(1.0f, 1.0f, 1.0f, 0.85f);
        main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.08f);
        main.maxParticles = 60;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = false;
        emission.rateOverTime = new ParticleSystem.MinMaxCurve(0f);
        emission.SetBursts(new ParticleSystem.Burst[]
        {
            new ParticleSystem.Burst(0.0f, 16)
        });

        // Circle Shape ขนานพื้น
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.35f;
        shape.radiusThickness = 0.85f;

        ParticleSystem.VelocityOverLifetimeModule vel = ps.velocityOverLifetime;
        vel.enabled = false;

        // Limit Velocity over Lifetime จำลองแรงต้านอากาศให้ควันพุ่งออกเร็วตอนแรกแล้วชะลอตัวลอยอ้อยอิ่ง
        ParticleSystem.LimitVelocityOverLifetimeModule limitVel = ps.limitVelocityOverLifetime;
        limitVel.enabled = true;
        limitVel.limit = new ParticleSystem.MinMaxCurve(0.65f);
        limitVel.dampen = 0.28f;

        // Rotation over Lifetime ให้ก้อนควันม้วนตัวนุ่มนวล
        ParticleSystem.RotationOverLifetimeModule rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-1.2f, 1.2f);

        // Size over Lifetime ขยายตัวออกขณะจางหาย
        ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
        sol.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve(
            new Keyframe(0.0f, 0.45f, 0f, 2.2f),
            new Keyframe(0.35f, 1.0f, 0.6f, 0.6f),
            new Keyframe(1.0f, 1.45f, 0.2f, 0f)
        );
        sol.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(BuildSoftWhiteSmokeGradient(0.85f));

        // กฎเหล็กข้อที่ 2: เปิดใช้งาน Texture Sheet Animation (2x2 Whole Sheet Random)
        Configure2x2SmokeTextureSheet(ps);

        ParticleSystemRenderer psr = ps.GetComponent<ParticleSystemRenderer>();
        psr.renderMode = ParticleSystemRenderMode.Billboard;
        psr.sharedMaterial = smokeMat;
        psr.shadowCastingMode = ShadowCastingMode.Off;
        psr.receiveShadows = false;
        psr.sortingOrder = 6;
    }

    private static void ConfigureLayer2RollingSmokeTrail(ParticleSystem ps, Material smokeMat)
    {
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        // กฎเหล็กข้อที่ 1 (Layer 2): World Space, Rate over Time = 35, Rate over Distance = 12,
        // Rotation over Lifetime ให้ก้อนควันม้วนตัว, Size over Lifetime ขยายตัวออกขณะจางหาย
        ParticleSystem.MainModule main = ps.main;
        main.duration = 1.0f;
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.48f, 0.78f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.45f, 1.35f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.85f, 1.45f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new Color(1.0f, 1.0f, 1.0f, 0.82f);
        main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.12f); // ลอยตัวขึ้นเบาๆ เหมือนหมอกควัน
        main.maxParticles = 200;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = false;
        emission.rateOverTime = new ParticleSystem.MinMaxCurve(35f);
        emission.rateOverDistance = new ParticleSystem.MinMaxCurve(12f);

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.38f;

        ParticleSystem.VelocityOverLifetimeModule vel = ps.velocityOverLifetime;
        vel.enabled = false;

        ParticleSystem.LimitVelocityOverLifetimeModule limitVel = ps.limitVelocityOverLifetime;
        limitVel.enabled = true;
        limitVel.limit = new ParticleSystem.MinMaxCurve(0.5f);
        limitVel.dampen = 0.22f;

        ParticleSystem.RotationOverLifetimeModule rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-1.6f, 1.6f);

        ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
        sol.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve(
            new Keyframe(0.0f, 0.42f, 0f, 2.0f),
            new Keyframe(0.35f, 1.05f, 0.8f, 0.8f),
            new Keyframe(1.0f, 1.65f, 0.3f, 0f)
        );
        sol.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(BuildSoftWhiteSmokeGradient(0.82f));

        Configure2x2SmokeTextureSheet(ps);

        ParticleSystemRenderer psr = ps.GetComponent<ParticleSystemRenderer>();
        psr.renderMode = ParticleSystemRenderMode.Billboard;
        psr.sharedMaterial = smokeMat;
        psr.shadowCastingMode = ShadowCastingMode.Off;
        psr.receiveShadows = false;
        psr.sortingOrder = 7;
    }

    private static void ConfigureLayer3WindSpeedStreaks(ParticleSystem ps, Material windStreakMat)
    {
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        // กฎเหล็กข้อที่ 1 (Layer 3): Stretched Billboard (Velocity Scale = 0.06, Length Scale = 4.5, World Space)
        ParticleSystem.MainModule main = ps.main;
        main.duration = 1.0f;
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.30f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(8.5f, 14.0f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.09f, 0.18f);
        main.startColor = new Color(1.0f, 1.0f, 1.0f, 0.88f);
        main.gravityModifier = new ParticleSystem.MinMaxCurve(0f);
        main.maxParticles = 120;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = false;
        emission.rateOverTime = new ParticleSystem.MinMaxCurve(48f);
        emission.rateOverDistance = new ParticleSystem.MinMaxCurve(6f);

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 14f;
        shape.radius = 0.45f;
        shape.radiusThickness = 0.80f;

        ParticleSystem.VelocityOverLifetimeModule vel = ps.velocityOverLifetime;
        vel.enabled = false;

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(BuildSoftWhiteSmokeGradient(0.88f));

        ParticleSystemRenderer psr = ps.GetComponent<ParticleSystemRenderer>();
        psr.renderMode = ParticleSystemRenderMode.Stretch;
        psr.cameraVelocityScale = 0f;
        psr.velocityScale = 0.06f;
        psr.lengthScale = 4.5f;
        psr.sharedMaterial = windStreakMat;
        psr.shadowCastingMode = ShadowCastingMode.Off;
        psr.receiveShadows = false;
        psr.sortingOrder = 8;
    }

    private static void ConfigureLayer4RecoverySmokeRing(ParticleSystem ps, Material smokeMat)
    {
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        // กฎเหล็กข้อที่ 1 (Layer 4): 0.42s - 0.85s, Burst 10 ก้อน, World Space กระจายออกรอบตัวขณะลุกขึ้นยืน
        ParticleSystem.MainModule main = ps.main;
        main.duration = 0.6f;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.75f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.8f, 3.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.65f, 1.15f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new Color(1.0f, 1.0f, 1.0f, 0.68f);
        main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.06f);
        main.maxParticles = 40;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = false;
        emission.rateOverTime = new ParticleSystem.MinMaxCurve(0f);
        emission.SetBursts(new ParticleSystem.Burst[]
        {
            new ParticleSystem.Burst(0.0f, 10)
        });

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.40f;
        shape.radiusThickness = 1.0f;

        ParticleSystem.VelocityOverLifetimeModule vel = ps.velocityOverLifetime;
        vel.enabled = false;

        ParticleSystem.LimitVelocityOverLifetimeModule limitVel = ps.limitVelocityOverLifetime;
        limitVel.enabled = true;
        limitVel.limit = new ParticleSystem.MinMaxCurve(0.45f);
        limitVel.dampen = 0.25f;

        ParticleSystem.RotationOverLifetimeModule rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-1.0f, 1.0f);

        ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
        sol.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve(
            new Keyframe(0.0f, 0.55f),
            new Keyframe(0.4f, 1.10f),
            new Keyframe(1.0f, 1.55f)
        );
        sol.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(BuildSoftWhiteSmokeGradient(0.68f));

        Configure2x2SmokeTextureSheet(ps);

        ParticleSystemRenderer psr = ps.GetComponent<ParticleSystemRenderer>();
        psr.renderMode = ParticleSystemRenderMode.Billboard;
        psr.sharedMaterial = smokeMat;
        psr.shadowCastingMode = ShadowCastingMode.Off;
        psr.receiveShadows = false;
        psr.sortingOrder = 6;
    }

    // ================================================================================================
    // กฎเหล็กข้อที่ 2: เปิดใช้งาน Texture Sheet Animation (2x2 Whole Sheet Random)
    // ================================================================================================
    private static void Configure2x2SmokeTextureSheet(ParticleSystem ps)
    {
        ParticleSystem.TextureSheetAnimationModule tsa = ps.textureSheetAnimation;
        tsa.enabled = true;
        tsa.mode = ParticleSystemAnimationMode.Grid;
        tsa.numTilesX = 2;
        tsa.numTilesY = 2;
        tsa.animation = ParticleSystemAnimationType.WholeSheet;
        tsa.timeMode = ParticleSystemAnimationTimeMode.Lifetime;

        // สุ่มเลือก 1 ใน 4 รูปทรงก้อนเมฆควัน (Constant Random Between Two Constants 0 ถึง 1)
        ParticleSystem.MinMaxCurve randomFrame = new ParticleSystem.MinMaxCurve(0f, 1f);
        randomFrame.mode = ParticleSystemCurveMode.TwoConstants;
        tsa.frameOverTime = randomFrame;
        tsa.startFrame = new ParticleSystem.MinMaxCurve(0f);
        tsa.cycleCount = 1;
    }

    private static Gradient BuildSoftWhiteSmokeGradient(float peakAlpha)
    {
        float clamped = Mathf.Clamp01(peakAlpha);
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(1.0f, 1.0f, 1.0f), 0.0f),
                new GradientColorKey(new Color(0.95f, 0.97f, 1.0f), 0.5f),
                new GradientColorKey(new Color(0.90f, 0.93f, 0.98f), 1.0f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0.0f, 0.0f),
                new GradientAlphaKey(clamped, 0.16f),
                new GradientAlphaKey(clamped * 0.62f, 0.55f),
                new GradientAlphaKey(0.0f, 1.0f)
            }
        );
        return grad;
    }

    // ================================================================================================
    // กฎเหล็กข้อที่ 2: วาด Texture ก้อนเมฆควัน 2x2 Flipbook Sprite Sheet (T_SoftWhiteSmoke_2x2.png ขนาด 512x512 px)
    // แบ่งเป็น 4 ช่อง (ช่องละ 256x256 px) โดยใช้ Perlin Noise หลายความถี่ (Multi-Octave FBM) ที่ตั้ง Seed ต่างกันทั้ง 4 ก้อน
    // ================================================================================================
    private static Texture2D GenerateSoftWhiteSmoke2x2Atlas(string assetPath)
    {
        int totalSize = 512;
        int cellSize = 256;
        Texture2D tex = new Texture2D(totalSize, totalSize, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[totalSize * totalSize];

        Vector2[] cellSeeds = new Vector2[]
        {
            new Vector2(12.34f, 45.67f),
            new Vector2(78.91f, 23.45f),
            new Vector2(142.7f, 89.12f),
            new Vector2(215.4f, 167.8f)
        };

        for (int cy = 0; cy < 2; cy++)
        {
            for (int cx = 0; cx < 2; cx++)
            {
                int cellIndex = cy * 2 + cx;
                Vector2 seed = cellSeeds[cellIndex];

                for (int ly = 0; ly < cellSize; ly++)
                {
                    float ny = ((ly / (float)(cellSize - 1)) * 2f) - 1f;
                    for (int lx = 0; lx < cellSize; lx++)
                    {
                        float nx = ((lx / (float)(cellSize - 1)) * 2f) - 1f;

                        // คำนวณระยะห่างจากจุดศูนย์กลางช่อง พร้อมบิดรูปทรงด้วย Low-Frequency Noise ให้ขอบหยักเหมือนปุยเมฆจริง
                        float angle = Mathf.Atan2(ny, nx);
                        float r = Mathf.Sqrt(nx * nx + ny * ny);

                        float warp = Mathf.PerlinNoise(seed.x + nx * 1.8f, seed.y + ny * 1.8f) * 0.28f;
                        float radialMask = 1f - Mathf.SmoothStep(0.18f, 0.88f, r - warp * 0.45f);
                        // บังคับขอบช่องให้เป็น 0 สนิทเพื่อไม่ให้เห็นรอยต่อสี่เหลี่ยมของตาราง 2x2
                        float hardBorderGuard = 1f - Mathf.SmoothStep(0.78f, 0.96f, r);

                        // Multi-Octave Perlin Noise (FBM 3 ความถี่) สร้างมวลความหนาแน่นภายในก้อนเมฆควัน
                        float n1 = Mathf.PerlinNoise(seed.x + nx * 2.2f, seed.y + ny * 2.2f);
                        float n2 = Mathf.PerlinNoise(seed.x * 1.7f + nx * 4.5f, seed.y * 1.7f + ny * 4.5f) * 0.5f;
                        float n3 = Mathf.PerlinNoise(seed.x * 2.3f + nx * 9.0f, seed.y * 2.3f + ny * 9.0f) * 0.25f;
                        float fbm = (n1 + n2 + n3) / 1.75f;

                        // เพิ่มความสว่างนุ่มตรงแกนกลางก้อนควันเหมือนแสงส่องทะลุไอน้ำ
                        float billowCore = Mathf.SmoothStep(0.22f, 0.85f, fbm);
                        float alpha = Mathf.Clamp01(radialMask * hardBorderGuard * (0.35f + billowCore * 0.85f));

                        // ไล่โทนสีขาวอมเทาเงินอ่อนๆ ที่ขอบเพื่อให้ก้อนควันดูมีมิติ 3D (Volumetric Feel)
                        float shade = Mathf.Lerp(0.88f, 1.0f, billowCore);

                        int gx = cx * cellSize + lx;
                        int gy = cy * cellSize + ly;
                        pixels[gy * totalSize + gx] = new Color(shade, shade, 1.0f, alpha);
                    }
                }
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        File.WriteAllBytes(assetPath, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        ConfigureTextureImporter(assetPath);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
    }

    private static Texture2D GenerateRollWindStreakTexture(string assetPath)
    {
        int width = 256;
        int height = 64;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[width * height];

        for (int y = 0; y < height; y++)
        {
            float ny = ((y / (float)(height - 1)) * 2f) - 1f;
            float outerSoft = Mathf.Exp(-ny * ny * 4.5f);
            float innerCore = Mathf.Exp(-ny * ny * 18.0f) * 0.85f;

            for (int x = 0; x < width; x++)
            {
                float nx = x / (float)(width - 1);
                float headFade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(nx / 0.16f));
                float tailFade = Mathf.Pow(Mathf.Clamp01((1f - nx) / 0.84f), 1.3f);

                float alpha = Mathf.Clamp01((outerSoft + innerCore) * headFade * tailFade);
                pixels[y * width + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        File.WriteAllBytes(assetPath, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        ConfigureTextureImporter(assetPath);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
    }

    private static void ConfigureTextureImporter(string assetPath)
    {
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
    }

    // ================================================================================================
    // กฎเหล็กข้อที่ 3: ใช้ Shader.Find("Sprites/Default") เป็นหลัก (สำรองด้วย Universal Render Pipeline/Particles/Unlit)
    // เพื่อรับประกันว่าควันขาวนุ่มฟุ้งและแสดงผลคมชัด 100% ใน Unity 6 URP ไร้ปัญหาสีชมพูหรือล่องหน
    // ================================================================================================
    private static Material CreateGuaranteedURPMaterial(string matPath, Texture2D tex, Color tintColor)
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        }
        if (shader == null)
        {
            shader = Shader.Find("Particles/Standard Unlit");
        }

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
