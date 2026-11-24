using UnityEngine;

/// <summary>
/// Connects the 3-Point Pure White Wuxia Wind Slash ('Point1_SwordTip', 'Point2_SwordMid', 'Point3_Scabbard_Hilt')
/// and the Frame-Locked Crescent Wave directly to the character's weapon bone ('Sword_Hand' &amp; 'Sword_Back').
/// Enforces:
///   1) 3-Point Sword &amp; Scabbard Attachment (Tip, Mid, Hilt/Scabbard).
///   2) Pure White Sheer Wind (R=1, G=1, B=1, Alpha 0.15..0.65, zero green/blue/blobs).
///   3) 100% Frame-Locked 3-Phase Animation Sync with the character's Animator attack states.
/// </summary>
[AddComponentMenu("yeen VFX/01. [Player] ฟันดาบสายลม (Sword Wind Slash)")]
[DisallowMultipleComponent]
public class SwordWindSlashVFX : MonoBehaviour
{
    [Header("Pure White Synced Crescent Prefab")]
    public GameObject windSlashPrefab;

    [Header("Frame-Locked Animation Sync (Normalized Time 0..1)")]
    [Tooltip("Phase 1 (0.00 -> activeSlashStartNormalized): Wind-up only; blade trails & wave stay OFF.")]
    [Range(0.08f, 0.38f)] public float activeSlashStartNormalized = 0.19f;

    [Tooltip("Phase 2 (activeSlashStartNormalized -> activeSlashEndNormalized): Blade whips forward; 3-Point Trails ON + Crescent Wave fires.")]
    [Range(0.30f, 0.75f)] public float activeSlashEndNormalized = 0.48f;

    public Vector3 waveSpawnOffset = new Vector3(0f, 1.15f, 0.75f);

    private Animator _anim;
    private Transform _swordHand;
    private TrailRenderer _tipTrail;
    private TrailRenderer _midTrail;
    private TrailRenderer _hiltTrail;

    private int _lastAttackStateHash;
    private int _lastAttackLoopCount = -1;
    private bool _waveFiredThisSwing;

    private static readonly int HashLight1 = Animator.StringToHash("Light Attack 1");
    private static readonly int HashLight2 = Animator.StringToHash("Light Attack 2");
    private static readonly int HashLight3 = Animator.StringToHash("Light Attack 3");
    private static readonly int HashHeavy = Animator.StringToHash("Heavy Attack");
    private static readonly int HashRunAttack = Animator.StringToHash("Run Attack");
    private static readonly int HashRunHeavy = Animator.StringToHash("Run Heavy Attack");
    private static readonly int HashJumpAttack = Animator.StringToHash("Jump Attack");
    private static readonly int HashJumpHeavy = Animator.StringToHash("Jump Heavy Attack");
    private static readonly int HashRunJumpHeavy = Animator.StringToHash("Run Jump Heavy Attack");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoHookPlayersInScene()
    {
        var monitorGo = new GameObject("VFX_AutoPlayerBinder");
        DontDestroyOnLoad(monitorGo);
        monitorGo.AddComponent<VfxPlayerAutoBinder>();
    }

    private void Awake()
    {
        _anim = GetComponentInChildren<Animator>();
        FindSwordHand();
        if (_swordHand != null)
        {
            Setup3PointBladeTrails();
        }
    }

    private void Start()
    {
        Set3PointTrailsEmitting(false, clear: true);
    }

    private void FindSwordHand()
    {
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "Sword_Hand")
            {
                _swordHand = t;
                break;
            }
        }
    }

    private void Setup3PointBladeTrails()
    {
        // Remove legacy green trail if present
        Transform legacy = _swordHand.Find("WindBladeTrailPoint");
        if (legacy != null) Destroy(legacy.gameObject);

        Material sharedWhiteTrailMat = BuildRuntimePureWhiteTrailMaterial();

        // Point 1: Sword Tip (Outer razor wind line)
        _tipTrail = EnsureAttachmentTrail(_swordHand, "Point1_SwordTip", new Vector3(0f, 0f, 4.4f),
            sharedWhiteTrailMat, width: 0.20f, duration: 0.15f, maxAlpha: 0.62f);

        // Point 2: Sword Mid-Blade (Wide sheer wind curtain)
        _midTrail = EnsureAttachmentTrail(_swordHand, "Point2_SwordMid", new Vector3(0f, 0f, 2.5f),
            sharedWhiteTrailMat, width: 0.58f, duration: 0.17f, maxAlpha: 0.38f);

        // Point 3: Scabbard / Hilt (Inner wind stream)
        _hiltTrail = EnsureAttachmentTrail(_swordHand, "Point3_Scabbard_Hilt", new Vector3(0f, 0f, 0.8f),
            sharedWhiteTrailMat, width: 0.28f, duration: 0.13f, maxAlpha: 0.22f);
    }

    private static TrailRenderer EnsureAttachmentTrail(
        Transform parent,
        string pointName,
        Vector3 localPos,
        Material mat,
        float width,
        float duration,
        float maxAlpha)
    {
        Transform pt = parent.Find(pointName);
        if (pt == null)
        {
            pt = new GameObject(pointName).transform;
            pt.SetParent(parent, false);
            pt.localPosition = localPos;
        }

        var tr = pt.GetComponent<TrailRenderer>();
        if (tr == null) tr = pt.gameObject.AddComponent<TrailRenderer>();

        tr.time = duration;
        tr.minVertexDistance = 0.03f;
        tr.autodestruct = false;
        tr.emitting = false;
        tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        tr.receiveShadows = false;
        tr.numCornerVertices = 4;
        tr.numCapVertices = 4;
        tr.widthMultiplier = width;
        tr.widthCurve = new AnimationCurve(
            new Keyframe(0f, 0.15f),
            new Keyframe(0.15f, 1.0f),
            new Keyframe(1f, 0.0f)
        );

        // Pure White (R=1, G=1, B=1) with soft sheer alpha
        Gradient grad = new Gradient();
        grad.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0.0f),
                new GradientColorKey(Color.white, 1.0f)
            },
            new[]
            {
                new GradientAlphaKey(0.0f, 0.0f),
                new GradientAlphaKey(maxAlpha, 0.12f),
                new GradientAlphaKey(maxAlpha * 0.5f, 0.55f),
                new GradientAlphaKey(0.0f, 1.0f)
            }
        );
        tr.colorGradient = grad;
        tr.sharedMaterial = mat;
        return tr;
    }

    private static Material BuildRuntimePureWhiteTrailMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        var mat = new Material(shader);
        mat.SetColor("_BaseColor", new Color(1.65f, 1.65f, 1.65f, 1.0f)); // Soft HDR Pure White (1.65x)
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f); // Alpha Blend (SrcAlpha OneMinusSrcAlpha)
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_Cull", 0f);
        mat.renderQueue = 3060;
        return mat;
    }

    private void Set3PointTrailsEmitting(bool emitting, bool clear = false)
    {
        if (_tipTrail != null) { _tipTrail.emitting = emitting; if (clear) _tipTrail.Clear(); }
        if (_midTrail != null) { _midTrail.emitting = emitting; if (clear) _midTrail.Clear(); }
        if (_hiltTrail != null) { _hiltTrail.emitting = emitting; if (clear) _hiltTrail.Clear(); }
    }

    private void Update()
    {
        if (_anim == null) return;

        AnimatorStateInfo state = _anim.GetCurrentAnimatorStateInfo(0);
        bool inAttack = state.IsTag("Attack");

        if (!inAttack)
        {
            Set3PointTrailsEmitting(false, clear: false);
            _lastAttackStateHash = 0;
            _lastAttackLoopCount = -1;
            _waveFiredThisSwing = false;
            return;
        }

        int loopCount = Mathf.FloorToInt(state.normalizedTime);
        float normTime = state.normalizedTime - loopCount;

        if (state.shortNameHash != _lastAttackStateHash || loopCount != _lastAttackLoopCount)
        {
            _lastAttackStateHash = state.shortNameHash;
            _lastAttackLoopCount = loopCount;
            _waveFiredThisSwing = false;
            // Phase 1 (Wind-up): Ensure trails stay OFF during wind-up!
            Set3PointTrailsEmitting(false, clear: true);
        }

        // Phase 2 (Active Swing Window): normTime between activeSlashStartNormalized and activeSlashEndNormalized
        bool isActiveSwingPhase = normTime >= activeSlashStartNormalized && normTime <= activeSlashEndNormalized;
        Set3PointTrailsEmitting(isActiveSwingPhase, clear: false);

        if (!_waveFiredThisSwing && normTime >= activeSlashStartNormalized)
        {
            _waveFiredThisSwing = true;
            GetSwingAngleAndScale(state.shortNameHash, out float tiltAngle, out float scaleMult);
            SpawnSyncedWhiteSlashWave(tiltAngle, scaleMult);
        }
    }

    private static void GetSwingAngleAndScale(int shortHash, out float tiltAngle, out float scaleMult)
    {
        if (shortHash == HashLight1)
        {
            tiltAngle = -20f;
            scaleMult = 1.0f;
        }
        else if (shortHash == HashLight2)
        {
            tiltAngle = 160f;
            scaleMult = 1.08f;
        }
        else if (shortHash == HashLight3)
        {
            tiltAngle = 0f;
            scaleMult = 1.24f;
        }
        else if (shortHash == HashHeavy || shortHash == HashRunHeavy)
        {
            tiltAngle = -75f;
            scaleMult = 1.32f;
        }
        else if (shortHash == HashJumpAttack || shortHash == HashJumpHeavy || shortHash == HashRunJumpHeavy)
        {
            tiltAngle = -88f;
            scaleMult = 1.36f;
        }
        else if (shortHash == HashRunAttack)
        {
            tiltAngle = 15f;
            scaleMult = 1.15f;
        }
        else
        {
            tiltAngle = -20f;
            scaleMult = 1.0f;
        }
    }

    public void SpawnSyncedWhiteSlashWave(float rollTiltDegrees = -20f, float scaleMultiplier = 1f)
    {
        Vector3 worldPos = transform.position + transform.TransformDirection(waveSpawnOffset);
        Quaternion swingRot = transform.rotation * Quaternion.Euler(0f, 0f, rollTiltDegrees);

        GameObject prefab = windSlashPrefab != null
            ? windSlashPrefab
            : Resources.Load<GameObject>("VFX/VFX_WindBladeSlash");

        if (prefab == null) return;

        GameObject instance = Instantiate(prefab, worldPos, swingRot);
        instance.transform.localScale = Vector3.one * scaleMultiplier;

        foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            main.loop = false;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Play(true);
        }
        Destroy(instance, 1.2f);
    }
}

public class VfxPlayerAutoBinder : MonoBehaviour
{
    private float _nextCheck;

    private void Update()
    {
        if (Time.unscaledTime < _nextCheck) return;
        _nextCheck = Time.unscaledTime + 0.4f;

        foreach (var pc in FindObjectsByType<PlayerController>())
        {
            if (pc != null && pc.GetComponent<SwordWindSlashVFX>() == null)
                pc.gameObject.AddComponent<SwordWindSlashVFX>();
        }

        foreach (var combat in PlayerCombat.All)
        {
            if (combat != null && combat.GetComponent<SwordWindSlashVFX>() == null)
                combat.gameObject.AddComponent<SwordWindSlashVFX>();
        }
    }
}
