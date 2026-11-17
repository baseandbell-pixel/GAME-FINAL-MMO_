using System;
using TMPro;
using UnityEngine;

/// <summary>
/// Dedicated Safe / Comfort Zone (Safe Haven) in Dev_Kulpat.unity.
/// Serves as:
///   1. Reconnect &amp; crash-recovery safe anchor for returning players.
///   2. Non-combat sanctuary: disables player weapon attacks/damage and prevents enemies/bosses
///      from targeting, entering, or damaging players inside.
/// </summary>
[DisallowMultipleComponent]
public class SafeZone : MonoBehaviour
{
    public static SafeZone Instance { get; private set; }

    [SerializeField] private float radius = 7.5f;
    // ระยะชดเชยแนวดิ่งเมื่อวาร์ปผู้เล่นกลับเข้า Safe Zone (ปรับให้พอดีกับ Pivot ที่ปลายเท้าของ GreatSwordGirl)
    [SerializeField] private Vector3 reconnectLocalOffset = new Vector3(0f, 0.05f, 0f);

    public float Radius => radius;
    public Vector3 Center => transform.position;
    public Vector3 ReconnectSpawnPosition => transform.position + reconnectLocalOffset;
    public bool IsLocalPlayerInside { get; private set; }

    public event Action<bool> LocalPlayerSafeZoneChanged;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private TextMeshPro _worldSign;

    public static SafeZone EnsureAt(Vector3 position, float zoneRadius = 7.5f)
    {
        if (Instance != null) return Instance;
        var existing = FindAnyObjectByType<SafeZone>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        var go = new GameObject("ComfortSafeZone");
        go.transform.position = new Vector3(position.x, 0f, position.z);
        var zone = go.AddComponent<SafeZone>();
        zone.radius = zoneRadius;
        zone.BuildVisuals();
        Debug.Log($"[SafeZone] Created Comfort Zone (Safe Haven) at {go.transform.position}, radius={zoneRadius}m.");
        return zone;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        BuildVisuals();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void BuildVisuals()
    {
        if (transform.Find("SanctuaryFloor") != null) return;

        // Non-blocking sphere trigger volume
        var triggerCol = GetComponent<SphereCollider>();
        if (triggerCol == null) triggerCol = gameObject.AddComponent<SphereCollider>();
        triggerCol.isTrigger = true;
        triggerCol.radius = radius;

        Color padColor = new Color(0.08f, 0.34f, 0.28f, 1f);
        Color rimColor = new Color(0.18f, 0.92f, 0.68f, 1f);
        Color pillarColor = new Color(0.16f, 0.24f, 0.28f, 1f);

        // Floor disc
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        floor.name = "SanctuaryFloor";
        var fc = floor.GetComponent<Collider>();
        if (fc != null) Destroy(fc);
        floor.transform.SetParent(transform, false);
        floor.transform.localPosition = new Vector3(0f, 0.02f, 0f);
        floor.transform.localScale = new Vector3(radius * 2f, 0.02f, radius * 2f);
        Tint(floor.GetComponent<Renderer>(), padColor);

        // Inner Reconnect Anchor Seal
        var anchor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        anchor.name = "SanctuaryInnerSeal";
        var ac = anchor.GetComponent<Collider>();
        if (ac != null) Destroy(ac);
        anchor.transform.SetParent(transform, false);
        anchor.transform.localPosition = new Vector3(0f, 0.035f, 0f);
        anchor.transform.localScale = new Vector3(3.2f, 0.02f, 3.2f);
        Tint(anchor.GetComponent<Renderer>(), rimColor);

        // 4 Sanctuary Corner Lantern Pillars (with solid non-blocking perimeter placement)
        float cornerDist = radius * 0.72f;
        for (int i = 0; i < 4; i++)
        {
            float angle = (i * 90f + 45f) * Mathf.Deg2Rad;
            Vector3 pPos = new Vector3(Mathf.Cos(angle) * cornerDist, 0f, Mathf.Sin(angle) * cornerDist);

            var pillar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pillar.name = $"SanctuaryPillar_{i + 1}";
            pillar.transform.SetParent(transform, false);
            pillar.transform.localPosition = pPos + new Vector3(0f, 1.4f, 0f);
            pillar.transform.localScale = new Vector3(0.75f, 2.8f, 0.75f);
            Tint(pillar.GetComponent<Renderer>(), pillarColor);

            var crystal = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crystal.name = "LanternCrystal";
            var cc = crystal.GetComponent<Collider>();
            if (cc != null) Destroy(cc);
            crystal.transform.SetParent(pillar.transform, false);
            crystal.transform.localPosition = new Vector3(0f, 0.62f, 0f);
            crystal.transform.localScale = new Vector3(0.55f, 0.25f, 0.55f);
            crystal.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
            Tint(crystal.GetComponent<Renderer>(), rimColor);
        }

        // Warm Sanctuary Light
        var lightGo = new GameObject("SanctuaryLight");
        lightGo.transform.SetParent(transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 3.5f, 0f);
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(0.35f, 1f, 0.78f);
        l.intensity = 3.0f;
        l.range = radius + 4f;

        // 3D Floating Sign
        var signGo = new GameObject("SanctuarySign");
        signGo.transform.SetParent(transform, false);
        signGo.transform.localPosition = new Vector3(0f, 3.6f, 0f);
        _worldSign = signGo.AddComponent<TextMeshPro>();
        _worldSign.text = "COMFORT ZONE\n<size=65%>Safe Haven • Combat Disabled</size>";
        _worldSign.fontSize = 3.4f;
        _worldSign.alignment = TextAlignmentOptions.Center;
        _worldSign.color = new Color(0.32f, 0.98f, 0.72f, 1f);
        _worldSign.textWrappingMode = TextWrappingModes.NoWrap;
    }

    private static void Tint(Renderer r, Color c)
    {
        if (r == null) return;
        var mpb = new MaterialPropertyBlock();
        r.GetPropertyBlock(mpb);
        mpb.SetColor(BaseColorId, c);
        mpb.SetColor(ColorId, c);
        r.SetPropertyBlock(mpb);
    }

    private void Update()
    {
        var cam = Camera.main;
        if (cam != null && _worldSign != null)
            _worldSign.transform.rotation = cam.transform.rotation;

        var local = PlayerCombat.Local;
        bool inside = IsPlayerInside(local);
        if (inside != IsLocalPlayerInside)
        {
            IsLocalPlayerInside = inside;
            Debug.Log($"[SafeZone] Local player {(inside ? "ENTERED" : "EXITED")} Comfort Zone.");
            LocalPlayerSafeZoneChanged?.Invoke(inside);
            PlayerSaveManager.RequestAutoSave(inside ? "EnteredSafeZone" : "ExitedSafeZone");
        }
    }

    public bool IsPointInside(Vector3 worldPos, float extraMargin = 0f)
    {
        Vector3 a = transform.position;
        Vector3 b = worldPos;
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b) <= (radius + extraMargin);
    }

    public bool IsPlayerInside(PlayerCombat player)
    {
        if (player == null || !player.IsSpawned) return false;
        return IsPointInside(player.transform.position);
    }

    public static bool IsPointInAnySafeZone(Vector3 worldPos, float extraMargin = 0f)
    {
        return Instance != null && Instance.IsPointInside(worldPos, extraMargin);
    }

    public static bool IsPlayerInAnySafeZone(PlayerCombat player)
    {
        return player != null && player.IsSpawned && (player.IsInSafeZone || (Instance != null && Instance.IsPlayerInside(player)));
    }
}
