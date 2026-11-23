using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

public enum PartySignalAction
{
    Invite = 1,
    Accept = 2,
    Decline = 3,
    Leave = 4,
    SyncMember = 5
}

/// <summary>
/// ระบบปาร์ตี้ Co-op สำหรับ Photon Fusion Shared Mode (ไม่ต้องเพิ่ม [Networked] Property ใหม่บน Player Prefab):
///   - เชิญเพื่อนในห้องเข้าปาร์ตี้ได้สูงสุด 5 คนผ่าน RPC_PartySignal บน PlayerCombat
///   - ซิงก์รายชื่อสมาชิกเข้า PartyHudUI (แสดงชื่อ, เลเวล, หลอด HP สด, สถานะ Downed, และระยะห่างแบบเรียลไทม์)
///   - รองรับระบบชุบชีวิตเพื่อนกลางไฟต์ ([Hold G] 2.5 วินาที) ร่วมกับ PlayerCombat
/// </summary>
[DisallowMultipleComponent]
public sealed class PartyManager : MonoBehaviour
{
    public const int MaxPartySize = 5;
    public const float InviteTimeoutSeconds = 25f;

    public static PartyManager Instance { get; private set; }
    public static event Action PartyChanged;

    public int LocalPartyId { get; private set; }
    public PlayerRef PartyLeader { get; private set; }
    public bool IsInParty => LocalPartyId != 0 && _members.Count > 0;
    public IReadOnlyList<PlayerRef> Members => _members;

    // สถานะคำเชิญเข้าปาร์ตี้ที่กำลังรอการตอบรับ
    public bool HasPendingInvite => PendingInvitePartyId != 0 && Time.unscaledTime < _pendingInviteExpiresAt;
    public int PendingInvitePartyId { get; private set; }
    public PlayerRef PendingInviteSender { get; private set; }
    public string PendingInviteSenderName { get; private set; } = string.Empty;

    private readonly List<PlayerRef> _members = new List<PlayerRef>(MaxPartySize);
    private float _pendingInviteExpiresAt;
    private float _nextCleanupAt;

    public static PartyManager EnsureExists()
    {
        if (Instance != null) return Instance;
        var existing = FindAnyObjectByType<PartyManager>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        var go = new GameObject("PartyManager");
        return go.AddComponent<PartyManager>();
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
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (PendingInvitePartyId != 0 && Time.unscaledTime >= _pendingInviteExpiresAt)
        {
            ClearPendingInvite();
            PartyChanged?.Invoke();
        }

        if (Time.unscaledTime >= _nextCleanupAt)
        {
            _nextCleanupAt = Time.unscaledTime + 1.5f;
            PruneDisconnectedMembers();
        }
    }

    public static PlayerRef GetPlayerRef(PlayerCombat pc)
    {
        if (pc == null || !pc.IsSpawned) return PlayerRef.None;
        if (pc.Object != null && pc.Object.IsValid)
        {
            if (pc.Object.StateAuthority != PlayerRef.None) return pc.Object.StateAuthority;
            if (pc.Object.InputAuthority != PlayerRef.None) return pc.Object.InputAuthority;
        }
        if (pc.IsLocallyControlled && pc.Runner != null && pc.Runner.IsRunning)
            return pc.Runner.LocalPlayer;
        return PlayerRef.None;
    }

    public static string GetDisplayName(PlayerCombat pc)
    {
        if (pc == null) return "Player";
        var cap = pc.GetComponent<PlayerCapsule>();
        if (cap != null && !string.IsNullOrEmpty(cap.Nick.Value))
            return cap.Nick.Value;
        PlayerRef pRef = GetPlayerRef(pc);
        return pRef != PlayerRef.None ? $"Player {pRef.PlayerId}" : pc.name;
    }

    public static PlayerCombat FindCombatByRef(PlayerRef playerRef)
    {
        if (playerRef == PlayerRef.None) return null;
        for (int i = 0; i < PlayerCombat.All.Count; i++)
        {
            var pc = PlayerCombat.All[i];
            if (pc == null || !pc.IsSpawned) continue;
            if (GetPlayerRef(pc) == playerRef) return pc;
        }
        return null;
    }

    public bool IsPartyMember(PlayerRef playerRef)
    {
        return playerRef != PlayerRef.None && _members.Contains(playerRef);
    }

    public bool IsPartyMember(PlayerCombat pc)
    {
        return IsPartyMember(GetPlayerRef(pc));
    }

    /// <summary>
    /// ส่งคำเชิญเข้าปาร์ตี้ไปยังผู้เล่นคนอื่นในห้องเดียวกัน
    /// </summary>
    public bool SendInvite(PlayerCombat target)
    {
        var local = PlayerCombat.Local;
        if (local == null || !local.IsSpawned || target == null || !target.IsSpawned || target == local)
            return false;

        PlayerRef localRef = GetPlayerRef(local);
        PlayerRef targetRef = GetPlayerRef(target);
        if (localRef == PlayerRef.None || targetRef == PlayerRef.None) return false;

        if (_members.Count >= MaxPartySize)
        {
            PlayerStatusHud.Instance?.ShowToast($"Party is full ({MaxPartySize}/{MaxPartySize})!", new Color(1f, 0.55f, 0.30f), 2.2f);
            return false;
        }

        if (_members.Contains(targetRef))
        {
            PlayerStatusHud.Instance?.ShowToast($"{GetDisplayName(target)} is already in your party.", new Color(0.85f, 0.85f, 0.85f), 2.0f);
            return false;
        }

        // หากยังไม่ได้ตั้งปาร์ตี้ ให้สร้าง PartyId ใหม่โดยให้เราเป็นหัวหน้า
        if (LocalPartyId == 0)
        {
            LocalPartyId = (Mathf.Abs(localRef.RawEncoded) * 10000) + UnityEngine.Random.Range(100, 9999);
            PartyLeader = localRef;
            _members.Clear();
            _members.Add(localRef);
            PartyChanged?.Invoke();
        }

        string myName = GetDisplayName(local);
        target.RPC_PartySignal((int)PartySignalAction.Invite, LocalPartyId, localRef, myName);
        PlayerStatusHud.Instance?.ShowToast($"Sent party invite to {GetDisplayName(target)}.", new Color(0.35f, 0.90f, 1f), 2.2f);
        return true;
    }

    /// <summary>ตอบรับคำเชิญเข้าปาร์ตี้ที่ค้างอยู่</summary>
    public bool AcceptPendingInvite()
    {
        if (!HasPendingInvite) return false;
        var local = PlayerCombat.Local;
        if (local == null || !local.IsSpawned) return false;

        PlayerRef localRef = GetPlayerRef(local);
        int partyId = PendingInvitePartyId;
        PlayerRef senderRef = PendingInviteSender;
        string senderName = PendingInviteSenderName;
        ClearPendingInvite();

        if (LocalPartyId != 0 && LocalPartyId != partyId)
            LeaveParty();

        LocalPartyId = partyId;
        PartyLeader = senderRef;
        _members.Clear();
        if (senderRef != PlayerRef.None) _members.Add(senderRef);
        if (localRef != PlayerRef.None && !_members.Contains(localRef)) _members.Add(localRef);

        var senderCombat = FindCombatByRef(senderRef);
        if (senderCombat != null)
            senderCombat.RPC_PartySignal((int)PartySignalAction.Accept, partyId, localRef, GetDisplayName(local));

        PlayerStatusHud.Instance?.ShowToast($"Joined {senderName}'s Party!", new Color(0.30f, 0.96f, 0.72f), 2.4f);
        PartyChanged?.Invoke();
        return true;
    }

    /// <summary>ปฏิเสธคำเชิญเข้าปาร์ตี้</summary>
    public void DeclinePendingInvite()
    {
        if (!HasPendingInvite) return;
        var local = PlayerCombat.Local;
        PlayerRef localRef = GetPlayerRef(local);
        var senderCombat = FindCombatByRef(PendingInviteSender);
        if (senderCombat != null && local != null)
            senderCombat.RPC_PartySignal((int)PartySignalAction.Decline, PendingInvitePartyId, localRef, GetDisplayName(local));
        ClearPendingInvite();
        PartyChanged?.Invoke();
    }

    /// <summary>ออกจากปาร์ตี้ปัจจุบันและแจ้งสมาชิกทุกคน</summary>
    public void LeaveParty()
    {
        if (LocalPartyId == 0) return;
        var local = PlayerCombat.Local;
        PlayerRef localRef = GetPlayerRef(local);
        string myName = local != null ? GetDisplayName(local) : "Player";
        int oldPartyId = LocalPartyId;

        for (int i = 0; i < _members.Count; i++)
        {
            PlayerRef memberRef = _members[i];
            if (memberRef == localRef) continue;
            var memberCombat = FindCombatByRef(memberRef);
            if (memberCombat != null)
                memberCombat.RPC_PartySignal((int)PartySignalAction.Leave, oldPartyId, localRef, myName);
        }

        LocalPartyId = 0;
        PartyLeader = PlayerRef.None;
        _members.Clear();
        PlayerStatusHud.Instance?.ShowToast("Left the party.", new Color(0.95f, 0.78f, 0.35f), 2.0f);
        PartyChanged?.Invoke();
    }

    /// <summary>รับสัญญาณปาร์ตี้จากผู้เล่นคนอื่นผ่าน PlayerCombat.RPC_PartySignal</summary>
    public void HandleNetworkSignal(PartySignalAction action, int partyId, PlayerRef senderRef, string senderName)
    {
        var local = PlayerCombat.Local;
        PlayerRef localRef = GetPlayerRef(local);
        if (local == null || localRef == PlayerRef.None) return;

        switch (action)
        {
            case PartySignalAction.Invite:
                if (partyId == 0 || senderRef == PlayerRef.None || senderRef == localRef) return;
                PendingInvitePartyId = partyId;
                PendingInviteSender = senderRef;
                PendingInviteSenderName = string.IsNullOrWhiteSpace(senderName) ? $"Player {senderRef.PlayerId}" : senderName;
                _pendingInviteExpiresAt = Time.unscaledTime + InviteTimeoutSeconds;
                PlayerStatusHud.Instance?.ShowToast($"{PendingInviteSenderName} invited you to a Party! (Press [P] or Accept)", new Color(0.35f, 0.92f, 1f), 4.0f);
                PartyChanged?.Invoke();
                break;

            case PartySignalAction.Accept:
                if (partyId != LocalPartyId || senderRef == PlayerRef.None) return;
                if (!_members.Contains(senderRef) && _members.Count < MaxPartySize)
                    _members.Add(senderRef);

                // ซิงก์รายชื่อสมาชิกที่มีอยู่ทั้งหมดให้สมาชิกทุกคนในปาร์ตี้ตรงกัน
                for (int i = 0; i < _members.Count; i++)
                {
                    PlayerRef mRef = _members[i];
                    var mCombat = FindCombatByRef(mRef);
                    if (mCombat == null || mRef == localRef) continue;
                    for (int j = 0; j < _members.Count; j++)
                    {
                        PlayerRef syncRef = _members[j];
                        if (syncRef == mRef) continue;
                        var sc = FindCombatByRef(syncRef);
                        mCombat.RPC_PartySignal((int)PartySignalAction.SyncMember, LocalPartyId, syncRef, sc != null ? GetDisplayName(sc) : "");
                    }
                }
                PlayerStatusHud.Instance?.ShowToast($"{senderName} joined the party!", new Color(0.32f, 0.96f, 0.72f), 2.5f);
                PartyChanged?.Invoke();
                break;

            case PartySignalAction.Decline:
                if (partyId != LocalPartyId) return;
                PlayerStatusHud.Instance?.ShowToast($"{senderName} declined your party invite.", new Color(1f, 0.65f, 0.35f), 2.2f);
                // หากยังไม่มีสมาชิกคนอื่นอยู่ในปาร์ตี้เลย ให้ยุบปาร์ตี้ชั่วคราวทันทีเพื่อไม่ให้ค้างกล่องปาร์ตี้ 1 คนบนหน้าจอ
                if (_members.Count <= 1)
                {
                    LocalPartyId = 0;
                    PartyLeader = PlayerRef.None;
                    _members.Clear();
                    PartyChanged?.Invoke();
                }
                break;

            case PartySignalAction.SyncMember:
                if (partyId != LocalPartyId || senderRef == PlayerRef.None) return;
                if (!_members.Contains(senderRef) && _members.Count < MaxPartySize)
                {
                    _members.Add(senderRef);
                    PartyChanged?.Invoke();
                }
                break;

            case PartySignalAction.Leave:
                if (partyId != LocalPartyId || senderRef == PlayerRef.None) return;
                if (_members.Remove(senderRef))
                {
                    PlayerStatusHud.Instance?.ShowToast($"{senderName} left the party.", new Color(0.95f, 0.75f, 0.35f), 2.2f);
                    if (_members.Count <= 1)
                    {
                        LocalPartyId = 0;
                        PartyLeader = PlayerRef.None;
                        _members.Clear();
                    }
                    else if (PartyLeader == senderRef)
                    {
                        PartyLeader = _members[0];
                    }
                    PartyChanged?.Invoke();
                }
                break;
        }
    }

    /// <summary>สำหรับทดสอบใน Play Mode: จำลองสถานะคำเชิญหรือสมาชิกปาร์ตี้</summary>
    public void SimulateInviteForTest(int partyId, PlayerRef sender, string senderName)
    {
        HandleNetworkSignal(PartySignalAction.Invite, partyId, sender, senderName);
    }

    private void ClearPendingInvite()
    {
        PendingInvitePartyId = 0;
        PendingInviteSender = PlayerRef.None;
        PendingInviteSenderName = string.Empty;
        _pendingInviteExpiresAt = 0f;
    }

    private void PruneDisconnectedMembers()
    {
        if (LocalPartyId == 0 || _members.Count == 0) return;
        bool changed = false;
        for (int i = _members.Count - 1; i >= 0; i--)
        {
            if (FindCombatByRef(_members[i]) == null)
            {
                _members.RemoveAt(i);
                changed = true;
            }
        }
        if (changed)
        {
            if (_members.Count <= 1)
            {
                LocalPartyId = 0;
                PartyLeader = PlayerRef.None;
                _members.Clear();
            }
            else if (!_members.Contains(PartyLeader))
            {
                // โอนตำแหน่งหัวหน้าปาร์ตี้ให้สมาชิกที่มี RawEncoded ต่ำสุดแบบ Deterministic เพื่อให้ทุกเครื่องเห็นหัวหน้าคนเดียวกันทันที
                PlayerRef nextLeader = _members[0];
                for (int i = 1; i < _members.Count; i++)
                {
                    if (_members[i].RawEncoded < nextLeader.RawEncoded)
                        nextLeader = _members[i];
                }
                PartyLeader = nextLeader;
                var newLeaderCombat = FindCombatByRef(nextLeader);
                string newLeaderName = newLeaderCombat != null ? GetDisplayName(newLeaderCombat) : $"Player {nextLeader.PlayerId}";
                PlayerStatusHud.Instance?.ShowToast($"Party Leader transferred to {newLeaderName}.", new Color(0.95f, 0.82f, 0.32f), 2.5f);
            }
            PartyChanged?.Invoke();
        }
    }
}
