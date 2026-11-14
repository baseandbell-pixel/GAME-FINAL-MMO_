using UnityEngine;

public class EnemyZone : MonoBehaviour
{
    public float radius = 8f;
    public Color gizmoColor = new Color(1f, 0.3f, 0.2f, 0.9f);

    CombatState player;
    bool inside;

    void Update()
    {
        if (player == null)
        {
            player = FindFirstObjectByType<CombatState>();
            if (player == null) return;
        }

        Vector3 a = transform.position;
        Vector3 b = player.transform.position;
        a.y = 0f; b.y = 0f;

        bool now = (a - b).sqrMagnitude <= radius * radius;
        if (now == inside) return;

        inside = now;
        if (inside) player.EnemyEnter();
        else player.EnemyExit();
    }

    void OnDisable()
    {
        if (inside && player != null) player.EnemyExit();
        inside = false;
    }

    // วงรัศมีสีแดงที่เห็นใน Scene (เปิดดูใน Game ได้โดยกดปุ่ม Gizmos)
    void OnDrawGizmos()
    {
        Gizmos.color = gizmoColor;
        const int seg = 64;
        Vector3 c = transform.position + Vector3.up * 0.05f;
        Vector3 prev = c + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= seg; i++)
        {
            float ang = i * Mathf.PI * 2f / seg;
            Vector3 next = c + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * radius;
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }
}