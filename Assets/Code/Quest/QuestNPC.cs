using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Firebase.Firestore;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// ============================================================================
// 1. โครงสร้างข้อมูลเควสต์ที่ปรับแต่งได้บนตัว NPC ในหน้า Inspector (ครบทั้ง 6 ข้อ)
// ============================================================================
[Serializable]
public class NpcQuestConfig
{
    [Header("1. ข้อมูลพื้นฐาน & การเซฟ Firebase")]
    [Tooltip("รหัสเควสต์ห้ามซ้ำกัน เช่น quest_camp1_01 (ใช้บันทึกลง Firebase Firestore)")]
    public string questId = "quest_01";
    public string questTitle = "กำจัดมอนสเตอร์";
    [Tooltip("ติ๊กถูกหากต้องการให้รับเควสต์นี้ทำซ้ำได้เรื่อยๆ หลังส่งเควสต์แล้ว")]
    public bool isRepeatable = false;

    [Header("2. มอนสเตอร์เป้าหมาย & จำนวนที่ต้องสู้")]
    [Tooltip("ลาก Prefab ของ EnemyNPC ที่ต้องการให้ล่ามาใส่ (หรือปล่อยว่างเพื่อใช้ชื่อด้านล่าง / นับทุกตัว)")]
    public EnemyNPC targetMonsterPrefab;
    [Tooltip("คำค้นหาชื่อมอนสเตอร์ (เช่น Camp 1, Golem) หากไม่ได้ลาก Prefab ใส่ — ปล่อยว่างทั้งคู่ = นับทุกตัว")]
    public string targetMonsterNameContains = "";
    [Min(1)]
    public int requiredKillCount = 3;

    [Header("3. ไอเทมรางวัล & จำนวนที่ได้รับ (เข้ากระเป๋าทันที)")]
    [Tooltip("ลากไฟล์ ItemData จากโฟลเดอร์ Resources/Items มาใส่")]
    public ItemData rewardItem;
    [Min(1)]
    public int rewardItemAmount = 1;
    [Min(0)]
    public int rewardExp = 100;

    [Header("4. อนิเมชั่นของ NPC หลังกดรับ/ส่งเควสต์")]
    [Tooltip("ชื่อ Parameter (Trigger) ใน Animator ของ NPC ที่จะเล่นตอนกดรับเควสต์")]
    public string acceptAnimationTrigger = "AcceptQuest";
    [Tooltip("ชื่อ Parameter (Trigger) ใน Animator ของ NPC ตอนส่งเควสต์สำเร็จ (ปล่อยว่างได้)")]
    public string completeAnimationTrigger = "";

    [Header("5. คำพูด (Dialog) ของ NPC แยกตามช่วงเวลา")]
    [TextArea(2, 4)]
    public string[] offerDialogLines =
    {
        "เจ้ามาได้จังหวะพอดี! แถวนี้มีมอนสเตอร์อาละวาดหนักมาก",
        "ช่วยไปจัดการพวกมันให้ข้าหน่อย แล้วกลับมาเอาของรางวัลได้เลย"
    };

    [TextArea(2, 4)]
    public string[] inProgressDialogLines =
    {
        "เจ้ายังจัดการพวกมันไม่ครบตามจำนวนเลย รีบไปจัดการให้เรียบร้อยก่อนนะ!"
    };

    [TextArea(2, 4)]
    public string[] readyToTurnInDialogLines =
    {
        "ยอดเยี่ยมมาก! เจ้าจัดการพวกมันได้หมดจริงๆ",
        "นี่คือของรางวัลที่ข้าสัญญาไว้ รับไปได้เลย!"
    };

    [TextArea(2, 4)]
    public string[] completedDialogLines =
    {
        "ขอบคุณที่ช่วยเหลือหมู่บ้านของเราอีกครั้งนะนักรบ!"
    };

    public bool MatchesEnemy(EnemyNPC enemy)
    {
        if (enemy == null) return false;

        string cleanSceneName = enemy.gameObject.name.Replace("(Clone)", "").Trim();
        string lockName = (enemy.Object != null && enemy.Object.IsValid) ? (enemy.LockName ?? "") : "";
        // ดึงชื่อสายพันธุ์จริงจาก MonsterAnimatorBridge (กรณีใช้มอนสเตอร์จากระบบ MonsterSystem_Manager)
        var bridge = enemy.GetComponent<MonsterAnimatorBridge>();
        string speciesName = (bridge != null && bridge.Config != null && !string.IsNullOrWhiteSpace(bridge.Config.monsterName))
            ? bridge.Config.monsterName.Trim()
            : "";

        if (targetMonsterPrefab != null)
        {
            string prefabName = targetMonsterPrefab.gameObject.name.Trim();
            // ตัดคำนำหน้า Enemy_ / Boss_ ออกเพื่อเทียบกับชื่อจริงหลังเกิดของ MonsterEnemyNPC (เช่น Enemy_Orc -> Orc)
            string strippedPrefab = prefabName;
            if (strippedPrefab.StartsWith("Enemy_", StringComparison.OrdinalIgnoreCase))
                strippedPrefab = strippedPrefab.Substring(6);
            else if (strippedPrefab.StartsWith("Boss_", StringComparison.OrdinalIgnoreCase))
                strippedPrefab = strippedPrefab.Substring(5);

            if (cleanSceneName.StartsWith(prefabName, StringComparison.OrdinalIgnoreCase) ||
                cleanSceneName.StartsWith(strippedPrefab, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(speciesName) && (speciesName.Equals(prefabName, StringComparison.OrdinalIgnoreCase) || speciesName.Equals(strippedPrefab, StringComparison.OrdinalIgnoreCase))) ||
                (!string.IsNullOrEmpty(lockName) && (lockName.IndexOf(prefabName, StringComparison.OrdinalIgnoreCase) >= 0 || lockName.IndexOf(strippedPrefab, StringComparison.OrdinalIgnoreCase) >= 0)))
            {
                return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(targetMonsterNameContains))
        {
            string filter = targetMonsterNameContains.Trim();
            return cleanSceneName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (!string.IsNullOrEmpty(speciesName) && speciesName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   lockName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        return targetMonsterPrefab == null;
    }

    public string GetTargetDisplayName()
    {
        if (!string.IsNullOrWhiteSpace(targetMonsterNameContains))
            return targetMonsterNameContains.Trim();
        if (targetMonsterPrefab != null)
            return targetMonsterPrefab.gameObject.name.Trim();
        return "Monster";
    }
}

// ============================================================================
// 2. คอมโพเนนต์หลักสำหรับแปะบนตัว NPC ในฉาก (QuestNPC)
// ============================================================================
public class QuestNPC : MonoBehaviour, IWorldInteractable
{
    public InteractionChannel Channel => InteractionChannel.NpcTalk;
    public int PriorityBias => 0;
    public float InteractionRange => interactRadius;
    public Vector3 InteractionWorldPosition => transform.position;
    public bool CanInteractNow(PlayerCombat player) => isActiveAndEnabled && player != null && player.IsSpawned && !player.IsDead;
    public void ExecuteInteraction(PlayerCombat player) => BeginInteraction();

    public string GetPromptLabel(PlayerCombat player)
    {
        if (quest == null) return $"Talk to {npcName}";
        var qm = QuestSystemManager.EnsureExists();
        var prog = qm.GetProgress(quest);
        bool hasOtherActive = prog.status == QuestStatus.NotStarted && qm.HasActiveQuest(out _);
        return prog.status switch
        {
            QuestStatus.NotStarted => hasOtherActive ? $"Talk to {npcName} (Active Quest)" : $"Accept Quest  •  {npcName}",
            QuestStatus.InProgress => $"Talk to {npcName} (In Progress)",
            QuestStatus.ReadyToTurnIn => $"Turn In Quest  •  {npcName} (Ready!)",
            _ => $"Talk to {npcName}"
        };
    }

    public Color GetPromptAccentColor(PlayerCombat player)
    {
        return new Color(0.32f, 0.96f, 0.72f, 1f);
    }

    public static readonly List<QuestNPC> All = new List<QuestNPC>();

    [Header("ข้อมูลตัวละคร NPC")]
    public string npcName = "Village Elder";

    [Header("ตั้งค่าเควสต์ของ NPC ตัวนี้ (ปรับแต่งได้ครบทุกส่วน)")]
    public NpcQuestConfig quest = new NpcQuestConfig();

    [Header("ระยะกดคุย & อนิเมชั่น")]
    [Min(1f)] public float interactRadius = 3.2f;
    // แยกปุ่มคุยกับ NPC เป็น [G] เพื่อไม่ให้ชนกับปุ่มเก็บของ [E] หรือปุ่มสกิลในอนาคต [Q]/[E]
    public Key interactKey = InteractionManager.NpcTalkKey;
    [Tooltip("Animator ของตัว NPC (จะค้นหาให้อัตโนมัติถ้าปล่อยว่าง)")]
    public Animator npcAnimator;
    [Tooltip("หาก NPC มี NetworkObject ให้ลาก NetworkMecanimAnimator มาใส่เพื่อให้เพื่อนเห็นท่าทางพร้อมกัน (ไม่บังคับ)")]
    public NetworkMecanimAnimator networkAnimator;
    public bool showOverheadMarker = true;
    public float overheadMarkerHeight = 2.35f;

    public bool IsPlayerInRange { get; private set; }
    public float DistanceToLocalPlayer { get; private set; } = float.MaxValue;

    private TextMeshPro _overheadMarker;

    private void Awake()
    {
        if (npcAnimator == null) npcAnimator = GetComponentInChildren<Animator>();
        if (networkAnimator == null) networkAnimator = GetComponent<NetworkMecanimAnimator>();
        // อัปเกรด NPC ในฉากที่ยังค้างค่าปุ่มเก่า E ให้ย้ายมาใช้ปุ่ม G (ผ่าน InteractionManager กลาง) อัตโนมัติ
        if (interactKey == Key.E || interactKey == Key.Q)
            interactKey = InteractionManager.NpcTalkKey;
    }

    private void Start()
    {
        var qm = QuestSystemManager.EnsureExists();
        if (quest != null)
            qm.RegisterQuest(quest);

        if (showOverheadMarker)
            BuildOverheadMarker();
    }

    private void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
        InteractionManager.Register(this);
        if (QuestSystemManager.Instance != null)
            QuestSystemManager.Instance.QuestStateChanged += RefreshOverheadMarker;
    }

    private void OnDisable()
    {
        All.Remove(this);
        InteractionManager.Unregister(this);
        if (QuestSystemManager.Instance != null)
            QuestSystemManager.Instance.QuestStateChanged -= RefreshOverheadMarker;
    }

    private void Update()
    {
        RefreshOverheadMarker();

        var localPlayer = PlayerCombat.Local;
        if (localPlayer == null || !localPlayer.IsSpawned || localPlayer.IsDead)
        {
            IsPlayerInRange = false;
            DistanceToLocalPlayer = float.MaxValue;
            return;
        }

        Vector3 a = transform.position;
        Vector3 b = localPlayer.transform.position;
        a.y = 0f;
        b.y = 0f;
        DistanceToLocalPlayer = Vector3.Distance(a, b);
        IsPlayerInRange = DistanceToLocalPlayer <= interactRadius;

        if (_overheadMarker != null && Camera.main != null)
            _overheadMarker.transform.rotation = Camera.main.transform.rotation;

        // การรับปุ่มคุยกับ NPC ([G]) ถูกรวมศูนย์ไว้ที่ InteractionManager แล้ว เพื่อป้องกันการทับซ้อนกับ [E] และสกิล [Q]/[E]
    }

    public static QuestNPC GetClosestInteractableNPC()
    {
        QuestNPC best = null;
        float bestDist = float.MaxValue;

        for (int i = 0; i < All.Count; i++)
        {
            var npc = All[i];
            if (npc == null || !npc.isActiveAndEnabled || !npc.IsPlayerInRange) continue;
            if (npc.DistanceToLocalPlayer < bestDist)
            {
                bestDist = npc.DistanceToLocalPlayer;
                best = npc;
            }
        }
        return best;
    }

    public void BeginInteraction()
    {
        if (quest == null || string.IsNullOrWhiteSpace(quest.questId))
        {
            Debug.LogWarning($"[QuestNPC] '{npcName}' ยังไม่ได้ตั้งค่า Quest Id!");
            return;
        }
        QuestDialogAndTrackerUI.EnsureExists().OpenDialog(this, quest);
    }

    public void PlayNpcAnimation(string triggerName)
    {
        if (string.IsNullOrWhiteSpace(triggerName)) return;
        string cleanTrigger = triggerName.Trim();

        var netObj = GetComponent<NetworkObject>();
        if (networkAnimator != null && netObj != null && netObj.IsValid && netObj.HasStateAuthority)
        {
            networkAnimator.SetTrigger(cleanTrigger);
            return;
        }

        if (npcAnimator != null && npcAnimator.isActiveAndEnabled)
        {
            npcAnimator.ResetTrigger(cleanTrigger);
            npcAnimator.SetTrigger(cleanTrigger);
        }
    }

    private void BuildOverheadMarker()
    {
        var go = new GameObject("QuestOverheadMarker");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, overheadMarkerHeight, 0f);

        _overheadMarker = go.AddComponent<TextMeshPro>();
        _overheadMarker.fontSize = 4.2f;
        _overheadMarker.fontStyle = FontStyles.Bold;
        _overheadMarker.alignment = TextAlignmentOptions.Center;
        _overheadMarker.textWrappingMode = TextWrappingModes.NoWrap;
        RefreshOverheadMarker();
    }

    private void RefreshOverheadMarker()
    {
        if (_overheadMarker == null) return;
        if (quest == null || QuestSystemManager.Instance == null)
        {
            _overheadMarker.text = "";
            return;
        }

        var prog = QuestSystemManager.Instance.GetProgress(quest);
        switch (prog.status)
        {
            case QuestStatus.NotStarted:
                // ถ้ากำลังติดเควสต์อื่นอยู่ ให้เครื่องหมาย ! เป็นสีเทาเพื่อบอกว่ายังรับซ้อนไม่ได้
                bool busyWithOther = QuestSystemManager.Instance.HasActiveQuest(out _);
                string bangColor = busyWithOther ? "#8892A0" : "#FFD166";
                _overheadMarker.text = $"<color={bangColor}>!</color>\n<size=45%>{npcName}</size>";
                break;
            case QuestStatus.InProgress:
                _overheadMarker.text = $"<color=#A0AAB8>?</color>\n<size=45%>{npcName}</size>";
                break;
            case QuestStatus.ReadyToTurnIn:
                _overheadMarker.text = $"<color=#38E59E>?</color>\n<size=45%>{npcName}</size>";
                break;
            case QuestStatus.Completed:
                _overheadMarker.text = $"<size=45%>{npcName}</size>";
                break;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.9f, 0.65f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, interactRadius);
    }
}

// ============================================================================
// 3. ระบบจัดการเควสต์ + นับมอนสเตอร์ + ส่งไอเทมเข้ากระเป๋า + Firebase Firestore
// ============================================================================
public enum QuestStatus
{
    NotStarted = 0,
    InProgress = 1,
    ReadyToTurnIn = 2,
    Completed = 3
}

[Serializable]
public class QuestProgress
{
    public string questId;
    public QuestStatus status = QuestStatus.NotStarted;
    public int currentKills = 0;
    public int completedCount = 0;
}

[FirestoreData]
public class FirestoreQuestRecord
{
    [FirestoreProperty] public string questId { get; set; }
    [FirestoreProperty] public int status { get; set; }
    [FirestoreProperty] public int currentKills { get; set; }
    [FirestoreProperty] public int completedCount { get; set; }
    [FirestoreProperty] public long updatedAtUnix { get; set; }
}

public class QuestSystemManager : MonoBehaviour
{
    public static QuestSystemManager Instance { get; private set; }

    public event Action QuestStateChanged;

    private readonly Dictionary<string, QuestProgress> _progressById = new Dictionary<string, QuestProgress>();
    private readonly Dictionary<string, NpcQuestConfig> _questConfigById = new Dictionary<string, NpcQuestConfig>();
    private readonly HashSet<EnemyNPC> _subscribedEnemies = new HashSet<EnemyNPC>();

    private string _loadedForUuid;
    private bool _isLoadingFirestore;

    public static QuestSystemManager EnsureExists()
    {
        if (Instance != null) return Instance;
        var existing = FindAnyObjectByType<QuestSystemManager>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }
        var go = new GameObject("QuestSystemManager");
        return go.AddComponent<QuestSystemManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        QuestDialogAndTrackerUI.EnsureExists();
    }

    private void OnDestroy()
    {
        foreach (var enemy in _subscribedEnemies)
        {
            if (enemy != null) enemy.Died -= OnEnemyDied;
        }
        _subscribedEnemies.Clear();
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        for (int i = 0; i < EnemyNPC.All.Count; i++)
        {
            var enemy = EnemyNPC.All[i];
            if (enemy != null && !_subscribedEnemies.Contains(enemy))
            {
                enemy.Died += OnEnemyDied;
                _subscribedEnemies.Add(enemy);
            }
        }

        if (!_isLoadingFirestore && GoogleAPI.instance != null && !string.IsNullOrEmpty(GoogleAPI.instance.uuid))
        {
            if (_loadedForUuid != GoogleAPI.instance.uuid)
            {
                _ = LoadFromFirestoreAsync(GoogleAPI.instance.uuid);
            }
        }
    }

    public void RegisterQuest(NpcQuestConfig config)
    {
        if (config == null || string.IsNullOrWhiteSpace(config.questId)) return;
        _questConfigById[config.questId] = config;

        if (!_progressById.ContainsKey(config.questId))
        {
            _progressById[config.questId] = new QuestProgress
            {
                questId = config.questId,
                status = QuestStatus.NotStarted,
                currentKills = 0,
                completedCount = 0
            };
        }
        QuestStateChanged?.Invoke();
    }

    public QuestProgress GetProgress(NpcQuestConfig config)
    {
        if (config == null || string.IsNullOrWhiteSpace(config.questId))
            return new QuestProgress();

        RegisterQuest(config);
        var prog = _progressById[config.questId];

        if (prog.status == QuestStatus.Completed && config.isRepeatable)
        {
            prog.status = QuestStatus.NotStarted;
            prog.currentKills = 0;
            _ = SaveQuestToFirestoreAsync(prog);
        }

        return prog;
    }

    /// <summary>ตรวจสอบว่าผู้เล่นมีเควสต์ที่กำลังทำหรือรอส่งค้างอยู่หรือไม่ (จำกัดได้สูงสุด 1 เควสต์)</summary>
    public bool HasActiveQuest(out NpcQuestConfig activeConfig)
    {
        activeConfig = null;
        foreach (var kv in _progressById)
        {
            var prog = kv.Value;
            if (prog.status == QuestStatus.InProgress || prog.status == QuestStatus.ReadyToTurnIn)
            {
                _questConfigById.TryGetValue(kv.Key, out activeConfig);
                return true;
            }
        }
        return false;
    }

    public bool TryAcceptQuest(NpcQuestConfig config, out string failReason)
    {
        failReason = "";
        if (config == null)
        {
            failReason = "ไม่พบข้อมูลเควสต์";
            return false;
        }

        // บังคับรับเควสต์ได้ทีละ 1 เควสต์เท่านั้น
        if (HasActiveQuest(out var existingQuest))
        {
            string existingTitle = existingQuest != null ? existingQuest.questTitle : "ภารกิจอื่น";
            failReason = $"คุณรับเควสต์ได้ทีละ 1 เควสต์ (กำลังทำ: {existingTitle})";
            return false;
        }

        var prog = GetProgress(config);
        if (prog.status != QuestStatus.NotStarted && !(prog.status == QuestStatus.Completed && config.isRepeatable))
        {
            failReason = "ไม่สามารถรับเควสต์นี้ได้ในขณะนี้";
            return false;
        }

        prog.status = QuestStatus.InProgress;
        prog.currentKills = 0;

        QuestStateChanged?.Invoke();
        _ = SaveQuestToFirestoreAsync(prog);
        return true;
    }

    public bool TryTurnInQuest(NpcQuestConfig config, out string failReason)
    {
        failReason = "";
        if (config == null)
        {
            failReason = "Invalid quest.";
            return false;
        }

        var prog = GetProgress(config);
        if (prog.status != QuestStatus.ReadyToTurnIn)
        {
            failReason = "Quest is not ready to turn in.";
            return false;
        }

        if (config.rewardItem != null && config.rewardItemAmount > 0)
        {
            var inv = InventoryManager.Local;
            if (inv == null || !inv.IsActivated)
            {
                failReason = "Inventory is not ready yet.";
                return false;
            }

            if (!inv.CanAdd(config.rewardItem, config.rewardItemAmount))
            {
                failReason = "กระเป๋าเต็ม! กรุณาเคลียร์ช่องว่างก่อนรับรางวัล";
                return false;
            }

            int leftover = inv.AddItem(config.rewardItem, config.rewardItemAmount);
            if (leftover > 0)
            {
                failReason = "กระเป๋าเต็ม! ไม่สามารถรับไอเทมได้ครบ";
                return false;
            }
        }

        if (config.rewardExp > 0 && PlayerCombat.Local != null && PlayerCombat.Local.IsSpawned)
        {
            PlayerCombat.Local.AddExperience(config.rewardExp);
        }

        prog.completedCount++;
        prog.status = config.isRepeatable ? QuestStatus.NotStarted : QuestStatus.Completed;
        prog.currentKills = 0;

        PlayerSaveManager.Instance?.SaveNow("QuestRewardClaimed");
        QuestStateChanged?.Invoke();
        _ = SaveQuestToFirestoreAsync(prog);
        return true;
    }

    private void OnEnemyDied(EnemyNPC enemy)
    {
        _subscribedEnemies.Remove(enemy);
        if (enemy == null) return;

        var localPlayer = PlayerCombat.Local;
        if (localPlayer == null || !localPlayer.IsSpawned || localPlayer.IsDead) return;

        // เช็คว่าผู้เล่นต้องอยู่ใกล้จุดมอนตาย (<= 30m) หรือช่วยทำดาเมจใส่มอนตัวนี้จริง (กันคนยืน AFK ใน Safe Zone ผ่านเควสต์ฟรี)
        var runner = localPlayer.Runner;
        bool dealtDamage = runner != null && enemy.GetPlayerDamageShare(runner.LocalPlayer) > 0f;
        float dist = Vector3.Distance(localPlayer.transform.position, enemy.transform.position);
        if (!dealtDamage && dist > 30f) return;

        bool anyUpdated = false;

        foreach (var kv in _progressById)
        {
            var prog = kv.Value;
            if (prog.status != QuestStatus.InProgress) continue;
            if (!_questConfigById.TryGetValue(kv.Key, out var config) || config == null) continue;

            if (config.MatchesEnemy(enemy))
            {
                prog.currentKills = Mathf.Min(config.requiredKillCount, prog.currentKills + 1);
                if (prog.currentKills >= config.requiredKillCount)
                {
                    prog.status = QuestStatus.ReadyToTurnIn;
                }

                anyUpdated = true;
                _ = SaveQuestToFirestoreAsync(prog);
            }
        }

        if (anyUpdated)
            QuestStateChanged?.Invoke();
    }

    public List<(NpcQuestConfig config, QuestProgress progress)> GetActiveTrackedQuests()
    {
        var list = new List<(NpcQuestConfig, QuestProgress)>();
        foreach (var kv in _progressById)
        {
            var prog = kv.Value;
            if (prog.status == QuestStatus.InProgress || prog.status == QuestStatus.ReadyToTurnIn)
            {
                if (_questConfigById.TryGetValue(kv.Key, out var config) && config != null)
                    list.Add((config, prog));
            }
        }
        return list;
    }

    private static FirebaseFirestore GetFirestore()
    {
        var db = FirebaseFirestore.DefaultInstance;
        db.Settings.PersistenceEnabled = false;
        return db;
    }

    public async Task LoadFromFirestoreAsync(string uid)
    {
        if (string.IsNullOrEmpty(uid)) return;
        _isLoadingFirestore = true;
        try
        {
            var db = GetFirestore();
            var snapshot = await db.Collection("players").Document(uid).Collection("quests").GetSnapshotAsync();
            foreach (var doc in snapshot.Documents)
            {
                if (!doc.Exists) continue;
                var rec = doc.ConvertTo<FirestoreQuestRecord>();
                if (rec == null || string.IsNullOrEmpty(rec.questId)) continue;

                var loadedStatus = (QuestStatus)Mathf.Clamp(rec.status, 0, 3);
                int loadedKills = Mathf.Max(0, rec.currentKills);

                if (_questConfigById.TryGetValue(rec.questId, out var cfg) && cfg != null &&
                    cfg.isRepeatable && loadedStatus == QuestStatus.Completed)
                {
                    loadedStatus = QuestStatus.NotStarted;
                    loadedKills = 0;
                }

                _progressById[rec.questId] = new QuestProgress
                {
                    questId = rec.questId,
                    status = loadedStatus,
                    currentKills = loadedKills,
                    completedCount = Mathf.Max(0, rec.completedCount)
                };
            }

            _loadedForUuid = uid;
            QuestStateChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[Quest] Firestore load skipped: " + ex.Message);
            _loadedForUuid = uid;
        }
        finally
        {
            _isLoadingFirestore = false;
        }
    }

    public async Task SaveQuestToFirestoreAsync(QuestProgress prog)
    {
        if (prog == null || string.IsNullOrEmpty(prog.questId)) return;
        if (GoogleAPI.instance == null || string.IsNullOrEmpty(GoogleAPI.instance.uuid)) return;

        try
        {
            var db = GetFirestore();
            var docRef = db.Collection("players")
                .Document(GoogleAPI.instance.uuid)
                .Collection("quests")
                .Document(prog.questId);

            var record = new FirestoreQuestRecord
            {
                questId = prog.questId,
                status = (int)prog.status,
                currentKills = prog.currentKills,
                completedCount = prog.completedCount,
                updatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            await docRef.SetAsync(record, SetOptions.MergeAll);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Quest] Failed to save quest '{prog.questId}' to Firestore: {ex.Message}");
        }
    }
}

// ============================================================================
// 4. ระบบ UI บทสนทนา NPC, ป้าย [E] และแถบติดตามเควสต์ด้านซ้ายจอ
// ============================================================================
public class QuestDialogAndTrackerUI : MonoBehaviour
{
    public static QuestDialogAndTrackerUI Instance { get; private set; }

    public bool IsDialogOpen => _dialogPanel != null && _dialogPanel.activeSelf;

    private Font _font;
    private GameObject _promptRoot;
    private Text _promptText;

    private GameObject _dialogPanel;
    private Text _npcNameText;
    private Text _questTitleText;
    private Text _dialogBodyText;
    private GameObject _rewardBox;
    private Image _rewardIcon;
    private Text _rewardLabelText;
    private Text _errorNoticeText;
    private Button _primaryBtn;
    private Image _primaryBtnBg;
    private Text _primaryBtnText;
    private Button _closeBtn;
    private Text _closeBtnText;

    private GameObject _trackerRoot;
    private Text _trackerBodyText;

    private QuestNPC _activeNpc;
    private NpcQuestConfig _activeQuest;
    private QuestStatus _dialogStageStatus;
    private string[] _currentLines;
    private int _lineIndex;
    private float _openedAtTime;

    public static QuestDialogAndTrackerUI EnsureExists()
    {
        if (Instance != null) return Instance;
        var existing = FindAnyObjectByType<QuestDialogAndTrackerUI>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }
        var go = new GameObject("QuestDialogAndTrackerUI");
        return go.AddComponent<QuestDialogAndTrackerUI>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        _font = UITheme.GetFont();
        BuildAllUI();
    }

    private void OnEnable()
    {
        if (QuestSystemManager.Instance != null)
            QuestSystemManager.Instance.QuestStateChanged += RefreshQuestTracker;
    }

    private void OnDisable()
    {
        if (QuestSystemManager.Instance != null)
            QuestSystemManager.Instance.QuestStateChanged -= RefreshQuestTracker;
    }

    private void OnDestroy()
    {
        if (IsDialogOpen)
            GameplayInput.PopBlocker("QuestDialog");
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        UpdateInteractionPrompt();

        if (!IsDialogOpen) return;
        if (Time.unscaledTime - _openedAtTime < 0.15f) return;

        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.escapeKey.wasPressedThisFrame)
                CloseDialog();
            else if (kb[InteractionManager.NpcTalkKey].wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)
                OnClickPrimaryAction();
        }
    }

    public void OpenDialog(QuestNPC npc, NpcQuestConfig quest)
    {
        if (npc == null || quest == null) return;

        _activeNpc = npc;
        _activeQuest = quest;
        _openedAtTime = Time.unscaledTime;

        var prog = QuestSystemManager.EnsureExists().GetProgress(quest);
        _dialogStageStatus = prog.status;
        _lineIndex = 0;

        switch (_dialogStageStatus)
        {
            case QuestStatus.NotStarted:
                _currentLines = (quest.offerDialogLines != null && quest.offerDialogLines.Length > 0)
                    ? quest.offerDialogLines
                    : new[] { $"ช่วยข้ากำจัด {quest.GetTargetDisplayName()} จำนวน {quest.requiredKillCount} ตัวที!" };
                break;

            case QuestStatus.InProgress:
                _currentLines = (quest.inProgressDialogLines != null && quest.inProgressDialogLines.Length > 0)
                    ? quest.inProgressDialogLines
                    : new[] { $"ความคืบหน้า: {prog.currentKills}/{quest.requiredKillCount} ตัว รีบไปจัดการให้ครบนะ!" };
                break;

            case QuestStatus.ReadyToTurnIn:
                _currentLines = (quest.readyToTurnInDialogLines != null && quest.readyToTurnInDialogLines.Length > 0)
                    ? quest.readyToTurnInDialogLines
                    : new[] { "ยอดเยี่ยมมาก! นี่คือรางวัลของเจ้า" };
                break;

            default:
                _currentLines = (quest.completedDialogLines != null && quest.completedDialogLines.Length > 0)
                    ? quest.completedDialogLines
                    : new[] { "ขอบคุณที่ช่วยเหลือ!" };
                break;
        }

        if (_errorNoticeText) _errorNoticeText.text = "";

        if (!IsDialogOpen)
        {
            _dialogPanel.SetActive(true);
            GameplayInput.PushBlocker("QuestDialog");
        }

        RefreshDialogView();
    }

    public void CloseDialog()
    {
        if (!IsDialogOpen) return;
        _dialogPanel.SetActive(false);
        _activeNpc = null;
        _activeQuest = null;
        GameplayInput.PopBlocker("QuestDialog");
    }

    private void RefreshDialogView()
    {
        if (_activeNpc == null || _activeQuest == null) return;

        _npcNameText.text = _activeNpc.npcName;
        _questTitleText.text = $"[Quest] {_activeQuest.questTitle}  ({_activeQuest.GetTargetDisplayName()} x{_activeQuest.requiredKillCount})";

        int clampedIdx = Mathf.Clamp(_lineIndex, 0, _currentLines.Length - 1);
        _dialogBodyText.text = _currentLines[clampedIdx];

        bool isLastLine = clampedIdx >= _currentLines.Length - 1;

        bool showReward = _activeQuest.rewardItem != null &&
                          (_dialogStageStatus == QuestStatus.NotStarted || _dialogStageStatus == QuestStatus.ReadyToTurnIn);
        _rewardBox.SetActive(showReward);
        if (showReward)
        {
            _rewardIcon.sprite = HudIcons.GetForItem(_activeQuest.rewardItem);
            string expPart = _activeQuest.rewardExp > 0 ? $"  |  +{_activeQuest.rewardExp} EXP" : "";
            _rewardLabelText.text = $"รางวัล: {_activeQuest.rewardItem.displayName} x{_activeQuest.rewardItemAmount}{expPart}";
        }

        if (!isLastLine)
        {
            _primaryBtn.interactable = true;
            _primaryBtnText.text = "ถัดไป [G]";
            _primaryBtnBg.color = UITheme.BrushedGoldDark;
            _closeBtnText.text = "ปิด [Esc]";
        }
        else
        {
            switch (_dialogStageStatus)
            {
                case QuestStatus.NotStarted:
                    if (QuestSystemManager.EnsureExists().HasActiveQuest(out var activeQuest))
                    {
                        string activeName = activeQuest != null ? activeQuest.questTitle : "ภารกิจอื่น";
                        _primaryBtn.interactable = false;
                        _primaryBtnText.text = "ติดเควสต์อื่นอยู่";
                        _primaryBtnBg.color = UITheme.ObsidianSlotEmpty;
                        _closeBtnText.text = "ปิด [Esc]";
                        if (_errorNoticeText)
                            _errorNoticeText.text = $"รับได้ทีละ 1 เควสต์ (กำลังทำ: {activeName})";
                    }
                    else
                    {
                        _primaryBtn.interactable = true;
                        _primaryBtnText.text = "รับเควสต์ [G]";
                        _primaryBtnBg.color = UITheme.JadeButton;
                        _closeBtnText.text = "ปฏิเสธ [Esc]";
                        if (_errorNoticeText) _errorNoticeText.text = "";
                    }
                    break;

                case QuestStatus.ReadyToTurnIn:
                    _primaryBtn.interactable = true;
                    _primaryBtnText.text = "รับรางวัล [G]";
                    _primaryBtnBg.color = UITheme.JadeButton;
                    _closeBtnText.text = "ไว้ทีหลัง [Esc]";
                    break;

                default:
                    _primaryBtn.interactable = true;
                    _primaryBtnText.text = "ตกลง [G]";
                    _primaryBtnBg.color = UITheme.BrushedGoldDark;
                    _closeBtnText.text = "ปิด [Esc]";
                    break;
            }
        }
    }

    private void OnClickPrimaryAction()
    {
        if (_activeQuest == null || _activeNpc == null) return;

        if (_lineIndex < _currentLines.Length - 1)
        {
            _lineIndex++;
            RefreshDialogView();
            return;
        }

        if (_dialogStageStatus == QuestStatus.NotStarted)
        {
            if (QuestSystemManager.EnsureExists().TryAcceptQuest(_activeQuest, out string err))
            {
                _activeNpc.PlayNpcAnimation(_activeQuest.acceptAnimationTrigger);
                CloseDialog();
            }
            else
            {
                if (_errorNoticeText) _errorNoticeText.text = err;
            }
        }
        else if (_dialogStageStatus == QuestStatus.ReadyToTurnIn)
        {
            if (QuestSystemManager.EnsureExists().TryTurnInQuest(_activeQuest, out string err))
            {
                _activeNpc.PlayNpcAnimation(_activeQuest.completeAnimationTrigger);
                CloseDialog();
            }
            else
            {
                if (_errorNoticeText) _errorNoticeText.text = err;
            }
        }
        else
        {
            CloseDialog();
        }
    }

    private void UpdateInteractionPrompt()
    {
        if (_promptRoot == null) return;

        // หากมี InteractionPromptUI กลาง (uGUI Canvas Prefab) ทำงานอยู่แล้ว ให้ซ่อนป้ายซ้ำของเควสต์เพื่อไม่ให้ป้ายซ้อนกัน
        if (IsDialogOpen || GameplayInput.IsBlocked || InteractionPromptUI.Instance != null)
        {
            if (_promptRoot.activeSelf) _promptRoot.SetActive(false);
            return;
        }

        var closestNpc = QuestNPC.GetClosestInteractableNPC();
        bool show = closestNpc != null;
        if (_promptRoot.activeSelf != show)
            _promptRoot.SetActive(show);

        if (show && closestNpc.quest != null)
        {
            var qm = QuestSystemManager.EnsureExists();
            var prog = qm.GetProgress(closestNpc.quest);
            bool hasOtherActive = prog.status == QuestStatus.NotStarted && qm.HasActiveQuest(out _);

            string actionHint = prog.status switch
            {
                QuestStatus.NotStarted => hasOtherActive
                    ? $"คุยกับ {closestNpc.npcName} (ติดเควสต์อื่นอยู่)"
                    : $"รับเควสต์จาก {closestNpc.npcName}",
                QuestStatus.InProgress => $"คุยกับ {closestNpc.npcName}",
                QuestStatus.ReadyToTurnIn => $"ส่งเควสต์กับ {closestNpc.npcName} (สำเร็จแล้ว!)",
                _ => $"คุยกับ {closestNpc.npcName}"
            };
            _promptText.text = $"[G]  {actionHint}";
        }
    }

    private void RefreshQuestTracker()
    {
        if (_trackerRoot == null || QuestSystemManager.Instance == null) return;

        var activeList = QuestSystemManager.Instance.GetActiveTrackedQuests();
        if (activeList.Count == 0)
        {
            _trackerRoot.SetActive(false);
            return;
        }

        _trackerRoot.SetActive(true);
        var sb = new StringBuilder();
        for (int i = 0; i < activeList.Count; i++)
        {
            var (config, prog) = activeList[i];
            if (i > 0) sb.AppendLine();

            sb.AppendLine($"<color=#FFD166>◆ {config.questTitle}</color>");
            if (prog.status == QuestStatus.ReadyToTurnIn)
            {
                sb.AppendLine($"  <color=#38E59E>✔ กำจัดครบแล้ว ({prog.currentKills}/{config.requiredKillCount}) - กลับไปส่งเควสต์กับ NPC!</color>");
            }
            else
            {
                sb.AppendLine($"  • กำจัด {config.GetTargetDisplayName()}: {prog.currentKills} / {config.requiredKillCount}");
            }
        }

        _trackerBodyText.text = sb.ToString().TrimEnd();
    }

    private void BuildAllUI()
    {
        var canvasGo = new GameObject("QuestUICanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        BuildPromptUI(canvasGo.transform);
        BuildDialogUI(canvasGo.transform);
        BuildTrackerUI(canvasGo.transform);
    }

    private void BuildPromptUI(Transform parent)
    {
        var border = MakeImage(parent, "NpcPromptBorder", UITheme.BrushedGold);
        UITheme.ApplySlicedStyle(border, UITheme.BrushedGold);
        UITheme.AddShadow(border.gameObject, 2f);
        _promptRoot = border.gameObject;

        var rt = border.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 125f);
        rt.sizeDelta = new Vector2(400f, 44f);

        var inner = MakeImage(rt, "NpcPromptInner", UITheme.ObsidianDeep);
        UITheme.ApplySlicedStyle(inner, UITheme.ObsidianDeep);
        Stretch(inner.rectTransform, 2f);

        _promptText = MakeText(inner.transform, "PromptText", "[G] Talk", 18, FontStyle.Bold, TextAnchor.MiddleCenter, UITheme.TextPrimary);
        Stretch(_promptText.rectTransform, 4f);

        _promptRoot.SetActive(false);
    }

    private void BuildDialogUI(Transform parent)
    {
        var border = MakeImage(parent, "NpcDialogBorder", UITheme.BrushedGold);
        UITheme.ApplySlicedStyle(border, UITheme.BrushedGold);
        UITheme.AddShadow(border.gameObject, 3f);
        _dialogPanel = border.gameObject;

        var rt = border.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 45f);
        rt.sizeDelta = new Vector2(880f, 290f);

        var inner = MakeImage(rt, "NpcDialogInner", UITheme.ObsidianDeep);
        UITheme.ApplySlicedStyle(inner, UITheme.ObsidianDeep);
        Stretch(inner.rectTransform, 3f);

        _npcNameText = MakeText(inner.transform, "NpcName", "NPC Name", 24, FontStyle.Bold, TextAnchor.MiddleLeft, UITheme.BrushedGoldBright);
        PlaceTopLeft(_npcNameText.rectTransform, 24f, 14f, 420f, 34f);

        _questTitleText = MakeText(inner.transform, "QuestTitle", "[Quest]", 16, FontStyle.Bold, TextAnchor.MiddleRight, UITheme.JadeAccent);
        PlaceTopRight(_questTitleText.rectTransform, 24f, 16f, 420f, 30f);

        _dialogBodyText = MakeText(inner.transform, "DialogBody", "...", 20, FontStyle.Normal, TextAnchor.UpperLeft, UITheme.TextPrimary);
        PlaceTopLeft(_dialogBodyText.rectTransform, 24f, 58f, 826f, 115f);

        var rewBg = MakeImage(inner.transform, "RewardBox", UITheme.ObsidianPanel);
        UITheme.ApplySlicedStyle(rewBg, UITheme.ObsidianPanel);
        _rewardBox = rewBg.gameObject;
        PlaceBottomLeft(rewBg.rectTransform, 24f, 66f, 460f, 44f);

        _rewardIcon = MakeImage(rewBg.transform, "RewardIcon", Color.white);
        _rewardIcon.preserveAspect = true;
        PlaceTopLeft(_rewardIcon.rectTransform, 8f, 5f, 34f, 34f);

        _rewardLabelText = MakeText(rewBg.transform, "RewardLabel", "รางวัล:", 16, FontStyle.Bold, TextAnchor.MiddleLeft, UITheme.BrushedGoldBright);
        PlaceTopLeft(_rewardLabelText.rectTransform, 50f, 4f, 400f, 36f);

        _errorNoticeText = MakeText(inner.transform, "ErrorNotice", "", 15, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(1f, 0.38f, 0.38f, 1f));
        PlaceBottomLeft(_errorNoticeText.rectTransform, 24f, 18f, 480f, 34f);

        var closeGo = new GameObject("CloseButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        closeGo.transform.SetParent(inner.transform, false);
        var closeImg = closeGo.GetComponent<Image>();
        UITheme.ApplySlicedStyle(closeImg, UITheme.ObsidianHeader);
        PlaceBottomRight((RectTransform)closeGo.transform, 195f, 16f, 150f, 44f);
        _closeBtn = closeGo.GetComponent<Button>();
        _closeBtn.onClick.AddListener(CloseDialog);
        _closeBtnText = MakeText(closeGo.transform, "Label", "ปิด [Esc]", 17, FontStyle.Bold, TextAnchor.MiddleCenter, UITheme.TextSecondary);
        Stretch(_closeBtnText.rectTransform, 2f);

        var primGo = new GameObject("PrimaryButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        primGo.transform.SetParent(inner.transform, false);
        _primaryBtnBg = primGo.GetComponent<Image>();
        UITheme.ApplySlicedStyle(_primaryBtnBg, UITheme.JadeButton);
        PlaceBottomRight((RectTransform)primGo.transform, 24f, 16f, 160f, 44f);
        _primaryBtn = primGo.GetComponent<Button>();
        _primaryBtn.onClick.AddListener(OnClickPrimaryAction);
        _primaryBtnText = MakeText(primGo.transform, "Label", "รับเควสต์ [G]", 17, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        Stretch(_primaryBtnText.rectTransform, 2f);

        _dialogPanel.SetActive(false);
    }

    private void BuildTrackerUI(Transform parent)
    {
        var border = MakeImage(parent, "QuestTrackerBorder", UITheme.BrushedGoldDark);
        UITheme.ApplySlicedStyle(border, UITheme.BrushedGoldDark);
        UITheme.AddShadow(border.gameObject, 2f);
        _trackerRoot = border.gameObject;

        var rt = border.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(18f, -235f);
        rt.sizeDelta = new Vector2(360f, 110f);

        var inner = MakeImage(rt, "QuestTrackerInner", new Color(0.05f, 0.06f, 0.09f, 0.88f));
        UITheme.ApplySlicedStyle(inner, new Color(0.05f, 0.06f, 0.09f, 0.88f));
        Stretch(inner.rectTransform, 2f);

        _trackerBodyText = MakeText(inner.transform, "TrackerText", "", 15, FontStyle.Bold, TextAnchor.UpperLeft, UITheme.TextPrimary);
        Stretch(_trackerBodyText.rectTransform, 10f);

        _trackerRoot.SetActive(false);
    }

    private Image MakeImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        return img;
    }

    private Text MakeText(Transform parent, string name, string content, int size, FontStyle style, TextAnchor align, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.transform.SetParent(parent, false);
        var txt = go.GetComponent<Text>();
        txt.font = _font;
        txt.text = content;
        txt.fontSize = size;
        txt.fontStyle = style;
        txt.alignment = align;
        txt.color = color;
        txt.raycastTarget = false;
        UITheme.AddShadow(go, 1.2f);
        return txt;
    }

    private static void Stretch(RectTransform rt, float pad)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(pad, pad);
        rt.offsetMax = new Vector2(-pad, -pad);
    }

    private static void PlaceTopLeft(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
    }

    private static void PlaceTopRight(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-x, -y);
        rt.sizeDelta = new Vector2(w, h);
    }

    private static void PlaceBottomLeft(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
    }

    private static void PlaceBottomRight(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(-x, y);
        rt.sizeDelta = new Vector2(w, h);
    }
}