using System.Collections;
using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Physical Treasure Chest spawned at the center of an enemy camp or Boss Arena once defeated.
/// Supports both standard 4-Tier Camp Chests (50% Normal, 30% Rare, 15% Epic, 5% Legendary)
/// and Boss Reward Chests (30% Rare, 45% Epic, 25% Legendary).
/// </summary>
[DisallowMultipleComponent]
public class LootChest : MonoBehaviour
{
    public static readonly List<LootChest> All = new List<LootChest>();

    [Header("Chest configuration")]
    [SerializeField] private int campId = 1;
    [SerializeField] private bool isBossChest;
    [SerializeField] private float interactRadius = 3.2f;
    [SerializeField] private int dropsPerChest = 2;
    [SerializeField] private WorldItem worldItemPrefab;
    [SerializeField] private float vanishDelayAfterOpen = 3.8f;

    public int CampId => campId;
    public bool IsBossChest => isBossChest;
    public bool IsOpened { get; private set; }
    public IReadOnlyList<ItemData> DroppedItems => _droppedItems;

    private readonly List<ItemData> _droppedItems = new List<ItemData>();
    private Transform _lidPivot;
    private Transform _beaconRing;
    private Light _chestLight;
    private TextMeshPro _worldLabel;
    private MMOTreasureChestController _vfxController;
    private float _lidAngle;
    private float _bannerUntil = -1f;
    private string _bannerText = "";
    private Color _bannerColor = Color.white;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

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

        // Chest Base
        CreatePart(transform, "ChestBase", PrimitiveType.Cube,
            new Vector3(0f, 0.28f, 0f), new Vector3(1.15f, 0.56f, 0.76f), woodDark);

        // Gold straps on base
        CreatePart(transform, "BaseTrimLeft", PrimitiveType.Cube,
            new Vector3(-0.38f, 0.28f, 0f), new Vector3(0.10f, 0.58f, 0.80f), goldTrim);
        CreatePart(transform, "BaseTrimRight", PrimitiveType.Cube,
            new Vector3(0.38f, 0.28f, 0f), new Vector3(0.10f, 0.58f, 0.80f), goldTrim);

        // Hinged Lid Pivot at back edge of chest
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

        // Ground Beacon Ring
        var ring = CreatePart(transform, "ChestBeacon", PrimitiveType.Cylinder,
            new Vector3(0f, 0.02f, 0f), new Vector3(1.85f, 0.015f, 1.85f),
            isBossChest ? new Color(1f, 0.45f, 0.12f, 1f) : new Color(1f, 0.82f, 0.22f, 1f));
        _beaconRing = ring.transform;

        // Warm Golden Light
        var lightGo = new GameObject("ChestLight");
        lightGo.transform.SetParent(transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 1.15f, 0f);
        _chestLight = lightGo.AddComponent<Light>();
        _chestLight.type = LightType.Point;
        _chestLight.color = isBossChest ? new Color(1f, 0.55f, 0.18f) : new Color(1f, 0.82f, 0.28f);
        _chestLight.intensity = isBossChest ? 3.4f : 2.5f;
        _chestLight.range = isBossChest ? 7.0f : 5.5f;

        // World-space 3D Label
        var labelGo = new GameObject("ChestLabel");
        labelGo.transform.SetParent(transform, false);
        labelGo.transform.localPosition = new Vector3(0f, 1.55f, 0f);
        _worldLabel = labelGo.AddComponent<TextMeshPro>();
        _worldLabel.text = isBossChest
            ? "BOSS TREASURE CHEST\n<size=75%>[E] Open High-Tier Loot</size>"
            : "TREASURE CHEST\n<size=75%>[E] Open Reward</size>";
        _worldLabel.fontSize = 2.6f;
        _worldLabel.alignment = TextAlignmentOptions.Center;
        _worldLabel.color = new Color(1f, 0.86f, 0.24f, 1f);
        _worldLabel.textWrappingMode = TextWrappingModes.NoWrap;

        // เชื่อมระบบเอฟเฟกต์เปิดกล่องสมบัติแสงทอง 4 เลเยอร์ระดับ AAA เข้ากับหีบสมบัติในเกมจริง
        _vfxController = GetComponent<MMOTreasureChestController>();
        if (_vfxController == null)
        {
            _vfxController = gameObject.AddComponent<MMOTreasureChestController>();
        }
        _vfxController.openAngleX = -105f;
        _vfxController.BindExternalChest(
            _lidPivot,
            new Vector3(0f, 0.56f, 0f),
            dedicatedPointLight: null,
            enableStandaloneInput: false,
            enableDebugHud: false);
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
        // ให้ MMOTreasureChestController เป็นตัวหมุนบานพับฝาหีบแบบ Back Ease-Out (-105 องศา) เพื่อไม่ให้ตีกัน
        if (_vfxController == null && IsOpened && _lidPivot != null && _lidAngle > -68f)
        {
            _lidAngle = Mathf.MoveTowards(_lidAngle, -68f, 180f * Time.deltaTime);
            _lidPivot.localRotation = Quaternion.Euler(_lidAngle, 0f, 0f);
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
    /// Opens the chest, rolls the 4-Tier Loot Table, and spawns WorldItem drops around the chest.
    /// </summary>
    public List<ItemData> OpenChest(PlayerCombat opener = null)
    {
        if (IsOpened) return _droppedItems;
        IsOpened = true;

        if (_vfxController == null)
        {
            _vfxController = GetComponent<MMOTreasureChestController>();
        }
        if (_vfxController != null)
        {
            _vfxController.TriggerOpenChest();
        }

        if (opener == null) opener = PlayerCombat.Local;
        NetworkRunner runner = opener != null ? opener.Runner : null;

        if (worldItemPrefab == null && InventoryManager.Local != null)
            worldItemPrefab = InventoryManager.Local.WorldItemPrefab;

        _droppedItems.Clear();
        ItemTier highestTier = ItemTier.Normal;
        int count = Mathf.Max(1, dropsPerChest);

        for (int i = 0; i < count; i++)
        {
            ItemData rolled = isBossChest
                ? ItemRegistry.RollBossLoot(out ItemTier rolledTier)
                : ItemRegistry.RollTieredLoot(out rolledTier);
            if (rolled == null) continue;

            _droppedItems.Add(rolled);
            if (rolledTier > highestTier) highestTier = rolledTier;

            float angle = (i / (float)count) * Mathf.PI * 2f + 0.4f;
            Vector3 dropOffset = new Vector3(Mathf.Cos(angle) * 1.45f, 0.45f, Mathf.Sin(angle) * 1.45f);
            Vector3 dropPos = transform.position + dropOffset;

            WorldItem spawnedDrop = null;
            if (runner != null && runner.IsRunning && worldItemPrefab != null)
            {
                spawnedDrop = WorldItem.Spawn(runner, worldItemPrefab, dropPos, rolled, 1);
            }

            if (spawnedDrop == null && InventoryManager.Local != null)
            {
                InventoryManager.Local.AddItem(rolled, 1);
            }

            Debug.Log($"[LootChest] {(isBossChest ? "Boss " : "")}Camp {campId} Chest rolled [{ItemData.GetTierDisplayName(rolledTier)}] -> {rolled.displayName} (id={rolled.itemId}).");
        }

        if (_beaconRing != null) _beaconRing.gameObject.SetActive(false);
        if (_chestLight != null) _chestLight.color = ItemData.GetTierColor(highestTier);

        string summary = _droppedItems.Count > 0
            ? string.Join("  +  ", _droppedItems.ConvertAll(d => $"[{ItemData.GetTierDisplayName(d.tier)}] {d.displayName}"))
            : "Empty";

        if (_worldLabel != null)
        {
            _worldLabel.text = $"CHEST OPENED!\n<size=70%>{summary}</size>";
            _worldLabel.color = ItemData.GetTierColor(highestTier);
        }

        _bannerText = $"{(isBossChest ? "BOSS" : "CAMP")} CLEAR REWARD:  {summary}";
        _bannerColor = ItemData.GetTierColor(highestTier);
        _bannerUntil = Time.unscaledTime + 4.0f;

        if (gameObject.activeInHierarchy)
        {
            StartCoroutine(VanishAfterOpenRoutine());
        }

        return _droppedItems;
    }

    /// <summary>
    /// รอให้เอฟเฟกต์เปิดกล่องแสงทอง 4 เลเยอร์แสดงผลครบถ้วน จากนั้นค่อยๆ หรี่แสง ย่อตัว และทำลายกล่องสมบัติทิ้งอัตโนมัติ
    /// </summary>
    private IEnumerator VanishAfterOpenRoutine()
    {
        float holdTime = Mathf.Max(1.8f, vanishDelayAfterOpen - 0.8f);
        yield return new WaitForSeconds(holdTime);

        if (_vfxController != null)
        {
            _vfxController.StopVFXSoftly();
        }

        var box = GetComponent<BoxCollider>();
        if (box != null) box.enabled = false;

        if (_worldLabel != null) _worldLabel.gameObject.SetActive(false);

        Vector3 startScale = transform.localScale;
        float startLightIntensity = _chestLight != null ? _chestLight.intensity : 0f;
        const float shrinkDuration = 0.75f;
        float elapsed = 0f;

        while (elapsed < shrinkDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / shrinkDuration);
            float easeIn = t * t * t;
            transform.localScale = Vector3.Lerp(startScale, Vector3.zero, easeIn);

            if (_chestLight != null)
            {
                _chestLight.intensity = Mathf.Lerp(startLightIntensity, 0f, t);
            }

            yield return null;
        }

        All.Remove(this);
        Destroy(gameObject);
    }

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;

        var player = PlayerCombat.Local;
        if (!IsOpened && player != null && player.IsSpawned && !player.IsDead && IsPlayerInRange(player))
        {
            var rect = new Rect(Screen.width * 0.5f - 175f, Screen.height * 0.67f, 350f, 30f);
            var prev = GUI.color;
            GUI.color = new Color(0.06f, 0.05f, 0.02f, 0.82f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.85f, 0.25f, 1f);
            GUIStyle style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 15,
                fontStyle = FontStyle.Bold
            };
            GUI.Label(rect, isBossChest ? "[E] Open Boss Reward Chest (High-Tier Loot)" : "[E] Open Camp Reward Chest (Tiered Loot)", style);
            GUI.color = prev;
        }

        if (Time.unscaledTime < _bannerUntil && !string.IsNullOrEmpty(_bannerText))
        {
            float alpha = Mathf.Clamp01(_bannerUntil - Time.unscaledTime);
            var rect = new Rect(Screen.width * 0.5f - 280f, 82f, 560f, 36f);
            var prev = GUI.color;
            GUI.color = new Color(0.05f, 0.05f, 0.08f, 0.85f * alpha);
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
