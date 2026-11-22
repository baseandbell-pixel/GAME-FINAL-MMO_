# MonsterSystem — แพ็กเกจมอนสเตอร์แบบพกพา (โฟลเดอร์เดียวจบ / Zero Merge Conflicts)

ทุกอย่างของระบบมอนสเตอร์ทั้ง **7 เผ่า (Slime, TurtleShell, Goblin, Orc, Troll, Golem, BossOgre)** ถูกรวมและแยกสัดส่วนไว้อย่างเป็นระเบียบใน **โฟลเดอร์เดียวเท่านั้น (`Assets/MonsterSystem/`)**:

```text
Assets/MonsterSystem/
├── Models/
│   ├── Slime/                            # สไลม์ (16 ท่าแยก + PBR URP Lit)
│   ├── TurtleShell/                      # เต่าเกราะ (18 ท่าแยก มี Defend/Dizzy + PBR URP Lit)
│   ├── Goblin/                           # ก็อบลิน (11 ท่าในตัว ตี 3 คอมโบ + 4 ชิ้น PBR Textures)
│   ├── Orc/                              # ออร์ค (8 ท่าแยก + PBR Textures)
│   ├── Troll/                            # โทรลล์ (8 ท่าในตัว + 4 URP Lit Materials + 15 PBR Maps)
│   ├── Golem/                            # โกเล็ม (7 ท่าในตัว + URP Lit Material)
│   └── BossOgre/                         # บอสโอเกอร์ (15 ท่าในตัว ตี 3 คอมโบ + URP Lit Body & Eyes)
├── Animations/
│   ├── AC_Monster_Base.controller        # Base Animator กลาง (11 Params / 14 Slots ใช้ร่วมกันทุกมอนสเตอร์)
│   └── <ชื่อเผ่า>/
│       ├── AOC_<ชื่อเผ่า>.overrideController
│       └── Config_<ชื่อเผ่า>.asset       # ปรับ Stat, ความสูง, จังหวะฟัน, isBoss, Loot Drop (%), Gold, เสียง 3D
├── Materials/<ชื่อเผ่า>/                 # URP Lit PBR Materials + _EMISSION (Hit/Block Flash)
├── Prefabs/
│   ├── MonsterSystem_Manager.prefab      # Empty กลางคุมระบบทั้งฉาก (มีครบทั้ง 7 โซนในตัวเดียว!)
│   ├── Monsters/
│   │   ├── Enemy_Slime.prefab            # Slime (1.10m | Lv.1 ~96 HP / 8 ATK | 2 ท่าตี + Dizzy)
│   │   ├── Enemy_TurtleShell.prefab      # TurtleShell (1.30m | Lv.1 ~156 HP / 11 ATK / +3 DEF | Defend + Dizzy)
│   │   ├── Enemy_Goblin.prefab           # Goblin (1.45m | Lv.1 ~132 HP / 12 ATK / +1 DEF | 3 คอมโบ)
│   │   ├── Enemy_Orc.prefab              # Orc (2.15m | Lv.1 ~200 HP / 15 ATK | 2 ท่าตี)
│   │   ├── Enemy_Troll.prefab            # Troll (2.70m | Lv.1 ~258 HP / 18 ATK / +2 DEF | 2 ท่าตี)
│   │   ├── Enemy_Golem.prefab            # Golem (3.00m | Lv.1 ~318 HP / 22 ATK / +4 DEF | ตีหนัก)
│   │   └── Enemy_BossOgre.prefab         # BOSS Ogre (3.20m | Boss HP 750+ / ~28 ATK / +5 DEF | 3 คอมโบ | Block | Slam | Enrage)
│   └── Zones/
│       ├── Zone_Slime.prefab             # 4 ตัว, ระยะ 10m, เกิดใหม่ 25–40 วิ
│       ├── Zone_TurtleShell.prefab       # 4 ตัว, ระยะ 11m, เกิดใหม่ 30–45 วิ
│       ├── Zone_Goblin.prefab            # 4 ตัว, ระยะ 11m, เกิดใหม่ 30–45 วิ
│       ├── Zone_Orc.prefab               # 4 ตัว, ระยะ 12m, เกิดใหม่ 35–50 วิ
│       ├── Zone_Troll.prefab             # 3 ตัว, ระยะ 14m, เกิดใหม่ 40–55 วิ
│       ├── Zone_Golem.prefab             # 2 ตัว, ระยะ 15m, เกิดใหม่ 50–65 วิ
│       └── Zone_BossOgre.prefab          # บอส 1 ตัว, เกิดใหม่ 120–150 วิ
├── Scripts/
│   ├── MonsterSystemManager.cs           # ตัวคุมกลางในฉาก (Global Multipliers + Event Hook สำหรับ Quest)
│   ├── MonsterAnimationConfig.cs         # ข้อมูลมอนสเตอร์ + ตารางดรอป (%) + ตัวคำนวณโอกาสไม่ออกเลย
│   ├── MonsterAnimatorBridge.cs          # เชื่อม Animator + ล็อกสะโพกกันสไลด์ทุก Rig + Hit/Block Flash
│   ├── MonsterEnemyNPC.cs                # สืบทอด EnemyNPC (ไม่แก้ไฟล์เพื่อน!) + Boss Mode + Loot/Gold Drop + Terrain Safe
│   └── MonsterSpawnZone.cs               # ระบบเกิดใหม่รายตัวแบบ MMO + Anti-Pop-In + Host-Migration Safe + Boss Spawn
├── Editor/
│   ├── MonsterClipImportProcessor.cs     # ตั้งค่า Generic/NoAvatar/In-Place/Loop อัตโนมัติ
│   ├── MonsterAnimatorBuilder.cs         # สร้าง Prefab/Zone/Manager อัตโนมัติ + ระบบตรวจสอบครบ 6 เผ่า
│   └── MonsterSystemEditors.cs           # Custom Inspector ภาษาไทยสำหรับ Config & Manager
├── VerificationReport.txt                # รายงานผลตรวจอัตโนมัติ (ครบทั้ง 6 เผ่า)
└── README_SETUP.md
```

> **รับประกัน 0 Conflict:** ไม่มีการแก้ไขไฟล์ของเพื่อนแม้แต่บรรทัดเดียว (`EnemyNPC.cs`, `PlayerCombat.cs`, `SandboxWorldSpawner.cs`, `Enemy.prefab`, `Dev_Kulpat.unity` คงเดิม 100%)

---

## วิธีขนไปลงบน Branch เพื่อน (3 ขั้นตอนจบ)

1. **ย้ายโฟลเดอร์เดียวเข้าโปรเจกต์เพื่อน:** ก๊อปปี้โฟลเดอร์ **`Assets/MonsterSystem/`** (พร้อม `Assets/MonsterSystem.meta`) ไปวางใน `Assets/` ของ branch เพื่อน *(หรือ Merge branch `CharactersEnemy` เข้าไปได้เลย)*
2. **ลงทะเบียน Fusion 1 ครั้ง:** เปิด Unity รอคอมไพล์เสร็จ แล้วกด **`Tools > Fusion > Rebuild Network Project Config`** 1 ครั้ง เพื่อให้ Photon Fusion ลงทะเบียนมอนสเตอร์ทั้ง 6 ตัว (`Enemy_Slime`, `Enemy_TurtleShell`, `Enemy_Orc`, `Enemy_Troll`, `Enemy_Golem`, `Enemy_BossOgre`)
3. **วางลงฉากเพื่อนแล้วกด Play:**
   - **แบบวางตัวเดียวจบ:** ลาก **`Assets/MonsterSystem/Prefabs/MonsterSystem_Manager.prefab`** ลง Hierarchy (หรือกด `Tools > Monster System > 3. Place MonsterSystem_Manager in Active Scene`) แล้วขยับโซนลูกทั้ง 6 โซนไปวางตามจุดต่าง ๆ บนแมป
   - **แบบวางแยกโซนเดี่ยว:** ลาก `Zone_Slime`, `Zone_TurtleShell`, `Zone_Orc`, `Zone_Troll`, `Zone_Golem`, หรือ `Zone_BossOgre` จาก `Assets/MonsterSystem/Prefabs/Zones/` ไปวางตรงจุดที่ต้องการได้เลย

---

## ฟีเจอร์เด่นในแพ็ก
- **6 สายพันธุ์ไล่ระดับครบทั้งแมป:** มอนเริ่มต้น (`Slime`) → มอนเกราะ (`TurtleShell`) → สายสมดุล (`Orc`) → มอนใหญ่ (`Troll`) → แทงก์ยักษ์ (`Golem`) → **บอสใหญ่ (`BossOgre` ตี 3 คอมโบ, SuperArmor ไม่ชะงัก, แถบเลือด Boss สีทองแดง, EXP 300+)**
- **Solo-Pull (ลากตีทีละตัว):** ระยะเห็น `5.5m`, `CampId = 0` ไม่ลากทั้งค่ายมารุม
- **Zero-Slide + Grounding เป๊ะทุก Rig:** รองรับทั้ง Mixamo, Auto-Rig Pro, 3ds Max CAT, Blender Rigify, และ Non-Humanoid (Slime/TurtleShell) ติดพื้น `y = 0` ไม่มี T-Pose
- **Anti-Stunlock + Block & Counter + Guard Break:** ตีสะดุ้งครบ 2 ที → ยกการ์ด (`GUARD!`), สแปม Combo 1–2 → สวน (`COUNTER!`), ใช้ Combo 3 → `GUARD BREAK!` ติด Stun `1.5 วิ` พร้อมดาวหมุนบนหัว
- **MMO Respawn + Host-Migration Safe:** เกิดใหม่แยกรายตัว + Anti-Pop-In `6m` + โอน Host ในโหมดหลายคนไม่เสกเบิ้ล + Fallback Spawn เมื่อเข้าห้อง Shared ที่ Master ยังไม่ได้วางโซน
