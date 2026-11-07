using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The "edit player" popup: a name field plus left/right arrows that page
/// through the hero cards. OK writes the choice to Firestore and pushes it onto
/// the networked lobby card; Cancel throws the edits away.
/// </summary>
public class EditPlayerPanelUI : MonoBehaviour
{
    [Header("Root")]
    [Tooltip("The popup itself. Leave empty to use this GameObject.")]
    public GameObject panelRoot;

    [Header("Name")]
    public TMP_InputField nameInput;

    [Header("Hero card preview")]
    public Image heroImage;
    public TMP_Text heroLabel;
    public Button prevButton;
    public Button nextButton;

    [Header("Avatars (leave empty to reuse the ones on LobbyPanelUI)")]
    public Sprite[] avatarSprites;

    [Header("Buttons")]
    public Button okButton;
    public Button cancelButton;
    public TMP_Text statusText;

    [Header("Lobby")]
    [Tooltip("Used as the avatar source and refreshed after a successful save.")]
    public LobbyPanelUI lobbyPanel;

    int _avatarIndex;
    bool _saving;

    void Awake()
    {
        if (panelRoot == null) panelRoot = gameObject;
        panelRoot.SetActive(false);
    }

    void OnEnable()
    {
        if (prevButton) prevButton.onClick.AddListener(OnClickPrev);
        if (nextButton) nextButton.onClick.AddListener(OnClickNext);
        if (okButton) okButton.onClick.AddListener(OnClickOk);
        if (cancelButton) cancelButton.onClick.AddListener(Close);
    }

    void OnDisable()
    {
        if (prevButton) prevButton.onClick.RemoveListener(OnClickPrev);
        if (nextButton) nextButton.onClick.RemoveListener(OnClickNext);
        if (okButton) okButton.onClick.RemoveListener(OnClickOk);
        if (cancelButton) cancelButton.onClick.RemoveListener(Close);
    }

    /// <summary>Opens the popup seeded with whatever the player currently has.</summary>
    public void Open()
    {
        var api = GoogleAPI.instance;
        if (api == null) return;

        if (nameInput)
        {
            nameInput.characterLimit = GoogleAPI.MaxNameLength;
            nameInput.text = api.username;
        }
        _avatarIndex = Mathf.Clamp(api.avatarId, 0, AvatarCount - 1);

        SetStatus("");
        RefreshHero();
        SetInteractable(true);

        if (panelRoot) panelRoot.SetActive(true);
    }

    public void Close()
    {
        if (_saving) return;
        if (panelRoot) panelRoot.SetActive(false);
    }

    public void OnClickPrev() => Step(-1);
    public void OnClickNext() => Step(1);

    void Step(int direction)
    {
        int count = AvatarCount;
        if (count <= 0) return;
        _avatarIndex = (_avatarIndex + direction + count) % count;   // wraps at both ends
        RefreshHero();
    }

    public async void OnClickOk()
    {
        if (_saving) return;

        var api = GoogleAPI.instance;
        if (api == null) return;

        string newName = GoogleAPI.ClampName(nameInput ? nameInput.text : api.username);

        _saving = true;
        SetInteractable(false);
        SetStatus("Saving...");

        string previousName = api.username;
        int previousAvatar = api.avatarId;

        api.username = newName;
        api.avatarId = _avatarIndex;

        bool ok = await api.SaveProfileAsync();

        if (!ok)
        {
            // Keep the local state honest with what Firestore actually holds.
            api.username = previousName;
            api.avatarId = previousAvatar;
            _saving = false;
            SetInteractable(true);
            SetStatus("Save failed. Try again.");
            return;
        }

        if (LobbyPlayer.Local != null) LobbyPlayer.Local.ApplyProfile(newName, _avatarIndex);

        _saving = false;
        SetInteractable(true);
        SetStatus("");
        if (panelRoot) panelRoot.SetActive(false);
    }

    int AvatarCount
    {
        get
        {
            var sprites = Sprites;
            return sprites != null && sprites.Length > 0 ? sprites.Length : LobbyNetwork.AvatarCount;
        }
    }

    Sprite[] Sprites
    {
        get
        {
            if (avatarSprites != null && avatarSprites.Length > 0) return avatarSprites;
            return lobbyPanel != null ? lobbyPanel.avatarSprites : null;
        }
    }

    void RefreshHero()
    {
        var sprites = Sprites;
        if (heroImage && sprites != null && sprites.Length > 0)
        {
            heroImage.sprite = sprites[Mathf.Clamp(_avatarIndex, 0, sprites.Length - 1)];
            heroImage.color = Color.white;
        }
        if (heroLabel) heroLabel.text = $"Hero {_avatarIndex + 1} / {AvatarCount}";
    }

    void SetInteractable(bool value)
    {
        if (okButton) okButton.interactable = value;
        if (cancelButton) cancelButton.interactable = value;
        if (prevButton) prevButton.interactable = value;
        if (nextButton) nextButton.interactable = value;
        if (nameInput) nameInput.interactable = value;
    }

    void SetStatus(string msg)
    {
        if (statusText) statusText.text = msg;
    }
}
