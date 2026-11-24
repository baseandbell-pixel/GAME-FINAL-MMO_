using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Editor Generator สำหรับสร้างระบบ "บล็อคค้างและตีบล็อคแตก 4 เลเยอร์ระดับ AAA (ใช้ได้ทั้ง Player และ Monster)"
/// กดคลิกเดียวจากเมนู:
/// VFX Mentor/19. สร้างระบบบล็อคและตีบล็อคแตก 4 เลเยอร์ (ใช้ได้ทั้ง Player และ Monster)
/// หรือ yeen VFX/15. [Player & Monster] สร้างระบบบล็อคและตีบล็อคแตก 4 เลเยอร์ (Universal Guard Break)
/// จะสร้างไฟล์ทั้งหมดลงใน Assets/VFX_UniversalGuardBreak อัตโนมัติ:
/// - Mesh_UniversalShieldDome.asset (โมเดล 3D โล่โค้งทรงโดมโปร่งแสงด้านหน้าตัวละครตอนบล็อคค้าง)
/// - Mesh_UniversalShard3D.asset (โมเดล 3D เศษผลึกโล่ทรงสามเหลี่ยม 2 ด้านมีสันนูนตรงกลาง 5 จุดยอด 6 หน้า)
/// - T_ShieldHexBarrier.png (512x512 ลายตารางโล่รังผึ้งพร้อมขอบเรืองแสง)
/// - T_ShieldCrackWeb.png (512x512 ลายรอยร้าวใยแมงมุม 11 แฉกพร้อมรอยหยัก Perlin Noise และวงเชื่อม 2 ชั้น)
/// - T_ShardCoreGlow.png (128x128 แสงเรืองขอบนุ่มสำหรับเศษผลึก 3 มิติและประกายไฟ)
/// - T_BreakShockwave.png (256x256 วงแหวนคลื่นกระแทกขอบคม)
/// - Material URP Particles/Unlit Additive HDR (Surface = Transparent, Blend = Additive, Cull = Off)
/// - VFX_UniversalGuardBreak.prefab (โครงสร้าง 4 เลเยอร์ครบสมบูรณ์)
/// พร้อมติดตั้งทั้ง VFX_ActiveGuardShield และ MMOUniversalGuardSystem ให้วัตถุที่เลือกอัตโนมัติ
/// (หากไม่ได้เลือกวัตถุใดเลย จะสร้างทั้งหุ่น Player และหุ่น Monster ที่กำลังยกโล่บล็อคค้างให้ทดสอบทันที)
/// </summary>
public static class VFXUniversalGuardBreakGenerator
{
    private const string OutputFolder = "Assets/VFX_UniversalGuardBreak";

    private const string ShieldDomeMeshPath = OutputFolder + "/Mesh_UniversalShieldDome.asset";
    private const string ShardMeshPath = OutputFolder + "/Mesh_UniversalShard3D.asset";

    private const string HexBarrierTexPath = OutputFolder + "/T_ShieldHexBarrier.png";
    private const string CrackWebTexPath = OutputFolder + "/T_ShieldCrackWeb.png";
    private const string ShardGlowTexPath = OutputFolder + "/T_ShardCoreGlow.png";
    private const string ShockwaveTexPath = OutputFolder + "/T_BreakShockwave.png";

    private const string MatShieldBarrierPath = OutputFolder + "/Mat_UniversalShieldBarrier_Add.mat";
    private const string MatCrackWebPath = OutputFolder + "/Mat_UniversalCrackWeb_Add.mat";
    private const string MatShard3DPath = OutputFolder + "/Mat_UniversalShard3D_Add.mat";
    private const string MatSparksPath = OutputFolder + "/Mat_UniversalShatterSparks_Add.mat";
    private const string MatShockwavePath = OutputFolder + "/Mat_UniversalBreakWave_Add.mat";

    private const string PrefabPath = OutputFolder + "/VFX_UniversalGuardBreak.prefab";

    [MenuItem("VFX Mentor/19. สร้างระบบบล็อคและตีบล็อคแตก 4 เลเยอร์ (ใช้ได้ทั้ง Player และ Monster)")]
    [MenuItem("yeen VFX/15. [Player & Monster] สร้างระบบบล็อคและตีบล็อคแตก 4 เลเยอร์ (Universal Guard Break)")]
    public static void GenerateUniversalGuardBreakSystem()
    {
        EnsureFolderHierarchy(OutputFolder);

        // 1. สร้างและบันทึก 3D Mesh 2 ชิ้น (โล่โค้งทรงโดม + เศษผลึก 3D มีสันนูน)
        Mesh shieldDomeMesh = CreateAndSaveShieldDomeMesh(ShieldDomeMeshPath);
        Mesh shardMesh = CreateAndSaveShard3DMesh(ShardMeshPath);

        // 2. วาดและบันทึกไฟล์ Texture 4 ใบด้วยสมการคณิตศาสตร์
        Texture2D hexTex = SaveTexturePng(GenerateShieldHexBarrierTexture(512), HexBarrierTexPath);
        Texture2D crackTex = SaveTexturePng(GenerateShieldCrackWebTexture(512), CrackWebTexPath);
        Texture2D glowTex = SaveTexturePng(GenerateShardCoreGlowTexture(128), ShardGlowTexPath);
        Texture2D waveTex = SaveTexturePng(GenerateBreakShockwaveTexture(256), ShockwaveTexPath);

        // 3. สร้าง Material URP Additive HDR (Surface = Transparent, Blend = Additive, Cull = Off)
        Material matShield = CreateOrUpdateAdditiveMaterial(MatShieldBarrierPath, hexTex, new Color(0.28f, 0.88f, 1.0f, 0.86f) * 2.2f);
        Material matCrack = CreateOrUpdateAdditiveMaterial(MatCrackWebPath, crackTex, new Color(4.8f, 4.8f, 4.8f, 1.0f));
        Material matShard = CreateOrUpdateAdditiveMaterial(MatShard3DPath, glowTex, new Color(3.2f, 3.2f, 3.2f, 1.0f));
        Material matSpark = CreateOrUpdateAdditiveMaterial(MatSparksPath, glowTex, new Color(4.5f, 4.5f, 4.5f, 1.0f));
        Material matWave = CreateOrUpdateAdditiveMaterial(MatShockwavePath, waveTex, new Color(3.8f, 3.8f, 3.8f, 1.0f));

        // 4. ประกอบ Prefab เอฟเฟกต์ตีบล็อคแตก 4 เลเยอร์และบันทึกเป็น Asset
        Mesh quadMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        GameObject root = new GameObject("VFX_UniversalGuardBreak");
        GameObject savedPrefab;

        try
        {
            BuildLayer1OverloadCrackWeb(root.transform, quadMesh, matCrack);
            BuildLayer2ShatteredShards3D(root.transform, shardMesh, matShard);
            BuildLayer3ShatterSparks(root.transform, matSpark);
            BuildLayer4ShockwaveRing(root.transform, quadMesh, matWave);

            savedPrefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // 5. ติดตั้งให้วัตถุที่เลือกใน Hierarchy อัตโนมัติ (หรือสร้างหุ่น Player + หุ่น Monster ทดสอบคู่กัน)
        InstallToSelectionOrSpawnTestDummies(shieldDomeMesh, matShield, savedPrefab);

        EditorGUIUtility.PingObject(savedPrefab);
        Debug.Log($"[VFXUniversalGuardBreakGenerator] สร้างระบบบล็อคและตีบล็อคแตก 4 เลเยอร์สำเร็จที่: {PrefabPath}");
    }

    // =========================================================================
    // 4-LAYER GUARD BREAK SHATTER PREFAB BUILDERS
    // =========================================================================

    /// <summary>
    /// Layer 1 (Anticipation / Overload Crack Web Flash - 0.00s - 0.24s)
    /// Mesh (Quad), Alignment = Local, Start Size = 2.4, HDR ทองสว่าง 4.8x ไล่เปลี่ยนเป็นสีฟ้าคราม
    /// </summary>
    private static void BuildLayer1OverloadCrackWeb(Transform parent, Mesh quadMesh, Material mat)
    {
        ParticleSystem ps = CreateParticleLayer(parent, "L1_OverloadCrackWeb");

        var main = ps.main;
        main.duration = 0.30f;
        main.loop = false;
        main.playOnAwake = true;
        main.startDelay = 0.00f;
        main.startLifetime = 0.24f;
        main.startSpeed = 0f;
        main.startSize = 2.4f;
        main.startColor = Color.white; // HDR Intensity 4.8x ขับผ่าน Mat_UniversalCrackWeb_Add._BaseColor
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 4;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0.00f, 1) });

        var shape = ps.shape;
        shape.enabled = false;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1.00f, 0.84f, 0.24f), 0.00f),
                new GradientColorKey(new Color(1.00f, 0.96f, 0.78f), 0.22f),
                new GradientColorKey(new Color(0.18f, 0.88f, 1.00f), 0.62f),
                new GradientColorKey(new Color(0.06f, 0.56f, 1.00f), 1.00f)
            },
            new[]
            {
                new GradientAlphaKey(0.00f, 0.00f),
                new GradientAlphaKey(1.00f, 0.09f),
                new GradientAlphaKey(1.00f, 0.50f),
                new GradientAlphaKey(0.00f, 1.00f)
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1.0f, new AnimationCurve(
            new Keyframe(0.00f, 0.60f, 3.8f, 3.8f),
            new Keyframe(0.18f, 1.00f, 0.45f, 0.45f),
            new Keyframe(1.00f, 1.18f, 0.12f, 0.12f)));

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = quadMesh;
        renderer.alignment = ParticleSystemRenderSpace.Local;
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sortingOrder = 10;
    }

    /// <summary>
    /// Layer 2 (Main Action / 3D Shattered Shield Shards - 0.02s - 0.95s)
    /// Mesh_UniversalShard3D.asset, Burst 26 ชิ้น, Simulation Space = World,
    /// 3D Start Rotation + Rotation over Lifetime -360 ถึง +360 องศาทุกแกน, Gravity Modifier = 1.65
    /// </summary>
    private static void BuildLayer2ShatteredShards3D(Transform parent, Mesh shardMesh, Material mat)
    {
        ParticleSystem ps = CreateParticleLayer(parent, "L2_ShatteredShieldShards3D");

        var main = ps.main;
        main.duration = 1.00f;
        main.loop = false;
        main.playOnAwake = true;
        main.startDelay = 0.02f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.68f, 0.93f); // 0.02s -> 0.95s
        main.startSpeed = new ParticleSystem.MinMaxCurve(4.6f, 10.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.24f, 0.52f);
        main.startColor = Color.white; // HDR Intensity 3.2x ขับผ่าน Mat_UniversalShard3D_Add._BaseColor
        main.gravityModifier = 1.65f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 40;

        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0.00f, 26) });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.78f;
        shape.radiusThickness = 1.0f;
        shape.arc = 360f;
        shape.randomDirectionAmount = 0.35f;

        var vol = ps.velocityOverLifetime;
        vol.enabled = true;
        vol.space = ParticleSystemSimulationSpace.Local;
        vol.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        vol.y = new ParticleSystem.MinMaxCurve(0.6f, 1.8f);
        vol.z = new ParticleSystem.MinMaxCurve(1.4f, 3.2f);

        var rol = ps.rotationOverLifetime;
        rol.enabled = true;
        rol.separateAxes = true;
        float rad360 = 360f * Mathf.Deg2Rad;
        rol.x = new ParticleSystem.MinMaxCurve(-rad360, rad360);
        rol.y = new ParticleSystem.MinMaxCurve(-rad360, rad360);
        rol.z = new ParticleSystem.MinMaxCurve(-rad360, rad360);

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.76f, 0.98f, 1.00f), 0.00f),
                new GradientColorKey(new Color(0.22f, 0.80f, 1.00f), 0.42f),
                new GradientColorKey(new Color(0.08f, 0.38f, 0.90f), 1.00f)
            },
            new[]
            {
                new GradientAlphaKey(1.00f, 0.00f),
                new GradientAlphaKey(1.00f, 0.68f),
                new GradientAlphaKey(0.00f, 1.00f)
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1.0f, new AnimationCurve(
            new Keyframe(0.00f, 1.00f),
            new Keyframe(0.66f, 0.92f),
            new Keyframe(1.00f, 0.14f)));

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = shardMesh;
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sortingOrder = 11;
    }

    /// <summary>
    /// Layer 3 (Secondary Elements / High-Velocity Shatter Sparks - 0.02s - 0.48s)
    /// Stretched Billboard, Simulation Space = World, Velocity Scale = 0.06, Length Scale = 3.8, Burst 24, สีส้มเพลิง HDR
    /// </summary>
    private static void BuildLayer3ShatterSparks(Transform parent, Material mat)
    {
        ParticleSystem ps = CreateParticleLayer(parent, "L3_ShatterSparks");

        var main = ps.main;
        main.duration = 0.55f;
        main.loop = false;
        main.playOnAwake = true;
        main.startDelay = 0.02f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.24f, 0.46f); // 0.02s -> 0.48s
        main.startSpeed = new ParticleSystem.MinMaxCurve(7.5f, 15.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.17f);
        main.startColor = Color.white; // HDR Intensity 4.5x ขับผ่าน Mat_UniversalShatterSparks_Add._BaseColor
        main.gravityModifier = 0.95f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 40;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0.00f, 24) });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 68f;
        shape.radius = 0.45f;
        shape.radiusThickness = 1.0f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1.00f, 0.94f, 0.62f), 0.00f),
                new GradientColorKey(new Color(1.00f, 0.45f, 0.06f), 0.34f),
                new GradientColorKey(new Color(0.92f, 0.15f, 0.02f), 1.00f)
            },
            new[]
            {
                new GradientAlphaKey(1.00f, 0.00f),
                new GradientAlphaKey(0.92f, 0.42f),
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
        renderer.velocityScale = 0.06f;
        renderer.lengthScale = 3.8f;
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sortingOrder = 12;
    }

    /// <summary>
    /// Layer 4 (Impact & Residual / Guard Break Shockwave Ring - 0.03s - 0.34s)
    /// Mesh (Quad), Alignment = Local, Start Size = 3.4, สีฟ้าคราม HDR intensity 3.8x
    /// </summary>
    private static void BuildLayer4ShockwaveRing(Transform parent, Mesh quadMesh, Material mat)
    {
        ParticleSystem ps = CreateParticleLayer(parent, "L4_GuardBreakShockwave");

        var main = ps.main;
        main.duration = 0.40f;
        main.loop = false;
        main.playOnAwake = true;
        main.startDelay = 0.03f;
        main.startLifetime = 0.31f; // 0.03s -> 0.34s
        main.startSpeed = 0f;
        main.startSize = 3.4f;
        main.startColor = Color.white; // HDR Intensity 3.8x ขับผ่าน Mat_UniversalBreakWave_Add._BaseColor
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 4;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0.00f, 1) });

        var shape = ps.shape;
        shape.enabled = false;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.68f, 0.98f, 1.00f), 0.00f),
                new GradientColorKey(new Color(0.16f, 0.78f, 1.00f), 0.36f),
                new GradientColorKey(new Color(0.06f, 0.42f, 0.96f), 1.00f)
            },
            new[]
            {
                new GradientAlphaKey(1.00f, 0.00f),
                new GradientAlphaKey(0.85f, 0.30f),
                new GradientAlphaKey(0.00f, 1.00f)
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1.0f, new AnimationCurve(
            new Keyframe(0.00f, 0.18f, 3.4f, 3.4f),
            new Keyframe(0.36f, 0.86f, 0.95f, 0.95f),
            new Keyframe(1.00f, 1.24f, 0.14f, 0.14f)));

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = quadMesh;
        renderer.alignment = ParticleSystemRenderSpace.Local;
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sortingOrder = 9;
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
    // PROCEDURAL 3D MESHES (2 ชิ้น: Shield Dome & 3D Crystal Shard)
    // =========================================================================

    private static Mesh CreateAndSaveShieldDomeMesh(string assetPath)
    {
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
        bool isNew = mesh == null;
        if (isNew) mesh = new Mesh { name = "Mesh_UniversalShieldDome" };
        else { mesh.Clear(); mesh.name = "Mesh_UniversalShieldDome"; }

        const int rings = 12;
        const int segs = 32;
        const float radius = 0.82f;
        const float bulgeDepth = 0.26f;

        Vector3[] vertices = new Vector3[1 + rings * segs];
        Vector2[] uvs = new Vector2[vertices.Length];
        int[] triangles = new int[segs * 3 + (rings - 1) * segs * 6];

        vertices[0] = new Vector3(0f, 0f, bulgeDepth);
        uvs[0] = new Vector2(0.5f, 0.5f);

        int vIdx = 1;
        for (int r = 1; r <= rings; r++)
        {
            float t = r / (float)rings;
            float z = Mathf.Cos(t * Mathf.PI * 0.5f) * bulgeDepth;
            float ringRad = t * radius;
            for (int s = 0; s < segs; s++)
            {
                float ang = (s / (float)segs) * Mathf.PI * 2f;
                float cx = Mathf.Cos(ang);
                float sy = Mathf.Sin(ang);
                vertices[vIdx] = new Vector3(cx * ringRad, sy * ringRad, z);
                uvs[vIdx] = new Vector2(0.5f + cx * t * 0.5f, 0.5f + sy * t * 0.5f);
                vIdx++;
            }
        }

        int tIdx = 0;
        for (int s = 0; s < segs; s++)
        {
            int next = (s + 1) % segs;
            triangles[tIdx++] = 0;
            triangles[tIdx++] = 1 + next;
            triangles[tIdx++] = 1 + s;
        }
        for (int r = 1; r < rings; r++)
        {
            int ringStart = 1 + (r - 1) * segs;
            int nextRingStart = 1 + r * segs;
            for (int s = 0; s < segs; s++)
            {
                int next = (s + 1) % segs;
                triangles[tIdx++] = ringStart + s;
                triangles[tIdx++] = ringStart + next;
                triangles[tIdx++] = nextRingStart + s;

                triangles[tIdx++] = ringStart + next;
                triangles[tIdx++] = nextRingStart + next;
                triangles[tIdx++] = nextRingStart + s;
            }
        }

        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        if (isNew) AssetDatabase.CreateAsset(mesh, assetPath);
        else EditorUtility.SetDirty(mesh);
        return AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
    }

    private static Mesh CreateAndSaveShard3DMesh(string assetPath)
    {
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
        bool isNew = mesh == null;
        if (isNew) mesh = new Mesh { name = "Mesh_UniversalShard3D" };
        else { mesh.Clear(); mesh.name = "Mesh_UniversalShard3D"; }

        // 5 จุดยอด: สามเหลี่ยมคมไม่สมมาตร 3 จุด + สันนูนหน้า/หลัง 2 จุด
        Vector3[] vertices =
        {
            new Vector3( 0.00f,  0.58f,  0.00f),
            new Vector3(-0.44f, -0.38f,  0.00f),
            new Vector3( 0.48f, -0.30f,  0.00f),
            new Vector3( 0.02f, -0.02f,  0.14f),
            new Vector3( 0.02f, -0.02f, -0.14f)
        };

        // 6 หน้าสามเหลี่ยมปิดทึบสมบูรณ์ 2 ด้าน
        int[] triangles =
        {
            0, 1, 3,
            1, 2, 3,
            2, 0, 3,
            0, 4, 1,
            1, 4, 2,
            2, 4, 0
        };

        Vector2[] uvs =
        {
            new Vector2(0.50f, 0.96f),
            new Vector2(0.08f, 0.10f),
            new Vector2(0.94f, 0.16f),
            new Vector2(0.50f, 0.50f),
            new Vector2(0.50f, 0.50f)
        };

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        if (isNew) AssetDatabase.CreateAsset(mesh, assetPath);
        else EditorUtility.SetDirty(mesh);
        return AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
    }

    // =========================================================================
    // PROCEDURAL TEXTURES (4 ใบ: HexBarrier, CrackWeb, ShardCoreGlow, BreakShockwave)
    // =========================================================================

    /// <summary>
    /// T_ShieldHexBarrier.png (512x512 px): ลายตารางโล่รังผึ้งพลังงานพร้อมวงขอบเรืองแสง
    /// </summary>
    private static Texture2D GenerateShieldHexBarrierTexture(int size)
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
                if (r >= 0.99f)
                {
                    pixels[y * size + x] = Color.clear;
                    continue;
                }

                float outerCut = 1f - SmoothStep(0.91f, 0.985f, r);
                float crispRim = Mathf.Exp(-Mathf.Pow((r - 0.89f) / 0.032f, 2f)) * 1.15f;
                float innerRim = Mathf.Exp(-Mathf.Pow((r - 0.78f) / 0.018f, 2f)) * 0.45f;

                // คำนวณระยะขอบรังผึ้งหกเหลี่ยมแท้ (Exact Axial Hex Distance)
                float hexEdge = EvaluateHexGridEdge(nx * 5.2f, ny * 5.2f);
                float centerGlow = Mathf.Exp(-r * r * 3.2f) * 0.26f;
                float baseGlass = 0.14f + 0.12f * r;

                float alpha = Mathf.Clamp01((baseGlass + centerGlow + hexEdge * 0.62f + crispRim + innerRim) * outerCut);
                float lum = Mathf.Clamp01(0.58f + hexEdge * 0.35f + crispRim * 0.45f);
                pixels[y * size + x] = new Color(lum, lum, lum, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    private static float EvaluateHexGridEdge(float px, float py)
    {
        const float sqrt3 = 1.7320508f;
        float sx = 1.0f;
        float sy = sqrt3;

        float ax = Mathf.Repeat(px, sx) - sx * 0.5f;
        float ay = Mathf.Repeat(py, sy) - sy * 0.5f;
        float bx = Mathf.Repeat(px + sx * 0.5f, sx) - sx * 0.5f;
        float by = Mathf.Repeat(py + sy * 0.5f, sy) - sy * 0.5f;

        float da = ax * ax + ay * ay;
        float db = bx * bx + by * by;
        float gx = da < db ? Mathf.Abs(ax) : Mathf.Abs(bx);
        float gy = da < db ? Mathf.Abs(ay) : Mathf.Abs(by);

        float hexDist = Mathf.Max(gx, gx * 0.5f + gy * (sqrt3 * 0.5f));
        return SmoothStep(0.41f, 0.49f, hexDist);
    }

    /// <summary>
    /// T_ShieldCrackWeb.png (512x512 px): ลายรอยร้าวใยแมงมุม 11 แฉกพร้อมรอยหยัก Perlin Noise และวงเชื่อม 2 ชั้น
    /// </summary>
    private static Texture2D GenerateShieldCrackWebTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        const int rayCount = 11;
        float[] rayAngles = new float[rayCount];

        for (int i = 0; i < rayCount; i++)
        {
            float baseAngle = (i / (float)rayCount) * Mathf.PI * 2f;
            float offset = (Mathf.PerlinNoise(i * 3.71f, 4.19f) - 0.5f) * 0.28f;
            rayAngles[i] = baseAngle + offset;
        }

        Color[] pixels = new Color[size * size];
        float inv = 1f / (size - 1);

        for (int y = 0; y < size; y++)
        {
            float ny = y * inv * 2f - 1f;
            for (int x = 0; x < size; x++)
            {
                float nx = x * inv * 2f - 1f;
                float r = Mathf.Sqrt(nx * nx + ny * ny);
                if (r >= 0.99f)
                {
                    pixels[y * size + x] = Color.clear;
                    continue;
                }

                float angle = Mathf.Atan2(ny, nx);
                if (angle < 0f) angle += Mathf.PI * 2f;
                float edgeMask = 1f - SmoothStep(0.76f, 0.98f, r);

                float macroJag = (Mathf.PerlinNoise(nx * 5.2f + 11.4f, ny * 5.2f + 3.7f) - 0.5f) * 0.24f;
                float microJag = (Mathf.PerlinNoise(nx * 14.5f + 2.1f, ny * 14.5f + 8.9f) - 0.5f) * 0.08f;

                float crackIntensity = 0f;
                for (int i = 0; i < rayCount; i++)
                {
                    float jaggedAngle = rayAngles[i] + macroJag * Mathf.Clamp01(r * 1.45f) + microJag;
                    float deltaRad = Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, jaggedAngle * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
                    float arcDist = deltaRad * Mathf.Max(r, 0.04f);

                    float lineThickness = Mathf.Lerp(0.026f, 0.008f, r);
                    float coreLine = Mathf.Exp(-(arcDist * arcDist) / (lineThickness * lineThickness));
                    float softHalo = Mathf.Exp(-(arcDist * arcDist) / ((lineThickness * 2.8f) * (lineThickness * 2.8f))) * 0.35f;
                    crackIntensity = Mathf.Max(crackIntensity, coreLine + softHalo);
                }

                float cCos = Mathf.Cos(angle);
                float cSin = Mathf.Sin(angle);
                float ringOffset1 = (Mathf.PerlinNoise(cCos * 3.1f + 5.0f, cSin * 3.1f + 5.0f) - 0.5f) * 0.10f;
                float ringOffset2 = (Mathf.PerlinNoise(cCos * 4.6f + 17.0f, cSin * 4.6f + 9.0f) - 0.5f) * 0.12f;

                float distRing1 = Mathf.Abs(r - (0.34f + ringOffset1));
                float distRing2 = Mathf.Abs(r - (0.64f + ringOffset2));
                float ring1 = Mathf.Exp(-(distRing1 * distRing1) / (0.015f * 0.015f)) * 0.92f;
                float ring2 = Mathf.Exp(-(distRing2 * distRing2) / (0.013f * 0.013f)) * 0.78f;

                float impactCore = Mathf.Exp(-(r * r) / (0.14f * 0.14f)) * 1.15f;
                float val = Mathf.Clamp01(Mathf.Max(Mathf.Max(crackIntensity, impactCore), Mathf.Max(ring1, ring2)) * edgeMask);
                pixels[y * size + x] = new Color(val, val, val, val);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    /// <summary>
    /// T_ShardCoreGlow.png (128x128 px): แสงเรืองขอบนุ่มสำหรับเคลือบเศษผลึก 3 มิติและประกายไฟ
    /// </summary>
    private static Texture2D GenerateShardCoreGlowTexture(int size)
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
                float val = Mathf.Clamp01(Mathf.Pow(invR, 1.85f) * 0.72f + Mathf.Pow(invR, 4.5f) * 0.48f);
                pixels[y * size + x] = new Color(val, val, val, val);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    /// <summary>
    /// T_BreakShockwave.png (256x256 px): วงแหวนคลื่นกระแทกขอบคม
    /// </summary>
    private static Texture2D GenerateBreakShockwaveTexture(int size)
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
                float outerCut = 1f - SmoothStep(0.84f, 0.94f, r);
                float innerTrail = SmoothStep(0.50f, 0.80f, r);
                float crispRim = Mathf.Exp(-Mathf.Pow((r - 0.82f) / 0.042f, 2f));
                float ripple = 0.82f + 0.18f * Mathf.PerlinNoise(Mathf.Cos(angle) * 6.0f + 4.0f, Mathf.Sin(angle) * 6.0f + 4.0f);

                float val = Mathf.Clamp01((innerTrail * outerCut * 0.72f + crispRim * 0.96f) * ripple * outerCut);
                pixels[y * size + x] = new Color(val, val, val, val);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    // =========================================================================
    // AUTO-INSTALLER (ติดตั้งให้ Player / Monster ที่เลือก หรือสร้างหุ่นทดสอบคู่กันทันที)
    // =========================================================================

    private static void InstallToSelectionOrSpawnTestDummies(Mesh shieldDomeMesh, Material matShield, GameObject shatterPrefab)
    {
        GameObject selected = Selection.activeGameObject;

        if (selected != null && !EditorUtility.IsPersistent(selected))
        {
            MMOUniversalGuardSystem.EntityRole detectedRole = DetectRoleFromGameObject(selected);
            InstallGuardSystemOnTarget(selected, detectedRole, shieldDomeMesh, matShield, shatterPrefab, startBlockingIfMonster: detectedRole == MMOUniversalGuardSystem.EntityRole.Monster);
            Selection.activeGameObject = selected;
            Debug.Log($"[VFXUniversalGuardBreakGenerator] ติดตั้งระบบ Universal Guard ({detectedRole}) ลงบน '{selected.name}' เรียบร้อยแล้ว");
            return;
        }

        // หากไม่ได้เลือกวัตถุใดใน Hierarchy ให้สร้างทั้งหุ่น Player และหุ่น Monster ที่ยกโล่บล็อคค้างไว้ให้ทดสอบคู่กันทันที
        GameObject testGroup = new GameObject("AAA_UniversalGuard_TestArena");
        Undo.RegisterCreatedObjectUndo(testGroup, "Create Universal Guard Test Dummies");

        GameObject playerDummy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        playerDummy.name = "Player_GuardTester";
        playerDummy.transform.SetParent(testGroup.transform, false);
        playerDummy.transform.position = new Vector3(-2.0f, 1.0f, 0f);
        playerDummy.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
        InstallGuardSystemOnTarget(playerDummy, MMOUniversalGuardSystem.EntityRole.Player, shieldDomeMesh, matShield, shatterPrefab, startBlockingIfMonster: false);

        GameObject monsterDummy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        monsterDummy.name = "Monster_GuardTester";
        monsterDummy.transform.SetParent(testGroup.transform, false);
        monsterDummy.transform.position = new Vector3(2.0f, 1.0f, 0f);
        monsterDummy.transform.rotation = Quaternion.Euler(0f, -90f, 0f);
        InstallGuardSystemOnTarget(monsterDummy, MMOUniversalGuardSystem.EntityRole.Monster, shieldDomeMesh, matShield, shatterPrefab, startBlockingIfMonster: true);

        Selection.activeGameObject = testGroup;
        if (SceneView.lastActiveSceneView != null)
        {
            SceneView.lastActiveSceneView.FrameSelected();
        }

        Debug.Log("[VFXUniversalGuardBreakGenerator] ไม่ได้เลือกวัตถุใน Hierarchy จึงสร้างทั้ง 'Player_GuardTester' และ 'Monster_GuardTester' (ยกโล่ค้าง) ให้ทดสอบคู่กันเรียบร้อยแล้ว!");
    }

    private static MMOUniversalGuardSystem.EntityRole DetectRoleFromGameObject(GameObject target)
    {
        string lowerName = target.name.ToLowerInvariant();
        string lowerTag = string.Empty;
        try { lowerTag = target.tag.ToLowerInvariant(); } catch { }

        if (lowerTag.Contains("enemy") || lowerTag.Contains("monster") || lowerTag.Contains("boss") ||
            lowerName.Contains("enemy") || lowerName.Contains("monster") || lowerName.Contains("boss") ||
            lowerName.Contains("orc") || lowerName.Contains("ogre") || lowerName.Contains("goblin"))
        {
            return MMOUniversalGuardSystem.EntityRole.Monster;
        }

        return MMOUniversalGuardSystem.EntityRole.Player;
    }

    private static void InstallGuardSystemOnTarget(
        GameObject target,
        MMOUniversalGuardSystem.EntityRole role,
        Mesh shieldDomeMesh,
        Material matShield,
        GameObject shatterPrefab,
        bool startBlockingIfMonster)
    {
        Transform existingShield = target.transform.Find("VFX_ActiveGuardShield");
        if (existingShield != null)
        {
            Undo.DestroyObjectImmediate(existingShield.gameObject);
        }

        GameObject shieldObj = new GameObject("VFX_ActiveGuardShield");
        Undo.RegisterCreatedObjectUndo(shieldObj, "Create VFX_ActiveGuardShield");
        shieldObj.transform.SetParent(target.transform, false);
        CapsuleCollider rootCapsule = target.GetComponent<CapsuleCollider>();
        bool pivotAtBodyCenter = rootCapsule != null && Mathf.Abs(rootCapsule.center.y) < 0.25f;
        shieldObj.transform.localPosition = pivotAtBodyCenter
            ? new Vector3(0f, 0.15f, 0.85f)
            : new Vector3(0f, 1.15f, 0.85f);
        shieldObj.transform.localRotation = Quaternion.identity;
        shieldObj.transform.localScale = Vector3.one;

        MeshFilter mf = shieldObj.AddComponent<MeshFilter>();
        mf.sharedMesh = shieldDomeMesh;

        MeshRenderer mr = shieldObj.AddComponent<MeshRenderer>();
        mr.sharedMaterial = matShield;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.sortingOrder = 10;

        MMOUniversalGuardSystem guardSystem = target.GetComponent<MMOUniversalGuardSystem>();
        if (guardSystem == null)
        {
            guardSystem = Undo.AddComponent<MMOUniversalGuardSystem>(target);
        }

        Undo.RecordObject(guardSystem, "Configure MMOUniversalGuardSystem");
        guardSystem.entityRole = role;
        guardSystem.maxGuardHP = 100f;
        guardSystem.currentGuardHP = 100f;
        guardSystem.guardBreakStaggerDuration = 2.0f;
        guardSystem.activeGuardShieldObject = shieldObj;
        guardSystem.guardBreakShatterPrefab = shatterPrefab;
        guardSystem.shieldLocalOffset = shieldObj.transform.localPosition;
        guardSystem.testHitGuardDamage = 40f;
        guardSystem.enableTestHitKey = true;
        guardSystem.showDebugHUD = true;

        if (role == MMOUniversalGuardSystem.EntityRole.Monster && startBlockingIfMonster)
        {
            guardSystem.SetBlockingState(true);
            shieldObj.SetActive(true);
        }
        else
        {
            shieldObj.SetActive(false);
        }

        EditorUtility.SetDirty(guardSystem);
        EditorUtility.SetDirty(shieldObj);
    }

    // =========================================================================
    // ASSET SAVING & MATERIAL HELPERS
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

    private static Material CreateOrUpdateAdditiveMaterial(string assetPath, Texture2D mainTex, Color hdrColor)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Particles/Standard Unlit");

        Material mat = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
        bool isNew = mat == null;
        if (isNew) mat = new Material(shader);
        else mat.shader = shader;

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
