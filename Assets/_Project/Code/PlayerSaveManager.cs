using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

[Serializable]
public class SavedInventorySlotData
{
    public int slotIndex;
    public string instanceId;
    public string itemId;
    public int quantity;
    public int tier;
    public int enhancementLevel;
}

[Serializable]
public class SavedEquippedSlotData
{
    public int slotType; // (int)EquipmentSlotType
    public string slotName; // Weapon, Shield, Armor, Pants
    public string instanceId;
    public string itemId;
    public int tier;
    public int enhancementLevel;
}

[Serializable]
public class PlayerSaveData
{
    public int schemaVersion = 1;
    public string playerId;
    public string playerKey;
    public string playerName;
    public long lastSavedUtcTicks;
    public bool hasInitializedOnce;

    // Progression & Economy
    public int level = 1;
    public int currentExp = 0;
    public int experience = 0;
    public int unspentStatPoints = 0;
    public int statSTR = 0;
    public int statVIT = 0;
    public int statEND = 0;
    public int statAGI = 0;
    public int gold = 0;
    public int currentHealth = 100;
    public float currentStamina = 100f;

    // Exact Inventory Layout & Equipped Gear
    public List<SavedInventorySlotData> inventorySlots = new List<SavedInventorySlotData>();
    public List<SavedEquippedSlotData> equippedSlots = new List<SavedEquippedSlotData>();
}

/// <summary>
/// Crash-safe, zero-data-loss Player Persistence System (JSON with atomic temp-replace writes &amp; .bak recovery).
/// Keyed by unique Player ID / Account Name.
///   - First-Time Player (no save): spawns at New Player Start Point and creates starter profile.
///   - Returning Player / Crash Recovery: loads exact stats, Gold, Inventory slots (with +Level &amp; instanceId),
///     and Equipped Gear, and spawns strictly inside the Comfort Zone (Safe Haven).
/// </summary>
[DisallowMultipleComponent]
public class PlayerSaveManager : MonoBehaviour
{
    public static PlayerSaveManager Instance { get; private set; }

    public const float PeriodicAutoSaveSeconds = 15f;

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

    // ------------------------------------------------------------------ Key & File Paths

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
        return Path.Combine(Application.persistentDataPath, "Saves");
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

    // ------------------------------------------------------------------ Initialization & Restore

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
                combat.ApplySaveData(loaded);
                inv.RestoreSaveData(loaded);
                combat.ApplySaveHealthAndStamina(loaded.currentHealth, loaded.currentStamina);

                // Returning player / reconnect / crash recovery: ALWAYS spawn inside the Safe / Comfort Zone
                SafeZone safe = SafeZone.Instance ?? SafeZone.EnsureAt(new Vector3(-14f, 0f, 8f), 7.5f);
                Vector3 safePos = safe.ReconnectSpawnPosition;
                combat.TeleportTo(safePos);
            }
            finally
            {
                _restoringNow = false;
            }

            Debug.Log($"[PlayerSaveManager] RETURNING PLAYER restored from '{GetSavePath(key)}' -> Lv.{loaded.level}, Gold={loaded.gold}, Slots={loaded.inventorySlots.Count}, Equipped={loaded.equippedSlots.Count}. Spawned in Comfort Zone {combat.transform.position}.");
        }
        else
        {
            LastLoadWasReturningPlayer = false;
            Debug.Log($"[PlayerSaveManager] FIRST-TIME PLAYER '{key}' -> keeping New Player Start Point {combat.transform.position} and creating initial save.");
            SaveNow("InitialNewPlayerProfile");
        }
    }

    // ------------------------------------------------------------------ Auto-Save & Atomic Write

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

        string key = ResolveActivePlayerKey();
        var data = new PlayerSaveData
        {
            schemaVersion = 1,
            playerId = key,
            playerKey = key,
            playerName = GoogleAPI.instance != null && !string.IsNullOrEmpty(GoogleAPI.instance.username)
                ? GoogleAPI.instance.username
                : "Player",
            lastSavedUtcTicks = DateTime.UtcNow.Ticks,
            hasInitializedOnce = true
        };

        combat.ExportSaveData(data);
        data.currentExp = data.experience;
        inv.ExportSaveData(data);

        string json = JsonUtility.ToJson(data, prettyPrint: true);
        string path = GetSavePath(key);

        if (WriteFileAtomic(path, json))
        {
            _dirty = false;
            LastSaveReason = reason;
            _nextPeriodicSaveTime = Time.unscaledTime + PeriodicAutoSaveSeconds;
            Debug.Log($"[PlayerSaveManager] Saved ({reason}) -> {path} [Lv.{data.level}, Gold={data.gold}, Items={data.inventorySlots.Count}]");
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

            if (!TryReadJson(tempPath, out _))
            {
                Debug.LogError($"[PlayerSaveManager] Temp file JSON verification failed for '{tempPath}'.");
                return false;
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
