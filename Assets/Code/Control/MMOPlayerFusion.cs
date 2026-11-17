
using System;
using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(NetworkTransform))]
[RequireComponent(typeof(MMOPlayerController))]
public sealed class MMOPlayerFusion : NetworkBehaviour
{
    // ---------------------------------------------------------
    // NETWORKED MOVEMENT AND ANIMATION
    // ---------------------------------------------------------

    [Networked] public float NetworkSpeed { get; set; }

    [Networked] public NetworkBool NetworkGrounded { get; set; }

    // 0 = Locomotion
    // 1 = Jump
    // 2 = Attack1
    // 3 = Attack2

    [Networked] public int NetworkAction { get; set; }

    [Networked] public int NetworkActionSequence { get; set; }

    [Networked] public int NetworkHealth { get; set; }

    [Networked] public int NetworkMaxHealth { get; set; }

    [Networked] public int NetworkLevel { get; set; }

    [Networked] public NetworkBool NetworkDead { get; set; }

    [Networked] public int NetworkWeaponRarity { get; set; }

    [Networked] public int NetworkWeaponUpgrade { get; set; }

    // ---------------------------------------------------------
    // NETWORKED PLAYER IDENTITY
    // ---------------------------------------------------------

    [Networked] public int AvatarIndex { get; set; }

    [Networked] public NetworkString<_16> Nick { get; set; }

    // ---------------------------------------------------------
    // REFERENCES
    // ---------------------------------------------------------

    [Header("Player Name")]

    [SerializeField]
    private TMP_Text nameLabel;

    [SerializeField]
    private Vector3 nameLabelLocalPosition =
        new Vector3(0f, 2.3f, 0f);

    [SerializeField]
    private bool showYouSuffix = true;

    [Header("Camera")]

    [SerializeField]
    private MMOCameraController cameraController;

    // ---------------------------------------------------------
    // PRIVATE VARIABLES
    // ---------------------------------------------------------

    private MMOPlayerController playerController;

    private MMOPlayerCombat playerCombat;

    private MMOPlayerInventory playerInventory;

    private Transform nameLabelTransform;

    private bool isLocalPlayer;

    private string appliedNickname;

    private const string DefaultPlayerName = "Player";

    // ---------------------------------------------------------
    // PROPERTIES
    // ---------------------------------------------------------

    public static readonly List<MMOPlayerFusion> AllPlayers = new List<MMOPlayerFusion>();

    public static event Action<string, string> OnChatMessageReceived;

    public bool IsLocalPlayer => isLocalPlayer;

    public string PlayerName => Nick.Value;

    // ---------------------------------------------------------
    // AWAKE
    // ---------------------------------------------------------

    private void Awake()
    {
        playerController = GetComponent<MMOPlayerController>();
        playerCombat = GetComponent<MMOPlayerCombat>();

        if (nameLabel == null)
        {
            nameLabel = GetComponentInChildren<TMP_Text>(true);
        }

        if (nameLabel != null)
        {
            nameLabelTransform = nameLabel.transform;

            // Keep the name inside the Player hierarchy.
            // It will follow the network character automatically.

            nameLabelTransform.localPosition =
                nameLabelLocalPosition;
        }
        else
        {
            Debug.LogWarning(
                "[MMOPlayerFusion] Player Name TMP_Text is not assigned.",
                this
            );
        }
    }

    // ---------------------------------------------------------
    // NETWORK SPAWNED
    // ---------------------------------------------------------

    public override void Spawned()
    {
        if (!AllPlayers.Contains(this))
        {
            AllPlayers.Add(this);
        }

        isLocalPlayer = Object.HasStateAuthority;

        if (isLocalPlayer)
        {
            InitializeLocalPlayerIdentity();

            if (Runner.LocalPlayer != PlayerRef.None)
            {
                Runner.SetPlayerObject(
                    Runner.LocalPlayer,
                    Object
                );
            }
        }

        // Find the MMORPG camera.
        if (cameraController == null)
        {
            cameraController = MMOCameraController.Instance;
        }

        if (cameraController == null)
        {
            cameraController =
                FindAnyObjectByType<MMOCameraController>();
        }

        // Enable input only for the owner.
        playerController.SetLocalAuthority(
            isLocalPlayer,
            cameraController
        );

        if (isLocalPlayer && cameraController != null)
        {
            cameraController.SetTarget(transform);

            if (LobbyNetwork.Instance != null &&
                LobbyNetwork.Instance.CurrentState == LobbyNetwork.State.InGame)
            {
                cameraController.SetGameplayEnabled(true);
            }
        }

        if (Object.HasStateAuthority)
        {
            NetworkSpeed = 0f;
            NetworkGrounded = true;
            NetworkAction = 0;
            NetworkActionSequence = 0;
            NetworkHealth = 1200;
            NetworkMaxHealth = 1200;
            NetworkLevel = 5;
            NetworkDead = false;
            NetworkWeaponRarity = -1;
            NetworkWeaponUpgrade = 0;
        }

        ApplyPlayerName();
    }

    // ---------------------------------------------------------
    // INITIALIZE GOOGLE / FIREBASE PLAYER DATA
    // ---------------------------------------------------------

    private void InitializeLocalPlayerIdentity()
    {
        int avatar = 0;
        string nickname = DefaultPlayerName;

        if (GoogleAPI.instance != null)
        {
            avatar = GoogleAPI.instance.avatarId;

            nickname = GoogleAPI.ClampName(
                GoogleAPI.instance.username
            );
        }

        int avatarCount = Mathf.Max(
            1,
            LobbyNetwork.AvatarCount
        );

        AvatarIndex = Mathf.Clamp(
            avatar,
            0,
            avatarCount - 1
        );

        if (string.IsNullOrWhiteSpace(nickname))
        {
            nickname = DefaultPlayerName;
        }

        Nick = nickname;

        Debug.Log(
            "[MMOPlayerFusion] Player Identity Initialized: " +
            nickname +
            " | Avatar: " +
            AvatarIndex
        );
    }

    // ---------------------------------------------------------
    // NETWORK MOVEMENT UPDATE
    // ---------------------------------------------------------

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority)
        {
            return;
        }

        if (playerController == null)
        {
            return;
        }

        // CharacterController movement must happen on Fusion's simulation tick.
        // NetworkTransform then captures a coherent state for remote interpolation.
        playerController.SimulateNetworked(Runner.DeltaTime); // <--- จุดที่แก้ไข/เพิ่มเติม

        NetworkSpeed =
            playerController.NetworkAnimationSpeed;

        NetworkGrounded =
            playerController.NetworkIsGrounded;

        NetworkAction =
            playerController.NetworkActionCode;

        NetworkActionSequence =
            playerController.NetworkActionSequence;

        if (playerCombat == null)
        {
            playerCombat = GetComponent<MMOPlayerCombat>();
        }

        if (playerCombat != null)
        {
            NetworkHealth = playerCombat.CurrentHealth;
            NetworkMaxHealth = playerCombat.MaxHealth;
            NetworkLevel = playerCombat.playerLevel;
            NetworkDead = playerCombat.IsDead;
        }

        if (playerInventory == null)
        {
            playerInventory = GetComponent<MMOPlayerInventory>();
        }

        if (playerInventory != null)
        {
            MMOItemInstance wpn = playerInventory.GetEquipped(EquipSlot.Weapon);
            NetworkWeaponRarity = wpn != null ? (int)wpn.rarity : -1;
            NetworkWeaponUpgrade = wpn != null ? wpn.upgradeLevel : 0;
        }
    }

    // ---------------------------------------------------------
    // NETWORK RENDER
    // ---------------------------------------------------------

    public override void Render()
    {
        // Update nickname and avatar data on all clients.
        ApplyPlayerName();

        if (isLocalPlayer)
        {
            return;
        }

        if (playerInventory == null)
        {
            playerInventory = GetComponent<MMOPlayerInventory>();
        }
        playerInventory?.ApplyWeaponVisualState(NetworkWeaponRarity, NetworkWeaponUpgrade);

        if (playerController == null)
        {
            return;
        }

        // Apply movement and animation to remote characters.
        playerController.ApplyNetworkAnimation(
            NetworkSpeed,
            (bool)NetworkGrounded,
            NetworkAction,
            NetworkActionSequence,
            (bool)NetworkDead
        );
    }

    // ---------------------------------------------------------
    // PLAYER NAME DISPLAY
    // ---------------------------------------------------------

    private void ApplyPlayerName()
    {
        if (nameLabel == null)
        {
            return;
        }

        string nickname = Nick.Value;

        if (string.IsNullOrWhiteSpace(nickname))
        {
            nickname = DefaultPlayerName;
        }

        if (isLocalPlayer && showYouSuffix)
        {
            nickname += " (You)";
        }
        else if (!isLocalPlayer && Object != null && Object.IsValid && NetworkLevel > 0)
        {
            nickname = $"Lv.{NetworkLevel} {nickname}";
        }

        if (appliedNickname != nickname)
        {
            appliedNickname = nickname;

            nameLabel.text = nickname;
        }
    }

    // ---------------------------------------------------------
    // NAMEPLATE BILLBOARD
    // ---------------------------------------------------------

    private void LateUpdate()
    {
        if (nameLabelTransform == null)
        {
            return;
        }

        // Keep name above the character.
        nameLabelTransform.localPosition =
            nameLabelLocalPosition;

        Camera mainCamera = Camera.main;

        if (mainCamera != null)
        {
            // Make the text face the camera.
            nameLabelTransform.rotation =
                mainCamera.transform.rotation;
        }
    }

    // ---------------------------------------------------------
    // DESPAWN
    // ---------------------------------------------------------

    public override void Despawned(
        NetworkRunner runner,
        bool hasState
    )
    {
        AllPlayers.Remove(this);

        if (isLocalPlayer && cameraController != null)
        {
            if (cameraController.Target == transform)
            {
                cameraController.SetTarget(null);
            }
        }

        isLocalPlayer = false;
    }

    public void SendChatMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        string trimmed = message.Trim();
        if (trimmed.Length > 120) trimmed = trimmed.Substring(0, 120);

        string sender = string.IsNullOrWhiteSpace(Nick.Value) ? DefaultPlayerName : Nick.Value;
        if (Object != null && Object.IsValid && Runner != null && Runner.IsRunning)
        {
            RPC_BroadcastChat(sender, trimmed);
        }
        else
        {
            OnChatMessageReceived?.Invoke(sender, trimmed);
        }
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    private void RPC_BroadcastChat(string senderName, string message)
    {
        OnChatMessageReceived?.Invoke(senderName, message);
    }
}
