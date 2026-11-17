using System.Collections.Generic;
using TMPro;
using Fusion;
using UnityEngine;
using UnityEngine.UI;

public class LobbyPanelUI : MonoBehaviour
{
    [Header("Cards (exactly 4 - one per seat)")]
    public PlayerCardUI[] cards = new PlayerCardUI[LobbyNetwork.MaxPlayers];

    [Header("Avatars (drop your 4 avatar sprites here)")]
    public Sprite[] avatarSprites = new Sprite[LobbyNetwork.AvatarCount];

    [Header("Controls")]
    public Button readyButton;
    public TMP_Text readyButtonLabel;
    public Button playButton;
    public TMP_Text playButtonLabel;
    [Tooltip("Hide the PLAY button entirely for players that are not the first player.")]
    public bool hidePlayForNonHost = true;
    [Tooltip("Everybody in the room has to be ready before PLAY becomes clickable.")]
    public bool requireAllReady = true;
    [Tooltip("Smallest number of players the first player can start the map with.")]
    public int minPlayersToStart = 1;
    public Button leaveButton;
    public Button editPlayerButton;
    public TMP_Text statusText;

    [Header("Edit popup")]
    public EditPlayerPanelUI editPlayerPanel;

    [Tooltip("How often the cards are refreshed, in seconds.")]
    public float refreshInterval = 0.1f;

    [Header("Host change notice")]
    [Tooltip("How long the 'new host' message stays on the status line, in seconds.")]
    public float hostNoticeDuration = 4f;

    float _nextRefresh;
    string _notice;
    float _noticeUntil;
    readonly List<LobbyPlayer> _sorted = new List<LobbyPlayer>();
    Button _copyCodeButton;
    TMP_Text _copyCodeLabel;
    float _copiedUntil;

    void OnEnable()
    {
        EnsureCopyCodeBadge();
        if (readyButton) readyButton.onClick.AddListener(OnClickReady);
        if (playButton) playButton.onClick.AddListener(OnClickPlay);
        if (leaveButton) leaveButton.onClick.AddListener(OnClickLeave);
        if (editPlayerButton) editPlayerButton.onClick.AddListener(OnClickEditPlayer);
        if (LobbyNetwork.Instance != null) LobbyNetwork.Instance.OnMasterClientChanged += OnMasterClientChanged;
        Refresh();
    }

    private void EnsureCopyCodeBadge()
    {
        if (_copyCodeButton != null) return;

        GameObject go = new GameObject("RoomCodeBadge");
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.SetParent(transform, false);
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-24f, -18f);
        rt.sizeDelta = new Vector2(210f, 34f);

        Image bg = go.AddComponent<Image>();
        bg.color = new Color(0.14f, 0.22f, 0.34f, 0.92f);

        _copyCodeButton = go.AddComponent<Button>();
        _copyCodeButton.onClick.AddListener(() =>
        {
            string code = LobbyNetwork.Instance != null ? LobbyNetwork.Instance.CurrentRoomCode : string.Empty;
            if (!string.IsNullOrEmpty(code))
            {
                GUIUtility.systemCopyBuffer = code;
                _copiedUntil = Time.unscaledTime + 1.5f;
            }
        });

        GameObject lblGo = new GameObject("Label");
        RectTransform lRt = lblGo.AddComponent<RectTransform>();
        lRt.SetParent(rt, false);
        lRt.anchorMin = Vector2.zero;
        lRt.anchorMax = Vector2.one;
        lRt.offsetMin = Vector2.zero;
        lRt.offsetMax = Vector2.zero;

        TextMeshProUGUI tmp = lblGo.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        tmp.fontSize = 14;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(0.75f, 0.94f, 1f);
        _copyCodeLabel = tmp;
    }

    void OnDisable()
    {
        if (readyButton) readyButton.onClick.RemoveListener(OnClickReady);
        if (playButton) playButton.onClick.RemoveListener(OnClickPlay);
        if (leaveButton) leaveButton.onClick.RemoveListener(OnClickLeave);
        if (editPlayerButton) editPlayerButton.onClick.RemoveListener(OnClickEditPlayer);
        if (LobbyNetwork.Instance != null) LobbyNetwork.Instance.OnMasterClientChanged -= OnMasterClientChanged;
        if (editPlayerPanel != null && editPlayerPanel.gameObject.activeSelf)
        {
            editPlayerPanel.gameObject.SetActive(false);
        }
        _sorted.Clear();
    }

    /// <summary>
    /// The first player left, so Photon handed the room to somebody else. Everybody gets
    /// told who it is; the new host also gets the PLAY button on the next Refresh.
    /// </summary>
    void OnMasterClientChanged(PlayerRef master, bool localIsMaster)
    {
        _notice = localIsMaster
            ? "The host left - you are the host now."
            : $"The host left - {NickOf(master)} is the host now.";
        _noticeUntil = Time.unscaledTime + hostNoticeDuration;
        Refresh();
    }

    string NickOf(PlayerRef player)
    {
        foreach (var p in LobbyPlayer.All)
        {
            if (p != null && p.Object != null && p.Object.InputAuthority == player)
                return p.Nick.Value;
        }
        return $"Player {player.PlayerId}";
    }

    void Update()
    {
        if (Time.unscaledTime < _nextRefresh) return;
        _nextRefresh = Time.unscaledTime + refreshInterval;
        Refresh();
    }

    public void OnClickReady()
    {
        if (LobbyPlayer.Local == null) return;
        LobbyPlayer.Local.ToggleReady();
        Refresh();
    }

    public void OnClickEditPlayer()
    {
        if (editPlayerPanel == null) return;
        if (editPlayerPanel.lobbyPanel == null) editPlayerPanel.lobbyPanel = this;
        editPlayerPanel.Open();
    }

    /// <summary>Only the first player in the room can send everybody to the map.</summary>
    public void OnClickPlay()
    {
        var net = LobbyNetwork.Instance;
        if (net == null || !net.IsFirstPlayer) return;
        if (playButton) playButton.interactable = false;
        net.StartGame();
    }

    public void OnClickLeave()
    {
        if (LobbyNetwork.Instance != null) LobbyNetwork.Instance.LeaveLobby();
    }

    void Refresh()
    {
        // Stable seat order so every client shows the same player in the same slot.
        _sorted.Clear();
        foreach (var p in LobbyPlayer.All)
        {
            if (p != null && p.Object != null) _sorted.Add(p);
        }
        _sorted.Sort((a, b) => a.Object.InputAuthority.PlayerId.CompareTo(b.Object.InputAuthority.PlayerId));

        var net = LobbyNetwork.Instance;
        int readyCount = 0;

        for (int i = 0; i < cards.Length; i++)
        {
            var card = cards[i];
            if (card == null) continue;

            if (i < _sorted.Count)
            {
                var p = _sorted[i];
                if (p.IsReady) readyCount++;
                bool isHost = net != null && net.MasterClient.IsRealPlayer && p.Object.InputAuthority == net.MasterClient;
                card.ShowPlayer(GetAvatar(p.AvatarIndex), p.Nick.Value, p.IsReady, p == LobbyPlayer.Local, isHost);
            }
            else
            {
                card.ShowBlank();   // still waiting for this seat to be taken
            }
        }

        bool localReady = LobbyPlayer.Local != null && LobbyPlayer.Local.IsReady;
        if (readyButtonLabel) readyButtonLabel.text = localReady ? "CANCEL READY" : "READY";
        if (readyButton) readyButton.interactable = LobbyPlayer.Local != null;
        if (editPlayerButton) editPlayerButton.interactable = GoogleAPI.instance != null;

        UpdatePlayButton(_sorted.Count, readyCount);

        string roomCode = net != null ? net.CurrentRoomCode : string.Empty;
        if (_copyCodeLabel != null)
        {
            if (Time.unscaledTime < _copiedUntil)
            {
                _copyCodeLabel.text = "Copied Room Code!";
            }
            else
            {
                _copyCodeLabel.text = string.IsNullOrEmpty(roomCode) ? "Room: ..." : $"Room: {roomCode} [Copy]";
            }
        }

        if (statusText)
        {
            int total = _sorted.Count;
            if (_notice != null && Time.unscaledTime < _noticeUntil)
            {
                statusText.text = _notice;
            }
            else
            {
                _notice = null;
                string roomPrefix = string.IsNullOrEmpty(roomCode) ? "" : $"Room {roomCode}  |  ";
                statusText.text = total >= LobbyNetwork.MaxPlayers && readyCount == total
                    ? $"{roomPrefix}All players ready!"
                    : $"{roomPrefix}Players {total}/{LobbyNetwork.MaxPlayers}   -   Ready {readyCount}/{total}";
            }
        }
    }

    void UpdatePlayButton(int total, int readyCount)
    {
        if (playButton == null) return;

        var net = LobbyNetwork.Instance;
        bool isFirstPlayer = net != null && net.IsFirstPlayer;

        if (hidePlayForNonHost && playButton.gameObject.activeSelf != isFirstPlayer)
            playButton.gameObject.SetActive(isFirstPlayer);

        bool enoughPlayers = total >= Mathf.Max(1, minPlayersToStart);
        bool allReady = !requireAllReady || (total > 0 && readyCount == total);

        playButton.interactable = isFirstPlayer && enoughPlayers && allReady;

        if (playButtonLabel)
        {
            playButtonLabel.text = !isFirstPlayer ? "WAITING FOR HOST"
                : !enoughPlayers ? "WAITING FOR PLAYERS"
                : !allReady ? "WAITING FOR READY"
                : "PLAY";
        }
    }

    Sprite GetAvatar(int index)
    {
        if (avatarSprites == null || avatarSprites.Length == 0) return null;
        return avatarSprites[Mathf.Clamp(index, 0, avatarSprites.Length - 1)];
    }
}
