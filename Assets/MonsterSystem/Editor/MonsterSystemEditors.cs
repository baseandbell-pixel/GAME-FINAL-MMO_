#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom Inspector ภาษาไทยสำหรับ <see cref="MonsterAnimationConfig"/>:
///   - พรีวิว Stat Lv.1 (เลือด / พลังโจมตี / พลังป้องกัน)
///   - กล่องคำนวณอัตราดรอปสด ๆ ("โอกาสไม่ออกเลย %" vs "โอกาสได้ของอย่างน้อย 1 ชิ้น %")
///   - ปุ่มลัด บิลด์เฉพาะมอนตัวนี้ และวางโซนลงฉากในคลิกเดียว
/// </summary>
[CustomEditor(typeof(MonsterAnimationConfig))]
public class MonsterAnimationConfigEditor : Editor
{
    public override void OnInspectorGUI()
    {
        MonsterAnimationConfig cfg = (MonsterAnimationConfig)target;

        // 1. พรีวิว Stat Lv.1
        int lv1Hp = Mathf.Max(20, Mathf.RoundToInt(120 * cfg.healthMultiplier));
        int lv1Atk = Mathf.Max(2, Mathf.RoundToInt(10 * cfg.damageMultiplier));
        int lv1Def = Mathf.Max(0, cfg.defenseBonus);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField($"ข้อมูลมอนสเตอร์: {cfg.monsterName} ({(cfg.isBoss ? "บอส (BOSS)" : "มอนทั่วไป")})", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"พรีวิว Lv.1 → เลือด: {lv1Hp}  |  โจมตี: {lv1Atk}  |  ป้องกัน: {lv1Def}  |  สูง: {cfg.targetWorldHeight:0.00} ม.");
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(4);

        // 2. กล่องคำนวณโอกาสของดรอปสด ๆ
        float noDropPct = cfg.CalculateNoDropProbabilityPercent(1f);
        float anyDropPct = Mathf.Clamp(100f - noDropPct, 0f, 100f);
        int validItems = 0;
        if (cfg.lootDrops != null)
        {
            for (int i = 0; i < cfg.lootDrops.Count; i++)
                if (cfg.lootDrops[i] != null && cfg.lootDrops[i].item != null && cfg.lootDrops[i].dropRatePercent > 0f)
                    validItems++;
        }

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("สรุปโอกาสของดรอป (คำนวณสด)", EditorStyles.boldLabel);
        if (validItems == 0)
        {
            EditorGUILayout.HelpBox("ยังไม่ได้ใส่ไอเทมดรอป (ตอนนี้ไม่ออกเลย 100%) — กด + ในหมวด 'Loot Drops' ด้านล่างแล้วลาก ItemData ของเพื่อนมาวางได้เลย", MessageType.Info);
        }
        else
        {
            string modeLabel = cfg.lootRollMode == MonsterLootRollMode.IndependentPerItem
                ? "ทอยแยกทีละชิ้น (Independent)"
                : "ทอยเลือก 1 ชิ้นจาก 100% (Single Pick)";
            EditorGUILayout.LabelField($"โหมดการทอย: {modeLabel}  |  จำนวนไอเทมที่ตั้งไว้: {validItems} ชิ้น");
            EditorGUILayout.LabelField($"โอกาสได้ของอย่างน้อย 1 ชิ้น: {anyDropPct:0.0}%   |   โอกาสไม่ออกเลย: {noDropPct:0.0}%");

            if (cfg.lootRollMode == MonsterLootRollMode.SinglePickTable)
            {
                float sum = cfg.CalculateTotalConfiguredPercent();
                if (sum > 100.01f)
                {
                    EditorGUILayout.HelpBox($"ผลรวม % ในโหมด Single Pick เกิน 100% (รวมได้ {sum:0.0}%) — ชิ้นท้าย ๆ ที่เกิน 100% จะไม่มีทางออก กรุณาลดเรทให้รวมไม่เกิน 100%", MessageType.Warning);
                }
            }
        }
        if (cfg.goldDropChancePercent > 0f)
        {
            EditorGUILayout.LabelField($"ดรอปเงิน (Gold): โอกาส {cfg.goldDropChancePercent:0.0}% ได้ {Mathf.Min(cfg.minGold, cfg.maxGold)} - {Mathf.Max(cfg.minGold, cfg.maxGold)} เหรียญ");
        }
        else
        {
            EditorGUILayout.LabelField("ดรอปเงิน (Gold): ปิดอยู่ (0%)");
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(6);

        // 3. ช่องปรับค่าทั้งหมด
        DrawDefaultInspector();

        EditorGUILayout.Space(8);

        // 4. ปุ่มลัด
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button($"อัปเดต Prefab '{cfg.monsterName}' ตัวเดียว", GUILayout.Height(28)))
        {
            MonsterAnimatorBuilder.BuildSingleMonsterByName(cfg.monsterName);
        }
        if (GUILayout.Button($"วาง Zone_{cfg.monsterName} ลงฉากนี้", GUILayout.Height(28)))
        {
            MonsterAnimatorBuilder.PlaceZoneInActiveScene(cfg.monsterName);
        }
        EditorGUILayout.EndHorizontal();
    }
}

/// <summary>
/// Custom Inspector ภาษาไทยสำหรับ <see cref="MonsterSystemManager"/>:
///   - อ่านง่ายทุกช่องพร้อมคำอธิบายภาษาไทย
///   - โชว์สถานะสดตอนกด Play (รอดกี่ตัว / รอเกิดกี่ตัว)
///   - ปุ่มดึงโซนลูกทั้งหมดเข้าลิสต์อัตโนมัติ
/// </summary>
[CustomEditor(typeof(MonsterSystemManager))]
public class MonsterSystemManagerEditor : Editor
{
    private SerializedProperty _enableAllZones;
    private SerializedProperty _sharedWorldItemPrefab;
    private SerializedProperty _globalHealthMultiplier;
    private SerializedProperty _globalDamageMultiplier;
    private SerializedProperty _globalRespawnSpeedMultiplier;
    private SerializedProperty _globalDropRateMultiplier;
    private SerializedProperty _managedZones;
    private SerializedProperty _onAnyMonsterKilled;
    private SerializedProperty _onAnyMonsterSpawned;

    private void OnEnable()
    {
        _enableAllZones = serializedObject.FindProperty("enableAllZones");
        _sharedWorldItemPrefab = serializedObject.FindProperty("sharedWorldItemPrefab");
        _globalHealthMultiplier = serializedObject.FindProperty("globalHealthMultiplier");
        _globalDamageMultiplier = serializedObject.FindProperty("globalDamageMultiplier");
        _globalRespawnSpeedMultiplier = serializedObject.FindProperty("globalRespawnSpeedMultiplier");
        _globalDropRateMultiplier = serializedObject.FindProperty("globalDropRateMultiplier");
        _managedZones = serializedObject.FindProperty("managedZones");
        _onAnyMonsterKilled = serializedObject.FindProperty("onAnyMonsterKilled");
        _onAnyMonsterSpawned = serializedObject.FindProperty("onAnyMonsterSpawned");
    }

    public override void OnInspectorGUI()
    {
        MonsterSystemManager mgr = (MonsterSystemManager)target;
        serializedObject.Update();

        // กล่องสรุปสถานะด้านบน
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("ตัวคุมระบบมอนสเตอร์ประจำฉาก (MonsterSystem Manager)", EditorStyles.boldLabel);
        int zoneCount = mgr.managedZones != null ? mgr.managedZones.Count : 0;
        string statusText = mgr.enableAllZones ? "เปิดทำงานอยู่" : "หยุดชั่วคราว (ปิดทุกโซน)";
        EditorGUILayout.LabelField($"สถานะระบบ: {statusText}  |  จำนวนโซนที่คุมอยู่: {zoneCount} โซน");

        if (Application.isPlaying && mgr.managedZones != null)
        {
            EditorGUILayout.Space(2);
            for (int i = 0; i < mgr.managedZones.Count; i++)
            {
                MonsterSpawnZone z = mgr.managedZones[i];
                if (z == null) continue;
                string mobName = z.MonsterPrefab != null ? z.MonsterPrefab.name : "ยังไม่ใส่ Prefab";
                EditorGUILayout.LabelField($"  • {z.name} ({mobName}): รอด {z.AliveCount}/{z.TotalSlotCount} ตัว  (กำลังรอเกิด {z.RespawningCount} ตัว)");
            }
            Repaint();
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(6);

        // หมวด 1: ควบคุมหลัก & พรีแฟบของดรอป
        EditorGUILayout.LabelField("1. ควบคุมหลัก & พรีแฟบของดรอป", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_enableAllZones, new GUIContent("เปิดใช้งานทุกโซน", "ติ๊กออกเพื่อหยุดการเกิดของมอนสเตอร์ทุกโซนในฉากนี้ชั่วคราว"));
        EditorGUILayout.PropertyField(_sharedWorldItemPrefab, new GUIContent("พรีแฟบของดรอปกลาง (WorldItem)", "ใส่ WorldItem.prefab ของเพื่อนไว้ตรงนี้ที่เดียว ทุกโซนจะใช้ร่วมกัน (ถ้าไม่ใส่จะใช้ของบน Prefab มอนแทน)"));

        EditorGUILayout.Space(6);

        // หมวด 2: ตัวคูณปรับทั้งแมป
        EditorGUILayout.LabelField("2. ตัวคูณปรับทั้งแมป (ปรับง่ายจากจุดเดียว)", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_globalHealthMultiplier, new GUIContent("ตัวคูณเลือดมอนทั้งแมป (HP x)", "1.0 = ปกติ | 1.5 = ถึกขึ้น 50% | 0.5 = เลือดครึ่งเดียว"));
        EditorGUILayout.PropertyField(_globalDamageMultiplier, new GUIContent("ตัวคูณดาเมจมอนทั้งแมป (ATK x)", "1.0 = ปกติ | 1.5 = ตีแรงขึ้น 50%"));
        EditorGUILayout.PropertyField(_globalRespawnSpeedMultiplier, new GUIContent("ความเร็วเกิดใหม่ (Respawn x)", "1.0 = ปกติ (35-50 วิ) | 3.0 = เกิดเร็วขึ้น 3 เท่า (เหมาะไว้เทส!)"));
        EditorGUILayout.PropertyField(_globalDropRateMultiplier, new GUIContent("ตัวคูณเรทดรอปของทั้งแมป (Drop x)", "1.0 = ปกติ | 2.0 = อีเวนต์คูณดรอป 2 เท่า | 0 = ปิดดรอปทั้งหมด"));

        EditorGUILayout.Space(6);

        // หมวด 3: รายชื่อโซนมอนสเตอร์ในฉากนี้
        EditorGUILayout.LabelField("3. โซนมอนสเตอร์ที่อยู่ภายใต้ตัวคุมนี้", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_managedZones, new GUIContent("รายชื่อโซนทั้งหมด (Managed Zones)", "ถ้าเว้นว่าง ระบบจะดึงโซนลูกทั้งหมดใต้ MonsterSystem_Manager ให้อัตโนมัติ"), true);

        if (GUILayout.Button("ดึงรายชื่อโซนลูกทั้งหมดอัตโนมัติ", GUILayout.Height(26)))
        {
            Undo.RecordObject(mgr, "Refresh Child Zones");
            mgr.RefreshChildZones();
            EditorUtility.SetDirty(mgr);
            serializedObject.Update();
        }

        EditorGUILayout.Space(8);

        // หมวด 4: ช่องเสียบ Event เผื่อเชื่อมระบบเพื่อนในอนาคต
        EditorGUILayout.LabelField("4. ช่องเสียบ Event (เผื่อเชื่อมระบบเควส/รางวัลของเพื่อนทีหลัง)", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_onAnyMonsterKilled, new GUIContent("เมื่อมีมอนสเตอร์ตาย (On Any Monster Killed)"));
        EditorGUILayout.PropertyField(_onAnyMonsterSpawned, new GUIContent("เมื่อมีมอนสเตอร์เกิด (On Any Monster Spawned)"));

        serializedObject.ApplyModifiedProperties();
    }
}
#endif
