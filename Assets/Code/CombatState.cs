using UnityEngine;

public class CombatState : MonoBehaviour
{
    public float exitDelay = 1.5f;          // ออกนอกระยะ/หมดเวลาแล้ว นานเท่านี้ค่อยเก็บดาบ
    public float manualCombatTime = 5f;     // กดตีแล้วถือดาบค้างไว้กี่วินาที (นับจากฟันครั้งล่าสุด)
    public bool InCombat { get; private set; }

    Animator anim;
    int enemiesInRange;
    float outTimer;
    float manualUntil = -1f;
    bool skipDrawNext;

    void Awake() => anim = GetComponent<Animator>();

    public void EnemyEnter() => enemiesInRange++;
    public void EnemyExit() => enemiesInRange = Mathf.Max(0, enemiesInRange - 1);

    // เรียกตอนกดตีโดยยังไม่ได้ถือดาบ: เข้าสถานะต่อสู้ทันที และข้ามท่าชักดาบ
    public void ForceCombat()
    {
        manualUntil = Time.time + manualCombatTime;
        if (!InCombat)
        {
            skipDrawNext = true;
            SetCombat(true);
        }
    }

    // เรียกทุกครั้งที่ฟัน เพื่อต่อเวลาถือดาบ
    public void NotifyAttack()
    {
        if (InCombat) manualUntil = Time.time + manualCombatTime;
    }

    void Update()
    {
        bool wantCombat = enemiesInRange > 0 || Time.time < manualUntil;

        if (wantCombat)
        {
            outTimer = 0f;
            SetCombat(true);
        }
        else if (InCombat)
        {
            outTimer += Time.deltaTime;
            if (outTimer >= exitDelay) SetCombat(false);
        }
    }

    void SetCombat(bool value)
    {
        if (InCombat == value) return;
        InCombat = value;

        // SkipDraw = true: Weapon Action ข้ามท่าชักดาบ ไปถือดาบเลย
        anim.SetBool("SkipDraw", value && skipDrawNext);
        skipDrawNext = false;
        anim.SetBool("InCombat", value);
    }
}