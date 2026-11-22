# MonsterSystem — แพ็กเกจมอนสเตอร์แบบพกพา (โฟลเดอร์เดียวจบ / Zero Merge Conflicts)

ทุกอย่างของระบบมอนสเตอร์ทั้ง **3 เผ่า (Orc, Golem, Troll)** ถูกรวมและแยกสัดส่วนไว้อย่างเป็นระเบียบใน **โฟลเดอร์เดียวเท่านั้น (`Assets/MonsterSystem/`)**:

```text
Assets/MonsterSystem/
├── Models/
│   ├── Orc/                              # โมเดล Rigged FBX + คลิปแอนิเมชัน + PBR Textures
│   ├── Golem/                            # โมเดล Rigged FBX (7 ท่าในตัว) + G2.mat + Golem_Albedo.png
│   └── Troll/                            # โมเดล Rigged FBX (8 ท่าในตัว) + Textures/ (4 URP Lit Materials + 15 PBR Maps)
├── Animations/
│   ├── AC_Monster_Base.controller        # Base Animator กลาง (10 Params ใช้ร่วมกันทุกมอนสเตอร์)
│   ├── Orc/
│   │   ├── AOC_Orc.overrideController
│   │   └── Config_Orc.asset              # ปรับ Stat, ความสูง, จังหวะฟัน, ตาราง Loot Drop (%), Gold, เสียง 3D
│   ├── Golem/
│   │   ├── AOC_Golem.overrideController
│   │   └── Config_Golem.asset
│   └── Troll/
│       ├── AOC_Troll.overrideController
│       └── Config_Troll.asset
├── Materials/
│   ├── Orc/                              # URP Lit PBR Materials + Packed MetallicSmoothness + _EMISSION
│   ├── Golem/                            # URP Lit PBR Material + _EMISSION (Hit/Block Flash)
│   └── Troll/                            # 4 URP Lit PBR Materials (Body/Hair/Tanga/Weapon) + _EMISSION
├── Prefabs/
│   ├── MonsterSystem_Manager.prefab      # Empty กลางคุมระบบทั้งฉาก (มี Zone_Orc + Zone_Golem + Zone_Troll ครบในตัวเดียว!)
│   ├── Monsters/
│   │   ├── Enemy_Orc.prefab              # Orc (สูง 2.15m | Lv.1 ~200 HP / 15 ATK | 2 ท่าตี)
│   │   ├── Enemy_Golem.prefab            # Golem (สูง 3.00m | Lv.1 ~318 HP / 22 ATK / +4 DEF | 1 ท่าตีหนัก)
│   │   └── Enemy_Troll.prefab            # Troll (สูง 2.70m | Lv.1 ~258 HP / 18 ATK / +2 DEF | 2 ท่าตี)
│   └── Zones/
│       ├── Zone_Orc.prefab               # โซนเกิด Orc (4 ตัว, ระยะห่าง 12m, เกิดใหม่ 35–50 วิ)
│       ├── Zone_Golem.prefab             # โซนเกิด Golem (2 ตัว, ระยะห่าง 15m, เกิดใหม่ 50–65 วิ)
│       └── Zone_Troll.prefab             # โซนเกิด Troll (3 ตัว, ระยะห่าง 14m, เกิดใหม่ 40–55 วิ)
├── Scripts/
│   ├── MonsterSystemManager.cs           # ตัวคุมกลางในฉาก (Global Multipliers + Event Hook สำหรับ Quest/รางวัลในอนาคต)
│   ├── MonsterAnimationConfig.cs         # ข้อมูลมอนสเตอร์ + ตารางดรอป (%) + ตัวคำนวณโอกาสไม่ออกเลย
│   ├── MonsterAnimatorBridge.cs          # เชื่อม Animator + ล็อกสะโพกกันสไลด์ (Mixamo / Auto-Rig Pro / CAT Rig) + Hit/Block Flash
│   ├── MonsterEnemyNPC.cs                # สืบทอด EnemyNPC (ไม่แก้ไฟล์เพื่อน!) + Loot/Gold Drop + Terrain Safe
│   └── MonsterSpawnZone.cs               # ระบบเกิดใหม่รายตัวแบบ MMO + Anti-Pop-In + Host-Migration Safe
├── Editor/
│   ├── MonsterClipImportProcessor.cs     # ตั้งค่า Generic/NoAvatar/In-Place/Loop อัตโนมัติ
│   ├── MonsterAnimatorBuilder.cs         # สร้าง Prefab/Zone/Manager อัตโนมัติ + ระบบตรวจสอบ 30 ข้อ
│   └── MonsterSystemEditors.cs           # Custom Inspector ภาษาไทยสำหรับ Config & Manager
├── VerificationReport.txt                # รายงานผลตรวจอัตโนมัติ (30/30 PASS)
└── README_SETUP.md
```

> **รับประกัน 0 Conflict:** ไม่มีการแก้ไขไฟล์ของเพื่อนแม้แต่บรรทัดเดียว (`EnemyNPC.cs`, `PlayerCombat.cs`, `SandboxWorldSpawner.cs`, `Enemy.prefab`, `Dev_Kulpat.unity` คงเดิม 100%)

---

## วิธีขนไปลงบน Branch เพื่อน (2 ขั้นตอนจบ)

### ขั้นตอนที่ 1 — ย้ายโฟลเดอร์เดียวเข้าโปรเจกต์เพื่อน
1. ก๊อปปี้โฟลเดอร์ **`Assets/MonsterSystem/`** (พร้อมไฟล์ `Assets/MonsterSystem.meta`) ไปวางใน `Assets/` ของ Branch เพื่อน
2. เปิด Unity รอคอมไพล์เสร็จ
3. กด **`Tools > Fusion > Rebuild Network Project Config`** 1 ครั้ง เพื่อให้ Photon Fusion ลงทะเบียน `Enemy_Orc`, `Enemy_Golem`, และ `Enemy_Troll` เข้า Network Prefab Table ในเครื่องเพื่อน

### ขั้นตอนที่ 2 — วางตัวคุมกลางลงฉากเพื่อน
เลือกทำวิธีใดวิธีหนึ่ง:
- **วิธี A (แนะนำ — วาง Empty ตัวเดียวจบ):** กดเมนู **`Tools > Monster System > 3. Place MonsterSystem_Manager in Active Scene`** (หรือลาก `Assets/MonsterSystem/Prefabs/MonsterSystem_Manager.prefab` ลง Hierarchy) → กางลูกออกมาแล้วเลื่อน **`Zone_Orc`**, **`Zone_Golem`**, **`Zone_Troll`** ไปวางตามจุดที่ต้องการในแมป (รองรับทั้งพื้นราบและ `Terrain` เนินเขาอัตโนมัติ)
- **วิธี B (วางเฉพาะโซนเดี่ยว):** ลาก `Zone_Orc.prefab`, `Zone_Golem.prefab`, หรือ `Zone_Troll.prefab` จาก `Assets/MonsterSystem/Prefabs/Zones/` ลง Hierarchy ได้โดยตรง

---

## วิธีตั้งค่าของดรอป (Loot Drop & % Rate) หลังไปเชื่อมกับของเพื่อน
1. คลิกเลือก **`Config_Orc.asset`**, **`Config_Golem.asset`**, หรือ **`Config_Troll.asset`** ใน `Assets/MonsterSystem/Animations/<ชื่อเผ่า>/`
2. ในหมวด **`Loot Drop Table`**:
   - กด `+` เพิ่มช่อง แล้วลาก **`ItemData`** ของเพื่อนมาวาง
   - ลากแถบ **`Drop Rate Percent`** (`0% – 100%`) และตั้งจำนวน **`Min / Max Amount`**
   - เลือกโหมดได้ 2 แบบ:
     - **`IndependentPerItem`:** ทอยแยกทีละชิ้นตาม `%` ถ้าไม่ติดสักชิ้น = **ไม่ออกเลย**
     - **`SinglePickTable`:** ทอย 1 ครั้งจาก 100% ส่วนที่เหลือ = **ไม่ออกเลย**
   - ด้านบน Inspector จะคำนวณให้ดูสด ๆ ว่า **"Chance of NO DROP: XX.X%"**
3. ตารางดรอปจะไม่หายแม้จะกด Rebuild Prefab ใหม่ และของที่ดรอปจะยิง Raycast วางบนพื้น/เนินจริง (ไม่จมดิน)
4. *(ถ้าอยากให้ดรอปเงิน Gold ด้วย)* ปรับ `Gold Drop Chance Percent` จาก `0%` เป็นเรทที่ต้องการได้เลย

---

## วิธีเพิ่มมอนสเตอร์ตัวใหม่ในอนาคต (แยกโซนใครโซนมัน 100%)
1. สร้างโฟลเดอร์ใหม่ไว้ใน `Assets/MonsterSystem/Models/<ชื่อมอนสเตอร์>/` เช่น `Models/Goblin/`
2. วางไฟล์โมเดล FBX (แบบมีกระดูก) + คลิปแอนิเมชัน (รองรับทั้งแยกไฟล์มี `@`, แยกไฟล์ไม่มี `@`, และ 1 ไฟล์ FBX รวมทุกท่า) + `.mat` / Textures (ระบบจะกรองใช้เฉพาะ `.mat` จริงหรือประกอบจาก PNG เป็น URP Lit ให้อัตโนมัติ ไม่ขึ้นสีชมพู/สีเทา)
3. กดเมนู **`Tools > Monster System > 2. Build All Monsters in Models Folder`** → ได้ `Enemy_Goblin.prefab`, `Zone_Goblin.prefab`, `Config_Goblin.asset` และพ่วงเข้า `MonsterSystem_Manager.prefab` ให้ทันทีโดยไม่ยุ่งกับ Orc/Golem/Troll!

---

## ฟีเจอร์เด่นในแพ็ก
- **3 สายพันธุ์พร้อมใช้งาน:** **Orc** (สายสมดุล), **Troll** (อึด+ตีแรง 2 ท่า), **Golem** (ยักษ์แทงก์พลังโจมตีหนัก + DEF สูง)
- **Solo-Pull (ลากตีทีละตัว):** ระยะเห็น `5.5m`, `CampId = 0` ไม่ลากทั้งค่ายมารุม
- **Zero-Slide + Impact Timing:** ล็อกขา + ล็อกสะโพกทุกโครงกระดูก (`Hips`/`Pelvis`/`root.x`/`cave_troll_Hub001`), เช็กโดนดาเมจตรงจังหวะแขนเหวี่ยง ผู้เล่นกลิ้งหลบได้จริง
- **Anti-Stunlock + Block & Counter + Guard Break:** ตีสะดุ้งครบ 2 ที → ยกการ์ด (`GUARD!`), สแปม Combo 1–2 ใส่การ์ด → สวนทันที (`COUNTER!`), ใช้ Combo 3 → `GUARD BREAK!` ติด Stun `1.5 วิ` พร้อมดาวหมุนบนหัว
- **MMO Respawn + Host-Migration Safe:** เกิดใหม่แยกรายตัว + Anti-Pop-In `6m` + โอน Host ในโหมดหลายคนไม่เสกเบิ้ล + Fallback Spawn เมื่อเข้าห้อง Shared ที่ Master ยังไม่ได้วางโซน
- **Scene Hub (`MonsterSystem_Manager`):** ปรับตัวคูณ HP/ATK/Respawn Speed/Drop Rate ทั้งแมปจากจุดเดียว + Event `OnAnyMonsterKilled` เผื่อเชื่อมระบบ Quest ของเพื่อนในอนาคต
