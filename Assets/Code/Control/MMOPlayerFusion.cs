using Fusion;
using UnityEngine;

/// <summary>
/// Fusion Shared Mode adapter for a player character.
/// NetworkTransform replicates the root transform; these Networked values replicate locomotion and action events.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(NetworkTransform))]
[RequireComponent(typeof(MMOPlayerController))]
public sealed class MMOPlayerFusion : NetworkBehaviour
{
    [Networked] public float NetworkSpeed { get; set; }
    [Networked] public NetworkBool NetworkGrounded { get; set; }
    [Networked] public int NetworkAction { get; set; }
    [Networked] public int NetworkActionSequence { get; set; }

    private MMOPlayerController _playerController;
    private bool _isLocalPlayer;

    public override void Spawned()
    {
        _playerController = GetComponent<MMOPlayerController>();
        _isLocalPlayer = Object.HasStateAuthority;

        MMOCameraController cameraController = MMOCameraController.Instance;
        if (cameraController == null)
        {
            cameraController = FindObjectOfType<MMOCameraController>();
        }

        _playerController.SetLocalAuthority(_isLocalPlayer, cameraController);

        if (Object.HasStateAuthority)
        {
            NetworkSpeed = 0f;
            NetworkGrounded = true;
            NetworkAction = 0;
            NetworkActionSequence = 0;
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority || _playerController == null)
        {
            return;
        }

        NetworkSpeed = _playerController.NetworkAnimationSpeed;
        NetworkGrounded = _playerController.NetworkIsGrounded;
        NetworkAction = _playerController.NetworkActionCode;
        NetworkActionSequence = _playerController.NetworkActionSequence;
    }

    public override void Render()
    {
        if (_isLocalPlayer || _playerController == null)
        {
            return;
        }

        _playerController.ApplyNetworkAnimation(
            NetworkSpeed,
            (bool)NetworkGrounded,
            NetworkAction,
            NetworkActionSequence);
    }
}
