# คู่มือระบบเอฟเฟกต์ทั้งหมดในโฟลเดอร์ `yeen VFX`

เอกสารนี้สรุปชื่อโฟลเดอร์ ชื่อเอฟเฟกต์ หน้าที่ และตำแหน่งที่ต้องนำไปใส่ใน Unity (ทั้งหมดพร้อมทำงานอัตโนมัติในโปรเจกต์แล้ว)

---

## 1. โค้ดเอฟเฟกต์สำหรับ "ตัวละครผู้เล่น (Player)" — `01_Player_VFX_Scripts/`
*(ใน Unity กด Add Component → `yeen VFX` จะเห็นเมนูภาษาไทยเรียงตามหมายเลข)*

| โฟลเดอร์ | สคริปต์ | ทำอะไร | ใส่ตรงไหน | วิธีทำงาน / ทดสอบ |
|---|---|---|---|---|
| `01_SwordSlash_AttachToPlayer` | `SwordWindSlashVFX.cs` | ปล่อยคลื่นลมดาบเวลาฟัน | **แปะบน Player** (`Player.prefab`) | ฟันดาบในเกม (คลิกซ้าย) |
| `01_SwordSlash_AttachToPlayer` | `WuxiaSlashController.cs`, `WuxiaSyncedSlashController.cs` | ตัวควบคุมคลื่นดาบอากาศ/ลมขาว 3 จุด | **แปะบน Player / ตัวทดสอบดาบ** | ใช้คู่กับ Tool `02` หรือ `03` |
| `02_Sprint_AttachToPlayer` | `RunWindSlipstream.cs` | เอฟเฟกต์ลมผ่านตัว + วงแหวนความเร็วตอนวิ่ง | **แปะบน Player** (`Player.prefab`) | วิ่งปกติ / กด `Shift` พุ่งวิ่ง |
| `02_Sprint_AttachToPlayer` | `SimpleSprintTrail.cs`, `WuxiaSprintController.cs`, `ExistingRunVFXActivator.cs` | ตัวเสริมเส้นลมวิ่งติดเท้า | **แปะบน Player** (ตัวเลือกเสริม) | ใช้คู่กับ Tool `04` / `05` |
| `03_BlockAndDodge_AttachToPlayer` | `MMOBlockParryController.cs` | โล่แสงป้องกัน 3 มิติ (ไม่สะท้อนกระเด็น) | **แปะบน Player** (`Player.prefab`) | กดคลิกขวาค้าง (Block) |
| `03_BlockAndDodge_AttachToPlayer` | `MMODodgeSmokeController.cs` | กลิ้งหลบเป็นควันขาว 4 เลเยอร์ | **แปะบน Player** (`Player.prefab`) | กด `Space` / `Alt` กลิ้งหลบ |
| `04_PotionsAndLevelUp_AttachToPlayer` | `MMOHealingPotionVFX.cs` | ยารักษา: หิ้งห้อยแสงเขียวลอยวนรอบตัว | **แปะบน Player** (ออโต้ตอน Spawn) | ใช้ยาฮีลในกระเป๋า หรือกด `H` / `1` |
| `04_PotionsAndLevelUp_AttachToPlayer` | `MMOStaminaPotionVFX.cs` | ยาสเตมิน่า: ผีเสื้อแสงสีฟ้าบินวนช้าๆ รอบตัว | **แปะบน Player** (ออโต้ตอน Spawn) | ใช้ยาสเตมิน่า หรือกด `V` / `2` |
| `04_PotionsAndLevelUp_AttachToPlayer` | `MMOLevelUpController.cs` | เลเวลอัพ: เสาแสงทอง + วงแหวนพื้น + ละอองดาว | **แปะบน Player** (ออโต้ตอน Spawn) | เลเวลอัพในเกม หรือกด `L` ทดสอบ |
| `05_ComboUI_AttachToPlayer` | `MMOHeartbeatComboUI.cs` | กรอบคอมโบมุมขวาบน เต้นจังหวะหัวใจ "ตึก-ตัก" (แรงก์ C→SSS) | **แปะบน Player** (ออโต้ตอน Spawn) | ตีศัตรูต่อเนื่อง (หรือติ๊กโหมดเทสแล้วกด `J`/คลิกซ้าย) |

---

## 2. โค้ดเอฟเฟกต์ "ตอนตีศัตรู / มอนตาย / หีบสมบัติ" — `02_Combat_And_World_Scripts/`

| โฟลเดอร์ | สคริปต์ | ทำอะไร | ใส่ตรงไหน | วิธีทำงาน / ทดสอบ |
|---|---|---|---|---|
| `01_CriticalHit_AutoOnEnemyHit` | `MMOCriticalHitVFX.cs` | ฟันคริติคอลสีแดงขนาดใหญ่ตรงจุดปะทะ | **ไม่ต้องแปะมือ** (ถูกเรียกอัตโนมัติตอนตีติดคริ) | ตีศัตรูติด Critical ในเกม |
| `02_DeathBlood_AutoOnEnemyDeath` | `MMOMonsterDeathBloodVFX.cs` | เลือดพุ่งและไหลนองบนพื้นเมื่อมอนสเตอร์/บอสตาย | **ไม่ต้องแปะมือ** (ถูกเรียกอัตโนมัติตอนมอนตาย) | ฆ่ามอนสเตอร์หรือบอส |
| `03_TreasureChest_AttachToChest` | `MMOTreasureChestController.cs` | หมุนเปิดฝาหีบ + ลำแสงทองพุ่ง 4 เลเยอร์ + หีบหายไปหลังเปิด | **แปะบนหีบสมบัติ** (`LootChest` ออโต้) | เดินไปกด `E` เปิดหีบสมบัติ |

---

## 3. ไฟล์ Prefab / Material / Texture / Mesh / Shader — `03_VFX_Prefabs_And_Assets/`
- `Prefabs/VFX_LevelUp_GoldenPillar_PutOnPlayer.prefab` — พรีแฟบเสาแสงเลเวลอัพ (**ใส่ใต้ตัวละคร Player**)
- `Prefabs/VFX_SwordSlash_WhiteWind_PutOnPlayerSword.prefab` — พรีแฟบคลื่นลมขาว 3 จุด (**ใส่ที่ดาบของ Player**)
- `Prefabs/VFX_SwordSlash_WindBlade_PutOnPlayerSword.prefab` — พรีแฟบคลื่นดาบสายลม (**ใส่ที่ดาบของ Player**)
- `Prefabs/VFX_SwordSlash_AirWave_PutOnPlayerSword.prefab` — พรีแฟบคลื่นอากาศบิดเบือน (**ใส่ที่ดาบของ Player**)
- `Resources/VFX/VFX_WindBladeSlash.prefab` — *(โฟลเดอร์ระบบสำหรับ `Resources.Load` อัตโนมัติตอนฟันดาบ ห้ามย้ายออกนอก `Resources/VFX`)*

---

## 4. เครื่องมือสร้าง/ซ่อมเอฟเฟกต์ในแถบเมนู Unity — `Editor/`
เปิดแถบเมนูด้านบน Unity เลือก **`yeen VFX`** จะเรียงหมายเลข `01` ถึง `12` ชัดเจนพร้อมวงเล็บตำแหน่งที่ติดตั้ง เช่น `[ใส่บน Player]`, `[ใส่บน Chest]`, `[เกิดตอนตีศัตรู]`, `[เกิดตอนมอนตาย]`
