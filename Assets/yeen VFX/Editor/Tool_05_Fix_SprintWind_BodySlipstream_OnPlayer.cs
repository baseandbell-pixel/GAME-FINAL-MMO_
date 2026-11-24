#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class Tool_05_Fix_SprintWind_BodySlipstream_OnPlayer : EditorWindow
{
    private const string GeneratedFolder = "Assets/yeen VFX/03_VFX_Prefabs_And_Assets/Generated_Sprint";
    private const string SlipstreamTexPath = "Assets/yeen VFX/03_VFX_Prefabs_And_Assets/Generated_Sprint/TEX_Sprint_BodyWindSlipstream.png";
    private const string SonicRingTexPath = "Assets/yeen VFX/03_VFX_Prefabs_And_Assets/Generated_Sprint/TEX_Sprint_SonicRing.png";
    private const string FootRibbonTexPath = "Assets/yeen VFX/03_VFX_Prefabs_And_Assets/Generated_Sprint/TEX_Sprint_FootWindRibbon.png";

    private const string SlipstreamMatPath = "Assets/yeen VFX/03_VFX_Prefabs_And_Assets/Generated_Sprint/MAT_Sprint_BodyWindSlipstream.mat";
    private const string SonicRingMatPath = "Assets/yeen VFX/03_VFX_Prefabs_And_Assets/Generated_Sprint/MAT_Sprint_SonicRing.mat";
    private const string FootRibbonMatPath = "Assets/yeen VFX/03_VFX_Prefabs_And_Assets/Generated_Sprint/MAT_Sprint_FootWindRibbon.mat";
    private const string FootPuffMatPath = "Assets/yeen VFX/03_VFX_Prefabs_And_Assets/Generated_Sprint/MAT_Sprint_FootWindPuff.mat";

    [MenuItem("yeen VFX/05. [ใส่บน Player] ซ่อมและอัปเกรดเอฟเฟกต์วิ่งลมผ่านตัว (Run Slipstream)")]
    public static void UpgradeAndSyncRunWindVFX()
    {
        EnsureDirectoriesExist();

        // 1. วาดและบันทึกไฟล์ Texture ระดับ AAA (สีขาวบริสุทธิ์ Pure White 100% ไร้ขอบสี่เหลี่ยม)
        Texture2D slipstreamTex = GenerateBodyWindSlipstreamTexture(SlipstreamTexPath);
        Texture2D sonicRingTex = GenerateSonicRingTexture(SonicRingTexPath);
        Texture2D footRibbonTex = GenerateFootRibbonTexture(FootRibbonTexPath);

        // 2. สร้าง Materials สำหรับ URP Particles/Unlit (Additive / Transparent)
        Material slipstreamMat = CreateOrUpdateWindMaterial(SlipstreamMatPath, slipstreamTex, new Color(1f, 1f, 1f, 0.85f), true);
        Material sonicRingMat = CreateOrUpdateWindMaterial(SonicRingMatPath, sonicRingTex, new Color(1f, 1f, 1f, 0.9f), true);
        Material footRibbonMat = CreateOrUpdateWindMaterial(FootRibbonMatPath, footRibbonTex, new Color(1f, 1f, 1f, 0.8f), true);
        Material footPuffMat = CreateOrUpdateWindMaterial(FootPuffMatPath, slipstreamTex, new Color(1f, 1f, 1f, 0.65f), false);

        int upgradedCount = 0;
        List<string> upgradedTargets = new List<string>();

        // 3. ค้นหาและอัปเกรดตัวละครที่ผู้ใช้คลิกเลือกใน Hierarchy หรือค้นหาใน Scene ปัจจุบันทั้งหมด
        GameObject selectedObj = Selection.activeGameObject;
        GameObject sceneRootTarget = ResolveCharacterRoot(selectedObj);

        if (sceneRootTarget != null)
        {
            if (UpgradeCharacterRunWindHierarchy(sceneRootTarget, sonicRingMat, slipstreamMat, footRibbonMat, footPuffMat))
            {
                upgradedCount++;
                upgradedTargets.Add("Scene Object: " + sceneRootTarget.name);
            }
        }
        else
        {
            // ค้นหาทุกตัวละครใน Scene ที่มีกระดูก Bip001 L Foot หรือมี SimpleSprintTrail
            SimpleSprintTrail[] existingTrails = Object.FindObjectsByType<SimpleSprintTrail>(FindObjectsInactive.Include);
            for (int i = 0; i < existingTrails.Length; i++)
            {
                if (existingTrails[i] != null && existingTrails[i].gameObject.scene.IsValid())
                {
                    if (UpgradeCharacterRunWindHierarchy(existingTrails[i].gameObject, sonicRingMat, slipstreamMat, footRibbonMat, footPuffMat))
                    {
                        upgradedCount++;
                        upgradedTargets.Add("Scene Object: " + existingTrails[i].gameObject.name);
                    }
                }
            }
        }

        // 4. อัปเกรดลงใน Player Prefabs หลักของโปรเจกต์โดยอัตโนมัติ (GreatSwordGirl.prefab และ Player.prefab)
        string[] targetPrefabPaths = new string[]
        {
            "Assets/Organ/Models/Prefab/GreatSwordPrefab/GreatSwordGirl.prefab",
            "Assets/_Project/Prefabs/Player.prefab"
        };

        for (int i = 0; i < targetPrefabPaths.Length; i++)
        {
            string prefabPath = targetPrefabPaths[i];
            if (File.Exists(prefabPath))
            {
                GameObject prefabContents = PrefabUtility.LoadPrefabContents(prefabPath);
                if (prefabContents != null)
                {
                    bool modified = UpgradeCharacterRunWindHierarchy(prefabContents, sonicRingMat, slipstreamMat, footRibbonMat, footPuffMat);
                    if (modified)
                    {
                        PrefabUtility.SaveAsPrefabAsset(prefabContents, prefabPath);
                        upgradedCount++;
                        upgradedTargets.Add("Prefab: " + Path.GetFileName(prefabPath));
                    }
                    PrefabUtility.UnloadPrefabContents(prefabContents);
                }
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (Application.isPlaying == false && UnityEngine.SceneManagement.SceneManager.GetActiveScene().IsValid())
        {
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }

        string targetSummary = upgradedTargets.Count > 0
            ? string.Join("\n• ", upgradedTargets.ToArray())
            : "ไม่พบโมเดลตัวละครใน Scene (กรุณาคลิกเลือกตัวละครใน Hierarchy แล้วกดเมนูนี้ซ้ำอีกครั้ง)";

        EditorUtility.DisplayDialog(
            "อัปเกรดระบบสายลมวิ่ง 4 ชั้น (Sync Run & Shift) สำเร็จ!",
            "ดำเนินการสร้างและซ่อมแซมระบบ Wuxia Multi-Trigger Sprint Wind & Body Slipstream VFX ครบ 4 ชั้นเรียบร้อยแล้ว (" + upgradedCount + " เป้าหมาย):\n\n" +
            "• " + targetSummary + "\n\n" +
            "สิ่งที่ถูกอัปเกรด:\n" +
            "1. ซ่อมบั๊ก Unity 6 Velocity Curve Mode (Linear/Orbital/Radial = Constant ครบทุกแกน X,Y,Z)\n" +
            "2. สร้าง VFX_RunStart_SonicRing (วงแหวนอัดอากาศรอบเอวจังหวะพุ่งตัว)\n" +
            "3. สร้าง VFX_BodyWind_Slipstream (สายลมสีขาวแหวกอากาศผ่านใบหน้า ไหล่ และลำตัว)\n" +
            "4. อัปเกรด Sprint Wind ที่กระดูก Bip001 L Foot และ Bip001 R Foot\n" +
            "5. ผูกสคริปต์ SimpleSprintTrail (ตรวจจับ 5 ช่องทาง + กด Shift + ปุ่มลัด F1 + หน้าต่าง Debug มุมซ้ายบน)",
            "รับทราบ (พร้อมกด Play ทดสอบ)"
        );
    }

    private static GameObject ResolveCharacterRoot(GameObject candidate)
    {
        if (candidate == null) return null;

        SimpleSprintTrail trail = candidate.GetComponentInParent<SimpleSprintTrail>();
        if (trail != null) return trail.gameObject;

        Animator anim = candidate.GetComponentInParent<Animator>();
        if (anim != null) return anim.gameObject;

        Transform lFoot = FindDeepChild(candidate.transform, "Bip001 L Foot");
        if (lFoot != null) return candidate;

        return null;
    }

    private static bool UpgradeCharacterRunWindHierarchy(
        GameObject characterRoot,
        Material sonicRingMat,
        Material slipstreamMat,
        Material footRibbonMat,
        Material footPuffMat)
    {
        if (characterRoot == null) return false;

        Undo.RegisterFullObjectHierarchyUndo(characterRoot, "Upgrade Wuxia Sprint Wind VFX");

        // 1. ติดตั้งหรือดึง SimpleSprintTrail บน Root ของตัวละคร
        SimpleSprintTrail sprintScript = characterRoot.GetComponent<SimpleSprintTrail>();
        if (sprintScript == null)
        {
            sprintScript = characterRoot.AddComponent<SimpleSprintTrail>();
        }

        // ตั้งค่าพารามิเตอร์เริ่มต้นให้ตรงกับความเร็วของโปรเจกต์ (Walk = 3.0, Run = 5.0 -> Threshold = 3.2)
        sprintScript.detectShiftKey = true;
        sprintScript.triggerOnShiftImmediately = false;
        sprintScript.detectAnimatorParameters = true;
        sprintScript.detectAnimatorStateName = true;
        sprintScript.detectTransformVelocity = true;
        sprintScript.minRunSpeedThreshold = 3.2f;
        sprintScript.animatorFloatRunThreshold = 3.2f;
        sprintScript.normalizedBlendRunThreshold = 0.75f;
        sprintScript.stopGraceDuration = 0.15f;
        sprintScript.showDebugOverlay = false;

        // 2. สร้าง/อัปเกรด Layer 1: VFX_RunStart_SonicRing (วงแหวนคลื่นอัดอากาศรอบเอวจังหวะพุ่งตัวออกวิ่ง)
        Transform sonicRingTr = FindDeepChild(characterRoot.transform, "VFX_RunStart_SonicRing");
        if (sonicRingTr == null)
        {
            GameObject ringObj = new GameObject("VFX_RunStart_SonicRing");
            ringObj.transform.SetParent(characterRoot.transform, false);
            sonicRingTr = ringObj.transform;
        }
        sonicRingTr.localPosition = new Vector3(0f, 0.95f, 0.15f);
        sonicRingTr.localRotation = Quaternion.identity;
        sonicRingTr.localScale = Vector3.one;

        ParticleSystem sonicRingPS = GetOrAddParticleSystem(sonicRingTr.gameObject);
        ConfigureSonicRingParticle(sonicRingPS, sonicRingMat);

        // 3. สร้าง/อัปเกรด Layer 2: VFX_BodyWind_Slipstream (สายลมสีขาวแหวกอากาศผ่านใบหน้า ไหล่ และลำตัว)
        Transform slipstreamTr = FindDeepChild(characterRoot.transform, "VFX_BodyWind_Slipstream");
        if (slipstreamTr == null)
        {
            GameObject slipObj = new GameObject("VFX_BodyWind_Slipstream");
            slipObj.transform.SetParent(characterRoot.transform, false);
            slipstreamTr = slipObj.transform;
        }
        // วางไว้ด้านหน้าหน้าอกตัวละคร (Y = 1.05m, Z = +0.55m) และหันหัวกรวยย้อนเข้าหาลำตัว (Rotation Y = 180)
        // เพื่อให้เส้นลมพุ่งสวนจากด้านหน้าแหวกออกข้างแก้ม ไหล่ และสีข้างไปด้านหลังอย่างเป็นธรรมชาติ
        slipstreamTr.localPosition = new Vector3(0f, 1.05f, 0.55f);
        slipstreamTr.localRotation = Quaternion.Euler(0f, 180f, 0f);
        slipstreamTr.localScale = Vector3.one;

        ParticleSystem slipstreamPS = GetOrAddParticleSystem(slipstreamTr.gameObject);
        ConfigureBodySlipstreamParticle(slipstreamPS, slipstreamMat);

        // 4. สร้าง/อัปเกรด Layer 3 & Layer 4 ที่กระดูกเท้าซ้ายและขวา (Bip001 L Foot และ Bip001 R Foot)
        Transform leftFootBone = FindDeepChild(characterRoot.transform, "Bip001 L Foot");
        Transform rightFootBone = FindDeepChild(characterRoot.transform, "Bip001 R Foot");

        List<TrailRenderer> footTrails = new List<TrailRenderer>();
        List<ParticleSystem> footParticles = new List<ParticleSystem>();

        if (leftFootBone != null)
        {
            UpgradeFootSprintWindNode(leftFootBone, footRibbonMat, footPuffMat, footTrails, footParticles);
        }

        if (rightFootBone != null)
        {
            UpgradeFootSprintWindNode(rightFootBone, footRibbonMat, footPuffMat, footTrails, footParticles);
        }

        // หากโมเดลไม่มีกระดูกชื่อ Bip001 L/R Foot ให้ค้นหา Sprint Wind ที่มีอยู่เดิมใน Hierarchy
        if (leftFootBone == null && rightFootBone == null)
        {
            Transform existingSprintWind = FindDeepChild(characterRoot.transform, "Sprint Wind");
            if (existingSprintWind != null && existingSprintWind.parent != null)
            {
                UpgradeFootSprintWindNode(existingSprintWind.parent, footRibbonMat, footPuffMat, footTrails, footParticles);
            }
        }

        // 5. ผูก References ทั้งหมดเข้าสู่ SimpleSprintTrail Inspector โดยอัตโนมัติ
        sprintScript.runStartSonicRingParticle = sonicRingPS;
        sprintScript.bodySlipstreamParticle = slipstreamPS;
        sprintScript.footWindTrails = footTrails.ToArray();
        sprintScript.footWindParticles = footParticles.ToArray();

        EditorUtility.SetDirty(sprintScript);
        EditorUtility.SetDirty(characterRoot);
        return true;
    }

    private static void UpgradeFootSprintWindNode(
        Transform footBone,
        Material ribbonMat,
        Material puffMat,
        List<TrailRenderer> outTrails,
        List<ParticleSystem> outParticles)
    {
        Transform sprintWindTr = footBone.Find("Sprint Wind");
        if (sprintWindTr == null)
        {
            GameObject windObj = new GameObject("Sprint Wind");
            windObj.transform.SetParent(footBone, false);
            sprintWindTr = windObj.transform;
        }

        sprintWindTr.localPosition = Vector3.zero;
        sprintWindTr.localRotation = Quaternion.identity;
        sprintWindTr.localScale = Vector3.one;

        // Layer 3: เส้นริบบิ้นสายลมสีขาวติดข้อเท้า (TrailRenderer)
        TrailRenderer trail = sprintWindTr.GetComponent<TrailRenderer>();
        if (trail == null)
        {
            trail = sprintWindTr.gameObject.AddComponent<TrailRenderer>();
        }
        ConfigureFootTrailRenderer(trail, ribbonMat);
        outTrails.Add(trail);

        // Layer 4: ละอองลมสีขาวฟุ้งกระจายใต้ฝ่าเท้าตามจังหวะก้าววิ่ง (ParticleSystem)
        ParticleSystem footPS = GetOrAddParticleSystem(sprintWindTr.gameObject);
        ConfigureFootPuffParticle(footPS, puffMat);
        outParticles.Add(footPS);
    }

    // กฎเหล็กข้อที่ 3: บังคับรีเซ็ตแกนความเร็วทุกแกน (X, Y, Z) ของ velocityOverLifetime ให้เป็นโหมด Constant เหมือนกัน 100%
    // ป้องกัน Error "Particle Velocity curves must all be in the same mode" ใน Unity 6 อย่างเด็ดขาด
    private static void EnforceUniformVelocityCurveModes(ParticleSystem ps, float linX, float linY, float linZ, bool enableVelocityModule)
    {
        ParticleSystem.VelocityOverLifetimeModule vel = ps.velocityOverLifetime;
        vel.enabled = enableVelocityModule;
        vel.space = ParticleSystemSimulationSpace.Local;

        ParticleSystem.MinMaxCurve curveX = new ParticleSystem.MinMaxCurve(linX);
        curveX.mode = ParticleSystemCurveMode.Constant;
        ParticleSystem.MinMaxCurve curveY = new ParticleSystem.MinMaxCurve(linY);
        curveY.mode = ParticleSystemCurveMode.Constant;
        ParticleSystem.MinMaxCurve curveZ = new ParticleSystem.MinMaxCurve(linZ);
        curveZ.mode = ParticleSystemCurveMode.Constant;

        vel.x = curveX;
        vel.y = curveY;
        vel.z = curveZ;

        ParticleSystem.MinMaxCurve zeroConstant = new ParticleSystem.MinMaxCurve(0f);
        zeroConstant.mode = ParticleSystemCurveMode.Constant;

        vel.orbitalX = zeroConstant;
        vel.orbitalY = zeroConstant;
        vel.orbitalZ = zeroConstant;
        vel.orbitalOffsetX = zeroConstant;
        vel.orbitalOffsetY = zeroConstant;
        vel.orbitalOffsetZ = zeroConstant;
        vel.radial = zeroConstant;

        ParticleSystem.MinMaxCurve unitConstant = new ParticleSystem.MinMaxCurve(1f);
        unitConstant.mode = ParticleSystemCurveMode.Constant;
        vel.speedModifier = unitConstant;
    }

    private static void ConfigureSonicRingParticle(ParticleSystem ps, Material mat)
    {
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.duration = 0.4f;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.28f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f);
        main.startSize = new ParticleSystem.MinMaxCurve(1.1f);
        main.startColor = new Color(1f, 1f, 1f, 0.85f);
        main.gravityModifier = 0f;
        main.maxParticles = 8;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = false;
        emission.rateOverTime = 0f;
        emission.rateOverDistance = 0f;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;

        EnforceUniformVelocityCurveModes(ps, 0f, 0f, -0.8f, true);

        ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
        sol.enabled = true;
        AnimationCurve ringScaleCurve = new AnimationCurve(
            new Keyframe(0f, 0.35f, 0f, 3.2f),
            new Keyframe(0.5f, 1.35f, 1.2f, 1.2f),
            new Keyframe(1f, 1.85f, 0f, 0f)
        );
        sol.size = new ParticleSystem.MinMaxCurve(1f, ringScaleCurve);

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.85f, 0.15f),
                new GradientAlphaKey(0.35f, 0.55f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        col.color = new ParticleSystem.MinMaxGradient(grad);

        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    private static void ConfigureBodySlipstreamParticle(ParticleSystem ps, Material mat)
    {
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.duration = 1.0f;
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.28f, 0.42f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(7.5f, 11.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.11f, 0.22f);
        main.startColor = new Color(1f, 1f, 1f, 0.95f);
        main.gravityModifier = 0f;
        main.maxParticles = 180;

        // ปิดหัวฉีด Emission ไว้เป็นค่าเริ่มต้น (สคริปต์ SimpleSprintTrail จะสลับ emission.enabled = true ทันทีที่กด Shift วิ่ง)
        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = false;
        emission.rateOverTime = new ParticleSystem.MinMaxCurve(85f);
        emission.rateOverDistance = new ParticleSystem.MinMaxCurve(0f);

        // กฎเหล็กข้อที่ 2 (Layer 2): ใช้รูปทรงกรวยกลวง (Cone Shape, Radius Thickness = 0.85)
        // เพื่อให้เส้นลมลอยโอบรอบแก้ม ไหล่ และสีข้างลำตัว ไม่พุ่งทะลุกลางหน้าอก
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 14f;
        shape.radius = 0.42f;
        shape.radiusThickness = 0.85f;
        shape.length = 1.2f;

        EnforceUniformVelocityCurveModes(ps, 0f, 0.15f, 1.5f, true);

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.95f, 0.15f),
                new GradientAlphaKey(0.85f, 0.65f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        col.color = new ParticleSystem.MinMaxGradient(grad);

        ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
        sol.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve(
            new Keyframe(0f, 0.5f),
            new Keyframe(0.25f, 1f),
            new Keyframe(1f, 0.25f)
        );
        sol.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        // ตั้งค่า Renderer เป็น Stretched Billboard ให้เส้นลมยืดยาวโฉบเฉี่ยวตามความเร็วลม
        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.cameraVelocityScale = 0f;
        renderer.velocityScale = 0.14f;
        renderer.lengthScale = 7.2f;
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    private static void ConfigureFootTrailRenderer(TrailRenderer trail, Material mat)
    {
        trail.time = 0.34f;
        trail.minVertexDistance = 0.02f;
        trail.autodestruct = false;
        trail.emitting = false;
        trail.numCornerVertices = 4;
        trail.numCapVertices = 4;
        trail.alignment = LineAlignment.View;
        trail.textureMode = LineTextureMode.Stretch;
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.receiveShadows = false;
        trail.sharedMaterial = mat;

        AnimationCurve widthCurve = new AnimationCurve(
            new Keyframe(0f, 0.28f),
            new Keyframe(0.35f, 0.22f),
            new Keyframe(1f, 0f)
        );
        trail.widthMultiplier = 1f;
        trail.widthCurve = widthCurve;

        Gradient trailGrad = new Gradient();
        trailGrad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.95f, 0.12f),
                new GradientAlphaKey(0.80f, 0.65f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        trail.colorGradient = trailGrad;
    }

    private static void ConfigureFootPuffParticle(ParticleSystem ps, Material mat)
    {
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.duration = 1.0f;
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.26f, 0.40f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.20f);
        main.startColor = new Color(1f, 1f, 1f, 0.90f);
        main.gravityModifier = -0.05f;
        main.maxParticles = 100;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = false;
        emission.rateOverTime = new ParticleSystem.MinMaxCurve(25f);
        emission.rateOverDistance = new ParticleSystem.MinMaxCurve(10f);

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.08f;

        EnforceUniformVelocityCurveModes(ps, 0f, 0.25f, 0f, true);

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.65f, 0.2f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        col.color = new ParticleSystem.MinMaxGradient(grad);

        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.cameraVelocityScale = 0f;
        renderer.velocityScale = 0.08f;
        renderer.lengthScale = 2.8f;
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    private static ParticleSystem GetOrAddParticleSystem(GameObject obj)
    {
        ParticleSystem ps = obj.GetComponent<ParticleSystem>();
        if (ps == null)
        {
            ps = obj.AddComponent<ParticleSystem>();
        }
        return ps;
    }

    private static void EnsureDirectoriesExist()
    {
        if (!Directory.Exists(GeneratedFolder))
        {
            Directory.CreateDirectory(GeneratedFolder);
        }
    }

    private static Texture2D GenerateBodyWindSlipstreamTexture(string assetPath)
    {
        int width = 256;
        int height = 64;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;

        Color[] pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            float ny = (y / (float)(height - 1)) * 2f - 1f;
            float verticalFalloff = Mathf.Exp(-ny * ny * 7.5f);

            for (int x = 0; x < width; x++)
            {
                float nx = x / (float)(width - 1);
                // หัวเรียวแหลม หางจางหายแบบ Aerodynamic Silk
                float headFade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(nx / 0.22f));
                float tailFade = Mathf.Pow(Mathf.Clamp01((1f - nx) / 0.78f), 1.6f);
                float coreRibbon = Mathf.Exp(-ny * ny * 24f) * 0.45f;

                float alpha = Mathf.Clamp01((verticalFalloff + coreRibbon) * headFade * tailFade);
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

    private static Texture2D GenerateSonicRingTexture(string assetPath)
    {
        int size = 256;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;

        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            float dy = (y / (float)(size - 1)) * 2f - 1f;
            for (int x = 0; x < size; x++)
            {
                float dx = (x / (float)(size - 1)) * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);

                // วงแหวนคลื่นอากาศบางเฉียบขอบฟุ้งนุ่ม
                float ringDist = Mathf.Abs(r - 0.68f);
                float ringAlpha = Mathf.Exp(-(ringDist * ringDist) / (2f * 0.055f * 0.055f));
                float outerMask = 1f - Mathf.SmoothStep(0.82f, 0.98f, r);
                float alpha = Mathf.Clamp01(ringAlpha * outerMask);

                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        File.WriteAllBytes(assetPath, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        ConfigureTextureImporter(assetPath);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
    }

    private static Texture2D GenerateFootRibbonTexture(string assetPath)
    {
        int width = 256;
        int height = 64;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;

        Color[] pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            float ny = (y / (float)(height - 1)) * 2f - 1f;
            float dualStrand = Mathf.Exp(-Mathf.Pow(Mathf.Abs(ny) - 0.22f, 2f) * 18f);
            float centerGlow = Mathf.Exp(-ny * ny * 6f) * 0.65f;

            for (int x = 0; x < width; x++)
            {
                float nx = x / (float)(width - 1);
                float lengthFade = Mathf.Sin(nx * Mathf.PI);
                float alpha = Mathf.Clamp01((dualStrand * 0.55f + centerGlow) * lengthFade);
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

    private static Material CreateOrUpdateWindMaterial(string matPath, Texture2D tex, Color tintColor, bool additive)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
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

        mat.SetTexture("_BaseMap", tex);
        mat.SetColor("_BaseColor", tintColor);

        // ตั้งค่า URP Transparent + Additive / Alpha Blend เพื่อไม่ให้เกิดกรอบสี่เหลี่ยมทึบ
        mat.SetFloat("_Surface", 1f); // 1 = Transparent
        mat.SetFloat("_Blend", additive ? 2f : 0f); // 2 = Additive, 0 = Alpha
        mat.SetFloat("_Cull", 0f); // Render Both Sides
        mat.SetFloat("_ZWrite", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

        if (additive)
        {
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        }
        else
        {
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        }

        mat.renderQueue = 3000;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static Transform FindDeepChild(Transform parent, string targetName)
    {
        if (parent == null) return null;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name == targetName)
            {
                return child;
            }

            Transform found = FindDeepChild(child, targetName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }
}
#endif
