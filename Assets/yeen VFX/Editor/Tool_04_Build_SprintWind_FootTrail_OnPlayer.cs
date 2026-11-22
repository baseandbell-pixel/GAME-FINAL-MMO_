using UnityEngine;
using UnityEditor;
using System.IO;

public class Tool_04_Build_SprintWind_FootTrail_OnPlayer : EditorWindow
{
    [MenuItem("yeen VFX/04. [ใส่บน Player] สร้างเอฟเฟกต์วิ่งติดเท้าพุ่งตัว (Sprint Foot Trail)")]
    public static void GenerateSprintWindVFX()
    {
        if (!AssetDatabase.IsValidFolder("Assets/yeen VFX"))
        {
            AssetDatabase.CreateFolder("Assets", "yeen VFX");
        }
        string folderPath = "Assets/yeen VFX/VFX_SprintFootWind";
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            AssetDatabase.CreateFolder("Assets/yeen VFX", "VFX_SprintFootWind");
        }

        // 1. สร้าง Texture เส้นสายลม (Wind Trail) และวงแหวนคลื่นกระแทก (Shockwave Ring)
        string streakPath = folderPath + "/T_SprintWindRibbon.png";
        File.WriteAllBytes(streakPath, GenerateWindRibbonTexture(512, 128).EncodeToPNG());
        AssetDatabase.ImportAsset(streakPath);
        SetupTexture(streakPath, TextureWrapMode.Repeat);
        Texture2D streakTex = AssetDatabase.LoadAssetAtPath<Texture2D>(streakPath);

        string ringPath = folderPath + "/T_DashShockwaveRing.png";
        File.WriteAllBytes(ringPath, GenerateShockwaveRingTexture(256, 256).EncodeToPNG());
        AssetDatabase.ImportAsset(ringPath);
        SetupTexture(ringPath, TextureWrapMode.Clamp);
        Texture2D ringTex = AssetDatabase.LoadAssetAtPath<Texture2D>(ringPath);

        // 2. สร้าง Materials สีขาวใสพริ้วๆ (Sheer White Wind Materials)
        Material ribbonMat = CreateSheerWindMaterial(folderPath + "/M_SprintWindRibbon.mat", streakTex);
        Material ringMat = CreateSheerWindMaterial(folderPath + "/M_DashShockwaveRing.mat", ringTex);

        // 3. สร้าง GameObject ตัวละครจำลองพร้อมจุดยึดเท้าซ้าย-ขวา
        GameObject rootRunner = new GameObject("MMO_Sprint_Tester (กด Play แล้วกดปุ่ม Left Shift ค้างไว้!)");
        rootRunner.transform.position = Vector3.zero;

        // หุ่นจำลองลำตัว
        GameObject bodyDummy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        bodyDummy.name = "Character_Body_Dummy";
        bodyDummy.transform.SetParent(rootRunner.transform, false);
        bodyDummy.transform.localPosition = new Vector3(0f, 1.1f, 0f);
        bodyDummy.transform.localScale = new Vector3(0.7f, 0.75f, 0.7f);
        DestroyImmediate(bodyDummy.GetComponent<Collider>());

        // สร้างจุดยึดเท้าซ้าย (Left Foot Mount) และเท้าขวา (Right Foot Mount)
        GameObject leftFoot = CreateFootMount("FootMount_Left (ติดกระดูกเท้าซ้าย)", rootRunner.transform, new Vector3(-0.22f, 0.15f, 0f));
        GameObject rightFoot = CreateFootMount("FootMount_Right (ติดกระดูกเท้าขวา)", rootRunner.transform, new Vector3(0.22f, 0.15f, 0f));

        // =========================================================================
        // LAYER 1: Anticipation / Dash Sonic Boom (วงแหวนคลื่นลมกระแทกจังหวะเริ่มกด Shift)
        // =========================================================================
        GameObject l1Obj = new GameObject("L1_DashSonicBoom_Ring");
        l1Obj.transform.SetParent(rootRunner.transform, false);
        l1Obj.transform.localPosition = new Vector3(0f, 0.75f, -0.2f); // กลางหลังระดับเอว
        ParticleSystem l1PS = l1Obj.AddComponent<ParticleSystem>();
        ParticleSystemRenderer l1PSR = l1Obj.GetComponent<ParticleSystemRenderer>();

        var l1Main = l1PS.main;
        l1Main.duration = 0.4f;
        l1Main.loop = false;
        l1Main.playOnAwake = false;
        l1Main.startLifetime = 0.25f; // ขยายออกเร็วและคมตอนเริ่มกด Shift
        l1Main.startSpeed = -3.5f;    // ถอยไปข้างหลังตามแรงถีบออกตัว
        l1Main.startSize = 2.2f;
        l1Main.startColor = new Color(1f, 1f, 1f, 0.65f);

        var l1Emis = l1PS.emission;
        l1Emis.rateOverTime = 0f;
        l1Emis.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0.0f, 1) });

        var l1Shape = l1PS.shape;
        l1Shape.enabled = false;

        var l1Size = l1PS.sizeOverLifetime;
        l1Size.enabled = true;
        l1Size.size = new ParticleSystem.MinMaxCurve(1.0f, new AnimationCurve(
            new Keyframe(0.0f, 0.2f, 0f, 8f),
            new Keyframe(0.3f, 1.0f, 0f, 0.5f),
            new Keyframe(1.0f, 1.25f, 0f, 0f)
        ));

        var l1Col = l1PS.colorOverLifetime;
        l1Col.enabled = true;
        l1Col.color = CreateWhiteFadeGradient(0.1f, 0.4f, 0.7f);

        l1PSR.renderMode = ParticleSystemRenderMode.Billboard;
        l1PSR.material = ringMat;

        // =========================================================================
        // LAYER 2: Main Action / Foot Wind Ribbons (เส้นสายลมติดข้อเท้าซ้ายและขวา)
        // =========================================================================
        TrailRenderer leftTrail = SetupFootWindTrail(leftFoot, ribbonMat);
        TrailRenderer rightTrail = SetupFootWindTrail(rightFoot, ribbonMat);

        // =========================================================================
        // LAYER 3: Secondary / Aerodynamic Slipstream (เส้นลมความเร็วสูงพุ่งสวนรอบตัว)
        // =========================================================================
        GameObject l3Obj = new GameObject("L3_Aerodynamic_SpeedLines");
        l3Obj.transform.SetParent(rootRunner.transform, false);
        l3Obj.transform.localPosition = new Vector3(0f, 0.8f, 0.8f); // ดักหน้าตัวละครแล้วพุ่งสวนไปด้านหลัง
        l3Obj.transform.localEulerAngles = new Vector3(0f, 180f, 0f); // หันหัวไปด้านหลัง (-Z)

        ParticleSystem l3PS = l3Obj.AddComponent<ParticleSystem>();
        ParticleSystemRenderer l3PSR = l3Obj.GetComponent<ParticleSystemRenderer>();

        var l3Main = l3PS.main;
        l3Main.loop = true;
        l3Main.playOnAwake = false;
        l3Main.simulationSpace = ParticleSystemSimulationSpace.World; // อิสระในฉากเพื่อให้ดูเหมือนพุ่งผ่านอากาศ
        l3Main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.32f);
        l3Main.startSpeed = new ParticleSystem.MinMaxCurve(14f, 22f); // พุ่งเร็วมากเพื่อให้เกิดเส้น Speed Lines
        l3Main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.08f);
        l3Main.startColor = new Color(1f, 1f, 1f, 0.5f);

        var l3Emis = l3PS.emission;
        l3Emis.rateOverTime = 35f;

        var l3Shape = l3PS.shape;
        l3Shape.enabled = true;
        l3Shape.shapeType = ParticleSystemShapeType.Cone;
        l3Shape.angle = 8f;
        l3Shape.radius = 0.65f;
        l3Shape.radiusThickness = 0.85f; // ให้เกิดรอบๆ ตัว ไม่บังกลางหน้าจอ

        var l3Col = l3PS.colorOverLifetime;
        l3Col.enabled = true;
        l3Col.color = CreateWhiteFadeGradient(0.15f, 0.6f, 0.65f);

        // ยืดเม็ดลมเป็นเส้นยาวคมกริบตามความเร็ว (Stretched Billboard)
        l3PSR.renderMode = ParticleSystemRenderMode.Stretch;
        l3PSR.velocityScale = 0.08f;
        l3PSR.lengthScale = 6.5f;
        l3PSR.material = ribbonMat;

        // =========================================================================
        // LAYER 4: Impact & Residual / Footstep Wind Puffs (ละอองลมฉีกออกที่ส้นเท้าซ้าย-ขวา)
        // =========================================================================
        ParticleSystem leftDustPS = CreateFootWindPuffEmitter("L4_LeftFoot_WindPuffs", leftFoot.transform, ribbonMat);
        ParticleSystem rightDustPS = CreateFootWindPuffEmitter("L4_RightFoot_WindPuffs", rightFoot.transform, ribbonMat);

        // ผูกทุกเลเยอร์เข้ากับสคริปต์ควบคุมปุ่ม Shift
        WuxiaSprintController ctrl = rootRunner.AddComponent<WuxiaSprintController>();
        ctrl.leftFootMount = leftFoot.transform;
        ctrl.rightFootMount = rightFoot.transform;
        ctrl.l1DashBurstRing = l1PS;
        ctrl.l2LeftFootTrail = leftTrail;
        ctrl.l2RightFootTrail = rightTrail;
        ctrl.l3SpeedLines = l3PS;
        ctrl.l4LeftFootDust = leftDustPS;
        ctrl.l4RightFootDust = rightDustPS;

        Selection.activeGameObject = rootRunner;
        Debug.Log("✅ สร้างเอฟเฟกต์วิ่งติดเท้า (Sprint Wind VFX) สำเร็จ! กดปุ่ม Play แล้วกดปุ่ม Left Shift ค้างไว้เพื่อดูเอฟเฟกต์วิ่งความเร็วสูงได้เลยครับ!");
    }

    private static GameObject CreateFootMount(string name, Transform parent, Vector3 localPos)
    {
        GameObject foot = new GameObject(name);
        foot.transform.SetParent(parent, false);
        foot.transform.localPosition = localPos;

        // สร้างก้อนทรงกลมเล็กๆ แทนตำแหน่งข้อเท้าเพื่อให้มองเห็นชัดตอนจัดวาง
        GameObject ankleVisual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ankleVisual.name = "Ankle_Visual_Marker";
        ankleVisual.transform.SetParent(foot.transform, false);
        ankleVisual.transform.localScale = new Vector3(0.16f, 0.12f, 0.25f);
        DestroyImmediate(ankleVisual.GetComponent<Collider>());
        return foot;
    }

    // ตั้งค่าเส้นสายลมที่ข้อเท้า (Layer 2 Trail Renderer)
    private static TrailRenderer SetupFootWindTrail(GameObject footObj, Material mat)
    {
        TrailRenderer tr = footObj.AddComponent<TrailRenderer>();
        tr.material = mat;
        tr.time = 0.28f; // ความยาวของหางลมที่ลากตามเท้า
        tr.minVertexDistance = 0.02f;
        tr.emitting = false;
        tr.textureMode = LineTextureMode.Stretch;
        tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        tr.receiveShadows = false;

        // หัวกว้างตรงข้อเท้า แล้วเรียวแหลมหายไปที่ปลายหาง
        tr.widthCurve = new AnimationCurve(
            new Keyframe(0.0f, 0.28f),
            new Keyframe(0.35f, 0.18f),
            new Keyframe(1.0f, 0.0f)
        );

        tr.colorGradient = CreateWhiteFadeGradient(0.05f, 0.45f, 0.65f);
        return tr;
    }

    // สร้างละอองลมฉีกออกใต้ฝ่าเท้า (Layer 4)
    private static ParticleSystem CreateFootWindPuffEmitter(string name, Transform footParent, Material mat)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(footParent, false);
        obj.transform.localEulerAngles = new Vector3(0f, 180f, 0f); // พัดเฉียงไปด้านหลังเท้า

        ParticleSystem ps = obj.AddComponent<ParticleSystem>();
        ParticleSystemRenderer psr = obj.GetComponent<ParticleSystemRenderer>();

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.38f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 5.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
        main.startColor = new Color(1f, 1f, 1f, 0.45f);

        var emis = ps.emission;
        emis.rateOverTime = 22f; // ปล่อยตามเวลาและตามระยะทางที่เท้าก้าว
        emis.rateOverDistance = 6f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 22f;
        shape.radius = 0.08f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = CreateWhiteFadeGradient(0.1f, 0.5f, 0.5f);

        psr.renderMode = ParticleSystemRenderMode.Stretch;
        psr.velocityScale = 0.05f;
        psr.lengthScale = 3.2f;
        psr.material = mat;
        return ps;
    }

    private static Gradient CreateWhiteFadeGradient(float fadeIn, float fadeOutStart, float maxAlpha)
    {
        Gradient g = new Gradient();
        g.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new GradientAlphaKey[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(maxAlpha, fadeIn),
                new GradientAlphaKey(maxAlpha * 0.5f, fadeOutStart),
                new GradientAlphaKey(0f, 1f)
            }
        );
        return g;
    }

    // วาด Texture ริบบิ้นสายลมสีขาวบริสุทธิ์
    private static Texture2D GenerateWindRibbonTexture(int width, int height)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        for (int y = 0; y < height; y++)
        {
            float v = y / (float)height;
            // ขอบบนและล่างของเส้นลมค่อยๆ จางลง ไม่เป็นขอบสี่เหลี่ยมแข็งๆ
            float edgeFadeV = Mathf.Sin(v * Mathf.PI);
            edgeFadeV = Mathf.Pow(edgeFadeV, 1.6f);

            for (int x = 0; x < width; x++)
            {
                float u = x / (float)width;
                float fadeU = Mathf.Sin(u * Mathf.PI);

                float s1 = Mathf.PerlinNoise(u * 3.0f, v * 18.0f);
                float s2 = Mathf.PerlinNoise(u * 7.0f + 4.2f, v * 36.0f);
                float coreLine = Mathf.Clamp01(1.0f - Mathf.Abs(v - 0.5f) * 4.5f);

                float alpha = Mathf.Clamp01((s1 * 0.5f + s2 * 0.3f + coreLine * 0.6f) * edgeFadeV * fadeU);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply();
        return tex;
    }

    // วาด Texture วงแหวนคลื่นลมกระแทก (Sonic Boom Ring)
    private static Texture2D GenerateShockwaveRingTexture(int width, int height)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float u = (x / (float)width) * 2f - 1f;
                float v = (y / (float)height) * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);

                float ring = Mathf.Clamp01(1.0f - Mathf.Abs(r - 0.78f) * 12.0f);
                ring = Mathf.Pow(ring, 2.0f);
                if (r > 0.96f) ring = 0f;

                tex.SetPixel(x, y, new Color(1f, 1f, 1f, ring));
            }
        }
        tex.Apply();
        return tex;
    }

    private static void SetupTexture(string path, TextureWrapMode wrap)
    {
        TextureImporter imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp != null)
        {
            imp.alphaIsTransparency = true;
            imp.wrapMode = wrap;
            imp.SaveAndReimport();
        }
    }

    private static Material CreateSheerWindMaterial(string path, Texture2D tex)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (sh == null) sh = Shader.Find("Particles/Standard Unlit");
        Material mat = new Material(sh);
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.85f));
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1.0f);
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0.0f); // Alpha Blend สีขาวใสเหมือนสายลมจริง
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0.0f);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = 3000;
        AssetDatabase.CreateAsset(mat, path);
        AssetDatabase.SaveAssets();
        return mat;
    }
}