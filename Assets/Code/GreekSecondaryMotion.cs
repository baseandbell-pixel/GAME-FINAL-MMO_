using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Secondary motion for skirt, belt and breast helper bones.
/// Front/back skirt chains can use separate spring profiles. Leg capsules,
/// particle/segment collision and a gentle no-crossing guard help keep panels
/// on their authored side without forcing the skirt away from the body.
/// No external packages required.
/// </summary>
[DefaultExecutionOrder(10000)]
[DisallowMultipleComponent]
public class GreekSecondaryMotion : MonoBehaviour
{
    [Serializable]
    public class Profile
    {
        [Tooltip("Spring speed in Hz. Higher = follows the animated pose faster.")]
        [Min(0.2f)] public float frequency = 3f;
        [Tooltip("Damping ratio. Around 1 reduces bouncing; lower values are more lively.")]
        [Range(0f, 2f)] public float dampingRatio = 0.7f;
        [Tooltip("Multiplier on Physics.gravity.")]
        public float gravity = 0.4f;
        [Tooltip("How much the chain lags behind root movement. 0 = follows closely.")]
        [Range(0f, 1f)] public float inertia = 0.5f;
        [Tooltip("Maximum swing away from the animated segment direction, in degrees.")]
        [Range(1f, 120f)] public float maxAngle = 35f;
        [Tooltip("Extra fabric thickness added to collision radii, in metres.")]
        [Min(0f)] public float colliderRadius = 0.05f;
        [Tooltip("How much overlap already present in the bind pose is forgiven: 1 keeps the original overlap, 0 removes it. Lower values strengthen collision but can push the skirt outward at rest.")]
        [Range(0f, 1f)] public float initialPenetrationAllowance = 0.5f;
        [Range(0f, 1f)] public float weight = 1f;
        [Tooltip("Overall motion multiplier. 0 = disabled, 1 = normal.")]
        [Range(0f, 3f)] public float amount = 1f;
    }

    [Serializable]
    public class SphereCollider3
    {
        public Transform bone;
        public Vector3 localCenter;
        [Min(0f)] public float radius = 0.08f;
    }

    [Serializable]
    public class CapsuleCollider3
    {
        [Tooltip("Transform that owns the first capsule endpoint.")]
        public Transform startBone;
        [Tooltip("Transform that owns the second capsule endpoint.")]
        public Transform endBone;
        [Tooltip("First endpoint in startBone local space.")]
        public Vector3 startLocal;
        [Tooltip("Second endpoint in endBone local space.")]
        public Vector3 endLocal;
        [Min(0.001f)] public float radius = 0.07f;
        [Tooltip("Only leg capsules participate in the front/back side constraint.")]
        public bool isLegCollider = true;
    }

    struct CapsuleFrame
    {
        public Vector3 a;
        public Vector3 b;
        public Vector3 previousA;
        public Vector3 previousB;
        public float radius;
        public bool isLeg;
        public bool valid;
    }

    [Header("Bones")]
    public string skirtRootName = "Skirt_Root";
    public string beltPrefix = "Belt";
    public string[] breastBoneNames = { "Chest.L", "Chest.R" };
    [Tooltip("Length in metres of the virtual tip used by single bones such as Chest.L / Chest.R.")]
    [Min(0.001f)] public float breastTipLength = 0.09f;
    [Tooltip("Flip the virtual breast tip if the imported bone axis points backwards.")]
    public bool breastTipBackwards = false;

    [Header("Skirt Profiles")]
    [Tooltip("Fallback profile for skirt chains whose names do not contain _f_ or _b_.")]
    public Profile skirt = new Profile
    {
        frequency = 3.2f,
        dampingRatio = 0.7f,
        gravity = 0.35f,
        inertia = 0.5f,
        maxAngle = 35f,
        colliderRadius = 0.05f,
        initialPenetrationAllowance = 0.95f
    };
    public bool useSeparateFrontBackProfiles = true;
    [Tooltip("Used automatically by chain names containing _f_. This is intentionally calmer.")]
    public Profile frontSkirt = new Profile
    {
        frequency = 4.2f,
        dampingRatio = 0.95f,
        gravity = 0.10f,
        inertia = 0.18f,
        maxAngle = 16f,
        colliderRadius = 0.015f,
        initialPenetrationAllowance = 0.98f,
        weight = 0.50f,
        amount = 1f
    };
    [Tooltip("Used automatically by chain names containing _b_. This is a little freer than the front.")]
    public Profile backSkirt = new Profile
    {
        frequency = 3.8f,
        dampingRatio = 0.9f,
        gravity = 0.18f,
        inertia = 0.25f,
        maxAngle = 20f,
        colliderRadius = 0.015f,
        initialPenetrationAllowance = 0.98f,
        weight = 0.55f,
        amount = 1f
    };

    [Header("Other Profiles")]
    public Profile belt = new Profile
    {
        frequency = 2.0f,
        dampingRatio = 0.3f,
        gravity = 1.0f,
        inertia = 0.9f,
        maxAngle = 80f,
        colliderRadius = 0.015f,
        initialPenetrationAllowance = 0.95f
    };
    public Profile breast = new Profile
    {
        frequency = 4.5f,
        dampingRatio = 0.22f,
        gravity = 0.25f,
        inertia = 0.55f,
        maxAngle = 16f,
        colliderRadius = 0f
    };

    [Header("Leg / Body Colliders")]
    [Tooltip("Capsules are the main leg collision shapes. Run Auto Setup Capsule Colliders (Humanoid) once.")]
    public List<CapsuleCollider3> capsuleColliders = new List<CapsuleCollider3>();
    [Range(0.5f, 1.5f)] public float capsuleScale = 1f;
    [Tooltip("Optional old sphere colliders. Keep disabled initially to avoid over-inflating the collision volume.")]
    public bool useLegacySphereColliders = false;
    public List<SphereCollider3> colliders = new List<SphereCollider3>();
    [Range(0.5f, 1.5f)] public float colliderScale = 1f;

    [Header("Front / Back Side Constraint")]
    public bool useFrontBackSideConstraint = true;
    [Tooltip("Character forward in Animator local space. If front panels are pushed backwards, try (0, 0, -1).")]
    public Vector3 frontDirectionLocal = new Vector3(0f, 0f, 1f);
    [Tooltip("Extra distance to keep a front/back particle outside the leg centre line, in metres.")]
    [Min(0f)] public float sideClearance = 0.005f;
    [Tooltip("Keep the one-sided correction local to the leg. A smaller range avoids pulling whole skirt panels outward.")]
    [Min(0.01f)] public float sideConstraintRange = 0.22f;
    [Range(0f, 1f)] public float sideConstraintStrength = 0.35f;

    [Header("Simulation Quality")]
    [Range(1, 8)] public int collisionPasses = 4;
    [Tooltip("Maximum simulation step in seconds. 0.0083 is about 120 Hz.")]
    [Range(0.002f, 0.02f)] public float maxSubstepDelta = 0.0083f;
    [Range(1, 20)] public int maxSubsteps = 12;
    [Tooltip("Test particle movement between substeps against moving leg capsules to reduce tunnelling.")]
    public bool useSweptCapsuleCollision = true;
    [Range(1, 4)] public int sweptSamples = 3;
    [Tooltip("Extra collision-only projection passes after length/angle solving. These run last so length correction cannot push the skirt back into the legs.")]
    [Range(0, 6)] public int finalCollisionPasses = 1;
    [Tooltip("After a swept hit, force the particle back to its authored front/back side even if it ended beyond the normal side-constraint range.")]
    public bool recoverSideAfterSweptHit = true;

    [Header("Global")]
    [Range(0f, 1f)] public float globalWeight = 1f;
    [Tooltip("If a chain root moves further than this in one frame, its simulation is reset.")]
    [Min(0.1f)] public float teleportDistance = 1.5f;
    public bool drawGizmos = true;

    class Chain
    {
        public Profile profile;
        public Transform[] bones;
        public Vector3[] initLocalPos;
        public Quaternion[] initLocalRot;
        public Vector3 tipLocal;
        public Vector3[] rest;
        public Vector3[] pos;
        public Vector3[] prev;
        public float[,] sphereSlack;
        public float[,] capsuleSlack;
        public float[] sideThresholds;
        public int sideSign; // +1 front, -1 back, 0 = no side constraint
        public bool ready;
        public Vector3 lastRoot;
    }

    readonly List<Chain> chains = new List<Chain>();
    Animator cachedAnimator;
    Vector3[] previousCapsuleA;
    Vector3[] previousCapsuleB;
    bool[] previousCapsuleValid;
    bool warnedNoCapsules;

    void Awake()
    {
        cachedAnimator = GetComponentInChildren<Animator>();
        Build();
    }

    [ContextMenu("Rebuild Chains")]
    public void Build()
    {
        chains.Clear();
        if (cachedAnimator == null) cachedAnimator = GetComponentInChildren<Animator>();

        Transform skirtRoot = FindDeep(transform, skirtRootName);
        if (skirtRoot != null)
        {
            foreach (Transform child in skirtRoot)
            {
                bool isBelt = child.name.StartsWith(beltPrefix, StringComparison.OrdinalIgnoreCase);
                bool isFront = !isBelt && child.name.IndexOf("_f_", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isBack = !isBelt && child.name.IndexOf("_b_", StringComparison.OrdinalIgnoreCase) >= 0;

                Profile selectedProfile = skirt;
                if (isBelt)
                    selectedProfile = belt;
                else if (useSeparateFrontBackProfiles && isFront)
                    selectedProfile = frontSkirt;
                else if (useSeparateFrontBackProfiles && isBack)
                    selectedProfile = backSkirt;

                AddChain(child, selectedProfile, isFront ? 1 : (isBack ? -1 : 0));
            }
        }
        else
        {
            Debug.LogWarning("[GreekSecondaryMotion] '" + skirtRootName + "' was not found under " + name + ".", this);
        }

        foreach (string boneName in breastBoneNames)
        {
            Transform bone = FindDeep(transform, boneName);
            if (bone != null)
                AddChain(bone, breast, 0, true);
            else
                Debug.LogWarning("[GreekSecondaryMotion] Breast bone '" + boneName + "' was not found.", this);
        }

        ResetSimulation();
        Debug.Log("[GreekSecondaryMotion] Built " + chains.Count + " secondary-motion chains.", this);
    }

    void AddChain(Transform first, Profile profile, int sideSign, bool singleBone = false)
    {
        if (first == null || profile == null) return;

        var boneList = new List<Transform>();
        Transform current = first;
        while (current != null)
        {
            boneList.Add(current);
            if (singleBone || current.childCount == 0) break;
            current = current.GetChild(0);
        }

        if (boneList.Count == 0) return;

        Chain chain = new Chain
        {
            profile = profile,
            bones = boneList.ToArray(),
            sideSign = sideSign
        };

        int count = chain.bones.Length;
        chain.initLocalPos = new Vector3[count];
        chain.initLocalRot = new Quaternion[count];
        for (int i = 0; i < count; i++)
        {
            chain.initLocalPos[i] = chain.bones[i].localPosition;
            chain.initLocalRot[i] = chain.bones[i].localRotation;
        }

        Transform lastBone = chain.bones[count - 1];
        Vector3 tipWorld;
        if (singleBone || count == 1)
        {
            Vector3 tipDirection = transform.forward * (breastTipBackwards ? -1f : 1f);
            tipWorld = lastBone.position + tipDirection * breastTipLength;
        }
        else
        {
            Vector3 previousDirection = lastBone.position - chain.bones[count - 2].position;
            tipWorld = lastBone.position + previousDirection;
        }

        chain.tipLocal = lastBone.InverseTransformPoint(tipWorld);
        chain.rest = new Vector3[count + 1];
        chain.pos = new Vector3[count + 1];
        chain.prev = new Vector3[count + 1];
        chains.Add(chain);
    }

    static Transform FindDeep(Transform root, string targetName)
    {
        if (root == null) return null;
        if (root.name == targetName) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeep(root.GetChild(i), targetName);
            if (found != null) return found;
        }
        return null;
    }

    [ContextMenu("Auto Setup Capsule Colliders (Humanoid)")]
    public void AutoSetupColliders()
    {
        if (cachedAnimator == null) cachedAnimator = GetComponentInChildren<Animator>();
        if (cachedAnimator == null || !cachedAnimator.isHuman)
        {
            Debug.LogWarning("[GreekSecondaryMotion] Auto Setup needs a Humanoid Animator on this object or a child.", this);
            return;
        }

        capsuleColliders.Clear();

        AddCapsuleBetweenBones(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, 0.078f, true);
        AddCapsuleBetweenBones(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, 0.078f, true);
        AddCapsuleBetweenBones(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, 0.052f, true);
        AddCapsuleBetweenBones(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, 0.052f, true);
        AddCapsuleBetweenBones(HumanBodyBones.Hips, HumanBodyBones.Spine, 0.115f, false);
        AddCapsuleBetweenBones(HumanBodyBones.Spine, HumanBodyBones.Chest, 0.105f, false);

        int legCount = 0;
        for (int i = 0; i < capsuleColliders.Count; i++)
            if (capsuleColliders[i].isLegCollider) legCount++;

        previousCapsuleA = null;
        previousCapsuleB = null;
        previousCapsuleValid = null;
        ResetSimulation();

        Debug.Log("[GreekSecondaryMotion] Created " + capsuleColliders.Count + " capsule colliders (" + legCount + " leg capsules). Legacy sphere list was preserved but is disabled by default.", this);
    }

    void AddCapsuleBetweenBones(HumanBodyBones startId, HumanBodyBones endId, float radius, bool isLeg)
    {
        Transform startBone = cachedAnimator.GetBoneTransform(startId);
        Transform endBone = cachedAnimator.GetBoneTransform(endId);
        if (startBone == null || endBone == null) return;

        Vector3 startWorld = Vector3.Lerp(startBone.position, endBone.position, 0.04f);
        Vector3 endWorld = Vector3.Lerp(startBone.position, endBone.position, 0.96f);
        capsuleColliders.Add(new CapsuleCollider3
        {
            startBone = startBone,
            endBone = endBone,
            startLocal = startBone.InverseTransformPoint(startWorld),
            endLocal = endBone.InverseTransformPoint(endWorld),
            radius = radius,
            isLegCollider = isLeg
        });
    }

    [ContextMenu("Reset Simulation (Rebuild Collision Slack)")]
    public void ResetSimulation()
    {
        for (int i = 0; i < chains.Count; i++)
        {
            chains[i].ready = false;
        }
        if (previousCapsuleValid != null)
            Array.Clear(previousCapsuleValid, 0, previousCapsuleValid.Length);
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || globalWeight <= 0f || chains.Count == 0) return;
        dt = Mathf.Min(dt, 0.05f);

        int sphereCount = useLegacySphereColliders ? colliders.Count : 0;
        Vector3[] spherePositions = new Vector3[sphereCount];
        float[] sphereRadii = new float[sphereCount];
        for (int i = 0; i < sphereCount; i++)
        {
            SphereCollider3 sphere = colliders[i];
            if (sphere == null || sphere.bone == null)
            {
                sphereRadii[i] = -1f;
                continue;
            }
            spherePositions[i] = sphere.bone.TransformPoint(sphere.localCenter);
            sphereRadii[i] = sphere.radius * colliderScale;
        }

        CapsuleFrame[] capsuleFrames = BuildCapsuleFrames();
        Vector3 forward = GetCharacterForward();

        if (useFrontBackSideConstraint && capsuleFrames.Length == 0 && !warnedNoCapsules)
        {
            Debug.LogWarning("[GreekSecondaryMotion] Front/back side constraint needs leg capsules. Select Auto Setup Capsule Colliders (Humanoid) from the component menu.", this);
            warnedNoCapsules = true;
        }

        for (int i = 0; i < chains.Count; i++)
            Simulate(chains[i], dt, spherePositions, sphereRadii, capsuleFrames, forward);
    }

    CapsuleFrame[] BuildCapsuleFrames()
    {
        int count = capsuleColliders != null ? capsuleColliders.Count : 0;
        if (previousCapsuleA == null || previousCapsuleA.Length != count)
        {
            previousCapsuleA = new Vector3[count];
            previousCapsuleB = new Vector3[count];
            previousCapsuleValid = new bool[count];
        }

        CapsuleFrame[] frames = new CapsuleFrame[count];
        for (int i = 0; i < count; i++)
        {
            CapsuleCollider3 capsule = capsuleColliders[i];
            if (capsule == null || capsule.startBone == null || capsule.endBone == null)
                continue;

            Vector3 a = capsule.startBone.TransformPoint(capsule.startLocal);
            Vector3 b = capsule.endBone.TransformPoint(capsule.endLocal);
            bool hasPrevious = previousCapsuleValid[i];

            frames[i] = new CapsuleFrame
            {
                a = a,
                b = b,
                previousA = hasPrevious ? previousCapsuleA[i] : a,
                previousB = hasPrevious ? previousCapsuleB[i] : b,
                radius = capsule.radius * capsuleScale,
                isLeg = capsule.isLegCollider,
                valid = true
            };

            previousCapsuleA[i] = a;
            previousCapsuleB[i] = b;
            previousCapsuleValid[i] = true;
        }
        return frames;
    }

    Vector3 GetCharacterForward()
    {
        Transform reference = cachedAnimator != null ? cachedAnimator.transform : transform;
        Vector3 local = frontDirectionLocal;
        if (local.sqrMagnitude < 0.0001f) local = Vector3.forward;
        Vector3 world = reference.TransformDirection(local.normalized);
        if (world.sqrMagnitude < 0.0001f) world = reference.forward;
        return world.normalized;
    }

    void Simulate(Chain chain, float dt, Vector3[] spherePositions, float[] sphereRadii,
        CapsuleFrame[] capsulesNow, Vector3 characterForward)
    {
        int boneCount = chain.bones.Length;
        Profile profile = chain.profile;
        if (boneCount == 0 || profile == null) return;

        // Restore the helper bones' authored local pose before reading this frame's rest positions.
        for (int i = 0; i < boneCount; i++)
        {
            chain.bones[i].localPosition = chain.initLocalPos[i];
            chain.bones[i].localRotation = chain.initLocalRot[i];
        }

        for (int i = 0; i < boneCount; i++) chain.rest[i] = chain.bones[i].position;
        chain.rest[boneCount] = chain.bones[boneCount - 1].TransformPoint(chain.tipLocal);

        Vector3 rootMove = chain.rest[0] - chain.lastRoot;
        bool firstFrame = !chain.ready;
        bool teleported = chain.ready && rootMove.sqrMagnitude > teleportDistance * teleportDistance;
        if (firstFrame || teleported)
        {
            for (int i = 0; i <= boneCount; i++)
            {
                chain.pos[i] = chain.rest[i];
                chain.prev[i] = chain.rest[i];
            }
            rootMove = Vector3.zero;
        }
        chain.lastRoot = chain.rest[0];

        bool sphereDataChanged = chain.sphereSlack == null ||
            chain.sphereSlack.GetLength(0) != boneCount + 1 ||
            chain.sphereSlack.GetLength(1) != spherePositions.Length;
        if (firstFrame || sphereDataChanged)
            InitializeSphereSlack(chain, profile, spherePositions, sphereRadii);

        bool capsuleDataChanged = chain.capsuleSlack == null ||
            chain.capsuleSlack.GetLength(0) != boneCount + 1 ||
            chain.capsuleSlack.GetLength(1) != capsulesNow.Length;
        if (firstFrame || capsuleDataChanged)
            InitializeCapsuleAndSideData(chain, profile, capsulesNow, characterForward);

        chain.ready = true;

        float inertiaEffective = Mathf.Clamp01(profile.inertia * profile.amount);
        float maxAngle = Mathf.Clamp(profile.maxAngle * Mathf.Max(profile.amount, 0.05f), 1f, 120f);
        Vector3 bodyShift = rootMove * (1f - inertiaEffective);
        for (int i = 1; i <= boneCount; i++)
        {
            chain.pos[i] += bodyShift;
            chain.prev[i] += bodyShift;
        }

        int steps = Mathf.Clamp(Mathf.CeilToInt(dt / Mathf.Max(0.002f, maxSubstepDelta)), 1, maxSubsteps);
        float h = dt / steps;
        float omega = 2f * Mathf.PI * profile.frequency;
        float omegaSquared = omega * omega;
        float damping = 2f * profile.dampingRatio * omega;
        Vector3 gravity = Physics.gravity * profile.gravity;
        int passes = Mathf.Clamp(collisionPasses, 1, 8);

        for (int step = 0; step < steps; step++)
        {
            float t0 = step / (float)steps;
            float t1 = (step + 1) / (float)steps;

            chain.pos[0] = chain.rest[0];
            chain.prev[0] = chain.rest[0];

            for (int i = 1; i <= boneCount; i++)
            {
                Vector3 oldPosition = chain.pos[i];
                Vector3 velocity = (chain.pos[i] - chain.prev[i]) / h;
                Vector3 acceleration = omegaSquared * (chain.rest[i] - chain.pos[i]) - damping * velocity + gravity;
                velocity += acceleration * h;
                chain.prev[i] = oldPosition;
                chain.pos[i] = oldPosition + velocity * h;
            }

            for (int pass = 0; pass < passes; pass++)
            {
                // First satisfy bone lengths and swing limits, then solve collisions.
                for (int i = 1; i <= boneCount; i++)
                    ConstrainLengthAndAngle(chain, i, maxAngle);

                for (int i = 1; i <= boneCount; i++)
                {
                    CollideSphereParticle(chain, i, profile, spherePositions, sphereRadii);
                    CollideCapsuleParticle(chain, i, profile, capsulesNow, t1, characterForward);

                    if (useSweptCapsuleCollision)
                        CollideSweptCapsuleParticle(chain, i, profile, capsulesNow, t0, t1, characterForward);

                    CollideSphereSegment(chain, i, profile, spherePositions, sphereRadii);
                    CollideCapsuleSegment(chain, i, profile, capsulesNow, t1, characterForward);
                    ApplyOneSidedParticleConstraint(chain, i, capsulesNow, t1, characterForward);
                    ApplyOneSidedSegmentConstraint(chain, i, capsulesNow, t1, characterForward);
                }
            }

            // Final collision-only passes run AFTER all length/angle corrections.
            // Do not run length correction after these passes: that could push cloth back
            // into the leg immediately after it has been separated from the capsule.
            int finalPasses = Mathf.Clamp(finalCollisionPasses, 0, 6);
            for (int finalPass = 0; finalPass < finalPasses; finalPass++)
            {
                for (int i = 1; i <= boneCount; i++)
                {
                    CollideSphereParticle(chain, i, profile, spherePositions, sphereRadii);
                    CollideCapsuleParticle(chain, i, profile, capsulesNow, t1, characterForward);
                    CollideSphereSegment(chain, i, profile, spherePositions, sphereRadii);
                    CollideCapsuleSegment(chain, i, profile, capsulesNow, t1, characterForward);
                    ApplyOneSidedParticleConstraint(chain, i, capsulesNow, t1, characterForward);
                    ApplyOneSidedSegmentConstraint(chain, i, capsulesNow, t1, characterForward);
                }
            }
        }

        // Write the simulated particle directions back as bone rotations, root to tip.
        float weight = profile.weight * globalWeight;
        for (int i = 0; i < boneCount; i++)
        {
            Transform bone = chain.bones[i];
            Vector3 animatedDirection = i < boneCount - 1
                ? bone.TransformVector(chain.bones[i + 1].localPosition)
                : bone.TransformVector(chain.tipLocal);
            Vector3 simulatedDirection = chain.pos[i + 1] - bone.position;

            if (animatedDirection.sqrMagnitude < 1e-10f || simulatedDirection.sqrMagnitude < 1e-10f)
                continue;

            Quaternion delta = Quaternion.FromToRotation(animatedDirection, simulatedDirection);
            if (weight < 0.999f)
                delta = Quaternion.Slerp(Quaternion.identity, delta, weight);
            bone.rotation = delta * bone.rotation;
        }
    }

    void InitializeSphereSlack(Chain chain, Profile profile, Vector3[] spherePositions, float[] sphereRadii)
    {
        int count = chain.bones.Length + 1;
        chain.sphereSlack = new float[count, spherePositions.Length];
        for (int particle = 0; particle < count; particle++)
        {
            for (int c = 0; c < spherePositions.Length; c++)
            {
                if (sphereRadii[c] < 0f) continue;
                float radius = sphereRadii[c] + profile.colliderRadius;
                float distance = Vector3.Distance(chain.rest[particle], spherePositions[c]);
                float rawOverlap = Mathf.Max(0f, radius - distance);
                chain.sphereSlack[particle, c] = rawOverlap * Mathf.Clamp01(profile.initialPenetrationAllowance);
            }
        }
    }

    void InitializeCapsuleAndSideData(Chain chain, Profile profile, CapsuleFrame[] capsules, Vector3 characterForward)
    {
        int particleCount = chain.bones.Length + 1;
        chain.capsuleSlack = new float[particleCount, capsules.Length];
        chain.sideThresholds = new float[particleCount];

        for (int particle = 0; particle < particleCount; particle++)
        {
            for (int c = 0; c < capsules.Length; c++)
            {
                if (!capsules[c].valid) continue;
                Vector3 closest = ClosestPointOnSegment(chain.rest[particle], capsules[c].a, capsules[c].b);
                float radius = capsules[c].radius + profile.colliderRadius;
                float distance = Vector3.Distance(chain.rest[particle], closest);
                float rawOverlap = Mathf.Max(0f, radius - distance);
                chain.capsuleSlack[particle, c] = rawOverlap * Mathf.Clamp01(profile.initialPenetrationAllowance);
            }

            chain.sideThresholds[particle] = float.NegativeInfinity;
            if (chain.sideSign == 0) continue;

            if (TryGetNearestLegAxis(chain.rest[particle], capsules, 1f, float.PositiveInfinity, out Vector3 nearest, out float nearestRadius))
            {
                float signedDistance = Vector3.Dot(chain.rest[particle] - nearest, characterForward) * chain.sideSign;

                // Important: this is a no-crossing guard, not a fixed cloth-to-leg spacing.
                // Keep only a tiny margin (and never larger than the authored margin), so
                // the solver does not push an entire panel away from the body during attacks.
                if (signedDistance > 0f)
                    chain.sideThresholds[particle] = Mathf.Min(Mathf.Max(0f, sideClearance), signedDistance);
            }
        }

    }

    void ConstrainLengthAndAngle(Chain chain, int particle, float maxAngle)
    {
        Vector3 restSegment = chain.rest[particle] - chain.rest[particle - 1];
        float length = restSegment.magnitude;
        if (length < 1e-6f) return;

        Vector3 direction = chain.pos[particle] - chain.pos[particle - 1];
        float magnitude = direction.magnitude;
        if (magnitude > 1e-6f)
            direction *= length / magnitude;
        else
            direction = restSegment;

        float angle = Vector3.Angle(restSegment, direction);
        if (angle > maxAngle)
        {
            Quaternion fromRest = Quaternion.FromToRotation(restSegment, direction);
            fromRest.ToAngleAxis(out float ignoredAngle, out Vector3 axis);
            if (axis.sqrMagnitude > 1e-8f)
                direction = Quaternion.AngleAxis(maxAngle, axis) * restSegment;
            else
                direction = restSegment;
        }

        chain.pos[particle] = chain.pos[particle - 1] + direction;
    }

    void CollideSphereParticle(Chain chain, int particle, Profile profile, Vector3[] spherePositions, float[] sphereRadii)
    {
        if (!useLegacySphereColliders) return;
        for (int c = 0; c < spherePositions.Length; c++)
        {
            if (sphereRadii[c] < 0f) continue;
            float radius = sphereRadii[c] + profile.colliderRadius - chain.sphereSlack[particle, c];
            if (radius <= 0f) continue;

            Vector3 delta = chain.pos[particle] - spherePositions[c];
            float distanceSquared = delta.sqrMagnitude;
            if (distanceSquared >= radius * radius) continue;

            if (distanceSquared < radius * radius * 0.36f)
            {
                Vector3 restDirection = chain.rest[particle] - spherePositions[c];
                if (restDirection.sqrMagnitude > 1e-8f) delta = restDirection;
                else if (chain.sideSign != 0) delta = GetCharacterForward() * chain.sideSign;
            }

            if (delta.sqrMagnitude < 1e-8f) delta = Vector3.up;
            chain.pos[particle] = spherePositions[c] + delta.normalized * radius;
        }
    }

    void CollideCapsuleParticle(Chain chain, int particle, Profile profile, CapsuleFrame[] capsules, float time01, Vector3 characterForward)
    {
        for (int c = 0; c < capsules.Length; c++)
        {
            CapsuleFrame capsule = capsules[c];
            if (!capsule.valid) continue;

            GetCapsuleAt(capsule, time01, out Vector3 a, out Vector3 b);
            float radius = capsule.radius + profile.colliderRadius - chain.capsuleSlack[particle, c];
            if (radius <= 0f) continue;

            Vector3 closest = ClosestPointOnSegment(chain.pos[particle], a, b);
            Vector3 delta = chain.pos[particle] - closest;
            float distanceSquared = delta.sqrMagnitude;
            if (distanceSquared >= radius * radius) continue;

            if (distanceSquared < radius * radius * 0.36f)
                delta = GetCollisionFallbackDirection(chain, particle, closest, characterForward);
            if (delta.sqrMagnitude < 1e-8f) delta = characterForward * (chain.sideSign == 0 ? 1 : chain.sideSign);
            chain.pos[particle] = closest + delta.normalized * radius;
        }
    }

    void CollideSweptCapsuleParticle(Chain chain, int particle, Profile profile, CapsuleFrame[] capsules,
        float timeStart, float timeEnd, Vector3 characterForward)
    {
        Vector3 pathStart = chain.prev[particle];
        Vector3 pathEnd = chain.pos[particle];
        if ((pathEnd - pathStart).sqrMagnitude < 1e-8f) return;

        for (int c = 0; c < capsules.Length; c++)
        {
            CapsuleFrame capsule = capsules[c];
            if (!capsule.valid) continue;

            float radius = capsule.radius + profile.colliderRadius - chain.capsuleSlack[particle, c];
            if (radius <= 0f) continue;

            bool sweptHit = false;
            int samples = Mathf.Clamp(sweptSamples, 1, 4);
            for (int sample = 0; sample <= samples; sample++)
            {
                float sampleT = Mathf.Lerp(timeStart, timeEnd, sample / (float)samples);
                GetCapsuleAt(capsule, sampleT, out Vector3 a, out Vector3 b);
                ClosestPointsOnSegments(pathStart, pathEnd, a, b, out Vector3 pathPoint, out Vector3 axisPoint);
                if ((pathPoint - axisPoint).sqrMagnitude < radius * radius)
                {
                    sweptHit = true;
                    break;
                }
            }

            if (!sweptHit) continue;

            // Resolve against the capsule at the end of this substep.
            GetCapsuleAt(capsule, timeEnd, out Vector3 endA, out Vector3 endB);
            Vector3 currentClosest = ClosestPointOnSegment(chain.pos[particle], endA, endB);
            Vector3 currentDelta = chain.pos[particle] - currentClosest;
            if (currentDelta.sqrMagnitude < radius * radius)
            {
                if (currentDelta.sqrMagnitude < 1e-8f)
                    currentDelta = GetCollisionFallbackDirection(chain, particle, currentClosest, characterForward);
                chain.pos[particle] = currentClosest + currentDelta.normalized * radius;
            }

            // A fast particle may cross the complete capsule and finish outside it.
            // Ordinary overlap collision will not catch that case, so restore its original
            // front/back identity immediately after a swept hit.
            if (recoverSideAfterSweptHit && capsule.isLeg && useFrontBackSideConstraint && chain.sideSign != 0 &&
                chain.sideThresholds != null && particle < chain.sideThresholds.Length)
            {
                float authoredSide = chain.sideThresholds[particle];
                if (!float.IsNegativeInfinity(authoredSide) && !float.IsNaN(authoredSide))
                {
                    Vector3 sideClosest = ClosestPointOnSegment(chain.pos[particle], endA, endB);
                    float signedDistance = Vector3.Dot(chain.pos[particle] - sideClosest, characterForward) * chain.sideSign;
                    float target = authoredSide;
                    float correction = target - signedDistance;
                    if (correction > 0f)
                    {
                        chain.pos[particle] += characterForward * (chain.sideSign * correction * sideConstraintStrength);
                    }
                }
            }
        }
    }

    void CollideSphereSegment(Chain chain, int particle, Profile profile, Vector3[] spherePositions, float[] sphereRadii)
    {
        if (!useLegacySphereColliders) return;
        for (int c = 0; c < spherePositions.Length; c++)
        {
            if (sphereRadii[c] < 0f) continue;
            float averageSlack = 0.5f * (chain.sphereSlack[particle - 1, c] + chain.sphereSlack[particle, c]);
            float radius = sphereRadii[c] + profile.colliderRadius - averageSlack;
            if (radius <= 0f) continue;

            Vector3 closest = ClosestPointOnSegment(spherePositions[c], chain.pos[particle - 1], chain.pos[particle]);
            Vector3 delta = closest - spherePositions[c];
            float distance = delta.magnitude;
            if (distance >= radius) continue;

            if (distance < radius * 0.6f)
                delta = 0.5f * (chain.rest[particle - 1] + chain.rest[particle]) - spherePositions[c];
            if (delta.sqrMagnitude < 1e-8f) delta = Vector3.up;
            Vector3 push = delta.normalized * (radius - distance);
            MoveSegment(chain, particle, push);
        }
    }

    void CollideCapsuleSegment(Chain chain, int particle, Profile profile, CapsuleFrame[] capsules,
        float time01, Vector3 characterForward)
    {
        for (int c = 0; c < capsules.Length; c++)
        {
            CapsuleFrame capsule = capsules[c];
            if (!capsule.valid) continue;

            GetCapsuleAt(capsule, time01, out Vector3 a, out Vector3 b);
            float averageSlack = 0.5f * (chain.capsuleSlack[particle - 1, c] + chain.capsuleSlack[particle, c]);
            float radius = capsule.radius + profile.colliderRadius - averageSlack;
            if (radius <= 0f) continue;

            ClosestPointsOnSegments(chain.pos[particle - 1], chain.pos[particle], a, b,
                out Vector3 clothPoint, out Vector3 axisPoint);
            Vector3 delta = clothPoint - axisPoint;
            float distance = delta.magnitude;
            if (distance >= radius) continue;

            if (distance < radius * 0.6f)
            {
                Vector3 restMid = 0.5f * (chain.rest[particle - 1] + chain.rest[particle]);
                delta = restMid - axisPoint;
                if (delta.sqrMagnitude < 1e-8f)
                    delta = GetCollisionFallbackDirection(chain, particle, axisPoint, characterForward);
            }

            if (delta.sqrMagnitude < 1e-8f) delta = characterForward * (chain.sideSign == 0 ? 1 : chain.sideSign);
            Vector3 push = delta.normalized * (radius - distance);
            MoveSegment(chain, particle, push);
        }
    }

    void ApplyOneSidedParticleConstraint(Chain chain, int particle, CapsuleFrame[] capsules,
        float time01, Vector3 characterForward)
    {
        if (!useFrontBackSideConstraint || chain.sideSign == 0 || chain.sideThresholds == null ||
            particle >= chain.sideThresholds.Length)
            return;

        float threshold = chain.sideThresholds[particle];
        if (float.IsNegativeInfinity(threshold)) return;

        if (!TryGetNearestLegAxis(chain.pos[particle], capsules, time01, sideConstraintRange, out Vector3 closest, out _))
            return;

        float signedDistance = Vector3.Dot(chain.pos[particle] - closest, characterForward) * chain.sideSign;
        float target = threshold;
        float correction = target - signedDistance;
        if (correction > 0f)
        {
            Vector3 sideDirection = characterForward * chain.sideSign;
            chain.pos[particle] += sideDirection * correction * sideConstraintStrength;
        }
    }

    void ApplyOneSidedSegmentConstraint(Chain chain, int particle, CapsuleFrame[] capsules,
        float time01, Vector3 characterForward)
    {
        if (!useFrontBackSideConstraint || chain.sideSign == 0 || chain.sideThresholds == null ||
            particle >= chain.sideThresholds.Length)
            return;

        float threshold = 0.5f * (chain.sideThresholds[particle - 1] + chain.sideThresholds[particle]);
        if (float.IsNegativeInfinity(threshold) || float.IsNaN(threshold)) return;

        Vector3 midpoint = 0.5f * (chain.pos[particle - 1] + chain.pos[particle]);
        if (!TryGetNearestLegAxis(midpoint, capsules, time01, sideConstraintRange, out Vector3 closest, out _))
            return;

        float signedDistance = Vector3.Dot(midpoint - closest, characterForward) * chain.sideSign;
        float target = threshold;
        float correction = target - signedDistance;
        if (correction <= 0f) return;

        Vector3 push = characterForward * (chain.sideSign * correction * sideConstraintStrength);
        MoveSegment(chain, particle, push);
    }

    bool TryGetNearestLegAxis(Vector3 point, CapsuleFrame[] capsules, float time01, float maxDistance,
        out Vector3 closestPoint, out float radius)
    {
        closestPoint = Vector3.zero;
        radius = 0f;
        float bestDistanceSquared = float.IsPositiveInfinity(maxDistance)
            ? float.PositiveInfinity
            : maxDistance * maxDistance;
        bool found = false;

        for (int i = 0; i < capsules.Length; i++)
        {
            CapsuleFrame capsule = capsules[i];
            if (!capsule.valid || !capsule.isLeg) continue;
            GetCapsuleAt(capsule, time01, out Vector3 a, out Vector3 b);
            Vector3 candidate = ClosestPointOnSegment(point, a, b);
            float distanceSquared = (point - candidate).sqrMagnitude;
            if (distanceSquared < bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                closestPoint = candidate;
                radius = capsule.radius;
                found = true;
            }
        }
        return found;
    }

    Vector3 GetCollisionFallbackDirection(Chain chain, int particle, Vector3 colliderPoint, Vector3 characterForward)
    {
        if (chain.sideSign != 0)
            return characterForward * chain.sideSign;

        Vector3 restDirection = chain.rest[particle] - colliderPoint;
        if (restDirection.sqrMagnitude > 1e-8f) return restDirection;
        return Vector3.up;
    }

    static void MoveSegment(Chain chain, int particle, Vector3 push)
    {
        if (particle - 1 == 0)
            chain.pos[particle] += push * 2f;
        else
        {
            chain.pos[particle] += push;
            chain.pos[particle - 1] += push;
        }
    }

    static void GetCapsuleAt(CapsuleFrame capsule, float time01, out Vector3 a, out Vector3 b)
    {
        a = Vector3.Lerp(capsule.previousA, capsule.a, time01);
        b = Vector3.Lerp(capsule.previousB, capsule.b, time01);
    }

    static Vector3 ClosestPointOnSegment(Vector3 point, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float denominator = Vector3.Dot(ab, ab);
        if (denominator < 1e-10f) return a;
        float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / denominator);
        return a + ab * t;
    }

    // Closest points between finite segments p1-p2 and q1-q2.
    static void ClosestPointsOnSegments(Vector3 p1, Vector3 p2, Vector3 q1, Vector3 q2,
        out Vector3 c1, out Vector3 c2)
    {
        Vector3 d1 = p2 - p1;
        Vector3 d2 = q2 - q1;
        Vector3 r = p1 - q1;
        float a = Vector3.Dot(d1, d1);
        float e = Vector3.Dot(d2, d2);
        float f = Vector3.Dot(d2, r);
        float s;
        float t;

        if (a <= 1e-10f && e <= 1e-10f)
        {
            c1 = p1;
            c2 = q1;
            return;
        }

        if (a <= 1e-10f)
        {
            s = 0f;
            t = Mathf.Clamp01(f / e);
        }
        else
        {
            float c = Vector3.Dot(d1, r);
            if (e <= 1e-10f)
            {
                t = 0f;
                s = Mathf.Clamp01(-c / a);
            }
            else
            {
                float b = Vector3.Dot(d1, d2);
                float denominator = a * e - b * b;
                s = Mathf.Abs(denominator) > 1e-10f
                    ? Mathf.Clamp01((b * f - c * e) / denominator)
                    : 0f;
                t = (b * s + f) / e;
                if (t < 0f)
                {
                    t = 0f;
                    s = Mathf.Clamp01(-c / a);
                }
                else if (t > 1f)
                {
                    t = 1f;
                    s = Mathf.Clamp01((b - c) / a);
                }
            }
        }

        c1 = p1 + d1 * s;
        c2 = q1 + d2 * t;
    }

    void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        if (useLegacySphereColliders && colliders != null)
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.65f);
            foreach (SphereCollider3 sphere in colliders)
            {
                if (sphere == null || sphere.bone == null) continue;
                Gizmos.DrawWireSphere(sphere.bone.TransformPoint(sphere.localCenter), sphere.radius * colliderScale);
            }
        }

        if (capsuleColliders != null)
        {
            for (int i = 0; i < capsuleColliders.Count; i++)
            {
                CapsuleCollider3 capsule = capsuleColliders[i];
                if (capsule == null || capsule.startBone == null || capsule.endBone == null) continue;
                Vector3 a = capsule.startBone.TransformPoint(capsule.startLocal);
                Vector3 b = capsule.endBone.TransformPoint(capsule.endLocal);
                float radius = capsule.radius * capsuleScale;
                Gizmos.color = capsule.isLegCollider
                    ? new Color(1f, 0.55f, 0.15f, 0.8f)
                    : new Color(0.25f, 0.8f, 1f, 0.65f);
                Gizmos.DrawLine(a, b);
                Gizmos.DrawWireSphere(a, radius);
                Gizmos.DrawWireSphere(b, radius);
            }
        }

        Transform reference = cachedAnimator != null ? cachedAnimator.transform : transform;
        Vector3 forward = reference.TransformDirection(frontDirectionLocal.sqrMagnitude > 0.0001f
            ? frontDirectionLocal.normalized : Vector3.forward);
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(reference.position, reference.position + forward * 0.35f);
    }
}
