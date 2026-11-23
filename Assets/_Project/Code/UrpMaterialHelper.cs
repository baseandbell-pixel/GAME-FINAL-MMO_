using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// ตัวช่วยจัดการ Material สำหรับ Universal Render Pipeline (URP) อย่างปลอดภัยทั้งใน Editor และ Standalone Build:
///   - โหลด Material ถาวรจาก Assets/_Project/Resources/Materials/ ที่ผูกกับ URP Shader จริงไว้แล้ว
///     (Unity จะไม่ทำการ Strip Shader ที่ถูกอ้างอิงผ่านไฟล์ .mat ในโฟลเดอร์ Resources ตอน Build เด็ดขาด)
///   - ป้องกันปัญหาโมเดล Primitive ที่สร้างตอน Runtime (เช่น Comfort Zone, Boss Arena, Chest, VFX) กลายเป็นสีชมพู/บานเย็น (Magenta)
///   - ใช้ MaterialPropertyBlock ในการเปลี่ยนสี (_BaseColor / _Color) เพื่อไม่ให้เกิด Memory Leak จากการสร้าง Material ซ้ำ
/// </summary>
public static class UrpMaterialHelper
{
    private const string LitResourcePath = "Materials/URP_RuntimeLit";
    private const string UnlitOpaqueResourcePath = "Materials/URP_RuntimeUnlitOpaque";
    private const string UnlitTransparentResourcePath = "Materials/URP_RuntimeUnlitTransparent";

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private static Material s_litMat;
    private static Material s_unlitOpaqueMat;
    private static Material s_unlitTransparentMat;

    /// <summary>
    /// คืนค่า Material URP Lit (Opaque) ถาวรจาก Resources หรือสร้างสำรองจาก URP Shader ที่พร้อมใช้งาน
    /// </summary>
    public static Material GetLitMaterial()
    {
        if (IsValidUrpMaterial(s_litMat)) return s_litMat;

        s_litMat = Resources.Load<Material>(LitResourcePath);
        if (!IsValidUrpMaterial(s_litMat))
        {
            Shader s = Shader.Find("Universal Render Pipeline/Lit")
                       ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                       ?? Shader.Find("Universal Render Pipeline/Unlit");
            if (s != null)
            {
                s_litMat = new Material(s) { name = "URP_RuntimeLit_Fallback" };
                s_litMat.SetColor(BaseColorId, Color.white);
                s_litMat.SetColor(ColorId, Color.white);
            }
        }
        return s_litMat;
    }

    /// <summary>
    /// คืนค่า Material URP Unlit (Opaque) ถาวรสำหรับวัตถุเรืองแสง คริสตัล และลูกไฟพลังงาน
    /// </summary>
    public static Material GetUnlitOpaqueMaterial()
    {
        if (IsValidUrpMaterial(s_unlitOpaqueMat)) return s_unlitOpaqueMat;

        s_unlitOpaqueMat = Resources.Load<Material>(UnlitOpaqueResourcePath);
        if (!IsValidUrpMaterial(s_unlitOpaqueMat))
        {
            Shader s = Shader.Find("Universal Render Pipeline/Unlit")
                       ?? Shader.Find("Universal Render Pipeline/Lit");
            if (s != null)
            {
                s_unlitOpaqueMat = new Material(s) { name = "URP_RuntimeUnlitOpaque_Fallback" };
                s_unlitOpaqueMat.SetColor(BaseColorId, Color.white);
                s_unlitOpaqueMat.SetColor(ColorId, Color.white);
            }
        }
        return s_unlitOpaqueMat;
    }

    /// <summary>
    /// คืนค่า Material URP Unlit (Transparent Alpha Blend) ถาวรสำหรับวง Telegraph, โล่ป้องกัน และลำแสง Waypoint
    /// </summary>
    public static Material GetUnlitTransparentMaterial()
    {
        if (IsValidUrpMaterial(s_unlitTransparentMat)) return s_unlitTransparentMat;

        s_unlitTransparentMat = Resources.Load<Material>(UnlitTransparentResourcePath);
        if (!IsValidUrpMaterial(s_unlitTransparentMat))
        {
            Shader s = Shader.Find("Universal Render Pipeline/Unlit")
                       ?? Shader.Find("Sprites/Default");
            if (s != null)
            {
                s_unlitTransparentMat = new Material(s) { name = "URP_RuntimeUnlitTransparent_Fallback" };
                ConfigureUrpTransparent(s_unlitTransparentMat);
            }
        }
        return s_unlitTransparentMat;
    }

    /// <summary>
    /// แนบ Material URP Lit และทาสีด้วย MaterialPropertyBlock ให้กับ Renderer (เหมาะกับพื้น เสา กล่องสมบัติ และสิ่งกีดขวาง)
    /// </summary>
    public static void ApplyLitColor(Renderer renderer, Color color, bool disableShadows = false)
    {
        if (renderer == null) return;
        Material mat = GetLitMaterial();
        if (mat != null) renderer.sharedMaterial = mat;
        if (disableShadows)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
        ApplyColorBlock(renderer, color);
    }

    /// <summary>
    /// แนบ Material URP Unlit (ทึบแสง) และทาสีด้วย MaterialPropertyBlock (เหมาะกับคริสตัล ลูกไฟ และวงเรืองแสง)
    /// </summary>
    public static void ApplyUnlitColor(Renderer renderer, Color color, bool disableShadows = true)
    {
        if (renderer == null) return;
        Material mat = GetUnlitOpaqueMaterial();
        if (mat != null) renderer.sharedMaterial = mat;
        if (disableShadows)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
        ApplyColorBlock(renderer, color);
    }

    /// <summary>
    /// แนบ Material URP Unlit แบบโปร่งแสง (Transparent) และทาสีพร้อมค่า Alpha ด้วย MaterialPropertyBlock
    /// </summary>
    public static void ApplyTransparentColor(Renderer renderer, Color color, bool disableShadows = true)
    {
        if (renderer == null) return;
        Material mat = GetUnlitTransparentMaterial();
        if (mat != null) renderer.sharedMaterial = mat;
        if (disableShadows)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
        ApplyColorBlock(renderer, color);
    }

    /// <summary>
    /// ตรวจสอบและซ่อมแซม Renderer หากพบว่า Material หาย, Shader เป็น Error (สีชมพู) หรือใช้ Built-in Standard ในโปรเจกต์ URP
    /// </summary>
    public static bool EnsureValidUrpMaterial(Renderer renderer, bool transparent = false)
    {
        if (renderer == null) return false;
        if (IsValidUrpMaterial(renderer.sharedMaterial)) return false;

        Material replacement = transparent ? GetUnlitTransparentMaterial() : GetLitMaterial();
        if (replacement == null) return false;

        Color preservedColor = Color.white;
        if (renderer.sharedMaterial != null)
        {
            if (renderer.sharedMaterial.HasProperty(BaseColorId))
                preservedColor = renderer.sharedMaterial.GetColor(BaseColorId);
            else if (renderer.sharedMaterial.HasProperty(ColorId))
                preservedColor = renderer.sharedMaterial.GetColor(ColorId);
        }

        renderer.sharedMaterial = replacement;
        ApplyColorBlock(renderer, preservedColor);
        return true;
    }

    /// <summary>
    /// ตรวจสอบว่า Material ที่ส่งเข้ามาใช้งานได้จริงใน URP (ไม่ใช่ null, ไม่ใช่ Error Shader สีชมพู และไม่ใช่ Built-in Standard)
    /// </summary>
    public static bool IsValidUrpMaterial(Material mat)
    {
        if (mat == null || mat.shader == null) return false;
        string shaderName = mat.shader.name;
        if (string.IsNullOrEmpty(shaderName)) return false;
        if (shaderName == "Hidden/InternalErrorShader") return false;
        if (shaderName == "Standard" || shaderName == "Standard (Specular setup)") return false;
        if (shaderName.StartsWith("Legacy Shaders/")) return false;
        return true;
    }

    private static void ApplyColorBlock(Renderer renderer, Color color)
    {
        var mpb = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(mpb);
        mpb.SetColor(BaseColorId, color);
        mpb.SetColor(ColorId, color);
        renderer.SetPropertyBlock(mpb);
    }

    /// <summary>
    /// ตั้งค่า Render State ของ URP Unlit Material ให้โปร่งแสงแบบ Alpha Blend อย่างถูกต้อง
    /// </summary>
    public static void ConfigureUrpTransparent(Material mat)
    {
        if (mat == null) return;
        mat.SetColor(BaseColorId, Color.white);
        mat.SetColor(ColorId, Color.white);
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
        if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
        if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.renderQueue = (int)RenderQueue.Transparent;
    }

    /// <summary>
    /// สร้างเส้นวงกลมขอบเขตบนพื้นด้วย LineRenderer ที่ใช้ Material URP Unlit ถาวร (ไม่เป็นสีชมพูทั้งใน Editor และ Build)
    /// วางนอนขนานกับพื้นโลก (LineAlignment.TransformZ + หมุนแกน X 90 องศา) เพื่อให้คมชัดจากทุกมุมกล้องและแผนที่
    /// </summary>
    public static LineRenderer CreateGroundCircleRing(
        Transform parent,
        string ringName,
        Vector3 worldCenter,
        float radius,
        float lineWidth,
        Color color,
        int segments = 64,
        float yHeight = 0.06f)
    {
        var go = new GameObject(ringName);
        if (parent != null) go.transform.SetParent(parent, true);
        float ringY = SandboxWorldSpawner.SampleGroundY(worldCenter, worldCenter.y) + yHeight;
        go.transform.position = new Vector3(worldCenter.x, ringY, worldCenter.z);
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.loop = true;
        lr.alignment = LineAlignment.TransformZ;
        lr.startWidth = lineWidth;
        lr.endWidth = lineWidth;
        lr.numCornerVertices = 2;
        lr.numCapVertices = 2;
        lr.shadowCastingMode = ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.startColor = color;
        lr.endColor = color;

        // ผูก URP Unlit Transparent Material ถาวรและตั้งสีผ่าน MaterialPropertyBlock เพื่อรองรับ URP โดยไม่เป็นสีชมพู
        ApplyTransparentColor(lr, color, disableShadows: true);
        UpdateGroundCircleRing(lr, worldCenter, radius, segments, yHeight);
        return lr;
    }

    /// <summary>
    /// อัปเดตตำแหน่งและรัศมีของเส้นวงกลมขอบเขตบนพื้น (LineRenderer)
    /// </summary>
    public static void UpdateGroundCircleRing(LineRenderer lr, Vector3 worldCenter, float radius, int segments = 64, float yHeight = 0.06f)
    {
        if (lr == null) return;
        int seg = Mathf.Clamp(segments, 16, 180);
        float r = Mathf.Max(0.25f, radius);
        float ringY = SandboxWorldSpawner.SampleGroundY(worldCenter, worldCenter.y) + yHeight;
        lr.transform.position = new Vector3(worldCenter.x, ringY, worldCenter.z);
        lr.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        lr.positionCount = seg;

        for (int i = 0; i < seg; i++)
        {
            float angle = (i / (float)seg) * Mathf.PI * 2f;
            lr.SetPosition(i, new Vector3(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r, 0f));
        }
    }
}
