using System.Collections;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 100% Frame-Locked Wuxia Pure White Wind Slash Controller with 3-Point Sword &amp; Scabbard Attachment:
///   - Point 1 (Point1_SwordTip):      Crisp Pure White outer wind edge ribbon trail.
///   - Point 2 (Point2_SwordMid):      Sheer White Wind Curtain &amp; Air Distortion trail along mid-blade.
///   - Point 3 (Point3_Scabbard_Hilt): Inner wind ribbon trail + Scabbard Draw Wind Burst (Phase 1).
///
/// Enforces the 3-Phase Frame-Locked Animation Sync:
///   - Phase 1: Wind-up / Draw (0.00s - 0.07s)    -> Sword draws back; ONLY scabbardBurst plays. Blade trails &amp; wave stay OFF.
///   - Phase 2: Active Slash Swing (0.07s - 0.21s)-> Sword whips forward; all 3 blade trails emit + syncedCrescentWave fires in lockstep.
///   - Phase 3: Follow-through (0.21s - 0.35s)    -> Sword stops; all trails immediately stop emitting so the white wind tail fades naturally.
/// </summary>
[AddComponentMenu("yeen VFX/01C. [Player] ตัวควบคุมดาบลมขาว 3 จุด (White Wind Slash)")]
[DisallowMultipleComponent]
public class WuxiaSyncedSlashController : MonoBehaviour
{
    [Header("1. Weapon & 3-Point Attachment References")]
    [Tooltip("Root pivot that rotates the sword across the swing plane.")]
    public Transform weaponPivot;

    [Tooltip("Point 1 (Sword Tip): Razor-sharp pure white wind tip trail.")]
    public TrailRenderer tipTrail;

    [Tooltip("Point 2 (Sword Mid-Blade): Wide sheer wind & air distortion curtain trail.")]
    public TrailRenderer midTrail;

    [Tooltip("Point 3 (Sword Hilt / Inner Blade): Subtle inner wind stream trail.")]
    public TrailRenderer hiltTrail;

    [Header("2. Phase-Synced Particle Layers")]
    [Tooltip("Point 3 (Scabbard Mouth): Pure white wind burst fired ONLY during Phase 1 (Wind-up / Draw: 0.00s - 0.07s).")]
    public ParticleSystem scabbardBurst;

    [Tooltip("Main Crescent Slash Wave + Torn Wind Needles + Residual Wake fired strictly at the start of Phase 2 (0.07s).")]
    public ParticleSystem syncedCrescentWave;

    [Header("3. 3-Hit Combo Angles (-20 -> 160 -> 0 Degrees)")]
    public float[] comboRollAngles = new float[] { -20f, 160f, 0f };
    public float[] comboScales = new float[] { 1.0f, 1.06f, 1.22f };
    public float comboResetSeconds = 1.4f;

    [Header("4. Frame-Locked 3-Phase Timings (Seconds)")]
    [Tooltip("Phase 1 (0.00s - 0.07s): Wind-up & Scabbard Draw Burst. No blade trail or slash wave yet.")]
    public float phase1WindUpDuration = 0.07f;

    [Tooltip("Phase 2 (0.07s - 0.21s): High-speed Active Slash Swing. 3-Point Trails ON + Crescent Wave locked to blade.")]
    public float phase2ActiveSwingDuration = 0.14f;

    [Tooltip("Phase 3 (0.21s - 0.35s): Follow-through & Recovery. Trails stop emitting and fade out smoothly.")]
    public float phase3FollowThroughDuration = 0.14f;

    [Header("5. Sword Arc Angles (Degrees on Swing Plane)")]
    public float windUpStartAngle = -78f;
    public float activeReleaseAngle = -55f;
    public float swingFinishAngle = 78f;

    [Header("Input")]
    public bool listenToDirectInput = true;

    private ParticleSystem[] _allWaveParticles;
    private Coroutine _activeSlashRoutine;
    private int _comboIndex;
    private float _lastSlashTimestamp = -999f;
    private bool _isPerformingSlash;
    private Quaternion _idlePivotRotation = Quaternion.identity;

    private void Awake()
    {
        if (weaponPivot != null)
            _idlePivotRotation = weaponPivot.localRotation;

        CacheParticleSystems();
    }

    private void Start()
    {
        // Strict Requirement: Hide and clear all trails and particles on Start()
        ResetAndClearAllVFX();
    }

    public void CacheParticleSystems()
    {
        if (syncedCrescentWave != null)
            _allWaveParticles = syncedCrescentWave.GetComponentsInChildren<ParticleSystem>(true);
    }

    /// <summary>
    /// Stops all 3 TrailRenderers and clears every ParticleSystem so nothing is visible while idle.
    /// </summary>
    public void ResetAndClearAllVFX()
    {
        SetBladeTrailsEmitting(false, clearExisting: true);

        if (scabbardBurst != null)
        {
            var main = scabbardBurst.main;
            main.playOnAwake = false;
            main.loop = false;
            scabbardBurst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        if (_allWaveParticles == null || _allWaveParticles.Length == 0)
            CacheParticleSystems();

        if (_allWaveParticles != null)
        {
            for (int i = 0; i < _allWaveParticles.Length; i++)
            {
                if (_allWaveParticles[i] == null) continue;
                var main = _allWaveParticles[i].main;
                main.playOnAwake = false;
                main.loop = false;
                _allWaveParticles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }
    }

    private void SetBladeTrailsEmitting(bool emitting, bool clearExisting = false)
    {
        ConfigureTrailState(tipTrail, emitting, clearExisting);
        ConfigureTrailState(midTrail, emitting, clearExisting);
        ConfigureTrailState(hiltTrail, emitting, clearExisting);
    }

    private static void ConfigureTrailState(TrailRenderer tr, bool emitting, bool clearExisting)
    {
        if (tr == null) return;
        tr.emitting = emitting;
        if (clearExisting)
            tr.Clear();
    }

    private void Update()
    {
        if (listenToDirectInput && PollSlashInput())
        {
            // Allow triggering a new slash once Phase 2 (Active Swing) has completed
            if (!_isPerformingSlash)
            {
                TriggerSyncedComboSlash();
            }
        }

        // Smooth return to idle pose when not actively slashing
        if (!_isPerformingSlash && weaponPivot != null)
        {
            weaponPivot.localRotation = Quaternion.Slerp(
                weaponPivot.localRotation,
                _idlePivotRotation,
                1f - Mathf.Exp(-10f * Time.deltaTime));
        }
    }

    private static bool PollSlashInput()
    {
        if (GameplayInput.IsBlocked) return false;

#if ENABLE_INPUT_SYSTEM
        bool leftClick = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        bool spaceKey = Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
        if (leftClick || spaceKey) return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space))
            return true;
#endif
        return false;
    }

    /// <summary>
    /// Starts the 3-Phase Frame-Locked Wuxia Slash Coroutine for the current combo step.
    /// </summary>
    public void TriggerSyncedComboSlash()
    {
        if (Time.time - _lastSlashTimestamp > comboResetSeconds)
            _comboIndex = 0;

        float rollAngle = (comboRollAngles != null && comboRollAngles.Length > 0)
            ? comboRollAngles[_comboIndex % comboRollAngles.Length]
            : -20f;

        float scale = (comboScales != null && comboScales.Length > 0)
            ? comboScales[_comboIndex % comboScales.Length]
            : 1.0f;

        _comboIndex = (_comboIndex + 1) % Mathf.Max(1, comboRollAngles != null ? comboRollAngles.Length : 3);
        _lastSlashTimestamp = Time.time;

        if (_activeSlashRoutine != null)
            StopCoroutine(_activeSlashRoutine);

        _activeSlashRoutine = StartCoroutine(ExecuteSyncedSlashRoutine(rollAngle, scale));
    }

    /// <summary>
    /// 3-Phase Frame-Locked Animation &amp; VFX Synchronization Coroutine:
    ///   - Phase 1 (0.00s - 0.07s): Wind-up / Scabbard Draw Burst ONLY.
    ///   - Phase 2 (0.07s - 0.21s): Active Swing -> 3-Point Trails ON + Crescent Wave locked to sword arc.
    ///   - Phase 3 (0.21s - 0.35s): Follow-through -> Trails OFF, wind wake dissipates naturally.
    /// </summary>
    public IEnumerator ExecuteSyncedSlashRoutine(float rollPlaneDegrees, float scaleMultiplier)
    {
        _isPerformingSlash = true;
        Quaternion planeRotation = Quaternion.Euler(0f, 0f, rollPlaneDegrees);

        // =========================================================================
        // PHASE 1: WIND-UP / DRAW (0.00s - 0.07s)
        // Sword pulls back into chamber pose; ONLY Point 3 Scabbard Draw Burst plays.
        // =========================================================================
        OnAnimEvent_WindUp(rollPlaneDegrees);

        float p1Elapsed = 0f;
        float p1Duration = Mathf.Max(0.01f, phase1WindUpDuration);
        while (p1Elapsed < p1Duration)
        {
            p1Elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(p1Elapsed / p1Duration);
            // Smooth Ease-In chambering motion
            float chamberYaw = Mathf.Lerp(windUpStartAngle, activeReleaseAngle, t * t);
            if (weaponPivot != null)
                weaponPivot.localRotation = planeRotation * Quaternion.Euler(0f, chamberYaw, 0f);
            yield return null;
        }

        // =========================================================================
        // PHASE 2: ACTIVE SLASH SWING (0.07s - 0.21s)
        // Blade whips forward; 3-Point Blade Trails emit + Crescent Wave fires in lockstep!
        // =========================================================================
        OnAnimEvent_ActiveSlash(rollPlaneDegrees, scaleMultiplier);

        float p2Elapsed = 0f;
        float p2Duration = Mathf.Max(0.02f, phase2ActiveSwingDuration);
        while (p2Elapsed < p2Duration)
        {
            p2Elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(p2Elapsed / p2Duration);

            // Explosive Ease-Out curve (1 - (1 - t)^3): fastest at release, decelerating toward finish
            float easeOut = 1f - Mathf.Pow(1f - t, 3f);
            float currentYaw = Mathf.Lerp(activeReleaseAngle, swingFinishAngle, easeOut);

            if (weaponPivot != null)
                weaponPivot.localRotation = planeRotation * Quaternion.Euler(0f, currentYaw, 0f);

            // Frame-lock the Crescent Wave orientation to follow the sword plane cleanly
            if (syncedCrescentWave != null)
            {
                syncedCrescentWave.transform.localRotation = planeRotation * Quaternion.Euler(0f, currentYaw * 0.35f, 0f);
            }

            yield return null;
        }

        // =========================================================================
        // PHASE 3: FOLLOW-THROUGH / RECOVERY (0.21s - 0.35s)
        // Sword stops at the end of the cut; immediately stop trail emission so tails fade out.
        // =========================================================================
        OnAnimEvent_EndSlash();

        float p3Elapsed = 0f;
        float p3Duration = Mathf.Max(0.01f, phase3FollowThroughDuration);
        while (p3Elapsed < p3Duration)
        {
            p3Elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(p3Elapsed / p3Duration);
            // Slight settle overshoot at the end of the cut
            float settleYaw = Mathf.Lerp(swingFinishAngle + 4f, swingFinishAngle, t);
            if (weaponPivot != null)
                weaponPivot.localRotation = planeRotation * Quaternion.Euler(0f, settleYaw, 0f);
            yield return null;
        }

        _isPerformingSlash = false;
        _activeSlashRoutine = null;
    }

    // ====================================================================
    // PUBLIC ANIMATION EVENT HOOKS (For Character Animation Clips)
    // ====================================================================

    /// <summary>Animation Event 1 (0.00s): Chamber sword &amp; trigger Point 3 Scabbard Wind Burst ONLY.</summary>
    public void OnAnimEvent_WindUp()
    {
        float angle = (comboRollAngles != null && comboRollAngles.Length > 0)
            ? comboRollAngles[_comboIndex % comboRollAngles.Length]
            : -20f;
        OnAnimEvent_WindUp(angle);
    }

    public void OnAnimEvent_WindUp(float rollPlaneDegrees)
    {
        // Strictly ensure blade trails and main slash wave are OFF during wind-up!
        SetBladeTrailsEmitting(false, clearExisting: true);

        if (scabbardBurst != null)
        {
            scabbardBurst.transform.localRotation = Quaternion.Euler(0f, 0f, rollPlaneDegrees);
            scabbardBurst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            scabbardBurst.Play(true);
        }
    }

    /// <summary>Animation Event 2 (0.07s): Enable 3-Point Blade Trails &amp; release Crescent Wind Wave.</summary>
    public void OnAnimEvent_ActiveSlash()
    {
        float angle = (comboRollAngles != null && comboRollAngles.Length > 0)
            ? comboRollAngles[_comboIndex % comboRollAngles.Length]
            : -20f;
        float scale = (comboScales != null && comboScales.Length > 0)
            ? comboScales[_comboIndex % comboScales.Length]
            : 1.0f;
        OnAnimEvent_ActiveSlash(angle, scale);
    }

    public void OnAnimEvent_ActiveSlash(float rollPlaneDegrees, float scaleMultiplier)
    {
        // 1. Turn ON all 3 Sword Trail attachment points simultaneously
        SetBladeTrailsEmitting(true, clearExisting: false);

        // 2. Fire the Frame-Locked Crescent Wind Wave
        if (syncedCrescentWave != null)
        {
            syncedCrescentWave.transform.localRotation = Quaternion.Euler(0f, 0f, rollPlaneDegrees);
            syncedCrescentWave.transform.localScale = Vector3.one * scaleMultiplier;

            if (_allWaveParticles == null || _allWaveParticles.Length == 0)
                CacheParticleSystems();

            if (_allWaveParticles != null)
            {
                for (int i = 0; i < _allWaveParticles.Length; i++)
                {
                    if (_allWaveParticles[i] == null) continue;
                    _allWaveParticles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    _allWaveParticles[i].Play(true);
                }
            }
        }
    }

    /// <summary>Animation Event 3 (0.21s): Immediately stop Trail emission at the end of the swing.</summary>
    public void OnAnimEvent_EndSlash()
    {
        SetBladeTrailsEmitting(false, clearExisting: false);
    }
}
