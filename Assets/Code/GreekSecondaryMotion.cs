using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Secondary motion for skirt, belt and breast helper bones.
/// Uses forward/back-preserving capsule projection, bounded root push-out for upper-pelvis clearance,
/// horizontal cloth-bridge collision between adjacent chains, and neighbor cohesion so thighs/underwear
/// never poke through between skirt chains without puffing the hips.
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
        [Min(0f)] public float colliderRadius = 0.02f;
        [Tooltip("How much overlap already present in the bind pose is forgiven (capped to colliderRadius on leg capsules).")]
        [Range(0f, 1f)] public float initialPenetrationAllowance = 0.25f;
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
        [Tooltip("Radius at startBone endpoint, in metres.")]
        [Min(0.001f)] public float radius = 0.07f;
        [Tooltip("Optional separate radius at endBone endpoint (0 = same as radius).")]
        [Min(0f)] public float endRadius = 0f;
        [Tooltip("Only leg capsules participate in the front/back side constraint.")]
        public bool isLegCollider = true;
    }

    struct CapsuleFrame
    {
        public Vector3 a;
        public Vector3 b;
        public Vector3 previousA;
        public Vector3 previousB;
        public float startRadius;
        public float endRadius;
        public bool isLeg;
        public bool valid;
    }

    struct NeighborLink
    {
        public Chain a;
        public Chain b;
        public int sideSign;
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
        colliderRadius = 0.018f,
        initialPenetrationAllowance = 0.25f
    };
    public bool useSeparateFrontBackProfiles = true;
    [Tooltip("Used automatically by chain names containing _f_. This is intentionally calmer.")]
    public Profile frontSkirt = new Profile
    {
        frequency = 4.5f,
        dampingRatio = 0.95f,
        gravity = 0.10f,
        inertia = 0.18f,
        maxAngle = 16f,
        colliderRadius = 0.020f,
        initialPenetrationAllowance = 0f,
        weight = 0.55f,
        amount = 1f
    };
    [Tooltip("Used automatically by chain names containing _b_. This is a little freer than the front.")]
    public Profile backSkirt = new Profile
    {
        frequency = 3.8f,
        dampingRatio = 0.9f,
        gravity = 0.16f,
        inertia = 0.25f,
        maxAngle = 22f,
        colliderRadius = 0.015f,
        initialPenetrationAllowance = 0.35f,
        weight = 0.60f,
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
        colliderRadius = 0f,
        initialPenetrationAllowance = 1f
    };

    [Header("Leg / Body Colliders")]
    [Tooltip("Capsules are the main leg collision shapes. Run Auto Setup Capsule Colliders (Humanoid) once.")]
    public List<CapsuleCollider3> capsuleColliders = new List<CapsuleCollider3>();
    [Range(0.5f, 1.5f)] public float capsuleScale = 1f;
    [Tooltip("Allow Hips/Spine body capsules to push skirt chains forward to cover the lower pelvis/groin under the belt.")]
    public bool collideSkirtWithBodyCapsules = true;
    [Tooltip("Optional old sphere colliders. Keep disabled initially to avoid over-inflating the collision volume.")]
    public bool useLegacySphereColliders = false;
    public List<SphereCollider3> colliders = new List<SphereCollider3>();
    [Range(0.5f, 1.5f)] public float colliderScale = 1f;

    [Header("Front / Back Side Constraint")]
    public bool useFrontBackSideConstraint = true;
    [Tooltip("Use the Humanoid Hips bone rotation for front/back direction so torso twists during attacks and rolls do not push the skirt sideways.")]
    public bool usePelvisReferenceForFrontBack = true;
    [Tooltip("Character forward in Animator local space at bind/neutral pose. If front panels are pushed backwards, try (0, 0, -1).")]
    public Vector3 frontDirectionLocal = new Vector3(0f, 0f, 1f);
    [Tooltip("Extra distance outside the leg surface when recovering a panel that crossed behind a leg, in metres.")]
    [Min(0f)] public float sideClearance = 0.005f;
    [Tooltip("Maximum wrong-side depth behind a stepping leg that triggers surface recovery, in metres.")]
    [Min(0.01f)] public float sideConstraintRange = 0.35f;
    [Tooltip("Lateral margin around each leg capsule where wrong-side recovery is allowed to act, in metres.")]
    [Min(0f)] public float sideLateralMargin = 0.03f;
    [Range(0f, 1f)] public float sideConstraintStrength = 0.85f;

    [Header("Cloth Cohesion (Prevent Splitting & Mid-Span Poking)")]
    [Tooltip("Link adjacent front chains together (and back chains together) so a stepping leg lifts the panel as one sheet.")]
    public bool useNeighborCohesion = true;
    [Tooltip("Maximum horizontal stretch allowed between adjacent skirt chains relative to rest spacing (0.04 = 4%).")]
    [Range(0f, 0.4f)] public float maxHorizontalStretch = 0.04f;
    [Tooltip("How strongly a chain lifted forward by a leg carries its neighboring chains forward with it.")]
    [Range(0f, 1f)] public float neighborCohesionStrength = 0.9f;
    [Tooltip("Collide the horizontal mesh bridge spanning between adjacent skirt chains against leg/pelvis capsules to stop oval poke-through spots.")]
    public bool useHorizontalBridgeCollision = true;
    [Tooltip("Extra clearance (in metres) added only to horizontal bridge checks to compensate for curved thigh surfaces poking between discrete bones.")]
    [Range(0f, 0.03f)] public float bridgeThicknessBonus = 0.008f;
    [Tooltip("Allow the root bone (_1) of front skirt chains to slide slightly forward when pelvis/upper thigh pokes right below the belt.")]
    public bool allowRootPushOut = true;
    [Tooltip("Also allow back skirt root bones (_1) to slide backward on collision. Keep false unless back hips poke through.")]
    public bool allowBackRootPushOut = false;
    [Tooltip("Maximum forward/backward translation allowed on root bone (_1), in metres.")]
    [Range(0f, 0.05f)] public float maxRootPushOut = 0.025f;

    [Header("Simulation Quality")]
    [Range(1, 8)] public int collisionPasses = 4;
    [Tooltip("Maximum simulation step in seconds. 0.0083 is about 120 Hz.")]
    [Range(0.002f, 0.02f)] public float maxSubstepDelta = 0.0083f;
    [Range(1, 20)] public int maxSubsteps = 12;
    [Tooltip("Test particle movement between substeps against moving leg capsules to reduce tunnelling.")]
    public bool useSweptCapsuleCollision = true;
    [Range(1, 4)] public int sweptSamples = 3;
    [Tooltip("Extra collision passes after main solving inside each substep.")]
    [Range(0, 6)] public int finalCollisionPasses = 1;
    [Tooltip("Final hard collision + horizontal bridge + cohesion passes run AFTER profile.weight blending.")]
    [Range(0, 8)] public int renderCollisionPasses = 6;
    [Tooltip("Extra degrees beyond maxAngle used inside the physics substep solver.")]
    [Range(0f, 70f)] public float collisionAngleAllowance = 55f;
    [Tooltip("After a swept hit, recover the particle onto the authored front/back surface of the leg.")]
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
        public Vector3[] animatedLocalPos;
        public Quaternion[] animatedLocalRot;
        public bool hasAnimatedPose;
        public Vector3 tipLocal;
        public Vector3[] rest;
        public Vector3[] pos;
        public Vector3[] prev;
        public Vector3[] preStepPos;
        public Vector3[] renderPos;
        public float[,] sphereSlack;
        public float[,] capsuleSlack;
        public int sideSign; // +1 front, -1 back, 0 = no side constraint
        public bool isBelt;
        public bool skipBodyColliders;
        public bool ready;
        public bool activeThisFrame;
        public Vector3 lastRoot;
    }

    readonly List<Chain> chains = new List<Chain>();
    readonly List<NeighborLink> neighborLinks = new List<NeighborLink>();
    Animator cachedAnimator;
    Transform pelvisBone;
    Vector3 pelvisLocalForward = Vector3.forward;
    bool pelvisCalibrated;
    Vector3[] previousCapsuleA;
    Vector3[] previousCapsuleB;
    bool[] previousCapsuleValid;
    bool warnedNoCapsules;

    void Awake()
    {
        cachedAnimator = GetComponentInChildren<Animator>();
        if (cachedAnimator != null && cachedAnimator.updateMode == AnimatorUpdateMode.Fixed)
        {
            Debug.LogWarning("[GreekSecondaryMotion] Animated-pose preservation expects Animator Update Mode = Normal. Animate Physics may evaluate before this component's Update().", this);
        }
        Build();
    }

    void OnEnable()
    {
        if (Application.isPlaying)
        {
            ResetSimulation();
        }
    }

    [ContextMenu("Rebuild Chains")]
    public void Build()
    {
        chains.Clear();
        neighborLinks.Clear();
        if (cachedAnimator == null) cachedAnimator = GetComponentInChildren<Animator>();
        CalibratePelvisForward();

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

                AddChain(child, selectedProfile, isFront ? 1 : (isBack ? -1 : 0), false, isBelt);
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
                AddChain(bone, breast, 0, true, false);
            else
                Debug.LogWarning("[GreekSecondaryMotion] Breast bone '" + boneName + "' was not found.", this);
        }

        BuildNeighborLinks();
        ResetSimulation();
        Debug.Log("[GreekSecondaryMotion] Built " + chains.Count + " chains and " + neighborLinks.Count + " neighbor cloth links.", this);
    }

    void BuildNeighborLinks()
    {
        neighborLinks.Clear();
        Vector3 forward = GetCharacterForward();
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        if (right.sqrMagnitude < 1e-6f) right = transform.right;
        right.Normalize();
        Vector3 origin = pelvisBone != null ? pelvisBone.position : transform.position;

        BuildSideNeighborLinks(1, origin, right);
        BuildSideNeighborLinks(-1, origin, right);
    }

    void BuildSideNeighborLinks(int sideSign, Vector3 origin, Vector3 rightAxis)
    {
        var group = new List<Chain>();
        for (int i = 0; i < chains.Count; i++)
        {
            Chain c = chains[i];
            if (c.sideSign == sideSign && !c.skipBodyColliders && c.bones != null && c.bones.Length > 0)
                group.Add(c);
        }

        if (group.Count < 2) return;

        group.Sort((a, b) =>
        {
            float projA = Vector3.Dot(a.bones[0].position - origin, rightAxis);
            float projB = Vector3.Dot(b.bones[0].position - origin, rightAxis);
            return projA.CompareTo(projB);
        });

        for (int i = 0; i < group.Count - 1; i++)
        {
            neighborLinks.Add(new NeighborLink
            {
                a = group[i],
                b = group[i + 1],
                sideSign = sideSign
            });
        }
    }

    void CalibratePelvisForward()
    {
        pelvisBone = null;
        pelvisCalibrated = false;

        if (cachedAnimator != null && cachedAnimator.isHuman)
            pelvisBone = cachedAnimator.GetBoneTransform(HumanBodyBones.Hips);

        if (pelvisBone == null)
        {
            Transform skirtRoot = FindDeep(transform, skirtRootName);
            pelvisBone = skirtRoot != null ? skirtRoot : (cachedAnimator != null ? cachedAnimator.transform : transform);
        }

        Transform reference = cachedAnimator != null ? cachedAnimator.transform : transform;
        Vector3 localFwd = frontDirectionLocal.sqrMagnitude > 0.0001f ? frontDirectionLocal.normalized : Vector3.forward;
        Vector3 worldFwd = reference.TransformDirection(localFwd);
        if (worldFwd.sqrMagnitude < 0.0001f) worldFwd = reference.forward;
        worldFwd.Normalize();

        if (pelvisBone != null)
        {
            Vector3 localInPelvis = pelvisBone.InverseTransformDirection(worldFwd);
            if (localInPelvis.sqrMagnitude > 0.0001f)
            {
                pelvisLocalForward = localInPelvis.normalized;
                pelvisCalibrated = true;
            }
        }
    }

    void AddChain(Transform first, Profile profile, int sideSign, bool singleBone = false, bool isBelt = false)
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
            sideSign = sideSign,
            isBelt = isBelt,
            skipBodyColliders = singleBone
        };

        int count = chain.bones.Length;
        chain.animatedLocalPos = new Vector3[count];
        chain.animatedLocalRot = new Quaternion[count];
        for (int i = 0; i < count; i++)
        {
            chain.animatedLocalPos[i] = chain.bones[i].localPosition;
            chain.animatedLocalRot[i] = chain.bones[i].localRotation;
        }
        chain.hasAnimatedPose = false;

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
        chain.preStepPos = new Vector3[count + 1];
        chain.renderPos = new Vector3[count + 1];
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

        // Tapered leg capsules: upper thigh (0.082 m) to knee (0.066 m).
        AddCapsuleBetweenBones(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, 0.082f, 0.066f, true, 0.00f, 1.00f);
        AddCapsuleBetweenBones(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, 0.082f, 0.066f, true, 0.00f, 1.00f);
        AddCapsuleBetweenBones(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, 0.060f, 0.046f, true, 0.00f, 0.94f);
        AddCapsuleBetweenBones(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, 0.060f, 0.046f, true, 0.00f, 0.94f);
        // Pelvis capsule extends slightly below Hips (startT = -0.08) to protect the lower belly/groin under the belt.
        AddCapsuleBetweenBones(HumanBodyBones.Hips, HumanBodyBones.Spine, 0.084f, 0.080f, false, -0.08f, 0.90f);
        AddCapsuleBetweenBones(HumanBodyBones.Spine, HumanBodyBones.Chest, 0.085f, 0.085f, false, 0.05f, 0.95f);

        int legCount = 0;
        for (int i = 0; i < capsuleColliders.Count; i++)
            if (capsuleColliders[i].isLegCollider) legCount++;

        previousCapsuleA = null;
        previousCapsuleB = null;
        previousCapsuleValid = null;
        ResetSimulation();

        Debug.Log("[GreekSecondaryMotion] Created " + capsuleColliders.Count + " tapered capsule colliders (" + legCount + " leg capsules).", this);
    }

    void AddCapsuleBetweenBones(HumanBodyBones startId, HumanBodyBones endId, float startRadius, float endRadius,
        bool isLeg, float startT = 0.04f, float endT = 0.96f)
    {
        Transform startBone = cachedAnimator.GetBoneTransform(startId);
        Transform endBone = cachedAnimator.GetBoneTransform(endId);
        if (startBone == null || endBone == null) return;

        Vector3 startWorld = Vector3.LerpUnclamped(startBone.position, endBone.position, startT);
        Vector3 endWorld = Vector3.LerpUnclamped(startBone.position, endBone.position, endT);
        capsuleColliders.Add(new CapsuleCollider3
        {
            startBone = startBone,
            endBone = endBone,
            startLocal = startBone.InverseTransformPoint(startWorld),
            endLocal = endBone.InverseTransformPoint(endWorld),
            radius = startRadius,
            endRadius = endRadius,
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

    void Update()
    {
        if (!Application.isPlaying) return;

        for (int chainIndex = 0; chainIndex < chains.Count; chainIndex++)
        {
            Chain chain = chains[chainIndex];
            if (!chain.hasAnimatedPose) continue;

            for (int i = 0; i < chain.bones.Length; i++)
            {
                Transform bone = chain.bones[i];
                if (bone == null) continue;
                bone.localPosition = chain.animatedLocalPos[i];
                bone.localRotation = chain.animatedLocalRot[i];
            }
        }
    }

    void OnDisable()
    {
        if (!Application.isPlaying) return;

        for (int chainIndex = 0; chainIndex < chains.Count; chainIndex++)
        {
            Chain chain = chains[chainIndex];
            if (!chain.hasAnimatedPose) continue;

            for (int i = 0; i < chain.bones.Length; i++)
            {
                Transform bone = chain.bones[i];
                if (bone == null) continue;
                bone.localPosition = chain.animatedLocalPos[i];
                bone.localRotation = chain.animatedLocalRot[i];
            }
        }
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

        // Phase 1: Physics simulation and profile.weight blending for all chains.
        for (int i = 0; i < chains.Count; i++)
            SimulateChainPhysics(chains[i], dt, spherePositions, sphereRadii, capsuleFrames, forward);

        // Phase 2: Joint hard collision + horizontal bridge collision + neighbor cohesion.
        if (renderCollisionPasses > 0)
            ResolveAllRenderCollisions(spherePositions, sphereRadii, capsuleFrames, forward);

        // Phase 3: Write final root offset (if any) and rotations back to bones.
        for (int i = 0; i < chains.Count; i++)
            ApplyChainTransforms(chains[i]);
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
            float rStart = capsule.radius * capsuleScale;
            float rEnd = (capsule.endRadius > 0.0001f ? capsule.endRadius : capsule.radius) * capsuleScale;

            frames[i] = new CapsuleFrame
            {
                a = a,
                b = b,
                previousA = hasPrevious ? previousCapsuleA[i] : a,
                previousB = hasPrevious ? previousCapsuleB[i] : b,
                startRadius = rStart,
                endRadius = rEnd,
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
        if (usePelvisReferenceForFrontBack && pelvisCalibrated && pelvisBone != null)
        {
            Vector3 pelvisWorld = pelvisBone.TransformDirection(pelvisLocalForward);
            if (pelvisWorld.sqrMagnitude > 0.0001f)
                return pelvisWorld.normalized;
        }

        Transform reference = cachedAnimator != null ? cachedAnimator.transform : transform;
        Vector3 local = frontDirectionLocal;
        if (local.sqrMagnitude < 0.0001f) local = Vector3.forward;
        Vector3 world = reference.TransformDirection(local.normalized);
        if (world.sqrMagnitude < 0.0001f) world = reference.forward;
        return world.normalized;
    }

    bool CanChainCollideWithCapsule(Chain chain, CapsuleFrame capsule)
    {
        if (!capsule.valid) return false;
        if (capsule.isLeg) return true;
        return chain.isBelt || collideSkirtWithBodyCapsules;
    }

    bool CanPushRoot(Chain chain)
    {
        if (!allowRootPushOut || maxRootPushOut <= 0.0001f || chain.sideSign == 0 || chain.skipBodyColliders)
            return false;
        if (chain.sideSign < 0 && !allowBackRootPushOut)
            return false;
        return true;
    }

    void ApplyRootPush(Chain chain, Vector3 rawPush, Vector3 sideDir)
    {
        float outward = Vector3.Dot(rawPush, sideDir);
        if (outward <= 1e-6f) return;

        Vector3 currentOffset = chain.renderPos[0] - chain.rest[0];
        float currentOutward = Vector3.Dot(currentOffset, sideDir);
        float targetOutward = Mathf.Clamp(currentOutward + outward, 0f, maxRootPushOut);
        float applied = targetOutward - currentOutward;
        if (applied <= 1e-6f) return;

        Vector3 delta = sideDir * applied;
        int boneCount = chain.bones.Length;
        for (int j = 0; j <= boneCount; j++)
            chain.renderPos[j] += delta;
    }

    static float GetCapsuleRadiusAt(CapsuleFrame capsule, float u)
    {
        return Mathf.Lerp(capsule.startRadius, capsule.endRadius, Mathf.Clamp01(u));
    }

    void SimulateChainPhysics(Chain chain, float dt, Vector3[] spherePositions, float[] sphereRadii,
        CapsuleFrame[] capsulesNow, Vector3 characterForward)
    {
        chain.activeThisFrame = false;
        int boneCount = chain.bones.Length;
        Profile profile = chain.profile;
        if (boneCount == 0 || profile == null) return;

        for (int i = 0; i < boneCount; i++)
        {
            Transform bone = chain.bones[i];
            chain.animatedLocalPos[i] = bone.localPosition;
            chain.animatedLocalRot[i] = bone.localRotation;
            chain.rest[i] = bone.position;
        }
        chain.rest[boneCount] = chain.bones[boneCount - 1].TransformPoint(chain.tipLocal);
        chain.hasAnimatedPose = true;

        float profileWeight = Mathf.Clamp01(profile.weight);
        if (profileWeight <= 0.0001f)
        {
            chain.lastRoot = chain.rest[0];
            chain.ready = false;
            return;
        }

        Vector3 rootMove = chain.rest[0] - chain.lastRoot;
        bool firstFrame = !chain.ready;
        bool teleported = chain.ready && rootMove.sqrMagnitude > teleportDistance * teleportDistance;
        if (firstFrame || teleported)
        {
            for (int i = 0; i <= boneCount; i++)
            {
                chain.pos[i] = chain.rest[i];
                chain.prev[i] = chain.rest[i];
                chain.preStepPos[i] = chain.rest[i];
                chain.renderPos[i] = chain.rest[i];
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
            InitializeCapsuleSlack(chain, profile, capsulesNow);

        chain.ready = true;
        chain.activeThisFrame = true;

        float inertiaEffective = Mathf.Clamp01(profile.inertia * profile.amount);
        float maxAngle = Mathf.Clamp(profile.maxAngle * Mathf.Max(profile.amount, 0.05f), 1f, 120f);
        float collisionMaxAngle = Mathf.Clamp(maxAngle + collisionAngleAllowance, maxAngle, 115f);

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
        int finalPasses = Mathf.Clamp(finalCollisionPasses, 0, 6);

        for (int step = 0; step < steps; step++)
        {
            float t0 = step / (float)steps;
            float t1 = (step + 1) / (float)steps;

            chain.pos[0] = chain.rest[0];
            chain.prev[0] = chain.rest[0];
            chain.preStepPos[0] = chain.rest[0];

            for (int i = 1; i <= boneCount; i++)
            {
                Vector3 oldPosition = chain.pos[i];
                chain.preStepPos[i] = oldPosition;

                Vector3 velocity = (chain.pos[i] - chain.prev[i]) / h;
                Vector3 acceleration = omegaSquared * (chain.rest[i] - chain.pos[i]) - damping * velocity + gravity;
                velocity += acceleration * h;
                chain.prev[i] = oldPosition;
                chain.pos[i] = oldPosition + velocity * h;
            }

            if (!chain.skipBodyColliders && useSweptCapsuleCollision)
            {
                for (int i = 1; i <= boneCount; i++)
                    CollideSweptCapsuleParticle(chain, i, profile, capsulesNow, t0, t1, characterForward);
            }

            for (int pass = 0; pass < passes; pass++)
            {
                for (int i = 1; i <= boneCount; i++)
                    ConstrainLengthAndAngle(chain, i, maxAngle);

                if (chain.skipBodyColliders) continue;

                for (int i = 1; i <= boneCount; i++)
                {
                    ApplyWrongSideLegRecovery(chain.pos, i, chain, profile, capsulesNow, t1, characterForward, sideConstraintStrength, true);
                    CollideSphereParticle(chain, i, profile, spherePositions, sphereRadii);
                    CollideCapsuleParticle(chain, i, profile, capsulesNow, t1, characterForward);
                    CollideSphereSegment(chain, i, profile, spherePositions, sphereRadii);
                    CollideCapsuleSegment(chain, i, profile, capsulesNow, t1, characterForward);
                }
            }

            if (!chain.skipBodyColliders)
            {
                for (int finalPass = 0; finalPass < finalPasses; finalPass++)
                {
                    for (int i = 1; i <= boneCount; i++)
                    {
                        ApplyWrongSideLegRecovery(chain.pos, i, chain, profile, capsulesNow, t1, characterForward, 1f, true);
                        CollideSphereParticle(chain, i, profile, spherePositions, sphereRadii);
                        CollideCapsuleParticle(chain, i, profile, capsulesNow, t1, characterForward);
                        CollideSphereSegment(chain, i, profile, spherePositions, sphereRadii);
                        CollideCapsuleSegment(chain, i, profile, capsulesNow, t1, characterForward);
                    }

                    for (int i = 1; i <= boneCount; i++)
                        ConstrainLengthAndAngle(chain, i, collisionMaxAngle);
                }
            }
        }

        // Blend free secondary motion by profile.weight; hard leg/pelvis/bridge collisions run in Phase 2 at 100%.
        chain.renderPos[0] = chain.rest[0];
        for (int i = 1; i <= boneCount; i++)
            chain.renderPos[i] = Vector3.Lerp(chain.rest[i], chain.pos[i], profileWeight);

        for (int i = 1; i <= boneCount; i++)
        {
            float boneLen = (chain.rest[i] - chain.rest[i - 1]).magnitude;
            if (boneLen > 1e-6f)
                EnforceRenderLength(chain.renderPos, chain.rest, i, boneLen);
        }
    }

    void ResolveAllRenderCollisions(Vector3[] spherePositions, float[] sphereRadii,
        CapsuleFrame[] capsulesNow, Vector3 characterForward)
    {
        int passes = Mathf.Clamp(renderCollisionPasses, 1, 8);

        for (int pass = 0; pass < passes; pass++)
        {
            for (int c = 0; c < chains.Count; c++)
            {
                Chain chain = chains[c];
                if (!chain.activeThisFrame || chain.skipBodyColliders) continue;
                ResolveChainRenderCollisionPass(chain, spherePositions, sphereRadii, capsulesNow, characterForward);
            }

            if (useHorizontalBridgeCollision && neighborLinks.Count > 0)
            {
                ResolveHorizontalBridgeCollisions(capsulesNow, characterForward);
            }

            if (useNeighborCohesion && neighborLinks.Count > 0)
            {
                ApplyNeighborCohesion(characterForward);
            }
        }

        // Final quick projection so cohesion never pulls any bone or horizontal bridge back inside a capsule.
        for (int c = 0; c < chains.Count; c++)
        {
            Chain chain = chains[c];
            if (!chain.activeThisFrame || chain.skipBodyColliders) continue;
            ResolveChainRenderCollisionPass(chain, spherePositions, sphereRadii, capsulesNow, characterForward);
        }
        if (useHorizontalBridgeCollision && neighborLinks.Count > 0)
        {
            ResolveHorizontalBridgeCollisions(capsulesNow, characterForward);
        }
    }

    void ResolveChainRenderCollisionPass(Chain chain, Vector3[] spherePositions, float[] sphereRadii,
        CapsuleFrame[] capsulesNow, Vector3 characterForward)
    {
        int boneCount = chain.bones.Length;
        Profile profile = chain.profile;
        Vector3[] rp = chain.renderPos;
        bool canPushRoot = CanPushRoot(chain);
        Vector3 sideDir = chain.sideSign != 0 ? characterForward * chain.sideSign : Vector3.zero;

        // 1. Optional bounded outward push on the root bone (_1) for upper pelvis / belt clearance.
        if (canPushRoot)
        {
            for (int c = 0; c < capsulesNow.Length; c++)
            {
                CapsuleFrame capsule = capsulesNow[c];
                if (!CanChainCollideWithCapsule(chain, capsule)) continue;

                Vector3 closest = ClosestPointOnSegment(rp[0], capsule.a, capsule.b, out float u);
                float radius = GetCapsuleRadiusAt(capsule, u) + profile.colliderRadius - chain.capsuleSlack[0, c];
                if (radius <= 0f) continue;

                Vector3 delta = rp[0] - closest;
                if (delta.sqrMagnitude < radius * radius)
                {
                    Vector3 target = ComputeSafeCapsuleTarget(rp[0], closest, radius,
                        chain.sideSign, characterForward, chain.rest[0]);
                    ApplyRootPush(chain, target - rp[0], sideDir);
                }
            }
        }

        // 2. Vertical particles & segments along this chain.
        for (int i = 1; i <= boneCount; i++)
        {
            float boneLen = (chain.rest[i] - chain.rest[i - 1]).magnitude;
            if (boneLen < 1e-6f) continue;

            Vector3 before = rp[i];
            EnforceRenderLength(rp, chain.rest, i, boneLen);

            for (int sub = 0; sub < 2; sub++)
            {
                ApplyWrongSideLegRecovery(rp, i, chain, profile, capsulesNow, 1f, characterForward, 1f, false);

                if (useLegacySphereColliders)
                {
                    for (int c = 0; c < spherePositions.Length; c++)
                    {
                        if (sphereRadii[c] < 0f) continue;
                        float radius = sphereRadii[c] + profile.colliderRadius - chain.sphereSlack[i, c];
                        if (radius > 0f)
                        {
                            Vector3 delta = rp[i] - spherePositions[c];
                            if (delta.sqrMagnitude < radius * radius)
                            {
                                if (delta.sqrMagnitude < radius * radius * 0.36f)
                                {
                                    Vector3 restDir = chain.rest[i] - spherePositions[c];
                                    if (restDir.sqrMagnitude > 1e-8f) delta = restDir;
                                    else if (chain.sideSign != 0) delta = characterForward * chain.sideSign;
                                }
                                if (delta.sqrMagnitude < 1e-8f) delta = Vector3.up;
                                rp[i] = spherePositions[c] + delta.normalized * radius;
                            }
                        }

                        Vector3 ab = rp[i] - rp[i - 1];
                        float denom = Vector3.Dot(ab, ab);
                        float s = denom > 1e-10f ? Mathf.Clamp01(Vector3.Dot(spherePositions[c] - rp[i - 1], ab) / denom) : 0.5f;
                        float slack = Mathf.Lerp(chain.sphereSlack[i - 1, c], chain.sphereSlack[i, c], s);
                        float segRadius = sphereRadii[c] + profile.colliderRadius - slack;
                        if (segRadius > 0f)
                        {
                            Vector3 closest = rp[i - 1] + ab * s;
                            Vector3 delta = closest - spherePositions[c];
                            float dist = delta.magnitude;
                            if (dist < segRadius)
                            {
                                if (dist < segRadius * 0.6f)
                                    delta = Vector3.Lerp(chain.rest[i - 1], chain.rest[i], s) - spherePositions[c];
                                if (delta.sqrMagnitude < 1e-8f) delta = Vector3.up;
                                MoveRenderSegment(rp, i, delta.normalized * (segRadius - dist), s);
                            }
                        }
                    }
                }

                for (int c = 0; c < capsulesNow.Length; c++)
                {
                    CapsuleFrame capsule = capsulesNow[c];
                    if (!CanChainCollideWithCapsule(chain, capsule)) continue;

                    Vector3 a = capsule.a;
                    Vector3 b = capsule.b;

                    // Particle vs Capsule
                    Vector3 closest = ClosestPointOnSegment(rp[i], a, b, out float u);
                    float radius = GetCapsuleRadiusAt(capsule, u) + profile.colliderRadius - chain.capsuleSlack[i, c];
                    if (radius > 0f)
                    {
                        Vector3 delta = rp[i] - closest;
                        if (delta.sqrMagnitude < radius * radius)
                        {
                            rp[i] = ComputeSafeCapsuleTarget(rp[i], closest, radius,
                                chain.sideSign, characterForward, chain.rest[i]);
                        }
                    }

                    // Segment vs Capsule
                    ClosestPointsOnSegments(rp[i - 1], rp[i], a, b,
                        out Vector3 clothPoint, out Vector3 axisPoint, out float s, out float uSeg);

                    float segSlack = Mathf.Lerp(chain.capsuleSlack[i - 1, c], chain.capsuleSlack[i, c], s);
                    float segRadius = GetCapsuleRadiusAt(capsule, uSeg) + profile.colliderRadius - segSlack;
                    if (segRadius > 0f)
                    {
                        Vector3 delta = clothPoint - axisPoint;
                        if (delta.sqrMagnitude < segRadius * segRadius)
                        {
                            Vector3 restMid = Vector3.Lerp(chain.rest[i - 1], chain.rest[i], s);
                            Vector3 targetPoint = ComputeSafeCapsuleTarget(clothPoint, axisPoint, segRadius,
                                chain.sideSign, characterForward, restMid);
                            Vector3 push = targetPoint - clothPoint;

                            if (i == 1 && canPushRoot && s < 0.65f)
                            {
                                float w0 = 1f - s;
                                float w1 = s;
                                float denom = w0 * w0 + w1 * w1;
                                ApplyRootPush(chain, push * (w0 / denom), sideDir);
                                rp[1] += push * (w1 / denom);
                            }
                            else if (!(i == 1 && s < 0.02f))
                            {
                                MoveRenderSegment(rp, i, push, s);
                            }

                            if (i > 1)
                            {
                                float parentLen = (chain.rest[i - 1] - chain.rest[i - 2]).magnitude;
                                if (parentLen > 1e-6f)
                                    EnforceRenderLength(rp, chain.rest, i - 1, parentLen);
                            }
                        }
                    }
                }

                EnforceRenderLength(rp, chain.rest, i, boneLen);
            }

            Vector3 netShift = rp[i] - before;
            if (netShift.sqrMagnitude > 1e-10f)
            {
                for (int j = i + 1; j <= boneCount; j++)
                    rp[j] += netShift;
            }
        }
    }

    // Collides horizontal cloth spans connecting adjacent front/back skirt chains against capsules,
    // preventing curved thigh/pelvis surfaces from poking through the gap between discrete bone chains.
    void ResolveHorizontalBridgeCollisions(CapsuleFrame[] capsulesNow, Vector3 characterForward)
    {
        for (int l = 0; l < neighborLinks.Count; l++)
        {
            NeighborLink link = neighborLinks[l];
            Chain a = link.a;
            Chain b = link.b;
            if (!a.activeThisFrame || !b.activeThisFrame || a.skipBodyColliders || b.skipBodyColliders)
                continue;

            int maxLevel = Mathf.Min(a.bones.Length, b.bones.Length);
            bool bothPushRoot = CanPushRoot(a) && CanPushRoot(b);
            int startLevel = bothPushRoot ? 0 : 1;
            Vector3 sideDir = characterForward * link.sideSign;
            float avgColliderRadius = 0.5f * (a.profile.colliderRadius + b.profile.colliderRadius) + bridgeThicknessBonus;

            for (int i = startLevel; i <= maxLevel; i++)
            {
                for (int c = 0; c < capsulesNow.Length; c++)
                {
                    CapsuleFrame capsule = capsulesNow[c];
                    if (!CanChainCollideWithCapsule(a, capsule) || !CanChainCollideWithCapsule(b, capsule))
                        continue;

                    Vector3 pA = a.renderPos[i];
                    Vector3 pB = b.renderPos[i];
                    ClosestPointsOnSegments(pA, pB, capsule.a, capsule.b,
                        out Vector3 clothPoint, out Vector3 axisPoint, out float s, out float u);

                    float slack = Mathf.Lerp(a.capsuleSlack[i, c], b.capsuleSlack[i, c], s);
                    float radius = GetCapsuleRadiusAt(capsule, u) + avgColliderRadius - slack;
                    if (radius <= 0f) continue;

                    Vector3 delta = clothPoint - axisPoint;
                    bool penetrates = delta.sqrMagnitude < radius * radius;
                    bool wrongSide = false;

                    if (!penetrates && capsule.isLeg && useFrontBackSideConstraint)
                    {
                        float signedDist = Vector3.Dot(delta, sideDir);
                        if (signedDist < 0f && signedDist > -sideConstraintRange)
                        {
                            Vector3 lateral = delta - sideDir * signedDist;
                            float maxLat = radius + sideLateralMargin;
                            if (lateral.sqrMagnitude < maxLat * maxLat)
                                wrongSide = true;
                        }
                    }

                    if (!penetrates && !wrongSide) continue;

                    Vector3 restMid = Vector3.Lerp(a.rest[i], b.rest[i], s);
                    float targetRadius = wrongSide ? (radius + sideClearance) : radius;
                    Vector3 targetPoint = ComputeSafeCapsuleTarget(clothPoint, axisPoint, targetRadius,
                        link.sideSign, characterForward, restMid);
                    Vector3 push = targetPoint - clothPoint;

                    float wA = 1f - s;
                    float wB = s;
                    float denom = wA * wA + wB * wB;

                    if (i == 0)
                    {
                        ApplyRootPush(a, push * (wA / denom), sideDir);
                        ApplyRootPush(b, push * (wB / denom), sideDir);
                    }
                    else
                    {
                        Vector3 beforeA = a.renderPos[i];
                        Vector3 beforeB = b.renderPos[i];

                        a.renderPos[i] += push * (wA / denom);
                        b.renderPos[i] += push * (wB / denom);

                        float lenA = (a.rest[i] - a.rest[i - 1]).magnitude;
                        float lenB = (b.rest[i] - b.rest[i - 1]).magnitude;
                        if (lenA > 1e-6f) EnforceRenderLength(a.renderPos, a.rest, i, lenA);
                        if (lenB > 1e-6f) EnforceRenderLength(b.renderPos, b.rest, i, lenB);

                        Vector3 shiftA = a.renderPos[i] - beforeA;
                        if (shiftA.sqrMagnitude > 1e-10f)
                        {
                            for (int j = i + 1; j <= a.bones.Length; j++)
                                a.renderPos[j] += shiftA;
                        }

                        Vector3 shiftB = b.renderPos[i] - beforeB;
                        if (shiftB.sqrMagnitude > 1e-10f)
                        {
                            for (int j = i + 1; j <= b.bones.Length; j++)
                                b.renderPos[j] += shiftB;
                        }
                    }
                }
            }
        }
    }

    // Prevents adjacent front/back skirt chains from splitting apart over a stepping thigh.
    // Biases the correction so the trailing chain lifts forward to match the chain pushed by the leg.
    void ApplyNeighborCohesion(Vector3 characterForward)
    {
        float stretchAllowance = 1f + Mathf.Max(0f, maxHorizontalStretch);
        float strength = Mathf.Clamp01(neighborCohesionStrength);
        if (strength <= 0.001f) return;

        for (int l = 0; l < neighborLinks.Count; l++)
        {
            NeighborLink link = neighborLinks[l];
            Chain a = link.a;
            Chain b = link.b;
            if (!a.activeThisFrame || !b.activeThisFrame) continue;

            int maxLevel = Mathf.Min(a.bones.Length, b.bones.Length);
            Vector3 sideDir = characterForward * link.sideSign;

            for (int i = 1; i <= maxLevel; i++)
            {
                float restDist = Vector3.Distance(a.rest[i], b.rest[i]);
                if (restDist < 1e-5f) continue;

                Vector3 delta = b.renderPos[i] - a.renderPos[i];
                float dist = delta.magnitude;
                float maxAllowed = restDist * stretchAllowance;
                if (dist <= maxAllowed || dist < 1e-6f) continue;

                float excess = (dist - maxAllowed) * strength;
                Vector3 dir = delta / dist;

                float leadA = Vector3.Dot(a.renderPos[i] - a.rest[i], sideDir);
                float leadB = Vector3.Dot(b.renderPos[i] - b.rest[i], sideDir);

                float weightA;
                float weightB;
                if (leadA > leadB + 0.002f)
                {
                    weightA = 0.1f;
                    weightB = 0.9f;
                }
                else if (leadB > leadA + 0.002f)
                {
                    weightA = 0.9f;
                    weightB = 0.1f;
                }
                else
                {
                    weightA = 0.5f;
                    weightB = 0.5f;
                }

                a.renderPos[i] += dir * (excess * weightA);
                b.renderPos[i] -= dir * (excess * weightB);

                float lenA = (a.rest[i] - a.rest[i - 1]).magnitude;
                float lenB = (b.rest[i] - b.rest[i - 1]).magnitude;
                if (lenA > 1e-6f) EnforceRenderLength(a.renderPos, a.rest, i, lenA);
                if (lenB > 1e-6f) EnforceRenderLength(b.renderPos, b.rest, i, lenB);
            }
        }
    }

    void ApplyChainTransforms(Chain chain)
    {
        if (!chain.activeThisFrame) return;

        int boneCount = chain.bones.Length;
        float finalWeight = Mathf.Clamp01(globalWeight);

        if (CanPushRoot(chain))
        {
            Vector3 rootOffset = chain.renderPos[0] - chain.rest[0];
            if (rootOffset.sqrMagnitude > 1e-10f)
                chain.bones[0].position = chain.rest[0] + rootOffset * finalWeight;
        }

        for (int i = 0; i < boneCount; i++)
        {
            Transform bone = chain.bones[i];
            Vector3 animatedDirection = i < boneCount - 1
                ? bone.TransformVector(chain.bones[i + 1].localPosition)
                : bone.TransformVector(chain.tipLocal);
            Vector3 simulatedDirection = chain.renderPos[i + 1] - chain.renderPos[i];

            if (animatedDirection.sqrMagnitude < 1e-10f || simulatedDirection.sqrMagnitude < 1e-10f)
                continue;

            Quaternion delta = Quaternion.FromToRotation(animatedDirection, simulatedDirection);
            if (finalWeight < 0.999f)
                delta = Quaternion.Slerp(Quaternion.identity, delta, finalWeight);
            bone.rotation = delta * bone.rotation;
        }
    }

    static void EnforceRenderLength(Vector3[] rp, Vector3[] rest, int particle, float boneLen)
    {
        Vector3 restSeg = rest[particle] - rest[particle - 1];
        Vector3 dir = rp[particle] - rp[particle - 1];
        float mag = dir.magnitude;
        if (mag > 1e-6f)
            dir *= boneLen / mag;
        else
            dir = restSeg;

        if (Vector3.Angle(restSeg, dir) > 120f)
        {
            Quaternion fromRest = Quaternion.FromToRotation(restSeg, dir);
            fromRest.ToAngleAxis(out _, out Vector3 axis);
            if (axis.sqrMagnitude > 1e-8f)
                dir = Quaternion.AngleAxis(120f, axis) * restSeg;
            else
                dir = restSeg;
        }

        rp[particle] = rp[particle - 1] + dir;
    }

    static void MoveRenderSegment(Vector3[] arr, int particle, Vector3 push, float segmentT)
    {
        float t = Mathf.Clamp01(segmentT);
        if (particle - 1 == 0)
        {
            float lever = Mathf.Clamp(1f / Mathf.Max(t, 0.35f), 1f, 2.5f);
            arr[particle] += push * lever;
        }
        else
        {
            float w0 = 1f - t;
            float w1 = t;
            float denom = w0 * w0 + w1 * w1;
            arr[particle - 1] += push * (w0 / denom);
            arr[particle] += push * (w1 / denom);
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

    void InitializeCapsuleSlack(Chain chain, Profile profile, CapsuleFrame[] capsules)
    {
        int particleCount = chain.bones.Length + 1;
        chain.capsuleSlack = new float[particleCount, capsules.Length];
        float allowance = Mathf.Clamp01(profile.initialPenetrationAllowance);

        for (int particle = 0; particle < particleCount; particle++)
        {
            for (int c = 0; c < capsules.Length; c++)
            {
                if (!capsules[c].valid) continue;
                Vector3 closest = ClosestPointOnSegment(chain.rest[particle], capsules[c].a, capsules[c].b, out float u);
                float radius = GetCapsuleRadiusAt(capsules[c], u) + profile.colliderRadius;
                float distance = Vector3.Distance(chain.rest[particle], closest);
                float rawOverlap = Mathf.Max(0f, radius - distance);
                float slack = rawOverlap * allowance;

                if (capsules[c].isLeg)
                    slack = Mathf.Min(slack, profile.colliderRadius);

                chain.capsuleSlack[particle, c] = slack;
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
            fromRest.ToAngleAxis(out _, out Vector3 axis);
            if (axis.sqrMagnitude > 1e-8f)
                direction = Quaternion.AngleAxis(maxAngle, axis) * restSegment;
            else
                direction = restSegment;
        }

        SetParticlePosition(chain, particle, chain.pos[particle - 1] + direction);
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
            SetParticlePosition(chain, particle, spherePositions[c] + delta.normalized * radius);
        }
    }

    void CollideCapsuleParticle(Chain chain, int particle, Profile profile, CapsuleFrame[] capsules, float time01, Vector3 characterForward)
    {
        for (int c = 0; c < capsules.Length; c++)
        {
            CapsuleFrame capsule = capsules[c];
            if (!CanChainCollideWithCapsule(chain, capsule)) continue;

            GetCapsuleAt(capsule, time01, out Vector3 a, out Vector3 b);
            Vector3 closest = ClosestPointOnSegment(chain.pos[particle], a, b, out float u);
            float radius = GetCapsuleRadiusAt(capsule, u) + profile.colliderRadius - chain.capsuleSlack[particle, c];
            if (radius <= 0f) continue;

            Vector3 delta = chain.pos[particle] - closest;
            if (delta.sqrMagnitude >= radius * radius) continue;

            Vector3 target = ComputeSafeCapsuleTarget(chain.pos[particle], closest, radius,
                chain.sideSign, characterForward, chain.rest[particle]);
            SetParticlePosition(chain, particle, target);
        }
    }

    void CollideSweptCapsuleParticle(Chain chain, int particle, Profile profile, CapsuleFrame[] capsules,
        float timeStart, float timeEnd, Vector3 characterForward)
    {
        Vector3 pathStart = chain.preStepPos[particle];
        Vector3 pathEnd = chain.pos[particle];
        if ((pathEnd - pathStart).sqrMagnitude < 1e-8f) return;

        for (int c = 0; c < capsules.Length; c++)
        {
            CapsuleFrame capsule = capsules[c];
            if (!CanChainCollideWithCapsule(chain, capsule)) continue;

            bool sweptHit = false;
            int samples = Mathf.Clamp(sweptSamples, 1, 4);
            for (int sample = 0; sample <= samples; sample++)
            {
                float sampleT = Mathf.Lerp(timeStart, timeEnd, sample / (float)samples);
                GetCapsuleAt(capsule, sampleT, out Vector3 a, out Vector3 b);
                ClosestPointsOnSegments(pathStart, pathEnd, a, b, out Vector3 pathPoint, out Vector3 axisPoint, out _, out float uSample);
                float sampleRadius = GetCapsuleRadiusAt(capsule, uSample) + profile.colliderRadius - chain.capsuleSlack[particle, c];
                if (sampleRadius > 0f && (pathPoint - axisPoint).sqrMagnitude < sampleRadius * sampleRadius)
                {
                    sweptHit = true;
                    break;
                }
            }

            if (!sweptHit) continue;

            GetCapsuleAt(capsule, timeEnd, out Vector3 endA, out Vector3 endB);
            Vector3 currentClosest = ClosestPointOnSegment(chain.pos[particle], endA, endB, out float uEnd);
            float endRadius = GetCapsuleRadiusAt(capsule, uEnd) + profile.colliderRadius - chain.capsuleSlack[particle, c];
            if (endRadius <= 0f) continue;

            Vector3 currentDelta = chain.pos[particle] - currentClosest;
            if (currentDelta.sqrMagnitude < endRadius * endRadius)
            {
                Vector3 target = ComputeSafeCapsuleTarget(chain.pos[particle], currentClosest, endRadius,
                    chain.sideSign, characterForward, chain.rest[particle]);
                SetParticlePosition(chain, particle, target);
            }
            else if (recoverSideAfterSweptHit && capsule.isLeg && useFrontBackSideConstraint && chain.sideSign != 0)
            {
                Vector3 sideDir = characterForward * chain.sideSign;
                float signedDistance = Vector3.Dot(currentDelta, sideDir);
                if (signedDistance < 0f)
                {
                    Vector3 target = ComputeSafeCapsuleTarget(chain.pos[particle], currentClosest, endRadius + sideClearance,
                        chain.sideSign, characterForward, chain.rest[particle]);
                    SetParticlePosition(chain, particle, Vector3.Lerp(chain.pos[particle], target, sideConstraintStrength));
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

            Vector3 p0 = chain.pos[particle - 1];
            Vector3 p1 = chain.pos[particle];
            Vector3 ab = p1 - p0;
            float denom = Vector3.Dot(ab, ab);
            float s = denom > 1e-10f ? Mathf.Clamp01(Vector3.Dot(spherePositions[c] - p0, ab) / denom) : 0.5f;
            float slack = Mathf.Lerp(chain.sphereSlack[particle - 1, c], chain.sphereSlack[particle, c], s);
            float radius = sphereRadii[c] + profile.colliderRadius - slack;
            if (radius <= 0f) continue;

            Vector3 closest = p0 + ab * s;
            Vector3 delta = closest - spherePositions[c];
            float distance = delta.magnitude;
            if (distance >= radius) continue;

            if (distance < radius * 0.6f)
                delta = Vector3.Lerp(chain.rest[particle - 1], chain.rest[particle], s) - spherePositions[c];
            if (delta.sqrMagnitude < 1e-8f) delta = Vector3.up;
            Vector3 push = delta.normalized * (radius - distance);
            MoveSegment(chain, particle, push, s);
        }
    }

    void CollideCapsuleSegment(Chain chain, int particle, Profile profile, CapsuleFrame[] capsules,
        float time01, Vector3 characterForward)
    {
        for (int c = 0; c < capsules.Length; c++)
        {
            CapsuleFrame capsule = capsules[c];
            if (!CanChainCollideWithCapsule(chain, capsule)) continue;

            GetCapsuleAt(capsule, time01, out Vector3 a, out Vector3 b);
            ClosestPointsOnSegments(chain.pos[particle - 1], chain.pos[particle], a, b,
                out Vector3 clothPoint, out Vector3 axisPoint, out float s, out float u);
            if (particle == 1 && s < 0.02f) continue;

            float slack = Mathf.Lerp(chain.capsuleSlack[particle - 1, c], chain.capsuleSlack[particle, c], s);
            float radius = GetCapsuleRadiusAt(capsule, u) + profile.colliderRadius - slack;
            if (radius <= 0f) continue;

            Vector3 delta = clothPoint - axisPoint;
            if (delta.sqrMagnitude >= radius * radius) continue;

            Vector3 restMid = Vector3.Lerp(chain.rest[particle - 1], chain.rest[particle], s);
            Vector3 targetPoint = ComputeSafeCapsuleTarget(clothPoint, axisPoint, radius,
                chain.sideSign, characterForward, restMid);
            MoveSegment(chain, particle, targetPoint - clothPoint, s);
        }
    }

    void ApplyWrongSideLegRecovery(Vector3[] arr, int particle, Chain chain, Profile profile, CapsuleFrame[] capsules,
        float time01, Vector3 characterForward, float strength, bool syncPrev)
    {
        if (!useFrontBackSideConstraint || chain.sideSign == 0)
            return;

        Vector3 sideDir = characterForward * chain.sideSign;

        for (int c = 0; c < capsules.Length; c++)
        {
            CapsuleFrame capsule = capsules[c];
            if (!capsule.valid || !capsule.isLeg) continue;

            GetCapsuleAt(capsule, time01, out Vector3 a, out Vector3 b);
            Vector3 point = arr[particle];
            Vector3 closest = ClosestPointOnSegment(point, a, b, out float u);
            Vector3 delta = point - closest;

            float signedDist = Vector3.Dot(delta, sideDir);
            if (signedDist >= 0f || signedDist < -sideConstraintRange) continue;

            float radius = GetCapsuleRadiusAt(capsule, u) + profile.colliderRadius - chain.capsuleSlack[particle, c];
            if (radius <= 0f) continue;

            Vector3 lateral = delta - sideDir * signedDist;
            float maxLateral = radius + sideLateralMargin;
            if (lateral.sqrMagnitude > maxLateral * maxLateral) continue;

            Vector3 target = ComputeSafeCapsuleTarget(point, closest, radius + sideClearance,
                chain.sideSign, characterForward, chain.rest[particle]);
            Vector3 newPos = strength >= 0.999f ? target : Vector3.Lerp(point, target, strength);
            if (syncPrev)
                SetParticlePosition(chain, particle, newPos);
            else
                arr[particle] = newPos;
        }
    }

    // Projects penetrating front/back skirt points purely along sideDir onto the authored surface,
    // preserving lateral position so hips never puff sideways on leg or pelvis capsules.
    static Vector3 ComputeSafeCapsuleTarget(Vector3 point, Vector3 closest, float radius,
        int sideSign, Vector3 characterForward, Vector3 restPoint)
    {
        Vector3 delta = point - closest;

        if (sideSign != 0)
        {
            Vector3 sideDir = characterForward * sideSign;
            float forwardDot = Vector3.Dot(delta, sideDir);
            Vector3 lateral = delta - sideDir * forwardDot;
            float lateralSqr = lateral.sqrMagnitude;
            float rSqr = radius * radius;

            if (lateralSqr < rSqr)
            {
                float neededForward = Mathf.Sqrt(rSqr - lateralSqr);
                return closest + lateral + sideDir * neededForward;
            }

            float clampedLat = radius * 0.95f;
            Vector3 safeLateral = lateral.normalized * clampedLat;
            float forwardLift = Mathf.Sqrt(Mathf.Max(0f, rSqr - clampedLat * clampedLat));
            return closest + safeLateral + sideDir * forwardLift;
        }

        float distSqr = delta.sqrMagnitude;
        if (distSqr < radius * radius * 0.36f)
        {
            Vector3 restDir = restPoint - closest;
            if (restDir.sqrMagnitude > 1e-8f) delta = restDir;
        }

        if (delta.sqrMagnitude < 1e-8f)
            delta = Vector3.up;

        return closest + delta.normalized * radius;
    }

    static void MoveParticle(Chain chain, int particle, Vector3 delta)
    {
        if (particle <= 0 || particle >= chain.pos.Length) return;
        chain.pos[particle] += delta;
        chain.prev[particle] += delta;
    }

    static void SetParticlePosition(Chain chain, int particle, Vector3 position)
    {
        if (particle <= 0 || particle >= chain.pos.Length) return;
        Vector3 correction = position - chain.pos[particle];
        chain.pos[particle] = position;
        chain.prev[particle] += correction;
    }

    static void MoveSegment(Chain chain, int particle, Vector3 push, float segmentT)
    {
        float t = Mathf.Clamp01(segmentT);
        if (particle - 1 == 0)
        {
            float lever = Mathf.Clamp(1f / Mathf.Max(t, 0.35f), 1f, 2.5f);
            MoveParticle(chain, particle, push * lever);
        }
        else
        {
            float w0 = 1f - t;
            float w1 = t;
            float denom = w0 * w0 + w1 * w1;
            MoveParticle(chain, particle - 1, push * (w0 / denom));
            MoveParticle(chain, particle, push * (w1 / denom));
        }
    }

    static void GetCapsuleAt(CapsuleFrame capsule, float time01, out Vector3 a, out Vector3 b)
    {
        a = Vector3.Lerp(capsule.previousA, capsule.a, time01);
        b = Vector3.Lerp(capsule.previousB, capsule.b, time01);
    }

    static Vector3 ClosestPointOnSegment(Vector3 point, Vector3 a, Vector3 b, out float uOut)
    {
        Vector3 ab = b - a;
        float denominator = Vector3.Dot(ab, ab);
        if (denominator < 1e-10f)
        {
            uOut = 0f;
            return a;
        }
        uOut = Mathf.Clamp01(Vector3.Dot(point - a, ab) / denominator);
        return a + ab * uOut;
    }

    static void ClosestPointsOnSegments(Vector3 p1, Vector3 p2, Vector3 q1, Vector3 q2,
        out Vector3 c1, out Vector3 c2, out float sOut, out float tOut)
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
            sOut = 0f;
            tOut = 0f;
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

        sOut = s;
        tOut = t;
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
                float rA = capsule.radius * capsuleScale;
                float rB = (capsule.endRadius > 0.0001f ? capsule.endRadius : capsule.radius) * capsuleScale;
                if (capsule.isLegCollider)
                    Gizmos.color = new Color(1f, 0.55f, 0.15f, 0.85f);
                else
                    Gizmos.color = collideSkirtWithBodyCapsules
                        ? new Color(0.25f, 0.8f, 1f, 0.65f)
                        : new Color(0.5f, 0.5f, 0.5f, 0.35f);

                Gizmos.DrawLine(a, b);
                Gizmos.DrawWireSphere(a, rA);
                Gizmos.DrawWireSphere(b, rB);

                Vector3 axis = b - a;
                if (axis.sqrMagnitude > 1e-6f)
                {
                    Vector3 up = axis.normalized;
                    Vector3 side1 = Vector3.Cross(up, Mathf.Abs(up.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                    Vector3 side2 = Vector3.Cross(up, side1).normalized;
                    Gizmos.DrawLine(a + side1 * rA, b + side1 * rB);
                    Gizmos.DrawLine(a - side1 * rA, b - side1 * rB);
                    Gizmos.DrawLine(a + side2 * rA, b + side2 * rB);
                    Gizmos.DrawLine(a - side2 * rA, b - side2 * rB);
                }
            }
        }

        if (Application.isPlaying)
        {
            Gizmos.color = Color.green;
            for (int c = 0; c < chains.Count; c++)
            {
                Chain chain = chains[c];
                if (!chain.ready || chain.renderPos == null) continue;
                for (int i = 1; i < chain.renderPos.Length; i++)
                    Gizmos.DrawLine(chain.renderPos[i - 1], chain.renderPos[i]);
            }

            if (useNeighborCohesion || useHorizontalBridgeCollision)
            {
                Gizmos.color = new Color(1f, 0.95f, 0.2f, 0.7f);
                for (int l = 0; l < neighborLinks.Count; l++)
                {
                    NeighborLink link = neighborLinks[l];
                    if (!link.a.ready || !link.b.ready) continue;
                    int maxLevel = Mathf.Min(link.a.bones.Length, link.b.bones.Length);
                    for (int i = 0; i <= maxLevel; i++)
                        Gizmos.DrawLine(link.a.renderPos[i], link.b.renderPos[i]);
                }
            }
        }

        Vector3 gizmoOrigin = (usePelvisReferenceForFrontBack && pelvisCalibrated && pelvisBone != null)
            ? pelvisBone.position
            : (cachedAnimator != null ? cachedAnimator.transform.position : transform.position);
        Vector3 forward = GetCharacterForward();
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(gizmoOrigin, gizmoOrigin + forward * 0.35f);
    }
}