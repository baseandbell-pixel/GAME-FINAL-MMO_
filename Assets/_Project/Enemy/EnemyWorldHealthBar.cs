using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// หลอดเลือดแบบ World-Space UI ลอยเหนือศีรษะมอนสเตอร์ทั่วไป (World-Space Enemy Health Bar):
///   - แสดงชื่อ + เลเวลของมอนสเตอร์ และแถบเลือดสีแดงเข้มพร้อมแถบ Trailing Damage ที่ลดตามอย่างนุ่มนวล
///   - หันเข้าหากล้องผู้เล่นตลอดเวลาแบบ Billboard Rotation
///   - เปิด/ซ่อนอัจฉริยะ (แสดงเมื่อมอนสเตอร์บาดเจ็บ, กำลังต่อสู้, ถูกล็อกเป้า หรืออยู่ในระยะสายตา 22 เมตร) เพื่อให้หน้าจอสะอาดตา
/// </summary>
[DisallowMultipleComponent]
public class EnemyWorldHealthBar : MonoBehaviour
{
    // ระยะสูงสุดที่จะแสดงหลอดเลือดของมอนสเตอร์ที่ยังไม่บาดเจ็บ
    private const float CombatVisibilityRange = 22f;

    private EnemyNPC _enemy;
    private GameObject _canvasGo;
    private Canvas _canvas;
    private Text _nameLabel;
    private Text _hpText;
    private Image _hpFill;
    private Image _hpTrailFill;
    private float _trailFrac = 1f;
    private float _verticalOffset = 1.65f;

    // คุณสมบัติตรวจสอบสถานะสำหรับ Play Mode Verification
    public bool IsBarBuilt => _canvasGo != null && _hpFill != null;
    public bool IsBarVisible => _canvasGo != null && _canvasGo.activeSelf;
    public string DisplayedTitle => _nameLabel != null ? _nameLabel.text : string.Empty;

    /// <summary>
    /// ติดตั้งหลอดเลือด World-Space ให้กับมอนสเตอร์ทั่วไป (ไม่ติดให้บอสเพราะบอสใช้ Top-Center BossHudUI)
    /// </summary>
    public static EnemyWorldHealthBar AttachTo(EnemyNPC enemy)
    {
        if (enemy == null || enemy.IsBoss) return null;
        var existing = enemy.GetComponent<EnemyWorldHealthBar>();
        if (existing != null) return existing;
        var bar = enemy.gameObject.AddComponent<EnemyWorldHealthBar>();
        bar.Initialize(enemy);
        return bar;
    }

    private void Initialize(EnemyNPC enemy)
    {
        _enemy = enemy;
        _verticalOffset = enemy.HasRiggedModel ? 2.55f : 1.62f;
        BuildWorldCanvas();
        RefreshImmediate();
    }

    /// <summary>
    /// สร้างโครงสร้าง World-Space uGUI Canvas เหนือศีรษะมอนสเตอร์
    /// </summary>
    private void BuildWorldCanvas()
    {
        if (_canvasGo != null) return;

        _canvasGo = new GameObject("EnemyWorldHealthBarCanvas");
        _canvasGo.transform.SetParent(transform, false);
        _canvasGo.transform.localPosition = new Vector3(0f, _verticalOffset, 0f);
        _canvasGo.transform.localScale = Vector3.one * 0.011f;

        _canvas = _canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        _canvas.sortingOrder = 12;

        var rt = (RectTransform)_canvasGo.transform;
        rt.sizeDelta = new Vector2(180f, 46f);

        Font font = UITheme.GetFont();

        // ป้ายชื่อ + เลเวลของมอนสเตอร์ด้านบนหลอดเลือด
        var nameGo = new GameObject("EnemyTitle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        nameGo.transform.SetParent(_canvasGo.transform, false);
        _nameLabel = nameGo.GetComponent<Text>();
        _nameLabel.font = font;
        _nameLabel.fontSize = 16;
        _nameLabel.fontStyle = FontStyle.Bold;
        _nameLabel.alignment = TextAnchor.LowerCenter;
        _nameLabel.color = new Color(0.96f, 0.96f, 0.98f, 1f);
        _nameLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
        _nameLabel.raycastTarget = false;
        var nameRt = _nameLabel.rectTransform;
        nameRt.anchorMin = new Vector2(0f, 0.48f);
        nameRt.anchorMax = new Vector2(1f, 1f);
        nameRt.offsetMin = Vector2.zero;
        nameRt.offsetMax = Vector2.zero;
        UITheme.AddShadow(nameGo, 1.4f);

        // กรอบหลอดเลือดสีเข้มพร้อมเส้นขอบบาง
        var borderGo = new GameObject("HpBorder", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        borderGo.transform.SetParent(_canvasGo.transform, false);
        var borderImg = borderGo.GetComponent<Image>();
        borderImg.color = new Color(0.05f, 0.05f, 0.07f, 0.90f);
        borderImg.raycastTarget = false;
        var borderRt = borderImg.rectTransform;
        borderRt.anchorMin = new Vector2(0.04f, 0.08f);
        borderRt.anchorMax = new Vector2(0.96f, 0.44f);
        borderRt.offsetMin = Vector2.zero;
        borderRt.offsetMax = Vector2.zero;

        // พื้นหลังรางหลอดเลือด
        var trackGo = new GameObject("HpTrack", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        trackGo.transform.SetParent(borderGo.transform, false);
        var trackImg = trackGo.GetComponent<Image>();
        trackImg.color = new Color(0.16f, 0.04f, 0.05f, 0.94f);
        trackImg.raycastTarget = false;
        StretchWithPadding(trackImg.rectTransform, 2f);

        // แถบเลือดลดตาม (Damage Trail สีส้มทอง)
        var trailGo = new GameObject("HpTrail", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        trailGo.transform.SetParent(trackGo.transform, false);
        _hpTrailFill = trailGo.GetComponent<Image>();
        _hpTrailFill.color = new Color(0.98f, 0.65f, 0.22f, 0.90f);
        _hpTrailFill.raycastTarget = false;
        StretchWithPadding(_hpTrailFill.rectTransform, 0f);

        // แถบเลือดหลักสีแดงสด
        var fillGo = new GameObject("HpFill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        fillGo.transform.SetParent(trackGo.transform, false);
        _hpFill = fillGo.GetComponent<Image>();
        _hpFill.color = new Color(0.88f, 0.16f, 0.16f, 1f);
        _hpFill.raycastTarget = false;
        StretchWithPadding(_hpFill.rectTransform, 0f);

        // ตัวเลขเลือดขนาดกะทัดรัดกลางหลอด
        var hpTextGo = new GameObject("HpText", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        hpTextGo.transform.SetParent(trackGo.transform, false);
        _hpText = hpTextGo.GetComponent<Text>();
        _hpText.font = font;
        _hpText.fontSize = 11;
        _hpText.fontStyle = FontStyle.Bold;
        _hpText.alignment = TextAnchor.MiddleCenter;
        _hpText.color = Color.white;
        _hpText.horizontalOverflow = HorizontalWrapMode.Overflow;
        _hpText.raycastTarget = false;
        StretchWithPadding(_hpText.rectTransform, 0f);
        UITheme.AddShadow(hpTextGo, 1.0f);
    }

    private void LateUpdate()
    {
        if (_enemy == null)
        {
            _enemy = GetComponent<EnemyNPC>();
            if (_enemy == null) return;
        }
        if (_canvasGo == null) BuildWorldCanvas();

        bool shouldShow = ComputeVisibility();
        if (_canvasGo.activeSelf != shouldShow)
            _canvasGo.SetActive(shouldShow);

        if (!shouldShow) return;

        // หมุนหน้าป้ายเข้าหากล้องผู้เล่นตลอดเวลา (Billboard Rotation)
        var cam = Camera.main;
        if (cam != null)
        {
            _canvasGo.transform.rotation = cam.transform.rotation;
        }

        RefreshImmediate();
    }

    /// <summary>
    /// คำนวณเงื่อนไขการแสดงหลอดเลือด: แสดงเมื่อยังมีชีวิต, ไม่ติดหน้าต่างเมนู และ (บาดเจ็บ / ตื่นตัว / ถูกล็อกเป้า / อยู่ในระยะ 22 เมตร)
    /// </summary>
    private bool ComputeVisibility()
    {
        if (_enemy == null || !_enemy.IsSpawned || !_enemy.IsAlive || _enemy.IsBoss) return false;
        if (GameplayInput.IsBlocked) return false;

        int maxHp = Mathf.Max(1, _enemy.ScaledMaxHealth);
        bool damaged = _enemy.CurrentHealth < maxHp;
        bool inCombat = _enemy.IsAlerted || _enemy.State == NpcState.Returning;
        var local = PlayerCombat.Local;
        var lockSys = local != null ? local.GetComponent<TargetLockSystem>() : null;
        bool isLocked = lockSys != null && ReferenceEquals(lockSys.CurrentTarget, _enemy);

        if (damaged || inCombat || isLocked) return true;

        if (local != null && local.IsSpawned)
        {
            Vector3 a = local.transform.position;
            Vector3 b = _enemy.transform.position;
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b) <= CombatVisibilityRange;
        }
        return false;
    }

    /// <summary>
    /// อัปเดตชื่อ เลเวล ตัวเลขเลือด และสัดส่วนแถบสีแดง + แถบ Trailing Damage
    /// </summary>
    public void RefreshImmediate()
    {
        if (_enemy == null || _hpFill == null) return;

        int curHp = Mathf.Max(0, _enemy.CurrentHealth);
        int maxHp = Mathf.Max(1, _enemy.ScaledMaxHealth);
        float frac = Mathf.Clamp01(curHp / (float)maxHp);

        if (_trailFrac > frac)
            _trailFrac = Mathf.MoveTowards(_trailFrac, frac, Time.deltaTime * 0.75f);
        else
            _trailFrac = frac;

        _hpFill.rectTransform.anchorMax = new Vector2(frac, 1f);
        if (_hpTrailFill != null) _hpTrailFill.rectTransform.anchorMax = new Vector2(_trailFrac, 1f);

        string roleTitle = _enemy.ScaledLevel <= 2 ? "Camp Guard" : "Frontier Marauder";
        if (_nameLabel != null)
        {
            _nameLabel.text = $"Lv.{_enemy.ScaledLevel}  {roleTitle}";
            _nameLabel.color = _enemy.State == NpcState.Staggered
                ? new Color(1f, 0.88f, 0.22f, 1f)
                : (_enemy.IsAlerted ? new Color(1f, 0.76f, 0.62f, 1f) : new Color(0.95f, 0.96f, 0.98f, 1f));
        }

        if (_hpText != null)
            _hpText.text = $"{curHp} / {maxHp}";
    }

    private static void StretchWithPadding(RectTransform rt, float pad)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(pad, pad);
        rt.offsetMax = new Vector2(-pad, -pad);
    }

    private void OnDestroy()
    {
        if (_canvasGo != null) Destroy(_canvasGo);
    }
}
