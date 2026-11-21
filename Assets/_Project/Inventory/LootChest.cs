using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// กล่องสมบัติรางวัลประจำแคมป์มอนสเตอร์และบอส (Participation-Locked Personal Treasure Chest):
///   - บันทึกรายชื่อผู้เล่นที่มีส่วนร่วมทำดาเมจในแคมป์นั้น (damage > 0) และล็อกสิทธิ์การเปิดอย่างเคร่งครัด
///   - ผู้เล่นที่ทำดาเมจ 0 (รวมถึง Host ที่ยืนดูเฉยๆ) จะไม่สามารถเปิดกล่องหรือรับรางวัลได้ (Zero-Freeloading Lockout)
///   - เมื่อผู้มีสิทธิ์กดเปิด กล่องจะสุ่มไอเทมตามสัดส่วนดาเมจ (Top DPS ได้โบนัส Epic/Legendary สูงสุด)
///     แล้วสร้างไอเทมตกแบบ Client-Local (LocalPersonalLoot) เฉพาะหน้าจอของคนเปิดเท่านั้น
/// </summary>
[DisallowMultipleComponent]
public class LootChest : MonoBehaviour
{
    /// <summary>
    /// โครงสร้างเก็บสัดส่วนการทำดาเมจของผู้เล่นที่มีสิทธิ์เปิดกล่องใบนี้
    /// </summary>
    public struct ParticipantContribution
    {
        public int playerRawEncoded;
        public float damageShare;
        public bool isTopDps;
    }

    // ข้อความแจ้งเตือนมาตรฐานเมื่อผู้เล่นที่ไม่ได้ร่วมสู้พยายามเปิดกล่อง
    public const string LockedWarningMessage = "Locked: You did not participate in this battle";

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
    // จำนวนผู้เล่นที่มีส่วนร่วมทำดาเมจและมีสิทธิ์เปิดกล่องนี้
    public int EligibleContributorCount => _eligibleContributors.Count;

    private readonly List<ItemData> _droppedItems = new List<ItemData>();
    private readonly Dictionary<int, ParticipantContribution> _eligibleContributors = new Dictionary<int, ParticipantContribution>();
    // เปิดระบบล็อกสิทธิ์ตามสัดส่วนดาเมจเป็นค่าเริ่มต้นเสมอ (ป้องกันการยืนชุบมือเปิบทุกกรณี)
    private bool _hasParticipationLock = true;
    private float _lastLockedToastAt = -10f;
    private Transform _lidPivot;
    private Transform _beaconRing;
    private Light _chestLight;
    private TextMeshPro _worldLabel;
    private float _lidAngle;
    private float _despawnAtTime = -1f;
    private float _bannerUntil = -1f;
    private string _bannerText = "";
    private Color _bannerColor = Color.white;

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
        chest.RefreshParticipationLabel();
        Debug.Log($"[LootChest] Spawned {(isBossChest ? "BOSS " : "")}Camp {campId} Reward Treasure Chest at {go.transform.position}.");
        return chest;
    }

    /// <summary>
    /// กำหนดรายชื่อผู้เล่นที่มีสิทธิ์เปิดกล่องนี้จากสถิติดาเมจสะสมของแคมป์ (ผู้ที่ทำดาเมจ 0 จะถูกล็อกทันที)
    /// </summary>
    public void ConfigureParticipation(IReadOnlyDictionary<int, ParticipantContribution> contributors)
    {
        _eligibleContributors.Clear();
        _hasParticipationLock = true;
        if (contributors != null)
        {
            foreach (var kv in contributors)
            {
                if (kv.Value.damageShare > 0f)
                    _eligibleContributors[kv.Key] = kv.Value;
            }
        }
        RefreshParticipationLabel();
    }

    /// <summary>
    /// ตรวจสอบว่าผู้เล่นที่ระบุ (PlayerRef) มีส่วนร่วมทำดาเมจและมีสิทธิ์เปิดกล่องนี้หรือไม่
    /// </summary>
    public bool IsPlayerEligible(PlayerRef playerRef, out ParticipantContribution contribution)
    {
        contribution = default;
        if (!_hasParticipationLock)
        {
            contribution = new ParticipantContribution { playerRawEncoded = playerRef.RawEncoded, damageShare = 1f, isTopDps = true };
            return true;
        }
        if (playerRef == PlayerRef.None) return false;
        return _eligibleContributors.TryGetValue(playerRef.RawEncoded, out contribution) && contribution.damageShare > 0f;
    }

    /// <summary>
    /// ตรวจสอบสิทธิ์ของผู้เล่นเครื่องปัจจุบัน (Local Player)
    /// </summary>
    public bool IsLocalPlayerEligible(out ParticipantContribution contribution)
    {
        contribution = default;
        var local = PlayerCombat.Local;
        PlayerRef localRef = ResolvePlayerRef(local);
        return IsPlayerEligible(localRef, out contribution);
    }

    public bool IsLocalPlayerEligible() => IsLocalPlayerEligible(out _);

    private static PlayerRef ResolvePlayerRef(PlayerCombat player)
    {
        if (player != null)
        {
            if (player.Object != null && player.Object.IsValid && player.Object.StateAuthority != PlayerRef.None)
                return player.Object.StateAuthority;
            if (player.Runner != null && player.Runner.IsRunning && player.Runner.LocalPlayer != PlayerRef.None)
                return player.Runner.LocalPlayer;
        }
        return PlayerRef.None;
    }

    /// <summary>
    /// อัปเดตป้าย 3 มิติเหนือกล่องให้สะท้อนสิทธิ์ของผู้เล่นเครื่องนี้ (เปิดได้ vs ถูกล็อก)
    /// </summary>
    private void RefreshParticipationLabel()
    {
        if (_worldLabel == null || IsOpened) return;

        if (IsLocalPlayerEligible(out ParticipantContribution contrib))
        {
            string rankTag = contrib.isTopDps ? "TOP DPS" : $"{contrib.damageShare * 100f:0}% DMG";
            _worldLabel.text = isBossChest
                ? $"BOSS TREASURE CHEST\n<size=72%>[E] Open Personal Loot ({rankTag})</size>"
                : $"TREASURE CHEST\n<size=72%>[E] Open Personal Reward ({rankTag})</size>";
            _worldLabel.color = new Color(1f, 0.86f, 0.24f, 1f);
            if (_chestLight != null) _chestLight.color = isBossChest ? new Color(1f, 0.55f, 0.18f) : new Color(1f, 0.82f, 0.28f);
        }
        else
        {
            _worldLabel.text = isBossChest
                ? "LOCKED BOSS CHEST\n<size=68%>You did not participate in this battle</size>"
                : "LOCKED TREASURE CHEST\n<size=68%>You did not participate in this battle</size>";
            _worldLabel.color = new Color(1f, 0.34f, 0.28f, 1f);
            if (_chestLight != null) _chestLight.color = new Color(0.85f, 0.18f, 0.16f);
        }
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

        // วงแหวนเรืองแสงบนพื้น (ใช้ URP Unlit ถาวรเพื่อไม่ให้เป็นสีชมพูใน Standalone Build)
        var ring = CreatePart(transform, "ChestBeacon", PrimitiveType.Cylinder,
            new Vector3(0f, 0.02f, 0f), new Vector3(1.85f, 0.015f, 1.85f),
            isBossChest ? new Color(1f, 0.45f, 0.12f, 1f) : new Color(1f, 0.82f, 0.22f, 1f),
            unlit: true);
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

    /// <summary>
    /// สร้างชิ้นส่วนของกล่องสมบัติพร้อมผูก URP Material ถาวรผ่าน UrpMaterialHelper (แก้ปัญหาสีชมพูใน Standalone Build)
    /// </summary>
    private static GameObject CreatePart(Transform parent, string partName, PrimitiveType type, Vector3 localPos, Vector3 localScale, Color color, bool unlit = false)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = partName;
        var col = part.GetComponent<Collider>();
        if (col != null) Destroy(col);

        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPos;
        part.transform.localScale = localScale;

        var r = part.GetComponent<Renderer>();
        if (unlit)
            UrpMaterialHelper.ApplyUnlitColor(r, color);
        else
            UrpMaterialHelper.ApplyLitColor(r, color);
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
    /// เปิดกล่องสมบัติสำหรับผู้เล่นที่มีส่วนร่วมทำดาเมจเท่านั้น (บล็อกผู้เล่นที่ทำดาเมจ 0 รวมถึง Host)
    /// สุ่มไอเทมตามสัดส่วนดาเมจและสร้างแบบ Client-Local (LocalPersonalLoot) เฉพาะหน้าจอคนเปิด
    /// </summary>
    public List<ItemData> OpenChest(PlayerCombat opener = null, float? overrideDamageShare = null, bool? overrideIsTopDps = null)
    {
        if (IsOpened) return _droppedItems;
        if (opener == null) opener = PlayerCombat.Local;

        PlayerRef looterRef = ResolvePlayerRef(opener);
        return OpenChestForPlayer(looterRef, overrideDamageShare, overrideIsTopDps);
    }

    /// <summary>
    /// ตรวจสอบสิทธิ์และเปิดกล่องสำหรับ PlayerRef ที่ระบุ (ใช้ได้ทั้งการเล่นจริงและการทดสอบจำลอง Host vs Client)
    /// </summary>
    public List<ItemData> OpenChestForPlayer(PlayerRef looterRef, float? overrideDamageShare = null, bool? overrideIsTopDps = null)
    {
        if (IsOpened) return _droppedItems;

        // ตรวจสอบสิทธิ์การมีส่วนร่วมในการต่อสู้อย่างเคร่งครัด (Zero-Freeloading Lockout)
        if (!IsPlayerEligible(looterRef, out ParticipantContribution contrib))
        {
            if (Time.unscaledTime - _lastLockedToastAt >= 1.2f)
            {
                _lastLockedToastAt = Time.unscaledTime;
                PlayerStatusHud.Instance?.ShowToast(LockedWarningMessage, new Color(1f, 0.30f, 0.25f), 3.0f);
            }
            _bannerText = LockedWarningMessage;
            _bannerColor = new Color(1f, 0.32f, 0.26f, 1f);
            _bannerUntil = Time.unscaledTime + 2.8f;
            Debug.LogWarning($"[LootChest] Access DENIED on Camp {campId} for {looterRef}: {LockedWarningMessage}");
            return _droppedItems;
        }

        IsOpened = true;
        _despawnAtTime = Time.time + Mathf.Max(0.5f, openedDespawnDelay);

        float effectiveShare = overrideDamageShare.HasValue ? Mathf.Clamp01(overrideDamageShare.Value) : contrib.damageShare;
        bool effectiveTopDps = overrideIsTopDps.HasValue ? overrideIsTopDps.Value : contrib.isTopDps;

        _droppedItems.Clear();
        ItemTier highestTier = ItemTier.Normal;
        int count = Mathf.Max(1, dropsPerChest);

        for (int i = 0; i < count; i++)
        {
            // ใช้ตารางดรอปเฉพาะหีบสมบัติ (isChest: true -> แคมป์ 62/25/10/3%, บอส 35/40/18/7% ถ่วงน้ำหนักตาม DPS)
            ItemData rolled = LootDropSystem.RollDamageWeightedLoot(
                isBossChest,
                effectiveShare,
                effectiveTopDps,
                out ItemTier rolledTier,
                out _,
                isChest: true);

            if (rolled == null) continue;

            _droppedItems.Add(rolled);
            if (rolledTier > highestTier) highestTier = rolledTier;

            float angle = (i / (float)count) * Mathf.PI * 2f + 0.4f;
            Vector3 dropOffset = new Vector3(Mathf.Cos(angle) * 1.45f, 0.45f, Mathf.Sin(angle) * 1.45f);
            Vector3 dropPos = transform.position + dropOffset;

            // สร้างไอเทมดรอปแบบ Client-Local แท้จริง (ผู้เล่นอื่นในห้องจะไม่เห็น Ghost Object บนพื้น)
            string sourceLabel = isBossChest ? "Boss Chest" : "Camp Chest";
            LocalPersonalLoot localDrop = LocalPersonalLoot.SpawnLocal(dropPos, rolled, 1, 0, LocalPersonalLoot.DefaultLifetimeSeconds, sourceLabel);

            if (localDrop == null && InventoryManager.Local != null)
            {
                InventoryManager.Local.AddItem(rolled, 1);
            }

            Debug.Log($"[LootChest] {(isBossChest ? "Boss " : "")}Camp {campId} Chest rolled client-local loot [{ItemData.GetTierDisplayName(rolledTier)}] -> {rolled.displayName} for {looterRef} (Share={effectiveShare * 100f:0}%, TopDPS={effectiveTopDps}).");
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
            bool eligible = IsLocalPlayerEligible(out ParticipantContribution contrib);
            var rect = new Rect(Screen.width * 0.5f - 215f, Screen.height * 0.67f, 430f, 34f);
            var prev = GUI.color;
            GUI.color = eligible ? new Color(0.06f, 0.05f, 0.02f, 0.86f) : new Color(0.14f, 0.02f, 0.02f, 0.90f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = eligible ? new Color(1f, 0.85f, 0.25f, 1f) : new Color(1f, 0.36f, 0.30f, 1f);
            GUIStyle style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 15,
                fontStyle = FontStyle.Bold
            };

            string prompt;
            if (!eligible)
            {
                prompt = LockedWarningMessage;
            }
            else
            {
                string rankTag = contrib.isTopDps ? "TOP DPS ★" : $"{contrib.damageShare * 100f:0}% DMG";
                prompt = isBossChest
                    ? $"[E] Open Boss Reward Chest ({rankTag})"
                    : $"[E] Open Camp Reward Chest ({rankTag})";
            }

            GUI.Label(rect, prompt, style);
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
