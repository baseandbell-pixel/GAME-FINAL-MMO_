using System.Collections;
using UnityEngine;

/// <summary>
/// Bridges Monster AI/Network scripts (<see cref="MonsterEnemyNPC"/>) to a 3D monster's
/// <see cref="Animator"/> driven by <c>AC_Monster_Base</c> and <c>AOC_&lt;MonsterName&gt;</c>.
///
/// Features:
///   - Cached <see cref="Animator.StringToHash"/> IDs exclusively (zero string calls in Update).
///   - Enforces <c>applyRootMotion = false</c> + pins <c>mixamorig:Hips</c> local XZ in LateUpdate while alive.
///   - Exposes <see cref="IsInAttackState"/> / <see cref="IsInHitState"/> so AI never slides during animations.
///   - PBR-safe Emission hit-flash & block-flash via MaterialPropertyBlock.
/// </summary>
[DisallowMultipleComponent]
public class MonsterAnimatorBridge : MonoBehaviour
{
    // ---- Cached Animator parameter hashes (exact 10 parameters of AC_Monster_Base) ----
    public static readonly int HashSpeed = Animator.StringToHash("Speed");
    public static readonly int HashIsCombat = Animator.StringToHash("IsCombat");
    public static readonly int HashAttack = Animator.StringToHash("Attack");
    public static readonly int HashAttackIndex = Animator.StringToHash("AttackIndex");
    public static readonly int HashSkill = Animator.StringToHash("Skill");
    public static readonly int HashSkillIndex = Animator.StringToHash("SkillIndex");
    public static readonly int HashIsCasting = Animator.StringToHash("IsCasting");
    public static readonly int HashHit = Animator.StringToHash("Hit");
    public static readonly int HashIsStunned = Animator.StringToHash("IsStunned");
    public static readonly int HashIsDead = Animator.StringToHash("IsDead");

    private static readonly int StateLocomotion = Animator.StringToHash("Base Layer.Locomotion");
    private static readonly int StateSpawn = Animator.StringToHash("Base Layer.Spawn");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private MonsterAnimationConfig config;
    [SerializeField] private Transform visualRoot;
    [SerializeField] private Renderer[] visualRenderers;

    // ---- Public API ----
    public Animator Animator => animator;
    public MonsterAnimationConfig Config => config;
    public Transform VisualRoot => visualRoot;
    public Renderer[] VisualRenderers => visualRenderers;
    public Renderer PrimaryVisualRenderer => (visualRenderers != null && visualRenderers.Length > 0) ? visualRenderers[0] : null;

    public bool IsDead => _isDead;
    public bool IsCombat => _isCombat;
    public bool IsStunned => _isStunned;
    public bool IsCasting => _isCasting;
    public float CurrentSpeed => _smoothedSpeed;

    public bool CanFlinch
    {
        get
        {
            if (_superArmorOverride) return false;
            return config == null || config.EffectiveCanFlinch;
        }
    }

    public int AttackVariantCount => config != null ? Mathf.Clamp(config.attackVariantCount, 1, 3) : 2;
    public int SkillVariantCount => config != null ? Mathf.Clamp(config.skillVariantCount, 1, 3) : 1;

    /// <summary>True while Base Layer is currently in (or transitioning into) an Attack state.</summary>
    public bool IsInAttackState => IsBaseLayerInTag("Attack");

    /// <summary>True while Base Layer is currently in (or transitioning into) a Hit_React state.</summary>
    public bool IsInHitState => IsBaseLayerInTag("Hit");

    /// <summary>True while Base Layer is currently in (or transitioning into) a Stun state.</summary>
    public bool IsInStunState => IsBaseLayerInTag("Stun");

    // ---- Runtime state ----
    private bool _isDead;
    private bool _isCombat;
    private bool _isStunned;
    private bool _isCasting;
    private bool _superArmorOverride;
    private float _smoothedSpeed;
    private float _speedVelocity;
    private float _lastHitReactTime = -100f;
    private int _lastAttackIndex = -1;
    private Vector3 _lastWorldPos;
    private bool _hasLastWorldPos;
    private bool _speedInjectedThisFrame;

    private Transform _hipsBone;
    private Vector2 _hipsBaseLocalXZ;
    private bool _hasHipsBase;

    private Coroutine _flashRoutine;
    private MaterialPropertyBlock _flashMpb;
    private bool _parametersValid;

    private void Awake()
    {
        ResolveReferences();
        ConfigureAnimator();
        _lastWorldPos = transform.position;
        _hasLastWorldPos = true;
    }

    private void OnEnable()
    {
        ConfigureAnimator();
        _lastWorldPos = transform.position;
        _hasLastWorldPos = true;
    }

    private void OnDisable()
    {
        if (_flashRoutine != null)
        {
            StopCoroutine(_flashRoutine);
            _flashRoutine = null;
        }
        ClearHitFlash();
    }

    public void BindReferences(Animator targetAnimator, MonsterAnimationConfig targetConfig, Transform targetVisualRoot, Renderer[] renderers)
    {
        animator = targetAnimator;
        config = targetConfig;
        visualRoot = targetVisualRoot;
        visualRenderers = renderers;
        ConfigureAnimator();
    }

    private void ResolveReferences()
    {
        if (animator == null)
        {
            animator = visualRoot != null
                ? visualRoot.GetComponentInChildren<Animator>(true)
                : GetComponentInChildren<Animator>(true);
        }

        if (visualRoot == null && animator != null && animator.transform != transform)
            visualRoot = animator.transform;

        if (visualRenderers == null || visualRenderers.Length == 0)
        {
            if (visualRoot != null)
            {
                visualRenderers = visualRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (visualRenderers.Length == 0)
                    visualRenderers = visualRoot.GetComponentsInChildren<Renderer>(true);
            }
        }

        if (_hipsBone == null && visualRoot != null)
        {
            foreach (Transform t in visualRoot.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                if (n == "mixamorig:Hips" ||
                    n.EndsWith(":Hips", System.StringComparison.OrdinalIgnoreCase) ||
                    n.Equals("Hips", System.StringComparison.OrdinalIgnoreCase) ||
                    n.Equals("Pelvis", System.StringComparison.OrdinalIgnoreCase) ||
                    n.EndsWith("Pelvis", System.StringComparison.OrdinalIgnoreCase) ||
                    n.Equals("root.x", System.StringComparison.OrdinalIgnoreCase) ||
                    n.Equals("cave_troll_Hub001", System.StringComparison.OrdinalIgnoreCase) ||
                    n.Equals("COG", System.StringComparison.OrdinalIgnoreCase) ||
                    n.Equals("DEF-hips", System.StringComparison.OrdinalIgnoreCase) ||
                    n.Equals("Root_M", System.StringComparison.OrdinalIgnoreCase))
                {
                    _hipsBone = t;
                    _hipsBaseLocalXZ = new Vector2(t.localPosition.x, t.localPosition.z);
                    _hasHipsBase = true;
                    break;
                }
            }
        }
    }

    private void ConfigureAnimator()
    {
        if (animator == null) return;
        animator.applyRootMotion = false;
        _parametersValid = animator.runtimeAnimatorController != null && animator.isActiveAndEnabled;
    }

    private void LateUpdate()
    {
        if (!_speedInjectedThisFrame)
            UpdateSpeedFromTransform(Time.deltaTime);

        _speedInjectedThisFrame = false;
        _lastWorldPos = transform.position;
        _hasLastWorldPos = true;

        // Pin Hips local XZ while alive so Generic clips never slide away from the collider
        bool pinHips = config == null || config.lockHipsXZInPlace;
        if (pinHips && !_isDead && _hipsBone != null && _hasHipsBase)
        {
            Vector3 lp = _hipsBone.localPosition;
            _hipsBone.localPosition = new Vector3(_hipsBaseLocalXZ.x, lp.y, _hipsBaseLocalXZ.y);
        }
    }

    // ------------------------------------------------------------------ Locomotion

    public void SetWorldSpeed(float worldSpeed)
    {
        _speedInjectedThisFrame = true;
        if (!EnsureAnimatorReady()) return;

        float target = (_isDead || _isStunned || IsInAttackState || IsInHitState) ? 0f : Mathf.Max(0f, worldSpeed);
        float stopEps = config != null ? config.moveStopEpsilon : 0.05f;
        if (target < stopEps) target = 0f;

        float damp = config != null ? config.speedDampTime : 0.14f;
        _smoothedSpeed = Mathf.SmoothDamp(_smoothedSpeed, target, ref _speedVelocity, Mathf.Max(0.02f, damp), Mathf.Infinity, Time.deltaTime);
        if (target == 0f && _smoothedSpeed < stopEps)
        {
            _smoothedSpeed = 0f;
            _speedVelocity = 0f;
        }

        animator.SetFloat(HashSpeed, _smoothedSpeed);
    }

    public void UpdateSpeedFromTransform(float dt)
    {
        if (!_hasLastWorldPos || dt <= 0.0001f)
        {
            SetWorldSpeed(0f);
            return;
        }

        Vector3 delta = transform.position - _lastWorldPos;
        delta.y = 0f;
        float speed = delta.magnitude / dt;
        if (speed > 15f) speed = 0f;
        SetWorldSpeed(speed);
    }

    // ------------------------------------------------------------------ Combat & Actions

    public void SetCombat(bool inCombat)
    {
        if (_isDead) inCombat = false;
        _isCombat = inCombat;
        if (!EnsureAnimatorReady()) return;
        animator.SetBool(HashIsCombat, inCombat);
    }

    public int PickNextAttackIndex()
    {
        int count = AttackVariantCount;
        if (count <= 1)
        {
            _lastAttackIndex = 0;
            return 0;
        }

        int pick = Random.Range(0, count);
        if (pick == _lastAttackIndex)
            pick = (pick + 1) % count;

        _lastAttackIndex = pick;
        return pick;
    }

    public int TriggerRandomAttack()
    {
        int idx = PickNextAttackIndex();
        TriggerAttack(idx);
        return idx;
    }

    public void TriggerAttack(int attackIndex)
    {
        if (_isDead || _isStunned || !EnsureAnimatorReady()) return;

        int clamped = Mathf.Clamp(attackIndex, 0, Mathf.Max(0, AttackVariantCount - 1));
        _lastAttackIndex = clamped;

        // Snap speed to 0 immediately when launching an attack so the blend tree doesn't fight the swing
        _smoothedSpeed = 0f;
        _speedVelocity = 0f;
        animator.SetFloat(HashSpeed, 0f);

        animator.SetInteger(HashAttackIndex, clamped);
        animator.ResetTrigger(HashHit);
        animator.ResetTrigger(HashAttack);
        animator.SetTrigger(HashAttack);
    }

    public void StartSkillCast(int skillIndex = 0)
    {
        if (_isDead || _isStunned || !EnsureAnimatorReady()) return;

        int clamped = Mathf.Clamp(skillIndex, 0, Mathf.Max(0, SkillVariantCount - 1));
        _isCasting = true;

        animator.SetInteger(HashSkillIndex, clamped);
        animator.SetBool(HashIsCasting, true);
        animator.ResetTrigger(HashSkill);
        animator.SetTrigger(HashSkill);
    }

    public void ReleaseSkill()
    {
        _isCasting = false;
        if (!EnsureAnimatorReady()) return;
        animator.SetBool(HashIsCasting, false);
    }

    // ------------------------------------------------------------------ Hit / Stun / Death / Pool Reset

    public void TriggerHit()
    {
        if (_isDead || !CanFlinch || !EnsureAnimatorReady()) return;

        float cooldown = config != null ? config.hitReactCooldown : 0.25f;
        if (Time.time - _lastHitReactTime < cooldown) return;
        _lastHitReactTime = Time.time;

        animator.ResetTrigger(HashHit);
        animator.SetTrigger(HashHit);
    }

    public void SetSuperArmor(bool superArmor)
    {
        _superArmorOverride = superArmor;
    }

    public void SetStunned(bool stunned)
    {
        if (_isDead) stunned = false;
        _isStunned = stunned;
        if (!EnsureAnimatorReady()) return;

        if (stunned)
        {
            _isCasting = false;
            _smoothedSpeed = 0f;
            _speedVelocity = 0f;
            animator.SetFloat(HashSpeed, 0f);
            animator.SetBool(HashIsCasting, false);
            animator.ResetTrigger(HashAttack);
            animator.ResetTrigger(HashSkill);
        }
        animator.SetBool(HashIsStunned, stunned);
    }

    public void SetDead(bool dead)
    {
        if (_isDead == dead && !dead) return;
        _isDead = dead;
        if (!EnsureAnimatorReady()) return;

        if (dead)
        {
            _isCombat = false;
            _isStunned = false;
            _isCasting = false;
            _smoothedSpeed = 0f;
            _speedVelocity = 0f;

            animator.SetFloat(HashSpeed, 0f);
            animator.SetBool(HashIsCombat, false);
            animator.SetBool(HashIsStunned, false);
            animator.SetBool(HashIsCasting, false);
            animator.ResetTrigger(HashAttack);
            animator.ResetTrigger(HashSkill);
            animator.ResetTrigger(HashHit);
            animator.SetBool(HashIsDead, true);
        }
        else
        {
            animator.SetBool(HashIsDead, false);
        }
    }

    public void ResetForPoolSpawn()
    {
        _isDead = false;
        _isCombat = false;
        _isStunned = false;
        _isCasting = false;
        _smoothedSpeed = 0f;
        _speedVelocity = 0f;
        _lastHitReactTime = -100f;
        _lastWorldPos = transform.position;
        _hasLastWorldPos = true;

        ClearHitFlash();

        if (!EnsureAnimatorReady()) return;

        animator.applyRootMotion = false;
        animator.SetFloat(HashSpeed, 0f);
        animator.SetBool(HashIsCombat, false);
        animator.SetBool(HashIsCasting, false);
        animator.SetBool(HashIsStunned, false);
        animator.SetBool(HashIsDead, false);
        animator.ResetTrigger(HashAttack);
        animator.ResetTrigger(HashSkill);
        animator.ResetTrigger(HashHit);

        if (animator.HasState(0, StateSpawn))
            animator.Play(StateSpawn, 0, 0f);
        else if (animator.HasState(0, StateLocomotion))
            animator.Play(StateLocomotion, 0, 0f);
    }

    // ------------------------------------------------------------------ PBR-safe Hit & Block Flash

    public void PlayHitFlash()
    {
        Color col = config != null ? config.hitFlashEmissionColor : new Color(0.85f, 0.25f, 0.20f, 1f);
        PlayFlash(col);
    }

    public void PlayBlockFlash()
    {
        Color col = config != null ? config.blockFlashEmissionColor : new Color(1.0f, 0.72f, 0.18f, 1f);
        PlayFlash(col);
    }

    private void PlayFlash(Color flashCol)
    {
        if (config != null && !config.enableHitFlash) return;
        if (!gameObject.activeInHierarchy || visualRenderers == null || visualRenderers.Length == 0) return;

        if (_flashRoutine != null) StopCoroutine(_flashRoutine);
        _flashRoutine = StartCoroutine(FlashRoutine(flashCol));
    }

    private IEnumerator FlashRoutine(Color flashCol)
    {
        float dur = config != null ? config.hitFlashDuration : 0.11f;
        ApplyEmissionOverride(flashCol);
        yield return new WaitForSeconds(dur);
        ClearHitFlash();
        _flashRoutine = null;
    }

    private void ApplyEmissionOverride(Color emission)
    {
        if (visualRenderers == null) return;
        _flashMpb ??= new MaterialPropertyBlock();
        for (int i = 0; i < visualRenderers.Length; i++)
        {
            Renderer r = visualRenderers[i];
            if (r == null) continue;
            r.GetPropertyBlock(_flashMpb);
            _flashMpb.SetColor(EmissionColorId, emission);
            r.SetPropertyBlock(_flashMpb);
        }
    }

    private void ClearHitFlash()
    {
        if (visualRenderers == null) return;
        for (int i = 0; i < visualRenderers.Length; i++)
        {
            Renderer r = visualRenderers[i];
            if (r == null) continue;
            r.SetPropertyBlock(null);
        }
    }

    private bool IsBaseLayerInTag(string tagName)
    {
        if (!EnsureAnimatorReady()) return false;
        AnimatorStateInfo cur = animator.GetCurrentAnimatorStateInfo(0);
        if (cur.IsTag(tagName)) return true;
        return animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).IsTag(tagName);
    }

    private bool EnsureAnimatorReady()
    {
        if (animator == null) ResolveReferences();
        if (animator == null) return false;
        if (!_parametersValid)
            _parametersValid = animator.runtimeAnimatorController != null && animator.isActiveAndEnabled;
        return _parametersValid && animator.isActiveAndEnabled && animator.runtimeAnimatorController != null;
    }
}
