using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Editor Generator สำหรับสร้างระบบเอฟเฟกต์ "มอนสเตอร์โจมตีตามแอนิเมชัน 4 เลเยอร์ระดับ AAA (Monster Anim Attack VFX)"
/// กดคลิกเดียวจากเมนู:
/// VFX Mentor/20. สร้างเอฟเฟกต์มอนสเตอร์โจมตีตามแอนิเมชัน 4 เลเยอร์ (Monster Anim Attack VFX)
/// หรือ yeen VFX/16. [ใส่บน Monster] สร้างเอฟเฟกต์มอนสเตอร์โจมตีตามแอนิเมชัน 4 เลเยอร์ (Monster Anim Attack)
/// จะสร้างไฟล์ทั้งหมดลงใน Assets/VFX_MonsterAnimAttack อัตโนมัติ:
/// - Mesh_BeastClawArc.asset (3D Mesh คลื่นกรงเล็บโค้งจันทร์เสี้ยว 36 segments, มุมโค้ง 150 องศา พร้อม Vertex Color เรียวแหลมหัว-ท้าย)
/// - T_BeastClawArc.png (256x128 px: รอยคลื่นกรงเล็บขนาน 3 เส้น)
/// - T_BeastTelegraphFlare.png (256x256 px: แสงแฟลชเตือนอันตรายทรงกากบาทแฉกแสงแนวนอน-แนวตั้ง)
/// - T_BeastSlamRing.png (256x256 px: วงแหวนคลื่นกระแทกพื้นขอบคม)
/// - T_BeastFeralSpark.png (128x128 px: ประกายไฟขอบนุ่มนวล)
/// - URP Additive HDR Materials (Surface = Transparent, Blend = Additive, Cull = Off)
/// พร้อมค้นหากระดูกมือขวาของมอนสเตอร์อัตโนมัติ (HumanBodyBones.RightHand หรือชื่อ Bone ที่มี RightHand/R_Hand/Hand_R/Claw)
/// และติดตั้งครบทั้ง 4 เลเยอร์ลงบนมอนสเตอร์ที่เลือกใน Hierarchy ทันที (หรือสร้างหุ่นมอนสเตอร์ทดสอบให้หากไม่ได้เลือกวัตถุใด)
/// </summary>
public static class VFXMonsterAnimAttackGenerator
{
    private const string OutputFolder = "Assets/VFX_MonsterAnimAttack";

    private const string ClawArcMeshPath = OutputFolder + "/Mesh_BeastClawArc.asset";

    private const string ClawArcTexPath = OutputFolder + "/T_BeastClawArc.png";
    private const string TelegraphTexPath = OutputFolder + "/T_BeastTelegraphFlare.png";
    private const string SlamRingTexPath = OutputFolder + "/T_BeastSlamRing.png";
    private const string FeralSparkTexPath = OutputFolder + "/T_BeastFeralSpark.png";

    private const string MatTelegraphPath = OutputFolder + "/Mat_BeastTelegraph_Add.mat";
    private const string MatSlashRedPath = OutputFolder + "/Mat_BeastClawSlash_Add.mat";
    private const string MatSlamGoldPath = OutputFolder + "/Mat_BeastHeavySlam_Add.mat";
    private const string MatSparksPath = OutputFolder + "/Mat_BeastFeralSparks_Add.mat";
    private const string MatGroundRingPath = OutputFolder + "/Mat_BeastSlamRing_Add.mat";

    [MenuItem("VFX Mentor/20. สร้างเอฟเฟกต์มอนสเตอร์โจมตีตามแอนิเมชัน 4 เลเยอร์ (Monster Anim Attack VFX)")]
    [MenuItem("yeen VFX/16. [ใส่บน Monster] สร้างเอฟเฟกต์มอนสเตอร์โจมตีตามแอนิเมชัน 4 เลเยอร์ (Monster Anim Attack)")]
    public static void GenerateMonsterAnimAttackVFX()
    {
        EnsureFolderHierarchy(OutputFolder);

        // 1. สร้างและบันทึก 3D Mesh คลื่นกรงเล็บโค้งจันทร์เสี้ยว (36 segments, 150 องศา)
        Mesh clawArcMesh = CreateAndSaveBeastClawArcMesh(ClawArcMeshPath);

        // 2. วาดและบันทึกไฟล์ Texture 4 ใบด้วยสมการคณิตศาสตร์
        Texture2D clawTex = SaveTexturePng(GenerateBeastClawArcTexture(256, 128), ClawArcTexPath);
        Texture2D telegraphTex = SaveTexturePng(GenerateTelegraphFlareTexture(256), TelegraphTexPath);
        Texture2D slamRingTex = SaveTexturePng(GenerateSlamRingTexture(256), SlamRingTexPath);
        Texture2D sparkTex = SaveTexturePng(GenerateFeralSparkTexture(128), FeralSparkTexPath);

        // 3. สร้าง Materials แบบ URP Additive HDR (ขับค่า HDR Intensity ผ่าน _BaseColor เพื่อ Bloom จริงใน URP)
        Material matTelegraph = CreateOrUpdateAdditiveMaterial(MatTelegraphPath, telegraphTex, 5.0f);
        Material matSlashRed = CreateOrUpdateAdditiveMaterial(MatSlashRedPath, clawTex, 4.2f);
        Material matSlamGold = CreateOrUpdateAdditiveMaterial(MatSlamGoldPath, clawTex, 4.8f);
        Material matSparks = CreateOrUpdateAdditiveMaterial(MatSparksPath, sparkTex, 4.2f);
        Material matGroundRing = CreateOrUpdateAdditiveMaterial(MatGroundRingPath, slamRingTex, 3.8f);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // 4. ติดตั้งระบบ 4 เลเยอร์ลงบนตัวมอนสเตอร์ที่เลือกใน Hierarchy (หรือสร้างหุ่นมอนสเตอร์ทดสอบให้)
        GameObject targetMonster = ResolveOrCreateTargetMonster();
        Install4LayerSystemOnMonster(
            targetMonster,
            clawArcMesh,
            matTelegraph,
            matSlashRed,
            matSlamGold,
            matSparks,
            matGroundRing);

        Selection.activeGameObject = targetMonster;
        EditorGUIUtility.PingObject(clawArcMesh);
        if (SceneView.lastActiveSceneView != null)
        {
            SceneView.lastActiveSceneView.FrameSelected();
        }

        Debug.Log($"[VFXMonsterAnimAttackGenerator] ติดตั้งระบบมอนสเตอร์โจมตีตามแอนิเมชัน 4 เลเยอร์บน '{targetMonster.name}' สำเร็จแล้ว (ไฟล์ทั้งหมดอยู่ที่ {OutputFolder})");
    }

    // =========================================================================
    // INSTALL 4-LAYER SYSTEM & AUTO RIGHT-HAND BONE ATTACHMENT
    // =========================================================================

    private static GameObject ResolveOrCreateTargetMonster()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected != null && !EditorUtility.IsPersistent(selected))
        {
            return selected;
        }

        GameObject dummy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        dummy.name = "Monster_AnimAttackVFX_Tester";
        dummy.transform.position = new Vector3(0f, 1.0f, 0f);
        Undo.RegisterCreatedObjectUndo(dummy, "Create Monster Anim Attack VFX Tester");

        // สร้างกระดูกมือขวาจำลองให้เห็นการทำงานของระบบผูกกระดูกทันที
        GameObject rightArmBone = new GameObject("RightHand_Claw");
        rightArmBone.transform.SetParent(dummy.transform, false);
        rightArmBone.transform.localPosition = new Vector3(0.65f, 0.35f, 0.55f);

        Debug.Log("[VFXMonsterAnimAttackGenerator] ไม่ได้เลือกมอนสเตอร์ใน Hierarchy จึงสร้าง 'Monster_AnimAttackVFX_Tester' พร้อมกระดูก RightHand_Claw ให้ทดสอบทันที");
        return dummy;
    }

    private static void Install4LayerSystemOnMonster(
        GameObject monster,
        Mesh clawArcMesh,
        Material matTelegraph,
        Material matSlashRed,
        Material matSlamGold,
        Material matSparks,
        Material matGroundRing)
    {
        // ลบโหนดเก่าหากเคยติดตั้งไว้เพื่อป้องกันซ้ำซ้อน
        Transform oldRoot = monster.transform.Find("VFX_MonsterAnimAttack_Root");
        if (oldRoot != null)
        {
            Undo.DestroyObjectImmediate(oldRoot.gameObject);
        }

        Animator anim = monster.GetComponent<Animator>();
        if (anim == null) anim = monster.GetComponentInChildren<Animator>(true);

        Transform rightHandOrHeadBone = MMOMonsterAnimAttackVFX.FindMonsterRightHandOrHeadBone(monster.transform, anim);
        if (rightHandOrHeadBone != null)
        {
            Transform oldTelegraphOnBone = rightHandOrHeadBone.Find("Pivot_TelegraphCharge");
            if (oldTelegraphOnBone != null)
            {
                Undo.DestroyObjectImmediate(oldTelegraphOnBone.gameObject);
            }
        }

        CapsuleCollider rootCap = monster.GetComponent<CapsuleCollider>();
        bool pivotAtCenter = rootCap != null && Mathf.Abs(rootCap.center.y) < 0.25f;
        float chestY = pivotAtCenter ? 0.25f : 1.25f;
        float groundY = pivotAtCenter ? -0.92f : 0.08f;

        // โหนดแม่ของระบบเอฟเฟกต์มอนสเตอร์
        GameObject vfxRoot = new GameObject("VFX_MonsterAnimAttack_Root");
        Undo.RegisterCreatedObjectUndo(vfxRoot, "Create VFX_MonsterAnimAttack_Root");
        vfxRoot.transform.SetParent(monster.transform, false);
        vfxRoot.transform.localPosition = Vector3.zero;
        vfxRoot.transform.localRotation = Quaternion.identity;

        // Layer 1: Telegraph Charge Flash ผูกติดกระดูกมือขวาหรือหัวของมอนสเตอร์
        GameObject telegraphPivot = new GameObject("Pivot_TelegraphCharge");
        Undo.RegisterCreatedObjectUndo(telegraphPivot, "Create Pivot_TelegraphCharge");
        if (rightHandOrHeadBone != null)
        {
            telegraphPivot.transform.SetParent(rightHandOrHeadBone, false);
            telegraphPivot.transform.localPosition = Vector3.zero;
            Debug.Log($"[VFXMonsterAnimAttackGenerator] ผูก Pivot_TelegraphCharge เข้ากับกระดูก '{rightHandOrHeadBone.name}' อัตโนมัติแล้ว");
        }
        else
        {
            telegraphPivot.transform.SetParent(vfxRoot.transform, false);
            telegraphPivot.transform.localPosition = new Vector3(0.50f, chestY + 0.25f, 0.60f);
        }
        ParticleSystem psL1 = BuildLayer1TelegraphChargeFlash(telegraphPivot.transform, matTelegraph);

        // จุดศูนย์กลางการเหวี่ยงกรงเล็บ 3 ทิศทาง + สะเก็ดไฟ
        GameObject swingOrigin = new GameObject("Pivot_ClawSwingOrigin");
        swingOrigin.transform.SetParent(vfxRoot.transform, false);
        swingOrigin.transform.localPosition = new Vector3(0f, chestY, 0.55f);
        swingOrigin.transform.localRotation = Quaternion.identity;

        // Layer 2A: ตวัดแขนขวาเฉียงลงซ้าย (-38 องศา, Scale = 1.75, Spin Y = -580 deg/s, HDR 4.2x)
        ParticleSystem psL2A = BuildLayer2BeastClawArc(
            swingOrigin.transform, "L2A_Main_SlashRight", -38f, 1.75f, -580f, clawArcMesh, matSlashRed, isHeavyGold: false);

        // Layer 2B: ตวัดแขนซ้ายเฉียงลงขวา (142 องศา, Scale = 1.75, Spin Y = +580 deg/s, HDR 4.2x)
        ParticleSystem psL2B = BuildLayer2BeastClawArc(
            swingOrigin.transform, "L2B_Main_SlashLeft", 142f, 1.75f, 580f, clawArcMesh, matSlashRed, isHeavyGold: false);

        // Layer 2C: ฟันทุบผ่าตรงกลางขนาดใหญ่ (-90 องศา, Scale = 2.25, Spin Y = -640 deg/s, สีส้มทอง HDR 4.8x)
        ParticleSystem psL2C = BuildLayer2BeastClawArc(
            swingOrigin.transform, "L2C_Main_HeavySlamSlash", -90f, 2.25f, -640f, clawArcMesh, matSlamGold, isHeavyGold: true);

        // Layer 3: สะเก็ดพลังสีแดงเพลิงพุ่งกระจาย (Stretched Billboard, World, Vel 0.055, Len 4.0, Cone 38, Burst 22)
        ParticleSystem psL3 = BuildLayer3FeralSparks(swingOrigin.transform, matSparks);

        // Layer 4: คลื่นกระแทกพื้นขนาดใหญ่ด้านหน้ามอนสเตอร์ (Horizontal Billboard, Start Size = 3.8, Burst 2 วง)
        GameObject slamImpactPivot = new GameObject("Pivot_GroundSlamImpact");
        slamImpactPivot.transform.SetParent(vfxRoot.transform, false);
        slamImpactPivot.transform.localPosition = new Vector3(0f, groundY, 1.70f);
        slamImpactPivot.transform.localRotation = Quaternion.identity;
        ParticleSystem psL4 = BuildLayer4GroundSlamShockwave(slamImpactPivot.transform, matGroundRing);

        // ติดตั้งและผูกคอมโพเนนต์ MMOMonsterAnimAttackVFX
        MMOMonsterAnimAttackVFX controller = monster.GetComponent<MMOMonsterAnimAttackVFX>();
        if (controller == null)
        {
            controller = Undo.AddComponent<MMOMonsterAnimAttackVFX>(monster);
        }

        Undo.RecordObject(controller, "Assign Monster Anim Attack VFX Layers");
        controller.targetAnimator = anim;
        controller.telegraphPivot = telegraphPivot.transform;
        controller.layer1TelegraphFlash = psL1;
        controller.layer2ASlashRight = psL2A;
        controller.layer2BSlashLeft = psL2B;
        controller.layer2CHeavySlamSlash = psL2C;
        controller.layer3FeralSparks = psL3;
        controller.layer4GroundSlamShockwave = psL4;
        controller.enableAutoAnimSync = true;
        controller.enableTestHotkey = true;
        controller.showDebugHUD = true;

        EditorUtility.SetDirty(controller);
        EditorUtility.SetDirty(monster);
    }

    // =========================================================================
    // LAYER 1 - LAYER 4 PARTICLE BUILDERS
    // =========================================================================

    private static ParticleSystem BuildLayer1TelegraphChargeFlash(Transform parent, Material mat)
    {
        ParticleSystem ps = CreateParticleLayer(parent, "L1_TelegraphChargeFlash");

        var main = ps.main;
        main.duration = 0.35f;
        main.loop = false;
        main.playOnAwake = false;
        main.startDelay = 0.00f;
        main.startLifetime = 0.32f;
        main.startSpeed = 0f;
        main.startSize = 1.45f;
        main.startColor = Color.white; // HDR 5.0x ขับผ่าน Mat_BeastTelegraph_Add._BaseColor
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
            new Keyframe(0.00f, 0.12f, 6.8f, 6.8f),
            new Keyframe(0.26f, 1.08f, 0.3f, 0.3f),
            new Keyframe(0.70f, 0.85f, -0.6f, -0.6f),
            new Keyframe(1.00f, 0.00f, -2.5f, -2.5f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1.00f, 0.92f, 0.80f), 0.00f),
                new GradientColorKey(new Color(1.00f, 0.14f, 0.08f), 0.32f),
                new GradientColorKey(new Color(0.85f, 0.04f, 0.04f), 1.00f)
            },
            new[]
            {
                new GradientAlphaKey(0.00f, 0.00f),
                new GradientAlphaKey(1.00f, 0.12f),
                new GradientAlphaKey(0.90f, 0.60f),
                new GradientAlphaKey(0.00f, 1.00f)
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sortingOrder = 14;
        return ps;
    }

    private static ParticleSystem BuildLayer2BeastClawArc(
        Transform parent,
        string layerName,
        float rollZDeg,
        float scale,
        float spinYDegPerSec,
        Mesh clawArcMesh,
        Material mat,
        bool isHeavyGold)
    {
        ParticleSystem ps = CreateParticleLayer(parent, layerName);
        ps.transform.localPosition = Vector3.zero;
        ps.transform.localRotation = Quaternion.Euler(0f, 0f, rollZDeg);
        ps.transform.localScale = Vector3.one;

        var main = ps.main;
        main.duration = 0.28f;
        main.loop = false;
        main.playOnAwake = false;
        main.startDelay = 0.00f;
        main.startLifetime = 0.25f;
        main.startSpeed = 0f;
        main.startSize = scale;
        main.startColor = Color.white; // HDR 4.2x / 4.8x ขับผ่าน Material._BaseColor
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 4;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0.00f, 1) });

        var shape = ps.shape;
        shape.enabled = false;

        // Spin Y ด้วย Constant Mode ครบทั้ง 3 แกน (ปลอดภัยสำหรับ Unity 6)
        var rol = ps.rotationOverLifetime;
        rol.enabled = true;
        rol.separateAxes = true;
        rol.x = new ParticleSystem.MinMaxCurve(0f);
        rol.y = new ParticleSystem.MinMaxCurve(spinYDegPerSec * Mathf.Deg2Rad);
        rol.z = new ParticleSystem.MinMaxCurve(0f);

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1.0f, new AnimationCurve(
            new Keyframe(0.00f, 0.72f, 2.6f, 2.6f),
            new Keyframe(0.28f, 1.06f, 0.3f, 0.3f),
            new Keyframe(1.00f, 1.18f, 0.1f, 0.1f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        if (isHeavyGold)
        {
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1.00f, 0.96f, 0.72f), 0.00f),
                    new GradientColorKey(new Color(1.00f, 0.56f, 0.08f), 0.38f),
                    new GradientColorKey(new Color(0.95f, 0.16f, 0.02f), 1.00f)
                },
                new[]
                {
                    new GradientAlphaKey(0.00f, 0.00f),
                    new GradientAlphaKey(1.00f, 0.10f),
                    new GradientAlphaKey(0.88f, 0.52f),
                    new GradientAlphaKey(0.00f, 1.00f)
                });
        }
        else
        {
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1.00f, 0.84f, 0.68f), 0.00f),
                    new GradientColorKey(new Color(1.00f, 0.18f, 0.08f), 0.35f),
                    new GradientColorKey(new Color(0.75f, 0.02f, 0.04f), 1.00f)
                },
                new[]
                {
                    new GradientAlphaKey(0.00f, 0.00f),
                    new GradientAlphaKey(1.00f, 0.10f),
                    new GradientAlphaKey(0.86f, 0.52f),
                    new GradientAlphaKey(0.00f, 1.00f)
                });
        }
        col.color = new ParticleSystem.MinMaxGradient(grad);

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = clawArcMesh;
        renderer.alignment = ParticleSystemRenderSpace.Local;
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sortingOrder = 12;
        return ps;
    }

    private static ParticleSystem BuildLayer3FeralSparks(Transform parent, Material mat)
    {
        ParticleSystem ps = CreateParticleLayer(parent, "L3_FeralCrimsonSparks");

        var main = ps.main;
        main.duration = 0.42f;
        main.loop = false;
        main.playOnAwake = false;
        main.startDelay = 0.02f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.36f); // 0.02s -> 0.38s
        main.startSpeed = new ParticleSystem.MinMaxCurve(7.0f, 14.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.15f);
        main.startColor = Color.white; // HDR 4.2x ขับผ่าน Mat_BeastFeralSparks_Add._BaseColor
        main.gravityModifier = 0.65f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 36;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0.00f, 22) });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 38f;
        shape.radius = 0.32f;
        shape.radiusThickness = 1.0f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1.00f, 0.90f, 0.55f), 0.00f),
                new GradientColorKey(new Color(1.00f, 0.24f, 0.06f), 0.42f),
                new GradientColorKey(new Color(0.75f, 0.02f, 0.02f), 1.00f)
            },
            new[]
            {
                new GradientAlphaKey(1.00f, 0.00f),
                new GradientAlphaKey(0.88f, 0.48f),
                new GradientAlphaKey(0.00f, 1.00f)
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1.0f, new AnimationCurve(
            new Keyframe(0.00f, 1.00f),
            new Keyframe(1.00f, 0.16f)));

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.055f;
        renderer.lengthScale = 4.0f;
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sortingOrder = 13;
        return ps;
    }

    private static ParticleSystem BuildLayer4GroundSlamShockwave(Transform parent, Material mat)
    {
        ParticleSystem ps = CreateParticleLayer(parent, "L4_HeavyGroundSlamShockwave");

        var main = ps.main;
        main.duration = 0.48f;
        main.loop = false;
        main.playOnAwake = false;
        main.startDelay = 0.04f;
        main.startLifetime = 0.38f; // 0.04s -> 0.42s
        main.startSpeed = 0f;
        main.startSize = 3.8f;
        main.startColor = Color.white; // HDR 3.8x ขับผ่าน Mat_BeastSlamRing_Add._BaseColor
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 6;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[]
        {
            new ParticleSystem.Burst(0.00f, 1),
            new ParticleSystem.Burst(0.07f, 1)
        });

        var shape = ps.shape;
        shape.enabled = false;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1.0f, new AnimationCurve(
            new Keyframe(0.00f, 0.14f, 4.2f, 4.2f),
            new Keyframe(0.40f, 0.88f, 0.9f, 0.9f),
            new Keyframe(1.00f, 1.24f, 0.1f, 0.1f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1.00f, 0.86f, 0.45f), 0.00f),
                new GradientColorKey(new Color(1.00f, 0.32f, 0.06f), 0.42f),
                new GradientColorKey(new Color(0.80f, 0.06f, 0.02f), 1.00f)
            },
            new[]
            {
                new GradientAlphaKey(0.95f, 0.00f),
                new GradientAlphaKey(0.80f, 0.36f),
                new GradientAlphaKey(0.00f, 1.00f)
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sortingOrder = 9;
        return ps;
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
    // PROCEDURAL 3D MESH (Mesh_BeastClawArc.asset: 36 segments, 150 องศา)
    // =========================================================================

    private static Mesh CreateAndSaveBeastClawArcMesh(string assetPath)
    {
        Mesh generated = MMOMonsterAnimAttackVFX.BuildClawArcMeshRuntime(36, 150f);
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);

        if (existing != null)
        {
            existing.Clear();
            existing.name = "Mesh_BeastClawArc";
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

        generated.name = "Mesh_BeastClawArc";
        AssetDatabase.CreateAsset(generated, assetPath);
        return AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
    }

    // =========================================================================
    // PROCEDURAL TEXTURES (4 ใบ: ClawArc 256x128, Telegraph 256x256, SlamRing 256x256, Spark 128x128)
    // =========================================================================

    /// <summary>
    /// T_BeastClawArc.png (256x128 px): รอยคลื่นกรงเล็บขนาน 3 เส้น (เส้นนอกหลัก + รอยข่วนรอง 2 เส้นด้านใน)
    /// </summary>
    private static Texture2D GenerateBeastClawArcTexture(int width, int height)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[width * height];

        for (int y = 0; y < height; y++)
        {
            float v = y / (float)(height - 1);
            for (int x = 0; x < width; x++)
            {
                float u = x / (float)(width - 1);

                // บีบปลายหัวและหางให้เรียวแหลมคมกริบ
                float headMask = SmoothStep(0.00f, 0.14f, u);
                float tailMask = 1f - SmoothStep(0.72f, 1.00f, u);
                float lengthMask = headMask * tailMask;

                float noiseWarp = (Mathf.PerlinNoise(u * 9.5f + 2.3f, v * 5.0f + 7.1f) - 0.5f) * 0.032f;
                float vv = v + noiseWarp;

                // เส้นนอกหลักหนาชัด + รอยข่วนรอง 2 เส้นขนานด้านใน
                float mainOuterClaw = Mathf.Exp(-Mathf.Pow((vv - 0.84f) / 0.080f, 2f)) * 1.15f;
                float midClaw = Mathf.Exp(-Mathf.Pow((vv - 0.52f) / 0.052f, 2f)) * 0.85f;
                float innerClaw = Mathf.Exp(-Mathf.Pow((vv - 0.23f) / 0.044f, 2f)) * 0.68f;
                float subtleSheet = Mathf.Exp(-Mathf.Pow((vv - 0.60f) / 0.28f, 2f)) * 0.22f;

                float val = Mathf.Clamp01((mainOuterClaw + midClaw + innerClaw + subtleSheet) * lengthMask);
                pixels[y * width + x] = new Color(val, val, val, val);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    /// <summary>
    /// T_BeastTelegraphFlare.png (256x256 px): แสงแฟลชเตือนอันตรายทรงกากบาทแฉกแสงแนวนอน-แนวตั้งสว่างจ้า
    /// </summary>
    private static Texture2D GenerateTelegraphFlareTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[size * size];
        float inv = 1f / (size - 1);

        for (int y = 0; y < size; y++)
        {
            float ny = y * inv * 2f - 1f;
            for (int x = 0; x < size; x++)
            {
                float nx = x * inv * 2f - 1f;
                float r = Mathf.Sqrt(nx * nx + ny * ny);
                float edgeMask = 1f - SmoothStep(0.78f, 0.98f, r);

                float brightCore = Mathf.Exp(-r * r * 18.0f);
                float softHalo = Mathf.Exp(-r * r * 4.5f) * 0.42f;
                float horizontalSpike = Mathf.Exp(-Mathf.Abs(ny) * 22.0f) * Mathf.Exp(-Mathf.Abs(nx) * 1.6f) * 1.05f;
                float verticalSpike = Mathf.Exp(-Mathf.Abs(nx) * 22.0f) * Mathf.Exp(-Mathf.Abs(ny) * 2.0f) * 0.88f;
                float diagGlint = Mathf.Exp(-Mathf.Abs(nx - ny) * 26.0f) * Mathf.Exp(-r * 4.5f) * 0.32f
                                + Mathf.Exp(-Mathf.Abs(nx + ny) * 26.0f) * Mathf.Exp(-r * 4.5f) * 0.32f;

                float val = Mathf.Clamp01((brightCore + softHalo + horizontalSpike + verticalSpike + diagGlint) * edgeMask);
                pixels[y * size + x] = new Color(val, val, val, val);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    /// <summary>
    /// T_BeastSlamRing.png (256x256 px): วงแหวนคลื่นกระแทกพื้นขอบคม
    /// </summary>
    private static Texture2D GenerateSlamRingTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[size * size];
        float inv = 1f / (size - 1);

        for (int y = 0; y < size; y++)
        {
            float ny = y * inv * 2f - 1f;
            for (int x = 0; x < size; x++)
            {
                float nx = x * inv * 2f - 1f;
                float r = Mathf.Sqrt(nx * nx + ny * ny);
                if (r >= 0.98f)
                {
                    pixels[y * size + x] = Color.clear;
                    continue;
                }

                float angle = Mathf.Atan2(ny, nx);
                float outerCut = 1f - SmoothStep(0.84f, 0.95f, r);
                float innerTrail = SmoothStep(0.54f, 0.81f, r);
                float crispRim = Mathf.Exp(-Mathf.Pow((r - 0.83f) / 0.040f, 2f));
                float shockJag = 0.82f + 0.18f * Mathf.PerlinNoise(Mathf.Cos(angle) * 7.5f + 5.0f, Mathf.Sin(angle) * 7.5f + 5.0f);

                float val = Mathf.Clamp01((innerTrail * outerCut * 0.68f + crispRim * 0.98f) * shockJag * outerCut);
                pixels[y * size + x] = new Color(val, val, val, val);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    /// <summary>
    /// T_BeastFeralSpark.png (128x128 px): ประกายไฟขอบนุ่มนวล
    /// </summary>
    private static Texture2D GenerateFeralSparkTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[size * size];
        float inv = 1f / (size - 1);

        for (int y = 0; y < size; y++)
        {
            float ny = y * inv * 2f - 1f;
            for (int x = 0; x < size; x++)
            {
                float nx = x * inv * 2f - 1f;
                float invR = Mathf.Clamp01(1f - Mathf.Sqrt(nx * nx + ny * ny));
                float val = Mathf.Clamp01(Mathf.Pow(invR, 1.9f) * 0.72f + Mathf.Pow(invR, 4.6f) * 0.48f);
                pixels[y * size + x] = new Color(val, val, val, val);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    // =========================================================================
    // ASSET SAVING & URP ADDITIVE HDR MATERIAL HELPERS
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
        if (isNew) mat = new Material(shader);
        else mat.shader = shader;

        Color hdrCol = new Color(hdrIntensity, hdrIntensity, hdrIntensity, 1f);
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", mainTex);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", mainTex);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", hdrCol);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", hdrCol);

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
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.renderQueue = (int)RenderQueue.Transparent;

        if (isNew) AssetDatabase.CreateAsset(mat, assetPath);
        else EditorUtility.SetDirty(mat);

        return AssetDatabase.LoadAssetAtPath<Material>(assetPath);
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
        float t = Mathf.Clamp01((x - edge0) / Mathf.Max(0.0001f, edge1 - edge0));
        return t * t * (3f - 2f * t);
    }
}
