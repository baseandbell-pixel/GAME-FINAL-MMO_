using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Runtime controller for the AAA Wuxia Sheer Wind &amp; Air Distortion Sword Slash.
/// - Hides and clears all particle layers on Start() so no effect is visible while idle.
/// - Triggers a 3-Hit Wuxia Combo (-25 deg -> 155 deg -> 0 deg) on Left Mouse Click or Spacebar.
/// - Smoothly whips the sword model pivot using explosive ease-out Slerp interpolation.
/// </summary>
[DisallowMultipleComponent]
public class WuxiaSlashController : MonoBehaviour
{
    [Header("Hierarchy References")]
    [Tooltip("Transform that rotates to orient the slash plane (-25, 155, 0 degrees) and triggers the Particle Systems.")]
    public Transform slashVfxRoot;

    [Tooltip("Optional sword pivot Transform that swings across the arc when attacking.")]
    public Transform swordPivot;

    [Header("3-Hit Wuxia Combo Angles (Roll Z Degrees)")]
    public float[] comboSlashAngles = new float[] { -25f, 155f, 0f };
    public float[] comboScaleMultipliers = new float[] { 1.0f, 1.08f, 1.28f };
    public float comboResetTimeout = 1.35f;

    [Header("Sword Swing Motion (Smooth Easing)")]
    public float swingDuration = 0.22f;
    public float swingSlerpSpeed = 28f;
    public float swingStartYaw = -65f;
    public float swingEndYaw = 75f;

    [Header("Input Settings")]
    [Tooltip("When true, listens to Left Mouse Button or Spacebar directly.")]
    public bool listenToDirectInput = true;
    public float minAttackInterval = 0.18f;

    private ParticleSystem[] _allParticles;
    private int _comboIndex;
    private float _lastAttackTime = -999f;

    private bool _isSwinging;
    private float _swingElapsed;
    private Quaternion _swordIdleLocalRot = Quaternion.identity;
    private Quaternion _swordTargetLocalRot = Quaternion.identity;

    private void Awake()
    {
        CacheParticles();
        if (swordPivot != null)
        {
            _swordIdleLocalRot = swordPivot.localRotation;
            _swordTargetLocalRot = _swordIdleLocalRot;
        }
    }

    private void Start()
    {
        // Rule 1: Immediately stop and clear all VFX layers so nothing is visible while standing idle.
        StopAndClearAllEffects();
    }

    public void CacheParticles()
    {
        Transform root = slashVfxRoot != null ? slashVfxRoot : transform;
        _allParticles = root.GetComponentsInChildren<ParticleSystem>(true);
    }

    public void StopAndClearAllEffects()
    {
        if (_allParticles == null || _allParticles.Length == 0)
            CacheParticles();

        if (_allParticles == null) return;

        for (int i = 0; i < _allParticles.Length; i++)
        {
            if (_allParticles[i] == null) continue;
            var main = _allParticles[i].main;
            main.playOnAwake = false;
            main.loop = false;
            _allParticles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    private void Update()
    {
        if (listenToDirectInput && PollAttackTriggered())
        {
            if (Time.time - _lastAttackTime >= minAttackInterval)
            {
                TriggerNextComboSlash();
            }
        }

        UpdateSwordSwingEasing(Time.deltaTime);
    }

    private static bool PollAttackTriggered()
    {
        if (GameplayInput.IsBlocked) return false;

#if ENABLE_INPUT_SYSTEM
        bool mouseClicked = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        bool spacePressed = Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
        if (mouseClicked || spacePressed) return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space))
            return true;
#endif
        return false;
    }

    /// <summary>
    /// Advances the 3-hit combo (-25 deg -> 155 deg -> 0 deg), rotates the slash plane,
    /// starts the high-speed sword swing easing, and fires all 4 VFX layers.
    /// </summary>
    public void TriggerNextComboSlash()
    {
        if (Time.time - _lastAttackTime > comboResetTimeout)
        {
            _comboIndex = 0;
        }

        float angle = (comboSlashAngles != null && comboSlashAngles.Length > 0)
            ? comboSlashAngles[_comboIndex % comboSlashAngles.Length]
            : -25f;

        float scale = (comboScaleMultipliers != null && comboScaleMultipliers.Length > 0)
            ? comboScaleMultipliers[_comboIndex % comboScaleMultipliers.Length]
            : 1.0f;

        TriggerSlashAtAngle(angle, scale);

        _comboIndex = (_comboIndex + 1) % Mathf.Max(1, comboSlashAngles != null ? comboSlashAngles.Length : 3);
        _lastAttackTime = Time.time;
    }

    /// <summary>
    /// Plays the 4-layer Wuxia Sheer Wind &amp; Air Distortion slash at a specific roll angle and scale.
    /// </summary>
    public void TriggerSlashAtAngle(float rollAngleDegrees, float scaleMultiplier = 1.0f)
    {
        Transform vfxTarget = slashVfxRoot != null ? slashVfxRoot : transform;
        vfxTarget.localRotation = Quaternion.Euler(0f, 0f, rollAngleDegrees);
        vfxTarget.localScale = Vector3.one * scaleMultiplier;

        // Start high-speed sword model swing across the same plane
        if (swordPivot != null)
        {
            _isSwinging = true;
            _swingElapsed = 0f;
            Quaternion planeRot = Quaternion.Euler(0f, 0f, rollAngleDegrees);
            swordPivot.localRotation = planeRot * Quaternion.Euler(0f, swingStartYaw, 0f);
            _swordTargetLocalRot = planeRot * Quaternion.Euler(0f, swingEndYaw, 0f);
        }

        if (_allParticles == null || _allParticles.Length == 0)
            CacheParticles();

        if (_allParticles == null) return;

        for (int i = 0; i < _allParticles.Length; i++)
        {
            ParticleSystem ps = _allParticles[i];
            if (ps == null) continue;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Play(true);
        }
    }

    private void UpdateSwordSwingEasing(float dt)
    {
        if (swordPivot == null) return;

        if (_isSwinging)
        {
            _swingElapsed += dt;
            // Smooth explosive ease-out rotation using Quaternion.Slerp
            swordPivot.localRotation = Quaternion.Slerp(
                swordPivot.localRotation,
                _swordTargetLocalRot,
                1f - Mathf.Exp(-swingSlerpSpeed * dt));

            if (_swingElapsed >= swingDuration)
            {
                _isSwinging = false;
            }
        }
        else
        {
            // Gently return sword to idle stance after the slash finishes
            swordPivot.localRotation = Quaternion.Slerp(
                swordPivot.localRotation,
                _swordIdleLocalRot,
                1f - Mathf.Exp(-8f * dt));
        }
    }
}
