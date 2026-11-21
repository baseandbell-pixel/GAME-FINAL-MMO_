# MonsterSystem — แพ็กเกจมอนสเตอร์แบบพกพา (โฟลเดอร์เดียวจบ / Zero Merge Conflicts)

ทุกอย่างของระบบมอนสเตอร์ถูกรวมและแยกสัดส่วนไว้อย่างเป็นระเบียบใน **โฟลเดอร์เดียวเท่านั้น (`Assets/MonsterSystem/`)**:

```text
Assets/MonsterSystem/
├── Models/
│   └── Orc/                              # โมเดล Rigged FBX, คลิปแอนิเมชัน, Textures (เพิ่มตัวใหม่แค่สร้างโฟลเดอร์ใหม่ที่นี่!)
├── Animations/
│   ├── AC_Monster_Base.controller        # Base Animator กลาง (10 Params ใช้ร่วมกันทุกมอนสเตอร์)
│   └── Orc/
│       ├── AOC_Orc.overrideController
│       └── Config_Orc.asset              # ปรับ Stat, ความสูง, จังหวะฟัน, ตาราง Loot Drop (%), Gold, เสียง 3D ในหน้าเดียว!
├── Materials/
│   └── Orc/                              # URP Lit PBR Materials + Packed MetallicSmoothness
├── Prefabs/
│   ├── MonsterSystem_Manager.prefab      # Empty กลางคุมระบบทั้งฉาก (ลากวางตัวเดียวจบ!)
│   ├── Monsters/
│   │   └── Enemy_Orc.prefab              # ตัวมอนสเตอร์ Orc (AI Solo-Pull + Block/Counter + Guard Break + Terrain Safe)
│   └── Zones/
│       └── Zone_Orc.prefab               # โซนเกิด Orc อิสระ (ใช้ใน Manager หรือลากวางเดี่ยวก็ได้)
├── Scripts/
│   ├── MonsterSystemManager.cs           # ตัวคุมกลางในฉาก (Global Multipliers + Event Hook สำหรับ Quest/รางวัลในอนาคต)
│   ├── MonsterAnimationConfig.cs         # ข้อมูลมอนสเตอร์ + ตารางดรอป (%) + ตัวคำนวณโอกาสไม่ออกเลย
│   ├── MonsterAnimatorBridge.cs          # เชื่อม Animator + ล็อกสะโพกกันสไลด์ + Hit/Block Flash
│   ├── MonsterEnemyNPC.cs                # สืบทอด EnemyNPC (ไม่แก้ไฟล์เพื่อน!) + Loot/Gold Drop + Terrain Safe
│   └── MonsterSpawnZone.cs               # ระบบเกิดใหม่รายตัวแบบ MMO + Anti-Pop-In + Host-Migration Safe
├── Editor/
│   ├── MonsterClipImportProcessor.cs     # ตั้งค่า Generic/NoAvatar/In-Place/Loop อัตโนมัติ
│   ├── MonsterAnimatorBuilder.cs         # สร้าง Prefab/Zone/Manager อัตโนมัติ (รองรับทั้งไฟล์แยก @, ไม่แยก @, และ FBX รวมคลิป)
│   └── MonsterSystemEditors.cs           # Custom Inspector สำหรับ Config (คำนวณ % ไม่ออกเลยสด ๆ) & Manager
├── VerificationReport.txt
└── README_SETUP.md
```

> **รับประกัน 0 Conflict:** ไม่มีการแก้ไขไฟล์ของเพื่อนแม้แต่บรรทัดเดียว (`EnemyNPC.cs`, `PlayerCombat.cs`, `SandboxWorldSpawner.cs`, `Enemy.prefab`, `Dev_Kulpat.unity` คงเดิม 100%)

---

## วิธีขนไปลงบน Branch เพื่อน (2 ขั้นตอนจบ)

### ขั้นตอนที่ 1 — ย้ายโฟลเดอร์เดียวเข้าโปรเจกต์เพื่อน
1. ก๊อปปี้โฟลเดอร์ **`Assets/MonsterSystem/`** (พร้อมไฟล์ `Assets/MonsterSystem.meta`) ไปวางใน `Assets/` ของ Branch เพื่อน
2. เปิด Unity รอคอมไพล์เสร็จ
3. *(ถ้ากด Play รอบแรกแล้วมอนไม่เกิด)* ให้กด **`Tools > Fusion > Rebuild Network Project Config`** 1 ครั้ง (หรือคลิกขวา `Enemy_Orc.prefab` → **Reimport**) เพื่อให้ Photon Fusion ลงทะเบียน Prefab ในเครื่องเพื่อน

### ขั้นตอนที่ 2 — วางตัวคุมกลางลงฉากเพื่อน
เลือกทำวิธีใดวิธีหนึ่ง:
- **วิธี A (แนะนำ — วาง Empty ตัวเดียวจบ):** กดเมนู **`Tools > Monster System > 3. Place MonsterSystem_Manager in Active Scene`** (หรือลาก `Assets/MonsterSystem/Prefabs/MonsterSystem_Manager.prefab` ลง Hierarchy) → กางลูกออกมาแล้วเลื่อน **`Zone_Orc`** ไปวางตรงจุดที่ต้องการในแมป (รองรับทั้งพื้นราบและ `Terrain` เนินเขาอัตโนมัติ)
- **วิธี B (วางเฉพาะโซนเดี่ยว):** ลาก `Assets/MonsterSystem/Prefabs/Zones/Zone_Orc.prefab` ลง Hierarchy ได้โดยตรง

---

## วิธีตั้งค่าของดรอป (Loot Drop & % Rate) หลังไปเชื่อมกับของเพื่อน
1. คลิกเลือก **`Assets/MonsterSystem/Animations/Orc/Config_Orc.asset`** (หรือของมอนตัวอื่นในอนาคต)
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
2. วางไฟล์โมเดล FBX (แบบมีกระดูก) + คลิปแอนิเมชัน (รองรับทั้งแยกไฟล์มี `@`, แยกไฟล์ไม่มี `@`, และ 1 ไฟล์ FBX รวมทุกท่า) + Textures (ถ้าไม่มี Texture ระบบจะแปลง Material เป็น URP Lit ให้อัตโนมัติ ไม่ขึ้นสีชมพู)
3. กดเมนู **`Tools > Monster System > 2. Build All Monsters in Models Folder`** → ได้ `Enemy_Goblin.prefab`, `Zone_Goblin.prefab`, `Config_Goblin.asset` และพ่วงเข้า `MonsterSystem_Manager.prefab` ให้ทันทีโดยไม่ยุ่งกับ Orc!

---

## ฟีเจอร์เด่นในแพ็ก
- **Solo-Pull (ลากตีทีละตัว):** ระยะเห็น `5.5m`, `CampId = 0` ไม่ลากทั้งค่ายมารุม
- **Zero-Slide + Impact Timing:** ล็อกขา + ล็อกสะโพกทุกโครงกระดูก (`Hips`/`Pelvis`/`COG`), เช็กโดนดาเมจตรงจังหวะแขนเหวี่ยง ผู้เล่นกลิ้งหลบได้จริง
- **Anti-Stunlock + Block & Counter + Guard Break:** ตีสะดุ้งครบ 2 ที → ยกการ์ด (`GUARD!`), สแปม Combo 1–2 ใส่การ์ด → สวนทันที (`COUNTER!`), ใช้ Combo 3 → `GUARD BREAK!` ติด Stun `1.5 วิ` พร้อมดาวหมุนบนหัว
- **MMO Respawn + Host-Migration Safe:** เกิดใหม่แยกรายตัว `35–50 วิ` + Anti-Pop-In `6m` + โอน Host ในโหมดหลายคนไม่เสกเบิ้ล
- **Scene Hub (`MonsterSystem_Manager`):** ปรับตัวคูณ HP/ATK/Respawn Speed/Drop Rate ทั้งแมปจากจุดเดียว + Event `OnAnyMonsterKilled` เผื่อเชื่อมระบบ Quest ของเพื่อนในอนาคต
