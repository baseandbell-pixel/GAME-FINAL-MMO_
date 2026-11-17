using Fusion;
using UnityEngine;

/// <summary>
/// Dual-mode Photon Fusion Shared Mode synchronization bridge for MMOMonsterAI.
/// - In Standalone mode (no active NetworkRunner), all properties safely report local authority.
/// - In Photon Fusion Shared Mode, the StateAuthority (Master Client) drives AI, movement (NetworkTransform),
///   HP, states, attacks, skills, and broadcasts damage/status feedback to all connected peers.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(MMOMonsterAI))]
public sealed class MMOMonsterNetwork : NetworkBehaviour
{
    public const int ActionNone = 0;
    public const int ActionAttack0 = 1;
    public const int ActionAttack1 = 2;
    public const int ActionAttack2 = 3;
    public const int ActionSkillCast = 4;
    public const int ActionSkillRelease = 5;
    public const int ActionHitReact = 6;

    [Networked] public int NetHealth { get; set; }
    [Networked] public int NetMaxHealth { get; set; }
    [Networked] public int NetState { get; set; }
    [Networked] public float NetWorldSpeed { get; set; }
    [Networked] public NetworkBool NetStunned { get; set; }
    [Networked] public int NetActionCode { get; set; }
    [Networked] public int NetActionSequence { get; set; }

    private MMOMonsterAI _monsterAI;
    private int _lastProcessedActionSeq = -1;
    private int _localActionSeq;

    public bool IsNetworkedActive =>
        Object != null && Object.IsValid && Runner != null && Runner.IsRunning;

    public bool HasSimulationAuthority =>
        !IsNetworkedActive || Object.HasStateAuthority;

    private void Awake()
    {
        _monsterAI = GetComponent<MMOMonsterAI>();
    }

    public override void Spawned()
    {
        if (_monsterAI == null)
        {
            _monsterAI = GetComponent<MMOMonsterAI>();
        }

        if (Object.HasStateAuthority && _monsterAI != null)
        {
            _monsterAI.EnsureReferencesInitialized();
            NetMaxHealth = _monsterAI.MaxHealth;
            NetHealth = _monsterAI.CurrentHealth;
            NetState = (int)_monsterAI.CurrentState;
            NetWorldSpeed = 0f;
            NetStunned = false;
            NetActionCode = ActionNone;
            NetActionSequence = 0;
        }
        else if (_monsterAI != null)
        {
            _lastProcessedActionSeq = NetActionSequence;
            _monsterAI.ApplyNetworkSnapshot(
                NetHealth,
                NetMaxHealth,
                (MMOMonsterAI.AIState)NetState,
                NetWorldSpeed,
                (bool)NetStunned);
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!IsNetworkedActive || !Object.HasStateAuthority || _monsterAI == null)
        {
            return;
        }

        _monsterAI.SimulateAuthoritativeStep(Runner.DeltaTime);

        NetMaxHealth = _monsterAI.MaxHealth;
        NetHealth = _monsterAI.CurrentHealth;
        NetState = (int)_monsterAI.CurrentState;
        NetWorldSpeed = _monsterAI.CurrentWorldSpeed;
        NetStunned = _monsterAI.IsStunned;
    }

    public override void Render()
    {
        if (!IsNetworkedActive || Object.HasStateAuthority || _monsterAI == null)
        {
            return;
        }

        _monsterAI.ApplyNetworkSnapshot(
            NetHealth,
            NetMaxHealth,
            (MMOMonsterAI.AIState)NetState,
            NetWorldSpeed,
            (bool)NetStunned);

        int seq = NetActionSequence;
        if (seq != _lastProcessedActionSeq)
        {
            _lastProcessedActionSeq = seq;
            _monsterAI.ApplyNetworkAction(NetActionCode);
        }
    }

    public void PublishAction(int actionCode)
    {
        if (!IsNetworkedActive || !Object.HasStateAuthority)
        {
            return;
        }

        _localActionSeq++;
        NetActionCode = actionCode;
        NetActionSequence = _localActionSeq;
    }

    public void RequestDamageFromClient(int amount, bool isCritical, Transform attacker, Vector3 hitWorldPoint)
    {
        if (!IsNetworkedActive)
        {
            _monsterAI.TakeDamage(amount, isCritical, attacker, hitWorldPoint);
            return;
        }

        NetworkId attackerId = default;
        if (attacker != null)
        {
            NetworkObject attackerObj = attacker.GetComponentInParent<NetworkObject>();
            if (attackerObj != null && attackerObj.IsValid)
            {
                attackerId = attackerObj.Id;
            }
        }

        if (Object.HasStateAuthority)
        {
            ProcessAuthoritativeDamage(amount, isCritical, attackerId, hitWorldPoint);
        }
        else
        {
            RPC_RequestDamage(amount, isCritical, attackerId, hitWorldPoint);
        }
    }

    public void RequestStunFromClient(float duration)
    {
        if (!IsNetworkedActive)
        {
            _monsterAI.ApplyStun(duration);
            return;
        }

        if (Object.HasStateAuthority)
        {
            _monsterAI.ApplyStun(duration);
        }
        else
        {
            RPC_RequestStun(duration);
        }
    }

    public void BroadcastHitFeedback(int damage, bool isCritical, Vector3 hitPoint)
    {
        if (!IsNetworkedActive || !Object.HasStateAuthority)
        {
            return;
        }

        RPC_BroadcastHitFeedback(damage, isCritical, hitPoint);
    }

    public void BroadcastStatusPopup(Vector3 worldPos, string text, Color color, float scale)
    {
        if (!IsNetworkedActive || !Object.HasStateAuthority)
        {
            return;
        }

        RPC_BroadcastStatusPopup(worldPos, text, color.r, color.g, color.b, color.a, scale);
    }

    public void BroadcastBossTelegraph(Vector3 center, float radius, float duration)
    {
        if (!IsNetworkedActive || !Object.HasStateAuthority)
        {
            return;
        }

        RPC_BroadcastBossTelegraph(center, radius, duration);
    }

    public void BroadcastBossSlamBurst(Vector3 center)
    {
        if (!IsNetworkedActive || !Object.HasStateAuthority)
        {
            return;
        }

        RPC_BroadcastBossSlamBurst(center);
    }

    public void DeliverDamageToPlayer(Transform targetPlayer, int damage, bool isCritical)
    {
        if (targetPlayer == null)
        {
            return;
        }

        if (!IsNetworkedActive)
        {
            MMOPlayerCombat combat = targetPlayer.GetComponent<MMOPlayerCombat>();
            if (combat != null)
            {
                combat.TakeDamageFromMonster(damage, isCritical, _monsterAI);
            }
            return;
        }

        NetworkObject playerNetObj = targetPlayer.GetComponentInParent<NetworkObject>();
        if (playerNetObj != null && playerNetObj.IsValid)
        {
            RPC_DeliverDamageToPlayer(playerNetObj.Id, damage, isCritical);
        }
    }

    public void AwardKillExpToKiller(Transform killer, int exp)
    {
        if (killer == null || exp <= 0)
        {
            return;
        }

        if (!IsNetworkedActive)
        {
            MMOPlayerCombat pc = killer.GetComponent<MMOPlayerCombat>();
            if (pc != null)
            {
                pc.AwardKillExp(exp, _monsterAI);
            }
            return;
        }

        NetworkObject killerNetObj = killer.GetComponentInParent<NetworkObject>();
        if (killerNetObj != null && killerNetObj.IsValid)
        {
            RPC_AwardKillExp(killerNetObj.Id, exp);
        }
    }

    private void ProcessAuthoritativeDamage(int amount, bool isCritical, NetworkId attackerId, Vector3 hitWorldPoint)
    {
        Transform attackerTransform = null;
        if (attackerId.IsValid && Runner != null)
        {
            NetworkObject attackerObj = Runner.FindObject(attackerId);
            if (attackerObj != null)
            {
                attackerTransform = attackerObj.transform;
            }
        }

        _monsterAI.TakeDamage(amount, isCritical, attackerTransform, hitWorldPoint);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestDamage(int amount, NetworkBool isCritical, NetworkId attackerId, Vector3 hitWorldPoint)
    {
        ProcessAuthoritativeDamage(amount, (bool)isCritical, attackerId, hitWorldPoint);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestStun(float duration)
    {
        if (_monsterAI != null)
        {
            _monsterAI.ApplyStun(duration);
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_BroadcastHitFeedback(int damage, NetworkBool isCritical, Vector3 hitWorldPoint)
    {
        // StateAuthority already spawned local feedback inside TakeDamage().
        if (Object != null && Object.HasStateAuthority)
        {
            return;
        }

        if (_monsterAI != null)
        {
            _monsterAI.PlayRemoteHitVisuals(damage, (bool)isCritical, hitWorldPoint);
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_BroadcastStatusPopup(Vector3 worldPos, string text, float r, float g, float b, float a, float scale)
    {
        if (Object != null && Object.HasStateAuthority)
        {
            return;
        }

        MMOCombatVFX.SpawnStatusPopup(worldPos, text, new Color(r, g, b, a), scale);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_BroadcastBossTelegraph(Vector3 center, float radius, float duration)
    {
        if (Object != null && Object.HasStateAuthority)
        {
            return;
        }

        MMOCombatVFX.SpawnGroundTelegraph(center, radius, duration, new Color(1f, 0.25f, 0.12f, 0.95f));
        MMOAudioManager.PlayBossCast(center);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_BroadcastBossSlamBurst(Vector3 center)
    {
        if (Object != null && Object.HasStateAuthority)
        {
            return;
        }

        MMOCombatVFX.SpawnHitEffect(center, isCritical: true, new Color(1f, 0.35f, 0.1f, 1f));
        MMOAudioManager.PlayStunStrike(center);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_DeliverDamageToPlayer(NetworkId playerNetId, int damage, NetworkBool isCritical)
    {
        if (Runner == null || !playerNetId.IsValid)
        {
            return;
        }

        NetworkObject playerObj = Runner.FindObject(playerNetId);
        if (playerObj == null || !playerObj.HasStateAuthority)
        {
            return;
        }

        MMOPlayerCombat combat = playerObj.GetComponent<MMOPlayerCombat>();
        if (combat != null)
        {
            combat.TakeDamageFromMonster(damage, (bool)isCritical, _monsterAI);
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_AwardKillExp(NetworkId killerNetId, int exp)
    {
        if (Runner == null || !killerNetId.IsValid)
        {
            return;
        }

        NetworkObject killerObj = Runner.FindObject(killerNetId);
        if (killerObj == null || !killerObj.HasStateAuthority)
        {
            return;
        }

        MMOPlayerCombat combat = killerObj.GetComponent<MMOPlayerCombat>();
        if (combat != null)
        {
            combat.AwardKillExp(exp, _monsterAI);
        }
    }
}
