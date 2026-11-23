using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Firebase.Firestore;
using UnityEngine;

// โครงสร้างข้อมูลช่องกระเป๋าแต่ละช่องสำหรับบันทึกลงไฟล์ JSON / Firestore
[Serializable]
public class SavedInventorySlotData
{
    public int slotIndex;         // ตำแหน่งช่องกระเป๋าที่แน่นอน (0..47)
    public string instanceId;     // รหัสอ้างอิงเฉพาะชิ้น
    public string itemId;         // รหัสไอเทม
    public int quantity;          // จำนวนในช่อง
    public int enhancementLevel;  // ระดับการตีบวก (+0..+10)
    public int enhanceFailStacks; // แต้ม Pity (Failstack) สะสมเมื่อตีบวกพลาด
}

// โครงสร้างข้อมูลอุปกรณ์ที่สวมใส่อยู่ใน 4 ช่องหลัก (Sword, Shield, Armor, Pants)
[Serializable]
public class SavedEquippedSlotData
{
    public int slotType;         // ประเภทช่องอุปกรณ์ ((int)EquipmentSlotType)
    public string instanceId;    // รหัสอ้างอิงเฉพาะชิ้น
    public string itemId;        // รหัสไอเทม
    public int enhancementLevel; // ระดับการตีบวก (+0..+10)
}

// โครงสร้างข้อมูลการผูกปุ่มลัด Hotbar (ช่อง 1 ถึง 6) สำหรับบันทึกลงไฟล์ JSON
[Serializable]
public class SavedHotbarBindingData
{
    public int hotbarIndex;        // ตำแหน่งปุ่มลัด 0..5 (ตรงกับปุ่ม 1..6)
    public int inventorySlotIndex; // ตำแหน่งช่องกระเป๋าที่ผูกไว้
    public string instanceId;      // รหัสอ้างอิงเฉพาะชิ้น
    public string itemId;          // รหัสไอเทม
}

// โครงสร้างข้อมูลเซฟผู้เล่นทั้งหมด (Player Save Data Schema v2 — รองรับ Firestore Cloud Sync + Anti-Rollback Revision)
[Serializable]
public class PlayerSaveData
{
    public int schemaVersion = 2;
    public int saveRevision = 0; // เลขเวอร์ชันเซฟแบบเพิ่มทางเดียว (ป้องกันการย้อนเซฟ / Rollback)
    public string playerKey;
    public string playerName;
    public int avatarId = 0; // รหัสรูปโปรไฟล์จากหน้า Lobby (0..3)
    public long lastSavedUtcTicks;
    public bool hasInitializedOnce;

    // ข้อมูลเลเวล ค่าประสบการณ์ แต้มสเตตัส และเงิน Gold
    public int level = 1;
    public int experience = 0;
    public int unspentStatPoints = 0;
    public int statSTR = 0;
    public int statVIT = 0;
    public int statEND = 0;
    public int statAGI = 0;
    public int gold = 0;
    public int currentHealth = 100;
    public float currentStamina = 100f;

    // รายการไอเทมในกระเป๋า, อุปกรณ์ที่สวมใส่, ปุ่มลัด Hotbar 1-6 และหินเทเลพอร์ตที่ปลดล็อกแล้ว
    public List<SavedInventorySlotData> inventorySlots = new List<SavedInventorySlotData>();
    public List<SavedEquippedSlotData> equippedSlots = new List<SavedEquippedSlotData>();
    public List<SavedHotbarBindingData> hotbarBindings = new List<SavedHotbarBindingData>();
    public List<string> unlockedWaystones = new List<string>();

    // ค่าแฮช SHA-256 สำหรับตรวจสอบความสมบูรณ์ของไฟล์เซฟ (ป้องกันการเปิดไฟล์ JSON แก้ค่าสเตตัส/เงิน/ตีบวก)
    public string integrityHash = "";
}

/// <summary>
/// ระบบบันทึกและกู้คืนข้อมูลผู้เล่นแบบป้องกันข้อมูลสูญหาย 100% (Crash-Safe Atomic JSON Player Persistence System)
///   - บันทึกแบบ Atomic (.tmp -> .json พร้อมสำรอง .bak) ป้องกันไฟล์เสียเมื่อเกมหลุดหรือปิดกะทันหัน
///   - บันทึกข้อมูลครบถ้วน: เลเวล, EXP, สเตตัส, Gold, รูปโปรไฟล์ Lobby Avatar, ช่องกระเป๋า และอุปกรณ์สวมใส่ 4 ชิ้น
///   - ผู้เล่นที่กลับเข้าเกมใหม่หลังหลุด (Returning Player) จะถูกส่งไปเกิดใน Safe / Comfort Zone เสมอ
/// </summary>
[DisallowMultipleComponent]
public class PlayerSaveManager : MonoBehaviour
{
    public static PlayerSaveManager Instance { get; private set; }

    public const float PeriodicAutoSaveSeconds = 15f;

    // ตัวแปรสำหรับสลับคีย์ผู้เล่นในการจำลองทดสอบ
    public static string OverridePlayerKeyForTesting { get; set; }

    public bool HasLoadedOrInitialized { get; private set; }
    public bool LastLoadWasReturningPlayer { get; private set; }
    // ระบุว่าไฟล์เซฟล่าสุดที่โหลดถูกตรวจพบความไม่ตรงกันของลายเซ็น SHA-256 หรือไม่
    public bool LastLoadWasTampered { get; private set; }
    // ระบุว่าซิงก์ข้อมูลจาก Firebase Firestore สำเร็จในรอบนี้หรือไม่
    public bool LastSyncUsedFirestore { get; private set; }
    public int CurrentSaveRevision => _currentSaveRevision;
    public string ActiveSavePath => GetSavePath(ResolveActivePlayerKey());
    public string LastSaveReason { get; private set; }

    private const string SaveIntegritySalt = "GMMO_V1_SAVE_SEAL_94A7";
    private const int CurrentSchemaVersion = 2;

    // พิกัด Safe Zone สำรองให้ตรงกับเมืองหลักของแผนที่ FinalTestv1 เสมอ
    private static readonly Vector3 DefaultSafeZoneCenter = new Vector3(0f, 6.0f, 0f);
    private const float DefaultSafeZoneRadius = 310.0f;
    private static readonly Vector3 DefaultSafeZoneReconnectOffset = new Vector3(0f, 0.15f, 34.0f);

    private bool _dirty;
    private float _nextPeriodicSaveTime;
    private bool _restoringNow;
    private int _currentSaveRevision;
    private bool _cloudSyncInFlight;
    private bool _hasCompletedInitialCloudReconcile;
    private bool _firestoreUnavailableThisSession;
    private FirebaseFirestore _cachedFirestoreDb;

    public static PlayerSaveManager EnsureExists()
    {
        if (Instance != null) return Instance;
        var existing = FindAnyObjectByType<PlayerSaveManager>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        var go = new GameObject("PlayerSaveManager");
        return go.AddComponent<PlayerSaveManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        _nextPeriodicSaveTime = Time.unscaledTime + PeriodicAutoSaveSeconds;
    }

    private void OnDestroy()
    {
        if (HasLoadedOrInitialized)
            SaveNow("OnDestroy");
        if (Instance == this) Instance = null;
    }

    private void OnApplicationQuit()
    {
        if (HasLoadedOrInitialized)
            SaveNow("OnApplicationQuit");
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus && HasLoadedOrInitialized)
            SaveNow("OnApplicationPause");
    }

    private void Update()
    {
        var combat = PlayerCombat.Local;
        var inv = InventoryManager.Local;

        if (!HasLoadedOrInitialized)
        {
            if (combat != null && combat.IsSpawned && combat.IsLocallyControlled && inv != null && inv.IsActivated)
            {
                InitializeOrRestorePlayer(combat, inv);
            }
            return;
        }

        if (Time.unscaledTime >= _nextPeriodicSaveTime)
        {
            _nextPeriodicSaveTime = Time.unscaledTime + PeriodicAutoSaveSeconds;
            if (_dirty)
                SaveNow("Periodic15sAutoSave");
        }
    }

    // ------------------------------------------------------------------ การระบุตัวตนผู้เล่นและที่อยู่ไฟล์เซฟ

    public static string ResolveActivePlayerKey()
    {
        if (!string.IsNullOrEmpty(OverridePlayerKeyForTesting))
            return OverridePlayerKeyForTesting;

        if (GoogleAPI.instance != null)
        {
            if (!string.IsNullOrEmpty(GoogleAPI.instance.uuid))
                return "uid_" + GoogleAPI.instance.uuid;
            if (!string.IsNullOrEmpty(GoogleAPI.instance.username))
                return "user_" + GoogleAPI.instance.username;
        }

        // แยกคีย์เซฟออฟไลน์ระหว่าง Unity Editor กับ Standalone Build (.exe) เพื่อไม่ให้เซฟทับกันเมื่อเปิดทดสอบ Co-op 2 จอบนเครื่องเดียว
        return Application.isEditor ? "LocalSandboxPlayer" : "LocalSandboxPlayer_Standalone";
    }

    public static string GetSaveDirectory()
    {
        return Path.Combine(Application.persistentDataPath, "PlayerSaves");
    }

    public static string GetSavePath(string playerKey)
    {
        string sanitized = SanitizeFileName(string.IsNullOrEmpty(playerKey) ? "LocalSandboxPlayer" : playerKey);
        return Path.Combine(GetSaveDirectory(), sanitized + ".json");
    }

    private static string SanitizeFileName(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        char[] invalid = Path.GetInvalidFileNameChars();
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            bool bad = Array.IndexOf(invalid, c) >= 0 || c == ' ';
            sb.Append(bad ? '_' : c);
        }
        return sb.Length > 0 ? sb.ToString() : "LocalSandboxPlayer";
    }

    // ------------------------------------------------------------------ การโหลดและกู้คืนข้อมูลผู้เล่น

    public void InitializeOrRestorePlayer(PlayerCombat combat, InventoryManager inv)
    {
        if (combat == null || inv == null) return;
        HasLoadedOrInitialized = true;

        string key = ResolveActivePlayerKey();
        string firebaseUid = ResolveLoggedInFirebaseUid();
        _hasCompletedInitialCloudReconcile = string.IsNullOrEmpty(firebaseUid);

        PlayerSaveData loaded = LoadFromDisk(key);

        if (loaded != null && loaded.hasInitializedOnce)
        {
            _currentSaveRevision = Mathf.Max(0, loaded.saveRevision);
            ApplyLoadedDataToPlayer(loaded, combat, inv, sourceLabel: $"Disk '{GetSavePath(key)}'");
        }
        else
        {
            _currentSaveRevision = 0;
            LastLoadWasReturningPlayer = false;
            // แจกชุดเริ่มต้นสำหรับผู้เล่นใหม่ (ดาบ Iron GreatSword สวมใส่ในช่อง WEAPON + ยาเลือด Healing Potion x3 ผูกปุ่มลัดช่อง [2])
            GrantFirstTimeStarterKit(inv);
            Debug.Log($"[PlayerSaveManager] FIRST-TIME PLAYER '{key}' -> granted starter kit, keeping New Player Start Point {combat.transform.position} and creating initial save.");
            // หากล็อกอิน Google อยู่ จะรอให้ ReconcileWithFirestoreAsync อ่านเซฟจาก Cloud ก่อนแล้วค่อยอัปโหลด เพื่อป้องกันเครื่องใหม่ส่ง Rev.1 ไปชน Rev สูงกว่าบน Cloud
            SaveNow("InitialNewPlayerProfile");
        }

        // หากผู้เล่นล็อกอินผ่าน Google/Firebase (มี uuid) ให้ซิงก์รวมกับ Firestore (players/{uuid}/profile/save) เพื่อรวมเซฟให้ตรงกับระบบ Quest
        if (!string.IsNullOrEmpty(firebaseUid))
        {
            _ = ReconcileWithFirestoreAsync(firebaseUid, loaded, combat, inv);
        }
    }

    private void ApplyLoadedDataToPlayer(PlayerSaveData loaded, PlayerCombat combat, InventoryManager inv, string sourceLabel)
    {
        if (loaded == null || combat == null || inv == null) return;

        LastLoadWasReturningPlayer = true;
        _currentSaveRevision = Mathf.Max(_currentSaveRevision, loaded.saveRevision);
        _restoringNow = true;
        try
        {
            // ซิงก์รหัสโปรไฟล์ Avatar จากเซฟ (หากไม่ได้เข้าผ่านหน้า Lobby ใหม่)
            var capsule = combat.GetComponent<PlayerCapsule>();
            if (capsule != null && GoogleAPI.instance == null)
            {
                capsule.SetAvatarIndex(loaded.avatarId);
            }

            // ตรวจสอบและ Clamp ข้อมูลเซฟให้เข้ากับเพดาน Lv.30 Cap และเพดาน Stamina 150 เสมอ
            loaded.level = Mathf.Clamp(loaded.level, 1, PlayerCombat.MaxLevel);
            if (loaded.level >= PlayerCombat.MaxLevel) loaded.experience = 0;

            combat.ApplySaveData(loaded);
            inv.RestoreSaveData(loaded);
            TeleportWaystone.RestoreSaveData(loaded);
            // เมื่อผู้เล่นกลับเข้าเกมในเขตปลอดภัย (Comfort SafeZone) ให้ฟื้นฟู HP และ Stamina เต็ม 100% ทันที
            // ป้องกันปัญหาผู้เล่นเพิ่งเข้าห้องมายังไม่ทันโดนตีแต่เลือดแหว่งค้างจากเซสชันก่อน (เช่น 415/441)
            combat.ApplySaveHealthAndStamina(combat.MaxHealth, combat.MaxStamina);

            // ส่งผู้เล่นที่กลับเข้ามาเกิดในเขตปลอดภัย (ใช้พิกัดเมืองหลัก FinalTestv1 เป็น Fallback เสมอ)
            SafeZone safe = SafeZone.Instance ?? SafeZone.EnsureAt(
                DefaultSafeZoneCenter,
                DefaultSafeZoneRadius,
                DefaultSafeZoneReconnectOffset,
                showProceduralVisuals: false);
            Vector3 safePos = safe.ReconnectSpawnPosition;
            combat.TeleportTo(safePos);
        }
        finally
        {
            _restoringNow = false;
        }

        Debug.Log($"[PlayerSaveManager] RETURNING PLAYER restored from {sourceLabel} -> Rev.{loaded.saveRevision}, Lv.{loaded.level}, Gold={loaded.gold}, Avatar={loaded.avatarId}, Slots={loaded.inventorySlots.Count}, Equipped={loaded.equippedSlots.Count}. Spawned in Comfort Zone {combat.transform.position}.");
    }

    /// <summary>
    /// คืนค่า Firebase UID เฉพาะเมื่อผู้เล่นล็อกอินผ่าน Lobby (Google Sign-In) และไม่ได้ใช้ Override Test Key
    /// </summary>
    public static string ResolveLoggedInFirebaseUid()
    {
        if (!string.IsNullOrEmpty(OverridePlayerKeyForTesting))
            return string.Empty;
        if (GoogleAPI.instance != null && !string.IsNullOrEmpty(GoogleAPI.instance.uuid))
            return GoogleAPI.instance.uuid;
        return string.Empty;
    }

    private FirebaseFirestore TryGetFirestoreDb()
    {
        if (_firestoreUnavailableThisSession) return null;
        if (_cachedFirestoreDb != null) return _cachedFirestoreDb;
        try
        {
            var db = FirebaseFirestore.DefaultInstance;
            // ปิด offline persistence (LevelDB) เพื่อไม่ให้เกมแครชเมื่อเปิดทดสอบ 2 จอพร้อมกันบนเครื่องเดียว (เหมือน GoogleAPI และ QuestNPC)
            db.Settings.PersistenceEnabled = false;
            _cachedFirestoreDb = db;
            return _cachedFirestoreDb;
        }
        catch (Exception ex)
        {
            _firestoreUnavailableThisSession = true;
            Debug.LogWarning($"[PlayerSaveManager] Firestore unavailable (falling back cleanly to Local JSON): {ex.Message}");
            return null;
        }
    }

    private async Task ReconcileWithFirestoreAsync(string firebaseUid, PlayerSaveData localData, PlayerCombat combat, InventoryManager inv)
    {
        if (_cloudSyncInFlight || string.IsNullOrEmpty(firebaseUid)) return;
        var db = TryGetFirestoreDb();
        if (db == null) return;

        _cloudSyncInFlight = true;
        try
        {
            DocumentReference doc = db.Collection("players").Document(firebaseUid)
                .Collection("profile").Document("save");
            DocumentSnapshot snap = await doc.GetSnapshotAsync();

            if (this == null || combat == null || inv == null || !combat.IsSpawned)
                return;

            if (snap != null && snap.Exists && snap.TryGetValue("payloadJson", out string cloudJson) && !string.IsNullOrWhiteSpace(cloudJson))
            {
                PlayerSaveData cloudData = JsonUtility.FromJson<PlayerSaveData>(cloudJson);
                if (cloudData != null && cloudData.hasInitializedOnce)
                {
                    if (!VerifyIntegrity(cloudData, out _))
                    {
                        Debug.LogWarning("[PlayerSaveManager] Cloud save integrity hash mismatch — applying strict stat/gear sanitization.");
                        LastLoadWasTampered = true;
                    }

                    int localRev = (localData != null && localData.hasInitializedOnce) ? localData.saveRevision : -1;
                    long localTicks = (localData != null && localData.hasInitializedOnce) ? localData.lastSavedUtcTicks : 0L;
                    bool cloudIsNewer = cloudData.saveRevision > localRev ||
                                        (cloudData.saveRevision == localRev && cloudData.lastSavedUtcTicks > localTicks);

                    if (cloudIsNewer)
                    {
                        LastSyncUsedFirestore = true;
                        _hasCompletedInitialCloudReconcile = true;
                        ApplyLoadedDataToPlayer(cloudData, combat, inv, sourceLabel: $"Firestore (players/{firebaseUid}/profile/save)");
                        // แคชสำเนา Cloud ล่าสุดลง Local Disk ด้วยเพื่อความเร็วและความปลอดภัย
                        WriteFileAtomic(GetSavePath(ResolveActivePlayerKey()), JsonUtility.ToJson(cloudData, prettyPrint: true));
                        return;
                    }
                }
            }

            // กรณีบน Cloud ยังไม่มีข้อมูล หรือเครื่องนี้มีเวอร์ชันเซฟใหม่กว่า ให้ปลดล็อกและซิงก์ขึ้น Firestore ทันที (Auto-Migrate)
            _hasCompletedInitialCloudReconcile = true;
            LastSyncUsedFirestore = true;
            SaveNow("FirestoreInitialSync");
        }
        catch (Exception ex)
        {
            _hasCompletedInitialCloudReconcile = true;
            _firestoreUnavailableThisSession = true;
            Debug.LogWarning($"[PlayerSaveManager] Firestore sync skipped (using Local JSON fallback until Firestore Rules allow players/{{uid}}/profile/save): {ex.Message}");
        }
        finally
        {
            _cloudSyncInFlight = false;
        }
    }

    private async Task PushSaveToFirestoreAsync(string firebaseUid, PlayerSaveData data, string json)
    {
        // รอให้การอ่าน/เทียบเวอร์ชันจาก Cloud ครั้งแรกเสร็จก่อน เพื่อป้องกันเครื่องใหม่ส่ง Rev.1 ไปชน Rev สูงกว่าบน Firestore
        if (!_hasCompletedInitialCloudReconcile) return;
        if (string.IsNullOrEmpty(firebaseUid) || data == null || string.IsNullOrEmpty(json)) return;
        var db = TryGetFirestoreDb();
        if (db == null) return;

        try
        {
            DocumentReference doc = db.Collection("players").Document(firebaseUid)
                .Collection("profile").Document("save");
            var payload = new Dictionary<string, object>
            {
                { "schemaVersion", data.schemaVersion },
                { "saveRevision", data.saveRevision },
                { "level", data.level },
                { "gold", data.gold },
                { "playerName", data.playerName ?? "Player" },
                { "updatedAtUnix", DateTimeOffset.UtcNow.ToUnixTimeSeconds() },
                { "payloadJson", json }
            };
            await doc.SetAsync(payload, SetOptions.MergeAll);
            LastSyncUsedFirestore = true;
        }
        catch (Exception ex)
        {
            _firestoreUnavailableThisSession = true;
            Debug.LogWarning($"[PlayerSaveManager] Firestore cloud save deferred (Local JSON saved safely; enable Firestore Rules for players/{{uid}}/profile/save): {ex.Message}");
        }
    }

    /// <summary>
    /// แจกอุปกรณ์และยาเริ่มต้นสำหรับผู้เล่นที่เข้าเกมครั้งแรก (เมื่อกระเป๋ายังว่างเปล่า):
    ///   - สวมใส่ Iron GreatSword (iron_sword) ลงช่อง [WEAPON] ทันที (+5 ATK รวมเป็น 19 ATK ที่ Lv.1)
    ///   - เพิ่ม Healing Potion (health_potion) x3 และผูกเข้าช่อง Hotbar index 0 (ปุ่มเลข [2])
    /// </summary>
    private void GrantFirstTimeStarterKit(InventoryManager inv)
    {
        if (inv == null) return;

        for (int i = 0; i < inv.Slots.Count; i++)
        {
            if (!inv.Slots[i].IsEmpty) return;
        }

        _restoringNow = true;
        try
        {
            ItemData starterSword = ItemRegistry.Get("iron_sword");
            if (starterSword != null && inv.AddItemAtSlot(0, starterSword, 1, 0))
            {
                inv.EquipItem(0);
            }

            ItemData starterPotion = ItemRegistry.Get("health_potion");
            if (starterPotion != null && inv.AddItemAtSlot(1, starterPotion, 3, 0))
            {
                inv.AssignHotbarSlot(0, 1);
            }
        }
        finally
        {
            _restoringNow = false;
        }
    }

    // ------------------------------------------------------------------ การบันทึกไฟล์แบบ Atomic Write

    public static void RequestAutoSave(string reason)
    {
        if (Instance == null || !Instance.HasLoadedOrInitialized || Instance._restoringNow) return;
        Instance._dirty = true;
        Instance.SaveNow(reason);
    }

    public bool SaveNow(string reason = "Manual")
    {
        var combat = PlayerCombat.Local;
        var inv = InventoryManager.Local;
        if (combat == null || !combat.IsSpawned || inv == null || !inv.IsActivated)
            return false;

        if (_restoringNow) return false;

        var capsule = combat.GetComponent<PlayerCapsule>();
        int currentAvatar = GoogleAPI.instance != null
            ? GoogleAPI.instance.avatarId
            : (capsule != null ? capsule.AvatarIndex : 0);

        string key = ResolveActivePlayerKey();
        _currentSaveRevision++;
        var data = new PlayerSaveData
        {
            schemaVersion = CurrentSchemaVersion,
            saveRevision = _currentSaveRevision,
            playerKey = key,
            playerName = GoogleAPI.instance != null && !string.IsNullOrEmpty(GoogleAPI.instance.username)
                ? GoogleAPI.instance.username
                : (capsule != null && !string.IsNullOrEmpty(capsule.Nick.Value) ? capsule.Nick.Value : "Player"),
            avatarId = Mathf.Clamp(currentAvatar, 0, 3),
            lastSavedUtcTicks = DateTime.UtcNow.Ticks,
            hasInitializedOnce = true
        };

        combat.ExportSaveData(data);
        inv.ExportSaveData(data);
        TeleportWaystone.ExportSaveData(data);

        // คำนวณลายเซ็น SHA-256 เพื่อตรวจสอบความถูกต้องของข้อมูลเซฟในครั้งถัดไป
        data.integrityHash = ComputeIntegrityHash(data);

        string json = JsonUtility.ToJson(data, prettyPrint: true);
        string path = GetSavePath(key);

        if (WriteFileAtomic(path, json))
        {
            _dirty = false;
            LastSaveReason = reason;
            _nextPeriodicSaveTime = Time.unscaledTime + PeriodicAutoSaveSeconds;
            Debug.Log($"[PlayerSaveManager] Saved ({reason}, Rev.{data.saveRevision}) -> {path} [Lv.{data.level}, Gold={data.gold}, Avatar={data.avatarId}, Items={data.inventorySlots.Count}]");

            // หากล็อกอินผ่าน Google/Firebase ให้ซิงก์ขึ้น Firestore แบบไม่บล็อกเฟรมเกม (และ Fallback อย่างปลอดภัยหาก Rules ยังไม่เปิด)
            string firebaseUid = ResolveLoggedInFirebaseUid();
            if (!string.IsNullOrEmpty(firebaseUid))
            {
                _ = PushSaveToFirestoreAsync(firebaseUid, data, json);
            }
            return true;
        }
        return false;
    }

    public PlayerSaveData LoadFromDisk(string playerKey = null)
    {
        LastLoadWasTampered = false;
        string key = string.IsNullOrEmpty(playerKey) ? ResolveActivePlayerKey() : playerKey;
        string path = GetSavePath(key);
        string bakPath = path + ".bak";

        if (TryReadJson(path, out PlayerSaveData primary))
        {
            if (VerifyIntegrity(primary, out bool isLegacy))
            {
                if (isLegacy)
                    Debug.Log($"[PlayerSaveManager] Loaded legacy unsigned save '{path}' (will be sealed with SHA-256 on next save).");
                return primary;
            }

            Debug.LogWarning($"[PlayerSaveManager] Integrity hash mismatch detected in '{path}'! Attempting recovery from clean backup...");
            if (TryReadJson(bakPath, out PlayerSaveData cleanBackup) && VerifyIntegrity(cleanBackup, out _))
            {
                Debug.LogWarning($"[PlayerSaveManager] Recovered verified clean save from backup '{bakPath}'.");
                return cleanBackup;
            }

            LastLoadWasTampered = true;
            Debug.LogWarning($"[PlayerSaveManager] No clean backup available for '{path}'; proceeding with strict stat/gear sanitization.");
            return primary;
        }

        if (TryReadJson(bakPath, out PlayerSaveData backup))
        {
            if (!VerifyIntegrity(backup, out _))
            {
                LastLoadWasTampered = true;
                Debug.LogWarning($"[PlayerSaveManager] Backup save '{bakPath}' failed integrity check; proceeding with strict sanitization.");
            }
            else
            {
                Debug.LogWarning($"[PlayerSaveManager] Primary save corrupt/missing; recovered from backup '{bakPath}'.");
            }
            return backup;
        }

        return null;
    }

    /// <summary>สร้างลายเซ็น SHA-256 จากข้อมูลสำคัญในไฟล์เซฟ (เลเวล, สเตตัส, เงิน, ช่องกระเป๋า, อุปกรณ์สวมใส่)</summary>
    public static string ComputeIntegrityHash(PlayerSaveData data)
    {
        if (data == null) return string.Empty;

        var sb = new StringBuilder(512);
        sb.Append(SaveIntegritySalt).Append('|')
          .Append(data.schemaVersion).Append('|');
        if (data.schemaVersion >= 2)
            sb.Append(data.saveRevision).Append('|');
        sb.Append(data.playerKey ?? string.Empty).Append('|')
          .Append(data.hasInitializedOnce ? '1' : '0').Append('|')
          .Append(data.level).Append('|')
          .Append(data.experience).Append('|')
          .Append(data.unspentStatPoints).Append('|')
          .Append(data.statSTR).Append('|')
          .Append(data.statVIT).Append('|')
          .Append(data.statEND).Append('|')
          .Append(data.statAGI).Append('|')
          .Append(data.gold).Append('|')
          .Append(data.currentHealth).Append('|')
          .Append(Mathf.RoundToInt(data.currentStamina * 10f).ToString(CultureInfo.InvariantCulture));

        sb.Append("#INV:");
        if (data.inventorySlots != null)
        {
            for (int i = 0; i < data.inventorySlots.Count; i++)
            {
                var s = data.inventorySlots[i];
                if (s == null) continue;
                sb.Append(s.slotIndex).Append(':')
                  .Append(s.itemId ?? string.Empty).Append(':')
                  .Append(s.quantity).Append(':')
                  .Append(s.enhancementLevel);
                if (data.schemaVersion >= 2)
                    sb.Append(':').Append(s.enhanceFailStacks);
                sb.Append(';');
            }
        }

        sb.Append("#EQ:");
        if (data.equippedSlots != null)
        {
            for (int i = 0; i < data.equippedSlots.Count; i++)
            {
                var eq = data.equippedSlots[i];
                if (eq == null) continue;
                sb.Append(eq.slotType).Append(':')
                  .Append(eq.itemId ?? string.Empty).Append(':')
                  .Append(eq.enhancementLevel).Append(';');
            }
        }

        using (var sha = SHA256.Create())
        {
            byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
            byte[] hash = sha.ComputeHash(bytes);
            var hex = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
                hex.Append(hash[i].ToString("x2"));
            return hex.ToString();
        }
    }

    /// <summary>ตรวจสอบความถูกต้องของลายเซ็น SHA-256 (รองรับไฟล์เซฟเวอร์ชันเก่าที่ยังไม่มี integrityHash แบบ Backwards-Compatible)</summary>
    public static bool VerifyIntegrity(PlayerSaveData data, out bool isLegacyUnsigned)
    {
        isLegacyUnsigned = false;
        if (data == null) return false;
        if (string.IsNullOrEmpty(data.integrityHash))
        {
            isLegacyUnsigned = true;
            return true;
        }

        string expected = ComputeIntegrityHash(data);
        return string.Equals(data.integrityHash, expected, StringComparison.OrdinalIgnoreCase);
    }

    public static bool DeleteSaveForTesting(string playerKey = null)
    {
        string key = string.IsNullOrEmpty(playerKey) ? ResolveActivePlayerKey() : playerKey;
        string path = GetSavePath(key);
        string bak = path + ".bak";
        string tmp = path + ".tmp";
        bool deleted = false;
        if (File.Exists(path)) { File.Delete(path); deleted = true; }
        if (File.Exists(bak)) File.Delete(bak);
        if (File.Exists(tmp)) File.Delete(tmp);
        return deleted;
    }

    private static bool TryReadJson(string path, out PlayerSaveData data)
    {
        data = null;
        if (!File.Exists(path)) return false;
        try
        {
            string raw = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(raw)) return false;
            data = JsonUtility.FromJson<PlayerSaveData>(raw);
            return data != null;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PlayerSaveManager] Failed reading save file '{path}': {e.Message}");
            return false;
        }
    }

    private static bool WriteFileAtomic(string targetPath, string content)
    {
        try
        {
            string dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string tempPath = targetPath + ".tmp";
            string bakPath = targetPath + ".bak";

            using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var sw = new StreamWriter(fs, new UTF8Encoding(false)))
            {
                sw.Write(content);
                sw.Flush();
                fs.Flush(true);
            }

            if (File.Exists(targetPath))
            {
                File.Copy(targetPath, bakPath, overwrite: true);
                File.Delete(targetPath);
            }

            File.Move(tempPath, targetPath);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[PlayerSaveManager] Atomic save failed for '{targetPath}': {e.Message}");
            return false;
        }
    }
}
