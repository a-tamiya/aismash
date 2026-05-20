using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using TMPro;
using PromptFighters.AI;
using PromptFighters.Battle;
using PromptFighters.Battle.Skills;
using PromptFighters.UI;
using PromptFighters.Utils;

namespace PromptFighters.GameFlow
{
    public class PreBattlePanel : MonoBehaviour
    {
        List<CharacterData> _presets;
        int _builtInPresetCount = 0; // プリセット（初期キャラ）の件数。以降が生成済みキャラ。
        int _p1PresetIdx = 0;
        int _p2PresetIdx = 1;
        int _p1IconPage = 0;
        int _p2IconPage = 0;

        TextMeshProUGUI _p1GamepadLabel;
        TextMeshProUGUI _p2GamepadLabel;
        TextMeshProUGUI _p1PresetLabel;
        TextMeshProUGUI _p2PresetLabel;
        TextMeshProUGUI _p1CategoryLabel;
        TextMeshProUGUI _p2CategoryLabel;
        Transform _p1IconGrid;
        Transform _p2IconGrid;
        TextMeshProUGUI _p1DetailText;
        TextMeshProUGUI _p2DetailText;
        Image _p1PreviewImage;
        Image _p2PreviewImage;
        Button _p1DeleteButton;
        Button _p2DeleteButton;
        TextMeshProUGUI _p1PageLabel;
        TextMeshProUGUI _p2PageLabel;
        TMP_InputField _p1NameInput;
        TMP_InputField _p1FeatureInput;
        TMP_InputField _p2NameInput;
        TMP_InputField _p2FeatureInput;

        GameObject _titlePanel;
        GameObject _panel;
        GameObject _generationSetupPanel;
        GameObject _trainingPanel;
        TextMeshProUGUI _trainingControlsText;

        // Phase 4: 生成中・技確認パネル
        GameObject _generatingPanel;
        GameObject _skillConfirmPanel;
        TextMeshProUGUI _generatingStatusText;
        TextMeshProUGUI[] _confirmP1SkillTexts = new TextMeshProUGUI[4];
        TextMeshProUGUI[] _confirmP2SkillTexts = new TextMeshProUGUI[4];
        TextMeshProUGUI _confirmP1Name;
        TextMeshProUGUI _confirmP2Name;
        TextMeshProUGUI _confirmP1Desc;
        TextMeshProUGUI _confirmP2Desc;
        TextMeshProUGUI _confirmP1Stats;
        TextMeshProUGUI _confirmP2Stats;
        Image _confirmP1Image;
        Image _confirmP2Image;
        CharacterData _pendingData1;
        CharacterData _pendingData2;
        Coroutine _generationCoroutine;
        bool _generationTrainingActive;
        TextMeshProUGUI _debugSkipImageLabel;

        Image _titleTopGlow;
        Image _titleBottomGlow;
        RectTransform _titleMainRect;
        RectTransform _startButtonRect;
        bool _waitForMenuInputRelease;

        // AI機能トグル
        Image _commentaryToggleBg;
        TextMeshProUGUI _commentaryToggleLabel;
        Image _angelToggleBg;
        TextMeshProUGUI _angelToggleLabel;

        static readonly Color ToggleOnColor  = new Color(0.15f, 0.55f, 0.9f, 1f);
        static readonly Color ToggleOffColor = new Color(0.18f, 0.18f, 0.22f, 1f);

        void Awake()
        {
            EnsureInputSystemUIInputModule();
        }

        void Start()
        {
            // プリセット + 保存済みキャラを合わせてリストを構築
            var builtIn = PresetCharacterLoader.LoadAll();
            _builtInPresetCount = builtIn.Count;
            _presets = new List<CharacterData>(builtIn);
            _presets.AddRange(CharacterSaveManager.LoadAll());
            if (_presets.Count < 2) _p2PresetIdx = 0;
            BuildTitlePanel();
            BuildPanel();
            BuildGenerationSetupPanel();
            BuildGeneratingPanel();
            BuildSkillConfirmPanel();
            UITheme.ApplyAllInScene();
            RebuildIconGrids();
            RefreshCharacterPreview();
            UpdateCategoryLabels();
            ShowTitlePanel();

            if (BattleManager.Instance != null)
            {
                BattleManager.Instance.OnReturnedToSetup += ShowPanel;
                BattleManager.Instance.OnTrainingStart    += ShowTrainingPanel;
            }
        }

        void Update()
        {
            if (_titlePanel != null && _titlePanel.activeSelf)
            {
                AnimateTitle();

                if (WasMenuConfirmPressed())
                {
                    ShowCharacterSelect();
                }
            }

            if (_panel != null && _panel.activeSelf)
            {
                RefreshGamepadLabels();
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (_waitForMenuInputRelease)
                {
                    if (kb == null ||
                        (!kb.spaceKey.isPressed && !kb.enterKey.isPressed && !kb.tKey.isPressed))
                        _waitForMenuInputRelease = false;
                    return;
                }

                if (IsEditingText()) return;

                if (WasMenuConfirmPressed()) OnStartPressed();
                if (WasTrainingPressed()) OnTrainingPressed();
                if (WasGeneratePressed()) ShowGenerationSetupPanel();
            }

            if (_trainingPanel != null && _trainingPanel.activeSelf)
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (WasKeyboardCancelPressed())
                {
                    if (_generationTrainingActive && _generationCoroutine != null)
                        ReturnToGeneratingFromTraining();
                    else
                        BattleManager.Instance?.ReturnToSetup();
                }
                if (WasResetPressed())
                {
                    BattleManager.Instance?.ResetTrainingRound();
                }
            }

            if (_generatingPanel != null && _generatingPanel.activeSelf)
            {
                if (WasCancelPressed())
                    CancelGeneration();
                // 生成中でもTキーでトレーニング。生成コルーチンは止めない。
                if (WasTrainingPressed())
                {
                    StartTrainingDuringGeneration();
                }
            }

            if (_skillConfirmPanel != null && _skillConfirmPanel.activeSelf)
            {
                if (WasMenuConfirmPressed())
                    OnSkillConfirmBattlePressed();
                if (WasCancelPressed())
                    ShowPanel();
            }
        }

        void BuildTitlePanel()
        {
            _titlePanel = CreateUIObject("TitleOverlay", transform);
            StretchFull(_titlePanel.GetComponent<RectTransform>());

            var bg = _titlePanel.AddComponent<Image>();
            bg.sprite = CreateGradientSprite(
                new Color(0.015f, 0.02f, 0.05f, 1f),
                new Color(0.08f, 0.0f, 0.18f, 1f),
                new Color(0.0f, 0.12f, 0.20f, 1f),
                new Color(0.0f, 0.0f, 0.03f, 1f));
            bg.type = Image.Type.Simple;

            var cg = _titlePanel.AddComponent<CanvasGroup>();
            cg.interactable = true;
            cg.blocksRaycasts = true;

            _titleTopGlow = MakePanel(_titlePanel.transform, "TopGlow",
                new Vector2(0, 245), new Vector2(900, 120),
                new Color(0.1f, 0.45f, 1f, 0.22f));
            _titleBottomGlow = MakePanel(_titlePanel.transform, "BottomGlow",
                new Vector2(0, -245), new Vector2(920, 140),
                new Color(1f, 0.2f, 0.55f, 0.18f));
            MakePanel(_titlePanel.transform, "CenterFrame",
                new Vector2(0, 4), new Vector2(760, 380),
                new Color(0.02f, 0.025f, 0.055f, 0.72f));
            MakeOutline(_titlePanel.transform, "FrameTop", new Vector2(0, 198), new Vector2(760, 3),
                new Color(0.7f, 0.95f, 1f, 0.75f));
            MakeOutline(_titlePanel.transform, "FrameBottom", new Vector2(0, -190), new Vector2(760, 3),
                new Color(1f, 0.65f, 0.2f, 0.75f));

            // ロゴ画像 (Resources/Art/logo.png) があれば表示、なければテキストフォールバック
            var logoSprite = Resources.Load<Sprite>("Art/logo");
            if (logoSprite != null)
            {
                var logoGo = CreateUIObject("LogoImage", _titlePanel.transform);
                var logoRt = logoGo.GetComponent<RectTransform>();
                float aspect = (float)logoSprite.texture.width / logoSprite.texture.height;
                float logoW = Mathf.Min(640f, logoSprite.texture.width);
                float logoH = logoW / aspect;
                logoRt.anchoredPosition = new Vector2(0, 80f);
                logoRt.sizeDelta = new Vector2(logoW, logoH);
                var logoImg = logoGo.AddComponent<Image>();
                logoImg.sprite = logoSprite;
                logoImg.preserveAspect = true;
                _titleMainRect = logoRt;
            }
            else
            {
                MakeLabel(_titlePanel.transform, "TitleKicker", "AI PROMPT FIGHTER",
                    new Vector2(0, 120), new Vector2(620, 44), 22, new Color(0.75f, 0.95f, 1f));
                var title = MakeLabel(_titlePanel.transform, "TitleMain", "PROMPT FIGHTERS",
                    new Vector2(0, 54), new Vector2(760, 92), 58, new Color(1f, 0.92f, 0.46f));
                title.fontStyle = FontStyles.Bold;
                title.characterSpacing = 2f;
                _titleMainRect = title.rectTransform;
            }

            MakeLabel(_titlePanel.transform, "TitleSub",
                "プロンプトでファイターを作ろう。API準備中はプリセットで対戦・トレーニングができます。",
                new Vector2(0, -25), new Vector2(700, 38), 15, new Color(0.92f, 0.96f, 1f));
            MakeLabel(_titlePanel.transform, "ApiNote",
                "現在はプリセットキャラ・サンプル技・トレーニングモードでプレイできます。",
                new Vector2(0, -58), new Vector2(700, 28), 13, new Color(0.72f, 0.84f, 1f));

            var startButton = MakeButton(_titlePanel.transform, "GameStartBtn", "ゲームスタート",
                new Vector2(0, -128), new Vector2(310, 62), ShowCharacterSelect,
                new Color(0.92f, 0.42f, 0.08f, 1f));
            SetButtonLabelStyle(startButton, 22f, FontStyles.Bold, new Color(1f, 0.98f, 0.9f));
            _startButtonRect = startButton.GetComponent<RectTransform>();
            MakeLabel(_titlePanel.transform, "StartHelp", "スペース / エンターキー",
                new Vector2(0, -178), new Vector2(320, 24), 13, new Color(0.8f, 0.88f, 1f));

            // AI機能トグルボタン
            MakeLabel(_titlePanel.transform, "AiToggleLabel", "AI機能",
                new Vector2(-100f, -220f), new Vector2(80f, 24f), 13, new Color(0.72f, 0.84f, 1f));

            var commentaryBtn = MakeButton(_titlePanel.transform, "CommentaryToggle",
                CommentaryToggleText(),
                new Vector2(20f, -220f), new Vector2(150f, 34f),
                OnCommentaryToggle, ToggleOnColor);
            _commentaryToggleBg   = commentaryBtn.GetComponent<Image>();
            _commentaryToggleLabel = commentaryBtn.GetComponentInChildren<TextMeshProUGUI>();

            var angelBtn = MakeButton(_titlePanel.transform, "AngelToggle",
                AngelToggleText(),
                new Vector2(180f, -220f), new Vector2(150f, 34f),
                OnAngelToggle, ToggleOnColor);
            _angelToggleBg   = angelBtn.GetComponent<Image>();
            _angelToggleLabel = angelBtn.GetComponentInChildren<TextMeshProUGUI>();

            RefreshToggleVisuals();

            MakeLabel(_titlePanel.transform, "Footer",
                "1P: WASD + J/K/L/G    スマッシュ: A/Dはじき+J    2P: 矢印 + テンキー2/3/1/0",
                new Vector2(0, -286), new Vector2(760, 28), 12, new Color(0.76f, 0.82f, 0.9f));
        }

        static string CommentaryToggleText() =>
            PromptFighters.UI.CommentaryController.Enabled ? "実況 ON" : "実況 OFF";
        static string AngelToggleText() =>
            PromptFighters.UI.AngelController.Enabled ? "天使 ON" : "天使 OFF";

        void OnCommentaryToggle()
        {
            PromptFighters.UI.CommentaryController.Enabled = !PromptFighters.UI.CommentaryController.Enabled;
            RefreshToggleVisuals();
        }

        void OnAngelToggle()
        {
            PromptFighters.UI.AngelController.Enabled = !PromptFighters.UI.AngelController.Enabled;
            RefreshToggleVisuals();
        }

        void RefreshToggleVisuals()
        {
            bool ce = PromptFighters.UI.CommentaryController.Enabled;
            bool ae = PromptFighters.UI.AngelController.Enabled;
            if (_commentaryToggleBg  != null) _commentaryToggleBg.color  = ce ? ToggleOnColor  : ToggleOffColor;
            if (_commentaryToggleLabel != null) _commentaryToggleLabel.text = CommentaryToggleText();
            if (_angelToggleBg       != null) _angelToggleBg.color       = ae ? ToggleOnColor  : ToggleOffColor;
            if (_angelToggleLabel    != null) _angelToggleLabel.text    = AngelToggleText();
        }

        void BuildPanel()
        {
            _panel = CreateUIObject("PreBattleOverlay", transform);
            StretchFull(_panel.GetComponent<RectTransform>());

            var bg = _panel.AddComponent<Image>();
            bg.sprite = CreateGradientSprite(
                new Color(0.02f, 0.02f, 0.06f, 1f),
                new Color(0.06f, 0f, 0.14f, 1f),
                new Color(0f, 0.08f, 0.16f, 1f),
                new Color(0f, 0f, 0.04f, 1f));
            bg.type = Image.Type.Simple;

            var cg = _panel.AddComponent<CanvasGroup>();
            cg.interactable = true;
            cg.blocksRaycasts = true;

            // ── ヘッダー ──
            var header = CreateUIObject("Header", _panel.transform);
            var hRt = header.GetComponent<RectTransform>();
            hRt.anchorMin = new Vector2(0f, 1f);
            hRt.anchorMax = new Vector2(1f, 1f);
            hRt.offsetMin = new Vector2(0f, -80f);
            hRt.offsetMax = Vector2.zero;
            header.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);

            MakeLabel(_panel.transform, "PanelTitle", "キャラクター選択",
                new Vector2(0, 504), new Vector2(700, 56), 32, new Color(1f, 0.88f, 0.3f));

            // ── 中央仕切り線 ──
            MakeOutline(_panel.transform, "Divider", new Vector2(0, 0), new Vector2(3, 860),
                new Color(1f, 1f, 1f, 0.12f));

            // ── 1P エリア（左半分） ──
            BuildPlayerColumn(_panel.transform, true);

            // ── 2P エリア（右半分） ──
            BuildPlayerColumn(_panel.transform, false);

            // ── フッター: ボタン ──
            var startBtn = MakeButton(_panel.transform, "StartBtn", "バトル開始",
                new Vector2(-300, -460), new Vector2(250, 64), OnStartPressed,
                new Color(0.1f, 0.55f, 0.1f, 1f));
            SetButtonLabelStyle(startBtn, 22f, FontStyles.Bold, Color.white);

            var trainBtn = MakeButton(_panel.transform, "TrainingBtn", "トレーニング",
                new Vector2(0, -460), new Vector2(250, 64), OnTrainingPressed,
                new Color(0.1f, 0.25f, 0.65f, 1f));
            SetButtonLabelStyle(trainBtn, 22f, FontStyles.Bold, Color.white);

            var genBtn = MakeButton(_panel.transform, "GenerateBtn", "キャラ生成",
                new Vector2(300, -460), new Vector2(250, 64), ShowGenerationSetupPanel,
                new Color(0.55f, 0.22f, 0.08f, 1f));
            SetButtonLabelStyle(genBtn, 22f, FontStyles.Bold, Color.white);

            MakeLabel(_panel.transform, "StartHelp", "スペース: 既存キャラでバトル / T: トレーニング / G: 新規生成",
                new Vector2(0, -505), new Vector2(800, 28), 14, new Color(0.72f, 0.8f, 1f));

            // ── 操作ガイド ──
            MakeLabel(_panel.transform, "CtrlHelp",
                "1P: WASD 移動 / J 基本技A / K 基本技B / L 基本技C / A/Dはじき+J スマッシュ / G つかみ / 左Shift ガード・回避\n" +
                "2P: 矢印キー 移動 / テンキー2 基本技A / 3 基本技B / 1 基本技C / ←/→はじき+2 スマッシュ / 0 つかみ / 右Shift ガード・回避",
                new Vector2(0, 540 - 810), new Vector2(840, 52), 13, new Color(0.72f, 0.78f, 0.92f));

            BuildTrainingPanel();
        }

        void RefreshGamepadLabels()
        {
            var active = new System.Collections.Generic.List<UnityEngine.InputSystem.Gamepad>();
            foreach (var gp in UnityEngine.InputSystem.Gamepad.all)
                if (gp.lastUpdateTime > 0) active.Add(gp);

            UpdateGpLabel(active, _p1GamepadLabel, 0);
            UpdateGpLabel(active, _p2GamepadLabel, 1);
        }

        static void UpdateGpLabel(System.Collections.Generic.List<UnityEngine.InputSystem.Gamepad> active, TextMeshProUGUI label, int index)
        {
            if (label == null) return;
            if (index < active.Count)
            {
                label.text  = $"● コントローラー接続中 ({active[index].displayName})";
                label.color = new Color(0.3f, 0.9f, 0.3f);
            }
            else
            {
                label.text  = "● コントローラー未接続 (キーボード)";
                label.color = new Color(0.6f, 0.6f, 0.6f);
            }
        }

        void BuildPlayerColumn(Transform parent, bool isP1)
        {
            float cx = isP1 ? -480f : 480f;
            var pColor = isP1 ? new Color(0.4f, 0.75f, 1f) : new Color(1f, 0.55f, 0.35f);
            var bgColor = isP1
                ? new Color(0.08f, 0.12f, 0.22f, 0.6f)
                : new Color(0.22f, 0.08f, 0.08f, 0.6f);

            // 背景
            var colBg = CreateUIObject(isP1 ? "P1ColBg" : "P2ColBg", parent);
            var cbRt = colBg.GetComponent<RectTransform>();
            cbRt.anchorMin = new Vector2(isP1 ? 0f : 0.5f, 0f);
            cbRt.anchorMax = new Vector2(isP1 ? 0.5f : 1f, 1f);
            cbRt.offsetMin = isP1 ? new Vector2(0f, 80f) : new Vector2(0f, 80f);
            cbRt.offsetMax = isP1 ? new Vector2(-2f, -80f) : new Vector2(0f, -80f);
            colBg.AddComponent<Image>().color = bgColor;

            // プレイヤーバッジ
            MakeLabel(parent, isP1 ? "P1Badge" : "P2Badge",
                isP1 ? "1P" : "2P",
                new Vector2(cx, 360f), new Vector2(100f, 60f), 40, pColor)
                .fontStyle = FontStyles.Bold;

            // コントローラー接続状態
            var gpLabel = MakeLabel(parent, isP1 ? "P1GpStatus" : "P2GpStatus",
                "",
                new Vector2(cx, 323f), new Vector2(280f, 22f), 12, Color.gray);
            if (isP1) _p1GamepadLabel = gpLabel;
            else      _p2GamepadLabel = gpLabel;

            // カラーライン
            MakeOutline(parent, isP1 ? "P1Line" : "P2Line",
                new Vector2(cx, 310f), new Vector2(280f, 3f), pColor);

            // 選択中キャラ名
            var row = CreateUIObject(isP1 ? "P1Row" : "P2Row", parent);
            var rowRt = row.GetComponent<RectTransform>();
            rowRt.anchorMin = rowRt.anchorMax = new Vector2(0.5f, 0.5f);
            rowRt.anchoredPosition = new Vector2(cx, 280f);
            rowRt.sizeDelta = new Vector2(390f, 48f);

            MakeButton(row.transform, "Left", "<", new Vector2(-172f, 0f), new Vector2(42f, 42f),
                isP1
                    ? () => ChangePreset(ref _p1PresetIdx, -1, _p1PresetLabel)
                    : () => ChangePreset(ref _p2PresetIdx, -1, _p2PresetLabel),
                new Color(0.2f, 0.2f, 0.35f));

            var label = MakeLabel(row.transform, "Preset",
                isP1 ? GetPresetName(_p1PresetIdx) : GetPresetName(_p2PresetIdx),
                new Vector2(0f, 0f), new Vector2(250f, 48f), 18, Color.white);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;

            MakeButton(row.transform, "Right", ">", new Vector2(172f, 0f), new Vector2(42f, 42f),
                isP1
                    ? () => ChangePreset(ref _p1PresetIdx, +1, _p1PresetLabel)
                    : () => ChangePreset(ref _p2PresetIdx, +1, _p2PresetLabel),
                new Color(0.2f, 0.2f, 0.35f));

            if (isP1) _p1PresetLabel = label;
            else       _p2PresetLabel = label;

            // カテゴリラベル（初期キャラ / 生成済み）
            var catLabel = MakeLabel(row.transform, "Category", "初期キャラ",
                new Vector2(0f, -26f), new Vector2(250f, 22f), 12f,
                new Color(0.65f, 0.75f, 0.9f));
            catLabel.textWrappingMode = TextWrappingModes.NoWrap;
            if (isP1) _p1CategoryLabel = catLabel;
            else       _p2CategoryLabel = catLabel;

            var gridFrame = CreateUIObject(isP1 ? "P1IconGridFrame" : "P2IconGridFrame", parent);
            var gfRt = gridFrame.GetComponent<RectTransform>();
            gfRt.anchoredPosition = new Vector2(cx, 168f);
            gfRt.sizeDelta = new Vector2(392f, 210f);
            AddImage(gridFrame, new Color(0.015f, 0.018f, 0.035f, 0.82f));
            MakeOutline(gridFrame.transform, "GridTop", new Vector2(0f, 104f), new Vector2(392f, 2f), pColor);

            var grid = CreateUIObject(isP1 ? "P1IconGrid" : "P2IconGrid", gridFrame.transform);
            StretchFull(grid.GetComponent<RectTransform>());
            var layout = grid.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(86f, 58f);
            layout.spacing = new Vector2(8f, 8f);
            layout.padding = new RectOffset(12, 12, 6, 6);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 4;
            if (isP1) _p1IconGrid = grid.transform;
            else _p2IconGrid = grid.transform;

            var prevPage = MakeButton(gridFrame.transform, "PrevPage", "<", new Vector2(-166f, -88f), new Vector2(42f, 30f),
                () => ChangeIconPage(isP1, -1), new Color(0.12f, 0.13f, 0.18f));
            SetButtonLabelStyle(prevPage, 15f, FontStyles.Bold, Color.white);
            var nextPage = MakeButton(gridFrame.transform, "NextPage", ">", new Vector2(166f, -88f), new Vector2(42f, 30f),
                () => ChangeIconPage(isP1, 1), new Color(0.12f, 0.13f, 0.18f));
            SetButtonLabelStyle(nextPage, 15f, FontStyles.Bold, Color.white);
            var pageLabel = MakeLabel(gridFrame.transform, "PageLabel", "1/1",
                new Vector2(0f, -88f), new Vector2(120f, 24f), 12f, new Color(0.82f, 0.9f, 1f));
            if (isP1) _p1PageLabel = pageLabel;
            else _p2PageLabel = pageLabel;

            var previewFrame = CreateUIObject(isP1 ? "P1PreviewFrame" : "P2PreviewFrame", parent);
            var pfRt = previewFrame.GetComponent<RectTransform>();
            pfRt.anchoredPosition = new Vector2(cx - 120f, -72f);
            pfRt.sizeDelta = new Vector2(150f, 150f);
            AddImage(previewFrame, new Color(0.01f, 0.012f, 0.02f, 0.72f));
            MakeOutline(previewFrame.transform, "PreviewTop", new Vector2(0f, 74f), new Vector2(150f, 3f), pColor);

            var previewGo = CreateUIObject(isP1 ? "P1PreviewImage" : "P2PreviewImage", previewFrame.transform);
            var pvRt = previewGo.GetComponent<RectTransform>();
            pvRt.anchorMin = Vector2.zero;
            pvRt.anchorMax = Vector2.one;
            pvRt.offsetMin = new Vector2(18f, 12f);
            pvRt.offsetMax = new Vector2(-18f, -12f);
            var preview = previewGo.AddComponent<Image>();
            preview.preserveAspect = true;
            preview.raycastTarget = false;
            preview.color = Color.white;
            if (isP1) _p1PreviewImage = preview;
            else _p2PreviewImage = preview;

            var detailBg = CreateUIObject(isP1 ? "P1DetailBg" : "P2DetailBg", parent);
            var dbRt = detailBg.GetComponent<RectTransform>();
            dbRt.anchoredPosition = new Vector2(cx + 84f, -72f);
            dbRt.sizeDelta = new Vector2(248f, 172f);
            AddImage(detailBg, new Color(0.015f, 0.018f, 0.035f, 0.82f));

            var detailText = MakeLabel(detailBg.transform, "DetailText", "",
                Vector2.zero, new Vector2(228f, 156f), 10.5f, new Color(0.9f, 0.95f, 1f));
            detailText.alignment = TextAlignmentOptions.TopLeft;
            detailText.textWrappingMode = TextWrappingModes.Normal;
            if (isP1) _p1DetailText = detailText;
            else _p2DetailText = detailText;

            MakeLabel(parent, isP1 ? "P1ExistingHint" : "P2ExistingHint",
                "既存キャラ選択",
                new Vector2(cx, -182f), new Vector2(300f, 26f), 13f, new Color(0.72f, 0.8f, 1f));

            var deleteBtn = MakeButton(parent, isP1 ? "P1DeleteGeneratedBtn" : "P2DeleteGeneratedBtn", "生成キャラ削除",
                new Vector2(cx, -220f), new Vector2(190f, 38f), () => DeleteSelectedCharacter(isP1),
                new Color(0.32f, 0.08f, 0.08f, 1f));
            SetButtonLabelStyle(deleteBtn, 14f, FontStyles.Bold, Color.white);
            if (isP1) _p1DeleteButton = deleteBtn;
            else _p2DeleteButton = deleteBtn;
        }

        void BuildTrainingPanel()
        {
            _trainingPanel = CreateUIObject("TrainingOverlay", transform);
            StretchFull(_trainingPanel.GetComponent<RectTransform>());
            _trainingPanel.SetActive(false);

            var cg = _trainingPanel.AddComponent<CanvasGroup>();
            cg.interactable = false;
            cg.blocksRaycasts = false;

            MakeLabel(_trainingPanel.transform, "TrainingTitle", "トレーニングモード",
                new Vector2(0, 485), new Vector2(480, 46), 28, new Color(0.5f, 0.85f, 1f));
            _trainingControlsText = MakeLabel(_trainingPanel.transform, "TrainingControls",
                BuildTrainingHelpText(),
                new Vector2(0, 440), new Vector2(900, 52), 14, new Color(0.9f, 0.95f, 1f));
        }

        void BuildGenerationSetupPanel()
        {
            _generationSetupPanel = CreateUIObject("GenerationSetupOverlay", transform);
            StretchFull(_generationSetupPanel.GetComponent<RectTransform>());
            _generationSetupPanel.SetActive(false);

            var bg = _generationSetupPanel.AddComponent<Image>();
            bg.sprite = CreateGradientSprite(
                new Color(0.02f, 0.02f, 0.06f, 1f),
                new Color(0.09f, 0.02f, 0.12f, 1f),
                new Color(0f, 0.08f, 0.14f, 1f),
                new Color(0f, 0f, 0.04f, 1f));

            MakeLabel(_generationSetupPanel.transform, "GenSetupTitle", "新規キャラクター生成",
                new Vector2(0f, 455f), new Vector2(760f, 56f), 30f, new Color(1f, 0.88f, 0.35f));

            BuildGenerationColumn(_generationSetupPanel.transform, true);
            BuildGenerationColumn(_generationSetupPanel.transform, false);

            var startGen = MakeButton(_generationSetupPanel.transform, "StartGenerateBtn", "生成開始",
                new Vector2(-170f, -430f), new Vector2(260f, 62f), OnGeneratePressed,
                new Color(0.65f, 0.28f, 0.08f, 1f));
            SetButtonLabelStyle(startGen, 22f, FontStyles.Bold, Color.white);

            var back = MakeButton(_generationSetupPanel.transform, "BackToSelectBtn", "戻る",
                new Vector2(170f, -430f), new Vector2(220f, 62f), ShowCharacterSelect,
                new Color(0.14f, 0.16f, 0.22f, 1f));
            SetButtonLabelStyle(back, 20f, FontStyles.Bold, Color.white);

            MakeLabel(_generationSetupPanel.transform, "GenSetupHint",
                "空欄のプレイヤーは選択中の既存キャラを使用します。生成中はTキーで練習できます。",
                new Vector2(0f, -485f), new Vector2(840f, 28f), 13f, new Color(0.78f, 0.86f, 1f));

            var debugBtn = MakeButton(_generationSetupPanel.transform, "DebugSkipImageBtn",
                "", new Vector2(0f, -525f), new Vector2(420f, 40f),
                ToggleSkipImageMode, new Color(0.08f, 0.12f, 0.08f, 1f));
            _debugSkipImageLabel = debugBtn.GetComponentInChildren<TextMeshProUGUI>();
            RefreshDebugSkipLabel();
        }

        void ToggleSkipImageMode()
        {
            DebugSettings.SkipImageGeneration = !DebugSettings.SkipImageGeneration;
            RefreshDebugSkipLabel();
        }

        void RefreshDebugSkipLabel()
        {
            if (_debugSkipImageLabel == null) return;
            _debugSkipImageLabel.text = DebugSettings.SkipImageGeneration
                ? "[デバッグ] 画像スキップ: ON"
                : "[デバッグ] 画像スキップ: OFF";
            _debugSkipImageLabel.color = DebugSettings.SkipImageGeneration
                ? new Color(0.4f, 1f, 0.4f)
                : new Color(0.55f, 0.65f, 0.55f);
        }

        void BuildGenerationColumn(Transform parent, bool isP1)
        {
            float cx = isP1 ? -330f : 330f;
            var pColor = isP1 ? new Color(0.4f, 0.75f, 1f) : new Color(1f, 0.55f, 0.35f);

            MakePanel(parent, isP1 ? "P1GenBg" : "P2GenBg",
                new Vector2(cx, 35f), new Vector2(520f, 650f), new Color(0.01f, 0.014f, 0.03f, 0.72f));
            MakeLabel(parent, isP1 ? "P1GenBadge" : "P2GenBadge", isP1 ? "1P" : "2P",
                new Vector2(cx, 325f), new Vector2(120f, 44f), 28f, pColor).fontStyle = FontStyles.Bold;
            MakeOutline(parent, isP1 ? "P1GenLine" : "P2GenLine",
                new Vector2(cx, 292f), new Vector2(360f, 2f), pColor);

            var nameInput = MakeInputField(parent, isP1 ? "P1GenerateNameInput" : "P2GenerateNameInput",
                "キャラクター名", new Vector2(cx, 220f), new Vector2(430f, 48f), false);
            var featureInput = MakeInputField(parent, isP1 ? "P1GenerateFeatureInput" : "P2GenerateFeatureInput",
                "特徴・見た目・戦い方", new Vector2(cx, 55f), new Vector2(430f, 210f), true);

            MakeLabel(parent, isP1 ? "P1GenNote" : "P2GenNote",
                "例: 雷をまとった小柄な剣士。素早く跳び回り、遠距離から雷を飛ばす。",
                new Vector2(cx, -78f), new Vector2(430f, 54f), 13f, new Color(0.72f, 0.78f, 0.9f));

            if (isP1)
            {
                _p1NameInput = nameInput;
                _p1FeatureInput = featureInput;
            }
            else
            {
                _p2NameInput = nameInput;
                _p2FeatureInput = featureInput;
            }
        }

        void BuildGeneratingPanel()
        {
            _generatingPanel = CreateUIObject("GeneratingOverlay", transform);
            StretchFull(_generatingPanel.GetComponent<RectTransform>());
            _generatingPanel.SetActive(false);

            var bg = _generatingPanel.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.88f);

            MakeLabel(_generatingPanel.transform, "GenTitle",
                "AIがキャラクターと技を生成中...",
                new Vector2(0, 120), new Vector2(700, 56), 30f, new Color(0.5f, 0.9f, 1f));

            _generatingStatusText = MakeLabel(_generatingPanel.transform, "GenStatus",
                "生成を開始しています...",
                new Vector2(0, 40), new Vector2(700, 40), 18f, new Color(0.85f, 0.9f, 1f));

            MakeLabel(_generatingPanel.transform, "GenNote",
                "しばらくお待ちください。OpenAI API を使用しています。",
                new Vector2(0, -20), new Vector2(700, 32), 14f, new Color(0.65f, 0.75f, 0.9f));

            MakeButton(_generatingPanel.transform, "CancelBtn", "キャンセル（ローカル生成で続行）",
                new Vector2(0, -100), new Vector2(400, 52), CancelGeneration,
                new Color(0.25f, 0.15f, 0.15f));

            MakeLabel(_generatingPanel.transform, "TrainHint",
                "Tキー: 生成を続けたままトレーニング　Esc: キャンセル",
                new Vector2(0, -165), new Vector2(700, 30), 13f, new Color(0.6f, 0.7f, 0.85f));
        }

        void BuildSkillConfirmPanel()
        {
            _skillConfirmPanel = CreateUIObject("SkillConfirmOverlay", transform);
            StretchFull(_skillConfirmPanel.GetComponent<RectTransform>());
            _skillConfirmPanel.SetActive(false);

            var bg = _skillConfirmPanel.AddComponent<Image>();
            bg.sprite = CreateGradientSprite(
                new Color(0.02f, 0.02f, 0.06f, 1f), new Color(0.06f, 0f, 0.14f, 1f),
                new Color(0f, 0.08f, 0.16f, 1f), new Color(0f, 0f, 0.04f, 1f));

            MakeLabel(_skillConfirmPanel.transform, "ConfirmTitle", "キャラクター確認",
                new Vector2(0, 492), new Vector2(700, 46), 28f, new Color(1f, 0.88f, 0.3f));

            // 中央仕切り
            MakeOutline(_skillConfirmPanel.transform, "Divider",
                new Vector2(0, 0), new Vector2(3, 900), new Color(1f, 1f, 1f, 0.1f));

            // 1P 列（左）
            float lx = -440f;
            MakeLabel(_skillConfirmPanel.transform, "P1Badge", "1P",
                new Vector2(lx, 438), new Vector2(100, 44), 30f, new Color(0.4f, 0.75f, 1f))
                .fontStyle = FontStyles.Bold;
            MakeOutline(_skillConfirmPanel.transform, "P1Line",
                new Vector2(lx, 404), new Vector2(360, 2), new Color(0.4f, 0.75f, 1f));

            _confirmP1Name = MakeLabel(_skillConfirmPanel.transform, "P1Name", "---",
                new Vector2(lx, 374), new Vector2(390, 32), 20f, new Color(1f, 1f, 1f));
            _confirmP1Name.textWrappingMode = TMPro.TextWrappingModes.NoWrap;

            _confirmP1Desc = MakeLabel(_skillConfirmPanel.transform, "P1Desc", "",
                new Vector2(lx, 338), new Vector2(390, 40), 12f, new Color(0.82f, 0.88f, 1f));
            _confirmP1Desc.textWrappingMode = TMPro.TextWrappingModes.Normal;

            _confirmP1Image = MakePortrait(_skillConfirmPanel.transform, "P1ConfirmImage",
                new Vector2(lx - 145f, 205f), new Vector2(175f, 210f));
            _confirmP1Stats = MakeLabel(_skillConfirmPanel.transform, "P1Stats", "",
                new Vector2(lx + 110f, 210f), new Vector2(240f, 168f), 12f, new Color(0.9f, 0.95f, 1f));
            _confirmP1Stats.alignment = TextAlignmentOptions.TopLeft;

            string[] slotLabels = { "基本技A", "基本技B", "基本技C", "スマッシュ" };
            float[] skillY      = { 70f, -28f, -126f, -224f };

            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                MakeLabel(_skillConfirmPanel.transform, $"P1SlotLabel{i}", slotLabels[i],
                    new Vector2(lx - 100f, skillY[i] + 20f), new Vector2(100f, 28f), 12f,
                    new Color(0.5f, 0.7f, 1f));
                _confirmP1SkillTexts[i] = MakeLabel(_skillConfirmPanel.transform, $"P1Skill{i}", "---",
                    new Vector2(lx + 54f, skillY[i]), new Vector2(360f, 72f), 12f, Color.white);
                _confirmP1SkillTexts[i].alignment = TextAlignmentOptions.TopLeft;
                _confirmP1SkillTexts[i].textWrappingMode = TMPro.TextWrappingModes.Normal;
            }

            // 2P 列（右）
            float rx = 440f;
            MakeLabel(_skillConfirmPanel.transform, "P2Badge", "2P",
                new Vector2(rx, 438), new Vector2(100, 44), 30f, new Color(1f, 0.55f, 0.35f))
                .fontStyle = FontStyles.Bold;
            MakeOutline(_skillConfirmPanel.transform, "P2Line",
                new Vector2(rx, 404), new Vector2(360, 2), new Color(1f, 0.55f, 0.35f));

            _confirmP2Name = MakeLabel(_skillConfirmPanel.transform, "P2Name", "---",
                new Vector2(rx, 374), new Vector2(390, 32), 20f, Color.white);
            _confirmP2Name.textWrappingMode = TMPro.TextWrappingModes.NoWrap;

            _confirmP2Desc = MakeLabel(_skillConfirmPanel.transform, "P2Desc", "",
                new Vector2(rx, 338), new Vector2(390, 40), 12f, new Color(1f, 0.88f, 0.82f));
            _confirmP2Desc.textWrappingMode = TMPro.TextWrappingModes.Normal;

            _confirmP2Image = MakePortrait(_skillConfirmPanel.transform, "P2ConfirmImage",
                new Vector2(rx - 145f, 205f), new Vector2(175f, 210f));
            _confirmP2Stats = MakeLabel(_skillConfirmPanel.transform, "P2Stats", "",
                new Vector2(rx + 110f, 210f), new Vector2(240f, 168f), 12f, new Color(1f, 0.92f, 0.88f));
            _confirmP2Stats.alignment = TextAlignmentOptions.TopLeft;

            for (int i = 0; i < 4; i++)
            {
                MakeLabel(_skillConfirmPanel.transform, $"P2SlotLabel{i}", slotLabels[i],
                    new Vector2(rx - 100f, skillY[i] + 20f), new Vector2(100f, 28f), 12f,
                    new Color(1f, 0.7f, 0.5f));
                _confirmP2SkillTexts[i] = MakeLabel(_skillConfirmPanel.transform, $"P2Skill{i}", "---",
                    new Vector2(rx + 54f, skillY[i]), new Vector2(360f, 72f), 12f, Color.white);
                _confirmP2SkillTexts[i].alignment = TextAlignmentOptions.TopLeft;
                _confirmP2SkillTexts[i].textWrappingMode = TMPro.TextWrappingModes.Normal;
            }

            // フッター
            var battleBtn = MakeButton(_skillConfirmPanel.transform, "BattleBtn", "バトル開始",
                new Vector2(0, -428), new Vector2(330, 58), OnSkillConfirmBattlePressed,
                new Color(0.1f, 0.55f, 0.1f, 1f));
            SetButtonLabelStyle(battleBtn, 24f, FontStyles.Bold, Color.white);

            MakeLabel(_skillConfirmPanel.transform, "BattleHint",
                "スペースキー: バトル開始　Esc: 戻る",
                new Vector2(0, -475), new Vector2(600, 28), 13f, new Color(0.72f, 0.8f, 1f));
        }

        void RefreshSkillConfirmContent()
        {
            void FillPlayer(CharacterData d, TextMeshProUGUI nameT, TextMeshProUGUI descT,
                TextMeshProUGUI statsT, Image image, TextMeshProUGUI[] skillTs)
            {
                if (d == null) return;
                if (nameT != null) nameT.text = d.characterName;
                if (descT != null) descT.text = d.visualDescription;
                if (statsT != null) statsT.text = BuildStatsText(d);
                if (image != null)
                {
                    EnsurePreviewSprite(d);
                    image.sprite = d.characterSprite;
                    image.enabled = image.sprite != null;
                }
                for (int i = 0; i < 4 && i < skillTs.Length; i++)
                {
                    if (skillTs[i] == null) continue;
                    var s = d.skills[i];
                    skillTs[i].text = s != null
                        ? $"{s.skill_name}\n威力 {s.parameters.damage:F0} / リーチ {s.parameters.range:F1} / 発生 {s.parameters.startup:F2}s\n{s.description}"
                        : "---";
                }
            }

            FillPlayer(_pendingData1, _confirmP1Name, _confirmP1Desc, _confirmP1Stats, _confirmP1Image, _confirmP1SkillTexts);
            FillPlayer(_pendingData2, _confirmP2Name, _confirmP2Desc, _confirmP2Stats, _confirmP2Image, _confirmP2SkillTexts);
        }

        void ChangePreset(ref int idx, int delta, TextMeshProUGUI label)
        {
            if (_presets == null || _presets.Count == 0) return;
            idx = (idx + delta + _presets.Count) % _presets.Count;
            if (label != null) label.text = GetPresetName(idx);
            UpdateCategoryLabels();
            RefreshCharacterPreview();
            RebuildIconGrids();
        }

        void ChangeIconPage(bool isP1, int delta)
        {
            if (_presets == null || _presets.Count == 0) return;
            int maxPage = Mathf.Max(0, (_presets.Count - 1) / 12);
            if (isP1) _p1IconPage = Mathf.Clamp(_p1IconPage + delta, 0, maxPage);
            else _p2IconPage = Mathf.Clamp(_p2IconPage + delta, 0, maxPage);
            RebuildIconGrids();
        }

        void SelectPreset(bool isP1, int idx)
        {
            if (_presets == null || idx < 0 || idx >= _presets.Count) return;
            if (isP1)
            {
                _p1PresetIdx = idx;
                if (_p1PresetLabel != null) _p1PresetLabel.text = GetPresetName(idx);
            }
            else
            {
                _p2PresetIdx = idx;
                if (_p2PresetLabel != null) _p2PresetLabel.text = GetPresetName(idx);
            }

            UpdateCategoryLabels();
            RefreshCharacterPreview();
            RebuildIconGrids();
        }

        void RefreshCharacterPreview()
        {
            SetPreview(_p1PreviewImage, _p1PresetIdx);
            SetPreview(_p2PreviewImage, _p2PresetIdx);
            SetDetail(_p1DetailText, _p1PresetIdx);
            SetDetail(_p2DetailText, _p2PresetIdx);
        }

        void SetPreview(Image image, int idx)
        {
            if (image == null || _presets == null || idx < 0 || idx >= _presets.Count) return;

            var data = _presets[idx];
            if (data.characterSprite == null && !string.IsNullOrEmpty(data.spritePath))
                data.characterSprite = SpriteLoader.LoadWithWhiteBgRemoved(data.spritePath);

            image.sprite = data.characterSprite;
            image.enabled = image.sprite != null;
        }

        void SetDetail(TextMeshProUGUI label, int idx)
        {
            if (label == null || _presets == null || idx < 0 || idx >= _presets.Count) return;
            label.text = BuildCharacterDetail(_presets[idx]);
        }

        string BuildCharacterDetail(CharacterData data)
        {
            if (data == null) return "---";

            var s = data.stats ?? new CharacterStats();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"地上 {s.groundMoveSpeed:F1}  空中 {s.airMoveSpeed:F1}");
            sb.AppendLine($"ジャンプ {s.jumpForce:F1}  2段 {s.airJumpHeightMultiplier:F2}  歩き {s.walkSpeedRatio:F2}");
            sb.AppendLine($"ガード {s.guardDurability:F0}  軽さ {s.lightness:F2}");
            sb.AppendLine($"回避 地上{s.groundDodgeDistance:F1}  空中{s.airDodgeDistance:F1}");
            for (int i = 0; i < data.skills.Length; i++)
            {
                var skill = data.skills[i];
                if (skill == null) continue;
                string slot = i switch { 0 => "A", 1 => "B", 2 => "C", 3 => "S", _ => "?" };
                sb.AppendLine($"{slot}: {skill.skill_name}");
                sb.AppendLine($"  威力{skill.parameters.damage:F0}  リーチ{skill.parameters.range:F1}");
            }
            return sb.ToString();
        }

        string BuildStatsText(CharacterData data)
        {
            var s = data?.stats ?? new CharacterStats();
            return $"地上移動 {s.groundMoveSpeed:F1}\n" +
                   $"空中移動 {s.airMoveSpeed:F1}\n" +
                   $"ジャンプ {s.jumpForce:F1}\n" +
                   $"2段ジャンプ倍率 {s.airJumpHeightMultiplier:F2}\n" +
                   $"歩き速度倍率 {s.walkSpeedRatio:F2}\n" +
                   $"ガード耐久 {s.guardDurability:F0}\n" +
                   $"軽さ {s.lightness:F2} / 重さ {s.weight:F2}\n" +
                   $"回避距離 地上 {s.groundDodgeDistance:F1} / 空中 {s.airDodgeDistance:F1}";
        }

        void RebuildIconGrids()
        {
            RebuildIconGrid(_p1IconGrid, true);
            RebuildIconGrid(_p2IconGrid, false);
        }

        void RebuildIconGrid(Transform grid, bool isP1)
        {
            if (grid == null || _presets == null) return;

            for (int i = grid.childCount - 1; i >= 0; i--)
                Destroy(grid.GetChild(i).gameObject);

            int selected = isP1 ? _p1PresetIdx : _p2PresetIdx;
            int page = isP1 ? _p1IconPage : _p2IconPage;
            int maxPage = Mathf.Max(0, (_presets.Count - 1) / 12);
            page = Mathf.Clamp(page, 0, maxPage);
            if (isP1) _p1IconPage = page;
            else _p2IconPage = page;
            int start = page * 12;
            int end = Mathf.Min(_presets.Count, start + 12);
            var pageLabel = isP1 ? _p1PageLabel : _p2PageLabel;
            if (pageLabel != null) pageLabel.text = $"{page + 1}/{maxPage + 1}";

            for (int i = start; i < end; i++)
            {
                int idx = i;
                var data = _presets[i];
                EnsurePreviewSprite(data);
                bool isSelected = idx == selected;
                Color bg = isSelected
                    ? (isP1 ? new Color(0.2f, 0.55f, 1f, 1f) : new Color(1f, 0.35f, 0.2f, 1f))
                    : new Color(0.08f, 0.09f, 0.13f, 1f);
                MakeIconButton(grid, $"Icon_{idx}", data.characterSprite, idx + 1, () => SelectPreset(isP1, idx), bg);
            }
        }

        void EnsurePreviewSprite(CharacterData data)
        {
            if (data == null) return;
            if (data.characterSprite == null && !string.IsNullOrEmpty(data.spritePath))
                data.characterSprite = SpriteLoader.LoadWithWhiteBgRemoved(data.spritePath);
        }

        string GetPresetName(int idx)
        {
            if (_presets == null || idx < 0 || idx >= _presets.Count) return "---";
            return _presets[idx].characterName;
        }

        void OnStartPressed()
        {
            if (BattleManager.Instance == null) return;
            if (_presets == null || _presets.Count == 0) return;

            var data1 = PromptCharacterFactory.Clone(GetPreset(true));
            var data2 = PromptCharacterFactory.Clone(GetPreset(false));
            EnsureSpriteSet(data1);
            EnsureSpriteSet(data2);
            _panel.SetActive(false);
            BattleManager.Instance.StartCountdown(data1, data2);
        }

        void OnGeneratePressed()
        {
            if (BattleManager.Instance == null) return;
            if (_presets == null || _presets.Count == 0) return;

            bool hasP1Input = HasCharacterInput(true);
            bool hasP2Input = HasCharacterInput(false);
            if (!hasP1Input && !hasP2Input) return;

            var preset1 = PromptCharacterFactory.Clone(GetPreset(true));
            var preset2 = PromptCharacterFactory.Clone(GetPreset(false));
            EnsureSpriteSet(preset1);
            EnsureSpriteSet(preset2);
            _generationSetupPanel?.SetActive(false);
            ShowGeneratingPanel();
            _generationCoroutine = StartCoroutine(GenerateBothChars(preset1, preset2, hasP1Input, hasP2Input));
        }

        void OnSkillConfirmBattlePressed()
        {
            if (BattleManager.Instance == null || _pendingData1 == null || _pendingData2 == null) return;
            if (_generationTrainingActive)
            {
                BattleManager.Instance.ReturnToSetup();
                _generationTrainingActive = false;
            }
            _panel?.SetActive(false);
            _trainingPanel?.SetActive(false);
            _skillConfirmPanel?.SetActive(false);
            BattleManager.Instance.StartCountdown(_pendingData1, _pendingData2);
        }

        void CancelGeneration()
        {
            if (_generationCoroutine != null)
            {
                StopCoroutine(_generationCoroutine);
                _generationCoroutine = null;
            }
            _generationTrainingActive = false;
            _generatingPanel?.SetActive(false);
            ShowPanel();
        }

        IEnumerator GenerateBothChars(CharacterData preset1, CharacterData preset2,
            bool genP1, bool genP2)
        {
            _pendingData1 = null;
            _pendingData2 = null;
            string errorMsg = null;

            if (genP1)
            {
                UpdateGeneratingStatus("1P キャラクターを生成中...");
                string name1 = _p1NameInput?.text ?? "";
                string feat1 = _p1FeatureInput?.text ?? "";
                bool done = false;
                AICharacterClient.Generate(this, name1, feat1,
                    data =>
                    {
                        _pendingData1 = data;
                        CharacterSaveManager.Save(data);
                        // 即座にプリセットリストへ追加し選択状態にする
                        if (!_presets.Contains(data)) _presets.Add(data);
                        _p1PresetIdx = _presets.Count - 1;
                        done = true;
                    },
                    err  => { errorMsg = err; done = true; });
                yield return new WaitUntil(() => done);
            }
            else _pendingData1 = preset1;

            if (genP2)
            {
                UpdateGeneratingStatus("2P キャラクターを生成中...");
                string name2 = _p2NameInput?.text ?? "";
                string feat2 = _p2FeatureInput?.text ?? "";
                bool done = false;
                AICharacterClient.Generate(this, name2, feat2,
                    data =>
                    {
                        _pendingData2 = data;
                        CharacterSaveManager.Save(data);
                        if (!_presets.Contains(data)) _presets.Add(data);
                        _p2PresetIdx = _presets.Count - 1;
                        done = true;
                    },
                    err  => { if (errorMsg == null) errorMsg = err; done = true; });
                yield return new WaitUntil(() => done);
            }
            else _pendingData2 = preset2;

            // 生成失敗時はローカル生成で代替
            if (_pendingData1 == null)
            {
                UpdateGeneratingStatus("生成に失敗しました。ローカル生成で代替します...");
                _pendingData1 = PromptCharacterFactory.Create(
                    _p1NameInput?.text, _p1FeatureInput?.text, preset1);
                yield return new WaitForSeconds(0.8f);
            }
            if (_pendingData2 == null)
            {
                _pendingData2 = PromptCharacterFactory.Create(
                    _p2NameInput?.text, _p2FeatureInput?.text, preset2);
            }

            // 画像生成は新規生成した側だけ行う。既存キャラ側は選択中スプライトをそのまま使う。
            if ((genP1 || genP2) && !DebugSettings.SkipImageGeneration)
            {
                UpdateGeneratingStatus("キャラクター画像を生成中...");
                yield return GenerateImages(_pendingData1, _pendingData2, genP1, genP2);
            }
            else if (DebugSettings.SkipImageGeneration)
            {
                UpdateGeneratingStatus("[デバッグ] 画像生成をスキップしました");
            }

            _generatingPanel?.SetActive(false);
            _generationCoroutine = null;
            if (_generationTrainingActive)
            {
                BattleManager.Instance?.ReturnToSetup();
                _generationTrainingActive = false;
            }
            ShowSkillConfirmPanel();
        }

        IEnumerator GenerateImages(CharacterData data1, CharacterData data2, bool generateP1, bool generateP2)
        {
            // P1 → P2 の順に直列生成（同時リクエストによるレート制限エラーを防ぐ）
            if (generateP1 && data1 != null && !string.IsNullOrEmpty(data1.visualPrompt))
            {
                bool img1Done = false;
                AIImageClient.GenerateSpriteSet(this, data1,
                    msg => UpdateGeneratingStatus("1P " + msg),
                    sprites =>
                    {
                        data1.spriteSet = sprites;
                        data1.characterSprite = sprites.Get(CharacterSpriteId.Idle1);
                        img1Done = true;
                    },
                    err => { Debug.LogWarning("[AIImage] 1P: " + err); img1Done = true; },
                    saveDir: data1.spriteDir);
                yield return new WaitUntil(() => img1Done);
            }

            if (generateP2 && data2 != null && !string.IsNullOrEmpty(data2.visualPrompt))
            {
                bool img2Done = false;
                AIImageClient.GenerateSpriteSet(this, data2,
                    msg => UpdateGeneratingStatus("2P " + msg),
                    sprites =>
                    {
                        data2.spriteSet = sprites;
                        data2.characterSprite = sprites.Get(CharacterSpriteId.Idle1);
                        img2Done = true;
                    },
                    err => { Debug.LogWarning("[AIImage] 2P: " + err); img2Done = true; },
                    saveDir: data2.spriteDir);
                yield return new WaitUntil(() => img2Done);
            }
        }

        // 保存済みスプライトがある場合はフルロードする（バトル開始直前に呼ぶ）
        static void EnsureSpriteSet(CharacterData data)
        {
            if (data == null) return;
            if (HasPoseAndEffectSprites(data.spriteSet)) return;
            if (string.IsNullOrEmpty(data.spriteDir)) return;

            var loaded = CharacterSaveManager.LoadSpriteSet(data.spriteDir);
            if (loaded == null) return;

            data.spriteSet = loaded;
            if (data.characterSprite == null)
                data.characterSprite = loaded.Get(CharacterSpriteId.Idle1);
        }

        static bool HasPoseAndEffectSprites(CharacterSpriteSet spriteSet)
        {
            if (spriteSet?.sprites == null) return false;
            for (int i = 1; i < spriteSet.sprites.Length; i++)
            {
                if (spriteSet.sprites[i] != null) return true;
            }
            return false;
        }

        CharacterData GetPreset(bool isP1)
        {
            int idx = isP1 ? _p1PresetIdx : _p2PresetIdx;
            return _presets != null && idx >= 0 && idx < _presets.Count ? _presets[idx] : null;
        }

        void UpdateGeneratingStatus(string msg)
        {
            if (_generatingStatusText != null) _generatingStatusText.text = msg;
        }

        void OnTrainingPressed()
        {
            if (BattleManager.Instance == null) return;
            if (_presets == null || _presets.Count == 0) return;

            int p2Idx = _presets.Count > 1 ? _p2PresetIdx : _p1PresetIdx;
            var data1 = PromptCharacterFactory.Clone(GetPreset(true));
            var data2 = PromptCharacterFactory.Clone(_presets[p2Idx]);
            EnsureSpriteSet(data1);
            EnsureSpriteSet(data2);

            _panel.SetActive(false);
            BattleManager.Instance.StartTraining(data1, data2);
        }

        void StartTrainingDuringGeneration()
        {
            if (_generationTrainingActive) return;
            if (BattleManager.Instance == null || _presets == null || _presets.Count == 0) return;
            _generationTrainingActive = true;
            _generatingPanel?.SetActive(false);

            int p2Idx = _presets.Count > 1 ? _p2PresetIdx : _p1PresetIdx;
            var data1 = PromptCharacterFactory.Clone(GetPreset(true));
            var data2 = PromptCharacterFactory.Clone(_presets[p2Idx]);
            EnsureSpriteSet(data1);
            EnsureSpriteSet(data2);
            BattleManager.Instance.StartTraining(data1, data2);
        }

        void ReturnToGeneratingFromTraining()
        {
            BattleManager.Instance?.ReturnToSetup();
            if (_panel != null) _panel.SetActive(false);
            if (_trainingPanel != null) _trainingPanel.SetActive(false);
            if (_generatingPanel != null) _generatingPanel.SetActive(true);
        }

        void ShowPanel()
        {
            RefreshPresets();
            if (_trainingPanel != null) _trainingPanel.SetActive(false);
            if (_panel != null) _panel.SetActive(true);
            if (_titlePanel != null) _titlePanel.SetActive(false);
            if (_generationSetupPanel != null) _generationSetupPanel.SetActive(false);
            _waitForMenuInputRelease = true;
        }

        // バトルから戻るたびにプリセットを再読み込みし、直前生成キャラを自動選択する
        void RefreshPresets()
        {
            string p1Name = GetPresetName(_p1PresetIdx);
            string p2Name = GetPresetName(_p2PresetIdx);

            var builtIn = PresetCharacterLoader.LoadAll();
            _builtInPresetCount = builtIn.Count;
            _presets = new List<CharacterData>(builtIn);
            _presets.AddRange(CharacterSaveManager.LoadAll());

            int maxIdx = Mathf.Max(0, _presets.Count - 1);

            int f1 = p1Name != "---" ? _presets.FindIndex(c => c.characterName == p1Name) : -1;
            _p1PresetIdx = f1 >= 0 ? f1 : Mathf.Clamp(_p1PresetIdx, 0, maxIdx);

            int f2 = p2Name != "---" ? _presets.FindIndex(c => c.characterName == p2Name) : -1;
            _p2PresetIdx = f2 >= 0 ? f2 : Mathf.Clamp(_p2PresetIdx, 0, maxIdx);

            if (_p1PresetLabel != null) _p1PresetLabel.text = GetPresetName(_p1PresetIdx);
            if (_p2PresetLabel != null) _p2PresetLabel.text = GetPresetName(_p2PresetIdx);
            UpdateCategoryLabels();
            RebuildIconGrids();
            RefreshCharacterPreview();
        }

        void UpdateCategoryLabels()
        {
            if (_p1CategoryLabel != null)
            {
                bool isGenerated1 = _p1PresetIdx >= _builtInPresetCount;
                _p1CategoryLabel.text  = isGenerated1 ? "生成済み" : "初期キャラ";
                _p1CategoryLabel.color = isGenerated1
                    ? new Color(1f, 0.85f, 0.2f)       // 金色：生成済み
                    : new Color(0.65f, 0.75f, 0.9f);    // 薄青：初期キャラ
            }
            if (_p2CategoryLabel != null)
            {
                bool isGenerated2 = _p2PresetIdx >= _builtInPresetCount;
                _p2CategoryLabel.text  = isGenerated2 ? "生成済み" : "初期キャラ";
                _p2CategoryLabel.color = isGenerated2
                    ? new Color(1f, 0.85f, 0.2f)
                    : new Color(0.65f, 0.75f, 0.9f);
            }

            if (_p1DeleteButton != null)
                _p1DeleteButton.gameObject.SetActive(_p1PresetIdx >= _builtInPresetCount);
            if (_p2DeleteButton != null)
                _p2DeleteButton.gameObject.SetActive(_p2PresetIdx >= _builtInPresetCount);
        }

        void ShowTrainingPanel()
        {
            if (_trainingControlsText != null)
                _trainingControlsText.text = BuildTrainingHelpText();
            if (_trainingPanel != null) _trainingPanel.SetActive(true);
        }

        void ShowTitlePanel()
        {
            if (_titlePanel != null) _titlePanel.SetActive(true);
            if (_panel != null) _panel.SetActive(false);
            if (_generationSetupPanel != null) _generationSetupPanel.SetActive(false);
            if (_trainingPanel != null) _trainingPanel.SetActive(false);
        }

        void ShowCharacterSelect()
        {
            if (_titlePanel != null) _titlePanel.SetActive(false);
            if (_panel != null) _panel.SetActive(true);
            if (_generationSetupPanel != null) _generationSetupPanel.SetActive(false);
            if (_trainingPanel != null) _trainingPanel.SetActive(false);
            if (_generatingPanel != null) _generatingPanel.SetActive(false);
            if (_skillConfirmPanel != null) _skillConfirmPanel.SetActive(false);
        }

        void ShowGenerationSetupPanel()
        {
            if (_titlePanel != null) _titlePanel.SetActive(false);
            if (_panel != null) _panel.SetActive(false);
            if (_generationSetupPanel != null) _generationSetupPanel.SetActive(true);
            if (_trainingPanel != null) _trainingPanel.SetActive(false);
            if (_generatingPanel != null) _generatingPanel.SetActive(false);
            if (_skillConfirmPanel != null) _skillConfirmPanel.SetActive(false);
        }

        void ShowGeneratingPanel()
        {
            if (_generatingPanel != null) _generatingPanel.SetActive(true);
            if (_generatingStatusText != null) _generatingStatusText.text = "生成を開始しています...";
        }

        void ShowSkillConfirmPanel()
        {
            RefreshSkillConfirmContent();
            if (_trainingPanel != null) _trainingPanel.SetActive(false);
            if (_skillConfirmPanel != null) _skillConfirmPanel.SetActive(true);
        }

        string BuildTrainingHelpText()
        {
            string esc = _generationTrainingActive && _generationCoroutine != null
                ? "Escキー: 生成進行画面に戻る"
                : "Escキー: キャラ選択に戻る";
            return "1P: WASD 移動 / J K L 技 / A/Dはじき+J スマッシュ / G つかみ / 左Shift ガード・回避    " +
                   "2P: 矢印 移動 / テンキー2 3 1 技 / ←/→はじき+2 スマッシュ / 0 つかみ / 右Shift ガード・回避\n" +
                   "Pad: Y ジャンプ / LT・LB つかみ / RT・RB ガード・回避 / B A X 技 / はじき+B スマッシュ    " +
                   $"{esc}    Rキー: 位置・HP・技状態をリセット";
        }

        void DeleteSelectedCharacter(bool isP1)
        {
            if (_presets == null) return;
            int idx = isP1 ? _p1PresetIdx : _p2PresetIdx;
            if (idx < _builtInPresetCount || idx < 0 || idx >= _presets.Count) return;

            var data = _presets[idx];
            if (!CharacterSaveManager.Delete(data)) return;

            _presets.RemoveAt(idx);
            int maxIdx = Mathf.Max(0, _presets.Count - 1);
            _p1PresetIdx = Mathf.Clamp(_p1PresetIdx >= idx ? _p1PresetIdx - 1 : _p1PresetIdx, 0, maxIdx);
            _p2PresetIdx = Mathf.Clamp(_p2PresetIdx >= idx ? _p2PresetIdx - 1 : _p2PresetIdx, 0, maxIdx);

            if (_p1PresetLabel != null) _p1PresetLabel.text = GetPresetName(_p1PresetIdx);
            if (_p2PresetLabel != null) _p2PresetLabel.text = GetPresetName(_p2PresetIdx);
            UpdateCategoryLabels();
            RebuildIconGrids();
            RefreshCharacterPreview();
        }

        void AnimateTitle()
        {
            float pulse = (Mathf.Sin(Time.unscaledTime * 2.2f) + 1f) * 0.5f;
            if (_titleTopGlow != null)
                _titleTopGlow.color = new Color(0.1f, 0.45f, 1f, Mathf.Lerp(0.16f, 0.30f, pulse));
            if (_titleBottomGlow != null)
                _titleBottomGlow.color = new Color(1f, 0.2f, 0.55f, Mathf.Lerp(0.12f, 0.24f, 1f - pulse));
            if (_titleMainRect != null)
                _titleMainRect.localScale = Vector3.one * Mathf.Lerp(0.99f, 1.015f, pulse);
            if (_startButtonRect != null)
                _startButtonRect.localScale = Vector3.one * Mathf.Lerp(1f, 1.035f, pulse);
        }

        CharacterData BuildCharacterData(bool isP1)
        {
            int idx = isP1 ? _p1PresetIdx : _p2PresetIdx;
            var fallback = _presets != null && idx >= 0 && idx < _presets.Count ? _presets[idx] : null;
            var nameInput = isP1 ? _p1NameInput : _p2NameInput;
            var featureInput = isP1 ? _p1FeatureInput : _p2FeatureInput;
            string characterName = nameInput != null ? nameInput.text : string.Empty;
            string features = featureInput != null ? featureInput.text : string.Empty;
            return PromptCharacterFactory.Create(characterName, features, fallback);
        }

        bool HasCharacterInput(bool isP1)
        {
            var nameInput = isP1 ? _p1NameInput : _p2NameInput;
            var featureInput = isP1 ? _p1FeatureInput : _p2FeatureInput;
            return (nameInput != null && !string.IsNullOrWhiteSpace(nameInput.text)) ||
                   (featureInput != null && !string.IsNullOrWhiteSpace(featureInput.text));
        }

        static bool IsEditingText()
        {
            if (EventSystem.current == null) return false;
            var selected = EventSystem.current.currentSelectedGameObject;
            return selected != null && selected.GetComponentInParent<TMP_InputField>() != null;
        }

        static bool WasMenuConfirmPressed()
        {
            var kb = Keyboard.current;
            if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
                return true;
            var gp = Gamepad.current;
            return gp != null && (gp.buttonSouth.wasPressedThisFrame || gp.startButton.wasPressedThisFrame);
        }

        static bool WasCancelPressed()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) return true;
            var gp = Gamepad.current;
            return gp != null && gp.buttonEast.wasPressedThisFrame;
        }

        static bool WasKeyboardCancelPressed()
        {
            var kb = Keyboard.current;
            return kb != null && kb.escapeKey.wasPressedThisFrame;
        }

        static bool WasTrainingPressed()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.tKey.wasPressedThisFrame) return true;
            var gp = Gamepad.current;
            return gp != null && gp.selectButton.wasPressedThisFrame;
        }

        static bool WasGeneratePressed()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.gKey.wasPressedThisFrame) return true;
            var gp = Gamepad.current;
            return gp != null && gp.buttonNorth.wasPressedThisFrame;
        }

        static bool WasResetPressed()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.rKey.wasPressedThisFrame) return true;
            var gp = Gamepad.current;
            return gp != null && gp.leftStickButton.wasPressedThisFrame;
        }

        static bool IsAscii(string value)
        {
            if (string.IsNullOrEmpty(value)) return true;
            for (int i = 0; i < value.Length; i++)
                if (value[i] > 127) return false;
            return true;
        }

        static void EnsureInputSystemUIInputModule()
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null) return;

            var go = eventSystem.gameObject;
            var standalone = go.GetComponent<StandaloneInputModule>();
            if (standalone != null)
            {
                standalone.enabled = false;
                Destroy(standalone);
            }

            if (go.GetComponent<InputSystemUIInputModule>() == null)
                go.AddComponent<InputSystemUIInputModule>();
        }

        // ── UIヘルパー ────────────────────────────────────────────

        static GameObject CreateUIObject(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.layer = parent.gameObject.layer;
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
            return go;
        }

        static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        static TextMeshProUGUI MakeLabel(Transform parent, string name, string text,
            Vector2 pos, Vector2 size, float fontSize, Color color)
        {
            var go = CreateUIObject(name, parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text      = text;
            tmp.fontSize  = fontSize;
            tmp.color     = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            UITheme.Apply(tmp);
            return tmp;
        }

        static TMP_InputField MakeInputField(Transform parent, string name, string placeholder,
            Vector2 pos, Vector2 size, bool multiline)
        {
            var go = CreateUIObject(name, parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.02f, 0.025f, 0.05f, 0.86f);

            var viewport = CreateUIObject("TextArea", go.transform);
            var vpRt = viewport.GetComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = new Vector2(12f, 6f);
            vpRt.offsetMax = new Vector2(-12f, -6f);
            viewport.AddComponent<RectMask2D>();

            var textGo = CreateUIObject("Text", viewport.transform);
            StretchFull(textGo.GetComponent<RectTransform>());
            var text = textGo.AddComponent<TextMeshProUGUI>();
            text.fontSize = multiline ? 15f : 18f;
            text.color = Color.white;
            text.alignment = multiline ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.Left;
            text.textWrappingMode = multiline ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            text.overflowMode = multiline ? TextOverflowModes.Overflow : TextOverflowModes.Ellipsis;
            UITheme.Apply(text);

            var placeholderGo = CreateUIObject("Placeholder", viewport.transform);
            StretchFull(placeholderGo.GetComponent<RectTransform>());
            var placeholderText = placeholderGo.AddComponent<TextMeshProUGUI>();
            placeholderText.text = placeholder;
            placeholderText.fontSize = text.fontSize;
            placeholderText.color = new Color(0.7f, 0.75f, 0.85f, 0.62f);
            placeholderText.alignment = text.alignment;
            placeholderText.textWrappingMode = text.textWrappingMode;
            placeholderText.overflowMode = TextOverflowModes.Ellipsis;
            UITheme.Apply(placeholderText);

            var input = go.AddComponent<TMP_InputField>();
            input.textViewport = vpRt;
            input.textComponent = text;
            input.placeholder = placeholderText;
            input.lineType = multiline
                ? TMP_InputField.LineType.MultiLineNewline
                : TMP_InputField.LineType.SingleLine;
            input.characterLimit = multiline ? 300 : 36;
            input.caretColor = Color.white;
            input.selectionColor = new Color(0.35f, 0.55f, 1f, 0.45f);
            return input;
        }

        static Button MakeButton(Transform parent, string name, string label,
            Vector2 pos, Vector2 size, System.Action onClick, Color? bgColor = null)
        {
            var go = CreateUIObject(name, parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            var img = go.AddComponent<Image>();
            img.color = bgColor ?? new Color(0.2f, 0.2f, 0.3f, 1f);

            var btn = go.AddComponent<Button>();
            var cols = btn.colors;
            cols.highlightedColor = new Color(0.4f, 0.4f, 0.6f);
            cols.pressedColor     = new Color(0.1f, 0.1f, 0.1f);
            btn.colors = cols;
            btn.onClick.AddListener(() =>
            {
                PromptFighters.Audio.GameAudioManager.Instance?.PlayMenu();
                onClick?.Invoke();
            });

            var textGo = CreateUIObject("Label", go.transform);
            StretchFull(textGo.GetComponent<RectTransform>());
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text      = label;
            tmp.fontSize  = 16;
            tmp.color     = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            UITheme.Apply(tmp);

            return btn;
        }

        static Button MakeIconButton(Transform parent, string name, Sprite sprite, int number,
            System.Action onClick, Color bgColor)
        {
            var go = CreateUIObject(name, parent);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(86f, 58f);

            var bg = go.AddComponent<Image>();
            bg.color = bgColor;

            var btn = go.AddComponent<Button>();
            var cols = btn.colors;
            cols.highlightedColor = new Color(0.55f, 0.65f, 0.85f);
            cols.pressedColor = new Color(0.08f, 0.08f, 0.1f);
            btn.colors = cols;
            btn.onClick.AddListener(() => onClick?.Invoke());

            var imageGo = CreateUIObject("Portrait", go.transform);
            var imgRt = imageGo.GetComponent<RectTransform>();
            imgRt.anchorMin = new Vector2(0f, 0f);
            imgRt.anchorMax = new Vector2(1f, 1f);
            imgRt.offsetMin = new Vector2(6f, 4f);
            imgRt.offsetMax = new Vector2(-6f, -4f);
            var img = imageGo.AddComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.color = sprite != null ? Color.white : new Color(0.35f, 0.38f, 0.45f);

            var badge = MakeLabel(go.transform, "No", number.ToString(),
                new Vector2(-31f, 19f), new Vector2(22f, 18f), 10f, Color.white);
            badge.fontStyle = FontStyles.Bold;
            return btn;
        }

        static Image MakePortrait(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            var frame = CreateUIObject(name + "Frame", parent);
            var frt = frame.GetComponent<RectTransform>();
            frt.anchoredPosition = pos;
            frt.sizeDelta = size;
            AddImage(frame, new Color(0.01f, 0.012f, 0.02f, 0.78f));

            var imageGo = CreateUIObject(name, frame.transform);
            var rt = imageGo.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(10f, 8f);
            rt.offsetMax = new Vector2(-10f, -8f);
            var img = imageGo.AddComponent<Image>();
            img.preserveAspect = true;
            img.raycastTarget = false;
            return img;
        }

        static Image MakePanel(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
        {
            var go = CreateUIObject(name, parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return AddImage(go, color);
        }

        static Image MakeOutline(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
        {
            return MakePanel(parent, name, pos, size, color);
        }

        static Image AddImage(GameObject go, Color color)
        {
            var img = go.GetComponent<Image>();
            if (img == null) img = go.AddComponent<Image>();
            img.color = color;
            return img;
        }

        static void SetButtonLabelStyle(Button button, float fontSize, FontStyles style, Color color)
        {
            if (button == null) return;
            var label = button.GetComponentInChildren<TextMeshProUGUI>();
            if (label == null) return;
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.color = color;
            UITheme.Apply(label);
        }

        static Sprite CreateGradientSprite(Color topLeft, Color topRight, Color bottomLeft, Color bottomRight)
        {
            const int width = 8;
            const int height = 8;
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;

            for (int y = 0; y < height; y++)
            {
                float ty = y / (height - 1f);
                Color left = Color.Lerp(bottomLeft, topLeft, ty);
                Color right = Color.Lerp(bottomRight, topRight, ty);
                for (int x = 0; x < width; x++)
                {
                    float tx = x / (width - 1f);
                    tex.SetPixel(x, y, Color.Lerp(left, right, tx));
                }
            }

            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f));
        }
    }
}
