using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// ระบบควบคุม "การเปิด-ปิดฝาหีบสมบัติจากบานพับและเอฟเฟกต์แสงทองพุ่ง 4 เลเยอร์ระดับ AAA"
/// (Treasure Chest Open & Golden Loot Burst VFX) สำหรับ Unity 6 (URP)
/// - รองรับทั้งกล่องทดสอบ (กดปุ่ม G / คลิกซ้าย) และกล่องสมบัติจริงในเกม (LootChest กดปุ่ม E)
/// - มีระบบ Runtime Self-Healing Procedural VFX: หากโหนดเอฟเฟกต์ทั้ง 4 เลเยอร์ยังไม่ได้สร้างไว้
///   สคริปต์จะสร้าง Mesh_LootLightCone + 3 Procedural Textures + URP Additive HDR Materials + 4 ParticleSystems ให้อัตโนมัติทันที!
/// - หมุนเปิดฝาหีบด้วยสมการ Back Ease-Out (openAngleX = -105f) ให้ความรู้สึกมีน้ำหนักและดีดตัว
/// - จุดระเบิด Layer 1 ทันทีที่ฝาเริ่มแง้ม และจุดระเบิด Layer 2, 3, 4 พร้อมกันทันทีที่ฝาเปิดถึง 25%
/// </summary>
[AddComponentMenu("yeen VFX/11. [Treasure Chest] เปิดหีบสมบัติแสงทอง (Treasure Chest Burst)")]
[DisallowMultipleComponent]
public class MMOTreasureChestController : MonoBehaviour
{
    [Header("=== 1. การตั้งค่าบานพับฝาหีบสมบัติ (Chest Lid Hinge Settings) ===")]
    [Tooltip("Transform จุดหมุนบานพับของฝาหีบสมบัติ (วางไว้ที่ขอบหลังด้านบนของตัวกล่อง)")]
    public Transform chestLidPivot;

    [Tooltip("มุมองศาแกน X ตอนฝาหีบเปิดสุด (ค่าเริ่มต้น = -105 องศา ให้ฝาหีบเปิดง้างไปด้านหลัง)")]
    public float openAngleX = -105f;

    [Tooltip("มุมองศาแกน X ตอนฝาหีบปิดสนิท")]
    public float closedAngleX = 0f;

    [Tooltip("ระยะเวลาในการหมุนเปิดฝาหีบ (วินาที)")]
    [Range(0.25f, 1.5f)]
    public float openDuration = 0.55f;

    [Tooltip("ระยะเวลาในการหมุนปิดฝาหีบ (วินาที)")]
    [Range(0.2f, 1.2f)]
    public float closeDuration = 0.38f;

    [Tooltip("สัดส่วนระยะการเปิดฝาที่สั่งจุดระเบิดลำแสงทอง Layer 2, 3, 4 (0.25 = 25% ของระยะการเปิดฝา)")]
    [Range(0.05f, 0.75f)]
    public float lootBurstTriggerProgress = 0.25f;

    [Header("=== 2. ปุ่มกดและการโต้ตอบ (Input & Interaction) ===")]
    [Tooltip("อนุญาตให้สคริปต์นี้รับปุ่มกดคีย์บอร์ดโดยตรง (เปิดไว้สำหรับกล่องทดสอบ, ปิดไว้เมื่อถูกควบคุมโดย LootChest)")]
    public bool allowStandaloneInput = true;

    [Tooltip("ปุ่มคีย์บอร์ดสำหรับสลับเปิด-ปิดกล่องสมบัติ (ค่าเริ่มต้น = G)")]
    public KeyCode toggleKey = KeyCode.G;

    [Tooltip("อนุญาตให้คลิกเมาส์ซ้ายที่ตัวกล่องสมบัติเพื่อสลับเปิด-ปิดได้")]
    public bool allowMouseClickOnChest = true;

    [Header("=== 3. โครงสร้างเอฟเฟกต์แสงทอง 4 เลเยอร์ (4-Layer Golden Loot VFX) ===")]
    [Tooltip("โหนดจุดกำเนิดเอฟเฟกต์แสงทองทั้งหมดภายในปากหีบสมบัติ")]
    public Transform vfxLightOrigin;

    [Tooltip("ตำแหน่ง Local เริ่มต้นของจุดกำเนิดเอฟเฟกต์ (หากต้องสร้างอัตโนมัติตอน Runtime)")]
    public Vector3 defaultOriginLocalPosition = new Vector3(0f, 0.56f, 0f);

    [Tooltip("Layer 1 (0.00s - 0.25s): วงแหวนคลื่นแสงสีทอง Horizontal Billboard ระเบิดออกรอบกล่องตอนฝาเริ่มแง้ม")]
    public ParticleSystem l1LidCrackShockwave;

    [Tooltip("Layer 2 (เมื่อฝาเปิด 25%): กรวยลำแสงทอง 3D Mesh พุ่งทะยานขึ้นฟ้าพร้อมหมุนช้าๆ รอบแกน Y")]
    public ParticleSystem l2CoreGoldenLightCone;

    [Tooltip("Layer 3 (0.15s - 2.00s): ละอองดาวสีขาวอมทองลอยควงสว่านขึ้นด้านบน (Burst 25 + Rate over Time 24)")]
    public ParticleSystem l3FloatingGoldenSparkles;

    [Tooltip("Layer 4 (0.20s - 0.85s): แสงแฟลชประกายดาว 4 แฉกขนาดใหญ่สีทอง HDR ระเบิดกลางกล่อง")]
    public ParticleSystem l4TreasureStarFlash;

    [Tooltip("ไฟ Point Light สีทองเสริมภายในหีบสมบัติ")]
    public Light chestGoldenPointLight;

    [Tooltip("ความสว่างสูงสุดของ Point Light สีทองเมื่อเปิดกล่อง")]
    public float maxPointLightIntensity = 6.5f;

    [Header("=== 4. หน้าต่างแสดงสถานะบนหน้าจอ (On-Screen Debug HUD) ===")]
    [Tooltip("แสดงกรอบข้อมูลสถานะกล่องสมบัติมุมซ้ายบนของหน้าจอขณะทดสอบ (เอาติ๊กออกเมื่อใช้งานจริง)")]
    public bool showDebugHUD = true;

    [Header("=== 5. สถานะปัจจุบันของกล่องสมบัติ (Read-Only) ===")]
    [SerializeField] private bool isChestOpen = false;
    [SerializeField] private bool isAnimating = false;
    [SerializeField] private float currentLidOpenProgress = 0f;

    private Coroutine activeLidCoroutine;
    private Vector3 initialLidEuler;
    private bool hasSavedInitialLidEuler = false;

    // แคชทรัพยากร Procedural ระดับ Runtime เพื่อใช้ร่วมกันทุกกล่องสมบัติ (เอฟเฟกต์เปิดกล่องสีทอง + วัสดุเส้นแสงตามไอเทม)
    private static Mesh s_RuntimeLightConeMesh;
    private static Texture2D s_RuntimeGodRaysTex;
    private static Texture2D s_RuntimeSparkleStarTex;
    private static Texture2D s_RuntimeShockwaveRingTex;
    private static Texture2D s_RuntimeStreakRibbonTex;
    private static Material s_RuntimeMatShockwave;
    private static Material s_RuntimeMatLightCone;
    private static Material s_RuntimeMatSparkles;
    private static Material s_RuntimeMatStarFlash;
    private static Material s_RuntimeMatLootArcTrail;

    private void Awake()
    {
        AutoBindReferences();
        EnsureVFXRigBuilt();

        if (chestLidPivot != null)
        {
            initialLidEuler = chestLidPivot.localEulerAngles;
            hasSavedInitialLidEuler = true;
            ApplyLidAngleX(closedAngleX);
        }

        StopAndResetAllChestVFX(true);
    }

    /// <summary>
    /// ผูกระบบเอฟเฟกต์แสงทอง 4 เลเยอร์เข้ากับกล่องสมบัติภายนอก (เช่น LootChest ในเกมจริง)
    /// </summary>
    public void BindExternalChest(
        Transform lidPivot,
        Vector3 originLocalPosition,
        Light dedicatedPointLight = null,
        bool enableStandaloneInput = false,
        bool enableDebugHud = false)
    {
        allowStandaloneInput = enableStandaloneInput;
        allowMouseClickOnChest = enableStandaloneInput;
        showDebugHUD = enableDebugHud;
        defaultOriginLocalPosition = originLocalPosition;

        if (lidPivot != null)
        {
            chestLidPivot = lidPivot;
            initialLidEuler = chestLidPivot.localEulerAngles;
            hasSavedInitialLidEuler = true;
            ApplyLidAngleX(closedAngleX);
        }

        if (dedicatedPointLight != null)
        {
            chestGoldenPointLight = dedicatedPointLight;
        }

        EnsureVFXRigBuilt();

        if (vfxLightOrigin != null)
        {
            vfxLightOrigin.localPosition = originLocalPosition;
            vfxLightOrigin.localRotation = Quaternion.identity;
        }

        StopAndResetAllChestVFX(true);
    }

    public void AutoBindReferences()
    {
        if (chestLidPivot == null)
        {
            chestLidPivot = FindDeepChild(transform, "Chest_Lid_Pivot");
            if (chestLidPivot == null)
            {
                chestLidPivot = FindDeepChild(transform, "LidPivot");
            }
        }

        if (vfxLightOrigin == null)
        {
            vfxLightOrigin = FindDeepChild(transform, "VFX_ChestLight_Origin");
        }

        Transform searchRoot = vfxLightOrigin != null ? vfxLightOrigin : transform;

        if (l1LidCrackShockwave == null)
        {
            Transform t = FindDeepChild(searchRoot, "L1_LidCrack_Shockwave");
            if (t != null) l1LidCrackShockwave = t.GetComponent<ParticleSystem>();
        }

        if (l2CoreGoldenLightCone == null)
        {
            Transform t = FindDeepChild(searchRoot, "L2_Core_GoldenLightCone");
            if (t != null) l2CoreGoldenLightCone = t.GetComponent<ParticleSystem>();
        }

        if (l3FloatingGoldenSparkles == null)
        {
            Transform t = FindDeepChild(searchRoot, "L3_Floating_GoldenSparkles");
            if (t != null) l3FloatingGoldenSparkles = t.GetComponent<ParticleSystem>();
        }

        if (l4TreasureStarFlash == null)
        {
            Transform t = FindDeepChild(searchRoot, "L4_Treasure_StarFlash");
            if (t != null) l4TreasureStarFlash = t.GetComponent<ParticleSystem>();
        }

        if (chestGoldenPointLight == null)
        {
            Transform t = FindDeepChild(searchRoot, "Chest_GoldenPointLight");
            if (t != null) chestGoldenPointLight = t.GetComponent<Light>();
        }
    }

    /// <summary>
    /// ตรวจสอบและสร้างโหนดเอฟเฟกต์เปิดกล่องสีทอง 4 เลเยอร์ให้อัตโนมัติตอน Runtime หากกล่องสมบัติยังไม่มี
    /// </summary>
    public void EnsureVFXRigBuilt()
    {
        AutoBindReferences();

        bool missingAnyLayer =
            l1LidCrackShockwave == null ||
            l2CoreGoldenLightCone == null ||
            l3FloatingGoldenSparkles == null ||
            l4TreasureStarFlash == null;

        if (!missingAnyLayer)
        {
            ValidateExistingMaterials();
            return;
        }

        EnsureRuntimeSharedAssets();

        if (vfxLightOrigin == null)
        {
            GameObject originGo = new GameObject("VFX_ChestLight_Origin");
            originGo.transform.SetParent(transform, false);
            originGo.transform.localPosition = defaultOriginLocalPosition;
            originGo.transform.localRotation = Quaternion.identity;
            vfxLightOrigin = originGo.transform;
        }

        if (l1LidCrackShockwave == null)
        {
            l1LidCrackShockwave = CreateRuntimeLayer1Shockwave(vfxLightOrigin, s_RuntimeMatShockwave);
        }

        if (l2CoreGoldenLightCone == null)
        {
            l2CoreGoldenLightCone = CreateRuntimeLayer2LightCone(vfxLightOrigin, s_RuntimeLightConeMesh, s_RuntimeMatLightCone);
        }

        if (l3FloatingGoldenSparkles == null)
        {
            l3FloatingGoldenSparkles = CreateRuntimeLayer3Sparkles(vfxLightOrigin, s_RuntimeMatSparkles);
        }

        if (l4TreasureStarFlash == null)
        {
            l4TreasureStarFlash = CreateRuntimeLayer4StarFlash(vfxLightOrigin, s_RuntimeMatStarFlash);
        }

        if (chestGoldenPointLight == null)
        {
            GameObject lightObj = new GameObject("Chest_GoldenPointLight");
            lightObj.transform.SetParent(vfxLightOrigin, false);
            lightObj.transform.localPosition = new Vector3(0f, 0.34f, 0f);
            chestGoldenPointLight = lightObj.AddComponent<Light>();
            chestGoldenPointLight.type = LightType.Point;
            chestGoldenPointLight.color = new Color(1.0f, 0.80f, 0.32f, 1f);
            chestGoldenPointLight.range = 8.5f;
            chestGoldenPointLight.intensity = 0f;
            chestGoldenPointLight.enabled = false;
        }

        ValidateExistingMaterials();
    }

    private void ValidateExistingMaterials()
    {
        EnsureRendererMaterial(l1LidCrackShockwave, () => { EnsureRuntimeSharedAssets(); return s_RuntimeMatShockwave; });
        EnsureRendererMaterial(l2CoreGoldenLightCone, () => { EnsureRuntimeSharedAssets(); return s_RuntimeMatLightCone; });
        EnsureRendererMaterial(l3FloatingGoldenSparkles, () => { EnsureRuntimeSharedAssets(); return s_RuntimeMatSparkles; });
        EnsureRendererMaterial(l4TreasureStarFlash, () => { EnsureRuntimeSharedAssets(); return s_RuntimeMatStarFlash; });

        if (l2CoreGoldenLightCone != null)
        {
            ParticleSystemRenderer r2 = l2CoreGoldenLightCone.GetComponent<ParticleSystemRenderer>();
            if (r2 != null && r2.renderMode == ParticleSystemRenderMode.Mesh && r2.mesh == null)
            {
                EnsureRuntimeSharedAssets();
                r2.mesh = s_RuntimeLightConeMesh;
            }
        }
    }

    private static void EnsureRendererMaterial(ParticleSystem ps, System.Func<Material> fallbackProvider)
    {
        if (ps == null) return;
        ParticleSystemRenderer r = ps.GetComponent<ParticleSystemRenderer>();
        if (r == null) return;

        if (r.sharedMaterial == null || r.sharedMaterial.shader == null || r.sharedMaterial.shader.name == "Hidden/InternalErrorShader")
        {
            r.sharedMaterial = fallbackProvider();
        }
    }

    private void Update()
    {
        if (!allowStandaloneInput && !allowMouseClickOnChest) return;

        // 1. ตรวจจับการกดปุ่ม G ทั้งจากระบบใหม่ (New Input System) และระบบเก่า (Legacy Input)
        if (allowStandaloneInput && ReadUniversalToggleKeyPressed())
        {
            ToggleChest();
        }

        // 2. ตรวจจับการคลิกซ้ายที่ตัวกล่องสมบัติผ่าน New Input System Raycast (รองรับคู่กับ OnMouseDown)
        if (allowMouseClickOnChest && CheckNewInputMouseClickOnChest())
        {
            ToggleChest();
        }
    }

    /// <summary>
    /// รองรับการคลิกซ้ายที่กล่องสมบัติโดยตรงผ่านระบบ OnMouseDown ของ Unity
    /// </summary>
    private void OnMouseDown()
    {
        if (!allowMouseClickOnChest) return;
        // ป้องกันการเรียกซ้ำในเฟรมเดียวหากระบบ New Input System Raycast ทำงานไปแล้ว
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) return;

        ToggleChest();
    }

    /// <summary>
    /// สลับสถานะเปิด/ปิดกล่องสมบัติ
    /// </summary>
    public void ToggleChest()
    {
        if (isChestOpen)
        {
            TriggerCloseChest();
        }
        else
        {
            TriggerOpenChest();
        }
    }

    /// <summary>
    /// ฟังก์ชัน Public สำหรับสั่งเปิดกล่องสมบัติด้วยแสงสีทอง (คงเมธอดนี้ไว้เพื่อความเข้ากันได้)
    /// </summary>
    public void TriggerOpenChestForLoot(ItemTier highestTier, IList<ItemTier> droppedTiers = null)
    {
        TriggerOpenChest();
    }

    /// <summary>
    /// ฟังก์ชัน Public สำหรับสั่งเปิดกล่องสมบัติและจุดระเบิดเอฟเฟกต์แสงสีทอง 4 เลเยอร์
    /// </summary>
    [ContextMenu("เปิดกล่องสมบัติและปล่อยแสงสีทอง (Trigger Open Chest)")]
    public void TriggerOpenChest()
    {
        EnsureVFXRigBuilt();

        if (isChestOpen && !isAnimating) return;

        if (activeLidCoroutine != null)
        {
            StopCoroutine(activeLidCoroutine);
        }

        if (!gameObject.activeInHierarchy)
        {
            return;
        }

        activeLidCoroutine = StartCoroutine(OpenChestSequenceRoutine());
    }

    /// <summary>
    /// สั่งปิดฝาหีบสมบัติและค่อยๆ ดับเอฟเฟกต์แสงอย่างนุ่มนวล
    /// </summary>
    [ContextMenu("ปิดกล่องสมบัติ (Trigger Close Chest)")]
    public void TriggerCloseChest()
    {
        if (!isChestOpen && !isAnimating) return;

        if (activeLidCoroutine != null)
        {
            StopCoroutine(activeLidCoroutine);
        }

        if (!gameObject.activeInHierarchy)
        {
            return;
        }

        activeLidCoroutine = StartCoroutine(CloseChestSequenceRoutine());
    }

    /// <summary>
    /// สั่งค่อยๆ ดับเอฟเฟกต์แสงและไฟสีทองอย่างนุ่มนวล (ใช้ตอนกล่องสมบัติกำลังจางหายไปหลังเปิดแล้ว)
    /// </summary>
    public void StopVFXSoftly()
    {
        StopAndResetAllChestVFX(false);
        if (chestGoldenPointLight != null)
        {
            chestGoldenPointLight.intensity = 0f;
            chestGoldenPointLight.enabled = false;
        }
    }

    private IEnumerator OpenChestSequenceRoutine()
    {
        isAnimating = true;
        isChestOpen = true;

        if (vfxLightOrigin != null && !vfxLightOrigin.gameObject.activeSelf)
        {
            vfxLightOrigin.gameObject.SetActive(true);
        }

        // =========================================================================
        // จังหวะที่ 1 (0.00s - 0.25s): ฝาหีบเริ่มแง้ม -> จุดระเบิด Layer 1 (Lid Crack Shockwave) ทันที!
        // =========================================================================
        if (l1LidCrackShockwave != null)
        {
            l1LidCrackShockwave.gameObject.SetActive(true);
            ParticleSystem.EmissionModule em1 = l1LidCrackShockwave.emission;
            em1.enabled = true;
            l1LidCrackShockwave.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            l1LidCrackShockwave.Play(true);
        }

        float startAngle = GetCurrentLidAngleX();
        float elapsed = 0f;
        bool hasTriggeredMainLootBurst = false;

        while (elapsed < openDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, openDuration));
            currentLidOpenProgress = t;

            // กฎเหล็กข้อที่ 2: ใช้สมการ Back Ease-Out ให้ฝาหีบดีดเปิดขึ้นอย่างมีน้ำหนักระดับ AAA
            float easedT = EvaluateBackEaseOut(t);
            float angleX = Mathf.LerpUnclamped(startAngle, openAngleX, easedT);
            ApplyLidAngleX(angleX);

            if (chestGoldenPointLight != null)
            {
                chestGoldenPointLight.enabled = true;
                chestGoldenPointLight.intensity = Mathf.Lerp(0f, maxPointLightIntensity, Mathf.Clamp01(t * 1.4f));
            }

            // =========================================================================
            // จังหวะที่ 2 (เมื่อฝาหีบเปิดถึง 25%): สั่งระเบิด Layer 2, Layer 3 และ Layer 4 พร้อมกัน!
            // =========================================================================
            if (!hasTriggeredMainLootBurst && t >= lootBurstTriggerProgress)
            {
                hasTriggeredMainLootBurst = true;
                FireMainGoldenLootBurstLayers();
            }

            yield return null;
        }

        // รับประกันว่าหาก openDuration สั้นมาก Layer 2, 3, 4 จะต้องถูกยิงแน่นอน
        if (!hasTriggeredMainLootBurst)
        {
            FireMainGoldenLootBurstLayers();
        }

        ApplyLidAngleX(openAngleX);
        currentLidOpenProgress = 1f;

        if (chestGoldenPointLight != null)
        {
            chestGoldenPointLight.enabled = true;
            chestGoldenPointLight.intensity = maxPointLightIntensity;
        }

        isAnimating = false;
        activeLidCoroutine = null;
    }

    private void FireMainGoldenLootBurstLayers()
    {
        // Layer 2: Core Golden Light Cone (กรวยลำแสงทอง 3D Mesh พุ่งทะยานขึ้นฟ้าและหมุนช้าๆ รอบแกน Y)
        if (l2CoreGoldenLightCone != null)
        {
            l2CoreGoldenLightCone.gameObject.SetActive(true);
            l2CoreGoldenLightCone.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.EmissionModule em2 = l2CoreGoldenLightCone.emission;
            em2.enabled = true;
            l2CoreGoldenLightCone.Play(true);
        }

        // Layer 3: Floating Golden Sparkles (ละอองดาวสีทองลอยควงสว่านขึ้นด้านบน)
        if (l3FloatingGoldenSparkles != null)
        {
            l3FloatingGoldenSparkles.gameObject.SetActive(true);
            ParticleSystem.EmissionModule em3 = l3FloatingGoldenSparkles.emission;
            em3.enabled = true;
            if (l3FloatingGoldenSparkles.isPlaying)
            {
                l3FloatingGoldenSparkles.Emit(25);
            }
            else
            {
                l3FloatingGoldenSparkles.Play(true);
            }
        }

        // Layer 4: Treasure Star Flash (แสงแฟลชประกายดาว 4 แฉกสีทองขนาดใหญ่ระเบิดจ้าตรงกลางกล่อง)
        if (l4TreasureStarFlash != null)
        {
            l4TreasureStarFlash.gameObject.SetActive(true);
            ParticleSystem.EmissionModule em4 = l4TreasureStarFlash.emission;
            em4.enabled = true;
            l4TreasureStarFlash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            l4TreasureStarFlash.Play(true);
        }
    }

    /// <summary>
    /// คืนค่าสีเส้นแสงประจำระดับไอเทมสำหรับเส้นหางตอนไอเทมลอยออกจากกล่อง
    /// Normal = ขาวเงิน, Rare = ฟ้า, Epic = ม่วง, Legendary = ทอง
    /// </summary>
    public static Color GetTierVfxColor(ItemTier tier)
    {
        switch (tier)
        {
            case ItemTier.Rare:
                return new Color(0.18f, 0.72f, 1.00f, 1f); // ฟ้าสดใส (Rare)
            case ItemTier.Epic:
                return new Color(0.82f, 0.24f, 1.00f, 1f); // ม่วงเจิดจ้า (Epic)
            case ItemTier.Legendary:
                return new Color(1.00f, 0.78f, 0.14f, 1f); // ทองอร่าม (Legendary)
            default:
                return new Color(0.88f, 0.94f, 1.00f, 1f); // ขาวเงินสะอาด (Normal)
        }
    }

    /// <summary>
    /// สร้าง/ดึง Material สำหรับ TrailRenderer เส้นแสงสีตามระดับไอเทมที่ลอยตามตัวไอเทมตอนกระเด็นออกจากกล่อง
    /// </summary>
    public static Material GetOrCreateLootArcTrailMaterial()
    {
        EnsureRuntimeSharedAssets();
        return s_RuntimeMatLootArcTrail;
    }

    /// <summary>
    /// สร้าง TrailRenderer เส้นแสงสีตามระดับไอเทม (พร้อมจางหายเมื่อสั่ง emitting = false ตอนตกพื้น)
    /// </summary>
    public static TrailRenderer AttachLootArcTrail(Transform parent, ItemTier tier, float trailLifetime = 0.36f)
    {
        GameObject trailObj = new GameObject("VFX_ItemLootArcTrail_" + tier);
        trailObj.transform.SetParent(parent, false);
        trailObj.transform.localPosition = Vector3.zero;
        trailObj.transform.localRotation = Quaternion.identity;

        TrailRenderer tr = trailObj.AddComponent<TrailRenderer>();
        tr.time = trailLifetime;
        tr.minVertexDistance = 0.02f;
        tr.autodestruct = false;
        tr.emitting = false;
        tr.numCornerVertices = 6;
        tr.numCapVertices = 4;
        tr.alignment = LineAlignment.View;
        tr.textureMode = LineTextureMode.Stretch;
        tr.shadowCastingMode = ShadowCastingMode.Off;
        tr.receiveShadows = false;
        tr.sharedMaterial = GetOrCreateLootArcTrailMaterial();
        tr.sortingOrder = 15;

        tr.widthMultiplier = 1.0f;
        tr.widthCurve = new AnimationCurve(
            new Keyframe(0.0f, 0.14f),
            new Keyframe(0.28f, 0.085f),
            new Keyframe(0.72f, 0.032f),
            new Keyframe(1.0f, 0.0f));

        Color tierColor = GetTierVfxColor(tier);
        Color brightHead = Color.Lerp(tierColor, Color.white, 0.45f);
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(brightHead, 0.0f),
                new GradientColorKey(tierColor, 0.25f),
                new GradientColorKey(tierColor, 1.0f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(1.0f, 0.0f),
                new GradientAlphaKey(0.88f, 0.40f),
                new GradientAlphaKey(0.0f, 1.0f)
            });
        tr.colorGradient = grad;

        return tr;
    }

    /// <summary>
    /// ตัวสำรองสำหรับโหมดออฟไลน์ที่ไอเทมเข้ากระเป๋าตรงโดยไม่ Spawn WorldItem:
    /// สร้างลูกแก้วแสงลอยโค้งออกจากปากกล่องพร้อมเส้นสีตามระดับไอเทม แล้วจางหายทันทีเมื่อตกลงพื้น
    /// </summary>
    public static void SpawnStandaloneLootArc(
        Vector3 startWorldPos,
        Vector3 landWorldPos,
        ItemTier tier,
        float launchDelay = 0.14f,
        float flightDuration = 0.72f,
        float arcHeight = 1.95f)
    {
        GameObject host = new GameObject("VFX_StandaloneLootArc_" + tier);
        host.transform.position = startWorldPos;
        var runner = host.AddComponent<LootArcStandaloneRunner>();
        runner.Launch(startWorldPos, landWorldPos, tier, launchDelay, flightDuration, arcHeight);
    }

    private IEnumerator CloseChestSequenceRoutine()
    {
        isAnimating = true;
        isChestOpen = false;

        // หยุดปล่อยอนุภาคใหม่ของ Layer 2 และ Layer 3 เพื่อให้แสงและละอองดาวที่ลอยอยู่ค่อยๆ จางสลายไปอย่างนุ่มนวล
        StopAndResetAllChestVFX(false);

        float startAngle = GetCurrentLidAngleX();
        float startLightIntensity = chestGoldenPointLight != null ? chestGoldenPointLight.intensity : 0f;
        float elapsed = 0f;

        while (elapsed < closeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, closeDuration));
            currentLidOpenProgress = 1f - t;

            // Cubic Ease-In-Out ให้ฝาหีบปิดลงอย่างนุ่มนวลแน่นหนา
            float smoothT = t * t * (3f - 2f * t);
            ApplyLidAngleX(Mathf.Lerp(startAngle, closedAngleX, smoothT));

            if (chestGoldenPointLight != null)
            {
                chestGoldenPointLight.intensity = Mathf.Lerp(startLightIntensity, 0f, smoothT);
            }

            yield return null;
        }

        ApplyLidAngleX(closedAngleX);
        currentLidOpenProgress = 0f;

        if (chestGoldenPointLight != null)
        {
            chestGoldenPointLight.intensity = 0f;
            chestGoldenPointLight.enabled = false;
        }

        isAnimating = false;
        activeLidCoroutine = null;
    }

    private void StopAndResetAllChestVFX(bool clearImmediately)
    {
        ParticleSystemStopBehavior stopMode = clearImmediately
            ? ParticleSystemStopBehavior.StopEmittingAndClear
            : ParticleSystemStopBehavior.StopEmitting;

        if (l1LidCrackShockwave != null && clearImmediately)
        {
            l1LidCrackShockwave.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        if (l2CoreGoldenLightCone != null)
        {
            ParticleSystem.EmissionModule em2 = l2CoreGoldenLightCone.emission;
            em2.enabled = false;
            l2CoreGoldenLightCone.Stop(true, stopMode);
        }

        if (l3FloatingGoldenSparkles != null)
        {
            ParticleSystem.EmissionModule em3 = l3FloatingGoldenSparkles.emission;
            em3.enabled = false;
            if (clearImmediately)
            {
                l3FloatingGoldenSparkles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        if (l4TreasureStarFlash != null && clearImmediately)
        {
            l4TreasureStarFlash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        if (chestGoldenPointLight != null && clearImmediately)
        {
            chestGoldenPointLight.intensity = 0f;
            chestGoldenPointLight.enabled = false;
        }
    }

    /// <summary>
    /// สมการคณิตศาสตร์ Back Ease-Out ให้ฝาหีบดีดเปิดขึ้นเร็วแล้วกระเด้งเบาๆ ตอนสุดระยะอย่างมีน้ำหนัก
    /// </summary>
    private static float EvaluateBackEaseOut(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float x = t - 1f;
        return 1f + c3 * (x * x * x) + c1 * (x * x);
    }

    private void ApplyLidAngleX(float angleX)
    {
        if (chestLidPivot == null) return;
        Vector3 baseEuler = hasSavedInitialLidEuler ? initialLidEuler : Vector3.zero;
        chestLidPivot.localRotation = Quaternion.Euler(angleX, baseEuler.y, baseEuler.z);
    }

    private float GetCurrentLidAngleX()
    {
        if (chestLidPivot == null) return closedAngleX;
        float rawX = chestLidPivot.localEulerAngles.x;
        if (rawX > 180f) rawX -= 360f;
        return rawX;
    }

    // ================================================================================================
    // กฎเหล็กข้อที่ 2: รองรับปุ่ม G ทั้ง New Input System และ Legacy Input + คลิกเมาส์ซ้ายบนตัวกล่อง
    // ================================================================================================
    private bool ReadUniversalToggleKeyPressed()
    {
        bool pressedNew = false;
        if (Keyboard.current != null)
        {
            if (toggleKey == KeyCode.G && Keyboard.current.gKey.wasPressedThisFrame) pressedNew = true;
            else if (toggleKey == KeyCode.F && Keyboard.current.fKey.wasPressedThisFrame) pressedNew = true;
            else if (toggleKey == KeyCode.E && Keyboard.current.eKey.wasPressedThisFrame) pressedNew = true;
        }

        bool pressedLegacy = false;
        try
        {
            if (Input.GetKeyDown(toggleKey))
            {
                pressedLegacy = true;
            }
        }
        catch
        {
            // ป้องกัน Error หากโปรเจกต์ตั้ง Active Input Handling เป็น Input System Package (New) อย่างเดียว
        }

        return pressedNew || pressedLegacy;
    }

    private bool CheckNewInputMouseClickOnChest()
    {
        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame)
        {
            return false;
        }

        Camera mainCam = Camera.main;
        if (mainCam == null) return false;

        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray ray = mainCam.ScreenPointToRay(mousePos);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f))
        {
            if (hit.collider != null && (hit.collider.transform == transform || hit.collider.transform.IsChildOf(transform)))
            {
                return true;
            }
        }

        return false;
    }

    // ================================================================================================
    // กฎเหล็กข้อที่ 2: หน้าต่าง OnGUI() แสดงสถานะกล่องสมบัติมุมซ้ายบนของหน้าจอขณะทดสอบ
    // ================================================================================================
    private void OnGUI()
    {
        if (!showDebugHUD || !Application.isPlaying)
        {
            return;
        }

        GUIStyle boxStyle = new GUIStyle(GUI.skin.box);
        boxStyle.alignment = TextAnchor.UpperLeft;
        boxStyle.fontSize = 14;
        boxStyle.normal.textColor = Color.white;

        GUI.color = new Color(0.08f, 0.06f, 0.03f, 0.86f);
        GUI.Box(new Rect(14f, 14f, 445f, 118f), GUIContent.none, boxStyle);
        GUI.color = Color.white;

        GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.fontSize = 14;
        labelStyle.richText = true;
        labelStyle.normal.textColor = Color.white;

        string statusBadge;
        if (isAnimating)
        {
            statusBadge = isChestOpen
                ? "<color=#FFE066><b>กำลังดีดเปิดฝาหีบ... (" + Mathf.RoundToInt(currentLidOpenProgress * 100f) + "%)</b></color>"
                : "<color=#FFAA55><b>กำลังปิดฝาหีบ...</b></color>";
        }
        else if (isChestOpen)
        {
            statusBadge = "<color=#55FF77><b>เปิดอยู่ (OPENED - แสงทองทำงาน)</b></color>";
        }
        else
        {
            statusBadge = "<color=#CCCCCC><b>ปิดอยู่ (CLOSED)</b></color>";
        }

        string hudText =
            "<b>[AAA Treasure Chest Open & Golden Loot Burst VFX]</b>\n" +
            "• <b>สถานะกล่องสมบัติ:</b> " + statusBadge + "\n" +
            "• <b>วิธีควบคุม:</b> กดปุ่ม <b>[" + toggleKey + "]</b> บนคีย์บอร์ด หรือ <b>คลิกซ้ายที่ตัวกล่องสมบัติ</b>\n" +
            "• <b>องศาเปิดฝา:</b> " + GetCurrentLidAngleX().ToString("F1") + "°  |  <b>จังหวะระเบิดแสง:</b> ที่ 25% ของระยะเปิดฝา";

        GUI.Label(new Rect(24f, 20f, 425f, 106f), hudText, labelStyle);
    }

    // ================================================================================================
    // RUNTIME PROCEDURAL VFX BUILDER (สำหรับกล่องที่ Spawn ตอน Runtime เช่น LootChest)
    // ================================================================================================
    private static void EnsureRuntimeSharedAssets()
    {
        if (s_RuntimeLightConeMesh == null)
        {
            s_RuntimeLightConeMesh = BuildRuntimeLightConeMesh();
        }
        if (s_RuntimeGodRaysTex == null)
        {
            s_RuntimeGodRaysTex = BuildRuntimeGodRaysTexture(256, 256);
        }
        if (s_RuntimeSparkleStarTex == null)
        {
            s_RuntimeSparkleStarTex = BuildRuntimeSparkleStarTexture(256, 256);
        }
        if (s_RuntimeShockwaveRingTex == null)
        {
            s_RuntimeShockwaveRingTex = BuildRuntimeShockwaveRingTexture(256, 256);
        }
        if (s_RuntimeStreakRibbonTex == null)
        {
            s_RuntimeStreakRibbonTex = BuildRuntimeStreakRibbonTexture(256, 64);
        }
        // เอฟเฟกต์ตัวกล่องยังคงเป็นสีทองอร่าม HDR ทั้ง 4 เลเยอร์ ส่วน s_RuntimeMatLootArcTrail เป็นสีขาว HDR กลางๆ เพื่อรับสีเส้นตามระดับไอเทม
        if (s_RuntimeMatShockwave == null)
        {
            s_RuntimeMatShockwave = CreateRuntimeURPAdditiveMaterial(s_RuntimeShockwaveRingTex, new Color(2.8f, 2.05f, 0.62f, 1f));
        }
        if (s_RuntimeMatLightCone == null)
        {
            s_RuntimeMatLightCone = CreateRuntimeURPAdditiveMaterial(s_RuntimeGodRaysTex, new Color(2.6f, 1.90f, 0.56f, 1f));
        }
        if (s_RuntimeMatSparkles == null)
        {
            s_RuntimeMatSparkles = CreateRuntimeURPAdditiveMaterial(s_RuntimeSparkleStarTex, new Color(3.0f, 2.35f, 0.95f, 1f));
        }
        if (s_RuntimeMatStarFlash == null)
        {
            s_RuntimeMatStarFlash = CreateRuntimeURPAdditiveMaterial(s_RuntimeSparkleStarTex, new Color(3.6f, 2.85f, 1.15f, 1f));
        }
        if (s_RuntimeMatLootArcTrail == null)
        {
            s_RuntimeMatLootArcTrail = CreateRuntimeURPAdditiveMaterial(s_RuntimeStreakRibbonTex, new Color(3.2f, 3.2f, 3.2f, 1f));
        }
    }

    private static Material CreateRuntimeURPAdditiveMaterial(Texture2D mainTex, Color baseColor)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        Material mat = new Material(shader);
        mat.name = "M_Runtime_ChestVFX_" + (mainTex != null ? mainTex.name : "Add");

        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", mainTex);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", mainTex);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseColor);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", baseColor);

        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 2f);
        if (mat.HasProperty("_ColorMode")) mat.SetFloat("_ColorMode", 0f);
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
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
        return mat;
    }

    private static Mesh BuildRuntimeLightConeMesh()
    {
        const int radialSegments = 40;
        const int heightSegments = 14;
        const float bottomRadius = 0.36f;
        const float topRadius = 1.18f;
        const float height = 1.0f;

        int ringVertexCount = radialSegments + 1;
        int totalVertices = ringVertexCount * (heightSegments + 1);

        Vector3[] vertices = new Vector3[totalVertices];
        Vector3[] normals = new Vector3[totalVertices];
        Vector2[] uvs = new Vector2[totalVertices];
        Color[] colors = new Color[totalVertices];

        int vertIndex = 0;
        for (int yStep = 0; yStep <= heightSegments; yStep++)
        {
            float v = (float)yStep / heightSegments;
            float y = v * height;
            float radius = Mathf.Lerp(bottomRadius, topRadius, Mathf.Pow(v, 0.92f));

            float bottomFade = SmoothStep(0.0f, 0.14f, v);
            float topFade = 1.0f - SmoothStep(0.32f, 1.0f, v);
            float alpha = Mathf.Clamp01(bottomFade * Mathf.Pow(topFade, 1.45f));

            Color ringColor = new Color(1.0f, 1.0f, 1.0f, alpha);

            for (int rStep = 0; rStep <= radialSegments; rStep++)
            {
                float u = (float)rStep / radialSegments;
                float angle = u * Mathf.PI * 2f;
                float cosA = Mathf.Cos(angle);
                float sinA = Mathf.Sin(angle);

                vertices[vertIndex] = new Vector3(cosA * radius, y, sinA * radius);
                normals[vertIndex] = new Vector3(cosA, 0.25f, sinA).normalized;
                uvs[vertIndex] = new Vector2(u * 2.0f, v);
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

        Mesh mesh = new Mesh();
        mesh.name = "Mesh_LootLightCone_Runtime";
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.colors = colors;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Texture2D BuildRuntimeGodRaysTexture(int width, int height)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.name = "T_GoldenGodRays_Runtime";
        tex.wrapModeU = TextureWrapMode.Repeat;
        tex.wrapModeV = TextureWrapMode.Clamp;

        Color[] pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            float v = (float)y / (height - 1);
            float verticalMask = SmoothStep(0.0f, 0.10f, v) * Mathf.Pow(1.0f - SmoothStep(0.18f, 1.0f, v), 1.6f);

            for (int x = 0; x < width; x++)
            {
                float u = (float)x / width;
                float angle = u * Mathf.PI * 2f;

                float rayPrimary = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(angle * 7f + 0.4f), 2.6f);
                float raySecondary = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(angle * 13f - 1.1f), 4.0f);
                float rayFine = Mathf.Pow(0.5f + 0.5f * Mathf.Cos(angle * 23f + 0.8f), 5.5f);
                float softBaseGlow = 0.28f + 0.20f * Mathf.Sin(angle * 3f + 0.2f);

                float rayPattern = softBaseGlow * (1.0f - v * 0.45f)
                                   + rayPrimary * 0.58f
                                   + raySecondary * 0.42f
                                   + rayFine * 0.30f;

                float intensity = Mathf.Clamp01(rayPattern * verticalMask * 1.15f);
                pixels[y * width + x] = new Color(1.0f, 1.0f, 1.0f, intensity);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    private static Texture2D BuildRuntimeSparkleStarTexture(int width, int height)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.name = "T_LootSparkleStar_Runtime";
        tex.wrapMode = TextureWrapMode.Clamp;

        Color[] pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            float ny = ((float)y / (height - 1)) * 2f - 1f;
            for (int x = 0; x < width; x++)
            {
                float nx = ((float)x / (width - 1)) * 2f - 1f;
                float dist = Mathf.Sqrt(nx * nx + ny * ny);
                float edgeMask = 1.0f - SmoothStep(0.72f, 0.98f, dist);

                float core = Mathf.Exp(-dist * dist * 16.0f);
                float halo = Mathf.Exp(-dist * dist * 4.8f) * 0.42f;

                float ax = Mathf.Abs(nx);
                float ay = Mathf.Abs(ny);
                float horizontalSpike = Mathf.Exp(-ay * 22.0f) * Mathf.Exp(-ax * 2.4f);
                float verticalSpike = Mathf.Exp(-ax * 22.0f) * Mathf.Exp(-ay * 2.4f);

                float d1 = Mathf.Abs((nx + ny) * 0.7071f);
                float d2 = Mathf.Abs((nx - ny) * 0.7071f);
                float diagSpikeA = Mathf.Exp(-d1 * 28.0f) * Mathf.Exp(-d2 * 6.5f) * 0.32f;
                float diagSpikeB = Mathf.Exp(-d2 * 28.0f) * Mathf.Exp(-d1 * 6.5f) * 0.32f;

                float value = Mathf.Clamp01((core + halo + (horizontalSpike + verticalSpike) * 0.95f + diagSpikeA + diagSpikeB) * edgeMask);
                pixels[y * width + x] = new Color(1.0f, 1.0f, 1.0f, value);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    private static Texture2D BuildRuntimeShockwaveRingTexture(int width, int height)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.name = "T_ChestShockwaveRing_Runtime";
        tex.wrapMode = TextureWrapMode.Clamp;

        Color[] pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            float ny = ((float)y / (height - 1)) * 2f - 1f;
            for (int x = 0; x < width; x++)
            {
                float nx = ((float)x / (width - 1)) * 2f - 1f;
                float dist = Mathf.Sqrt(nx * nx + ny * ny);
                float angle = Mathf.Atan2(ny, nx);

                float mainRing = Mathf.Exp(-Mathf.Pow((dist - 0.74f) / 0.065f, 2f));
                float innerTrail = Mathf.Exp(-Mathf.Pow((dist - 0.60f) / 0.14f, 2f)) * 0.45f * SmoothStep(0.32f, 0.65f, dist);
                float outerHalo = Mathf.Exp(-Mathf.Pow((dist - 0.78f) / 0.11f, 2f)) * 0.35f;
                float energyRipple = 0.84f + 0.16f * Mathf.Sin(angle * 10f) * Mathf.Cos(angle * 4f);
                float outerCutoff = 1.0f - SmoothStep(0.88f, 0.99f, dist);

                float value = Mathf.Clamp01((mainRing + innerTrail + outerHalo) * energyRipple * outerCutoff);
                pixels[y * width + x] = new Color(1.0f, 1.0f, 1.0f, value);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    private static Texture2D BuildRuntimeStreakRibbonTexture(int width, int height)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.name = "T_TierLootStreakRibbon_Runtime";
        tex.wrapMode = TextureWrapMode.Clamp;

        Color[] pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            float ny = ((float)y / (height - 1)) * 2f - 1f;
            float sharpCore = Mathf.Exp(-ny * ny * 26.0f);
            float softGlow = Mathf.Exp(-ny * ny * 5.2f) * 0.55f;
            float edgeGuard = 1f - SmoothStep(0.75f, 0.98f, Mathf.Abs(ny));

            for (int x = 0; x < width; x++)
            {
                float nx = (float)x / (width - 1);
                float headFade = SmoothStep(0.0f, 0.14f, nx);
                float tailFade = Mathf.Pow(1f - SmoothStep(0.18f, 1.0f, nx), 1.35f);
                float val = Mathf.Clamp01((sharpCore + softGlow) * edgeGuard * headFade * tailFade);
                pixels[y * width + x] = new Color(1.0f, 1.0f, 1.0f, val);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    private static ParticleSystem CreateRuntimeLayer1Shockwave(Transform parent, Material mat)
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
        main.startSize = 3.2f;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = Color.white;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 4;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0.0f, 2) });

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;

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
        renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return ps;
    }

    private static ParticleSystem CreateRuntimeLayer2LightCone(Transform parent, Mesh coneMesh, Material mat)
    {
        GameObject obj = new GameObject("L2_Core_GoldenLightCone");
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = new Vector3(0f, -0.04f, 0f);
        obj.transform.localRotation = Quaternion.identity;

        ParticleSystem ps = obj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.duration = 1.6f;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = 1.6f;
        main.startSpeed = 0f;
        main.startSize3D = true;
        main.startSizeX = 1.25f;
        main.startSizeY = 3.55f;
        main.startSizeZ = 1.25f;
        main.startColor = Color.white;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 4;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0.85f;
        emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0.0f, 1) });

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;

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
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = coneMesh;
        renderer.alignment = ParticleSystemRenderSpace.Local;
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return ps;
    }

    private static ParticleSystem CreateRuntimeLayer3Sparkles(Transform parent, Material mat)
    {
        GameObject obj = new GameObject("L3_Floating_GoldenSparkles");
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = new Vector3(0f, 0.10f, 0f);

        ParticleSystem ps = obj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.duration = 2.0f;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.25f, 1.85f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.75f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.11f, 0.26f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = Color.white;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 140;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 24f;
        emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0.0f, 25) });

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(1.12f, 0.12f, 0.62f);

        ParticleSystem.VelocityOverLifetimeModule vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = new ParticleSystem.MinMaxCurve(0f);
        vel.y = new ParticleSystem.MinMaxCurve(0.8f);
        vel.z = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalX = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalY = new ParticleSystem.MinMaxCurve(1.8f);
        vel.orbitalZ = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalOffsetX = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalOffsetY = new ParticleSystem.MinMaxCurve(0f);
        vel.orbitalOffsetZ = new ParticleSystem.MinMaxCurve(0f);
        vel.radial = new ParticleSystem.MinMaxCurve(0.16f);
        vel.speedModifier = new ParticleSystem.MinMaxCurve(1f);

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
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return ps;
    }

    private static ParticleSystem CreateRuntimeLayer4StarFlash(Transform parent, Material mat)
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
        main.startLifetime = 0.65f;
        main.startSpeed = 0f;
        main.startSize = 2.6f;
        main.startColor = Color.white;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 2;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0.0f, 1) });

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;

        ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.separateAxes = false;
        AnimationCurve explosiveCurve = new AnimationCurve(
            new Keyframe(0.0f, 0.05f, 11.5f, 11.5f),
            new Keyframe(0.14f, 1.15f, 0.5f, 0.5f),
            new Keyframe(0.42f, 0.90f, -0.7f, -0.7f),
            new Keyframe(1.0f, 0.0f, -1.8f, -1.8f));
        sol.size = new ParticleSystem.MinMaxCurve(1f, explosiveCurve);

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
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return ps;
    }

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp01((x - edge0) / Mathf.Max(0.00001f, edge1 - edge0));
        return t * t * (3f - 2f * t);
    }

    private static Transform FindDeepChild(Transform parent, string targetName)
    {
        if (parent == null || string.IsNullOrEmpty(targetName)) return null;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name == targetName) return child;

            Transform found = FindDeepChild(child, targetName);
            if (found != null) return found;
        }

        return null;
    }
}

/// <summary>
/// ตัววิ่งเส้นโค้งพาราโบลาสำหรับไอเทมที่ลอยออกจากกล่องในโหมดออฟไลน์ (ไม่มี WorldItem)
/// เมื่อตกลงพื้นจะหยุดปล่อยเส้นแสงแล้วค่อยๆ จางหายไปเองอย่างสะอาด
/// </summary>
public class LootArcStandaloneRunner : MonoBehaviour
{
    public void Launch(Vector3 startPos, Vector3 endPos, ItemTier tier, float delay, float duration, float arcHeight)
    {
        StartCoroutine(FlyAndFadeRoutine(startPos, endPos, tier, delay, duration, arcHeight));
    }

    private IEnumerator FlyAndFadeRoutine(Vector3 startPos, Vector3 endPos, ItemTier tier, float delay, float duration, float arcHeight)
    {
        transform.position = startPos;
        if (delay > 0f) yield return new WaitForSeconds(delay);

        TrailRenderer trail = MMOTreasureChestController.AttachLootArcTrail(transform, tier, 0.36f);
        trail.Clear();
        trail.emitting = true;

        float elapsed = 0f;
        float safeDur = Mathf.Max(0.25f, duration);
        while (elapsed < safeDur)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / safeDur);
            Vector3 pos = Vector3.Lerp(startPos, endPos, t);
            pos.y += 4f * arcHeight * t * (1f - t);
            transform.position = pos;
            yield return null;
        }

        transform.position = endPos;
        trail.emitting = false; // ตกลงพื้นแล้ว -> หยุดปล่อยเส้นแสงให้ค่อยๆ จางหายไปจนหมด

        yield return new WaitForSeconds(trail.time + 0.12f);
        Destroy(gameObject);
    }
}
