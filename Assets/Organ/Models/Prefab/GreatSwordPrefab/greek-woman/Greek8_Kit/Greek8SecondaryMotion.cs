using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lightweight secondary motion for Greek8's existing chest and skirt bones.
/// Attach this component to the character root that contains the Animator and Armature.
///
/// It does NOT create, rename, re-parent, or re-skin bones. It only adds a small spring
/// rotation after the Animator has evaluated. The rig's custom Chest.* and Skirt_* bones
/// are restored to their bind local rotations every frame before simulation, so previous
/// physics rotations cannot feed back and accumulate as they did in the earlier prototype.
///
/// Assumption: the imported animation clips do not intentionally animate Chest.L/R or
/// Skirt_* custom bones. If they do, disable restoreBindLocalRotationEachFrame and make
/// sure the animation's own custom-bone curves are suitable for spring simulation.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(10000)]
public sealed class Greek8SecondaryMotion : MonoBehaviour
{
    [Header("Automatic bone detection")]
    [SerializeField] private bool autoFindBones = true;
    [SerializeField] private bool restoreBindLocalRotationEachFrame = true;
    [SerializeField] private bool logDetectedBones = true;
    [SerializeField] private string chestLeftBoneName = "Chest.L";
    [SerializeField] private string chestRightBoneName = "Chest.R";
    [SerializeField] private string skirtBonePrefix = "Skirt_";

    [Header("Chest spring - subtle movement")]
    [Range(0f, 1f)] public float chestInfluence = 0.38f;
    [Min(0f)] public float chestStiffness = 85f;
    [Min(0f)] public float chestDamping = 18f;
    [Range(0f, 1f)] public float chestGravity = 0.08f;
    [Range(0f, 20f)] public float chestMaxSwingAngle = 10f;
    [Min(0.01f)] public float chestVirtualTipLength = 0.065f;

    [Header("Skirt spring - soft cloth-like sway")]
    [Range(0f, 1f)] public float skirtInfluence = 0.78f;
    [Min(0f)] public float skirtStiffness = 48f;
    [Min(0f)] public float skirtDamping = 14f;
    [Range(0f, 1f)] public float skirtGravity = 0.28f;
    [Range(0f, 35f)] public float skirtMaxSwingAngle = 24f;
    [Min(0.01f)] public float skirtLeafTipLength = 0.19f;

    [Header("Simulation stability")]
    [Range(0.005f, 0.0333f)] public float maxSubstep = 0.0167f;
    [Min(0.1f)] public float maxVelocity = 3.0f;
    [Min(0.1f)] public float teleportResetDistance = 0.65f;

    private sealed class SpringBone
    {
        public Transform bone;
        public Transform childTip;
        public Vector3 virtualTipLocal;
        public Quaternion bindLocalRotation;
        public bool isChest;
        public int hierarchyDepth;
        public Vector3 simulatedTip;
        public Vector3 velocity;
        public bool initialized;
    }

    private readonly List<SpringBone> springBones = new List<SpringBone>();

    private void OnEnable()
    {
        RebuildBoneList();
    }

    private void OnDisable()
    {
        RestoreBindPose();
        springBones.Clear();
    }

    [ContextMenu("Rebuild Greek8 Spring Bones")]
    public void RebuildBoneList()
    {
        springBones.Clear();
        if (!autoFindBones) return;

        Transform[] all = GetComponentsInChildren<Transform>(true);
        int chestCount = 0;
        int skirtCount = 0;

        foreach (Transform item in all)
        {
            if (item == transform) continue;
            if (item.name == chestLeftBoneName || item.name == chestRightBoneName)
            {
                springBones.Add(CreateChestState(item));
                chestCount++;
            }
        }

        foreach (Transform item in all)
        {
            if (item == transform || !item.name.StartsWith(skirtBonePrefix, StringComparison.Ordinal))
                continue;

            Transform nextSkirtBone = null;
            for (int i = 0; i < item.childCount; i++)
            {
                Transform child = item.GetChild(i);
                if (child.name.StartsWith(skirtBonePrefix, StringComparison.Ordinal))
                {
                    nextSkirtBone = child;
                    break;
                }
            }

            springBones.Add(CreateSkirtState(item, nextSkirtBone));
            skirtCount++;
        }

        // Parent bones must be simulated before their child bones.
        springBones.Sort((a, b) => a.hierarchyDepth.CompareTo(b.hierarchyDepth));

        if (logDetectedBones)
        {
            Debug.Log($"[Greek8SecondaryMotion] Found {chestCount} chest bones and {skirtCount} skirt bones under '{name}'.", this);
            if (chestCount != 2)
                Debug.LogWarning("[Greek8SecondaryMotion] Expected Chest.L and Chest.R. Check the imported FBX bone names.", this);
            if (skirtCount == 0)
                Debug.LogWarning("[Greek8SecondaryMotion] No Skirt_ bones found. Put this component on the character root containing the Armature.", this);
        }
    }

    private SpringBone CreateChestState(Transform bone)
    {
        // Chest.L/R are leaf bones. Local +Y is used as a short virtual bone segment.
        Vector3 parentDirection = bone.parent != null ? bone.position - bone.parent.position : bone.up;
        if (parentDirection.sqrMagnitude < 0.000001f) parentDirection = bone.up;
        Vector3 localDirection = bone.InverseTransformDirection(parentDirection.normalized).normalized;

        return new SpringBone
        {
            bone = bone,
            childTip = null,
            virtualTipLocal = localDirection * chestVirtualTipLength,
            bindLocalRotation = bone.localRotation,
            isChest = true,
            hierarchyDepth = GetDepth(bone),
            initialized = false
        };
    }

    private SpringBone CreateSkirtState(Transform bone, Transform childTip)
    {
        Vector3 virtualTip;
        if (childTip != null)
        {
            // Use the real next bone as the segment endpoint.
            virtualTip = bone.InverseTransformPoint(childTip.position);
        }
        else
        {
            // Last bone in each skirt chain has no child. Continue along local +Y using
            // the previous segment length (about 0.19-0.21 m in this Greek5 rig).
            float length = skirtLeafTipLength;
            if (bone.parent != null && bone.parent.name.StartsWith(skirtBonePrefix, StringComparison.Ordinal))
            {
                Vector3 parentToBone = bone.parent.InverseTransformPoint(bone.position);
                if (parentToBone.sqrMagnitude > 0.0001f)
                    length = parentToBone.magnitude;
            }
            Vector3 parentDirection = bone.parent != null ? bone.position - bone.parent.position : bone.up;
            if (parentDirection.sqrMagnitude < 0.000001f) parentDirection = bone.up;
            Vector3 localDirection = bone.InverseTransformDirection(parentDirection.normalized).normalized;
            virtualTip = localDirection * length;
        }

        return new SpringBone
        {
            bone = bone,
            childTip = childTip,
            virtualTipLocal = virtualTip,
            bindLocalRotation = bone.localRotation,
            isChest = false,
            hierarchyDepth = GetDepth(bone),
            initialized = false
        };
    }

    private static int GetDepth(Transform item)
    {
        int depth = 0;
        while (item != null)
        {
            depth++;
            item = item.parent;
        }
        return depth;
    }

    private void LateUpdate()
    {
        if (springBones.Count == 0 || Time.deltaTime <= 0f)
            return;

        // Reset only the custom secondary-motion bones. This prevents the spring output
        // from becoming next frame's input and causing rotation drift/feedback.
        if (restoreBindLocalRotationEachFrame)
        {
            foreach (SpringBone state in springBones)
            {
                if (state.bone != null)
                    state.bone.localRotation = state.bindLocalRotation;
            }
        }

        float frameDt = Mathf.Min(Time.deltaTime, 0.1f);
        int steps = Mathf.Clamp(Mathf.CeilToInt(frameDt / maxSubstep), 1, 12);
        float dt = frameDt / steps;

        // States are parent-first. A skirt parent's spring rotation therefore moves its
        // child segment before that child segment is simulated.
        foreach (SpringBone state in springBones)
        {
            if (state.bone == null) continue;

            Vector3 basePosition = state.bone.position;
            Vector3 targetTip = state.childTip != null
                ? state.childTip.position
                : state.bone.TransformPoint(state.virtualTipLocal);

            Vector3 targetDirection = targetTip - basePosition;
            float segmentLength = targetDirection.magnitude;
            if (segmentLength < 0.0001f) continue;

            if (!state.initialized || (state.simulatedTip - targetTip).sqrMagnitude > teleportResetDistance * teleportResetDistance)
            {
                state.simulatedTip = targetTip;
                state.velocity = Vector3.zero;
                state.initialized = true;
                continue;
            }

            float stiffness = state.isChest ? chestStiffness : skirtStiffness;
            float damping = state.isChest ? chestDamping : skirtDamping;
            float gravityScale = state.isChest ? chestGravity : skirtGravity;
            float influence = state.isChest ? chestInfluence : skirtInfluence;
            float maxAngle = state.isChest ? chestMaxSwingAngle : skirtMaxSwingAngle;

            for (int i = 0; i < steps; i++)
            {
                Vector3 acceleration = (targetTip - state.simulatedTip) * stiffness
                                     + Physics.gravity * gravityScale
                                     - state.velocity * damping;

                state.velocity += acceleration * dt;
                state.velocity = Vector3.ClampMagnitude(state.velocity, maxVelocity);
                state.simulatedTip += state.velocity * dt;

                Vector3 fromBase = state.simulatedTip - basePosition;
                if (fromBase.sqrMagnitude < 0.000001f)
                    fromBase = targetDirection;

                state.simulatedTip = basePosition + fromBase.normalized * segmentLength;
                Vector3 radial = (state.simulatedTip - basePosition).normalized;
                state.velocity = Vector3.ProjectOnPlane(state.velocity, radial);
            }

            Vector3 simulatedDirection = state.simulatedTip - basePosition;
            if (simulatedDirection.sqrMagnitude < 0.000001f) continue;

            Quaternion swing = Quaternion.FromToRotation(targetDirection, simulatedDirection);
            float swingAngle = Quaternion.Angle(Quaternion.identity, swing);
            if (swingAngle > maxAngle && swingAngle > 0.0001f)
                swing = Quaternion.Slerp(Quaternion.identity, swing, maxAngle / swingAngle);

            Quaternion desiredWorldRotation = swing * state.bone.rotation;
            state.bone.rotation = Quaternion.Slerp(state.bone.rotation, desiredWorldRotation, influence);
        }
    }

    private void RestoreBindPose()
    {
        foreach (SpringBone state in springBones)
        {
            if (state.bone != null)
                state.bone.localRotation = state.bindLocalRotation;
        }
    }
}
