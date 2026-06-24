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
    public partial class PreBattlePanel : MonoBehaviour
    {
        List<CharacterData> _presets;
        int _builtInPresetCount = 0; // プリセット（初期キャラ）の件数。以降が生成済みキャラ。
        int _p1PresetIdx = 0;
        int _p2PresetIdx = 1;

        // 共有ロスター（スマブラ風キャラ選択グリッド）
        const int RosterColumns = 8;
        const int RosterRows = 3;
        Transform _rosterGrid;
        int _rosterPage = 0;
        int _displayedPage = -1;
        TextMeshProUGUI _rosterPageLabel;
        readonly Dictionary<int, Image> _rosterCellBgs = new Dictionary<int, Image>();

        TextMeshProUGUI _p1GamepadLabel;
        TextMeshProUGUI _p2GamepadLabel;
        TextMeshProUGUI _p1PresetLabel;
        TextMeshProUGUI _p2PresetLabel;
        TextMeshProUGUI _p1DetailText;
        TextMeshProUGUI _p2DetailText;
        Image[] _p1StatFills;
        Image[] _p2StatFills;
        TextMeshProUGUI[] _p1StatValues;
        TextMeshProUGUI[] _p2StatValues;
        Image _p1PreviewImage;
        Image _p2PreviewImage;
        CharacterData _p1PreviewData;
        CharacterData _p2PreviewData;
        float _previewIdleTimer;
        int _previewIdleFrame;
        Button _p1DeleteButton;
        Button _p2DeleteButton;
        TMP_InputField _p1NameInput;
        TMP_InputField _p1FeatureInput;
        TMP_InputField _p2NameInput;
        TMP_InputField _p2FeatureInput;
        Button _p1ConceptButton;
        Button _p2ConceptButton;
        TextMeshProUGUI _p1ConceptStatus;
        TextMeshProUGUI _p2ConceptStatus;
        bool _p1ConceptBusy;
        bool _p2ConceptBusy;

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
        GameObject _deleteConfirmPanel;
        TextMeshProUGUI _deleteConfirmNameText;
        bool _deletePendingIsP1;
        Coroutine _generationCoroutine;
        bool _generationTrainingActive;
        TextMeshProUGUI _debugSkipImageLabel;

        Image _titleTopGlow;
        Image _titleBottomGlow;
        RectTransform _titleMainRect;
        RectTransform _startButtonRect;
        bool _waitForMenuInputRelease;

        // ゲームパッド左スティック駆動の自前カーソル（1P/2P 専用に2つ）
        const int CursorCount = 2; // 0=1P, 1=2P
        readonly GameObject[]    _gamepadCursor        = new GameObject[CursorCount];
        readonly RectTransform[] _gamepadCursorRect    = new RectTransform[CursorCount];
        readonly bool[]          _gamepadCursorVisible = new bool[CursorCount];
        readonly Vector2[]       _cursorScreenPos      = new Vector2[CursorCount];
        Canvas _cursorCanvas;
        RectTransform _cursorCanvasRect;
        const float CursorSpeed = 1.25f; // 画面高さ/秒（解像度に依存しない速度）

        // AI機能・ステージトグル
        Image _commentaryToggleBg;
        TextMeshProUGUI _commentaryToggleLabel;
        Image _angelToggleBg;
        TextMeshProUGUI _angelToggleLabel;
        Image _platformToggleBg;
        TextMeshProUGUI _platformToggleLabel;
        Image _cpuToggleBg;
        TextMeshProUGUI _cpuToggleLabel;
        Image _cpuSideToggleBg;
        TextMeshProUGUI _cpuSideToggleLabel;
        // バトルモード選択（1 vs 1 / 協力ボス討伐）
        Image _modeVersusBg;
        TextMeshProUGUI _modeVersusLabel;
        Image _modeCoopBg;
        TextMeshProUGUI _modeCoopLabel;
        GameObject _bossSelectorRoot;
        TextMeshProUGUI _bossPresetLabel;
        int _bossPresetIdx = 0;
        // 操作説明オーバーレイ
        GameObject _controlsPanel;

        static readonly Color ToggleOnColor  = PromptFighters.UI.UITheme.Gold;
        static readonly Color ToggleOffColor = new Color(0.14f, 0.15f, 0.19f, 1f);

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
            BuildDeleteConfirmPanel();
            BuildControlsPanel();
            EnsureVirtualCursor();
            UITheme.ApplyAllInScene();
            // ApplyAllInScene が全TMPの自動縮小・折り返しを既定へ戻すため、長いキャラ名が
            // はみ出さないよう選択名ラベルだけ設定を再適用する
            ConfigurePresetNameLabel(_p1PresetLabel);
            ConfigurePresetNameLabel(_p2PresetLabel);
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

        // ゲームパッド左スティックで動く自前カーソルを構築。
        // 位置はスクリーン座標で自己管理し（画面内にクランプ）、Aでカーソル位置を
        // 手動レイキャストしてUIをクリックする。物理Mouseと仮想Mouseの座標ズレを避ける。
        void EnsureVirtualCursor()
        {
            var canvas = GetComponentInParent<Canvas>();
            _cursorCanvas = canvas != null ? canvas.rootCanvas : null;
            Transform canvasT = _cursorCanvas != null ? _cursorCanvas.transform : transform;
            _cursorCanvasRect = canvasT as RectTransform;

            // 1P=青 / 2P=赤 の専用カーソルを2つ作る。
            BuildOneCursor(0, "GamepadCursorP1", PromptFighters.UI.UITheme.P1Neon, canvasT);
            BuildOneCursor(1, "GamepadCursorP2", PromptFighters.UI.UITheme.P2Neon, canvasT);
        }

        void BuildOneCursor(int idx, string name, Color outerColor, Transform canvasT)
        {
            var go = CreateUIObject(name, canvasT);
            var rt = go.GetComponent<RectTransform>();
            _gamepadCursor[idx]     = go;
            _gamepadCursorRect[idx] = rt;
            rt.sizeDelta = new Vector2(30f, 30f);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;

            // 菱形レティクル（外:プレイヤー色 / 内:ダーク / 中心:白ドット）
            var outer = go.AddComponent<Image>();
            outer.sprite = PromptFighters.UI.UITheme.VGradient; outer.type = Image.Type.Simple;
            outer.color = outerColor;
            outer.raycastTarget = false;
            rt.localRotation = Quaternion.Euler(0f, 0f, 45f);

            var inner = CreateUIObject("CursorInner", go.transform);
            var iRt = inner.GetComponent<RectTransform>();
            iRt.anchorMin = Vector2.zero; iRt.anchorMax = Vector2.one;
            iRt.offsetMin = new Vector2(5f, 5f); iRt.offsetMax = new Vector2(-5f, -5f);
            var innerImg = inner.AddComponent<Image>();
            innerImg.color = new Color(0.04f, 0.05f, 0.08f, 0.95f);
            innerImg.raycastTarget = false;

            var dot = CreateUIObject("CursorDot", go.transform);
            var dRt = dot.GetComponent<RectTransform>();
            dRt.anchorMin = dRt.anchorMax = new Vector2(0.5f, 0.5f);
            dRt.sizeDelta = new Vector2(8f, 8f);
            dRt.anchoredPosition = Vector2.zero;
            var dotImg = dot.AddComponent<Image>();
            dotImg.color = Color.white;
            dotImg.raycastTarget = false;

            // 初期位置は左右に少しずらして重ならないようにする。
            _cursorScreenPos[idx] = new Vector2(
                Screen.width * (idx == 0 ? 0.42f : 0.58f), Screen.height * 0.5f);
            ApplyCursorPosition(idx);
            SetGamepadCursorVisible(idx, false);
        }

        // 全カーソルを即時非表示にする（試合開始時など、Updateを待たず確実に消すため）。
        void HideGamepadCursors()
        {
            for (int i = 0; i < CursorCount; i++) SetGamepadCursorVisible(i, false);
        }

        void SetGamepadCursorVisible(int idx, bool visible)
        {
            var go = _gamepadCursor[idx];
            if (go == null || _gamepadCursorVisible[idx] == visible) return;
            _gamepadCursorVisible[idx] = visible;
            foreach (var g in go.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                g.enabled = visible;
        }

        // 各プレイヤーの左スティックで自分のカーソルを移動＋表示、A押下でクリック、物理マウス操作で非表示。
        void UpdateGamepadCursor()
        {
            // カーソルはタイトル/選択画面のUI操作専用。試合（カウントダウン/対戦/トレーニング）中は
            // 左スティックが移動入力になるため、カーソルを出さない。両パネルが閉じている間も同様。
            var bm = BattleManager.Instance;
            bool inMatch = bm != null && bm.Phase != BattlePhase.Setup;
            bool uiActive = !inMatch
                && ((_panel != null && _panel.activeSelf)
                    || (_titlePanel != null && _titlePanel.activeSelf));
            if (!uiActive)
            {
                for (int i = 0; i < CursorCount; i++) SetGamepadCursorVisible(i, false);
                return;
            }

            var pads = UnityEngine.InputSystem.Gamepad.all;
            var mouse = UnityEngine.InputSystem.Mouse.current;
            bool mouseMoved = mouse != null && mouse.delta.ReadValue().sqrMagnitude > 1f;

            const float deadZone = 0.2f;
            for (int i = 0; i < CursorCount; i++)
            {
                if (i >= pads.Count) { SetGamepadCursorVisible(i, false); continue; }
                var gp = pads[i];

                Vector2 stick = gp.leftStick.ReadValue();
                float mag = stick.magnitude;
                if (mag > deadZone)
                {
                    SetGamepadCursorVisible(i, true);
                    // デッドゾーン超過分を 0→1 に正規化して比例移動（素直な追従・ドリフトで動かない）。
                    Vector2 dir = stick.normalized * ((mag - deadZone) / (1f - deadZone));
                    float speedPx = CursorSpeed * Screen.height; // 解像度非依存
                    _cursorScreenPos[i] += dir * (speedPx * Time.unscaledDeltaTime);
                    _cursorScreenPos[i].x = Mathf.Clamp(_cursorScreenPos[i].x, 0f, Screen.width);
                    _cursorScreenPos[i].y = Mathf.Clamp(_cursorScreenPos[i].y, 0f, Screen.height);
                    ApplyCursorPosition(i);
                }

                if (mouseMoved) SetGamepadCursorVisible(i, false);

                if (_gamepadCursorVisible[i] && gp.buttonSouth.wasPressedThisFrame)
                    DoGamepadCursorClick(i);
            }
        }

        // スクリーン座標をCanvasローカル座標へ変換してカーソルを配置（CanvasScaler対応）。
        void ApplyCursorPosition(int idx)
        {
            if (_gamepadCursorRect[idx] == null || _cursorCanvasRect == null) return;
            var cam = (_cursorCanvas != null && _cursorCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
                ? _cursorCanvas.worldCamera : null;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_cursorCanvasRect, _cursorScreenPos[idx], cam, out var local))
                _gamepadCursorRect[idx].anchoredPosition = local;
        }

        // カーソル位置でUIをレイキャストし、ヒットした要素にクリックを送る。
        void DoGamepadCursorClick(int idx)
        {
            var es = EventSystem.current;
            if (es == null) return;
            var ped = new PointerEventData(es) { position = _cursorScreenPos[idx], button = PointerEventData.InputButton.Left };
            var results = new List<RaycastResult>();
            es.RaycastAll(ped, results);
            if (results.Count == 0) return;

            var target = results[0].gameObject;

            // ゲームパッド2台接続時：ロスターセルのクリックは左右半分ではなく、
            // 「クリックしたカーソルのプレイヤー」へ割り当てる（idx 0=1P / 1=2P）。
            if (UnityEngine.InputSystem.Gamepad.all.Count >= 2)
            {
                var cellRef = target.GetComponentInParent<RosterCellRef>();
                if (cellRef != null) { SelectPreset(idx == 0, cellRef.index); return; }
            }

            var handler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(target);
            if (handler == null) return;
            ped.pointerPressRaycast = ped.pointerCurrentRaycast = results[0];
            ExecuteEvents.Execute(handler, ped, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(handler, ped, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(handler, ped, ExecuteEvents.pointerClickHandler);
        }

        void Update()
        {
            UpdateGamepadCursor();

            // 削除確認モーダルが開いている間は他の入力を遮断
            if (_deleteConfirmPanel != null && _deleteConfirmPanel.activeSelf)
            {
                if (WasKeyboardConfirmPressed()) ConfirmDeleteCharacter();
                else if (WasKeyboardCancelPressed()) HideDeleteConfirm();
                return;
            }

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
                AnimatePreviewIdle();
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (_waitForMenuInputRelease)
                {
                    if (kb == null ||
                        (!kb.spaceKey.isPressed && !kb.enterKey.isPressed && !kb.tKey.isPressed))
                        _waitForMenuInputRelease = false;
                    return;
                }

                if (IsEditingText()) return;

                HandleRosterCursorInput();

                // ゲームパッドAはカーソルのクリックに使うため、ここでは誤発進防止でキーボードのみ
                if (WasKeyboardConfirmPressed()) OnStartPressed();
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
                    OnSkillConfirmDonePressed();
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
                new Color(0.05f, 0.06f, 0.09f, 1f),
                new Color(0.06f, 0.07f, 0.11f, 1f),
                new Color(0.012f, 0.014f, 0.022f, 1f),
                new Color(0.0f, 0.0f, 0.012f, 1f));
            bg.type = Image.Type.Simple;

            var cg = _titlePanel.AddComponent<CanvasGroup>();
            cg.interactable = true;
            cg.blocksRaycasts = true;

            // ── 斜めのネオンサイドストライプ（1P青 / 2P赤） ──
            MakeSlantBar(_titlePanel.transform, "P1Stripe",
                new Vector2(-820f, 0f), new Vector2(150f, 1100f),
                new Color(PromptFighters.UI.UITheme.P1Neon.r, PromptFighters.UI.UITheme.P1Neon.g, PromptFighters.UI.UITheme.P1Neon.b, 0.10f), 110f);
            MakeSlantBar(_titlePanel.transform, "P1StripeThin",
                new Vector2(-690f, 0f), new Vector2(26f, 1100f),
                new Color(PromptFighters.UI.UITheme.P1Neon.r, PromptFighters.UI.UITheme.P1Neon.g, PromptFighters.UI.UITheme.P1Neon.b, 0.22f), 110f);
            MakeSlantBar(_titlePanel.transform, "P2Stripe",
                new Vector2(820f, 0f), new Vector2(150f, 1100f),
                new Color(PromptFighters.UI.UITheme.P2Neon.r, PromptFighters.UI.UITheme.P2Neon.g, PromptFighters.UI.UITheme.P2Neon.b, 0.10f), 110f);
            MakeSlantBar(_titlePanel.transform, "P2StripeThin",
                new Vector2(690f, 0f), new Vector2(26f, 1100f),
                new Color(PromptFighters.UI.UITheme.P2Neon.r, PromptFighters.UI.UITheme.P2Neon.g, PromptFighters.UI.UITheme.P2Neon.b, 0.22f), 110f);

            _titleTopGlow = MakePanel(_titlePanel.transform, "TopGlow",
                new Vector2(0, 245), new Vector2(1000, 130),
                new Color(PromptFighters.UI.UITheme.P1Neon.r, PromptFighters.UI.UITheme.P1Neon.g, PromptFighters.UI.UITheme.P1Neon.b, 0.16f));
            _titleBottomGlow = MakePanel(_titlePanel.transform, "BottomGlow",
                new Vector2(0, -245), new Vector2(1000, 150),
                new Color(PromptFighters.UI.UITheme.P2Neon.r, PromptFighters.UI.UITheme.P2Neon.g, PromptFighters.UI.UITheme.P2Neon.b, 0.14f));

            // ── センターフレーム（スチール地＋ゴールドの斜めエッジ） ──
            var frame = MakePanel(_titlePanel.transform, "CenterFrame",
                new Vector2(0, 4), new Vector2(820, 400), PromptFighters.UI.UITheme.Steel);
            frame.sprite = PromptFighters.UI.UITheme.VGradient;
            frame.type = Image.Type.Simple;
            MakeSlantBar(_titlePanel.transform, "FrameTop", new Vector2(0, 206), new Vector2(820, 5),
                PromptFighters.UI.UITheme.Gold, 22f);
            MakeSlantBar(_titlePanel.transform, "FrameBottom", new Vector2(0, -198), new Vector2(820, 5),
                PromptFighters.UI.UITheme.Gold, 22f);

            // ── タイトルロゴ背後のゴールドスラッシュ ──
            MakeSlantBar(_titlePanel.transform, "TitleSlash", new Vector2(0, 70f), new Vector2(700f, 96f),
                new Color(PromptFighters.UI.UITheme.GoldDim.r, PromptFighters.UI.UITheme.GoldDim.g, PromptFighters.UI.UITheme.GoldDim.b, 0.22f), 40f);

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
                    new Vector2(0, 124), new Vector2(620, 44), 22, PromptFighters.UI.UITheme.P1Neon)
                    .fontStyle = FontStyles.Bold | FontStyles.Italic;
                // 影
                var titleShadow = MakeLabel(_titlePanel.transform, "TitleShadow", "PROMPT FIGHTERS",
                    new Vector2(4, 50), new Vector2(800, 96), 64, new Color(0f, 0f, 0f, 0.6f));
                titleShadow.fontStyle = FontStyles.Bold | FontStyles.Italic;
                titleShadow.characterSpacing = 4f;
                var title = MakeLabel(_titlePanel.transform, "TitleMain", "PROMPT FIGHTERS",
                    new Vector2(0, 54), new Vector2(800, 96), 64, PromptFighters.UI.UITheme.Gold);
                title.fontStyle = FontStyles.Bold | FontStyles.Italic;
                title.characterSpacing = 4f;
                _titleMainRect = title.rectTransform;
            }

            MakeLabel(_titlePanel.transform, "TitleSub",
                "プロンプトでファイターを作ろう。API準備中はプリセットで対戦・トレーニングができます。",
                new Vector2(0, -28), new Vector2(780, 44), 18, PromptFighters.UI.UITheme.Ink);
            MakeLabel(_titlePanel.transform, "ApiNote",
                "現在はプリセットキャラ・サンプル技・トレーニングモードでプレイできます。",
                new Vector2(0, -64), new Vector2(760, 32), 15, PromptFighters.UI.UITheme.InkDim);

            var startButton = MakeButton(_titlePanel.transform, "GameStartBtn", "ゲームスタート",
                new Vector2(0, -132), new Vector2(360, 70), ShowCharacterSelect,
                PromptFighters.UI.UITheme.Gold);
            StyleArcadeButton(startButton, PromptFighters.UI.UITheme.Gold, 18f);
            SetButtonLabelStyle(startButton, 26f, FontStyles.Bold | FontStyles.Italic, new Color(0.12f, 0.08f, 0.0f));
            _startButtonRect = startButton.GetComponent<RectTransform>();
            MakeLabel(_titlePanel.transform, "StartHelp", "▶ スペース / エンターキー",
                new Vector2(0, -186), new Vector2(360, 28), 15, PromptFighters.UI.UITheme.InkDim);

            // 操作説明（タイトルからいつでも確認できるように）
            var controlsBtn = MakeButton(_titlePanel.transform, "TitleControlsBtn", "操作説明",
                new Vector2(0, -240), new Vector2(220, 46), ShowControlsPanel, ToggleOffColor);
            StyleArcadeButton(controlsBtn, ToggleOffColor, 12f);
            SetButtonLabelStyle(controlsBtn, 18f, FontStyles.Bold | FontStyles.Italic, PromptFighters.UI.UITheme.Ink);
        }

        // ── 操作説明オーバーレイ ─────────────────────────────────────
        // タイトル/キャラ選択の「操作説明」ボタンから開く。クリックまたは閉じるボタンで閉じる。
        void BuildControlsPanel()
        {
            _controlsPanel = CreateUIObject("ControlsOverlay", transform);
            StretchFull(_controlsPanel.GetComponent<RectTransform>());
            var dim = _controlsPanel.AddComponent<Image>();
            dim.color = new Color(0.01f, 0.012f, 0.03f, 0.94f);
            // どこをクリックしても閉じる
            var closeAll = _controlsPanel.AddComponent<Button>();
            closeAll.transition = Selectable.Transition.None;
            closeAll.onClick.AddListener(HideControlsPanel);

            var t = _controlsPanel.transform;
            MakeSlantBar(t, "CtrlTitleSlash", new Vector2(0, 430f), new Vector2(460f, 54f),
                new Color(PromptFighters.UI.UITheme.GoldDim.r, PromptFighters.UI.UITheme.GoldDim.g, PromptFighters.UI.UITheme.GoldDim.b, 0.30f), 24f);
            MakeLabel(t, "CtrlTitle", "操作説明",
                new Vector2(0, 430f), new Vector2(500f, 60f), 36, PromptFighters.UI.UITheme.Gold)
                .fontStyle = FontStyles.Bold | FontStyles.Italic;

            const string p1Text =
                "移動　　　　A / D\n" +
                "ジャンプ　　W（空中でもう一度で2段ジャンプ）\n" +
                "急降下　　　S（空中）　台すり抜け　S（台の上）\n" +
                "ガード　　　左Shift 長押し\n" +
                "回避　　　　ガード + 方向入力\n" +
                "技　　　　　J / K / L\n" +
                "スマッシュ　A/D 直後に J 長押し → 離す\n" +
                "掴み　　　　G　→ 方向入力で投げ";
            const string p2Text =
                "移動　　　　← / →\n" +
                "ジャンプ　　↑（空中でもう一度で2段ジャンプ）\n" +
                "急降下　　　↓（空中）　台すり抜け　↓（台の上）\n" +
                "ガード　　　右Ctrl 長押し\n" +
                "回避　　　　ガード + 方向入力\n" +
                "技　　　　　テンキー 2 / 3 / 1\n" +
                "スマッシュ　←/→ 直後に テン2 長押し → 離す\n" +
                "掴み　　　　テン0　→ 方向入力で投げ";
            const string padText =
                "ゲームパッド（1台目=1P / 2台目=2P）　移動: 左スティック・十字キー　ジャンプ: Y/△　" +
                "ガード: RB・RT　技: B・A・X　掴み: LB・LT　回避: ガード + 方向";

            BuildControlsColumn(t, true,  p1Text);
            BuildControlsColumn(t, false, p2Text);

            var pad = MakeLabel(t, "PadHelp", padText,
                new Vector2(0, -310f), new Vector2(1500f, 64f), 18, PromptFighters.UI.UITheme.Ink);
            pad.alignment = TextAlignmentOptions.Center;

            var closeBtn = MakeButton(t, "CtrlCloseBtn", "閉じる",
                new Vector2(0, -420f), new Vector2(240f, 56f), HideControlsPanel,
                PromptFighters.UI.UITheme.Gold);
            StyleArcadeButton(closeBtn, PromptFighters.UI.UITheme.Gold, 14f);
            SetButtonLabelStyle(closeBtn, 22f, FontStyles.Bold | FontStyles.Italic, new Color(0.12f, 0.08f, 0f));
            MakeLabel(t, "CtrlCloseHelp", "クリックでも閉じられます",
                new Vector2(0, -470f), new Vector2(400f, 26f), 14, PromptFighters.UI.UITheme.InkDim);

            _controlsPanel.SetActive(false);
        }

        void BuildControlsColumn(Transform parent, bool isP1, string body)
        {
            float cx = isP1 ? -390f : 390f;
            var pColor = isP1 ? PromptFighters.UI.UITheme.P1Neon : PromptFighters.UI.UITheme.P2Neon;
            float slant = isP1 ? 14f : -14f;

            var card = MakePanel(parent, isP1 ? "CtrlCard1P" : "CtrlCard2P",
                new Vector2(cx, 40f), new Vector2(700f, 600f),
                new Color(0.012f, 0.014f, 0.024f, 0.95f));
            card.raycastTarget = false;
            MakeSlantBar(parent, isP1 ? "CtrlTop1P" : "CtrlTop2P",
                new Vector2(cx, 338f), new Vector2(700f, 6f), pColor, slant);

            var head = MakeLabel(parent, isP1 ? "CtrlHead1P" : "CtrlHead2P",
                isP1 ? "1P（キーボード）" : "2P（キーボード）",
                new Vector2(cx, 290f), new Vector2(640f, 44f), 26, pColor);
            head.fontStyle = FontStyles.Bold | FontStyles.Italic;

            var bodyLabel = MakeLabel(parent, isP1 ? "CtrlBody1P" : "CtrlBody2P", body,
                new Vector2(cx, 0f), new Vector2(620f, 480f), 21, PromptFighters.UI.UITheme.Ink);
            bodyLabel.alignment = TextAlignmentOptions.TopLeft;
            bodyLabel.lineSpacing = 22f;
        }

        void ShowControlsPanel()
        {
            if (_controlsPanel != null) _controlsPanel.SetActive(true);
        }

        void HideControlsPanel()
        {
            if (_controlsPanel != null) _controlsPanel.SetActive(false);
        }

        // 実況・天使・台・CPU のロビートグルを1行に並べて生成する。キャラ選択画面で使用。
        void BuildLobbyToggles(Transform parent, float rowY)
        {
            float[] xs = { -270f, -90f, 90f, 270f };
            var toggleSize = new Vector2(168f, 40f);
            const float ToggleFontSize = 17f;

            var commentaryBtn = MakeButton(parent, "CommentaryToggle", CommentaryToggleText(),
                new Vector2(xs[0], rowY), toggleSize, OnCommentaryToggle, ToggleOnColor);
            StyleArcadeButton(commentaryBtn, ToggleOnColor, 10f);
            _commentaryToggleBg    = commentaryBtn.GetComponent<Image>();
            _commentaryToggleLabel = commentaryBtn.GetComponentInChildren<TextMeshProUGUI>();
            _commentaryToggleLabel.fontStyle = FontStyles.Bold | FontStyles.Italic;
            _commentaryToggleLabel.fontSize = ToggleFontSize;

            var angelBtn = MakeButton(parent, "AngelToggle", AngelToggleText(),
                new Vector2(xs[1], rowY), toggleSize, OnAngelToggle, ToggleOnColor);
            StyleArcadeButton(angelBtn, ToggleOnColor, 10f);
            _angelToggleBg    = angelBtn.GetComponent<Image>();
            _angelToggleLabel = angelBtn.GetComponentInChildren<TextMeshProUGUI>();
            _angelToggleLabel.fontStyle = FontStyles.Bold | FontStyles.Italic;
            _angelToggleLabel.fontSize = ToggleFontSize;

            var platformBtn = MakeButton(parent, "PlatformToggle", PlatformToggleText(),
                new Vector2(xs[2], rowY), toggleSize, OnPlatformToggle, ToggleOnColor);
            StyleArcadeButton(platformBtn, ToggleOnColor, 10f);
            _platformToggleBg    = platformBtn.GetComponent<Image>();
            _platformToggleLabel = platformBtn.GetComponentInChildren<TextMeshProUGUI>();
            _platformToggleLabel.fontStyle = FontStyles.Bold | FontStyles.Italic;
            _platformToggleLabel.fontSize = ToggleFontSize;

            var cpuBtn = MakeButton(parent, "CpuToggle", CpuToggleText(),
                new Vector2(xs[3], rowY), toggleSize, OnCpuToggle, ToggleOnColor);
            StyleArcadeButton(cpuBtn, ToggleOnColor, 10f);
            _cpuToggleBg    = cpuBtn.GetComponent<Image>();
            _cpuToggleLabel = cpuBtn.GetComponentInChildren<TextMeshProUGUI>();
            _cpuToggleLabel.fontStyle = FontStyles.Bold | FontStyles.Italic;
            _cpuToggleLabel.fontSize = ToggleFontSize;

            // CPUが操作する側（1P/2P）の選択。CPU ON時のみ意味を持つ。
            var cpuSideBtn = MakeButton(parent, "CpuSideToggle", CpuSideToggleText(),
                new Vector2(455f, rowY), new Vector2(158f, 40f), OnCpuSideToggle, ToggleOnColor);
            StyleArcadeButton(cpuSideBtn, ToggleOnColor, 10f);
            _cpuSideToggleBg    = cpuSideBtn.GetComponent<Image>();
            _cpuSideToggleLabel = cpuSideBtn.GetComponentInChildren<TextMeshProUGUI>();
            _cpuSideToggleLabel.fontStyle = FontStyles.Bold | FontStyles.Italic;
            _cpuSideToggleLabel.fontSize = ToggleFontSize;

            // 協力モード時のみ表示するボスキャラ選択（◀ ボス名 ▶）。トグル行とフッターの間に配置。
            BuildBossSelector(parent, -407f);

            RefreshToggleVisuals();
        }

        // 討伐ボスを選ぶ ◀ / ▶ 付きの選択行。協力モード時のみ表示。
        void BuildBossSelector(Transform parent, float rowY)
        {
            _bossSelectorRoot = CreateUIObject("BossSelector", parent);
            var rt = _bossSelectorRoot.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
            var t = _bossSelectorRoot.transform;

            // 左端ラベル「討伐ボス」
            MakeLabel(t, "BossSelectHeading", "討伐ボス",
                new Vector2(-310f, rowY), new Vector2(130f, 32f), 17f, PromptFighters.UI.UITheme.Urgent)
                .fontStyle = FontStyles.Bold | FontStyles.Italic;

            var prevBtn = MakeButton(t, "BossPrev", "◀",
                new Vector2(-185f, rowY), new Vector2(50f, 34f), OnBossPresetPrev, PromptFighters.UI.UITheme.Urgent);
            StyleArcadeButton(prevBtn, PromptFighters.UI.UITheme.Urgent, 8f);
            SetButtonLabelStyle(prevBtn, 20f, FontStyles.Bold, Color.white);

            var nameBtn = MakeButton(t, "BossName", BossPresetText(),
                new Vector2(0f, rowY), new Vector2(280f, 36f), OnBossPresetCycle, ToggleOffColor);
            StyleArcadeButton(nameBtn, ToggleOffColor, 8f);
            _bossPresetLabel = nameBtn.GetComponentInChildren<TextMeshProUGUI>();
            _bossPresetLabel.fontStyle = FontStyles.Bold | FontStyles.Italic;
            _bossPresetLabel.fontSize = 17f;

            var nextBtn = MakeButton(t, "BossNext", "▶",
                new Vector2(185f, rowY), new Vector2(50f, 34f), OnBossPresetCycle, PromptFighters.UI.UITheme.Urgent);
            StyleArcadeButton(nextBtn, PromptFighters.UI.UITheme.Urgent, 8f);
            SetButtonLabelStyle(nextBtn, 20f, FontStyles.Bold, Color.white);
        }

        string BossPresetText() =>
            $"ボス: {GetPresetName(_bossPresetIdx)}";

        void OnBossPresetCycle()
        {
            if (_presets == null || _presets.Count == 0) return;
            _bossPresetIdx = (_bossPresetIdx + 1) % _presets.Count;
            SyncBossCharacter();
            RefreshToggleVisuals();
        }

        void OnBossPresetPrev()
        {
            if (_presets == null || _presets.Count == 0) return;
            _bossPresetIdx = (_bossPresetIdx - 1 + _presets.Count) % _presets.Count;
            SyncBossCharacter();
            RefreshToggleVisuals();
        }

        // 選択中のボスキャラをBattleManagerへ反映する。
        void SyncBossCharacter()
        {
            if (_presets == null || _presets.Count == 0) return;
            if (_bossPresetIdx < 0 || _bossPresetIdx >= _presets.Count) _bossPresetIdx = 0;
            PromptFighters.Battle.BattleManager.RequestedBossCharacter =
                PromptCharacterFactory.Clone(_presets[_bossPresetIdx]);
        }

        static string CommentaryToggleText() =>
            PromptFighters.UI.CommentaryController.Enabled ? "実況 ON" : "実況 OFF";
        static string AngelToggleText() =>
            PromptFighters.UI.AngelController.Enabled ? "アイテム ON" : "アイテム OFF";
        static string PlatformToggleText() =>
            PromptFighters.Battle.StagePlatformSpawner.PlatformsEnabled ? "台 ON" : "台 OFF";
        static string CpuToggleText()
        {
            switch (PromptFighters.Battle.FighterAI.Level)
            {
                case PromptFighters.Battle.FighterAI.CpuLevel.Easy:   return "CPU 弱";
                case PromptFighters.Battle.FighterAI.CpuLevel.Normal: return "CPU 中";
                case PromptFighters.Battle.FighterAI.CpuLevel.Hard:   return "CPU 強";
                default:                                              return "CPU OFF";
            }
        }

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

        void OnPlatformToggle()
        {
            PromptFighters.Battle.StagePlatformSpawner.PlatformsEnabled =
                !PromptFighters.Battle.StagePlatformSpawner.PlatformsEnabled;
            RefreshToggleVisuals();
        }

        void OnCpuToggle()
        {
            PromptFighters.Battle.FighterAI.Level =
                (PromptFighters.Battle.FighterAI.CpuLevel)(((int)PromptFighters.Battle.FighterAI.Level + 1) % 4);
            RefreshToggleVisuals();
        }

        static string CpuSideToggleText() =>
            PromptFighters.Battle.FighterAI.CpuSide == 1 ? "CPU側: 1P" : "CPU側: 2P";

        void OnCpuSideToggle()
        {
            PromptFighters.Battle.FighterAI.CpuSide =
                PromptFighters.Battle.FighterAI.CpuSide == 1 ? 2 : 1;
            RefreshToggleVisuals();
        }

        // バトルモード選択行（1 vs 1 / 協力ボス討伐）をパネル上部に並べて生成する。
        void BuildModeSelector(Transform parent, float rowY)
        {
            var versusBtn = MakeButton(parent, "ModeVersusBtn", "1 vs 1 対戦",
                new Vector2(-155f, rowY), new Vector2(280f, 48f), OnSelectVersus, ToggleOnColor);
            StyleArcadeButton(versusBtn, ToggleOnColor, 11f);
            _modeVersusBg    = versusBtn.GetComponent<Image>();
            _modeVersusLabel = versusBtn.GetComponentInChildren<TextMeshProUGUI>();
            _modeVersusLabel.fontStyle = FontStyles.Bold | FontStyles.Italic;
            _modeVersusLabel.fontSize = 19f;

            var coopBtn = MakeButton(parent, "ModeCoopBtn", "ボス討伐（協力）",
                new Vector2(155f, rowY), new Vector2(280f, 48f), OnSelectCoop, ToggleOnColor);
            StyleArcadeButton(coopBtn, ToggleOnColor, 11f);
            _modeCoopBg    = coopBtn.GetComponent<Image>();
            _modeCoopLabel = coopBtn.GetComponentInChildren<TextMeshProUGUI>();
            _modeCoopLabel.fontStyle = FontStyles.Bold | FontStyles.Italic;
            _modeCoopLabel.fontSize = 19f;
        }

        void OnSelectVersus()
        {
            PromptFighters.Battle.BattleManager.RequestedMode = PromptFighters.Battle.BattleMode.Versus;
            RefreshToggleVisuals();
        }

        void OnSelectCoop()
        {
            PromptFighters.Battle.BattleManager.RequestedMode = PromptFighters.Battle.BattleMode.CoopVsBoss;
            RefreshToggleVisuals();
        }

        void RefreshToggleVisuals()
        {
            bool ce = PromptFighters.UI.CommentaryController.Enabled;
            bool ae = PromptFighters.UI.AngelController.Enabled;
            bool pe = PromptFighters.Battle.StagePlatformSpawner.PlatformsEnabled;
            bool cpu = PromptFighters.Battle.FighterAI.Enabled;
            if (_commentaryToggleBg  != null) _commentaryToggleBg.color  = ce ? ToggleOnColor  : ToggleOffColor;
            if (_commentaryToggleLabel != null) _commentaryToggleLabel.text = CommentaryToggleText();
            if (_angelToggleBg       != null) _angelToggleBg.color       = ae ? ToggleOnColor  : ToggleOffColor;
            if (_angelToggleLabel    != null) _angelToggleLabel.text    = AngelToggleText();
            if (_platformToggleBg    != null) _platformToggleBg.color    = pe ? ToggleOnColor  : ToggleOffColor;
            if (_platformToggleLabel != null) _platformToggleLabel.text  = PlatformToggleText();
            if (_cpuToggleBg         != null) _cpuToggleBg.color         = cpu ? ToggleOnColor : ToggleOffColor;
            if (_cpuToggleLabel      != null) _cpuToggleLabel.text       = CpuToggleText();
            // CPU側トグルはCPU有効時のみ点灯。OFF時はダミー表示（暗色）。
            if (_cpuSideToggleBg     != null) _cpuSideToggleBg.color     = cpu ? ToggleOnColor : ToggleOffColor;
            if (_cpuSideToggleLabel  != null) _cpuSideToggleLabel.text   = CpuSideToggleText();
            bool coop = PromptFighters.Battle.BattleManager.RequestedMode == PromptFighters.Battle.BattleMode.CoopVsBoss;
            if (_modeVersusBg   != null) _modeVersusBg.color = coop ? ToggleOffColor : ToggleOnColor;
            if (_modeCoopBg     != null) _modeCoopBg.color   = coop ? ToggleOnColor  : ToggleOffColor;
            if (_modeVersusLabel != null) _modeVersusLabel.color = coop ? PromptFighters.UI.UITheme.InkDim : new Color(0.12f, 0.08f, 0f);
            if (_modeCoopLabel   != null) _modeCoopLabel.color   = coop ? new Color(0.12f, 0.08f, 0f) : PromptFighters.UI.UITheme.InkDim;
            if (_bossSelectorRoot != null) _bossSelectorRoot.SetActive(coop);
            if (coop)
            {
                SyncBossCharacter();
                if (_bossPresetLabel != null) _bossPresetLabel.text = BossPresetText();
            }
        }

        void BuildPanel()
        {
            _panel = CreateUIObject("PreBattleOverlay", transform);
            StretchFull(_panel.GetComponent<RectTransform>());

            var bg = _panel.AddComponent<Image>();
            bg.sprite = CreateGradientSprite(
                new Color(0.05f, 0.06f, 0.09f, 1f),
                new Color(0.06f, 0.07f, 0.11f, 1f),
                new Color(0.012f, 0.014f, 0.022f, 1f),
                new Color(0.0f, 0.0f, 0.012f, 1f));
            bg.type = Image.Type.Simple;

            var cg = _panel.AddComponent<CanvasGroup>();
            cg.interactable = true;
            cg.blocksRaycasts = true;

            // ── ヘッダー（スチール地＋ゴールドの斜めライン） ──
            var header = CreateUIObject("Header", _panel.transform);
            var hRt = header.GetComponent<RectTransform>();
            hRt.anchorMin = new Vector2(0f, 1f);
            hRt.anchorMax = new Vector2(1f, 1f);
            hRt.offsetMin = new Vector2(0f, -88f);
            hRt.offsetMax = Vector2.zero;
            var hImg = header.AddComponent<Image>();
            hImg.sprite = PromptFighters.UI.UITheme.VGradient;
            hImg.type = Image.Type.Simple;
            hImg.color = PromptFighters.UI.UITheme.Steel;
            MakeOutline(_panel.transform, "HeaderEdge", new Vector2(0, 496), new Vector2(2200, 4),
                PromptFighters.UI.UITheme.Gold);

            MakeSlantBar(_panel.transform, "TitleSlash", new Vector2(0, 522f), new Vector2(420f, 50f),
                new Color(PromptFighters.UI.UITheme.GoldDim.r, PromptFighters.UI.UITheme.GoldDim.g, PromptFighters.UI.UITheme.GoldDim.b, 0.30f), 24f);
            MakeLabel(_panel.transform, "PanelTitle", "キャラクター選択",
                new Vector2(0, 522), new Vector2(700, 56), 34, PromptFighters.UI.UITheme.Gold)
                .fontStyle = FontStyles.Bold | FontStyles.Italic;

            // ── 中央 VS ディバイダ ──
            MakeSlantBar(_panel.transform, "Divider", new Vector2(0, 0), new Vector2(8, 880),
                new Color(PromptFighters.UI.UITheme.Gold.r, PromptFighters.UI.UITheme.Gold.g, PromptFighters.UI.UITheme.Gold.b, 0.35f), 60f);
            var vsShadow = MakeLabel(_panel.transform, "VsShadow", "VS",
                new Vector2(4, 360), new Vector2(160, 120), 76, new Color(0f, 0f, 0f, 0.55f));
            vsShadow.fontStyle = FontStyles.Bold | FontStyles.Italic;
            MakeLabel(_panel.transform, "Vs", "VS",
                new Vector2(0, 364), new Vector2(160, 120), 76, PromptFighters.UI.UITheme.Gold)
                .fontStyle = FontStyles.Bold | FontStyles.Italic;

            // ── バトルモード選択（1 vs 1 / 協力ボス討伐） ──
            BuildModeSelector(_panel.transform, 452f);

            // ── 1P エリア（左半分） ──
            BuildPlayerColumn(_panel.transform, true);

            // ── 2P エリア（右半分） ──
            BuildPlayerColumn(_panel.transform, false);

            // ── 共有ロスター（スマブラ風キャラ選択グリッド） ──
            BuildSharedRoster(_panel.transform);

            // ── フッター: ボタン ──
            var startBtn = MakeButton(_panel.transform, "StartBtn", "バトル開始",
                new Vector2(-310, -460), new Vector2(280, 70), OnStartPressed,
                PromptFighters.UI.UITheme.Gold);
            StyleArcadeButton(startBtn, PromptFighters.UI.UITheme.Gold, 16f);
            SetButtonLabelStyle(startBtn, 26f, FontStyles.Bold | FontStyles.Italic, new Color(0.12f, 0.08f, 0f));

            var trainBtn = MakeButton(_panel.transform, "TrainingBtn", "トレーニング",
                new Vector2(0, -460), new Vector2(280, 70), OnTrainingPressed,
                PromptFighters.UI.UITheme.P1Neon);
            StyleArcadeButton(trainBtn, PromptFighters.UI.UITheme.P1Neon, 16f);
            SetButtonLabelStyle(trainBtn, 26f, FontStyles.Bold | FontStyles.Italic, Color.white);

            var genBtn = MakeButton(_panel.transform, "GenerateBtn", "キャラ生成",
                new Vector2(310, -460), new Vector2(280, 70), ShowGenerationSetupPanel,
                PromptFighters.UI.UITheme.P2Neon);
            StyleArcadeButton(genBtn, PromptFighters.UI.UITheme.P2Neon, 16f);
            SetButtonLabelStyle(genBtn, 26f, FontStyles.Bold | FontStyles.Italic, Color.white);

            // ── 操作説明（ヘッダー右） ──
            var helpBtn = MakeButton(_panel.transform, "ControlsBtn", "操作説明",
                new Vector2(820f, 522f), new Vector2(180f, 44f), ShowControlsPanel, ToggleOffColor);
            StyleArcadeButton(helpBtn, ToggleOffColor, 12f);
            SetButtonLabelStyle(helpBtn, 18f, FontStyles.Bold | FontStyles.Italic, PromptFighters.UI.UITheme.Ink);

            // ── ロビー設定トグル（実況・天使・台・CPU） ──
            BuildLobbyToggles(_panel.transform, -372f);

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

        // 長いキャラ名が VS 中央へはみ出さないよう、1行・幅に収まるまで自動縮小する
        static void ConfigurePresetNameLabel(TextMeshProUGUI label)
        {
            if (label == null) return;
            label.fontStyle = FontStyles.Bold | FontStyles.Italic;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.enableAutoSizing = true;
            label.fontSizeMin = 16f;
            label.fontSizeMax = 28f;
        }

        void BuildPlayerColumn(Transform parent, bool isP1)
        {
            float cx = isP1 ? -480f : 480f;
            var pColor = isP1 ? PromptFighters.UI.UITheme.P1Neon : PromptFighters.UI.UITheme.P2Neon;
            var pColorDark = isP1 ? PromptFighters.UI.UITheme.P1NeonDark : PromptFighters.UI.UITheme.P2NeonDark;
            float slant = isP1 ? 18f : -18f;
            var bgColor = new Color(pColorDark.r, pColorDark.g, pColorDark.b, 0.28f);

            // 背景（ネオン地＋上端の斜めエッジ）
            var colBg = CreateUIObject(isP1 ? "P1ColBg" : "P2ColBg", parent);
            var cbRt = colBg.GetComponent<RectTransform>();
            cbRt.anchorMin = new Vector2(isP1 ? 0f : 0.5f, 0f);
            cbRt.anchorMax = new Vector2(isP1 ? 0.5f : 1f, 1f);
            cbRt.offsetMin = isP1 ? new Vector2(0f, 80f) : new Vector2(0f, 80f);
            cbRt.offsetMax = isP1 ? new Vector2(-2f, -80f) : new Vector2(0f, -80f);
            var cbImg = colBg.AddComponent<Image>();
            cbImg.sprite = PromptFighters.UI.UITheme.VGradient;
            cbImg.type = Image.Type.Simple;
            cbImg.color = bgColor;

            // プレイヤーバッジ（斜めネオンプレート）— 左上に小さく
            MakeSlantBar(parent, isP1 ? "P1BadgePlate" : "P2BadgePlate",
                new Vector2(cx - 300f, 404f), new Vector2(120f, 56f), pColor, slant);
            var badge = MakeLabel(parent, isP1 ? "P1Badge" : "P2Badge",
                isP1 ? "1P" : "2P",
                new Vector2(cx - 300f, 404f), new Vector2(120f, 56f), 38, Color.white);
            badge.fontStyle = FontStyles.Bold | FontStyles.Italic;

            // 選択中キャラ名（中央・大きく）
            var row = CreateUIObject(isP1 ? "P1Row" : "P2Row", parent);
            var rowRt = row.GetComponent<RectTransform>();
            rowRt.anchorMin = rowRt.anchorMax = new Vector2(0.5f, 0.5f);
            rowRt.anchoredPosition = new Vector2(cx + 20f, 404f);
            rowRt.sizeDelta = new Vector2(440f, 52f);

            var label = MakeLabel(row.transform, "Preset",
                isP1 ? GetPresetName(_p1PresetIdx) : GetPresetName(_p2PresetIdx),
                new Vector2(0f, 0f), new Vector2(420f, 52f), 26, Color.white);
            ConfigurePresetNameLabel(label);

            if (isP1) _p1PresetLabel = label;
            else       _p2PresetLabel = label;

            // ── 大きなキャラプレビュー（左） ──
            var previewFrame = CreateUIObject(isP1 ? "P1PreviewFrame" : "P2PreviewFrame", parent);
            var pfRt = previewFrame.GetComponent<RectTransform>();
            pfRt.anchoredPosition = new Vector2(cx - 168f, 92f);
            pfRt.sizeDelta = new Vector2(280f, 360f);
            var pfImg = AddImage(previewFrame, new Color(0.012f, 0.014f, 0.024f, 0.92f));
            pfImg.sprite = PromptFighters.UI.UITheme.VGradient; pfImg.type = Image.Type.Simple;
            MakeSlantBar(previewFrame.transform, "PreviewTop", new Vector2(0f, 178f), new Vector2(280f, 5f), pColor, slant);
            MakeSlantBar(previewFrame.transform, "PreviewBottom", new Vector2(0f, -178f), new Vector2(280f, 5f), pColor, slant);

            var previewGo = CreateUIObject(isP1 ? "P1PreviewImage" : "P2PreviewImage", previewFrame.transform);
            var pvRt = previewGo.GetComponent<RectTransform>();
            pvRt.anchorMin = Vector2.zero;
            pvRt.anchorMax = Vector2.one;
            pvRt.offsetMin = new Vector2(22f, 18f);
            pvRt.offsetMax = new Vector2(-22f, -18f);
            var preview = previewGo.AddComponent<Image>();
            preview.preserveAspect = true;
            preview.raycastTarget = false;
            preview.color = Color.white;
            if (isP1) _p1PreviewImage = preview;
            else _p2PreviewImage = preview;

            // ── ステータスグラフ＋技（右パネル） ──
            var statPanel = CreateUIObject(isP1 ? "P1StatPanel" : "P2StatPanel", parent);
            var spRt = statPanel.GetComponent<RectTransform>();
            spRt.anchoredPosition = new Vector2(cx + 168f, 92f);
            spRt.sizeDelta = new Vector2(330f, 360f);
            var spImg = AddImage(statPanel, new Color(0.012f, 0.014f, 0.024f, 0.92f));
            spImg.sprite = PromptFighters.UI.UITheme.VGradient; spImg.type = Image.Type.Simple;
            MakeSlantBar(statPanel.transform, "StatTop", new Vector2(0f, 178f), new Vector2(330f, 5f), pColor, slant);

            MakeLabel(statPanel.transform, "StatHeader", "STATUS",
                new Vector2(0f, 152f), new Vector2(300f, 26f), 18f, PromptFighters.UI.UITheme.Gold)
                .fontStyle = FontStyles.Bold | FontStyles.Italic;

            var fills  = new Image[StatAxisLabels.Length];
            var values = new TextMeshProUGUI[StatAxisLabels.Length];
            for (int i = 0; i < StatAxisLabels.Length; i++)
            {
                MakeStatGauge(statPanel.transform, StatAxisLabels[i],
                    new Vector2(0f, 118f - i * 28f), new Vector2(300f, 22f), pColor,
                    out fills[i], out values[i]);
            }
            if (isP1) { _p1StatFills = fills; _p1StatValues = values; }
            else      { _p2StatFills = fills; _p2StatValues = values; }

            MakeOutline(statPanel.transform, "StatDiv", new Vector2(0f, -54f), new Vector2(300f, 2f),
                new Color(pColor.r, pColor.g, pColor.b, 0.4f));

            // 技・プロンプト詳細はパネルからはみ出さないよう、マスク内で縦スクロール可能にする
            var scrollGo = CreateUIObject("DetailScroll", statPanel.transform);
            var scRt = scrollGo.GetComponent<RectTransform>();
            scRt.anchoredPosition = new Vector2(0f, -116f);
            scRt.sizeDelta = new Vector2(300f, 120f);
            var scBg = scrollGo.AddComponent<Image>();
            scBg.color = new Color(0f, 0f, 0f, 0.001f); // ほぼ透明・ホイールスクロール入力受付用
            scrollGo.AddComponent<RectMask2D>();
            var scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 20f;
            scroll.viewport = scRt;

            var detailText = MakeLabel(scrollGo.transform, "DetailText", "",
                Vector2.zero, new Vector2(300f, 120f), 16f, PromptFighters.UI.UITheme.Ink);
            var dtRt = detailText.rectTransform;
            dtRt.anchorMin = new Vector2(0f, 1f);
            dtRt.anchorMax = new Vector2(1f, 1f);
            dtRt.pivot = new Vector2(0.5f, 1f);
            dtRt.offsetMin = new Vector2(4f, 0f);
            dtRt.offsetMax = new Vector2(-4f, 0f);
            dtRt.anchoredPosition = new Vector2(0f, 0f);
            detailText.alignment = TextAlignmentOptions.TopLeft;
            detailText.textWrappingMode = TextWrappingModes.Normal;
            detailText.raycastTarget = false;
            var dtFitter = detailText.gameObject.AddComponent<ContentSizeFitter>();
            dtFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = dtRt;
            if (isP1) _p1DetailText = detailText;
            else _p2DetailText = detailText;

            // 生成キャラ削除（プレビュー枠の下）
            var deleteBtn = MakeButton(parent, isP1 ? "P1DeleteGeneratedBtn" : "P2DeleteGeneratedBtn", "生成キャラ削除",
                new Vector2(cx - 168f, -106f), new Vector2(172f, 32f), () => RequestDeleteCharacter(isP1),
                PromptFighters.UI.UITheme.P2NeonDark);
            SetButtonLabelStyle(deleteBtn, 15f, FontStyles.Bold, Color.white);
            if (isP1) _p1DeleteButton = deleteBtn;
            else _p2DeleteButton = deleteBtn;

            // コントローラー接続状態（バッジ下）
            var gpLabel = MakeLabel(parent, isP1 ? "P1GpStatus" : "P2GpStatus",
                "",
                new Vector2(cx - 220f, 366f), new Vector2(340f, 24f), 13, Color.gray);
            gpLabel.alignment = TextAlignmentOptions.Left;
            if (isP1) _p1GamepadLabel = gpLabel;
            else      _p2GamepadLabel = gpLabel;
        }

        // スマブラ風の共有キャラロスター。1P/2Pそれぞれのカーソルでセルを選ぶ。
        void BuildSharedRoster(Transform parent)
        {
            var frame = CreateUIObject("RosterFrame", parent);
            var frRt = frame.GetComponent<RectTransform>();
            frRt.anchoredPosition = new Vector2(0f, -230f);
            frRt.sizeDelta = new Vector2(1480f, 232f);
            var frImg = AddImage(frame, new Color(0.012f, 0.014f, 0.024f, 0.92f));
            frImg.sprite = PromptFighters.UI.UITheme.VGradient; frImg.type = Image.Type.Simple;
            MakeSlantBar(frame.transform, "RosterTop", new Vector2(0f, 112f), new Vector2(1480f, 4f),
                PromptFighters.UI.UITheme.Gold, 24f);

            var grid = CreateUIObject("RosterGrid", frame.transform);
            var gRt = grid.GetComponent<RectTransform>();
            gRt.anchorMin = Vector2.zero; gRt.anchorMax = Vector2.one;
            gRt.offsetMin = new Vector2(14f, 8f); gRt.offsetMax = new Vector2(-14f, -8f);
            var layout = grid.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(168f, 64f);
            layout.spacing = new Vector2(10f, 8f);
            layout.padding = new RectOffset(6, 6, 4, 4);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = RosterColumns;
            layout.childAlignment = TextAnchor.UpperCenter;
            _rosterGrid = grid.transform;

            // ヘッダー: タイトルプレート＋ページ表示
            MakeSlantBar(frame.transform, "RosterTitlePlate", new Vector2(-636f, 112f), new Vector2(232f, 30f),
                new Color(PromptFighters.UI.UITheme.Gold.r, PromptFighters.UI.UITheme.Gold.g, PromptFighters.UI.UITheme.Gold.b, 0.20f), 16f);
            MakeLabel(frame.transform, "RosterTitle", "CHARACTER SELECT",
                new Vector2(-636f, 112f), new Vector2(280f, 30f), 17f, PromptFighters.UI.UITheme.Gold)
                .fontStyle = FontStyles.Bold | FontStyles.Italic;

            _rosterPageLabel = MakeLabel(frame.transform, "RosterPage", "1 / 1",
                new Vector2(0f, 112f), new Vector2(120f, 26f), 16f, PromptFighters.UI.UITheme.Ink);
            _rosterPageLabel.fontStyle = FontStyles.Bold | FontStyles.Italic;

            // 選択方法のヒント（セルの左半分=1P / 右半分=2P は分かりにくいため明示）
            var hint = MakeLabel(frame.transform, "RosterHint",
                "<color=#4FC3F7>マウス左=1P</color> <color=#FF6B6B>右=2P</color> / パッドは各自のカーソルで選択",
                new Vector2(636f, 112f), new Vector2(360f, 26f), 15f, PromptFighters.UI.UITheme.InkDim);
            hint.fontStyle = FontStyles.Bold;
            hint.alignment = TextAlignmentOptions.Right;

            // ページ送り（ロスター左右に配置。1ページに収まらない場合のみ機能）
            var prevPage = MakeButton(frame.transform, "RosterPrev", "‹",
                new Vector2(-748f, 0f), new Vector2(50f, 156f), () => ChangeRosterPage(-1),
                PromptFighters.UI.UITheme.P1Neon);
            StyleArcadeButton(prevPage, PromptFighters.UI.UITheme.P1NeonDark, 12f);
            SetButtonLabelStyle(prevPage, 40f, FontStyles.Bold, Color.white);
            var nextPage = MakeButton(frame.transform, "RosterNext", "›",
                new Vector2(748f, 0f), new Vector2(50f, 156f), () => ChangeRosterPage(1),
                PromptFighters.UI.UITheme.P2Neon);
            StyleArcadeButton(nextPage, PromptFighters.UI.UITheme.P2NeonDark, -12f);
            SetButtonLabelStyle(nextPage, 40f, FontStyles.Bold, Color.white);
        }

        // ロスターセル1枚（ポートレート＋名前＋選択カーソル色＋左右クリック領域）を生成。
        void MakeRosterCell(Transform parent, int idx, CharacterData data)
        {
            bool selP1 = idx == _p1PresetIdx;
            bool selP2 = idx == _p2PresetIdx;

            var cell = CreateUIObject($"Cell_{idx}", parent);
            cell.AddComponent<RosterCellRef>().index = idx; // カーソルのプレイヤー判定用
            var bg = cell.AddComponent<Image>();
            bg.color = RosterCellColor(selP1, selP2);
            bg.raycastTarget = false;
            _rosterCellBgs[idx] = bg;

            var portraitGo = CreateUIObject("Portrait", cell.transform);
            var pRt = portraitGo.GetComponent<RectTransform>();
            pRt.anchorMin = new Vector2(0f, 0f); pRt.anchorMax = new Vector2(1f, 1f);
            pRt.offsetMin = new Vector2(4f, 22f); pRt.offsetMax = new Vector2(-4f, -3f);
            var pImg = portraitGo.AddComponent<Image>();
            pImg.sprite = data.characterSprite;
            pImg.preserveAspect = true;
            pImg.raycastTarget = false;
            pImg.color = data.characterSprite != null ? Color.white : new Color(0.35f, 0.38f, 0.45f);

            var nm = MakeLabel(cell.transform, "Name", data.characterName,
                new Vector2(0f, -21f), new Vector2(164f, 20f), 15f, Color.white);
            nm.fontStyle = FontStyles.Bold;
            nm.textWrappingMode = TextWrappingModes.NoWrap;
            nm.overflowMode = TextOverflowModes.Ellipsis;
            nm.raycastTarget = false;

            AddRosterClickZone(cell.transform, true, idx);   // 左半分 → 1P
            AddRosterClickZone(cell.transform, false, idx);  // 右半分 → 2P
        }

        void AddRosterClickZone(Transform parent, bool leftHalf, int idx)
        {
            var go = CreateUIObject(leftHalf ? "PickP1" : "PickP2", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(leftHalf ? 0f : 0.5f, 0f);
            rt.anchorMax = new Vector2(leftHalf ? 0.5f : 1f, 1f);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f); // 透明だがレイキャストは受ける
            var btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            bool assignP1 = leftHalf;
            int captured = idx;
            btn.onClick.AddListener(() => SelectPreset(assignP1, captured));
        }

        static Color RosterCellColor(bool selP1, bool selP2)
        {
            return (selP1 && selP2) ? PromptFighters.UI.UITheme.Gold
                 : selP1 ? PromptFighters.UI.UITheme.P1Neon
                 : selP2 ? PromptFighters.UI.UITheme.P2Neon
                 : new Color(0.08f, 0.09f, 0.13f, 1f);
        }

        // 選択カーソル色だけを既存セルに反映（グリッド再生成なしで高速）。
        void RecolorRosterCells()
        {
            foreach (var kv in _rosterCellBgs)
            {
                if (kv.Value == null) continue;
                kv.Value.color = RosterCellColor(kv.Key == _p1PresetIdx, kv.Key == _p2PresetIdx);
            }
        }

        void ChangeRosterPage(int delta)
        {
            if (_presets == null || _presets.Count == 0) return;
            int pageSize = RosterColumns * RosterRows;
            int maxPage = Mathf.Max(0, (_presets.Count - 1) / pageSize);
            _rosterPage = Mathf.Clamp(_rosterPage + delta, 0, maxPage);
            RebuildSharedGrid();
        }

        // ステータスグラフの軸ラベル（norm計算順と一致させること）
        static readonly string[] StatAxisLabels = { "HP", "パワー", "スピード", "ジャンプ", "ガード", "重さ" };

        void MakeStatGauge(Transform parent, string axis, Vector2 pos, Vector2 size, Color pColor,
            out Image fill, out TextMeshProUGUI valLabel)
        {
            var go = CreateUIObject("Gauge_" + axis, parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            var nameLabel = MakeLabel(go.transform, "Name", axis,
                new Vector2(-size.x * 0.5f + 44f, 0f), new Vector2(88f, size.y), 15f, PromptFighters.UI.UITheme.InkDim);
            nameLabel.alignment = TextAlignmentOptions.Left;
            nameLabel.fontStyle = FontStyles.Bold;

            float trackW = size.x - 152f;
            float trackCx = -size.x * 0.5f + 96f + trackW * 0.5f;

            var track = CreateUIObject("Track", go.transform);
            var trRt = track.GetComponent<RectTransform>();
            trRt.anchoredPosition = new Vector2(trackCx, 0f);
            trRt.sizeDelta = new Vector2(trackW, 14f);
            AddImage(track, PromptFighters.UI.UITheme.SteelDark);

            var fillGo = CreateUIObject("Fill", track.transform);
            var fRt = fillGo.GetComponent<RectTransform>();
            fRt.anchorMin = Vector2.zero;
            fRt.anchorMax = Vector2.one;
            fRt.offsetMin = fRt.offsetMax = Vector2.zero;
            fill = fillGo.AddComponent<Image>();
            fill.sprite = PromptFighters.UI.UITheme.VGradient;
            fill.type = Image.Type.Simple;
            fill.color = pColor;
            fill.raycastTarget = false;

            valLabel = MakeLabel(go.transform, "Val", "",
                new Vector2(size.x * 0.5f - 26f, 0f), new Vector2(52f, size.y), 15f, Color.white);
            valLabel.alignment = TextAlignmentOptions.Right;
            valLabel.fontStyle = FontStyles.Bold;
        }

        // 1技あたりの「全段ヒット時の総ダメージ」。多段ヒット(hit_count)や多発射(projectile_count)は合計する。
        static float SkillFullHitTotal(SkillData sk)
        {
            if (sk?.parameters == null) return 0f;
            float dmg = sk.parameters.damage;
            int hits = Mathf.Max(1, sk.parameters.hit_count);
            int proj = 1;
            if (sk.actions != null)
                foreach (var a in sk.actions)
                    if (a != null && a.projectile_count > proj) proj = a.projectile_count;
            return dmg * hits * proj;
        }

        // パワー基準＝各技の全段ヒット総ダメージの平均
        static float CharacterPower(CharacterData data)
        {
            if (data?.skills == null) return 0f;
            float sum = 0f; int n = 0;
            foreach (var sk in data.skills)
            {
                if (sk == null) continue;
                sum += SkillFullHitTotal(sk); n++;
            }
            return n > 0 ? sum / n : 0f;
        }

        // ステータスバーの生の指標（StatAxisLabelsと同順: HP, パワー, スピード, ジャンプ, ガード, 重さ）
        static float[] RawMetrics(CharacterData data)
        {
            var s = data?.stats ?? new CharacterStats();
            return new[] { s.maxHP, CharacterPower(data), s.groundMoveSpeed, s.jumpForce, s.guardDurability, s.weight };
        }

        // 保存済み全キャラの中での相対値でバーを決める
        static float[] ComputeStatNorms(CharacterData data, List<CharacterData> roster, out string[] vals)
        {
            var raw = RawMetrics(data);
            int len = raw.Length;
            var mn = (float[])raw.Clone();
            var mx = (float[])raw.Clone();
            if (roster != null)
                foreach (var c in roster)
                {
                    if (c == null) continue;
                    var r = RawMetrics(c);
                    for (int i = 0; i < len; i++) { mn[i] = Mathf.Min(mn[i], r[i]); mx[i] = Mathf.Max(mx[i], r[i]); }
                }

            var norms = new float[len];
            for (int i = 0; i < len; i++)
            {
                norms[i] = mx[i] > mn[i] + 0.0001f ? Mathf.InverseLerp(mn[i], mx[i], raw[i]) : 0.5f;
                norms[i] = Mathf.Clamp(norms[i], 0.08f, 1f);
            }

            vals = new[]
            {
                raw[0].ToString("F0"),
                raw[1].ToString("F0"),
                raw[2].ToString("F1"),
                raw[3].ToString("F0"),
                raw[4].ToString("F0"),
                raw[5].ToString("F2"),
            };
            return norms;
        }

        void SetStats(bool isP1, CharacterData data)
        {
            var fills  = isP1 ? _p1StatFills  : _p2StatFills;
            var values = isP1 ? _p1StatValues : _p2StatValues;
            if (fills == null) return;
            var norms = ComputeStatNorms(data, _presets, out var vals);
            for (int i = 0; i < fills.Length && i < norms.Length; i++)
            {
                if (fills[i] != null)
                    fills[i].rectTransform.anchorMax = new Vector2(norms[i], 1f);
                if (values != null && values[i] != null)
                    values[i].text = vals[i];
            }
        }

        void BuildTrainingPanel()
        {
            _trainingPanel = CreateUIObject("TrainingOverlay", transform);
            StretchFull(_trainingPanel.GetComponent<RectTransform>());
            _trainingPanel.SetActive(false);

            var cg = _trainingPanel.AddComponent<CanvasGroup>();
            cg.interactable = false;
            cg.blocksRaycasts = false;

            MakeSlantBar(_trainingPanel.transform, "TrainSlash", new Vector2(0, 485), new Vector2(420, 44),
                new Color(PromptFighters.UI.UITheme.P1NeonDark.r, PromptFighters.UI.UITheme.P1NeonDark.g, PromptFighters.UI.UITheme.P1NeonDark.b, 0.5f), 22f);
            MakeLabel(_trainingPanel.transform, "TrainingTitle", "トレーニングモード",
                new Vector2(0, 485), new Vector2(480, 46), 28, PromptFighters.UI.UITheme.P1Neon)
                .fontStyle = FontStyles.Bold | FontStyles.Italic;
            _trainingControlsText = MakeLabel(_trainingPanel.transform, "TrainingControls",
                BuildTrainingHelpText(),
                new Vector2(0, 440), new Vector2(900, 52), 14, PromptFighters.UI.UITheme.Ink);
        }

        void BuildGenerationSetupPanel()
        {
            _generationSetupPanel = CreateUIObject("GenerationSetupOverlay", transform);
            StretchFull(_generationSetupPanel.GetComponent<RectTransform>());
            _generationSetupPanel.SetActive(false);

            var bg = _generationSetupPanel.AddComponent<Image>();
            bg.sprite = CreateGradientSprite(
                new Color(0.05f, 0.06f, 0.09f, 1f),
                new Color(0.06f, 0.07f, 0.11f, 1f),
                new Color(0.012f, 0.014f, 0.022f, 1f),
                new Color(0.0f, 0.0f, 0.012f, 1f));

            MakeSlantBar(_generationSetupPanel.transform, "GenSlash", new Vector2(0f, 455f), new Vector2(520f, 52f),
                new Color(PromptFighters.UI.UITheme.GoldDim.r, PromptFighters.UI.UITheme.GoldDim.g, PromptFighters.UI.UITheme.GoldDim.b, 0.30f), 24f);
            MakeLabel(_generationSetupPanel.transform, "GenSetupTitle", "新規キャラクター生成",
                new Vector2(0f, 455f), new Vector2(760f, 56f), 32f, PromptFighters.UI.UITheme.Gold)
                .fontStyle = FontStyles.Bold | FontStyles.Italic;

            BuildGenerationColumn(_generationSetupPanel.transform, true);
            BuildGenerationColumn(_generationSetupPanel.transform, false);

            var startGen = MakeButton(_generationSetupPanel.transform, "StartGenerateBtn", "生成開始",
                new Vector2(-170f, -420f), new Vector2(260f, 64f), OnGeneratePressed,
                PromptFighters.UI.UITheme.Gold);
            StyleArcadeButton(startGen, PromptFighters.UI.UITheme.Gold, 16f);
            SetButtonLabelStyle(startGen, 23f, FontStyles.Bold | FontStyles.Italic, new Color(0.12f, 0.08f, 0f));

            var back = MakeButton(_generationSetupPanel.transform, "BackToSelectBtn", "戻る",
                new Vector2(170f, -420f), new Vector2(220f, 64f), ShowCharacterSelect,
                PromptFighters.UI.UITheme.SteelLight);
            StyleArcadeButton(back, PromptFighters.UI.UITheme.SteelLight, 16f);
            SetButtonLabelStyle(back, 20f, FontStyles.Bold | FontStyles.Italic, Color.white);

            MakeLabel(_generationSetupPanel.transform, "GenSetupHint",
                "空欄のプレイヤーは選択中の既存キャラを使用します。生成中はTキーで練習できます。",
                new Vector2(0f, -470f), new Vector2(840f, 28f), 13f, PromptFighters.UI.UITheme.InkDim);

            var debugBtn = MakeButton(_generationSetupPanel.transform, "DebugSkipImageBtn",
                "", new Vector2(0f, -370f), new Vector2(420f, 40f),
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
            var pColor = isP1 ? PromptFighters.UI.UITheme.P1Neon : PromptFighters.UI.UITheme.P2Neon;
            var pColorDark = isP1 ? PromptFighters.UI.UITheme.P1NeonDark : PromptFighters.UI.UITheme.P2NeonDark;
            float slant = isP1 ? 16f : -16f;

            var genBg = MakePanel(parent, isP1 ? "P1GenBg" : "P2GenBg",
                new Vector2(cx, 35f), new Vector2(520f, 650f),
                new Color(pColorDark.r, pColorDark.g, pColorDark.b, 0.24f));
            genBg.sprite = PromptFighters.UI.UITheme.VGradient; genBg.type = Image.Type.Simple;
            MakeSlantBar(parent, isP1 ? "P1GenTop" : "P2GenTop",
                new Vector2(cx, 358f), new Vector2(520f, 5f), pColor, slant);
            MakeSlantBar(parent, isP1 ? "P1GenBadgePlate" : "P2GenBadgePlate",
                new Vector2(cx, 325f), new Vector2(120f, 46f), pColor, slant);
            MakeLabel(parent, isP1 ? "P1GenBadge" : "P2GenBadge", isP1 ? "1P" : "2P",
                new Vector2(cx, 325f), new Vector2(120f, 46f), 28f, Color.white)
                .fontStyle = FontStyles.Bold | FontStyles.Italic;
            MakeSlantBar(parent, isP1 ? "P1GenLine" : "P2GenLine",
                new Vector2(cx, 292f), new Vector2(360f, 3f), pColor, slant);

            var nameInput = MakeInputField(parent, isP1 ? "P1GenerateNameInput" : "P2GenerateNameInput",
                "キャラクター名", new Vector2(cx, 220f), new Vector2(430f, 48f), false);
            var featureInput = MakeInputField(parent, isP1 ? "P1GenerateFeatureInput" : "P2GenerateFeatureInput",
                "特徴・見た目・戦い方", new Vector2(cx, 55f), new Vector2(430f, 210f), true);

            MakeLabel(parent, isP1 ? "P1GenNote" : "P2GenNote",
                "例: 雷をまとった小柄な剣士。素早く跳び回り、遠距離から雷を飛ばす。",
                new Vector2(cx, -78f), new Vector2(430f, 54f), 13f, new Color(0.72f, 0.78f, 0.9f));

            // AIに名前・特徴を考えてもらうボタン（人間が後で編集・確認できる）
            float btnSlant = isP1 ? 14f : -14f;
            var conceptBtn = MakeButton(parent, isP1 ? "P1ConceptBtn" : "P2ConceptBtn",
                "AIで名前・特徴を考える", new Vector2(cx - 62f, -135f), new Vector2(300f, 50f),
                () => OnConceptGeneratePressed(isP1), pColor);
            StyleArcadeButton(conceptBtn, pColor, btnSlant);
            SetButtonLabelStyle(conceptBtn, 17f, FontStyles.Bold | FontStyles.Italic, Color.white);

            // 名前・特徴をクリアするリセットボタン
            var resetBtn = MakeButton(parent, isP1 ? "P1ResetBtn" : "P2ResetBtn",
                "リセット", new Vector2(cx + 158f, -135f), new Vector2(108f, 50f),
                () => OnResetConceptPressed(isP1), PromptFighters.UI.UITheme.SteelLight);
            StyleArcadeButton(resetBtn, PromptFighters.UI.UITheme.SteelLight, btnSlant);
            SetButtonLabelStyle(resetBtn, 16f, FontStyles.Bold | FontStyles.Italic, Color.white);

            var conceptStatus = MakeLabel(parent, isP1 ? "P1ConceptStatus" : "P2ConceptStatus",
                "", new Vector2(cx, -180f), new Vector2(430f, 26f), 13f, new Color(0.72f, 0.82f, 0.95f));

            if (isP1)
            {
                _p1NameInput = nameInput;
                _p1FeatureInput = featureInput;
                _p1ConceptButton = conceptBtn;
                _p1ConceptStatus = conceptStatus;
            }
            else
            {
                _p2NameInput = nameInput;
                _p2FeatureInput = featureInput;
                _p2ConceptButton = conceptBtn;
                _p2ConceptStatus = conceptStatus;
            }
        }

        // 「AIで名前・特徴を考える」ボタン。AIが原案を出し、入力欄へ流し込む（人間が編集・確認可能）。
        void OnConceptGeneratePressed(bool isP1)
        {
            bool busy = isP1 ? _p1ConceptBusy : _p2ConceptBusy;
            if (busy) return;

            var nameInput    = isP1 ? _p1NameInput    : _p2NameInput;
            var featureInput = isP1 ? _p1FeatureInput : _p2FeatureInput;
            var statusLabel  = isP1 ? _p1ConceptStatus : _p2ConceptStatus;
            var button       = isP1 ? _p1ConceptButton : _p2ConceptButton;

            // 名前・特徴の入力状況で生成方向が変わる（双方向）。片方だけ入力ならもう片方を補完する。
            string nameHint = nameInput?.text;
            string featHint = featureInput?.text;
            bool hadName = !string.IsNullOrWhiteSpace(nameHint);
            bool hadFeat = !string.IsNullOrWhiteSpace(featHint);
            bool oneSided = hadName ^ hadFeat; // 片方だけ入力 → 入力済みの側は尊重して上書きしない

            if (isP1) _p1ConceptBusy = true; else _p2ConceptBusy = true;
            if (button != null) button.interactable = false;
            if (statusLabel != null)
            {
                statusLabel.color = new Color(0.72f, 0.82f, 0.95f);
                statusLabel.text = "AIが考え中...";
            }

            AICharacterClient.GenerateConcept(this, nameHint, featHint,
                concept =>
                {
                    // 片方だけ入力していた場合、その入力済みの側は尊重して上書きしない（補完のみ）。
                    if (nameInput != null && !string.IsNullOrWhiteSpace(concept.character_name)
                        && !(oneSided && hadName))
                        nameInput.text = concept.character_name;
                    if (featureInput != null && !string.IsNullOrWhiteSpace(concept.features)
                        && !(oneSided && hadFeat))
                        featureInput.text = concept.features;
                    if (statusLabel != null)
                    {
                        statusLabel.color = new Color(0.55f, 0.9f, 0.6f);
                        statusLabel.text = "AIが原案を作成しました。編集して生成できます。";
                    }
                    if (isP1) _p1ConceptBusy = false; else _p2ConceptBusy = false;
                    if (button != null) button.interactable = true;
                },
                err =>
                {
                    if (statusLabel != null)
                    {
                        statusLabel.color = new Color(1f, 0.55f, 0.5f);
                        statusLabel.text = "生成に失敗しました。もう一度お試しください。";
                    }
                    Debug.LogWarning($"[PreBattlePanel] 原案生成失敗: {err}");
                    if (isP1) _p1ConceptBusy = false; else _p2ConceptBusy = false;
                    if (button != null) button.interactable = true;
                });
        }

        // 名前・特徴の入力欄をクリアして空に戻す。
        void OnResetConceptPressed(bool isP1)
        {
            var nameInput    = isP1 ? _p1NameInput    : _p2NameInput;
            var featureInput = isP1 ? _p1FeatureInput : _p2FeatureInput;
            var statusLabel  = isP1 ? _p1ConceptStatus : _p2ConceptStatus;

            if (nameInput != null) nameInput.text = "";
            if (featureInput != null) featureInput.text = "";
            if (statusLabel != null) statusLabel.text = "";
        }

        void BuildGeneratingPanel()
        {
            _generatingPanel = CreateUIObject("GeneratingOverlay", transform);
            StretchFull(_generatingPanel.GetComponent<RectTransform>());
            _generatingPanel.SetActive(false);

            var bg = _generatingPanel.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.9f);

            MakeSlantBar(_generatingPanel.transform, "GenBar1",
                new Vector2(0, 122), new Vector2(760, 70),
                new Color(PromptFighters.UI.UITheme.P1NeonDark.r, PromptFighters.UI.UITheme.P1NeonDark.g, PromptFighters.UI.UITheme.P1NeonDark.b, 0.4f), 40f);
            MakeLabel(_generatingPanel.transform, "GenTitle",
                "AIがキャラクターと技を生成中...",
                new Vector2(0, 120), new Vector2(760, 56), 32f, PromptFighters.UI.UITheme.Gold)
                .fontStyle = FontStyles.Bold | FontStyles.Italic;

            _generatingStatusText = MakeLabel(_generatingPanel.transform, "GenStatus",
                "生成を開始しています...",
                new Vector2(0, 40), new Vector2(700, 40), 18f, PromptFighters.UI.UITheme.Ink);
            _generatingStatusText.fontStyle = FontStyles.Bold;

            MakeLabel(_generatingPanel.transform, "GenNote",
                "しばらくお待ちください。OpenAI API を使用しています。",
                new Vector2(0, -20), new Vector2(700, 32), 14f, PromptFighters.UI.UITheme.InkDim);

            var cancelBtn = MakeButton(_generatingPanel.transform, "CancelBtn", "キャンセル（ローカル生成で続行）",
                new Vector2(0, -100), new Vector2(420, 54), CancelGeneration,
                PromptFighters.UI.UITheme.SteelLight);
            StyleArcadeButton(cancelBtn, PromptFighters.UI.UITheme.SteelLight, 14f);
            SetButtonLabelStyle(cancelBtn, 18f, FontStyles.Bold | FontStyles.Italic, Color.white);

            MakeLabel(_generatingPanel.transform, "TrainHint",
                "Tキー: 生成を続けたままトレーニング　Esc: キャンセル",
                new Vector2(0, -165), new Vector2(700, 30), 13f, PromptFighters.UI.UITheme.InkDim);
        }

        void BuildSkillConfirmPanel()
        {
            _skillConfirmPanel = CreateUIObject("SkillConfirmOverlay", transform);
            StretchFull(_skillConfirmPanel.GetComponent<RectTransform>());
            _skillConfirmPanel.SetActive(false);

            var bg = _skillConfirmPanel.AddComponent<Image>();
            bg.sprite = CreateGradientSprite(
                new Color(0.05f, 0.06f, 0.09f, 1f), new Color(0.06f, 0.07f, 0.11f, 1f),
                new Color(0.012f, 0.014f, 0.022f, 1f), new Color(0.0f, 0.0f, 0.012f, 1f));

            MakeSlantBar(_skillConfirmPanel.transform, "ConfirmSlash", new Vector2(0, 492), new Vector2(420, 46),
                new Color(PromptFighters.UI.UITheme.GoldDim.r, PromptFighters.UI.UITheme.GoldDim.g, PromptFighters.UI.UITheme.GoldDim.b, 0.30f), 22f);
            MakeLabel(_skillConfirmPanel.transform, "ConfirmTitle", "キャラクター確認",
                new Vector2(0, 492), new Vector2(700, 46), 30f, PromptFighters.UI.UITheme.Gold)
                .fontStyle = FontStyles.Bold | FontStyles.Italic;

            // 中央仕切り（斜めゴールド）
            MakeSlantBar(_skillConfirmPanel.transform, "Divider",
                new Vector2(0, -20), new Vector2(6, 880), new Color(PromptFighters.UI.UITheme.Gold.r, PromptFighters.UI.UITheme.Gold.g, PromptFighters.UI.UITheme.Gold.b, 0.32f), 50f);

            // 1P 列（左）
            float lx = -440f;
            MakeSlantBar(_skillConfirmPanel.transform, "P1BadgePlate",
                new Vector2(lx, 438), new Vector2(110, 46), PromptFighters.UI.UITheme.P1Neon, 14f);
            MakeLabel(_skillConfirmPanel.transform, "P1Badge", "1P",
                new Vector2(lx, 438), new Vector2(110, 46), 30f, Color.white)
                .fontStyle = FontStyles.Bold | FontStyles.Italic;
            MakeSlantBar(_skillConfirmPanel.transform, "P1Line",
                new Vector2(lx, 404), new Vector2(360, 3), PromptFighters.UI.UITheme.P1Neon, 14f);

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

            // 表示は実機の物理ボタンに合わせる（attack_a=B / attack_b=A / attack_c=X、ヘルプ文の「B A X 技」と整合）
            string[] slotLabels = { "B", "A", "X", "スマッシュ" };
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
            MakeSlantBar(_skillConfirmPanel.transform, "P2BadgePlate",
                new Vector2(rx, 438), new Vector2(110, 46), PromptFighters.UI.UITheme.P2Neon, -14f);
            MakeLabel(_skillConfirmPanel.transform, "P2Badge", "2P",
                new Vector2(rx, 438), new Vector2(110, 46), 30f, Color.white)
                .fontStyle = FontStyles.Bold | FontStyles.Italic;
            MakeSlantBar(_skillConfirmPanel.transform, "P2Line",
                new Vector2(rx, 404), new Vector2(360, 3), PromptFighters.UI.UITheme.P2Neon, -14f);

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
            var doneBtn = MakeButton(_skillConfirmPanel.transform, "DoneBtn", "ロスターに保存して戻る",
                new Vector2(0, -428), new Vector2(380, 62), OnSkillConfirmDonePressed,
                PromptFighters.UI.UITheme.Gold);
            StyleArcadeButton(doneBtn, PromptFighters.UI.UITheme.Gold, 16f);
            SetButtonLabelStyle(doneBtn, 23f, FontStyles.Bold | FontStyles.Italic, new Color(0.12f, 0.08f, 0f));

            MakeLabel(_skillConfirmPanel.transform, "BattleHint",
                "生成したキャラはロスターに保存されます。キャラ選択画面でモードを選んでバトルへ。",
                new Vector2(0, -475), new Vector2(720, 28), 13f, PromptFighters.UI.UITheme.InkDim);
        }

        // 生成キャラ削除の確認モーダル（アーケード調・誤削除防止）。
        void BuildDeleteConfirmPanel()
        {
            _deleteConfirmPanel = CreateUIObject("DeleteConfirmOverlay", transform);
            StretchFull(_deleteConfirmPanel.GetComponent<RectTransform>());
            _deleteConfirmPanel.SetActive(false);

            // 背景を暗転（クリックは奥へ通さない）
            var dim = _deleteConfirmPanel.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.72f);
            var cg = _deleteConfirmPanel.AddComponent<CanvasGroup>();
            cg.interactable = true; cg.blocksRaycasts = true;

            // ダイアログ枠
            var box = CreateUIObject("DeleteBox", _deleteConfirmPanel.transform);
            var bRt = box.GetComponent<RectTransform>();
            bRt.anchoredPosition = new Vector2(0f, 0f);
            bRt.sizeDelta = new Vector2(620f, 320f);
            var boxImg = box.AddComponent<Image>();
            boxImg.sprite = PromptFighters.UI.UITheme.VGradient; boxImg.type = Image.Type.Simple;
            boxImg.color = new Color(0.05f, 0.055f, 0.08f, 0.99f);

            // 上下のネオン縁（危険色）
            MakeSlantBar(box.transform, "DelTop", new Vector2(0f, 158f), new Vector2(620f, 6f),
                PromptFighters.UI.UITheme.Urgent, 18f);
            MakeSlantBar(box.transform, "DelBottom", new Vector2(0f, -158f), new Vector2(620f, 6f),
                new Color(PromptFighters.UI.UITheme.Urgent.r, PromptFighters.UI.UITheme.Urgent.g, PromptFighters.UI.UITheme.Urgent.b, 0.55f), -18f);

            // タイトル
            MakeSlantBar(box.transform, "DelTitlePlate", new Vector2(0f, 108f), new Vector2(300f, 50f),
                new Color(PromptFighters.UI.UITheme.Urgent.r, PromptFighters.UI.UITheme.Urgent.g, PromptFighters.UI.UITheme.Urgent.b, 0.22f), 16f);
            MakeLabel(box.transform, "DelTitle", "⚠ 削除の確認",
                new Vector2(0f, 108f), new Vector2(560f, 50f), 28f, PromptFighters.UI.UITheme.Urgent)
                .fontStyle = FontStyles.Bold | FontStyles.Italic;

            // 対象キャラ名
            _deleteConfirmNameText = MakeLabel(box.transform, "DelName", "",
                new Vector2(0f, 36f), new Vector2(560f, 36f), 22f, Color.white);
            _deleteConfirmNameText.fontStyle = FontStyles.Bold;
            _deleteConfirmNameText.textWrappingMode = TMPro.TextWrappingModes.NoWrap;

            MakeLabel(box.transform, "DelHint", "この操作は取り消せません。",
                new Vector2(0f, -6f), new Vector2(560f, 28f), 14f, PromptFighters.UI.UITheme.InkDim);

            // キャンセル（左・スティール）
            var cancelBtn = MakeButton(box.transform, "DelCancel", "キャンセル",
                new Vector2(-150f, -94f), new Vector2(230f, 64f), HideDeleteConfirm,
                PromptFighters.UI.UITheme.SteelLight);
            StyleArcadeButton(cancelBtn, PromptFighters.UI.UITheme.SteelLight, 14f);
            SetButtonLabelStyle(cancelBtn, 20f, FontStyles.Bold | FontStyles.Italic, Color.white);

            // 削除する（右・危険色）
            var delBtn = MakeButton(box.transform, "DelConfirm", "削除する",
                new Vector2(150f, -94f), new Vector2(230f, 64f), ConfirmDeleteCharacter,
                PromptFighters.UI.UITheme.Urgent);
            StyleArcadeButton(delBtn, PromptFighters.UI.UITheme.Urgent, 14f);
            SetButtonLabelStyle(delBtn, 20f, FontStyles.Bold | FontStyles.Italic, Color.white);

            MakeLabel(box.transform, "DelKeyHint", "Enter: 削除　Esc: キャンセル",
                new Vector2(0f, -140f), new Vector2(560f, 24f), 12f, PromptFighters.UI.UITheme.InkDim);
        }

        void RefreshSkillConfirmContent()
        {
            void FillPlayer(CharacterData d, TextMeshProUGUI nameT, TextMeshProUGUI descT,
                TextMeshProUGUI statsT, Image image, TextMeshProUGUI[] skillTs)
            {
                if (d == null) return;
                string catchTag = !string.IsNullOrEmpty(d.catchCopy) ? $"「{d.catchCopy}」\n" : "";
                if (nameT != null) nameT.text = d.characterName;
                if (descT != null) descT.text = catchTag + d.visualDescription;
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
                        ? $"{s.skill_name}\n発生 {s.parameters.startup:F2}s\n{s.description}"
                        : "---";
                }
            }

            FillPlayer(_pendingData1, _confirmP1Name, _confirmP1Desc, _confirmP1Stats, _confirmP1Image, _confirmP1SkillTexts);
            FillPlayer(_pendingData2, _confirmP2Name, _confirmP2Desc, _confirmP2Stats, _confirmP2Image, _confirmP2SkillTexts);
        }

        // キャラ選択画面でのカーソル操作。1P=WASD、2P=矢印キー、ゲームパッドはdパッド。
        void HandleRosterCursorInput()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                if (kb.aKey.wasPressedThisFrame) MoveRosterCursor(true, -1, 0);
                if (kb.dKey.wasPressedThisFrame) MoveRosterCursor(true, 1, 0);
                if (kb.wKey.wasPressedThisFrame) MoveRosterCursor(true, 0, -1);
                if (kb.sKey.wasPressedThisFrame) MoveRosterCursor(true, 0, 1);
                if (kb.leftArrowKey.wasPressedThisFrame) MoveRosterCursor(false, -1, 0);
                if (kb.rightArrowKey.wasPressedThisFrame) MoveRosterCursor(false, 1, 0);
                if (kb.upArrowKey.wasPressedThisFrame) MoveRosterCursor(false, 0, -1);
                if (kb.downArrowKey.wasPressedThisFrame) MoveRosterCursor(false, 0, 1);
            }

            var pads = UnityEngine.InputSystem.Gamepad.all;
            if (pads.Count > 0) ReadPadCursor(pads[0], true);
            if (pads.Count > 1) ReadPadCursor(pads[1], false);
        }

        void ReadPadCursor(UnityEngine.InputSystem.Gamepad gp, bool isP1)
        {
            if (gp == null) return;
            if (gp.dpad.left.wasPressedThisFrame) MoveRosterCursor(isP1, -1, 0);
            if (gp.dpad.right.wasPressedThisFrame) MoveRosterCursor(isP1, 1, 0);
            if (gp.dpad.up.wasPressedThisFrame) MoveRosterCursor(isP1, 0, -1);
            if (gp.dpad.down.wasPressedThisFrame) MoveRosterCursor(isP1, 0, 1);
        }

        // 1P/2Pカーソルをグリッド上で移動する（dx=左右, dy=上下）。
        void MoveRosterCursor(bool isP1, int dx, int dy)
        {
            if (_presets == null || _presets.Count == 0) return;
            int idx = isP1 ? _p1PresetIdx : _p2PresetIdx;
            int ni = idx + dx + dy * RosterColumns;
            if (ni < 0 || ni >= _presets.Count) return;
            _rosterPage = ni / (RosterColumns * RosterRows);
            SelectPreset(isP1, ni);
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
            // ページが変わらなければ再生成せず色だけ更新（カーソル移動を高速化）
            if (_rosterPage != _displayedPage) RebuildSharedGrid();
            else RecolorRosterCells();
        }

        void RefreshCharacterPreview()
        {
            SetPreview(_p1PreviewImage, _p1PresetIdx);
            SetPreview(_p2PreviewImage, _p2PresetIdx);
            SetDetail(_p1DetailText, _p1PresetIdx);
            SetDetail(_p2DetailText, _p2PresetIdx);
            if (_presets != null)
            {
                if (_p1PresetIdx >= 0 && _p1PresetIdx < _presets.Count) SetStats(true, _presets[_p1PresetIdx]);
                if (_p2PresetIdx >= 0 && _p2PresetIdx < _presets.Count) SetStats(false, _presets[_p2PresetIdx]);
            }
        }

        void SetPreview(Image image, int idx)
        {
            if (image == null || _presets == null || idx < 0 || idx >= _presets.Count) return;

            var data = _presets[idx];
            EnsurePreviewSprite(data);
            EnsureSpriteSetDeferred(data); // 待機モーション用 Idle1/2/3。選択直後フレームから外して表示遅延を防ぐ

            if (image == _p1PreviewImage) _p1PreviewData = data;
            else if (image == _p2PreviewImage) _p2PreviewData = data;

            image.sprite = data.characterSprite;
            image.enabled = image.sprite != null;
            _previewIdleTimer = 0f;
            _previewIdleFrame = 0;
        }

        // ロビーのキャラプレビューを待機モーション（Idle1→2→3）でループ再生する。
        void AnimatePreviewIdle()
        {
            _previewIdleTimer += Time.unscaledDeltaTime;
            if (_previewIdleTimer < 0.3f) return;
            _previewIdleTimer = 0f;
            _previewIdleFrame = (_previewIdleFrame + 1) % 3;

            ApplyIdleFrame(_p1PreviewImage, _p1PreviewData);
            ApplyIdleFrame(_p2PreviewImage, _p2PreviewData);
        }

        void ApplyIdleFrame(Image image, CharacterData data)
        {
            if (image == null || data?.spriteSet == null) return;
            var id = (CharacterSpriteId)((int)CharacterSpriteId.Idle1 + _previewIdleFrame);
            var s = data.spriteSet.Get(id, data.characterSprite);
            if (s != null) { image.sprite = s; image.enabled = true; }
        }

        void SetDetail(TextMeshProUGUI label, int idx)
        {
            if (label == null || _presets == null || idx < 0 || idx >= _presets.Count) return;
            label.text = BuildCharacterDetail(_presets[idx]);
        }

        string BuildCharacterDetail(CharacterData data)
        {
            if (data == null) return "---";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<b>技</b>");
            if (data.skills != null)
                for (int i = 0; i < data.skills.Length; i++)
                {
                    var skill = data.skills[i];
                    if (skill == null) continue;
                    // 実機ボタン表記（attack_a=B / attack_b=A / attack_c=X / スマッシュ）
                    string slot = i switch { 0 => "B", 1 => "A", 2 => "X", 3 => "スマッシュ", _ => "?" };
                    sb.AppendLine($"<color=#FFC72E>{slot}</color> {skill.skill_name}");
                }
            if (!string.IsNullOrWhiteSpace(data.inputFeatures))
            {
                sb.AppendLine();
                sb.AppendLine("<b>プロンプト</b>");
                sb.AppendLine($"<color=#9FB3C8><i>{data.inputFeatures}</i></color>");
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

        void RebuildIconGrids() => RebuildSharedGrid();

        void RebuildSharedGrid()
        {
            if (_rosterGrid == null || _presets == null) return;

            for (int i = _rosterGrid.childCount - 1; i >= 0; i--)
                Destroy(_rosterGrid.GetChild(i).gameObject);
            _rosterCellBgs.Clear();

            int pageSize = RosterColumns * RosterRows;
            int maxPage = Mathf.Max(0, (_presets.Count - 1) / pageSize);
            _rosterPage = Mathf.Clamp(_rosterPage, 0, maxPage);
            _displayedPage = _rosterPage;
            int start = _rosterPage * pageSize;
            int end = Mathf.Min(_presets.Count, start + pageSize);
            if (_rosterPageLabel != null) _rosterPageLabel.text = $"{_rosterPage + 1} / {maxPage + 1}";

            for (int i = start; i < end; i++)
            {
                var data = _presets[i];
                EnsurePreviewSprite(data);
                MakeRosterCell(_rosterGrid, i, data);
            }
        }

        readonly HashSet<CharacterData> _spriteLoading = new HashSet<CharacterData>();

        // プレビュー用スプライトをバックグラウンドI/Oで非同期ロードする。
        // 同じdataの二重ロードはガードし、完了後にプレビュー/アイコンを更新する。
        void EnsurePreviewSprite(CharacterData data)
        {
            if (data == null || data.characterSprite != null) return;
            if (string.IsNullOrEmpty(data.spritePath)) return;
            if (!_spriteLoading.Add(data)) return; // 既にロード中
            StartCoroutine(LoadPreviewSpriteCo(data));
        }

        IEnumerator LoadPreviewSpriteCo(CharacterData data)
        {
            yield return SpriteLoader.LoadWithWhiteBgRemovedAsync(data.spritePath, s => data.characterSprite = s);
            _spriteLoading.Remove(data);
            if (data.characterSprite != null)
            {
                RefreshCharacterPreview();
                RebuildIconGrids();
            }
        }

        readonly HashSet<CharacterData> _spriteSetLoading = new HashSet<CharacterData>();

        // 選択画面のプレビューが使うのは idle1/2/3 のみ。pose/effect は戦闘開始時に
        // ロードするため、ここでは待機モーション3枚だけを非同期で読み込む。
        void EnsureSpriteSetDeferred(CharacterData data)
        {
            if (data == null) return;
            if (HasIdleSprites(data.spriteSet)) return;
            if (string.IsNullOrEmpty(data.spriteDir)) return;
            if (!_spriteSetLoading.Add(data)) return;
            StartCoroutine(LoadSpriteSetCo(data));
        }

        IEnumerator LoadSpriteSetCo(CharacterData data)
        {
            yield return null; // プレビュー表示(idle1)を先に出してからロード
            // idle1/2/3 のみを1枚ずつフレーム分割で読み込み、選択時のヒッチを解消する。
            yield return CharacterSaveManager.LoadSpriteSetAsync(data, idleOnly: true);
            _spriteSetLoading.Remove(data);
        }

        // 待機モーション(idle2/idle3)が揃っているか。選択画面の遅延ロード判定用。
        static bool HasIdleSprites(CharacterSpriteSet spriteSet)
        {
            if (spriteSet?.sprites == null || spriteSet.sprites.Length < 3) return false;
            return spriteSet.sprites[(int)CharacterSpriteId.Idle2] != null
                && spriteSet.sprites[(int)CharacterSpriteId.Idle3] != null;
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
            HideGamepadCursors();
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

        // 生成キャラ確認後はロスターに保存済みなので、バトルへ進まずキャラ選択画面へ戻る。
        void OnSkillConfirmDonePressed()
        {
            if (_generationTrainingActive)
            {
                BattleManager.Instance?.ReturnToSetup();
                _generationTrainingActive = false;
            }
            _skillConfirmPanel?.SetActive(false);
            _trainingPanel?.SetActive(false);
            ShowPanel();
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
            bool aiOk1 = false;
            bool aiOk2 = false;

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
                        aiOk1 = true;
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
                        aiOk2 = true;
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
                if (!string.IsNullOrEmpty(errorMsg))
                    Debug.LogWarning("[PreBattle] AI生成失敗: " + errorMsg);
                UpdateGeneratingStatus(BuildFallbackMessage(errorMsg));
                _pendingData1 = PromptCharacterFactory.Create(
                    _p1NameInput?.text, _p1FeatureInput?.text, preset1);
                yield return new WaitForSeconds(1.5f);
            }
            if (_pendingData2 == null)
            {
                _pendingData2 = PromptCharacterFactory.Create(
                    _p2NameInput?.text, _p2FeatureInput?.text, preset2);
            }

            // 画像生成はAIキャラ生成が成功した側だけ行う。
            // 画像はローカル生成できないため、キャラ生成が失敗（API不通）した側で
            // 画像生成を試みても無駄に長時間ハングするだけなのでスキップする。
            bool genImg1 = genP1 && aiOk1;
            bool genImg2 = genP2 && aiOk2;
            if ((genImg1 || genImg2) && !DebugSettings.SkipImageGeneration)
            {
                UpdateGeneratingStatus("キャラクター画像を生成中...");
                yield return GenerateImages(_pendingData1, _pendingData2, genImg1, genImg2);
            }
            else if (DebugSettings.SkipImageGeneration)
            {
                UpdateGeneratingStatus("[デバッグ] 画像生成をスキップしました");
            }
            else if ((genP1 && !aiOk1) || (genP2 && !aiOk2))
            {
                UpdateGeneratingStatus("画像生成をスキップしました（選択中の画像を使用します）");
                yield return new WaitForSeconds(1.0f);
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
                    msg => UpdateGeneratingStatus("1P " + FormatImageProgress(msg)),
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
                    msg => UpdateGeneratingStatus("2P " + FormatImageProgress(msg)),
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

        // pose/effect スプライト（Jump 以降 = index 3..）が1枚でもあるか。
        // idle1/2/3(index 0..2) は対象外。戦闘開始時の完全ロード判定に使う。
        static bool HasPoseAndEffectSprites(CharacterSpriteSet spriteSet)
        {
            if (spriteSet?.sprites == null) return false;
            for (int i = (int)CharacterSpriteId.Jump; i < spriteSet.sprites.Length; i++)
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

        // 生成失敗の理由を簡潔に伝えつつローカル生成へ切り替える旨を表示する
        static string BuildFallbackMessage(string error)
        {
            string reason;
            if (string.IsNullOrEmpty(error))                 reason = "AI生成に失敗";
            else if (error.Contains("timeout") || error.Contains("タイムアウト"))
                                                             reason = "AIサーバーが応答しません（通信タイムアウト）";
            else if (error.Contains("APIキー"))               reason = "APIキー未設定";
            else                                             reason = "AI生成に失敗";
            return reason + " — ローカル生成で続行します...";
        }

        // 生成進捗メッセージから画像枚数を解析して "N/15" 表示に変換する（Feature E）
        static string FormatImageProgress(string msg)
        {
            if (msg.Contains("残り") && msg.Contains("枚"))
            {
                var m = System.Text.RegularExpressions.Regex.Match(msg, @"残り\s*(\d+)\s*枚");
                if (m.Success && int.TryParse(m.Groups[1].Value, out int rem))
                    return $"画像生成中 {15 - rem}/15 完了";
            }
            if (msg.Contains("バリエーション")) return "画像生成中 1/15 完了";
            if (msg.Contains("ベース画像"))    return "画像生成中 0/15 完了";
            return msg;
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
            HideGamepadCursors();
            BattleManager.Instance.StartTraining(data1, data2);
        }

        void StartTrainingDuringGeneration()
        {
            if (_generationTrainingActive) return;
            if (BattleManager.Instance == null || _presets == null || _presets.Count == 0) return;
            _generationTrainingActive = true;
            _generatingPanel?.SetActive(false);
            HideGamepadCursors();

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

        // 削除ボタン押下。誤削除防止のため即削除せず確認モーダルを開く。
        void RequestDeleteCharacter(bool isP1)
        {
            if (_presets == null) return;
            int idx = isP1 ? _p1PresetIdx : _p2PresetIdx;
            if (idx < _builtInPresetCount || idx < 0 || idx >= _presets.Count) return; // 初期キャラは削除不可

            _deletePendingIsP1 = isP1;
            if (_deleteConfirmNameText != null)
                _deleteConfirmNameText.text = $"<color=#FFC72E>{_presets[idx].characterName}</color> を削除します";
            if (_deleteConfirmPanel != null) _deleteConfirmPanel.SetActive(true);
        }

        void HideDeleteConfirm()
        {
            if (_deleteConfirmPanel != null) _deleteConfirmPanel.SetActive(false);
        }

        void ConfirmDeleteCharacter()
        {
            HideDeleteConfirm();
            DeleteSelectedCharacter(_deletePendingIsP1);
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

    }
}
