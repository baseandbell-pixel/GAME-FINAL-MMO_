using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// UI ระบบปาร์ตี้ Co-op แบบ Editable uGUI Canvas Prefab (UI_PartyHUD):
///   1. แถบสมาชิกปาร์ตี้ด้านซ้ายจอ (Party HUD): แสดงชื่อ, เลเวล, หลอด HP สด, ระยะห่าง, และสถานะ DOWNED [Hold G]
///   2. หน้าต่างจัดการปาร์ตี้ ([P]): แสดงผู้เล่นทั้งหมดในห้องพร้อมปุ่ม Invite และปุ่ม Leave Party
///   3. กล่องแจ้งเตือนคำเชิญเข้าปาร์ตี้ (Accept [Y] / Decline [N])
/// </summary>
[DisallowMultipleComponent]
public sealed class PartyHudUI : MonoBehaviour
{
    public static PartyHudUI Instance { get; private set; }

    [Header("1. Live Left-Side Party HUD")]
    [SerializeField] private GameObject partyListRoot;
    [SerializeField] private Transform memberContainer;
    [SerializeField] private GameObject memberRowTemplate;

    [Header("2. Party Invite & Roster Modal ([P])")]
    [SerializeField] private Key togglePartyKey = Key.P;
    [SerializeField] private GameObject partyModalRoot;
    [SerializeField] private TMP_Text modalStatusText;
    [SerializeField] private Transform roomPlayerContainer;
    [SerializeField] private GameObject roomPlayerRowTemplate;
    [SerializeField] private Button leavePartyButton;
    [SerializeField] private Button closeModalButton;

    [Header("3. Incoming Party Invite Popup")]
    [SerializeField] private GameObject invitePopupRoot;
    [SerializeField] private TMP_Text inviteMessageText;
    [SerializeField] private Button acceptInviteButton;
    [SerializeField] private Button declineInviteButton;

    public bool IsModalOpen { get; private set; }

    private struct MemberRowBinding
    {
        public GameObject root;
        public PlayerRef playerRef;
        public TMP_Text nameText;
        public TMP_Text hpText;
        public Image hpFill;
    }

    private readonly List<MemberRowBinding> _memberRows = new List<MemberRowBinding>();
    private readonly List<GameObject> _roomRows = new List<GameObject>();
    private float _nextRefreshAt;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (memberRowTemplate != null) memberRowTemplate.SetActive(false);
        if (roomPlayerRowTemplate != null) roomPlayerRowTemplate.SetActive(false);
        if (partyListRoot != null) partyListRoot.SetActive(false);
        if (partyModalRoot != null) partyModalRoot.SetActive(false);
        if (invitePopupRoot != null) invitePopupRoot.SetActive(false);

        if (leavePartyButton != null)
            leavePartyButton.onClick.AddListener(() =>
            {
                PartyManager.EnsureExists().LeaveParty();
                RefreshAll();
            });
        if (closeModalButton != null)
            closeModalButton.onClick.AddListener(() => SetModalOpen(false));
        if (acceptInviteButton != null)
            acceptInviteButton.onClick.AddListener(() =>
            {
                PartyManager.EnsureExists().AcceptPendingInvite();
                RefreshAll();
            });
        if (declineInviteButton != null)
            declineInviteButton.onClick.AddListener(() =>
            {
                PartyManager.EnsureExists().DeclinePendingInvite();
                RefreshAll();
            });
    }

    private void OnEnable()
    {
        PartyManager.PartyChanged += RefreshAll;
    }

    private void OnDisable()
    {
        PartyManager.PartyChanged -= RefreshAll;
    }

    private void OnDestroy()
    {
        if (IsModalOpen) GameplayInput.PopBlocker("PartyHudUI");
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb[togglePartyKey].wasPressedThisFrame)
            {
                if (IsModalOpen || !GameplayInput.IsBlocked)
                    SetModalOpen(!IsModalOpen);
            }
            else if (IsModalOpen && kb.escapeKey.wasPressedThisFrame)
            {
                SetModalOpen(false);
            }

            var pm = PartyManager.Instance;
            if (pm != null && pm.HasPendingInvite && !IsModalOpen && !GameplayInput.IsBlocked)
            {
                if (kb.yKey.wasPressedThisFrame) { pm.AcceptPendingInvite(); RefreshAll(); }
                else if (kb.nKey.wasPressedThisFrame) { pm.DeclinePendingInvite(); RefreshAll(); }
            }
        }

        if (Time.unscaledTime >= _nextRefreshAt)
        {
            _nextRefreshAt = Time.unscaledTime + 0.2f;
            UpdateLiveMemberBars();
            UpdateInvitePopup();
            if (IsModalOpen) RebuildRoomPlayerList();
        }
    }

    public void SetModalOpen(bool open)
    {
        if (open == IsModalOpen) return;
        if (open && GameplayInput.IsBlocked) return;

        IsModalOpen = open;
        if (partyModalRoot != null) partyModalRoot.SetActive(open);
        if (open)
        {
            GameplayInput.PushBlocker("PartyHudUI");
            PartyManager.EnsureExists();
            RefreshAll();
        }
        else
        {
            GameplayInput.PopBlocker("PartyHudUI");
        }
    }

    public void RefreshAll()
    {
        RebuildMemberHudRows();
        UpdateLiveMemberBars();
        UpdateInvitePopup();
        if (IsModalOpen) RebuildRoomPlayerList();
    }

    private void UpdateInvitePopup()
    {
        if (invitePopupRoot == null) return;
        var pm = PartyManager.Instance;
        bool show = pm != null && pm.HasPendingInvite;
        if (invitePopupRoot.activeSelf != show)
            invitePopupRoot.SetActive(show);

        if (show && inviteMessageText != null)
            inviteMessageText.text = $"{pm.PendingInviteSenderName} invited you to join their Party!\n<size=80%>[Y] Accept   •   [N] Decline</size>";
    }

    private void RebuildMemberHudRows()
    {
        for (int i = 0; i < _memberRows.Count; i++)
        {
            if (_memberRows[i].root != null) Destroy(_memberRows[i].root);
        }
        _memberRows.Clear();

        var pm = PartyManager.Instance;
        bool inParty = pm != null && pm.IsInParty;
        if (partyListRoot != null) partyListRoot.SetActive(inParty);
        if (!inParty || memberContainer == null || memberRowTemplate == null) return;

        for (int i = 0; i < pm.Members.Count; i++)
        {
            PlayerRef mRef = pm.Members[i];
            GameObject row = Instantiate(memberRowTemplate, memberContainer);
            row.name = $"PartyMember_{mRef.PlayerId}";
            row.SetActive(true);
            _memberRows.Add(new MemberRowBinding
            {
                root = row,
                playerRef = mRef,
                nameText = row.transform.Find("NameText")?.GetComponent<TMP_Text>(),
                hpText = row.transform.Find("HpText")?.GetComponent<TMP_Text>(),
                hpFill = row.transform.Find("HpBarBg/HpBarFill")?.GetComponent<Image>()
            });
        }
    }

    private void UpdateLiveMemberBars()
    {
        var pm = PartyManager.Instance;
        if (pm == null || !pm.IsInParty)
        {
            if (partyListRoot != null && partyListRoot.activeSelf) partyListRoot.SetActive(false);
            return;
        }
        if (_memberRows.Count != pm.Members.Count)
            RebuildMemberHudRows();

        Vector3 localPos = PlayerCombat.Local != null ? PlayerCombat.Local.transform.position : Vector3.zero;
        for (int i = 0; i < _memberRows.Count; i++)
        {
            var binding = _memberRows[i];
            var pc = PartyManager.FindCombatByRef(binding.playerRef);
            if (pc == null) continue;

            bool isLeader = pm.PartyLeader == binding.playerRef;
            string leaderStar = isLeader ? "[L] " : "";
            string nick = PartyManager.GetDisplayName(pc);
            float dist = pc == PlayerCombat.Local ? 0f : Vector3.Distance(localPos, pc.transform.position);
            string distStr = pc == PlayerCombat.Local ? "You" : $"{dist:0}m";

            if (binding.nameText != null)
            {
                binding.nameText.text = pc.IsDead
                    ? $"<color=#FF5C5C>{leaderStar}Lv.{pc.NetworkLevel} {nick}  [DOWNED!]</color>"
                    : $"{leaderStar}Lv.{pc.NetworkLevel} {nick}  <color=#9EB0C4>({distStr})</color>";
            }

            int maxHp = Mathf.Max(1, pc.MaxHealth);
            int curHp = Mathf.Clamp(pc.NetworkHealth, 0, maxHp);
            float frac = curHp / (float)maxHp;

            if (binding.hpFill != null)
            {
                binding.hpFill.fillAmount = frac;
                binding.hpFill.color = pc.IsDead
                    ? new Color(0.85f, 0.20f, 0.20f, 1f)
                    : (frac > 0.35f ? new Color(0.24f, 0.88f, 0.56f, 1f) : new Color(0.95f, 0.55f, 0.18f, 1f));
            }
            if (binding.hpText != null)
            {
                binding.hpText.text = pc.IsDead ? "HOLD [G] NEARBY TO REVIVE" : $"{curHp} / {maxHp}";
            }
        }
    }

    private void RebuildRoomPlayerList()
    {
        for (int i = 0; i < _roomRows.Count; i++)
        {
            if (_roomRows[i] != null) Destroy(_roomRows[i]);
        }
        _roomRows.Clear();

        var pm = PartyManager.EnsureExists();
        if (leavePartyButton != null) leavePartyButton.interactable = pm.IsInParty;

        if (modalStatusText != null)
        {
            modalStatusText.text = pm.IsInParty
                ? $"Party Active ({pm.Members.Count}/{PartyManager.MaxPartySize})   •   Hold [G] near downed allies to Revive!"
                : "Solo Mode   •   Invite players in this room to form a Co-op Party (Max 5)";
        }

        if (roomPlayerContainer == null || roomPlayerRowTemplate == null) return;

        var local = PlayerCombat.Local;
        int shown = 0;
        for (int i = 0; i < PlayerCombat.All.Count; i++)
        {
            var pc = PlayerCombat.All[i];
            if (pc == null || !pc.IsSpawned || pc == local) continue;
            shown++;

            GameObject row = Instantiate(roomPlayerRowTemplate, roomPlayerContainer);
            row.name = $"RoomPlayer_{pc.name}";
            row.SetActive(true);
            _roomRows.Add(row);

            bool inMyParty = pm.IsPartyMember(pc);
            var labelTmp = row.transform.Find("PlayerLabel")?.GetComponent<TMP_Text>();
            var inviteBtn = row.transform.Find("InviteButton")?.GetComponent<Button>();
            var inviteBtnTmp = inviteBtn != null ? inviteBtn.GetComponentInChildren<TMP_Text>() : null;

            if (labelTmp != null)
                labelTmp.text = $"Lv.{pc.NetworkLevel}  {PartyManager.GetDisplayName(pc)}  ({pc.NetworkHealth}/{pc.MaxHealth} HP)";

            if (inviteBtn != null)
            {
                inviteBtn.interactable = !inMyParty && pm.Members.Count < PartyManager.MaxPartySize;
                if (inviteBtnTmp != null)
                    inviteBtnTmp.text = inMyParty ? "IN PARTY" : "INVITE";

                PlayerCombat captured = pc;
                inviteBtn.onClick.RemoveAllListeners();
                inviteBtn.onClick.AddListener(() =>
                {
                    pm.SendInvite(captured);
                    RebuildRoomPlayerList();
                });
            }
        }

        if (shown == 0)
        {
            GameObject emptyRow = Instantiate(roomPlayerRowTemplate, roomPlayerContainer);
            emptyRow.name = "NoOtherPlayersRow";
            emptyRow.SetActive(true);
            _roomRows.Add(emptyRow);
            var labelTmp = emptyRow.transform.Find("PlayerLabel")?.GetComponent<TMP_Text>();
            var inviteBtn = emptyRow.transform.Find("InviteButton")?.GetComponent<Button>();
            if (labelTmp != null) labelTmp.text = "No other players in this room yet. Friends who join appear here!";
            if (inviteBtn != null) inviteBtn.gameObject.SetActive(false);
        }
    }
}
