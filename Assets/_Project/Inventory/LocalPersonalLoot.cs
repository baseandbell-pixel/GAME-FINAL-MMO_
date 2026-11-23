using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// ไอเทมดรอปส่วนตัวแบบ Client-Local แท้จริง (Purely Local Non-Networked Personal Loot):
///   - ไม่ใช้ NetworkObject / NetworkBehaviour ของ Photon Fusion -> สร้างเฉพาะบนเครื่องของผู้เล่นที่มีส่วนร่วมทำดาเมจเท่านั้น
///   - ผู้เล่นคนอื่นในห้องจะไม่เห็น Ghost Object บนพื้น ไม่เห็นจุดบน Minimap และไม่สามารถแย่งเก็บได้ 100%
///   - กดปุ่ม [E] เพื่อเก็บเข้ากระเป๋า InventoryManager.Local ทันทีพร้อมระบบนับเวลาสลายตัวอัตโนมัติ (90 วินาที)
/// </summary>
[DisallowMultipleComponent]
public sealed class LocalPersonalLoot : MonoBehaviour, IWorldInteractable
{
    public InteractionChannel Channel => InteractionChannel.PickupOrChest;
    public int PriorityBias => 0;
    public float InteractionRange => _pickupRadius;
    public Vector3 InteractionWorldPosition => transform.position;
    public bool CanInteractNow(PlayerCombat player) => !IsCollected && Data != null;
    public void ExecuteInteraction(PlayerCombat player) => TryPickup();

    public string GetPromptLabel(PlayerCombat player)
    {
        string enhTag = EnhancementLevel > 0 ? $" +{EnhancementLevel}" : "";
        return $"Pick up  {(Data != null ? Data.displayName : ItemId)}{enhTag}  x{Quantity}  [{SourceTag}]";
    }

    public Color GetPromptAccentColor(PlayerCombat player)
    {
        return Data != null ? ItemData.GetTierColor(Data.tier) : Color.white;
    }
    // รายการไอเทมส่วนตัวแบบ Client-Local ทั้งหมดที่อยู่บนพื้นเครื่องนี้
    public static readonly List<LocalPersonalLoot> All = new List<LocalPersonalLoot>();

    // ค่าคงที่เวลาสลายตัวของไอเทมส่วนตัวบนพื้น (90 วินาที) และช่วงกะพริบเตือน (5 วินาทีสุดท้าย)
    public const float DefaultLifetimeSeconds = 90f;
    public const float BlinkWarningThresholdSeconds = 5f;

    // ข้อมูลไอเทม จำนวน ระดับตีบวก และแหล่งที่มา
    public string ItemId { get; private set; }
    public ItemData Data { get; private set; }
    public int Quantity { get; private set; } = 1;
    public int EnhancementLevel { get; private set; }
    public string SourceTag { get; private set; } = "Personal Loot";
    public bool IsCollected { get; private set; }
    public float PickupRadius => _pickupRadius;
    public float RemainingLifetime => Mathf.Max(0f, _lifetimeRemaining);

    private float _pickupRadius = 2.6f;
    private float _lifetimeRemaining = DefaultLifetimeSeconds;
    private float _bobHeight = 0.15f;
    private float _spinSpeed = 95f;
    private Transform _visual;
    private Renderer _visualRenderer;
    private Renderer _ringRenderer;
    private Vector3 _visualBaseLocalPos;
    private float _lastPickupAttemptTime = -10f;

    // แคชเป้าหมายไอเทม Local ที่อยู่ใกล้ผู้เล่นมากที่สุดในเฟรมปัจจุบัน
    private static int s_frame = -1;
    private static LocalPersonalLoot s_promptItem;

    /// <summary>
    /// คืนค่าไอเทมส่วนตัว Local ที่ใกล้ผู้เล่นที่สุดในเฟรมนี้พร้อมระยะทาง (ใช้ประสานงานกับ WorldItem เพื่อไม่ให้กด E ซ้อนกัน)
    /// </summary>
    public static LocalPersonalLoot GetClosestLocalLoot(out float distance)
    {
        RefreshPromptTarget();
        distance = s_closestDistance;
        return s_promptItem;
    }

    private static float s_closestDistance = float.MaxValue;

    /// <summary>
    /// สร้างไอเทมดรอปส่วนตัวแบบ Client-Local ลงบนพื้นเฉพาะหน้าจอผู้เล่นเครื่องนี้เท่านั้น
    /// </summary>
    public static LocalPersonalLoot SpawnLocal(
        Vector3 position,
        ItemData item,
        int quantity = 1,
        int enhancementLevel = 0,
        float lifetimeSeconds = DefaultLifetimeSeconds,
        string sourceTag = "Personal Loot")
    {
        if (item == null || ItemRegistry.IsRemovedJunkItem(item.itemId)) return null;

        var go = new GameObject($"LocalPersonalLoot_{item.itemId}");
        // แนบไอเทมดรอปส่วนตัวเข้ากับความสูงพื้นผิวจริงของแผนที่ (ไม่จมเนินหรือลอยสูงบนภูมิประเทศต่างระดับ)
        float groundY = SandboxWorldSpawner.SampleGroundY(position, position.y);
        go.transform.position = new Vector3(position.x, groundY + 0.08f, position.z);

        var loot = go.AddComponent<LocalPersonalLoot>();
        loot.Initialize(item, Mathf.Max(1, quantity), Mathf.Max(0, enhancementLevel), Mathf.Max(10f, lifetimeSeconds), sourceTag);
        Debug.Log($"[LocalPersonalLoot] Spawned client-only personal drop: {loot.Quantity}x {item.displayName} at {go.transform.position} (Source={loot.SourceTag}).");
        return loot;
    }

    /// <summary>
    /// สร้างไอเทมดรอปส่วนตัวจากรหัส ItemId (เรียกใช้จาก Targeted RPC ของ EnemyNPC)
    /// </summary>
    public static LocalPersonalLoot SpawnLocalById(
        Vector3 position,
        string itemId,
        int quantity = 1,
        int enhancementLevel = 0,
        float lifetimeSeconds = DefaultLifetimeSeconds,
        string sourceTag = "Personal Loot")
    {
        if (string.IsNullOrEmpty(itemId) || ItemRegistry.IsRemovedJunkItem(itemId)) return null;
        ItemData data = ItemRegistry.Get(itemId);
        if (data == null)
        {
            Debug.LogWarning($"[LocalPersonalLoot] Unknown itemId '{itemId}' - skipped local spawn.");
            return null;
        }
        return SpawnLocal(position, data, quantity, enhancementLevel, lifetimeSeconds, sourceTag);
    }

    private void Initialize(ItemData item, int quantity, int enhancementLevel, float lifetimeSeconds, string sourceTag)
    {
        Data = item;
        ItemId = item.itemId;
        Quantity = quantity;
        EnhancementLevel = enhancementLevel;
        _lifetimeRemaining = lifetimeSeconds;
        SourceTag = string.IsNullOrEmpty(sourceTag) ? "Personal Loot" : sourceTag;

        BuildVisual(item);
    }

    private void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
        InteractionManager.Register(this);
    }

    private void OnDisable()
    {
        All.Remove(this);
        InteractionManager.Unregister(this);
        if (s_promptItem == this) s_promptItem = null;
    }

    /// <summary>
    /// สร้างโมเดลกล่องไอเทมและวงแหวนสีระดับความหายากบนพื้นด้วย URP Material เพื่อไม่ให้เกิดสีชมพูใน Standalone Build
    /// </summary>
    private void BuildVisual(ItemData item)
    {
        Color tierColor = ItemData.GetTierColor(item.tier);
        Color itemColor = item.worldColor != Color.white ? item.worldColor : tierColor;

        // 1. กล่องไอเทมหลักที่ลอยหมุน
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "Visual";
        var cubeCol = cube.GetComponent<Collider>();
        if (cubeCol != null) Destroy(cubeCol);
        cube.transform.SetParent(transform, false);
        cube.transform.localPosition = new Vector3(0f, 0.32f, 0f);
        cube.transform.localScale = new Vector3(0.44f, 0.44f, 0.44f);
        _visual = cube.transform;
        _visualBaseLocalPos = _visual.localPosition;
        _visualRenderer = cube.GetComponent<Renderer>();
        UrpMaterialHelper.ApplyLitColor(_visualRenderer, itemColor);

        // 2. วงแหวนสี Tier ใต้ไอเทมให้มองเห็นเด่นชัดเฉพาะเจ้าของ (ใช้ URP Unlit ถาวร)
        GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "RarityRing";
        var ringCol = ring.GetComponent<Collider>();
        if (ringCol != null) Destroy(ringCol);
        ring.transform.SetParent(transform, false);
        ring.transform.localPosition = new Vector3(0f, 0.03f, 0f);
        ring.transform.localScale = new Vector3(0.95f, 0.015f, 0.95f);
        _ringRenderer = ring.GetComponent<Renderer>();
        UrpMaterialHelper.ApplyUnlitColor(_ringRenderer, tierColor);
    }

    private void Update()
    {
        if (IsCollected) return;

        _lifetimeRemaining -= Time.deltaTime;
        if (_lifetimeRemaining <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        // กะพริบเตือนในช่วง 5 วินาทีสุดท้ายก่อนหมดเวลา
        bool blinking = _lifetimeRemaining <= BlinkWarningThresholdSeconds;
        bool visible = !blinking || (Mathf.FloorToInt(Time.time * 10f) % 2 == 0);
        if (_visualRenderer != null) _visualRenderer.enabled = visible;
        if (_ringRenderer != null) _ringRenderer.enabled = visible;

        if (_visual != null && visible)
        {
            _visual.localPosition = _visualBaseLocalPos + Vector3.up * (Mathf.Sin(Time.time * 2.4f + transform.position.x) * _bobHeight);
            _visual.Rotate(0f, _spinSpeed * Time.deltaTime, 0f, Space.World);
        }

        // การกดปุ่ม [E] ถูกรวมศูนย์ไว้ที่ InteractionManager แล้ว (ป้องกันการเก็บของซ้อนเปิดกล่อง)
        RefreshPromptTarget();
    }

    private static void RefreshPromptTarget()
    {
        if (s_frame == Time.frameCount) return;
        s_frame = Time.frameCount;
        s_promptItem = null;
        s_closestDistance = float.MaxValue;

        var player = PlayerCombat.Local;
        if (player == null || !player.IsSpawned || player.IsDead || GameplayInput.IsBlocked) return;

        Vector3 p = player.transform.position;
        for (int i = 0; i < All.Count; i++)
        {
            var it = All[i];
            if (it == null || it.IsCollected || it.Data == null) continue;
            float d = PlanarDistance(it.transform.position, p);
            if (d <= it._pickupRadius && d < s_closestDistance)
            {
                s_closestDistance = d;
                s_promptItem = it;
            }
        }
    }

    /// <summary>
    /// เก็บไอเทมส่วนตัวเข้าสู่ InventoryManager.Local โดยตรง (ไม่ต้องส่ง RPC ซ้ำซ้อน)
    /// </summary>
    public bool TryPickup()
    {
        if (IsCollected || Data == null) return false;
        if (Time.time - _lastPickupAttemptTime < 0.25f) return false;
        _lastPickupAttemptTime = Time.time;

        var inv = InventoryManager.Local;
        if (inv == null) return false;

        if (!inv.CanAdd(Data, Quantity))
        {
            PlayerStatusHud.Instance?.ShowToast("Inventory Full! Cannot pick up item.", new Color(1f, 0.35f, 0.25f));
            return false;
        }

        int remainder = inv.AddItem(Data, Quantity, EnhancementLevel);
        int collected = Quantity - remainder;
        if (collected <= 0) return false;

        string enhTag = EnhancementLevel > 0 ? $" +{EnhancementLevel}" : "";
        Debug.Log($"[LocalPersonalLoot] Collected {collected}x {Data.displayName}{enhTag} (Private Client-Local Drop).");
        PlayerStatusHud.Instance?.ShowToast($"Picked up {Data.displayName}{enhTag} x{collected}", ItemData.GetTierColor(Data.tier), 2.2f);

        if (remainder > 0)
        {
            Quantity = remainder;
            return true;
        }

        IsCollected = true;
        Destroy(gameObject);
        return true;
    }

    // ป้ายแจ้งเตือน [E] ย้ายไปวาดรวมบน uGUI Canvas กลางที่ InteractionPromptUI แล้ว (รองรับ 2K/4K และแก้ไขหน้าตาใน Unity Prefab ได้)

    private static float PlanarDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }
}
