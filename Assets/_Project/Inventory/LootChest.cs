using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Physical Treasure Chest spawned at the center of an enemy camp once all enemies in the camp are defeated.
/// Players can approach within <see cref="interactRadius"/> and press [E] to open the chest and roll
/// the Tiered Loot Table (50% Common, 30% Rare, 15% Epic, 5% Legendary), spawning networked <see cref="WorldItem"/> drops.
/// </summary>
[DisallowMultipleComponent]
public class LootChest : MonoBehaviour
{
    public static readonly List<LootChest> All = new List<LootChest>();

    [Header("Chest configuration")]
    [SerializeField] private int campId = 1;
    [SerializeField] private float interactRadius = 3.2f;
    [SerializeField] private int dropsPerChest = 2;
    [SerializeField] private WorldItem worldItemPrefab;

    public int CampId => campId;
    public bool IsOpened { get; private set; }
    public IReadOnlyList<ItemData> DroppedItems => _droppedItems;

    private readonly List<ItemData> _droppedItems = new List<ItemData>();
    private Transform _lidPivot;
    private Transform _beaconRing;
    private Light _chestLight;
    private TextMeshPro _worldLabel;
    private float _lidAngle;
    private float _bannerUntil = -1f;
    private string _bannerText = "";
    private Color _bannerColor = Color.white;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    public static LootChest SpawnAt(Vector3 position, int campId, WorldItem itemPrefab)
    {
        for (int i = 0; i < All.Count; i++)
        {
            if (All[i] != null && All[i].campId == campId)
                return All[i];
        }

        var go = new GameObject($"CampTreasureChest_{campId}");
        go.transform.position = new Vector3(position.x, 0f, position.z);
        var chest = go.AddComponent<LootChest>();
        chest.campId = campId;
        chest.worldItemPrefab = itemPrefab;
        chest.BuildVisuals();
        Debug.Log($"[LootChest] Spawned Camp {campId} Reward Treasure Chest at {go.transform.position}.");
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

        // Solid physical collider on root so players & NPCs do not walk through the chest
        var box = GetComponent<BoxCollider>();
        if (box == null) box = gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.45f, 0f);
        box.size = new Vector3(1.25f, 0.9f, 0.85f);
        box.isTrigger = false;

        Color woodDark = new Color(0.36f, 0.21f, 0.09f, 1f);
        Color woodLid = new Color(0.44f, 0.26f, 0.11f, 1f);
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
            new Vector3(0f, 0.02f, 0f), new Vector3(1.85f, 0.015f, 1.85f), new Color(1f, 0.82f, 0.22f, 1f));
        _beaconRing = ring.transform;

        // Warm Golden Light
        var lightGo = new GameObject("ChestLight");
        lightGo.transform.SetParent(transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 1.15f, 0f);
        _chestLight = lightGo.AddComponent<Light>();
        _chestLight.type = LightType.Point;
        _chestLight.color = new Color(1f, 0.82f, 0.28f);
        _chestLight.intensity = 2.5f;
        _chestLight.range = 5.5f;

        // World-space 3D Label
        var labelGo = new GameObject("ChestLabel");
        labelGo.transform.SetParent(transform, false);
        labelGo.transform.localPosition = new Vector3(0f, 1.55f, 0f);
        _worldLabel = labelGo.AddComponent<TextMeshPro>();
        _worldLabel.text = "TREASURE CHEST\n<size=75%>[E] Open Reward</size>";
        _worldLabel.fontSize = 3.0f;
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
        // Animate lid opening
        if (IsOpened && _lidPivot != null && _lidAngle > -68f)
        {
            _lidAngle = Mathf.MoveTowards(_lidAngle, -68f, 180f * Time.deltaTime);
            _lidPivot.localRotation = Quaternion.Euler(_lidAngle, 0f, 0f);
        }

        // Gentle pulse on ground ring while unopened
        if (_beaconRing != null && !IsOpened)
        {
            float pulse = 1.80f + Mathf.Sin(Time.time * 3.2f) * 0.12f;
            _beaconRing.localScale = new Vector3(pulse, 0.015f, pulse);
        }

        // Billboard world label
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
    /// Opens the chest, rolls the Tiered Loot Table (50% Common, 30% Rare, 15% Epic, 5% Legendary),
    /// and spawns WorldItem drops around the chest.
    /// </summary>
    public List<ItemData> OpenChest(PlayerCombat opener = null)
    {
        if (IsOpened) return _droppedItems;
        IsOpened = true;

        if (opener == null) opener = PlayerCombat.Local;
        NetworkRunner runner = opener != null ? opener.Runner : null;

        if (worldItemPrefab == null && InventoryManager.Local != null)
            worldItemPrefab = InventoryManager.Local.WorldItemPrefab;

        _droppedItems.Clear();
        ItemTier highestTier = ItemTier.Common;
        int count = Mathf.Max(1, dropsPerChest);

        for (int i = 0; i < count; i++)
        {
            ItemData rolled = ItemRegistry.RollTieredLoot(out ItemTier rolledTier);
            if (rolled == null) continue;

            _droppedItems.Add(rolled);
            if (rolledTier > highestTier) highestTier = rolledTier;

            float angle = (i / (float)count) * Mathf.PI * 2f + 0.4f;
            Vector3 dropOffset = new Vector3(Mathf.Cos(angle) * 1.35f, 0.45f, Mathf.Sin(angle) * 1.35f);
            Vector3 dropPos = transform.position + dropOffset;

            WorldItem spawnedDrop = null;
            if (runner != null && runner.IsRunning && worldItemPrefab != null)
            {
                spawnedDrop = WorldItem.Spawn(runner, worldItemPrefab, dropPos, rolled, 1);
            }

            if (spawnedDrop == null && InventoryManager.Local != null)
            {
                // Fallback if WorldItem prefab wasn't wired: add directly to local inventory
                InventoryManager.Local.AddItem(rolled, 1);
            }

            Debug.Log($"[LootChest] Camp {campId} Chest rolled [{rolledTier}] -> {rolled.displayName} (id={rolled.itemId}, worldDrop={(spawnedDrop != null)}).");
        }

        if (_beaconRing != null) _beaconRing.gameObject.SetActive(false);
        if (_chestLight != null) _chestLight.color = ItemData.GetTierColor(highestTier);

        string summary = _droppedItems.Count > 0
            ? string.Join("  +  ", _droppedItems.ConvertAll(d => $"[{d.tier}] {d.displayName}"))
            : "Empty";

        if (_worldLabel != null)
        {
            _worldLabel.text = $"CHEST OPENED!\n<size=70%>{summary}</size>";
            _worldLabel.color = ItemData.GetTierColor(highestTier);
        }

        _bannerText = $"CAMP CLEAR REWARD:  {summary}";
        _bannerColor = ItemData.GetTierColor(highestTier);
        _bannerUntil = Time.unscaledTime + 4.0f;

        return _droppedItems;
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
            GUI.Label(rect, "[E] Open Camp Reward Chest (Tiered Loot)", style);
            GUI.color = prev;
        }

        if (Time.unscaledTime < _bannerUntil && !string.IsNullOrEmpty(_bannerText))
        {
            float alpha = Mathf.Clamp01(_bannerUntil - Time.unscaledTime);
            var rect = new Rect(Screen.width * 0.5f - 260f, 82f, 520f, 36f);
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
