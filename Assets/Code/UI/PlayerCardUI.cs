using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One of the 4 slots in the lobby. Has two visual stages:
/// - Blank  : nobody has taken this slot yet (grey placeholder, no name).
/// - Filled : a player is in the slot, so the coloured avatar, the name and the
///            ready badge are shown.
/// </summary>
public class PlayerCardUI : MonoBehaviour
{
    [Header("Blank stage")]
    public GameObject blankRoot;

    [Header("Filled stage")]
    public GameObject filledRoot;
    public Image avatarImage;
    public TMP_Text nameText;
    public TMP_Text readyText;
    public Image readyBadge;
    [Tooltip("Optional 'HOST' tag - shown on the player that owns the room (the one with the PLAY button).")]
    public GameObject hostBadge;

    [Header("Colors")]
    public Color readyColor = new Color(0.20f, 0.78f, 0.35f);
    public Color notReadyColor = new Color(0.80f, 0.27f, 0.27f);
    public Color localHighlight = new Color(1f, 0.85f, 0.30f);
    public Image frame;

    Color _frameDefault;
    bool _frameCached;

    void Awake()
    {
        if (frame != null && !_frameCached)
        {
            _frameDefault = frame.color;
            _frameCached = true;
        }
    }

    /// <summary>Empty seat - waiting for a player.</summary>
    public void ShowBlank()
    {
        if (blankRoot) blankRoot.SetActive(true);
        if (filledRoot) filledRoot.SetActive(false);
        if (hostBadge) hostBadge.SetActive(false);
        if (frame && _frameCached) frame.color = _frameDefault;
    }

    /// <summary>Seat taken - show avatar, name, ready state and whether this player owns the room.</summary>
    public void ShowPlayer(Sprite avatar, string playerName, bool isReady, bool isLocal, bool isHost = false)
    {
        if (hostBadge) hostBadge.SetActive(isHost);

        if (blankRoot) blankRoot.SetActive(false);
        if (filledRoot) filledRoot.SetActive(true);

        if (avatarImage)
        {
            if (avatar != null) avatarImage.sprite = avatar;
            avatarImage.color = Color.white;   // blank cards are greyed out, filled ones are not
        }

        if (nameText) nameText.text = isLocal ? $"{playerName} (You)" : playerName;

        if (readyText) readyText.text = isReady ? "READY" : "NOT READY";
        if (readyBadge) readyBadge.color = isReady ? readyColor : notReadyColor;
        if (frame && _frameCached) frame.color = isLocal ? localHighlight : _frameDefault;
    }
}
