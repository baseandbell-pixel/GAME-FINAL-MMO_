#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// PropertyDrawer ภาษาไทยสำหรับแต่ละแถวของตารางของดรอป (<see cref="MonsterLootDropEntry"/>)
/// </summary>
[CustomPropertyDrawer(typeof(MonsterLootDropEntry))]
public class MonsterLootDropEntryDrawer : PropertyDrawer
{
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return (EditorGUIUtility.singleLineHeight + 3f) * 3f + 6f;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        SerializedProperty itemProp = property.FindPropertyRelative("item");
        SerializedProperty rateProp = property.FindPropertyRelative("dropRatePercent");
        SerializedProperty minProp = property.FindPropertyRelative("minAmount");
        SerializedProperty maxProp = property.FindPropertyRelative("maxAmount");

        float lineH = EditorGUIUtility.singleLineHeight;
        float y = position.y + 2f;

        Rect rItem = new Rect(position.x, y, position.width, lineH);
        EditorGUI.PropertyField(rItem, itemProp, new GUIContent("ไอเทมที่ดรอป (ItemData)", "ลากไฟล์ ItemData ของเพื่อนมาใส่ช่องนี้"));
        y += lineH + 3f;

        Rect rRate = new Rect(position.x, y, position.width, lineH);
        EditorGUI.Slider(rRate, rateProp, 0f, 100f, new GUIContent("โอกาสดรอป (%)", "0% = ไม่ออกเลย | 100% = ออกแน่นอน"));
        y += lineH + 3f;

        float halfW = (position.width - 8f) * 0.5f;
        Rect rMin = new Rect(position.x, y, halfW, lineH);
        Rect rMax = new Rect(position.x + halfW + 8f, y, halfW, lineH);
        EditorGUI.PropertyField(rMin, minProp, new GUIContent("จำนวนต่ำสุด"));
        EditorGUI.PropertyField(rMax, maxProp, new GUIContent("จำนวนสูงสุด"));

        EditorGUI.EndProperty();
    }
}

/// <summary>
/// Custom Inspector ภาษาไทยทั้งหมดสำหรับ <see cref="MonsterAnimationConfig"/>
/// </summary>
[CustomEditor(typeof(MonsterAnimationConfig))]
public class MonsterAnimationConfigEditor : Editor
{
    private bool _showAnimAdvanced;
    private bool _showAudio;

    public override void OnInspectorGUI()
    {
        MonsterAnimationConfig cfg = (MonsterAnimationConfig)target;
        serializedObject.Update();

        // 1. พรีวิว Stat Lv.1 (อ้างอิงฐานของ TMRKKxOG: 120 HP / 8 ATK)
        int lv1Hp = Mathf.Max(20, Mathf.RoundToInt(120 * cfg.healthMultiplier));
        int lv1Atk = Mathf.Max(2, Mathf.RoundToInt(8 * cfg.damageMultiplier));
        int lv1Def = Mathf.Max(0, cfg.defenseBonus);

        string archName = cfg.archetype switch
        {
            MonsterArchetype.Skirmisher => "นักล่าคล่องตัว (Skirmisher)",
            MonsterArchetype.Brute => "จอมพลัง (Brute)",
            _ => "นักรบ (Warrior)"
        };

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField($"ข้อมูลมอนสเตอร์: {cfg.monsterName} ({(cfg.isBoss ? "บอส BOSS" : archName)})", EditorStyles.boldLabel);
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
        EditorGUILayout.LabelField("สรุปโอกาสของดรอปโบนัสประจำสายพันธุ์ (คำนวณสด)", EditorStyles.boldLabel);
        if (validItems == 0)
        {
            EditorGUILayout.HelpBox("ตารางดรอปโบนัสยังว่างอยู่ (ตอนนี้ใช้ระบบดรอปส่วนตัวของเพื่อนเป็นหลัก) — กด + ในหมวด 'รายการของดรอป' ด้านล่างเพื่อเพิ่มของดรอปเฉพาะมอนตัวนี้", MessageType.Info);
        }
        else
        {
            string modeLabel = cfg.lootRollMode == MonsterLootRollMode.IndependentPerItem
                ? "ทอยแยกทีละชิ้น (Independent)"
                : "ทอยเลือก 1 ชิ้นจาก 100% (Single Pick)";
            EditorGUILayout.LabelField($"โหมดการทอย: {modeLabel}  |  ตั้งไว้: {validItems} ชิ้น");
            EditorGUILayout.LabelField($"โอกาสได้ของอย่างน้อย 1 ชิ้น: {anyDropPct:0.0}%   |   โอกาสไม่ออกเลย: {noDropPct:0.0}%");

            if (cfg.lootRollMode == MonsterLootRollMode.SinglePickTable)
            {
                float sum = cfg.CalculateTotalConfiguredPercent();
                if (sum > 100.01f)
                    EditorGUILayout.HelpBox($"ผลรวม % ในโหมด Single Pick เกิน 100% (รวมได้ {sum:0.0}%) — กรุณาลดเรทให้รวมไม่เกิน 100%", MessageType.Warning);
            }
        }
        if (cfg.goldDropChancePercent > 0f)
            EditorGUILayout.LabelField($"ดรอปเงิน (Gold) เสริม: โอกาส {cfg.goldDropChancePercent:0.0}% ได้ {Mathf.Min(cfg.minGold, cfg.maxGold)} - {Mathf.Max(cfg.minGold, cfg.maxGold)} เหรียญ");
        else
            EditorGUILayout.LabelField("ดรอปเงิน (Gold) เสริม: ปิดอยู่ (0%)");
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(6);

        // หมวด 1: ชื่อ ระดับ & นิสัย AI
        EditorGUILayout.LabelField("1. ชื่อ ระดับ & นิสัย AI ของมอนสเตอร์", EditorStyles.boldLabel);
        Prop("monsterName", "ชื่อมอนสเตอร์", "ชื่อที่จะแสดงบนหลอดเลือดและตอนล็อกเป้า");
        Prop("archetype", "นิสัยการต่อสู้ (Archetype)", "Warrior = ยกการ์ด/สวน | Skirmisher = ไว/พุ่งกระโจน | Brute = Super Armor/ฟาดหนักมีวงแดง");
        Prop("isBoss", "เป็นบอส (BOSS)", "ติ๊กเพื่อเปิด Super Armor ตีไม่สะดุ้ง");
        Prop("targetWorldHeight", "ความสูงโมเดลในโลกจริง (เมตร)", "เช่น Orc 2.15 ม. | Goblin 1.35 ม. | บอส 3.2 ม.");

        EditorGUILayout.Space(5);

        // หมวด 2: สเตตัสสายพันธุ์
        EditorGUILayout.LabelField("2. ตัวคูณสเตตัสเฉพาะสายพันธุ์", EditorStyles.boldLabel);
        Prop("healthMultiplier", "ตัวคูณเลือด (HP x)", "1.67 = เลือด ~200 ที่ Lv.1 และยังโตตามเลเวลโซน");
        Prop("damageMultiplier", "ตัวคูณพลังโจมตี (ATK x)", "1.50 = โจมตี 15 ที่ Lv.1");
        Prop("defenseBonus", "พลังป้องกันเสริม (+DEF)", "ค่าป้องกันพื้นฐานเพิ่มจากเลเวลโซน");

        EditorGUILayout.Space(5);

        // หมวด 3: ระยะตี & จังหวะฟัน
        EditorGUILayout.LabelField("3. ระยะโจมตี & จังหวะดาเมจลง", EditorStyles.boldLabel);
        Prop("attackRange", "ระยะตีประชิด (เมตร)", "ระยะที่มอนเริ่มง้างตี (ปกติ 2.5 ม.)");
        Prop("attackImpactDelay", "หน่วงจังหวะฟันปกติ (วินาที)", "เวลานับจากเริ่มง้างจนแขนฟันโดน (ช่วงนี้กลิ้งหลบ/Parry ได้)");
        Prop("counterImpactDelay", "หน่วงจังหวะฟันสวน (วินาที)", "เวลานับจากสวนกลับจนแขนฟันโดน");

        EditorGUILayout.Space(5);

        // หมวด 4: ตารางของดรอป
        EditorGUILayout.LabelField("4. ตารางของดรอปโบนัส & อัตราดรอป (%)", EditorStyles.boldLabel);
        Prop("lootRollMode", "โหมดการทอยดรอป", "IndependentPerItem = ทอยแยกทีละชิ้น | SinglePickTable = ทอยเลือก 1 ชิ้นจาก 100%");
        Prop("maxDropsPerKill", "จำกัดจำนวนชิ้นสูงสุดต่อตัว (0 = ไม่จำกัด)", "ใช้เฉพาะโหมด IndependentPerItem");
        Prop("lootDrops", "รายการของดรอป (กด + เพื่อเพิ่ม)", "ลาก ItemData ของเพื่อนมาวางแล้วปรับ % ได้เลย", includeChildren: true);

        EditorGUILayout.Space(5);

        // หมวด 5: ดรอปเงินเสริม
        EditorGUILayout.LabelField("5. ดรอปเงิน (Gold) เสริม", EditorStyles.boldLabel);
        Prop("goldDropChancePercent", "โอกาสดรอปเงิน (%)", "ตั้ง 0% เพื่อปิด");
        Prop("minGold", "จำนวนเงินต่ำสุด (Gold)", "จำนวนเหรียญต่ำสุดเมื่อสุ่มติด");
        Prop("maxGold", "จำนวนเงินสูงสุด (Gold)", "จำนวนเหรียญสูงสุดเมื่อสุ่มติด");

        EditorGUILayout.Space(5);

        // หมวด 6: แอนิเมชัน & เอฟเฟกต์ขั้นสูง (พับเก็บได้)
        _showAnimAdvanced = EditorGUILayout.Foldout(_showAnimAdvanced, "6. ตั้งค่าแอนิเมชัน & แสงกระพริบตอนโดนตี (ขั้นสูง)", true, EditorStyles.foldoutHeader);
        if (_showAnimAdvanced)
        {
            EditorGUI.indentLevel++;
            Prop("canFlinch", "สะดุ้งเมื่อโดนตี (Flinch)", "ถ้าปิดจะตีไม่สะดุ้งตลอดเวลา (Super Armor)");
            Prop("hitReactCooldown", "คูลดาวน์ท่าสะดุ้ง (วินาที)", "กันท่าสะดุ้งรัวเกินไป");
            Prop("attackVariantCount", "จำนวนท่าโจมตีที่มี (1-3)", "ระบบจะสุ่มสลับท่าให้ไม่ซ้ำ");
            Prop("skillVariantCount", "จำนวนท่าสกิลที่มี (1-3)", "สำหรับมอนที่มีท่าสกิล");
            Prop("speedDampTime", "ความนุ่มนวลเปลี่ยนท่าเดิน/วิ่ง", "ค่าน้อย = ตอบสนองไว | ค่ามาก = สมูทขึ้น");
            Prop("walkSpeedThreshold", "ความเร็วท่าเดิน (Walk)", "ความเร็วอ้างอิงใน Blend Tree");
            Prop("runSpeedThreshold", "ความเร็วท่าวิ่ง (Run)", "ความเร็วอ้างอิงใน Blend Tree");
            Prop("moveStopEpsilon", "ระยะหยุดนิ่ง (Epsilon)", "ต่ำกว่านี้จะถือว่าหยุดเดิน");
            Prop("lockHipsXZInPlace", "ล็อกสะโพกกันสไลด์ (In-Place)", "แนะนำให้เปิดไว้เสมอเพื่อกันตัวสไลด์หลุด Collider");
            Prop("enableHitFlash", "เปิดแสงกระพริบตอนโดนตี/บล็อก", "กระพริบสีแดงตอนโดนตี / สีทองตอนบล็อก");
            Prop("hitFlashDuration", "ระยะเวลากระพริบแสง (วินาที)", "ค่าเริ่มต้น 0.11 วินาที");
            Prop("hitFlashEmissionColor", "สีแสงกระพริบตอนโดนตี", "สี Emission ชั่วคราวตอนโดนฟัน");
            Prop("blockFlashEmissionColor", "สีแสงกระพริบตอนบล็อก", "สี Emission ชั่วคราวตอนยกการ์ดรับ");
            EditorGUI.indentLevel--;
        }

        // หมวด 7: เสียง 3D (พับเก็บได้)
        _showAudio = EditorGUILayout.Foldout(_showAudio, "7. เสียงเอฟเฟกต์ 3D (ไม่บังคับ — เว้นว่างได้)", true, EditorStyles.foldoutHeader);
        if (_showAudio)
        {
            EditorGUI.indentLevel++;
            Prop("audioVolume", "ความดังเสียง (0-1)", "ความดังเสียง 3D ของมอนตัวนี้");
            Prop("attackSwingClips", "เสียงเหวี่ยงอาวุธ/ฟัน", "ใส่หลายไฟล์เพื่อสุ่มเสียงไม่ซ้ำ", includeChildren: true);
            Prop("hitReactClips", "เสียงตอนโดนตี", "ใส่หลายไฟล์เพื่อสุ่มเสียงไม่ซ้ำ", includeChildren: true);
            Prop("guardRaiseClip", "เสียงตอนยกการ์ด (GUARD!)", "เล่นตอนมอนยกโล่/ตั้งการ์ด");
            Prop("guardBlockClip", "เสียงตอนบล็อกได้ (BLOCKED!)", "เสียงดาบปะทะการ์ดดังเคร้ง");
            Prop("guardBreakClip", "เสียงตอนการ์ดแตก (GUARD BREAK!)", "เล่นตอนโดน Combo 3 แตกการ์ด");
            Prop("deathClip", "เสียงตอนตาย", "เล่นเมื่อมอนสเตอร์ล้มตาย");
            EditorGUI.indentLevel--;
        }

        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(8);

        // ปุ่มลัด
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button($"อัปเดต Prefab '{cfg.monsterName}' ตัวเดียว", GUILayout.Height(28)))
            MonsterAnimatorBuilder.BuildSingleMonsterByName(cfg.monsterName);
        if (GUILayout.Button($"วาง Zone_{cfg.monsterName} ลงฉากนี้", GUILayout.Height(28)))
            MonsterAnimatorBuilder.PlaceZoneInActiveScene(cfg.monsterName);
        EditorGUILayout.EndHorizontal();
    }

    private void Prop(string name, string label, string tooltip, bool includeChildren = false)
    {
        SerializedProperty p = serializedObject.FindProperty(name);
        if (p != null)
            EditorGUILayout.PropertyField(p, new GUIContent(label, tooltip), includeChildren);
    }
}

/// <summary>
/// Custom Inspector ภาษาไทยทั้งหมดสำหรับ <see cref="MonsterSystemManager"/> (Empty Hub รวมและคุมโซนทั้งหมดในฉาก)
/// </summary>
[CustomEditor(typeof(MonsterSystemManager))]
public class MonsterSystemManagerEditor : Editor
{
    private static readonly string[] QuickAddRow1 = { "Orc", "Goblin", "Slime", "TurtleShell" };
    private static readonly string[] QuickAddRow2 = { "Troll", "Golem", "BossOgre" };

    public override void OnInspectorGUI()
    {
        MonsterSystemManager mgr = (MonsterSystemManager)target;
        serializedObject.Update();

        // นับจำนวนโซนลูกจริงใน Hierarchy แบบสด
        MonsterSpawnZone[] childZones = mgr.GetComponentsInChildren<MonsterSpawnZone>(includeInactive: true);
        int totalSlots = 0;
        int totalAlive = 0;
        for (int i = 0; i < childZones.Length; i++)
        {
            if (childZones[i] == null) continue;
            totalSlots += childZones[i].TotalSlotCount;
            if (Application.isPlaying) totalAlive += childZones[i].AliveCount;
        }

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("ศูนย์รวมและคุมโซนมอนสเตอร์ทั้งหมด (MonsterZones Hub)", EditorStyles.boldLabel);
        string statusText = mgr.enableAllZones ? "เปิดทำงานอยู่" : "หยุดชั่วคราว (ปิดทุกโซน)";
        EditorGUILayout.LabelField($"สถานะ: {statusText}  |  โซนภายใต้ตัวนี้: {childZones.Length} โซน ({totalSlots} ตัว)");

        if (Application.isPlaying)
        {
            EditorGUILayout.LabelField($"มอนที่มีชีวิตอยู่ตอนนี้ทั้งหมด: {totalAlive}/{totalSlots} ตัว");
            Repaint();
        }
        EditorGUILayout.EndVertical();

        // กล่องจัดการโซนด่วน (เพิ่มโซน / รวบโซน / เรียงเลข Camp)
        bool isSceneObject = !EditorUtility.IsPersistent(mgr);
        if (isSceneObject && !Application.isPlaying)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("เพิ่มแคมป์มอนสเตอร์ใหม่ใต้ตัวคุมนี้ (คลิกวางได้หลายแคมป์)", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            foreach (string sp in QuickAddRow1)
            {
                if (GUILayout.Button($"+ {sp}", GUILayout.Height(24)))
                {
                    MonsterAnimatorBuilder.AddZoneAsChildOfHub(mgr, sp);
                    serializedObject.Update();
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            foreach (string sp in QuickAddRow2)
            {
                if (GUILayout.Button($"+ {sp}", GUILayout.Height(24)))
                {
                    MonsterAnimatorBuilder.AddZoneAsChildOfHub(mgr, sp);
                    serializedObject.Update();
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(3);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("รวบโซนทั้งหมดในฉากมาไว้ใต้ตัวนี้", GUILayout.Height(25)))
            {
                MonsterAnimatorBuilder.GroupAllSceneZonesUnderHub(mgr);
                serializedObject.Update();
            }
            if (GUILayout.Button("เรียงเลข Camp 1..N ตามลำดับลูก", GUILayout.Height(25)))
            {
                MonsterAnimatorBuilder.AutoNumberChildZones(mgr);
                serializedObject.Update();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        // ตารางสรุปแคมป์ลูกทั้งหมดพร้อมปุ่มคลิกเลือก/ซูมดูทันที
        if (childZones.Length > 0)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"รายการแคมป์ที่คุมอยู่ ({childZones.Length} แคมป์)", EditorStyles.boldLabel);
            for (int i = 0; i < childZones.Length; i++)
            {
                MonsterSpawnZone z = childZones[i];
                if (z == null) continue;
                EditorGUILayout.BeginHorizontal();
                string info = Application.isPlaying
                    ? $"• {z.GetMapMarkerLabel()}  [รอด {z.AliveCount}/{z.TotalSlotCount}]"
                    : $"• {z.GetMapMarkerLabel()}  [{z.SpawnCount} ตัว]";
                EditorGUILayout.LabelField(info);
                if (isSceneObject && GUILayout.Button("เลือก", GUILayout.Width(46)))
                {
                    Selection.activeGameObject = z.gameObject;
                }
                if (isSceneObject && GUILayout.Button("ซูมดู", GUILayout.Width(48)))
                {
                    Selection.activeGameObject = z.gameObject;
                    if (SceneView.lastActiveSceneView != null)
                        SceneView.lastActiveSceneView.FrameSelected();
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.Space(6);

        EditorGUILayout.LabelField("1. ควบคุมหลัก & พรีแฟบของดรอป", EditorStyles.boldLabel);
        Prop("enableAllZones", "เปิดใช้งานทุกโซน", "ติ๊กออกเพื่อหยุดการเกิดของมอนสเตอร์ทุกโซนในฉากนี้ชั่วคราว");
        Prop("sharedWorldItemPrefab", "พรีแฟบของดรอปกลาง (WorldItem)", "ใส่ WorldItem.prefab ของเพื่อนไว้ตรงนี้ที่เดียว ทุกโซนจะใช้ร่วมกัน");

        EditorGUILayout.Space(6);

        EditorGUILayout.LabelField("2. ตัวคูณปรับทั้งแมป (ปรับง่ายจากจุดเดียว)", EditorStyles.boldLabel);
        Prop("globalHealthMultiplier", "ตัวคูณเลือดมอนทั้งแมป (HP x)", "1.0 = ปกติ | 1.5 = ถึกขึ้น 50% | 0.5 = เลือดครึ่งเดียว");
        Prop("globalDamageMultiplier", "ตัวคูณดาเมจมอนทั้งแมป (ATK x)", "1.0 = ปกติ | 1.5 = ตีแรงขึ้น 50%");
        Prop("globalRespawnSpeedMultiplier", "ความเร็วเกิดใหม่ (Respawn x)", "1.0 = ปกติ | 3.0 = เกิดเร็วขึ้น 3 เท่า (เหมาะไว้เทส!)");
        Prop("globalDropRateMultiplier", "ตัวคูณเรทดรอปของทั้งแมป (Drop x)", "1.0 = ปกติ | 2.0 = อีเวนต์คูณดรอป 2 เท่า | 0 = ปิดดรอปทั้งหมด");

        EditorGUILayout.Space(6);

        EditorGUILayout.LabelField("3. โซนมอนสเตอร์ที่อยู่ภายใต้ตัวคุมนี้", EditorStyles.boldLabel);
        Prop("autoSyncChildZones", "ซิงก์โซนลูกอัตโนมัติ", "เปิดไว้เพื่อให้ลาก Zone_* เข้า/ออกใต้ Empty นี้แล้วอัปเดตรายชื่อเองทันที");
        Prop("managedZones", "รายชื่อโซนทั้งหมด (Managed Zones)", "รายชื่อโซนที่ตัวคุมนี้ดูแลอยู่", includeChildren: true);

        if (GUILayout.Button("รีเฟรชรายชื่อโซนลูกทั้งหมด", GUILayout.Height(24)))
        {
            Undo.RecordObject(mgr, "Refresh Child Zones");
            mgr.RefreshChildZones();
            EditorUtility.SetDirty(mgr);
            serializedObject.Update();
        }

        EditorGUILayout.Space(8);

        EditorGUILayout.LabelField("4. โซนปลอดภัยสำรอง (ทำงานอัตโนมัติถ้าลบ SandboxWorldSpawner)", EditorStyles.boldLabel);
        Prop("ensureFallbackSafeZoneIfMissing", "สร้าง SafeZone สำรองอัตโนมัติถ้าฉากยังไม่มี", "เปิดไว้เสมอถ้าลบ SandboxWorldSpawner ออก (มีวงสีเขียวให้ดูใน Scene View)");
        Prop("fallbackSafeZoneCenter", "พิกัดศูนย์กลาง SafeZone สำรอง", "ค่าเริ่มต้น (0, 0, -60)");
        Prop("fallbackSafeZoneRadius", "รัศมี SafeZone สำรอง (เมตร)", "ค่าเริ่มต้น 12 เมตร");

        EditorGUILayout.Space(8);

        EditorGUILayout.LabelField("5. ช่องเสียบ Event (เผื่อเชื่อมระบบเควส/รางวัลของเพื่อนทีหลัง)", EditorStyles.boldLabel);
        Prop("onAnyMonsterKilled", "เมื่อมีมอนสเตอร์ตาย (On Any Monster Killed)", "ลากสคริปต์ระบบเควส/รางวัลของเพื่อนมาเสียบที่นี่ได้");
        Prop("onAnyMonsterSpawned", "เมื่อมีมอนสเตอร์เกิด (On Any Monster Spawned)", "แจ้งเตือนทุกครั้งที่มีมอนเกิดใหม่");

        serializedObject.ApplyModifiedProperties();
    }

    private void Prop(string name, string label, string tooltip, bool includeChildren = false)
    {
        SerializedProperty p = serializedObject.FindProperty(name);
        if (p != null)
            EditorGUILayout.PropertyField(p, new GUIContent(label, tooltip), includeChildren);
    }
}

/// <summary>
/// Custom Inspector ภาษาไทยทั้งหมดสำหรับ <see cref="MonsterSpawnZone"/>
/// </summary>
[CustomEditor(typeof(MonsterSpawnZone))]
public class MonsterSpawnZoneEditor : Editor
{
    public override void OnInspectorGUI()
    {
        MonsterSpawnZone zone = (MonsterSpawnZone)target;
        serializedObject.Update();

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        string mobName = zone.MonsterPrefab != null ? zone.MonsterPrefab.name : "ยังไม่ใส่พรีแฟบมอนสเตอร์";
        EditorGUILayout.LabelField($"แคมป์มอนสเตอร์: {zone.GetMapMarkerLabel()}  ({mobName})", EditorStyles.boldLabel);
        if (Application.isPlaying)
        {
            EditorGUILayout.LabelField($"สถานะสด → รอดอยู่: {zone.AliveCount}/{zone.TotalSlotCount} ตัว  |  กำลังรอเกิดใหม่: {zone.RespawningCount} ตัว");
            Repaint();
        }
        else
        {
            EditorGUILayout.LabelField($"จำนวนมอนในโซนนี้: {zone.SpawnCount} ตัว  |  โชว์บน World Map: {(zone.ShowOnWorldMap ? "เปิด" : "ปิด")}");
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(6);

        EditorGUILayout.LabelField("1. มอนสเตอร์และจำนวนในโซน", EditorStyles.boldLabel);
        Prop("monsterPrefab", "พรีแฟบมอนสเตอร์", "ลาก Enemy_Orc.prefab (หรือมอนตัวอื่น) มาวาง");
        Prop("spawnCount", "จำนวนมอนสเตอร์สูงสุด", "จำนวนตัวที่จะเกิดในโซนนี้");
        Prop("customSpawnPoints", "จุดเกิดแบบกำหนดเอง (ไม่บังคับ)", "ถ้าเว้นว่าง ระบบจะกระจายจุดเกิดรอบโซนอัตโนมัติ", includeChildren: true);
        Prop("slotSpacing", "ระยะห่างระหว่างแต่ละตัว (เมตร)", "แนะนำ 10-14 เมตร เพื่อให้ผู้เล่นลากตีทีละตัวง่าย");
        Prop("territoryRadius", "รัศมีวงเขตโซน (Scene View & World Map / เมตร)", "ใช้แสดงทั้งใน Scene View และวงเขตบน World Map (M)");

        EditorGUILayout.Space(6);

        EditorGUILayout.LabelField("2. เลเวลประจำโซน & กติกาการเกิดใหม่", EditorStyles.boldLabel);
        Prop("useFixedZoneLevel", "ใช้เลเวลคงที่ประจำโซน (MMORPG)", "เปิดไว้ = ใช้ช่วงเลเวลด้านล่าง | ปิด = สเกลตามผู้เล่นเลเวลสูงสุด");
        Prop("zoneMinLevel", "เลเวลต่ำสุดของโซน", "เช่น Lv.1 สำหรับโซนเริ่มต้น");
        Prop("zoneMaxLevel", "เลเวลสูงสุดของโซน", "เช่น Lv.2 สำหรับโซนเริ่มต้น");
        Prop("respawnMode", "รูปแบบการเกิดใหม่", "IndividualStaggered = ตายตัวไหนเกิดตัวนั้น | CampClearWave = ตายหมดฝูงค่อยเกิดพร้อมกัน");
        Prop("respawnMinSeconds", "เวลาเกิดใหม่ต่ำสุด (วินาที)", "สุ่มเวลาระหว่างต่ำสุด-สูงสุด จะได้ไม่เกิดพร้อมกันแบบหุ่นยนต์");
        Prop("respawnMaxSeconds", "เวลาเกิดใหม่สูงสุด (วินาที)", "ค่าแนะนำ 35-50 วินาที");
        Prop("antiPopInPlayerRadius", "ระยะกันเกิดทับหัวผู้เล่น (เมตร)", "ถ้าผู้เล่นยืนใกล้จุดเกิดกว่านี้จะรอจนผู้เล่นเดินออกห่างก่อน");
        Prop("antiPopInRetryDelay", "ระยะเวลารอเช็กซ้ำ (วินาที)", "เช็กซ้ำทุกกี่วินาทีตอนผู้เล่นยืนขวางจุดเกิด");

        EditorGUILayout.Space(6);

        EditorGUILayout.LabelField("2.5 แผนที่ (World Map) & กล่องสมบัติเมื่อเคลียร์แคมป์", EditorStyles.boldLabel);
        Prop("campNumber", "หมายเลขแคมป์บนแผนที่ (0 = รันเลขอัตโนมัติ)", "ใส่ 0 เพื่อรันเลขต่อจากแคมป์ที่มีอยู่อัตโนมัติ (ถ้าลบ SandboxWorldSpawner จะเริ่มที่ Camp 1 เอง) หรือพิมพ์เลขที่ต้องการได้เลย");
        Prop("campDisplayName", "ชื่อแคมป์ที่กำหนดเอง (เว้นว่าง = ใช้ชื่อมอน)", "พิมพ์ได้ทั้งชื่ออย่างเดียว (เช่น ป่าสไลม์ → แสดง 'Camp 5 ป่าสไลม์') หรือพิมพ์เต็ม (เช่น 'Camp 9 ลานบอสยักษ์' / 'แคมป์ 1 ลานฝึก')");
        EditorGUILayout.HelpBox($"ตัวอย่างป้ายที่จะโชว์บน World Map [M]:  {zone.GetMapMarkerLabel()}", MessageType.None);
        Prop("showOnWorldMap", "แสดงหมุดแคมป์และวงเขตบน World Map (M)", "เปิดไว้เพื่อให้ผู้เล่นเปิดแมพ [M] เห็นแคมป์นี้และเวลานับถอยหลังเกิดใหม่");
        Prop("spawnRewardChestOnZoneClear", "เสกกล่องสมบัติ (LootChest) เมื่อเคลียร์แคมป์", "เมื่อฆ่ามอนครบทุกตัวในโซนจะเสกหีบสมบัติของเพื่อนตรงกลางโซน + ขึ้นไอคอนบนแผนที่ (แนะนำให้ใช้โหมด CampClearWave)");
        Prop("awardClearGold", "แจกโบนัส Gold เมื่อเคลียร์แคมป์", "แจกเงินรางวัลตามเลเวลโซนเมื่อเคลียร์ครบทุกตัว");

        EditorGUILayout.Space(6);

        EditorGUILayout.LabelField("3. สีเส้นเขตใน Editor & Log", EditorStyles.boldLabel);
        Prop("gizmoTerritoryColor", "สีวงเขตโซน (Scene View)", "สีวงกลมบอกอาณาเขตใน Scene View");
        Prop("gizmoAnchorColor", "สีจุดเกิดแต่ละตัว (Scene View)", "สีลูกกลมบอกตำแหน่งเกิดแต่ละตัว");
        Prop("verboseLogs", "แสดง Log รายละเอียดใน Console", "ปิดไว้เพื่อไม่ให้ Console รก");

        serializedObject.ApplyModifiedProperties();
    }

    private void Prop(string name, string label, string tooltip, bool includeChildren = false)
    {
        SerializedProperty p = serializedObject.FindProperty(name);
        if (p != null)
            EditorGUILayout.PropertyField(p, new GUIContent(label, tooltip), includeChildren);
    }
}

/// <summary>
/// Custom Inspector ภาษาไทยทั้งหมดสำหรับ <see cref="MonsterEnemyNPC"/> (AI มอนสเตอร์แบบ Hybrid)
/// </summary>
[CustomEditor(typeof(MonsterEnemyNPC))]
public class MonsterEnemyNPCEditor : Editor
{
    private bool _showCombatAdvanced;
    private bool _showTerrainAdvanced;
    private bool _showBaseRewards;

    public override void OnInspectorGUI()
    {
        MonsterEnemyNPC npc = (MonsterEnemyNPC)target;
        serializedObject.Update();

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField($"ระบบ AI มอนสเตอร์ (MonsterEnemyNPC): {npc.name}", EditorStyles.boldLabel);
        if (Application.isPlaying && npc.IsSpawned)
        {
            string guardText = npc.IsGuarding ? " | กำลังยกการ์ด!" : string.Empty;
            string stunText = (npc.IsStunnedNet || npc.State == NpcState.Staggered) ? " | ติด Stun/Stagger!" : string.Empty;
            EditorGUILayout.LabelField($"Lv.{npc.ScaledLevel} | สถานะ: {npc.State} | HP: {npc.CurrentHealth}/{npc.ScaledMaxHealth}{guardText}{stunText}");
            Repaint();
        }
        else
        {
            EditorGUILayout.LabelField("รองรับ: ย่องลอบสังหาร 3.0x | Perfect Parry | กันสไลด์ | Guard/Counter/Guard Break");
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(5);

        EditorGUILayout.LabelField("1. ตัวเชื่อมแอนิเมชัน & ค่าพลังพื้นฐาน", EditorStyles.boldLabel);
        Prop("animatorBridge", "ตัวเชื่อมแอนิเมชัน (Animator Bridge)", "ลิงก์กับ MonsterAnimatorBridge บนตัวมอนสเตอร์");
        Prop("MaxHealth", "เลือดพื้นฐาน (ก่อนคูณสายพันธุ์)", "ค่าเลือดจริงจะถูกคูณเพิ่มด้วย Config_<มอน>.asset");
        Prop("AttackDamage", "พลังโจมตีพื้นฐาน", "ค่าโจมตีจริงจะถูกคูณเพิ่มด้วย Config_<มอน>.asset");
        Prop("Defense", "พลังป้องกันพื้นฐาน", "ค่าป้องกันพื้นฐาน");
        Prop("AttackCooldown", "คูลดาวน์ระหว่างการฟัน (วินาที)", "ระยะเวลาพักก่อนฟันครั้งถัดไป");
        Prop("AttackRange", "ระยะโจมตีประชิด (เมตร)", "ระยะเริ่มง้างฟันผู้เล่น");

        EditorGUILayout.Space(5);

        EditorGUILayout.LabelField("2. การมองเห็น การลากตีทีละตัว & การเคลื่อนที่", EditorStyles.boldLabel);
        Prop("personalAggroRadius", "ระยะเห็นผู้เล่น (เมตร)", "ระยะที่มอนเริ่มไล่ล่า (5.5 ม. ลากตีทีละตัวกำลังดี)");
        Prop("personalLeashRadius", "ระยะไล่ไกลสุดก่อนวิ่งกลับจุดเกิด (เมตร)", "เกินระยะนี้จะเลิกไล่แล้ววิ่งกลับไปฮีลเลือดเต็ม");
        Prop("personalPatrolRadius", "รัศมีเดินลาดตระเวน (เมตร)", "ระยะเดินไปมาแถวจุดเกิดตอนว่าง");
        Prop("personalIdleWait", "เวลายืนพักก่อนเดินต่อ (ต่ำสุด-สูงสุด วิ)", "สุ่มเวลายืนพักตอนลาดตระเวน");
        Prop("walkMoveSpeed", "ความเร็วเดินลาดตระเวน", "ความเร็วตอนเดินตรวจตรา");
        Prop("runMoveSpeed", "ความเร็ววิ่งไล่ผู้เล่น", "ความเร็วตอนวิ่งไล่โจมตี");
        Prop("returnMoveSpeed", "ความเร็ววิ่งกลับจุดเกิด", "ความเร็วตอนวิ่งกลับบ้าน");
        Prop("rotationSpeed", "ความเร็วในการหันหน้า", "ความไวในการหมุนตัวหาผู้เล่น");
        Prop("corpseDespawnDelay", "เวลาศพหายหลังตาย (วินาที)", "รอกี่วินาทีหลังล้มตายก่อนลบศพออก");

        EditorGUILayout.Space(5);

        EditorGUILayout.LabelField("3. ระบบยกการ์ด / สวนกลับ / การ์ดแตก (Anti-Stunlock)", EditorStyles.boldLabel);
        Prop("hitsBeforeGuard", "โดนตีกี่ทีถึงจะยกการ์ด", "เช่น 2 = โดนตีสะดุ้ง 2 ฮิต ฮิตถัดไปจะยกการ์ดทันที");
        Prop("comboResetWindow", "เวลารีเซ็ตตัวนับฮิต (วินาที)", "ถ้าไม่โดนตีเกินเวลานี้จะนับฮิตใหม่");
        Prop("guardDuration", "ระยะเวลายกการ์ด (วินาที)", "ยืนยกการ์ดอยู่นานกี่วินาที");
        Prop("guardCooldown", "คูลดาวน์ก่อนยกการ์ดรอบใหม่ (วินาที)", "กันไม่ให้มอนยกการ์ดรัวเกินไป");
        Prop("reactiveGuardChanceOnCooldown", "โอกาสยกการ์ดดักตอนผู้เล่นง้างตี (0-1)", "เช่น 0.28 = มีโอกาส 28% ยกการ์ดดัก");
        Prop("allowLowHpEmergencyGuard", "ยกการ์ดฉุกเฉินเมื่อเลือด < 55%", "เปิดไว้สำหรับมอนสายแทงก์/บอส | ปิดสำหรับ Slime/Goblin จะได้ไม่ยกการ์ดรัวตอนฟาร์ม");
        Prop("guardBonusDefense", "พลังป้องกันเพิ่มขณะยกการ์ด", "ลดดาเมจประมาณ 75% ขณะตั้งการ์ด");
        Prop("counterDamageMultiplier", "ตัวคูณดาเมจท่าฟันสวน (COUNTER!)", "เมื่อผู้เล่นสแปมตีใส่การ์ดจะโดนสวนแรงขึ้น");
        Prop("guardBreakStunDuration", "ระยะเวลาติดมึนเมื่อการ์ดแตก (วินาที)", "โดน Heavy / Stealth / Combo 3 แตกการ์ดจะติด Stun เปิดช่องตีฟรี");
        Prop("hitFlinchPause", "เวลาหยุดขาตอนสะดุ้ง (วินาที)", "ป้องกันการสไลด์ขาขณะโดนตี");

        EditorGUILayout.Space(5);

        EditorGUILayout.LabelField("3.5 ฟีลลิ่งการต่อสู้ (เดินคุมเชิง / แสงเตือน Parry / แรงกระแทก)", EditorStyles.boldLabel);
        Prop("enableCombatStrafe", "เปิดการเดินคุมเชิงวนข้าง (Combat Strafe)", "เดินวนช้า ๆ รอบผู้เล่นช่วงรอคูลดาวน์ฟัน ไม่ยืนจ้องแข็งทื่อ");
        Prop("combatStrafeSpeedFactor", "ตัวคูณความเร็วเดินคุมเชิง", "0.72 = เดินวนกำลังสวยสมจริง");
        Prop("enableParryTelegraphGlint", "เปิดประกายแสงเตือนจังหวะ Parry", "แสงสีแดง-ส้มสว่างวาบก่อนดาเมจลง บอกจังหวะกด Perfect Parry!");
        Prop("parryTelegraphLeadTime", "เวลาเปิดแสงเตือนก่อนฟันโดน (วินาที)", "0.22 วิ ตรงกับหน้าต่าง Perfect Parry 0.25 วิ ของเพื่อน");
        Prop("microKnockbackDistance", "ระยะผงะถอยหลังตอนโดนตีหนัก (เมตร)", "เพิ่มน้ำหนักการปะทะตอนตีหนัก/การ์ดแตก");

        EditorGUILayout.Space(5);

        EditorGUILayout.LabelField("4. การเชื่อมต่อระบบเพื่อน (Stealth / Parry / หลอดเลือด)", EditorStyles.boldLabel);
        Prop("hybridVisionConeAngle", "มุมมองสายตาด้านหน้า (องศา)", "110° = ย่องเข้าด้านหลังเพื่อกด Stealth Assassinate (3.0x) ได้จริง");
        Prop("hybridBackHearingRadius", "ระยะได้ยินฝีเท้าด้านหลัง (เมตร)", "เข้าชิดด้านหลังเกินระยะนี้มอนจะหันมารู้ตัว");
        Prop("showPersonalZoneGroundRing", "แสดงวงกลมสีส้มบนพื้นของเพื่อน", "ปิดไว้เพื่อให้พื้นแมปเนียนแบบ MMO");
        Prop("worldHealthBarLocalY", "ความสูงหลอดเลือด World-Space (Local Y)", "ปรับตำแหน่งหลอดเลือดบนหัวมอนให้พอดี");
        Prop("lockPointLocalY", "ความสูงจุดล็อกเป้า Lock-On (Local Y)", "ปรับจุดล็อกเป้าให้อยู่กลางอกมอน");

        EditorGUILayout.Space(5);

        _showCombatAdvanced = EditorGUILayout.Foldout(_showCombatAdvanced, "5. จังหวะฟัน & กันสไลด์ (ขั้นสูง)", true, EditorStyles.foldoutHeader);
        if (_showCombatAdvanced)
        {
            EditorGUI.indentLevel++;
            Prop("attackImpactDelay", "จังหวะดาเมจลงตอนฟันปกติ (วินาที)", "เช็กโดนดาเมจตอนแขนเหวี่ยงลง (หลบ/Parry ได้)");
            Prop("counterImpactDelay", "จังหวะดาเมจลงตอนฟันสวน (วินาที)", "เช็กโดนดาเมจตอนฟันสวนกลับ");
            Prop("impactReachSlack", "ระยะเผื่อตอนแขนฟันโดน (เมตร)", "ระยะเผื่อเล็กน้อยตอนเช็กฮิต");
            Prop("minAttackAnimLock", "เวลาล็อกขาขั้นต่ำตอนง้างฟัน (วินาที)", "กันมอนเดินสไลด์ก่อนแอนิเมชันฟันเริ่มเล่น");
            EditorGUI.indentLevel--;
        }

        _showTerrainAdvanced = EditorGUILayout.Foldout(_showTerrainAdvanced, "6. การหลบสิ่งกีดขวาง เนินเขา & Debug", true, EditorStyles.foldoutHeader);
        if (_showTerrainAdvanced)
        {
            EditorGUI.indentLevel++;
            Prop("obstacleProbeDist", "ระยะตรวจจับสิ่งกีดขวางด้านหน้า (เมตร)", "ระยะยิงเรย์หลบหิน/กำแพง");
            Prop("obstacleProbeRad", "รัศมีลำแสงตรวจสิ่งกีดขวาง (เมตร)", "ความกว้างของตัวมอนตอนหลบสิ่งกีดขวาง");
            Prop("allySepRadius", "ระยะเว้นห่างจากมอนตัวอื่น (เมตร)", "กันมอนเดินซ้อนทับกันเอง");
            Prop("walkableStepHeight", "ความสูงขั้นบันได/เนินที่เดินขึ้นได้ (เมตร)", "ต่ำกว่านี้ถือเป็นพื้น ไม่ใช่กำแพง");
            Prop("verboseLogs", "แสดง Log การต่อสู้ละเอียดใน Console", "ปิดไว้เพื่อไม่ให้ Console รก");
            EditorGUI.indentLevel--;
        }

        _showBaseRewards = EditorGUILayout.Foldout(_showBaseRewards, "7. รางวัล EXP / พรีแฟบของดรอป & อีเวนต์พื้นฐาน", true, EditorStyles.foldoutHeader);
        if (_showBaseRewards)
        {
            EditorGUI.indentLevel++;
            Prop("experienceReward", "ค่าประสบการณ์ที่ได้รับ (EXP)", "EXP พื้นฐานเมื่อฆ่ามอนตัวนี้ได้");
            Prop("lootPrefab", "พรีแฟบของดรอปบนพื้น (WorldItem)", "อ้างอิง WorldItem.prefab ของเพื่อน");
            Prop("onHit", "อีเวนต์เมื่อโดนตี (On Hit)", "UnityEvent ของ EnemyNPC เดิม");
            Prop("onDeath", "อีเวนต์เมื่อตาย (On Death)", "UnityEvent ของ EnemyNPC เดิม");
            EditorGUI.indentLevel--;
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void Prop(string name, string label, string tooltip, bool includeChildren = false)
    {
        SerializedProperty p = serializedObject.FindProperty(name);
        if (p != null)
            EditorGUILayout.PropertyField(p, new GUIContent(label, tooltip), includeChildren);
    }
}

/// <summary>
/// Custom Inspector ภาษาไทยทั้งหมดสำหรับ <see cref="MonsterAnimatorBridge"/>
/// </summary>
[CustomEditor(typeof(MonsterAnimatorBridge))]
public class MonsterAnimatorBridgeEditor : Editor
{
    public override void OnInspectorGUI()
    {
        MonsterAnimatorBridge bridge = (MonsterAnimatorBridge)target;
        serializedObject.Update();

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("ตัวเชื่อมแอนิเมชันมอนสเตอร์ (MonsterAnimatorBridge)", EditorStyles.boldLabel);
        if (Application.isPlaying)
        {
            EditorGUILayout.LabelField($"ความเร็ว: {bridge.CurrentSpeed:0.00} | ต่อสู้: {(bridge.IsCombat ? "ใช่" : "ไม่")} | ตาย: {(bridge.IsDead ? "ใช่" : "ไม่")}");
            Repaint();
        }
        else
        {
            EditorGUILayout.LabelField("คุม 10 พารามิเตอร์กลาง + ล็อกสะโพกกันสไลด์ + กระพริบแสง PBR");
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(5);
        Prop("animator", "ตัวคุมแอนิเมชัน (Animator)", "Animator บนตัวโมเดล 3D ที่ใส่ AOC_<มอน> ไว้");
        Prop("config", "ไฟล์ตั้งค่ามอนสเตอร์ (Config)", "ลาก Config_<ชื่อมอน>.asset มาวางที่นี่");
        Prop("visualRoot", "โหนดหลักโมเดล 3D (Visual Root)", "Child ที่เก็บโมเดล 3D และโครงกระดูก");
        Prop("visualRenderers", "เมชทั้งหมดของโมเดล (Renderers)", "รายชื่อ SkinnedMeshRenderer สำหรับกระพริบแสงตอนโดนตี/บล็อก", includeChildren: true);

        serializedObject.ApplyModifiedProperties();
    }

    private void Prop(string name, string label, string tooltip, bool includeChildren = false)
    {
        SerializedProperty p = serializedObject.FindProperty(name);
        if (p != null)
            EditorGUILayout.PropertyField(p, new GUIContent(label, tooltip), includeChildren);
    }
}

/// <summary>
/// Custom Inspector ภาษาไทยทั้งหมดสำหรับ <see cref="SandboxWorldSpawner"/> ของเพื่อน
/// </summary>
[CustomEditor(typeof(SandboxWorldSpawner))]
public class SandboxWorldSpawnerEditor : Editor
{
    private static readonly string[] CampSlotLabels =
    {
        "Camp 1 — Starter ซ้าย (Lv.1-2)",
        "Camp 2 — Starter ขวา (Lv.1-2)",
        "Camp 3 — Mid-Field ซ้าย (Lv.3-5)",
        "Camp 4 — Mid-Field ขวา (Lv.3-5)"
    };

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        SerializedProperty enemyPrefab = serializedObject.FindProperty("enemyPrefab");
        SerializedProperty campEnemyPrefabs = serializedObject.FindProperty("campEnemyPrefabs");
        SerializedProperty bossPrefab = serializedObject.FindProperty("bossPrefab");

        string defaultEnemyName = (enemyPrefab != null && enemyPrefab.objectReferenceValue != null)
            ? enemyPrefab.objectReferenceValue.name
            : "ยังไม่ใส่";
        string bossName = (bossPrefab != null && bossPrefab.objectReferenceValue != null)
            ? bossPrefab.objectReferenceValue.name
            : $"{defaultEnemyName} (ใช้ตัวสำรอง)";

        // กล่องสรุปแคมป์ทั้ง 4 แคมป์และบอส
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("ตัวเสกมอนสเตอร์ประจำฉาก (SandboxWorldSpawner)", EditorStyles.boldLabel);
        for (int i = 0; i < 4; i++)
        {
            string slotName = defaultEnemyName + " (เดิม)";
            if (campEnemyPrefabs != null && i < campEnemyPrefabs.arraySize)
            {
                var elem = campEnemyPrefabs.GetArrayElementAtIndex(i);
                if (elem.objectReferenceValue != null)
                    slotName = elem.objectReferenceValue.name + " ★";
            }
            EditorGUILayout.LabelField($"  • {CampSlotLabels[i]}: {slotName}");
        }
        EditorGUILayout.LabelField($"  • ลานบอส (Boss Arena): {bossName}");
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(6);

        // หมวด 1: มอนสเตอร์และ 4 แคมป์
        EditorGUILayout.LabelField("1. มอนสเตอร์ & การตั้งค่าแคมป์ (4 แคมป์)", EditorStyles.boldLabel);
        Prop("enemyPrefab", "พรีแฟบมอนสเตอร์หลัก (ค่าเริ่มต้น)", "ใช้สำหรับแคมป์ที่ไม่ได้ระบุพรีแฟบเฉพาะ (เช่น แคปซูลของเพื่อน)");

        if (campEnemyPrefabs != null)
        {
            EditorGUILayout.Space(3);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("พรีแฟบมอนสเตอร์แยกทีละแคมป์ (เว้นว่าง = ใช้ตัวหลักด้านบน)", EditorStyles.miniBoldLabel);
            if (campEnemyPrefabs.arraySize < 4)
                campEnemyPrefabs.arraySize = 4;

            for (int i = 0; i < 4; i++)
            {
                SerializedProperty elem = campEnemyPrefabs.GetArrayElementAtIndex(i);
                EditorGUILayout.PropertyField(elem, new GUIContent(CampSlotLabels[i], "ลาก Prefab มอนสเตอร์ (เช่น Enemy_Orc หรือ Enemy_Goblin) มาใส่ช่องนี้เพื่อเปลี่ยนเฉพาะแคมป์นี้"));
            }
            if (GUILayout.Button("ใส่ Enemy_Orc ลง Camp 1 อัตโนมัติ (คง Camp 2-4 เดิม)", GUILayout.Height(24)))
            {
                serializedObject.ApplyModifiedProperties();
                MonsterAnimatorBuilder.AssignOrcToCamp1InActiveScene(saveScene: false, removeExistingManager: false);
                serializedObject.Update();
            }
            if (GUILayout.Button("★ จัดวางมอนสเตอร์ครบทั้ง 7 ตัวลงฉากนี้ (4 แคมป์หลัก + Slime & Turtle + BossOgre)", GUILayout.Height(28)))
            {
                serializedObject.ApplyModifiedProperties();
                MonsterAnimatorBuilder.AssignAllCampsAndBossToSpawnerInActiveScene(saveScene: true);
                serializedObject.Update();
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(3);
        }

        Prop("campId", "รหัสแคมป์เริ่มต้น (Camp ID)", "ค่าเริ่มต้น 1");
        Prop("enemyPoints", "จุดเกิดกำหนดเองสำหรับ Camp 1 (ถ้ามี)", "ถ้าจุดชิดเกินไป ระบบจะใช้พิกัดกระจายโซนอัตโนมัติ", includeChildren: true);
        Prop("individualEnemyPatrolRadius", "รัศมีเดินลาดตระเวน (โซน Starter / เมตร)", "ระยะเดินส่วนตัวของมอนแต่ละตัวใน Camp 1 และ 2");
        Prop("midFieldEnemyPatrolRadius", "รัศมีเดินลาดตระเวน (โซน Mid-Field / เมตร)", "ระยะเดินส่วนตัวของมอนแต่ละตัวใน Camp 3 และ 4");
        Prop("minIndividualZoneSpacing", "ระยะห่างขั้นต่ำระหว่างจุดเกิด (เมตร)", "กันมอนเกิดชิดกันเกินไป");
        Prop("campRespawnDelaySeconds", "เวลาเกิดใหม่หลังเคลียร์แคมป์ (วินาที)", "ค่าเริ่มต้น 90 วินาที (1 นาที 30 วิ)");

        EditorGUILayout.Space(6);

        // หมวด 2: ลานบอส
        EditorGUILayout.LabelField("2. ลานบอสใหญ่ (Boss Arena)", EditorStyles.boldLabel);
        Prop("bossPrefab", "พรีแฟบบอส 3D (Boss Prefab)", "เช่น Boss Golem (ถ้าเว้นว่างจะใช้พรีแฟบมอนหลักแทน)");
        Prop("bossArenaCenter", "พิกัดกลางลานบอส (X, Y, Z)", "ค่าเริ่มต้น (0, 0, 74) ทางทิศเหนือ");
        Prop("bossArenaProximityRadius", "รัศมีเขตลานบอส (เมตร)", "ระยะแจ้งเตือน/แสดงแถบเลือดบอสเมื่อผู้เล่นเข้าใกล้");
        Prop("bossRespawnDelaySeconds", "เวลาเกิดใหม่ของบอส (วินาที)", "ค่าเริ่มต้น 240 วินาที (4 นาที)");

        EditorGUILayout.Space(6);

        // หมวด 3: ช่วงเลเวลประจำโซน
        EditorGUILayout.LabelField("3. ช่วงเลเวลประจำโซน (Zone Levels)", EditorStyles.boldLabel);
        Prop("starterCampMinLevel", "เลเวลต่ำสุดโซน Starter (Camp 1-2)", "ค่าเริ่มต้น Lv.1");
        Prop("starterCampMaxLevel", "เลเวลสูงสุดโซน Starter (Camp 1-2)", "ค่าเริ่มต้น Lv.2");
        Prop("midFieldCampMinLevel", "เลเวลต่ำสุดโซน Mid-Field (Camp 3-4)", "ค่าเริ่มต้น Lv.3");
        Prop("midFieldCampMaxLevel", "เลเวลสูงสุดโซน Mid-Field (Camp 3-4)", "ค่าเริ่มต้น Lv.5");
        Prop("bossArenaZoneLevel", "เลเวลบอส (Boss Level)", "ค่าเริ่มต้น Lv.5+");

        EditorGUILayout.Space(6);

        // หมวด 4: เขตปลอดภัย
        EditorGUILayout.LabelField("4. เขตปลอดภัย (Comfort Safe Zone)", EditorStyles.boldLabel);
        Prop("safeZoneCenter", "พิกัดกลาง Safe Zone (X, Y, Z)", "จุดเกิดผู้เล่นและจุดปลอดภัย ห้ามมอนเข้ามา");
        Prop("safeZoneRadius", "รัศมี Safe Zone (เมตร)", "ค่าเริ่มต้น 12 เมตร");

        EditorGUILayout.Space(6);

        // หมวด 5: ของดรอปและกล่องสมบัติ
        EditorGUILayout.LabelField("5. ของดรอป & กล่องสมบัติเคลียร์แคมป์", EditorStyles.boldLabel);
        Prop("itemPrefab", "พรีแฟบไอเทมบนพื้น (WorldItem)", "ใช้สำหรับเสกของดรอปและกล่องสมบัติ");
        Prop("items", "ไอเทมที่วางแจกในฉาก (Items)", "รายการของวางตามจุดในฉากตอนเริ่มเกม", includeChildren: true);
        Prop("spawnRewardChestOnCampClear", "เสกกล่องสมบัติเมื่อเคลียร์แคมป์", "ติ๊กเปิดเพื่อให้มีหีบสมบัติโผล่เมื่อฆ่ามอนครบทั้งแคมป์");

        EditorGUILayout.Space(6);

        // หมวด 6: ตั้งค่าอื่น ๆ
        EditorGUILayout.LabelField("6. ตั้งค่าเพิ่มเติม & ฉากทดสอบ", EditorStyles.boldLabel);
        Prop("settleSeconds", "หน่วงเวลาก่อนเสกมอน (วินาที)", "ตั้ง 0 เพื่อเสกทันทีเมื่อเข้าฉาก");
        Prop("createTestObstacles", "สร้างกำแพง/สิ่งกีดขวางทดสอบ", "สร้างฉากกั้นและเสาลานบอสอัตโนมัติสำหรับฉาก Sandbox");

        serializedObject.ApplyModifiedProperties();
    }

    private void Prop(string name, string label, string tooltip, bool includeChildren = false)
    {
        SerializedProperty p = serializedObject.FindProperty(name);
        if (p != null)
            EditorGUILayout.PropertyField(p, new GUIContent(label, tooltip), includeChildren);
    }
}
#endif
