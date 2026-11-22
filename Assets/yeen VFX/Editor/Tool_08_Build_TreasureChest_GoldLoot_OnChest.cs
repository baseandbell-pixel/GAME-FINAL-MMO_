using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// เครื่องมือสร้างระบบเอฟเฟกต์เปิดกล่องสมบัติแสงทอง 4 เลเยอร์ระดับ AAA อัตโนมัติ (Zero-External-Asset)
/// - สร้าง Mesh_LootLightCone.asset (กรวยแสงเปิดปลาย 2 ด้านพร้อม Vertex Color Alpha ไล่จางบน-ล่าง)
/// - สร้าง Texture Procedural 3 ไฟล์ (T_GoldenGodRays.png, T_LootSparkleStar.png, T_ChestShockwaveRing.png)
/// - สร้าง Material URP Additive HDR พร้อมรองรับ Bloom 100%
/// - สร้างกล่องสมบัติทดสอบ 3D (AAA_TreasureChest_Tester) พร้อมบานพับ Chest_Lid_Pivot และเอฟเฟกต์ครบทั้ง 4 เลเยอร์
/// </summary>
public static class Tool_08_Build_TreasureChest_GoldLoot_OnChest
{
    private const string OutputFolder = "Assets/yeen VFX/03_VFX_Prefabs_And_Assets/Generated_TreasureChest";
    private const string ConeMeshPath = OutputFolder + "/Mesh_LootLightCone.asset";
    private const string TexGodRaysPath = OutputFolder + "/T_GoldenGodRays.png";
    private const string TexSparkleStarPath = OutputFolder + "/T_LootSparkleStar.png";
    private const string TexShockwaveRingPath = OutputFolder + "/T_ChestShockwaveRing.png";

    private const string MatShockwavePath = OutputFolder + "/M_Chest_ShockwaveRing_Add.mat";
    private const string MatLightConePath = OutputFolder + "/M_Chest_GoldenLightCone_Add.mat";
    private const string MatSparklesPath = OutputFolder + "/M_Chest_GoldenSparkles_Add.mat";
    private const string MatStarFlashPath = OutputFolder + "/M_Chest_StarFlash_Add.mat";

    private const string MatChestWoodPath = OutputFolder + "/M_Chest_Wood_URP.mat";
    private const string MatChestGoldPath = OutputFolder + "/M_Chest_GoldTrim_URP.mat";
    private const string MatChestInteriorPath = OutputFolder + "/M_Chest_VelvetInterior_URP.mat";

    [MenuItem("yeen VFX/08. [ใส่บน Chest] สร้างเอฟเฟกต์เปิดหีบสมบัติแสงทอง (Treasure Chest Burst)")]
    public static void GenerateTreasureChestVFX()
    {
        EnsureFolderHierarchy(OutputFolder);

        // 1. สร้าง Procedural 3D Mesh กรวยลำแสงเปิดปลาย 2 ด้านพร้อม Vertex Color Alpha
        Mesh lightConeMesh = CreateAndSaveInvertedLightConeMesh(ConeMeshPath);

        // 2. สร้าง Procedural Textures 3 ไฟล์ด้วยโค้ด 100%
        Texture2D texGodRays = GenerateAndSaveGodRaysTexture(TexGodRaysPath, 512, 512);
        Texture2D texSparkleStar = GenerateAndSaveSparkleStarTexture(TexSparkleStarPath, 256, 256);
        Texture2D texShockwaveRing = GenerateAndSaveShockwaveRingTexture(TexShockwaveRingPath, 256, 256);

        AssetDatabase.Refresh();

        // ตั้งค่า Texture Importer (WrapMode, Alpha, sRGB, Compression)
        ConfigureTextureImporter(TexGodRaysPath, TextureWrapMode.Repeat, TextureWrapMode.Clamp);
        ConfigureTextureImporter(TexSparkleStarPath, TextureWrapMode.Clamp, TextureWrapMode.Clamp);
        ConfigureTextureImporter(TexShockwaveRingPath, TextureWrapMode.Clamp, TextureWrapMode.Clamp);

        texGodRays = AssetDatabase.LoadAssetAtPath<Texture2D>(TexGodRaysPath);
        texSparkleStar = AssetDatabase.LoadAssetAtPath<Texture2D>(TexSparkleStarPath);
        texShockwaveRing = AssetDatabase.LoadAssetAtPath<Texture2D>(TexShockwaveRingPath);

        // 3. สร้าง URP Additive HDR Materials สำหรับแต่ละเลเยอร์
        Material matShockwave = CreateOrUpdateURPAdditiveMaterial(
            MatShockwavePath, texShockwaveRing, new Color(1.6f, 1.25f, 0.55f, 1f));

        Material matLightCone = CreateOrUpdateURPAdditiveMaterial(
            MatLightConePath, texGodRays, new Color(2.4f, 1.75f, 0.52f, 1f));

        Material matSparkles = CreateOrUpdateURPAdditiveMaterial(
            MatSparklesPath, texSparkleStar, new Color(1.8f, 1.55f, 0.85f, 1f));

        Material matStarFlash = CreateOrUpdateURPAdditiveMaterial(
            MatStarFlashPath, texSparkleStar, new Color(2.2f, 1.85f, 1.0f, 1f));

        // สร้าง Material URP Lit สำหรับตัวกล่องสมบัติทดสอบ 3D (ป้องกันโมเดลเป็นสีชมพูใน URP)
        Material matWood = CreateOrUpdateURPLitMaterial(MatChestWoodPath, new Color(0.28f, 0.14f, 0.06f, 1f), 0.05f, 0.35f);
        Material matGold = CreateOrUpdateURPLitMaterial(MatChestGoldPath, new Color(0.95f, 0.72f, 0.22f, 1f), 0.88f, 0.78f);
        Material matInterior = CreateOrUpdateURPLitMaterial(MatChestInteriorPath, new Color(0.16f, 0.03f, 0.04f, 1f), 0.0f, 0.2f);

        // 4. ลบตัวทดสอบเก่าหากมีชื่อซ้ำใน Scene เพื่อให้สร้างใหม่สะอาด 100%
        GameObject existing = GameObject.Find("AAA_TreasureChest_Tester");
        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing);
        }

        // 5. สร้างกล่องสมบัติทดสอบ 3D พร้อมบานพับ Chest_Lid_Pivot และเอฟเฟกต์ทั้ง 4 เลเยอร์
        GameObject chestRoot = BuildTreasureChestHierarchy(
            lightConeMesh,
            matShockwave,
            matLightCone,
            matSparkles,
            matStarFlash,
            matWood,
            matGold,
            matInterior);

        Undo.RegisterCreatedObjectUndo(chestRoot, "Create AAA Treasure Chest VFX");
        Selection.activeGameObject = chestRoot;
        if (SceneView.lastActiveSceneView != null)
        {
            SceneView.lastActiveSceneView.FrameSelected();
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog(
            "สร้างระบบ Treasure Chest VFX สำเร็จ 100%!",
            "สร้างเอฟเฟกต์เปิดกล่องสมบัติแสงทอง 4 เลเยอร์และกล่องทดสอบ 'AAA_TreasureChest_Tester' เรียบร้อยแล้ว!\n\n" +
            "ไฟล์ Assets ทั้งหมดถูกบันทึกไว้ที่:\n" + OutputFolder + "\n\n" +
            "วิธีทดสอบทันที:\n" +
            "1. กดปุ่ม Play ด้านบน\n" +
            "2. กดปุ่ม [G] บนคีย์บอร์ด หรือคลิกซ้ายที่ตัวกล่องสมบัติเพื่อเปิด-ปิดฝาและชมแสงทองพุ่ง 4 เลเยอร์!",
            "รับทราบ (OK)");
    }

    // =================================================================================================
    // PART A: สร้าง Procedural 3D Mesh กรวยลำแสงเปิดปลาย 2 ด้าน (Mesh_LootLightCone.asset)
    // =================================================================================================
    private static Mesh CreateAndSaveInvertedLightConeMesh(string assetPath)
    {
        const int radialSegments = 48;
        const int heightSegments = 16;
        const float bottomRadius = 0.36f; // ปากล่างแคบพอดีปากหีบสมบัติ
        const float topRadius = 1.18f;    // ปากบนบานกว้างพุ่งขึ้นฟ้า
        const float height = 1.0f;        // ความสูงมาตรฐาน 1 หน่วย (ขยายด้วย 3D Start Size แกน Y)

        int ringVertexCount = radialSegments + 1;
        int totalVertices = ringVertexCount * (heightSegments + 1);

        Vector3[] vertices = new Vector3[totalVertices];
        Vector3[] normals = new Vector3[totalVertices];
        Vector2[] uvs = new Vector2[totalVertices];
        Color[] colors = new Color[totalVertices];

        int vertIndex = 0;
        for (int yStep = 0; yStep <= heightSegments; yStep++)
        {
            float v = (float)yStep / heightSegments; // 0 = ปากล่างในหีบ, 1 = ปลายบนกลางอากาศ
            float y = v * height;

            // ขยายรัศมีแบบโค้งอ่อนๆ (Flared Cone) ให้ลำแสงดูทรงพลังกว่ากรวยเส้นตรงธรรมดา
            float radius = Mathf.Lerp(bottomRadius, topRadius, Mathf.Pow(v, 0.92f));

            // ฝัง Vertex Color Alpha:
            // - ด้านล่างสุดในกล่องค่อยๆ ไล่สว่างขึ้นนุ่มนวล (0 -> 1 ในช่วง v = 0.00..0.14) ไม่ตัดขอบแข็งกับปากกล่อง
            // - ด้านบนสุดไล่จางหายไปจนเป็น 0 สนิท (1 -> 0 ในช่วง v = 0.32..1.00) กลมกลืนกับท้องฟ้า
            float bottomFade = SmoothStep(0.0f, 0.14f, v);
            float topFade = 1.0f - SmoothStep(0.32f, 1.0f, v);
            float alpha = Mathf.Clamp01(bottomFade * Mathf.Pow(topFade, 1.45f));

            // ไล่สีทองสว่างตรงฐานในหีบไปเป็นทองอุ่นที่ปลายลำแสง
            Color ringColor = Color.Lerp(
                new Color(1.0f, 0.95f, 0.78f, alpha),
                new Color(1.0f, 0.76f, 0.25f, alpha),
                v);

            for (int rStep = 0; rStep <= radialSegments; rStep++)
            {
                float u = (float)rStep / radialSegments;
                float angle = u * Mathf.PI * 2f;
                float cosA = Mathf.Cos(angle);
                float sinA = Mathf.Sin(angle);

                vertices[vertIndex] = new Vector3(cosA * radius, y, sinA * radius);
                normals[vertIndex] = new Vector3(cosA, 0.25f, sinA).normalized;
                uvs[vertIndex] = new Vector2(u * 2.0f, v); // วนลายลำแสง 2 รอบรอบกรวยไร้รอยต่อ
                colors[vertIndex] = ringColor;
                vertIndex++;
            }
        }

        int[] triangles = new int[radialSegments * heightSegments * 6];
        int triIndex = 0;
        for (int yStep = 0; yStep < heightSegments; yStep++)
        {
            for (int rStep = 0; rStep < radialSegments; rStep++)
            {
                int current = yStep * ringVertexCount + rStep;
                int next = current + 1;
                int above = (yStep + 1) * ringVertexCount + rStep;
                int aboveNext = above + 1;

                triangles[triIndex++] = current;
                triangles[triIndex++] = above;
                triangles[triIndex++] = next;

                triangles[triIndex++] = next;
                triangles[triIndex++] = above;
                triangles[triIndex++] = aboveNext;
            }
        }

        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
        if (mesh == null)
        {
            mesh = new Mesh();
            mesh.name = "Mesh_LootLightCone";
            AssetDatabase.CreateAsset(mesh, assetPath);
        }
        else
        {
            mesh.Clear();
        }

        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.colors = colors;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);

        return mesh;
    }

    // =================================================================================================
    // PART B: สร้าง Procedural Textures 3 ไฟล์ (T_GoldenGodRays, T_LootSparkleStar, T_ChestShockwaveRing)
    // =================================================================================================
    private static Texture2D GenerateAndSaveGodRaysTexture(string assetPath, int width, int height)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[width * height];

        for (int y = 0; y < height; y++)
        {
            float v = (float)y / (height - 1);

            // ไล่ระดับความเข้มแนวตั้ง: เข้มตรงฐาน (v ~ 0.12) แล้วค่อยๆ จางละมุนสู่ขอบบนสุดเป็น 0
            float verticalIn = SmoothStep(0.0f, 0.10f, v);
            float verticalOut = Mathf.Pow(1.0f - SmoothStep(0.18f, 1.0f, v), 1.6f);
            float verticalMask = verticalIn * verticalOut;

            for (int x = 0; x < width; x++)
            {
                float u = (float)x / width;
                float angle = u * Mathf.PI * 2f; // ใช้คลื่นคาบ 2*PI เพื่อวนรอยต่อแนวนอน Seamless 100%

                // ผสมลำแสงหลายความถี่ให้เหมือน God Rays จริงในเกม Genshin Impact / Lost Ark
                float rayPrimary = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(angle * 7f + 0.4f), 2.6f);
                float raySecondary = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(angle * 13f - 1.1f), 4.0f);
                float rayFine = Mathf.Pow(0.5f + 0.5f * Mathf.Cos(angle * 23f + 0.8f), 5.5f);
                float softBaseGlow = 0.28f + 0.20f * Mathf.Sin(angle * 3f + 0.2f);

                // ให้ลำแสงแหลมเรียวขึ้นเบาๆ เมื่อสูงขึ้น
                float rayPattern = softBaseGlow * (1.0f - v * 0.45f)
                                   + rayPrimary * 0.58f
                                   + raySecondary * 0.42f
                                   + rayFine * 0.30f;

                float intensity = Mathf.Clamp01(rayPattern * verticalMask * 1.15f);

                // ไล่โทนสีขาวทองตรงแกนลำแสงที่สว่างจัด -> สีทองอร่ามที่ขอบลำแสง
                Color col = Color.Lerp(
                    new Color(1.0f, 0.72f, 0.18f, intensity),
                    new Color(1.0f, 0.96f, 0.78f, intensity),
                    Mathf.Clamp01(intensity * 1.15f));
                col.a = intensity;

                pixels[y * width + x] = col;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        SaveTextureAsPNG(tex, assetPath);
        Object.DestroyImmediate(tex);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
    }

    private static Texture2D GenerateAndSaveSparkleStarTexture(string assetPath, int width, int height)
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
                float edgeMask = 1.0f - SmoothStep(0.72f, 0.98f, dist);

                // 1. แกนกลางเรืองแสงนุ่มนวล (Soft Glowing Core)
                float core = Mathf.Exp(-dist * dist * 16.0f);
                float halo = Mathf.Exp(-dist * dist * 4.8f) * 0.42f;

                // 2. แฉกดาว 4 แฉกคมชัดแนวตั้งและแนวนอน (Crisp 4-Point Diamond Star Flare)
                float ax = Mathf.Abs(nx);
                float ay = Mathf.Abs(ny);
                float horizontalSpike = Mathf.Exp(-ay * 22.0f) * Mathf.Exp(-ax * 2.4f);
                float verticalSpike = Mathf.Exp(-ax * 22.0f) * Mathf.Exp(-ay * 2.4f);

                // 3. แฉกเฉียงเล็กๆ เสริมมิติประกายเพชรระดับ AAA
                float d1 = Mathf.Abs((nx + ny) * 0.7071f);
                float d2 = Mathf.Abs((nx - ny) * 0.7071f);
                float diagSpikeA = Mathf.Exp(-d1 * 28.0f) * Mathf.Exp(-d2 * 6.5f) * 0.32f;
                float diagSpikeB = Mathf.Exp(-d2 * 28.0f) * Mathf.Exp(-d1 * 6.5f) * 0.32f;

                float value = (core + halo + (horizontalSpike + verticalSpike) * 0.95f + diagSpikeA + diagSpikeB) * edgeMask;
                value = Mathf.Clamp01(value);

                // แกนกลางขาวสว่างจัด ปลายแฉกทองละมุน
                Color col = Color.Lerp(
                    new Color(1.0f, 0.80f, 0.32f, value),
                    new Color(1.0f, 0.99f, 0.94f, value),
                    Mathf.Pow(value, 0.75f));
                col.a = value;

                pixels[y * width + x] = col;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        SaveTextureAsPNG(tex, assetPath);
        Object.DestroyImmediate(tex);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
    }

    private static Texture2D GenerateAndSaveShockwaveRingTexture(string assetPath, int width, int height)
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

                // วงแหวนหลักคมสว่างรอบนอก (radius ~ 0.74) + หางคลื่นจางเข้าด้านใน
                float mainRing = Mathf.Exp(-Mathf.Pow((dist - 0.74f) / 0.065f, 2f));
                float innerTrail = Mathf.Exp(-Mathf.Pow((dist - 0.60f) / 0.14f, 2f)) * 0.45f * SmoothStep(0.32f, 0.65f, dist);
                float outerHalo = Mathf.Exp(-Mathf.Pow((dist - 0.78f) / 0.11f, 2f)) * 0.35f;

                // ลวดลายพลังงานหมุนรอบวงแหวนเบาๆ ให้ไม่เรียบแข็งเกินไป
                float energyRipple = 0.84f + 0.16f * Mathf.Sin(angle * 10f) * Mathf.Cos(angle * 4f);
                float outerCutoff = 1.0f - SmoothStep(0.88f, 0.99f, dist);

                float value = Mathf.Clamp01((mainRing + innerTrail + outerHalo) * energyRipple * outerCutoff);

                Color col = Color.Lerp(
                    new Color(1.0f, 0.70f, 0.15f, value),
                    new Color(1.0f, 0.96f, 0.76f, value),
                    Mathf.Pow(mainRing, 1.3f));
                col.a = value;

                pixels[y * width + x] = col;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        SaveTextureAsPNG(tex, assetPath);
        Object.DestroyImmediate(tex);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
    }

    // =================================================================================================
    // PART C: สร้าง URP Additive HDR Material และ URP Lit Material
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

        // ตั้งค่ามาตรฐาน URP Particles/Unlit โหมด Transparent + Additive + Two-Sided (Cull Off)
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", mainTex);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", mainTex);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseColor);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", baseColor);

        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);     // 1 = Transparent
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 2f);         // 2 = Additive
        if (mat.HasProperty("_ColorMode")) mat.SetFloat("_ColorMode", 0f); // 0 = Multiply Vertex Color
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);           // 0 = Off (มองเห็นทั้งด้านนอกและด้านในกรวย)
        if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);       // ปิด ZWrite สำหรับ Additive VFX
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
        mat.renderQueue = (int)RenderQueue.Transparent; // 3000

        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static Material CreateOrUpdateURPLitMaterial(string assetPath, Color color, float metallic, float smoothness)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
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

        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);

        EditorUtility.SetDirty(mat);
        return mat;
    }

    // =================================================================================================
    // PART D: ประกอบกล่องสมบัติทดสอบ 3D (AAA_TreasureChest_Tester) + โหนดเอฟเฟกต์ทั้ง 4 เลเยอร์
    // =================================================================================================
    private static GameObject BuildTreasureChestHierarchy(
        Mesh lightConeMesh,
        Material matShockwave,
        Material matLightCone,
        Material matSparkles,
        Material matStarFlash,
        Material matWood,
        Material matGold,
        Material matInterior)
    {
        GameObject root = new GameObject("AAA_TreasureChest_Tester");
        root.transform.position = Vector3.zero;
        root.transform.rotation = Quaternion.identity;

        // เพิ่ม BoxCollider ที่ตัวกล่องเพื่อให้ผู้เล่นคลิกซ้ายที่กล่องสมบัติได้ทันที
        BoxCollider chestCollider = root.AddComponent<BoxCollider>();
        chestCollider.center = new Vector3(0f, 0.56f, 0f);
        chestCollider.size = new Vector3(1.52f, 1.15f, 1.02f);

        // --- 1. สร้างตัวกล่องด้านล่าง (Chest_Base_Group) ---
        GameObject baseGroup = new GameObject("Chest_Base_Group");
        baseGroup.transform.SetParent(root.transform, false);

        CreatePrimitivePart("Base_WoodBody", PrimitiveType.Cube, baseGroup.transform,
            new Vector3(0f, 0.36f, 0f), new Vector3(1.42f, 0.72f, 0.90f), matWood);

        CreatePrimitivePart("Base_VelvetInterior", PrimitiveType.Cube, baseGroup.transform,
            new Vector3(0f, 0.70f, 0f), new Vector3(1.26f, 0.06f, 0.74f), matInterior);

        // ขอบทองตกแต่งรอบตัวกล่องสมบัติ
        CreatePrimitivePart("Base_GoldTrim_Bottom", PrimitiveType.Cube, baseGroup.transform,
            new Vector3(0f, 0.06f, 0f), new Vector3(1.48f, 0.12f, 0.96f), matGold);
        CreatePrimitivePart("Base_GoldTrim_TopRim", PrimitiveType.Cube, baseGroup.transform,
            new Vector3(0f, 0.69f, 0f), new Vector3(1.48f, 0.09f, 0.96f), matGold);
        CreatePrimitivePart("Base_GoldStrap_Left", PrimitiveType.Cube, baseGroup.transform,
            new Vector3(-0.48f, 0.36f, 0f), new Vector3(0.11f, 0.74f, 0.95f), matGold);
        CreatePrimitivePart("Base_GoldStrap_Right", PrimitiveType.Cube, baseGroup.transform,
            new Vector3(0.48f, 0.36f, 0f), new Vector3(0.11f, 0.74f, 0.95f), matGold);
        CreatePrimitivePart("Base_GoldLockPlate", PrimitiveType.Cube, baseGroup.transform,
            new Vector3(0f, 0.58f, 0.47f), new Vector3(0.24f, 0.24f, 0.06f), matGold);

        // --- 2. สร้างจุดหมุนบานพับฝาหีบสมบัติ (Chest_Lid_Pivot) วางไว้ที่ขอบหลังด้านบน (Z = -0.45, Y = 0.72) ---
        GameObject lidPivot = new GameObject("Chest_Lid_Pivot");
        lidPivot.transform.SetParent(root.transform, false);
        lidPivot.transform.localPosition = new Vector3(0f, 0.72f, -0.45f);
        lidPivot.transform.localRotation = Quaternion.identity;

        // ฝาหีบสมบัติทรงโค้งแบบหีบสมบัติแฟนตาซี MMORPG (ยึดพิกัดเทียบกับจุดหมุนบานพับด้านหลัง)
        CreatePrimitivePart("Lid_WoodMain", PrimitiveType.Cube, lidPivot.transform,
            new Vector3(0f, 0.14f, 0.45f), new Vector3(1.44f, 0.28f, 0.92f), matWood);
        CreatePrimitivePart("Lid_WoodTopArch", PrimitiveType.Cube, lidPivot.transform,
            new Vector3(0f, 0.30f, 0.45f), new Vector3(1.38f, 0.14f, 0.68f), matWood);

        // แถบทองคาดฝาหีบและตัวล็อกด้านหน้า
        CreatePrimitivePart("Lid_GoldRim", PrimitiveType.Cube, lidPivot.transform,
            new Vector3(0f, 0.04f, 0.45f), new Vector3(1.50f, 0.08f, 0.97f), matGold);
        CreatePrimitivePart("Lid_GoldStrap_Left", PrimitiveType.Cube, lidPivot.transform,
            new Vector3(-0.48f, 0.21f, 0.45f), new Vector3(0.12f, 0.38f, 0.96f), matGold);
        CreatePrimitivePart("Lid_GoldStrap_Right", PrimitiveType.Cube, lidPivot.transform,
            new Vector3(0.48f, 0.21f, 0.45f), new Vector3(0.12f, 0.38f, 0.96f), matGold);
        CreatePrimitivePart("Lid_GoldLatch", PrimitiveType.Cube, lidPivot.transform,
            new Vector3(0f, 0.06f, 0.92f), new Vector3(0.14f, 0.16f, 0.07f), matGold);

        // --- 3. สร้างโหนดจุดกำเนิดเอฟเฟกต์แสงทอง (VFX_ChestLight_Origin) ภายในปากหีบสมบัติ ---
        GameObject vfxOrigin = new GameObject("VFX_ChestLight_Origin");
        vfxOrigin.transform.SetParent(root.transform, false);
        vfxOrigin.transform.localPosition = new Vector3(0f, 0.72f, 0f);
        vfxOrigin.transform.localRotation = Quaternion.identity;

        // ประกอบเอฟเฟกต์แสงทองครบทั้ง 4 เลเยอร์ตามกฎเหล็กข้อที่ 1
        ParticleSystem psL1 = BuildLayer1Shockwave(vfxOrigin.transform, matShockwave);
        ParticleSystem psL2 = BuildLayer2GoldenLightCone(vfxOrigin.transform, lightConeMesh, matLightCone);
        ParticleSystem psL3 = BuildLayer3FloatingSparkles(vfxOrigin.transform, matSparkles);
        ParticleSystem psL4 = BuildLayer4TreasureStarFlash(vfxOrigin.transform, matStarFlash);

        // ไฟ Point Light สีทองเสริมภายในหีบสมบัติ
        GameObject lightObj = new GameObject("Chest_GoldenPointLight");
        lightObj.transform.SetParent(vfxOrigin.transform, false);
        lightObj.transform.localPosition = new Vector3(0f, 0.32f, 0f);
        Light pointLight = lightObj.AddComponent<Light>();
        pointLight.type = LightType.Point;
        pointLight.color = new Color(1.0f, 0.80f, 0.32f, 1f);
        pointLight.range = 8.5f;
        pointLight.intensity = 0f;
        pointLight.enabled = false;

        // --- 4. ติดตั้งและผูกค่า MMOTreasureChestController อัตโนมัติ ---
        MMOTreasureChestController controller = root.AddComponent<MMOTreasureChestController>();
        controller.chestLidPivot = lidPivot.transform;
        controller.openAngleX = -105f;
        controller.closedAngleX = 0f;
        controller.openDuration = 0.55f;
        controller.closeDuration = 0.38f;
        controller.lootBurstTriggerProgress = 0.25f;
        controller.allowStandaloneInput = true;
        controller.toggleKey = KeyCode.G;
        controller.allowMouseClickOnChest = true;
        controller.vfxLightOrigin = vfxOrigin.transform;
        controller.l1LidCrackShockwave = psL1;
        controller.l2CoreGoldenLightCone = psL2;
        controller.l3FloatingGoldenSparkles = psL3;
        controller.l4TreasureStarFlash = psL4;
        controller.chestGoldenPointLight = pointLight;
        controller.maxPointLightIntensity = 6.5f;
        controller.showDebugHUD = true;

        EditorUtility.SetDirty(controller);
        return root;
    }

    // =================================================================================================
    // LAYER 1 (Anticipation / Lid Crack Shockwave - 0.00s - 0.25s)
    // =================================================================================================
    private static ParticleSystem BuildLayer1Shockwave(Transform parent, Material mat)
    {
        GameObject obj = new GameObject("L1_LidCrack_Shockwave");
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = new Vector3(0f, 0.08f, 0f);

        ParticleSystem ps = obj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.duration = 0.30f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = 0.25f;
        main.startSpeed = 0f;
        main.startSize = 3.2f; // กฎเหล็กข้อที่ 1: Start Size = 3.2
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(4.0f, 2.85f, 0.72f, 1f)); // สีทองสว่าง HDR
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 4;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new ParticleSystem.Burst[]
        {
            new ParticleSystem.Burst(0.0f, 2) // กฎเหล็กข้อที่ 1: Burst 2 วง
        });

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;

        // ขยายวงคลื่นอย่างรวดเร็วแบบ Explosive Ease-Out
        ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.separateAxes = false;
        AnimationCurve sizeCurve = new AnimationCurve(
            new Keyframe(0.0f, 0.16f, 4.8f, 4.8f),
            new Keyframe(0.45f, 0.82f, 0.9f, 0.9f),
            new Keyframe(1.0f, 1.0f, 0.0f, 0.0f));
        sol.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(1.0f, 0.95f, 0.78f), 0.0f),
                new GradientColorKey(new Color(1.0f, 0.78f, 0.22f), 0.55f),
                new GradientColorKey(new Color(0.95f, 0.55f, 0.10f), 1.0f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0.0f, 0.0f),
                new GradientAlphaKey(1.0f, 0.12f),
                new GradientAlphaKey(0.75f, 0.55f),
                new GradientAlphaKey(0.0f, 1.0f)
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard; // กฎเหล็กข้อที่ 1: Horizontal Billboard
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        return ps;
    }

    // =================================================================================================
    // LAYER 2 (Main Action / Core Golden Light Cone - เมื่อฝาเปิด 25%)
    // =================================================================================================
    private static ParticleSystem BuildLayer2GoldenLightCone(Transform parent, Mesh coneMesh, Material mat)
    {
        GameObject obj = new GameObject("L2_Core_GoldenLightCone");
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = new Vector3(0f, -0.06f, 0f);
        obj.transform.localRotation = Quaternion.identity;

        ParticleSystem ps = obj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.duration = 1.6f;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = 1.6f;
        main.startSpeed = 0f;
        main.startSize3D = true; // กฎเหล็กข้อที่ 1: ใช้ 3D Start Size
        main.startSizeX = 1.25f;
        main.startSizeY = 3.55f; // พุ่งสูงทะยานขึ้นฟ้าจากปากหีบ
        main.startSizeZ = 1.25f;
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(3.2f, 2.35f, 0.72f, 1f));
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 4;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0.85f;
        emission.SetBursts(new ParticleSystem.Burst[]
        {
            new ParticleSystem.Burst(0.0f, 1)
        });

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;

        // กฎเหล็กข้อที่ 1: Size over Lifetime (Separate Axes) ให้ลำแสงพุ่งยืดขึ้นฟ้าอย่างรวดเร็ว
        ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.separateAxes = true;

        AnimationCurve curveXZ = new AnimationCurve(
            new Keyframe(0.0f, 0.62f, 2.2f, 2.2f),
            new Keyframe(0.22f, 1.0f, 0.35f, 0.35f),
            new Keyframe(1.0f, 1.14f, 0.0f, 0.0f));

        AnimationCurve curveY = new AnimationCurve(
            new Keyframe(0.0f, 0.08f, 8.0f, 8.0f),
            new Keyframe(0.14f, 1.0f, 0.6f, 0.6f),
            new Keyframe(1.0f, 1.08f, 0.0f, 0.0f));

        sol.x = new ParticleSystem.MinMaxCurve(1f, curveXZ);
        sol.y = new ParticleSystem.MinMaxCurve(1f, curveY);
        sol.z = new ParticleSystem.MinMaxCurve(1f, curveXZ);

        // กฎเหล็กข้อที่ 1: หมุนช้าๆ รอบแกน Y (35 deg/s) และบังคับโหมด Constant ทุกแกนเพื่อความปลอดภัยใน Unity 6
        ParticleSystem.RotationOverLifetimeModule rol = ps.rotationOverLifetime;
        rol.enabled = true;
        rol.separateAxes = true;
        rol.x = new ParticleSystem.MinMaxCurve(0f);
        rol.y = new ParticleSystem.MinMaxCurve(35f * Mathf.Deg2Rad);
        rol.z = new ParticleSystem.MinMaxCurve(0f);

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(1.0f, 0.96f, 0.78f), 0.0f),
                new GradientColorKey(new Color(1.0f, 0.80f, 0.28f), 0.45f),
                new GradientColorKey(new Color(1.0f, 0.65f, 0.14f), 1.0f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0.0f, 0.0f),
                new GradientAlphaKey(0.95f, 0.14f),
                new GradientAlphaKey(0.85f, 0.65f),
                new GradientAlphaKey(0.0f, 1.0f)
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh; // กฎเหล็กข้อที่ 1: โหมด Mesh
        renderer.mesh = coneMesh;
        renderer.alignment = ParticleSystemRenderSpace.Local;
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        return ps;
    }

    // =================================================================================================
    // LAYER 3 (Secondary Elements / Floating Golden Sparkles - 0.15s - 2.00s)
    // =================================================================================================
    private static ParticleSystem BuildLayer3FloatingSparkles(Transform parent, Material mat)
    {
        GameObject obj = new GameObject("L3_Floating_GoldenSparkles");
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = new Vector3(0f, 0.08f, 0f);

        ParticleSystem ps = obj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.duration = 2.0f;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.25f, 1.85f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.75f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.11f, 0.28f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(3.6f, 2.95f, 1.35f, 1f),
            new Color(4.4f, 3.85f, 2.15f, 1f)); // ละอองดาวสีขาวอมทอง HDR
        main.simulationSpace = ParticleSystemSimulationSpace.World; // กฎเหล็กข้อที่ 1: World Space
        main.maxParticles = 140;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 24f; // กฎเหล็กข้อที่ 1: Rate over Time = 24
        emission.SetBursts(new ParticleSystem.Burst[]
        {
            new ParticleSystem.Burst(0.0f, 25) // กฎเหล็กข้อที่ 1: Burst 25 เม็ดตอนเปิด
        });

        // กฎเหล็กข้อที่ 1: Box Shape ขนาดพอดีปากหีบสมบัติ
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(1.12f, 0.12f, 0.62f);

        // กฎเหล็กข้อที่ 1 (กฎความปลอดภัย Unity 6):
        // ทุกแกนใน Velocity over Lifetime ต้องตั้งเป็น ParticleSystemCurveMode.Constant เหมือนกันหมด 100%!
        ParticleSystem.VelocityOverLifetimeModule vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = new ParticleSystem.MinMaxCurve(0f);
        vel.y = new ParticleSystem.MinMaxCurve(0.8f); // Linear Y = 0.8
        vel.z = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalX = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalY = new ParticleSystem.MinMaxCurve(1.8f); // Orbital Y = 1.8 (ลอยควงสว่านขึ้นด้านบน)
        vel.orbitalZ = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalOffsetX = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalOffsetY = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalOffsetZ = new ParticleSystem.MinMaxCurve(0f);
        vel.radial = new ParticleSystem.MinMaxCurve(0.16f);
        vel.speedModifier = new ParticleSystem.MinMaxCurve(1f);

        // ให้ละอองดาวระยิบระยับขยายแล้วค่อยๆ เล็กลงตอนจางหาย
        ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.separateAxes = false;
        AnimationCurve sparkleSize = new AnimationCurve(
            new Keyframe(0.0f, 0.0f),
            new Keyframe(0.18f, 1.0f),
            new Keyframe(0.50f, 0.72f),
            new Keyframe(0.72f, 0.96f),
            new Keyframe(1.0f, 0.0f));
        sol.size = new ParticleSystem.MinMaxCurve(1f, sparkleSize);

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(1.0f, 0.99f, 0.90f), 0.0f),
                new GradientColorKey(new Color(1.0f, 0.85f, 0.35f), 0.65f),
                new GradientColorKey(new Color(1.0f, 0.68f, 0.18f), 1.0f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0.0f, 0.0f),
                new GradientAlphaKey(1.0f, 0.15f),
                new GradientAlphaKey(0.85f, 0.75f),
                new GradientAlphaKey(0.0f, 1.0f)
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard; // กฎเหล็กข้อที่ 1: Billboard
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        return ps;
    }

    // =================================================================================================
    // LAYER 4 (Impact & Residual / Treasure Star Flash - 0.20s - 0.85s)
    // =================================================================================================
    private static ParticleSystem BuildLayer4TreasureStarFlash(Transform parent, Material mat)
    {
        GameObject obj = new GameObject("L4_Treasure_StarFlash");
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = new Vector3(0f, 0.32f, 0f);

        ParticleSystem ps = obj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.duration = 0.70f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = 0.65f; // กฎเหล็กข้อที่ 1: Start Lifetime = 0.65s
        main.startSpeed = 0f;
        main.startSize = 2.6f;      // กฎเหล็กข้อที่ 1: Start Size = 2.6
        // กฎเหล็กข้อที่ 1: สีขาวอมทอง HDR intensity 4.8x
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(4.8f, 3.9f, 1.8f, 1f));
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 2;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new ParticleSystem.Burst[]
        {
            new ParticleSystem.Burst(0.0f, 1)
        });

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;

        // กฎเหล็กข้อที่ 1: Size over Lifetime แบบ Explosive Ease-Out
        ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.separateAxes = false;
        AnimationCurve explosiveCurve = new AnimationCurve(
            new Keyframe(0.0f, 0.05f, 11.5f, 11.5f),
            new Keyframe(0.14f, 1.15f, 0.5f, 0.5f),
            new Keyframe(0.42f, 0.90f, -0.7f, -0.7f),
            new Keyframe(1.0f, 0.0f, -1.8f, -1.8f));
        sol.size = new ParticleSystem.MinMaxCurve(1f, explosiveCurve);

        // กฎเหล็กข้อที่ 1: หมุนแกน Z (ทุกแกนเป็น Constant mode เสมอกัน)
        ParticleSystem.RotationOverLifetimeModule rol = ps.rotationOverLifetime;
        rol.enabled = true;
        rol.separateAxes = true;
        rol.x = new ParticleSystem.MinMaxCurve(0f);
        rol.y = new ParticleSystem.MinMaxCurve(0f);
        rol.z = new ParticleSystem.MinMaxCurve(55f * Mathf.Deg2Rad);

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(1.0f, 1.0f, 0.95f), 0.0f),
                new GradientColorKey(new Color(1.0f, 0.88f, 0.45f), 0.45f),
                new GradientColorKey(new Color(1.0f, 0.65f, 0.15f), 1.0f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0.0f, 0.0f),
                new GradientAlphaKey(1.0f, 0.08f),
                new GradientAlphaKey(0.92f, 0.42f),
                new GradientAlphaKey(0.0f, 1.0f)
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard; // กฎเหล็กข้อที่ 1: Billboard
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        return ps;
    }

    // =================================================================================================
    // UTILITY HELPERS
    // =================================================================================================
    private static GameObject CreatePrimitivePart(
        string name,
        PrimitiveType type,
        Transform parent,
        Vector3 localPos,
        Vector3 localScale,
        Material mat)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPos;
        part.transform.localRotation = Quaternion.identity;
        part.transform.localScale = localScale;

        // ลบ Collider ย่อยออกเพื่อให้ใช้ BoxCollider หลักที่ตัวกล่องสมบัติเพียงตัวเดียว
        Collider col = part.GetComponent<Collider>();
        if (col != null)
        {
            Object.DestroyImmediate(col);
        }

        MeshRenderer mr = part.GetComponent<MeshRenderer>();
        if (mr != null && mat != null)
        {
            mr.sharedMaterial = mat;
        }

        return part;
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

    private static void ConfigureTextureImporter(string assetPath, TextureWrapMode wrapU, TextureWrapMode wrapV)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null) return;

        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.wrapModeU = wrapU;
        importer.wrapModeV = wrapV;
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
