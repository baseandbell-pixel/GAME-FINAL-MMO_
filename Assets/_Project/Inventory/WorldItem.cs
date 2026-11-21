using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// ไอเทมที่ดรอปอยู่บนพื้นโลก (World Item) สำหรับ Photon Fusion Shared Mode:
///   1. รองรับระบบ Personal Instanced Loot (AllowedLooter):
///      - เมื่อมอนสเตอร์/บอสตาย จะสร้างไอเทมที่ผูกสิทธิ์เฉพาะผู้เล่นคนนั้น (AllowedLooter = PlayerRef)
///      - เครื่องของผู้เล่นคนอื่นจะมองไม่เห็นและไม่สามารถกดแย่งเก็บไอเทมชิ้นนี้ได้ (แก้ปัญหา Host/Client แย่งของ 100%)
///   2. รองรับระบบลบไอเทมอัตโนมัติใน 30 วินาที (30-Second Dropped Item Despawn):
///      - เมื่อผู้เล่นกดทิ้งไอเทมลงพื้น (IsPlayerDropped = true) จะเริ่มนับถอยหลัง 30 วินาที
///      - ในช่วง 5 วินาทีสุดท้าย ไอเทมจะกะพริบเตือนก่อนสลายหายไปจากฉากอัตโนมัติ
/// </summary>
public class WorldItem : NetworkBehaviour
{
    // รายการไอเทมบนพื้นทั้งหมดที่กำลังทำงานอยู่ในฉาก
    public static readonly List<WorldItem> All = new List<WorldItem>();

    // ค่าคงที่เวลาสลายตัวของไอเทมที่ผู้เล่นทิ้งลงพื้น (30 วินาที) และเวลาเริ่มกะพริบเตือน (5 วินาทีสุดท้าย)
    public const float DefaultDroppedDespawnSeconds = 30f;
    public const float BlinkWarningThresholdSeconds = 5f;

    // รหัสไอเทมที่ซิงก์ผ่าน Network
    [Networked] public NetworkString<_32> ItemId { get; set; }

    // จำนวนไอเทมในกองนี้
    [Networked] public int Quantity { get; set; } = 1;

    // ระดับตีบวกของอุปกรณ์ที่ถูกทิ้งลงพื้น (+0..+10)
    [Networked] public int EnhancementLevel { get; set; } = 0;

    // สถานะว่าถูกเก็บไปแล้วหรือยัง
    [Networked, OnChangedRender(nameof(OnClaimedChanged))] public NetworkBool Claimed { get; set; }

    // ผู้เล่นเจ้าของสิทธิ์เก็บไอเทมชิ้นนี้ (PlayerRef.None = เก็บได้ทุกคน, หากระบุ PlayerRef = เห็นและเก็บได้เฉพาะคนนั้น)
    [Networked, OnChangedRender(nameof(OnOwnerChanged))] public PlayerRef AllowedLooter { get; set; }

    // ระบุว่าเป็นไอเทมที่ผู้เล่นกดทิ้งลงพื้นเองหรือไม่ (ใช้เปิดระบบนับถอยหลัง 30 วินาที)
    [Networked] public NetworkBool IsPlayerDropped { get; set; }

    [Header("Interaction")]
    // ระยะกดเก็บไอเทมด้วยปุ่ม E
    [Min(0.5f)] [SerializeField] private float pickupRadius = 2.6f;
    // ระยะเผื่อความหน่วงของเครือข่าย (Lag Compensation Slack)
    [SerializeField] private float validationSlack = 2.5f;
    // เวลาหน่วงก่อนลบ Object หลังเก็บสำเร็จ
    [SerializeField] private float despawnDelay = 0.5f;
    // เวลาสลายตัวอัตโนมัติเมื่อผู้เล่นทิ้งลงพื้น (30 วินาที)
    [SerializeField] private float droppedDespawnSeconds = DefaultDroppedDespawnSeconds;

    [Header("Visuals")]
    [SerializeField] private Transform visual;
    [SerializeField] private Renderer visualRenderer;
    [SerializeField] private float bobHeight = 0.15f;
    [SerializeField] private float spinSpeed = 90f;

    // สถานะว่า Spawn ในระบบสมบูรณ์แล้วหรือยัง
    public bool IsSpawned { get; private set; }

    // ข้อมูล ItemData ที่อ้างอิงจาก ItemRegistry
    public ItemData Data => ItemRegistry.Get(ItemId.ToString());

    // เวลาที่เหลือก่อนไอเทมที่ทิ้งลงพื้นจะสลายไป (วินาที)
    public float DespawnRemainingSeconds => Mathf.Max(0f, _despawnTimerRemaining);

    // ตรวจสอบว่าอยู่ในช่วง 5 วินาทีสุดท้ายที่กำลังกะพริบเตือนหรือไม่
    public bool IsBlinkingWarning => IsPlayerDropped && !Claimed && _despawnTimerRemaining > 0f && _despawnTimerRemaining <= BlinkWarningThresholdSeconds;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private MaterialPropertyBlock _mpb;
    private Vector3 _visualBase;
    private float _claimedAt = -1f;
    private float _lastRequestTime = -10f;
    private float _despawnTimerRemaining = DefaultDroppedDespawnSeconds;

    // ตัวแปรคำนวณหาไอเทมที่อยู่ใกล้ผู้เล่นที่สุดต่อเฟรม
    private static int s_frame = -1;
    private static WorldItem s_promptItem;
    private static float s_promptDistance = float.MaxValue;

    /// <summary>
    /// ตรวจสอบว่ามีไอเทม Networked WorldItem อยู่ใกล้ผู้เล่นมากกว่าระยะที่ระบุหรือไม่ (ใช้ประสานงานกับ LocalPersonalLoot)
    /// </summary>
    public static bool IsAnyNetworkedItemCloserThan(float otherDistance)
    {
        RefreshPromptTarget();
        return s_promptItem != null && s_promptDistance < otherDistance;
    }

    // ------------------------------------------------------------------ ฟังก์ชันสร้างไอเทมลงพื้น (Spawn Helpers)

    /// <summary>
    /// สร้างไอเทมดรอปส่วนตัวแบบ Client-Local แท้จริง (ไม่มี NetworkObject และผู้เล่นอื่นมองไม่เห็น 100%)
    /// </summary>
    public static LocalPersonalLoot SpawnLocal(
        Vector3 position,
        ItemData item,
        int quantity = 1,
        int enhancementLevel = 0,
        string sourceTag = "Personal Loot")
    {
        return LocalPersonalLoot.SpawnLocal(position, item, quantity, enhancementLevel, LocalPersonalLoot.DefaultLifetimeSeconds, sourceTag);
    }

    /// <summary>
    /// สร้างไอเทมลงพื้นโลก (รองรับทั้งไอเทมส่วนกลาง, ไอเทมส่วนตัว Personal Instanced Loot, และไอเทมที่ผู้เล่นทิ้งเอง 30s)
    /// </summary>
    public static WorldItem Spawn(
        NetworkRunner runner,
        WorldItem prefab,
        Vector3 position,
        ItemData item,
        int quantity,
        PlayerRef allowedLooter = default,
        bool isPlayerDropped = false,
        int enhancementLevel = 0)
    {
        if (runner == null || !runner.IsRunning) { Debug.LogWarning("[WorldItem] Spawn failed: runner not running."); return null; }
        if (prefab == null) { Debug.LogWarning("[WorldItem] Spawn failed: no prefab."); return null; }
        if (item == null || ItemRegistry.IsRemovedJunkItem(item.itemId)) return null;

        string id = item.itemId;
        int qty = Mathf.Max(1, quantity);
        return runner.Spawn(prefab, position, Quaternion.identity, null, (r, o) =>
        {
            var wi = o.GetComponent<WorldItem>();
            wi.ItemId = id;
            wi.Quantity = qty;
            wi.EnhancementLevel = Mathf.Max(0, enhancementLevel);
            wi.AllowedLooter = allowedLooter;
            wi.IsPlayerDropped = isPlayerDropped;
            wi._despawnTimerRemaining = wi.droppedDespawnSeconds;
        });
    }

    // ------------------------------------------------------------------ วงจรการทำงาน (Lifecycle)

    private void Awake()
    {
        _mpb = new MaterialPropertyBlock();
        if (visual == null && visualRenderer != null) visual = visualRenderer.transform;
        if (visualRenderer == null) visualRenderer = GetComponentInChildren<Renderer>();
        if (visual == null && visualRenderer != null) visual = visualRenderer.transform;
        if (visual != null) _visualBase = visual.localPosition;
        _despawnTimerRemaining = droppedDespawnSeconds;
    }

    public override void Spawned()
    {
        IsSpawned = true;
        if (!All.Contains(this)) All.Add(this);
        _despawnTimerRemaining = droppedDespawnSeconds;

        var data = Data;
        // หากเป็นไอเทมขยะที่ถูกตัดออก ให้ลบออกจากฉากทันที
        if (data == null || ItemRegistry.IsRemovedJunkItem(ItemId.ToString()))
        {
            if (Object.HasStateAuthority) Runner.Despawn(Object);
            else gameObject.SetActive(false);
            return;
        }

        Debug.Log($"[WorldItem] Spawned {Quantity}x {data.displayName} at {transform.position} (AllowedLooter={AllowedLooter}, PlayerDropped={IsPlayerDropped}).");
        ApplyVisual();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        IsSpawned = false;
        All.Remove(this);
        if (s_promptItem == this) s_promptItem = null;
    }

    /// <summary>
    /// ตรวจสอบว่าผู้เล่นเครื่องนี้ (Local Player) มีสิทธิ์มองเห็นและเก็บไอเทมชิ้นนี้หรือไม่
    /// </summary>
    public bool IsVisibleAndLootableByLocalPlayer()
    {
        if (!IsSpawned || Claimed) return false;
        if (AllowedLooter == PlayerRef.None) return true;
        if (Runner != null && Runner.LocalPlayer != PlayerRef.None)
            return Runner.LocalPlayer == AllowedLooter;
        return true;
    }

    /// <summary>
    /// ตรวจสอบว่าผู้เล่นที่ระบุ (PlayerRef) มีสิทธิ์เก็บไอเทมชิ้นนี้หรือไม่
    /// </summary>
    public bool CanPlayerLoot(PlayerRef player)
    {
        if (Claimed) return false;
        if (AllowedLooter == PlayerRef.None) return true;
        return AllowedLooter == player;
    }

    /// <summary>
    /// อัปเดตการแสดงผลสีและซ่อนไอเทมทั้งหมดหากไม่ใช่ของเครื่องผู้เล่นคนนี้
    /// </summary>
    private void ApplyVisual()
    {
        bool visible = IsVisibleAndLootableByLocalPlayer();
        var data = Data;
        Color c = data != null ? data.worldColor : new Color(0.95f, 0.82f, 0.28f, 1f);

        if (visualRenderer != null)
        {
            // ซ่อมแซม Material ให้เป็น URP เสมอหาก Prefab สูญเสียการอ้างอิง Shader
            UrpMaterialHelper.EnsureValidUrpMaterial(visualRenderer);
            visualRenderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, c);
            _mpb.SetColor(ColorId, c);
            visualRenderer.SetPropertyBlock(_mpb);
            visualRenderer.enabled = visible;
        }

        // ซ่อน Renderer ย่อยทุกตัวเพื่อป้องกันไม่ให้เห็นชิ้นส่วนค้างหากไม่ใช่เจ้าของสิทธิ์
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (r != null)
            {
                UrpMaterialHelper.EnsureValidUrpMaterial(r);
                r.enabled = visible;
            }
        }
    }

    private void OnClaimedChanged()
    {
        if (!IsSpawned) return;
        ApplyVisual();
    }

    private void OnOwnerChanged()
    {
        if (!IsSpawned) return;
        ApplyVisual();
    }

    /// <summary>
    /// สำหรับการจำลองทดสอบ (Testing Helper): ปรับค่าเวลาที่เหลือก่อนไอเทมที่ทิ้งลงพื้นจะสลายตัว
    /// </summary>
    public void SetDespawnTimerForTesting(float remainingSeconds)
    {
        IsPlayerDropped = true;
        _despawnTimerRemaining = Mathf.Max(0f, remainingSeconds);
    }

    // ------------------------------------------------------------------ ระบบจัดการฝั่ง Authority (FixedUpdateNetwork)

    public override void FixedUpdateNetwork()
    {
        if (!IsSpawned || !Object.HasStateAuthority) return;

        // 1. กรณีถูกเก็บไปแล้ว รอเวลาสั้นๆ แล้วลบออกจาก Network
        if (Claimed)
        {
            if (_claimedAt < 0f) _claimedAt = Time.time;
            if (Time.time - _claimedAt >= despawnDelay)
            {
                Runner.Despawn(Object);
            }
            return;
        }

        // 2. กรณีเป็นไอเทมที่ผู้เล่นกดทิ้งลงพื้น นับถอยหลัง 30 วินาทีแล้วลบอัตโนมัติ
        if (IsPlayerDropped)
        {
            _despawnTimerRemaining -= Runner.DeltaTime;
            if (_despawnTimerRemaining <= 0f)
            {
                Debug.Log($"[WorldItem] Player-dropped item '{ItemId}' expired after {droppedDespawnSeconds:0}s - despawning.");
                Runner.Despawn(Object);
            }
        }
    }

    // ------------------------------------------------------------------ การโต้ตอบฝั่งผู้เล่น (Local Update)

    private void Update()
    {
        if (!IsSpawned) return;

        // นับเวลาถอยหลังฝั่ง Client ด้วยเพื่อให้จังหวะกะพริบ 5 วินาทีสุดท้ายตรงกันทุกเครื่อง
        if (IsPlayerDropped && !Claimed && (Object == null || !Object.HasStateAuthority))
        {
            _despawnTimerRemaining = Mathf.Max(0f, _despawnTimerRemaining - Time.deltaTime);
        }

        bool visibleToLocal = IsVisibleAndLootableByLocalPlayer();
        if (visualRenderer != null)
        {
            if (!visibleToLocal)
            {
                visualRenderer.enabled = false;
            }
            else if (IsBlinkingWarning)
            {
                // กะพริบเตือนในช่วง 5 วินาทีสุดท้ายก่อนไอเทมหาย
                bool blinkOn = Mathf.FloorToInt(Time.time * 10f) % 2 == 0;
                visualRenderer.enabled = blinkOn;
            }
            else
            {
                visualRenderer.enabled = true;
            }
        }

        if (visual != null && visibleToLocal)
        {
            visual.localPosition = _visualBase + Vector3.up * (Mathf.Sin(Time.time * 2.2f + transform.position.x) * bobHeight);
            visual.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
        }

        RefreshPromptTarget();
        if (s_promptItem != this) return;

        // หากมีไอเทมส่วนตัว LocalPersonalLoot อยู่ใกล้กว่า ให้ไอเทมส่วนตัวเป็นตัวรับปุ่ม [E]
        LocalPersonalLoot closestLocal = LocalPersonalLoot.GetClosestLocalLoot(out float localDist);
        if (closestLocal != null && localDist <= s_promptDistance) return;

        var kb = Keyboard.current;
        if (kb != null && kb.eKey.wasPressedThisFrame) RequestPickup();
    }

    /// <summary>
    /// ค้นหาไอเทมที่อยู่ใกล้ผู้เล่น Local มากที่สุดและผู้เล่นมีสิทธิ์เก็บได้
    /// </summary>
    private static void RefreshPromptTarget()
    {
        if (s_frame == Time.frameCount) return;
        s_frame = Time.frameCount;
        s_promptItem = null;
        s_promptDistance = float.MaxValue;

        var player = PlayerCombat.Local;
        if (player == null || !player.IsSpawned || player.IsDead || GameplayInput.IsBlocked) return;

        Vector3 p = player.transform.position;
        for (int i = 0; i < All.Count; i++)
        {
            var it = All[i];
            if (it == null || !it.IsVisibleAndLootableByLocalPlayer()) continue;
            float d = Planar(it.transform.position, p);
            if (d <= it.pickupRadius && d < s_promptDistance)
            {
                s_promptDistance = d;
                s_promptItem = it;
            }
        }
    }

    /// <summary>
    /// ส่งคำขอเก็บไอเทมชิ้นนี้ไปยัง State Authority
    /// </summary>
    public void RequestPickup()
    {
        if (!IsVisibleAndLootableByLocalPlayer()) return;
        if (Time.time - _lastRequestTime < 0.35f) return;
        _lastRequestTime = Time.time;

        var data = Data;
        var inv = InventoryManager.Local;
        if (data == null || inv == null) return;
        if (!inv.CanAdd(data, Quantity))
        {
            PlayerStatusHud.Instance?.ShowToast("Inventory Full! Cannot pick up item.", new Color(1f, 0.35f, 0.25f));
            return;
        }

        RPC_RequestPickup();
    }

    // ------------------------------------------------------------------ Network RPCs

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestPickup(RpcInfo info = default)
    {
        if (!IsSpawned || !Object.HasStateAuthority || Claimed) return;

        PlayerRef requester = info.Source;
        if (requester == PlayerRef.None && Runner != null)
            requester = Runner.LocalPlayer;

        // ตรวจสอบสิทธิ์เจ้าของไอเทม (ป้องกัน Host หรือผู้เล่นอื่นแย่งเก็บของส่วนตัว)
        if (!CanPlayerLoot(requester))
        {
            Debug.LogWarning($"[WorldItem] Pickup rejected: {requester} is not AllowedLooter ({AllowedLooter}).");
            return;
        }

        PlayerCombat picker = null;
        for (int i = 0; i < PlayerCombat.All.Count; i++)
        {
            var p = PlayerCombat.All[i];
            if (p != null && p.IsSpawned && (p.Object.StateAuthority == requester || p.Object.InputAuthority == requester))
            {
                picker = p;
                break;
            }
        }
        if (picker == null && PlayerCombat.Local != null && requester == Runner.LocalPlayer)
            picker = PlayerCombat.Local;

        if (picker == null || picker.IsDead) return;

        float d = Planar(picker.transform.position, transform.position);
        if (d > pickupRadius + validationSlack) return;

        Claimed = true;
        RPC_ConfirmPickup(requester, EnhancementLevel);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_ConfirmPickup(PlayerRef winner, int enhancementLvl)
    {
        if (Runner == null || Runner.LocalPlayer != winner) return;

        var data = Data;
        var inv = InventoryManager.Local;
        if (data == null || inv == null) return;

        int remainder = inv.AddItem(data, Quantity, enhancementLvl);
        string enhTag = enhancementLvl > 0 ? $" +{enhancementLvl}" : "";
        Debug.Log($"[WorldItem] Local player collected personal/world drop: {Quantity - remainder}x {data.displayName}{enhTag}.");
        PlayerStatusHud.Instance?.ShowToast($"Picked up {data.displayName}{enhTag} x{Quantity - remainder}", ItemData.GetTierColor(data.tier), 2.2f);

        if (remainder > 0)
        {
            inv.SpawnWorldItem(data, remainder, transform.position, enhancementLvl);
        }
    }

    // ------------------------------------------------------------------ ป้ายแจ้งเตือนกดเก็บ [E] บนหน้าจอ

    private void OnGUI()
    {
        if (!IsSpawned || s_promptItem != this || Event.current.type != EventType.Repaint) return;
        if (!IsVisibleAndLootableByLocalPlayer()) return;

        LocalPersonalLoot closestLocal = LocalPersonalLoot.GetClosestLocalLoot(out float localDist);
        if (closestLocal != null && localDist <= s_promptDistance) return;

        var data = Data;
        string enhTag = EnhancementLevel > 0 ? $" +{EnhancementLevel}" : "";
        string timerTag = IsPlayerDropped ? $"  ({Mathf.CeilToInt(_despawnTimerRemaining)}s)" : "";
        string personalTag = AllowedLooter != PlayerRef.None ? " [Personal Loot]" : "";
        string text = $"[E] Pick up  {(data != null ? data.displayName : ItemId.ToString())}{enhTag}  x{Quantity}{personalTag}{timerTag}";

        var rect = new Rect(Screen.width * 0.5f - 190f, Screen.height * 0.72f, 380f, 30f);
        var prev = GUI.color;
        GUI.color = new Color(0.04f, 0.05f, 0.08f, 0.84f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = data != null ? ItemData.GetTierColor(data.tier) : Color.white;
        var style = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 14,
            fontStyle = FontStyle.Bold
        };
        GUI.Label(rect, text, style);
        GUI.color = prev;
    }

    private static float Planar(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }
}
