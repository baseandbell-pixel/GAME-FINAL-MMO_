using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// ระบบเอฟเฟกต์ "มอนสเตอร์โจมตีตามแอนิเมชันระดับ AAA (Animation-Synced Monster Attack & Telegraph VFX)"
/// สำหรับ Unity 6 (URP - Universal Render Pipeline)
/// - ซ่อนเอฟเฟกต์สะอาดตา 100% เมื่อไม่ได้โจมตี และปล่อยออกมาตรงจังหวะแอนิเมชันเป๊ะๆ
/// - รองรับการซิงก์ 2 ระบบพร้อมกันในไฟล์เดียว:
///   (1) ระบบ Auto-Sync Normalized Time (List<AutoAnimSyncRule>) สำหรับไฟล์แอนิเมชัน .fbx ที่ติด Read-Only
///   (2) ระบบ Animation Event Callbacks (AnimEvent_TriggerTelegraph, AnimEvent_SlashRight, AnimEvent_SlashLeft, AnimEvent_HeavySlam)
/// - รองรับปุ่มทดสอบ [G] ทั้ง New Input System & Legacy Input กดสลับครบทั้ง 4 จังหวะ + หน้าต่าง OnGUI มุมขวาบน
/// - ออกแบบเป็น Standalone 100% ไม่กระทบโค้ดระบบเกมหลักของเพื่อน
/// </summary>
[AddComponentMenu("yeen VFX/13. [Monster] โจมตีตามแอนิเมชัน 4 เลเยอร์ (Monster Anim Attack VFX)")]
[DisallowMultipleComponent]
public class MMOMonsterAnimAttackVFX : MonoBehaviour
{
    public enum AttackSwingType
    {
        TelegraphCharge,
        SlashRightToLeft,
        SlashLeftToRight,
        HeavyOverheadSlam
    }

    [Serializable]
    public class AutoAnimSyncRule
    {
        [Tooltip("เปิดใช้งานกฎการซิงก์นี้")]
        public bool enabled = true;

        [Tooltip("ชื่อ State หรือ Tag ใน Animator (เช่น Attack, Attack1, HeavyAttack, Slam)")]
        public string stateNameOrTag = "Attack";

        [Tooltip("หมายเลข Layer ใน Animator (ค่าเริ่มต้น = 0 คือ Base Layer)")]
        [Min(0)]
        public int layerIndex = 0;

        [Tooltip("จังหวะเปอร์เซ็นต์ของแอนิเมชันที่จะปล่อยเอฟเฟกต์ (0.05 = ต้นท่า, 0.45 = จังหวะเหวี่ยงฟัน, 0.95 = ปลายท่า)")]
        [Range(0.05f, 0.95f)]
        public float triggerNormalizedTime = 0.42f;

        [Tooltip("ประเภทเอฟเฟกต์ที่ต้องการปล่อยในจังหวะนี้")]
        public AttackSwingType swingType = AttackSwingType.SlashRightToLeft;

        // ติดตามรอบการเล่นภายในเพื่อไม่ให้ยิงซ้ำเฟรมเดิม แม้แอนิเมชันเล่นวนลูป
        [NonSerialized] public int lastTriggeredStateHash = 0;
        [NonSerialized] public int lastTriggeredLoop = -1;
    }

    [Header("=== 1. อ้างอิง Animator และจุดยึดเอฟเฟกต์ 4 เลเยอร์ (VFX Layer References) ===")]
    [Tooltip("Animator ของมอนสเตอร์ (หากเว้นว่างจะค้นหาอัตโนมัติในตัวละครและลูกหลาน)")]
    public Animator targetAnimator;

    [Tooltip("จุดเกิดแสงเตือนอันตราย (ผูกติดกระดูกมือขวาหรือหัวมอนสเตอร์: Pivot_TelegraphCharge)")]
    public Transform telegraphPivot;

    [Tooltip("Layer 1: แสงแฟลชเตือนอันตรายสีแดงชาด (0.00s - 0.32s, Billboard, Start Size = 1.45, HDR 5.0x)")]
    public ParticleSystem layer1TelegraphFlash;

    [Tooltip("Layer 2A: คลื่นกรงเล็บ 3D ตวัดขวาเฉียงลงซ้าย (-38 องศา, Scale 1.75, Spin Y -580 deg/s)")]
    public ParticleSystem layer2ASlashRight;

    [Tooltip("Layer 2B: คลื่นกรงเล็บ 3D ตวัดซ้ายเฉียงลงขวา (142 องศา, Scale 1.75, Spin Y +580 deg/s)")]
    public ParticleSystem layer2BSlashLeft;

    [Tooltip("Layer 2C: คลื่นกรงเล็บ 3D ฟันทุบผ่าตรงกลางขนาดใหญ่ (-90 องศา, Scale 2.25, Spin Y -640 deg/s)")]
    public ParticleSystem layer2CHeavySlamSlash;

    [Tooltip("Layer 3: สะเก็ดพลังสีแดงเพลิง Stretched Billboard (World, Vel 0.055, Len 4.0, Burst 22)")]
    public ParticleSystem layer3FeralSparks;

    [Tooltip("Layer 4: วงแหวนคลื่นกระแทกพื้น Horizontal Billboard (Pivot_GroundSlamImpact, Size 3.8, Burst 2)")]
    public ParticleSystem layer4GroundSlamShockwave;

    [Header("=== 2. ระบบที่ 1: Auto-Sync Normalized Time (สำหรับไฟล์ .fbx Read-Only) ===")]
    [Tooltip("เปิดใช้งานการตรวจจับเปอร์เซ็นต์เฟรมแอนิเมชันอัตโนมัติ (เหมาะสำหรับไฟล์แอนิเมชัน Read-Only ที่ปัก Event ไม่ได้)")]
    public bool enableAutoAnimSync = true;

    [Tooltip("รายการกฎจับคู่ชื่อ State ใน Animator + เปอร์เซ็นต์เวลา -> ประเภทเอฟเฟกต์")]
    public List<AutoAnimSyncRule> autoSyncRules = new List<AutoAnimSyncRule>
    {
        // รองรับ MonsterSystem (Attack_01, Attack_02, Attack_03, Skill_Cast, Skill_Release)
        new AutoAnimSyncRule { enabled = true, stateNameOrTag = "Attack_01", layerIndex = 0, triggerNormalizedTime = 0.12f, swingType = AttackSwingType.TelegraphCharge },
        new AutoAnimSyncRule { enabled = true, stateNameOrTag = "Attack_01", layerIndex = 0, triggerNormalizedTime = 0.36f, swingType = AttackSwingType.SlashRightToLeft },
        new AutoAnimSyncRule { enabled = true, stateNameOrTag = "Attack_02", layerIndex = 0, triggerNormalizedTime = 0.12f, swingType = AttackSwingType.TelegraphCharge },
        new AutoAnimSyncRule { enabled = true, stateNameOrTag = "Attack_02", layerIndex = 0, triggerNormalizedTime = 0.36f, swingType = AttackSwingType.SlashLeftToRight },
        new AutoAnimSyncRule { enabled = true, stateNameOrTag = "Attack_03", layerIndex = 0, triggerNormalizedTime = 0.14f, swingType = AttackSwingType.TelegraphCharge },
        new AutoAnimSyncRule { enabled = true, stateNameOrTag = "Attack_03", layerIndex = 0, triggerNormalizedTime = 0.42f, swingType = AttackSwingType.HeavyOverheadSlam },
        new AutoAnimSyncRule { enabled = true, stateNameOrTag = "Skill_Cast", layerIndex = 0, triggerNormalizedTime = 0.15f, swingType = AttackSwingType.TelegraphCharge },
        new AutoAnimSyncRule { enabled = true, stateNameOrTag = "Skill_Release", layerIndex = 0, triggerNormalizedTime = 0.25f, swingType = AttackSwingType.HeavyOverheadSlam },
        // รองรับ Animator ทั่วไป (Attack, Attack2, HeavyAttack)
        new AutoAnimSyncRule { enabled = true, stateNameOrTag = "Attack", layerIndex = 0, triggerNormalizedTime = 0.14f, swingType = AttackSwingType.TelegraphCharge },
        new AutoAnimSyncRule { enabled = true, stateNameOrTag = "Attack", layerIndex = 0, triggerNormalizedTime = 0.42f, swingType = AttackSwingType.SlashRightToLeft },
        new AutoAnimSyncRule { enabled = true, stateNameOrTag = "Attack2", layerIndex = 0, triggerNormalizedTime = 0.42f, swingType = AttackSwingType.SlashLeftToRight },
        new AutoAnimSyncRule { enabled = true, stateNameOrTag = "HeavyAttack", layerIndex = 0, triggerNormalizedTime = 0.18f, swingType = AttackSwingType.TelegraphCharge },
        new AutoAnimSyncRule { enabled = true, stateNameOrTag = "HeavyAttack", layerIndex = 0, triggerNormalizedTime = 0.50f, swingType = AttackSwingType.HeavyOverheadSlam }
    };

    [Header("=== 3. ปุ่มทดสอบ [G], ระยะแสดงผล และหน้าต่าง OnGUI มุมขวาบน ===")]
    [Tooltip("ระยะมองเห็นสูงสุดจากกล้องสำหรับปล่อยเอฟเฟกต์ (0 = ไม่จำกัดระยะ, ใช้ตัดเอฟเฟกต์มอนที่อยู่ไกลข้ามแมป)")]
    [Min(0f)]
    public float maxVisualRange = 0f;

    [Tooltip("เปิดใช้งานปุ่มทดสอบ [G] สำหรับกดสลับทดสอบครบทั้ง 4 จังหวะต่อเนื่อง")]
    public bool enableTestHotkey = true;

    [Tooltip("ปุ่มคีย์บอร์ดทดสอบ (ค่าเริ่มต้น = G)")]
    public KeyCode testCycleKey = KeyCode.G;

    [Tooltip("แสดงหน้าต่างสถานะมุมขวาบนของหน้าจอ")]
    public bool showDebugHUD = true;

    // Runtime state tracking
    private int testCycleIndex = 0;
    private string lastTriggerLabel = "รอจังหวะโจมตี...";
    private string lastSyncSource = "-";
    private float lastTriggerTime = -99f;
    private string currentAnimStateReadout = "ไม่มี Animator";
    private float currentAnimNormalizedReadout = 0f;

    // Per-frame HUD stack tracking (กันมอนสเตอร์หลายตัววาด OnGUI ทับกัน)
    private static int hudFrameMarker = -1;
    private static int hudStackCounter = 0;

    // Runtime Self-Healing Fallback Assets (ทำงานได้ทันทีแม้ยังไม่ได้กดสร้างจาก Generator)
    private static Mesh fallbackClawArcMesh;
    private static Texture2D fallbackClawTex;
    private static Texture2D fallbackTelegraphTex;
    private static Texture2D fallbackSlamRingTex;
    private static Texture2D fallbackSparkTex;
    private static Material fallbackTelegraphMat;
    private static Material fallbackSlashRedMat;
    private static Material fallbackSlamGoldMat;
    private static Material fallbackSparkMat;
    private static Material fallbackGroundRingMat;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticsOnPlay()
    {
        hudFrameMarker = -1;
        hudStackCounter = 0;
        fallbackClawArcMesh = null;
        fallbackClawTex = null;
        fallbackTelegraphTex = null;
        fallbackSlamRingTex = null;
        fallbackSparkTex = null;
        fallbackTelegraphMat = null;
        fallbackSlashRedMat = null;
        fallbackSlamGoldMat = null;
        fallbackSparkMat = null;
        fallbackGroundRingMat = null;
    }

    private void Awake()
    {
        if (targetAnimator == null)
        {
            targetAnimator = GetComponent<Animator>();
            if (targetAnimator == null)
            {
                targetAnimator = GetComponentInChildren<Animator>(true);
            }
        }

        EnsureVFXRigReady();
        StopAndClearAllLayers();
    }

    private void OnEnable()
    {
        ResetAllRuleTracking();
        StopAndClearAllLayers();
    }

    private void Update()
    {
        // 1. ระบบที่ 1: ตรวจจับเปอร์เซ็นต์แอนิเมชันอัตโนมัติ (Auto-Sync Normalized Time)
        if (enableAutoAnimSync && targetAnimator != null && targetAnimator.isActiveAndEnabled && targetAnimator.runtimeAnimatorController != null)
        {
            EvaluateAutoAnimSyncRules();
        }
        else
        {
            currentAnimStateReadout = targetAnimator == null ? "ไม่มี Animator" : "Animator ยังไม่ผูก Controller";
            currentAnimNormalizedReadout = 0f;
        }

        // 2. ปุ่มทดสอบ [G]: กดสลับทดสอบครบทั้ง 4 จังหวะต่อเนื่อง (New Input System + Legacy Input)
        if (enableTestHotkey && IsTestKeyPressedThisFrame())
        {
            CycleTestAll4AttackPhases();
        }
    }

    // =========================================================================
    // ระบบที่ 1: AUTO-SYNC NORMALIZED TIME (สำหรับไฟล์แอนิเมชัน .fbx Read-Only)
    // =========================================================================

    private void EvaluateAutoAnimSyncRules()
    {
        AnimatorStateInfo baseInfo = GetActiveOrEnteringStateInfo(targetAnimator, 0);
        currentAnimNormalizedReadout = baseInfo.normalizedTime - Mathf.Floor(baseInfo.normalizedTime);
        currentAnimStateReadout = ResolveReadableStateName(targetAnimator, 0, baseInfo);

        if (autoSyncRules == null || autoSyncRules.Count == 0) return;

        // ตรวจว่าในรายการกฎมีกฎที่ตรงชื่อ State เป๊ะๆ (Exact IsName) อยู่หรือไม่ เพื่อไม่ให้กฎ Tag กว้างๆ ยิงซ้ำซ้อน
        bool hasExactBaseMatch = false;
        for (int i = 0; i < autoSyncRules.Count; i++)
        {
            AutoAnimSyncRule r = autoSyncRules[i];
            if (r != null && r.enabled && r.layerIndex == 0 && !string.IsNullOrEmpty(r.stateNameOrTag) && baseInfo.IsName(r.stateNameOrTag))
            {
                hasExactBaseMatch = true;
                break;
            }
        }

        for (int i = 0; i < autoSyncRules.Count; i++)
        {
            AutoAnimSyncRule rule = autoSyncRules[i];
            if (rule == null || !rule.enabled || string.IsNullOrEmpty(rule.stateNameOrTag)) continue;
            if (rule.layerIndex < 0 || rule.layerIndex >= targetAnimator.layerCount) continue;

            AnimatorStateInfo stateInfo = rule.layerIndex == 0
                ? baseInfo
                : GetActiveOrEnteringStateInfo(targetAnimator, rule.layerIndex);

            bool allowFallbackTag = rule.layerIndex != 0 || !hasExactBaseMatch;
            bool matched = IsRuleMatchingState(targetAnimator, rule.layerIndex, stateInfo, rule.stateNameOrTag, allowFallbackTag);
            if (!matched)
            {
                // ออกจาก State เดิมแล้ว รีเซ็ตตัวนับเพื่อให้การโจมตีรอบถัดไปยิงใหม่ได้ทันที
                if (rule.lastTriggeredStateHash != 0)
                {
                    rule.lastTriggeredStateHash = 0;
                    rule.lastTriggeredLoop = -1;
                }
                continue;
            }

            int currentLoop = Mathf.FloorToInt(stateInfo.normalizedTime);
            float normalizedFraction = stateInfo.normalizedTime - currentLoop;

            bool alreadyTriggeredThisLoop =
                rule.lastTriggeredStateHash == stateInfo.fullPathHash &&
                rule.lastTriggeredLoop == currentLoop;

            if (!alreadyTriggeredThisLoop && normalizedFraction >= rule.triggerNormalizedTime)
            {
                rule.lastTriggeredStateHash = stateInfo.fullPathHash;
                rule.lastTriggeredLoop = currentLoop;
                TriggerAttackVFX(rule.swingType, $"Auto-Sync ({rule.stateNameOrTag} @ {rule.triggerNormalizedTime * 100f:0}%)");
            }
        }
    }

    private static AnimatorStateInfo GetActiveOrEnteringStateInfo(Animator anim, int layerIndex)
    {
        if (anim.IsInTransition(layerIndex))
        {
            AnimatorStateInfo next = anim.GetNextAnimatorStateInfo(layerIndex);
            if (next.fullPathHash != 0)
            {
                return next;
            }
        }
        return anim.GetCurrentAnimatorStateInfo(layerIndex);
    }

    private static bool IsRuleMatchingState(Animator anim, int layerIndex, AnimatorStateInfo stateInfo, string stateNameOrTag, bool allowFallbackTagAndClip)
    {
        if (stateInfo.IsName(stateNameOrTag))
        {
            return true;
        }

        if (!allowFallbackTagAndClip)
        {
            return false;
        }

        if (stateInfo.IsTag(stateNameOrTag))
        {
            return true;
        }

        // รองรับการจับคู่ชื่อ Clip ภายใน State สำหรับมือใหม่ที่ตั้งชื่อย่อ (เช่น พิมพ์ "Attack" จับ "Orc_Attack_01")
        AnimatorClipInfo[] clips = anim.GetCurrentAnimatorClipInfo(layerIndex);
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i].clip != null &&
                clips[i].clip.name.IndexOf(stateNameOrTag, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// ตั้งค่าสำหรับมอนสเตอร์จริงในฉากเกม (เรียกจาก YeenVFXAutoIntegrator อัตโนมัติ ไม่รบกวนปุ่มและหน้าจอ HUD)
    /// </summary>
    public void ConfigureForLiveMonster(Animator monsterAnimator, bool isBoss, float visualRangeMeters)
    {
        if (monsterAnimator != null)
        {
            targetAnimator = monsterAnimator;
        }

        enableAutoAnimSync = true;
        enableTestHotkey = false;
        showDebugHUD = false;
        maxVisualRange = Mathf.Max(0f, visualRangeMeters);

        EnsureVFXRigReady();

        Transform vfxRoot = transform.Find("VFX_MonsterAnimAttack_Root");
        if (vfxRoot != null && isBoss)
        {
            vfxRoot.localScale = Vector3.one * 1.28f;
        }
    }

    private static string ResolveReadableStateName(Animator anim, int layerIndex, AnimatorStateInfo stateInfo)
    {
        AnimatorClipInfo[] clips = anim.GetCurrentAnimatorClipInfo(layerIndex);
        if (clips != null && clips.Length > 0 && clips[0].clip != null)
        {
            return clips[0].clip.name;
        }
        return $"StateHash({stateInfo.shortNameHash})";
    }

    private void ResetAllRuleTracking()
    {
        if (autoSyncRules == null) return;
        for (int i = 0; i < autoSyncRules.Count; i++)
        {
            if (autoSyncRules[i] == null) continue;
            autoSyncRules[i].lastTriggeredStateHash = 0;
            autoSyncRules[i].lastTriggeredLoop = -1;
        }
    }

    // =========================================================================
    // ระบบที่ 2: ANIMATION EVENT CALLBACKS (เลือกได้โดยตรงจากหน้าต่าง Animation)
    // =========================================================================

    /// <summary>Animation Event จังหวะง้างมือ: แสงแฟลชเตือนอันตรายสีแดงชาดบนมือ/หัวมอนสเตอร์</summary>
    public void AnimEvent_TriggerTelegraph()
    {
        TriggerAttackVFX(AttackSwingType.TelegraphCharge, "Animation Event");
    }

    /// <summary>Animation Event จังหวะตวัดแขนขวาเฉียงลงซ้าย (-38 องศา)</summary>
    public void AnimEvent_SlashRight()
    {
        TriggerAttackVFX(AttackSwingType.SlashRightToLeft, "Animation Event");
    }

    /// <summary>Animation Event จังหวะตวัดแขนซ้ายเฉียงลงขวา (142 องศา)</summary>
    public void AnimEvent_SlashLeft()
    {
        TriggerAttackVFX(AttackSwingType.SlashLeftToRight, "Animation Event");
    }

    /// <summary>Animation Event จังหวะฟันทุบหนักผ่าตรงกลาง (-90 องศา) พร้อมคลื่นกระแทกพื้นขนาดใหญ่</summary>
    public void AnimEvent_HeavySlam()
    {
        TriggerAttackVFX(AttackSwingType.HeavyOverheadSlam, "Animation Event");
    }

    /// <summary>
    /// ฟังก์ชันสั่งปล่อยเอฟเฟกต์ตามประเภทการโจมตี (เรียกใช้จากโค้ด AI หรือระบบซิงก์ได้โดยตรง)
    /// </summary>
    public void TriggerAttackVFX(AttackSwingType swingType, string sourceLabel = "Script Call")
    {
        if (maxVisualRange > 0f)
        {
            Camera cam = Camera.main;
            if (cam != null && (cam.transform.position - transform.position).sqrMagnitude > (maxVisualRange * maxVisualRange))
            {
                return;
            }
        }

        EnsureVFXRigReady();

        switch (swingType)
        {
            case AttackSwingType.TelegraphCharge:
                PlayOneShotLayer(layer1TelegraphFlash);
                lastTriggerLabel = "1. Telegraph Charge Flash (แสงเตือนง้างมือ)";
                break;

            case AttackSwingType.SlashRightToLeft:
                PlayOneShotLayer(layer2ASlashRight);
                PlaySparksForSwing(AttackSwingType.SlashRightToLeft);
                lastTriggerLabel = "2. Slash Right -> Left (-38° กรงเล็บขวา)";
                break;

            case AttackSwingType.SlashLeftToRight:
                PlayOneShotLayer(layer2BSlashLeft);
                PlaySparksForSwing(AttackSwingType.SlashLeftToRight);
                lastTriggerLabel = "3. Slash Left -> Right (142° กรงเล็บซ้าย)";
                break;

            case AttackSwingType.HeavyOverheadSlam:
                PlayOneShotLayer(layer2CHeavySlamSlash);
                PlaySparksForSwing(AttackSwingType.HeavyOverheadSlam);
                PlayOneShotLayer(layer4GroundSlamShockwave);
                lastTriggerLabel = "4. Heavy Overhead Slam (-90° + คลื่นทุบพื้น)";
                break;
        }

        lastSyncSource = sourceLabel;
        lastTriggerTime = Time.time;
    }

    /// <summary>
    /// ทดสอบสลับยิงครบทั้ง 4 จังหวะต่อเนื่องเมื่อกดปุ่ม [G]
    /// </summary>
    [ContextMenu("ทดสอบยิงเอฟเฟกต์โจมตีจังหวะถัดไป (Test Key G)")]
    public void CycleTestAll4AttackPhases()
    {
        AttackSwingType nextPhase = (AttackSwingType)(testCycleIndex % 4);
        testCycleIndex = (testCycleIndex + 1) % 4;
        TriggerAttackVFX(nextPhase, $"Test Key [{testCycleKey}] ({(int)nextPhase + 1}/4)");
    }

    private void PlaySparksForSwing(AttackSwingType swingType)
    {
        if (layer3FeralSparks == null) return;

        Transform sparkTf = layer3FeralSparks.transform;
        switch (swingType)
        {
            case AttackSwingType.SlashRightToLeft:
                sparkTf.localRotation = Quaternion.Euler(8f, -22f, -38f);
                break;
            case AttackSwingType.SlashLeftToRight:
                sparkTf.localRotation = Quaternion.Euler(8f, 22f, 142f);
                break;
            case AttackSwingType.HeavyOverheadSlam:
                sparkTf.localRotation = Quaternion.Euler(28f, 0f, -90f);
                break;
        }

        PlayOneShotLayer(layer3FeralSparks);
    }

    private static void PlayOneShotLayer(ParticleSystem ps)
    {
        if (ps == null) return;
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.Play(true);
    }

    public void StopAndClearAllLayers()
    {
        StopLayer(layer1TelegraphFlash);
        StopLayer(layer2ASlashRight);
        StopLayer(layer2BSlashLeft);
        StopLayer(layer2CHeavySlamSlash);
        StopLayer(layer3FeralSparks);
        StopLayer(layer4GroundSlamShockwave);
    }

    private static void StopLayer(ParticleSystem ps)
    {
        if (ps != null)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    // =========================================================================
    // INPUT DETECTION (รองรับทั้ง New Input System และ Legacy Input Manager)
    // =========================================================================

    private bool IsTestKeyPressedThisFrame()
    {
        bool pressed = false;

#if ENABLE_INPUT_SYSTEM
        try
        {
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (testCycleKey == KeyCode.G && kb.gKey.wasPressedThisFrame)
                {
                    pressed = true;
                }
                else if (Enum.TryParse(testCycleKey.ToString(), true, out Key mappedKey) && mappedKey != Key.None)
                {
                    var ctrl = kb[mappedKey];
                    if (ctrl != null && ctrl.wasPressedThisFrame)
                    {
                        pressed = true;
                    }
                }
            }
        }
        catch (Exception)
        {
        }
#endif

        if (!pressed)
        {
            try
            {
                if (Input.GetKeyDown(testCycleKey))
                {
                    pressed = true;
                }
            }
            catch (InvalidOperationException)
            {
            }
        }

        return pressed;
    }

    // =========================================================================
    // OnGUI HUD (มุมขวาบนของจอ ไม่ทับกับหน้าต่างระบบอื่น)
    // =========================================================================

    private void OnGUI()
    {
        if (!showDebugHUD) return;

        if (hudFrameMarker != Time.frameCount)
        {
            hudFrameMarker = Time.frameCount;
            hudStackCounter = 0;
        }

        const float boxW = 410f;
        const float boxH = 148f;
        const float margin = 16f;
        int idx = hudStackCounter++;

        Rect panel = new Rect(Screen.width - boxW - margin, margin + idx * (boxH + 10f), boxW, boxH);

        Color prevBg = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.16f, 0.05f, 0.06f, 0.88f);
        GUI.Box(panel, string.Empty);
        GUI.backgroundColor = prevBg;

        GUIStyle titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, richText = true };
        GUIStyle bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true };

        string activePulse = (Time.time - lastTriggerTime < 0.75f) ? " <color=#FFE066>[FIRED!]</color>" : string.Empty;
        string boneInfo = (telegraphPivot != null && telegraphPivot.parent != null)
            ? telegraphPivot.parent.name
            : "Monster Root";

        GUI.Label(new Rect(panel.x + 12f, panel.y + 8f, boxW - 24f, 22f),
            $"<color=#FF5E5E><b>[Monster Anim Attack VFX]</b></color> {gameObject.name}{activePulse}", titleStyle);

        GUI.Label(new Rect(panel.x + 12f, panel.y + 32f, boxW - 24f, 20f),
            $"Animator State: <b>{currentAnimStateReadout}</b>  ({currentAnimNormalizedReadout * 100f:0}%)", bodyStyle);

        GUI.Label(new Rect(panel.x + 12f, panel.y + 54f, boxW - 24f, 20f),
            $"Telegraph Bone: <color=#8BE9FD>{boneInfo}</color>  |  Rules: <b>{(autoSyncRules != null ? autoSyncRules.Count : 0)}</b>", bodyStyle);

        GUI.Label(new Rect(panel.x + 12f, panel.y + 76f, boxW - 24f, 20f),
            $"จังหวะล่าสุด: <color=#FFD166>{lastTriggerLabel}</color>", bodyStyle);

        GUI.Label(new Rect(panel.x + 12f, panel.y + 98f, boxW - 24f, 20f),
            $"แหล่งทริกเกอร์: {lastSyncSource}", bodyStyle);

        GUI.Label(new Rect(panel.x + 12f, panel.y + 120f, boxW - 24f, 22f),
            $"<color=#FFE066>กดปุ่ม [{testCycleKey}] สลับทดสอบครบทั้ง 4 จังหวะ (Telegraph / ขวา / ซ้าย / ทุบหนัก)</color>", bodyStyle);
    }

    // =========================================================================
    // RUNTIME SELF-HEALING 4-LAYER FALLBACK (ทำงานได้ทันทีแม้ยังไม่ได้กดเมนู Generator)
    // =========================================================================

    private void EnsureVFXRigReady()
    {
        if (layer1TelegraphFlash != null &&
            layer2ASlashRight != null &&
            layer2BSlashLeft != null &&
            layer2CHeavySlamSlash != null &&
            layer3FeralSparks != null &&
            layer4GroundSlamShockwave != null)
        {
            return;
        }

        EnsureFallbackAssets();

        Transform vfxRoot = transform.Find("VFX_MonsterAnimAttack_Root");
        if (vfxRoot == null)
        {
            GameObject rootGo = new GameObject("VFX_MonsterAnimAttack_Root");
            rootGo.transform.SetParent(transform, false);
            vfxRoot = rootGo.transform;
        }

        if (telegraphPivot == null)
        {
            Transform handBone = FindMonsterRightHandOrHeadBone(transform, targetAnimator);
            GameObject pGo = new GameObject("Pivot_TelegraphCharge");
            pGo.transform.SetParent(handBone != null ? handBone : vfxRoot, false);
            pGo.transform.localPosition = handBone != null ? Vector3.zero : new Vector3(0.45f, 1.45f, 0.55f);
            telegraphPivot = pGo.transform;
        }

        if (layer1TelegraphFlash == null)
        {
            layer1TelegraphFlash = BuildFallbackLayer1Telegraph(telegraphPivot, fallbackTelegraphMat);
        }

        Transform swingOrigin = vfxRoot.Find("Pivot_ClawSwingOrigin");
        if (swingOrigin == null)
        {
            GameObject sGo = new GameObject("Pivot_ClawSwingOrigin");
            sGo.transform.SetParent(vfxRoot, false);
            sGo.transform.localPosition = new Vector3(0f, 1.25f, 0.55f);
            swingOrigin = sGo.transform;
        }

        if (layer2ASlashRight == null)
            layer2ASlashRight = BuildFallbackClawArc(swingOrigin, "L2A_Main_SlashRight", -38f, 1.75f, -580f, fallbackClawArcMesh, fallbackSlashRedMat, false);

        if (layer2BSlashLeft == null)
            layer2BSlashLeft = BuildFallbackClawArc(swingOrigin, "L2B_Main_SlashLeft", 142f, 1.75f, 580f, fallbackClawArcMesh, fallbackSlashRedMat, false);

        if (layer2CHeavySlamSlash == null)
            layer2CHeavySlamSlash = BuildFallbackClawArc(swingOrigin, "L2C_Main_HeavySlamSlash", -90f, 2.25f, -640f, fallbackClawArcMesh, fallbackSlamGoldMat, true);

        if (layer3FeralSparks == null)
            layer3FeralSparks = BuildFallbackLayer3Sparks(swingOrigin, fallbackSparkMat);

        Transform slamPivot = vfxRoot.Find("Pivot_GroundSlamImpact");
        if (slamPivot == null)
        {
            GameObject gGo = new GameObject("Pivot_GroundSlamImpact");
            gGo.transform.SetParent(vfxRoot, false);
            gGo.transform.localPosition = new Vector3(0f, 0.08f, 1.65f);
            slamPivot = gGo.transform;
        }

        if (layer4GroundSlamShockwave == null)
            layer4GroundSlamShockwave = BuildFallbackLayer4GroundSlam(slamPivot, fallbackGroundRingMat);
    }

    public static Transform FindMonsterRightHandOrHeadBone(Transform root, Animator anim)
    {
        if (anim != null && anim.isHuman)
        {
            Transform humanRightHand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            if (humanRightHand != null) return humanRightHand;
        }

        string[] priorityKeywords = { "RightHand", "R_Hand", "Hand_R", "R Hand", "Right_Hand", "Claw_R", "Claw" };
        Transform[] allBones = root.GetComponentsInChildren<Transform>(true);

        for (int k = 0; k < priorityKeywords.Length; k++)
        {
            string key = priorityKeywords[k];
            for (int i = 0; i < allBones.Length; i++)
            {
                if (allBones[i] != root && allBones[i].name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return allBones[i];
                }
            }
        }

        if (anim != null && anim.isHuman)
        {
            Transform humanHead = anim.GetBoneTransform(HumanBodyBones.Head);
            if (humanHead != null) return humanHead;
        }

        for (int i = 0; i < allBones.Length; i++)
        {
            if (allBones[i] != root && allBones[i].name.IndexOf("Head", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return allBones[i];
            }
        }

        return null;
    }

    private static ParticleSystem BuildFallbackLayer1Telegraph(Transform parent, Material mat)
    {
        ParticleSystem ps = CreateChildPS(parent, "L1_TelegraphChargeFlash");
        var main = ps.main;
        main.duration = 0.35f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = 0.32f;
        main.startSpeed = 0f;
        main.startSize = 1.45f;
        main.startColor = Color.white;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 4;

        var em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0.00f, 1) });
        var sh = ps.shape;
        sh.enabled = false;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0.00f, 0.12f, 6.8f, 6.8f),
            new Keyframe(0.26f, 1.08f, 0.3f, 0.3f),
            new Keyframe(0.70f, 0.85f, -0.6f, -0.6f),
            new Keyframe(1.00f, 0.00f, -2.5f, -2.5f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
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
        col.color = new ParticleSystem.MinMaxGradient(g);

        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.sortingOrder = 14;
        return ps;
    }

    private static ParticleSystem BuildFallbackClawArc(
        Transform parent,
        string name,
        float rollZDeg,
        float scale,
        float spinYDegPerSec,
        Mesh clawMesh,
        Material mat,
        bool isHeavyGold)
    {
        ParticleSystem ps = CreateChildPS(parent, name);
        ps.transform.localRotation = Quaternion.Euler(0f, 0f, rollZDeg);
        ps.transform.localScale = Vector3.one;

        var main = ps.main;
        main.duration = 0.28f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = 0.25f;
        main.startSpeed = 0f;
        main.startSize = scale;
        main.startColor = Color.white;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 4;

        var em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0.00f, 1) });
        var sh = ps.shape;
        sh.enabled = false;

        var rol = ps.rotationOverLifetime;
        rol.enabled = true;
        rol.separateAxes = true;
        rol.x = new ParticleSystem.MinMaxCurve(0f);
        rol.y = new ParticleSystem.MinMaxCurve(spinYDegPerSec * Mathf.Deg2Rad);
        rol.z = new ParticleSystem.MinMaxCurve(0f);

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0.00f, 0.72f, 2.6f, 2.6f),
            new Keyframe(0.28f, 1.06f, 0.3f, 0.3f),
            new Keyframe(1.00f, 1.18f, 0.1f, 0.1f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        if (isHeavyGold)
        {
            g.SetKeys(
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
            g.SetKeys(
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
        col.color = new ParticleSystem.MinMaxGradient(g);

        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Mesh;
        r.mesh = clawMesh;
        r.alignment = ParticleSystemRenderSpace.Local;
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.sortingOrder = 12;
        return ps;
    }

    private static ParticleSystem BuildFallbackLayer3Sparks(Transform parent, Material mat)
    {
        ParticleSystem ps = CreateChildPS(parent, "L3_FeralCrimsonSparks");
        var main = ps.main;
        main.duration = 0.42f;
        main.loop = false;
        main.playOnAwake = false;
        main.startDelay = 0.02f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.36f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(7.0f, 14.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.15f);
        main.startColor = Color.white;
        main.gravityModifier = 0.65f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 36;

        var em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0.00f, 22) });

        var sh = ps.shape;
        sh.enabled = true;
        sh.shapeType = ParticleSystemShapeType.Cone;
        sh.angle = 38f;
        sh.radius = 0.32f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
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
        col.color = new ParticleSystem.MinMaxGradient(g);

        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.055f;
        r.lengthScale = 4.0f;
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.sortingOrder = 13;
        return ps;
    }

    private static ParticleSystem BuildFallbackLayer4GroundSlam(Transform parent, Material mat)
    {
        ParticleSystem ps = CreateChildPS(parent, "L4_HeavyGroundSlamShockwave");
        var main = ps.main;
        main.duration = 0.48f;
        main.loop = false;
        main.playOnAwake = false;
        main.startDelay = 0.04f;
        main.startLifetime = 0.38f;
        main.startSpeed = 0f;
        main.startSize = 3.8f;
        main.startColor = Color.white;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 6;

        var em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new[]
        {
            new ParticleSystem.Burst(0.00f, 1),
            new ParticleSystem.Burst(0.07f, 1)
        });
        var sh = ps.shape;
        sh.enabled = false;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0.00f, 0.14f, 4.2f, 4.2f),
            new Keyframe(0.40f, 0.88f, 0.9f, 0.9f),
            new Keyframe(1.00f, 1.24f, 0.1f, 0.1f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
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
        col.color = new ParticleSystem.MinMaxGradient(g);

        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.sortingOrder = 9;
        return ps;
    }

    private static ParticleSystem CreateChildPS(Transform parent, string childName)
    {
        GameObject go = new GameObject(childName);
        go.transform.SetParent(parent, false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        return ps;
    }

    private static void EnsureFallbackAssets()
    {
        if (fallbackClawArcMesh == null) fallbackClawArcMesh = BuildClawArcMeshRuntime(36, 150f);
        if (fallbackClawTex == null) fallbackClawTex = BuildBeastClawTexRuntime(256, 128);
        if (fallbackTelegraphTex == null) fallbackTelegraphTex = BuildTelegraphFlareTexRuntime(256);
        if (fallbackSlamRingTex == null) fallbackSlamRingTex = BuildSlamRingTexRuntime(256);
        if (fallbackSparkTex == null) fallbackSparkTex = BuildFeralSparkTexRuntime(128);

        if (fallbackTelegraphMat == null) fallbackTelegraphMat = CreateURPAdditiveMaterial("Mat_Runtime_BeastTelegraph", fallbackTelegraphTex, 5.0f);
        if (fallbackSlashRedMat == null) fallbackSlashRedMat = CreateURPAdditiveMaterial("Mat_Runtime_BeastClawRed", fallbackClawTex, 4.2f);
        if (fallbackSlamGoldMat == null) fallbackSlamGoldMat = CreateURPAdditiveMaterial("Mat_Runtime_BeastSlamGold", fallbackClawTex, 4.8f);
        if (fallbackSparkMat == null) fallbackSparkMat = CreateURPAdditiveMaterial("Mat_Runtime_BeastSpark", fallbackSparkTex, 4.2f);
        if (fallbackGroundRingMat == null) fallbackGroundRingMat = CreateURPAdditiveMaterial("Mat_Runtime_BeastGroundRing", fallbackSlamRingTex, 3.8f);
    }

    public static Mesh BuildClawArcMeshRuntime(int segments, float arcAngleDeg)
    {
        Mesh mesh = new Mesh { name = "Mesh_BeastClawArc_Runtime" };
        float halfRad = arcAngleDeg * 0.5f * Mathf.Deg2Rad;
        float outerRadius = 1.0f;
        float innerRadius = 0.46f;

        Vector3[] verts = new Vector3[(segments + 1) * 2];
        Vector2[] uvs = new Vector2[verts.Length];
        Color[] colors = new Color[verts.Length];
        int[] tris = new int[segments * 6];

        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float angle = Mathf.Lerp(-halfRad, halfRad, t);
            float sinA = Mathf.Sin(angle);
            float cosA = Mathf.Cos(angle);

            // หัวและหางเรียวแหลมคมกริบ
            float tipTaper = Mathf.Sin(t * Mathf.PI);
            float localInner = Mathf.Lerp(outerRadius * 0.96f, innerRadius, Mathf.Pow(tipTaper, 0.55f));
            float tipAlpha = Mathf.Pow(tipTaper, 0.72f);

            int v0 = i * 2;
            int v1 = v0 + 1;

            verts[v0] = new Vector3(sinA * localInner, 0f, cosA * localInner);
            verts[v1] = new Vector3(sinA * outerRadius, 0f, cosA * outerRadius);

            uvs[v0] = new Vector2(t, 0f);
            uvs[v1] = new Vector2(t, 1f);

            colors[v0] = new Color(1f, 1f, 1f, tipAlpha * 0.78f);
            colors[v1] = new Color(1f, 1f, 1f, tipAlpha);
        }

        int ti = 0;
        for (int i = 0; i < segments; i++)
        {
            int a = i * 2;
            int b = a + 1;
            int c = a + 2;
            int d = a + 3;

            tris[ti++] = a; tris[ti++] = b; tris[ti++] = c;
            tris[ti++] = b; tris[ti++] = d; tris[ti++] = c;
        }

        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.colors = colors;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Texture2D BuildBeastClawTexRuntime(int w, int h)
    {
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        Color[] px = new Color[w * h];
        for (int y = 0; y < h; y++)
        {
            float v = y / (float)(h - 1);
            for (int x = 0; x < w; x++)
            {
                float u = x / (float)(w - 1);
                float endMask = SmoothStep(0.00f, 0.16f, u) * (1f - SmoothStep(0.78f, 1.00f, u));
                float claw1 = Mathf.Exp(-Mathf.Pow((v - 0.84f) / 0.085f, 2f)) * 1.10f;
                float claw2 = Mathf.Exp(-Mathf.Pow((v - 0.52f) / 0.055f, 2f)) * 0.82f;
                float claw3 = Mathf.Exp(-Mathf.Pow((v - 0.24f) / 0.045f, 2f)) * 0.65f;
                float val = Mathf.Clamp01((claw1 + claw2 + claw3) * endMask);
                px[y * w + x] = new Color(val, val, val, val);
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    private static Texture2D BuildTelegraphFlareTexRuntime(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] px = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            float ny = (y / (float)(size - 1)) * 2f - 1f;
            for (int x = 0; x < size; x++)
            {
                float nx = (x / (float)(size - 1)) * 2f - 1f;
                float r = Mathf.Sqrt(nx * nx + ny * ny);
                float mask = 1f - SmoothStep(0.78f, 0.98f, r);
                float core = Mathf.Exp(-r * r * 16f);
                float hSpike = Mathf.Exp(-Mathf.Abs(ny) * 20f) * Mathf.Exp(-Mathf.Abs(nx) * 1.8f);
                float vSpike = Mathf.Exp(-Mathf.Abs(nx) * 20f) * Mathf.Exp(-Mathf.Abs(ny) * 2.2f) * 0.85f;
                float val = Mathf.Clamp01((core + hSpike + vSpike) * mask);
                px[y * size + x] = new Color(val, val, val, val);
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    private static Texture2D BuildSlamRingTexRuntime(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] px = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            float ny = (y / (float)(size - 1)) * 2f - 1f;
            for (int x = 0; x < size; x++)
            {
                float nx = (x / (float)(size - 1)) * 2f - 1f;
                float r = Mathf.Sqrt(nx * nx + ny * ny);
                if (r >= 0.98f) { px[y * size + x] = Color.clear; continue; }
                float outer = 1f - SmoothStep(0.84f, 0.95f, r);
                float inner = SmoothStep(0.56f, 0.82f, r);
                float rim = Mathf.Exp(-Mathf.Pow((r - 0.83f) / 0.040f, 2f));
                float val = Mathf.Clamp01((inner * outer * 0.65f + rim * 0.98f) * outer);
                px[y * size + x] = new Color(val, val, val, val);
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    private static Texture2D BuildFeralSparkTexRuntime(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] px = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            float ny = (y / (float)(size - 1)) * 2f - 1f;
            for (int x = 0; x < size; x++)
            {
                float nx = (x / (float)(size - 1)) * 2f - 1f;
                float inv = Mathf.Clamp01(1f - Mathf.Sqrt(nx * nx + ny * ny));
                float val = Mathf.Clamp01(Mathf.Pow(inv, 1.9f) * 0.72f + Mathf.Pow(inv, 4.6f) * 0.48f);
                px[y * size + x] = new Color(val, val, val, val);
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    private static Material CreateURPAdditiveMaterial(string name, Texture2D mainTex, float hdrIntensity)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        Color hdrCol = new Color(hdrIntensity, hdrIntensity, hdrIntensity, 1f);
        Material mat = new Material(shader) { name = name };
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
        return mat;
    }

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp01((x - edge0) / Mathf.Max(0.0001f, edge1 - edge0));
        return t * t * (3f - 2f * t);
    }
}
