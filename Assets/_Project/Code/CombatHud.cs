using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;

/// <summary>
/// ตัวเลขความเสียหายและข้อความแจ้งเตือนแบบ 3D ลอยบนโลกเกม (World-space Floating Damage &amp; Combat Warning Popup)
/// แยกออกมาจาก PlayerCombat.cs เพื่อลดการผูกมัดกับระบบต่อสู้หลักและให้ทีม UI ปรับแต่ง/เปลี่ยนแทนได้อิสระ
/// </summary>
public sealed class FloatingDamagePopup : MonoBehaviour
{
    // รายการ Popup ที่กำลังแสดงผลอยู่ทั้งหมดในฉาก
    public static readonly List<FloatingDamagePopup> ActivePopups = new List<FloatingDamagePopup>();

    // ค่าความเสียหายที่แสดงบนป้ายนี้
    public int DamageAmount { get; private set; }
    // เป็นการโจมตีติดคริติคอลหรือไม่
    public bool IsCritical { get; private set; }
    // ลำดับจังหวะคอมโบ (1..3)
    public int ComboStep { get; private set; }

    private TextMeshPro _tmp;
    private Vector3 _startPos;
    private float _spawnTime;
    private const float Lifetime = 1.05f;

    /// <summary>
    /// สร้างตัวเลขดาเมจลอยเหนือจุดปะทะ (ซ่อนอัตโนมัติหากอยู่ไกลเกินระยะมองเห็นของผู้เล่นเครื่องนี้)
    /// </summary>
    public static FloatingDamagePopup Spawn(Vector3 worldPos, int amount, bool isCrit, int comboStep = 1, bool isBoss = false, PlayerRef attacker = default)
    {
        if (!CombatHud.IsWithinCombatVisualRange(worldPos, isBoss, attacker))
            return null;

        var go = new GameObject(isCrit ? $"FloatingCritPopup_{amount}" : $"FloatingDamagePopup_{amount}");
        Vector2 jitter = UnityEngine.Random.insideUnitCircle * 0.25f;
        go.transform.position = worldPos + new Vector3(jitter.x, 0.35f, jitter.y);

        var popup = go.AddComponent<FloatingDamagePopup>();
        popup.Init(amount, isCrit, comboStep);
        return popup;
    }

    /// <summary>
    /// สร้างข้อความแจ้งเตือนพิเศษบนโลก 3D (เช่น PERFECT PARRY!, STAGGERED!, ท่าเตือนของบอส)
    /// ซ่อนอัตโนมัติหากอยู่ไกลเกินระยะมองเห็นของผู้เล่นเครื่องนี้
    /// </summary>
    public static FloatingDamagePopup SpawnWarning(Vector3 worldPos, string text, Color color, bool isBoss = false)
    {
        if (!CombatHud.IsWithinCombatVisualRange(worldPos, isBoss))
            return null;

        var go = new GameObject("FloatingWarningPopup");
        go.transform.position = worldPos;
        var popup = go.AddComponent<FloatingDamagePopup>();
        popup.InitText(text, color, fontSize: 4.5f, isCrit: true);
        return popup;
    }

    private void InitText(string text, Color color, float fontSize, bool isCrit)
    {
        DamageAmount = 0;
        IsCritical = isCrit;
        ComboStep = 1;
        _startPos = transform.position;
        _spawnTime = Time.unscaledTime;

        _tmp = gameObject.AddComponent<TextMeshPro>();
        _tmp.alignment = TextAlignmentOptions.Center;
        _tmp.textWrappingMode = TextWrappingModes.NoWrap;
        _tmp.fontStyle = FontStyles.Bold;
        _tmp.text = text;
        _tmp.fontSize = fontSize;
        _tmp.color = color;

        if (!ActivePopups.Contains(this)) ActivePopups.Add(this);
    }

    private void Init(int amount, bool isCrit, int comboStep)
    {
        DamageAmount = amount;
        IsCritical = isCrit;
        ComboStep = comboStep;
        _startPos = transform.position;
        _spawnTime = Time.unscaledTime;

        _tmp = gameObject.AddComponent<TextMeshPro>();
        _tmp.alignment = TextAlignmentOptions.Center;
        _tmp.textWrappingMode = TextWrappingModes.NoWrap;
        _tmp.fontStyle = FontStyles.Bold;

        if (isCrit)
        {
            _tmp.text = $"CRIT!\n-{amount}";
            _tmp.fontSize = comboStep >= 3 ? 5.2f : 4.4f;
            _tmp.color = comboStep >= 3
                ? new Color(1.0f, 0.38f, 0.08f, 1f)
                : new Color(1.0f, 0.78f, 0.12f, 1f);
        }
        else
        {
            _tmp.text = $"-{amount}";
            _tmp.fontSize = 3.2f;
            _tmp.color = new Color(1.0f, 0.95f, 0.78f, 1f);
        }

        if (!ActivePopups.Contains(this)) ActivePopups.Add(this);
    }

    private void OnDestroy()
    {
        ActivePopups.Remove(this);
    }

    private void Update()
    {
        float age = Time.unscaledTime - _spawnTime;
        if (age >= Lifetime)
        {
            Destroy(gameObject);
            return;
        }

        float t = Mathf.Clamp01(age / Lifetime);
        float rise = (IsCritical ? 1.35f : 0.95f) * t;
        transform.position = _startPos + Vector3.up * rise;

        float scale = IsCritical
            ? (t < 0.18f ? Mathf.Lerp(0.6f, 1.35f, t / 0.18f) : Mathf.Lerp(1.35f, 1.0f, (t - 0.18f) / 0.82f))
            : 1.0f;
        transform.localScale = Vector3.one * scale;

        var cam = Camera.main;
        if (cam != null)
            transform.rotation = cam.transform.rotation;

        if (_tmp != null && t > 0.6f)
        {
            Color c = _tmp.color;
            c.a = Mathf.Clamp01(1f - (t - 0.6f) / 0.4f);
            _tmp.color = c;
        }
    }
}

/// <summary>
/// หน้าจอ HUD การต่อสู้ฝั่งผู้เล่น (Crosshair, ตัวนับ Combo และ Popup ดาเมจ/Parry/Gold บนหน้าจอ)
/// แยกออกจาก PlayerCombat.cs อย่างสมบูรณ์และฟังสัญญาณผ่าน C# Events เพื่อความเป็นโมดูลาร์
/// </summary>
public sealed class CombatHud : MonoBehaviour
{
    private struct Popup
    {
        public Transform target;
        public string label;
        public Color color;
        public bool isCrit;
        public float startedAt;
        public float maxVisibleDistance;
    }

    public const float NormalDamageVisualRange = 30f;
    public const float BossDamageVisualRange = 65f;

    private const float PopupLifetime = 1.05f;
    private const float ComboTimeout = 2.5f;
    private static CombatHud _localHud;
    private readonly List<Popup> _popups = new List<Popup>(16);
    private PlayerCombat _combat;
    private TargetLockSystem _lock;
    private int _lastHealth = -1;
    private int _combo;
    private float _lastComboTime = -100f;

    /// <summary>
    /// ตรวจสอบว่าจุดเกิดดาเมจอยู่ในระยะมองเห็นของผู้เล่นเครื่องนี้หรือไม่ (ป้องกันเห็น UI ดาเมจเพื่อนที่อยู่ห่าง 500–800 เมตร)
    /// </summary>
    public static bool IsWithinCombatVisualRange(Vector3 worldPos, bool isBoss = false, PlayerRef attacker = default)
    {
        float maxDist = isBoss ? BossDamageVisualRange : NormalDamageVisualRange;
        var local = PlayerCombat.Local;
        if (local != null && local.IsSpawned)
        {
            if (attacker != PlayerRef.None && local.Runner != null && local.Runner.IsRunning && local.Runner.LocalPlayer == attacker)
                return true;

            Vector3 diff = worldPos - local.transform.position;
            diff.y = 0f;
            return diff.sqrMagnitude <= maxDist * maxDist;
        }

        var cam = Camera.main;
        if (cam != null)
        {
            Vector3 diff = worldPos - cam.transform.position;
            return diff.sqrMagnitude <= maxDist * maxDist;
        }
        return true;
    }

    /// <summary>
    /// ตรวจสอบและสร้าง CombatHud พร้อม PlayerStatusHud ให้กับผู้เล่นฝั่งเครื่องนี้
    /// </summary>
    public static void EnsureFor(PlayerCombat combat)
    {
        if (combat == null || !combat.IsLocallyControlled) return;
        if (combat.GetComponent<CombatHud>() == null) combat.gameObject.AddComponent<CombatHud>();
        PlayerStatusHud.EnsureFor(combat);
    }

    /// <summary>
    /// แจ้งเตือนเมื่อผู้เล่นได้รับ Gold เพื่อแสดงตัวเลขลอยและ Toast
    /// </summary>
    public static void NotifyGoldGained(int amount, string source)
    {
        if (_localHud == null || amount <= 0) return;
        var local = PlayerCombat.Local;
        if (local != null)
            _localHud.AddTextPopup(local.transform, $"+{amount} GOLD", new Color(1f, 0.86f, 0.18f), isCrit: true);
        PlayerStatusHud.Instance?.ShowToast($"+{amount} Gold ({source})", new Color(1f, 0.86f, 0.22f));
    }

    /// <summary>
    /// แจ้งเตือนเมื่อผู้เล่นทำ Perfect Parry สำเร็จ
    /// </summary>
    public static void NotifyParrySuccess(Transform playerTransform)
    {
        if (_localHud == null || playerTransform == null) return;
        _localHud.AddTextPopup(playerTransform, "PERFECT PARRY!", new Color(0.28f, 0.96f, 1.0f), isCrit: true);
    }

    /// <summary>
    /// แจ้งเตือนเมื่อศัตรูได้รับดาเมจเพื่อแสดง Popup และอัปเดตตัวนับ Combo (คัดกรองระยะไกลเพื่อไม่ให้เห็นดาเมจข้ามแมป)
    /// </summary>
    public static void NotifyEnemyDamaged(EnemyNPC enemy, int amount, PlayerRef attacker, bool isCrit = false, int comboStep = 1)
    {
        if (_localHud == null || enemy == null || amount <= 0) return;

        Transform anchor = enemy.LockPoint != null ? enemy.LockPoint : enemy.transform;
        if (!IsWithinCombatVisualRange(anchor.position, enemy.IsBoss, attacker))
            return;

        Color c = isCrit
            ? (comboStep >= 3 ? new Color(1f, 0.42f, 0.10f) : new Color(1f, 0.82f, 0.12f))
            : new Color(1f, 0.94f, 0.72f);

        float maxVis = enemy.IsBoss ? BossDamageVisualRange : NormalDamageVisualRange;
        _localHud.AddTextPopup(anchor, isCrit ? $"CRIT! -{amount}" : $"-{amount}", c, isCrit, maxVis);

        var local = PlayerCombat.Local;
        if (local != null && local.Runner != null && local.Runner.LocalPlayer == attacker)
        {
            _localHud._combo = Time.unscaledTime - _localHud._lastComboTime <= ComboTimeout ? _localHud._combo + 1 : 1;
            _localHud._lastComboTime = Time.unscaledTime;
            Debug.Log($"[CombatHud] Damage dealt: {amount}{(isCrit ? " (CRIT!)" : "")}; combo {_localHud._combo}.");
        }
    }

    private void Awake()
    {
        _combat = GetComponent<PlayerCombat>();
        _lock = GetComponent<TargetLockSystem>();
    }

    private void OnEnable()
    {
        if (_combat != null)
        {
            _combat.HealthChanged += OnHealthChanged;
            _combat.ParrySucceeded += OnParrySucceeded;
            _combat.GuardBroken += OnGuardBroken;
        }
    }

    private void Start()
    {
        _localHud = this;
        if (_combat != null) _lastHealth = _combat.NetworkHealth;
        Debug.Log("[CombatHud] Local tactical crosshair, HP & Stamina bars, and combat text enabled.");
    }

    private void OnDisable()
    {
        if (_combat != null)
        {
            _combat.HealthChanged -= OnHealthChanged;
            _combat.ParrySucceeded -= OnParrySucceeded;
            _combat.GuardBroken -= OnGuardBroken;
        }
        if (_localHud == this) _localHud = null;
    }

    private void Update()
    {
        if (_combo > 0 && Time.unscaledTime - _lastComboTime > ComboTimeout) _combo = 0;
        for (int i = _popups.Count - 1; i >= 0; i--)
            if (_popups[i].target == null || Time.unscaledTime - _popups[i].startedAt > PopupLifetime) _popups.RemoveAt(i);
    }

    private void OnHealthChanged(int current, int max)
    {
        if (_lastHealth >= 0 && current < _lastHealth)
        {
            int damage = _lastHealth - current;
            AddTextPopup(transform, $"-{damage}", new Color(1f, 0.25f, 0.25f), isCrit: false);
            Debug.Log($"[CombatHud] Local player received {damage} damage ({current}/{max}).");
        }
        _lastHealth = current;
    }

    private void OnParrySucceeded(EnemyNPC attacker)
    {
        AddTextPopup(transform, "PERFECT PARRY!", new Color(0.28f, 0.96f, 1.0f), isCrit: true);
    }

    private void OnGuardBroken()
    {
        AddTextPopup(transform, "GUARD BROKEN!", new Color(1f, 0.28f, 0.14f), isCrit: true);
    }

    private void AddTextPopup(Transform target, string label, Color color, bool isCrit, float maxVisibleDistance = NormalDamageVisualRange)
    {
        _popups.Add(new Popup
        {
            target = target,
            label = label,
            isCrit = isCrit,
            color = color,
            startedAt = Time.unscaledTime,
            maxVisibleDistance = maxVisibleDistance
        });
    }

    // ตรวจสอบว่าควรแสดงเป้าเล็งกลางจอหรือไม่ (ซ่อนอัตโนมัติเมื่อเปิดแผนที่โลกหรือหน้าต่าง UI)
    public static bool IsCrosshairVisible =>
        (MapManager.Instance == null || !MapManager.Instance.IsWorldMapOpen) && !GameplayInput.IsBlocked;

    private void OnGUI()
    {
        if (_localHud != this || Event.current.type != EventType.Repaint) return;
        DrawCrosshair();
        if (!GameplayInput.IsBlocked)
        {
            DrawPopups();
            DrawCombo();
        }
    }

    private void DrawCrosshair()
    {
        if (!IsCrosshairVisible) return;

        bool locked = _lock != null && _lock.IsLocked;
        Color color = locked ? new Color(1f, 0.72f, 0.12f) : new Color(0.9f, 0.95f, 1f);
        float radius = locked ? 15f : 10f;
        float thickness = locked ? 3f : 2f;
        float x = Screen.width * .5f, y = Screen.height * .5f;
        var old = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(new Rect(x - radius - 7f, y - thickness * .5f, 6f, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(x + radius + 1f, y - thickness * .5f, 6f, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(x - thickness * .5f, y - radius - 7f, thickness, 6f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(x - thickness * .5f, y + radius + 1f, thickness, 6f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(x - 1.5f, y - 1.5f, 3f, 3f), Texture2D.whiteTexture);
        GUI.color = old;
    }

    private void DrawPopups()
    {
        var cam = Camera.main;
        if (cam == null) return;
        var old = GUI.color;
        for (int i = 0; i < _popups.Count; i++)
        {
            Popup popup = _popups[i];
            if (popup.target == null) continue;
            float t = Mathf.Clamp01((Time.unscaledTime - popup.startedAt) / PopupLifetime);
            Vector3 screen = cam.WorldToScreenPoint(popup.target.position + Vector3.up * (1.5f + t * .9f));
            float maxVis = popup.maxVisibleDistance > 0f ? popup.maxVisibleDistance : NormalDamageVisualRange;
            if (screen.z <= 0f || screen.z > maxVis) continue;

            GUI.color = new Color(popup.color.r, popup.color.g, popup.color.b, 1f - t);
            GUIStyle style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = popup.isCrit ? 26 : 21,
                fontStyle = FontStyle.Bold
            };
            GUI.Label(new Rect(screen.x - 105f, Screen.height - screen.y - 20f, 210f, 36f), popup.label, style);
        }
        GUI.color = old;
    }

    private void DrawCombo()
    {
        if (_combo <= 1) return;
        float age = Time.unscaledTime - _lastComboTime;
        GUI.color = new Color(1f, 0.85f, .2f, Mathf.Clamp01(1f - age / ComboTimeout));
        GUIStyle style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 23, fontStyle = FontStyle.Bold };
        GUI.Label(new Rect(Screen.width * .5f - 110f, Screen.height * .5f + 45f, 220f, 35f), $"COMBO x{_combo}", style);
        GUI.color = Color.white;
    }
}
