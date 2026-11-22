using UnityEngine;

/// <summary>
/// Locks the character's left hand onto a LeftHandGrip transform on a two-handed weapon
/// using Humanoid OnAnimatorIK, with smooth automatic blending across Animator states
/// and Blend Tree locomotion speeds.
/// Must be placed on the same GameObject as the Animator component, and the target Animator
/// Layer must have IK Pass enabled.
/// </summary>
[RequireComponent(typeof(Animator))]
[DisallowMultipleComponent]
public class GreatSwordLeftHandIK : MonoBehaviour
{
    public enum ActivationMode
    {
        [Tooltip("Active whenever LeftHandGrip is active in hierarchy, except on states matching Disable Tags.")]
        AlwaysExceptDisabledTags,
        [Tooltip("Active only on states matching Enable Tags (recommended if the sword stays active on the back when unarmed).")]
        OnlyOnEnabledTags,
        [Tooltip("Controlled directly by a Float Parameter / Animation Curve in the Animator.")]
        ByFloatParameter
    }

    [Header("Targets")]
    [Tooltip("Empty child Transform placed on the Great Sword handle where the left palm should hold.")]
    public Transform leftHandGrip;
    [Tooltip("Optional Transform used as an elbow pole hint if the left elbow bends awkwardly inward or upward.")]
    public Transform leftElbowHint;

    [Header("Animator Setup")]
    [Tooltip("Animator layer index that plays the Great Sword animations (Base Layer = 0). Must have IK Pass checked.")]
    [Min(0)] public int animatorLayerIndex = 0;
    public ActivationMode activationMode = ActivationMode.AlwaysExceptDisabledTags;
    [Tooltip("If true, IK automatically blends to 0 when LeftHandGrip (or the sword) is disabled in Hierarchy.")]
    public bool requireGripActiveInHierarchy = true;

    [Header("State Tag Rules")]
    [Tooltip("Used in OnlyOnEnabledTags mode. Add these Tags to Great Sword Animator states.")]
    public string[] enableTags = { "TwoHanded", "GreatSword" };
    [Tooltip("States with these Tags always disable Left Hand IK.")]
    public string[] disableTags = { "Unarmed", "Roll", "NoLeftHandIK" };

    [Header("Blend Tree / Run Auto-Disable")]
    [Tooltip("Automatically disable Left Hand IK when Blend Tree movement speed exceeds Disable Speed Threshold.")]
    public bool disableOnLocomotionSpeed = true;
    [Tooltip("Float parameter driving your 1D Blend Tree (e.g. Speed) or the X axis of a 2D Blend Tree (e.g. MoveX).")]
    public string speedParameter = "Speed";
    [Tooltip("Optional second Float parameter for 2D Blend Trees (e.g. MoveY). Leave empty for 1D Blend Trees.")]
    public string secondSpeedParameter = "";
    [Tooltip("IK turns off when movement magnitude is greater than this value. Match the Run threshold in your Blend Tree.")]
    [Min(0f)] public float disableSpeedThreshold = 0.6f;
    [Tooltip("Optional Bool parameter in Animator (e.g. IsRunning or IsSprinting) that disables IK when true.")]
    public string disableBoolParameter = "";
    [Tooltip("States with these Tags ignore the speed/bool cutoff so attacks triggered while running still grab the handle.")]
    public string[] ignoreSpeedCutoffTags = { "Attack" };

    [Header("Animator Float Parameter (Optional)")]
    [Tooltip("Float parameter or Animation Curve name (0 to 1). Used in ByFloatParameter mode, or as an extra multiplier if enabled below.")]
    public string ikWeightParameter = "LeftHandIK";
    [Tooltip("Even in Tag modes, multiply the target IK weight by the Float Parameter/Curve above if it exists in the Animator.")]
    public bool useParameterAsMultiplier = false;

    [Header("Weights & Blending")]
    public bool masterEnabled = true;
    [Range(0f, 1f)] public float positionWeight = 1f;
    [Range(0f, 1f)] public float rotationWeight = 1f;
    [Range(0f, 1f)] public float elbowHintWeight = 0.35f;
    [Tooltip("How fast IK blends from 0 to 1 when grabbing the handle.")]
    [Min(0.1f)] public float blendInSpeed = 12f;
    [Tooltip("How fast IK blends from 1 to 0 when releasing the handle (e.g. Run, Roll, or Unarmed).")]
    [Min(0.1f)] public float blendOutSpeed = 24f;

    [Header("Fine-Tune Offset (Grip Local Space)")]
    [Tooltip("Optional local position nudge relative to LeftHandGrip, in metres.")]
    public Vector3 localPositionOffset = Vector3.zero;
    [Tooltip("Optional local rotation nudge relative to LeftHandGrip, in Euler degrees.")]
    public Vector3 localRotationOffset = Vector3.zero;

    [Header("Debug")]
    public bool drawGizmos = true;

    Animator animator;
    bool parametersCached;

    int ikWeightParamHash;
    bool hasIkWeightParam;

    int speedParamHash;
    bool hasSpeedParam;

    int secondSpeedParamHash;
    bool hasSecondSpeedParam;

    int disableBoolParamHash;
    bool hasDisableBoolParam;

    float currentWeight;
    bool eventDisabledForState;
    int eventDisabledStateHash;
    bool warnedNonHuman;

    public float CurrentWeight => currentWeight;

    void Awake()
    {
        animator = GetComponent<Animator>();
        parametersCached = false;
    }

    void OnEnable()
    {
        if (animator == null) animator = GetComponent<Animator>();
        parametersCached = false;
    }

    void OnValidate()
    {
        parametersCached = false;
    }

    void EnsureParametersCached()
    {
        if (parametersCached) return;
        if (animator == null || animator.runtimeAnimatorController == null || !animator.isInitialized) return;

        hasIkWeightParam = false;
        hasSpeedParam = false;
        hasSecondSpeedParam = false;
        hasDisableBoolParam = false;

        ikWeightParamHash = !string.IsNullOrEmpty(ikWeightParameter) ? Animator.StringToHash(ikWeightParameter) : 0;
        speedParamHash = !string.IsNullOrEmpty(speedParameter) ? Animator.StringToHash(speedParameter) : 0;
        secondSpeedParamHash = !string.IsNullOrEmpty(secondSpeedParameter) ? Animator.StringToHash(secondSpeedParameter) : 0;
        disableBoolParamHash = !string.IsNullOrEmpty(disableBoolParameter) ? Animator.StringToHash(disableBoolParameter) : 0;

        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter p = parameters[i];
            if (p.type == AnimatorControllerParameterType.Float)
            {
                if (ikWeightParamHash != 0 && p.nameHash == ikWeightParamHash)
                    hasIkWeightParam = true;
                if (speedParamHash != 0 && p.nameHash == speedParamHash)
                    hasSpeedParam = true;
                if (secondSpeedParamHash != 0 && p.nameHash == secondSpeedParamHash)
                    hasSecondSpeedParam = true;
            }
            else if (p.type == AnimatorControllerParameterType.Bool)
            {
                if (disableBoolParamHash != 0 && p.nameHash == disableBoolParamHash)
                    hasDisableBoolParam = true;
            }
        }

        parametersCached = true;
    }

    public void SetIKEnabled(bool enabled)
    {
        masterEnabled = enabled;
    }

    public void SetLeftHandGrip(Transform newGrip)
    {
        leftHandGrip = newGrip;
    }

    public void DisableIKForCurrentState()
    {
        if (animator == null || animatorLayerIndex >= animator.layerCount) return;
        eventDisabledForState = true;
        eventDisabledStateHash = animator.GetCurrentAnimatorStateInfo(animatorLayerIndex).fullPathHash;
    }

    public void EnableIKForCurrentState()
    {
        eventDisabledForState = false;
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (layerIndex != animatorLayerIndex) return;
        if (animator == null) return;

        if (!animator.isHuman)
        {
            if (!warnedNonHuman)
            {
                Debug.LogWarning("[GreatSwordLeftHandIK] Animator must use a Humanoid Avatar for OnAnimatorIK.", this);
                warnedNonHuman = true;
            }
            return;
        }

        if (animatorLayerIndex >= animator.layerCount) return;

        EnsureParametersCached();

        float dt = animator.updateMode == AnimatorUpdateMode.Fixed ? Time.fixedDeltaTime : Time.deltaTime;
        float targetWeight = EvaluateTargetWeight();
        float speed = targetWeight >= currentWeight ? blendInSpeed : blendOutSpeed;
        currentWeight = Mathf.MoveTowards(currentWeight, targetWeight, speed * dt);

        if (currentWeight <= 0.0001f || leftHandGrip == null)
        {
            animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
            animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
            animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 0f);
            return;
        }

        Vector3 targetPos = leftHandGrip.TransformPoint(localPositionOffset);
        Quaternion targetRot = leftHandGrip.rotation * Quaternion.Euler(localRotationOffset);

        animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, currentWeight * positionWeight);
        animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, currentWeight * rotationWeight);
        animator.SetIKPosition(AvatarIKGoal.LeftHand, targetPos);
        animator.SetIKRotation(AvatarIKGoal.LeftHand, targetRot);

        if (leftElbowHint != null && elbowHintWeight > 0.001f)
        {
            animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, currentWeight * elbowHintWeight);
            animator.SetIKHintPosition(AvatarIKHint.LeftElbow, leftElbowHint.position);
        }
        else
        {
            animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 0f);
        }
    }

    float EvaluateTargetWeight()
    {
        if (!masterEnabled || leftHandGrip == null)
            return 0f;

        if (requireGripActiveInHierarchy && !leftHandGrip.gameObject.activeInHierarchy)
            return 0f;

        AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(animatorLayerIndex);

        if (eventDisabledForState && currentState.fullPathHash != eventDisabledStateHash)
            eventDisabledForState = false;

        if (eventDisabledForState)
            return 0f;

        AnimatorStateInfo activeState = animator.IsInTransition(animatorLayerIndex)
            ? animator.GetNextAnimatorStateInfo(animatorLayerIndex)
            : currentState;

        bool ignoreSpeedCutoff = MatchesAnyTag(activeState, ignoreSpeedCutoffTags);
        if (!ignoreSpeedCutoff)
        {
            if (hasDisableBoolParam && animator.GetBool(disableBoolParamHash))
                return 0f;

            if (disableOnLocomotionSpeed && hasSpeedParam)
            {
                float v1 = animator.GetFloat(speedParamHash);
                float moveMagnitude;
                if (hasSecondSpeedParam)
                {
                    float v2 = animator.GetFloat(secondSpeedParamHash);
                    moveMagnitude = Mathf.Sqrt(v1 * v1 + v2 * v2);
                }
                else
                {
                    moveMagnitude = Mathf.Abs(v1);
                }

                if (moveMagnitude > disableSpeedThreshold)
                    return 0f;
            }
        }

        float result = 0f;

        switch (activationMode)
        {
            case ActivationMode.AlwaysExceptDisabledTags:
                result = MatchesAnyTag(activeState, disableTags) ? 0f : 1f;
                break;

            case ActivationMode.OnlyOnEnabledTags:
                if (MatchesAnyTag(activeState, disableTags))
                    result = 0f;
                else
                    result = MatchesAnyTag(activeState, enableTags) ? 1f : 0f;
                break;

            case ActivationMode.ByFloatParameter:
                if (hasIkWeightParam)
                    result = Mathf.Clamp01(animator.GetFloat(ikWeightParamHash));
                else
                    result = 0f;
                break;
        }

        if (useParameterAsMultiplier && activationMode != ActivationMode.ByFloatParameter && hasIkWeightParam)
            result *= Mathf.Clamp01(animator.GetFloat(ikWeightParamHash));

        return result;
    }

    static bool MatchesAnyTag(AnimatorStateInfo stateInfo, string[] tags)
    {
        if (tags == null || tags.Length == 0) return false;
        for (int i = 0; i < tags.Length; i++)
        {
            string tag = tags[i];
            if (!string.IsNullOrEmpty(tag) && stateInfo.IsTag(tag))
                return true;
        }
        return false;
    }

    void OnDrawGizmosSelected()
    {
        if (!drawGizmos || leftHandGrip == null) return;

        Vector3 pos = leftHandGrip.TransformPoint(localPositionOffset);
        Quaternion rot = leftHandGrip.rotation * Quaternion.Euler(localRotationOffset);

        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.9f);
        Gizmos.DrawWireSphere(pos, 0.025f);

        Gizmos.color = Color.red;
        Gizmos.DrawLine(pos, pos + rot * Vector3.right * 0.06f);
        Gizmos.color = Color.green;
        Gizmos.DrawLine(pos, pos + rot * Vector3.up * 0.06f);
        Gizmos.color = Color.blue;
        Gizmos.DrawLine(pos, pos + rot * Vector3.forward * 0.06f);

        if (leftElbowHint != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(leftElbowHint.position, 0.02f);
        }
    }
}