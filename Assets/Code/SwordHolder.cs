using UnityEngine;

[RequireComponent(typeof(Animator))]
public class SwordHolder : MonoBehaviour
{
    [Header("ดาบ 2 เล่ม")]
    public GameObject swordHand;   // Sword_Hand
    public GameObject swordBack;   // Sword_Back
    public bool showSwordOnBack = true; // ติ๊กออก = เก็บแล้วดาบหายทันที

    [Header("เวลา (วินาที)")]
    public float drawShowDelay = 0.05f;    // หน่วงก่อนดาบโผล่ในมือ หลังถึงจังหวะชัก
    public float backSwordDuration = 2.5f; // ดาบอยู่บนหลังกี่วินาที ก่อนหายไป

    [Header("ชื่อใน Animator (ต้องตรงเป๊ะ)")]
    public string weaponLayerName = "Weapon Action";
    public string unarmedStateName = "Unarmed (Empty)";
    public string armedStateName = "Armed (Empty)";

    public string drawStateName = "Draw A Great Sword";
    public string sheathStateName = "Sheath A Great Sword";
    [Range(0f, 1f)] public float drawShowAt = 0.35f;   // ดาบโผล่ตอนท่าชักเล่นไปกี่ %
    [Range(0f, 1f)] public float sheathHideAt = 0.6f;  // ดาบหายจากมือตอนท่าเก็บเล่นไปกี่ %

    Animator anim;
    int weaponLayer;
    bool handShown;
    float showHandAt = -1f;   // เวลาที่ดาบจะโผล่ในมือ (-1 = ไม่มีนัด)
    float hideBackAt = -1f;   // เวลาที่ดาบหลังจะหาย (-1 = ไม่มีนัด)

    void Awake()
    {
        anim = GetComponent<Animator>();
        weaponLayer = anim.GetLayerIndex(weaponLayerName);
        SetHand(false);
        SetBack(false);
    }

    void Update()
    {
        if (showHandAt >= 0f && Time.time >= showHandAt)
        {
            showHandAt = -1f;
            SetHand(true);
        }

        if (hideBackAt >= 0f && Time.time >= hideBackAt)
        {
            hideBackAt = -1f;
            SetBack(false);
        }

        if (weaponLayer < 0) return;

        bool inTransition = anim.IsInTransition(weaponLayer);
        var s = inTransition
            ? anim.GetNextAnimatorStateInfo(weaponLayer)
            : anim.GetCurrentAnimatorStateInfo(weaponLayer);

        if (s.IsName(drawStateName))
        {
            if (!handShown && showHandAt < 0f && s.normalizedTime >= drawShowAt) OnDrawSword();
        }
        else if (s.IsName(sheathStateName))
        {
            if (handShown && s.normalizedTime >= sheathHideAt) OnSheathSword();
        }
        else if (!inTransition)
        {
            // กันดาบค้างผิดที่
            if (s.IsName(armedStateName) && !handShown && showHandAt < 0f) OnDrawSword();
            else if (s.IsName(unarmedStateName) && handShown) OnSheathSword();
        }
    }

    // เรียกจาก Animation Event ในคลิปชักดาบ
    public void OnDrawSword()
    {
        hideBackAt = -1f;
        SetBack(false);

        if (drawShowDelay <= 0f) SetHand(true);
        else showHandAt = Time.time + drawShowDelay;
    }

    // เรียกจาก Animation Event ในคลิปเก็บดาบ
    public void OnSheathSword()
    {
        showHandAt = -1f;
        SetHand(false);

        if (showSwordOnBack)
        {
            SetBack(true);
            hideBackAt = Time.time + backSwordDuration;
        }
    }

    void SetHand(bool on)
    {
        handShown = on;
        if (swordHand != null && swordHand.activeSelf != on) swordHand.SetActive(on);
    }

    void SetBack(bool on)
    {
        if (swordBack != null && swordBack.activeSelf != on) swordBack.SetActive(on);
    }
}