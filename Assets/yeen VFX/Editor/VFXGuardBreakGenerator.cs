using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Editor Generator สำหรับสร้างระบบเอฟเฟกต์ "ตีบล็อคแตก / โล่พลังงานแตกละเอียดระดับ AAA (Guard Break & Energy Shield Shatter VFX)"
/// กดคลิกเดียวจากเมนู:
/// VFX Mentor/16. สร้างเอฟเฟกต์ตีบล็อคแตกโล่กระจาย 4 เลเยอร์ (Guard Break Shatter VFX)
/// จะสร้างไฟล์ทั้งหมดลงใน Assets/yeen VFX/03_VFX_Prefabs_And_Assets/Generated_GuardBreak อัตโนมัติ:
/// - Mesh_ShieldShard3D.asset (3D Mesh เศษผลึกโล่ทรงสามเหลี่ยม 2 ด้านมีสันนูน 5 จุดยอด 6 หน้าสามเหลี่ยม)
/// - T_ShieldCrackWeb.png (512x512 ลายรอยร้าวใยแมงมุม 11 แฉก + Perlin Noise + วงรอยร้าวเชื่อม 2 ชั้น)
/// - T_ShardGlow.png (128x128 แสงเรืองขอบนุ่มสำหรับเศษผลึก 3 มิติและประกายไฟ)
/// - T_GuardBreakWave.png (256x256 วงแหวนคลื่นกระแทกขอบคม)
/// - Material แบบ URP Particles/Unlit Additive HDR (Cull = Off)
/// - VFX_GuardBreakShatter.prefab (โครงสร้าง 4 เลเยอร์ครบสมบูรณ์)
/// พร้อมผูกเข้ากับตัวละครที่เลือกใน Hierarchy อัตโนมัติ
/// </summary>
public static class VFXGuardBreakGenerator
{
    private const string OutputFolder = "Assets/yeen VFX/03_VFX_Prefabs_And_Assets/Generated_GuardBreak";
    private const string ShardMeshPath = OutputFolder + "/Mesh_ShieldShard3D.asset";
    private const string CrackWebTexPath = OutputFolder + "/T_ShieldCrackWeb.png";
    private const string ShardGlowTexPath = OutputFolder + "/T_ShardGlow.png";
    private const string WaveRingTexPath = OutputFolder + "/T_GuardBreakWave.png";

    private const string MatCrackPath = OutputFolder + "/Mat_GuardCrackWeb_Add.mat";
    private const string MatShardPath = OutputFolder + "/Mat_ShieldShard3D_Add.mat";
    private const string MatSparkPath = OutputFolder + "/Mat_ShatterSparks_Add.mat";
    private const string MatWavePath = OutputFolder + "/Mat_GuardBreakWave_Add.mat";

    private const string PrefabPath = OutputFolder + "/VFX_GuardBreakShatter.prefab";

    [MenuItem("yeen VFX/13. [ใส่บน Player] สร้างเอฟเฟกต์ตีบล็อคแตกโล่กระจาย 4 เลเยอร์ (Guard Break Shatter)")]
    [MenuItem("VFX Mentor/16. สร้างเอฟเฟกต์ตีบล็อคแตกโล่กระจาย 4 เลเยอร์ (Guard Break Shatter VFX)")]
    public static void GenerateGuardBreakVFX()
    {
        EnsureOutputFolder();

        // 1. สร้างและบันทึก 3D Mesh เศษผลึกโล่ทรงสามเหลี่ยมมีสันนูน (5 จุดยอด, 6 หน้าสามเหลี่ยม)
        Mesh shardMesh = CreateAndSaveShieldShardMesh(ShardMeshPath);

        // 2. วาดและบันทึกไฟล์ Texture 3 ใบด้วยสมการคณิตศาสตร์
        Texture2D crackTex = SaveTexturePng(GenerateShieldCrackWebTexture(512), CrackWebTexPath);
        Texture2D glowTex = SaveTexturePng(GenerateShardGlowTexture(128), ShardGlowTexPath);
        Texture2D waveTex = SaveTexturePng(GenerateGuardBreakWaveTexture(256), WaveRingTexPath);

        // 3. สร้างและบันทึก Material แบบ URP Additive HDR (Transparent, Additive, Cull = Off)
        //    ตั้งค่า HDR Intensity ลงบน Material._BaseColor โดยตรง เพื่อไม่ให้ ParticleSystem Color32 vertex buffer ตัดเหลือ 1.0
        Material matCrack = CreateAndSaveAdditiveMaterial(MatCrackPath, crackTex, 4.8f);
        Material matShard = CreateAndSaveAdditiveMaterial(MatShardPath, glowTex, 3.2f);
        Material matSpark = CreateAndSaveAdditiveMaterial(MatSparkPath, glowTex, 4.5f);
        Material matWave = CreateAndSaveAdditiveMaterial(MatWavePath, waveTex, 3.8f);

        // 4. ประกอบโครงสร้าง Prefab 4 เลเยอร์ครบตามกฎเหล็ก
        Mesh quadMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        GameObject root = new GameObject("VFX_GuardBreakShatter");

        try
        {
            BuildLayer1OverloadCrackWeb(root.transform, quadMesh, matCrack);
            BuildLayer2ShatteredShards3D(root.transform, shardMesh, matShard);
            BuildLayer3ShatterSparks(root.transform, matSpark);
            BuildLayer4ShockwaveRing(root.transform, quadMesh, matWave);

            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // 5. ผูกเข้ากับตัวละครที่เลือกใน Hierarchy อัตโนมัติ (หรือสร้าง Tester ให้หากไม่ได้เลือกไว้)
            BindPrefabToSelectedCharacter(savedPrefab);

            EditorGUIUtility.PingObject(savedPrefab);
            Debug.Log($"[VFXGuardBreakGenerator] สร้าง Guard Break Shatter VFX 4 เลเยอร์สำเร็จที่: {PrefabPath}");
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
        main.startSpeed = 0.0f;
        main.startSize = 2.4f;
        main.startColor = Color.white; // HDR Intensity 4.8x ถูกขับจาก Mat_GuardCrackWeb_Add._BaseColor
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
                new GradientColorKey(new Color(1.00f, 0.84f, 0.24f), 0.00f), // ทองสว่าง (Overload Flash)
                new GradientColorKey(new Color(1.00f, 0.96f, 0.78f), 0.22f), // ขาวทองเจิดจ้า
                new GradientColorKey(new Color(0.18f, 0.88f, 1.00f), 0.62f), // ไล่เปลี่ยนเป็นสีฟ้าครามพลังงาน
                new GradientColorKey(new Color(0.06f, 0.56f, 1.00f), 1.00f)  // ฟ้าครามเข้มก่อนสลาย
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
    /// Mesh_ShieldShard3D.asset, Burst 26 ชิ้น, Simulation Space = World,
    /// 3D Start Rotation + Rotation over Lifetime ทุกแกน -360 ถึง +360 องศา, Gravity Modifier = 1.65
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
        main.startColor = Color.white; // HDR Crystal Glow 3.2x ถูกขับจาก Mat_ShieldShard3D_Add._BaseColor
        main.gravityModifier = 1.65f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 40;

        // เปิด 3D Start Rotation สุ่มหมุนครบทุกแกน
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0.00f, 26) });

        // ระเบิดพุ่งกระจายออกจากหน้าตัดวงกลมของโล่
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.78f;
        shape.radiusThickness = 1.0f;
        shape.arc = 360f;
        shape.randomDirectionAmount = 0.35f;

        // แรงผลักพุ่งออกด้านหน้าโล่เล็กน้อยใน Local Space ก่อนร่วงลงตามแรงโน้มถ่วง
        var vol = ps.velocityOverLifetime;
        vol.enabled = true;
        vol.space = ParticleSystemSimulationSpace.Local;
        vol.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        vol.y = new ParticleSystem.MinMaxCurve(0.6f, 1.8f);
        vol.z = new ParticleSystem.MinMaxCurve(1.4f, 3.2f);

        // Rotation over Lifetime ทุกแกน -360 ถึง +360 องศา (โหมด TwoConstants ตรงกันทั้ง 3 แกนสำหรับ Unity 6)
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
    /// Stretched Billboard, Simulation Space = World, Velocity Scale = 0.06, Length Scale = 3.8, Burst 24 เส้น, สีส้มเพลิง HDR
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
        main.startColor = Color.white; // HDR Fiery Orange 4.5x ถูกขับจาก Mat_ShatterSparks_Add._BaseColor
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
                new GradientColorKey(new Color(1.00f, 0.94f, 0.62f), 0.00f), // ขาวทองจุดระเบิด
                new GradientColorKey(new Color(1.00f, 0.45f, 0.06f), 0.34f), // ส้มเพลิง HDR
                new GradientColorKey(new Color(0.92f, 0.15f, 0.02f), 1.00f)  // แดงส้มหางไฟ
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
        main.startSpeed = 0.0f;
        main.startSize = 3.4f;
        main.startColor = Color.white; // HDR Intensity 3.8x ถูกขับจาก Mat_GuardBreakWave_Add._BaseColor
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

        // Explosive Ease-Out ขยายตัวผลักอากาศออกด้านหน้าเพื่อเปิดจังหวะ Stagger
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
    // PROCEDURAL 3D SHARD MESH (5 จุดยอด, 6 หน้าสามเหลี่ยม)
    // =========================================================================

    private static Mesh CreateAndSaveShieldShardMesh(string assetPath)
    {
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
        bool isNew = mesh == null;
        if (isNew)
        {
            mesh = new Mesh { name = "Mesh_ShieldShard3D" };
        }
        else
        {
            mesh.Clear();
            mesh.name = "Mesh_ShieldShard3D";
        }

        // 5 จุดยอด: ขอบสามเหลี่ยมคมไม่สมมาตร 3 จุด + สันนูนหน้า/หลัง 2 จุด
        Vector3[] vertices =
        {
            new Vector3( 0.00f,  0.58f,  0.00f), // 0: ยอดแหลมด้านบน
            new Vector3(-0.44f, -0.38f,  0.00f), // 1: มุมล่างซ้าย
            new Vector3( 0.48f, -0.30f,  0.00f), // 2: มุมล่างขวา
            new Vector3( 0.02f, -0.02f,  0.14f), // 3: สันนูนด้านหน้า (+Z)
            new Vector3( 0.02f, -0.02f, -0.14f)  // 4: สันนูนด้านหลัง (-Z)
        };

        // 6 หน้าสามเหลี่ยม (ด้านหน้า 3 หน้า + ด้านหลัง 3 หน้า ปิดทึบสมบูรณ์ 2 ด้าน)
        int[] triangles =
        {
            // หน้าผลึกฝั่ง +Z
            0, 1, 3,
            1, 2, 3,
            2, 0, 3,
            // หน้าผลึกฝั่ง -Z
            0, 4, 1,
            1, 4, 2,
            2, 4, 0
        };

        // UV วางจุดสว่างกลาง Texture ไว้ที่สันนูนตรงกลางผลึก
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

        if (isNew)
        {
            AssetDatabase.CreateAsset(mesh, assetPath);
        }
        else
        {
            EditorUtility.SetDirty(mesh);
        }

        return AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
    }

    // =========================================================================
    // PROCEDURAL TEXTURES (3 ใบ: Crack Web 512, Shard Glow 128, Shockwave 256)
    // =========================================================================

    /// <summary>
    /// T_ShieldCrackWeb.png (512x512 px):
    /// ลายเส้นรอยร้าวใยแมงมุม 11 แฉกกระจายออกจากศูนย์กลาง พร้อมรอยหยัก Perlin Noise และวงรอยร้าวเชื่อม 2 ชั้น
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
        float invSize = 1f / (size - 1);

        for (int y = 0; y < size; y++)
        {
            float ny = y * invSize * 2f - 1f;
            for (int x = 0; x < size; x++)
            {
                float nx = x * invSize * 2f - 1f;
                float r = Mathf.Sqrt(nx * nx + ny * ny);
                if (r >= 0.99f)
                {
                    pixels[y * size + x] = Color.clear;
                    continue;
                }

                float angle = Mathf.Atan2(ny, nx);
                if (angle < 0f) angle += Mathf.PI * 2f;

                float edgeMask = 1f - SmoothStep(0.76f, 0.98f, r);

                // รอยหยักหลักและรอยหยักละเอียดด้วย Perlin Noise
                float macroJag = (Mathf.PerlinNoise(nx * 5.2f + 11.4f, ny * 5.2f + 3.7f) - 0.5f) * 0.24f;
                float microJag = (Mathf.PerlinNoise(nx * 14.5f + 2.1f, ny * 14.5f + 8.9f) - 0.5f) * 0.08f;

                float crackIntensity = 0f;

                // 11 แฉกรอยร้าวหลัก
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

                // วงรอยร้าวเชื่อมระหว่างแฉก 2 ชั้น (ชั้นใน r ~ 0.34, ชั้นนอก r ~ 0.64)
                float cCos = Mathf.Cos(angle);
                float cSin = Mathf.Sin(angle);
                float ringOffset1 = (Mathf.PerlinNoise(cCos * 3.1f + 5.0f, cSin * 3.1f + 5.0f) - 0.5f) * 0.10f;
                float ringOffset2 = (Mathf.PerlinNoise(cCos * 4.6f + 17.0f, cSin * 4.6f + 9.0f) - 0.5f) * 0.12f;

                float distRing1 = Mathf.Abs(r - (0.34f + ringOffset1));
                float distRing2 = Mathf.Abs(r - (0.64f + ringOffset2));

                float ring1 = Mathf.Exp(-(distRing1 * distRing1) / (0.015f * 0.015f)) * 0.92f;
                float ring2 = Mathf.Exp(-(distRing2 * distRing2) / (0.013f * 0.013f)) * 0.78f;

                // จุดศูนย์กลาง Overload Flash
                float impactCore = Mathf.Exp(-(r * r) / (0.14f * 0.14f)) * 1.15f;

                float value = Mathf.Clamp01(Mathf.Max(Mathf.Max(crackIntensity, impactCore), Mathf.Max(ring1, ring2)) * edgeMask);
                pixels[y * size + x] = new Color(value, value, value, value);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    /// <summary>
    /// T_ShardGlow.png (128x128 px):
    /// แสงเรืองขอบนุ่มนวลสำหรับเคลือบชิ้นเศษผลึก 3 มิติและประกายไฟ
    /// </summary>
    private static Texture2D GenerateShardGlowTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[size * size];
        float invSize = 1f / (size - 1);

        for (int y = 0; y < size; y++)
        {
            float ny = y * invSize * 2f - 1f;
            for (int x = 0; x < size; x++)
            {
                float nx = x * invSize * 2f - 1f;
                float r = Mathf.Sqrt(nx * nx + ny * ny);
                float invR = Mathf.Clamp01(1f - r);

                float softHalo = Mathf.Pow(invR, 1.85f) * 0.72f;
                float brightCore = Mathf.Pow(invR, 4.5f) * 0.48f;
                float val = Mathf.Clamp01(softHalo + brightCore);

                pixels[y * size + x] = new Color(val, val, val, val);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    /// <summary>
    /// T_GuardBreakWave.png (256x256 px):
    /// วงแหวนคลื่นกระแทกขอบคมสำหรับ Layer 4
    /// </summary>
    private static Texture2D GenerateGuardBreakWaveTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[size * size];
        float invSize = 1f / (size - 1);

        for (int y = 0; y < size; y++)
        {
            float ny = y * invSize * 2f - 1f;
            for (int x = 0; x < size; x++)
            {
                float nx = x * invSize * 2f - 1f;
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
                float energyRipple = 0.82f + 0.18f * Mathf.PerlinNoise(Mathf.Cos(angle) * 6.0f + 4.0f, Mathf.Sin(angle) * 6.0f + 4.0f);

                float val = Mathf.Clamp01((innerTrail * outerCut * 0.72f + crispRim * 0.96f) * energyRipple * outerCut);
                pixels[y * size + x] = new Color(val, val, val, val);
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

    private static Material CreateAndSaveAdditiveMaterial(string assetPath, Texture2D mainTex, float hdrIntensity)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Particles/Standard Unlit");
        }

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

        // ตั้งค่า URP Surface = Transparent, Blend = Additive, Cull = Off
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

    private static void BindPrefabToSelectedCharacter(GameObject prefab)
    {
        GameObject target = Selection.activeGameObject;

        // ป้องกันการพยายาม AddComponent ใส่ Asset ใน Project Window
        if (target == null || EditorUtility.IsPersistent(target))
        {
            target = new GameObject("GuardBreakVFX_Tester");
            Undo.RegisterCreatedObjectUndo(target, "Create GuardBreakVFX Tester");
            Debug.Log("[VFXGuardBreakGenerator] ไม่ได้เลือกตัวละครใน Hierarchy จึงสร้าง GameObject 'GuardBreakVFX_Tester' พร้อมผูกสคริปต์ให้เรียบร้อยแล้ว");
        }

        MMOGuardBreakVFX guardBreakComp = target.GetComponent<MMOGuardBreakVFX>();
        if (guardBreakComp == null)
        {
            guardBreakComp = Undo.AddComponent<MMOGuardBreakVFX>(target);
        }

        Undo.RecordObject(guardBreakComp, "Assign Guard Break VFX Prefab");
        guardBreakComp.guardBreakPrefab = prefab;
        EditorUtility.SetDirty(guardBreakComp);
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

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp01((x - edge0) / Mathf.Max(0.0001f, edge1 - edge0));
        return t * t * (3f - 2f * t);
    }
}
