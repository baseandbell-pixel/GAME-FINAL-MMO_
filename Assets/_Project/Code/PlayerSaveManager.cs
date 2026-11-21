using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

// โครงสร้างข้อมูลช่องกระเป๋าแต่ละช่องสำหรับบันทึกลงไฟล์ JSON
[Serializable]
public class SavedInventorySlotData
{
    public int slotIndex;        // ตำแหน่งช่องกระเป๋าที่แน่นอน (0..23)
    public string instanceId;    // รหัสอ้างอิงเฉพาะชิ้น
    public string itemId;        // รหัสไอเทม
    public int quantity;         // จำนวนในช่อง
    public int enhancementLevel; // ระดับการตีบวก (+0..+10)
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

// โครงสร้างข้อมูลเซฟผู้เล่นทั้งหมด (Player Save Data Schema)
[Serializable]
public class PlayerSaveData
{
    public int schemaVersion = 1;
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

    // รายการไอเทมในกระเป๋า, อุปกรณ์ที่สวมใส่ และปุ่มลัด Hotbar 1-6
    public List<SavedInventorySlotData> inventorySlots = new List<SavedInventorySlotData>();
    public List<SavedEquippedSlotData> equippedSlots = new List<SavedEquippedSlotData>();
    public List<SavedHotbarBindingData> hotbarBindings = new List<SavedHotbarBindingData>();
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
    public string ActiveSavePath => GetSavePath(ResolveActivePlayerKey());
    public string LastSaveReason { get; private set; }

    private bool _dirty;
    private float _nextPeriodicSaveTime;
    private bool _restoringNow;

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

        return "LocalSandboxPlayer";
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
        PlayerSaveData loaded = LoadFromDisk(key);

        if (loaded != null && loaded.hasInitializedOnce)
        {
            LastLoadWasReturningPlayer = true;
            _restoringNow = true;
            try
            {
                // ซิงก์รหัสโปรไฟล์ Avatar จากเซฟ (หากไม่ได้เข้าผ่านหน้า Lobby ใหม่)
                var capsule = combat.GetComponent<PlayerCapsule>();
                if (capsule != null && GoogleAPI.instance == null)
                {
                    capsule.SetAvatarIndex(loaded.avatarId);
                }

                // ตรวจสอบและ Clamp ข้อมูลเซฟเก่าให้เข้ากับเพดาน Lv.30 Cap และเพดาน Stamina 150 เสมอ
                loaded.level = Mathf.Clamp(loaded.level, 1, PlayerCombat.MaxLevel);
                if (loaded.level >= PlayerCombat.MaxLevel) loaded.experience = 0;

                combat.ApplySaveData(loaded);
                inv.RestoreSaveData(loaded);
                // คำนวณและ Clamp ค่า HP / Stamina ปัจจุบันหลังสวมใส่อุปกรณ์แล้วให้ไม่เกิน MaxHealth / MaxStamina (<= 150) ใหม่
                combat.ApplySaveHealthAndStamina(loaded.currentHealth, loaded.currentStamina);

                // ส่งผู้เล่นที่กลับเข้ามาเกิดในเขตปลอดภัย (Comfort Safe Zone โซนใต้แผนที่ขยาย 220x220m) เสมอ
                SafeZone safe = SafeZone.Instance ?? SafeZone.EnsureAt(new Vector3(0f, 0f, -60f), 12.0f);
                Vector3 safePos = safe.ReconnectSpawnPosition;
                combat.TeleportTo(safePos);
            }
            finally
            {
                _restoringNow = false;
            }

            Debug.Log($"[PlayerSaveManager] RETURNING PLAYER restored from '{GetSavePath(key)}' -> Lv.{loaded.level}, Gold={loaded.gold}, Avatar={loaded.avatarId}, Slots={loaded.inventorySlots.Count}, Equipped={loaded.equippedSlots.Count}. Spawned in Comfort Zone {combat.transform.position}.");
        }
        else
        {
            LastLoadWasReturningPlayer = false;
            Debug.Log($"[PlayerSaveManager] FIRST-TIME PLAYER '{key}' -> keeping New Player Start Point {combat.transform.position} and creating initial save.");
            SaveNow("InitialNewPlayerProfile");
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
        var data = new PlayerSaveData
        {
            schemaVersion = 1,
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

        string json = JsonUtility.ToJson(data, prettyPrint: true);
        string path = GetSavePath(key);

        if (WriteFileAtomic(path, json))
        {
            _dirty = false;
            LastSaveReason = reason;
            _nextPeriodicSaveTime = Time.unscaledTime + PeriodicAutoSaveSeconds;
            Debug.Log($"[PlayerSaveManager] Saved ({reason}) -> {path} [Lv.{data.level}, Gold={data.gold}, Avatar={data.avatarId}, Items={data.inventorySlots.Count}]");
            return true;
        }
        return false;
    }

    public PlayerSaveData LoadFromDisk(string playerKey = null)
    {
        string key = string.IsNullOrEmpty(playerKey) ? ResolveActivePlayerKey() : playerKey;
        string path = GetSavePath(key);
        string bakPath = path + ".bak";

        if (TryReadJson(path, out PlayerSaveData primary))
            return primary;

        if (TryReadJson(bakPath, out PlayerSaveData backup))
        {
            Debug.LogWarning($"[PlayerSaveManager] Primary save corrupt/missing; recovered from backup '{bakPath}'.");
            return backup;
        }

        return null;
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
