using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lightweight secondary motion for the Greek5 character's existing bones.
/// It does not create, rename, re-parent, or re-skin any bones.
/// Attach it to the character root (the GameObject containing the Armature).
///
/// Auto-detects Chest.L / Chest.R and the 30 bones beginning with "Skirt_".
/// Uses a virtual tip for terminal bones and the next Skirt_ bone for chain links.
/// </summary>
[DefaultExecutionOrder(10000)]
public sealed class GreekSecondaryMotion : MonoBehaviour
{
    [Header("Automatic setup")]
    public bool autoFindBones = true;
    public bool logDetectedBones = true;

    [Header("Chest movement")]
    [Range(0f, 1f)] public float chestInfluence = 0.55f;
    [Min(0f)] public float chestStiffness = 75f;
    [Min(0f)] public float chestDamping = 16f;
    [Range(0f, 1f)] public float chestGravity = 0.05f;

    [Header("Skirt movement")]
    [Range(0f, 1f)] public float skirtInfluence = 0.80f;
    [Min(0f)] public float skirtStiffness = 42f;
    [Min(0f)] public float skirtDamping = 12f;
    [Range(0f, 2f)] public float skirtGravity = 0.65f;

    [Header("Simulation stability")]
    [Range(0.005f, 0.0333f)] public float maxSubstep = 0.0167f;
    [Min(0.1f)] public float maxVelocity = 3.5f;

    private sealed class BoneState
    {
        public Transform bone;
        public Transform childTip;
        public Vector3 virtualTipLocal;
        public bool isChest;
        public Vector3 simulatedTip;
        public Vector3 velocity;
        public bool initialized;
    }

    private readonly List<BoneState> boneStates = new List<BoneState>();

    private void OnEnable()
    {
        RebuildBoneList();
    }

    private void OnDisable()
    {
        boneStates.Clear();
    }

    [ContextMenu("Rebuild Bone List")]
    public void RebuildBoneList()
    {
        boneStates.Clear();

        if (!autoFindBones)
            return;

        Transform[] all = GetComponentsInChildren<Transform>(true);
        int chestCount = 0;
        int skirtCount = 0;

        // Chest.L and Chest.R are terminal bones in this FBX, so they use a virtual tip.
        foreach (Transform item in all)
        {
            if (item == transform) continue;
            if (item.name == "Chest.L" || item.name == "Chest.R")
            {
                boneStates.Add(CreateState(item, null, true));
                chestCount++;
            }
        }

        // Skirt chains have three segments. Each segment follows the next Skirt_ child;
        // the last segment uses a virtual tip based on its parent-to-bone direction.
        foreach (Transform item in all)
        {
            if (item == transform || !item.name.StartsWith("Skirt_", StringComparison.Ordinal))
                continue;

            Transform nextSkirtBone = null;
            for (int i = 0; i < item.childCount; i++)
            {
                Transform child = item.GetChild(i);
                if (child.name.StartsWith("Skirt_", StringComparison.Ordinal))
                {
                    nextSkirtBone = child;
                    break;
                }
            }

            boneStates.Add(CreateState(item, nextSkirtBone, false));
            skirtCount++;
        }

        if (logDetectedBones)
        {
            Debug.Log($"[GreekSecondaryMotion] Found {chestCount} chest bones and {skirtCount} skirt bones under '{name}'.", this);
            if (chestCount != 2)
                Debug.LogWarning("[GreekSecondaryMotion] Expected Chest.L and Chest.R. Check the imported bone names.", this);
            if (skirtCount == 0)
                Debug.LogWarning("[GreekSecondaryMotion] No Skirt_ bones found. Check that this component is on the character root containing the Armature.", this);
        }
    }

    private static BoneState CreateState(Transform bone, Transform childTip, bool isChest)
    {
        // For terminal bones, make a virtual endpoint extending away from the parent.
        // This gives leaf bones such as Chest.L/R a segment for the spring simulation.
        Vector3 localTip;
        if (childTip != null)
        {
            localTip = Vector3.zero;
        }
        else
        {
            Vector3 outward = bone.parent != null ? bone.position - bone.parent.position : bone.up;
            if (outward.sqrMagnitude < 0.000001f)
                outward = bone.up;

            float length = outward.magnitude;
            length = isChest
                ? Mathf.Clamp(length * 0.55f, 0.035f, 0.075f)
                : Mathf.Clamp(length * 0.65f, 0.025f, 0.10f);

            Vector3 localDirection = bone.InverseTransformDirection(outward.normalized).normalized;
            localTip = localDirection * length;
        }

        Vector3 initialTip = childTip != null ? childTip.position : bone.TransformPoint(localTip);
        return new BoneState
        {
            bone = bone,
            childTip = childTip,
            virtualTipLocal = localTip,
            isChest = isChest,
            simulatedTip = initialTip,
            velocity = Vector3.zero,
            initialized = false
        };
    }

    private void LateUpdate()
    {
        if (boneStates.Count == 0 || Time.deltaTime <= 0f)
            return;

        int steps = Mathf.Clamp(Mathf.CeilToInt(Time.deltaTime / maxSubstep), 1, 8);
        float dt = Time.deltaTime / steps;

        // GetComponentsInChildren returns parents before their descendants. This is useful
        // for skirt chains: the upper segment is simulated before the lower segments.
        foreach (BoneState state in boneStates)
        {
            if (state.bone == null) continue;

            Vector3 basePosition = state.bone.position;
            Vector3 targetTip = GetCurrentTip(state);
            Vector3 targetDirection = targetTip - basePosition;
            float segmentLength = targetDirection.magnitude;

            if (segmentLength < 0.0001f)
                continue;

            if (!state.initialized)
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

                // Keep the simulated tip at the bone's original segment length.
                state.simulatedTip = basePosition + fromBase.normalized * segmentLength;

                // Remove velocity along the constrained bone direction.
                Vector3 radial = (state.simulatedTip - basePosition).normalized;
                state.velocity = Vector3.ProjectOnPlane(state.velocity, radial);
            }

            Vector3 simulatedDirection = state.simulatedTip - basePosition;
            if (simulatedDirection.sqrMagnitude < 0.000001f)
                continue;

            Quaternion correction = Quaternion.FromToRotation(targetDirection, simulatedDirection);
            Quaternion targetWorldRotation = correction * state.bone.rotation;
            state.bone.rotation = Quaternion.Slerp(state.bone.rotation, targetWorldRotation, influence);
        }
    }

    private static Vector3 GetCurrentTip(BoneState state)
    {
        return state.childTip != null
            ? state.childTip.position
            : state.bone.TransformPoint(state.virtualTipLocal);
    }
}
