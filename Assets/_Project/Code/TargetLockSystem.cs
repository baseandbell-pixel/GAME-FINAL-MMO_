using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Anything the player can lock onto.</summary>
public interface ILockTarget
{
    Transform LockPoint { get; }
    bool IsLockable { get; }
    string LockName { get; }
}

/// <summary>
/// Lock-on for the LOCAL player (Middle Mouse or Q toggles). Picks the best valid enemy inside range and a view cone
/// with a clear line of sight, then:
///   - tells PlayerFollowCamera to enter Target-Lock mode (camera frames player + target),
///   - sets PlayerCapsule.FacingTarget so the character faces the target and WASD becomes strafing
///     (attacks therefore also go toward the target).
/// Releases automatically when the target dies, despawns, is too far away, or the player dies.
/// Input is only read by the peer that controls this player.
/// </summary>
[RequireComponent(typeof(PlayerCombat))]
[RequireComponent(typeof(PlayerCapsule))]
public class TargetLockSystem : MonoBehaviour
{
    [Header("Acquisition")]
    [Min(1f)] [SerializeField] private float lockRange = 20f;
    [Tooltip("Lose the lock beyond this distance (must be larger than lockRange to avoid flicker).")]
    [Min(1f)] [SerializeField] private float breakRange = 28f;
    [Tooltip("Full cone angle in degrees, measured around the camera's forward direction.")]
    [Range(10f, 360f)] [SerializeField] private float coneAngle = 110f;
    [SerializeField] private bool requireLineOfSight = true;
    [SerializeField] private LayerMask lineOfSightMask = ~0;
    [SerializeField] private float eyeHeight = 1.4f;

    [Header("Indicator")]
    [SerializeField] private Color indicatorColor = new Color(1f, 0.85f, 0.1f, 1f);

    public ILockTarget CurrentTarget { get; private set; }
    public bool IsLocked => CurrentTarget != null;

    private PlayerCombat _combat;
    private PlayerCapsule _capsule;
    private PlayerFollowCamera _camera;
    private bool _subscribed;

    private void Awake()
    {
        _combat = GetComponent<PlayerCombat>();
        _capsule = GetComponent<PlayerCapsule>();
    }

    private void OnDestroy()
    {
        if (_combat != null && _subscribed) _combat.DeathStateChanged -= OnDeathStateChanged;
        if (IsLocked) Release("component destroyed");
    }

    private void Update()
    {
        if (_combat == null || !_combat.IsSpawned || !_combat.IsLocallyControlled) return;

        if (!_subscribed)
        {
            _combat.DeathStateChanged += OnDeathStateChanged;
            _subscribed = true;
        }

        if (IsLocked) ValidateTarget();

        if (_combat.IsDead || GameplayInput.IsBlocked) return;

        if (PollTogglePressed())
        {
            if (IsLocked) Release("manual toggle");
            else TryAcquire();
        }
    }

    private static bool PollTogglePressed()
    {
        if (!Application.isFocused) return false;
        var mouse = Mouse.current;
        var kb = Keyboard.current;
        return (mouse != null && mouse.middleButton.wasPressedThisFrame) || (kb != null && kb.qKey.wasPressedThisFrame);
    }

    // ------------------------------------------------------------------ acquire / release

    public bool TryAcquire()
    {
        Vector3 eye = transform.position + Vector3.up * (eyeHeight - 1f);
        Vector3 forward = ReferenceForward();
        float halfCone = coneAngle * 0.5f;

        EnemyNPC best = null;
        float bestScore = float.MaxValue;

        for (int i = 0; i < EnemyNPC.All.Count; i++)
        {
            var npc = EnemyNPC.All[i];
            if (npc == null || !npc.IsLockable) continue;

            Vector3 to = npc.LockPoint.position - eye;
            float dist = to.magnitude;
            if (dist > lockRange || dist < 0.01f) continue;

            Vector3 flat = new Vector3(to.x, 0f, to.z);
            float angle = flat.sqrMagnitude < 0.0001f ? 0f : Vector3.Angle(forward, flat);
            if (angle > halfCone) continue;

            if (requireLineOfSight && !HasLineOfSight(eye, npc)) continue;

            // Prefer targets near the centre of the view, then closer ones.
            float score = dist + angle * 0.15f;
            if (score < bestScore) { bestScore = score; best = npc; }
        }

        if (best == null)
        {
            Debug.LogWarning($"[TargetLock] No valid target (range {lockRange}m, cone {coneAngle} deg, LOS {requireLineOfSight}).");
            return false;
        }

        Lock(best);
        return true;
    }

    private void Lock(ILockTarget target)
    {
        CurrentTarget = target;
        if (_capsule != null) _capsule.FacingTarget = target.LockPoint;

        var cam = GetCamera();
        if (cam != null) cam.SetLockTarget(target.LockPoint);

        float d = Vector3.Distance(transform.position, target.LockPoint.position);
        Debug.Log($"[TargetLock] LOCKED on {target.LockName} at {d:0.0}m.");
    }

    public void Release(string reason)
    {
        if (CurrentTarget == null) return;

        Debug.Log($"[TargetLock] Released {CurrentTarget.LockName} ({reason}).");
        CurrentTarget = null;
        if (_capsule != null) _capsule.FacingTarget = null;

        var cam = GetCamera();
        if (cam != null) cam.SetLockTarget(null);
    }

    private void ValidateTarget()
    {
        // Unity-null check: the target may have been destroyed (despawned) since we locked it.
        var comp = CurrentTarget as Component;
        if (comp == null) { Release("target destroyed"); return; }
        if (!CurrentTarget.IsLockable) { Release("target no longer lockable (dead)"); return; }

        float d = Vector3.Distance(transform.position, CurrentTarget.LockPoint.position);
        if (d > breakRange) Release($"target out of range ({d:0.0}m)");
    }

    private void OnDeathStateChanged(bool dead)
    {
        if (dead) Release("player died");
    }

    // ------------------------------------------------------------------ helpers

    private bool HasLineOfSight(Vector3 eye, EnemyNPC npc)
    {
        if (!Physics.Linecast(eye, npc.LockPoint.position, out RaycastHit hit, lineOfSightMask, QueryTriggerInteraction.Ignore))
            return true;
        // Hitting the target itself (or something of ours) does not block.
        return hit.transform.IsChildOf(npc.transform) || hit.transform.IsChildOf(transform);
    }

    private Vector3 ReferenceForward()
    {
        var cam = Camera.main;
        Vector3 f = cam != null ? cam.transform.forward : transform.forward;
        f.y = 0f;
        return f.sqrMagnitude < 0.0001f ? transform.forward : f.normalized;
    }

    private PlayerFollowCamera GetCamera()
    {
        if (_camera == null) _camera = FindFirstObjectByType<PlayerFollowCamera>();
        return _camera;
    }

    // ------------------------------------------------------------------ indicator

    private void OnGUI()
    {
        if (!IsLocked || Event.current.type != EventType.Repaint) return;
        var cam = Camera.main;
        var comp = CurrentTarget as Component;
        if (cam == null || comp == null) return;

        Vector3 sp = cam.WorldToScreenPoint(CurrentTarget.LockPoint.position + Vector3.up * 0.9f);
        if (sp.z <= 0f) return;

        float x = sp.x, y = Screen.height - sp.y;
        const float s = 16f, t = 3f;
        var prev = GUI.color;
        GUI.color = indicatorColor;
        // downward-pointing chevron made of two bars + a dot
        GUI.DrawTexture(new Rect(x - s, y - s, s * 2f, t), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(x - t * 0.5f, y - s, t, s * 1.4f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(x - 4f, y + s * 0.6f, 8f, 8f), Texture2D.whiteTexture);
        GUI.color = prev;
    }

    private void OnDrawGizmos()
    {
        if (CurrentTarget == null || !(CurrentTarget as Component)) return;
        Gizmos.color = indicatorColor;
        Vector3 p = CurrentTarget.LockPoint.position;
        Gizmos.DrawWireSphere(p, 0.6f);
        Gizmos.DrawLine(transform.position + Vector3.up, p);
    }
}
