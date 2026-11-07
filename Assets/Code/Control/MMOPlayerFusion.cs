
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

    private Transform nameLabelTransform;

    private bool isLocalPlayer;

    private string appliedNickname;

    private int lastAppliedAvatar = -1;

    private const string DefaultPlayerName = "Player";

    // ---------------------------------------------------------
    // PROPERTIES
    // ---------------------------------------------------------

    public bool IsLocalPlayer => isLocalPlayer;

    public string PlayerName => Nick.Value;

    // ---------------------------------------------------------
    // AWAKE
    // ---------------------------------------------------------

    private void Awake()
    {
        playerController = GetComponent<MMOPlayerController>();

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
                FindObjectOfType<MMOCameraController>();
        }

        // Enable input only for the owner.
        playerController.SetLocalAuthority(
            isLocalPlayer,
            cameraController
        );

        if (isLocalPlayer && cameraController != null)
        {
            cameraController.SetTarget(transform);
        }

        if (Object.HasStateAuthority)
        {
            NetworkSpeed = 0f;
            NetworkGrounded = true;
            NetworkAction = 0;
            NetworkActionSequence = 0;
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

        NetworkSpeed =
            playerController.NetworkAnimationSpeed;

        NetworkGrounded =
            playerController.NetworkIsGrounded;

        NetworkAction =
            playerController.NetworkActionCode;

        NetworkActionSequence =
            playerController.NetworkActionSequence;
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

        if (playerController == null)
        {
            return;
        }

        // Apply movement and animation to remote characters.
        playerController.ApplyNetworkAnimation(
            NetworkSpeed,
            (bool)NetworkGrounded,
            NetworkAction,
            NetworkActionSequence
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
        if (isLocalPlayer && cameraController != null)
        {
            if (cameraController.Target == transform)
            {
                cameraController.SetTarget(null);
            }
        }

        isLocalPlayer = false;
    }
}
