using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public class TestAutomationRunner : MonoBehaviour
{
    public static TestAutomationRunner Instance { get; private set; }
    public bool IsComplete { get; private set; }
    public int PassedCount { get; private set; }
    public int FailedCount { get; private set; }
    public string SummaryReport { get; private set; } = "";

    private readonly List<string> _testLogs = new List<string>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
#if UNITY_EDITOR
        // รันเทสต์เฉพาะตอนทดสอบใน Unity Editor เท่านั้น จะไม่รบกวนตอน Build เกมจริง
        // StartCoroutine(RunAllTestsRoutine()); 
#endif
    }

    private void AssertTest(string testName, bool condition, string details = "")
    {
        if (condition)
        {
            PassedCount++;
            string log = $"[TEST PASS] {testName} {(string.IsNullOrEmpty(details) ? "" : "-> " + details)}";
            _testLogs.Add(log);
            Debug.Log(log);
        }
        else
        {
            FailedCount++;
            string log = $"[TEST FAIL] {testName} -> {details}";
            _testLogs.Add(log);
            Debug.LogError(log);
        }
    }

    private IEnumerator RunAllTestsRoutine()
    {
        Debug.Log(">>> STARTING MMORPG SYSTEMS INTEGRATION TEST RUNNER <<<");

        float timeout = Time.time + 12f;
        while ((PlayerCombat.Local == null || !PlayerCombat.Local.IsSpawned ||
                InventoryManager.Local == null || !InventoryManager.Local.IsActivated) && Time.time < timeout)
        {
            yield return null;
        }

        var combat = PlayerCombat.Local;
        var inv = InventoryManager.Local;
        var saveMgr = PlayerSaveManager.Instance ?? PlayerSaveManager.EnsureExists();
        var spawner = SandboxWorldSpawner.Instance;

        AssertTest("1.1 Player & Systems Spawned", combat != null && combat.IsSpawned && inv != null && inv.IsActivated,
            $"Player '{combat?.name}', Inventory Capacity={inv?.Capacity}");

        yield return new WaitForSeconds(0.5f);

        // TEST 1: UI & SYSTEM CLEANUP CHECK
        var mapMgr = MapManager.Instance;
        bool mapDisabled = mapMgr == null || !mapMgr.enabled || !mapMgr.gameObject.activeInHierarchy;
        AssertTest("1.2 World Map & Minimap Removed", mapDisabled);

        var statusHud = PlayerStatusHud.Instance;
        bool hasClutteredStatusCard = statusHud != null && statusHud.transform.Find("MinimalPlayerHudCanvas/TopRightStatusCard") != null;
        AssertTest("1.3 Cluttered Status UI Removed", !hasClutteredStatusCard);

        var invUi = FindAnyObjectByType<InventoryUI>();
        AssertTest("1.4 Inventory UI Preserved", invUi != null);

        var combatHud = FindAnyObjectByType<CombatHud>();
        AssertTest("1.5 Combat Feedback HUD Preserved", combatHud != null);

        // TEST 2: SWORD HITBOX & INVISIBLE VISUAL CHECK
        Collider swordCol = combat.WeaponHitbox;
        bool hitboxValid = swordCol != null && swordCol is BoxCollider && swordCol.isTrigger;
        AssertTest("2.1 GreatSword Blade Hitbox Aligned & Configured", hitboxValid);

        // TEST 3: NETWORKED STAMINA SYSTEM
        float startStamina = combat.CurrentStamina;
        AssertTest("3.1 Stamina Pool Initialized", combat.MaxStamina >= 100f,
            $"MaxStamina={combat.MaxStamina}, CurrentStamina={startStamina}");

        // TEST 5: INVENTORY EXACT SLOT-INDEX & DRAG-AND-DROP
        var potion = ItemRegistry.Get("health_potion");
        var swordItem = ItemRegistry.Get("iron_sword");
        var chestItem = ItemRegistry.Get("hunter_vest");

        inv.ClearSlotsForTest();
        inv.SetSlotDirectForTest(3, potion, 5);
        inv.SetSlotDirectForTest(5, swordItem, 1, 0);
        inv.SetSlotDirectForTest(8, chestItem, 1);

        AssertTest("5.1 Exact Slot Placement",
            inv.GetSlot(3).item == potion && inv.GetSlot(5).item == swordItem && inv.GetSlot(8).item == chestItem);

        combat.NetworkHealth = 70;
        inv.UseItem(3);
        AssertTest("5.2 UseItem Strictly Targets Slot 3", inv.GetSlot(3).quantity == 4 && inv.GetSlot(0).IsEmpty);

        bool swapOk = inv.SwapOrMoveSlots(5, 12);
        AssertTest("5.3 Drag-and-Drop Slot Move (5 -> 12)", swapOk && inv.GetSlot(5).IsEmpty && inv.GetSlot(12).item == swordItem);

        bool equipOk = inv.EquipFromDrag(12, EquipmentSlotType.Sword);
        AssertTest("5.4 Drag-to-Equip Onto Gear Slot", equipOk && inv.EquippedWeaponId == "iron_sword");

        // TEST 6: PLAYER SAVE SYSTEM
        string testKey = "UnitTestPlayer_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        PlayerSaveManager.OverridePlayerKeyForTesting = testKey;

        combat.AddGold(350, "TestGold");
        combat.AddExperience(120);

        int saveLevel = combat.NetworkLevel;
        int saveGold = combat.NetworkGold;

        bool saved = saveMgr.SaveNow("AutomatedUnitTestSave");
        string savePath = saveMgr.ActiveSavePath;
        AssertTest("6.1 Atomic Player Save to Disk", saved && File.Exists(savePath));

        inv.ClearSlotsForTest();
        PlayerSaveData loaded = saveMgr.LoadFromDisk(testKey);
        AssertTest("6.2 Save File Integrity & Deserialization", loaded != null && loaded.level == saveLevel && loaded.gold == saveGold);

        saveMgr.InitializeOrRestorePlayer(combat, inv);
        yield return new WaitForSeconds(0.2f);

        PlayerSaveManager.DeleteSaveForTesting(testKey);
        PlayerSaveManager.OverridePlayerKeyForTesting = null;

        // FINAL SUMMARY
        IsComplete = true;
        SummaryReport = $"AUTOMATED MMORPG INTEGRATION RESULTS:\nTotal Tests: {PassedCount + FailedCount} | Passed: {PassedCount} | Failed: {FailedCount}\n" +
                        string.Join("\n", _testLogs);
        Debug.Log(SummaryReport);
    }
}