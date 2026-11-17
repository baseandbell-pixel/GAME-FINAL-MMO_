using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// กล่องสมบัติรางวัลประจำแคมป์มอนสเตอร์และบอส (Treasure Chest System):
///   - ดรอปไอเทมแบบส่วนตัว (Personal Instanced Loot) ให้เฉพาะผู้เล่นที่กดเปิด โดยถ่วงน้ำหนักตามดาเมจที่ทำได้
///   - เมื่อเปิดรับของแล้ว กล่องจะสลายตัวหายไปเองอย่างสะอาดภายในเวลาที่กำหนด (ก่อนที่มอนสเตอร์ในแคมป์จะเกิดใหม่ในรอบ 1:30 นาที)
///   - เมื่อมอนสเตอร์เกิดใหม่และถูกเคลียร์อีกครั้ง ระบบจะสร้างกล่องสมบัติใบใหม่ขึ้นมาครบวงจร
/// </summary>
[DisallowMultipleComponent]
public class LootChest : MonoBehaviour
{
    // รายการกล่องสมบัติทั้งหมดในฉาก
    public static readonly List<LootChest> All = new List<LootChest>();

    // เวลาสลายตัวของกล่องหลังจากถูกเปิดแล้ว (วินาที) เพื่อให้หายไปก่อนมอนสเตอร์เกิดใหม่
    public const float DefaultOpenedDespawnDelay = 4.0f;

    [Header("Chest configuration")]
    [SerializeField] private int campId = 1;
    [SerializeField] private bool isBossChest;
    [SerializeField] private float interactRadius = 3.4f;
    [SerializeField] private int dropsPerChest = 2;
    [SerializeField] private float openedDespawnDelay = DefaultOpenedDespawnDelay;
    [SerializeField] private WorldItem worldItemPrefab;

    // รหัสแคมป์ของกล่องใบนี้
    public int CampId => campId;
    // เป็นกล่องสมบัติของบอสหรือไม่
    public bool IsBossChest => isBossChest;
    // สถานะว่ากล่องถูกเปิดไปแล้วหรือยัง
    public bool IsOpened { get; private set; }
    // เวลาที่เหลือก่อนกล่องที่เปิดแล้วจะสลายไป
    public float DespawnRemainingSeconds => IsOpened ? Mathf.Max(0f, _despawnAtTime - Time.time) : -1f;
    // รายการไอเทมที่สุ่มได้จากการเปิดกล่อง
    public IReadOnlyList<ItemData> DroppedItems => _droppedItems;

    private readonly List<ItemData> _droppedItems = new List<ItemData>();
    private Transform _lidPivot;
    private Transform _beaconRing;
    private Light _chestLight;
    private TextMeshPro _worldLabel;
    private float _lidAngle;
    private float _despawnAtTime = -1f;
    private float _bannerUntil = -1f;
    private string _bannerText = "";
    private Color _bannerColor = Color.white;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    /// <summary>
    /// สร้างกล่องสมบัติประจำแคมป์หรือบอสลงในฉาก
    /// </summary>
    public static LootChest SpawnAt(Vector3 position, int campId, WorldItem itemPrefab, bool isBossChest = false, bool replaceExisting = true)
    {
        for (int i = All.Count - 1; i >= 0; i--)
        {
            if (All[i] != null && All[i].campId == campId)
            {
                if (replaceExisting)
                {
                    Destroy(All[i].gameObject);
                    All.RemoveAt(i);
                }
                else
                {
                    return All[i];
                }
            }
        }

        var go = new GameObject(isBossChest ? $"BossTreasureChest_{campId}" : $"CampTreasureChest_{campId}");
        go.transform.position = new Vector3(position.x, 0f, position.z);
        if (isBossChest) go.transform.localScale = new Vector3(1.35f, 1.35f, 1.35f);

        var chest = go.AddComponent<LootChest>();
        chest.campId = campId;
        chest.isBossChest = isBossChest;
        chest.dropsPerChest = isBossChest ? 3 : 2;
        chest.worldItemPrefab = itemPrefab;
        chest.BuildVisuals();
        Debug.Log($"[LootChest] Spawned {(isBossChest ? "BOSS " : "")}Camp {campId} Reward Treasure Chest at {go.transform.position}.");
        return chest;
    }

    private void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
    }

    private void OnDisable()
    {
        All.Remove(this);
    }

    private void Awake()
    {
        if (_lidPivot == null) BuildVisuals();
    }

    /// <summary>
    /// สร้างโมเดลกล่องสมบัติ 3 มิติ พร้อมฝาเปิด-ปิด แสงไฟ และป้ายชื่อลอยเหนือกล่อง
    /// </summary>
    private void BuildVisuals()
    {
        if (_lidPivot != null) return;

        var box = GetComponent<BoxCollider>();
        if (box == null) box = gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.45f, 0f);
        box.size = new Vector3(1.25f, 0.9f, 0.85f);
        box.isTrigger = false;

        Color woodDark = isBossChest ? new Color(0.45f, 0.08f, 0.12f, 1f) : new Color(0.36f, 0.21f, 0.09f, 1f);
        Color woodLid = isBossChest ? new Color(0.58f, 0.10f, 0.16f, 1f) : new Color(0.44f, 0.26f, 0.11f, 1f);
        Color goldTrim = new Color(0.95f, 0.76f, 0.18f, 1f);
        Color glowGold = new Color(1.0f, 0.85f, 0.25f, 1f);

        // ตัวถังกล่องสมบัติ
        CreatePart(transform, "ChestBase", PrimitiveType.Cube,
            new Vector3(0f, 0.28f, 0f), new Vector3(1.15f, 0.56f, 0.76f), woodDark);

        CreatePart(transform, "BaseTrimLeft", PrimitiveType.Cube,
            new Vector3(-0.38f, 0.28f, 0f), new Vector3(0.10f, 0.58f, 0.80f), goldTrim);
        CreatePart(transform, "BaseTrimRight", PrimitiveType.Cube,
            new Vector3(0.38f, 0.28f, 0f), new Vector3(0.10f, 0.58f, 0.80f), goldTrim);

        // จุดหมุนฝากล่อง
        var pivotGo = new GameObject("LidPivot");
        pivotGo.transform.SetParent(transform, false);
        pivotGo.transform.localPosition = new Vector3(0f, 0.56f, -0.38f);
        _lidPivot = pivotGo.transform;

        CreatePart(_lidPivot, "ChestLid", PrimitiveType.Cube,
            new Vector3(0f, 0.15f, 0.38f), new Vector3(1.19f, 0.30f, 0.80f), woodLid);
        CreatePart(_lidPivot, "LidTrimLeft", PrimitiveType.Cube,
            new Vector3(-0.38f, 0.16f, 0.38f), new Vector3(0.11f, 0.32f, 0.83f), goldTrim);
        CreatePart(_lidPivot, "LidTrimRight", PrimitiveType.Cube,
            new Vector3(0.38f, 0.16f, 0.38f), new Vector3(0.11f, 0.32f, 0.83f), goldTrim);
        CreatePart(_lidPivot, "ChestLock", PrimitiveType.Cube,
            new Vector3(0f, 0.04f, 0.79f), new Vector3(0.20f, 0.24f, 0.08f), glowGold);

        // วงแหวนเรืองแสงบนพื้น
        var ring = CreatePart(transform, "ChestBeacon", PrimitiveType.Cylinder,
            new Vector3(0f, 0.02f, 0f), new Vector3(1.85f, 0.015f, 1.85f),
            isBossChest ? new Color(1f, 0.45f, 0.12f, 1f) : new Color(1f, 0.82f, 0.22f, 1f));
        _beaconRing = ring.transform;

        // แสงไฟส่องสว่างรอบกล่อง
        var lightGo = new GameObject("ChestLight");
        lightGo.transform.SetParent(transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 1.15f, 0f);
        _chestLight = lightGo.AddComponent<Light>();
        _chestLight.type = LightType.Point;
        _chestLight.color = isBossChest ? new Color(1f, 0.55f, 0.18f) : new Color(1f, 0.82f, 0.28f);
        _chestLight.intensity = isBossChest ? 3.4f : 2.5f;
        _chestLight.range = isBossChest ? 7.0f : 5.5f;

        // ป้ายข้อความ 3 มิติเหนือกล่อง
        var labelGo = new GameObject("ChestLabel");
        labelGo.transform.SetParent(transform, false);
        labelGo.transform.localPosition = new Vector3(0f, 1.55f, 0f);
        _worldLabel = labelGo.AddComponent<TextMeshPro>();
        _worldLabel.text = isBossChest
            ? "BOSS TREASURE CHEST\n<size=75%>[E] Open Personal High-Tier Loot</size>"
            : "TREASURE CHEST\n<size=75%>[E] Open Personal Reward</size>";
        _worldLabel.fontSize = 2.6f;
        _worldLabel.alignment = TextAlignmentOptions.Center;
        _worldLabel.color = new Color(1f, 0.86f, 0.24f, 1f);
        _worldLabel.textWrappingMode = TextWrappingModes.NoWrap;
    }

    private static GameObject CreatePart(Transform parent, string partName, PrimitiveType type, Vector3 localPos, Vector3 localScale, Color color)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = partName;
        var col = part.GetComponent<Collider>();
        if (col != null) Destroy(col);

        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPos;
        part.transform.localScale = localScale;

        var r = part.GetComponent<Renderer>();
        if (r != null)
        {
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, color);
            mpb.SetColor(ColorId, color);
            r.SetPropertyBlock(mpb);
        }
        return part;
    }

    private void Update()
    {
        // เมื่อเปิดกล่องแล้ว ให้หมุนฝาเปิดและนับถอยหลังเพื่อลบกล่องออกจากฉากก่อนมอนสเตอร์เกิดใหม่
        if (IsOpened)
        {
            if (_lidPivot != null && _lidAngle > -68f)
            {
                _lidAngle = Mathf.MoveTowards(_lidAngle, -68f, 180f * Time.deltaTime);
                _lidPivot.localRotation = Quaternion.Euler(_lidAngle, 0f, 0f);
            }

            // ย่อขนาดลงอย่างนุ่มนวลในช่วง 0.8 วินาทีสุดท้ายก่อนสลายหายไป
            float remain = _despawnAtTime - Time.time;
            if (remain <= 0.8f && remain > 0f)
            {
                float s = Mathf.Clamp01(remain / 0.8f) * (isBossChest ? 1.35f : 1f);
                transform.localScale = new Vector3(s, s, s);
            }
            else if (remain <= 0f)
            {
                Debug.Log($"[LootChest] Opened chest for Camp {campId} despawned cleanly (ready for next respawn cycle).");
                Destroy(gameObject);
                return;
            }
        }

        if (_beaconRing != null && !IsOpened)
        {
            float pulse = 1.80f + Mathf.Sin(Time.time * 3.2f) * 0.12f;
            _beaconRing.localScale = new Vector3(pulse, 0.015f, pulse);
        }

        var cam = Camera.main;
        if (cam != null && _worldLabel != null)
        {
            _worldLabel.transform.rotation = cam.transform.rotation;
        }

        if (IsOpened) return;

        var player = PlayerCombat.Local;
        if (player == null || !player.IsSpawned || player.IsDead || GameplayInput.IsBlocked) return;

        if (IsPlayerInRange(player))
        {
            var kb = Keyboard.current;
            if (kb != null && kb.eKey.wasPressedThisFrame)
            {
                OpenChest(player);
            }
        }
    }

    public bool IsPlayerInRange(PlayerCombat player)
    {
        if (player == null) return false;
        Vector3 a = transform.position;
        Vector3 b = player.transform.position;
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b) <= interactRadius;
    }

    /// <summary>
    /// เปิดกล่องสมบัติ สุ่มไอเทมส่วนตัว (Personal Instanced Loot) ให้ผู้เปิด และตั้งเวลาลบกล่องอัตโนมัติ
    /// </summary>
    public List<ItemData> OpenChest(PlayerCombat opener = null, float damageShare = 1.0f, bool isTopDps = true)
    {
        if (IsOpened) return _droppedItems;
        IsOpened = true;
        _despawnAtTime = Time.time + Mathf.Max(0.5f, openedDespawnDelay);

        if (opener == null) opener = PlayerCombat.Local;
        NetworkRunner runner = opener != null ? opener.Runner : null;
        PlayerRef looterRef = (runner != null && runner.IsRunning) ? runner.LocalPlayer : PlayerRef.None;

        if (worldItemPrefab == null && InventoryManager.Local != null)
            worldItemPrefab = InventoryManager.Local.WorldItemPrefab;

        _droppedItems.Clear();
        ItemTier highestTier = ItemTier.Normal;
        int count = Mathf.Max(1, dropsPerChest);

        for (int i = 0; i < count; i++)
        {
            ItemData rolled = LootDropSystem.RollDamageWeightedLoot(
                isBossChest,
                damageShare,
                isTopDps,
                out ItemTier rolledTier,
                out _);

            if (rolled == null) continue;

            _droppedItems.Add(rolled);
            if (rolledTier > highestTier) highestTier = rolledTier;

            float angle = (i / (float)count) * Mathf.PI * 2f + 0.4f;
            Vector3 dropOffset = new Vector3(Mathf.Cos(angle) * 1.45f, 0.45f, Mathf.Sin(angle) * 1.45f);
            Vector3 dropPos = transform.position + dropOffset;

            WorldItem spawnedDrop = null;
            if (runner != null && runner.IsRunning && worldItemPrefab != null)
            {
                // ผูกสิทธิ์ไอเทมให้เฉพาะผู้เล่นที่เปิดกล่อง (Personal Instanced Loot)
                spawnedDrop = WorldItem.Spawn(runner, worldItemPrefab, dropPos, rolled, 1, allowedLooter: looterRef);
            }

            if (spawnedDrop == null && InventoryManager.Local != null)
            {
                InventoryManager.Local.AddItem(rolled, 1);
            }

            Debug.Log($"[LootChest] {(isBossChest ? "Boss " : "")}Camp {campId} Chest rolled personal loot [{ItemData.GetTierDisplayName(rolledTier)}] -> {rolled.displayName} for {looterRef}.");
        }

        if (_beaconRing != null) _beaconRing.gameObject.SetActive(false);
        if (_chestLight != null) _chestLight.color = ItemData.GetTierColor(highestTier);

        string summary = _droppedItems.Count > 0
            ? string.Join("  +  ", _droppedItems.ConvertAll(d => $"[{ItemData.GetTierDisplayName(d.tier)}] {d.displayName}"))
            : "Empty";

        if (_worldLabel != null)
        {
            _worldLabel.text = $"CHEST LOOTED!\n<size=70%>{summary}</size>";
            _worldLabel.color = ItemData.GetTierColor(highestTier);
        }

        _bannerText = $"{(isBossChest ? "BOSS" : "CAMP")} CLEAR REWARD:  {summary}";
        _bannerColor = ItemData.GetTierColor(highestTier);
        _bannerUntil = Time.unscaledTime + 3.8f;

        return _droppedItems;
    }

    /// <summary>
    /// สำหรับการทดสอบ (Testing Helper): สั่งให้กล่องที่เปิดแล้วสลายตัวทันทีเพื่อตรวจสอบวงจร Respawn
    /// </summary>
    public void ForceDespawnNow()
    {
        if (this != null && gameObject != null)
            Destroy(gameObject);
    }

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;

        var player = PlayerCombat.Local;
        if (!IsOpened && player != null && player.IsSpawned && !player.IsDead && IsPlayerInRange(player))
        {
            var rect = new Rect(Screen.width * 0.5f - 185f, Screen.height * 0.67f, 370f, 32f);
            var prev = GUI.color;
            GUI.color = new Color(0.06f, 0.05f, 0.02f, 0.85f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.85f, 0.25f, 1f);
            GUIStyle style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 15,
                fontStyle = FontStyle.Bold
            };
            GUI.Label(rect, isBossChest ? "[E] Open Boss Reward Chest (Personal Loot)" : "[E] Open Camp Reward Chest (Personal Loot)", style);
            GUI.color = prev;
        }

        if (Time.unscaledTime < _bannerUntil && !string.IsNullOrEmpty(_bannerText))
        {
            float alpha = Mathf.Clamp01(_bannerUntil - Time.unscaledTime);
            var rect = new Rect(Screen.width * 0.5f - 300f, 82f, 600f, 38f);
            var prev = GUI.color;
            GUI.color = new Color(0.05f, 0.05f, 0.08f, 0.88f * alpha);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = new Color(_bannerColor.r, _bannerColor.g, _bannerColor.b, alpha);
            GUIStyle style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 16,
                fontStyle = FontStyle.Bold
            };
            GUI.Label(rect, _bannerText, style);
            GUI.color = prev;
        }
    }
}
