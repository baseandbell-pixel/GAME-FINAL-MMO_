using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// ระบบหินเทเลพอร์ต (Where Winds Meet–style Teleport Waystone):
///   - วางกระจายไว้ตามจุดสำคัญทั่วแผนที่ FinalTestv1 โดยเริ่มต้นยังไม่ปลดล็อก
///   - ผู้เล่นต้องเดินไปค้นพบแล้วกด [E] เพื่อบันทึก/ปลดล็อกหิน (Attune) -> เซฟลง PlayerSaveManager &amp; Firestore อัตโนมัติ
///   - เมื่อปลดล็อกแล้ว สามารถวาร์ปข้ามจุดได้ 2 ช่องทาง:
///       1. คลิกที่ไอคอนหินบนแผนที่ใหญ่ [M] (World Map)
///       2. กด [E] ที่หินที่ปลดล็อกแล้วเพื่อเปิดหน้าต่าง Fast Travel UI
///   - เมื่อผู้เล่นตาย สามารถใช้หินที่ปลดล็อกแล้วใกล้ที่สุดเป็นจุดเกิดใหม่แทนการเดินไกลจากเมือง
/// </summary>
[DisallowMultipleComponent]
public sealed class TeleportWaystone : MonoBehaviour, IWorldInteractable
{
    public static readonly List<TeleportWaystone> All = new List<TeleportWaystone>();
    private static readonly HashSet<string> s_unlockedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static event Action<TeleportWaystone> WaystoneUnlocked;
    public static event Action WaystoneStateChanged;

    private static float s_lastFastTravelTime = -100f;
    public const float FastTravelCooldownSeconds = 1.5f;

    [Header("Waystone Identity")]
    [Tooltip("รหัสเฉพาะของหินเทเลพอร์ต (ใช้บันทึกในไฟล์เซฟและ Firestore ห้ามซ้ำกัน)")]
    [SerializeField] private string waystoneId = "waystone_city_plaza";
    [Tooltip("ชื่อจุดวาร์ปที่แสดงบนแผนที่และหน้าต่าง Fast Travel")]
    [SerializeField] private string displayName = "City Plaza Waystone";
    [Tooltip("คำอธิบายพื้นที่ใกล้เคียง เช่น Lv.1-3 Slimes หรือ Boss Citadel")]
    [SerializeField] private string regionSubtitle = "Safe Haven";

    [Header("Interaction & Arrival")]
    [Min(1.5f)] [SerializeField] private float interactRadius = 3.8f;
    [Tooltip("ระยะห่างหน้าหินเวลาวาร์ปมาลง เพื่อไม่ให้ติดอยู่ในเสาหิน")]
    [SerializeField] private Vector3 arrivalLocalOffset = new Vector3(0f, 0.1f, 2.6f);

    [Header("Optional Visual References (Auto-Wired in Prefab)")]
    [SerializeField] private Renderer crystalRenderer;
    [SerializeField] private Renderer ringRenderer;
    [SerializeField] private Light waystoneLight;
    [SerializeField] private TextMeshPro worldLabel;
    [SerializeField] private Transform floatingCrystal;

    public string WaystoneId => waystoneId;
    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? waystoneId : displayName;
    public string RegionSubtitle => regionSubtitle ?? string.Empty;
    public bool IsUnlocked => IsWaystoneUnlocked(waystoneId);

    // ---- IWorldInteractable ----
    public InteractionChannel Channel => InteractionChannel.PickupOrChest;
    public int PriorityBias => 2; // ต่ำกว่าของตก (0) และหีบสมบัติ (1) เพื่อไม่ให้หินวาร์ปแย่งปุ่มเก็บของ
    public float InteractionRange => interactRadius;
    public Vector3 InteractionWorldPosition => transform.position;

    public bool CanInteractNow(PlayerCombat player)
    {
        return isActiveAndEnabled && player != null && player.IsSpawned && !player.IsDead;
    }

    public string GetPromptLabel(PlayerCombat player)
    {
        return IsUnlocked
            ? $"Fast Travel  •  {DisplayName}"
            : $"Attune Waystone  •  {DisplayName}";
    }

    public Color GetPromptAccentColor(PlayerCombat player)
    {
        return IsUnlocked
            ? new Color(0.28f, 0.96f, 0.78f, 1f)
            : new Color(1.00f, 0.84f, 0.32f, 1f);
    }

    public void ExecuteInteraction(PlayerCombat player)
    {
        if (player == null || !player.IsSpawned || player.IsDead) return;

        if (!IsUnlocked)
        {
            UnlockWaystone(waystoneId, saveImmediately: true);
            PlayerStatusHud.Instance?.ShowToast(
                $"Waystone Attuned: {DisplayName}! (Fast Travel unlocked via [M] Map or [E])",
                new Color(0.32f, 0.98f, 0.76f),
                3.4f);
            FloatingDamagePopup.SpawnWarning(
                transform.position + Vector3.up * 3.1f,
                "WAYSTONE ATTUNED!",
                new Color(0.30f, 0.98f, 0.78f, 1f));
        }
        else
        {
            // เปิดหน้าต่าง Fast Travel uGUI เพื่อเลือกวาร์ปไปยังหินอื่นที่ปลดล็อกไว้แล้ว
            WaystoneFastTravelUI.OpenFromWaystone(this);
        }
    }

    private Vector3 _crystalBaseLocalPos;
    private bool _capturedCrystalPos;

    private void Awake()
    {
        AutoBindVisualReferences();
    }

    private void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
        InteractionManager.Register(this);
        WaystoneStateChanged += RefreshVisualState;
        AutoBindVisualReferences();
        RefreshVisualState();
    }

    private void OnDisable()
    {
        All.Remove(this);
        InteractionManager.Unregister(this);
        WaystoneStateChanged -= RefreshVisualState;
    }

    private void Update()
    {
        if (floatingCrystal != null)
        {
            if (!_capturedCrystalPos)
            {
                _crystalBaseLocalPos = floatingCrystal.localPosition;
                _capturedCrystalPos = true;
            }
            float bobSpeed = IsUnlocked ? 2.4f : 1.0f;
            float bobAmp = IsUnlocked ? 0.18f : 0.06f;
            float spinSpeed = IsUnlocked ? 75f : 18f;
            floatingCrystal.localPosition = _crystalBaseLocalPos + Vector3.up * (Mathf.Sin(Time.time * bobSpeed + transform.position.x) * bobAmp);
            floatingCrystal.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
        }

        if (worldLabel != null && Camera.main != null)
        {
            worldLabel.transform.rotation = Camera.main.transform.rotation;
        }
    }

    public Vector3 GetSafeArrivalPosition()
    {
        Vector3 worldOffset = transform.TransformDirection(arrivalLocalOffset);
        Vector3 candidate = transform.position + worldOffset;
        float groundY = SandboxWorldSpawner.SampleGroundY(candidate, transform.position.y);
        return new Vector3(candidate.x, groundY + 0.12f, candidate.z);
    }

    public void ConfigureIdentity(string id, string name, string subtitle)
    {
        waystoneId = id;
        displayName = name;
        regionSubtitle = subtitle;
        RefreshVisualState();
    }

    private void AutoBindVisualReferences()
    {
        if (floatingCrystal == null)
            floatingCrystal = transform.Find("Crystal");
        if (crystalRenderer == null && floatingCrystal != null)
            crystalRenderer = floatingCrystal.GetComponent<Renderer>();
        if (ringRenderer == null)
        {
            Transform r = transform.Find("BeaconRing");
            if (r != null) ringRenderer = r.GetComponent<Renderer>();
        }
        if (waystoneLight == null)
            waystoneLight = GetComponentInChildren<Light>();
        if (worldLabel == null)
            worldLabel = GetComponentInChildren<TextMeshPro>();
    }

    public void RefreshVisualState()
    {
        bool unlocked = IsUnlocked;
        Color crystalColor = unlocked
            ? new Color(0.22f, 0.96f, 0.78f, 1f)
            : new Color(0.36f, 0.44f, 0.54f, 1f);
        Color ringColor = unlocked
            ? new Color(0.24f, 0.96f, 0.78f, 0.95f)
            : new Color(0.45f, 0.52f, 0.62f, 0.45f);

        if (crystalRenderer != null)
            UrpMaterialHelper.ApplyUnlitColor(crystalRenderer, crystalColor);
        if (ringRenderer != null)
            UrpMaterialHelper.ApplyUnlitColor(ringRenderer, ringColor);
        if (waystoneLight != null)
        {
            waystoneLight.color = unlocked ? new Color(0.25f, 0.96f, 0.80f) : new Color(0.42f, 0.58f, 0.78f);
            waystoneLight.intensity = unlocked ? 3.8f : 1.1f;
            waystoneLight.range = unlocked ? 11.5f : 5.5f;
        }
        if (worldLabel != null)
        {
            string sub = string.IsNullOrWhiteSpace(regionSubtitle) ? "" : $"\n<size=62%><color=#C8D6E5>{regionSubtitle}</color></size>";
            worldLabel.text = unlocked
                ? $"<color=#42F5C8>[WAYSTONE] {DisplayName}</color>{sub}\n<size=58%>[E] Fast Travel</size>"
                : $"<color=#FFD166>[UNATTUNED] Ancient Waystone</color>{sub}\n<size=58%>[E] Attune to Unlock</size>";
        }
    }

    // ------------------------------------------------------------------ Static Registry, Fast Travel & Save API

    public static int UnlockedCount => s_unlockedIds.Count;

    public static bool IsWaystoneUnlocked(string id)
    {
        return !string.IsNullOrEmpty(id) && s_unlockedIds.Contains(id);
    }

    public static bool UnlockWaystone(string id, bool saveImmediately = true)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        bool added = s_unlockedIds.Add(id.Trim());
        if (added)
        {
            TeleportWaystone target = FindById(id);
            if (target != null)
            {
                target.RefreshVisualState();
                WaystoneUnlocked?.Invoke(target);
            }
            WaystoneStateChanged?.Invoke();
            if (saveImmediately)
                PlayerSaveManager.RequestAutoSave($"UnlockWaystone_{id}");
            Debug.Log($"[TeleportWaystone] Unlocked '{id}' (Total unlocked: {s_unlockedIds.Count}).");
        }
        return added;
    }

    public static TeleportWaystone FindById(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        for (int i = 0; i < All.Count; i++)
        {
            if (All[i] != null && string.Equals(All[i].waystoneId, id, StringComparison.OrdinalIgnoreCase))
                return All[i];
        }
        return null;
    }

    /// <summary>
    /// ค้นหาหินเทเลพอร์ตที่อยู่ใกล้พิกัดโลกที่คลิกบน World Map มากที่สุดภายในรัศมีที่กำหนด
    /// </summary>
    public static TeleportWaystone FindNearestToWorldPoint(Vector3 worldPos, float maxPlanarDistance)
    {
        TeleportWaystone best = null;
        float bestDist = maxPlanarDistance;
        for (int i = 0; i < All.Count; i++)
        {
            var w = All[i];
            if (w == null || !w.isActiveAndEnabled) continue;
            Vector3 diff = w.transform.position - worldPos;
            diff.y = 0f;
            float d = diff.magnitude;
            if (d <= bestDist)
            {
                bestDist = d;
                best = w;
            }
        }
        return best;
    }

    /// <summary>
    /// สั่งวาร์ปผู้เล่น Local ไปยังหินเทเลพอร์ตที่ปลดล็อกแล้ว (ใช้ได้ทั้งจากหน้าต่าง Fast Travel และคลิกบน World Map [M])
    /// </summary>
    public static bool TryFastTravelTo(TeleportWaystone destination, out string message)
    {
        message = string.Empty;
        if (destination == null)
        {
            message = "Invalid destination Waystone.";
            return false;
        }
        if (!destination.IsUnlocked)
        {
            message = $"Waystone '{destination.DisplayName}' is locked! Discover and press [E] to attune it first.";
            PlayerStatusHud.Instance?.ShowToast(message, new Color(1f, 0.72f, 0.28f), 2.8f);
            return false;
        }

        var player = PlayerCombat.Local;
        if (player == null || !player.IsSpawned || player.IsDead)
        {
            message = "Cannot Fast Travel while dead.";
            return false;
        }

        if (player.IsInCombat && !SafeZone.IsPlayerInAnySafeZone(player))
        {
            message = "Cannot Fast Travel while in combat! Disengage for 6 seconds first.";
            PlayerStatusHud.Instance?.ShowToast(message, new Color(1f, 0.42f, 0.30f), 2.6f);
            return false;
        }

        if (Time.unscaledTime - s_lastFastTravelTime < FastTravelCooldownSeconds)
        {
            message = "Fast Travel cooling down...";
            return false;
        }

        s_lastFastTravelTime = Time.unscaledTime;
        Vector3 arrivalPos = destination.GetSafeArrivalPosition();
        player.TeleportTo(arrivalPos);

        // ปิดหน้าต่างแผนที่และหน้าต่าง Fast Travel อัตโนมัติหลังวาร์ป
        MapManager.Instance?.SetWorldMapOpen(false);
        WaystoneFastTravelUI.Instance?.Close();

        message = $"Fast Traveled to {destination.DisplayName}";
        PlayerStatusHud.Instance?.ShowToast(message, new Color(0.30f, 0.96f, 0.80f), 2.6f);
        Debug.Log($"[TeleportWaystone] {message} -> {arrivalPos}");
        return true;
    }

    /// <summary>
    /// หาจุดเกิดใหม่ที่ใกล้ที่สุดระหว่างหินเทเลพอร์ตที่ปลดล็อกแล้วกับ SafeZone เมืองหลัก (ลดการเดินไกลหลังตาย!)
    /// </summary>
    public static Vector3 ResolveBestRespawnPosition(Vector3 deathPos, Vector3 fallbackSafePos)
    {
        Vector3 bestPos = fallbackSafePos;
        float bestDistSq = PlanarDistSq(deathPos, fallbackSafePos);

        for (int i = 0; i < All.Count; i++)
        {
            var w = All[i];
            if (w == null || !w.isActiveAndEnabled || !w.IsUnlocked) continue;
            Vector3 arrival = w.GetSafeArrivalPosition();
            float dSq = PlanarDistSq(deathPos, arrival);
            if (dSq < bestDistSq)
            {
                bestDistSq = dSq;
                bestPos = arrival;
            }
        }
        return bestPos;
    }

    public static void ExportSaveData(PlayerSaveData data)
    {
        if (data == null) return;
        if (data.unlockedWaystones == null)
            data.unlockedWaystones = new List<string>();
        data.unlockedWaystones.Clear();
        foreach (string id in s_unlockedIds)
        {
            if (!string.IsNullOrEmpty(id))
                data.unlockedWaystones.Add(id);
        }
        data.unlockedWaystones.Sort(StringComparer.OrdinalIgnoreCase);
    }

    public static void RestoreSaveData(PlayerSaveData data)
    {
        s_unlockedIds.Clear();
        if (data != null && data.unlockedWaystones != null)
        {
            for (int i = 0; i < data.unlockedWaystones.Count; i++)
            {
                string id = data.unlockedWaystones[i];
                if (!string.IsNullOrWhiteSpace(id))
                    s_unlockedIds.Add(id.Trim());
            }
        }
        WaystoneStateChanged?.Invoke();
    }

    public static void ResetUnlockedForTesting()
    {
        s_unlockedIds.Clear();
        WaystoneStateChanged?.Invoke();
    }

    private static float PlanarDistSq(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return dx * dx + dz * dz;
    }
}
