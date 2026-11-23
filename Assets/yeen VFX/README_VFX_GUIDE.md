# คู่มือระบบเอฟเฟกต์ทั้งหมดในโฟลเดอร์ `yeen VFX`

เอกสารนี้รวบรวมโครงสร้างโฟลเดอร์ โค้ดเอฟเฟกต์ เครื่องมือสร้าง (Editor Generators) และพรีแฟบ/แอสเซ็ตทั้งหมดที่ถูกจัดรวมไว้ภายใน `Assets/yeen VFX/` เพียงที่เดียว (พร้อมเชื่อมต่อเข้ากับระบบเกมหลักใน `PlayerCombat.cs`, `EnemyNPC.cs`, `InventoryManager.cs`, `LootChest.cs` และ `WorldItem.cs` อัตโนมัติแล้ว)

---

## 1. โค้ดเอฟเฟกต์สำหรับ "ตัวละครผู้เล่น (Player)" — `01_Player_VFX_Scripts/`
*(ใน Unity กด Add Component → `yeen VFX` จะเห็นเมนูภาษาไทยครบทุกตัว)*

| โฟลเดอร์ | สคริปต์ | หน้าที่ / รูปแบบเอฟเฟกต์ | ตำแหน่งติดตั้ง | วิธีทำงาน / ทดสอบ |
|---|---|---|---|---|
| `01_SwordSlash_AttachToPlayer` | `SwordWindSlashVFX.cs` | ปล่อยคลื่นลมดาบเวลาฟัน | **แปะบน Player** (`Player.prefab`) | ฟันดาบในเกม (คลิกซ้าย) |
| `01_SwordSlash_AttachToPlayer` | `WuxiaSlashController.cs`, `WuxiaSyncedSlashController.cs` | ตัวควบคุมคลื่นดาบอากาศ/ลมขาว 3 จุด | **แปะบน Player / ตัวทดสอบดาบ** | ใช้คู่กับ Tool `02` หรือ `03` |
| `02_Sprint_AttachToPlayer` | `RunWindSlipstream.cs` | เส้นสายลมสีขาวพลิ้วไหวสไตล์กำลังภายในที่ **ระดับขา/ข้อเท้า** (ไม่มีใบไม้/ไม่มีละออง/ไม่มีวงแหวน) | **แปะบน Player** (`Player.prefab`) | วิ่งปกติ / กด `Shift` พุ่งวิ่ง |
| `02_Sprint_AttachToPlayer` | `SimpleSprintTrail.cs`, `WuxiaSprintController.cs`, `ExistingRunVFXActivator.cs` | ตัวเสริมเส้นลมวิ่งติดเท้า | **แปะบน Player** (ตัวเลือกเสริม) | ใช้คู่กับ Tool `04` / `05` |
| `03_BlockAndDodge_AttachToPlayer` | `MMOBlockParryController.cs` | **โล่พลังงานวงกลม 3 มิติ** (`Mesh_CircularGuardShield` อัตราส่วน 1:1) ด้านหน้าตัวละคร | **แปะบน Player** (`Player.prefab`) | กดคลิกขวาค้าง / ปุ่ม `F` (Block) |
| `03_BlockAndDodge_AttachToPlayer` | `MMOGuardBreakVFX.cs` | **ตีบล็อคแตกโล่กระจาย 4 เลเยอร์** (ใยรอยร้าว + เศษผลึก 3D 26 ชิ้น + สะเก็ดไฟ + คลื่นกระแทก + สั่นกล้อง) | **ออโต้ตอน Spawn** (`PlayerCombat`) | โดนตีจนเกราะแตก หรือกด `B` ทดสอบ |
| `03_BlockAndDodge_AttachToPlayer` | `MMODodgeSmokeController.cs` | **กลิ้งหลบสายลมสีขาวระดับขา** (ไม่มีใบไม้/ไม่มีละออง/ไม่มีวงแหวน) | **แปะบน Player** (`Player.prefab`) | กด `Space` / `Alt` กลิ้งหลบ |
| `04_PotionsAndLevelUp_AttachToPlayer` | `MMOHealingPotionVFX.cs` | ยารักษา: หิ้งห้อยแสงเขียวมรกตลอยวนรอบตัว 4 เลเยอร์ | **ออโต้ตอน Spawn** (`PlayerCombat`) | ใช้ยาฮีลในกระเป๋า หรือกด `H` / `1` |
| `04_PotionsAndLevelUp_AttachToPlayer` | `MMOStaminaPotionVFX.cs` | ยาสเตมิน่า: ผีเสื้อแสงสีฟ้าครามบินวนรอบตัว 4 เลเยอร์ | **ออโต้ตอน Spawn** (`PlayerCombat`) | ใช้ยาสเตมิน่า หรือกด `V` / `2` |
| `04_PotionsAndLevelUp_AttachToPlayer` | `MMOLevelUpController.cs` | เลเวลอัพ: วงแหวนและเสาแสงทอง **พุ่งลอยขึ้นด้านบนแล้วจางหายหมดจด** | **ออโต้ตอน Spawn** (`PlayerCombat`) | เลเวลอัพในเกม หรือกด `L` ทดสอบ |
| `05_ComboUI_AttachToPlayer` | `MMOHeartbeatComboUI.cs` | **ตัวเลขคอมโบแบบไร้กรอบ** ฝั่งกลาง-ขวาจอ เต้นจังหวะหัวใจ (ไม่มีการไล่แรงก์ C–SSS) | **ออโต้ตอน Spawn** (`PlayerCombat`) | ตีศัตรูต่อเนื่อง |

---

## 2. โค้ดเอฟเฟกต์ "ตอนตีศัตรู / มอนตาย / หีบสมบัติ" — `02_Combat_And_World_Scripts/`

| โฟลเดอร์ | สคริปต์ | หน้าที่ / รูปแบบเอฟเฟกต์ | ตำแหน่งติดตั้ง | วิธีทำงาน / ทดสอบ |
|---|---|---|---|---|
| `01_CriticalHit_AutoOnEnemyHit` | `MMOCriticalHitVFX.cs` | ฟันคริติคอลสีแดงขนาดใหญ่ 4 เลเยอร์ตรงจุดปะทะ (พร้อมมอนกระพริบแสง) | **อัตโนมัติตอนตีติดคริ** (`PlayerCombat`) | ตีศัตรูติด Critical ในเกม |
| `02_DeathBlood_AutoOnEnemyDeath` | `MMOMonsterDeathBloodVFX.cs` | เลือดพุ่งและไหลนองบนพื้นเมื่อมอนสเตอร์/บอสตาย 4 เลเยอร์ | **อัตโนมัติตอนมอนตาย** (`EnemyNPC`) | ฆ่ามอนสเตอร์หรือบอส |
| `03_TreasureChest_AttachToChest` | `MMOTreasureChestController.cs` | เปิดฝาหีบ + **เอฟเฟกต์หีบสีทอง 4 เลเยอร์** + **เส้นแสงไอเทมลอยโค้งตามระดับความหายาก** (ขาวเงิน / ฟ้า / ม่วง / ทอง) แล้วจางหายเมื่อตกพื้น | **ออโต้บนหีบ** (`LootChest` + `WorldItem`) | เดินไปกด `E` เปิดหีบสมบัติ |
| `04_WeaponHitImpact_OnMonsterHit` | `MMOWeaponHitImpactVFX.cs` | **ฟันโดนมอนสเตอร์ 4 เลเยอร์ + มอนกระพริบสีแดง-ส้ม 0.09s** (รองรับทั้ง Static API และติดดาบ `OnTriggerEnter`) | **ออโต้ตอน Spawn / ติดตัวดาบ** (`PlayerCombat`) | ฟันมอนสเตอร์ในเกม หรือกด `Y` ทดสอบ |

---

## 3. แอสเซ็ตทั้งหมด (Prefab / Material / Texture / Mesh / Shader) — `03_VFX_Prefabs_And_Assets/`
ไฟล์ทั้งหมดที่สร้างจากเครื่องมือ Editor (ทั้ง 14 เครื่องมือ) จะถูกเก็บไว้รวมกันภายในโฟลเดอร์นี้ทั้งหมด ไม่กระจายออกนอก `yeen VFX`:

- `Prefabs/` — พรีแฟบสำเร็จรูป (`VFX_LevelUp_GoldenPillar_PutOnPlayer.prefab`, `VFX_SwordSlash_*`)
- `Materials/`, `Textures/`, `Meshes/`, `Shaders/` — วัสดุ เทกซ์เจอร์ เมช และเชดเดอร์สำหรับดาบและสายลมวิ่ง
- `Generated_Sprint/`, `Generated_DodgeSmoke/`, `Generated_BlockShield/`, `Generated_TreasureChest/`, `Generated_CriticalHit/`, `Generated_DeathBlood/`, `Generated_HealingPotion/`, `Generated_StaminaPotion/`, `Generated_GuardBreak/`, `Generated_WeaponHitImpact/` — โฟลเดอร์ปลายทางของเครื่องมือ Editor `04–14`
- `Resources/VFX/VFX_WindBladeSlash.prefab` — *(โฟลเดอร์ `Resources/VFX` สำหรับ `Resources.Load` ตอนฟันดาบ ห้ามย้ายออกนอกชื่อโฟลเดอร์ `Resources`)*

---

## 4. เครื่องมือสร้าง/ซ่อมเอฟเฟกต์ในแถบเมนู Unity — `Editor/`
เปิดแถบเมนูด้านบน Unity เลือก **`yeen VFX`** จะมีเครื่องมือครบทั้ง **`01` ถึง `14`** เรียงลำดับชัดเจน:
- `01`–`03`: สร้างคลื่นฟันดาบ (WindBlade / AirWave / WhiteWind)
- `04`–`05`: สร้างและซ่อมสายลมวิ่งระดับขา
- `06`: สร้างเอฟเฟกต์กลิ้งหลบระดับขา
- `07`: สร้างเอฟเฟกต์บล็อคโล่พลังงานวงกลม 3 มิติ
- `08`: สร้างเอฟเฟกต์หีบสมบัติแสงทอง
- `09`: สร้างเอฟเฟกต์คริติคอลฟันสีแดง
- `10`: สร้างเอฟเฟกต์เลือดนองพื้นตอนมอนตาย
- `11`–`12`: สร้างเอฟเฟกต์ยาฮีลหิ้งห้อยเขียว / ยาสเตมิน่าผีเสื้อฟ้า
- `13` *(หรือ `VFX Mentor/16`)*: สร้างเอฟเฟกต์ตีบล็อคแตกโล่กระจาย 4 เลเยอร์ (`Generated_GuardBreak/VFX_GuardBreakShatter.prefab`)
- `14` *(หรือ `VFX Mentor/18`)*: สร้างเอฟเฟกต์โจมตีโดนมอนสเตอร์ 4 เลเยอร์ (`Generated_WeaponHitImpact/VFX_WeaponHitImpact.prefab`)

---

## 5. จุดเชื่อมต่อกับระบบเกมหลัก (Game Integration Hooks)
โค้ดระบบเกมหลักของโปรเจกต์ยังคงอยู่ในโฟลเดอร์เดิม (`Assets/_Project/` และ `Assets/Code/`) และเรียกใช้ระบบใน `Assets/yeen VFX/` ผ่านจุดเชื่อมต่อดังนี้:
- `Assets/_Project/Code/PlayerCombat.cs` — ติดตั้งคอนโทรลเลอร์ VFX ให้ Local Player อัตโนมัติ, เรียก `MMOWeaponHitImpactVFX`, `MMOCriticalHitVFX`, `MMOHeartbeatComboUI`, `MMOGuardBreakVFX`, `MMOBlockParryController`, `MMODodgeSmokeController`, `MMOLevelUpController`, `MMOHealingPotionVFX`, `MMOStaminaPotionVFX`
- `Assets/_Project/Enemy/EnemyNPC.cs` — เรียก `MMOMonsterDeathBloodVFX.SpawnDeathBlood(...)` ตอนมอนสเตอร์/บอสตาย
- `Assets/_Project/Inventory/InventoryManager.cs` — เรียกเอฟเฟกต์ยาฮีลและยาสเตมิน่าเมื่อกดใช้จากกระเป๋า
- `Assets/_Project/Inventory/LootChest.cs` & `WorldItem.cs` — เรียก `MMOTreasureChestController` เปิดหีบแสงทองและติดเส้นแสงโค้งตามระดับไอเทม (Normal/Rare/Epic/Legendary)
- `Assets/Code/PlayerController.cs` & `NetworkCharacter.cs` — ซิงก์เอฟเฟกต์กลิ้งหลบ (`MMODodgeSmokeController`)
