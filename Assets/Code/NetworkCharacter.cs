using Fusion;
using TMPro;
using UnityEngine;

public class NetworkCharacter : NetworkBehaviour
{
    public const int STATE_IDLE = 0;
    public const int STATE_WALK = 1;
    public const int STATE_RUN = 2;
    public const int STATE_ROLL = 3;
    public const int STATE_JUMP = 4;
    public const int STATE_ATTACK = 5;

    public const int ACT_NONE = 0;
    public const int ACT_ROLL = 1;
    public const int ACT_JUMP = 2;
    public const int ACT_ATTACK = 3;
    public const int ACT_HEAVY_ATTACK = 4;
    public const int ACT_RUN_ATTACK = 5;
    public const int ACT_RUN_HEAVY_ATTACK = 6;
    public const int ACT_JUMP_ATTACK = 7;
    public const int ACT_JUMP_HEAVY_ATTACK = 8;
    public const int ACT_RUN_JUMP_HEAVY_ATTACK = 9;
    public const int ACT_IDLE_VARIATION = 10;

    [Header("Optional Name Label")]
    public TMP_Text nameLabel;
    public bool detachNameLabel = true;
    public Vector3 labelOffset = new Vector3(0f, 2.2f, 0f);

    [Header("Remote Smoothing")]
    public float remotePosLerpSpeed = 18f;
    public float remoteRotLerpSpeed = 20f;

    // ---- ข้อมูลผู้เล่นจาก Lobby ----
    [Networked] public int AvatarIndex { get; set; }
    [Networked] public NetworkString<_16> Nick { get; set; }

    // ---- ข้อมูลตำแหน่งและการหมุน (ซิงก์แทน NetworkTransform เพื่อไม่ให้บล็อก CharacterController) ----
    [Networked] public Vector3 NetPosition { get; set; }
    [Networked] public Quaternion NetRotation { get; set; }

    // ---- ข้อมูล Animation ที่ซิงก์ให้ทุกเครื่องเห็นตรงกัน ----
    [Networked] public int AnimState { get; set; }
    [Networked] public NetworkBool NetMoving { get; set; }
    [Networked] public NetworkBool NetGrounded { get; set; }
    [Networked] public NetworkBool NetInCombat { get; set; }
    [Networked] public NetworkBool NetSkipDraw { get; set; }
    [Networked] public float NetMoveX { get; set; }
    [Networked] public float NetMoveY { get; set; }

    // ---- ข้อมูล Trigger Action (Roll / Jump / Attack / IdleVariation) ----
    [Networked] public int ActionCounter { get; set; }
    [Networked] public int ActionType { get; set; }
    [Networked] public float ActionParamX { get; set; }
    [Networked] public float ActionParamY { get; set; }
    [Networked] public int ActionIntParam { get; set; }

    public static NetworkCharacter Local { get; private set; }

    public bool IsOwner => Object == null || !Object.IsValid || Object.HasStateAuthority;
    public bool IsSpawned => Object != null && Object.IsValid;

    Animator _anim;
    CharacterController _cc;
    Transform _labelTransform;
    int _appliedAvatar = -1;
    string _appliedNick;
    int _lastSeenActionCounter;

    void Awake()
    {
        _anim = GetComponent<Animator>();
        _cc = GetComponent<CharacterController>();
    }

    public override void Spawned()
    {
        _lastSeenActionCounter = ActionCounter;

        if (Object.HasStateAuthority)
        {
            Local = this;
            if (_cc != null) _cc.enabled = true;

            NetPosition = transform.position;
            NetRotation = transform.rotation;

            int avatar = 0;
            string nick = "Player";
            if (GoogleAPI.instance != null)
            {
                avatar = GoogleAPI.instance.avatarId;
                nick = GoogleAPI.ClampName(GoogleAPI.instance.username);
            }

            AvatarIndex = Mathf.Clamp(avatar, 0, LobbyNetwork.AvatarCount - 1);
            Nick = nick;
            NetGrounded = true;

            Runner.SetPlayerObject(Runner.LocalPlayer, Object);
        }
        else
        {
            // ปิด CharacterController บนตัวเพื่อน เพื่อให้ขยับตาม NetPosition ได้อย่างอิสระและไม่สั่นชนพื้น
            if (_cc != null) _cc.enabled = false;
            transform.SetPositionAndRotation(NetPosition, NetRotation);
        }

        SetUpNameLabel();
        ApplyLook(force: true);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Local == this) Local = null;
        DestroyDetachedLabel();
    }

    void OnDestroy()
    {
        DestroyDetachedLabel();
    }

    public override void FixedUpdateNetwork()
    {
        if (Object.HasStateAuthority)
        {
            NetPosition = transform.position;
            NetRotation = transform.rotation;
        }
    }

    public void SyncLocomotion(int animState, bool moving, bool grounded, float moveX, float moveY)
    {
        if (!IsSpawned || !Object.HasStateAuthority) return;

        AnimState = animState;
        NetMoving = moving;
        NetGrounded = grounded;
        NetMoveX = moveX;
        NetMoveY = moveY;
        NetPosition = transform.position;
        NetRotation = transform.rotation;
    }

    public void SyncCombat(bool inCombat, bool skipDraw)
    {
        if (!IsSpawned || !Object.HasStateAuthority) return;

        NetInCombat = inCombat;
        NetSkipDraw = skipDraw;
    }

    public void RecordAction(int actionType, float paramX = 0f, float paramY = 0f, int intParam = 0)
    {
        if (!IsSpawned || !Object.HasStateAuthority) return;

        ActionType = actionType;
        ActionParamX = paramX;
        ActionParamY = paramY;
        ActionIntParam = intParam;
        ActionCounter++;
    }

    public override void Render()
    {
        ApplyLook(force: false);

        if (!Object.HasStateAuthority)
        {
            // เลื่อนตำแหน่งและการหมุนของตัวละครเพื่อนอย่างนุ่มนวล
            transform.position = Vector3.Lerp(transform.position, NetPosition, remotePosLerpSpeed * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, NetRotation, remoteRotLerpSpeed * Time.deltaTime);

            if (_anim != null)
            {
                ApplyRemoteAnimator();
            }
        }
    }

    void ApplyRemoteAnimator()
    {
        _anim.SetBool("Moving", NetMoving);
        _anim.SetBool("Grounded", NetGrounded);
        _anim.SetFloat("MoveX", NetMoveX);
        _anim.SetFloat("MoveY", NetMoveY);

        _anim.SetBool("SkipDraw", NetSkipDraw);
        _anim.SetBool("InCombat", NetInCombat);

        if (_lastSeenActionCounter != ActionCounter)
        {
            _lastSeenActionCounter = ActionCounter;
            ExecuteRemoteAction(ActionType, ActionParamX, ActionParamY, ActionIntParam);
        }
    }

    void ExecuteRemoteAction(int type, float px, float py, int intParam)
    {
        switch (type)
        {
            case ACT_ROLL:
                ResetRemoteAttackTriggers();
                _anim.SetFloat("RollX", px);
                _anim.SetFloat("RollY", py);
                _anim.ResetTrigger("Roll");
                _anim.SetTrigger("Roll");
                var remoteSmokeVfx = GetComponent<MMODodgeSmokeController>();
                if (remoteSmokeVfx != null) remoteSmokeVfx.TriggerSmokeRoll();
                break;

            case ACT_JUMP:
                _anim.SetFloat("JumpX", px);
                _anim.SetFloat("JumpY", py);
                _anim.ResetTrigger("Jump");
                _anim.SetTrigger("Jump");
                break;

            case ACT_ATTACK:
                FireRemoteTrigger("Attack");
                break;
            case ACT_HEAVY_ATTACK:
                FireRemoteTrigger("HeavyAttack");
                break;
            case ACT_RUN_ATTACK:
                FireRemoteTrigger("RunAttack");
                break;
            case ACT_RUN_HEAVY_ATTACK:
                FireRemoteTrigger("RunHeavyAttack");
                break;
            case ACT_JUMP_ATTACK:
                FireRemoteTrigger("JumpAttack");
                break;
            case ACT_JUMP_HEAVY_ATTACK:
                FireRemoteTrigger("JumpHeavyAttack");
                break;
            case ACT_RUN_JUMP_HEAVY_ATTACK:
                FireRemoteTrigger("RunJumpHeavyAttack");
                break;

            case ACT_IDLE_VARIATION:
                _anim.SetInteger("IdleVariant", intParam);
                _anim.ResetTrigger("IdleVariation");
                _anim.SetTrigger("IdleVariation");
                break;
        }
    }

    void FireRemoteTrigger(string trigger)
    {
        _anim.ResetTrigger(trigger);
        _anim.SetTrigger(trigger);
    }

    void ResetRemoteAttackTriggers()
    {
        _anim.ResetTrigger("Attack");
        _anim.ResetTrigger("HeavyAttack");
        _anim.ResetTrigger("RunAttack");
        _anim.ResetTrigger("RunHeavyAttack");
        _anim.ResetTrigger("JumpAttack");
        _anim.ResetTrigger("JumpHeavyAttack");
        _anim.ResetTrigger("RunJumpHeavyAttack");
    }

    public static int TriggerNameToActionType(string trigger)
    {
        return trigger switch
        {
            "Attack" => ACT_ATTACK,
            "HeavyAttack" => ACT_HEAVY_ATTACK,
            "RunAttack" => ACT_RUN_ATTACK,
            "RunHeavyAttack" => ACT_RUN_HEAVY_ATTACK,
            "JumpAttack" => ACT_JUMP_ATTACK,
            "JumpHeavyAttack" => ACT_JUMP_HEAVY_ATTACK,
            "RunJumpHeavyAttack" => ACT_RUN_JUMP_HEAVY_ATTACK,
            _ => ACT_NONE
        };
    }

    void SetUpNameLabel()
    {
        if (nameLabel == null) return;

        _labelTransform = nameLabel.transform;
        labelOffset = _labelTransform.localPosition;
        if (detachNameLabel) _labelTransform.SetParent(null, worldPositionStays: true);
    }

    void LateUpdate()
    {
        if (_labelTransform == null) return;
        _labelTransform.position = transform.position + labelOffset;
        var cam = Camera.main;
        if (cam != null) _labelTransform.rotation = cam.transform.rotation;
    }

    void DestroyDetachedLabel()
    {
        if (detachNameLabel && _labelTransform != null) Destroy(_labelTransform.gameObject);
        _labelTransform = null;
    }

    void ApplyLook(bool force)
    {
        if (force || _appliedAvatar != AvatarIndex)
        {
            _appliedAvatar = AvatarIndex;
            if (nameLabel != null) nameLabel.color = AvatarPalette.Get(AvatarIndex);
        }

        string nick = Nick.Value;
        if (nameLabel != null && (force || _appliedNick != nick))
        {
            _appliedNick = nick;
            nameLabel.text = Object.HasStateAuthority ? $"{nick} (You)" : nick;
        }
    }
}