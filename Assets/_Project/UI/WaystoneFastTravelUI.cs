using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// หน้าต่าง Fast Travel ของระบบหินเทเลพอร์ต (Where Winds Meet–style)
/// ผูกกับ Editable uGUI Canvas Prefab (UI_WaystoneFastTravel) เพื่อให้ปรับหน้าตาใน Unity Editor ได้ทันที
/// </summary>
[DisallowMultipleComponent]
public sealed class WaystoneFastTravelUI : MonoBehaviour
{
    public static WaystoneFastTravelUI Instance { get; private set; }

    [Header("Editable Unity Canvas References")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TMP_Text headerTitleText;
    [SerializeField] private TMP_Text subtitleText;
    [SerializeField] private Transform rowContainer;
    [SerializeField] private GameObject rowTemplate;
    [SerializeField] private Button closeButton;

    public bool IsOpen { get; private set; }

    private TeleportWaystone _sourceWaystone;
    private readonly List<GameObject> _spawnedRows = new List<GameObject>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (rowTemplate != null) rowTemplate.SetActive(false);
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    private void OnDestroy()
    {
        if (IsOpen) GameplayInput.PopBlocker("WaystoneFastTravelUI");
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (!IsOpen) return;
        var kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame)
            Close();
    }

    public static void OpenFromWaystone(TeleportWaystone source)
    {
        if (Instance == null)
        {
            Debug.LogWarning("[WaystoneFastTravelUI] UI_WaystoneFastTravel panel not found in scene Canvas.");
            return;
        }
        Instance.Open(source);
    }

    public void Open(TeleportWaystone source)
    {
        if (IsOpen)
        {
            _sourceWaystone = source;
            RebuildList();
            return;
        }
        if (GameplayInput.IsBlocked) return;

        _sourceWaystone = source;
        IsOpen = true;
        GameplayInput.PushBlocker("WaystoneFastTravelUI");
        if (panelRoot != null) panelRoot.SetActive(true);
        RebuildList();
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        _sourceWaystone = null;
        if (panelRoot != null) panelRoot.SetActive(false);
        GameplayInput.PopBlocker("WaystoneFastTravelUI");
    }

    private void RebuildList()
    {
        for (int i = 0; i < _spawnedRows.Count; i++)
        {
            if (_spawnedRows[i] != null) Destroy(_spawnedRows[i]);
        }
        _spawnedRows.Clear();

        if (headerTitleText != null)
            headerTitleText.text = _sourceWaystone != null
                ? $"FAST TRAVEL  •  {_sourceWaystone.DisplayName}"
                : "FAST TRAVEL WAYSTONES";

        int total = TeleportWaystone.All.Count;
        int unlocked = 0;
        for (int i = 0; i < total; i++)
            if (TeleportWaystone.All[i] != null && TeleportWaystone.All[i].IsUnlocked) unlocked++;

        if (subtitleText != null)
            subtitleText.text = $"Attuned Waystones: {unlocked} / {total}   •   Tip: You can also click unlocked shrines on [M] World Map";

        if (rowContainer == null || rowTemplate == null) return;

        var sorted = new List<TeleportWaystone>(TeleportWaystone.All);
        sorted.Sort((a, b) =>
        {
            if (a == null || b == null) return 0;
            int uCmp = b.IsUnlocked.CompareTo(a.IsUnlocked);
            if (uCmp != 0) return uCmp;
            return string.Compare(a.DisplayName, b.DisplayName, System.StringComparison.OrdinalIgnoreCase);
        });

        Vector3 playerPos = PlayerCombat.Local != null ? PlayerCombat.Local.transform.position : Vector3.zero;

        for (int i = 0; i < sorted.Count; i++)
        {
            TeleportWaystone ws = sorted[i];
            if (ws == null || !ws.isActiveAndEnabled) continue;

            GameObject row = Instantiate(rowTemplate, rowContainer);
            row.name = $"WaystoneRow_{ws.WaystoneId}";
            row.SetActive(true);
            _spawnedRows.Add(row);

            bool isUnlocked = ws.IsUnlocked;
            bool isHere = _sourceWaystone == ws;
            float dist = Vector3.Distance(new Vector3(playerPos.x, 0f, playerPos.z), new Vector3(ws.transform.position.x, 0f, ws.transform.position.z));

            var titleTmp = row.transform.Find("TitleText")?.GetComponent<TMP_Text>();
            var subTmp = row.transform.Find("SubtitleText")?.GetComponent<TMP_Text>();
            var btn = row.transform.Find("WarpButton")?.GetComponent<Button>();
            var btnTmp = btn != null ? btn.GetComponentInChildren<TMP_Text>() : null;

            if (titleTmp != null)
            {
                titleTmp.text = isUnlocked ? $"•  {ws.DisplayName}" : $"-  {ws.DisplayName}  (Locked)";
                titleTmp.color = isUnlocked ? new Color(0.32f, 0.98f, 0.80f) : new Color(0.60f, 0.65f, 0.72f);
            }
            if (subTmp != null)
            {
                subTmp.text = $"{ws.RegionSubtitle}  •  {dist:0}m away";
            }
            if (btn != null)
            {
                btn.interactable = isUnlocked && !isHere;
                if (btnTmp != null)
                    btnTmp.text = isHere ? "HERE" : (isUnlocked ? "WARP" : "LOCKED");

                TeleportWaystone captured = ws;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => TeleportWaystone.TryFastTravelTo(captured, out _));
            }
        }
    }
}
