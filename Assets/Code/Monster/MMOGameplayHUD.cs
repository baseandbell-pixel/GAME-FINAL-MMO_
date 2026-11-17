using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Runtime uGUI Canvas HUD, Minimap/Radar, Party Frame, Chat Box, and Settings Pause Menu
/// for Map1 combat scenes (replaces legacy OnGUI rendering).
/// </summary>
[DisallowMultipleComponent]
public sealed class MMOGameplayHUD : MonoBehaviour
{
    private const int MaxPartySlots = 4;
    private const int MaxMinimapBlips = 20;
    private const float MinimapWorldRange = 42f;
    private const float MinimapPixelRadius = 72f;

    private MMOPlayerCombat _combat;
    private MMOPlayerController _playerController;

    private Canvas _canvas;
    private Sprite _rectSprite;
    private Sprite _circleSprite;
    private Texture2D _rectTex;
    private Texture2D _circleTex;

    // Target Monster Frame
    private GameObject _targetRoot;
    private Image _targetHpFill;
    private TMP_Text _targetTitleText;
    private TMP_Text _targetStateText;
    private TMP_Text _targetHpText;

    // Party Frame
    private GameObject _partyRoot;
    private TMP_Text _partyHeaderText;
    private readonly GameObject[] _partyCards = new GameObject[MaxPartySlots];
    private readonly TMP_Text[] _partyNames = new TMP_Text[MaxPartySlots];
    private readonly Image[] _partyHpFills = new Image[MaxPartySlots];

    // Minimap
    private RectTransform _minimapPlot;
    private RectTransform _minimapPlayerIcon;
    private TMP_Text _minimapCoordsText;
    private readonly List<Image> _minimapBlips = new List<Image>();

    // Player Action & Vital Bars
    private TMP_Text _bannerText;
    private TMP_Text _defeatedText;
    private Image _playerHpFill;
    private Image _playerExpFill;
    private TMP_Text _playerStatsText;
    private TMP_Text _playerControlsText;

    private struct SkillSlotUI
    {
        public Image border;
        public Image fill;
        public TMP_Text keyLabel;
        public TMP_Text statusLabel;
    }

    private readonly SkillSlotUI[] _skillSlots = new SkillSlotUI[3];
    private readonly SkillSlotUI[] _hotbarSlots = new SkillSlotUI[MMOPlayerInventory.HotbarSlotCount];
    private GameObject _lootPromptBar;
    private TMP_Text _lootPromptText;

    // Chat Box
    private GameObject _chatLogBg;
    private TMP_Text _chatLogText;
    private GameObject _chatInputRow;
    private TMP_InputField _chatInputField;
    private TMP_Text _chatHintText;

    // Pause / Settings Menu
    private GameObject _pauseOverlay;
    private TMP_Text _sensValueText;
    private Slider _sensSlider;
    private TMP_Text _sfxValueText;
    private Slider _sfxSlider;
    private TMP_Text _bgmValueText;
    private Slider _bgmSlider;
    private TMP_Text _camModeBtnText;
    private TMP_Text _fullscreenBtnText;

    // Quest Tracker & Economy/Inventory Panel
    private TMP_Text _questTitleText;
    private TMP_Text _questDetailText;
    private TMP_Text _goldHudText;
    private TMP_Text _buffHudText;
    private MMOInventoryPanelUI _inventoryUI;

    private bool _suppressUiEvents;

    public static MMOGameplayHUD CreateForPlayer(MMOPlayerCombat combat, MMOPlayerController controller)
    {
        GameObject root = new GameObject("MMO_GameplayHUD_Canvas");
        MMOGameplayHUD hud = root.AddComponent<MMOGameplayHUD>();
        hud.Initialize(combat, controller);
        return hud;
    }

    private static TMP_FontAsset _thaiFallbackFont;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void EnsureUnicodeFallbackFont()
    {
        if (_thaiFallbackFont != null)
        {
            return;
        }

        Font osFont = Font.CreateDynamicFontFromOSFont(
            new[] { "Tahoma", "Leelawadee UI", "Segoe UI", "TH Sarabun New", "Arial Unicode MS" },
            28);
        if (osFont == null)
        {
            return;
        }

        _thaiFallbackFont = TMP_FontAsset.CreateFontAsset(osFont);
        if (_thaiFallbackFont == null)
        {
            return;
        }

        _thaiFallbackFont.name = "Runtime_Thai_Unicode_Fallback";
        DontDestroyOnLoad(_thaiFallbackFont);

        if (TMP_Settings.fallbackFontAssets != null)
        {
            TMP_Settings.fallbackFontAssets.RemoveAll(f => f == null);
            if (!TMP_Settings.fallbackFontAssets.Contains(_thaiFallbackFont))
            {
                TMP_Settings.fallbackFontAssets.Add(_thaiFallbackFont);
            }
        }

        if (TMP_Settings.defaultFontAsset != null)
        {
            if (TMP_Settings.defaultFontAsset.fallbackFontAssetTable == null)
            {
                TMP_Settings.defaultFontAsset.fallbackFontAssetTable = new List<TMP_FontAsset>();
            }
            TMP_Settings.defaultFontAsset.fallbackFontAssetTable.RemoveAll(f => f == null);
            if (!TMP_Settings.defaultFontAsset.fallbackFontAssetTable.Contains(_thaiFallbackFont))
            {
                TMP_Settings.defaultFontAsset.fallbackFontAssetTable.Add(_thaiFallbackFont);
            }
        }

        Application.quitting += CleanupRuntimeFallbackFont;
    }

    private static void CleanupRuntimeFallbackFont()
    {
        Application.quitting -= CleanupRuntimeFallbackFont;
        if (_thaiFallbackFont == null) return;

        TMP_Settings.fallbackFontAssets?.Remove(_thaiFallbackFont);
        TMP_Settings.defaultFontAsset?.fallbackFontAssetTable?.Remove(_thaiFallbackFont);
        _thaiFallbackFont = null;
    }

    private void Initialize(MMOPlayerCombat combat, MMOPlayerController controller)
    {
        _combat = combat;
        _playerController = controller;
        EnsureUnicodeFallbackFont();
        EnsureEventSystem();
        CreateRuntimeSprites();
        BuildCanvasHierarchy();
    }

    private void OnDestroy()
    {
        if (_rectSprite != null) Destroy(_rectSprite);
        if (_circleSprite != null) Destroy(_circleSprite);
        if (_rectTex != null) Destroy(_rectTex);
        if (_circleTex != null) Destroy(_circleTex);
    }

    private void EnsureEventSystem()
    {
        if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null)
        {
            return;
        }

        GameObject esGo = new GameObject("MMO_HUD_EventSystem");
        esGo.transform.SetParent(transform, false);
        esGo.AddComponent<EventSystem>();
        esGo.AddComponent<InputSystemUIInputModule>();
    }

    private void CreateRuntimeSprites()
    {
        _rectTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        Color[] whitePixels = { Color.white, Color.white, Color.white, Color.white };
        _rectTex.SetPixels(whitePixels);
        _rectTex.Apply();
        _rectSprite = Sprite.Create(_rectTex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f));

        const int size = 64;
        _circleTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float half = (size - 1) * 0.5f;
        float radiusSq = half * half;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - half;
                float dy = y - half;
                float distSq = dx * dx + dy * dy;
                float alpha = distSq <= radiusSq ? Mathf.Clamp01((half - Mathf.Sqrt(distSq)) * 1.4f + 0.35f) : 0f;
                _circleTex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        _circleTex.Apply();
        _circleSprite = Sprite.Create(_circleTex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    private void BuildCanvasHierarchy()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 50;

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        gameObject.AddComponent<GraphicRaycaster>();

        BuildTargetMonsterFrame();
        BuildPartyFrame();
        BuildMinimapPanel();
        BuildQuestAndEconomyPanel();
        BuildPlayerActionBar();
        BuildChatPanel();
        BuildInventoryPanel();
        BuildPauseMenu();
    }

    // =========================================================
    // HUD SECTIONS
    // =========================================================

    private void BuildTargetMonsterFrame()
    {
        _targetRoot = CreateBox("TargetFrame", transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -22f), new Vector2(540f, 84f), new Color(0.06f, 0.07f, 0.11f, 0.88f));
        AddOutline(_targetRoot, new Color(0.85f, 0.25f, 0.2f, 0.65f));

        _targetTitleText = CreateText("TargetTitle", _targetRoot.transform,
            new Vector2(0f, 1f), new Vector2(0.65f, 1f), new Vector2(14f, -8f), new Vector2(0f, 28f),
            19, FontStyles.Bold, TextAlignmentOptions.Left, Color.white);
        _targetTitleText.rectTransform.pivot = new Vector2(0f, 1f);

        _targetStateText = CreateText("TargetState", _targetRoot.transform,
            new Vector2(0.62f, 1f), new Vector2(1f, 1f), new Vector2(-14f, -8f), new Vector2(0f, 28f),
            15, FontStyles.Bold, TextAlignmentOptions.Right, new Color(1f, 0.68f, 0.3f));
        _targetStateText.rectTransform.pivot = new Vector2(1f, 1f);

        Image hpBg = CreateBar("TargetHpBar", _targetRoot.transform,
            new Vector2(14f, 14f), new Vector2(512f, 28f), new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Color(0.22f, 0.06f, 0.07f, 1f), new Color(0.88f, 0.18f, 0.18f, 1f), out _targetHpFill);

        _targetHpText = CreateText("TargetHpText", hpBg.transform,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
            15, FontStyles.Bold, TextAlignmentOptions.Center, Color.white);

        _targetRoot.SetActive(false);
    }

    private void BuildPartyFrame()
    {
        _partyRoot = new GameObject("PartyFrame");
        RectTransform rt = _partyRoot.AddComponent<RectTransform>();
        rt.SetParent(transform, false);
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(24f, -24f);
        rt.sizeDelta = new Vector2(280f, 260f);

        _partyHeaderText = CreateText("PartyHeader", rt,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(0f, 26f),
            16, FontStyles.Bold, TextAlignmentOptions.Left, new Color(0.68f, 0.88f, 1f));
        _partyHeaderText.rectTransform.pivot = new Vector2(0f, 1f);

        for (int i = 0; i < MaxPartySlots; i++)
        {
            GameObject card = CreateBox($"PartySlot_{i}", rt, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, -30f - i * 54f), new Vector2(276f, 48f), new Color(0.07f, 0.09f, 0.14f, 0.84f));
            AddOutline(card, new Color(0.32f, 0.56f, 0.85f, 0.45f));

            _partyNames[i] = CreateText("Name", card.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(10f, -4f), new Vector2(-20f, 20f),
                14, FontStyles.Bold, TextAlignmentOptions.Left, Color.white);
            _partyNames[i].rectTransform.pivot = new Vector2(0f, 1f);

            CreateBar("HpBar", card.transform,
                new Vector2(10f, 8f), new Vector2(256f, 12f), Vector2.zero, Vector2.zero,
                new Color(0.18f, 0.08f, 0.08f, 1f), new Color(0.22f, 0.84f, 0.40f, 1f), out _partyHpFills[i]);

            _partyCards[i] = card;
        }

        _partyRoot.SetActive(false);
    }

    private void BuildMinimapPanel()
    {
        GameObject panel = CreateBox("MinimapPanel", transform, new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-24f, -24f), new Vector2(204f, 232f), new Color(0.06f, 0.07f, 0.11f, 0.88f));
        AddOutline(panel, new Color(0.42f, 0.65f, 0.95f, 0.55f));

        TMP_Text title = CreateText("MinimapHeader", panel.transform,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -4f), new Vector2(0f, 22f),
            13, FontStyles.Bold, TextAlignmentOptions.Center, new Color(0.75f, 0.90f, 1f));
        title.rectTransform.pivot = new Vector2(0.5f, 1f);
        title.text = "RADAR  [N]";

        GameObject radarDisk = new GameObject("RadarDisk");
        _minimapPlot = radarDisk.AddComponent<RectTransform>();
        _minimapPlot.SetParent(panel.transform, false);
        _minimapPlot.anchorMin = new Vector2(0.5f, 0.5f);
        _minimapPlot.anchorMax = new Vector2(0.5f, 0.5f);
        _minimapPlot.pivot = new Vector2(0.5f, 0.5f);
        _minimapPlot.anchoredPosition = new Vector2(0f, 2f);
        _minimapPlot.sizeDelta = new Vector2(MinimapPixelRadius * 2f, MinimapPixelRadius * 2f);

        Image diskImg = radarDisk.AddComponent<Image>();
        diskImg.sprite = _circleSprite;
        diskImg.color = new Color(0.10f, 0.14f, 0.20f, 0.95f);

        // Crosshairs
        CreateBox("AxisH", _minimapPlot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(MinimapPixelRadius * 1.9f, 1f), new Color(0.4f, 0.6f, 0.8f, 0.22f));
        CreateBox("AxisV", _minimapPlot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(1f, MinimapPixelRadius * 1.9f), new Color(0.4f, 0.6f, 0.8f, 0.22f));

        for (int i = 0; i < MaxMinimapBlips; i++)
        {
            Image blip = CreateBlipDot($"Blip_{i}", _minimapPlot, 8f, Color.yellow);
            blip.gameObject.SetActive(false);
            _minimapBlips.Add(blip);
        }

        Image playerDot = CreateBlipDot("LocalPlayerDot", _minimapPlot, 11f, new Color(0.25f, 0.95f, 1f, 1f));
        _minimapPlayerIcon = playerDot.rectTransform;

        GameObject dirTip = CreateBox("FacingTip", _minimapPlayerIcon,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 8f), new Vector2(4f, 9f), new Color(0.75f, 1f, 1f, 0.95f));
        dirTip.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 0.5f);

        _minimapCoordsText = CreateText("Coords", panel.transform,
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 6f), new Vector2(0f, 22f),
            12, FontStyles.Normal, TextAlignmentOptions.Center, new Color(0.75f, 0.82f, 0.92f));
        _minimapCoordsText.rectTransform.pivot = new Vector2(0.5f, 0f);
    }

    private void BuildPlayerActionBar()
    {
        _defeatedText = CreateText("DefeatedBanner", transform,
            new Vector2(0.5f, 0.62f), new Vector2(0.5f, 0.62f), Vector2.zero, new Vector2(700f, 56f),
            34, FontStyles.Bold, TextAlignmentOptions.Center, new Color(1f, 0.30f, 0.30f));
        _defeatedText.gameObject.SetActive(false);

        _lootPromptBar = CreateBox("LootPromptBar", transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 260f), new Vector2(460f, 36f), new Color(0.06f, 0.09f, 0.14f, 0.92f));
        AddOutline(_lootPromptBar, new Color(1f, 0.82f, 0.25f, 0.85f));
        _lootPromptText = CreateText("LootPromptText", _lootPromptBar.transform,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
            15, FontStyles.Bold, TextAlignmentOptions.Center, Color.white);
        _lootPromptBar.SetActive(false);

        _bannerText = CreateText("CombatBanner", transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 198f), new Vector2(780f, 36f),
            24, FontStyles.Bold, TextAlignmentOptions.Center, new Color(1f, 0.88f, 0.2f));
        _bannerText.gameObject.SetActive(false);

        GameObject root = CreateBox("PlayerActionBar", transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 20f), new Vector2(780f, 160f), new Color(0.06f, 0.07f, 0.11f, 0.90f));
        AddOutline(root, new Color(0.32f, 0.58f, 0.95f, 0.55f));

        // 3 Combat Skill slots on left
        string[] keys = { "L-Click", "R-Click/Ctrl", "E" };
        string[] names = { "Combo", "Dodge", "Stun" };
        float combatSlotWidth = 114f;
        float xCursor = 14f;
        for (int i = 0; i < 3; i++)
        {
            GameObject slotGo = CreateBox($"SkillSlot_{i}", root.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(xCursor, -12f), new Vector2(combatSlotWidth, 52f),
                new Color(0.11f, 0.14f, 0.21f, 1f));
            AddOutline(slotGo, new Color(0.28f, 0.72f, 0.95f, 0.7f));
            Image bgImg = slotGo.GetComponent<Image>();

            TMP_Text keyLabel = CreateText("Key", slotGo.transform,
                new Vector2(0f, 0.5f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero,
                12, FontStyles.Bold, TextAlignmentOptions.Center, new Color(0.85f, 0.93f, 1f));
            keyLabel.text = $"[{keys[i]}] {names[i]}";

            TMP_Text statusLabel = CreateText("Status", slotGo.transform,
                new Vector2(0f, 0f), new Vector2(1f, 0.5f), Vector2.zero, Vector2.zero,
                12, FontStyles.Bold, TextAlignmentOptions.Center, new Color(0.35f, 1f, 0.55f));
            statusLabel.text = "READY";

            _skillSlots[i] = new SkillSlotUI { border = bgImg, fill = bgImg, keyLabel = keyLabel, statusLabel = statusLabel };
            xCursor += combatSlotWidth + 8f;
        }

        xCursor += 6f;

        // 5 Quick Hotbar Slots [1]..[5]
        float hbWidth = 72f;
        for (int h = 0; h < MMOPlayerInventory.HotbarSlotCount; h++)
        {
            int idx = h;
            GameObject hbGo = CreateBox($"HotbarSlot_{h}", root.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(xCursor, -12f), new Vector2(hbWidth, 52f),
                new Color(0.12f, 0.15f, 0.22f, 1f));
            AddOutline(hbGo, new Color(0.45f, 0.56f, 0.72f, 0.75f));
            Image hbImg = hbGo.GetComponent<Image>();
            Button hbBtn = hbGo.AddComponent<Button>();
            hbBtn.onClick.AddListener(() => _combat?.Inventory?.UseHotbarSlot(idx));

            TMP_Text keyLabel = CreateText("Key", hbGo.transform,
                new Vector2(0f, 0.52f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero,
                12, FontStyles.Bold, TextAlignmentOptions.Center, Color.white);
            TMP_Text statusLabel = CreateText("Status", hbGo.transform,
                new Vector2(0f, 0f), new Vector2(1f, 0.52f), Vector2.zero, Vector2.zero,
                11, FontStyles.Bold, TextAlignmentOptions.Center, new Color(0.85f, 0.92f, 1f));

            _hotbarSlots[h] = new SkillSlotUI { border = hbImg, fill = hbImg, keyLabel = keyLabel, statusLabel = statusLabel };
            xCursor += hbWidth + 6f;
        }

        // Player HP Bar
        Image hpBg = CreateBar("PlayerHpBar", root.transform,
            new Vector2(14f, 54f), new Vector2(752f, 28f), Vector2.zero, Vector2.zero,
            new Color(0.08f, 0.20f, 0.10f, 1f), new Color(0.18f, 0.80f, 0.34f, 1f), out _playerHpFill);

        _playerStatsText = CreateText("PlayerHpLabel", hpBg.transform,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
            15, FontStyles.Bold, TextAlignmentOptions.Center, Color.white);

        // Player EXP Bar
        CreateBar("PlayerExpBar", root.transform,
            new Vector2(14f, 34f), new Vector2(752f, 12f), Vector2.zero, Vector2.zero,
            new Color(0.12f, 0.13f, 0.24f, 1f), new Color(0.40f, 0.60f, 1f, 1f), out _playerExpFill);

        _playerControlsText = CreateText("ControlsHint", root.transform,
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 6f), new Vector2(0f, 22f),
            12, FontStyles.Normal, TextAlignmentOptions.Center, new Color(0.78f, 0.85f, 0.94f));
        _playerControlsText.rectTransform.pivot = new Vector2(0.5f, 0f);
        _playerControlsText.text = "[WASD+Shift] Move | [1-5 / R] Hotbar | [F] Loot | [I] Bag/Forge | [Caps] Lock | [V] Cam | [ESC] Menu";
    }

    private void BuildChatPanel()
    {
        GameObject root = new GameObject("ChatPanel");
        RectTransform rt = root.AddComponent<RectTransform>();
        rt.SetParent(transform, false);
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(20f, 20f);
        rt.sizeDelta = new Vector2(410f, 240f);

        _chatLogBg = CreateBox("ChatLogBg", rt, new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(0f, 44f), new Vector2(400f, 165f), new Color(0.05f, 0.06f, 0.10f, 0.68f));

        _chatLogText = CreateText("ChatLogText", _chatLogBg.transform,
            Vector2.zero, Vector2.one, new Vector2(10f, 6f), new Vector2(-20f, -12f),
            14, FontStyles.Normal, TextAlignmentOptions.BottomLeft, new Color(0.94f, 0.96f, 1f));
        _chatLogText.textWrappingMode = TextWrappingModes.Normal;

        _chatInputRow = new GameObject("ChatInputRow");
        RectTransform rowRt = _chatInputRow.AddComponent<RectTransform>();
        rowRt.SetParent(rt, false);
        rowRt.anchorMin = new Vector2(0f, 0f);
        rowRt.anchorMax = new Vector2(0f, 0f);
        rowRt.pivot = new Vector2(0f, 0f);
        rowRt.anchoredPosition = Vector2.zero;
        rowRt.sizeDelta = new Vector2(400f, 36f);

        GameObject inputBg = CreateBox("InputField", rowRt, Vector2.zero, Vector2.zero,
            Vector2.zero, new Vector2(316f, 36f), new Color(0.08f, 0.10f, 0.15f, 0.95f));
        AddOutline(inputBg, new Color(0.35f, 0.65f, 1f, 0.6f));

        GameObject textArea = new GameObject("TextArea");
        RectTransform areaRt = textArea.AddComponent<RectTransform>();
        areaRt.SetParent(inputBg.transform, false);
        areaRt.anchorMin = Vector2.zero;
        areaRt.anchorMax = Vector2.one;
        areaRt.offsetMin = new Vector2(10f, 4f);
        areaRt.offsetMax = new Vector2(-10f, -4f);
        textArea.AddComponent<RectMask2D>();

        TMP_Text inputText = CreateText("Text", areaRt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
            15, FontStyles.Normal, TextAlignmentOptions.Left, Color.white);
        TMP_Text placeholder = CreateText("Placeholder", areaRt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
            15, FontStyles.Italic, TextAlignmentOptions.Left, new Color(0.6f, 0.68f, 0.78f, 0.7f));
        placeholder.text = "Type message & press Enter...";

        _chatInputField = inputBg.AddComponent<TMP_InputField>();
        _chatInputField.textViewport = areaRt;
        _chatInputField.textComponent = inputText;
        _chatInputField.placeholder = placeholder;
        _chatInputField.characterLimit = 120;
        _chatInputField.onSubmit.AddListener(_ => SubmitChatFromUI());

        Button sendBtn = CreateButton("SendButton", rowRt,
            new Vector2(324f, 0f), new Vector2(76f, 36f),
            new Color(0.20f, 0.48f, 0.88f, 1f), "Send", 15);
        sendBtn.onClick.AddListener(SubmitChatFromUI);

        _chatHintText = CreateText("ChatHint", rt, new Vector2(0f, 0f), new Vector2(0f, 0f),
            Vector2.zero, new Vector2(220f, 26f),
            14, FontStyles.Normal, TextAlignmentOptions.Left, new Color(0.80f, 0.86f, 0.94f, 0.75f));
        _chatHintText.text = "[Enter] Open Chat";

        _chatInputRow.SetActive(false);
        _chatLogBg.SetActive(false);
    }

    private void BuildQuestAndEconomyPanel()
    {
        GameObject questPanel = CreateBox("QuestTrackerPanel", transform, new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-24f, -268f), new Vector2(246f, 118f), new Color(0.06f, 0.07f, 0.11f, 0.88f));
        AddOutline(questPanel, new Color(0.95f, 0.78f, 0.25f, 0.65f));

        _questTitleText = CreateText("QuestTitle", questPanel.transform,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(10f, -6f), new Vector2(-20f, 22f),
            14, FontStyles.Bold, TextAlignmentOptions.Left, new Color(1f, 0.86f, 0.28f));
        _questTitleText.rectTransform.pivot = new Vector2(0f, 1f);

        _questDetailText = CreateText("QuestDetail", questPanel.transform,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(10f, -30f), new Vector2(-20f, 44f),
            12, FontStyles.Normal, TextAlignmentOptions.TopLeft, new Color(0.88f, 0.92f, 0.98f));
        _questDetailText.rectTransform.pivot = new Vector2(0f, 1f);
        _questDetailText.textWrappingMode = TextWrappingModes.Normal;

        _goldHudText = CreateText("GoldHud", questPanel.transform,
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(10f, 6f), new Vector2(-20f, 22f),
            13, FontStyles.Bold, TextAlignmentOptions.Left, new Color(1f, 0.88f, 0.32f));
        _goldHudText.rectTransform.pivot = new Vector2(0f, 0f);

        _buffHudText = CreateText("PowerBuffHud", transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 222f), new Vector2(420f, 26f),
            15, FontStyles.Bold, TextAlignmentOptions.Center, new Color(0.32f, 0.96f, 1f));
        _buffHudText.gameObject.SetActive(false);
    }

    private void BuildInventoryPanel()
    {
        _inventoryUI = gameObject.AddComponent<MMOInventoryPanelUI>();
        _inventoryUI.Build(transform, _combat, _combat != null ? _combat.Inventory : null, _rectSprite);
    }

    private void BuildPauseMenu()
    {
        _pauseOverlay = CreateBox("PauseOverlay", transform,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.60f));

        RectTransform overlayRt = _pauseOverlay.GetComponent<RectTransform>();
        overlayRt.anchorMin = Vector2.zero;
        overlayRt.anchorMax = Vector2.one;
        overlayRt.pivot = new Vector2(0.5f, 0.5f);
        overlayRt.anchoredPosition = Vector2.zero;
        overlayRt.sizeDelta = Vector2.zero;

        GameObject panel = CreateBox("PausePanel", _pauseOverlay.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(440f, 490f),
            new Color(0.08f, 0.10f, 0.15f, 0.97f));
        AddOutline(panel, new Color(0.30f, 0.62f, 0.98f, 0.85f));

        TMP_Text header = CreateText("PauseHeader", panel.transform,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -18f), new Vector2(0f, 38f),
            24, FontStyles.Bold, TextAlignmentOptions.Center, Color.white);
        header.rectTransform.pivot = new Vector2(0.5f, 1f);
        header.text = "GAME MENU & SETTINGS";

        float cursorY = -74f;
        _sensValueText = CreateText("SensLabel", panel.transform,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(28f, cursorY), new Vector2(-56f, 24f),
            15, FontStyles.Bold, TextAlignmentOptions.Left, new Color(0.88f, 0.92f, 0.98f));
        _sensValueText.rectTransform.pivot = new Vector2(0f, 1f);
        _sensSlider = CreateSlider("SensSlider", panel.transform, new Vector2(28f, cursorY - 30f), new Vector2(384f, 22f), 0.5f, 6.0f);
        _sensSlider.onValueChanged.AddListener(val =>
        {
            if (_suppressUiEvents) return;
            if (MMOCameraController.Instance != null)
            {
                MMOCameraController.Instance.MouseSensitivity = val;
            }
        });

        cursorY -= 72f;
        _sfxValueText = CreateText("SfxLabel", panel.transform,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(28f, cursorY), new Vector2(-56f, 24f),
            15, FontStyles.Bold, TextAlignmentOptions.Left, new Color(0.88f, 0.92f, 0.98f));
        _sfxValueText.rectTransform.pivot = new Vector2(0f, 1f);
        _sfxSlider = CreateSlider("SfxSlider", panel.transform, new Vector2(28f, cursorY - 30f), new Vector2(384f, 22f), 0f, 1f);
        _sfxSlider.onValueChanged.AddListener(val =>
        {
            if (_suppressUiEvents) return;
            MMOAudioManager.MasterSfxVolume = val;
        });

        cursorY -= 72f;
        _bgmValueText = CreateText("BgmLabel", panel.transform,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(28f, cursorY), new Vector2(-56f, 24f),
            15, FontStyles.Bold, TextAlignmentOptions.Left, new Color(0.88f, 0.92f, 0.98f));
        _bgmValueText.rectTransform.pivot = new Vector2(0f, 1f);
        _bgmSlider = CreateSlider("BgmSlider", panel.transform, new Vector2(28f, cursorY - 30f), new Vector2(384f, 22f), 0f, 1f);
        _bgmSlider.onValueChanged.AddListener(val =>
        {
            if (_suppressUiEvents) return;
            MMOAudioManager.MasterBgmVolume = val;
        });

        Button camBtn = CreateButton("CamPresetButton", panel.transform,
            new Vector2(28f, 198f), new Vector2(384f, 40f),
            new Color(0.20f, 0.34f, 0.56f, 1f), "Camera Mode", 16);
        _camModeBtnText = camBtn.GetComponentInChildren<TMP_Text>();
        camBtn.onClick.AddListener(() => MMOCameraController.Instance?.CyclePreset());

        Button fsBtn = CreateButton("FullscreenButton", panel.transform,
            new Vector2(28f, 148f), new Vector2(384f, 40f),
            new Color(0.20f, 0.34f, 0.56f, 1f), "Display Mode", 16);
        _fullscreenBtnText = fsBtn.GetComponentInChildren<TMP_Text>();
        fsBtn.onClick.AddListener(() => Screen.fullScreen = !Screen.fullScreen);

        Button resumeBtn = CreateButton("ResumeButton", panel.transform,
            new Vector2(28f, 82f), new Vector2(384f, 44f),
            new Color(0.18f, 0.62f, 0.34f, 1f), "Resume Game (ESC)", 17);
        resumeBtn.onClick.AddListener(() => _combat?.ClosePauseMenuFromUI());

        Button lobbyBtn = CreateButton("LobbyButton", panel.transform,
            new Vector2(28f, 26f), new Vector2(384f, 44f),
            new Color(0.62f, 0.22f, 0.22f, 1f), "Return to Lobby", 17);
        lobbyBtn.onClick.AddListener(() => _combat?.ReturnToLobbyFromUI());

        _pauseOverlay.SetActive(false);
    }

    // =========================================================
    // FRAME SYNC FROM MMOPlayerCombat
    // =========================================================

    public void OnChatOpened()
    {
        if (_chatInputRow != null) _chatInputRow.SetActive(true);
        if (_chatHintText != null) _chatHintText.gameObject.SetActive(false);
        if (_chatInputField != null)
        {
            _chatInputField.text = string.Empty;
            _chatInputField.ActivateInputField();
            _chatInputField.Select();
        }
    }

    public void OnChatClosed()
    {
        if (_chatInputRow != null) _chatInputRow.SetActive(false);
        if (_chatHintText != null) _chatHintText.gameObject.SetActive(true);
    }

    private void SubmitChatFromUI()
    {
        if (_combat == null) return;
        string text = _chatInputField != null ? _chatInputField.text : string.Empty;
        _combat.SubmitChatMessageFromUI(text);
    }

    public void RefreshHUD(
        MMOMonsterAI target,
        bool isDead,
        float respawnCountdown,
        string combatBanner,
        bool showBanner,
        int level,
        int hp,
        int maxHp,
        int exp,
        int expToNext,
        float critChance,
        float critMult,
        float dodgeCd,
        float stunCd,
        int gold,
        int bossKills,
        float powerBuffRemaining,
        float hasteBuffRemaining,
        string questTitle,
        string questDetail,
        string lootPrompt,
        MMOPlayerInventory inventory,
        bool inventoryOpen,
        bool chatOpen,
        string visibleChatText,
        bool pauseMenuOpen)
    {
        UpdateTargetFrame(target);
        UpdatePartyFrame();
        UpdateMinimap();
        UpdateQuestAndEconomyVisual(gold, powerBuffRemaining, hasteBuffRemaining, questTitle, questDetail, lootPrompt, inventory, inventoryOpen);
        UpdatePlayerBars(isDead, respawnCountdown, combatBanner, showBanner, level, hp, maxHp, exp, expToNext, critChance, critMult, dodgeCd, stunCd, inventory);
        UpdateChatVisual(chatOpen, visibleChatText);
        UpdatePauseVisual(pauseMenuOpen);
    }

    private void UpdateTargetFrame(MMOMonsterAI target)
    {
        if (_targetRoot == null) return;
        bool active = target != null;
        if (_targetRoot.activeSelf != active) _targetRoot.SetActive(active);
        if (!active) return;

        bool isBoss = target.Bridge != null && target.Bridge.Config != null && target.Bridge.Config.isBossOrElite;
        string bossTag = isBoss ? "[BOSS] " : "";
        _targetTitleText.text = $"{bossTag}Lv.{target.level} {target.displayName}";
        _targetTitleText.color = isBoss ? new Color(1f, 0.85f, 0.25f) : Color.white;

        _targetStateText.text = target.CurrentState switch
        {
            MMOMonsterAI.AIState.AggroChase => "AGGRO",
            MMOMonsterAI.AIState.Attacking => "COMBAT",
            MMOMonsterAI.AIState.EvadeReturn => "EVADING",
            MMOMonsterAI.AIState.Dead => "DEFEATED",
            _ => "PATROL"
        };

        _targetHpFill.fillAmount = target.HealthRatio;
        _targetHpText.text = $"{target.CurrentHealth} / {target.MaxHealth} HP ({Mathf.RoundToInt(target.HealthRatio * 100f)}%)";
    }

    private void UpdatePartyFrame()
    {
        if (_partyRoot == null) return;
        int count = MMOPlayerFusion.AllPlayers.Count;
        bool showParty = count > 1;
        if (_partyRoot.activeSelf != showParty) _partyRoot.SetActive(showParty);
        if (!showParty) return;

        _partyHeaderText.text = $"PARTY ({count}/{LobbyNetwork.MaxPlayers})";
        int slot = 0;
        for (int i = 0; i < count && slot < MaxPartySlots; i++)
        {
            MMOPlayerFusion member = MMOPlayerFusion.AllPlayers[i];
            if (member == null || member.Object == null || !member.Object.IsValid) continue;

            _partyCards[slot].SetActive(true);
            int lv = Mathf.Max(1, member.NetworkLevel);
            int hp = Mathf.Max(0, member.NetworkHealth);
            int maxHp = Mathf.Max(1, member.NetworkMaxHealth);
            string tag = member.IsLocalPlayer ? " (You)" : "";

            _partyNames[slot].text = $"Lv.{lv} {member.PlayerName}{tag}";
            _partyNames[slot].color = member.IsLocalPlayer ? new Color(1f, 0.90f, 0.4f) : Color.white;
            _partyHpFills[slot].fillAmount = Mathf.Clamp01((float)hp / maxHp);
            slot++;
        }

        for (int i = slot; i < MaxPartySlots; i++)
        {
            _partyCards[i].SetActive(false);
        }
    }

    private void UpdateMinimap()
    {
        if (_minimapPlot == null || _playerController == null) return;

        Vector3 playerPos = _playerController.transform.position;
        if (_minimapPlayerIcon != null)
        {
            _minimapPlayerIcon.localRotation = Quaternion.Euler(0f, 0f, -_playerController.transform.eulerAngles.y);
        }
        if (_minimapCoordsText != null)
        {
            _minimapCoordsText.text = $"({playerPos.x:0}, {playerPos.z:0})";
        }

        int blipIdx = 0;

        // Remote players in green
        for (int i = 0; i < MMOPlayerFusion.AllPlayers.Count && blipIdx < MaxMinimapBlips; i++)
        {
            MMOPlayerFusion p = MMOPlayerFusion.AllPlayers[i];
            if (p == null || p.IsLocalPlayer) continue;
            SetMinimapBlip(blipIdx++, p.transform.position - playerPos, 9f, new Color(0.28f, 0.95f, 0.45f, 1f));
        }

        // Active monsters in orange / Boss in crimson-gold
        IReadOnlyList<MMOMonsterAI> monsters = MMOMonsterAI.All;
        for (int i = 0; i < monsters.Count && blipIdx < MaxMinimapBlips; i++)
        {
            MMOMonsterAI m = monsters[i];
            if (m == null || m.IsDead) continue;

            bool isBoss = m.Bridge != null && m.Bridge.Config != null && m.Bridge.Config.isBossOrElite;
            SetMinimapBlip(
                blipIdx++,
                m.transform.position - playerPos,
                isBoss ? 12f : 8.5f,
                isBoss ? new Color(1f, 0.25f, 0.15f, 1f) : new Color(1f, 0.72f, 0.20f, 1f));
        }

        for (int i = blipIdx; i < _minimapBlips.Count; i++)
        {
            if (_minimapBlips[i].gameObject.activeSelf)
            {
                _minimapBlips[i].gameObject.SetActive(false);
            }
        }
    }

    private void SetMinimapBlip(int index, Vector3 worldOffset, float dotSize, Color color)
    {
        Image blip = _minimapBlips[index];
        blip.gameObject.SetActive(true);
        blip.color = color;

        Vector2 scaled = new Vector2(worldOffset.x, worldOffset.z) / MinimapWorldRange * MinimapPixelRadius;
        if (scaled.magnitude > MinimapPixelRadius - 5f)
        {
            scaled = scaled.normalized * (MinimapPixelRadius - 5f);
        }

        RectTransform rt = blip.rectTransform;
        rt.anchoredPosition = scaled;
        rt.sizeDelta = new Vector2(dotSize, dotSize);
    }

    private void UpdateQuestAndEconomyVisual(
        int gold,
        float powerBuffRemaining,
        float hasteBuffRemaining,
        string questTitle,
        string questDetail,
        string lootPrompt,
        MMOPlayerInventory inventory,
        bool inventoryOpen)
    {
        if (_questTitleText != null) _questTitleText.text = questTitle;
        if (_questDetailText != null) _questDetailText.text = questDetail;
        if (_goldHudText != null)
        {
            int stones = inventory != null ? inventory.GetItemCount("enhance_stone") : 0;
            _goldHudText.text = $"GOLD: {gold}G  |  STN: {stones}  |  [I] BAG";
        }

        if (_lootPromptBar != null)
        {
            bool hasPrompt = !string.IsNullOrEmpty(lootPrompt) && !inventoryOpen;
            if (_lootPromptBar.activeSelf != hasPrompt) _lootPromptBar.SetActive(hasPrompt);
            if (hasPrompt && _lootPromptText != null) _lootPromptText.text = lootPrompt;
        }

        if (_buffHudText != null)
        {
            bool hasPower = powerBuffRemaining > 0.05f;
            bool hasHaste = hasteBuffRemaining > 0.05f;
            bool hasAny = hasPower || hasHaste;
            if (_buffHudText.gameObject.activeSelf != hasAny)
            {
                _buffHudText.gameObject.SetActive(hasAny);
            }
            if (hasAny)
            {
                string pStr = hasPower ? $"BERSERK +30% ATK ({powerBuffRemaining:0.0}s)" : "";
                string hStr = hasHaste ? $"HASTE +35% SPD ({hasteBuffRemaining:0.0}s)" : "";
                _buffHudText.text = (hasPower && hasHaste) ? $"{pStr}   |   {hStr}" : (pStr + hStr);
            }
        }

        if (_inventoryUI != null)
        {
            _inventoryUI.SetVisible(inventoryOpen);
        }
    }

    private void UpdatePlayerBars(
        bool isDead,
        float respawnCountdown,
        string combatBanner,
        bool showBanner,
        int level,
        int hp,
        int maxHp,
        int exp,
        int expToNext,
        float critChance,
        float critMult,
        float dodgeCd,
        float stunCd,
        MMOPlayerInventory inventory)
    {
        if (_defeatedText != null)
        {
            _defeatedText.gameObject.SetActive(isDead);
            if (isDead)
            {
                _defeatedText.text = $"DEFEATED — Respawning in {respawnCountdown:0.0}s...";
            }
        }

        if (_bannerText != null)
        {
            bool visible = showBanner && !string.IsNullOrEmpty(combatBanner);
            _bannerText.gameObject.SetActive(visible);
            if (visible) _bannerText.text = combatBanner;
        }

        if (_playerHpFill != null)
        {
            _playerHpFill.fillAmount = maxHp > 0 ? Mathf.Clamp01((float)hp / maxHp) : 1f;
        }

        if (_playerExpFill != null)
        {
            _playerExpFill.fillAmount = expToNext > 0 ? Mathf.Clamp01((float)exp / expToNext) : 0f;
        }

        if (_playerStatsText != null)
        {
            _playerStatsText.text = $"PLAYER Lv.{level}   |   HP: {hp}/{maxHp}   |   EXP: {exp}/{expToNext}   |   CRIT: {Mathf.RoundToInt(critChance * 100f)}% (x{critMult:0.0})";
        }

        UpdateSkillSlot(0, 0f, _playerController != null && _playerController.IsAttacking ? "SWING" : "READY");
        UpdateSkillSlot(1, dodgeCd, dodgeCd > 0.05f ? $"{dodgeCd:0.0}s" : "READY");
        UpdateSkillSlot(2, stunCd, stunCd > 0.05f ? $"{stunCd:0.0}s" : "READY");

        for (int h = 0; h < MMOPlayerInventory.HotbarSlotCount; h++)
        {
            SkillSlotUI hb = _hotbarSlots[h];
            string itemId = inventory != null ? inventory.GetHotbarItemId(h) : string.Empty;
            MMOItemTemplate tpl = MMOItemCatalog.Get(itemId);
            int qty = inventory != null ? inventory.GetItemCount(itemId) : 0;
            float cd = inventory != null ? inventory.GetHotbarCooldownRemaining(h) : 0f;

            string keyPrefix = h == 0 ? "[1/R]" : $"[{h + 1}]";
            hb.keyLabel.text = tpl != null ? $"{keyPrefix} {tpl.shortCode}" : $"{keyPrefix} ---";
            hb.statusLabel.text = tpl == null ? "EMPTY" : (cd > 0.05f ? $"{cd:0.0}s (x{qty})" : $"x{qty}");
            hb.statusLabel.color = qty <= 0 ? new Color(0.65f, 0.45f, 0.45f)
                : (cd > 0.05f ? new Color(1f, 0.72f, 0.30f) : new Color(0.38f, 1f, 0.56f));
            hb.fill.color = qty > 0 && cd <= 0.05f
                ? new Color(0.12f, 0.17f, 0.26f, 0.96f)
                : new Color(0.08f, 0.09f, 0.12f, 0.85f);
        }
    }

    private void UpdateSkillSlot(int index, float cooldown, string label)
    {
        SkillSlotUI slot = _skillSlots[index];
        bool ready = cooldown <= 0.05f;
        slot.statusLabel.text = label;
        slot.statusLabel.color = ready ? new Color(0.38f, 1f, 0.56f) : new Color(0.95f, 0.65f, 0.30f);
        slot.fill.color = ready ? new Color(0.11f, 0.16f, 0.24f, 0.96f) : new Color(0.08f, 0.09f, 0.12f, 0.85f);
    }

    private void UpdateChatVisual(bool chatOpen, string visibleChatText)
    {
        bool hasMessages = !string.IsNullOrEmpty(visibleChatText);
        if (_chatLogBg != null)
        {
            _chatLogBg.SetActive(hasMessages);
        }
        if (_chatLogText != null && hasMessages)
        {
            _chatLogText.text = visibleChatText;
        }
        if (_chatInputRow != null && _chatInputRow.activeSelf != chatOpen)
        {
            _chatInputRow.SetActive(chatOpen);
        }
        if (_chatHintText != null && _chatHintText.gameObject.activeSelf == chatOpen)
        {
            _chatHintText.gameObject.SetActive(!chatOpen);
        }
    }

    private void UpdatePauseVisual(bool pauseMenuOpen)
    {
        if (_pauseOverlay == null) return;
        if (_pauseOverlay.activeSelf != pauseMenuOpen)
        {
            _pauseOverlay.SetActive(pauseMenuOpen);
        }
        if (!pauseMenuOpen) return;

        _suppressUiEvents = true;
        MMOCameraController cam = MMOCameraController.Instance;
        if (cam != null)
        {
            if (_sensSlider != null) _sensSlider.value = cam.MouseSensitivity;
            if (_sensValueText != null) _sensValueText.text = $"Mouse Sensitivity: {cam.MouseSensitivity:0.0}";
            if (_camModeBtnText != null) _camModeBtnText.text = $"Camera Mode: {cam.CurrentPreset} (Cycle)";
        }

        if (_sfxSlider != null) _sfxSlider.value = MMOAudioManager.MasterSfxVolume;
        if (_sfxValueText != null) _sfxValueText.text = $"SFX Volume: {Mathf.RoundToInt(MMOAudioManager.MasterSfxVolume * 100f)}%";

        if (_bgmSlider != null) _bgmSlider.value = MMOAudioManager.MasterBgmVolume;
        if (_bgmValueText != null) _bgmValueText.text = $"BGM Volume: {Mathf.RoundToInt(MMOAudioManager.MasterBgmVolume * 100f)}%";

        if (_fullscreenBtnText != null) _fullscreenBtnText.text = Screen.fullScreen ? "Display: Fullscreen (Toggle)" : "Display: Windowed (Toggle)";
        _suppressUiEvents = false;
    }

    // =========================================================
    // UI BUILDER HELPERS
    // =========================================================

    private GameObject CreateBox(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 anchoredPos, Vector2 sizeDelta, Color color)
    {
        GameObject go = new GameObject(name);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = anchorMin;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = sizeDelta;

        Image img = go.AddComponent<Image>();
        img.sprite = _rectSprite;
        img.color = color;
        return go;
    }

    private Image CreateBlipDot(string name, Transform parent, float diameter, Color color)
    {
        GameObject go = new GameObject(name);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(diameter, diameter);

        Image img = go.AddComponent<Image>();
        img.sprite = _circleSprite;
        img.color = color;
        return img;
    }

    private Image CreateBar(string name, Transform parent, Vector2 pos, Vector2 size,
        Vector2 anchorMin, Vector2 anchorMax, Color bgColor, Color fillColor, out Image fillImg)
    {
        GameObject bg = CreateBox(name, parent, anchorMin, anchorMax, pos, size, bgColor);
        Image bgImg = bg.GetComponent<Image>();

        GameObject fillGo = new GameObject("Fill");
        RectTransform fillRt = fillGo.AddComponent<RectTransform>();
        fillRt.SetParent(bg.transform, false);
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = Vector2.one;
        fillRt.offsetMin = Vector2.zero;
        fillRt.offsetMax = Vector2.zero;

        fillImg = fillGo.AddComponent<Image>();
        fillImg.sprite = _rectSprite;
        fillImg.color = fillColor;
        fillImg.type = Image.Type.Filled;
        fillImg.fillMethod = Image.FillMethod.Horizontal;
        fillImg.fillOrigin = (int)Image.OriginHorizontal.Left;
        fillImg.fillAmount = 1f;
        return bgImg;
    }

    private TMP_Text CreateText(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 anchoredPos, Vector2 sizeDelta, float fontSize, FontStyles style, TextAlignmentOptions align, Color color)
    {
        GameObject go = new GameObject(name);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = sizeDelta;

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null)
        {
            tmp.font = TMP_Settings.defaultFontAsset;
        }
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.color = color;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.raycastTarget = false;
        return tmp;
    }

    private Button CreateButton(string name, Transform parent, Vector2 pos, Vector2 size, Color bgColor, string text, float fontSize)
    {
        GameObject go = CreateBox(name, parent, Vector2.zero, Vector2.zero, pos, size, bgColor);
        AddOutline(go, new Color(1f, 1f, 1f, 0.25f));
        Button btn = go.AddComponent<Button>();

        TMP_Text label = CreateText("Label", go.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
            fontSize, FontStyles.Bold, TextAlignmentOptions.Center, Color.white);
        label.text = text;
        return btn;
    }

    private Slider CreateSlider(string name, Transform parent, Vector2 anchoredPos, Vector2 size, float minVal, float maxVal)
    {
        GameObject go = new GameObject(name);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;

        CreateBox("Background", rt, new Vector2(0f, 0.25f), new Vector2(1f, 0.75f), Vector2.zero, Vector2.zero, new Color(0.16f, 0.20f, 0.28f, 1f));

        GameObject fillArea = new GameObject("FillArea");
        RectTransform faRt = fillArea.AddComponent<RectTransform>();
        faRt.SetParent(rt, false);
        faRt.anchorMin = new Vector2(0f, 0.25f);
        faRt.anchorMax = new Vector2(1f, 0.75f);
        faRt.offsetMin = Vector2.zero;
        faRt.offsetMax = Vector2.zero;

        GameObject fill = CreateBox("Fill", faRt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0.28f, 0.68f, 1f, 1f));

        GameObject handleArea = new GameObject("HandleArea");
        RectTransform haRt = handleArea.AddComponent<RectTransform>();
        haRt.SetParent(rt, false);
        haRt.anchorMin = Vector2.zero;
        haRt.anchorMax = Vector2.one;
        haRt.offsetMin = new Vector2(8f, 0f);
        haRt.offsetMax = new Vector2(-8f, 0f);

        GameObject handle = CreateBox("Handle", haRt, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(18f, 0f), Color.white);
        handle.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 0.5f);
        Image handleImg = handle.GetComponent<Image>();
        handleImg.sprite = _circleSprite;

        Slider slider = go.AddComponent<Slider>();
        slider.minValue = minVal;
        slider.maxValue = maxVal;
        slider.fillRect = fill.GetComponent<RectTransform>();
        slider.handleRect = handle.GetComponent<RectTransform>();
        slider.targetGraphic = handleImg;
        return slider;
    }

    private static void AddOutline(GameObject go, Color color)
    {
        Outline ol = go.AddComponent<Outline>();
        ol.effectColor = color;
        ol.effectDistance = new Vector2(1.5f, 1.5f);
    }
}
