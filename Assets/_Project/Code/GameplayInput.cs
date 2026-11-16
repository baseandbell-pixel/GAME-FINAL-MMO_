using UnityEngine;

/// <summary>
/// Global gate for gameplay input. UI that needs the mouse (inventory, world map) raises the flag;
/// combat, camera look and lock-on read it. Kept static and tiny on purpose.
/// </summary>
public static class GameplayInput
{
    private static int _uiBlockers;

    /// <summary>True while any blocking UI (inventory, world map) is open.</summary>
    public static bool IsBlocked => _uiBlockers > 0;

    public static void PushBlocker(string who)
    {
        _uiBlockers++;
        Debug.Log($"[GameplayInput] Blocker +1 by {who} (total {_uiBlockers}).");
    }

    public static void PopBlocker(string who)
    {
        _uiBlockers = Mathf.Max(0, _uiBlockers - 1);
        Debug.Log($"[GameplayInput] Blocker -1 by {who} (total {_uiBlockers}).");
    }

    // Domain reload is disabled in some projects; make sure play sessions never start blocked.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => _uiBlockers = 0;
}
