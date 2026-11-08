using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Marks an object as a valid local camera-lock target.
/// Camera selection is intentionally local-only; no gameplay authority is granted here.
/// </summary>
[DisallowMultipleComponent]
public sealed class Targetable : MonoBehaviour
{
    private static readonly HashSet<Targetable> ActiveTargets = new HashSet<Targetable>();

    [Tooltip("Optional point the camera should aim at. Defaults to this object's Transform.")]
    [SerializeField] private Transform aimPoint;

    [Tooltip("Used when Aim Point is empty. The default is approximately chest height for a humanoid player.")]
    [SerializeField] private Vector3 fallbackAimOffset = new Vector3(0f, 1.25f, 0f);

    [Tooltip("Optional renderer used only to show that this target is locked by the local camera.")]
    [SerializeField] private Renderer lockVisual;

    [SerializeField] private Color lockColor = new Color(1f, 0.8f, 0.1f, 1f);

    private MaterialPropertyBlock lockVisualProperties;

    public static IEnumerable<Targetable> All => ActiveTargets;

    public Vector3 AimPosition =>
        aimPoint != null ? aimPoint.position : transform.TransformPoint(fallbackAimOffset);

    private void OnEnable()
    {
        ActiveTargets.Add(this);
    }

    private void OnDisable()
    {
        ActiveTargets.Remove(this);
        SetCameraLocked(false); // <--- จุดที่แก้ไข/เพิ่มเติม
    }

    /// <summary>
    /// Local visual feedback only. It does not change networked gameplay state.
    /// </summary>
    public void SetCameraLocked(bool isLocked) // <--- จุดที่แก้ไข/เพิ่มเติม
    {
        if (lockVisual == null)
        {
            return;
        }

        lockVisualProperties ??= new MaterialPropertyBlock();

        if (isLocked)
        {
            lockVisualProperties.Clear();
            lockVisualProperties.SetColor("_BaseColor", lockColor);
            lockVisualProperties.SetColor("_Color", lockColor);
            lockVisual.SetPropertyBlock(lockVisualProperties);
            return;
        }

        // The test dummy has no other per-renderer overrides, so clearing restores its material colour.
        lockVisual.SetPropertyBlock(null);
    }
}
