
using UnityEngine;

[RequireComponent(typeof(Animator))]
public class IdleVariationController : MonoBehaviour
{
    [Header("Idle Waiting Time")]
    public float minIdleWait = 8f;
    public float maxIdleWait = 15f;

    [Header("จำนวนท่าสุ่ม")]
    [Range(1, 4)]
    public int variationCount = 4;

    [Header("Animator Parameters")]
    public string idleVariantParameter = "IdleVariant";
    public string idleVariationTrigger = "IdleVariation";
    public string movingParameter = "Moving";
    public string groundedParameter = "Grounded";
    public string deadParameter = "Dead";

    [Header("Base Layer State Names")]
    public string unarmedIdleState = "Base Layer.Unarmed Idle";
    public string greatSwordIdleState = "Base Layer.Great Sword Idle";

    public string unarmedLookPrefix = "Base Layer.Unarmed Look ";
    public string greatSwordLookPrefix = "Base Layer.Great Sword Look ";

    [Header("Weapon Action Layer")]
    public string weaponLayerName = "Weapon Action";
    public string drawStateName = "Draw A Great Sword";
    public string sheathStateName = "Sheath A Great Sword";

    [Header("Safety")]
    public float variationTimeout = 20f;

    Animator anim;
    int weaponLayer = -1;

    float idleTimer;
    float variationTimer;

    bool timerStarted;
    bool variationPending;
    bool variationStateSeen;

    bool activeCombat;
    int activeVariant;

    int lastUnarmedVariant;
    int lastCombatVariant;

    void Awake()
    {
        anim = GetComponent<Animator>();
        weaponLayer = anim.GetLayerIndex(weaponLayerName);
    }

    void Update()
    {
        if (anim == null || !anim.isActiveAndEnabled)
            return;

        if (variationPending)
        {
            TrackVariation();
            return;
        }

        if (!CanStartVariation(out bool combatMode))
        {
            timerStarted = false;
            idleTimer = 0f;
            return;
        }

        if (!timerStarted)
        {
            float min = Mathf.Max(0f, minIdleWait);
            float max = Mathf.Max(min, maxIdleWait);

            idleTimer = Random.Range(min, max);
            timerStarted = true;
            return;
        }

        idleTimer -= Time.deltaTime;

        if (idleTimer > 0f)
            return;

        activeCombat = combatMode;
        activeVariant = GetNextVariant(combatMode);

        anim.SetInteger(idleVariantParameter, activeVariant);

        anim.ResetTrigger(idleVariationTrigger);
        anim.SetTrigger(idleVariationTrigger);

        variationPending = true;
        variationStateSeen = false;
        variationTimer = 0f;
    }

    bool CanStartVariation(out bool combatMode)
    {
        combatMode = false;

        if (anim.IsInTransition(0))
            return false;

        if (anim.GetBool(movingParameter))
            return false;

        if (!anim.GetBool(groundedParameter))
            return false;

        if (anim.GetBool(deadParameter))
            return false;

        AnimatorStateInfo state = anim.GetCurrentAnimatorStateInfo(0);

        bool unarmed = IsState(state, unarmedIdleState);
        bool armed = IsState(state, greatSwordIdleState);

        if (!unarmed && !armed)
            return false;

        // ป้องกันสุ่มระหว่างชักดาบหรือเก็บดาบ
        if (weaponLayer >= 0)
        {
            if (anim.IsInTransition(weaponLayer))
                return false;

            AnimatorStateInfo weaponState =
                anim.GetCurrentAnimatorStateInfo(weaponLayer);

            if (IsState(weaponState, drawStateName) ||
                IsState(weaponState, sheathStateName))
                return false;
        }

        combatMode = armed;
        return true;
    }

    int GetNextVariant(bool combatMode)
    {
        int count = Mathf.Clamp(variationCount, 1, 4);

        int last = combatMode
            ? lastCombatVariant
            : lastUnarmedVariant;

        int selected = Random.Range(1, count + 1);

        // ไม่ให้สุ่มท่าเดิมติดกัน
        if (count > 1 && selected == last)
        {
            selected = selected == count ? 1 : selected + 1;
        }

        if (combatMode)
            lastCombatVariant = selected;
        else
            lastUnarmedVariant = selected;

        return selected;
    }

    void TrackVariation()
    {
        variationTimer += Time.deltaTime;

        AnimatorStateInfo current =
            anim.GetCurrentAnimatorStateInfo(0);

        bool transitioning = anim.IsInTransition(0);

        string expectedState = activeCombat
            ? greatSwordLookPrefix + activeVariant
            : unarmedLookPrefix + activeVariant;

        string idleState = activeCombat
            ? greatSwordIdleState
            : unarmedIdleState;

        if (IsState(current, expectedState))
            variationStateSeen = true;

        if (transitioning &&
            IsState(anim.GetNextAnimatorStateInfo(0), expectedState))
        {
            variationStateSeen = true;
        }

        if (variationStateSeen && !transitioning)
        {
            // กลับ Idle หลัก หรือถูกขัดจังหวะด้วยการเคลื่อนที่
            if (IsState(current, idleState) ||
                !IsState(current, expectedState))
            {
                FinishVariation();
            }
        }

        if (variationTimer >= variationTimeout)
            FinishVariation();
    }

    bool IsState(AnimatorStateInfo state, string stateName)
    {
        if (state.IsName(stateName))
            return true;

        int dot = stateName.LastIndexOf('.');

        if (dot >= 0)
            return state.IsName(stateName.Substring(dot + 1));

        return false;
    }

    void FinishVariation()
    {
        variationPending = false;
        variationStateSeen = false;
        timerStarted = false;
        idleTimer = 0f;
    }
}
