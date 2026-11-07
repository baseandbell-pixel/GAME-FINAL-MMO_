using UnityEngine;

/// <summary>Simple smoothed third person camera; the target is set once the local capsule spawns.</summary>
public class PlayerFollowCamera : MonoBehaviour
{
    public Transform target;
    public Vector3 offset = new Vector3(0f, 8f, -8f);
    public float smoothing = 8f;

    void LateUpdate()
    {
        if (target == null) return;

        Vector3 wanted = target.position + offset;
        transform.position = Vector3.Lerp(transform.position, wanted, 1f - Mathf.Exp(-smoothing * Time.deltaTime));
        transform.LookAt(target.position + Vector3.up);
    }
}
