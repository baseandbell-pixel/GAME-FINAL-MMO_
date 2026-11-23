using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// ช่องทางปุ่มโต้ตอบหลักของเกม (แยกชัดเจนเพื่อไม่ให้ลั่นซ้อนกัน และไม่ทับปุ่มสกิล Q/E ในอนาคต):
///   - PickupOrChest ([E]): เก็บไอเทมบนพื้น (LocalPersonalLoot / WorldItem), เปิดหีบสมบัติ (LootChest), และปลดล็อก/วาร์ปหินเทเลพอร์ต (TeleportWaystone)
///   - NpcTalk       ([G]): คุยกับ NPC (QuestNPC / ร้านค้าในอนาคต) และกดค้างชุบเพื่อน (Revive) แยกจาก Q/E สกิล และไม่ทับ F บล็อก
/// </summary>
public enum InteractionChannel
{
    PickupOrChest = 0,
    NpcTalk = 1
}

/// <summary>
/// อินเทอร์เฟซกลางสำหรับวัตถุทุกชนิดที่ผู้เล่นสามารถกดโต้ตอบได้ในโลกเกม
/// รองรับทั้งการกดครั้งเดียว (Tap) และการกดค้างพร้อมแถบโหลด (Hold เช่น ชุบชีวิตเพื่อน)
/// </summary>
public interface IWorldInteractable
{
    InteractionChannel Channel { get; }
    // คะแนนลำดับความสำคัญเมื่อระยะห่างใกล้เคียงกัน (ค่าน้อยกว่าชนะ เช่น -1 = ชุบเพื่อน, 0 = ของตก/NPC, 1 = หีบสมบัติ, 2 = หินเทเลพอร์ต)
    int PriorityBias { get; }
    float InteractionRange { get; }
    Vector3 InteractionWorldPosition { get; }
    bool CanInteractNow(PlayerCombat player);
    void ExecuteInteraction(PlayerCombat player);

    // ข้อมูลสำหรับแสดงป้ายแจ้งเตือนแบบ uGUI Canvas ที่ InteractionPromptUI (ปรับแต่งหน้าตาได้ใน Unity Prefab)
    string GetPromptLabel(PlayerCombat player) => "Interact";
    Color GetPromptAccentColor(PlayerCombat player) => new Color(1f, 0.86f, 0.36f, 1f);
    bool RequiresHold => false;
    float HoldDurationSeconds => 0f;
}

/// <summary>
/// ศูนย์กลางจัดการปุ่มโต้ตอบทั้งหมดของเกม (Central Interaction Manager):
///   1. จัดการปุ่ม [E] แบบ Priority 1 เป้าหมายต่อการกด 1 ครั้ง (เก็บของ > เปิดหีบ > หินเทเลพอร์ต)
///   2. แยกปุ่มคุยกับ NPC และชุบเพื่อน ([Hold G]) เป็น [G] ไม่ชนกับ [E] และสกิลอนาคต [Q]/[E]
///   3. ส่งข้อมูลเป้าหมายปัจจุบันให้ InteractionPromptUI (uGUI Canvas Prefab) แสดงผลสเกลคมชัดทุกความละเอียดจอ แทน OnGUI เก่า
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-25)]
public sealed class InteractionManager : MonoBehaviour
{
    public const Key PickupOrChestKey = Key.E;
    public const Key NpcTalkKey = Key.G;

    public static InteractionManager Instance { get; private set; }

    private static readonly List<IWorldInteractable> s_registered = new List<IWorldInteractable>(48);

    // เป้าหมายที่ใกล้ที่สุดและพร้อมโต้ตอบในเฟรมปัจจุบัน
    public static IWorldInteractable ActivePickupTarget { get; private set; }
    public static float ActivePickupDistance { get; private set; } = float.MaxValue;
    public static IWorldInteractable ActiveNpcTarget { get; private set; }
    public static float ActiveNpcDistance { get; private set; } = float.MaxValue;

    // สถานะการกดค้าง (Hold-to-Interact เช่น ชุบเพื่อนด้วย [Hold G])
    public static IWorldInteractable HoldingTarget { get; private set; }
    public static float CurrentHoldProgress01 { get; private set; }
    public static bool IsHoldingInteraction => HoldingTarget != null && CurrentHoldProgress01 > 0f;

    /// <summary>จริงเมื่อมีไอเทม/กล่อง/หินเทเลพอร์ตอยู่ในระยะกด [E] (ให้ระบบสกิล [E] ในอนาคตเช็กก่อนออกสกิลได้)</summary>
    public static bool HasActivePickupTarget => ActivePickupTarget != null;

    /// <summary>จริงเมื่อมี NPC หรือเพื่อนที่ล้มอยู่ในระยะกด [G]</summary>
    public static bool HasActiveNpcTarget => ActiveNpcTarget != null;

    private static int s_resolvedFrame = -1;
    private float _holdElapsed;

    public static InteractionManager EnsureExists()
    {
        if (Instance != null) return Instance;
        var existing = FindAnyObjectByType<InteractionManager>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        var go = new GameObject("InteractionManager");
        return go.AddComponent<InteractionManager>();
    }

    public static void Register(IWorldInteractable interactable)
    {
        if (interactable == null) return;
        if (!s_registered.Contains(interactable))
            s_registered.Add(interactable);
        if (Application.isPlaying)
            EnsureExists();
    }

    public static void Unregister(IWorldInteractable interactable)
    {
        if (interactable == null) return;
        s_registered.Remove(interactable);
        if (ReferenceEquals(ActivePickupTarget, interactable))
        {
            ActivePickupTarget = null;
            ActivePickupDistance = float.MaxValue;
        }
        if (ReferenceEquals(ActiveNpcTarget, interactable))
        {
            ActiveNpcTarget = null;
            ActiveNpcDistance = float.MaxValue;
        }
        if (ReferenceEquals(HoldingTarget, interactable))
        {
            HoldingTarget = null;
            CurrentHoldProgress01 = 0f;
        }
    }

    public static bool IsActivePickupTarget(IWorldInteractable candidate)
    {
        ResolveTargetsForCurrentFrame();
        return candidate != null && ReferenceEquals(ActivePickupTarget, candidate);
    }

    public static bool IsActiveNpcTarget(IWorldInteractable candidate)
    {
        ResolveTargetsForCurrentFrame();
        return candidate != null && ReferenceEquals(ActiveNpcTarget, candidate);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
            ActivePickupTarget = null;
            ActiveNpcTarget = null;
            HoldingTarget = null;
            CurrentHoldProgress01 = 0f;
        }
    }

    private void Update()
    {
        ResolveTargetsForCurrentFrame(force: true);

        var player = PlayerCombat.Local;
        if (player == null || !player.IsSpawned || player.IsDead || GameplayInput.IsBlocked || !Application.isFocused)
        {
            ResetHoldState();
            return;
        }

        var kb = Keyboard.current;
        if (kb == null)
        {
            ResetHoldState();
            return;
        }

        // 1. ปุ่ม [E] (Pickup / Chest / Teleport Waystone)
        if (ActivePickupTarget != null)
        {
            if (ProcessChannelInput(ActivePickupTarget, kb[PickupOrChestKey], player))
                return;
        }

        // 2. ปุ่ม [G] (NPC Talk / Hold-G Co-op Revive)
        if (ActiveNpcTarget != null)
        {
            if (ProcessChannelInput(ActiveNpcTarget, kb[NpcTalkKey], player))
                return;
        }

        // หากปล่อยปุ่มหรือเดินออกนอกระยะระหว่างกดค้าง ให้รีเซ็ตหลอด Hold ทันที
        if (HoldingTarget != null &&
            !ReferenceEquals(HoldingTarget, ActivePickupTarget) &&
            !ReferenceEquals(HoldingTarget, ActiveNpcTarget))
        {
            ResetHoldState();
        }
    }

    private bool ProcessChannelInput(IWorldInteractable target, UnityEngine.InputSystem.Controls.KeyControl key, PlayerCombat player)
    {
        if (target == null || key == null) return false;

        if (target.RequiresHold && target.HoldDurationSeconds > 0.05f)
        {
            if (key.isPressed)
            {
                if (!ReferenceEquals(HoldingTarget, target))
                {
                    HoldingTarget = target;
                    _holdElapsed = 0f;
                }
                _holdElapsed += Time.deltaTime;
                CurrentHoldProgress01 = Mathf.Clamp01(_holdElapsed / target.HoldDurationSeconds);
                if (_holdElapsed >= target.HoldDurationSeconds)
                {
                    ResetHoldState();
                    target.ExecuteInteraction(player);
                    ResolveTargetsForCurrentFrame(force: true);
                    return true;
                }
                return true;
            }
            else if (ReferenceEquals(HoldingTarget, target))
            {
                ResetHoldState();
            }
            return false;
        }

        if (key.wasPressedThisFrame)
        {
            ResetHoldState();
            target.ExecuteInteraction(player);
            ResolveTargetsForCurrentFrame(force: true);
            return true;
        }
        return false;
    }

    private void ResetHoldState()
    {
        HoldingTarget = null;
        _holdElapsed = 0f;
        CurrentHoldProgress01 = 0f;
    }

    /// <summary>
    /// คืนเป้าหมายหลัก 1 ตัวที่ควรแสดงบนป้าย uGUI Prompt กลางจอ (ชุบเพื่อน > เก็บของ/กล่อง/หินวาร์ป > คุย NPC)
    /// </summary>
    public static IWorldInteractable GetPrimaryDisplayTarget(out Key displayKey)
    {
        ResolveTargetsForCurrentFrame();
        if (HoldingTarget != null)
        {
            displayKey = HoldingTarget.Channel == InteractionChannel.PickupOrChest ? PickupOrChestKey : NpcTalkKey;
            return HoldingTarget;
        }
        // ชุบเพื่อน (Hold G) แสดงเด่นเป็นอันดับแรกเสมอ
        if (ActiveNpcTarget != null && ActiveNpcTarget.RequiresHold)
        {
            displayKey = NpcTalkKey;
            return ActiveNpcTarget;
        }
        if (ActivePickupTarget != null)
        {
            displayKey = PickupOrChestKey;
            return ActivePickupTarget;
        }
        if (ActiveNpcTarget != null)
        {
            displayKey = NpcTalkKey;
            return ActiveNpcTarget;
        }
        displayKey = PickupOrChestKey;
        return null;
    }

    /// <summary>ตัวช่วยสำหรับทดสอบอัตโนมัติใน Play Mode: สั่งกดโต้ตอบช่อง [E]</summary>
    public static IWorldInteractable TriggerPickupForTest()
    {
        ResolveTargetsForCurrentFrame(force: true);
        var player = PlayerCombat.Local;
        if (player == null || !player.IsSpawned || player.IsDead || GameplayInput.IsBlocked) return null;
        var target = ActivePickupTarget;
        if (target == null) return null;
        target.ExecuteInteraction(player);
        ResolveTargetsForCurrentFrame(force: true);
        return target;
    }

    /// <summary>ตัวช่วยสำหรับทดสอบอัตโนมัติใน Play Mode: สั่งกดโต้ตอบช่อง [G]</summary>
    public static IWorldInteractable TriggerNpcTalkForTest()
    {
        ResolveTargetsForCurrentFrame(force: true);
        var player = PlayerCombat.Local;
        if (player == null || !player.IsSpawned || player.IsDead || GameplayInput.IsBlocked) return null;
        var target = ActiveNpcTarget;
        if (target == null) return null;
        target.ExecuteInteraction(player);
        ResolveTargetsForCurrentFrame(force: true);
        return target;
    }

    public static void ResolveTargetsForCurrentFrame(bool force = false)
    {
        if (!force && s_resolvedFrame == Time.frameCount) return;
        s_resolvedFrame = Time.frameCount;

        ActivePickupTarget = null;
        ActivePickupDistance = float.MaxValue;
        ActiveNpcTarget = null;
        ActiveNpcDistance = float.MaxValue;

        var player = PlayerCombat.Local;
        if (player == null || !player.IsSpawned || player.IsDead || GameplayInput.IsBlocked)
            return;

        Vector3 playerPos = player.transform.position;
        float bestPickupScore = float.MaxValue;
        float bestNpcScore = float.MaxValue;

        for (int i = s_registered.Count - 1; i >= 0; i--)
        {
            IWorldInteractable item = s_registered[i];
            if (item == null || (item is Object uObj && uObj == null))
            {
                s_registered.RemoveAt(i);
                continue;
            }

            if (!item.CanInteractNow(player)) continue;

            float dist = PlanarDistance(item.InteractionWorldPosition, playerPos);
            if (dist > item.InteractionRange) continue;

            // คะแนนลำดับความสำคัญ: เก็บของ (0) > หีบ (1) > หินวาร์ป (2), ชุบเพื่อน (-1) > คุย NPC (0)
            float score = dist + item.PriorityBias * 0.25f;

            if (item.Channel == InteractionChannel.PickupOrChest)
            {
                if (score < bestPickupScore)
                {
                    bestPickupScore = score;
                    ActivePickupDistance = dist;
                    ActivePickupTarget = item;
                }
            }
            else if (item.Channel == InteractionChannel.NpcTalk)
            {
                if (score < bestNpcScore)
                {
                    bestNpcScore = score;
                    ActiveNpcDistance = dist;
                    ActiveNpcTarget = item;
                }
            }
        }
    }

    private static float PlanarDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }
}
