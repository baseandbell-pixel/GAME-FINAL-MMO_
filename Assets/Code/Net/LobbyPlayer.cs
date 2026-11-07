using System.Collections.Generic;
using Fusion;
using UnityEngine;

public class LobbyPlayer : NetworkBehaviour
{
    public static readonly List<LobbyPlayer> All = new List<LobbyPlayer>();

    public static LobbyPlayer Local { get; private set; }

    [Networked] public NetworkString<_16> Nick { get; set; }
    [Networked] public int AvatarIndex { get; set; }
    [Networked] public NetworkBool IsReady { get; set; }

    public override void Spawned()
    {
        All.Add(this);

        if (Object.HasStateAuthority)
        {
            Local = this;

            ApplyProfile(GoogleAPI.instance.username, GoogleAPI.instance.avatarId);
            IsReady = false;

            Runner.SetPlayerObject(Runner.LocalPlayer, Object);
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        All.Remove(this);
        if (Local == this) Local = null;
    }


    public void ApplyProfile(string nick, int avatarIndex)
    {
        if (!Object.HasStateAuthority) return;
        Nick = GoogleAPI.ClampName(nick);
        AvatarIndex = Mathf.Clamp(avatarIndex, 0, LobbyNetwork.AvatarCount - 1);
    }

    public void ToggleReady()
    {
        if (!Object.HasStateAuthority) return;
        IsReady = !IsReady;
    }
}
