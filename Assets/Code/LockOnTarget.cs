using System.Collections.Generic;
using UnityEngine;

public class LockOnTarget : MonoBehaviour
{
    public static readonly List<LockOnTarget> All = new List<LockOnTarget>();

    public Transform lockPoint;        // จุดที่เล็ง (ว่าง = ใช้ตำแหน่ง + heightOffset)
    public float heightOffset = 1.5f;  // ความสูงระดับอก

    public Vector3 Point => lockPoint != null ? lockPoint.position : transform.position + Vector3.up * heightOffset;

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);
}